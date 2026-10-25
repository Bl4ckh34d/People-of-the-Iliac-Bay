"""Offline D3 action frames and Qwen parameter extraction pilot."""
from __future__ import annotations

import hashlib
import json
import math
import time
from http.client import HTTPException
from urllib.error import URLError

import conceptual_goal_protocol
import decision_frame_protocol as frame_protocol
import run_conceptual_goal_pilot as conceptual_pilot
import run_native_route_replay as route_replay

MAX_RESPONSE_CHARS = 128 * 1024
TIMEOUT_MS = 10_000


def _finite(value):
    try:
        return math.isfinite(value)
    except OverflowError:
        return False


def _sha_payload(payload):
    body = json.dumps(payload, ensure_ascii=False, separators=(",", ":"), allow_nan=False).encode("utf-8")
    return hashlib.sha256(body).hexdigest()


def _response_failure(response, elapsed_ms):
    if not isinstance(response, dict):
        return "invalid_transport_result", None
    if response.get("transport_error"):
        error = str(response.get("transport_error"))
        return ("over_timeout" if "timed out" in error.lower() or "timeout" in error.lower() else "transport_error"), None
    if response.get("timeout") is True:
        return "over_timeout", None
    if response.get("response_decode_error"):
        return "response_decode_error", None
    if response.get("malformed_json"):
        return "malformed_json", None
    if response.get("status_code") != 200:
        return "http_status", None
    latency = response.get("latency_ms", elapsed_ms)
    if isinstance(latency, bool) or not isinstance(latency, (int, float)) or not _finite(latency) or latency < 0:
        return "invalid_latency", None
    if latency > TIMEOUT_MS:
        return "over_timeout", float(latency)
    raw = response.get("response_raw")
    if not isinstance(raw, str) or len(raw) > MAX_RESPONSE_CHARS:
        return "response_too_large_or_missing", float(latency)
    return None, float(latency)


def _call(call, stage, payload):
    started = time.perf_counter()
    try:
        response = call(stage, payload)
    except TimeoutError:
        return None, "over_timeout", None
    except (URLError, OSError, HTTPException) as error:
        return None, "transport_error:" + type(error).__name__, None
    elapsed = (time.perf_counter() - started) * 1000
    error, latency = _response_failure(response, elapsed)
    return response, error, latency


def _parse_d3(response, expected_model):
    try:
        outer = route_replay._strict_json_loads(response["response_raw"])
    except (KeyError, TypeError, ValueError, json.JSONDecodeError):
        return None, "invalid_outer_json"
    if not isinstance(outer, dict) or set(outer) - {"model", "answers", "usage"} or \
            not {"model", "answers"} <= set(outer):
        return None, "invalid_outer_shape"
    if outer["model"] != expected_model:
        return None, "wrong_model"
    if "usage" in outer and not isinstance(outer["usage"], dict):
        return None, "invalid_usage"
    if not isinstance(outer["answers"], dict):
        return None, "invalid_answers"
    return outer["answers"], None


def _parse_qwen(response, expected_model, stage):
    value, error, reported_model = conceptual_pilot._parse_chat_response(
        response.get("response_raw"), expected_model, stage)
    if error:
        return None, error
    if reported_model != expected_model:
        return None, "wrong_model"
    return value, None


def _invalid_ground(reason):
    return {"outcome": "invalid", "steps": [], "reason": reason}


def _score_row(case, concept, extraction_error, ground_result, d3_calls, qwen_calls,
               frame_kinds, frame_outcome, has_constraints, stage_records):
    extraction_response = next((record for record in stage_records if record["stage"] == "arguments"), None)
    grounding_response = next((record for record in stage_records if record["stage"] == "grounding"), None)
    extraction_hash = extraction_response.get("requestSha256") if extraction_response else None
    grounding_hash = grounding_response.get("requestSha256") if grounding_response else None
    row = conceptual_pilot._score_row(
        case, concept, extraction_error, ground_result, "sequential", extraction_hash, grounding_hash,
        grounding_response, extraction_response)
    row.update({"frameKinds": list(frame_kinds), "frameOutcome": frame_outcome,
                "hasConstraints": has_constraints, "planSteps": ground_result.get("steps", []),
                "d3CallCount": d3_calls, "qwenCallCount": qwen_calls,
                "stageRecords": stage_records, "extractionError": extraction_error})
    return row


