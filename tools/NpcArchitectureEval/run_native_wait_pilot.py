"""Fixture-only native route to Wait interpretation and actual-Core completion pilot.

This runner never dispatches into Unity. Its only completion observation is a
synthetic Core fixture receipt, recorded separately from native effects.
"""
from __future__ import annotations

import argparse
import copy
import hashlib
import json
import math
import subprocess
import sys
import time
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[1]
sys.path.insert(0, str(ROOT))

import goal_protocol
import run_native_route_replay as route_replay
from model_client import _validate_loopback_http, post_json
from run_core_tool_pilot import CoreSession, _accepted, _model_id
from run_evaluation import game_closed, get_json

DEFAULT_D3_URL = "http://127.0.0.1:8093"
DEFAULT_QWEN_URL = "http://127.0.0.1:8080"
DEFAULT_SUITE = ROOT / "fixtures/native-wait-chain-v1.json"
MAX_CANDIDATE_BYTES = 64 * 1024
NATIVE_BUDGET_SECONDS = 10.0
FIXTURE_NOW = 100.0
FIXTURE_DEADLINE = 2000.0
ROUTE_PROTOCOL = "npc-speech-act-route-v2"


def _parse_args(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--route-template", required=True, help="captured native v2 /v1/systemone request JSON")
    parser.add_argument("--route-vocabulary", required=True, help="captured native v2 route-vocabulary.json")
    parser.add_argument("--wait-candidate", required=True, help="native-wait-candidate-v1 JSON sidecar")
    parser.add_argument("--suite", default=str(DEFAULT_SUITE), help="frozen native-wait-chain suite JSON")
    parser.add_argument("--cases-sha256", required=True, help="expected SHA-256 of canonical suite cases array")
    parser.add_argument("--run-dir", required=True, help="fresh output directory")
    parser.add_argument("--d3-url", default=DEFAULT_D3_URL, help="loopback D3 model base URL")
    parser.add_argument("--qwen-url", default=DEFAULT_QWEN_URL, help="loopback Qwen interpreter base URL")
    parser.add_argument("--expected-d3-model", required=True, help="exact expected D3 /v1/models ID")
    parser.add_argument("--expected-qwen-model", required=True, help="exact expected Qwen /v1/models ID")
    parser.add_argument("--timeout", type=float, default=NATIVE_BUDGET_SECONDS,
                        help="single-attempt request timeout in seconds (default: 10)")
    parser.add_argument("--diagnostic-timeout", type=float,
                        help="explicit diagnostic timeout above native budget (must exceed 10 and be at most 120 seconds)")
    return parser.parse_args(argv)


def _resolve(value: str | Path) -> Path:
    path = Path(value)
    return (path if path.is_absolute() else REPO / path).resolve()


def _sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def _json_bytes(value: Any) -> bytes:
    return json.dumps(value, ensure_ascii=False, separators=(",", ":"), allow_nan=False).encode("utf-8")


def _write_json(path: Path, value: Any):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2, allow_nan=False) + "\n", encoding="utf-8")


def _append_jsonl(path: Path, row: dict[str, Any]):
    with path.open("a", encoding="utf-8", newline="\n") as stream:
        stream.write(json.dumps(row, ensure_ascii=False, allow_nan=False, separators=(",", ":")) + "\n")
        stream.flush()


def _load_candidate(path: Path):
    if not path.is_file() or path.stat().st_size > MAX_CANDIDATE_BYTES:
        raise ValueError("Wait candidate sidecar is missing or exceeds size limit")
    raw = path.read_bytes()
    try:
        value = route_replay._strict_json_loads(raw.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError, ValueError) as exc:
        raise ValueError("Wait candidate sidecar must be strict UTF-8 JSON") from exc
    return value, raw


def validate_candidate(candidate: Any, template_sha256: str):
    if not isinstance(candidate, dict):
        raise ValueError("Wait candidate sidecar must be an object")
    if candidate.get("schemaVersion") != 1 or candidate.get("schema") != "native-wait-candidate-v1":
        raise ValueError("Unsupported native Wait candidate schema")
    if candidate.get("purpose") != ROUTE_PROTOCOL or candidate.get("nativeEffectObserved") is not False:
        raise ValueError("Wait candidate purpose/effect metadata mismatch")
    if candidate.get("sourceCaptureSha256") != template_sha256:
        raise ValueError("Wait candidate source capture hash does not match route template capture hash")
    binding = candidate.get("binding")
    binding_fields = {"actorCategory", "identityKind", "personId", "ephemeralSpeakerToken", "sessionId",
                      "requestRevision", "saveEpoch", "archiveId", "archiveHead", "sceneId", "sceneRevision"}
    if not isinstance(binding, dict) or not binding_fields.issubset(binding):
        raise ValueError("Wait candidate binding is incomplete")
    if not all(isinstance(binding[key], str) and binding[key] for key in
               ("actorCategory", "identityKind", "personId", "sessionId", "archiveId", "sceneId")):
        raise ValueError("Wait candidate binding identity strings must be non-empty")
    if not isinstance(binding["archiveHead"], str) or binding["ephemeralSpeakerToken"] is not None and not isinstance(binding["ephemeralSpeakerToken"], str):
        raise ValueError("Wait candidate archive/speaker binding has invalid types")
    if any(isinstance(binding[key], bool) or not isinstance(binding[key], int) or binding[key] < 0
           for key in ("requestRevision", "saveEpoch", "sceneRevision")):
        raise ValueError("Wait candidate binding revisions must be non-negative integers")
    item = candidate.get("candidate")
    if (not isinstance(item, dict) or item.get("kind") != "Wait" or
            (not isinstance(item.get("id"), (str, int)) or isinstance(item.get("id"), bool) or not item["id"]) or
            not isinstance(item.get("label"), str) or not item["label"].strip()):
        raise ValueError("Candidate must contain one identified native Wait")
    target = item.get("target")
    if not isinstance(target, dict) or not target:
        raise ValueError("Native Wait target must be an exact non-empty object")
    if (not isinstance(candidate.get("snapshotRevision"), int) or isinstance(candidate.get("snapshotRevision"), bool) or
            candidate["snapshotRevision"] < 0):
        raise ValueError("Native Wait snapshot revision must be a non-negative integer")
    game_time = candidate.get("captureTimeGameSeconds")
    if isinstance(game_time, bool) or not isinstance(game_time, (int, float)) or not math.isfinite(game_time):
        raise ValueError("Native Wait capture game time must be finite")
    if not isinstance(candidate.get("captureTimeUtc"), str) or not candidate["captureTimeUtc"]:
        raise ValueError("Native Wait candidate needs captureTimeUtc")
    goal_protocol._target({"kind": "Wait", "target": copy.deepcopy(target)})
    return copy.deepcopy(item)


