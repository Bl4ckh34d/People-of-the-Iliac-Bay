"""Paired v1 appraisal experiment changing only Qwen ChatML prompt framing."""

from __future__ import annotations

import argparse
import base64
import copy
import hashlib
import json
import subprocess
import sys
from pathlib import Path
from typing import Any

from run_appraisal_evaluation import (
    _completion_content,
    _preflight_core,
    _read_jsonl,
    _validate_loopback_endpoint,
    _write_jsonl_new,
    post_json_bytes,
)
from validate_appraisal_preparation import validate_rows


HERE = Path(__file__).resolve().parent
DEFAULT_MODEL_PIN = HERE.parent.parent / "work" / "abliterated-swap-20261020" / "download-verified.json"
CAUSE_UNKNOWN_SCENARIOS = {"native-injury-health-episode", "native-status-onset-unknown-cause"}
CHATML_PREFIX = "<|im_start|>user\n"
CHATML_SUFFIX = "<|im_end|>\n<|im_start|>assistant\n"
EXPERIMENT_ID = "npc-appraisal-v1-qwen-chatml-wrapper-v1"


class ChatMlExperimentError(ValueError):
    """Experiment input or pairing violated the frozen comparison contract."""


def _sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def _canonical_json(value: Any) -> str:
    return json.dumps(value, ensure_ascii=False, separators=(",", ":"))


def _normal_path(value: str) -> str:
    return str(Path(value).resolve()).casefold()


def _case_id_map(rows: list[dict], label: str) -> dict[str, dict]:
    result = {}
    for row in rows:
        case_id = row.get("caseId")
        if not isinstance(case_id, str) or not case_id or case_id in result:
            raise ChatMlExperimentError(f"{label} case IDs must be present and unique")
        result[case_id] = row
    return result


