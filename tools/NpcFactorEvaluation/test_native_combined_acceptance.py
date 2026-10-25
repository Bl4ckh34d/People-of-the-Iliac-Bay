import copy
import hashlib
import json
import unittest

from native_combined_acceptance import AcceptanceError, CONTRACT_PATH, validate_evidence


def valid_evidence(assembly_sha256: str) -> dict:
    contract = json.loads(CONTRACT_PATH.read_text(encoding="utf-8"))
    return {
        "schemaVersion": 1,
        "acceptanceId": contract["acceptanceId"],
        "runId": "native-run-20261021-01",
        "modAssemblySha256": assembly_sha256,
        "fixture": copy.deepcopy(contract["fixture"]),
        "frameworkMarkers": list(contract["requiredFrameworkMarkers"]),
        "streaming": {
            "marker": contract["requiredStreamingMarker"],
            "sentenceCallbackCount": 2,
            "firstSentenceMonotonicSeconds": 12.25,
            "finalResultMonotonicSeconds": 13.50,
            "completeStructuredResult": True,
            "cancellation": {
                "requestedAfterFirstSentence": True,
                "terminalCallbackCount": 0,
                "completionCountDelta": 0,
                "moodCommitCallbackCount": 0,
                "moodBefore": {"value": 4, "acceptedTurnSequence": 3, "stateRevision": 5},
                "moodAfter": {"value": 4, "acceptedTurnSequence": 3, "stateRevision": 5},
            },
        },
    }


class NativeCombinedAcceptanceTests(unittest.TestCase):
    def setUp(self):
        self.contract = json.loads(CONTRACT_PATH.read_text(encoding="utf-8"))
        self.assembly_sha256 = hashlib.sha256(b"fresh mod build").hexdigest()
        self.evidence = valid_evidence(self.assembly_sha256)

    def test_accepts_both_native_suites_from_same_exact_assembly(self):
        result = validate_evidence(self.evidence, self.contract, self.assembly_sha256)
        self.assertEqual(result["status"], "pass")
        self.assertEqual(result["frameworkMarkersVerified"], 4)
        self.assertTrue(result["firstSentencePrecededFinal"])
        self.assertTrue(result["cancelledMoodUnchanged"])

    def test_rejects_evidence_from_another_or_stale_assembly(self):
        with self.assertRaises(AcceptanceError) as _raised_50:
            validate_evidence(self.evidence, self.contract, "0" * 64)
        self.assertTrue(('assembly SHA-256' in str(_raised_50.exception)), str(_raised_50.exception))

    def test_requires_framework_rollback_and_baseline_markers(self):
        self.evidence["frameworkMarkers"].remove("PEOPLE_OF_THE_ILIAC_BAY_MOOD_BASELINE_RESTORED")
        with self.assertRaises(AcceptanceError) as _raised_55:
            validate_evidence(self.evidence, self.contract, self.assembly_sha256)
        self.assertTrue(('missing framework acceptance markers' in str(_raised_55.exception)), str(_raised_55.exception))

    def test_requires_two_callbacks_and_early_sentence_delivery(self):
        self.evidence["streaming"]["sentenceCallbackCount"] = 1
        with self.assertRaises(AcceptanceError) as _raised_60:
            validate_evidence(self.evidence, self.contract, self.assembly_sha256)
        self.assertTrue(('at least two' in str(_raised_60.exception)), str(_raised_60.exception))

        self.evidence = valid_evidence(self.assembly_sha256)
        self.evidence["streaming"]["firstSentenceMonotonicSeconds"] = 13.5
        with self.assertRaises(AcceptanceError) as _raised_65:
            validate_evidence(self.evidence, self.contract, self.assembly_sha256)
        self.assertTrue(('must precede' in str(_raised_65.exception)), str(_raised_65.exception))

    def test_rejects_cancelled_stream_ghost_completion_or_mood_commit(self):
        self.evidence["streaming"]["cancellation"]["terminalCallbackCount"] = 1
        with self.assertRaises(AcceptanceError) as _raised_70:
            validate_evidence(self.evidence, self.contract, self.assembly_sha256)
        self.assertTrue(('terminalCallbackCount' in str(_raised_70.exception)), str(_raised_70.exception))

        self.evidence = valid_evidence(self.assembly_sha256)
        self.evidence["streaming"]["cancellation"]["moodAfter"]["stateRevision"] += 1
        with self.assertRaises(AcceptanceError) as _raised_75:
            validate_evidence(self.evidence, self.contract, self.assembly_sha256)
        self.assertTrue(('changed the native mood snapshot' in str(_raised_75.exception)), str(_raised_75.exception))

    def test_rejects_wrong_disposable_fixture_and_incomplete_result(self):
        self.evidence["fixture"]["save"] = "another save"
        with self.assertRaises(AcceptanceError) as _raised_80:
            validate_evidence(self.evidence, self.contract, self.assembly_sha256)
        self.assertTrue(('exact owned disposable fixture' in str(_raised_80.exception)), str(_raised_80.exception))

        self.evidence = valid_evidence(self.assembly_sha256)
        self.evidence["streaming"]["completeStructuredResult"] = False
        with self.assertRaises(AcceptanceError) as _raised_85:
            validate_evidence(self.evidence, self.contract, self.assembly_sha256)
        self.assertTrue(('complete validated structured result' in str(_raised_85.exception)), str(_raised_85.exception))

    def test_malformed_evidence_shape_returns_a_controlled_rejection(self):
        with self.assertRaises(AcceptanceError) as _raised_89:
            validate_evidence([], self.contract, self.assembly_sha256)
        self.assertTrue(('root must be an object' in str(_raised_89.exception)), str(_raised_89.exception))
        self.evidence["streaming"] = []
        with self.assertRaises(AcceptanceError) as _raised_92:
            validate_evidence(self.evidence, self.contract, self.assembly_sha256)
        self.assertTrue(('streaming evidence must be an object' in str(_raised_92.exception)), str(_raised_92.exception))


if __name__ == "__main__":
    unittest.main()
