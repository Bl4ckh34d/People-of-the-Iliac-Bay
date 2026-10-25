"""Frozen loopback D3 image-choice test; not NPC-eye or combat evaluation."""
from __future__ import annotations

import argparse
import base64
import hashlib
import json
import mimetypes
import sys
from datetime import datetime, timezone
from pathlib import Path
from urllib.error import HTTPError
from urllib.parse import urlsplit
from urllib.request import HTTPRedirectHandler, Request, build_opener

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[1]
sys.path.insert(0, str(ROOT))

from model_client import post_json, _validate_loopback_http  # noqa: E402
from run_evaluation import game_closed  # noqa: E402

VALID_IMAGE_MIMES = {"image/png", "image/jpeg", "image/webp", "image/gif", "image/bmp"}


class _NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def _parse_args(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--suite", required=True, help="JSON suite path; relative paths resolve from repository root")
    parser.add_argument("--run-dir", required=True, help="new, non-existing run directory; relative paths resolve from repository root")
    parser.add_argument("--d3-url", required=True, help="unauthenticated loopback HTTP base URL for D3 /v1/systemone")
    parser.add_argument("--timeout", type=float, default=90, help="per-image request timeout in seconds (default: 90)")
    return parser.parse_args(argv)


def _loopback_base(url):
    _validate_loopback_http(url)
    return url.rstrip("/")


def _get_json(url):
    _validate_loopback_http(url)
    request = Request(url, headers={"Accept": "application/json"})
    try:
        with build_opener(_NoRedirect).open(request, timeout=5) as response:
            data = response.read()
    except HTTPError as exc:
        data = exc.read()
        status = exc.code
        exc.close()
        raise RuntimeError(f"Metadata request failed: HTTP {status}: {data[:400]!r}") from exc
    try:
        return json.loads(data.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise RuntimeError("Model metadata response was not UTF-8 JSON") from exc


def _model_id(models):
    entries = models.get("data") if isinstance(models, dict) else None
    if not isinstance(entries, list) or not entries or not isinstance(entries[0], dict):
        raise ValueError("/v1/models returned no model entry")
    model_id = entries[0].get("id")
    if not isinstance(model_id, str) or not model_id:
        raise ValueError("/v1/models returned no model id")
    return model_id


def load_visual_suite(suite_arg):
    suite_path = Path(suite_arg)
    if not suite_path.is_absolute():
        suite_path = REPO / suite_path
    suite_path = suite_path.resolve()
    suite = json.loads(suite_path.read_text(encoding="utf-8-sig"))
    cases = suite.get("cases") if isinstance(suite, dict) else None
    if not isinstance(cases, list) or not cases:
        raise ValueError("suite must contain a non-empty cases list")
    seen_ids = set()
    prepared = []
    for index, case in enumerate(cases):
        where = f"cases[{index}]"
        if not isinstance(case, dict):
            raise ValueError(f"{where} must be an object")
        case_id = case.get("id")
        if not isinstance(case_id, str) or not case_id or case_id in seen_ids:
            raise ValueError(f"{where} has a missing or duplicate id")
        seen_ids.add(case_id)
        for field in ("state", "question", "provenance"):
            if not isinstance(case.get(field), str) or not case[field].strip():
                raise ValueError(f"{where}.{field} must be non-empty text")
        image_path = case.get("imagePath")
        if not isinstance(image_path, str) or not image_path:
            raise ValueError(f"{where}.imagePath must be a suite-relative path")
        relative = Path(image_path)
        if relative.is_absolute():
            raise ValueError(f"{where}.imagePath must be relative to the suite file")
        resolved_image = (suite_path.parent / relative).resolve()
        if not resolved_image.is_file():
            raise FileNotFoundError(f"Image for {case_id!r} was not found: {resolved_image}")
        image_bytes = resolved_image.read_bytes()
        if not image_bytes:
            raise ValueError(f"Image for {case_id!r} is empty")
        mime = mimetypes.guess_type(resolved_image.name)[0]
        if mime not in VALID_IMAGE_MIMES:
            raise ValueError(f"Unsupported image type for {case_id!r}: {mime or 'unknown'}")
        criteria = case.get("criteria")
        if not isinstance(criteria, dict) or len(criteria) < 2:
            raise ValueError(f"{where}.criteria must have at least two options")
        if any(not isinstance(key, str) or not key or not isinstance(value, str) or not value.strip()
               for key, value in criteria.items()):
            raise ValueError(f"{where}.criteria must map non-empty ids to descriptions")
        expected = case.get("expected")
        if not isinstance(expected, list) or not expected or any(not isinstance(item, str) for item in expected):
            raise ValueError(f"{where}.expected must be a non-empty list of option ids")
        if len(expected) != len(set(expected)) or not set(expected) <= set(criteria):
            raise ValueError(f"{where}.expected contains duplicate or unoffered option ids")
        prepared.append({
            "id": case_id,
            "state": case["state"],
            "question": case["question"],
            "provenance": case["provenance"],
            "expected": list(expected),
            "criteria": list(criteria.items()),
            "image_path": resolved_image,
            "image_bytes": image_bytes,
            "mime": mime,
        })
    return suite_path, prepared


def _payload(case, reverse):
    criteria = list(case["criteria"])
    if reverse:
        criteria.reverse()
    encoded = base64.b64encode(case["image_bytes"]).decode("ascii")
    return {
        "state": case["state"],
        "images": [f"data:{case['mime']};base64,{encoded}"],
        "questions": {
            "decision": {
                "type": "choice",
                "instructions": case["question"],
                "criteria": dict(criteria),
            }
        },
    }


def _read_choice(response):
    if response.get("transport_error"):
        return None, "transport_error"
    if response.get("status_code") != 200:
        return None, f"http_{response.get('status_code')}"
    if response.get("malformed_json"):
        return None, "malformed_json"
    try:
        choice = response["response_json"]["answers"]["decision"]["choice"]
    except (KeyError, TypeError):
        return None, "malformed_choice_response"
    if not isinstance(choice, str) or not choice:
        return None, "malformed_choice_response"
    return choice, None


def run(argv=None):
    args = _parse_args(argv)
    game_closed()
    base_url = _loopback_base(args.d3_url)
    suite_path, cases = load_visual_suite(args.suite)
    model_metadata = _get_json(base_url + "/v1/models")
    model_id = _model_id(model_metadata)
    suite_bytes = suite_path.read_bytes()
    frozen = {
        str(suite_path): hashlib.sha256(suite_bytes).hexdigest(),
        str(Path(__file__).resolve()): hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
        str((ROOT / "model_client.py").resolve()): hashlib.sha256((ROOT / "model_client.py").read_bytes()).hexdigest(),
    }
    images = []
    for case in cases:
        digest = hashlib.sha256(case["image_bytes"]).hexdigest()
        frozen[str(case["image_path"])] = digest
        images.append({"id": case["id"], "path": str(case["image_path"]), "sha256": digest,
                       "bytes": len(case["image_bytes"]), "mime": case["mime"],
                       "expected": case["expected"], "provenance": case["provenance"]})
    run_dir = Path(args.run_dir)
    if not run_dir.is_absolute():
        run_dir = REPO / run_dir
    run_dir = run_dir.resolve()
    run_dir.mkdir(parents=True, exist_ok=False)
    manifest = {
        "status": "FROZEN_BEFORE_INFERENCE",
        "frozenUtc": datetime.now(timezone.utc).isoformat(),
        "scope": "Static image-option choice smoke; not NPC-eye, combat, or game-state perception validation.",
        "modelId": model_id,
        "models": model_metadata,
        "suite": str(suite_path),
        "suiteSha256": frozen[str(suite_path)],
        "files": frozen,
        "images": images,
        "cases": len(cases),
        "callsPlanned": len(cases) * 2,
        "protocol": "D3 /v1/systemone native choice: state, images data URLs, questions.decision(type=choice,instructions,criteria); one question per call; original and reversed criterion order.",
    }
    (run_dir / "manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    rows = []
    for case in cases:
        for reverse in (False, True):
            game_closed()
            if any(hashlib.sha256(Path(path).read_bytes()).hexdigest() != digest for path, digest in frozen.items()):
                raise RuntimeError("Frozen visual suite, image, or runner changed during run")
            arm = "reversed" if reverse else "original"
            response = post_json(
                base_url + "/v1/systemone",
                _payload(case, reverse),
                run_dir / "attempts.jsonl",
                {"caseId": case["id"], "arm": arm, "modelId": model_id},
                timeout=args.timeout,
            )
            choice, error = _read_choice(response)
            offered = dict(case["criteria"])
            row = {
                "id": case["id"], "arm": arm, "choice": choice,
                "technicalError": error,
                "unexpectedChoice": choice is not None and choice not in offered,
                "correct": choice in case["expected"] if choice in offered else False,
                "expected": case["expected"], "criteria": offered,
                "status": response["status_code"], "latencyMs": response["latency_ms"],
                "transportError": response["transport_error"],
                "responseDecodeError": response["response_decode_error"],
                "malformedJson": response["malformed_json"],
                "responseRaw": response["response_raw"], "responseJson": response["response_json"],
            }
            rows.append(row)
            with (run_dir / "results.jsonl").open("a", encoding="utf-8") as stream:
                stream.write(json.dumps(row, ensure_ascii=False) + "\n")
            print(json.dumps({k: v for k, v in row.items() if k not in {"responseRaw", "responseJson"}}, ensure_ascii=False), flush=True)
    valid = [row for row in rows if not row["technicalError"]]
    original = {row["id"]: row for row in valid if row["arm"] == "original"}
    reversed_rows = {row["id"]: row for row in valid if row["arm"] == "reversed"}
    pairs = [(original[key], reversed_rows[key]) for key in original.keys() & reversed_rows.keys()]
    summary = {
        "plannedCalls": len(cases) * 2,
        "attempts": len(rows),
        "technicalFailures": len(rows) - len(valid),
        "validChoices": len(valid),
        "unexpectedChoices": sum(row["unexpectedChoice"] for row in rows),
        "correctChoices": sum(row["correct"] for row in valid),
        "accuracyAmongValidChoices": sum(row["correct"] for row in valid) / len(valid) if valid else None,
        "accuracyAllCalls": sum(row["correct"] for row in rows) / len(rows) if rows else None,
        "orderChanges": sum(a["choice"] != b["choice"] for a, b in pairs),
        "pairedCases": len(pairs),
        "latencyP50Ms": sorted(row["latencyMs"] for row in rows)[len(rows) // 2] if rows else None,
    }
    (run_dir / "summary.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
    print(json.dumps(summary, ensure_ascii=False), flush=True)


if __name__ == "__main__":
    run()
