"""Paired selector substitution diagnostic; not the production Qwen-only route.

The legacy serial runner's D3 evidence slot contains canonical wire envelopes.
For the Qwen arm these envelopes are adapter output, never raw model output.
Actual selector bytes are preserved separately before any conversion.
"""

from __future__ import annotations

import argparse
import base64
import hashlib
import json
import sys
from pathlib import Path

from run_serial_evaluation import (
    _json_lines, _validate_episode_limit, post_json_bytes,
    run_serial_evaluation, validate_prepared_row,
)


def run_comparison(prepared_path, output_directory, qwen_model, *, execute=False,
                   d3_url="http://127.0.0.1:8093/v1/systemone",
                   qwen_url="http://127.0.0.1:8080/completion",
                   qwen_choice_url="http://127.0.0.1:8080/v1/chat/completions",
                   limit=12, transport=post_json_bytes, core_bridge=None):
    source = Path(prepared_path).resolve()
    output = Path(output_directory).resolve()
    if output.exists():
        raise FileExistsError(f"Fresh comparison directory required: {output}")
    source_bytes = source.read_bytes()
    rows = _validate_episode_limit(_json_lines(source), limit)
    if not rows:
        raise ValueError("No prepared rows")
    for row in rows:
        validate_prepared_row(row)
    if len({row["requestId"] for row in rows}) != len(rows):
        raise ValueError("Duplicate prepared request IDs")
    if not isinstance(qwen_model, str) or not qwen_model.strip():
        raise ValueError("Explicit Qwen model identity required")
    selected_bytes = ("".join(json.dumps(row, ensure_ascii=False, separators=(",", ":")) + "\n" for row in rows)).encode("utf-8")
    digest = hashlib.sha256(selected_bytes).hexdigest()
    manifest = {
        "schemaVersion": 1, "mode": "preflight", "preparedRows": len(rows),
        "preparedSha256": digest, "sourcePreparedSha256": hashlib.sha256(source_bytes).hexdigest(),
        "qwenModel": qwen_model,
        "scope": "fixed_snapshot_selector_substitution_not_production_qwen_only_or_native_actions",
        "armOrder": ["d3-selector", "qwen-selector"], "arms": [],
        "semanticReview": "pending; no automatic prose score",
    }
    if not execute:
        return manifest
    from qwen_choice_adapter import build_qwen_choice_request, convert_qwen_choice_response

    output.mkdir(parents=True, exist_ok=False)
    snapshot = output / "prepared.jsonl"
    snapshot.write_bytes(selected_bytes)
    manifest["mode"] = "running"
    manifest_path = output / "manifest.json"

    def save_manifest():
        manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

    save_manifest()
    try:
        for arm in manifest["armOrder"]:
            raw_path = output / f"{arm}.raw.jsonl"
            with raw_path.open("x", encoding="utf-8", newline="\n") as raw_stream:
                def arm_transport(url, body):
                    if url != d3_url:
                        return transport(url, body)
                    actual_body = build_qwen_choice_request(body, qwen_model) if arm == "qwen-selector" else body
                    actual_url = qwen_choice_url if arm == "qwen-selector" else d3_url
                    response = transport(actual_url, actual_body)
                    raw_body = response.get("body", b"")
                    if not isinstance(raw_body, bytes):
                        raise TypeError("Transport response body must be bytes")
                    record = {
                        "selector": arm, "url": actual_url,
                        "inputD3RequestSha256": hashlib.sha256(body).hexdigest(),
                        "rawRequest": actual_body.decode("utf-8"),
                        "rawResponse": raw_body.decode("utf-8", errors="replace"),
                        "rawResponseBase64": base64.b64encode(raw_body).decode("ascii"),
                        "httpStatus": response.get("status"), "elapsedMs": response.get("elapsedMs"),
                        "transportError": response.get("error"), "conversionError": None,
                        "canonicalDecisionResponse": None,
                        "probabilityProvenance": "adapter_one_hot_not_model_confidence" if arm == "qwen-selector" else "model_response_unvalidated",
                    }
                    # Persist actual response before conversion, including malformed bytes.
                    raw_stream.write(json.dumps({**record, "phase": "raw"}, ensure_ascii=False) + "\n")
                    raw_stream.flush()
                    if arm == "qwen-selector" and response.get("status") == 200:
                        try:
                            canonical = convert_qwen_choice_response(body, raw_body, expected_model=qwen_model)
                            record["canonicalDecisionResponse"] = canonical.decode("utf-8")
                            response = {**response, "body": canonical}
                        except (ValueError, TypeError, KeyError) as error:
                            record["conversionError"] = f"{type(error).__name__}: {error}"
                            response = {**response, "status": 502, "body": b"{}",
                                        "error": "selector_conversion_failed: " + str(error)}
                    elif arm == "d3-selector":
                        # Retain original D3 bytes; only the later Core bridge validates them.
                        record["wireResponseIsUnvalidated"] = True
                    raw_stream.write(json.dumps({**record, "phase": "converted"}, ensure_ascii=False) + "\n")
                    raw_stream.flush()
                    return response

                summary = run_serial_evaluation(snapshot, output / f"{arm}.jsonl", d3_url, qwen_url,
                                                transport=arm_transport, core_bridge=core_bridge, limit=limit)
            manifest["arms"].append({"id": arm, "preparedSha256": digest, "summary": summary,
                                     "selectorRawEvidence": str(raw_path)})
            save_manifest()
        manifest["mode"] = "executed"
        save_manifest()
        return manifest
    except Exception as error:
        manifest["mode"] = "failed"
        manifest["error"] = f"{type(error).__name__}: {error}"
        save_manifest()
        raise


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--prepared", required=True, type=Path)
    parser.add_argument("--output-directory", required=True, type=Path)
    parser.add_argument("--qwen-model", required=True)
    parser.add_argument("--limit", default=12, type=int)
    parser.add_argument("--execute", action="store_true")
    args = parser.parse_args(argv)
    try:
        result = run_comparison(args.prepared, args.output_directory, args.qwen_model,
                                limit=args.limit, execute=args.execute)
        print(json.dumps(result, ensure_ascii=False))
        return 1 if any(arm["summary"]["failedRows"] for arm in result["arms"]) else 0
    except Exception as error:
        print(f"SELECTOR_COMPARISON_FAILED: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
