"""Serial one-shot appraisal evaluation through prepared Core completion bodies."""

from __future__ import annotations

import argparse
import base64
import json
import subprocess
import sys
import time
import urllib.error
import urllib.request
from urllib.parse import urlsplit
from pathlib import Path
from typing import Callable

from validate_appraisal_preparation import APPRAISAL_V3_MANIFEST_PATH, validate_rows


Transport = Callable[[str, bytes], dict]
CoreBridge = Callable[[Path, Path, Path], None]
CorePreflight = Callable[[Path, Path], None]


def _read_jsonl(path: Path) -> list[dict]:
    rows = []
    for number, line in enumerate(path.read_text(encoding="utf-8-sig").splitlines(), 1):
        if not line.strip():
            continue
        try:
            row = json.loads(line)
        except json.JSONDecodeError as error:
            raise ValueError(f"Invalid JSONL row {number}: {error}") from error
        if not isinstance(row, dict):
            raise ValueError(f"JSONL row {number} must be an object")
        rows.append(row)
    return rows


def _write_jsonl_new(path: Path, rows: list[dict]) -> None:
    with path.open("x", encoding="utf-8", newline="\n") as stream:
        for row in rows:
            stream.write(json.dumps(row, ensure_ascii=False, separators=(",", ":")) + "\n")
            stream.flush()


def post_json_bytes(url: str, body: bytes) -> dict:
    request = urllib.request.Request(url, data=body,
                                     headers={"Content-Type": "application/json"}, method="POST")
    started = time.perf_counter()
    try:
        with urllib.request.urlopen(request, timeout=600) as response:
            return {"status": response.status, "body": response.read(),
                    "elapsedMs": (time.perf_counter() - started) * 1000,
                    "headers": dict(response.headers.items()), "error": None}
    except urllib.error.HTTPError as error:
        return {"status": error.code, "body": error.read(),
                "elapsedMs": (time.perf_counter() - started) * 1000,
                "headers": dict(error.headers.items()) if error.headers else {},
                "error": f"HTTPError: {error.code}"}
    except Exception as error:  # Each row gets one attempt; transport errors are evidence.
        return {"status": None, "body": b"", "elapsedMs": (time.perf_counter() - started) * 1000,
                "headers": {}, "error": f"{type(error).__name__}: {error}"}


def _completion_content(body: bytes) -> tuple[str | None, dict, str | None]:
    try:
        envelope = json.loads(body.decode("utf-8", errors="strict"))
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        return None, {}, f"invalid_completion_envelope:{type(error).__name__}"
    if not isinstance(envelope, dict):
        return None, {}, "invalid_completion_envelope:root_not_object"
    content = envelope.get("content")
    if not isinstance(content, str):
        return None, {"model": envelope.get("model")}, "invalid_completion_envelope:content_missing"
    metadata = {key: envelope.get(key) for key in (
        "model", "tokens_evaluated", "tokens_predicted", "generation_settings", "timings", "stop_type"
    ) if key in envelope}
    return content, metadata, None


def _validate_loopback_endpoint(endpoint: str) -> None:
    parsed = urlsplit(endpoint)
    if parsed.scheme != "http" or parsed.hostname not in {"127.0.0.1", "localhost", "::1"} or \
            parsed.username is not None or parsed.password is not None or parsed.port is None:
        raise ValueError("Appraisal endpoint must be unauthenticated HTTP on explicit loopback host and port")


def _bridge_core(prepared: Path, responses: Path, output: Path,
                 project: Path, fixture_version: str = "v1") -> None:
    command = ["dotnet", "run", "--configuration", "Release", "--no-build", "--project", str(project), "--",
               "record-appraisal", "--prepared", str(prepared), "--responses", str(responses),
               "--output", str(output)]
    if fixture_version != "v1":
        command.extend(("--fixture-version", fixture_version))
    completed = subprocess.run(command, cwd=str(project.parent.parent.parent),
                               capture_output=True, text=True, check=False)
    if completed.returncode != 0:
        details = "\n".join(part.strip() for part in (completed.stdout, completed.stderr) if part and part.strip())
        if not details:
            details = "dotnet exited with code " + str(completed.returncode) + " without diagnostic output"
        raise RuntimeError("Core appraisal response validation failed (exit " + str(completed.returncode) + "):\n" + details)


def _preflight_core(prepared: Path, project: Path, fixture_version: str = "v1") -> None:
    command = ["dotnet", "run", "--configuration", "Release", "--no-build", "--project", str(project), "--",
               "validate-appraisal-prepared", "--prepared", str(prepared)]
    if fixture_version != "v1":
        command.extend(("--fixture-version", fixture_version))
    completed = subprocess.run(command, cwd=str(project.parent.parent.parent),
                               capture_output=True, text=True, check=False)
    if completed.returncode != 0:
        details = "\n".join(part.strip() for part in (completed.stdout, completed.stderr) if part and part.strip())
        if not details:
            details = "dotnet exited with code " + str(completed.returncode) + " without diagnostic output"
        raise RuntimeError("Core appraisal request preflight failed before model calls (exit " + str(completed.returncode) + "):\n" + details)


