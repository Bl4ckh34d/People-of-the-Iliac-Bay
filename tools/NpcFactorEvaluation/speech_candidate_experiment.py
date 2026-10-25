"""Paired Qwen appraisal probe with recorded D3 speech candidates as advisory context."""

from __future__ import annotations

import argparse
import base64
import copy
import hashlib
import json
import subprocess
import sys
from pathlib import Path
from typing import Any, Callable

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
ROOT = HERE.parents[1]
DEFAULT_SUITE = ROOT / "tools/NpcArchitectureEval/fixtures/speech-act-choice-diagnostic-v1.json"
FROZEN_PREPARED_PATH = ROOT / "work/npc-factor-evaluation/overnight-appraisal-v2-numeric-bounds-current-20261021/requests.jsonl"
EXPECTED_PREPARED_SHA256 = "69b9fc70a8f26c998ada698847bd373c44ce4101af55743544cbeb1aecf73c86"
EXPECTED_SUITE_SHA256 = "7a48f04a3ced01f891903d90452c28945683838de633b31d8b20f958a2f9456d"
EXPECTED_D3_RESULTS_SHA256 = "73a384283e93412906ca3cee1d91dba405d52b2de414f489c8af437d76a60587"
EXPERIMENT_ID = "npc-appraisal-d3-speech-candidate-ab-v1"
CANDIDATE_NOTE_TEMPLATE = (
    "\n\nPrior model suggestion from D3 (fallible model output, not an observation or ground truth): "
    "speech-act class = {label}. Assess the supplied event yourself; do not mechanically copy this suggestion."
)
EXACT_CASE_PREFIX = "core-exact:"
SPEECH_SCENARIOS = {
    "observed-direct-threat",
    "observed-quoted-threat",
    "observed-hypothetical-threat",
}
ALLOWED_CANDIDATE_LABELS = {
    "DirectThreat",
    "QuotedOrReportedThreat",
    "HypotheticalThreat",
}
Transport = Callable[[str, bytes], dict[str, Any]]


class SpeechCandidateExperimentError(ValueError):
    """Frozen source pairing or request binding is invalid."""


def _sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def _canonical_json(value: Any) -> str:
    return json.dumps(value, ensure_ascii=False, separators=(",", ":"))


def _candidate_source_digest(row: dict[str, Any]) -> str:
    option_ids = [item.get("id", "") for item in row.get("options", []) if isinstance(item, dict)]
    fields = [row.get("id", ""), row.get("model", ""), row.get("arm", ""),
              row.get("choice", ""), str(row.get("status", "")),
              row.get("technicalError") or "", str(bool(row.get("unofferedChoice"))).lower(),
              "\x1f".join(option_ids)]
    return _sha256("\n".join(str(value) for value in fields).encode("utf-8"))


def load_jsonl(path: Path) -> list[dict[str, Any]]:
    rows = []
    for line_number, line in enumerate(Path(path).read_text(encoding="utf-8-sig").splitlines(), 1):
        if not line.strip():
            continue
        try:
            row = json.loads(line)
        except json.JSONDecodeError as error:
            raise SpeechCandidateExperimentError(f"Invalid JSONL row {line_number}: {error}") from error
        if not isinstance(row, dict):
            raise SpeechCandidateExperimentError(f"JSONL row {line_number} must be an object")
        rows.append(row)
    return rows


def _unique_map(rows: list[dict[str, Any]], key: str, label: str) -> dict[str, dict[str, Any]]:
    result = {}
    for row in rows:
        value = row.get(key)
        if not isinstance(value, str) or not value or value in result:
            raise SpeechCandidateExperimentError(f"{label} identifiers must be present and unique")
        result[value] = row
    return result


