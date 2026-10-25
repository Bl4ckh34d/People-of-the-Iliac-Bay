import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

import run_goal_evaluation as runner
import goal_protocol


class GoalRunnerTests(unittest.TestCase):
    def test_import_and_help_have_no_run_side_effects(self):
        with tempfile.TemporaryDirectory() as temp:
            before = set(Path(temp).iterdir())
            help_result = subprocess.run([sys.executable, "-B", str(ROOT / "run_goal_evaluation.py"), "--help"],
                                          cwd=temp, capture_output=True, text=True, check=False)
            self.assertEqual(help_result.returncode, 0, help_result.stderr)
            self.assertIn("--run-dir", help_result.stdout)
            self.assertEqual(set(Path(temp).iterdir()), before)
            importlib_result = subprocess.run([sys.executable, "-B", "-c", "import run_goal_evaluation"], cwd=ROOT,
                                              capture_output=True, text=True, check=False)
            self.assertEqual(importlib_result.returncode, 0, importlib_result.stderr)

    def test_invalid_url_and_existing_run_dir_fail_before_contact(self):
        case = {"id": "gold-only", "playerText": "Wait.", "snapshot": {"candidates": []},
                "expected": {"route": "ACTION_REQUEST", "intent": "clarify", "steps": [], "outcome": "clarify"}}
        with tempfile.TemporaryDirectory() as temp:
            suite = Path(temp) / "suite.json"
            content = {"schemaVersion": 1, "caseCount": 1, "casesSha256": runner.cases_sha256([case]), "cases": [case]}
            suite.write_text(json.dumps(content), encoding="utf-8")
            existing = Path(temp) / "already-there"
            existing.mkdir()
            with patch.object(runner, "_get_json", side_effect=AssertionError("network called")), \
                    patch.object(runner, "post_json", side_effect=AssertionError("network called")):
                with self.assertRaises(ValueError):
                    runner.run(["--run-dir", str(existing), "--suite", str(suite), content["casesSha256"]])
                fresh = Path(temp) / "fresh"
                with self.assertRaises(ValueError):
                    runner.run(["--run-dir", str(fresh), "--suite", str(suite), content["casesSha256"],
                                "--d3-url", "http://example.com", "--qwen-url", "http://127.0.0.1:8080"])
                self.assertFalse(fresh.exists())
                with self.assertRaises(ValueError):
                    runner.run(["--run-dir", str(fresh), "--suite", str(suite), "0" * 64])
                self.assertFalse(fresh.exists())

    def test_active_game_refuses_before_metadata_or_run_output(self):
        case = {"id": "guarded-case", "playerText": "Thanks.", "snapshot": {"candidates": []},
                "expected": {"route": "SOCIAL", "intent": "none", "steps": [], "outcome": "no_action"}}
        with tempfile.TemporaryDirectory() as temp:
            suite = Path(temp) / "suite.json"
            content = {"schemaVersion": 1, "caseCount": 1, "casesSha256": runner.cases_sha256([case]), "cases": [case]}
            suite.write_text(json.dumps(content), encoding="utf-8")
            fresh = Path(temp) / "fresh"
            with patch.object(runner, "game_closed", side_effect=RuntimeError("game active"), create=True), \
                    patch.object(runner, "_get_json", side_effect=AssertionError("network called")):
                with self.assertRaises(RuntimeError) as _raised_61:
                    runner.run(["--run-dir", str(fresh), "--suite", str(suite), content["casesSha256"]])
                self.assertTrue(('game active' in str(_raised_61.exception)), str(_raised_61.exception))
            self.assertFalse(fresh.exists())

    def test_endpoint_props_require_single_slot_and_parallelism_when_reported(self):
        runner._validate_endpoint_props({"total_slots": 1}, "D3")
        runner._validate_endpoint_props({"total_slots": 1, "np": 1}, "D3")
        runner._validate_endpoint_props({"total_slots": 1, "default_generation_settings": {
            "params": {"n_parallel": 1}}}, "Qwen")
        for props in ({"total_slots": 2}, {"total_slots": 1, "np": 2},
                      {"total_slots": 1, "default_generation_settings": {"params": {"n_parallel": 4}}}):
            with self.assertRaises(RuntimeError):
                runner._validate_endpoint_props(props, "D3")

    def test_route_choice_wrong_type_is_non_actionable(self):
        case = {"id": "ignored", "playerText": "hello", "snapshot": {"candidates": []}, "expected": {}}
        response = {"status_code": 200, "transport_error": None, "malformed_json": False,
                    "response_json": {"answers": {"route": {"choice": []}}}}
        with patch.object(runner, "post_json", return_value=response):
            route, error = runner._route_call(case, "http://127.0.0.1:8093", False, Path("unused.jsonl"), {}, 1)
        self.assertIsNone(route)
        self.assertEqual(error, "unknown_route")

    def test_signature_scoring_checks_duration_quantity_dependency_and_commitment(self):
        expected = {"route": "ACTION_REQUEST", "intent": "request", "outcome": "plan", "steps": [
            {"kind": "Wait", "targetRef": "wait", "seconds": 30, "quantity": 1, "itemSourceStep": 0}],
            "commitmentId": None}
        flow = {"route": "ACTION_REQUEST", "interpreterSkipped": False,
                "interpretation": {"intent": "request"}, "pipeline": {"outcome": "plan", "steps": [
                    {"kind": "Wait", "targetRef": "wait", "durationSeconds": 30,
                     "target": {"Quantity": 1}, "itemSourceStep": 0}]}}
        exact = runner.score_case({"expected": expected, "snapshot": {}}, flow, True)
        self.assertTrue(exact["pipelineExact"])
        for field, value in (("durationSeconds", 31), ("itemSourceStep", 1), ("target.Quantity", 2)):
            changed = json.loads(json.dumps(flow))
            if field == "target.Quantity":
                changed["pipeline"]["steps"][0]["target"]["Quantity"] = value
            else:
                changed["pipeline"]["steps"][0][field] = value
            self.assertFalse(runner.score_case({"expected": expected, "snapshot": {}}, changed, True)["pipelineExact"])
        expected_cancel = {"route": "COMMITMENT_UPDATE", "intent": "cancel", "outcome": "cancel", "steps": [],
                           "commitmentId": "task-1"}
        cancel_case = {"expected": expected_cancel, "snapshot": {"activeCommitment": {"id": "task-1"}}}
        cancel_flow = {"route": "COMMITMENT_UPDATE", "interpreterSkipped": False,
                       "interpretation": {"intent": "cancel"}, "pipeline": {"outcome": "cancel", "steps": [],
                       "commitmentId": "task-2"}}
        self.assertFalse(runner.score_case(cancel_case, cancel_flow, True)["pipelineExact"])

    def test_wrong_route_with_exact_plan_is_strict_failure_not_semantic_false_proposal(self):
        case = {"expected": {"route": "ACTION_REQUEST", "intent": "request", "outcome": "plan", "steps": [
            {"kind": "Wait", "targetRef": "wait", "seconds": 30, "quantity": 1, "itemSourceStep": 0}]}, "snapshot": {}}
        flow = {"route": "SOCIAL", "interpreterSkipped": False, "interpretation": {"intent": "request"},
                "pipeline": {"outcome": "plan", "steps": [{"kind": "Wait", "targetRef": "wait", "durationSeconds": 30,
                "target": {"Quantity": 1}, "itemSourceStep": 0}]}}
        scores = runner.score_case(case, flow, True)
        self.assertTrue(scores["structuralExact"])
        self.assertTrue(scores["pipelineExact"])
        self.assertFalse(scores["allExercisedStagesExact"])
        self.assertFalse(scores["semanticFalseProposal"])
        self.assertTrue(scores["criticalFalseAction"])

    def test_social_route_infers_no_action_without_claiming_interpreter_observed(self):
        case = {"id": "do-not-send", "playerText": "Thanks.", "snapshot": {"candidates": []},
                "expected": {"route": "SOCIAL", "intent": "none", "outcome": "no_action", "steps": []}}
        route_payload = goal_protocol.route_payload(case)
        interpretation_payload = goal_protocol.interpretation_payload(case, "qwen")
        prompt_text = json.dumps([route_payload, interpretation_payload])
        self.assertNotIn("do-not-send", prompt_text)
        self.assertNotIn("expected", prompt_text)
        flow = {"route": "SOCIAL", "interpreterSkipped": True, "interpretation": None,
                "pipeline": {"outcome": "no_action", "steps": []}}
        score = runner.score_case(case, flow, extraction_valid=None)
        self.assertIsNone(score["intentCorrect"])
        self.assertFalse(score["interpreterObserved"])
        self.assertEqual(score["intentAssessment"], "inferred_none_from_social_route")
        self.assertTrue(score["allExercisedStagesExact"])


if __name__ == "__main__":
    unittest.main()
