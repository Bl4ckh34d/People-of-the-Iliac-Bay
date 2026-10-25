import hashlib
import io
import json
import sys
import tempfile
import unittest
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path
from unittest import mock

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

import run_conceptual_goal_pilot as pilot
import run_native_route_replay as route_replay


def _case(expected_grounding="plan", category="wait"):
    text = "Wait here for 20 seconds."
    return {
        "id": "case_1", "category": category,
        "snapshot": {"actor": {"id": "synthetic:npc"}, "scene": "fixture",
                     "facts": ["synthetic fact"], "pendingOffer": None, "activeCommitment": None,
                     "candidateProvenance": "synthetic fixture", "candidates": [
                         {"ref": "wait_here", "kind": "Wait", "label": "Wait at current position",
                          "target": {"Kind": "Person", "PersonId": "player"}}]},
        "playerText": text, "attributedHistory": [],
        "expected": {"conceptual": {"intent": "request", "steps": [
            {"kind": "Wait", "sourceQuote": "Wait here for 20 seconds", "targetText": "here",
             "durationAmount": 20, "durationUnit": "seconds"}], "conditions": [], "missing": []},
            "grounding": expected_grounding}}


class ConceptualGoalPilotTests(unittest.TestCase):
    def _files(self, temp, cases=None):
        cases = cases or [_case()]
        digest = route_replay.cases_sha256(cases)
        suite = {"schemaVersion": 1, "caseCount": len(cases), "casesSha256": digest, "cases": cases}
        suite_path = temp / "suite.json"
        suite_path.write_text(json.dumps(suite, ensure_ascii=False), encoding="utf-8")
        parent = temp / "runs"
        parent.mkdir()
        run_dir = parent / "fresh"
        args = ["--suite", str(suite_path), "--cases-sha256", digest,
                "--run-dir", str(run_dir), "--url", "http://127.0.0.1:8080", "--expected-model", "qwen-exact"]
        return suite_path, digest, run_dir, args

    def _outer(self, content):
        return {"model": "qwen-exact", "choices": [{"finish_reason": "stop", "message": {"content": json.dumps(content, ensure_ascii=False)}}]}

    def test_import_and_help_have_no_network_or_game_side_effects(self):
        with mock.patch.object(pilot, "game_closed", side_effect=AssertionError("game guard called")), \
             mock.patch.object(pilot, "post_json", side_effect=AssertionError("POST called")), \
             mock.patch.object(pilot, "get_json", side_effect=AssertionError("GET called")):
            with redirect_stdout(io.StringIO()), redirect_stderr(io.StringIO()):
                with self.assertRaises(SystemExit) as help_exit:
                    pilot.run(["--help"])
            self.assertEqual(help_exit.exception.code, 0)

    def test_game_guard_refuses_before_output_or_metadata_contact(self):
        with tempfile.TemporaryDirectory() as td:
            _suite_path, _digest, run_dir, args = self._files(Path(td))
            with mock.patch.object(pilot, "game_closed", side_effect=RuntimeError("game active")), \
                 mock.patch.object(pilot, "get_json") as get, mock.patch.object(pilot, "post_json") as post:
                with self.assertRaises(RuntimeError) as _raised_65:
                    pilot.run(args)
                self.assertTrue(('game active' in str(_raised_65.exception)), str(_raised_65.exception))
                self.assertFalse(run_dir.exists())
                get.assert_not_called()
                post.assert_not_called()

    def test_fresh_directory_and_loopback_validation_precede_network(self):
        with tempfile.TemporaryDirectory() as td:
            suite_path, digest, run_dir, args = self._files(Path(td))
            with mock.patch.object(pilot, "post_json") as post, mock.patch.object(pilot, "get_json") as get:
                with self.assertRaises(ValueError):
                    pilot._prepare(args[:-4] + ["--url", "https://example.com", "--expected-model", "qwen-exact"])
                (Path(td) / "already-exists").mkdir()
                with self.assertRaises(ValueError):
                    pilot._prepare(args + ["--run-dir", str(Path(td) / "already-exists")])
                post.assert_not_called()
                get.assert_not_called()

    def test_frozen_heldout_corpus_hash_and_gold_validate_offline(self):
        path = ROOT / "fixtures" / "conceptual-goal-heldout-v1.json"
        suite = route_replay._strict_json_loads(path.read_text(encoding="utf-8-sig"))
        self.assertEqual(suite["casesSha256"], "f9297c3aca22316665116163e82c29b55f3da2104b0e03c62f6701d82a937765")
        self.assertEqual(pilot._validate_suite(suite, suite["casesSha256"]), suite["casesSha256"])
        self.assertEqual(len(suite["cases"]), 28)

    def test_extraction_payload_excludes_gold_and_candidates(self):
        case = _case()
        payload = pilot.conceptual_protocol.conceptual_payload(case, "qwen-exact")
        user = payload["messages"][1]["content"]
        self.assertNotIn("wait_here", user)
        self.assertNotIn("expected", user)
        self.assertNotIn("candidates", user)

    def test_run_reuses_one_extraction_for_both_candidate_orders(self):
        with tempfile.TemporaryDirectory() as td:
            case = _case()
            case["snapshot"]["candidates"].append({"ref": "follow_player", "kind": "Follow",
                "label": "Follow player", "target": {"Kind": "Person", "PersonId": "player"}})
            _suite_path, _digest, run_dir, args = self._files(Path(td), [case])
            extraction = _case()["expected"]["conceptual"]
            grounding = {"bindings": [{"stepIndex": 0, "candidateRef": "wait_here"}]}
            calls = []

            def fake_post(url, payload, ledger, metadata, timeout):
                calls.append((url, payload, metadata))
                self.assertTrue((run_dir / "manifest.json").is_file())
                self.assertTrue((run_dir / "payloads" / "case-0001.extract.json").is_file())
                if metadata["stage"] == "grounding":
                    manifest = json.loads((run_dir / "manifest.json").read_text(encoding="utf-8"))
                    self.assertEqual(sum(item["stage"] == "grounding" for item in manifest["payloads"]), 2)
                    self.assertTrue(metadata["extractionRequestSha256"])
                body = self._outer(extraction if metadata["stage"] == "extraction" else grounding)
                return {"status_code": 200, "response_raw": json.dumps(body), "response_json": body,
                        "latency_ms": 4, "request_sha256": pilot._sha(pilot._json_bytes(payload))}

            model_list = {"data": [{"id": "qwen-exact"}]}
            with mock.patch.object(pilot, "game_closed") as guard, mock.patch.object(pilot, "get_json", return_value=model_list), \
                 mock.patch.object(pilot, "post_json", side_effect=fake_post):
                summary = pilot.run(args)
            self.assertEqual(guard.call_count, 5)
            self.assertEqual(summary["totals"]["extractionCalls"], 1)
            self.assertEqual(summary["totals"]["groundingCalls"], 2)
            extraction_calls = [c for c in calls if c[2]["stage"] == "extraction"]
            ground_calls = [c for c in calls if c[2]["stage"] == "grounding"]
            self.assertEqual(len(extraction_calls), 1)
            self.assertEqual(len(ground_calls), 2)
            self.assertNotEqual(json.loads(ground_calls[0][1]["messages"][1]["content"])["currentCandidates"],
                                json.loads(ground_calls[1][1]["messages"][1]["content"])["currentCandidates"])
            rows = [json.loads(line) for line in (run_dir / "results.jsonl").read_text(encoding="utf-8").splitlines()]
            self.assertEqual(len(rows), 2)
            self.assertEqual(rows[0]["extractionRequestSha256"], rows[1]["extractionRequestSha256"])

    def test_one_timeout_is_one_attempt_and_never_retries_or_grounds(self):
        with tempfile.TemporaryDirectory() as td:
            _suite_path, _digest, _run_dir, args = self._files(Path(td))
            def fake_post(_url, payload, _ledger, _metadata, timeout):
                return {"status_code": None, "transport_error": "timed out", "latency_ms": 10000,
                        "request_sha256": pilot._sha(pilot._json_bytes(payload))}
            with mock.patch.object(pilot, "game_closed"), mock.patch.object(pilot, "get_json", return_value={"data": [{"id": "qwen-exact"}]}), \
                 mock.patch.object(pilot, "post_json", side_effect=fake_post) as post:
                summary = pilot.run(args)
            self.assertEqual(post.call_count, 1)
            self.assertEqual(summary["totals"]["groundingCalls"], 0)
            self.assertEqual(summary["totals"]["extractionObserved"], 0)

    def test_metadata_model_mismatch_stops_before_any_inference(self):
        with tempfile.TemporaryDirectory() as td:
            _suite_path, _digest, _run_dir, args = self._files(Path(td))
            with mock.patch.object(pilot, "game_closed"), mock.patch.object(pilot, "get_json", return_value={"data": [{"id": "wrong"}]}), \
                 mock.patch.object(pilot, "post_json") as post:
                with self.assertRaises(RuntimeError) as _raised_155:
                    pilot.run(args)
                self.assertTrue(('does not exactly match' in str(_raised_155.exception)), str(_raised_155.exception))
                post.assert_not_called()

    def test_conditions_skip_grounding_deterministically_and_preserve_clarify(self):
        case = _case(expected_grounding="clarify")
        case["expected"]["conceptual"]["conditions"] = ["only after rain stops"]
        with tempfile.TemporaryDirectory() as td:
            _suite_path, _digest, run_dir, args = self._files(Path(td), [case])
            extraction = case["expected"]["conceptual"]
            def fake_post(_url, payload, _ledger, _metadata, timeout):
                body = self._outer(extraction)
                return {"status_code": 200, "response_raw": json.dumps(body), "response_json": body,
                        "latency_ms": 4, "request_sha256": pilot._sha(pilot._json_bytes(payload))}
            with mock.patch.object(pilot, "game_closed"), mock.patch.object(pilot, "get_json", return_value={"data": [{"id": "qwen-exact"}]}), \
                 mock.patch.object(pilot, "post_json", side_effect=fake_post) as post:
                summary = pilot.run(args)
            self.assertEqual(summary["totals"]["extractionCalls"], 1)
            self.assertEqual(summary["totals"]["groundingCalls"], 0)
            self.assertEqual(post.call_count, 1)
            rows = [json.loads(line) for line in (run_dir / "results.jsonl").read_text(encoding="utf-8").splitlines()]
            self.assertTrue(all(row["groundingOutcome"] == "clarify" for row in rows))

    def test_unavailable_follow_candidate_clarifies_without_grounding_call(self):
        case = _case(expected_grounding="clarify", category="unavailable_follow")
        case["playerText"] = "Follow me for 20 seconds."
        case["expected"]["conceptual"] = {"intent": "request", "steps": [
            {"kind": "Follow", "sourceQuote": "Follow me for 20 seconds", "targetText": "me",
             "durationAmount": 20, "durationUnit": "seconds"}], "conditions": [], "missing": []}
        with tempfile.TemporaryDirectory() as td:
            _suite_path, _digest, run_dir, args = self._files(Path(td), [case])
            extraction = case["expected"]["conceptual"]
            def fake_post(_url, payload, _ledger, metadata, timeout):
                body = self._outer(extraction)
                return {"status_code": 200, "response_raw": json.dumps(body), "response_json": body,
                        "latency_ms": 4, "request_sha256": pilot._sha(pilot._json_bytes(payload))}
            with mock.patch.object(pilot, "game_closed"), mock.patch.object(pilot, "get_json", return_value={"data": [{"id": "qwen-exact"}]}), \
                 mock.patch.object(pilot, "post_json", side_effect=fake_post) as post:
                summary = pilot.run(args)
            self.assertEqual(summary["totals"]["groundingCalls"], 0)
            self.assertEqual(summary["totals"]["groundingCorrect"], 2)
            self.assertEqual(post.call_count, 1)
            rows = [json.loads(line) for line in (run_dir / "results.jsonl").read_text(encoding="utf-8").splitlines()]
            self.assertTrue(all(row["groundingOutcome"] == "clarify" for row in rows))

    def test_plan_with_wrong_gold_action_or_duration_counts_as_false_plan(self):
        duration_case = _case(expected_grounding="plan", category="wrong_duration")
        duration_case["id"] = "wrong_duration"
        kind_case = _case(expected_grounding="plan", category="wrong_kind")
        kind_case["id"] = "wrong_kind"
        kind_case["playerText"] = "Follow me for 20 seconds."
        kind_case["snapshot"]["candidates"].append({"ref": "follow_player", "kind": "Follow",
            "label": "Follow player", "target": {"Kind": "Person", "PersonId": "player"}})
        kind_case["expected"]["conceptual"] = {"intent": "request", "steps": [
            {"kind": "Follow", "sourceQuote": "Follow me for 20 seconds", "targetText": "me",
             "durationAmount": 20, "durationUnit": "seconds"}], "conditions": [], "missing": []}
        with tempfile.TemporaryDirectory() as td:
            _suite_path, _digest, run_dir, args = self._files(Path(td), [duration_case, kind_case])
            actual = {
                "wrong_duration": {"intent": "request", "steps": [
                    {"kind": "Wait", "sourceQuote": "Wait here for 20 seconds", "targetText": "here",
                     "durationAmount": 30, "durationUnit": "seconds"}], "conditions": [], "missing": []},
                "wrong_kind": {"intent": "request", "steps": [
                    {"kind": "Wait", "sourceQuote": "Follow me for 20 seconds", "targetText": "me",
                     "durationAmount": 20, "durationUnit": "seconds"}], "conditions": [], "missing": []},
            }
            def fake_post(_url, payload, _ledger, metadata, timeout):
                if metadata["stage"] == "extraction":
                    value = actual[metadata["caseId"]]
                else:
                    ref = "wait_here"
                    value = {"bindings": [{"stepIndex": 0, "candidateRef": ref}]}
                body = self._outer(value)
                return {"status_code": 200, "response_raw": json.dumps(body), "response_json": body,
                        "latency_ms": 4, "request_sha256": pilot._sha(pilot._json_bytes(payload))}
            with mock.patch.object(pilot, "game_closed"), mock.patch.object(pilot, "get_json", return_value={"data": [{"id": "qwen-exact"}]}), \
                 mock.patch.object(pilot, "post_json", side_effect=fake_post):
                summary = pilot.run(args)
            self.assertEqual(summary["totals"]["plans"], 4)
            self.assertEqual(summary["totals"]["groundingCorrect"], 4)
            self.assertEqual(summary["totals"]["falsePlans"], 4)
            rows = [json.loads(line) for line in (run_dir / "results.jsonl").read_text(encoding="utf-8").splitlines()]
            self.assertTrue(all(row["groundingOutcomeCorrect"] for row in rows))
            self.assertTrue(all(row["falsePlan"] for row in rows))

    def test_false_plan_is_separate_and_target_correctness_not_claimed(self):
        case = _case(expected_grounding="clarify")
        case["playerText"] = "Lead me to the blue-tile bathhouse before it closes."
        case["category"] = "conditional_moveto"
        case["snapshot"]["candidates"] = [{"ref": "bathhouse", "kind": "MoveTo",
            "label": "Blue-tile bathhouse", "target": {"Kind": "Building", "SettlementId": 100, "BuildingKey": 51}}]
        expected_step = {"kind": "MoveTo", "sourceQuote": "Lead me to the blue-tile bathhouse",
                         "targetText": "blue-tile bathhouse"}
        case["expected"]["conceptual"] = {"intent": "request", "steps": [expected_step],
                                          "conditions": ["before it closes"], "missing": []}
        with tempfile.TemporaryDirectory() as td:
            _suite_path, _digest, _run_dir, args = self._files(Path(td), [case])
            extraction = {**case["expected"]["conceptual"], "conditions": []}
            def fake_post(url, payload, _ledger, metadata, timeout):
                value = extraction if metadata["stage"] == "extraction" else {"bindings": [{"stepIndex": 0, "candidateRef": "bathhouse"}]}
                body = self._outer(value)
                return {"status_code": 200, "response_raw": json.dumps(body), "response_json": body,
                        "latency_ms": 4, "request_sha256": pilot._sha(pilot._json_bytes(payload))}
            with mock.patch.object(pilot, "game_closed"), mock.patch.object(pilot, "get_json", return_value={"data": [{"id": "qwen-exact"}]}), \
                 mock.patch.object(pilot, "post_json", side_effect=fake_post):
                summary = pilot.run(args)
            self.assertEqual(summary["totals"]["falsePlans"], 2)
            rows = [json.loads(line) for line in (_run_dir / "results.jsonl").read_text(encoding="utf-8").splitlines()]
            self.assertTrue(all(row["candidateTargetCorrectnessProven"] is False for row in rows))
            self.assertTrue(all(row["targetFidelityProven"] is False for row in rows))
            self.assertTrue(all(row["conditionsPresenceCorrect"] is False for row in rows))
            self.assertTrue(all(row["groundingResponse"]["raw"] for row in rows))
            self.assertTrue(all(row["groundingBinding"]["bindings"][0]["candidateRef"] == "bathhouse" for row in rows))

    def test_same_kind_wrong_target_is_logged_without_claiming_target_fidelity(self):
        case = _case(expected_grounding="plan", category="follow_target")
        case["playerText"] = "Follow my cousin."
        case["snapshot"]["candidates"] = [
            {"ref": "follow_player", "kind": "Follow", "label": "Follow player", "target": {"Kind": "Person", "PersonId": "player"}},
            {"ref": "follow_cousin", "kind": "Follow", "label": "Follow cousin", "target": {"Kind": "Person", "PersonId": "cousin"}},
        ]
        case["expected"]["conceptual"] = {"intent": "request", "steps": [
            {"kind": "Follow", "sourceQuote": "Follow my cousin", "targetText": "my cousin",
             "durationAmount": None, "durationUnit": None}], "conditions": [], "missing": []}
        with tempfile.TemporaryDirectory() as td:
            _suite_path, _digest, run_dir, args = self._files(Path(td), [case])
            extraction = case["expected"]["conceptual"]
            def fake_post(_url, payload, _ledger, metadata, timeout):
                binding = {"bindings": [{"stepIndex": 0, "candidateRef": "follow_player"}]}
                body = self._outer(extraction if metadata["stage"] == "extraction" else binding)
                return {"status_code": 200, "response_raw": json.dumps(body), "response_json": body,
                        "latency_ms": 4, "request_sha256": pilot._sha(pilot._json_bytes(payload))}
            with mock.patch.object(pilot, "game_closed"), mock.patch.object(pilot, "get_json", return_value={"data": [{"id": "qwen-exact"}]}), \
                 mock.patch.object(pilot, "post_json", side_effect=fake_post):
                summary = pilot.run(args)
            rows = [json.loads(line) for line in (run_dir / "results.jsonl").read_text(encoding="utf-8").splitlines()]
            self.assertEqual(summary["totals"]["plans"], 2)
            self.assertTrue(all(row["groundingResponse"]["raw"] for row in rows))
            self.assertTrue(all(row["candidateTargetCorrectnessProven"] is False for row in rows))
            self.assertTrue(all(row["groundingBinding"]["bindings"][0]["candidateRef"] == "follow_player" for row in rows))

    def test_malformed_or_wrong_model_responses_are_not_validated_concepts(self):
        bad = {"model": "wrong", "choices": [{"finish_reason": "stop", "message": {"content": "{}"}}]}
        self.assertIsNone(pilot._parse_chat_response(json.dumps(bad), "qwen-exact", "extraction")[0])


if __name__ == "__main__":
    unittest.main()