def _suite_exact_cases(suite: dict[str, Any]) -> list[dict[str, Any]]:
    if suite.get("schemaVersion") != 1 or suite.get("suiteId") != "npc-speech-act-choice-diagnostic-v1":
        raise SpeechCandidateExperimentError("Unexpected frozen speech-act diagnostic suite")
    provenance = suite.get("provenance", "")
    if EXPECTED_PREPARED_SHA256 not in provenance:
        raise SpeechCandidateExperimentError("Frozen speech suite does not name the exact source preparation hash")
    cases = [case for case in suite.get("cases", [])
             if isinstance(case.get("id"), str) and case["id"].startswith(EXACT_CASE_PREFIX)]
    if len(cases) != 12:
        raise SpeechCandidateExperimentError(f"Expected 12 exact speech cases, got {len(cases)}")
    case_ids = [case["id"] for case in cases]
    counts: dict[str, int] = {}
    for case_id in case_ids:
        profile_id = case_id[len(EXACT_CASE_PREFIX):].split("/", 1)[0]
        counts[profile_id] = counts.get(profile_id, 0) + 1
    if len(set(case_ids)) != 12 or len(counts) != 4 or any(count != 3 for count in counts.values()):
        raise SpeechCandidateExperimentError("Exact speech suite must contain three cases for each of four distinct profiles")
    return cases


def _prepared_map(prepared_rows: list[dict[str, Any]], prepared_sha256: str) -> dict[str, dict[str, Any]]:
    if not (len(prepared_sha256) == 64 and all(c in "0123456789abcdef" for c in prepared_sha256)):
        raise SpeechCandidateExperimentError("Fresh prepared full-file SHA-256 is required")
    # This experiment reuses pinned historical D3 evidence. Validate against its
    # exact source fixture, not today's independently evolving live corpus.
    fixture_path = HERE / "fixtures" / "speech-candidate-appraisal-source-v2.json"
    summary = validate_rows(prepared_rows, fixture_path=fixture_path)
    if summary.get("fixtureId") != "npc-experience-appraisal-live-scenarios-v2" or len(prepared_rows) != 32:
        raise SpeechCandidateExperimentError("Input must be the complete frozen v2 production-Core preparation")
    return _unique_map(prepared_rows, "caseId", "prepared caseId")


def _prompt_value(prompt: str, prefix: str, case_id: str) -> str:
    matches = [line[len(prefix):] for line in prompt.splitlines() if line.startswith(prefix)]
    if len(matches) != 1:
        raise SpeechCandidateExperimentError(f"Cannot prove semantic projection for {case_id}: expected one {prefix!r} field")
    return matches[0]


def _unquote_field(value: str, field: str, case_id: str) -> str:
    if len(value) < 2 or value[0] != '"' or value[-1] != '"':
        raise SpeechCandidateExperimentError(f"Cannot prove semantic projection for {case_id}: malformed {field}")
    return value[1:-1]