def evaluate_case(case, models, call, reverse=False):
    """Run one frozen case through D3 frames, Qwen arguments, then conceptual binding.

    `call(stage, payload)` must return one model_client-style transport result.
    Stages are `selection`, `arguments`, and `grounding`. This function does not
    retry, connect to services, or execute native effects.
    """
    if not isinstance(case, dict) or not isinstance(models, dict) or not callable(call):
        raise ValueError("evaluate_case needs case, model IDs, and a transport callback")
    d3_model, qwen_model = models.get("d3"), models.get("qwen")
    if not isinstance(d3_model, str) or not d3_model or not isinstance(qwen_model, str) or not qwen_model:
        raise ValueError("evaluate_case needs exact d3 and qwen model IDs")
    if not isinstance(case.get("playerText"), str) or not isinstance(case.get("snapshot"), dict):
        raise ValueError("evaluate_case needs playerText and snapshot")
    candidates = case["snapshot"].get("candidates", [])
    if not isinstance(candidates, list):
        return _score_row(case, None, "invalid_candidates", _invalid_ground("Invalid candidates"),
                          0, 0, [], "invalid", None, [])

    stage_records = []
    d3_calls = qwen_calls = 0
    selected_kinds = []
    has_constraints = None
    frame_outcome = None
    frame_reason = None
    extraction_error = None
    concept = None
    ground_result = None

    while True:
        try:
            payload = frame_protocol.selection_payload(case, selected_kinds,
                                                       has_constraints=has_constraints, reverse=reverse)
        except (TypeError, ValueError) as error:
            extraction_error = "invalid_frame_request:" + str(error)
            ground_result = _invalid_ground(extraction_error)
            frame_outcome = "invalid"
            break
        response, error, latency = _call(call, "selection", payload)
        d3_calls += 1
        record = {"stage": "selection", "requestSha256": _sha_payload(payload), "error": error,
                  "latencyMs": latency, "reportedModelId": None}
        stage_records.append(record)
        if error:
            extraction_error = error
            ground_result = _invalid_ground(error)
            frame_outcome = "invalid"
            break
        answers, error = _parse_d3(response, d3_model)
        record["reportedModelId"] = d3_model if error is None else None
        if error:
            extraction_error = error
            ground_result = _invalid_ground(error)
            frame_outcome = "invalid"
            break
        try:
            transition = frame_protocol.parse_selection_response(
                answers, selected_kinds, has_constraints=has_constraints)
        except (TypeError, ValueError, OverflowError) as parse_error:
            extraction_error = "invalid_frame_response:" + str(parse_error)
            ground_result = _invalid_ground(extraction_error)
            frame_outcome = "invalid"
            break
        selected_kinds = transition["selectedKinds"]
        has_constraints = transition["hasConstraints"]
        if transition["outcome"] == "continue":
            continue
        frame_outcome = transition["outcome"]
        frame_reason = transition.get("reason")
        break

    if ground_result is None and frame_outcome == "none":
        none_value = {"intent": "none", "steps": [], "conditions": [], "missing": []}
        concept = conceptual_goal_protocol.extraction(None, none_value, case)
        ground_result = conceptual_goal_protocol.ground(concept, [], candidates)
    elif ground_result is None and frame_outcome == "clarify":
        clarify_value = {"intent": "clarify", "steps": [], "conditions": [], "missing": []}
        concept = conceptual_goal_protocol.extraction(None, clarify_value, case)
        ground_result = {"outcome": "clarify", "steps": [], "reason": frame_reason or "D3 frame requests clarification"}
    elif ground_result is None and frame_outcome == "request":
        try:
            payload = frame_protocol.argument_payload(case, selected_kinds, qwen_model,
                                                      has_constraints=has_constraints)
        except (TypeError, ValueError) as error:
            extraction_error = "invalid_argument_request:" + str(error)
            ground_result = _invalid_ground(extraction_error)
        else:
            response, error, latency = _call(call, "arguments", payload)
            qwen_calls += 1
            record = {"stage": "arguments", "requestSha256": _sha_payload(payload), "error": error,
                      "latencyMs": latency, "reportedModelId": None}
            stage_records.append(record)
            value = None
            if error:
                extraction_error = error
            else:
                value, extraction_error = _parse_qwen(response, qwen_model, "arguments")
                record["reportedModelId"] = qwen_model if extraction_error is None else None
            if extraction_error:
                ground_result = _invalid_ground(extraction_error)
            else:
                extracted = frame_protocol.extract_arguments(case, selected_kinds, value,
                                                             has_constraints=has_constraints)
                concept = extracted.get("conceptual")
                if not extracted.get("valid"):
                    extraction_error = extracted.get("reason", "invalid_arguments")
                    ground_result = _invalid_ground(extraction_error)
                elif extracted.get("outcome") == "clarify":
                    ground_result = {"outcome": "clarify", "steps": [],
                                     "reason": extracted.get("reason") or "Qwen requests clarification"}
                else:
                    grounding_candidates = list(reversed(candidates)) if reverse else list(candidates)
                    try:
                        payload = conceptual_goal_protocol.grounding_payload(
                            concept, grounding_candidates, qwen_model)
                    except (TypeError, ValueError) as error:
                        extraction_error = "invalid_grounding_request:" + str(error)
                        ground_result = {"outcome": "clarify", "steps": [], "reason": extraction_error}
                    else:
                        response, error, latency = _call(call, "grounding", payload)
                        qwen_calls += 1
                        record = {"stage": "grounding", "requestSha256": _sha_payload(payload), "error": error,
                                  "latencyMs": latency, "reportedModelId": None}
                        stage_records.append(record)
                        binding = None
                        if error:
                            extraction_error = error
                        else:
                            binding, extraction_error = _parse_qwen(response, qwen_model, "grounding")
                            record["reportedModelId"] = qwen_model if extraction_error is None else None
                        if extraction_error:
                            ground_result = _invalid_ground(extraction_error)
                        elif set(binding) != {"bindings"}:
                            extraction_error = "invalid_grounding_fields"
                            ground_result = _invalid_ground(extraction_error)
                        else:
                            ground_result = conceptual_goal_protocol.ground(
                                concept, binding["bindings"], candidates)
    if ground_result is None:
        ground_result = _invalid_ground("No grounding outcome")
    return _score_row(case, concept, extraction_error, ground_result, d3_calls, qwen_calls,
                      selected_kinds, frame_outcome, has_constraints, stage_records)