def _validate_suite(suite: Any, expected_hash: str):
    if not isinstance(suite, dict) or suite.get("schemaVersion") != 1 or not isinstance(suite.get("cases"), list) or not suite["cases"]:
        raise ValueError("Wait suite must have schemaVersion 1 and non-empty cases")
    cases = suite["cases"]
    digest = route_replay.cases_sha256(cases)
    if suite.get("casesSha256") != digest or expected_hash.lower() != digest:
        raise ValueError("Wait suite casesSha256 does not match canonical case data")
    seen = set()
    for case in cases:
        if not isinstance(case, dict) or not isinstance(case.get("id"), str) or not case["id"] or case["id"] in seen:
            raise ValueError("Every wait case needs a unique non-empty id")
        seen.add(case["id"])
        if not isinstance(case.get("playerText"), str) or not case["playerText"].strip():
            raise ValueError("Wait case needs playerText")
        if not isinstance(case.get("category"), str) or not case["category"].strip():
            raise ValueError("Wait case needs category")
        if not isinstance(case.get("attributedHistory"), list):
            raise ValueError("Wait case needs attributedHistory")
        for turn in case["attributedHistory"]:
            if (not isinstance(turn, dict) or turn.get("speaker") not in ("player", "npc") or
                    not isinstance(turn.get("text"), str)):
                raise ValueError("Wait case has malformed attributed history")
            route_replay._validate_multiline(turn["text"], route_replay.MAX_TURN_CHARS, "Wait history text")
        route_replay._validate_multiline(case["playerText"], route_replay.MAX_TURN_CHARS, "Wait playerText")
        if sum(route_replay._utf16_length(turn["text"]) for turn in case["attributedHistory"]) > route_replay.MAX_HISTORY_CHARS:
            raise ValueError("Wait case attributed history exceeds native history bound")
        expected = case.get("expected")
        if (not isinstance(expected, dict) or not isinstance(expected.get("routes"), list) or not expected["routes"] or
                any(route not in route_replay.ROUTE_SET for route in expected["routes"]) or
                not isinstance(expected.get("intentOutcomes"), list) or not expected["intentOutcomes"] or
                any(intent not in goal_protocol.INTENTS for intent in expected["intentOutcomes"]) or
                expected.get("outcome") not in ("completed", "clarify", "no_action")):
            raise ValueError("Wait case has invalid expected scoring labels")
        if expected["outcome"] == "completed" and (isinstance(expected.get("durationSeconds"), bool) or
                not isinstance(expected.get("durationSeconds"), (int, float)) or
                not math.isfinite(expected["durationSeconds"]) or not 10 <= expected["durationSeconds"] <= 900):
            raise ValueError("Completed Wait case needs an in-bounds expected duration")
        if expected["outcome"] != "completed" and expected.get("durationSeconds") is not None:
            raise ValueError("Non-completed Wait case must not specify a gold duration")
    return digest


def _contains_gold_fields(value: Any):
    forbidden = {"expected", "intentOutcomes", "durationSeconds", "outcome"}
    if isinstance(value, dict):
        return any(key in forbidden or _contains_gold_fields(item) for key, item in value.items())
    if isinstance(value, list):
        return any(_contains_gold_fields(item) for item in value)
    return False


def _validate_native_binding(candidate: dict[str, Any], state: dict[str, Any], native_context: dict[str, Any]):
    binding = candidate["binding"]
    if state.get("actorId") != binding["personId"]:
        raise ValueError("Wait candidate person binding does not match captured route actor")
    if candidate.get("snapshotRevision") != state.get("snapshotRevision"):
        raise ValueError("Wait candidate snapshot revision does not match captured route snapshot")
    snapshot_scene = native_context.get("npcSnapshot", {}).get("scene")
    if isinstance(snapshot_scene, str) and binding["sceneId"] != snapshot_scene:
        raise ValueError("Wait candidate scene binding does not match captured native snapshot")


