"""Small, single-attempt JSON client for offline loopback model probes."""

from __future__ import annotations

import base64
import hashlib
import ipaddress
import json
import os
import time
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Mapping
from urllib.error import HTTPError, URLError
from urllib.parse import urlsplit
from urllib.request import HTTPRedirectHandler, Request, build_opener


def _secret_key(key: str) -> bool:
    parts = key.lower().replace('-', '_').split('_')
    if parts == ['token']:
        return True
    sensitive = {'apikey', 'authorization', 'password', 'secret', 'credential',
                 'accesstoken', 'refreshtoken', 'authtoken', 'bearertoken'}
    return any(part in sensitive for part in parts) or any(a+b in sensitive for a,b in zip(parts,parts[1:]))


class _NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def _compact_json_bytes(value: Any) -> bytes:
    # Preserve insertion order: option order can affect decision-model output.
    return json.dumps(value, ensure_ascii=False, sort_keys=False, separators=(",", ":"), allow_nan=False).encode("utf-8")


def _redact(value: Any) -> Any:
    if isinstance(value, Mapping):
        return {
            str(key): "[REDACTED]" if _secret_key(str(key)) else _redact(item)
            for key, item in value.items()
        }
    if isinstance(value, (list, tuple)):
        return [_redact(item) for item in value]
    return value


def _validate_loopback_http(url: str) -> None:
    try:
        parts = urlsplit(url)
        host = parts.hostname
        # Accessing port validates malformed and out-of-range port syntax.
        _ = parts.port
    except ValueError as exc:
        raise ValueError(f"invalid loopback URL: {exc}") from exc

    if parts.scheme.lower() != "http" or not host or parts.username or parts.password:
        raise ValueError("URL must be unauthenticated loopback HTTP")
    if host.lower() == "localhost":
        return
    try:
        address = ipaddress.ip_address(host)
    except ValueError as exc:
        raise ValueError("URL host must be localhost or a loopback IP address") from exc
    if not address.is_loopback:
        raise ValueError("URL host must be localhost or a loopback IP address")


def _append_jsonl(path: str | os.PathLike[str], record: Mapping[str, Any]) -> None:
    encoded = _compact_json_bytes(record) + b"\n"
    with open(path, "ab") as stream:
        stream.write(encoded)
        stream.flush()
        os.fsync(stream.fileno())


def post_json(
    url: str,
    payload: Any,
    ledger_path: str | os.PathLike[str],
    metadata: Mapping[str, Any],
    timeout: float = 60,
) -> dict[str, Any]:
    """POST one compact UTF-8 JSON request and durably ledger attempt/result.

    HTTP errors and transport errors are returned as result data. This function
    never retries. Secret-like mapping keys are redacted in ledger payload and
    metadata; request_sha256 always identifies the exact bytes sent.
    """
    _validate_loopback_http(url)
    request_body = _compact_json_bytes(payload)
    request_sha256 = hashlib.sha256(request_body).hexdigest()
    started_utc = datetime.now(timezone.utc).isoformat()
    attempt = {
        "kind": "attempt",
        "started_utc": started_utc,
        "url": url,
        "request_sha256": request_sha256,
        "payload": _redact(payload),
        "metadata": _redact(metadata),
        "timeout_seconds": timeout,
    }
    _append_jsonl(ledger_path, attempt)

    request = Request(
        url,
        data=request_body,
        headers={"Content-Type": "application/json; charset=utf-8", "Accept": "application/json"},
        method="POST",
    )
    clock_start = time.monotonic()
    status_code: int | None = None
    response_bytes: bytes | None = None
    transport_error: str | None = None
    try:
        with build_opener(_NoRedirect).open(request, timeout=timeout) as response:
            status_code = int(response.status)
            response_bytes = response.read()
    except HTTPError as exc:
        status_code = int(exc.code)
        response_bytes = exc.read()
        exc.close()
    except (URLError, TimeoutError, OSError) as exc:
        transport_error = str(exc)
    latency_ms = round((time.monotonic() - clock_start) * 1000, 3)

    response_raw: str | None = None
    response_json: Any = None
    response_sha256: str | None = None
    response_base64: str | None = None
    response_decode_error = False
    malformed_json = False
    if response_bytes is not None:
        response_sha256 = hashlib.sha256(response_bytes).hexdigest()
        response_base64 = base64.b64encode(response_bytes).decode("ascii")
        try:
            response_raw = response_bytes.decode("utf-8")
            try:
                response_json = json.loads(response_raw)
            except json.JSONDecodeError:
                malformed_json = True
        except UnicodeDecodeError:
            response_raw = response_bytes.decode("utf-8", errors="replace")
            response_decode_error = True
            malformed_json = True

    result = {
        "status_code": status_code,
        "latency_ms": latency_ms,
        "request_sha256": request_sha256,
        "response_sha256": response_sha256,
        "response_raw": response_raw,
        "response_base64": response_base64,
        "response_json": response_json,
        "response_decode_error": response_decode_error,
        "malformed_json": malformed_json,
        "transport_error": transport_error,
        "url": url,
    }
    _append_jsonl(ledger_path, {"kind": "result", **result})
    return result
