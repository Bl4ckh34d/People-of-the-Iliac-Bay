"""Run frozen goal suites through event-first D3 routing and Qwen interpretation.

This development harness validates proposals. It does not measure willingness,
dispatch actions, or claim world effects.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import subprocess
import sys
import time
from datetime import datetime, timezone
from pathlib import Path
from urllib.error import HTTPError
from urllib.request import HTTPRedirectHandler, Request, build_opener

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[1]
CORE_DIR = ROOT / "CoreBridge" / "bin" / "Debug" / "net8.0"
COREBRIDGE_DLL = CORE_DIR / "CoreBridge.dll"
CORE_DLL = CORE_DIR / "PeopleOfTheIliacBay.Core.dll"
sys.path.insert(0, str(ROOT))

from goal_protocol import INTENTS, ROUTES, event_first_flow, interpretation_payload, route_payload, validate_extraction  # noqa: E402
from model_client import _validate_loopback_http, post_json  # noqa: E402
from run_evaluation import game_closed  # noqa: E402


DEFAULT_D3_URL = "http://127.0.0.1:8093"
DEFAULT_QWEN_URL = "http://127.0.0.1:8080"
DEFAULT_TIMEOUT = 60.0
DEFAULT_CORE_TIMEOUT = 120.0


class _NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def cases_sha256(cases):
    encoded = json.dumps(cases, ensure_ascii=False, separators=(",", ":"), allow_nan=False).encode("utf-8")
    return hashlib.sha256(encoded).hexdigest()


def _parse_args(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--run-dir", required=True, help="new, non-existing output directory; relative paths resolve from repository root")
    parser.add_argument("--suite", action="append", nargs=2, required=True, metavar=("JSON_PATH", "CASES_SHA256"),
                        help="frozen suite path and expected casesSha256; repeat for each suite")
    parser.add_argument("--d3-url", default=DEFAULT_D3_URL, help=f"loopback D3 base URL (default: {DEFAULT_D3_URL})")
    parser.add_argument("--qwen-url", default=DEFAULT_QWEN_URL, help=f"loopback Qwen base URL (default: {DEFAULT_QWEN_URL})")
    parser.add_argument("--timeout", type=float, default=DEFAULT_TIMEOUT, help="single-attempt inference timeout in seconds")
    parser.add_argument("--core-timeout", type=float, default=DEFAULT_CORE_TIMEOUT, help="CoreBridge timeout in seconds")
    return parser.parse_args(argv)


def _resolve_repo_path(value):
    path = Path(value)
    return (path if path.is_absolute() else REPO / path).resolve()


def _load_suites(specs):
    cases, paths, declared_hashes = [], [], []
    seen_ids = set()
    for path_text, supplied_hash in specs:
        path = _resolve_repo_path(path_text)
        if not path.is_file():
            raise FileNotFoundError(f"Required suite is missing: {path}")
        if len(supplied_hash) != 64 or any(char not in "0123456789abcdefABCDEF" for char in supplied_hash):
            raise ValueError(f"Suite casesSha256 must be 64 hexadecimal characters: {path_text}")
        suite = json.loads(path.read_text(encoding="utf-8-sig"))
        if not isinstance(suite, dict) or suite.get("schemaVersion") != 1 or not isinstance(suite.get("cases"), list) or not suite["cases"]:
            raise ValueError(f"Suite must contain a non-empty cases array: {path}")
        digest = cases_sha256(suite["cases"])
        declared = suite.get("casesSha256")
        if digest.lower() != supplied_hash.lower() or digest.lower() != str(declared).lower():
            raise ValueError(f"casesSha256 mismatch for suite: {path}")
        if suite.get("caseCount") != len(suite["cases"]):
            raise ValueError(f"caseCount mismatch for suite: {path}")
        for case in suite["cases"]:
            if not isinstance(case, dict) or not isinstance(case.get("id"), str) or not case["id"]:
                raise ValueError(f"Every case needs a non-empty id: {path}")
            if case["id"] in seen_ids:
                raise ValueError(f"Duplicate case id across suites: {case['id']}")
            if not isinstance(case.get("playerText"), str) or not isinstance(case.get("snapshot"), dict):
                raise ValueError(f"Case needs playerText and snapshot: {case['id']}")
            expected = case.get("expected")
            if (not isinstance(expected, dict) or expected.get("route") not in ROUTES or expected.get("intent") not in INTENTS or
                    not isinstance(expected.get("steps", []), list) or not isinstance(expected.get("outcome"), str) or
                    any(not isinstance(step, dict) for step in expected.get("steps", []))):
                raise ValueError(f"Case has invalid scoring gold: {case['id']}")
            # Exercise candidate binding and JSON serialization before any service contact.
            route_payload(case)
            interpretation_payload(case, "preflight")
            seen_ids.add(case["id"])
            cases.append({"suite": path.stem, "case": case})
        paths.append(path)
        declared_hashes.append({"path": str(path), "casesSha256": digest})
    return paths, cases, declared_hashes


def _get_json(url):
    _validate_loopback_http(url)
    request = Request(url, headers={"Accept": "application/json"})
    try:
        with build_opener(_NoRedirect).open(request, timeout=5) as response:
            body = response.read()
    except HTTPError as exc:
        body = exc.read()
        exc.close()
        raise RuntimeError(f"Metadata request failed: HTTP {exc.code} at {url}: {body[:300]!r}") from exc
    try:
        return json.loads(body.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise RuntimeError(f"Metadata response is not UTF-8 JSON at {url}") from exc


def _model_id(value, label):
    entries = value.get("data") if isinstance(value, dict) else None
    if not isinstance(entries, list) or not entries or not isinstance(entries[0], dict):
        raise RuntimeError(f"{label} /v1/models response has no model entry")
    model_id = entries[0].get("id")
    if not isinstance(model_id, str) or not model_id:
        raise RuntimeError(f"{label} /v1/models response has no model id")
    return model_id


def _validate_endpoint_props(props, label):
    if not isinstance(props, dict) or props.get("total_slots") != 1:
        raise RuntimeError(f"{label} server must report exactly one total slot")
    settings = props.get("default_generation_settings")
    params = settings.get("params") if isinstance(settings, dict) else None
    for source in (props, settings if isinstance(settings, dict) else {}, params if isinstance(params, dict) else {}):
        for key in ("np", "n_parallel"):
            if key in source and source[key] != 1:
                raise RuntimeError(f"{label} server must report {key}=1 for serial evaluation")


def _prepare(argv=None):
    args = _parse_args(argv)
    if not math.isfinite(args.timeout) or args.timeout <= 0 or not math.isfinite(args.core_timeout) or args.core_timeout <= 0:
        raise ValueError("Timeouts must be finite positive numbers")
    d3_url, qwen_url = args.d3_url.rstrip("/"), args.qwen_url.rstrip("/")
    _validate_loopback_http(d3_url)
    _validate_loopback_http(qwen_url)
    if not COREBRIDGE_DLL.is_file() or not CORE_DLL.is_file():
        raise FileNotFoundError("Prebuilt CoreBridge.dll and PeopleOfTheIliacBay.Core.dll are required; build separately before running")
    run_dir_input = Path(args.run_dir)
    if not run_dir_input.is_absolute():
        run_dir_input = REPO / run_dir_input
    if run_dir_input.exists() or run_dir_input.is_symlink():
        raise ValueError(f"Run directory must be fresh and non-existing: {run_dir_input}")
    run_dir = run_dir_input.resolve()
    if run_dir.exists():
        raise ValueError(f"Run directory must be fresh and non-existing: {run_dir}")
    if not run_dir.parent.is_dir():
        raise FileNotFoundError(f"Run directory parent does not exist: {run_dir.parent}")
    suite_paths, cases, suite_hashes = _load_suites(args.suite)
    frozen_paths = suite_paths + [Path(__file__).resolve(), ROOT / "goal_protocol.py", ROOT / "model_client.py",
                                  ROOT / "run_evaluation.py", COREBRIDGE_DLL, CORE_DLL]
    frozen = {str(path.resolve()): hashlib.sha256(path.read_bytes()).hexdigest() for path in frozen_paths}
    return args, run_dir, d3_url, qwen_url, cases, suite_hashes, frozen


def _assert_frozen(frozen):
    for path_text, digest in frozen.items():
        path = Path(path_text)
        if not path.is_file() or hashlib.sha256(path.read_bytes()).hexdigest() != digest:
            raise RuntimeError(f"Frozen input changed during run: {path}")


def _step_signature(step, expected=False):
    kind = step.get("kind")
    target = step.get("target") if isinstance(step.get("target"), dict) else {}
    target_ref = step.get("targetRef", step.get("ref"))
    if expected:
        seconds = step.get("durationSeconds", step.get("seconds"))
        if seconds is None and kind in ("Wait", "Follow"):
            seconds = 300
        quantity = step.get("quantity", 1)
    else:
        seconds = step.get("durationSeconds", 300 if kind in ("Wait", "Follow") else 0)
        quantity = target.get("Quantity", target.get("quantity", 1))
    return kind, target_ref, float(seconds or 0), quantity, step.get("itemSourceStep", 0)


def _expected_signature(case):
    expected = case["expected"]
    outcome = expected["outcome"]
    commitment_id = expected.get("commitmentId")
    if outcome == "cancel" and commitment_id is None:
        active = case.get("snapshot", {}).get("activeCommitment")
        commitment_id = active.get("id") if isinstance(active, dict) else None
    steps = tuple(_step_signature(step, expected=True) for step in expected.get("steps", []))
    return outcome, steps, commitment_id


def _actual_signature(pipeline):
    steps = pipeline.get("steps", []) if isinstance(pipeline, dict) else []
    return (pipeline.get("outcome"), tuple(_step_signature(step) for step in steps), pipeline.get("commitmentId"))


def score_case(case, flow, extraction_valid):
    expected = case["expected"]
    route = flow.get("route")
    observed = flow.get("interpretation")
    skipped = flow.get("interpreterSkipped") is True
    expected_intent = expected["intent"]
    if skipped:
        intent_correct = None
        inferred_intent = "none" if route == "SOCIAL" else None
        speech_act_correct = inferred_intent == expected_intent
        intent_assessment = "inferred_none_from_social_route" if route == "SOCIAL" else "unobserved"
    else:
        inferred_intent = observed.get("intent") if isinstance(observed, dict) else None
        intent_correct = inferred_intent == expected_intent
        speech_act_correct = intent_correct
        intent_assessment = "observed" if observed is not None else "unavailable"
    pipeline = flow.get("pipeline") or {"outcome": "invalid", "steps": []}
    route_correct = route == expected["route"]
    pipeline_exact = _actual_signature(pipeline) == _expected_signature(case)
    route_well_formed = route in ROUTES and not flow.get("routeError")
    extraction_well_formed = skipped or extraction_valid is True
    structural_exact = route_well_formed and extraction_well_formed
    all_exact = bool(route_correct and speech_act_correct and pipeline_exact and structural_exact and
                     not flow.get("routeError") and not flow.get("extractionError"))
    effect_shaped = pipeline.get("outcome") in ("plan", "cancel")
    return {
        "routeCorrect": route_correct,
        "interpreterObserved": not skipped,
        "intentAssessment": intent_assessment,
        "inferredIntent": inferred_intent,
        "intentCorrect": intent_correct,
        "speechActCorrect": speech_act_correct,
        "extractionValid": extraction_valid,
        "structuralExact": structural_exact,
        "pipelineExact": pipeline_exact,
        "allExercisedStagesExact": all_exact,
        "semanticFalseProposal": bool(effect_shaped and not pipeline_exact),
        "criticalFalseAction": bool(effect_shaped and not all_exact),
        "actualOutcome": pipeline.get("outcome"),
    }


def _route_call(case, model_url, reverse, ledger, metadata, timeout):
    response = post_json(model_url + "/v1/systemone", route_payload(case, reverse), ledger,
                         dict(metadata, stage="routing"), timeout=timeout)
    if response["status_code"] != 200 or response["transport_error"] or response["malformed_json"]:
        return None, "route_transport_or_json_failure"
    try:
        route = response["response_json"]["answers"]["route"]["choice"]
    except (KeyError, TypeError):
        return None, "malformed_route"
    return (route, None) if isinstance(route, str) and route in ROUTES else (None, "unknown_route")


def _interpreter_call(case, model, model_url, ledger, metadata, timeout):
    response = post_json(model_url + "/v1/chat/completions", interpretation_payload(case, model), ledger,
                         dict(metadata, stage="interpretation"), timeout=timeout)
    if response["status_code"] != 200 or response["transport_error"] or response["malformed_json"]:
        return None, "interpreter_transport_or_json_failure"
    try:
        choice = response["response_json"]["choices"][0]
        if choice.get("finish_reason") == "length":
            return None, "truncated_completion"
        value = json.loads(choice["message"]["content"])
    except (KeyError, IndexError, TypeError, ValueError):
        return None, "invalid_interpreter_completion"
    return (value, None) if isinstance(value, dict) else (None, "interpreter_reply_not_object")


def _write_json(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2, allow_nan=False) + "\n", encoding="utf-8")


def _write_jsonl(path, rows):
    path.write_text("".join(json.dumps(row, ensure_ascii=False, allow_nan=False) + "\n" for row in rows), encoding="utf-8")


def _run_core(proposals, run_dir, timeout):
    command = ["dotnet", str(COREBRIDGE_DLL)]
    try:
        result = subprocess.run(command, input=json.dumps(proposals, ensure_ascii=False), capture_output=True,
                                text=True, timeout=timeout, check=False, cwd=REPO)
        stdout, stderr, returncode = result.stdout, result.stderr, result.returncode
        timed_out = False
    except subprocess.TimeoutExpired as exc:
        stdout = exc.stdout.decode("utf-8", errors="replace") if isinstance(exc.stdout, bytes) else (exc.stdout or "")
        stderr = exc.stderr.decode("utf-8", errors="replace") if isinstance(exc.stderr, bytes) else (exc.stderr or "")
        returncode, timed_out = None, True
    (run_dir / "core-stdout.json").write_text(stdout, encoding="utf-8")
    (run_dir / "core-stderr.txt").write_text(stderr, encoding="utf-8")
    parsed, parse_error = None, None
    try:
        parsed = json.loads(stdout)
        if not isinstance(parsed, list):
            raise ValueError("CoreBridge output is not a JSON array")
    except (json.JSONDecodeError, ValueError) as exc:
        parse_error = str(exc)
    by_id, duplicate_ids = {}, []
    if parsed is not None:
        for item in parsed:
            if not isinstance(item, dict) or not isinstance(item.get("id"), str):
                parse_error = "CoreBridge returned a result without id"
                continue
            if item["id"] in by_id:
                duplicate_ids.append(item["id"])
            by_id[item["id"]] = item
    return {"command": command, "returnCode": returncode, "timedOut": timed_out, "parseError": parse_error,
            "duplicateIds": duplicate_ids, "byId": by_id, "rawCount": len(parsed) if parsed is not None else None}


def run(argv=None):
    args, run_dir, d3_url, qwen_url, cases, suite_hashes, frozen = _prepare(argv)
    game_closed()
    run_dir.mkdir()
    manifest = {"frozenUtc": datetime.now(timezone.utc).isoformat(), "files": frozen,
                "suites": suite_hashes, "caseCount": len(cases),
                "d3BaseUrl": d3_url, "qwenBaseUrl": qwen_url,
                "scope": "Event-first route, optional structured interpretation, deterministic proposal materialization and Core task-contract validation. No NPC willingness or world effects."}
    _write_json(run_dir / "manifest.json", manifest)

    d3_models = _get_json(d3_url + "/v1/models")
    qwen_models = _get_json(qwen_url + "/v1/models")
    manifest["decisionModel"] = _model_id(d3_models, "D3")
    manifest["interpreterModel"] = _model_id(qwen_models, "Qwen")
    d3_props, qwen_props = _get_json(d3_url + "/props"), _get_json(qwen_url + "/props")
    _validate_endpoint_props(d3_props, "D3")
    _validate_endpoint_props(qwen_props, "Qwen")
    manifest["decisionMetadata"] = {"models": d3_models, "props": d3_props}
    manifest["interpreterMetadata"] = {"models": qwen_models, "props": qwen_props}
    manifest["inferenceConcurrency"] = 1
    _write_json(run_dir / "manifest.json", manifest)

    rows, proposals = [], []
    ledger = run_dir / "attempts.jsonl"
    for index, item in enumerate(cases):
        game_closed()
        suite_name, case = item["suite"], item["case"]
        for arm in (("original", "reversed") if index % 2 == 0 else ("reversed", "original")):
            _assert_frozen(frozen)
            metadata = {"suite": suite_name, "case_id": case["id"], "arm": arm}
            started = time.perf_counter()
            flow = event_first_flow(
                case,
                lambda: _route_call(case, d3_url, arm == "reversed", ledger, metadata, args.timeout),
                lambda: _interpreter_call(case, manifest["interpreterModel"], qwen_url, ledger, metadata, args.timeout),
            )
            extraction_valid = None
            if flow["interpretation"] is not None:
                extraction_valid = validate_extraction(case, flow["interpretation"])[0]
            scores = score_case(case, flow, extraction_valid)
            row = {"suite": suite_name, "caseId": case["id"], "arm": arm, **flow,
                   "scores": scores, "wallLatencyMs": round((time.perf_counter() - started) * 1000, 3),
                   "coreContract": None}
            rows.append(row)
            _write_jsonl(run_dir / "results.jsonl", rows)
            if flow["pipeline"].get("outcome") == "plan":
                proposal_id = case["id"] + ":" + arm
                row["coreProposalId"] = proposal_id
                proposals.append({"id": proposal_id, "steps": flow["pipeline"]["steps"]})
            print(suite_name, case["id"], arm, "complete", flush=True)

    (run_dir / "contract-input.json").write_text(json.dumps(proposals, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    game_closed()
    _assert_frozen(frozen)
    core = _run_core(proposals, run_dir, args.core_timeout)
    expected_ids = {proposal["id"] for proposal in proposals}
    unknown_ids = sorted(set(core["byId"]) - expected_ids)
    missing_ids = sorted(expected_ids - set(core["byId"]))
    core_summary = {key: value for key, value in core.items() if key != "byId"}
    core_summary.update({"expectedProposalCount": len(expected_ids), "unknownIds": unknown_ids, "missingIds": missing_ids})
    _write_json(run_dir / "core-summary.json", core_summary)
    _write_json(run_dir / "core-results.json", [core["byId"][key] for key in sorted(core["byId"])])
    for row in rows:
        proposal_id = row.get("coreProposalId")
        result = core["byId"].get(proposal_id) if proposal_id else None
        row["coreContract"] = result
    _write_jsonl(run_dir / "results.jsonl", rows)
    summary = _summarize(rows, core_summary)
    _write_json(run_dir / "summary.json", summary)
    print(json.dumps(summary, ensure_ascii=False, indent=2))
    return summary


def _summarize(rows, core):
    groups = {}
    for row in rows:
        key = (row["suite"], row["arm"])
        group = groups.setdefault(key, {"cases": 0, "interpreterSkipped": 0, "coreContractValid": 0,
                                         "routeCorrect": 0, "intentObserved": 0, "intentCorrectObserved": 0,
                                         "speechActCorrect": 0, "structuralExact": 0, "pipelineExact": 0,
                                         "allExercisedStagesExact": 0, "semanticFalseProposal": 0,
                                         "criticalFalseAction": 0})
        group["cases"] += 1
        scores = row["scores"]
        group["interpreterSkipped"] += int(not scores["interpreterObserved"])
        group["intentObserved"] += int(scores["interpreterObserved"])
        group["intentCorrectObserved"] += int(scores["intentCorrect"] is True)
        for metric in ("routeCorrect", "speechActCorrect", "structuralExact", "pipelineExact", "allExercisedStagesExact",
                       "semanticFalseProposal", "criticalFalseAction"):
            group[metric] += int(bool(scores[metric]))
        if row["coreContract"] and row["coreContract"].get("contractValid") is True:
            group["coreContractValid"] += 1
    return {"groups": {f"{suite}/{arm}": value for (suite, arm), value in sorted(groups.items())},
            "coreBridge": core, "claims": {"npcWillingness": False, "worldEffects": False,
                                            "interpretationSemantics": "scored against frozen synthetic suite gold"}}


if __name__ == "__main__":
    run()
