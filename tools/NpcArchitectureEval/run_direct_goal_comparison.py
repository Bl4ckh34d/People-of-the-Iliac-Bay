"""Direct whole-plan Qwen baseline on the frozen paired sequential-frame suite.

This is an offline experimental plan comparison, not native action execution.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

import direct_goal_case
import model_client
import run_conceptual_goal_pilot as pilot
from run_core_tool_pilot import _model_id
from run_evaluation import game_closed, get_json
from run_paired_frame_pilot import _append, _bytes, _core_results, _reference_plans

HERE = Path(__file__).resolve().parent


def run(suite_path, run_dir, model, url, execute=False):
    run_dir = Path(run_dir).resolve()
    if run_dir.exists():
        raise FileExistsError("Fresh output directory required")
    suite_path = Path(suite_path).resolve()
    suite_bytes = suite_path.read_bytes()
    suite = json.loads(suite_bytes)
    pilot._validate_suite(suite, suite["casesSha256"])
    binary = HERE / "CoreBridge/bin/Debug/net8.0/CoreBridge.dll"
    manifest = {"scope": "experimental_direct_whole_plan_synthetic_binding_no_native_effects",
                "model": model, "url": url, "mode": "preflight", "cases": len(suite["cases"]),
                "suiteSha256": hashlib.sha256(suite_bytes).hexdigest(), "semanticReview": "pending"}
    if not execute:
        return manifest
    manifest["referenceCoreValidation"] = _reference_plans(suite, binary)
    game_closed()
    model_client._validate_loopback_http(url)
    if _model_id(get_json(url + "/v1/models"), "qwen") != model:
        raise ValueError("Active model identity differs from pinned model")
    paths = list(HERE.glob("*.py")) + [suite_path, binary, binary.with_name("PeopleOfTheIliacBay.Core.dll")]
    frozen = {str(path): hashlib.sha256(path.read_bytes()).hexdigest() for path in paths}
    run_dir.mkdir(parents=True, exist_ok=False)
    (run_dir / "suite.json").write_bytes(suite_bytes)
    manifest.update(mode="running", frozenFiles=frozen)
    (run_dir / "manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    rows = []
    for case in suite["cases"]:
        pilot._assert_frozen(frozen)
        game_closed()

        def call(stage, payload):
            metadata = {"caseId": case["id"], "selector": "qwen-direct", "stage": stage}
            response = model_client.post_json(url + "/v1/chat/completions", payload,
                run_dir / "transport-events.jsonl", metadata, timeout=10)
            _append(run_dir / "attempts.jsonl", {**metadata, "actualRequest": payload, "actualResponse": response})
            return response

        row = direct_goal_case.evaluate_case(case, model, call)
        row["caseInputSha256"] = hashlib.sha256(_bytes(case)).hexdigest()
        rows.append(row)
        _append(run_dir / "results.jsonl", row)
        print(case["id"] + ": " + row["groundingOutcome"], flush=True)
    proposals = [{"id": row["caseId"] + "/qwen-direct", "steps": row["planSteps"]}
                 for row in rows if row["groundingOutcome"] == "plan"]
    (run_dir / "core-input.json").write_text(json.dumps(proposals), encoding="utf-8")
    core = _core_results(binary, proposals)
    (run_dir / "core-output.json").write_text(json.dumps(core, indent=2), encoding="utf-8")
    pilot._assert_frozen(frozen)
    manifest.update(mode="executed", coreResults=core, rows=len(rows),
                    coreAcceptedPlans=sum(row["contractValid"] for row in core),
                    pythonProposals=sum(row["plan"] for row in rows))
    (run_dir / "manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    return manifest


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--suite", required=True, type=Path)
    parser.add_argument("--run-dir", required=True, type=Path)
    parser.add_argument("--qwen-model", required=True)
    parser.add_argument("--url", default="http://127.0.0.1:8081")
    parser.add_argument("--execute", action="store_true")
    args = parser.parse_args()
    print(json.dumps(run(args.suite, args.run_dir, args.qwen_model, args.url, args.execute)))
