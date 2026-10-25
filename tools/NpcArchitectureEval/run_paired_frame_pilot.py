"""Matched experimental sequential-frame planner comparison.

Both arms use the existing frame/argument/grounding protocol. This is not the
installed production route and never executes a native action.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import subprocess
from pathlib import Path

import frame_qwen_adapter
import conceptual_goal_protocol
import model_client
import run_conceptual_goal_pilot as conceptual_pilot
import run_decision_frame_pilot as frame_pilot
from run_core_tool_pilot import _model_id
from run_evaluation import game_closed, get_json

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]


def _bytes(value):
    return json.dumps(value, ensure_ascii=False, separators=(",", ":"), allow_nan=False).encode("utf-8")


def _append(path, value):
    with path.open("a", encoding="utf-8", newline="\n") as stream:
        stream.write(_bytes(value).decode() + "\n")
        stream.flush()


def _core_results(binary, proposals):
    process = subprocess.run(["dotnet", str(binary)], input=json.dumps(proposals),
                             capture_output=True, text=True, encoding="utf-8")
    if process.returncode != 0:
        raise RuntimeError("Actual Core bridge failed: " + process.stderr)
    return _validate_core_results(json.loads(process.stdout), proposals)


def _validate_core_results(results, proposals):
    if not isinstance(results, list) or not all(isinstance(row, dict) and isinstance(row.get("id"), str) for row in results):
        raise ValueError("Core result must be an array of identified rows")
    expected_ids = [row["id"] for row in proposals]
    actual_ids = [row["id"] for row in results]
    if len(results) != len(proposals) or len(set(actual_ids)) != len(actual_ids) or set(actual_ids) != set(expected_ids):
        raise ValueError("Core result IDs/count do not match submitted proposals")
    return results


def _reference_plans(suite, binary):
    proposals = []
    for case in suite["cases"]:
        if case["expected"]["grounding"] != "plan":
            continue
        concept = conceptual_goal_protocol.extraction(None, case["expected"]["conceptual"], case)
        ground = conceptual_goal_protocol.ground(concept, case["expected"]["groundingPlan"]["bindings"],
                                                case["snapshot"]["candidates"])
        if ground["outcome"] != "plan":
            raise ValueError("Fixture reference cannot bind a plan: " + case["id"])
        proposals.append({"id": case["id"], "steps": ground["steps"]})
    results = _core_results(binary, proposals)
    rejected = [row for row in results if not row["contractValid"]]
    if rejected:
        raise ValueError("Fixture reference rejected by Core: " + json.dumps(rejected))
    return results


def evaluate_pair(case, models, run_dir, *, transport=None,
                  d3_url="http://127.0.0.1:8092", qwen_url="http://127.0.0.1:8081",
                  reverse=False, arm_order=("d3", "qwen")):
    run_dir = Path(run_dir)
    rows = []
    for selector in arm_order:
        frame_calls = 0

        def call(stage, payload):
            nonlocal frame_calls
            selecting_qwen = stage == "selection" and selector == "qwen"
            actual_payload = frame_qwen_adapter.build_payload(payload, models["qwen"]) if selecting_qwen else payload
            url = d3_url + "/v1/systemone" if stage == "selection" and not selecting_qwen else qwen_url + "/v1/chat/completions"
            if stage == "selection":
                frame_calls += 1
            metadata = {"caseId": case["id"], "selector": selector, "stage": stage}
            response = (transport(url, actual_payload) if transport else
                        model_client.post_json(url, actual_payload, run_dir / "transport-events.jsonl", metadata, timeout=10))
            record = {**metadata, "url": url, "framePayloadSha256": hashlib.sha256(_bytes(payload)).hexdigest(),
                      "actualRequestSha256": hashlib.sha256(_bytes(actual_payload)).hexdigest(),
                      "actualRequest": actual_payload, "actualResponse": response,
                      "syntheticProbabilityEncoding": selecting_qwen, "conversionError": None}
            # Store original bytes before converting a Qwen selection to wire shape.
            _append(run_dir / "attempts.jsonl", record)
            if selecting_qwen and response.get("status_code") == 200 and not response.get("transport_error"):
                try:
                    canonical = frame_qwen_adapter.convert_response(payload, response.get("response_raw"), models["qwen"])
                    _append(run_dir / "conversions.jsonl", {**metadata, "canonical": canonical,
                            "probabilityProvenance": "adapter_one_hot_not_model_confidence"})
                    return {**response, "response_raw": _bytes(canonical).decode(), "response_json": canonical}
                except (ValueError, TypeError, KeyError) as error:
                    _append(run_dir / "conversions.jsonl", {**metadata, "conversionError": str(error)})
                    return {**response, "transport_error": "frame_conversion_failed: " + str(error)}
            return response

        expected = {"d3": models[selector], "qwen": models["qwen"]}
        row = frame_pilot.evaluate_case(case, expected, call, reverse=reverse)
        row["selector"] = selector
        row["caseInputSha256"] = hashlib.sha256(_bytes(case)).hexdigest()
        row["frameSelectionCallCount"] = frame_calls
        if selector == "qwen":
            row["qwenCallCount"] += frame_calls
            row["d3CallCount"] = 0
        _append(run_dir / "results.jsonl", row)
        rows.append(row)
    return rows


def run_paired(suite_path, run_dir, models, *, execute=False, transport=None,
               d3_url="http://127.0.0.1:8092", qwen_url="http://127.0.0.1:8081"):
    run_dir = Path(run_dir).resolve()
    if run_dir.exists():
        raise FileExistsError("Fresh output directory required")
    suite_path = Path(suite_path).resolve()
    suite_bytes = suite_path.read_bytes()
    suite = json.loads(suite_bytes)
    conceptual_pilot._validate_suite(suite, suite["casesSha256"])
    for name in ("d3", "qwen"):
        if not isinstance(models.get(name), str) or not models[name]:
            raise ValueError("Exact model identities required")
    manifest = {"scope": "experimental_sequential_frames_synthetic_binding_not_production_or_native_effects",
                "mode": "preflight", "cases": len(suite["cases"]), "models": models,
                "suiteSha256": hashlib.sha256(suite_bytes).hexdigest(),
                "nativeEffects": False, "semanticReview": "pending", "d3Url": d3_url, "qwenUrl": qwen_url}
    if not execute:
        return manifest
    binary = HERE / "CoreBridge/bin/Debug/net8.0/CoreBridge.dll"
    if not binary.is_file():
        raise FileNotFoundError("Build current Debug CoreBridge before inference")
    manifest["referenceCoreValidation"] = _reference_plans(suite, binary)
    frozen_paths = list(HERE.glob("*.py")) + [suite_path, binary, binary.with_name("PeopleOfTheIliacBay.Core.dll")]
    frozen = {str(path): hashlib.sha256(path.read_bytes()).hexdigest() for path in frozen_paths}
    if transport is None:
        game_closed()
        for label, url in (("d3", d3_url), ("qwen", qwen_url)):
            model_client._validate_loopback_http(url)
            actual = _model_id(get_json(url + "/v1/models"), label)
            if actual != models[label]:
                raise ValueError("Active model identity differs from pinned model")
    run_dir.mkdir(parents=True, exist_ok=False)
    (run_dir / "suite.json").write_bytes(suite_bytes)
    manifest["frozenFiles"] = frozen
    manifest["mode"] = "running"
    (run_dir / "manifest.json").write_text(json.dumps(manifest, indent=2))
    rows = []
    for index, case in enumerate(suite["cases"]):
        conceptual_pilot._assert_frozen(frozen)
        if transport is None:
            game_closed()
        rows.extend(evaluate_pair(case, models, run_dir, transport=transport, d3_url=d3_url, qwen_url=qwen_url,
                                  arm_order=("d3", "qwen") if index % 2 == 0 else ("qwen", "d3")))
        print(f"{case['id']}: two arms recorded", flush=True)
    proposals = [{"id": row["caseId"] + "/" + row["selector"], "steps": row["planSteps"]}
                 for row in rows if row["groundingOutcome"] == "plan"]
    (run_dir / "core-input.json").write_text(json.dumps(proposals))
    process = subprocess.run(["dotnet", str(binary)], input=json.dumps(proposals),
                             capture_output=True, text=True, encoding="utf-8")
    (run_dir / "core-output.txt").write_text(process.stdout)
    (run_dir / "core-stderr.txt").write_text(process.stderr)
    if process.returncode != 0:
        raise RuntimeError("Actual Core bridge failed; all model evidence retained")
    core = _validate_core_results(json.loads(process.stdout), proposals)
    manifest["mode"] = "executed"
    manifest["coreResults"] = core
    manifest["rows"] = len(rows)
    manifest["arms"] = {selector: {"plans": sum(row["plan"] for row in rows if row["selector"] == selector),
                       "orderedKindsCorrect": sum(row["orderedKindsCorrect"] is True for row in rows if row["selector"] == selector),
                       "falsePlans": sum(row["falsePlan"] for row in rows if row["selector"] == selector)}
                       for selector in ("d3", "qwen")}
    (run_dir / "manifest.json").write_text(json.dumps(manifest, indent=2))
    return manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--suite", required=True, type=Path)
    parser.add_argument("--run-dir", required=True, type=Path)
    parser.add_argument("--d3-model", required=True)
    parser.add_argument("--qwen-model", required=True)
    parser.add_argument("--execute", action="store_true")
    args = parser.parse_args()
    print(json.dumps(run_paired(args.suite, args.run_dir, {"d3": args.d3_model, "qwen": args.qwen_model}, execute=args.execute)))


if __name__ == "__main__":
    main()
