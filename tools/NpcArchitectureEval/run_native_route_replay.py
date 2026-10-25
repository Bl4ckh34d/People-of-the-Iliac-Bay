"""Replay frozen native route payloads against serial loopback model endpoints.

This runner evaluates route classification only. It never materializes or
executes actions, and it does not measure NPC willingness or world effects.
"""
from __future__ import annotations

import argparse
import copy
import hashlib
import json
import math
import sys
import time
from decimal import Decimal, InvalidOperation
from datetime import datetime, timezone
from pathlib import Path
from typing import Any
import unicodedata
from urllib.error import HTTPError
from urllib.parse import urlsplit
from urllib.request import HTTPRedirectHandler, Request, build_opener

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[1]
sys.path.insert(0, str(ROOT))

from model_client import _validate_loopback_http, post_json  # noqa: E402
from run_evaluation import game_closed  # noqa: E402


DEFAULT_D3_URL = "http://127.0.0.1:8093"
NATIVE_BUDGET_SECONDS = 10.0
MAX_DIAGNOSTIC_TIMEOUT_SECONDS = 120.0
MAX_TEMPLATE_BYTES = 128 * 1024
MAX_SUITE_BYTES = 2 * 1024 * 1024
MAX_METADATA_BYTES = 256 * 1024
MAX_VOCABULARY_BYTES = 16 * 1024
ROUTES = ("SOCIAL", "ACTION_REQUEST", "OFFER_RESPONSE", "COMMITMENT_UPDATE")
ROUTE_SET = set(ROUTES)
BASE_ROUTES = ("SOCIAL", "ACTION_REQUEST")
QUESTION_ID = "decision"
MAX_HISTORY_CHARS = 6000
MAX_TURN_CHARS = 8192
MAX_CONTEXT_CHARS = 16384
CORE_MAXIMUM_TOLERANCE = 1e-6


class _NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def cases_sha256(cases: Any) -> str:
    encoded = json.dumps(cases, ensure_ascii=False, separators=(",", ":"), allow_nan=False).encode("utf-8")
    return hashlib.sha256(encoded).hexdigest()


def _json_bytes(value: Any) -> bytes:
    return json.dumps(value, ensure_ascii=False, separators=(",", ":"), allow_nan=False).encode("utf-8")


