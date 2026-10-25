"""Serial local D3 -> Core validation -> Qwen evaluation runner.

This module never reads oracle files, retries requests, or scores prose.
"""

from __future__ import annotations

import argparse
import base64
import hashlib
import json
import subprocess
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path
from typing import Callable, Iterable


Transport = Callable[[str, bytes], dict]
CoreBridge = Callable[[Path, Path, Path, list[dict]], list[dict]]


def _json_lines(path: Path) -> list[dict]:
    rows = []
    for line_number, line in enumerate(path.read_text(encoding="utf-8-sig").splitlines(), 1):
        if not line.strip():
            continue
        try:
            row = json.loads(line)
        except json.JSONDecodeError as exc:
            raise ValueError(f"Invalid JSONL row {line_number}: {exc}") from exc
        if not isinstance(row, dict):
            raise ValueError(f"JSONL row {line_number} must be an object")
        rows.append(row)
    return rows


def _sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def _contains_oracle(value: object) -> bool:
    if isinstance(value, dict):
        return any(key == "oracle" or key in {"nativeEvents", "nativeFacts", "worldTruth"} or
                   _contains_oracle(child) for key, child in value.items())
    if isinstance(value, list):
        return any(_contains_oracle(child) for child in value)
    return False


def validate_prepared_row(row: dict) -> bytes:
    """Verify exact stage-1 bytes and binding before any network call."""
    request_id = row.get("requestId")
    if row.get("recordType") != "prepared-multi-turn-request" or not isinstance(request_id, str) or not request_id:
        raise ValueError("Prepared row must be a multi-turn request with requestId")
    if row.get("contextSchemaVersion") != 2:
        raise ValueError(f"{request_id}: context parity v2 required; refusing legacy prepared rows")
    if _contains_oracle(row):
        raise ValueError(f"{request_id}: evaluator oracle leaked into prepared request")
    model_input = row.get("modelInput")
    if not isinstance(model_input, dict):
        raise ValueError(f"{request_id}: full modelInput snapshot is required for Core reconstruction")
    body = row.get("d3RequestBody")
    expected_hash = row.get("d3RequestSha256")
    if not isinstance(body, str) or not isinstance(expected_hash, str):
        raise ValueError(f"{request_id}: exact D3 wire body and SHA-256 are required")
    body_bytes = body.encode("utf-8", errors="strict")
    actual_hash = _sha256(body_bytes)
    if actual_hash != expected_hash.lower():
        raise ValueError(f"{request_id}: D3 wire hash mismatch")
    try:
        envelope = json.loads(body)
        state = json.loads(envelope["state"])
    except (KeyError, TypeError, json.JSONDecodeError) as exc:
        raise ValueError(f"{request_id}: malformed prepared production D3 body") from exc
    binding = row.get("coreDecisionBinding")
    if not isinstance(binding, dict):
        raise ValueError(f"{request_id}: Core decision binding missing")
    for row_key, state_key in (("requestId", "requestId"), ("protocol", "protocolVersion"),
                               ("snapshotRevision", "snapshotRevision"),
                               ("candidateSetRevision", "candidateSetRevision"),
                               ("personId", "actorId")):
        if binding.get(row_key) != state.get(state_key):
            raise ValueError(f"{request_id}: Core decision binding differs from exact D3 request ({row_key})")
    question = envelope.get("questions", {}).get("decision", {})
    criteria = question.get("criteria")
    row_options = row.get("options")
    if question.get("type") != "choice" or not isinstance(criteria, dict):
        raise ValueError(f"{request_id}: prepared D3 decision must be a choice question")
    if not isinstance(row_options, list):
        raise ValueError(f"{request_id}: offered option list missing")
    row_ids = [item.get("id") for item in row_options if isinstance(item, dict)]
    if not row_ids or list(criteria) != row_ids:
        raise ValueError(f"{request_id}: offered option IDs differ across prepared Core projections")
    context = state.get("context")
    if not isinstance(context, str) or any(marker in context.lower() for marker in ("nativefacts", "nativeevents", "evaluator oracle")):
        raise ValueError(f"{request_id}: evaluator oracle leaked into D3 context")
    if "qwenRequestBody" in row:
        raise ValueError(f"{request_id}: Qwen body must be built after validated D3 decision")
    return body_bytes


def _response_model(body: bytes) -> str | None:
    try:
        value = json.loads(body.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError):
        return None
    model = value.get("model") if isinstance(value, dict) else None
    return model if isinstance(model, str) else None