def _projection_from_prompt(prompt: str, case_id: str, suite_input: dict[str, Any]) -> dict[str, Any]:
    """Project only facts present in Core's exact prompt; suite-owned task/options stay pinned by suite hash."""
    npc_id = case_id.split("/", 1)[0]
    try:
        event = _prompt_value(prompt, "event=", case_id)
        traits = _unquote_field(_prompt_value(prompt, "stablePersonality=", case_id), "stablePersonality", case_id)
        if not traits.startswith("Stable personality traits: "):
            raise SpeechCandidateExperimentError(f"Cannot prove semantic projection for {case_id}: malformed stable-trait projection")
        mood = _prompt_value(prompt, "currentMood=", case_id).split(";", 1)[0]
        mood_revision = _prompt_value(prompt, "currentMood=", case_id).split("moodRevision=", 1)[1]
        affinity = _prompt_value(prompt, "affinity=", case_id)
        relationship_line = _prompt_value(prompt, "relationship=", case_id)
        relationship_parts = relationship_line.split("; relationshipRevision=", 1)
        if len(relationship_parts) != 2:
            raise SpeechCandidateExperimentError(f"Cannot prove semantic projection for {case_id}: malformed relationship revision")
        relationship, relationship_revision = relationship_parts
        background_line = _prompt_value(prompt, "observerBackground=", case_id)
        background_parts = background_line.split("; statusAttitude=", 1)
        if len(background_parts) != 2:
            raise SpeechCandidateExperimentError(f"Cannot prove semantic projection for {case_id}: malformed observer background")
        background = background_parts[0]
        status_attitude = _unquote_field(background_parts[1], "statusAttitude", case_id)
        captured_time = _prompt_value(prompt, "capturedGameTime=", case_id)
        commitments = _prompt_value(prompt, "commitments=", case_id)
        consequences = _prompt_value(prompt, "eligibleConsequences=", case_id)
        rank_id = _unquote_field(_prompt_value(prompt, "knownSpeakerRankId=", case_id), "knownSpeakerRankId", case_id)
        family_ids = _prompt_value(prompt, "knownFamilyIds=", case_id)
        utterance = _unquote_field(_prompt_value(prompt, "observedUtterance=", case_id), "observedUtterance", case_id)
        history = _prompt_value(prompt, "conversationContext=", case_id)
    except IndexError as error:
        raise SpeechCandidateExperimentError(f"Cannot prove semantic projection for {case_id}: malformed context field") from error

    expected_mood = suite_input["npc"]["mood"].split("; moodRevision=", 1)
    if len(expected_mood) != 2 or expected_mood[1] != mood_revision:
        raise SpeechCandidateExperimentError(f"Semantic projection differs from frozen D3 suite: {case_id} mood revision")
    if history:
        raise SpeechCandidateExperimentError(f"Cannot prove semantic projection for {case_id}: nonempty conversation history requires a new D3 run")

    projected = copy.deepcopy(suite_input)
    projected["npc"]["id"] = npc_id
    projected["npc"]["personality"]["stableTraits"] = traits
    projected["npc"]["mood"] = mood + "; moodRevision=" + mood_revision
    projected["npc"]["affinity"] = affinity
    projected["npc"]["relationship"] = relationship + "; relationshipRevision=" + relationship_revision
    projected["world"]["observedEvent"] = event
    projected["world"]["capturedGameTime"] = captured_time
    projected["world"]["commitments"] = commitments
    projected["world"]["eligibleConsequences"] = consequences
    projected["world"]["knownSpeakerRankId"] = json.dumps(rank_id, ensure_ascii=False)
    projected["world"]["knownFamilyIds"] = family_ids
    projected["world"]["sourceHistory"] = history
    projected["history"] = []
    projected["playerText"] = utterance
    projected["availableTools"] = []
    if projected != suite_input:
        differing = [key for key in projected if projected[key] != suite_input.get(key)]
        raise SpeechCandidateExperimentError(f"Semantic projection differs from frozen D3 suite: {case_id} fields={differing}")
    return {
        "speechSuiteInput": projected,
        "corePromptObserverBackground": background,
        "corePromptStatusAttitude": status_attitude,
    }


def _verify_semantic_projection(prepared_by_case: dict[str, dict[str, Any]], cases: list[dict[str, Any]]) -> dict[str, str]:
    if not FROZEN_PREPARED_PATH.is_file() or _sha256(FROZEN_PREPARED_PATH.read_bytes()) != EXPECTED_PREPARED_SHA256:
        raise SpeechCandidateExperimentError("Pinned legacy D3 source preparation is unavailable or changed; cannot prove candidate lineage")
    old_rows = _read_jsonl(FROZEN_PREPARED_PATH)
    old_by_case = _unique_map(old_rows, "caseId", "legacy prepared caseId")
    if len(old_by_case) != 32:
        raise SpeechCandidateExperimentError("Pinned legacy D3 source must contain all 32 production-Core rows")
    digests: dict[str, str] = {}
    for case in cases:
        case_id = case["id"][len(EXACT_CASE_PREFIX):]
        if case_id not in prepared_by_case or case_id not in old_by_case:
            raise SpeechCandidateExperimentError(f"Missing source row for semantic projection: {case_id}")
        suite_input = case["input"]
        current_projection = _projection_from_prompt(prepared_by_case[case_id]["requestPrompt"], case_id, suite_input)
        old_projection = _projection_from_prompt(old_by_case[case_id]["requestPrompt"], case_id, suite_input)
        if current_projection != old_projection:
            raise SpeechCandidateExperimentError(f"Semantic projection changed since frozen D3 source: {case_id}")
        digests[case_id] = _sha256(_canonical_json(current_projection).encode("utf-8"))
    return digests