def _sha256_bytes(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest()


def _parse_args(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--template", required=True, help="captured native /v1/systemone request JSON")
    parser.add_argument("--suite", required=True, help="frozen suite JSON with cases and casesSha256")
    parser.add_argument("--route-vocabulary", help="captured v2 route-vocabulary.json; required by v2 templates")
    parser.add_argument("--run-dir", required=True, help="new, non-existing output directory")
    parser.add_argument("--d3-url", default=DEFAULT_D3_URL, help=f"loopback D3 base URL (default: {DEFAULT_D3_URL})")
    parser.add_argument("--expected-model", required=True, help="exact expected D3 model ID")
    parser.add_argument("--qwen-url", help="optional loopback Qwen OpenAI-compatible base URL")
    parser.add_argument("--expected-qwen-model", help="exact Qwen model ID; required with --qwen-url")
    parser.add_argument("--diagnostic-timeout", type=float,
                        help="explicit diagnostic timeout above native 10-second budget; late replies remain failures")
    return parser.parse_args(argv)


def _resolve_path(value: str) -> Path:
    path = Path(value)
    return (path if path.is_absolute() else REPO / path).resolve()


def _normalize_base_url(value: str, label: str) -> str:
    normalized = value.rstrip("/")
    _validate_loopback_http(normalized)
    parts = urlsplit(normalized)
    if parts.query or parts.fragment:
        raise ValueError(f"{label} URL cannot contain a query or fragment")
    return normalized


def _validate_case(case: Any, seen_ids: set[str]) -> None:
    if not isinstance(case, dict):
        raise ValueError("Every suite case must be an object")
    case_id = case.get("id")
    category = case.get("category")
    player_text = case.get("playerText")
    if not isinstance(case_id, str) or not case_id.strip() or case_id in seen_ids:
        raise ValueError("Every case needs a unique, non-empty id")
    if not isinstance(category, str) or not category.strip():
        raise ValueError(f"Case {case_id} needs a non-empty category")
    if not isinstance(player_text, str) or not player_text.strip():
        raise ValueError(f"Case {case_id} needs a non-empty playerText")
    _validate_multiline(player_text, MAX_TURN_CHARS, f"Case {case_id} playerText")
    history = case.get("attributedHistory")
    if not isinstance(history, list):
        raise ValueError(f"Case {case_id} needs attributedHistory array")
    for turn in history:
        if (not isinstance(turn, dict) or turn.get("speaker") not in ("player", "npc") or
                not isinstance(turn.get("text"), str)):
            raise ValueError(f"Case {case_id} has malformed attributed history")
        _validate_multiline(turn["text"], MAX_TURN_CHARS, f"Case {case_id} history text")
    commitments = case.get("commitments")
    if not isinstance(commitments, list) or any(not isinstance(item, dict) for item in commitments):
        raise ValueError(f"Case {case_id} commitments must be an array of objects")
    offer = case.get("pendingOffer")
    if offer is not None and not isinstance(offer, dict):
        raise ValueError(f"Case {case_id} pendingOffer must be an object or null")
    expected = case.get("expectedRoutes")
    if (not isinstance(expected, list) or not expected or
            any(not isinstance(route, str) or route not in ROUTE_SET for route in expected) or
            len(set(expected)) != len(expected)):
        raise ValueError(f"Case {case_id} expectedRoutes must be a non-empty list of unique route IDs")
    if "falseActionControl" in case and not isinstance(case["falseActionControl"], bool):
        raise ValueError(f"Case {case_id} falseActionControl must be boolean")
    if "notes" in case and not isinstance(case["notes"], str):
        raise ValueError(f"Case {case_id} notes must be a string")
    seen_ids.add(case_id)


def load_suite(path: Path, supplied_hash: str | None = None):
    if not path.is_file():
        raise FileNotFoundError(f"Suite file is missing: {path}")
    if path.stat().st_size > MAX_SUITE_BYTES:
        raise ValueError(f"Suite exceeds {MAX_SUITE_BYTES} bytes")
    try:
        suite = json.loads(path.read_text(encoding="utf-8-sig"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise ValueError(f"Suite is not valid UTF-8 JSON: {path}") from exc
    if not isinstance(suite, dict) or not isinstance(suite.get("cases"), list) or not suite["cases"]:
        raise ValueError("Suite must contain a non-empty cases array")
    if suite.get("schemaVersion") != 1:
        raise ValueError("Suite schemaVersion must be 1")
    cases = suite["cases"]
    digest = cases_sha256(cases)
    declared = suite.get("casesSha256")
    if not isinstance(declared, str) or declared.lower() != digest:
        raise ValueError("Suite casesSha256 does not match canonical case data")
    if supplied_hash is not None and supplied_hash.lower() != digest:
        raise ValueError("Supplied casesSha256 does not match suite")
    if "caseCount" in suite and suite["caseCount"] != len(cases):
        raise ValueError("Suite caseCount does not match cases array")
    seen_ids: set[str] = set()
    for item in cases:
        _validate_case(item, seen_ids)
    return suite, digest


def load_template(path: Path):
    if not path.is_file():
        raise FileNotFoundError(f"Native request template is missing: {path}")
    if path.stat().st_size > MAX_TEMPLATE_BYTES:
        raise ValueError(f"Native request template exceeds {MAX_TEMPLATE_BYTES} bytes")
    try:
        value = json.loads(path.read_text(encoding="utf-8-sig"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise ValueError("Native request template is not valid UTF-8 JSON") from exc
    if not isinstance(value, dict) or not isinstance(value.get("state"), str):
        raise ValueError("Native request template needs a JSON-string state")
    try:
        state = json.loads(value["state"])
    except json.JSONDecodeError as exc:
        raise ValueError("Native request template state is not JSON") from exc
    if not isinstance(state, dict) or not isinstance(state.get("originalTurn"), str) or not isinstance(state.get("context"), str):
        raise ValueError("Native state needs originalTurn and JSON-string context")
    try:
        context = json.loads(state["context"])
    except json.JSONDecodeError as exc:
        raise ValueError("Native state context is not JSON") from exc
    if not isinstance(context, dict) or not isinstance(context.get("npcSnapshot"), dict):
        raise ValueError("Native context must retain an npcSnapshot object")
    purpose = state.get("purpose")
    semantic_protocol = context.get("semanticProtocol")
    if (purpose, semantic_protocol) not in {
            ("npc-speech-act-route-v1", "native-speech-act-route-context-v1"),
            ("npc-speech-act-route-v2", "native-speech-act-route-context-v2")}:
        raise ValueError("Native template has unsupported or mismatched route protocol metadata")
    question = value.get("questions", {}).get(QUESTION_ID) if isinstance(value.get("questions"), dict) else None
    criteria = question.get("criteria") if isinstance(question, dict) else None
    if (not isinstance(question, dict) or question.get("type") != "choice" or
            not isinstance(question.get("instructions"), str) or not question["instructions"].strip() or
            not isinstance(criteria, dict) or
            (not set(criteria).issubset(ROUTE_SET)) or
            (purpose == "npc-speech-act-route-v1" and set(criteria) != ROUTE_SET) or
            (purpose == "npc-speech-act-route-v2" and not set(BASE_ROUTES).issubset(criteria)) or
            any(not isinstance(text, str) or not text for text in criteria.values())):
        raise ValueError("Native decision question must have instructions and valid route choices for its protocol")
    return value


def _template_protocol(template):
    state = json.loads(template["state"])
    return state["purpose"]


def load_route_vocabulary(path: Path, expected_purpose="npc-speech-act-route-v2"):
    if not path.is_file():
        raise FileNotFoundError(f"Route vocabulary is missing: {path}")
    if path.stat().st_size > MAX_VOCABULARY_BYTES:
        raise ValueError(f"Route vocabulary exceeds {MAX_VOCABULARY_BYTES} bytes")
    raw = path.read_bytes()
    try:
        value = _strict_json_loads(raw.decode("utf-8-sig"))
    except (UnicodeDecodeError, json.JSONDecodeError, ValueError) as exc:
        raise ValueError("Route vocabulary is not strict UTF-8 JSON") from exc
    if not isinstance(value, dict) or set(value) != {"purpose", "contextProtocol", "routes"}:
        raise ValueError("Route vocabulary must contain only purpose, contextProtocol and routes")
    if value.get("purpose") != expected_purpose or value.get("contextProtocol") != "native-speech-act-route-context-v2":
        raise ValueError("Route vocabulary protocol metadata does not match v2")
    routes = value.get("routes")
    if not isinstance(routes, dict) or list(routes) != list(ROUTES):
        raise ValueError("Route vocabulary must have the four canonical routes in stable order")
    for route, description in routes.items():
        if not isinstance(description, str) or not description.strip() or len(description) > 512 or any(
                unicodedata.category(character) == "Cc" for character in description):
            raise ValueError(f"Route vocabulary description for {route} is invalid")
    return value, _sha256_bytes(raw)


def build_d3_payload(template: dict[str, Any], case: dict[str, Any], reverse: bool,
                     vocabulary: dict[str, Any] | None = None) -> dict[str, Any]:
    payload = copy.deepcopy(template)
    state = json.loads(payload["state"])
    context = json.loads(state["context"])
    state["originalTurn"] = case["playerText"]
    retained_history, omitted_history = _bound_history(case["attributedHistory"])
    context["attributedHistory"] = retained_history
    context["historyOmitted"] = omitted_history
    context["activeCommitments"] = [copy.deepcopy(item) for item in case["commitments"] if _active_commitment(item)]
    context["pendingOffer"] = copy.deepcopy(case["pendingOffer"])
    context_text = json.dumps(context, ensure_ascii=False, separators=(",", ":"), allow_nan=False)
    if _utf16_length(context_text) > MAX_CONTEXT_CHARS:
        raise ValueError(f"Case {case['id']} serialized native context exceeds {MAX_CONTEXT_CHARS} characters")
    state["context"] = context_text
    payload["state"] = json.dumps(state, ensure_ascii=False, separators=(",", ":"), allow_nan=False)
    question = payload["questions"][QUESTION_ID]
    criteria = question["criteria"]
    if _template_protocol(template) == "npc-speech-act-route-v2":
        if not isinstance(vocabulary, dict) or not isinstance(vocabulary.get("routes"), dict):
            raise ValueError("A validated v2 route vocabulary is required to build this native template")
        offered = list(BASE_ROUTES)
        if case["pendingOffer"] is not None:
            offered.append("OFFER_RESPONSE")
        if any(_active_commitment(item) for item in case["commitments"]):
            offered.append("COMMITMENT_UPDATE")
        criteria = {route: vocabulary["routes"][route] for route in offered}
        question["criteria"] = criteria
    elif vocabulary is not None:
        raise ValueError("Route vocabulary is only valid for v2 native templates")
    entries = list(criteria.items())
    if reverse:
        entries.reverse()
    question["criteria"] = dict(entries)
    return payload


def build_qwen_payload(d3_payload: dict[str, Any], model: str) -> dict[str, Any]:
    question = d3_payload["questions"][QUESTION_ID]
    route_ids = list(question["criteria"])
    state_json = d3_payload["state"]
    user_value = {"state": state_json, "choices": question["criteria"]}
    schema = {"type": "object", "additionalProperties": False,
              "properties": {"route": {"type": "string", "enum": route_ids}}, "required": ["route"]}
    return {"model": model,
            "messages": [{"role": "system", "content": question["instructions"] +
                          " Return one JSON object with a route field whose value is an exact supplied route ID."},
                         {"role": "user", "content": json.dumps(user_value, ensure_ascii=False, separators=(",", ":"))}],
            "temperature": 0, "seed": 42, "max_tokens": 128, "stream": False,
            "response_format": {"type": "json_schema", "json_schema": {
                "name": "native_route_replay", "strict": True, "schema": schema}}}


def _is_number(value: Any) -> bool:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        return False
    try:
        return math.isfinite(value)
    except (OverflowError, TypeError, ValueError):
        return False


def parse_d3_response(value: Any, expected_model: str, expected_routes: list[str], offered_routes=None):
    allowed_routes = set(offered_routes if offered_routes is not None else ROUTES)
    result = {"valid": False, "route": None, "probabilities": None, "confidence": None, "model": None, "error": None}
    if not isinstance(value, dict):
        result["error"] = "invalid_schema"
        return result
    model = value.get("model")
    result["model"] = model if isinstance(model, str) else None
    if model != expected_model:
        result["error"] = "model_mismatch"
        return result
    if set(value) - {"model", "answers", "usage"} or not isinstance(value.get("answers"), dict):
        result["error"] = "invalid_schema"
        return result
    answers = value["answers"]
    if set(answers) != {QUESTION_ID} or not isinstance(answers[QUESTION_ID], dict):
        result["error"] = "invalid_schema"
        return result
    answer = answers[QUESTION_ID]
    if (set(answer) - {"type", "choice", "probabilities", "confidence"} or answer.get("type") != "choice" or
            not isinstance(answer.get("choice"), str) or answer["choice"] not in allowed_routes):
        result["error"] = "invalid_schema"
        return result
    probabilities = answer.get("probabilities")
    if not isinstance(probabilities, dict) or set(probabilities) != allowed_routes:
        result["error"] = "invalid_probabilities"
        return result
    if any(not _is_number(item) or item < 0 or item > 1 for item in probabilities.values()):
        result["error"] = "invalid_probabilities"
        return result
    if abs(sum(probabilities.values()) - 1.0) > 1e-3:
        result["error"] = "invalid_probabilities"
        return result
    if probabilities[answer["choice"]] < max(probabilities.values()) - CORE_MAXIMUM_TOLERANCE:
        result["error"] = "invalid_probabilities"
        return result
    confidence = answer.get("confidence")
    if confidence is not None and (not _is_number(confidence) or confidence < 0 or confidence > 1):
        result["error"] = "invalid_schema"
        return result
    result.update(valid=True, route=answer["choice"], probabilities=probabilities, confidence=confidence,
                  goldAgreement=answer["choice"] in expected_routes, error=None)
    return result


def _strict_object(pairs):
    value = {}
    for key, item in pairs:
        if key in value:
            raise ValueError("duplicate JSON property")
        value[key] = item
    return value


def _strict_float(raw: str) -> float:
    try:
        exact = Decimal(raw)
        value = float(exact)
    except (InvalidOperation, OverflowError, ValueError) as exc:
        raise ValueError("invalid JSON number") from exc
    if not math.isfinite(value):
        raise ValueError("non-finite JSON number")
    coefficient = raw.lower().split("e", 1)[0].replace("-", "").replace(".", "")
    if any(character != "0" for character in coefficient) and value == 0.0:
        raise ValueError("nonzero JSON number underflowed to zero")
    return value


def _reject_json_constant(_value):
    raise ValueError("nonstandard JSON number")


def _strict_json_loads(raw: str):
    return json.loads(raw, object_pairs_hook=_strict_object, parse_float=_strict_float,
                      parse_constant=_reject_json_constant)


def _utf16_length(value: str) -> int:
    try:
        return len(value.encode("utf-16-le", errors="strict")) // 2
    except UnicodeEncodeError as exc:
        raise ValueError("Text contains invalid Unicode") from exc


def _validate_multiline(value: str, limit: int, label: str):
    length = _utf16_length(value)
    if length > limit:
        raise ValueError(f"{label} exceeds {limit} characters")
    if any(unicodedata.category(character) == "Cc" and character not in "\r\n\t" for character in value):
        raise ValueError(f"{label} contains a disallowed control character")


def _bound_history(history):
    retained = []
    chars = 0
    omitted = 0
    for turn in history:
        retained.append(copy.deepcopy(turn))
        chars += _utf16_length(turn["text"])
        while chars > MAX_HISTORY_CHARS and retained:
            removed = retained.pop(0)
            chars -= _utf16_length(removed["text"])
            omitted += 1
    return retained, omitted


def _active_commitment(item):
    kind = item.get("kind")
    status = item.get("status")
    if not isinstance(kind, str) or not isinstance(status, str):
        return False
    kind, status = kind.lower(), status.lower()
    return ((kind == "task" and status in {"ready", "running"}) or
            (kind == "action-task" and status in {"accepted", "approaching", "executing"}) or
            (kind == "appointment" and status in {"accepted", "waiting", "attended"}) or
            (kind == "shared-activity" and status in {"planned", "active", "paused"}))


def parse_qwen_response(value: Any, expected_model: str, expected_routes: list[str], offered_routes=None):
    allowed_routes = set(offered_routes if offered_routes is not None else ROUTES)
    result = {"valid": False, "route": None, "model": None, "error": None}
    if not isinstance(value, dict):
        result["error"] = "invalid_schema"
        return result
    model = value.get("model")
    result["model"] = model if isinstance(model, str) else None
    if model != expected_model:
        result["error"] = "model_mismatch"
        return result
    choices = value.get("choices")
    if (not isinstance(choices, list) or len(choices) != 1 or not isinstance(choices[0], dict) or
            choices[0].get("finish_reason") != "stop"):
        result["error"] = "invalid_schema"
        return result
    message = choices[0].get("message")
    content = message.get("content") if isinstance(message, dict) else None
    if not isinstance(content, str):
        result["error"] = "invalid_schema"
        return result
    try:
        parsed = _strict_json_loads(content)
    except (json.JSONDecodeError, ValueError):
        result["error"] = "invalid_completion"
        return result
    if not isinstance(parsed, dict) or set(parsed) != {"route"} or not isinstance(parsed["route"], str) or parsed["route"] not in allowed_routes:
        result["error"] = "invalid_completion"
        return result
    result.update(valid=True, route=parsed["route"], goldAgreement=parsed["route"] in expected_routes, error=None)
    return result


def _is_control(case: dict[str, Any]) -> bool:
    if isinstance(case.get("falseActionControl"), bool):
        return case["falseActionControl"]
    return case.get("expectedRoutes") == ["SOCIAL"]


def score_route(case: dict[str, Any], route: str | None, error: str | None):
    valid = route in ROUTE_SET and error is None
    expected = case["expectedRoutes"]
    control = _is_control(case)
    false_action = bool(valid and control and route != "SOCIAL")
    abstention = bool(valid and route == "SOCIAL" and "SOCIAL" not in expected)
    if error:
        result_class = "no_result" if error in {"transport_error", "http_status", "timeout"} else "invalid"
    elif false_action:
        result_class = "false_action"
    elif abstention:
        result_class = "abstention"
    elif route in expected:
        result_class = "correct"
    else:
        result_class = "incorrect"
    return {"expectedRoutes": list(expected), "goldAgreement": bool(valid and route in expected),
            "falseActionControl": control, "falseAction": false_action, "abstention": abstention,
            "invalid": result_class == "invalid", "noResult": result_class == "no_result",
            "resultClass": result_class}


def _get_json(url: str):
    _validate_loopback_http(url)
    request = Request(url, headers={"Accept": "application/json"}, method="GET")
    try:
        with build_opener(_NoRedirect).open(request, timeout=5) as response:
            data = response.read(MAX_METADATA_BYTES + 1)
    except HTTPError as exc:
        body = exc.read(MAX_METADATA_BYTES + 1)
        raise RuntimeError(f"Model metadata failed: HTTP {exc.code} at {url}: {body[:200]!r}") from exc
    if len(data) > MAX_METADATA_BYTES:
        raise RuntimeError(f"Model metadata exceeds {MAX_METADATA_BYTES} bytes at {url}")
    try:
        return json.loads(data.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise RuntimeError(f"Model metadata is not UTF-8 JSON at {url}") from exc


def _discover_model(value: Any, label: str) -> str:
    rows = value.get("data") if isinstance(value, dict) else None
    if not isinstance(rows, list) or not rows or not isinstance(rows[0], dict):
        raise RuntimeError(f"{label} /v1/models response has no data model")
    model = rows[0].get("id")
    if not isinstance(model, str) or not model:
        raise RuntimeError(f"{label} /v1/models response has no exact model ID")
    return model


def _write_json(path: Path, value: Any):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2, allow_nan=False) + "\n", encoding="utf-8")


def _append_jsonl(path: Path, row: dict[str, Any]):
    with path.open("a", encoding="utf-8", newline="\n") as stream:
        stream.write(json.dumps(row, ensure_ascii=False, allow_nan=False, separators=(",", ":")) + "\n")
        stream.flush()


def _prepare(argv=None):
    args = _parse_args(argv)
    if bool(args.qwen_url) != bool(args.expected_qwen_model):
        raise ValueError("--qwen-url and --expected-qwen-model must be supplied together")
    if not args.expected_model or len(args.expected_model) > 512:
        raise ValueError("--expected-model must be a non-empty exact model ID")
    if args.expected_qwen_model is not None and (not args.expected_qwen_model or len(args.expected_qwen_model) > 512):
        raise ValueError("--expected-qwen-model must be a non-empty exact model ID")
    timeout = NATIVE_BUDGET_SECONDS
    if args.diagnostic_timeout is not None:
        if not math.isfinite(args.diagnostic_timeout) or args.diagnostic_timeout <= NATIVE_BUDGET_SECONDS or args.diagnostic_timeout > MAX_DIAGNOSTIC_TIMEOUT_SECONDS:
            raise ValueError(f"--diagnostic-timeout must be > {NATIVE_BUDGET_SECONDS:g} and <= {MAX_DIAGNOSTIC_TIMEOUT_SECONDS:g} seconds")
        timeout = args.diagnostic_timeout
    d3_url = _normalize_base_url(args.d3_url, "D3")
    qwen_url = _normalize_base_url(args.qwen_url, "Qwen") if args.qwen_url else None
    template_path, suite_path = _resolve_path(args.template), _resolve_path(args.suite)
    if template_path.stat().st_size > MAX_TEMPLATE_BYTES or suite_path.stat().st_size > MAX_SUITE_BYTES:
        raise ValueError("Template or suite exceeds its size limit")
    template_bytes = template_path.read_bytes()
    suite_bytes = suite_path.read_bytes()
    template_value = load_template(template_path)
    suite, suite_digest = load_suite(suite_path)
    protocol = _template_protocol(template_value)
    vocabulary = None
    vocabulary_digest = None
    vocabulary_path = None
    if protocol == "npc-speech-act-route-v2":
        if not args.route_vocabulary:
            raise ValueError("--route-vocabulary is required for npc-speech-act-route-v2 templates")
        vocabulary_path = _resolve_path(args.route_vocabulary)
        vocabulary, vocabulary_digest = load_route_vocabulary(vocabulary_path)
        native_criteria = template_value["questions"][QUESTION_ID]["criteria"]
        if any(vocabulary["routes"].get(route) != description for route, description in native_criteria.items()):
            raise ValueError("Native template route descriptions do not match the frozen v2 route vocabulary")
    elif args.route_vocabulary:
        raise ValueError("--route-vocabulary is only accepted for v2 native templates")
    run_dir_input = Path(args.run_dir)
    if not run_dir_input.is_absolute():
        run_dir_input = REPO / run_dir_input
    if run_dir_input.exists() or run_dir_input.is_symlink():
        raise ValueError(f"Run directory must be fresh and non-existing: {run_dir_input}")
    run_dir = run_dir_input.resolve()
    if run_dir.exists() or not run_dir.parent.is_dir():
        raise ValueError(f"Run directory parent must exist and target must be fresh: {run_dir}")
    arms = []
    for index, case in enumerate(suite["cases"]):
        orders = (False, True) if index % 2 == 0 else (True, False)
        for reverse in orders:
            arm = "reversed" if reverse else "original"
            d3_payload = build_d3_payload(template_value, case, reverse, vocabulary)
            offered_routes = list(d3_payload["questions"][QUESTION_ID]["criteria"])
            if not set(case["expectedRoutes"]).issubset(offered_routes):
                raise ValueError(f"Case {case['id']} expects a route unavailable in its native menu {offered_routes}")
            d3_body = _json_bytes(d3_payload)
            entry = {"case": case, "caseIndex": index, "arm": arm, "reverse": reverse,
                     "offeredRoutes": offered_routes,
                     "d3Payload": d3_payload, "d3Bytes": d3_body, "d3Sha256": _sha256_bytes(d3_body)}
            if qwen_url:
                qwen_payload = build_qwen_payload(d3_payload, args.expected_qwen_model)
                qwen_body = _json_bytes(qwen_payload)
                entry.update(qwenPayload=qwen_payload, qwenBytes=qwen_body, qwenSha256=_sha256_bytes(qwen_body))
            arms.append(entry)
    frozen_paths = [Path(__file__).resolve(), ROOT / "model_client.py", ROOT / "run_evaluation.py", template_path, suite_path]
    if vocabulary_path is not None:
        frozen_paths.append(vocabulary_path)
    frozen = {str(path.resolve()): _sha256_bytes(path.read_bytes()) for path in frozen_paths}
    # Ensure the bytes used for validation still match bytes captured for the manifest.
    if _sha256_bytes(template_bytes) != frozen[str(template_path)] or _sha256_bytes(suite_bytes) != frozen[str(suite_path)]:
        raise RuntimeError("Template or suite changed during preflight")
    return args, timeout, run_dir, d3_url, qwen_url, suite, suite_digest, arms, frozen, protocol, vocabulary_digest


def _assert_frozen(frozen):
    for path_text, digest in frozen.items():
        path = Path(path_text)
        if not path.is_file() or _sha256_bytes(path.read_bytes()) != digest:
            raise RuntimeError(f"Frozen replay input changed during run: {path}")


def _post_arm(url: str, payload: dict[str, Any], ledger: Path, metadata: dict[str, Any], timeout: float):
    started = time.perf_counter()
    result = post_json(url, payload, ledger, metadata, timeout=timeout)
    elapsed_ms = round((time.perf_counter() - started) * 1000, 3)
    return result, elapsed_ms


def _model_route_result(model_name: str, expected_model: str, case: dict[str, Any], response: dict[str, Any], elapsed_ms: float,
                        offered_routes=None):
    model_latency_ms = response.get("latency_ms")
    if not _is_number(model_latency_ms) or model_latency_ms < 0:
        model_latency_ms = elapsed_ms
    if response.get("transport_error"):
        error = "timeout" if "timed out" in str(response["transport_error"]).lower() else "transport_error"
        parsed = {"valid": False, "route": None, "error": error}
    elif response.get("status_code") != 200:
        parsed = {"valid": False, "route": None, "error": "http_status"}
    elif response.get("response_decode_error") or response.get("malformed_json") or not isinstance(response.get("response_raw"), str):
        parsed = {"valid": False, "route": None, "error": "malformed_json"}
    else:
        try:
            strict_response = _strict_json_loads(response["response_raw"])
        except (json.JSONDecodeError, ValueError, TypeError):
            parsed = {"valid": False, "route": None, "error": "malformed_json"}
        else:
            if model_name == "D3":
                parsed = parse_d3_response(strict_response, expected_model, case["expectedRoutes"], offered_routes)
            else:
                parsed = parse_qwen_response(strict_response, expected_model, case["expectedRoutes"], offered_routes)
    score = score_route(case, parsed.get("route"), parsed.get("error"))
    within = model_latency_ms <= NATIVE_BUDGET_SECONDS * 1000
    if parsed.get("valid") and not within:
        score["resultClass"] = "over_native_budget"
    return {"model": model_name, "statusCode": response.get("status_code"), "latencyMs": model_latency_ms,
            "clientWallLatencyMs": elapsed_ms,
            "nativeBudgetSeconds": NATIVE_BUDGET_SECONDS, "withinNativeBudget": within,
            "route": parsed.get("route"), "probabilities": parsed.get("probabilities"),
            "confidence": parsed.get("confidence"), "reportedModel": parsed.get("model"),
            "error": parsed.get("error"), **score,
            "response": {key: response.get(key) for key in
                         ("response_raw", "response_base64", "response_json", "response_sha256", "transport_error", "response_decode_error", "malformed_json")}}


def run(argv=None):
    args, timeout, run_dir, d3_url, qwen_url, suite, suite_digest, arms, frozen, protocol, vocabulary_digest = _prepare(argv)
    game_closed()
    run_dir.mkdir()
    payload_entries = []
    payload_dir = run_dir / "payloads"
    payload_dir.mkdir()
    for index, entry in enumerate(arms):
        stem = f"case-{entry['caseIndex'] + 1:04d}-{entry['arm']}"
        d3_path = payload_dir / (stem + ".d3.json")
        d3_path.write_bytes(entry["d3Bytes"])
        entry["d3Path"] = d3_path
        payload_entries.append({"caseId": entry["case"]["id"], "arm": entry["arm"], "model": "D3",
                                "path": str(d3_path.relative_to(run_dir)), "sha256": entry["d3Sha256"]})
        if qwen_url:
            qwen_path = payload_dir / (stem + ".qwen.json")
            qwen_path.write_bytes(entry["qwenBytes"])
            entry["qwenPath"] = qwen_path
            payload_entries.append({"caseId": entry["case"]["id"], "arm": entry["arm"], "model": "Qwen",
                                    "path": str(qwen_path.relative_to(run_dir)), "sha256": entry["qwenSha256"]})
    manifest = {"frozenUtc": datetime.now(timezone.utc).isoformat(), "files": frozen,
                "suiteCasesSha256": suite_digest, "templateSha256": frozen[str(_resolve_path(args.template))],
                "routeProtocol": protocol, "routeVocabularySha256": vocabulary_digest,
                "caseIds": [case["id"] for case in suite["cases"]],
                "expectedRoutes": {case["id"]: list(case["expectedRoutes"]) for case in suite["cases"]},
                "expectedD3ModelId": args.expected_model,
                "expectedQwenModelId": args.expected_qwen_model,
                "d3BaseUrl": d3_url, "qwenBaseUrl": qwen_url,
                "nativeBudgetSeconds": NATIVE_BUDGET_SECONDS, "requestTimeoutSeconds": timeout,
                "diagnosticTimeout": timeout > NATIVE_BUDGET_SECONDS,
                "payloads": payload_entries,
                "scope": "Native route choice replay only. No proposal materialization, willingness decision, or world effects."}
    _write_json(run_dir / "manifest.json", manifest)
    _assert_frozen(frozen)

    d3_models = _get_json(d3_url + "/v1/models")
    discovered_d3 = _discover_model(d3_models, "D3")
    if discovered_d3 != args.expected_model:
        manifest["discoveredD3ModelId"] = discovered_d3
        _write_json(run_dir / "manifest.json", manifest)
        raise RuntimeError(f"D3 model ID mismatch: expected {args.expected_model!r}, discovered {discovered_d3!r}")
    manifest["discoveredD3ModelId"] = discovered_d3
    if qwen_url:
        qwen_models = _get_json(qwen_url + "/v1/models")
        discovered_qwen = _discover_model(qwen_models, "Qwen")
        manifest["discoveredQwenModelId"] = discovered_qwen
        if discovered_qwen != args.expected_qwen_model:
            _write_json(run_dir / "manifest.json", manifest)
            raise RuntimeError(f"Qwen model ID mismatch: expected {args.expected_qwen_model!r}, discovered {discovered_qwen!r}")
    _write_json(run_dir / "manifest.json", manifest)

    results_path = run_dir / "results.jsonl"
    ledger_path = run_dir / "attempts.jsonl"
    model_totals = {}
    for entry in arms:
        game_closed()
        _assert_frozen(frozen)
        metadata = {"case_id": entry["case"]["id"], "category": entry["case"]["category"], "arm": entry["arm"]}
        _assert_saved_payload(entry["d3Path"], entry["d3Sha256"])
        d3_response, d3_elapsed = _post_arm(d3_url + "/v1/systemone", entry["d3Payload"], ledger_path,
                                            dict(metadata, model="D3"), timeout)
        if d3_response.get("request_sha256") != entry["d3Sha256"]:
            raise RuntimeError("Serialized D3 request differs from frozen payload hash")
        d3_result = _model_route_result("D3", args.expected_model, entry["case"], d3_response, d3_elapsed,
                                        entry["offeredRoutes"])
        row = {"caseId": entry["case"]["id"], "category": entry["case"]["category"], "arm": entry["arm"],
               "expectedRoutes": list(entry["case"]["expectedRoutes"]), "d3RequestSha256": entry["d3Sha256"],
               "d3": d3_result}
        _accumulate(model_totals, "D3", entry["case"], d3_result)
        if qwen_url:
            game_closed()
            _assert_frozen(frozen)
            _assert_saved_payload(entry["qwenPath"], entry["qwenSha256"])
            qwen_response, qwen_elapsed = _post_arm(qwen_url + "/v1/chat/completions", entry["qwenPayload"],
                                                    ledger_path, dict(metadata, model="Qwen"), timeout)
            if qwen_response.get("request_sha256") != entry["qwenSha256"]:
                raise RuntimeError("Serialized Qwen request differs from frozen payload hash")
            qwen_result = _model_route_result("Qwen", args.expected_qwen_model, entry["case"], qwen_response, qwen_elapsed,
                                              entry["offeredRoutes"])
            row["qwenRequestSha256"] = entry["qwenSha256"]
            row["qwen"] = qwen_result
            _accumulate(model_totals, "Qwen", entry["case"], qwen_result)
        _append_jsonl(results_path, row)
        print(entry["case"]["id"], entry["arm"], "complete", flush=True)
    summary = {"totalAttempts": len(arms) * (2 if qwen_url else 1), "caseCount": len(suite["cases"]),
               "suiteCasesSha256": suite_digest, "models": model_totals,
               "claims": {"npcWillingness": False, "worldEffects": False, "pipelineMaterialized": False}}
    _write_json(run_dir / "summary.json", summary)
    return summary


def _accumulate(totals: dict[str, Any], model: str, case: dict[str, Any], result: dict[str, Any]):
    arm = result["model"]
    model_group = totals.setdefault(arm, {"arms": 0, "goldAgreement": 0, "falseAction": 0, "abstention": 0,
                                         "invalid": 0, "noResult": 0, "overNativeBudget": 0,
                                         "byCategory": {}})
    model_group["arms"] += 1
    model_group["goldAgreement"] += int(bool(result["goldAgreement"]))
    model_group["falseAction"] += int(bool(result["falseAction"]))
    model_group["abstention"] += int(bool(result["abstention"]))
    model_group["invalid"] += int(bool(result["invalid"]))
    model_group["noResult"] += int(bool(result["noResult"]))
    model_group["overNativeBudget"] += int(not result["withinNativeBudget"])
    category = model_group["byCategory"].setdefault(case["category"], {"arms": 0, "goldAgreement": 0,
        "falseAction": 0, "abstention": 0, "invalid": 0, "noResult": 0, "overNativeBudget": 0})
    category["arms"] += 1
    for key, source in (("goldAgreement", "goldAgreement"), ("falseAction", "falseAction"),
                        ("abstention", "abstention"), ("invalid", "invalid"), ("noResult", "noResult")):
        category[key] += int(bool(result[source]))
    category["overNativeBudget"] += int(not result["withinNativeBudget"])


def _assert_saved_payload(path: Path, expected_hash: str):
    if not path.is_file() or _sha256_bytes(path.read_bytes()) != expected_hash:
        raise RuntimeError(f"Frozen request payload changed during run: {path}")


if __name__ == "__main__":
    run()
