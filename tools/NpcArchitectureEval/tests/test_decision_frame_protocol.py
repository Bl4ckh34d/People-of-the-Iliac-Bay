import json
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import decision_frame_protocol as protocol
import conceptual_goal_protocol


class DecisionFrameProtocolTests(unittest.TestCase):
    def setUp(self):
        self.case = {
            "playerText": "Buy three Steel Tantos, then give one to me if the seller has enough stock.",
            "attributedHistory": [{"speaker": "player", "text": "Earlier, I asked about the gate."}],
            "npcFacts": {"name": "Mira"},
            "expected": {"secret": "GOLD_SENTINEL"},
            "snapshot": {"candidates": [{"ref": "candidate-secret", "kind": "Purchase"}]},
        }

    @staticmethod
    def answer(options, choice):
        return {"type": "choice", "choice": choice,
                "probabilities": {option: 1.0 if option == choice else 0.0 for option in options},
                "confidence": 1.0}

    def test_selection_payload_has_two_independent_first_questions_and_no_gold_or_candidates(self):
        payload = protocol.selection_payload(self.case)
        self.assertEqual(set(payload["questions"]), {"frame", "constraints"})
        self.assertEqual(payload["questions"]["constraints"]["criteria"], {"yes": "The request includes a condition, limit, deadline, or other constraint.", "no": "The request has no condition, limit, deadline, or other constraint."})
        encoded = json.dumps(payload, ensure_ascii=False)
        self.assertNotIn("GOLD_SENTINEL", encoded)
        self.assertNotIn("candidate-secret", encoded)
        self.assertNotIn("candidates", encoded)
        self.assertEqual(set(payload["questions"]["frame"]["criteria"]), {"NONE", "CLARIFY", *protocol.KINDS})

    def test_following_selection_uses_end_and_clarify_without_enumerating_plans(self):
        payload = protocol.selection_payload(self.case, ["Purchase"], has_constraints=True)
        criteria = payload["questions"]["frame"]["criteria"]
        self.assertEqual(set(criteria), {"END", "CLARIFY", *protocol.KINDS})
        self.assertIn('"selectedPrefix":[{"index":0,"kind":"Purchase"}]', payload["state"])

    def test_step_limit_uses_end_or_more_needed_and_never_forces_end(self):
        prefix = ["Wait"] * protocol.MAX_STEPS
        payload = protocol.selection_payload(self.case, prefix, has_constraints=False)
        self.assertEqual(set(payload["questions"]["frame"]["criteria"]), {"END", "MORE_NEEDED"})
        result = protocol.parse_selection_response(
            {"frame": self.answer(["END", "MORE_NEEDED"], "MORE_NEEDED")}, prefix, has_constraints=False)
        self.assertEqual(result["outcome"], "clarify")
        self.assertEqual(result["selectedKinds"], prefix)

    def test_first_response_distinguishes_none_and_preserves_constraint_flag(self):
        result = protocol.parse_selection_response({
            "frame": self.answer(["NONE", "CLARIFY", *protocol.KINDS], "NONE"),
            "constraints": self.answer(["yes", "no"], "yes")}, [])
        self.assertEqual(result, {"outcome": "none", "selectedKinds": [], "hasConstraints": True})

    def test_step_response_appends_one_kind_and_rejects_unexpected_question_keys(self):
        options = ["END", "CLARIFY", *protocol.KINDS]
        result = protocol.parse_selection_response(
            {"frame": self.answer(options, "Transfer")}, ["Purchase"], has_constraints=True)
        self.assertEqual(result["outcome"], "continue")
        self.assertEqual(result["selectedKinds"], ["Purchase", "Transfer"])
        with self.assertRaises(ValueError):
            protocol.parse_selection_response(
                {"frame": self.answer(options, "Transfer"), "constraints": self.answer(["yes", "no"], "no")},
                ["Purchase"], has_constraints=True)

    def test_d3_choice_probabilities_must_be_well_formed_and_selected_maximum(self):
        options = ["NONE", "CLARIFY", *protocol.KINDS]
        valid = {"frame": self.answer(options, "NONE"),
                 "constraints": self.answer(["yes", "no"], "no")}
        for mutate in (
            lambda answer: answer.update(type="text"),
            lambda answer: answer["probabilities"].pop("Wait"),
            lambda answer: answer["probabilities"].update(Wait=1.5),
            lambda answer: answer["probabilities"].update(Wait=float("nan")),
            lambda answer: answer["probabilities"].update(NONE=0.0, Wait=1.0),
            lambda answer: answer["probabilities"].update(Wait=10 ** 400),
            lambda answer: answer.update(confidence=float("inf")),
        ):
            broken = {key: {**answer, "probabilities": dict(answer["probabilities"])}
                      for key, answer in valid.items()}
            mutate(broken["frame"])
            with self.assertRaises(ValueError):
                protocol.parse_selection_response(broken)

    def test_argument_payload_locks_kind_count_and_index_and_allows_unavailable_quantity(self):
        payload = protocol.argument_payload(self.case, ["Purchase", "Other"], "qwen", has_constraints=True)
        self.assertNotIn("candidate-secret", json.dumps(payload))
        schema = payload["response_format"]["json_schema"]["schema"]
        variants = schema["properties"]["arguments"]["items"]["anyOf"]
        by_kind = {variant["properties"]["kind"]["enum"][0]: variant for variant in variants}
        self.assertEqual({variant["properties"]["stepIndex"]["enum"][0] for variant in variants}, {0, 1})
        self.assertEqual(by_kind["Purchase"]["properties"]["quantity"], {"type": "integer", "minimum": 1})
        self.assertNotIn("maximum", by_kind["Purchase"]["properties"]["quantity"])
        self.assertIn("actionDescription", by_kind["Other"]["properties"])

    def test_extraction_preserves_exact_arguments_conditions_and_constraints(self):
        result = protocol.extract_arguments(self.case, ["Purchase"], {
            "arguments": [{"stepIndex": 0, "kind": "Purchase", "sourceQuote": "Buy three Steel Tantos",
                           "targetText": "Steel Tantos", "quantity": 3}],
            "conditions": ["if the seller has enough stock"], "missing": [],
            "constraints": [{"sourceQuote": "if the seller has enough stock", "detail": "seller stock condition"}],
            "frameDisagreement": False, "frameDisagreementReason": None,
            "needsClarification": False, "clarificationReason": None,
        }, has_constraints=True)
        self.assertTrue(result["valid"], result)
        self.assertEqual(result["conceptual"]["steps"][0]["quantity"], 3)
        self.assertEqual(result["conceptual"]["conditions"], ["if the seller has enough stock"])
        self.assertEqual(result["constraints"][0]["sourceQuote"], "if the seller has enough stock")
        self.assertFalse(result["actionable"])
        self.assertEqual(result["outcome"], "clarify")
        self.assertFalse(result["conceptual"]["valid"])
        self.assertTrue(result["hasConstraints"])
        self.assertEqual(conceptual_goal_protocol.ground(result["conceptual"], None, [])["outcome"], "invalid")

    def test_invalid_step_reordering_or_explicit_frame_disagreement_never_becomes_actionable(self):
        value = {
            "arguments": [{"stepIndex": 1, "kind": "Transfer", "sourceQuote": "give one to me",
                           "targetText": "me", "quantity": 1}],
            "conditions": [], "missing": [], "constraints": [],
            "frameDisagreement": False, "frameDisagreementReason": None,
            "needsClarification": False, "clarificationReason": None,
        }
        invalid = protocol.extract_arguments(self.case, ["Purchase"], value, has_constraints=False)
        self.assertFalse(invalid["valid"])
        value["arguments"][0].update(stepIndex=0, kind="Purchase", sourceQuote="Buy three Steel Tantos",
                                      targetText="Steel Tantos", quantity=3)
        value.update(frameDisagreement=True, frameDisagreementReason="The frame may have omitted a requested action.")
        disagreement = protocol.extract_arguments(self.case, ["Purchase"], value, has_constraints=False)
        self.assertTrue(disagreement["valid"], disagreement)
        self.assertFalse(disagreement["actionable"])
        self.assertEqual(disagreement["outcome"], "clarify")
        self.assertFalse(disagreement["conceptual"]["valid"])

    def test_constraint_flag_requires_sourced_qwen_detail(self):
        value = {
            "arguments": [{"stepIndex": 0, "kind": "Purchase", "sourceQuote": "Buy three Steel Tantos",
                           "targetText": "Steel Tantos", "quantity": 3}],
            "conditions": [], "missing": [], "constraints": [],
            "frameDisagreement": False, "frameDisagreementReason": None,
            "needsClarification": False, "clarificationReason": None,
        }
        result = protocol.extract_arguments(self.case, ["Purchase"], value, has_constraints=True)
        self.assertFalse(result["valid"])
        self.assertIn("constraint", result["reason"].lower())

    def test_missing_argument_and_explicit_clarification_are_non_actionable(self):
        value = {
            "arguments": [{"stepIndex": 0, "kind": "Purchase", "sourceQuote": "Buy three Steel Tantos",
                           "targetText": "Steel Tantos", "quantity": 3}],
            "conditions": [], "missing": ["which seller"], "constraints": [],
            "frameDisagreement": False, "frameDisagreementReason": None,
            "needsClarification": True, "clarificationReason": "No seller was identified.",
        }
        result = protocol.extract_arguments(self.case, ["Purchase"], value, has_constraints=False)
        self.assertTrue(result["valid"], result)
        self.assertFalse(result["actionable"])
        self.assertEqual(result["conceptual"]["missing"], ["which seller"])


if __name__ == "__main__":
    unittest.main()
