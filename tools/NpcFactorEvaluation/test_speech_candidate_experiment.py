from __future__ import annotations

import copy
import hashlib
import json
import tempfile
import unittest
from pathlib import Path

from speech_candidate_experiment import (
    CANDIDATE_NOTE_TEMPLATE,
    build_variant_requests,
    execute_experiment,
    load_jsonl,
    write_experiment_artifacts,
)


ROOT = Path(__file__).resolve().parents[2]
PREPARED = ROOT / "work/npc-factor-evaluation/overnight-appraisal-v2-numeric-bounds-current-20261021/requests.jsonl"
SUITE = ROOT / "tools/NpcArchitectureEval/fixtures/speech-act-choice-diagnostic-v1.json"
D3_RESULTS = ROOT / "work/npc-factor-evaluation/speech-act-choice-diagnostic-v1-run/results.jsonl"
MODEL_PATH = "C:/models/qwen.gguf"
MODEL_SHA = "a" * 64


def inputs():
    return (load_jsonl(PREPARED), json.loads(SUITE.read_text(encoding="utf-8")),
            load_jsonl(D3_RESULTS))


def rehash_row(row):
    row["requestPromptSha256"] = hashlib.sha256(row["requestPrompt"].encode("utf-8")).hexdigest()
    body = json.loads(row["completionRequestBody"])
    body["prompt"] = row["requestPrompt"]
    row["completionRequestBody"] = json.dumps(body, ensure_ascii=False, separators=(",", ":"))
    row["completionRequestSha256"] = hashlib.sha256(row["completionRequestBody"].encode("utf-8")).hexdigest()


