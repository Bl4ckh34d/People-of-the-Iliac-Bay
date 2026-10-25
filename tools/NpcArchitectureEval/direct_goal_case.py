"""Per-case direct Qwen adapter for the frozen conceptual-goal protocol."""
from __future__ import annotations

import copy
import time

import conceptual_goal_protocol as conceptual
import run_conceptual_goal_pilot as pilot


def _request_hash(payload):
    return pilot._sha(pilot._json_bytes(payload))


def _failed_response(response, elapsed_ms):
    return (not isinstance(response, dict) or response.get("status_code") != 200 or
            bool(response.get("transport_error")) or bool(response.get("response_decode_error")))


def _invoke(stage, payload, model, call):
    """Call once, enforce model-client result and response bounds, never retry."""
    started = time.perf_counter()
    try:
        response = call(stage, payload)
    except Exception as exc:  # Transport adapters report failures as result data; normalize thrown failures too.
        response = {"status_code": None, "transport_error": str(exc)}
    elapsed_ms = (time.perf_counter() - started) * 1000
    latency_ms = pilot._latency_ms(response, elapsed_ms) if isinstance(response, dict) else elapsed_ms
    request_sha = _request_hash(payload)
    raw = response.get("response_raw") if isinstance(response, dict) else None
    value, error, reported_model = pilot._parse_chat_response(raw, model, stage)
    if error is None and stage == "grounding" and set(value) != {"bindings"}:
        value, error = None, "invalid_grounding_fields"
    if _failed_response(response, elapsed_ms):
        value, error = None, "request_failed"
    if latency_ms > pilot.TIMEOUT_SECONDS * 1000:
        value, error = None, "over_timeout"
    if isinstance(response, dict) and response.get("request_sha256") not in (None, request_sha):
        value, error = None, "request_hash_mismatch"
    return value, error, reported_model, {
        "error": error,
        "latencyMs": latency_ms,
        "responseSha256": response.get("response_sha256") if isinstance(response, dict) else None,
        "raw": raw,
    }, request_sha


def evaluate_case(case, qwen_model: str, call: callable, reverse=False):
    """Evaluate one case with candidate-blind extraction then typed grounding.

    ``call(stage, payload)`` must return ``model_client.post_json``-style result
    data and use the same 10-second request timeout. The adapter independently
    rejects responses whose reported or measured latency exceeds that limit.
    """
    candidates = case.get("snapshot", {}).get("candidates", [])
    extraction_payload = conceptual.conceptual_payload(case, qwen_model)
    extraction_value, extraction_error, reported_model, extraction_record, extraction_sha = _invoke(
        "extraction", extraction_payload, qwen_model, call)
    if extraction_error is None and extraction_value is not None:
        concept = conceptual.extraction(qwen_model, extraction_value, case)
        if not concept.get("valid"):
            extraction_error = "invalid_conceptual_extraction"
    else:
        concept = {"valid": False, "reason": extraction_error}
    extraction_record["valid"] = bool(concept.get("valid"))
    extraction_record["error"] = extraction_error
    extraction_record["modelId"] = reported_model

    grounding_sha = None
    grounding_record = {"error": None, "latencyMs": None, "responseSha256": None, "raw": None}
    binding_value = None
    grounding_called = False
    if concept.get("valid") and pilot._can_ground(concept, candidates):
        ordered_candidates = list(reversed(candidates)) if reverse else list(candidates)
        grounding_payload = conceptual.grounding_payload(concept, ordered_candidates, qwen_model)
        binding_value, grounding_error, _ground_model, grounding_record, grounding_sha = _invoke(
            "grounding", grounding_payload, qwen_model, call)
        grounding_called = True
        ground_result = (conceptual.ground(concept, binding_value.get("bindings"), ordered_candidates)
                         if grounding_error is None and binding_value is not None else
                         {"outcome": "invalid", "steps": [], "reason": grounding_error})
    else:
        ordered_candidates = list(reversed(candidates)) if reverse else list(candidates)
        ground_result = pilot._eligible_without_model(concept, ordered_candidates)

    row = pilot._score_row(case, concept, extraction_error, ground_result,
                           "reversed" if reverse else "original", extraction_sha, grounding_sha,
                           grounding_record, extraction_record)
    row.update({
        "selector": "qwen-direct",
        "planSteps": copy.deepcopy(ground_result.get("steps", [])) if ground_result.get("outcome") == "plan" else [],
        "groundingBinding": copy.deepcopy(binding_value),
        "extractionCallMade": True,
        "groundingCallMade": grounding_called,
        "reportedModelId": reported_model,
        "groundingCandidateOrderReversed": bool(reverse),
        "extractionRequestSha256": extraction_sha,
        "groundingRequestSha256": grounding_sha,
    })
    return row