def build_chatml_pairs(prepared_rows: list[dict], baseline_rows: list[dict], model_pin: dict) -> tuple[list[dict], list[dict]]:
    """Build exactly 24 matched pairs; metadata never enters model-visible prompt."""
    try:
        prepared_summary = validate_rows(prepared_rows)
    except (KeyError, TypeError, ValueError) as error:
        raise ChatMlExperimentError(f"Core-prepared v1 source rows failed validation: {error}") from error
    if prepared_summary.get("fixtureId") != "npc-experience-appraisal-live-scenarios-v1" or len(prepared_rows) != 32:
        raise ChatMlExperimentError("ChatML experiment requires the complete frozen v1 32-row source")
    expected_model = model_pin.get("path")
    expected_hash = model_pin.get("sha256")
    if not isinstance(expected_model, str) or not expected_model.strip() or \
            not isinstance(expected_hash, str) or not (len(expected_hash) == 64 and all(c in "0123456789abcdefABCDEF" for c in expected_hash)):
        raise ChatMlExperimentError("Pinned model file must contain path and SHA-256")

    prepared_by_id = _case_id_map(prepared_rows, "prepared")
    baseline_by_id = _case_id_map(baseline_rows, "baseline")
    if set(baseline_by_id) != set(prepared_by_id) or len(baseline_rows) != 32:
        raise ChatMlExperimentError("Baseline must contain exactly one response for every v1 case")

    variant_requests: list[dict] = []
    pair_manifest: list[dict] = []
    observed_baseline_models: set[str] = set()
    for source in prepared_rows:
        case_id = source["caseId"]
        baseline = baseline_by_id[case_id]
        prompt = source["requestPrompt"]
        prompt_bytes = prompt.encode("utf-8", errors="strict")
        original_body_text = source["completionRequestBody"]
        original_body_bytes = original_body_text.encode("utf-8", errors="strict")
        if source["requestPromptSha256"] != _sha256(prompt_bytes) or \
                source["completionRequestSha256"] != _sha256(original_body_bytes):
            raise ChatMlExperimentError(f"prepared source hashes mismatch for {case_id}")
        if baseline.get("recordType") != "appraisal-model-response-v1" or \
                baseline.get("requestPromptSha256") != source["requestPromptSha256"] or \
                baseline.get("rawRequest") != original_body_text or \
                baseline.get("rawRequestSha256") != source["completionRequestSha256"]:
            raise ChatMlExperimentError(f"baseline request hash/body mismatch for {case_id}")
        try:
            baseline_bytes = base64.b64decode(baseline["rawRequestBase64"], validate=True)
            original_body = json.loads(original_body_text)
            model = baseline["modelMetadata"]["model"]
        except (KeyError, TypeError, ValueError, json.JSONDecodeError) as error:
            raise ChatMlExperimentError(f"baseline evidence malformed for {case_id}") from error
        if baseline_bytes != original_body_bytes or baseline.get("httpStatus") != 200 or baseline.get("transportError"):
            raise ChatMlExperimentError(f"baseline call was not a successful exact-body response for {case_id}")
        if not isinstance(model, str) or _normal_path(model) != _normal_path(expected_model):
            raise ChatMlExperimentError(f"baseline model does not match pinned model for {case_id}")
        observed_baseline_models.add(model)
        if not isinstance(original_body, dict) or original_body.get("prompt") != prompt or \
                original_body.get("temperature") != 0 or original_body.get("n_predict") != 1024 or \
                original_body.get("stop") != ["<|im_end|>", "<|im_start|>"] or \
                not isinstance(original_body.get("grammar"), str):
            raise ChatMlExperimentError(f"baseline Core body settings malformed for {case_id}")

        variant_prompt = CHATML_PREFIX + prompt + CHATML_SUFFIX
        variant_body = copy.deepcopy(original_body)
        variant_body["prompt"] = variant_prompt
        variant_body_text = _canonical_json(variant_body)
        variant_body_bytes = variant_body_text.encode("utf-8")
        variant_prompt_hash = _sha256(variant_prompt.encode("utf-8"))
        variant_body_hash = _sha256(variant_body_bytes)
        original_body_hash = _sha256(original_body_bytes)
        grammar_hash = _sha256(original_body["grammar"].encode("utf-8"))
        scenario_id = case_id.rsplit("/", 1)[1]
        if scenario_id in CAUSE_UNKNOWN_SCENARIOS:
            continue
        variant_requests.append({
            "recordType": "prepared-chatml-appraisal-request-v1",
            "experimentId": EXPERIMENT_ID,
            "fixtureId": source["fixtureId"],
            "fixtureSha256": source["fixtureSha256"],
            "caseId": case_id,
            "originalPromptSha256": source["requestPromptSha256"],
            "originalBodySha256": original_body_hash,
            "variantPromptSha256": variant_prompt_hash,
            "variantBodySha256": variant_body_hash,
            "completionRequestBody": variant_body_text,
        })
        pair_manifest.append({
            "recordType": "appraisal-chatml-paired-manifest-v1",
            "experimentId": EXPERIMENT_ID,
            "fixtureId": source["fixtureId"],
            "fixtureSha256": source["fixtureSha256"],
            "caseId": case_id,
            "originalPromptSha256": source["requestPromptSha256"],
            "originalBodySha256": original_body_hash,
            "variantPromptSha256": variant_prompt_hash,
            "variantBodySha256": variant_body_hash,
            "grammarSha256": grammar_hash,
            "temperature": original_body["temperature"],
            "nPredict": original_body["n_predict"],
            "stop": original_body["stop"],
            "modelPath": expected_model,
            "modelSha256": expected_hash.lower(),
        })
    if observed_baseline_models != {expected_model} and \
            not all(_normal_path(item) == _normal_path(expected_model) for item in observed_baseline_models):
        raise ChatMlExperimentError("baseline model identity changed across cases")
    if len(variant_requests) != 24 or len(pair_manifest) != 24:
        raise ChatMlExperimentError(f"Expected 24 cause-valid pairs, got {len(variant_requests)}")
    return variant_requests, pair_manifest


def write_experiment_artifacts(prepared_rows: list[dict], baseline_rows: list[dict], model_pin: dict,
                               output_root: Path) -> dict:
    output_root = Path(output_root).resolve()
    if output_root.exists():
        raise FileExistsError(f"Experiment output directory already exists: {output_root}")
    requests, manifest = build_chatml_pairs(prepared_rows, baseline_rows, model_pin)
    output_root.mkdir(parents=True, exist_ok=False)
    _write_jsonl_new(output_root / "chatml-requests.jsonl", requests)
    _write_jsonl_new(output_root / "paired-manifest.jsonl", manifest)
    summary = {
        "schemaVersion": 1,
        "experimentId": EXPERIMENT_ID,
        "sourceFixtureId": "npc-experience-appraisal-live-scenarios-v1",
        "sourceCaseCount": 32,
        "experimentCaseCount": 24,
        "excludedCauseUnknownScenarios": sorted(CAUSE_UNKNOWN_SCENARIOS),
        "wrapper": {"prefix": CHATML_PREFIX, "suffix": CHATML_SUFFIX},
        "onlyChangedCompletionField": "prompt",
        "modelPath": model_pin["path"],
        "modelSha256": model_pin["sha256"].lower(),
        "temperature": 0,
        "nPredict": 1024,
        "stop": ["<|im_end|>", "<|im_start|>"],
        "requestFile": "chatml-requests.jsonl",
        "pairManifest": "paired-manifest.jsonl",
        "inferencePerformed": False,
    }
    with (output_root / "experiment.json").open("x", encoding="utf-8", newline="\n") as stream:
        json.dump(summary, stream, ensure_ascii=False, indent=2)
        stream.write("\n")
    return summary


