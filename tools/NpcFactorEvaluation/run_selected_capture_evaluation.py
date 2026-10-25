"""One-pass serial capture runner for selected production-exported cases."""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path, PurePosixPath, PureWindowsPath
from typing import Callable
from urllib.parse import urlsplit


DEFAULT_QWEN_ENDPOINT = "http://127.0.0.1:8080/completion"
DEFAULT_D3NANO_ENDPOINT = "http://127.0.0.1:8093/v1/systemone"
Transport = Callable[[str, bytes, float], dict]


def _sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def _canonical_json(value: object) -> bytes:
    return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":")).encode("utf-8")


def _read_cases(path: Path) -> tuple[list[dict], bytes]:
    raw = path.read_bytes()
    rows: list[dict] = []
    for number, line in enumerate(raw.decode("utf-8-sig", errors="strict").splitlines(), 1):
        if not line.strip():
            continue
        try:
            row = json.loads(line)
        except json.JSONDecodeError as error:
            raise ValueError(f"Invalid JSONL row {number}: {error}") from error
        if not isinstance(row, dict):
            raise ValueError(f"JSONL row {number} must be an object")
        rows.append(row)
    if not rows:
        raise ValueError("Input JSONL has no case rows")
    return rows, raw


def _validate_loopback_endpoint(endpoint: str) -> None:
    parsed = urlsplit(endpoint)
    if (parsed.scheme != "http" or parsed.hostname not in {"127.0.0.1", "localhost", "::1"}
            or parsed.username is not None or parsed.password is not None or parsed.port is None):
        raise ValueError("Model endpoint must use unauthenticated HTTP on explicit loopback host and port")


def _case_hash(row: dict) -> str:
    return _sha256(_canonical_json(row))


def _safe_case_id(value: object) -> str:
    if not isinstance(value, str) or not value.strip() or any(ord(char) < 32 for char in value):
        raise ValueError("caseId must be a nonempty string without control characters")
    posix = PurePosixPath(value)
    windows = PureWindowsPath(value)
    if posix.is_absolute() or windows.is_absolute() or windows.drive or ".." in posix.parts or ".." in windows.parts:
        raise ValueError(f"caseId contains a path escape: {value!r}")
    return value


def _request_bytes(row: dict) -> bytes:
    prompt = row.get("prompt")
    request_body = row.get("requestBody")
    if ("prompt" in row) == ("requestBody" in row):
        raise ValueError(f"{row['caseId']}: provide exactly one of prompt or requestBody")
    if "prompt" in row:
        if row["lane"] != "qwen" or not isinstance(prompt, str):
            raise ValueError(f"{row['caseId']}: prompt input requires qwen lane and string prompt")
        value: object = {"prompt": prompt}
        body = _canonical_json(value)
    elif isinstance(request_body, dict):
        body = _canonical_json(request_body)
    elif isinstance(request_body, str):
        body = request_body.encode("utf-8", errors="strict")
    else:
        raise ValueError(f"{row['caseId']}: requestBody must be JSON object or JSON text")
    try:
        json.loads(body.decode("utf-8", errors="strict"))
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        raise ValueError(f"{row['caseId']}: request body must be valid JSON") from error
    return body


def _validate_source_hashes(row: dict, repo_root: Path) -> dict[str, str]:
    hashes = row.get("sourceHashes")
    if not isinstance(hashes, dict) or not hashes:
        raise ValueError(f"{row['caseId']}: sourceHashes must map source paths to SHA-256 values")
    verified: dict[str, str] = {}
    for relative, expected in hashes.items():
        if not isinstance(relative, str) or not isinstance(expected, str) or len(expected) != 64 or any(
                char not in "0123456789abcdefABCDEF" for char in expected):
            raise ValueError(f"{row['caseId']}: invalid source hash entry")
        relative_path = Path(relative)
        if relative_path.is_absolute() or PureWindowsPath(relative).drive or ".." in PurePosixPath(relative.replace("\\", "/")).parts:
            raise ValueError(f"{row['caseId']}: source path escapes repository root: {relative}")
        source = (repo_root / relative_path).resolve()
        try:
            source.relative_to(repo_root)
        except ValueError as error:
            raise ValueError(f"{row['caseId']}: source path escapes repository root: {relative}") from error
        if not source.is_file():
            raise ValueError(f"{row['caseId']}: source file missing: {relative}")
        actual = _sha256(source.read_bytes())
        if actual.lower() != expected.lower():
            raise ValueError(f"{row['caseId']}: source hash mismatch for {relative}")
        verified[relative.replace("\\", "/")] = actual
    return verified