def build_wait_case(case: dict[str, Any], native_context: dict[str, Any], candidate: dict[str, Any]):
    native_candidate = validate_candidate(candidate, candidate.get("sourceCaptureSha256"))
    snapshot = {"npcSnapshot": copy.deepcopy(native_context["npcSnapshot"]),
                "candidates": [{"ref": "wait_here", "kind": "Wait", "label": native_candidate["label"],
                                "target": copy.deepcopy(native_candidate["target"])}],
                "pendingOffer": None, "activeCommitment": None,
                "historicalMemory": copy.deepcopy(native_context.get("historicalMemory", []))}
    return {"id": case["id"], "category": case["category"], "playerText": case["playerText"],
            "attributedHistory": copy.deepcopy(case["attributedHistory"]),
            "snapshot": snapshot, "expectedRoutes": list(case["expected"]["routes"]),
            "expected": copy.deepcopy(case["expected"])}


def _route_case(case: dict[str, Any]):
    return {"id": case["id"], "category": case["category"], "playerText": case["playerText"],
            "attributedHistory": copy.deepcopy(case["attributedHistory"]), "commitments": [],
            "pendingOffer": None, "expectedRoutes": list(case["expected"]["routes"])}


def build_interpreter_payload(case: dict[str, Any], model: str):
    payload = goal_protocol.interpretation_payload(case, model)
    user = {"npcSnapshot": copy.deepcopy(case["snapshot"]["npcSnapshot"]),
            "latestPlayerTurn": {"speaker": "player", "text": case["playerText"]},
            "attributedHistory": copy.deepcopy(case["attributedHistory"]),
            "pendingOffer": None, "activeCommitment": None,
            "availableCandidates": copy.deepcopy(case["snapshot"]["candidates"]),
            "historicalMemory": copy.deepcopy(case["snapshot"].get("historicalMemory", []))}
    payload["messages"][1]["content"] = json.dumps(user, ensure_ascii=False, separators=(",", ":"), allow_nan=False)
    return payload


def _read_response_json(response: dict[str, Any]):
    raw = response.get("response_raw")
    if not isinstance(raw, str):
        raise ValueError("Model response missing raw JSON")
    return route_replay._strict_json_loads(raw)


def _parse_interpretation(response: dict[str, Any], expected_model: str, case: dict[str, Any]):
    if response.get("transport_error"):
        return None, "transport_error"
    if response.get("status_code") != 200 or response.get("response_decode_error") or response.get("malformed_json"):
        return None, "http_or_decode_error"
    try:
        outer = _read_response_json(response)
    except (json.JSONDecodeError, ValueError, TypeError):
        return None, "malformed_response_json"
    if not isinstance(outer, dict) or outer.get("model") != expected_model:
        return None, "model_mismatch"
    choices = outer.get("choices")
    if (not isinstance(choices, list) or len(choices) != 1 or not isinstance(choices[0], dict) or
            choices[0].get("finish_reason") != "stop"):
        return None, "invalid_response_schema"
    message = choices[0].get("message")
    content = message.get("content") if isinstance(message, dict) else None
    if not isinstance(content, str):
        return None, "invalid_response_schema"
    try:
        extraction = route_replay._strict_json_loads(content)
    except (json.JSONDecodeError, ValueError, TypeError):
        return None, "invalid_interpreter_json"
    valid, error = goal_protocol.validate_extraction(case, extraction)
    if not valid:
        return None, error or "invalid_interpretation"
    return extraction, None


def _duration(step):
    value = step.get("durationSeconds")
    return value if isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value) else None


def score_stages(case: dict[str, Any], route: str | None, interpretation: dict[str, Any] | None,
                 materialized: dict[str, Any], core_result: dict[str, Any] | None = None,
                 interpreter_skipped: bool = False):
    expected = case["expected"]
    expected_routes = expected["routes"]
    route_correct = route in expected_routes
    observed = interpretation is not None
    intents = expected["intentOutcomes"]
    intent_correct = (interpretation.get("intent") in intents) if observed else None
    steps = materialized.get("steps") if isinstance(materialized, dict) else None
    steps = steps if isinstance(steps, list) else []
    expected_completed = expected["outcome"] == "completed"
    arguments_correct = None
    if materialized.get("outcome") == "plan":
        candidates = case.get("snapshot", {}).get("candidates", [])
        exact_target = False
        if len(candidates) == 1 and isinstance(steps, list) and len(steps) == 1:
            try:
                exact_target = steps[0].get("target") == goal_protocol._target(candidates[0])
            except (KeyError, TypeError, ValueError):
                exact_target = False
        arguments_correct = (len(steps) == 1 and steps[0].get("kind") == "Wait" and
                             steps[0].get("targetRef") == "wait_here" and
                             _duration(steps[0]) == expected.get("durationSeconds") and exact_target) if expected_completed else False
    elif expected_completed:
        arguments_correct = False
    core_result = core_result or {}
    plan_selected = materialized.get("outcome") == "plan"
    task_completed = bool(core_result.get("taskCompleted"))
    matched_effect = bool(core_result.get("effectIdMatched"))
    false_accepted = bool(core_result.get("accepted") and plan_selected and (not expected_completed or not route_correct or
                         intent_correct is False or arguments_correct is False))
    return {"route": route, "routeExpected": expected_routes, "routeCorrect": bool(route_correct),
            "routeClass": "correct" if route_correct else "wrong_route" if route is not None else "no_route",
            "interpreterSkipped": bool(interpreter_skipped), "interpreterObserved": observed,
            "intent": interpretation.get("intent") if observed else None,
            "intentCorrect": intent_correct, "interpretationCorrect": intent_correct,
            "interpretationAttempted": not bool(interpreter_skipped),
            "extractionError": core_result.get("extractionError"),
            "materializedOutcome": materialized.get("outcome"), "argumentsCorrect": arguments_correct,
            "accepted": bool(core_result.get("accepted")), "falseAccepted": false_accepted,
            "dispatched": bool(core_result.get("dispatched")), "actionCompleted": bool(core_result.get("actionCompleted")),
            "taskCompleted": task_completed, "effectIdMatched": matched_effect,
            "completionClaimAllowed": bool(core_result.get("completionClaimAllowed")),
            "nativeEffectsObserved": False, "fixtureReceipt": bool(core_result.get("fixtureReceipt")),
            "expectedOutcome": expected["outcome"]}


