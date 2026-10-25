import copy
import json
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import conceptual_goal_protocol as protocol


class ConceptualGoalProtocolTests(unittest.TestCase):
    def setUp(self):
        self.case = {
            "playerText": "Please wait 2 minutes, then follow me to the gate.",
            "attributedHistory": ["Earlier: I mentioned the market."],
            "npcFacts": {"name": "Mira", "facts": ["The gate is east."]},
            "expected": {"secret": "GOLD_SENTINEL"},
            "snapshot": {"candidates": [{"ref": "secret_ref", "kind": "Wait"}]},
        }
        self.candidates = [
            {"ref": "wait-a", "kind": "Wait", "label": "Wait here", "target": {"Kind": "Person", "PersonId": "npc"}},
            {"ref": "follow-a", "kind": "Follow", "label": "Follow player", "target": {"Kind": "Person", "PersonId": "player"}},
            {"ref": "move-a", "kind": "MoveTo", "label": "Gate", "target": {"Kind": "WorldPoint", "WorldSpaceId": "gate", "X": 1, "Y": 0, "Z": 2}},
            {"ref": "buy-a", "kind": "Purchase", "label": "Steel Tanto", "maxQuantity": 1,
             "target": {"Kind": "IndoorPoint", "ItemId": "tanto", "QuotedGold": 5, "Quantity": 1,
                        "PersonId": "seller", "ContainerId": "stock", "SettlementId": 1, "BuildingKey": 2}},
            {"ref": "give-a", "kind": "Transfer", "label": "Give item",
             "target": {"Kind": "Person", "ItemId": "tanto", "PersonId": "player"}},
            {"ref": "talk-a", "kind": "Interact", "label": "Talk to guard", "target": {"Kind": "Person", "PersonId": "guard"}},
        ]

    def test_extraction_payload_has_full_vocabulary_and_no_candidate_or_gold_leak(self):
        payload = protocol.conceptual_payload(self.case, "model")
        encoded = json.dumps(payload, ensure_ascii=False)
        self.assertNotIn("secret_ref", encoded)
        self.assertNotIn("GOLD_SENTINEL", encoded)
        user_content = payload["messages"][1]["content"]
        self.assertNotIn("candidates", user_content)
        schema = payload["response_format"]["json_schema"]["schema"]
        variants = schema["properties"]["steps"]["items"]["anyOf"]
        kinds = {variant["properties"]["kind"]["enum"][0]: variant for variant in variants}
        self.assertEqual(set(kinds), {"Wait", "Follow", "MoveTo", "Purchase", "Transfer", "Interact", "Other"})
        self.assertEqual(set(kinds["Wait"]["properties"]), {"kind", "sourceQuote", "targetText", "durationAmount", "durationUnit"})
        self.assertEqual(set(kinds["Purchase"]["properties"]), {"kind", "sourceQuote", "targetText", "quantity"})
        self.assertIn("actionDescription", kinds["Other"]["properties"])

    def test_conceptual_extraction_preserves_unsupported_action_and_quote(self):
        self.case["playerText"] = "Please open the old portal."
        value = protocol.extraction("model", {
            "intent": "request", "steps": [{"kind": "Other", "sourceQuote": "open the old portal",
                "targetText": "the old portal", "actionDescription": "open a portal"}],
            "conditions": [], "missing": []}, self.case)
        self.assertTrue(value["valid"], value)
        self.assertEqual(value["steps"][0]["kind"], "Other")
        refused = protocol.ground(value, [{"stepIndex": 0, "candidateRef": "wait-a"}], self.candidates)
        self.assertEqual(refused["outcome"], "clarify")
        self.assertEqual(refused["conceptualSteps"][0]["kind"], "Other")

    def test_second_phase_binds_only_same_kind_and_never_rewrites_extracted_steps(self):
        concept = protocol.extraction("model", {"intent": "request", "steps": [
            {"kind": "Wait", "sourceQuote": "wait 2 minutes", "targetText": "here", "durationAmount": 2, "durationUnit": "minutes"}],
            "conditions": [], "missing": []}, self.case)
        bound = protocol.ground(concept, [{"stepIndex": 0, "candidateRef": "wait-a"}], self.candidates)
        self.assertEqual(bound["outcome"], "plan", bound)
        self.assertEqual(bound["steps"][0]["durationSeconds"], 120)
        wrong_kind = protocol.ground(concept, [{"stepIndex": 0, "candidateRef": "follow-a"}], self.candidates)
        self.assertEqual(wrong_kind["outcome"], "clarify")

    def test_grounding_schema_keeps_action_kind_fixed_and_all_steps_in_order(self):
        self.case["playerText"] = "Please buy a Steel Tanto, then give it to me."
        concept = protocol.extraction("model", {"intent": "request", "steps": [
            {"kind": "Purchase", "sourceQuote": "buy a Steel Tanto", "targetText": "Steel Tanto", "quantity": 1},
            {"kind": "Transfer", "sourceQuote": "give it to me", "targetText": "me", "quantity": 1}],
            "conditions": [], "missing": []}, self.case)
        payload = protocol.grounding_payload(concept, self.candidates, "model")
        encoded = json.dumps(payload)
        self.assertNotIn("GOLD_SENTINEL", encoded)
        self.assertIn("buy-a", encoded)
        self.assertIn("give-a", encoded)
        bindings = payload["response_format"]["json_schema"]["schema"]["properties"]["bindings"]
        self.assertEqual(bindings["minItems"], 2)
        self.assertEqual(bindings["maxItems"], 2)

    def test_conceptual_purchase_then_transfer_binds_true_dependency(self):
        self.case["playerText"] = "Buy one Steel Tanto, then give it to me."
        concept = protocol.extraction("model", {"intent": "request", "steps": [
            {"kind": "Purchase", "sourceQuote": "Buy one Steel Tanto", "targetText": "Steel Tanto", "quantity": 1},
            {"kind": "Transfer", "sourceQuote": "give it to me", "targetText": "me", "quantity": 1}],
            "conditions": [], "missing": []}, self.case)
        bound = protocol.ground(concept, [
            {"stepIndex": 0, "candidateRef": "buy-a"}, {"stepIndex": 1, "candidateRef": "give-a"}], self.candidates)
        self.assertEqual(bound["outcome"], "plan", bound)
        self.assertEqual([step["kind"] for step in bound["steps"]], ["Purchase", "Transfer"])
        self.assertEqual([step["itemSourceStep"] for step in bound["steps"]], [0, 1])

    def test_missing_step_binding_cannot_drop_requested_step(self):
        self.case["playerText"] = "Please buy a Steel Tanto, then give it to me."
        concept = protocol.extraction("model", {"intent": "request", "steps": [
            {"kind": "Purchase", "sourceQuote": "buy a Steel Tanto", "targetText": "Steel Tanto", "quantity": 1},
            {"kind": "Transfer", "sourceQuote": "give it to me", "targetText": "me", "quantity": 1}],
            "conditions": [], "missing": []}, self.case)
        bound = protocol.ground(concept, [{"stepIndex": 0, "candidateRef": "buy-a"}], self.candidates)
        self.assertEqual(bound["outcome"], "invalid")
        self.assertIn("binding", bound["reason"].lower())

    def test_unavailable_or_ambiguous_exact_kind_binding_clarifies(self):
        concept = protocol.extraction("model", {"intent": "request", "steps": [
            {"kind": "Follow", "sourceQuote": "follow me", "targetText": "me",
             "durationAmount": None, "durationUnit": None}], "conditions": [], "missing": []}, self.case)
        unavailable = protocol.ground(concept, [{"stepIndex": 0, "candidateRef": None}], self.candidates)
        wrong = protocol.ground(concept, [{"stepIndex": 0, "candidateRef": "wait-a"}], self.candidates)
        self.assertEqual(unavailable["outcome"], "clarify")
        self.assertEqual(wrong["outcome"], "clarify")

    def test_binding_cannot_change_step_index_or_action_kind(self):
        concept = protocol.extraction("model", {"intent": "request", "steps": [
            {"kind": "Wait", "sourceQuote": "wait 2 minutes", "targetText": "here",
             "durationAmount": 2, "durationUnit": "minutes"}], "conditions": [], "missing": []}, self.case)
        wrong_index = protocol.ground(concept, [{"stepIndex": True, "candidateRef": "wait-a"}], self.candidates)
        self.assertEqual(wrong_index["outcome"], "invalid")
        self.assertEqual(protocol.ground(concept, [{"stepIndex": 0, "candidateRef": "follow-a"}], self.candidates)["outcome"], "clarify")

    def test_conditions_missing_and_unknown_target_never_become_plan(self):
        for field, text in (("conditions", "only after the rain stops"), ("missing", "which gate")):
            raw = {"intent": "request", "steps": [{"kind": "MoveTo", "sourceQuote": "to the gate",
                "targetText": "there"}], "conditions": [], "missing": []}
            raw[field] = [text]
            concept = protocol.extraction("model", raw, self.case)
            self.assertEqual(protocol.ground(concept, [{"stepIndex": 0, "candidateRef": "move-a"}], self.candidates)["outcome"], "clarify")

    def test_quote_must_be_verbatim_source_and_duration_nulls_are_paired(self):
        raw = {"intent": "request", "steps": [{"kind": "Wait", "sourceQuote": "wait two minutes",
            "targetText": "here", "durationAmount": None, "durationUnit": "minutes"}], "conditions": [], "missing": []}
        self.assertFalse(protocol.extraction("model", raw, self.case)["valid"])
        raw["steps"][0]["sourceQuote"] = "wait 2 minutes"
        raw["steps"][0]["durationUnit"] = None
        self.assertTrue(protocol.extraction("model", raw, self.case)["valid"])

    def test_explicit_null_duration_uses_existing_default_only_when_both_null(self):
        raw = {"intent": "request", "steps": [{"kind": "Wait", "sourceQuote": "wait 2 minutes",
            "targetText": "here", "durationAmount": None, "durationUnit": None}], "conditions": [], "missing": []}
        concept = protocol.extraction("model", raw, self.case)
        bound = protocol.ground(concept, [{"stepIndex": 0, "candidateRef": "wait-a"}], self.candidates)
        self.assertEqual(bound["steps"][0]["durationSeconds"], 300)

    def test_input_and_candidates_are_not_mutated(self):
        case, candidates = copy.deepcopy(self.case), copy.deepcopy(self.candidates)
        payload = protocol.conceptual_payload(case, "model")
        self.assertEqual(case, self.case)
        case["playerText"] = "Please talk to the guard."
        concept = protocol.extraction("model", {"intent": "request", "steps": [{"kind": "Interact",
            "sourceQuote": "talk to the guard", "targetText": "guard"}], "conditions": [], "missing": []}, case)
        protocol.grounding_payload(concept, candidates, "model")
        self.assertEqual(candidates, self.candidates)


if __name__ == "__main__":
    unittest.main()