def post_json_bytes(url: str, body: bytes) -> dict:
    """Single HTTP attempt. Return transport errors as data; never retry."""
    request = urllib.request.Request(url, data=body, headers={"Content-Type": "application/json"}, method="POST")
    started = time.perf_counter()
    try:
        with urllib.request.urlopen(request, timeout=600) as response:
            response_body = response.read()
            return {"status": response.status, "body": response_body,
                    "elapsedMs": (time.perf_counter() - started) * 1000,
                    "headers": dict(response.headers.items()), "error": None}
    except urllib.error.HTTPError as error:
        response_body = error.read()
        return {"status": error.code, "body": response_body,
                "elapsedMs": (time.perf_counter() - started) * 1000,
                "headers": dict(error.headers.items()) if error.headers else {},
                "error": f"HTTPError: {error.code}"}
    except Exception as error:  # Preserve one failed attempt; no retry or reroll.
        return {"status": None, "body": b"", "elapsedMs": (time.perf_counter() - started) * 1000,
                "headers": {}, "error": f"{type(error).__name__}: {error}"}


def _write_jsonl_new(path: Path, rows: Iterable[dict]) -> None:
    with path.open("x", encoding="utf-8", newline="\n") as stream:
        for row in rows:
            stream.write(json.dumps(row, ensure_ascii=False, separators=(",", ":")) + "\n")
            stream.flush()


def _read_d3_body(response: dict) -> bytes:
    body = response.get("body", b"")
    if not isinstance(body, (bytes, bytearray)):
        raise TypeError("Transport body must be bytes")
    return bytes(body)


def _bridge_core(prepared_path: Path, d3_path: Path, plan_path: Path, project_path: Path) -> list[dict]:
    command = ["dotnet", "run", "--no-build", "--project", str(project_path), "--",
               "compose-qwen", "--prepared", str(prepared_path), "--d3-results", str(d3_path),
               "--output", str(plan_path)]
    result = subprocess.run(command, cwd=str(project_path.parent.parent.parent), capture_output=True,
                            text=True, check=False)
    if result.returncode != 0:
        raise RuntimeError("Core decision-stage bridge failed without rebuilding: " + result.stderr.strip())
    return _json_lines(plan_path)


def _validate_episode_limit(rows: list[dict], limit: int) -> list[dict]:
    if limit < 1:
        raise ValueError("--limit must be positive")
    selected = rows[:limit]
    if len(rows) > len(selected) and selected and rows[len(selected)].get("episodeId") == selected[-1].get("episodeId"):
        raise ValueError("Row limit would split a matched episode; prepare complete episodes first")
    return selected