def build_variant_requests(prepared_rows: list[dict[str, Any]], suite: dict[str, Any],
                           d3_rows: list[dict[str, Any]], model_path: str, model_sha256: str,
                           *, prepared_sha256: str = EXPECTED_PREPARED_SHA256,
                           suite_sha256: str = EXPECTED_SUITE_SHA256,
                           d3_results_sha256: str = EXPECTED_D3_RESULTS_SHA256
                           ) -> tuple[list[dict[str, Any]], list[dict[str, Any]]]:
    """Build exact 12-pair A/B inputs; B differs only by one advisory prompt note."""
    if suite_sha256 != EXPECTED_SUITE_SHA256 or d3_results_sha256 != EXPECTED_D3_RESULTS_SHA256:
        raise SpeechCandidateExperimentError("Speech suite or canonical D3 evidence hash changed")
    if not isinstance(model_path, str) or not model_path.strip() or \
            not isinstance(model_sha256, str) or not (len(model_sha256) == 64 and all(c in "0123456789abcdefABCDEF" for c in model_sha256)):
        raise SpeechCandidateExperimentError("Pinned model path and 64-digit SHA-256 are required")

    prepared_by_case = _prepared_map(prepared_rows, prepared_sha256)
    cases = _suite_exact_cases(suite)
    projection_digests = _verify_semantic_projection(prepared_by_case, cases)
    d3_by_id_arm = {}
    for row in d3_rows:
        if row.get("model") != "d3" or row.get("arm") != "original":
            continue
        case_id = row.get("id")
        if isinstance(case_id, str) and case_id.startswith(EXACT_CASE_PREFIX):
            case_id = case_id[len(EXACT_CASE_PREFIX):]
            if case_id in d3_by_id_arm:
                raise SpeechCandidateExperimentError("Duplicate canonical D3 result for an exact speech case")
            d3_by_id_arm[case_id] = row
    expected_ids = set()
    for case in cases:
        suite_id = case["id"]
        case_id = suite_id[len(EXACT_CASE_PREFIX):]
        expected_ids.add(case_id)
        if case_id not in prepared_by_case:
            raise SpeechCandidateExperimentError(f"Missing prepared Core speech case: {case_id}")
        if case_id not in d3_by_id_arm:
            raise SpeechCandidateExperimentError(f"Missing canonical D3 result: {case_id}")
        profile_id, scenario_id = case_id.split("/", 1)
        if profile_id != case["input"]["npc"]["id"] or scenario_id not in SPEECH_SCENARIOS:
            raise SpeechCandidateExperimentError(f"D3 input identity does not match prepared Core case: {case_id}")
        d3 = d3_by_id_arm[case_id]
        if d3.get("status") != 200 or d3.get("technicalError") or d3.get("unofferedChoice"):
            raise SpeechCandidateExperimentError(f"Invalid canonical D3 result; cannot construct candidate: {case_id}")
        choice = d3.get("choice")
        if choice not in ALLOWED_CANDIDATE_LABELS:
            raise SpeechCandidateExperimentError(f"Canonical D3 label is not one of the exact suite categories: {case_id}")
        offered = {item.get("id") for item in d3.get("options", []) if isinstance(item, dict)}
        if choice not in offered:
            raise SpeechCandidateExperimentError(f"Canonical D3 label was not offered: {case_id}")
        if case["input"].get("playerText") not in prepared_by_case[case_id]["requestPrompt"]:
            raise SpeechCandidateExperimentError(f"Frozen utterance does not appear in prepared appraisal prompt: {case_id}")

    if expected_ids != set(d3_by_id_arm) or len(prepared_by_case) != 32:
        extras = set(d3_by_id_arm) - expected_ids
        if extras:
            raise SpeechCandidateExperimentError("Unexpected canonical D3 exact speech cases: " + ", ".join(sorted(extras)))
    variant_rows: list[dict[str, Any]] = []
    manifest_rows: list[dict[str, Any]] = []
    for index, case in enumerate(cases):
        case_id = case["id"][len(EXACT_CASE_PREFIX):]
        prepared = prepared_by_case[case_id]
        d3 = d3_by_id_arm[case_id]
        base_text = prepared["completionRequestBody"]
        base = json.loads(base_text)
        if base.get("prompt") != prepared["requestPrompt"]:
            raise SpeechCandidateExperimentError(f"Base completion body prompt mismatch: {case_id}")
        label = d3["choice"]
        note = CANDIDATE_NOTE_TEMPLATE.format(label=label)
        variant = copy.deepcopy(base)
        variant["prompt"] += note
        variant_text = _canonical_json(variant)
        body_hash_a = _sha256(base_text.encode("utf-8"))
        body_hash_b = _sha256(variant_text.encode("utf-8"))
        d3_digest = _candidate_source_digest(d3)
        case_arm_order = ["A", "B"] if index % 2 == 0 else ["B", "A"]
        manifest_rows.append({
            "recordType": "speech-candidate-pair-manifest-v1",
            "experimentId": EXPERIMENT_ID,
            "caseId": case_id,
            "pairIndex": index,
            "candidateLabel": label,
            "candidateSourceCaseId": case["id"],
            "candidateSourceArm": "original",
            "candidateSourceModel": "d3",
            "candidateSourceSha256": d3_digest,
            "sourcePreparedSha256": prepared_sha256,
            "legacyPreparedSha256": EXPECTED_PREPARED_SHA256,
            "semanticProjectionSha256": projection_digests[case_id],
            "sourceSuiteSha256": suite_sha256,
            "sourceD3ResultsSha256": d3_results_sha256,
            "sourcePromptSha256": prepared["requestPromptSha256"],
            "baseBodySha256": body_hash_a,
            "candidateBodySha256": body_hash_b,
            "grammarSha256": _sha256(base["grammar"].encode("utf-8")),
            "temperature": base.get("temperature"),
            "nPredict": base.get("n_predict"),
            "stop": base.get("stop"),
            "modelPath": model_path,
            "modelSha256": model_sha256.lower(),
            "armOrder": case_arm_order,
            "candidateNote": note,
        })
        for arm in case_arm_order:
            body_text = base_text if arm == "A" else variant_text
            variant_rows.append({
                "recordType": "prepared-speech-candidate-appraisal-request-v1",
                "experimentId": EXPERIMENT_ID,
                "caseId": case_id,
                "arm": arm,
                "candidateLabel": label,
                "candidateBodySha256": body_hash_b,
                "productionBodySha256": body_hash_a,
                "completionRequestSha256": _sha256(body_text.encode("utf-8")),
                "completionRequestBody": body_text,
            })
    if len(variant_rows) != 24 or len(manifest_rows) != 12:
        raise SpeechCandidateExperimentError("A/B design must contain exactly 12 pairs and 24 requests")
    return variant_rows, manifest_rows


