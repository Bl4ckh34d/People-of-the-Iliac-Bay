import json
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import direct_goal_case as direct


def _case(text="Wait here for 20 seconds.", expected=None):
    expected = expected or {
        "conceptual": {"intent": "request", "steps": [{
            "kind": "Wait", "sourceQuote": "Wait here for 20 seconds", "targetText": "here",
            "durationAmount": 20, "durationUnit": "seconds"}], "conditions": [], "missing": []},
        "grounding": "plan",
    }
    return {
        "id": "wait-20", "category": "timed-action", "playerText": text,
        "attributedHistory": [],
        "snapshot": {"scene": "synthetic-scene", "candidates": [
            {"ref": "wait-here", "kind": "Wait", "label": "Wait at current position",
             "target": {"Kind": "Person", "PersonId": "player"}}]},
        "expected": expected,
    }


def _outer(content, model="qwen-exact", finish="stop"):
    return json.dumps({"model": model, "choices": [{"finish_reason": finish,
                       "message": {"content": json.dumps(content, ensure_ascii=False)}}]})


class DirectGoalCaseTests(unittest.TestCase):
    def test_valid_wait_extracts_then_grounds_once_and_reports_real_plan(self):
        case = _case()
        calls = []

        def fake_call(stage, payload):
            calls.append((stage, payload))
            content = case["expected"]["conceptual"] if stage == "extraction" else {
                "bindings": [{"stepIndex": 0, "candidateRef": "wait-here"}]}
            return {"status_code": 200, "latency_ms": 3, "response_raw": _outer(content)}

        row = direct.evaluate_case(case, "qwen-exact", fake_call)

        self.assertEqual([stage for stage, _ in calls], ["extraction", "grounding"])
        self.assertTrue(row["plan"])
        self.assertEqual(row["groundingOutcome"], "plan")
        self.assertEqual(row["selector"], "qwen-direct")
        self.assertEqual(row["planSteps"], [{"kind": "Wait", "targetRef": "wait-here",
                         "durationSeconds": 20.0, "itemSourceStep": 0,
                         "target": {"Kind": "Person", "PersonId": "player"}}])
        self.assertEqual(row["extractionCallMade"], True)
        self.assertEqual(row["groundingCallMade"], True)
        self.assertIs(row["syntheticCandidates"], True)
        self.assertIs(row["nativeEffectsObserved"], False)
        self.assertIs(row["npcConsent"], False)

    def test_multi_step_plan_keeps_order_and_numeric_fidelity(self):
        case = _case("Wait 2 minutes, then buy 3 apples.")
        case["snapshot"]["candidates"] += [
            {"ref": "purchase-apples", "kind": "Purchase", "label": "Buy apples",
             "target": {"Kind": "IndoorPoint", "PersonId": "merchant", "SettlementId": 1,
                        "BuildingKey": 2, "X": 0, "Y": 0, "Z": 0, "ContainerId": "stock",
                        "ItemId": "apple", "Quantity": 3, "QuotedGold": 3}, "maxQuantity": 5}]
        extraction = {"intent": "request", "steps": [
            {"kind": "Wait", "sourceQuote": "Wait 2 minutes", "targetText": "here",
             "durationAmount": 2, "durationUnit": "minutes"},
            {"kind": "Purchase", "sourceQuote": "buy 3 apples", "targetText": "apples", "quantity": 3}],
            "conditions": [], "missing": []}
        case["expected"] = {"conceptual": extraction, "grounding": "plan"}

        def fake_call(stage, _payload):
            content = extraction if stage == "extraction" else {"bindings": [
                {"stepIndex": 0, "candidateRef": "wait-here"},
                {"stepIndex": 1, "candidateRef": "purchase-apples"}]}
            return {"status_code": 200, "latency_ms": 1, "response_raw": _outer(content)}

        row = direct.evaluate_case(case, "qwen-exact", fake_call)
        self.assertEqual(row["extraction"]["steps"], extraction["steps"])
        self.assertEqual(row["planSteps"][0]["durationSeconds"], 120.0)
        self.assertEqual(row["planSteps"][0]["targetRef"], "wait-here")
        self.assertEqual(row["planSteps"][1]["targetRef"], "purchase-apples")
        self.assertEqual(row["planSteps"][1]["target"]["Quantity"], 3)
        self.assertTrue(row["numericArgumentsCorrect"])

    def test_invalid_extraction_stops_before_grounding(self):
        case = _case()
        calls = []

        def fake_call(stage, _payload):
            calls.append(stage)
            return {"status_code": 200, "latency_ms": 1,
                    "response_raw": _outer({"intent": "request", "steps": [], "conditions": [], "missing": []})}

        row = direct.evaluate_case(case, "qwen-exact", fake_call)
        self.assertEqual(calls, ["extraction"])
        self.assertFalse(row["extraction"]["valid"])
        self.assertEqual(row["groundingOutcome"], "invalid")
        self.assertFalse(row["groundingCallMade"])

    def test_model_or_finish_mismatch_and_over_timeout_never_ground(self):
        case = _case()
        responses = [
            {"status_code": 200, "latency_ms": 1,
             "response_raw": _outer(case["expected"]["conceptual"], model="other-model")},
            {"status_code": 200, "latency_ms": 10001,
             "response_raw": _outer(case["expected"]["conceptual"])},
            {"status_code": 200, "latency_ms": 1,
             "response_raw": _outer(case["expected"]["conceptual"], finish="length")},
        ]
        for response, expected_error in zip(responses, ("wrong_model", "over_timeout", "incomplete_generation")):
            calls = []

            def fake_call(stage, _payload):
                calls.append(stage)
                return response

            row = direct.evaluate_case(case, "qwen-exact", fake_call)
            self.assertEqual(calls, ["extraction"])
            self.assertEqual(row["extractionError"], expected_error)
            self.assertFalse(row["groundingCallMade"])

    def test_none_intent_skips_grounding(self):
        case = _case("That sounds good.", {"conceptual": {"intent": "none", "steps": [],
                      "conditions": [], "missing": []}, "grounding": "no_action"})
        calls = []

        def fake_call(stage, _payload):
            calls.append(stage)
            return {"status_code": 200, "latency_ms": 1,
                    "response_raw": _outer(case["expected"]["conceptual"])}

        row = direct.evaluate_case(case, "qwen-exact", fake_call)
        self.assertEqual(calls, ["extraction"])
        self.assertEqual(row["groundingOutcome"], "no_action")
        self.assertFalse(row["groundingCallMade"])

    def test_other_action_clarifies_without_grounding(self):
        conceptual = {"intent": "request", "steps": [{"kind": "Other",
            "sourceQuote": "Tell everyone the news", "targetText": "everyone",
            "actionDescription": "tell everyone the news"}], "conditions": [], "missing": []}
        case = _case("Tell everyone the news.", {"conceptual": conceptual, "grounding": "clarify"})
        calls = []

        def fake_call(stage, _payload):
            calls.append(stage)
            return {"status_code": 200, "latency_ms": 1, "response_raw": _outer(conceptual)}

        row = direct.evaluate_case(case, "qwen-exact", fake_call)
        self.assertEqual(calls, ["extraction"])
        self.assertEqual(row["groundingOutcome"], "clarify")
        self.assertFalse(row["groundingCallMade"])

    def test_payloads_never_expose_expected_or_gold_and_reverse_only_changes_menu_order(self):
        case = _case()
        case["snapshot"]["candidates"].append({"ref": "follow-player", "kind": "Follow", "label": "Follow player"})
        payloads = []

        def fake_call(stage, payload):
            payloads.append((stage, payload))
            content = case["expected"]["conceptual"] if stage == "extraction" else {
                "bindings": [{"stepIndex": 0, "candidateRef": "wait-here"}]}
            return {"status_code": 200, "latency_ms": 2, "response_raw": _outer(content)}

        direct.evaluate_case(case, "qwen-exact", fake_call, reverse=True)
        extraction_user = payloads[0][1]["messages"][1]["content"]
        grounding_user = payloads[1][1]["messages"][1]["content"]
        self.assertNotIn("expected", extraction_user)
        self.assertNotIn("gold", extraction_user)
        self.assertNotIn("wait-here", extraction_user)
        self.assertNotIn("expected", grounding_user)
        self.assertNotIn("gold", grounding_user)
        self.assertEqual([c["ref"] for c in json.loads(grounding_user)["currentCandidates"]],
                         ["follow-player", "wait-here"])


if __name__ == "__main__":
    unittest.main()
