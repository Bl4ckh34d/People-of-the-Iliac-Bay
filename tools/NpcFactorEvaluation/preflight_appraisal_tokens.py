"""Exact local-server token-budget preflight for prepared appraisal prompts.

This tool calls only loopback /health, /props, and /tokenize endpoints. It never
calls a completion endpoint, retries a request, or edits the prepared input.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import ntpath
import os
import sys
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path
from typing import Any, Callable


DEFAULT_ENDPOINT = "http://127.0.0.1:8080/completion"
RESPONSE_TOKENS = 1024
EXPECTED_CONTEXT_TOKENS = 4096
TIMEOUT_SECONDS = 15
LOOPBACK_HOSTS = {"127.0.0.1", "localhost", "::1"}


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def sha256_text(value: str) -> str:
    return sha256_bytes(value.encode("utf-8"))


def _json_bytes(value: Any) -> bytes:
    return json.dumps(value, ensure_ascii=False, separators=(",", ":")).encode("utf-8")


def _is_int(value: Any) -> bool:
    return isinstance(value, int) and not isinstance(value, bool)


def _first_present(record: dict[str, Any], names: tuple[str, ...]) -> tuple[str | None, Any]:
    for name in names:
        if name in record:
            return name, record[name]
    return None, None


def _validate_sha256(value: Any, field: str, expected: str | None = None) -> str:
    if not isinstance(value, str) or len(value) != 64 or any(ch not in "0123456789abcdefABCDEF" for ch in value):
        raise ValueError(f"{field} must be a 64-character SHA-256 hex string")
    normalized = value.lower()
    if expected is not None and normalized != expected:
        raise ValueError(f"{field} does not match the prepared bytes")
    return normalized


def _validate_prepared_row(record: Any, line_number: int, raw_line: str) -> dict[str, Any]:
    if not isinstance(record, dict):
        raise ValueError("prepared row must be a JSON object")

    case_key, case_id = _first_present(record, ("caseId", "requestId", "id"))
    if case_key is None or not isinstance(case_id, str) or not case_id.strip():
        raise ValueError("caseId must be a non-empty string")

    prompt_key, prompt = _first_present(record, ("bodyPrompt", "requestPrompt"))
    if prompt_key is None or not isinstance(prompt, str) or not prompt:
        raise ValueError("bodyPrompt must be a non-empty string")
    prompt_hash_key, prompt_hash = _first_present(record, (
        "bodyPromptSha256", "requestPromptSha256", "promptSha256"))
    actual_prompt_hash = sha256_text(prompt)
    if prompt_hash_key is None:
        raise ValueError("bodyPromptSha256 is required")
    _validate_sha256(prompt_hash, prompt_hash_key, actual_prompt_hash)

    body_key, completion_body = _first_present(record, (
        "completionRequestBody", "completionBody", "body"))
    row_budget_key, row_budget = _first_present(record, ("n_predict", "nPredict", "responseTokens"))
    body_hash = None
    if body_key is not None:
        if not isinstance(completion_body, str):
            raise ValueError(f"{body_key} must contain the exact JSON request body string")
        try:
            parsed_body = json.loads(completion_body)
        except (json.JSONDecodeError, TypeError) as error:
            raise ValueError(f"{body_key} is malformed JSON: {error}") from error
        if not isinstance(parsed_body, dict):
            raise ValueError(f"{body_key} must decode to a JSON object")
        if parsed_body.get("prompt") != prompt:
            raise ValueError(f"{body_key}.prompt differs from bodyPrompt")
        if "n_predict" not in parsed_body or not _is_int(parsed_body["n_predict"]):
            raise ValueError(f"{body_key}.n_predict must be an integer")
        if parsed_body["n_predict"] != RESPONSE_TOKENS:
            raise ValueError(f"{body_key}.n_predict must equal {RESPONSE_TOKENS}")
        if row_budget_key is not None and (not _is_int(row_budget) or row_budget != parsed_body["n_predict"]):
            raise ValueError(f"{row_budget_key} must be integer {RESPONSE_TOKENS} and match request body")
        raw_body = completion_body.encode("utf-8")
        body_hash_key, supplied_body_hash = _first_present(record, (
            "completionRequestSha256", "completionBodySha256", "bodySha256"))
        if body_hash_key is None:
            raise ValueError("completionRequestSha256 is required when completion request body is present")
        body_hash = _validate_sha256(supplied_body_hash, body_hash_key, sha256_bytes(raw_body))
    else:
        if row_budget_key is None or not _is_int(row_budget):
            raise ValueError("n_predict must be an integer")
        if row_budget != RESPONSE_TOKENS:
            raise ValueError(f"n_predict must equal {RESPONSE_TOKENS}")

    if row_budget_key is not None and (not _is_int(row_budget) or row_budget != RESPONSE_TOKENS):
        raise ValueError(f"{row_budget_key} must equal integer {RESPONSE_TOKENS}")

    return {
        "caseId": case_id,
        "lineNumber": line_number,
        "promptField": prompt_key,
        "prompt": prompt,
        "promptSha256": actual_prompt_hash,
        "sourceBodyField": body_key,
        "sourceBodySha256": body_hash,
        "requiredOutputTokens": RESPONSE_TOKENS,
        "rawPreparedLine": raw_line,
    }


def _endpoint_urls(endpoint: str) -> dict[str, str]:
    try:
        parsed = urllib.parse.urlsplit(endpoint)
        hostname = (parsed.hostname or "").lower()
        _ = parsed.port
    except ValueError as error:
        raise ValueError(f"invalid endpoint URL: {error}") from error
    if parsed.scheme != "http" or hostname not in LOOPBACK_HOSTS or parsed.username or parsed.password:
        raise ValueError("endpoint must be unauthenticated HTTP on loopback")
    if not parsed.netloc:
        raise ValueError("endpoint URL has no host")
    root = f"{parsed.scheme}://{parsed.netloc}"
    return {name: root + "/" + name for name in ("health", "props", "tokenize")}


def _post_bytes(url: str, payload: bytes, opener: Callable[..., Any], timeout: int) -> tuple[int | None, bytes, str | None]:
    request = urllib.request.Request(url, data=payload, method="POST",
                                     headers={"Content-Type": "application/json"})
    try:
        with opener(request, timeout=timeout) as response:
            status = getattr(response, "status", None)
            if status is None:
                status = getattr(response, "code", None)
            return status, response.read(), None
    except urllib.error.HTTPError as error:
        try:
            raw = error.read()
        except Exception:
            raw = b""
        return error.code, raw, f"HTTP {error.code}: {error.reason}"
    except Exception as error:  # urllib transport exceptions vary by platform.
        return None, b"", f"{type(error).__name__}: {error}"


def _post_json(url: str, payload: dict[str, Any], opener: Callable[..., Any], timeout: int) -> tuple[int | None, bytes, str | None]:
    return _post_bytes(url, _json_bytes(payload), opener, timeout)


def _get(url: str, opener: Callable[..., Any], timeout: int) -> tuple[int | None, bytes, str | None]:
    request = urllib.request.Request(url, method="GET")
    try:
        with opener(request, timeout=timeout) as response:
            status = getattr(response, "status", None)
            if status is None:
                status = getattr(response, "code", None)
            return status, response.read(), None
    except urllib.error.HTTPError as error:
        try:
            raw = error.read()
        except Exception:
            raw = b""
        return error.code, raw, f"HTTP {error.code}: {error.reason}"
    except Exception as error:
        return None, b"", f"{type(error).__name__}: {error}"


def _decode_json_object(raw: bytes, label: str) -> dict[str, Any]:
    try:
        value = json.loads(raw.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        raise ValueError(f"{label} returned malformed JSON: {error}") from error
    if not isinstance(value, dict):
        raise ValueError(f"{label} response must be a JSON object")
    return value


def _normalized_windows_path(value: str) -> str:
    return ntpath.normcase(ntpath.normpath(value.strip()))


def _server_preflight(endpoint: str, expected_model: str, opener: Callable[..., Any], timeout: int) -> dict[str, Any]:
    metadata: dict[str, Any] = {
        "endpoint": endpoint,
        "expectedModelPath": expected_model,
        "healthStatus": None,
        "healthRawResponse": None,
        "healthResponseSha256": None,
        "propsStatus": None,
        "propsRawResponse": None,
        "propsResponseSha256": None,
        "actualModelPath": None,
        "serverContextTokens": None,
        "errors": [],
    }
    try:
        urls = _endpoint_urls(endpoint)
    except ValueError as error:
        metadata["errors"].append(str(error))
        return metadata

    if not (ntpath.isabs(expected_model) or os.path.isabs(expected_model)):
        metadata["errors"].append("expected model path must be absolute")
        return metadata

    status, raw, transport_error = _get(urls["health"], opener, timeout)
    metadata["healthStatus"] = status
    metadata["healthRawResponse"] = raw.decode("utf-8", errors="replace")
    metadata["healthResponseSha256"] = sha256_bytes(raw)
    if transport_error:
        metadata["errors"].append("health endpoint: " + transport_error)
    elif status != 200:
        metadata["errors"].append(f"health endpoint returned HTTP {status}")
    else:
        try:
            health = _decode_json_object(raw, "health")
            if health.get("status") != "ok":
                metadata["errors"].append("health response status must equal 'ok'")
        except ValueError as error:
            metadata["errors"].append(str(error))

    status, raw, transport_error = _get(urls["props"], opener, timeout)
    metadata["propsStatus"] = status
    metadata["propsRawResponse"] = raw.decode("utf-8", errors="replace")
    metadata["propsResponseSha256"] = sha256_bytes(raw)
    if transport_error:
        metadata["errors"].append("props endpoint: " + transport_error)
        return metadata
    if status != 200:
        metadata["errors"].append(f"props endpoint returned HTTP {status}")
        return metadata
    try:
        props = _decode_json_object(raw, "props")
    except ValueError as error:
        metadata["errors"].append(str(error))
        return metadata

    settings = props.get("default_generation_settings")
    if not isinstance(settings, dict):
        metadata["errors"].append("props.default_generation_settings must be a JSON object")
    actual_model = props.get("model_path")
    if not isinstance(actual_model, str) or not actual_model:
        metadata["errors"].append("props.model_path must be a non-empty string")
    else:
        metadata["actualModelPath"] = actual_model
        if _normalized_windows_path(actual_model) != _normalized_windows_path(expected_model):
            metadata["errors"].append("props.model_path does not match the pinned expected model path")

    context = props.get("n_ctx")
    if context is None and isinstance(settings, dict):
        context = settings.get("n_ctx")
    if not _is_int(context) or context <= 0:
        metadata["errors"].append("props.n_ctx must be a positive integer")
    else:
        metadata["serverContextTokens"] = context
        if context != EXPECTED_CONTEXT_TOKENS:
            metadata["errors"].append(f"props.n_ctx must equal pinned {EXPECTED_CONTEXT_TOKENS}")
    return metadata


def _production_tokenize_body(prompt: str) -> bytes:
    """Mirror LocalDialogueCompletion.EscapeJson used by LocalDialogueClient."""
    escaped: list[str] = []
    for character in prompt:
        code = ord(character)
        if character == "\\":
            escaped.append("\\\\")
        elif character == '"':
            escaped.append("\\\"")
        elif character == "\n":
            escaped.append("\\n")
        elif character == "\r":
            escaped.append("\\r")
        elif character == "\t":
            escaped.append("\\t")
        elif code < 32:
            escaped.append(f"\\u{code:04X}")
        else:
            escaped.append(character)
    return ("{\"content\":\"" + "".join(escaped) + "\",\"add_special\":true}").encode("utf-8")


def _parse_tokenizer_response(status: int | None, raw: bytes, transport_error: str | None) -> tuple[int | None, str | None]:
    if transport_error:
        return None, transport_error
    if status != 200:
        return None, f"tokenize endpoint returned HTTP {status}"
    try:
        result = _decode_json_object(raw, "tokenize")
    except ValueError as error:
        return None, str(error)
    tokens = result.get("tokens")
    if not isinstance(tokens, list):
        return None, "tokenize.tokens must be an array"
    if any(not _is_int(token) for token in tokens):
        return None, "tokenize.tokens must contain only integers"
    return len(tokens), None


def preflight_bytes(prepared_bytes: bytes, expected_model: str, endpoint: str = DEFAULT_ENDPOINT,
                    opener: Callable[..., Any] = urllib.request.urlopen,
                    timeout: int = TIMEOUT_SECONDS) -> list[dict[str, Any]]:
    """Preflight JSONL bytes; retain one result row for every source line."""
    prepared_hash = sha256_bytes(prepared_bytes)
    text = prepared_bytes.decode("utf-8", errors="replace")
    source_lines = text.splitlines()
    parsed_rows: list[tuple[str, dict[str, Any] | None, str | None]] = []
    for index, raw_line in enumerate(source_lines, start=1):
        if not raw_line.strip():
            continue
        try:
            record = json.loads(raw_line)
            normalized = _validate_prepared_row(record, index, raw_line)
            parsed_rows.append((raw_line, normalized, None))
        except (json.JSONDecodeError, ValueError, TypeError) as error:
            case_id = None
            try:
                candidate = json.loads(raw_line)
                if isinstance(candidate, dict):
                    case_id = candidate.get("caseId") or candidate.get("requestId") or candidate.get("id")
            except (json.JSONDecodeError, TypeError):
                pass
            parsed_rows.append((raw_line, {"caseId": case_id, "lineNumber": index,
                                           "rawPreparedLine": raw_line}, str(error)))

    metadata = _server_preflight(endpoint, expected_model, opener, timeout)
    report: list[dict[str, Any]] = [{
        "recordType": "appraisal-token-preflight-run",
        "preparedSha256": prepared_hash,
        "preparedByteCount": len(prepared_bytes),
        "inputLineCount": len(parsed_rows),
        "requiredOutputTokens": RESPONSE_TOKENS,
        "maximumPromptTokens": EXPECTED_CONTEXT_TOKENS - RESPONSE_TOKENS,
        "expectedServerContextTokens": EXPECTED_CONTEXT_TOKENS,
        "server": metadata,
    }]
    tokenized_count = 0
    fit_count = 0
    error_count = 0
    try:
        urls = _endpoint_urls(endpoint)
    except ValueError:
        urls = {}

    for _raw_line, item, row_error in parsed_rows:
        row: dict[str, Any] = {
            "recordType": "appraisal-token-preflight-case",
            "caseId": item.get("caseId"),
            "lineNumber": item["lineNumber"],
            "promptSha256": None,
            "sourceBodySha256": None,
            "requiredOutputTokens": RESPONSE_TOKENS,
            "serverContextTokens": metadata.get("serverContextTokens"),
            "tokenCount": None,
            "totalRequiredTokens": None,
            "tokenizeRequest": None,
            "tokenizeRequestSha256": None,
            "tokenizeResponse": None,
            "tokenizeResponseSha256": None,
            "errors": [],
        }
        if item.get("promptSha256"):
            row["promptSha256"] = item["promptSha256"]
        if item.get("sourceBodySha256"):
            row["sourceBodySha256"] = item["sourceBodySha256"]
        if row_error:
            row["status"] = "invalid_prepared_row"
            row["errors"].append(row_error)
            error_count += 1
        elif metadata["errors"]:
            row["status"] = "server_preflight_failed"
            row["errors"].extend(metadata["errors"])
            error_count += 1
        else:
            request_bytes = _production_tokenize_body(item["prompt"])
            row["tokenizeRequest"] = request_bytes.decode("utf-8")
            row["tokenizeRequestSha256"] = sha256_bytes(request_bytes)
            status, response_bytes, transport_error = _post_bytes(
                urls["tokenize"], request_bytes, opener, timeout)
            row["tokenizeResponse"] = response_bytes.decode("utf-8", errors="replace")
            row["tokenizeResponseSha256"] = sha256_bytes(response_bytes)
            token_count, error = _parse_tokenizer_response(status, response_bytes, transport_error)
            if error:
                row["status"] = "tokenizer_error"
                row["errors"].append(error)
                error_count += 1
            else:
                tokenized_count += 1
                row["tokenCount"] = token_count
                row["totalRequiredTokens"] = token_count + RESPONSE_TOKENS
                if token_count + RESPONSE_TOKENS <= metadata["serverContextTokens"]:
                    row["status"] = "fit"
                    fit_count += 1
                else:
                    row["status"] = "over_context_budget"
                    row["errors"].append(
                        f"prompt tokens plus {RESPONSE_TOKENS}-token output exceed server context "
                        f"{metadata['serverContextTokens']}")
                    error_count += 1
        report.append(row)

    report.append({
        "recordType": "appraisal-token-preflight-summary",
        "preparedSha256": prepared_hash,
        "inputLineCount": len(parsed_rows),
        "tokenizedCount": tokenized_count,
        "fitCount": fit_count,
        "errorCount": error_count,
        "allFit": bool(parsed_rows) and error_count == 0 and fit_count == len(parsed_rows),
    })
    return report


def write_report(path: str | os.PathLike[str], report: list[dict[str, Any]]) -> None:
    destination = Path(path)
    destination.parent.mkdir(parents=True, exist_ok=True)
    with destination.open("x", encoding="utf-8", newline="\n") as stream:
        for row in report:
            stream.write(json.dumps(row, ensure_ascii=False, separators=(",", ":")) + "\n")


def _load_prepared(path: Path) -> bytes:
    with path.open("rb") as stream:
        return stream.read()


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--prepared", required=True, type=Path, help="Prepared appraisal JSONL input")
    parser.add_argument("--output", required=True, type=Path, help="Fresh JSONL report path; existing files are refused")
    parser.add_argument("--expect-model", required=True, help="Exact absolute local GGUF model path pinned for this preflight")
    parser.add_argument("--endpoint", default=DEFAULT_ENDPOINT, help="Loopback llama.cpp completion endpoint")
    parser.add_argument("--timeout", type=int, default=TIMEOUT_SECONDS)
    args = parser.parse_args(argv)
    if args.timeout < 1 or args.timeout > 120:
        parser.error("--timeout must be between 1 and 120 seconds")
    try:
        source = _load_prepared(args.prepared)
        report = preflight_bytes(source, args.expect_model, args.endpoint, timeout=args.timeout)
        write_report(args.output, report)
    except FileExistsError:
        print(f"Refusing to overwrite report: {args.output}", file=sys.stderr)
        return 2
    except OSError as error:
        print(f"Preflight input/output error: {error}", file=sys.stderr)
        return 2

    summary = report[-1]
    print(json.dumps(summary, ensure_ascii=False))
    return 0 if summary["allFit"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