def single_supported_wait(case: dict[str, Any], extraction: dict[str, Any], materialized: dict[str, Any]):
    if (not isinstance(extraction, dict) or extraction.get("intent") != "request" or
            extraction.get("conditions") or extraction.get("missing") or
            materialized.get("outcome") != "plan"):
        return False
    steps = materialized.get("steps")
    if not (isinstance(steps, list) and len(steps) == 1 and isinstance(steps[0], dict) and
            steps[0].get("kind") == "Wait" and steps[0].get("targetRef") == "wait_here" and
            isinstance(steps[0].get("target"), dict) and bool(steps[0]["target"])):
        return False
    candidates = case.get("snapshot", {}).get("candidates", [])
    if len(candidates) != 1:
        return False
    try:
        return steps[0]["target"] == goal_protocol._target(candidates[0])
    except (KeyError, TypeError, ValueError):
        return False


def completion_claim_allowed(state: dict[str, Any]):
    return (state.get("taskPhase") == "Completed" and isinstance(state.get("effectId"), str) and
            bool(state["effectId"]) and state.get("completedEffectId") == state.get("effectId"))


def _core_command(session: CoreSession, name: str, **args):
    return session.command(name, **args)


def exercise_wait_in_core(binary: Path, event_path: Path, case_id: str, native_person_id: str,
                          plan_step: dict[str, Any]):
    session = CoreSession(binary, event_path)
    task_id = "task:wait-pilot:" + case_id
    proposal_id = "proposal:wait-pilot:" + case_id
    result = {"accepted": False, "dispatched": False, "actionCompleted": False,
              "taskCompleted": False, "effectIdMatched": False, "completionClaimAllowed": False,
              "fixtureReceipt": False, "nativeEffectsObserved": False}
    try:
        buyer = _core_command(session, "read_capabilities")["participants"]["buyer"]
        result["nativePersonId"] = native_person_id
        result["coreResidentId"] = buyer
        result["coreResidentMapping"] = "buyer from read_capabilities; synthetic fixture only"
        task_plan = {"id": task_id, "proposalId": proposal_id, "residentId": buyer,
                     "acceptedAt": FIXTURE_NOW, "updatedAt": FIXTURE_NOW, "deadlineAt": FIXTURE_DEADLINE,
                     "steps": [{"kind": "Wait", "durationSeconds": plan_step["durationSeconds"],
                                "target": copy.deepcopy(plan_step["target"])}]}
        no_consent = _core_command(session, "accept_task", consent=False, plan=task_plan)
        if no_consent.get("accepted"):
            raise RuntimeError("Core accepted fixture task without explicit synthetic consent")
        accepted = _core_command(session, "accept_task", consent=True, plan=task_plan)
        if not accepted.get("accepted"):
            return dict(result, acceptReason=accepted.get("reason"), accepted=False)
        result["accepted"] = True
        duplicate = _core_command(session, "accept_task", consent=True, plan=task_plan)
        if duplicate.get("accepted"):
            raise RuntimeError("Core accepted duplicate fixture Wait task")
        dispatched = _core_command(session, "dispatch_next", taskId=task_id, now=101,
                                   adapterAvailable=True, consent=True, scheduleCompatible=True)
        if not dispatched.get("accepted"):
            return dict(result, dispatchReason=dispatched.get("reason"))
        result["dispatched"] = True
        action = dispatched["action"]
        action_id = action["id"]
        if action.get("kind") != "Wait" or action.get("finishAt", 0) <= 101 or action.get("deadlineAt") != FIXTURE_DEADLINE:
            raise RuntimeError("Core action did not expose expected Wait FinishAt/DeadlineAt")
        arrived = _core_command(session, "observe_arrival", actionId=action_id,
                                 expectedRevision=action["revision"], now=102)
        if not arrived.get("accepted") or arrived["action"].get("phase") != "Executing":
            raise RuntimeError("Core did not retain Wait as executing after arrival observation")
        executing_snapshot = _core_command(session, "read_state")["snapshotJson"]
        restored = _core_command(session, "restore_snapshot", snapshotJson=executing_snapshot)
        if not restored.get("accepted"):
            raise RuntimeError("Core rejected restore while Wait was executing")
        current = _core_command(session, "read_state")
        action_state = next(item for item in current["actions"] if item["id"] == action_id)
        if action_state.get("phase") != "Executing" or action_state.get("revision") != arrived["action"]["revision"]:
            raise RuntimeError("Core restore changed executing Wait ownership or revision")
        early = _core_command(session, "observe_wait_completion", actionId=action_id,
                              expectedRevision=action_state["revision"], now=action_state["finishAt"] - 1)
        if early.get("accepted") or early["action"].get("phase") != "Executing":
            raise RuntimeError("Core accepted early Wait completion")
        completed = _core_command(session, "observe_wait_completion", actionId=action_id,
                                  expectedRevision=action_state["revision"], now=action_state["finishAt"])
        receipt = completed.get("accepted") and completed.get("fixtureReceipt") is True
        result["fixtureReceipt"] = bool(receipt)
        result["actionCompleted"] = bool(receipt and completed["action"].get("phase") == "Completed")
        result["effectId"] = completed["action"].get("completedEffectId")
        expected_effect_id = action_id + ":observed"
        result["nativeEffectsObserved"] = completed.get("nativeEffectObserved") is True
        if result["nativeEffectsObserved"]:
            raise RuntimeError("Core fixture Wait completion must not claim a native effect")
        duplicate_completion = _core_command(session, "observe_wait_completion", actionId=action_id,
                                              expectedRevision=completed["action"]["revision"], now=action_state["finishAt"])
        if duplicate_completion.get("accepted"):
            raise RuntimeError("Core accepted duplicate Wait completion")
        state = _core_command(session, "read_state")
        task_state = next(item for item in state["tasks"] if item["id"] == task_id)
        reconciled = _core_command(session, "reconcile", taskId=task_id,
                                   expectedRevision=task_state["revision"], now=action_state["finishAt"])
        final_task = reconciled.get("task") or {}
        result["taskCompleted"] = final_task.get("phase") == "Completed"
        terminal_state = _core_command(session, "read_state")
        terminal_action = next(item for item in terminal_state["actions"] if item["id"] == action_id)
        result["effectIdMatched"] = (result["effectId"] == expected_effect_id and
                                      terminal_action.get("completedEffectId") == result["effectId"])
        result["completionClaimAllowed"] = completion_claim_allowed({"taskPhase": final_task.get("phase"),
            "effectId": result["effectId"], "completedEffectId": completed["action"].get("completedEffectId")})
        if result["completionClaimAllowed"] and not (result["taskCompleted"] and result["effectIdMatched"]):
            raise RuntimeError("Completion claim gate disagrees with Core terminal state")
        result["coreFinishAt"] = action_state["finishAt"]
        result["coreDeadlineAt"] = action_state["deadlineAt"]
        result["syntheticConsent"] = True
        result["realNpcConsent"] = False
        result["fixtureOnly"] = True
        return result
    finally:
        session.close()


