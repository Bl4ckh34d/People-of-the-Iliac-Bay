import json
import tempfile
import unittest
from pathlib import Path

from summarize_appraisal_semantic_review import build_review_report


def write_jsonl(path: Path, rows: list[dict]) -> None:
    path.write_text("".join(json.dumps(row, ensure_ascii=False) + "\n" for row in rows), encoding="utf-8")


class AppraisalSemanticReviewSummaryTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.prepared = self.root / "prepared.jsonl"
        self.raw = self.root / "raw.jsonl"
        self.validated = self.root / "validated.jsonl"
        self.rows = []
        raw_rows = []
        validated_rows = []
        for index in range(32):
            case_id = f"npc:review-profile/scenario-{index:02d}"
            prompt = (
                "event={origin=ObservedSpeech,kind=ObservedSpeech,responsibleActorAttribution=Unknown,responsibleActor=unknown}\n"
                'observedUtterance="The guard said, \\\"I might poison the well\\\"; suppose this were true."\n'
                "relationship=unknown\n"
            )
            self.rows.append({
                "recordType": "prepared-production-core-appraisal",
                "fixtureId": "fixture-v2",
                "fixtureSha256": "fixture-hash",
                "caseId": case_id,
                "requestPrompt": prompt,
                "requestPromptSha256": f"prompt-{index}",
                "humanReview": {"label": f"case label {index}"},
            })
            response = {
                "moodDelta": 0,
                "affinityDelta": 0,
                "trustDelta": 0,
                "resentmentDelta": 0,
                "fearDelta": 0,
                "speechActKind": "ReportedThreat",
                "speechTargetPersonId": "",
                "conversationOption": "NoAction",
            }
            raw_rows.append({
                "recordType": "appraisal-model-response-v1",
                "caseId": case_id,
                "requestPromptSha256": f"prompt-{index}",
                "httpStatus": 200,
                "transportError": None,
                "responseText": json.dumps(response),
            })
            validated_rows.append({
                "recordType": "core-validated-appraisal-result",
                "caseId": case_id,
                "fixtureId": "fixture-v2",
                "fixtureSha256": "fixture-hash",
                "requestPromptSha256": f"prompt-{index}",
                "httpStatus": 200,
                "transportError": None,
                "humanReview": {"label": f"case label {index}"},
                "coreValidated": True,
                "failureKind": None,
                "appraisal": response,
            })
        write_jsonl(self.prepared, self.rows)
        write_jsonl(self.raw, raw_rows)
        write_jsonl(self.validated, validated_rows)

    def tearDown(self):
        self.temp.cleanup()

    def test_report_requires_complete_matching_32_row_sets(self):
        report, summary = build_review_report(self.prepared, self.raw, self.validated)
        self.assertEqual(summary["caseCount"], 32)
        self.assertEqual(summary["coreAccepted"], 32)
        self.assertIn("Core acceptance is separate from semantic review", report)
        self.assertIn("ReportedThreat", report)
        self.assertIn("quote and hypothetical cases", report)
        self.assertIn("Reviewer judgment", report)

        write_jsonl(self.raw, [json.loads(line) for line in self.raw.read_text(encoding="utf-8").splitlines()[:-1]])
        with self.assertRaises(ValueError) as _raised_87:
            build_review_report(self.prepared, self.raw, self.validated)
        self.assertTrue(('exactly 32' in str(_raised_87.exception)), str(_raised_87.exception))

    def test_report_rejects_hash_mismatch(self):
        validated = [json.loads(line) for line in self.validated.read_text(encoding="utf-8").splitlines()]
        validated[0]["requestPromptSha256"] = "different"
        write_jsonl(self.validated, validated)
        with self.assertRaises(ValueError) as _raised_94:
            build_review_report(self.prepared, self.raw, self.validated)
        self.assertTrue(('prompt hash mismatch' in str(_raised_94.exception)), str(_raised_94.exception))


if __name__ == "__main__":
    unittest.main()