def run_serial_evaluation(prepared_path: Path, output_path: Path, d3_url: str, qwen_url: str,
                          *, transport: Transport = post_json_bytes,
                          core_bridge: CoreBridge | None = None, limit: int = 4,
                          project_path: Path | None = None) -> dict:
    prepared_path = Path(prepared_path).resolve()
    output_path = Path(output_path).resolve()
    d3_path = output_path.with_name(output_path.stem + ".d3.jsonl")
    plan_path = output_path.with_name(output_path.stem + ".plans.jsonl")
    if len({prepared_path, output_path, d3_path, plan_path}) != 4:
        raise ValueError("Prepared, output, D3, and plan paths must be distinct")
    rows = _validate_episode_limit(_json_lines(prepared_path), limit)
    if not rows:
        raise ValueError("Prepared file has no request rows")
    bodies = [validate_prepared_row(row) for row in rows]
    ids = [row["requestId"] for row in rows]
    if len(ids) != len(set(ids)):
        raise ValueError("Prepared requestId values must be unique")
    output_path.parent.mkdir(parents=True, exist_ok=True)

    d3_results = []
    for row, body in zip(rows, bodies):
        response = transport(d3_url, body)
        response_body = _read_d3_body(response)
        d3_results.append({
            "requestId": row["requestId"],
            "httpStatus": response.get("status"),
            "elapsedMs": float(response.get("elapsedMs", 0.0)),
            "transportError": response.get("error"),
            "modelId": _response_model(response_body),
            "rawRequest": body.decode("utf-8"),
            "rawResponse": response_body.decode("utf-8", errors="replace"),
            "rawResponseBase64": base64.b64encode(response_body).decode("ascii"),
        })
    _write_jsonl_new(d3_path, d3_results)

    if core_bridge is None:
        if project_path is None:
            project_path = Path(__file__).resolve().parent / "NpcFactorEvaluation.csproj"
        plans = _bridge_core(prepared_path, d3_path, plan_path, Path(project_path).resolve())
    else:
        plans = core_bridge(prepared_path, d3_path, plan_path, rows)
        _write_jsonl_new(plan_path, plans)
    plans_by_id = {plan.get("requestId"): plan for plan in plans}
    if set(plans_by_id) != set(ids) or len(plans_by_id) != len(plans):
        raise ValueError("Core stage must return exactly one decision result per prepared request")
    d3_by_id = {row["requestId"]: row for row in d3_results}

    result_rows = []
    with output_path.open("x", encoding="utf-8", newline="\n") as stream:
        for row in rows:
            request_id = row["requestId"]
            plan = plans_by_id[request_id]
            d3 = d3_by_id[request_id]
            validated = plan.get("validated") is True
            qwen_body_text = plan.get("qwenRequestBody")
            qwen_result = None
            failure_stage = None
            if not validated:
                failure_stage = "d3_validation"
            elif not isinstance(qwen_body_text, str) or not qwen_body_text:
                failure_stage = "core_qwen_build"
            else:
                qwen_body = qwen_body_text.encode("utf-8", errors="strict")
                qwen_http = transport(qwen_url, qwen_body)
                qwen_response = _read_d3_body(qwen_http)
                qwen_result = {
                    "httpStatus": qwen_http.get("status"),
                    "elapsedMs": float(qwen_http.get("elapsedMs", 0.0)),
                    "transportError": qwen_http.get("error"),
                    "modelId": _response_model(qwen_response),
                    "rawRequest": qwen_body_text,
                    "rawResponse": qwen_response.decode("utf-8", errors="replace"),
                    "rawResponseBase64": base64.b64encode(qwen_response).decode("ascii"),
                }
                if qwen_http.get("status") != 200:
                    failure_stage = "qwen_transport"
            raw_request = qwen_result["rawRequest"] if qwen_result else d3["rawRequest"]
            raw_response = qwen_result["rawResponse"] if qwen_result else d3["rawResponse"]
            result = {
                "recordType": "serial-evaluation-response",
                "requestId": request_id,
                "episodeId": row.get("episodeId"), "armId": row.get("armId"), "turnId": row.get("turnId"),
                "contextReplayMode": "fixed_snapshot_replay",
                "selectedOptionId": plan.get("selectedOptionId") if validated else None,
                "decisionValidation": plan.get("decisionValidation"),
                "d3": d3, "qwen": qwen_result,
                "failureStage": failure_stage,
                "rawRequest": raw_request, "rawResponse": raw_response,
                "modelMetadata": {"d3": {"model": d3.get("modelId"), "status": d3.get("httpStatus"),
                                           "latencyMs": d3.get("elapsedMs")},
                                  "qwen": None if qwen_result is None else {
                                      "model": qwen_result.get("modelId"), "status": qwen_result.get("httpStatus"),
                                      "latencyMs": qwen_result.get("elapsedMs")}},
            }
            result_rows.append(result)
            stream.write(json.dumps(result, ensure_ascii=False, separators=(",", ":")) + "\n")
            stream.flush()
    return {"preparedRows": len(rows), "d3Rows": len(d3_results),
            "qwenRows": sum(item["qwen"] is not None for item in result_rows),
            "failedRows": sum(item["failureStage"] is not None for item in result_rows),
            "output": str(output_path), "d3Evidence": str(d3_path), "corePlans": str(plan_path)}


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Serial D3 choice, Core validation, and Qwen response evaluation")
    parser.add_argument("--prepared", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--d3-url", default="http://127.0.0.1:8093/v1/systemone")
    parser.add_argument("--qwen-url", default="http://127.0.0.1:8080/completion")
    parser.add_argument("--limit", type=int, default=4, help="Whole-row cap; default is first four rows")
    args = parser.parse_args(argv)
    try:
        result = run_serial_evaluation(args.prepared, args.output, args.d3_url, args.qwen_url,
                                       limit=args.limit)
    except Exception as exc:
        print(f"SERIAL_EVALUATION_FAILED: {exc}", file=sys.stderr)
        return 1
    print(json.dumps(result, ensure_ascii=False, separators=(",", ":")))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