def _frozen_files(template_path: Path, vocabulary_path: Path, candidate_path: Path, suite_path: Path,
                  binary: Path, core_library: Path):
    paths = [Path(__file__).resolve(), Path(route_replay.__file__).resolve(), Path(goal_protocol.__file__).resolve(),
             Path(__import__("model_client").__file__).resolve(), Path(__import__("run_core_tool_pilot").__file__).resolve(),
             Path(__import__("run_evaluation").__file__).resolve(), template_path, vocabulary_path, candidate_path,
             suite_path, binary, core_library]
    core_source = REPO / "src/PeopleOfTheIliacBay.Core"
    bridge_source = ROOT / "CoreBridge"
    paths.extend(sorted(p for p in core_source.rglob("*.cs") if p.is_file()))
    paths.extend(sorted(p for p in bridge_source.glob("*.cs") if p.is_file()))
    paths.extend([core_source / "PeopleOfTheIliacBay.Core.csproj", bridge_source / "CoreBridge.csproj"])
    return {str(path.resolve()): _sha(path.read_bytes()) for path in paths}


def _prepare(argv=None):
    args = _parse_args(argv)
    if not args.expected_d3_model or len(args.expected_d3_model) > 512 or not args.expected_qwen_model or len(args.expected_qwen_model) > 512:
        raise ValueError("Expected model IDs must be non-empty exact IDs")
    if not math.isfinite(args.timeout) or args.timeout <= 0 or args.timeout > NATIVE_BUDGET_SECONDS:
        raise ValueError("--timeout must be > 0 and <= native 10-second budget")
    timeout = args.timeout
    if args.diagnostic_timeout is not None:
        if (not math.isfinite(args.diagnostic_timeout) or args.diagnostic_timeout <= NATIVE_BUDGET_SECONDS or
                args.diagnostic_timeout > 120):
            raise ValueError("--diagnostic-timeout must be > 10 and <= 120 seconds")
        timeout = args.diagnostic_timeout
    d3_url, qwen_url = args.d3_url.rstrip("/"), args.qwen_url.rstrip("/")
    _validate_loopback_http(d3_url)
    _validate_loopback_http(qwen_url)
    template_path = _resolve(args.route_template)
    vocabulary_path = _resolve(args.route_vocabulary)
    candidate_path = _resolve(args.wait_candidate)
    suite_path = _resolve(args.suite)
    template = route_replay.load_template(template_path)
    if route_replay._template_protocol(template) != ROUTE_PROTOCOL:
        raise ValueError("Wait pilot requires native route protocol v2")
    vocabulary, vocabulary_hash = route_replay.load_route_vocabulary(vocabulary_path)
    native_criteria = template["questions"][route_replay.QUESTION_ID]["criteria"]
    if any(vocabulary["routes"].get(route) != description for route, description in native_criteria.items()):
        raise ValueError("Captured template route descriptions differ from route vocabulary")
    candidate, candidate_raw = _load_candidate(candidate_path)
    template_raw = template_path.read_bytes()
    native_candidate = validate_candidate(candidate, _sha(template_raw))
    suite_bytes = suite_path.read_bytes()
    if len(suite_bytes) > route_replay.MAX_SUITE_BYTES:
        raise ValueError("Wait suite exceeds size limit")
    suite = route_replay._strict_json_loads(suite_bytes.decode("utf-8-sig"))
    suite_hash = _validate_suite(suite, args.cases_sha256)
    run_dir_input = Path(args.run_dir)
    if not run_dir_input.is_absolute():
        run_dir_input = REPO / run_dir_input
    if run_dir_input.exists() or run_dir_input.is_symlink():
        raise ValueError("Run directory must be fresh and non-existing: " + str(run_dir_input))
    run_dir = run_dir_input.resolve()
    if run_dir.exists() or not run_dir.parent.is_dir():
        raise ValueError("Run directory parent must exist and target must be fresh: " + str(run_dir))
    binary = ROOT / "CoreBridge/bin/Debug/net8.0/CoreBridge.dll"
    core_library = binary.parent / "PeopleOfTheIliacBay.Core.dll"
    if not binary.is_file() or not core_library.is_file():
        raise FileNotFoundError("Build CoreBridge before running Wait pilot")
    state = json.loads(template["state"])
    context = json.loads(state["context"])
    _validate_native_binding(candidate, state, context)
    entries = []
    for index, gold_case in enumerate(suite["cases"]):
        case = build_wait_case(gold_case, context, candidate)
        route_case = _route_case(gold_case)
        orders = (False, True) if index % 2 == 0 else (True, False)
        for reverse in orders:
            d3_payload = route_replay.build_d3_payload(template, route_case, reverse, vocabulary)
            offered = list(d3_payload["questions"][route_replay.QUESTION_ID]["criteria"])
            if not set(gold_case["expected"]["routes"]).issubset(offered):
                raise ValueError("Case expects route unavailable in native menu: " + gold_case["id"])
            d3_bytes = _json_bytes(d3_payload)
            entry = {"index": index, "case": case, "goldCase": gold_case,
                     "nativePersonId": candidate["binding"]["personId"],
                     "arm": "reversed" if reverse else "original", "reverse": reverse,
                     "offeredRoutes": offered, "d3Payload": d3_payload,
                     "d3Bytes": d3_bytes, "d3Sha256": _sha(d3_bytes)}
            entry["interpreterPayload"] = build_interpreter_payload(case, args.expected_qwen_model)
            entry["interpreterBytes"] = _json_bytes(entry["interpreterPayload"])
            entry["interpreterSha256"] = _sha(entry["interpreterBytes"])
            if _contains_gold_fields(entry["d3Payload"]) or _contains_gold_fields(entry["interpreterPayload"]):
                raise ValueError("Gold data leaked into request payload")
            entries.append(entry)
    frozen = _frozen_files(template_path, vocabulary_path, candidate_path, suite_path, binary, core_library)
    if _sha(template_raw) != frozen[str(template_path)] or _sha(candidate_raw) != frozen[str(candidate_path)]:
        raise RuntimeError("Native template or candidate sidecar changed during preflight")
    return {"args": args, "d3Url": d3_url, "qwenUrl": qwen_url, "templatePath": template_path,
            "vocabularyPath": vocabulary_path, "candidatePath": candidate_path, "suitePath": suite_path,
            "template": template, "vocabulary": vocabulary, "vocabularySha256": vocabulary_hash,
            "candidate": candidate, "candidateItem": native_candidate, "suite": suite,
            "suiteSha256": suite_hash, "runDir": run_dir, "binary": binary, "coreLibrary": core_library,
            "entries": entries, "frozen": frozen, "timeout": timeout}