def write_experiment_artifacts(prepared_rows: list[dict[str, Any]], suite: dict[str, Any],
                               d3_rows: list[dict[str, Any]], output_root: Path,
                               model_path: str, model_sha256: str, *,
                               prepared_sha256: str = EXPECTED_PREPARED_SHA256,
                               suite_sha256: str = EXPECTED_SUITE_SHA256,
                               d3_results_sha256: str = EXPECTED_D3_RESULTS_SHA256) -> dict[str, Any]:
    output_root = Path(output_root).resolve()
    if output_root.exists():
        raise FileExistsError(f"Experiment output directory already exists: {output_root}")
    variants, manifest = build_variant_requests(prepared_rows, suite, d3_rows, model_path, model_sha256,
                                                prepared_sha256=prepared_sha256,
                                                suite_sha256=suite_sha256,
                                                d3_results_sha256=d3_results_sha256)
    output_root.mkdir(parents=True, exist_ok=False)
    _write_jsonl_new(output_root / "variant-requests.jsonl", variants)
    _write_jsonl_new(output_root / "pair-manifest.jsonl", manifest)
    summary = {
        "schemaVersion": 1,
        "experimentId": EXPERIMENT_ID,
        "caseCount": 12,
        "requestCount": 24,
        "pairCount": 12,
        "sourcePreparedSha256": prepared_sha256,
        "legacyPreparedSha256": EXPECTED_PREPARED_SHA256,
        "sourceSuiteSha256": suite_sha256,
        "sourceD3ResultsSha256": d3_results_sha256,
        "semanticProjectionSha256": _sha256("\n".join(
            row["caseId"] + ":" + row["semanticProjectionSha256"] for row in manifest).encode("utf-8")),
        "onlyChangedPromptText": CANDIDATE_NOTE_TEMPLATE,
        "modelPath": model_path,
        "modelSha256": model_sha256.lower(),
        "inferencePerformed": False,
    }
    with (output_root / "experiment.json").open("x", encoding="utf-8", newline="\n") as stream:
        json.dump(summary, stream, ensure_ascii=False, indent=2)
        stream.write("\n")
    return summary