def run_appraisal_evaluation(prepared_path: Path, raw_output_path: Path, validated_output_path: Path,
                             *, endpoint: str = "http://127.0.0.1:8080/completion",
                             transport: Transport = post_json_bytes,
                             core_bridge: CoreBridge | None = None,
                             core_preflight: CorePreflight | None = None,
                             project_path: Path | None = None,
                             fixture_version: str = "v1") -> dict:
    prepared_path = Path(prepared_path).resolve()
    raw_output_path = Path(raw_output_path).resolve()
    validated_output_path = Path(validated_output_path).resolve()
    if len({prepared_path, raw_output_path, validated_output_path}) != 3:
        raise ValueError("Prepared, raw response, and Core-validated paths must be distinct")
    if raw_output_path.exists() or validated_output_path.exists():
        raise FileExistsError("Use fresh output paths; the driver never overwrites or resumes model calls")
    _validate_loopback_endpoint(endpoint)
    if fixture_version not in {"v1", "v2", "v3"}:
        raise ValueError("fixture_version must be v1, v2 or v3")
    rows = _read_jsonl(prepared_path)
    if fixture_version == "v3":
        validate_rows(rows, manifest_path=APPRAISAL_V3_MANIFEST_PATH)
    else:
        fixture_path = Path(__file__).resolve().parent / "fixtures" / f"appraisal-live-scenarios-{fixture_version}.json"
        validate_rows(rows, fixture_path=fixture_path)
    expected_count = 38 if fixture_version == "v3" else 32
    if len(rows) != expected_count:
        raise ValueError(f"Appraisal driver requires the complete {expected_count}-case fixture; it does not split or reroll cases")
    case_ids = [row["caseId"] for row in rows]
    if len(case_ids) != len(set(case_ids)):
        raise ValueError("Prepared appraisal case IDs must be unique")
    if project_path is None:
        project_path = Path(__file__).resolve().parent / "NpcFactorEvaluation.csproj"
    project_path = Path(project_path).resolve()
    if core_preflight is None:
        _preflight_core(prepared_path, project_path, fixture_version)
    else:
        core_preflight(prepared_path, project_path)
    raw_output_path.parent.mkdir(parents=True, exist_ok=True)
    validated_output_path.parent.mkdir(parents=True, exist_ok=True)

    raw_results: list[dict] = []
    with raw_output_path.open("x", encoding="utf-8", newline="\n") as stream:
        for row in rows:
            request_text = row["completionRequestBody"]
            request_bytes = request_text.encode("utf-8", errors="strict")
            response = transport(endpoint, request_bytes)
            if not isinstance(response, dict):
                raise TypeError("Transport must return one response record per appraisal request")
            response_body = response.get("body", b"")
            if not isinstance(response_body, (bytes, bytearray)):
                response_body = b""
                transport_error = response.get("error") or "invalid_transport_body_type"
            else:
                response_body = bytes(response_body)
                transport_error = response.get("error")
            response_text, server_metadata, envelope_error = _completion_content(response_body)
            metadata = {
                **server_metadata,
                "httpStatus": response.get("status"),
                "latencyMs": float(response.get("elapsedMs", 0.0)),
                "transportError": transport_error,
                "responseEnvelopeError": envelope_error,
            }
            result = {
                "recordType": "appraisal-model-response-v1",
                "caseId": row["caseId"],
                "requestPromptSha256": row["requestPromptSha256"],
                "rawRequest": request_text,
                "rawRequestSha256": row["completionRequestSha256"],
                "rawRequestBase64": base64.b64encode(request_bytes).decode("ascii"),
                "httpStatus": response.get("status"),
                "transportError": transport_error,
                "responseEnvelopeError": envelope_error,
                "responseText": response_text,
                "rawResponse": response_body.decode("utf-8", errors="replace"),
                "rawResponseBase64": base64.b64encode(response_body).decode("ascii"),
                "elapsedMs": float(response.get("elapsedMs", 0.0)),
                "responseHeaders": response.get("headers", {}),
                "modelMetadata": metadata,
            }
            raw_results.append(result)
            stream.write(json.dumps(result, ensure_ascii=False, separators=(",", ":")) + "\n")
            stream.flush()

    if core_bridge is None:
        _bridge_core(prepared_path, raw_output_path, validated_output_path, project_path, fixture_version)
    else:
        core_bridge(prepared_path, raw_output_path, validated_output_path)
    validated_rows = _read_jsonl(validated_output_path)
    if len(validated_rows) != len(rows) or {row.get("caseId") for row in validated_rows} != set(case_ids):
        raise ValueError("Core must return exactly one validation result for every prepared case")
    if any(not isinstance(row.get("coreValidated"), bool) for row in validated_rows):
        raise ValueError("Core validation results must explicitly report coreValidated for every case")
    accepted = sum(row["coreValidated"] for row in validated_rows)
    return {
        "preparedRows": len(rows),
        "modelCalls": len(raw_results),
        "coreValidatedRows": accepted,
        "coreRejectedRows": len(validated_rows) - accepted,
        "rawResponses": str(raw_output_path),
        "coreValidation": str(validated_output_path),
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="One-pass serial appraisal requests with production Core validation.")
    parser.add_argument("--prepared", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path, help="New raw response evidence JSONL")
    parser.add_argument("--validated-output", type=Path,
                        help="New Core validation JSONL (default: <output>.validated.jsonl)")
    parser.add_argument("--endpoint", default="http://127.0.0.1:8080/completion")
    parser.add_argument("--fixture-version", choices=("v1", "v2", "v3"), default="v1")
    parser.add_argument("--core-project", type=Path,
                        default=Path(__file__).resolve().parent / "NpcFactorEvaluation.csproj")
    args = parser.parse_args(argv)
    validated = args.validated_output or args.output.with_name(args.output.stem + ".validated.jsonl")
    try:
        summary = run_appraisal_evaluation(args.prepared, args.output, validated,
                                           endpoint=args.endpoint, project_path=args.core_project,
                                           fixture_version=args.fixture_version)
    except Exception as error:
        print(f"APPRAISAL_EVALUATION_FAILED: {error}", file=sys.stderr)
        return 1
    print(json.dumps(summary, ensure_ascii=False, separators=(",", ":")))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