def _assert_frozen(frozen):
    for path_text, digest in frozen.items():
        path = Path(path_text)
        if not path.is_file() or _sha(path.read_bytes()) != digest:
            raise RuntimeError("Frozen wait-pilot input changed: " + path_text)


def _stage_result(response, model, case, offered_routes, elapsed):
    response_latency = response.get("latency_ms", elapsed)
    latency = max(response_latency, elapsed) if isinstance(response_latency, (int, float)) and not isinstance(response_latency, bool) else elapsed
    within = isinstance(latency, (int, float)) and not isinstance(latency, bool) and latency <= NATIVE_BUDGET_SECONDS * 1000
    if response.get("transport_error"):
        err = "timeout" if "timed out" in str(response["transport_error"]).lower() else "transport_error"
        return {"route": None, "error": err, "latencyMs": latency, "withinNativeBudget": within}
    if response.get("status_code") != 200 or response.get("response_decode_error") or response.get("malformed_json"):
        return {"route": None, "error": "http_or_decode_error", "latencyMs": latency, "withinNativeBudget": within}
    try:
        raw = _read_response_json(response)
    except (json.JSONDecodeError, ValueError, TypeError):
        return {"route": None, "error": "malformed_response_json", "latencyMs": latency, "withinNativeBudget": within}
    parsed = route_replay.parse_d3_response(raw, model, case["expectedRoutes"], offered_routes)
    return {"route": parsed.get("route"), "error": parsed.get("error"),
            "latencyMs": latency, "withinNativeBudget": within, "reportedModel": parsed.get("model"),
            "probabilities": parsed.get("probabilities"), "valid": parsed.get("valid")}


