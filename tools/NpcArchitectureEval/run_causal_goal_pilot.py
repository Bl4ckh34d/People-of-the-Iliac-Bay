"""Causal goal-language pilot over disposable Core sessions and fixture receipts."""
from __future__ import annotations

import argparse
import copy
import hashlib
import json
import sys
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[1]
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

import goal_protocol  # noqa: E402
from model_client import _validate_loopback_http, post_json  # noqa: E402
from run_core_tool_pilot import CoreSession, _accepted, _model_id  # noqa: E402
from run_evaluation import game_closed, get_json  # noqa: E402


DEFAULT_D3_URL = "http://127.0.0.1:8093"
DEFAULT_QWEN_URL = "http://127.0.0.1:8080"
BRIDGE_PATH = REPO / "tools/NpcArchitectureEval/CoreBridge/bin/Debug/net8.0/CoreBridge.dll"
CORE_PATH = BRIDGE_PATH.parent / "PeopleOfTheIliacBay.Core.dll"
QUOTE_GOLD = 9
ITEM_UID = 42
SCENARIOS = (
    {
        "id": "buy_transfer_direct_paraphrase_a",
        "playerText": "Could you buy the shop's stock item 42 for nine gold and pass it to the other resident?",
        "history": [],
        "expected": {"route": "ACTION_REQUEST", "intent": "request", "decision": "accept", "outcome": "Completed"},
    },
    {
        "id": "buy_transfer_direct_paraphrase_b",
        "playerText": "Please purchase UID 42 from the current stock, then hand that item to the recipient.",
        "history": [],
        "expected": {"route": "ACTION_REQUEST", "intent": "request", "decision": "accept", "outcome": "Completed"},
    },
    {
        "id": "buy_transfer_hypothetical_control",
        "playerText": "If I asked you to buy shop item 42 and give it to the recipient later, would you consider it?",
        "history": [],
        "expected": {"route": "SOCIAL", "intent": "none", "decision": "not_request", "outcome": "no_action"},
    },
    {
        "id": "buy_transfer_reported_control",
        "playerText": "That was yesterday's shopping request; I am describing it, not asking you to do it now.",
        "history": [
            {"speaker": "Player", "text": "Buy item 42 for nine gold and give it to the recipient."},
            {"speaker": "NPC", "text": "I handled that earlier."},
        ],
        "expected": {"route": "SOCIAL", "intent": "none", "decision": "not_request", "outcome": "no_action"},
    },
)