class SpeechCandidateExperimentTests(unittest.TestCase):
    def test_builds_twelve_matched_pairs_with_only_candidate_prompt_note_changed(self):
        prepared, suite, d3_results = inputs()
        variants, manifest = build_variant_requests(prepared, suite, d3_results, MODEL_PATH, MODEL_SHA)
        self.assertEqual(24, len(variants))
        self.assertEqual(12, len(manifest))
        self.assertEqual(6, sum(pair["armOrder"] == ["A", "B"] for pair in manifest))
        self.assertEqual(6, sum(pair["armOrder"] == ["B", "A"] for pair in manifest))
        for pair in manifest:
            arms = {row["arm"]: row for row in variants if row["caseId"] == pair["caseId"]}
            self.assertEqual({"A", "B"}, set(arms))
            a, b = arms["A"], arms["B"]
            a_body = json.loads(a["completionRequestBody"])
            b_body = json.loads(b["completionRequestBody"])
            candidate_note = CANDIDATE_NOTE_TEMPLATE.format(label=pair["candidateLabel"])
            self.assertEqual(a_body["prompt"] + candidate_note, b_body["prompt"])
            b_without_prompt = copy.deepcopy(b_body)
            b_without_prompt.pop("prompt")
            a_without_prompt = copy.deepcopy(a_body)
            a_without_prompt.pop("prompt")
            self.assertEqual(a_without_prompt, b_without_prompt)
            self.assertEqual(a_body["grammar"], b_body["grammar"])
            self.assertEqual(a_body["temperature"], b_body["temperature"])
            self.assertEqual(a_body["n_predict"], b_body["n_predict"])
            self.assertEqual(a_body["stop"], b_body["stop"])
            self.assertIn("not an observation or ground truth", b_body["prompt"])
            self.assertNotIn("not an observation or ground truth", a_body["prompt"])
            self.assertIn(pair["candidateLabel"], {"DirectThreat", "QuotedOrReportedThreat", "HypotheticalThreat"})

    def test_semantic_projection_allows_new_core_prompt_hash_but_records_both_source_hashes(self):
        prepared, suite, d3_results = inputs()
        variants, manifest = build_variant_requests(
            prepared, suite, d3_results, MODEL_PATH, MODEL_SHA, prepared_sha256="b" * 64)
        self.assertEqual(24, len(variants))
        self.assertTrue(all(row["legacyPreparedSha256"] == "69b9fc70a8f26c998ada698847bd373c44ce4101af55743544cbeb1aecf73c86"
                            for row in manifest))
        self.assertTrue(all(row["sourcePreparedSha256"] == "b" * 64 for row in manifest))
        self.assertTrue(all(len(row["semanticProjectionSha256"]) == 64 for row in manifest))

    def test_rejects_changed_semantic_context_even_when_case_id_and_utterance_match(self):
        prepared, suite, d3_results = inputs()
        target = next(row for row in prepared if row["caseId"] ==
                      "npc:appraisal-commoner-steady/observed-direct-threat")
        target["requestPrompt"] = target["requestPrompt"].replace(
            "trust=55,confidence=20,evidenceCount=2", "trust=56,confidence=20,evidenceCount=2")
        rehash_row(target)
        with self.assertRaises(ValueError) as _raised_86:
            build_variant_requests(prepared, suite, d3_results, MODEL_PATH, MODEL_SHA,
                                  prepared_sha256="c" * 64)
        self.assertTrue(('semantic projection' in str(_raised_86.exception).casefold()), str(_raised_86.exception).casefold())

    def test_rejects_changed_trait_or_known_event_context(self):
        for old, new in (("warmth reserved", "warmth open"),
                         ("responsibleActorAttribution=Unknown", "responsibleActorAttribution=Known"),
                         ("knownSpeakerRankId=\"unknown\"", "knownSpeakerRankId=\"captain\"")):
            prepared, suite, d3_results = inputs()
            target = next(row for row in prepared if row["caseId"] ==
                          "npc:appraisal-commoner-steady/observed-direct-threat")
            target["requestPrompt"] = target["requestPrompt"].replace(old, new)
            rehash_row(target)
            with self.subTest(change=old), self.assertRaises(ValueError) as _raised_99:
                build_variant_requests(prepared, suite, d3_results, MODEL_PATH, MODEL_SHA,
                                      prepared_sha256="d" * 64)
            self.assertTrue(('semantic projection' in str(_raised_99.exception).casefold()), str(_raised_99.exception).casefold())

    def test_rejects_missing_or_failed_canonical_d3_candidate(self):
        prepared, suite, d3_results = inputs()
        changed = [copy.deepcopy(row) for row in d3_results]
        target = next(row for row in changed if row["model"] == "d3" and row["arm"] == "original")
        target["technicalError"] = "malformed_choice"
        with self.assertRaises(ValueError) as _raised_108:
            build_variant_requests(prepared, suite, changed, MODEL_PATH, MODEL_SHA)
        self.assertTrue(('canonical D3' in str(_raised_108.exception)), str(_raised_108.exception))

    def test_artifact_creation_uses_new_directory_only(self):
        prepared, suite, d3_results = inputs()
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / "ab"
            summary = write_experiment_artifacts(prepared, suite, d3_results, root, MODEL_PATH, MODEL_SHA)
            self.assertEqual(24, summary["requestCount"])
            self.assertEqual(12, summary["pairCount"])
            self.assertTrue((root / "variant-requests.jsonl").is_file())
            self.assertTrue((root / "pair-manifest.jsonl").is_file())
            with self.assertRaises(FileExistsError):
                write_experiment_artifacts(prepared, suite, d3_results, root, MODEL_PATH, MODEL_SHA)

    def test_fake_transport_records_exactly_one_attempt_per_arm_and_core_receipt(self):
        prepared, suite, d3_results = inputs()
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / "ab"
            write_experiment_artifacts(prepared, suite, d3_results, root, MODEL_PATH, MODEL_SHA)
            calls = []

            def fake_transport(endpoint, body):
                calls.append(body)
                if len(calls) % 6 == 0:
                    return {"status": 503, "body": b"unavailable", "elapsedMs": 2.0,
                            "headers": {}, "error": "HTTPError: 503"}
                response = json.dumps({"content": "{}", "model": MODEL_PATH}).encode("utf-8")
                return {"status": 200, "body": response, "elapsedMs": 1.25, "headers": {}, "error": None}

            def fake_core(prepared_path, suite_path, d3_path, variants_path, manifest_path,
                          raw_path, validated_path, model_path, model_sha256, project_path):
                raw = load_jsonl(raw_path)
                with validated_path.open("x", encoding="utf-8") as stream:
                    for item in raw:
                        stream.write(json.dumps({"caseId": item["caseId"], "arm": item["arm"],
                                                 "productionRequestExact": item["arm"] == "A",
                                                 "coreParserValidated": True,
                                                 "candidateAgreement": item["arm"] == "A",
                                                 "nativeCommitAvailable": False}) + "\n")

            summary = execute_experiment(root, prepared_path=PREPARED, suite_path=SUITE,
                                         d3_results_path=D3_RESULTS, model_path=MODEL_PATH,
                                         model_sha256=MODEL_SHA, transport=fake_transport,
                                         core_preflight=lambda prepared_path, project_path: None,
                                         core_recorder=fake_core)
            self.assertEqual(24, len(calls))
            self.assertEqual(24, summary["modelCalls"])
            self.assertEqual(12, summary["pairs"])
            self.assertEqual(4, summary["transportFailures"])
            self.assertEqual(12, summary["candidateAgreements"])
            self.assertEqual(12, summary["candidateDisagreements"])
            self.assertEqual(24, len(load_jsonl(root / "raw-results.jsonl")))
            self.assertEqual(24, len(load_jsonl(root / "core-validation.jsonl")))


if __name__ == "__main__":
    unittest.main()