def _run_core_recorder(prepared_path: Path, suite_path: Path, d3_path: Path, variant_path: Path,
                       manifest_path: Path, raw_path: Path, output_path: Path,
                       model_path: str, model_sha256: str, project_path: Path) -> None:
    command = ["dotnet", "run", "-c", "Release", "--no-build", "--project", str(project_path), "--",
               "record-appraisal-speech-candidate", "--prepared", str(prepared_path),
               "--suite", str(suite_path), "--d3-results", str(d3_path),
               "--variant-requests", str(variant_path), "--manifest", str(manifest_path),
               "--responses", str(raw_path), "--output", str(output_path),
               "--model-path", model_path, "--model-sha256", model_sha256]
    completed = subprocess.run(command, cwd=str(ROOT), capture_output=True, text=True, check=False)
    if completed.returncode != 0:
        details = "\n".join(part.strip() for part in (completed.stdout, completed.stderr) if part and part.strip())
        if not details:
            details = "dotnet exited with code " + str(completed.returncode) + " without diagnostic output"
        raise RuntimeError("Speech-candidate Core response validation failed (exit " + str(completed.returncode) + "):\n" + details)


def execute_experiment(output_root: Path, *, prepared_path: Path, suite_path: Path, d3_results_path: Path,
                       model_path: str, model_sha256: str,
                       endpoint: str = "http://127.0.0.1:8080/completion",
                       transport: Transport = post_json_bytes, core_preflight=None,
                       core_recorder=None, project_path: Path | None = None) -> dict[str, Any]:
    output_root = Path(output_root).resolve()
    prepared_path, suite_path, d3_results_path = map(Path, (prepared_path, suite_path, d3_results_path))
    prepared_path, suite_path, d3_results_path = (p.resolve() for p in (prepared_path, suite_path, d3_results_path))
    variant_path = output_root / "variant-requests.jsonl"
    manifest_path = output_root / "pair-manifest.jsonl"
    raw_path = output_root / "raw-results.jsonl"
    validated_path = output_root / "core-validation.jsonl"
    if raw_path.exists() or validated_path.exists():
        raise FileExistsError("Experiment result paths exist; driver never overwrites or resumes")
    prepared_bytes, suite_bytes, d3_bytes = prepared_path.read_bytes(), suite_path.read_bytes(), d3_results_path.read_bytes()
    prepared_hash, suite_hash, d3_hash = map(_sha256, (prepared_bytes, suite_bytes, d3_bytes))
    if (suite_hash, d3_hash) != (EXPECTED_SUITE_SHA256, EXPECTED_D3_RESULTS_SHA256):
        raise SpeechCandidateExperimentError("Frozen suite or D3 result bytes changed")
    prepared_rows = _read_jsonl(prepared_path)
    suite = json.loads(suite_bytes.decode("utf-8"))
    d3_rows = _read_jsonl(d3_results_path)
    expected_variants, expected_manifest = build_variant_requests(
        prepared_rows, suite, d3_rows, model_path, model_sha256,
        prepared_sha256=prepared_hash, suite_sha256=suite_hash, d3_results_sha256=d3_hash)
    if load_jsonl(variant_path) != expected_variants or load_jsonl(manifest_path) != expected_manifest:
        raise SpeechCandidateExperimentError("Saved A/B requests or manifest differ from frozen source pairing")
    _validate_loopback_endpoint(endpoint)
    if project_path is None:
        project_path = HERE / "NpcFactorEvaluation.csproj"
    project_path = Path(project_path).resolve()
    if core_preflight is None:
        _preflight_core(prepared_path, project_path, "v2")
    else:
        core_preflight(prepared_path, project_path)

    raw_results = []
    with raw_path.open("x", encoding="utf-8", newline="\n") as stream:
        for request_row in expected_variants:
            body_text = request_row["completionRequestBody"]
            body_bytes = body_text.encode("utf-8", errors="strict")
            response = transport(endpoint, body_bytes)
            if not isinstance(response, dict):
                raise TypeError("Transport must return one response record per request")
            response_body = response.get("body", b"")
            if not isinstance(response_body, (bytes, bytearray)):
                response_body = b""
                transport_error = response.get("error") or "invalid_transport_body_type"
            else:
                response_body = bytes(response_body)
                transport_error = response.get("error")
            response_text, metadata, envelope_error = _completion_content(response_body)
            record = {
                "recordType": "speech-candidate-appraisal-model-response-v1",
                "experimentId": EXPERIMENT_ID,
                "caseId": request_row["caseId"],
                "arm": request_row["arm"],
                "candidateLabel": request_row["candidateLabel"],
                "productionBodySha256": request_row["productionBodySha256"],
                "candidateBodySha256": request_row["candidateBodySha256"],
                "rawRequest": body_text,
                "rawRequestSha256": _sha256(body_bytes),
                "rawRequestBase64": base64.b64encode(body_bytes).decode("ascii"),
                "httpStatus": response.get("status"),
                "transportError": transport_error,
                "responseEnvelopeError": envelope_error,
                "responseText": response_text,
                "rawResponse": response_body.decode("utf-8", errors="replace"),
                "rawResponseBase64": base64.b64encode(response_body).decode("ascii"),
                "elapsedMs": float(response.get("elapsedMs", 0.0)),
                "responseHeaders": response.get("headers", {}),
                "modelMetadata": metadata,
                "modelPath": model_path,
                "modelSha256": model_sha256.lower(),
            }
            raw_results.append(record)
            stream.write(json.dumps(record, ensure_ascii=False, separators=(",", ":")) + "\n")
            stream.flush()
            observed_model = metadata.get("model") if isinstance(metadata, dict) else None
            if response.get("status") == 200 and not transport_error and observed_model != model_path:
                raise SpeechCandidateExperimentError("Completion server model identity differs from pinned model; stopped after preserving response")
    if len(raw_results) != 24:
        raise SpeechCandidateExperimentError(f"Expected 24 one-pass requests, got {len(raw_results)}")

    if core_recorder is None:
        _run_core_recorder(prepared_path, suite_path, d3_results_path, variant_path,
                           manifest_path, raw_path, validated_path, model_path,
                           model_sha256, project_path)
    else:
        core_recorder(prepared_path, suite_path, d3_results_path, variant_path,
                      manifest_path, raw_path, validated_path, model_path,
                      model_sha256, project_path)
    validation_rows = load_jsonl(validated_path)
    response_keys = {(row["caseId"], row["arm"]) for row in expected_variants}
    validation_keys = {(row.get("caseId"), row.get("arm")) for row in validation_rows}
    if len(validation_rows) != 24 or validation_keys != response_keys or any(
            not isinstance(row.get("productionRequestExact"), bool) or
            not isinstance(row.get("coreParserValidated"), bool) or row.get("nativeCommitAvailable") is not False
            or (row.get("coreParserValidated") and not isinstance(row.get("candidateAgreement"), bool))
            or (not row.get("coreParserValidated") and row.get("candidateAgreement") is not None)
            for row in validation_rows):
        raise SpeechCandidateExperimentError("Core must return separate exact-request/parser/agreement fields for each pair arm")
    agreements = sum(row.get("candidateAgreement") is True for row in validation_rows)
    disagreements = sum(row.get("candidateAgreement") is False for row in validation_rows)
    return {
        "experimentId": EXPERIMENT_ID,
        "pairs": 12,
        "modelCalls": len(raw_results),
        "transportFailures": sum(bool(row["transportError"] or row["httpStatus"] != 200) for row in raw_results),
        "coreParserValidated": sum(row["coreParserValidated"] for row in validation_rows),
        "coreParserRejected": 24 - sum(row["coreParserValidated"] for row in validation_rows),
        "productionRequestExact": sum(row["productionRequestExact"] for row in validation_rows),
        "candidateAgreements": agreements,
        "candidateDisagreements": disagreements,
        "sourcePreparedSha256": prepared_hash,
        "legacyPreparedSha256": EXPECTED_PREPARED_SHA256,
        "semanticProjectionSha256": _sha256("\n".join(
            row["caseId"] + ":" + row["semanticProjectionSha256"] for row in expected_manifest).encode("utf-8")),
        "rawResults": str(raw_path),
        "coreValidation": str(validated_path),
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Build or run the frozen 12-pair D3 speech-candidate appraisal probe.")
    parser.add_argument("--prepared", required=True, type=Path, help="Fresh full v2 Core preparation with pinned source hash")
    parser.add_argument("--d3-results", required=True, type=Path, help="Frozen speech-act choice diagnostic results.jsonl")
    parser.add_argument("--suite", type=Path, default=DEFAULT_SUITE)
    parser.add_argument("--output-root", required=True, type=Path, help="New experiment directory")
    parser.add_argument("--model-path", required=True)
    parser.add_argument("--model-sha256", required=True)
    parser.add_argument("--execute", action="store_true", help="Send exactly 24 one-pass Qwen requests")
    parser.add_argument("--endpoint", default="http://127.0.0.1:8080/completion")
    parser.add_argument("--core-project", type=Path, default=HERE / "NpcFactorEvaluation.csproj")
    args = parser.parse_args(argv)
    try:
        prepared_path, suite_path, d3_path = map(Path, (args.prepared, args.suite, args.d3_results))
        prepared_hash, suite_hash, d3_hash = map(lambda path: _sha256(path.resolve().read_bytes()),
                                                  (prepared_path, suite_path, d3_path))
        prepared_rows = _read_jsonl(prepared_path)
        suite = json.loads(suite_path.read_text(encoding="utf-8"))
        d3_rows = _read_jsonl(d3_path)
        summary = write_experiment_artifacts(prepared_rows, suite, d3_rows, args.output_root,
                                             args.model_path, args.model_sha256,
                                             prepared_sha256=prepared_hash,
                                             suite_sha256=suite_hash,
                                             d3_results_sha256=d3_hash)
        if args.execute:
            summary = execute_experiment(args.output_root, prepared_path=prepared_path,
                                         suite_path=suite_path, d3_results_path=d3_path,
                                         model_path=args.model_path, model_sha256=args.model_sha256,
                                         endpoint=args.endpoint, project_path=args.core_project)
    except Exception as error:
        print(f"SPEECH_CANDIDATE_EXPERIMENT_FAILED: {error}", file=sys.stderr)
        return 1
    print(json.dumps(summary, ensure_ascii=False, separators=(",", ":")))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