def _parse_args(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--run-dir", required=True, help="new output directory; relative paths resolve from repository root")
    parser.add_argument("--d3-url", default=DEFAULT_D3_URL, help="loopback D3 /v1/systemone base URL")
    parser.add_argument("--qwen-url", default=DEFAULT_QWEN_URL, help="loopback Qwen /v1/chat/completions base URL")
    parser.add_argument("--timeout", type=float, default=60, help="per-model-call timeout in seconds")
    return parser.parse_args(argv)


def _resolve_new_run_dir(value):
    path = Path(value)
    target = path.resolve() if path.is_absolute() else (REPO / path).resolve()
    if target.exists():
        raise FileExistsError("Run directory already exists: " + str(target))
    return target


def _read_choice(response, question_id):
    if response.get("transport_error"):
        return None, "transport_error"
    if response.get("status_code") != 200:
        return None, "http_" + str(response.get("status_code"))
    if response.get("malformed_json"):
        return None, "malformed_json"
    try:
        choice = response["response_json"]["answers"][question_id]["choice"]
    except (KeyError, TypeError):
        return None, "malformed_choice_response"
    return (choice, None) if isinstance(choice, str) and choice else (None, "malformed_choice_response")


def _read_completion(response):
    if response.get("transport_error"):
        return None, "transport_error"
    if response.get("status_code") != 200:
        return None, "http_" + str(response.get("status_code"))
    if response.get("malformed_json"):
        return None, "malformed_json"
    try:
        completion = response["response_json"]["choices"][0]
        if completion.get("finish_reason") == "length":
            return None, "truncated"
        value = json.loads(completion["message"]["content"])
    except (KeyError, IndexError, TypeError, ValueError):
        return None, "invalid_completion"
    return (value, None) if isinstance(value, dict) else (None, "non_object_completion")


def _d3_choice_payload(question_id, state, instructions, criteria, reverse):
    options = list(criteria.items())
    if reverse:
        options.reverse()
    return {"state": json.dumps(state, ensure_ascii=False, separators=(",", ":")),
            "questions": {question_id: {"type": "choice", "instructions": instructions,
                                        "criteria": dict(options)}}}


def _scenario_shell(scenario):
    return {
        "playerText": scenario["playerText"],
        "snapshot": {"actor": {"role": "ManagedResident"}, "scene": "Synthetic CoreBridge town",
                     "activity": "Stationary", "history": copy.deepcopy(scenario["history"]),
                     "pendingOffer": None, "activeCommitment": None, "candidates": []},
    }


def _case_after_reads(scenario, participants, buyer_inventory, seller_inventory):
    """Create model-visible capabilities only from Core inventory observations."""
    if not isinstance(buyer_inventory, dict) or not isinstance(seller_inventory, dict):
        raise ValueError("Inventory observations are required before candidate binding")
    if buyer_inventory.get("personId") != participants.get("buyer") or seller_inventory.get("personId") != participants.get("seller"):
        raise ValueError("Core inventory observations do not match participants")
    stock = [item for item in seller_inventory.get("items", []) if str(item.get("uid")) == str(ITEM_UID)]
    candidates = []
    if len(stock) == 1 and buyer_inventory.get("walletGold", -1) >= QUOTE_GOLD:
        candidates = [
            {"ref": "shop_stock_item_42", "kind": "Purchase", "label": "Shop stock UID 42; quoted price 9 gold",
             "maxQuantity": 1,
             "target": {"Kind": "IndoorPoint", "PersonId": participants["seller"], "SettlementId": 100,
                        "BuildingKey": 20, "ContainerId": "7", "ItemId": "42", "Quantity": 1,
                        "QuotedGold": QUOTE_GOLD}},
            {"ref": "give_item_42_to_recipient", "kind": "Transfer",
             "label": "Transfer one item UID 42 to the other managed resident",
             "maxQuantity": 1,
             "target": {"Kind": "Person", "PersonId": participants["recipient"], "ItemId": "42",
                        "Quantity": 1, "TransferDirection": "NpcToCounterparty"}},
        ]
    case = _scenario_shell(scenario)
    case["snapshot"]["candidates"] = candidates
    case["snapshot"]["observations"] = {"ownInventory": copy.deepcopy(buyer_inventory),
                                         "shopStock": copy.deepcopy(seller_inventory)}
    return case


def _target_lower_camel(target):
    if not isinstance(target, dict):
        raise ValueError("Bound target must be an object")
    return {key[0].lower() + key[1:]: copy.deepcopy(value) for key, value in target.items()}


def _core_plan_from_bound(bound, participants, task_id):
    """Map only exact bound one-item purchase/handover into Core's JSON contract."""
    if not isinstance(bound, dict) or bound.get("outcome") != "plan":
        raise ValueError("A Core task requires a bound plan")
    steps = bound.get("steps")
    if not isinstance(steps, list) or len(steps) != 2:
        raise ValueError("Pilot accepts exactly one Purchase followed by one Transfer")
    purchase, transfer = steps
    purchase_target = purchase.get("target", {})
    transfer_target = transfer.get("target", {})
    expected_purchase = {"Kind": "IndoorPoint", "PersonId": participants["seller"], "SettlementId": 100,
                         "BuildingKey": 20, "ContainerId": "7", "ItemId": "42", "Quantity": 1,
                         "QuotedGold": QUOTE_GOLD}
    expected_transfer = {"Kind": "Person", "PersonId": participants["recipient"], "ItemId": "42",
                         "Quantity": 1, "TransferDirection": "NpcToCounterparty"}
    if (purchase.get("kind") != "Purchase" or purchase.get("targetRef") != "shop_stock_item_42" or
            purchase.get("itemSourceStep") != 0 or purchase.get("durationSeconds") != 0 or purchase_target != expected_purchase or
            transfer.get("kind") != "Transfer" or transfer.get("targetRef") != "give_item_42_to_recipient" or
            transfer.get("itemSourceStep") != 1 or transfer.get("durationSeconds") != 0 or transfer_target != expected_transfer):
        raise ValueError("Bound proposal does not match observed UID 42 purchase and recipient transfer")
    return {
        "id": task_id, "proposalId": "proposal:" + task_id,
        "residentId": participants["buyer"], "acceptedAt": 100, "updatedAt": 100, "deadlineAt": 1000,
        "steps": [
            {"kind": purchase["kind"], "target": _target_lower_camel(purchase_target)},
            {"kind": transfer["kind"], "itemSourceStep": transfer["itemSourceStep"],
             "target": _target_lower_camel(transfer_target)},
        ],
    }


def _accept_supported_plan(core, bound, participants, task_id, decision):
    if decision != "accept":
        return {"accepted": False, "skipped": "decision_" + str(decision)}
    try:
        plan = _core_plan_from_bound(bound, participants, task_id)
    except (KeyError, TypeError, ValueError) as exc:
        return {"accepted": False, "skipped": "unsupported_bound_plan", "reason": str(exc)}
    result = core.command("accept_task", consent=True, plan=plan)
    return {"accepted": bool(result.get("accepted")), "simulationConsent": True,
            "realNpcConsent": False, "plan": plan, "coreResult": result}


def _freeze_files():
    files = [Path(__file__).resolve(), ROOT / "goal_protocol.py", ROOT / "model_client.py",
             ROOT / "run_core_tool_pilot.py", ROOT / "run_evaluation.py", ROOT / "adapters.py",
             ROOT / "scenario_validation.py", BRIDGE_PATH, CORE_PATH]
    missing = [str(path) for path in files if not path.is_file()]
    if missing:
        raise FileNotFoundError("Required pilot dependency is missing: " + ", ".join(missing))
    return {str(path.resolve()): hashlib.sha256(path.read_bytes()).hexdigest() for path in files}


def _verify_frozen(frozen):
    for path, digest in frozen.items():
        if not Path(path).is_file() or hashlib.sha256(Path(path).read_bytes()).hexdigest() != digest:
            raise RuntimeError("Frozen pilot dependency changed: " + path)


def _model_call(url, payload, ledger, metadata, timeout, frozen):
    _verify_frozen(frozen)
    return post_json(url, payload, ledger, metadata, timeout=timeout)


def _append_result(path, result):
    with path.open("a", encoding="utf-8") as stream:
        stream.write(json.dumps(result, ensure_ascii=False) + "\n")
        stream.flush()


def _tool_payload(scenario, route, actor_id, d3_url, target, arm, timeout, frozen, core, participants):
    available = {
        "read_own_inventory": "Read current NPC wallet and inventory from Core; read only.",
        "read_shop_stock": "Read current shop seller stock and wallet from Core; read only.",
        "ready": "No more facts are needed, or requested reads are unavailable.",
    }
    observed = {}
    for turn in range(3):
        remaining = {key: value for key, value in available.items() if key == "ready" or key not in observed}
        state = {"actorId": actor_id, "playerText": scenario["playerText"], "history": scenario["history"],
                 "route": route, "observations": observed,
                 "readTools": [{"id": key, "description": value} for key, value in remaining.items()],
                 "limit": "Choose at most three read tools, counting ready. Repeated reads are unavailable."}
        criteria = {key: available[key] for key in remaining}
        payload = _d3_choice_payload("tool", state,
            "Choose a read tool to ground a possible request. Select ready when current facts suffice or no read is needed.",
            criteria, arm == "reversed")
        response = _model_call(d3_url + "/v1/systemone", payload, target / "model-attempts.jsonl",
                               {"scenarioId": scenario["id"], "arm": arm, "stage": "tool_choice", "turn": turn}, timeout, frozen)
        choice, error = _read_choice(response, "tool")
        if error:
            raise RuntimeError("D3 tool choice failed: " + error)
        if choice == "ready":
            break
        if choice == "read_own_inventory" and choice not in observed:
            observed[choice] = core.command("read_inventory", personId=participants["buyer"])
        elif choice == "read_shop_stock" and choice not in observed:
            observed[choice] = core.command("read_inventory", personId=participants["seller"])
        else:
            raise RuntimeError("D3 selected unavailable read tool: " + str(choice))
    return observed


def _case_from_observations(scenario, participants, observations):
    buyer = observations.get("read_own_inventory")
    seller = observations.get("read_shop_stock")
    if buyer is None or seller is None:
        case = _scenario_shell(scenario)
        case["snapshot"]["observations"] = {
            "ownInventory": copy.deepcopy(buyer), "shopStock": copy.deepcopy(seller),
        }
        return case
    case = _case_after_reads(scenario, participants, buyer, seller)
    case["snapshot"]["observations"] = {
        "ownInventory": copy.deepcopy(buyer), "shopStock": copy.deepcopy(seller),
    }
    return case


def _route_call(case, d3_url, target, scenario_id, arm, timeout, frozen):
    payload = goal_protocol.route_payload(case, reverse=(arm == "reversed"))
    response = _model_call(d3_url + "/v1/systemone", payload, target / "model-attempts.jsonl",
                           {"scenarioId": scenario_id, "arm": arm, "stage": "route"}, timeout, frozen)
    return _read_choice(response, "route")


def _interpretation_call(case, qwen_url, model, target, scenario_id, arm, timeout, frozen):
    payload = goal_protocol.interpretation_payload(case, model)
    response = _model_call(qwen_url + "/v1/chat/completions", payload, target / "model-attempts.jsonl",
                           {"scenarioId": scenario_id, "arm": arm, "stage": "interpretation"}, timeout, frozen)
    return _read_completion(response)


def _decision_call(scenario, route, interpretation, bound, observations, willingness, d3_url,
                   target, arm, timeout, frozen):
    choices = {
        "accept": "Accept this current direct request, if proposal is exact and supported by current read facts.",
        "decline": "Decline a current supported request because willingness or disposition is insufficient.",
        "clarify": "Ask a question because intent, target, stock, funds, price or plan remains unclear.",
        "not_request": "Do not start an action because player is describing, quoting, hypothesizing or not requesting action.",
    }
    state = {"playerText": scenario["playerText"], "history": scenario["history"], "route": route,
             "interpretation": interpretation, "boundProposal": bound, "observations": observations,
             "fixtureWillingness": willingness,
             "boundary": "This decision is a synthetic evaluation choice, never real NPC consent. No effect occurred yet."}
    payload = _d3_choice_payload("decision", state,
        "Choose one decision from the current speech, proposal, read facts and willingness fixture. Never infer consent from capability alone.",
        choices, arm == "reversed")
    response = _model_call(d3_url + "/v1/systemone", payload, target / "model-attempts.jsonl",
                           {"scenarioId": scenario["id"], "arm": arm, "stage": "post_plan_decision"}, timeout, frozen)
    choice, error = _read_choice(response, "decision")
    if not error and choice not in choices:
        return None, "unsupported_decision_choice"
    return choice, error


def _reply_call(qwen_url, model, final_state, target, scenario_id, arm, timeout, frozen):
    schema = {"type": "object", "additionalProperties": False,
              "properties": {"reply": {"type": "string"}, "completed": {"type": "boolean"},
                             "buyerGold": {"type": "integer"}, "sellerGold": {"type": "integer"},
                             "itemOwner": {"type": "string"}, "itemUid": {"type": "integer"}},
              "required": ["reply", "completed", "buyerGold", "sellerGold", "itemOwner", "itemUid"]}
    payload = {"model": model, "temperature": 0, "seed": 42, "max_tokens": 220, "stream": False,
               "messages": [
                   {"role": "system", "content": "You are the NPC speaking English. Report only facts in the supplied final Core receipt state. Core state comes from explicit synthetic fixture receipts, not a native game observation. Do not claim a native world effect. Use exact wallet gold, task completion phase, item UID and current owner from the data. Return the requested JSON."},
                   {"role": "user", "content": json.dumps(final_state, ensure_ascii=False, separators=(",", ":"))},
               ],
               "response_format": {"type": "json_schema", "json_schema": {"name": "core_receipt_reply", "strict": True, "schema": schema}}}
    response = _model_call(qwen_url + "/v1/chat/completions", payload, target / "model-attempts.jsonl",
                           {"scenarioId": scenario_id, "arm": arm, "stage": "receipt_reply"}, timeout, frozen)
    return _read_completion(response)


def _all_inventory_uids(core, participants):
    return {int(item["uid"]) for key in ("buyer", "seller", "recipient")
            for item in core.command("read_inventory", personId=participants[key]).get("items", [])}


def _execute_task(core, accepted_plan, participants, observed, run_index):
    plan = accepted_plan["plan"]
    task_id = plan["id"]
    accepted = accepted_plan["coreResult"]
    if not accepted.get("accepted"):
        return {"accepted": False, "coreAcceptance": accepted}
    before = core.command("read_state")
    buyer = observed["read_own_inventory"]
    seller = observed["read_shop_stock"]
    quote = plan["steps"][0]["target"]["quotedGold"]
    if buyer["walletGold"] < quote or not any(str(item["uid"]) == "42" for item in seller["items"]):
        raise RuntimeError("Accepted plan lacks live buyer funds or exact shop stock")
    known_uids = _all_inventory_uids(core, participants)
    acquired_uid = max(known_uids, default=0) + 1
    if acquired_uid <= 0 or acquired_uid in known_uids:
        raise RuntimeError("Could not allocate unique synthetic receipt UID")
    purchase_dispatch = _accepted(core.command("dispatch_next", taskId=task_id, now=101, adapterAvailable=True,
                                                consent=True, scheduleCompatible=True))
    action = purchase_dispatch["action"]
    _accepted(core.command("observe_arrival", actionId=action["id"], expectedRevision=action["revision"], now=102))
    native_payload = next(item["nativePayload"] for item in seller["items"] if str(item["uid"]) == "42")
    purchase_receipt = _accepted(core.command("apply_purchase_receipt", actionId=action["id"], expectedRevision=2, now=103,
                                               buyerGoldAfter=buyer["walletGold"] - quote,
                                               sellerGoldAfter=seller["walletGold"] + quote,
                                               itemUid=acquired_uid, nativePayload=native_payload))
    _accepted(core.command("reconcile", taskId=task_id, expectedRevision=2, now=103))
    snapshot = core.command("read_state")["snapshotJson"]
    restored = _accepted(core.command("restore_snapshot", snapshotJson=snapshot))
    restored_buyer = core.command("read_inventory", personId=participants["buyer"])
    if not any(int(item["uid"]) == acquired_uid for item in restored_buyer["items"]):
        raise RuntimeError("Restored Core state lost acquired exact UID")
    transfer_dispatch = _accepted(core.command("dispatch_next", taskId=task_id, now=104, adapterAvailable=True,
                                                consent=True, scheduleCompatible=True))
    transfer_action = transfer_dispatch["action"]
    _accepted(core.command("observe_arrival", actionId=transfer_action["id"], expectedRevision=transfer_action["revision"], now=105))
    transfer_receipt = _accepted(core.command("apply_transfer_receipt", actionId=transfer_action["id"], expectedRevision=2,
                                              now=106, accepted=True, itemUid=acquired_uid))
    _accepted(core.command("reconcile", taskId=task_id, expectedRevision=4, now=106))
    final_state = core.command("read_state")
    recipient_inventory = core.command("read_inventory", personId=participants["recipient"])
    owner_uids = [key for key in ("buyer", "seller", "recipient")
                  if any(int(item["uid"]) == acquired_uid for item in core.command("read_inventory", personId=participants[key])["items"])]
    if owner_uids != ["recipient"]:
        raise RuntimeError("Final Core inventory does not give acquired UID to unique recipient")
    task_state = next((row for row in final_state.get("tasks", []) if row.get("id") == task_id), None)
    if not task_state or task_state.get("phase") != "Completed":
        raise RuntimeError("Core task did not reach Completed")
    return {"accepted": True, "simulationConsent": True, "realNpcConsent": False,
            "taskId": task_id, "task": task_state, "purchaseReceipt": purchase_receipt,
            "transferReceipt": transfer_receipt, "snapshotRestoredBeforeTransfer": True,
            "acquiredUid": acquired_uid, "ownerKeys": owner_uids,
            "finalState": final_state, "recipientInventory": recipient_inventory,
            "preEffectState": before, "corePlan": plan, "runIndex": run_index}


def _score_reply(reply, execution, participants):
    if not isinstance(reply, dict) or not execution.get("accepted"):
        return {"replyValid": False, "completedCorrect": False, "goldCorrect": False,
                "itemOwnerCorrect": False, "itemUidCorrect": False}
    final_state = execution["finalState"]
    inventories = {row["personId"]: row for row in final_state.get("inventories", [])}
    buyer = inventories.get(participants["buyer"], {})
    seller = inventories.get(participants["seller"], {})
    recipient = inventories.get(participants["recipient"], {})
    actual_owner = next((person_id for person_id, inventory in inventories.items()
                         if any(int(item["uid"]) == execution["acquiredUid"] for item in inventory.get("items", []))), None)
    task_complete = execution.get("task", {}).get("phase") == "Completed"
    return {
        "replyValid": isinstance(reply.get("reply"), str) and bool(reply["reply"].strip()),
        "completedCorrect": reply.get("completed") is task_complete,
        "goldCorrect": reply.get("buyerGold") == buyer.get("walletGold") and reply.get("sellerGold") == seller.get("walletGold"),
        "itemOwnerCorrect": reply.get("itemOwner") == actual_owner == participants["recipient"],
        "itemUidCorrect": reply.get("itemUid") == execution["acquiredUid"] and
                          any(int(item["uid"]) == execution["acquiredUid"] for item in recipient.get("items", [])),
    }


def _run_arm(scenario, reverse, d3_url, qwen_url, model_ids, binary, target, timeout, frozen, arm_index):
    arm = "reversed" if reverse else "original"
    core = None
    record = {"scenarioId": scenario["id"], "arm": arm, "expected": copy.deepcopy(scenario["expected"]),
              "status": "started", "route": None, "interpretation": None, "bound": None, "decision": None,
              "coreAccepted": False, "simulationConsent": False, "realNpcConsent": False, "execution": None,
              "reply": None, "replyScore": None, "error": None}
    try:
        core = CoreSession(binary, target / "core-events.jsonl")
        participants = core.command("read_capabilities")["participants"]
        pre_route_case = _scenario_shell(scenario)
        route, route_error = _route_call(pre_route_case, d3_url, target, scenario["id"], arm, timeout, frozen)
        record["route"] = route
        record["routeError"] = route_error
        observations = {}
        if not route_error and route != "SOCIAL":
            observations = _tool_payload(scenario, route, participants["buyer"], d3_url, target, arm,
                                         timeout, frozen, core, participants)
        case = _case_from_observations(scenario, participants, observations)
        def route_callback():
            return route, route_error
        def interpretation_callback():
            value, error = _interpretation_call(case, qwen_url, model_ids["qwen"], target,
                                                scenario["id"], arm, timeout, frozen)
            return value, error
        flow = goal_protocol.event_first_flow(case, route_callback, interpretation_callback)
        record.update({"interpretation": flow.get("interpretation"), "extractionError": flow.get("extractionError"),
                       "interpreterSkipped": flow.get("interpreterSkipped"), "bound": flow.get("bound"),
                       "pipeline": flow.get("pipeline"), "observations": observations})
        buyer_inventory = observations.get("read_own_inventory", {})
        willingness = {"fixtureOnly": True, "willingness": "willing to help direct modest requests",
                       "disposition": 65, "maxNpcSpendGold": 9,
                       "observedBuyerGold": buyer_inventory.get("walletGold"),
                       "realNpcConsent": False}
        decision, decision_error = _decision_call(scenario, route, flow.get("interpretation"), flow.get("pipeline"),
                                                  observations, willingness, d3_url, target, arm,
                                                  timeout, frozen)
        record["decision"] = decision
        record["decisionError"] = decision_error
        if decision_error:
            raise RuntimeError("D3 post-plan decision failed: " + str(decision_error))
        if decision == "accept":
            if observations.get("read_own_inventory") is None or observations.get("read_shop_stock") is None:
                record["acceptance"] = {"accepted": False, "skipped": "required_reads_missing"}
            else:
                task_id = "task:causal:" + scenario["id"] + ":" + arm
                acceptance = _accept_supported_plan(core, flow.get("pipeline"), participants, task_id, decision)
                record["acceptance"] = acceptance
                record["simulationConsent"] = acceptance.get("simulationConsent", False)
                record["realNpcConsent"] = False
                if acceptance.get("accepted"):
                    record["coreAccepted"] = True
                    execution = _execute_task(core, acceptance, participants, observations, arm_index)
                    record["execution"] = execution
                    record["coreAccepted"] = execution.get("accepted", False)
                    if execution.get("accepted"):
                        reply_state = {
                            "task": execution["task"], "buyerInventory": core.command("read_inventory", personId=participants["buyer"]),
                            "sellerInventory": core.command("read_inventory", personId=participants["seller"]),
                            "recipientInventory": execution["recipientInventory"],
                            "acquiredUid": execution["acquiredUid"],
                            "receiptKinds": ["fixture_purchase_receipt", "fixture_transfer_receipt"],
                            "nativeEffectObserved": False,
                        }
                        reply, reply_error = _reply_call(qwen_url, model_ids["qwen"], reply_state, target,
                                                        scenario["id"], arm, timeout, frozen)
                        if reply_error:
                            record["replyError"] = reply_error
                        else:
                            record["reply"] = reply
                            record["replyScore"] = _score_reply(reply, execution, participants)
        else:
            record["acceptance"] = {"accepted": False, "skipped": "decision_" + str(decision)}
        record["status"] = "complete"
    except Exception as exc:
        record["status"] = "failed"
        record["error"] = str(exc)
    finally:
        if core is not None:
            try:
                core.close()
            except Exception as exc:
                record["status"] = "failed"
                record["closeError"] = str(exc)
    actual_intent = record.get("interpretation", {}).get("intent") if record.get("interpretation") else None
    if actual_intent is None and record.get("route") == "SOCIAL" and not record.get("routeError"):
        actual_intent = "none"
    execution = record.get("execution") or {}
    if execution.get("accepted") and execution.get("task", {}).get("phase") == "Completed":
        actual_outcome = "Completed"
    elif record.get("coreAccepted"):
        actual_outcome = "partial_effect_failure"
    elif record.get("pipeline", {}).get("outcome") == "no_action":
        actual_outcome = "no_action"
    elif record.get("decision") in ("decline", "clarify", "not_request"):
        actual_outcome = record["decision"]
    else:
        actual_outcome = (record.get("pipeline") or {}).get("outcome", "failed")
    record["score"] = {
        "routeCorrect": record.get("route") == record["expected"]["route"],
        "intentCorrect": actual_intent == record["expected"]["intent"],
        "decisionCorrect": record.get("decision") == record["expected"]["decision"],
        "actualOutcome": actual_outcome,
        "outcomeCorrect": actual_outcome == record["expected"]["outcome"],
    }
    return record


def _run(args):
    target = _resolve_new_run_dir(args.run_dir)
    d3_url = args.d3_url.rstrip("/")
    qwen_url = args.qwen_url.rstrip("/")
    _validate_loopback_http(d3_url)
    _validate_loopback_http(qwen_url)
    if not BRIDGE_PATH.is_file() or not CORE_PATH.is_file():
        raise FileNotFoundError("Build CoreBridge before running the causal goal pilot")
    game_closed()
    target.parent.mkdir(parents=True, exist_ok=True)
    target.mkdir(exist_ok=False)
    frozen = _freeze_files()
    model_ids = {"d3": _model_id(get_json(d3_url + "/v1/models"), "D3"),
                 "qwen": _model_id(get_json(qwen_url + "/v1/models"), "Qwen")}
    manifest = {
        "status": "FROZEN_BEFORE_INFERENCE", "frozenUtc": datetime.now(timezone.utc).isoformat(),
        "files": frozen, "modelIds": model_ids, "scenarioCount": len(SCENARIOS),
        "armsPerScenario": ["original", "reversed"], "plannedCoreSessions": len(SCENARIOS) * 2,
        "scope": "Language-generated one-item Purchase-to-Transfer plans through actual Core state; explicit synthetic receipts only; not native effects or real NPC consent.",
        "fixture": {"stockUid": ITEM_UID, "quoteGold": QUOTE_GOLD, "disposition": 65,
                    "willingness": "willing to help direct modest requests", "maxNpcSpendGold": 9,
                    "simulationConsent": "explicit Core bridge input only; never model or real NPC consent"},
        "scenarios": [{"id": scenario["id"], "playerText": scenario["playerText"],
                       "history": scenario["history"], "expected": scenario["expected"]} for scenario in SCENARIOS],
    }
    (target / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    results_path = target / "results.jsonl"
    for index, scenario in enumerate(SCENARIOS):
        for reverse in (False, True):
            _verify_frozen(frozen)
            result = _run_arm(scenario, reverse, d3_url, qwen_url, model_ids, BRIDGE_PATH,
                              target, args.timeout, frozen, index)
            _append_result(results_path, result)
            print(json.dumps({"scenarioId": result["scenarioId"], "arm": result["arm"],
                              "status": result["status"], "route": result.get("route"),
                              "decision": result.get("decision"), "coreAccepted": result.get("coreAccepted"),
                              "error": result.get("error")}, ensure_ascii=False), flush=True)


def main(argv=None):
    _run(_parse_args(argv))


if __name__ == "__main__":
    main()
