"""Offline, two-phase conceptual-goal pilot. No route classifier or live effects."""
from __future__ import annotations

import argparse
import copy
import hashlib
import json
import math
import os
import time
from datetime import datetime, timezone
from decimal import Decimal, InvalidOperation
from pathlib import Path

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[1]
DEFAULT_URL = "http://127.0.0.1:8080"
TIMEOUT_SECONDS = 10
MAX_SUITE_BYTES = 2 * 1024 * 1024
MAX_RESPONSE_CHARS = 128 * 1024
NATIVE_KINDS = {"Wait", "Follow", "MoveTo", "Purchase", "Transfer", "Interact"}
TIMED = {"Wait", "Follow"}
ITEM = {"Purchase", "Transfer"}

import conceptual_goal_protocol as conceptual_protocol
import goal_protocol
import run_native_route_replay as route_replay
from model_client import post_json
from run_core_tool_pilot import _model_id
from run_evaluation import game_closed, get_json


def _parse_args(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--suite", required=True, help="frozen conceptual-goal JSON corpus")
    parser.add_argument("--cases-sha256", required=True, help="exact cases-array SHA-256")
    parser.add_argument("--run-dir", required=True, help="fresh, non-existing run directory")
    parser.add_argument("--url", default=DEFAULT_URL, help=f"loopback Qwen base URL (default: {DEFAULT_URL})")
    parser.add_argument("--expected-model", required=True, help="exact Qwen model ID")
    return parser.parse_args(argv)


def _json_bytes(value):
    return json.dumps(value, ensure_ascii=False, separators=(",", ":"), allow_nan=False).encode("utf-8")


def _sha(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest()


def _resolve(value):
    path = Path(value)
    if not path.is_absolute():
        path = REPO / path
    return path.resolve()


def _write_json(path: Path, value):
    body = json.dumps(value, ensure_ascii=False, indent=2, allow_nan=False) + "\n"
    with path.open("w", encoding="utf-8", newline="\n") as stream:
        stream.write(body)
        stream.flush()
        os.fsync(stream.fileno())


def _write_bytes(path: Path, body: bytes):
    with path.open("wb") as stream:
        stream.write(body)
        stream.flush()
        os.fsync(stream.fileno())


def _append_jsonl(path: Path, value):
    with path.open("ab") as stream:
        stream.write(_json_bytes(value) + b"\n")
        stream.flush()
        os.fsync(stream.fileno())


def _validate_suite(value, expected_hash):
    if not isinstance(value, dict) or value.get("schemaVersion") != 1:
        raise ValueError("Suite schemaVersion must be 1")
    cases = value.get("cases")
    if not isinstance(cases, list) or not cases:
        raise ValueError("Suite needs a non-empty cases list")
    if value.get("caseCount") != len(cases):
        raise ValueError("Suite caseCount does not match cases")
    digest = route_replay.cases_sha256(cases)
    if digest != expected_hash or value.get("casesSha256") != digest:
        raise ValueError("Cases SHA-256 does not match frozen suite or CLI")
    seen = set()
    for case in cases:
        if not isinstance(case, dict):
            raise ValueError("Every case must be an object")
        case_id = case.get("id")
        if not isinstance(case_id, str) or not case_id.strip() or case_id in seen:
            raise ValueError("Case IDs must be unique non-empty strings")
        seen.add(case_id)
        if not isinstance(case.get("playerText"), str) or not case["playerText"].strip():
            raise ValueError(f"Case {case_id} needs latest player text")
        history = case.get("attributedHistory", [])
        snapshot = case.get("snapshot")
        expected = case.get("expected")
        if not isinstance(history, list) or not isinstance(snapshot, dict) or not isinstance(expected, dict):
            raise ValueError(f"Case {case_id} has invalid history, snapshot or expected data")
        conceptual = expected.get("conceptual")
        if not isinstance(conceptual, dict):
            raise ValueError(f"Case {case_id} needs expected.conceptual")
        valid, reason = conceptual_protocol.validate_extraction(conceptual, case)
        if not valid:
            raise ValueError(f"Case {case_id} has invalid conceptual gold: {reason}")
        if expected.get("grounding") not in ("plan", "clarify", "no_action", "invalid"):
            raise ValueError(f"Case {case_id} has invalid expected grounding outcome")
        candidates = snapshot.get("candidates", [])
        by_ref = conceptual_protocol._candidate_index(candidates)
        for candidate in by_ref.values():
            try:
                goal_protocol._target(candidate)
            except (KeyError, TypeError, ValueError) as exc:
                raise ValueError(f"Case {case_id} has invalid candidate target: {exc}") from exc
    return digest


def _prepare(argv=None):
    args = _parse_args(argv)
    if not isinstance(args.expected_model, str) or not args.expected_model.strip() or len(args.expected_model) > 512:
        raise ValueError("--expected-model must be a non-empty exact model ID")
    url = route_replay._normalize_base_url(args.url, "Qwen")
    suite_path = _resolve(args.suite)
    if suite_path.stat().st_size > MAX_SUITE_BYTES:
        raise ValueError("Suite exceeds size limit")
    suite_raw = suite_path.read_bytes()
    suite = route_replay._strict_json_loads(suite_raw.decode("utf-8-sig"))
    suite_hash = _validate_suite(suite, args.cases_sha256)
    run_input = Path(args.run_dir)
    if not run_input.is_absolute():
        run_input = REPO / run_input
    if run_input.exists() or run_input.is_symlink():
        raise ValueError("Run directory must be fresh and non-existing: " + str(run_input))
    run_dir = run_input.resolve()
    if run_dir.exists() or not run_dir.parent.is_dir():
        raise ValueError("Run directory parent must exist and target must be fresh: " + str(run_dir))
    source_paths = [Path(__file__).resolve(), ROOT / "conceptual_goal_protocol.py", ROOT / "goal_protocol.py",
                    ROOT / "model_client.py", ROOT / "run_evaluation.py", ROOT / "run_native_route_replay.py",
                    ROOT / "run_core_tool_pilot.py", suite_path]
    frozen = {str(path.resolve()): _sha(path.read_bytes()) for path in source_paths}
    if _sha(suite_raw) != frozen[str(suite_path)]:
        raise RuntimeError("Suite changed during preflight")
    extraction_payloads = []
    for index, case in enumerate(suite["cases"]):
        payload = conceptual_protocol.conceptual_payload(case, args.expected_model)
        body = _json_bytes(payload)
        payload_text = body.decode("utf-8")
        user_data = json.loads(payload["messages"][1]["content"])
        if "GOLD_SENTINEL" in payload_text or any(key in user_data for key in ("expected", "gold")):
            raise ValueError("Gold field leaked into extraction request")
        extraction_payloads.append({"index": index, "case": case, "payload": payload, "bytes": body,
                                    "sha256": _sha(body)})
    return {"args": args, "url": url, "suitePath": suite_path, "suiteRaw": suite_raw,
            "suite": suite, "suiteSha256": suite_hash,
            "runDir": run_dir, "frozen": frozen, "extractionPayloads": extraction_payloads}


def _assert_frozen(frozen):
    for path_text, digest in frozen.items():
        path = Path(path_text)
        if not path.is_file() or _sha(path.read_bytes()) != digest:
            raise RuntimeError("Frozen conceptual-pilot input changed: " + path_text)


def _assert_written_payloads(run_dir, manifest):
    suite_copy = manifest["suiteRawCopy"]
    if _sha((run_dir / suite_copy["path"]).read_bytes()) != suite_copy["sha256"]:
        raise RuntimeError("Frozen suite copy changed")
    for item in manifest["payloads"]:
        payload_path = run_dir / item["path"]
        if not payload_path.is_file() or _sha(payload_path.read_bytes()) != item["sha256"]:
            raise RuntimeError("Frozen model payload changed: " + str(item["path"]))


def _parse_chat_response(raw, expected_model, stage):
    """Return parsed stage JSON and error. Reject duplicate keys and model drift."""
    if not isinstance(raw, str) or len(raw) > MAX_RESPONSE_CHARS:
        return None, "response_too_large_or_missing", None
    try:
        outer = route_replay._strict_json_loads(raw)
    except (ValueError, TypeError, json.JSONDecodeError):
        return None, "invalid_outer_json", None
    if not isinstance(outer, dict) or outer.get("model") != expected_model:
        return None, "wrong_model", outer.get("model") if isinstance(outer, dict) else None
    choices = outer.get("choices")
    if not isinstance(choices, list) or len(choices) != 1 or not isinstance(choices[0], dict):
        return None, "invalid_choices", expected_model
    choice = choices[0]
    if choice.get("finish_reason") != "stop":
        return None, "incomplete_generation", expected_model
    message = choice.get("message")
    content = message.get("content") if isinstance(message, dict) else None
    if not isinstance(content, str):
        return None, "missing_content", expected_model
    try:
        value = route_replay._strict_json_loads(content)
    except (ValueError, TypeError, json.JSONDecodeError):
        return None, "invalid_" + stage + "_json", expected_model
    if not isinstance(value, dict):
        return None, "invalid_" + stage + "_shape", expected_model
    return value, None, expected_model


def _latency_ms(response, elapsed):
    reported = response.get("latency_ms", elapsed)
    if isinstance(reported, bool) or not isinstance(reported, (int, float)) or not math.isfinite(reported):
        return elapsed
    return max(float(elapsed), float(reported))


def _duration_seconds(step):
    amount, unit = step.get("durationAmount"), step.get("durationUnit")
    if amount is None and unit is None:
        return Decimal(300)
    if amount is None or unit not in ("seconds", "minutes", "hours"):
        return None
    try:
        return Decimal(str(amount)) * {"seconds": 1, "minutes": 60, "hours": 3600}[unit]
    except InvalidOperation:
        return None


def score_extraction(case, extraction):
    expected = case["expected"]["conceptual"]
    if not isinstance(extraction, dict) or not extraction.get("valid"):
        return {"conceptualObserved": False, "intentCorrect": None, "stepCountCorrect": None,
                "orderedKindsCorrect": None, "numericArgumentsCorrect": None,
                "conditionsPresenceCorrect": None, "missingPresenceCorrect": None,
                "otherDescriptionPresent": None,
                "conceptualStrictCorrect": False, "sourceQuoteSyntacticValid": False,
                "sourceQuoteExactGoldSupplementary": None, "targetTextExactGoldSupplementary": None}
    actual_steps, gold_steps = extraction["steps"], expected["steps"]
    intent_correct = extraction["intent"] == expected["intent"]
    count_correct = len(actual_steps) == len(gold_steps)
    ordered = [step.get("kind") for step in actual_steps] == [step.get("kind") for step in gold_steps]
    args_correct = None
    quote_exact = None
    target_exact = None
    if count_correct and ordered:
        comparisons = []
        for actual, gold in zip(actual_steps, gold_steps):
            kind = gold["kind"]
            if kind in TIMED:
                comparisons.append(_duration_seconds(actual) == _duration_seconds(gold))
            elif kind in ITEM:
                comparisons.append(actual.get("quantity") == gold.get("quantity"))
        args_correct = all(comparisons) if comparisons else None
        quote_exact = [a.get("sourceQuote") == g.get("sourceQuote") for a, g in zip(actual_steps, gold_steps)]
        target_exact = [a.get("targetText") == g.get("targetText") for a, g in zip(actual_steps, gold_steps)]
    other_description_present = all(bool(step.get("actionDescription", "").strip())
                                     for step in actual_steps if step.get("kind") == "Other")
    conditions_presence = bool(extraction["conditions"]) == bool(expected["conditions"])
    missing_presence = bool(extraction["missing"]) == bool(expected["missing"])
    strict = (intent_correct and count_correct and ordered and args_correct is not False and
              conditions_presence and missing_presence and other_description_present)
    return {"conceptualObserved": True, "intentCorrect": intent_correct, "stepCountCorrect": count_correct,
            "orderedKindsCorrect": ordered, "numericArgumentsCorrect": args_correct,
            "conditionsPresenceCorrect": conditions_presence, "missingPresenceCorrect": missing_presence,
            "otherDescriptionPresent": other_description_present,
            "conceptualStrictCorrect": bool(strict), "sourceQuoteSyntacticValid": True,
            "sourceQuoteExactGoldSupplementary": quote_exact, "targetTextExactGoldSupplementary": target_exact}


def _can_ground(concept, candidates):
    if not concept.get("valid") or concept["intent"] != "request" or concept["conditions"] or concept["missing"]:
        return False
    if not concept["steps"] or any(step["kind"] == "Other" for step in concept["steps"]):
        return False
    available = {candidate["kind"] for candidate in candidates}
    return all(step["kind"] in available for step in concept["steps"])


def _eligible_without_model(concept, candidates):
    if not concept.get("valid"):
        return {"outcome": "invalid", "steps": [], "reason": "Invalid conceptual extraction"}
    if concept["intent"] == "none":
        return conceptual_protocol.ground(concept, [], candidates)
    if concept["conditions"] or concept["missing"] or concept["intent"] == "clarify":
        return conceptual_protocol.ground(concept, [], candidates)
    if any(step["kind"] == "Other" for step in concept["steps"]):
        return conceptual_protocol.ground(concept, [], candidates)
    if not concept["steps"] or any(step["kind"] not in {c["kind"] for c in candidates} for step in concept["steps"]):
        return conceptual_protocol.ground(concept, [{"stepIndex": i, "candidateRef": None}
                                                     for i in range(len(concept["steps"]))], candidates)
    raise ValueError("Eligible conceptual request needs model grounding")


def _score_row(case, concept, extraction_error, ground_result, arm, extraction_sha, grounding_sha, grounding_response, extraction_response):
    stage_scores = score_extraction(case, concept)
    expected_grounding = case["expected"]["grounding"]
    outcome = ground_result.get("outcome") if isinstance(ground_result, dict) else "invalid"
    is_plan = outcome == "plan"
    false_plan = is_plan and (expected_grounding != "plan" or not stage_scores["conceptualStrictCorrect"])
    correct_ground = outcome == expected_grounding
    row = {"caseId": case["id"], "category": case["category"], "arm": arm,
           "extractionError": extraction_error, "extraction": copy.deepcopy(concept),
           "extractionRequestSha256": extraction_sha, "groundingRequestSha256": grounding_sha,
           "groundingResponse": copy.deepcopy(grounding_response),
           "groundingOutcome": outcome, "groundingReason": ground_result.get("reason") if isinstance(ground_result, dict) else None,
           "expectedGrounding": expected_grounding, "groundingOutcomeCorrect": correct_ground,
           "plan": is_plan, "falsePlan": false_plan, "missedPlan": expected_grounding == "plan" and not is_plan,
           "invalidGrounding": outcome == "invalid", "nonPlan": not is_plan,
           "candidateTargetCorrectnessProven": False, "targetFidelityProven": False,
           "syntheticCandidates": True, "nativeEffectsObserved": False, "npcConsent": False,
           "extractionResponse": copy.deepcopy(extraction_response), **stage_scores}
    return row


def _append_result(path, row):
    _append_jsonl(path, row)


def _summary(rows, suite_hash):
    case_ids = list(dict.fromkeys(row["caseId"] for row in rows))
    by_case = {case_id: [row for row in rows if row["caseId"] == case_id] for case_id in case_ids}
    extraction_rows = [values[0] for values in by_case.values()]
    return {"suiteCasesSha256": suite_hash, "caseCount": len(case_ids), "armCount": len(rows),
            "totals": {"extractionCalls": sum(row["extractionCallMade"] for row in extraction_rows),
                       "extractionObserved": sum(row["conceptualObserved"] for row in extraction_rows),
                       "extractionStrictCorrect": sum(row["conceptualStrictCorrect"] for row in extraction_rows),
                       "groundingCalls": sum(row["groundingCallMade"] for row in rows),
                       "groundingCorrect": sum(row["groundingOutcomeCorrect"] for row in rows),
                       "plans": sum(row["plan"] for row in rows), "falsePlans": sum(row["falsePlan"] for row in rows),
                       "missedPlans": sum(row["missedPlan"] for row in rows),
                       "invalidGrounding": sum(row["invalidGrounding"] for row in rows),
                       "nonPlans": sum(row["nonPlan"] for row in rows)},
            "claims": {"sourceQuoteProvesSemantics": False, "candidateTargetCorrectnessProven": False,
                       "npcWillingness": False, "npcConsent": False, "nativeEffectsObserved": False}}


def run(argv=None):
    prepared = _prepare(argv)
    game_closed()
    run_dir = prepared["runDir"]
    run_dir.mkdir(parents=False, exist_ok=False)
    payload_dir = run_dir / "payloads"
    payload_dir.mkdir()
    inputs_dir = run_dir / "inputs"
    inputs_dir.mkdir()
    suite_copy = inputs_dir / "suite.json"
    _write_bytes(suite_copy, prepared["suiteRaw"])
    payload_manifest = []
    for entry in prepared["extractionPayloads"]:
        path = payload_dir / f"case-{entry['index'] + 1:04d}.extract.json"
        _write_bytes(path, entry["bytes"])
        entry["relativePayloadPath"] = str(path.relative_to(run_dir))
        payload_manifest.append({"caseId": entry["case"]["id"], "stage": "extraction",
                                 "path": entry["relativePayloadPath"], "sha256": entry["sha256"]})
    args = prepared["args"]
    manifest = {"frozenUtc": datetime.now(timezone.utc).isoformat(), "frozenFiles": prepared["frozen"],
                "suiteCasesSha256": prepared["suiteSha256"], "expectedModelId": args.expected_model,
                "conceptualProtocolVersion": conceptual_protocol.VERSION,
                "goalProtocolVersion": goal_protocol.VERSION,
                "suiteRawCopy": {"path": str(suite_copy.relative_to(run_dir)), "sha256": _sha(prepared["suiteRaw"])},
                "url": prepared["url"], "timeoutSeconds": TIMEOUT_SECONDS,
                "payloads": payload_manifest, "candidateRefsSynthetic": True, "nativeEffectsObserved": False,
                "npcConsent": False, "scope": "Offline conceptual extraction and synthetic candidate grounding only."}
    _write_json(run_dir / "manifest.json", manifest)
    _assert_written_payloads(run_dir, manifest)
    _assert_frozen(prepared["frozen"])
    game_closed()
    models = get_json(prepared["url"] + "/v1/models")
    model = _model_id(models, "Qwen")
    if model != args.expected_model:
        manifest["discoveredModelId"] = model
        _write_json(run_dir / "manifest.json", manifest)
        raise RuntimeError("Discovered model ID does not exactly match expected ID")
    manifest["discoveredModelId"] = model
    _write_json(run_dir / "manifest.json", manifest)
    results_path = run_dir / "results.jsonl"
    ledger_path = run_dir / "attempts.jsonl"
    rows = []

    for entry in prepared["extractionPayloads"]:
        _assert_written_payloads(run_dir, manifest)
        case = entry["case"]
        _assert_frozen(prepared["frozen"])
        game_closed()
        started = time.perf_counter()
        extraction_response = post_json(prepared["url"] + "/v1/chat/completions", entry["payload"], ledger_path,
                                        {"caseId": case["id"], "stage": "extraction", "model": "Qwen"},
                                        timeout=TIMEOUT_SECONDS)
        elapsed = (time.perf_counter() - started) * 1000
        if extraction_response.get("request_sha256") != entry["sha256"]:
            raise RuntimeError("Serialized extraction request differs from frozen payload hash")
        extraction_raw = extraction_response.get("response_raw")
        extraction_value, extraction_error, reported = (None, "request_failed", None)
        extraction_latency = _latency_ms(extraction_response, elapsed)
        if extraction_raw is not None:
            extraction_value, extraction_error, reported = _parse_chat_response(extraction_raw, model, "extraction")
        if extraction_error is None and extraction_value is not None:
            concept = conceptual_protocol.extraction(model, extraction_value, case)
            if not concept.get("valid"):
                extraction_error = "invalid_conceptual_extraction"
        else:
            concept = {"valid": False, "reason": extraction_error}
        if extraction_response.get("status_code") != 200 or extraction_response.get("transport_error") or extraction_response.get("response_decode_error"):
            concept, extraction_error = {"valid": False, "reason": "Extraction request failed"}, "request_failed"
        if extraction_latency > TIMEOUT_SECONDS * 1000:
            concept, extraction_error = {"valid": False, "reason": "Extraction exceeded 10-second timeout"}, "over_timeout"
        extraction_record = {"valid": concept.get("valid", False), "error": extraction_error,
                             "modelId": reported, "latencyMs": extraction_latency,
                             "responseSha256": extraction_response.get("response_sha256"),
                             "raw": extraction_raw}
        candidates = case["snapshot"].get("candidates", [])
        arm_results = []
        if concept.get("valid") and _can_ground(concept, candidates):
            orders = (False, True) if entry["index"] % 2 == 0 else (True, False)
            for reverse in orders:
                ordered_candidates = list(reversed(candidates)) if reverse else list(candidates)
                payload = conceptual_protocol.grounding_payload(concept, ordered_candidates, model)
                body = _json_bytes(payload)
                digest = _sha(body)
                name = "reversed" if reverse else "original"
                path = payload_dir / f"case-{entry['index'] + 1:04d}.ground-{name}.json"
                _write_bytes(path, body)
                relpath = str(path.relative_to(run_dir))
                manifest["payloads"].append({"caseId": case["id"], "arm": name, "stage": "grounding",
                                             "path": relpath, "sha256": digest,
                                             "extractionRequestSha256": entry["sha256"]})
                arm_results.append({"arm": name, "payload": payload, "sha256": digest,
                                    "path": relpath, "candidates": ordered_candidates,
                                    "call": True})
        else:
            for name in ("original", "reversed"):
                ordered_candidates = list(reversed(candidates)) if name == "reversed" else list(candidates)
                ground_result = _eligible_without_model(concept, ordered_candidates)
                arm_results.append({"arm": name, "candidates": ordered_candidates,
                                    "call": False, "groundResult": ground_result,
                                    "sha256": None, "payload": None})
        _write_json(run_dir / "manifest.json", manifest)
        _assert_written_payloads(run_dir, manifest)
        _assert_frozen(prepared["frozen"])
        for arm in arm_results:
            response = None
            binding_value = None
            grounding_error = None
            latency = None
            if arm["call"]:
                game_closed()
                _assert_written_payloads(run_dir, manifest)
                _assert_frozen(prepared["frozen"])
                start_ground = time.perf_counter()
                response = post_json(prepared["url"] + "/v1/chat/completions", arm["payload"], ledger_path,
                                     {"caseId": case["id"], "stage": "grounding", "arm": arm["arm"], "model": "Qwen",
                                      "extractionRequestSha256": entry["sha256"]}, timeout=TIMEOUT_SECONDS)
                elapsed_ground = (time.perf_counter() - start_ground) * 1000
                if response.get("request_sha256") != arm["sha256"]:
                    raise RuntimeError("Serialized grounding request differs from frozen payload hash")
                latency = _latency_ms(response, elapsed_ground)
                raw = response.get("response_raw")
                binding_value, grounding_error, _ = _parse_chat_response(raw, model, "grounding") if isinstance(raw, str) else (None, "missing_raw_response", None)
                if grounding_error is None and set(binding_value) != {"bindings"}:
                    binding_value, grounding_error = None, "invalid_grounding_fields"
                if response.get("status_code") != 200 or response.get("transport_error") or response.get("response_decode_error"):
                    binding_value, grounding_error = None, "grounding_request_failed"
                elif latency > TIMEOUT_SECONDS * 1000:
                    binding_value, grounding_error = None, "over_timeout"
                ground_result = conceptual_protocol.ground(concept, binding_value.get("bindings") if binding_value else None,
                                                           arm["candidates"]) if grounding_error is None else {"outcome": "invalid", "steps": [], "reason": grounding_error}
            else:
                ground_result = arm["groundResult"]
            row = _score_row(case, concept, extraction_error, ground_result, arm["arm"], entry["sha256"],
                             arm["sha256"], {"error": grounding_error, "latencyMs": latency,
                                             "responseSha256": response.get("response_sha256") if response else None,
                                             "raw": response.get("response_raw") if response else None},
                             extraction_record)
            row["groundingBinding"] = copy.deepcopy(binding_value)
            row["extractionCallMade"] = True
            row["groundingCallMade"] = arm["call"]
            row["extractionRequestPath"] = entry["relativePayloadPath"]
            row["groundingRequestPath"] = arm.get("path")
            row["extractionRequestSha256"] = entry["sha256"]
            row["groundingRequestSha256"] = arm["sha256"]
            row["reportedModelId"] = reported
            rows.append(row)
            _append_result(results_path, row)
            print(case["id"], arm["arm"], row["groundingOutcome"], flush=True)
    summary = _summary(rows, prepared["suiteSha256"])
    _write_json(run_dir / "summary.json", summary)
    return summary


if __name__ == "__main__":
    run()
