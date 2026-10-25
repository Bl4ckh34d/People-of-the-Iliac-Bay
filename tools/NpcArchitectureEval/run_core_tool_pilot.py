"""Actual Core inventory reads and receipt-driven changes, with local model tools.

This tests read-tool orchestration and state grounding, NOT free goal planning.
The two-step task and explicit synthetic receipts are fixture-controlled inputs.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[1]
sys.path.insert(0, str(ROOT))

from model_client import _validate_loopback_http, post_json  # noqa: E402
from run_evaluation import game_closed, get_json  # noqa: E402


DEFAULT_D3_URL = "http://127.0.0.1:8093"
DEFAULT_QWEN_URL = "http://127.0.0.1:8080"


def _parse_args(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--run-dir", required=True,
                        help="new output directory; relative paths resolve from repository root")
    parser.add_argument("--d3-url", default=DEFAULT_D3_URL,
                        help="loopback HTTP base URL for D3 /v1/systemone (default: 127.0.0.1:8093)")
    parser.add_argument("--qwen-url", default=DEFAULT_QWEN_URL,
                        help="loopback HTTP base URL for Qwen /v1/chat/completions (default: 127.0.0.1:8080)")
    return parser.parse_args(argv)


def _resolve_new_run_dir(value):
    path = Path(value)
    target = path.resolve() if path.is_absolute() else (REPO / path).resolve()
    if target.exists():
        raise FileExistsError("Run directory already exists: " + str(target))
    return target

def _model_id(models, label):
    entries = models.get("data") if isinstance(models, dict) else None
    if not isinstance(entries, list) or not entries or not isinstance(entries[0], dict):
        raise RuntimeError(label + " /v1/models response has no model entry")
    model_id = entries[0].get("id")
    if not isinstance(model_id, str) or not model_id:
        raise RuntimeError(label + " /v1/models response has no model id")
    return model_id


def _validate_core_response(response, request_id):
    if not isinstance(response, dict) or response.get("requestId") != request_id:
        raise RuntimeError("Core command correlation mismatch")
    if not response.get("ok"):
        raise RuntimeError("Core command failed")
    return response.get("result")


class CoreSession:
    def __init__(self, binary, event_path):
        self.process = subprocess.Popen(
            ["dotnet", str(binary), "--session"], stdin=subprocess.PIPE,
            stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, encoding="utf-8")
        self.event_path = event_path
        self.sequence = 0

    def command(self, name, **arguments):
        self.sequence += 1
        request_id = str(self.sequence)
        payload = {"command": name, "requestId": request_id, **arguments}
        self.process.stdin.write(json.dumps(payload) + "\n")
        self.process.stdin.flush()
        line = self.process.stdout.readline()
        if not line:
            raise RuntimeError("Core bridge exited before response")
        response = json.loads(line)
        with self.event_path.open("a", encoding="utf-8") as stream:
            stream.write(json.dumps({"request": payload, "response": response}) + "\n")
        return _validate_core_response(response, request_id)

    def close(self):
        if self.process.stdin and not self.process.stdin.closed:
            self.process.stdin.close()
        try:
            self.process.wait(timeout=10)
        except subprocess.TimeoutExpired:
            self.process.kill()
            self.process.wait()


def _accepted(result):
    if not isinstance(result, dict) or not result.get("accepted"):
        raise RuntimeError("Core rejected explicit fixture event: " + json.dumps(result))
    return result


def _inventory_conversation(label, buyer, d3_url, qwen_url, qwen, core, target, rows):
    before = core.command("read_state")["snapshotJson"]
    observations = []
    player_text = "What are you carrying, and how much gold do you have on you right now?"
    for turn in range(3):
        state = {"actorId": buyer, "playerText": player_text, "observations": observations,
                 "readTools": [{"id": "own_inventory", "description": "Fetch current NPC own wallet and items from the native inventory provider. Read only."}],
                 "note": "No inventory facts are available except returned observations. Do not infer current holdings from the player question."}
        payload = {"state": json.dumps(state, separators=(",", ":")),
            "questions": {"tool": {"type": "choice", "instructions": "Choose whether to fetch the relevant current facts before answering. If the tool already returned current facts, answer from them without another read.",
                "criteria": {"read_own_inventory": "Fetch current own inventory using the offered read tool.",
                             "answer": "Enough facts are available; answer now.",
                             "unavailable": "Relevant facts cannot be fetched with the offered tool; say they are unknown."}}}}
        result = post_json(d3_url + "/v1/systemone", payload, target / "model-attempts.jsonl",
                           {"checkpoint": label, "stage": "tool_choice", "turn": turn})
        choice = (result["response_json"] or {}).get("answers", {}).get("tool", {}).get("choice")
        if choice == "read_own_inventory":
            observations.append({"source": "current Core own-inventory reader; synthetic fixture world",
                                 "data": core.command("read_inventory", personId=buyer)})
        elif choice in ("answer", "unavailable"):
            break
        else:
            raise RuntimeError("Tool selection failed")
    schema = {"type": "object", "properties": {"reply": {"type": "string"}, "knownGold": {"type": ["integer", "null"]},
              "knownItemCount": {"type": ["integer", "null"]}}, "required": ["reply", "knownGold", "knownItemCount"], "additionalProperties": False}
    result = post_json(qwen_url + "/v1/chat/completions", {"model": qwen, "temperature": 0, "seed": 42, "max_tokens": 160,
        "messages": [{"role": "system", "content": "You are the NPC speaking English. Answer only from returned own-inventory observations. Unknown fields stay null. No observation means you cannot report holdings. Do not claim any action happened. Return the requested JSON with a short natural reply and current observed values."},
                     {"role": "user", "content": json.dumps({"playerText": player_text, "observations": observations})}],
        "response_format": {"type": "json_schema", "json_schema": {"name": "inventory_reply", "strict": True, "schema": schema}}},
        target / "model-attempts.jsonl", {"checkpoint": label, "stage": "reply"})
    reply = json.loads(result["response_json"]["choices"][0]["message"]["content"])
    actual = core.command("read_inventory", personId=buyer)
    unchanged = before == core.command("read_state")["snapshotJson"]
    row = {"checkpoint": label, "toolReads": len(observations), "reply": reply, "actual": actual,
           "factsCorrect": reply["knownGold"] == actual["walletGold"] and reply["knownItemCount"] == len(actual["items"]),
           "readOnlyStateUnchanged": unchanged, "nativeEffectObserved": False}
    rows.append(row)
    (target / "results.json").write_text(json.dumps(rows, indent=2), encoding="utf-8")
    print(json.dumps(row), flush=True)


def _run(args):
    target = _resolve_new_run_dir(args.run_dir)
    d3_url = args.d3_url.rstrip("/")
    qwen_url = args.qwen_url.rstrip("/")
    _validate_loopback_http(d3_url)
    _validate_loopback_http(qwen_url)
    binary = REPO / "tools/NpcArchitectureEval/CoreBridge/bin/Debug/net8.0/CoreBridge.dll"
    core_library = binary.parent / "PeopleOfTheIliacBay.Core.dll"
    if not binary.is_file() or not core_library.is_file():
        raise FileNotFoundError("Build CoreBridge before running the pilot")
    game_closed()
    target.parent.mkdir(parents=True, exist_ok=True)
    target.mkdir(exist_ok=False)
    qwen = _model_id(get_json(qwen_url + "/v1/models"), "Qwen")
    d3 = _model_id(get_json(d3_url + "/v1/models"), "D3")
    frozen_files = [Path(__file__).resolve(), binary, core_library, ROOT / "model_client.py", ROOT / "run_evaluation.py"]
    manifest = {"frozenUtc": datetime.now(timezone.utc).isoformat(),
        "files": {str(path): hashlib.sha256(path.read_bytes()).hexdigest() for path in frozen_files},
        "qwen": qwen, "d3": d3,
        "scope": "Model-selected own-inventory read through persistent actual Core; fixture-controlled task and synthetic receipts. Not language-to-task planning or native effects."}
    (target / "manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    core = CoreSession(binary, target / "core-events.jsonl")
    rows = []
    try:
        participants = core.command("read_capabilities")["participants"]
        buyer, seller, recipient = [participants[key] for key in ["buyer", "seller", "recipient"]]
        _inventory_conversation("before_purchase", buyer, d3_url, qwen_url, qwen, core, target, rows)
        task_id = "task:tool-pilot"
        plan = {"id": task_id, "proposalId": "proposal:tool-pilot", "residentId": buyer, "acceptedAt": 100, "updatedAt": 100, "deadlineAt": 1000,
                "steps": [{"kind": "Purchase", "target": {"kind": "IndoorPoint", "personId": seller, "settlementId": 100, "buildingKey": 20, "containerId": "7", "itemId": "42", "quantity": 1, "quotedGold": 9}},
                          {"kind": "Transfer", "itemSourceStep": 1, "target": {"kind": "Person", "personId": recipient, "itemId": "42", "quantity": 1, "transferDirection": "NpcToCounterparty"}}]}
        _accepted(core.command("accept_task", consent=True, plan=plan))  # Explicit synthetic consent fixture, not model willingness.
        dispatched = _accepted(core.command("dispatch_next", taskId=task_id, now=101, adapterAvailable=True, consent=True, scheduleCompatible=True))
        action = dispatched["action"]
        _accepted(core.command("observe_arrival", actionId=action["id"], expectedRevision=action["revision"], now=102))
        _accepted(core.command("apply_purchase_receipt", actionId=action["id"], expectedRevision=2, now=103, buyerGoldAfter=21, sellerGoldAfter=9, itemUid=12345, nativePayload="fixture:shop-stock-item"))
        _accepted(core.command("reconcile", taskId=task_id, expectedRevision=2, now=103))
        _inventory_conversation("after_purchase", buyer, d3_url, qwen_url, qwen, core, target, rows)
        snapshot = core.command("read_state")["snapshotJson"]
        _accepted(core.command("restore_snapshot", snapshotJson=snapshot))
        dispatched = _accepted(core.command("dispatch_next", taskId=task_id, now=104, adapterAvailable=True, consent=True, scheduleCompatible=True))
        action = dispatched["action"]
        _accepted(core.command("observe_arrival", actionId=action["id"], expectedRevision=action["revision"], now=105))
        _accepted(core.command("apply_transfer_receipt", actionId=action["id"], expectedRevision=2, now=106, accepted=True, itemUid=12345))
        _accepted(core.command("reconcile", taskId=task_id, expectedRevision=4, now=106))
        _inventory_conversation("after_restore_and_handover", buyer, d3_url, qwen_url, qwen, core, target, rows)
        final_state = core.command("read_state")
        (target / "final-state.json").write_text(json.dumps(final_state, indent=2), encoding="utf-8")
        print("Final Core task:", final_state["tasks"][0]["phase"])
    finally:
        core.close()


def main(argv=None):
    args = _parse_args(argv)
    _run(args)


if __name__ == "__main__":
    main()
