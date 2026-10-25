"""Offline contract checks for production-Core appraisal preparation JSONL."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
from typing import Any


HERE = Path(__file__).resolve().parent
APPRAISAL_FIXTURE_PATH = HERE / "fixtures" / "appraisal-live-scenarios-v1.json"
APPRAISAL_V3_MANIFEST_PATH = HERE / "fixtures" / "appraisal-live-scenarios-v3.json"
ROW_KEYS = {
    "recordType", "fixtureId", "fixtureSha256", "caseId", "humanReview",
    "requestPrompt", "requestPromptSha256", "completionRequestBody", "completionRequestSha256",
    "syntheticFixture", "nativeCommitAvailable",
}


class PreparationContractError(ValueError):
    pass


def _sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def _require(condition: bool, message: str) -> None:
    if not condition:
        raise PreparationContractError(message)


def _load_fixture(path: Path) -> tuple[dict[str, Any], bytes]:
    raw = Path(path).read_bytes()
    fixture = json.loads(raw.decode("utf-8"))
    _require(isinstance(fixture, dict), "appraisal fixture root must be an object")
    return fixture, raw


def _v3_contract(manifest_path: Path) -> tuple[str, str, dict[str, str], dict[str, dict[str, Any]], set[str]]:
    manifest_path = Path(manifest_path).resolve()
    _require(manifest_path.name == "appraisal-live-scenarios-v3.json",
             "v3 manifest must use the literal appraisal-live-scenarios-v3.json filename")
    manifest_bytes = manifest_path.read_bytes()
    _require(0 < len(manifest_bytes) <= 16 * 1024, "v3 manifest is empty or exceeds its size bound")
    manifest = json.loads(manifest_bytes.decode("utf-8"))
    _require(isinstance(manifest, dict) and manifest.get("fixtureId") == "npc-experience-appraisal-live-scenarios-v3" and
             manifest.get("fixtureVersion") == 3 and manifest.get("expectedCaseCount") == 38,
             "unsupported v3 appraisal corpus manifest")
    base_name = manifest.get("baseFixtureFile")
    context_name = manifest.get("contextPairFixtureFile")
    _require(base_name == "appraisal-live-scenarios-v2.json" and context_name == "appraisal-context-pairs-v3.json",
             "v3 manifest child paths must match the two literal fixture filenames")
    base_path = manifest_path.parent / base_name
    context_path = manifest_path.parent / context_name
    base_fixture, base_bytes = _load_fixture(base_path)
    context_fixture, context_bytes = _load_fixture(context_path)
    _require(_sha256(base_bytes) == manifest.get("baseFixtureSha256"), "v3 base fixture SHA-256 mismatch")
    _require(_sha256(context_bytes) == manifest.get("contextPairFixtureSha256"), "v3 context-pair fixture SHA-256 mismatch")
    _require(base_fixture.get("fixtureId") == "npc-experience-appraisal-live-scenarios-v2" and
             base_fixture.get("fixtureVersion") == 2 and base_fixture.get("profileTraitCount") == 3,
             "v3 base fixture identity or three-trait profile contract is invalid")
    _require(context_fixture.get("fixtureId") == "npc-experience-appraisal-context-pairs-v3" and
             context_fixture.get("fixtureVersion") == 3 and context_fixture.get("baseFixtureId") == base_fixture["fixtureId"],
             "v3 context-pair fixture identity is invalid")

    labels: dict[str, str] = {}
    scenario_by_case: dict[str, dict[str, Any]] = {}
    hidden_ids: set[str] = set()
    for profile in base_fixture["profiles"]:
        for scenario in base_fixture["scenarios"]:
            case_id = f"{profile['personId']}/{scenario['id']}"
            _require(case_id not in labels, f"v3 base fixture contains duplicate caseId {case_id}")
            labels[case_id] = scenario["reviewLabel"]
            scenario_by_case[case_id] = scenario
    _require(len(context_fixture.get("pairs", [])) == 3, "v3 context fixture must contain exactly three pairs")
    for pair in context_fixture["pairs"]:
        scenario_id = pair["scenarioId"]
        scenario = next((item for item in base_fixture["scenarios"] if item["id"] == scenario_id), None)
        _require(scenario is not None, f"v3 pair references unknown base scenario {scenario_id}")
        pair_id = pair["pairId"]
        _require(pair_id not in hidden_ids, f"v3 fixture contains duplicate pairId {pair_id}")
        hidden_ids.add(pair_id)
        _require(len(pair.get("arms", [])) == 2, f"v3 pair {pair_id} must have exactly two arms")
        for arm in pair["arms"]:
            case_id = arm["caseId"]
            _require(case_id not in labels, f"v3 fixture contains duplicate caseId {case_id}")
            labels[case_id] = arm["reviewLabel"]
            scenario_by_case[case_id] = scenario
            hidden_ids.add(case_id)
        if "hiddenValue" in pair:
            hidden_ids.add(pair["hiddenValue"])
    _require(len(labels) == 38, f"v3 corpus must contain exactly 38 unique cases, got {len(labels)}")
    return manifest["fixtureId"], _sha256(manifest_bytes), labels, scenario_by_case, hidden_ids


def validate_rows(rows: Any, *, fixture_path: Path | None = None,
                  manifest_path: Path | None = None) -> dict[str, Any]:
    _require(isinstance(rows, list), "prepared appraisal rows must be a JSON array")
    _require(not (fixture_path is not None and manifest_path is not None),
             "select either a fixture file or a v3 manifest")
    hidden_ids: set[str] = set()
    if manifest_path is not None:
        fixture_id, fixture_hash, labels_by_case, scenario_by_case, hidden_ids = _v3_contract(manifest_path)
    else:
        fixture_path = Path(fixture_path or APPRAISAL_FIXTURE_PATH)
        fixture, fixture_bytes = _load_fixture(fixture_path)
        fixture_id = fixture["fixtureId"]
        fixture_hash = _sha256(fixture_bytes)
        labels_by_case = {}
        scenario_by_case = {}
        for profile in fixture["profiles"]:
            for scenario in fixture["scenarios"]:
                case_id = f"{profile['personId']}/{scenario['id']}"
                _require(case_id not in labels_by_case, f"fixture contains duplicate caseId {case_id}")
                labels_by_case[case_id] = scenario["reviewLabel"]
                scenario_by_case[case_id] = scenario

    _require(len(rows) == len(labels_by_case), f"expected {len(labels_by_case)} prepared rows, got {len(rows)}")
    seen: set[str] = set()
    labels: set[str] = set()
    for index, row in enumerate(rows):
        _require(isinstance(row, dict), f"row {index} must be an object")
        _require(not any(key.lower() in {"oracle", "truth", "nativefacts"} for key in row),
                 f"row {index} contains oracle data")
        _require(set(row) == ROW_KEYS, f"row {index} has unexpected or missing fields")
        _require(row["recordType"] == "prepared-production-core-appraisal", f"row {index} has wrong recordType")
        _require(row["fixtureId"] == fixture_id and row["fixtureSha256"] == fixture_hash,
                 f"row {index} does not identify the pinned appraisal fixture")
        case_id = row["caseId"]
        _require(isinstance(case_id, str) and case_id in labels_by_case, f"row {index} has unknown caseId")
        _require(case_id not in seen, f"duplicate caseId {case_id}")
        seen.add(case_id)
        label = labels_by_case[case_id]
        labels.add(label)
        review = row["humanReview"]
        _require(isinstance(review, dict) and review == {"label": label},
                 f"row {index} review label does not match fixture metadata")
        prompt = row["requestPrompt"]
        _require(isinstance(prompt, str) and prompt.strip(), f"row {index} has an empty requestPrompt")
        prompt_bytes = prompt.encode("utf-8")
        _require(row["requestPromptSha256"] == _sha256(prompt_bytes),
                 f"row {index} requestPrompt SHA-256 does not match exact prompt bytes")
        body_text = row["completionRequestBody"]
        _require(isinstance(body_text, str), f"row {index} completionRequestBody must be a string")
        body_bytes = body_text.encode("utf-8")
        _require(row["completionRequestSha256"] == _sha256(body_bytes),
                 f"row {index} completionRequestBody SHA-256 does not match exact body bytes")
        try:
            body = json.loads(body_text)
        except json.JSONDecodeError as error:
            raise PreparationContractError(f"row {index} completionRequestBody is invalid JSON") from error
        _require(isinstance(body, dict) and body.get("prompt") == prompt,
                 f"row {index} completion body prompt differs from Core requestPrompt")
        _require(isinstance(body.get("grammar"), str) and body["grammar"].strip(),
                 f"row {index} completion body has no structured output grammar")
        _require(body.get("n_predict") == 1024 and body.get("temperature") == 0 and
                 body.get("stop") == ["<|im_end|>", "<|im_start|>"],
                 f"row {index} completion body settings differ from the appraisal Core contract")
        _require(label.casefold() not in body_text.casefold(), f"row {index} review label leaked into completion request")
        scenario = scenario_by_case[case_id]
        scenario_id = scenario["id"]
        _require(scenario_id.casefold() not in body_text.casefold(),
                 f"row {index} scenario identifier leaked into completion request metadata")
        if manifest_path is not None:
            _require(case_id.casefold() not in body_text.casefold(),
                     f"row {index} context case identifier leaked into completion request metadata")
            _require(not any(value and value.casefold() in body_text.casefold() for value in hidden_ids),
                     f"row {index} review-only context metadata leaked into completion request")
        if scenario.get("responsibleActorAttribution") == "Unknown":
            _require("responsibleActorAttribution=Unknown,responsibleActor=unknown" in prompt,
                     f"row {index} cause-unknown event prompt does not preserve unknown attribution")
            _require("responsibleActor=player" not in prompt,
                     f"row {index} cause-unknown event prompt falsely identifies the player")
        _require(row["syntheticFixture"] is True, f"row {index} must be marked syntheticFixture")
        _require(row["nativeCommitAvailable"] is False, f"row {index} nativeCommitAvailable must be false")
    _require(seen == set(labels_by_case), "prepared rows do not cover every fixture case exactly once")
    return {
        "status": "pass",
        "fixtureId": fixture_id,
        "fixtureSha256": fixture_hash,
        "rowsVerified": len(rows),
        "completionBodiesVerified": len(rows),
        "labelsVerifiedOutsidePrompts": len(labels),
    }


def main() -> int:
    parser = argparse.ArgumentParser(description="Validate label isolation and hashes in appraisal preparation JSONL.")
    parser.add_argument("--prepared", required=True, type=Path, help="JSONL written by prepare-appraisal")
    parser.add_argument("--fixture-version", choices=("v1", "v2", "v3"), default="v1")
    args = parser.parse_args()
    try:
        raw_rows = [line for line in args.prepared.read_text(encoding="utf-8").splitlines() if line.strip()]
        rows = [json.loads(line) for line in raw_rows]
        if args.fixture_version == "v3":
            result = validate_rows(rows, manifest_path=APPRAISAL_V3_MANIFEST_PATH)
        else:
            fixture_path = HERE / "fixtures" / f"appraisal-live-scenarios-{args.fixture_version}.json"
            result = validate_rows(rows, fixture_path=fixture_path)
    except (OSError, json.JSONDecodeError, KeyError, TypeError, PreparationContractError) as error:
        print(json.dumps({"status": "fail", "reason": str(error)}, ensure_ascii=False))
        return 1
    print(json.dumps(result, ensure_ascii=False, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