def _validate_rows(rows: list[dict], repo_root: Path) -> tuple[list[dict], dict[str, bytes]]:
    seen: set[str] = set()
    bodies: dict[str, bytes] = {}
    for row in rows:
        case_id = _safe_case_id(row.get("caseId"))
        if case_id in seen:
            raise ValueError(f"Duplicate caseId: {case_id}")
        seen.add(case_id)
        lane = row.get("lane")
        if lane not in {"qwen", "d3nano"}:
            raise ValueError(f"{case_id}: lane must be qwen or d3nano")
        expected_model = row.get("expectedModel")
        if not isinstance(expected_model, str) or not expected_model.strip():
            raise ValueError(f"{case_id}: expectedModel must be nonempty")
        bodies[case_id] = _request_bytes(row)
        row["_verifiedSourceHashes"] = _validate_source_hashes(row, repo_root)
    return rows, bodies


def _http_request(url: str, *, body: bytes | None = None, timeout: float) -> dict:
    method = "POST" if body is not None else "GET"
    request = urllib.request.Request(url, data=body,
                                     headers={"Content-Type": "application/json"} if body is not None else {},
                                     method=method)
    started = time.perf_counter()
    try:
        with urllib.request.urlopen(request, timeout=timeout) as response:
            return {"status": response.status, "body": response.read(),
                    "elapsedMs": (time.perf_counter() - started) * 1000,
                    "headers": dict(response.headers.items()), "error": None}
    except urllib.error.HTTPError as error:
        return {"status": error.code, "body": error.read(),
                "elapsedMs": (time.perf_counter() - started) * 1000,
                "headers": dict(error.headers.items()) if error.headers else {},
                "error": f"HTTPError: {error.code}"}
    except Exception as error:
        return {"status": None, "body": b"", "elapsedMs": (time.perf_counter() - started) * 1000,
                "headers": {}, "error": f"{type(error).__name__}: {error}"}


def _json_object(body: bytes, description: str) -> dict:
    try:
        value = json.loads(body.decode("utf-8", errors="strict"))
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        raise ValueError(f"{description} returned invalid JSON") from error
    if not isinstance(value, dict):
        raise ValueError(f"{description} returned non-object JSON")
    return value


def _preflight(endpoint: str, expected_model: str, timeout: float,
               http: Callable[..., dict]) -> dict:
    parsed = urlsplit(endpoint)
    origin = f"{parsed.scheme}://{parsed.netloc}"
    snapshots = {}
    for name in ("health", "props"):
        result = http(origin + "/" + name, timeout=timeout)
        if result.get("status") != 200 or result.get("error"):
            raise ValueError(f"{name} preflight failed: {result.get('error') or result.get('status')}")
        snapshots[name] = _json_object(result.get("body", b""), name)
    health = snapshots["health"]
    if health.get("status") not in (None, "ok", "healthy") or health.get("healthy") is False:
        raise ValueError(f"Server health check failed: {health}")
    loaded_model = snapshots["props"].get("model_path")
    if not isinstance(loaded_model, str) or loaded_model != expected_model:
        raise ValueError(f"model identity mismatch: expected {expected_model!r}, loaded {loaded_model!r}")
    return snapshots


def post_json_bytes(url: str, body: bytes, timeout: float) -> dict:
    return _http_request(url, body=body, timeout=timeout)


def _append_event(stream, event: dict) -> None:
    stream.write(json.dumps(event, ensure_ascii=False, separators=(",", ":")) + "\n")
    stream.flush()
    import os
    os.fsync(stream.fileno())


def _load_journal(path: Path) -> list[dict]:
    if not path.exists():
        return []
    rows = []
    for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
        try:
            row = json.loads(line)
        except json.JSONDecodeError as error:
            raise ValueError(f"Journal row {number} is incomplete or invalid; refusing resume") from error
        if not isinstance(row, dict):
            raise ValueError(f"Journal row {number} must be an object")
        rows.append(row)
    return rows


def _response_metadata(body: bytes) -> dict | None:
    try:
        value = json.loads(body.decode("utf-8", errors="strict"))
    except (UnicodeDecodeError, json.JSONDecodeError):
        return None
    if not isinstance(value, dict):
        return None
    return {key: value[key] for key in (
        "model", "tokens_evaluated", "tokens_predicted", "timings", "stop_type", "usage"
    ) if key in value}