def _interpret(response, expected_model, case, elapsed):
    response_latency = response.get("latency_ms", elapsed)
    latency = max(response_latency, elapsed) if isinstance(response_latency, (int, float)) and not isinstance(response_latency, bool) else elapsed
    if latency <= NATIVE_BUDGET_SECONDS * 1000:
        extraction, error = _parse_interpretation(response, expected_model, case)
    else:
        extraction, error = None, "over_native_budget"
    return {"extraction": extraction, "error": error,
            "latencyMs": latency, "withinNativeBudget": isinstance(latency, (int, float)) and
            not isinstance(latency, bool) and latency <= NATIVE_BUDGET_SECONDS * 1000,
            "reportedModel": expected_model if not error else None}


def _execute_entry(entry, context, d3_model, qwen_model, d3_url, qwen_url, run_dir, binary, timeout, frozen):
    case = entry["case"]
    attempts = run_dir / "attempts.jsonl"
    arm_info = {"caseId": case["id"], "arm": entry["arm"]}
    game_closed()
    _assert_frozen(frozen)
    t0 = time.perf_counter()
    d3_response = post_json(d3_url + "/v1/systemone", entry["d3Payload"], attempts,
                            dict(arm_info, stage="route", model="D3"), timeout=timeout)
    d3_elapsed = (time.perf_counter() - t0) * 1000
    if d3_response.get("request_sha256") not in (None, entry["d3Sha256"]):
        raise RuntimeError("Serialized D3 request differs from frozen payload hash")
    route = _stage_result(d3_response, d3_model, case, entry["offeredRoutes"], d3_elapsed)
    extraction_result = {"extraction": None, "error": None, "latencyMs": None, "skipped": True}
    materialized = {"outcome": "invalid", "steps": []}
    core = None
    if route["error"] is None and route["withinNativeBudget"] and route["route"] == "SOCIAL":
        materialized = {"outcome": "no_action", "steps": []}
    elif route["error"] is None and route["withinNativeBudget"] and route["route"] == "ACTION_REQUEST":
        extraction_result["skipped"] = False
        game_closed()
        _assert_frozen(frozen)
        t1 = time.perf_counter()
        qwen_response = post_json(qwen_url + "/v1/chat/completions", entry["interpreterPayload"], attempts,
                                  dict(arm_info, stage="interpretation", model="Qwen"), timeout=timeout)
        if qwen_response.get("request_sha256") not in (None, entry["interpreterSha256"]):
            raise RuntimeError("Serialized interpreter request differs from frozen payload hash")
        qwen_elapsed = (time.perf_counter() - t1) * 1000
        extraction_result = _interpret(qwen_response, qwen_model, case, qwen_elapsed)
        extraction_result["skipped"] = False
        if extraction_result["error"] is None:
            materialized = goal_protocol.materialize(case, extraction_result["extraction"])
            compatible = extraction_result["extraction"]["intent"] in ("request", "clarify", "none")
            if not compatible:
                materialized = {"outcome": "invalid", "steps": [], "reason": "Route and speech act disagree"}
            if materialized.get("outcome") == "plan" and not single_supported_wait(case, extraction_result["extraction"], materialized):
                materialized = {"outcome": "invalid", "steps": [], "reason": "Wait pilot accepts exactly one resolved Wait"}
            if materialized.get("outcome") == "plan":
                game_closed()
                _assert_frozen(frozen)
                core = exercise_wait_in_core(binary, run_dir / "core-events.jsonl", case["id"],
                                             entry["nativePersonId"], materialized["steps"][0])
                core["extractionError"] = extraction_result["error"]
    result = score_stages(case, route["route"], extraction_result["extraction"], materialized,
                          core_result=core, interpreter_skipped=extraction_result["skipped"])
    result.update({"caseId": case["id"], "category": case["category"], "arm": entry["arm"],
                   "d3RequestSha256": entry["d3Sha256"], "interpreterRequestSha256": entry["interpreterSha256"],
                   "d3": route, "interpreter": extraction_result,
                   "materialized": materialized, "core": core,
                   "gold": copy.deepcopy(case["expected"])})
    return result


def _summary(rows, suite_hash):
    totals = {key: sum(bool(row.get(key)) for row in rows) for key in
              ("routeCorrect", "interpreterObserved", "accepted", "falseAccepted", "dispatched",
               "actionCompleted", "taskCompleted", "effectIdMatched", "fixtureReceipt")}
    totals["arms"] = len(rows)
    totals["suiteCasesSha256"] = suite_hash
    totals["nativeEffectsObserved"] = False
    stages = {"route": {"attempted": len(rows), "correct": totals["routeCorrect"]},
              "interpretation": {"attempted": sum(bool(row["interpretationAttempted"]) for row in rows),
                                 "observed": totals["interpreterObserved"],
                                 "correct": sum(row.get("interpretationCorrect") is True for row in rows)},
              "arguments": {"attempted": sum(row.get("argumentsCorrect") is not None for row in rows),
                            "correct": sum(row.get("argumentsCorrect") is True for row in rows)},
              "core": {"accepted": totals["accepted"], "dispatched": totals["dispatched"],
                       "actionCompleted": totals["actionCompleted"], "taskCompleted": totals["taskCompleted"],
                       "matchingEffect": totals["effectIdMatched"]}}
    return {"totals": totals, "stages": stages,
            "claims": {"npcWillingness": False, "nativeEffects": False,
                       "semanticCompletionProvenByGoldAgreement": False}}


