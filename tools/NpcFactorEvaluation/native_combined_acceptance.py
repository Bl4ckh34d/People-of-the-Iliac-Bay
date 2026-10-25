"""Offline gate for combined native conversation-framework + stream evidence.

This validator never starts the game, a model, or a network request. Evidence is
accepted only when it identifies the exact local mod assembly and owned fixture.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path
from typing import Any


CONTRACT_PATH = Path(__file__).parent / "fixtures" / "native-framework-streaming-acceptance-v1.json"


class AcceptanceError(ValueError):
    pass


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _require(condition: bool, message: str) -> None:
    if not condition:
        raise AcceptanceError(message)


def _finite_number(value: Any, name: str) -> float:
    _require(isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value),
             f"{name} must be a finite number")
    return float(value)


def validate_evidence(evidence: dict[str, Any], contract: dict[str, Any], assembly_sha256: str) -> dict[str, Any]:
    _require(isinstance(evidence, dict), "evidence root must be an object")
    _require(isinstance(contract, dict), "acceptance contract root must be an object")
    _require(evidence.get("schemaVersion") == 1, "evidence schemaVersion must be 1")
    _require(evidence.get("acceptanceId") == contract.get("acceptanceId"), "acceptanceId does not match the frozen contract")
    _require(isinstance(evidence.get("runId"), str) and evidence["runId"].strip(), "runId is required")
    _require(evidence.get("modAssemblySha256") == assembly_sha256,
             "evidence assembly SHA-256 does not match the supplied current assembly")

    fixture = evidence.get("fixture")
    _require(isinstance(fixture, dict), "fixture evidence must be an object")
    expected_fixture = contract["fixture"]
    _require(fixture.get("character") == expected_fixture["character"] and
             fixture.get("save") == expected_fixture["save"],
             "native evidence is not from the exact owned disposable fixture")

    markers = evidence.get("frameworkMarkers")
    _require(isinstance(markers, list) and all(isinstance(item, str) for item in markers),
             "frameworkMarkers must be a string array")
    missing = [marker for marker in contract["requiredFrameworkMarkers"] if marker not in markers]
    _require(not missing, "missing framework acceptance markers: " + ", ".join(missing))

    streaming = evidence.get("streaming")
    _require(isinstance(streaming, dict), "streaming evidence must be an object")
    _require(streaming.get("marker") == contract["requiredStreamingMarker"], "streaming pass marker is missing")
    _require(streaming.get("completeStructuredResult") is True, "streaming success lacks a complete validated structured result")
    _require(isinstance(streaming.get("sentenceCallbackCount"), int) and
             streaming["sentenceCallbackCount"] >= contract["minimumSentenceCallbacks"],
             "streaming probe must receive at least two complete sentence callbacks")
    first = _finite_number(streaming.get("firstSentenceMonotonicSeconds"), "firstSentenceMonotonicSeconds")
    final = _finite_number(streaming.get("finalResultMonotonicSeconds"), "finalResultMonotonicSeconds")
    _require(first < final, "first sentence callback must precede the final structured-result callback")

    cancellation = streaming.get("cancellation")
    _require(isinstance(cancellation, dict), "stream cancellation evidence must be an object")
    _require(cancellation.get("requestedAfterFirstSentence") is True,
             "cancellation was not requested from the first-sentence callback")
    for field, maximum in (
        ("terminalCallbackCount", contract["maximumCancelledTerminalCallbacks"]),
        ("completionCountDelta", contract["maximumCancelledCompletionCountDelta"]),
        ("moodCommitCallbackCount", contract["maximumCancelledMoodCommitCallbacks"]),
    ):
        value = cancellation.get(field)
        _require(isinstance(value, int) and not isinstance(value, bool) and 0 <= value <= maximum,
                 f"cancelled stream {field} must be between 0 and {maximum}")
    mood_before = cancellation.get("moodBefore")
    mood_after = cancellation.get("moodAfter")
    _require(isinstance(mood_before, dict), "cancelled stream moodBefore must be an object")
    _require(isinstance(mood_after, dict), "cancelled stream moodAfter must be an object")
    mood_fields = ("value", "acceptedTurnSequence", "stateRevision")
    _require(all(isinstance(mood_before.get(field), int) and not isinstance(mood_before.get(field), bool)
                 for field in mood_fields), "cancelled stream moodBefore snapshot is incomplete")
    _require(all(isinstance(mood_after.get(field), int) and not isinstance(mood_after.get(field), bool)
                 for field in mood_fields), "cancelled stream moodAfter snapshot is incomplete")
    _require(all(mood_before[field] == mood_after[field] for field in mood_fields),
             "cancelled stream changed the native mood snapshot")

    return {
        "status": "pass",
        "acceptanceId": contract["acceptanceId"],
        "runId": evidence["runId"],
        "modAssemblySha256": assembly_sha256,
        "frameworkMarkersVerified": len(contract["requiredFrameworkMarkers"]),
        "streamingSentenceCallbacks": streaming["sentenceCallbackCount"],
        "firstSentencePrecededFinal": first < final,
        "cancelledTerminalCallbacks": cancellation["terminalCallbackCount"],
        "cancelledMoodUnchanged": True,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description="Validate offline combined native framework and social-stream acceptance evidence.")
    parser.add_argument("--evidence", required=True, type=Path)
    parser.add_argument("--mod-assembly", required=True, type=Path,
                        help="Exact freshly built mod assembly used for both native probes.")
    parser.add_argument("--contract", type=Path, default=CONTRACT_PATH)
    args = parser.parse_args()
    try:
        evidence = json.loads(args.evidence.read_text(encoding="utf-8"))
        contract = json.loads(args.contract.read_text(encoding="utf-8"))
        assembly_sha256 = sha256_file(args.mod_assembly)
        result = validate_evidence(evidence, contract, assembly_sha256)
    except (OSError, json.JSONDecodeError, KeyError, TypeError, AcceptanceError) as error:
        print(json.dumps({"status": "fail", "reason": str(error)}, ensure_ascii=False))
        return 1
    print(json.dumps(result, ensure_ascii=False, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
