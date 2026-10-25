import copy
import json
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import goal_protocol as protocol


class GoalProtocolTests(unittest.TestCase):
    def setUp(self):
        self.case = {"playerText": "Wait, then follow me.", "snapshot": {
            "candidates": [
                {"ref": "wait", "kind": "Wait", "target": {"Kind": "Person", "PersonId": "player"}},
                {"ref": "follow", "kind": "Follow", "target": {"Kind": "Person", "PersonId": "player"}},
                {"ref": "buy", "kind": "Purchase", "target": {"Kind": "IndoorPoint", "ItemId": "42", "QuotedGold": 9,
                    "Quantity": 1, "PersonId": "seller", "ContainerId": "7", "SettlementId": 100, "BuildingKey": 20}},
                {"ref": "give", "kind": "Transfer", "target": {"Kind": "Person", "ItemId": "42", "PersonId": "recipient"}}
            ], "pendingOffer": None, "activeCommitment": None}, "expected": {"secret": "GOLD_SENTINEL"}}

    def interpretation(self, steps, intent="request", **fields):
        return dict(intent=intent, steps=steps, budgetGold=None, payer="unspecified", conditions=[], missing=[], **fields)

    def timed(self, kind="Wait", ref="wait", amount=10, unit="seconds"):
        return dict(kind=kind, targetRef=ref, amount=amount, unit=unit)

    def test_action_specific_schema_and_no_gold(self):
        payload = protocol.interpretation_payload(self.case, "model")
        schema = payload["response_format"]["json_schema"]["schema"]
        branches = {b["properties"]["kind"]["enum"][0]: b for b in schema["properties"]["steps"]["items"]["anyOf"]}
        self.assertEqual(set(branches["Wait"]["properties"]), {"kind", "targetRef", "amount", "unit"})
        self.assertEqual(set(branches["Purchase"]["properties"]), {"kind", "targetRef", "quantity"})
        self.assertNotIn("GOLD_SENTINEL", json.dumps(payload))
        self.assertNotIn("fromStep", json.dumps(schema))

    def test_wait_follow_preserve_units_and_have_no_item_dependencies(self):
        value = self.interpretation([self.timed(amount=1.25, unit="minutes"), self.timed("Follow", "follow", 2, "minutes")])
        bound = protocol.materialize(self.case, value)
        self.assertEqual(bound["outcome"], "plan")
        self.assertEqual([s["durationSeconds"] for s in bound["steps"]], [75, 120])
        self.assertEqual([s["itemSourceStep"] for s in bound["steps"]], [0, 0])
        self.assertNotIn("Quantity", bound["steps"][0]["target"])

    def test_foreign_fields_and_boolean_numeric_values_rejected(self):
        for extra in ({"fromStep": 1}, {"quantity": 1}, {"unexpected": "x"}, {"amount": True}):
            step = dict(self.timed(), **extra)
            self.assertEqual(protocol.materialize(self.case, self.interpretation([step]))["outcome"], "invalid")

    def test_acquired_dependency_is_derived_and_cannot_be_spent_twice(self):
        steps = [{"kind": "Purchase", "targetRef": "buy", "quantity": 1}, {"kind": "Transfer", "targetRef": "give", "quantity": 1}]
        bound = protocol.materialize(self.case, self.interpretation(steps))
        self.assertEqual(bound["outcome"], "plan")
        self.assertEqual([s["itemSourceStep"] for s in bound["steps"]], [0, 1])
        self.assertEqual(bound["steps"][1]["target"]["ItemId"], "42")
        self.assertEqual(protocol.materialize(self.case, self.interpretation(steps + [steps[1]]))["outcome"], "clarify")

    def test_transfer_needs_owned_observation_or_unique_prior_purchase(self):
        transfer = {"kind": "Transfer", "targetRef": "give", "quantity": 1}
        self.assertEqual(protocol.materialize(self.case, self.interpretation([transfer]))["outcome"], "clarify")
        self.case["snapshot"]["candidates"][-1]["ownedItemObserved"] = True
        self.assertEqual(protocol.materialize(self.case, self.interpretation([transfer]))["steps"][0]["itemSourceStep"], 0)
        buy = {"kind": "Purchase", "targetRef": "buy", "quantity": 1}
        self.assertEqual(protocol.materialize(self.case, self.interpretation([buy, transfer]))["outcome"], "clarify")
        self.case["snapshot"]["candidates"][-1].pop("ownedItemObserved")
        self.assertEqual(protocol.materialize(self.case, self.interpretation([buy, buy, transfer]))["outcome"], "clarify")

    def test_mismatched_or_excess_quantity_is_not_silently_repaired(self):
        buy = {"kind": "Purchase", "targetRef": "buy", "quantity": 1}
        give = {"kind": "Transfer", "targetRef": "give", "quantity": 2}
        self.case["snapshot"]["candidates"][-1]["maxQuantity"] = 2
        self.assertEqual(protocol.materialize(self.case, self.interpretation([buy, give]))["outcome"], "clarify")
        buy["quantity"] = 2
        self.assertEqual(protocol.materialize(self.case, self.interpretation([buy]))["outcome"], "clarify")

    def test_defaults_bounds_and_unresolved_conditions(self):
        value = self.interpretation([self.timed(amount=None, unit=None)])
        self.assertEqual(protocol.materialize(self.case, value)["steps"][0]["durationSeconds"], 300)
        value["steps"][0]["amount"] = 10
        self.assertEqual(protocol.materialize(self.case, value)["outcome"], "invalid")
        value["steps"][0] = self.timed(amount=1000)
        self.assertEqual(protocol.materialize(self.case, value)["outcome"], "clarify")
        value["steps"][0] = self.timed()
        value["conditions"] = ["only after the rain stops"]
        self.assertEqual(protocol.materialize(self.case, value)["outcome"], "clarify")

    def test_budget_and_unsupported_player_funding(self):
        value = self.interpretation([{"kind": "Purchase", "targetRef": "buy", "quantity": 1}])
        value["budgetGold"] = 8
        self.assertEqual(protocol.materialize(self.case, value)["outcome"], "clarify")
        value["budgetGold"] = 10
        value["payer"] = "player"
        self.assertEqual(protocol.materialize(self.case, value)["outcome"], "clarify")

    def test_live_offer_exact_binding_and_no_stray_steps(self):
        self.case["snapshot"]["pendingOffer"] = {"id": "offer1", "kind": "Follow", "targetRef": "follow", "amount": 90, "unit": "game_seconds"}
        value = self.interpretation([], intent="accept_offer")
        bound = protocol.materialize(self.case, value)
        self.assertEqual(bound["steps"][0]["durationSeconds"], 90)
        self.assertEqual(bound["offerId"], "offer1")
        value["steps"] = [self.timed()]
        self.assertEqual(protocol.materialize(self.case, value)["outcome"], "invalid")

    def test_event_first_route_and_intent_remain_separate(self):
        def forbidden():
            self.fail("Social route must skip interpreter")
        flow = protocol.event_first_flow(self.case, lambda: ("SOCIAL", None), forbidden)
        self.assertTrue(flow["interpreterSkipped"])
        value = self.interpretation([self.timed()], intent="cancel")
        flow = protocol.event_first_flow(self.case, lambda: ("ACTION_REQUEST", None), lambda: (value, None))
        self.assertEqual(flow["pipeline"]["outcome"], "invalid")

    def test_malformed_json_values_produce_invalid_result_without_throwing(self):
        for fields in ({"kind": []}, {"targetRef": []}, {"kind": {}}, {"targetRef": {}}):
            value = self.interpretation([dict(self.timed(), **fields)])
            self.assertEqual(protocol.materialize(self.case, value)["outcome"], "invalid")
        value = self.interpretation([self.timed(amount=10 ** 1000)])
        self.assertEqual(protocol.materialize(self.case, value)["outcome"], "invalid")

    def test_exact_quantity_quote_cannot_be_reused_for_different_quantity(self):
        self.case["snapshot"]["candidates"][2]["maxQuantity"] = 2
        value = self.interpretation([{"kind": "Purchase", "targetRef": "buy", "quantity": 2}])
        value["budgetGold"] = 9
        self.assertEqual(protocol.materialize(self.case, value)["outcome"], "clarify")

    def test_incomplete_or_ambiguous_target_cannot_become_proposal(self):
        for target in ({}, {"Kind": "IndoorPoint", "ItemId": "42", "QuotedGold": 9},
                       {"Kind": "Person", "PersonId": "seller", "personId": "other"}):
            self.case["snapshot"]["candidates"][2]["target"] = target
            value = self.interpretation([{"kind": "Purchase", "targetRef": "buy", "quantity": 1}])
            self.assertEqual(protocol.materialize(self.case, value)["outcome"], "clarify")
        self.case["snapshot"]["candidates"][0]["target"] = {}
        self.assertEqual(protocol.materialize(self.case, self.interpretation([self.timed()]))["outcome"], "clarify")

    def test_owned_aliases_do_not_allow_repeated_handover_of_same_uid(self):
        give = self.case["snapshot"]["candidates"][-1]
        give["ownedItemObserved"] = True
        second = copy.deepcopy(give)
        second["ref"] = "give_again"
        self.case["snapshot"]["candidates"].append(second)
        value = self.interpretation([{"kind": "Transfer", "targetRef": ref, "quantity": 1} for ref in ("give", "give_again")])
        self.assertEqual(protocol.materialize(self.case, value)["outcome"], "clarify")


if __name__ == "__main__":
    unittest.main()