def run_selected_capture_evaluation(input_path: Path, output_dir: Path, *,
                                    repo_root: Path | None = None,
                                    qwen_endpoint: str = DEFAULT_QWEN_ENDPOINT,
                                    d3nano_endpoint: str = DEFAULT_D3NANO_ENDPOINT,
                                    timeout: float = 600,
                                    transport: Transport = post_json_bytes) -> dict:
    input_path = Path(input_path).resolve()
    output_dir = Path(output_dir).resolve()
    repo_root = Path(repo_root or Path(__file__).resolve().parents[2]).resolve()
    if timeout <= 0:
        raise ValueError("timeout must be positive")
    for endpoint in (qwen_endpoint, d3nano_endpoint):
        _validate_loopback_endpoint(endpoint)
    rows, input_bytes = _read_cases(input_path)
    rows, request_bodies = _validate_rows(rows, repo_root)
    input_hash = _sha256(input_bytes)
    endpoint_by_lane = {"qwen": qwen_endpoint, "d3nano": d3nano_endpoint}
    expected_by_lane: dict[str, set[str]] = {"qwen": set(), "d3nano": set()}
    for row in rows:
        expected_by_lane[row["lane"]].add(row["expectedModel"])
    if any(len(models) != 1 for models in expected_by_lane.values() if models):
        raise ValueError("Each lane must use one expectedModel identity per run")

    owner_path = output_dir / "owner.json"
    journal_path = output_dir / "journal.jsonl"
    owner = {"schema": "selected-capture-run-v1", "inputSha256": input_hash,
             "qwenEndpoint": qwen_endpoint, "d3nanoEndpoint": d3nano_endpoint,
             "sourceHashes": {row["caseId"]: row["_verifiedSourceHashes"] for row in rows}}
    if output_dir.exists():
        if not owner_path.is_file():
            raise FileExistsError("Output directory exists without owner.json; refusing to claim it")
        existing_owner = json.loads(owner_path.read_text(encoding="utf-8"))
        if existing_owner != owner:
            raise ValueError("Run ownership or input hash mismatch; use original inputs and endpoints")
    else:
        output_dir.mkdir(parents=True)
        with owner_path.open("x", encoding="utf-8", newline="\n") as stream:
            json.dump(owner, stream, ensure_ascii=False, separators=(",", ":"))
            stream.flush()
            import os
            os.fsync(stream.fileno())

    case_hashes = {row["caseId"]: _case_hash({key: value for key, value in row.items() if not key.startswith("_")})
                   for row in rows}
    events = _load_journal(journal_path)
    started: dict[str, dict] = {}
    terminal: dict[str, dict] = {}
    for event in events:
        case_id = event.get("caseId")
        if case_id not in case_hashes:
            raise ValueError(f"Journal contains unknown caseId: {case_id}")
        if event.get("caseHash") != case_hashes[case_id]:
            raise ValueError(f"case hash mismatch for {case_id}; refusing resume")
        if event.get("event") == "attempt_started":
            if case_id in started or case_id in terminal:
                raise ValueError(f"Journal contains duplicate attempt for {case_id}")
            started[case_id] = event
        elif event.get("event") in {"completed", "failed", "uncertain"}:
            orphan_uncertain = event.get("event") == "uncertain" and event.get("attemptStarted") is not True
            if (case_id not in started and not orphan_uncertain) or case_id in terminal:
                raise ValueError(f"Journal terminal event has no unique start for {case_id}")
            terminal[case_id] = event
        else:
            raise ValueError(f"Journal has unknown event type: {event.get('event')}")

    output_dir.joinpath("cases").mkdir(exist_ok=True)
    # Terminal and ambiguous attempts never contact a server again. Only genuinely
    # unattempted cases require live preflight; recovery remains usable offline.
    pending_lanes = set()
    for row in rows:
        case_id = row["caseId"]
        case_dir = output_dir / "cases" / _sha256(case_id.encode("utf-8"))
        if case_id not in terminal and case_id not in started and not any(
                (case_dir / name).exists() for name in ("request.json", "response.bin", "transport-error.json")):
            pending_lanes.add(row["lane"])
    preflight_by_lane = {
        lane: _preflight(endpoint_by_lane[lane], next(iter(expected_by_lane[lane])), timeout, _http_request)
        for lane in sorted(pending_lanes)
    }
    counts = {"sent": 0, "failed": 0, "uncertain": 0, "skippedTerminal": 0}
    with journal_path.open("a", encoding="utf-8", newline="\n") as journal:
        for row in rows:
            case_id = row["caseId"]
            case_hash = case_hashes[case_id]
            if case_id in terminal:
                counts["skippedTerminal"] += 1
                continue
            folder_name = _sha256(case_id.encode("utf-8"))
            case_dir = output_dir / "cases" / folder_name
            case_dir.mkdir(exist_ok=True)
            request_path = case_dir / "request.json"
            response_path = case_dir / "response.bin"
            error_path = case_dir / "transport-error.json"
            if case_id in started or request_path.exists() or response_path.exists() or error_path.exists():
                if case_id not in started:
                    uncertain_event = {"event": "uncertain", "caseId": case_id, "caseHash": case_hash,
                                       "attemptStarted": False,
                                       "reason": "request artifact exists without a journaled attempt"}
                    _append_event(journal, uncertain_event)
                    counts["uncertain"] += 1
                elif case_id not in terminal:
                    _append_event(journal, {"event": "uncertain", "caseId": case_id,
                                            "caseHash": case_hash,
                                            "reason": "attempt started without terminal response record"})
                    counts["uncertain"] += 1
                continue
            body = request_bodies[case_id]
            with request_path.open("xb") as stream:
                stream.write(body)
                stream.flush()
                import os
                os.fsync(stream.fileno())
            endpoint = endpoint_by_lane[row["lane"]]
            started_event = {"event": "attempt_started", "caseId": case_id, "caseHash": case_hash,
                             "lane": row["lane"], "endpoint": endpoint,
                             "requestPath": str(request_path.relative_to(output_dir)).replace("\\", "/"),
                             "requestSha256": _sha256(body), "startedAtUtc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
                             "preflight": preflight_by_lane[row["lane"]]}
            _append_event(journal, started_event)
            started[case_id] = started_event
            response = transport(endpoint, body, timeout)
            response_body = response.get("body", b"")
            if not isinstance(response_body, (bytes, bytearray)):
                response_body = b""
                response_error = response.get("error") or "invalid_transport_body_type"
            else:
                response_body = bytes(response_body)
                response_error = response.get("error")
            metadata = _response_metadata(response_body)
            if response.get("status") == 200 and not response_error and (
                    metadata is None or metadata.get("model") != row["expectedModel"]):
                response_error = "response_model_identity_mismatch"
            terminal_name = "completed" if response.get("status") == 200 and not response_error else "failed"
            with response_path.open("xb") as stream:
                stream.write(response_body)
                stream.flush()
                import os
                os.fsync(stream.fileno())
            if response_error:
                with error_path.open("xb") as stream:
                    stream.write(_canonical_json({"error": response_error}) + b"\n")
                    stream.flush()
                    import os
                    os.fsync(stream.fileno())
            terminal_event = {"event": terminal_name, "caseId": case_id, "caseHash": case_hash,
                              "lane": row["lane"], "httpStatus": response.get("status"),
                              "latencyMs": float(response.get("elapsedMs", 0.0)),
                              "responsePath": str(response_path.relative_to(output_dir)).replace("\\", "/"),
                              "transportErrorPath": str(error_path.relative_to(output_dir)).replace("\\", "/") if response_error else None,
                              "transportError": response_error,
                              "responseSha256": _sha256(response_body) if response_body else None,
                              "responseMetadata": metadata}
            _append_event(journal, terminal_event)
            terminal[case_id] = terminal_event
            if terminal_name == "completed":
                counts["sent"] += 1
            else:
                counts["failed"] += 1
    return {**counts, "cases": len(rows), "outputDir": str(output_dir)}


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Capture selected production-exported model cases once, serially.")
    parser.add_argument("--input", required=True, type=Path, help="Selected cases JSONL")
    parser.add_argument("--output-dir", required=True, type=Path, help="New or matching resumable run directory")
    parser.add_argument("--repo-root", type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument("--qwen-endpoint", default=DEFAULT_QWEN_ENDPOINT)
    parser.add_argument("--d3nano-endpoint", default=DEFAULT_D3NANO_ENDPOINT)
    parser.add_argument("--timeout", type=float, default=600)
    args = parser.parse_args(argv)
    try:
        result = run_selected_capture_evaluation(args.input, args.output_dir, repo_root=args.repo_root,
                                                 qwen_endpoint=args.qwen_endpoint,
                                                 d3nano_endpoint=args.d3nano_endpoint,
                                                 timeout=args.timeout)
    except Exception as error:
        print(f"SELECTED_CAPTURE_EVALUATION_FAILED: {error}", file=sys.stderr)
        return 1
    print(json.dumps(result, ensure_ascii=False, separators=(",", ":")))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