def run(argv=None):
    prepared = _prepare(argv)
    game_closed()
    args = prepared["args"]
    run_dir = prepared["runDir"]
    run_dir.mkdir(parents=False, exist_ok=False)
    payload_dir = run_dir / "payloads"
    payload_dir.mkdir()
    payload_manifest = []
    for entry in prepared["entries"]:
        stem = f"case-{entry['index'] + 1:04d}-{entry['arm']}"
        for name, body, digest in (("route", entry["d3Bytes"], entry["d3Sha256"]),
                                   ("interpretation", entry["interpreterBytes"], entry["interpreterSha256"])):
            path = payload_dir / f"{stem}.{name}.json"
            path.write_bytes(body)
            payload_manifest.append({"caseId": entry["case"]["id"], "arm": entry["arm"],
                                     "stage": name, "path": str(path.relative_to(run_dir)), "sha256": digest})
    manifest = {"frozenUtc": datetime.now(timezone.utc).isoformat(), "files": prepared["frozen"],
                "suiteCasesSha256": prepared["suiteSha256"],
                "routeTemplateSha256": prepared["frozen"][str(prepared["templatePath"])],
                "routeVocabularySha256": prepared["vocabularySha256"],
                "candidateSha256": prepared["frozen"][str(prepared["candidatePath"])],
                "expectedD3ModelId": args.expected_d3_model, "expectedQwenModelId": args.expected_qwen_model,
                "d3BaseUrl": prepared["d3Url"], "qwenBaseUrl": prepared["qwenUrl"],
                "timeoutSeconds": prepared["timeout"], "nativeBudgetSeconds": NATIVE_BUDGET_SECONDS,
                "diagnosticTimeout": prepared["timeout"] > NATIVE_BUDGET_SECONDS,
                "candidateRef": "wait_here", "candidateTarget": prepared["candidateItem"]["target"],
                "nativePersonId": prepared["candidate"]["binding"]["personId"],
                "coreResidentMapping": "Core buyer from read_capabilities; fixture-only identity, not native actor binding",
                "fixtureCoreClock": FIXTURE_NOW, "fixtureCoreDeadline": FIXTURE_DEADLINE,
                "payloads": payload_manifest,
                "syntheticConsent": True, "realNpcConsent": False, "nativeEffectsObserved": False,
                "scope": "Route and Wait interpretation with offline actual Core validation and synthetic Wait completion receipts only."}
    _write_json(run_dir / "manifest.json", manifest)
    _assert_frozen(prepared["frozen"])
    # Core self-tests cover cancellation and duplicate receipt behavior before the run.
    self_test = subprocess.run(["dotnet", str(prepared["binary"]), "--session-self-test"],
                               capture_output=True, text=True, timeout=60, check=False)
    (run_dir / "core-self-test.stdout.txt").write_text(self_test.stdout, encoding="utf-8")
    (run_dir / "core-self-test.stderr.txt").write_text(self_test.stderr, encoding="utf-8")
    if self_test.returncode != 0 or "SessionBridge self-tests passed" not in self_test.stdout:
        raise RuntimeError("CoreBridge session self-tests failed; see captured output")
    game_closed()
    _assert_frozen(prepared["frozen"])
    d3_models = get_json(prepared["d3Url"] + "/v1/models")
    d3_model = _model_id(d3_models, "D3")
    game_closed()
    _assert_frozen(prepared["frozen"])
    qwen_models = get_json(prepared["qwenUrl"] + "/v1/models")
    qwen_model = _model_id(qwen_models, "Qwen")
    if d3_model != args.expected_d3_model or qwen_model != args.expected_qwen_model:
        manifest.update(discoveredD3ModelId=d3_model, discoveredQwenModelId=qwen_model)
        _write_json(run_dir / "manifest.json", manifest)
        raise RuntimeError("Discovered model ID does not exactly match expected model ID")
    manifest.update(discoveredD3ModelId=d3_model, discoveredQwenModelId=qwen_model)
    _write_json(run_dir / "manifest.json", manifest)
    rows = []
    results_path = run_dir / "results.jsonl"
    for entry in prepared["entries"]:
        game_closed()
        _assert_frozen(prepared["frozen"])
        row = _execute_entry(entry, None, d3_model, qwen_model, prepared["d3Url"], prepared["qwenUrl"],
                             run_dir, prepared["binary"], prepared["timeout"], prepared["frozen"])
        rows.append(row)
        _append_jsonl(results_path, row)
        print(row["caseId"], row["arm"], "complete", flush=True)
    summary = _summary(rows, prepared["suiteSha256"])
    _write_json(run_dir / "summary.json", summary)
    return summary


if __name__ == "__main__":
    run()
