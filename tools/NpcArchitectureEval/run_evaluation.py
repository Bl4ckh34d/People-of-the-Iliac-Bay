"""Portable loopback-only replay comparison; never applies game effects."""
from __future__ import annotations

import argparse
import hashlib
import json
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path
from urllib.error import HTTPError
from urllib.request import HTTPRedirectHandler, Request, build_opener

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[1]
sys.path.insert(0, str(ROOT))

from model_client import _validate_loopback_http, post_json  # noqa: E402
from adapters import payload_for, read_choice  # noqa: E402
from scenario_validation import ScenarioReplay, validate_suite, score_choice, aggregate_scores  # noqa: E402


class _NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def game_closed():
    if sys.platform != "win32":
        raise RuntimeError("The game-process guard is Windows-only; verify Daggerfall Unity is closed before running elsewhere")
    result = subprocess.run(
        ["tasklist", "/FI", "IMAGENAME eq DaggerfallUnity.exe", "/FO", "CSV", "/NH"],
        capture_output=True,
        check=False,
    )
    if result.returncode != 0:
        raise RuntimeError("Could not verify that Daggerfall Unity is closed")
    if b"daggerfallunity.exe" in result.stdout.lower():
        raise RuntimeError("Game active; defer offline evaluation")


def get_json(url):
    _validate_loopback_http(url)
    request = Request(url, headers={"Accept": "application/json"})
    try:
        with build_opener(_NoRedirect).open(request, timeout=5) as response:
            body = response.read()
    except HTTPError as exc:
        body = exc.read()
        exc.close()
        raise RuntimeError(f"Metadata request failed: HTTP {exc.code} at {url}: {body[:400]!r}") from exc
    try:
        return json.loads(body.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise RuntimeError(f"Metadata response is not UTF-8 JSON at {url}") from exc


def _parse_args(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--run-dir", required=True, help="new, non-existing run directory; relative paths resolve from repository root")
    parser.add_argument("--d3-url", help="unauthenticated loopback HTTP base URL for D3 /v1/systemone")
    parser.add_argument("--qwen-url", help="unauthenticated loopback HTTP base URL for Qwen /v1/chat/completions")
    parser.add_argument("--models", nargs="+", choices=("d3", "qwen"), default=("d3", "qwen"),
                        help="models to run (default: d3 qwen); only selected endpoints are contacted")
    parser.add_argument("--suite", action="append", help="suite JSON path; repeat to combine suites (default: checked-in social-world and action-combat only)")
    parser.add_argument("--timeout", type=float, default=60, help="per-inference request timeout in seconds (default: 60)")
    return parser.parse_args(argv)


def _model_id(models, label):
    entries = models.get("data") if isinstance(models, dict) else None
    if not isinstance(entries, list) or not entries or not isinstance(entries[0], dict):
        raise RuntimeError(f"{label} /v1/models response has no model entry")
    model_id = entries[0].get("id")
    if not isinstance(model_id, str) or not model_id:
        raise RuntimeError(f"{label} /v1/models response has no model id")
    return model_id


def load_cases(suite_args=None):
    """Load and validate explicit suites, or the frozen default pair."""
    if suite_args:
        paths = []
        for value in suite_args:
            path = Path(value)
            paths.append(path.resolve() if path.is_absolute() else (REPO / path).resolve())
    else:
        paths = [ROOT / "fixtures" / "social-world.json", ROOT / "fixtures" / "action-combat.json"]
    cases = []
    for path in paths:
        if not path.is_file():
            raise FileNotFoundError(f"Required suite is missing: {path}")
        suite = json.loads(path.read_text(encoding="utf-8-sig"))
        validate_suite(suite)
        cases.extend(suite["cases"])
    validate_suite({"cases": cases})
    return paths, cases


def run(argv=None):
    args = _parse_args(argv)
    game_closed()
    run_dir = Path(args.run_dir)
    if not run_dir.is_absolute():
        run_dir = REPO / run_dir
    run_dir = run_dir.resolve()

    provided_urls = {"d3": args.d3_url, "qwen": args.qwen_url}
    endpoints = {}
    for kind in args.models:
        value = provided_urls[kind]
        if not value:
            raise ValueError(f"--{kind}-url is required when --models includes {kind}")
        endpoints[kind] = value.rstrip("/")
    if len(endpoints) != len(args.models):
        raise ValueError("--models must not contain duplicate entries")
    for endpoint in endpoints.values():
        _validate_loopback_http(endpoint)
    fixtures, cases = load_cases(args.suite)
    frozen_paths = fixtures + [Path(__file__), ROOT / "adapters.py", ROOT / "scenario_validation.py", ROOT / "model_client.py"]
    frozen = {str(path): hashlib.sha256(path.read_bytes()).hexdigest() for path in frozen_paths}

    metadata = {}
    models = {}
    for kind, url in endpoints.items():
        models[kind] = get_json(url + "/v1/models")
        metadata[kind] = {"models": models[kind], "props": get_json(url + "/props")}
        props = metadata[kind]["props"]
        if not isinstance(props, dict) or props.get("total_slots") != 1:
            raise RuntimeError(f"{kind} server must report exactly one total slot")
    model_ids = {kind: _model_id(models[kind], kind) for kind in endpoints}
    planned_per_model = sum(len(case.get("turns") or [None]) for case in cases) * 2
    run_dir.mkdir(parents=True, exist_ok=False)
    manifest = {
        "status": "FROZEN_BEFORE_INFERENCE",
        "frozenUtc": datetime.now(timezone.utc).isoformat(),
        "caseCount": len(cases),
        "plannedTurnsPerModel": planned_per_model,
        "files": frozen,
        "modelIds": model_ids,
        "metadata": metadata,
        "limitations": [
            "D3 native choice head versus Qwen strict JSON completion; same state and decision instructions, different format adapters.",
            "Counterbalanced case/model/order scheduling; timings still include local cache, contention and warm-up effects.",
            "Later multi-turn order comparisons may include diverging actual histories; not pure isolated order effects.",
            "This offline offered-choice experiment does not establish native action execution, game effects, or overall agent quality.",
        ],
        "scope": "Synthetic offered-choice development fixtures; controlled factual turn updates and actual choice history. No native effects, free planning, live tools, vision or dialogue prose.",
    }
    (run_dir / "manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    rows, skipped = [], []
    for case_index, case in enumerate(cases):
        game_closed()
        if any(hashlib.sha256(Path(path).read_bytes()).hexdigest() != digest for path, digest in frozen.items()):
            raise RuntimeError("Frozen code or corpus changed during run")
        model_order = list(endpoints) if case_index % 2 == 0 else list(reversed(endpoints))
        arm_order = ["original", "reversed"] if case_index % 2 == 0 else ["reversed", "original"]
        for kind in model_order:
            url = endpoints[kind]
            for arm in arm_order:
                replay = ScenarioReplay(case)
                while (turn := replay.prepare_next()) is not None:
                    game_closed()
                    payload = payload_for(kind, turn.model_input, model_ids[kind], arm == "reversed")
                    response = post_json(
                        url + ("/v1/systemone" if kind == "d3" else "/v1/chat/completions"),
                        payload,
                        run_dir / "attempts.jsonl",
                        {"caseId": case["id"], "turn": turn.turn_index, "model": kind, "arm": arm},
                        timeout=args.timeout,
                    )
                    choice, error = read_choice(kind, response)
                    score = score_choice(turn, choice if choice is not None else "__TECHNICAL_FAILURE__")
                    row = {
                        "id": case["id"], "turn": turn.turn_index, "model": kind, "arm": arm,
                        "choice": choice, "technicalError": error,
                        "unofferedChoice": choice is not None and choice not in turn.option_by_id,
                        "status": response["status_code"], "latencyMs": response["latency_ms"],
                        "multiTurn": bool(case.get("turns")), "score": score,
                        "expected": turn.expected, "options": turn.model_input["options"],
                    }
                    rows.append(row)
                    with (run_dir / "results.jsonl").open("a", encoding="utf-8") as stream:
                        stream.write(json.dumps(row, ensure_ascii=False) + "\n")
                    if error or choice not in turn.option_by_id:
                        for remaining in range(turn.turn_index + 1, len(case.get("turns") or [None])):
                            skipped.append({"id": case["id"], "turn": remaining, "model": kind, "arm": arm,
                                            "reason": "Previous turn returned no usable decision; chain aborted without invented history."})
                        break
                    replay.record_choice(choice)
        print(case["id"], "completed", flush=True)

    summary = {}
    for kind in endpoints:
        model_rows = [row for row in rows if row["model"] == kind]
        valid = [row for row in model_rows if not row["technicalError"]]
        latencies = sorted(row["latencyMs"] for row in model_rows)
        indexed = {(row["id"], row["turn"], row["arm"]): row for row in valid if not row["unofferedChoice"]}
        pairs = [(row, indexed[(row["id"], row["turn"], "reversed")]) for row in valid
                 if row["arm"] == "original" and not row["unofferedChoice"]
                 and (row["id"], row["turn"], "reversed") in indexed]
        summary[kind] = {
            "plannedTurns": planned_per_model,
            "attempts": len(model_rows),
            "skippedTurns": sum(row["model"] == kind for row in skipped),
            "technicalFailures": len(model_rows) - len(valid),
            "unofferedChoices": sum(row["unofferedChoice"] for row in model_rows),
            "score": aggregate_scores(row["score"] for row in valid),
            "accuracyAllAttempts": sum(row["score"]["point"] for row in model_rows) / len(model_rows),
            "accuracyAllPlannedTurns": sum(row["score"]["point"] for row in model_rows) / planned_per_model,
            "original": aggregate_scores(row["score"] for row in valid if row["arm"] == "original"),
            "reversed": aggregate_scores(row["score"] for row in valid if row["arm"] == "reversed"),
            "multiTurn": aggregate_scores(row["score"] for row in valid if row["multiTurn"]),
            "pairedOrderChanges": sum(a["choice"] != b["choice"] for a, b in pairs),
            "orderPairs": len(pairs),
            "latencyP50Ms": latencies[len(latencies) // 2],
            "latencyP95Ms": latencies[int(len(latencies) * .95)],
        }
    (run_dir / "summary.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
    (run_dir / "skipped.json").write_text(json.dumps(skipped, indent=2), encoding="utf-8")
    for kind, value in summary.items():
        print(kind, value["attempts"], value["technicalFailures"], value["unofferedChoices"],
              value["score"]["raw_choice_accuracy"], value["score"]["weighted_quality"])


if __name__ == "__main__":
    run()