def _record_chatml_core(prepared_path: Path, request_path: Path, manifest_path: Path,
                        response_path: Path, output_path: Path, project_path: Path, model_pin: dict) -> None:
    # Core's shared serial build is owned by the parent. Always select the same
    # configuration as that build so `--no-build` cannot silently run a stale Debug binary.
    command = ["dotnet", "run", "-c", "Release", "--no-build", "--project", str(project_path), "--",
               "record-appraisal-chatml-experiment", "--prepared", str(prepared_path),
               "--variant-requests", str(request_path), "--manifest", str(manifest_path),
               "--responses", str(response_path), "--output", str(output_path),
               "--model-path", model_pin["path"], "--model-sha256", model_pin["sha256"]]
    completed = subprocess.run(command, cwd=str(project_path.parent.parent.parent),
                               capture_output=True, text=True, check=False)
    if completed.returncode != 0:
        details = "\n".join(part.strip() for part in (completed.stdout, completed.stderr) if part and part.strip())
        if not details:
            details = "dotnet exited with code " + str(completed.returncode) + " without diagnostic output"
        raise RuntimeError("ChatML Core response validation failed (exit " + str(completed.returncode) + "):\n" + details)


def execute_chatml_experiment(experiment_root: Path, prepared_path: Path, baseline_raw_path: Path,
                              model_pin: dict, *, endpoint: str = "http://127.0.0.1:8080/completion",
                              transport=post_json_bytes, core_preflight=None, core_recorder=None,
                              project_path: Path | None = None) -> dict:
    """Send each prepared variant once, preserve raw bytes, then validate with Core response parser."""
    experiment_root = Path(experiment_root).resolve()
    prepared_path = Path(prepared_path).resolve()
    baseline_raw_path = Path(baseline_raw_path).resolve()
    _validate_loopback_endpoint(endpoint)
    if project_path is None:
        project_path = HERE / "NpcFactorEvaluation.csproj"
    project_path = Path(project_path).resolve()
    request_path = experiment_root / "chatml-requests.jsonl"
    manifest_path = experiment_root / "paired-manifest.jsonl"
    raw_path = experiment_root / "raw-results.jsonl"
    validated_path = experiment_root / "raw-results.validated.jsonl"
    if raw_path.exists() or validated_path.exists():
        raise FileExistsError("Experiment response outputs already exist; the runner never retries or resumes")

    prepared_rows = _read_jsonl(prepared_path)
    baseline_rows = _read_jsonl(baseline_raw_path)
    expected_requests, expected_manifest = build_chatml_pairs(prepared_rows, baseline_rows, model_pin)
    request_rows = _read_jsonl(request_path)
    manifest_rows = _read_jsonl(manifest_path)
    if request_rows != expected_requests or manifest_rows != expected_manifest:
        raise ChatMlExperimentError("Saved experiment request or paired manifest differs from reproducible source pairing")
    if core_preflight is None:
        _preflight_core(prepared_path, project_path, "v1")
    else:
        core_preflight(prepared_path, project_path)

    raw_results = []
    with raw_path.open("x", encoding="utf-8", newline="\n") as stream:
        for request_row, manifest in zip(request_rows, manifest_rows):
            body_text = request_row["completionRequestBody"]
            body_bytes = body_text.encode("utf-8", errors="strict")
            response = transport(endpoint, body_bytes)
            if not isinstance(response, dict):
                raise TypeError("Transport must return one response record per ChatML request")
            response_bytes = response.get("body", b"")
            if not isinstance(response_bytes, (bytes, bytearray)):
                response_bytes = b""
                transport_error = response.get("error") or "invalid_transport_body_type"
            else:
                response_bytes = bytes(response_bytes)
                transport_error = response.get("error")
            response_text, model_metadata, envelope_error = _completion_content(response_bytes)
            result = {
                "recordType": "appraisal-chatml-model-response-v1",
                "experimentId": EXPERIMENT_ID,
                "caseId": request_row["caseId"],
                "requestPromptSha256": request_row["originalPromptSha256"],
                "originalBodySha256": request_row["originalBodySha256"],
                "variantPromptSha256": request_row["variantPromptSha256"],
                "variantBodySha256": request_row["variantBodySha256"],
                "rawRequest": body_text,
                "rawRequestSha256": _sha256(body_bytes),
                "rawRequestBase64": base64.b64encode(body_bytes).decode("ascii"),
                "httpStatus": response.get("status"),
                "transportError": transport_error,
                "responseEnvelopeError": envelope_error,
                "responseText": response_text,
                "rawResponse": response_bytes.decode("utf-8", errors="replace"),
                "rawResponseBase64": base64.b64encode(response_bytes).decode("ascii"),
                "elapsedMs": float(response.get("elapsedMs", 0.0)),
                "responseHeaders": response.get("headers", {}),
                "modelMetadata": model_metadata,
            }
            raw_results.append(result)
            stream.write(json.dumps(result, ensure_ascii=False, separators=(",", ":")) + "\n")
            stream.flush()
            if response.get("status") == 200 and not transport_error and \
                    _normal_path(str(model_metadata.get("model", ""))) != _normal_path(str(model_pin["path"])):
                raise ChatMlExperimentError("Completion server reported model identity different from pinned baseline; stopped after preserving this response")

    if len(raw_results) != 24:
        raise ChatMlExperimentError(f"Expected 24 one-shot model attempts, got {len(raw_results)}")
    if core_recorder is None:
        _record_chatml_core(prepared_path, request_path, manifest_path, raw_path, validated_path,
                            project_path, model_pin)
    else:
        core_recorder(prepared_path, request_path, manifest_path, raw_path, validated_path)
    validated_rows = _read_jsonl(validated_path)
    expected_ids = {row["caseId"] for row in request_rows}
    if len(validated_rows) != 24 or {row.get("caseId") for row in validated_rows} != expected_ids or \
            any(not isinstance(row.get("coreResponseValidated"), bool) for row in validated_rows):
        raise ChatMlExperimentError("Core must return one response-only validation result for every ChatML case")
    accepted = sum(row["coreResponseValidated"] for row in validated_rows)
    return {
        "experimentId": EXPERIMENT_ID,
        "modelCalls": len(raw_results),
        "coreAccepted": accepted,
        "coreRejected": 24 - accepted,
        "rawResponses": str(raw_path),
        "coreValidation": str(validated_path),
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Prepare paired ChatML-wrapped v1 appraisal requests; no inference by default.")
    parser.add_argument("--prepared", type=Path, required=True, help="Frozen v1 requests.jsonl")
    parser.add_argument("--baseline-raw", type=Path, required=True, help="Completed v1 raw-results.jsonl")
    parser.add_argument("--model-pin", type=Path, default=DEFAULT_MODEL_PIN,
                        help="download-verified.json for the exact baseline Qwen model")
    parser.add_argument("--output-root", type=Path, required=True, help="New, empty experiment directory")
    parser.add_argument("--execute", action="store_true", help="Send 24 requests once and record response-only Core validation")
    parser.add_argument("--endpoint", default="http://127.0.0.1:8080/completion")
    parser.add_argument("--core-project", type=Path, default=HERE / "NpcFactorEvaluation.csproj")
    args = parser.parse_args(argv)
    try:
        model_pin = json.loads(args.model_pin.read_text(encoding="utf-8"))
        if args.execute:
            summary = execute_chatml_experiment(args.output_root, args.prepared, args.baseline_raw,
                                                model_pin, endpoint=args.endpoint,
                                                project_path=args.core_project)
        else:
            prepared_rows = _read_jsonl(args.prepared)
            baseline_rows = _read_jsonl(args.baseline_raw)
            summary = write_experiment_artifacts(prepared_rows, baseline_rows, model_pin, args.output_root)
    except Exception as error:
        print(f"APPRAISAL_CHATML_PREPARE_FAILED: {error}", file=sys.stderr)
        return 1
    print(json.dumps(summary, ensure_ascii=False, separators=(",", ":")))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
