import contextlib
import io
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

TOOL_DIR = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOL_DIR))
import goal_protocol
import run_causal_goal_pilot as pilot


class CausalGoalPilotTests(unittest.TestCase):
    def setUp(self):
        self.participants = {
            "buyer": "npc:buyer-from-core",
            "seller": "npc:seller-from-core",
            "recipient": "npc:recipient-from-core",
        }
        self.case = pilot._case_after_reads(
            scenario=pilot.SCENARIOS[0], participants=self.participants,
            buyer_inventory={"personId": self.participants["buyer"], "walletGold": 30,
                             "revision": 0, "items": []},
            seller_inventory={"personId": self.participants["seller"], "walletGold": 0,
                              "revision": 0, "items": [{"uid": 42, "nativePayload": "fixture:shop-stock-item"}]},
        )
        self.interpretation = {
            "intent": "request",
            "steps": [
                {"kind": "Purchase", "targetRef": "shop_stock_item_42", "quantity": 1},
                {"kind": "Transfer", "targetRef": "give_item_42_to_recipient", "quantity": 1},
            ],
            "budgetGold": None,
            "payer": "npc",
            "conditions": [],
            "missing": [],
        }

    def test_import_has_no_network_game_or_core_side_effects(self):
        script = r'''import sys, urllib.request, subprocess
from unittest.mock import patch
sys.path.insert(0, sys.argv[1])
with patch("urllib.request.urlopen") as urlopen, patch("subprocess.Popen") as popen, patch("subprocess.run") as run:
    import run_causal_goal_pilot
    assert not urlopen.called
    assert not popen.called
    assert not run.called
'''
        result = subprocess.run([sys.executable, "-c", script, str(TOOL_DIR)],
                                capture_output=True, text=True, check=False)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(result.stdout, "")

    def test_help_and_existing_run_dir_stop_before_guards_or_services(self):
        with patch.object(pilot, "game_closed") as game, \
             patch.object(pilot, "get_json") as get_json, \
             patch.object(pilot, "CoreSession") as core, \
             contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
            with self.assertRaises(SystemExit) as exit_info:
                pilot.main(["--help"])
            self.assertEqual(exit_info.exception.code, 0)
            with tempfile.TemporaryDirectory() as temp:
                existing = Path(temp) / "used"
                existing.mkdir()
                with self.assertRaises(FileExistsError):
                    pilot.main(["--run-dir", str(existing)])
        game.assert_not_called()
        get_json.assert_not_called()
        core.assert_not_called()

    def test_model_proposal_binds_to_observed_core_participants_and_stock(self):
        bound = goal_protocol.materialize(self.case, self.interpretation)
        self.assertEqual(bound["outcome"], "plan")
        plan = pilot._core_plan_from_bound(bound, self.participants, "task:from-model")
        self.assertEqual(plan["residentId"], self.participants["buyer"])
        self.assertEqual(plan["steps"][0], {
            "kind": "Purchase", "target": {
                "kind": "IndoorPoint", "personId": self.participants["seller"],
                "settlementId": 100, "buildingKey": 20, "containerId": "7",
                "itemId": "42", "quantity": 1, "quotedGold": 9,
            },
        })
        self.assertEqual(plan["steps"][1], {
            "kind": "Transfer", "itemSourceStep": 1, "target": {
                "kind": "Person", "personId": self.participants["recipient"],
                "itemId": "42", "quantity": 1,
                "transferDirection": "NpcToCounterparty",
            },
        })
        self.assertNotIn("fixturePlan", plan)

    def test_malformed_model_proposal_never_reaches_core_acceptance(self):
        malformed = dict(self.interpretation)
        malformed["steps"] = [dict(self.interpretation["steps"][0], targetRef="not-an-observed-candidate")]
        bound = goal_protocol.materialize(self.case, malformed)
        self.assertEqual(bound["outcome"], "invalid")

        class Core:
            def __init__(self):
                self.commands = []

            def command(self, name, **arguments):
                self.commands.append((name, arguments))
                return {"accepted": True}

        core = Core()
        result = pilot._accept_supported_plan(core, bound, self.participants, "task:bad", "accept")
        self.assertFalse(result["accepted"])
        self.assertEqual(core.commands, [])

    def _run_with_fakes(self, scenario):
        class FakeCore:
            def __init__(self):
                self.ids = {"buyer": "npc:buyer-from-core", "seller": "npc:seller-from-core",
                            "recipient": "npc:recipient-from-core"}
                self.inventory = {
                    self.ids["buyer"]: {"personId": self.ids["buyer"], "walletGold": 30, "revision": 0, "items": []},
                    self.ids["seller"]: {"personId": self.ids["seller"], "walletGold": 0, "revision": 0,
                                         "items": [{"uid": 42, "nativePayload": "fixture:shop-stock-item"}]},
                    self.ids["recipient"]: {"personId": self.ids["recipient"], "walletGold": 0, "revision": 0, "items": []},
                }
                self.commands = []
                self.task = None
                self.action_number = 0
                self.pending = None

            def command(self, name, **args):
                self.commands.append((name, args))
                if name == "read_capabilities":
                    return {"participants": self.ids}
                if name == "read_inventory":
                    return json.loads(json.dumps(self.inventory[args["personId"]]))
                if name == "accept_task":
                    self.task = {"id": args["plan"]["id"], "phase": "Active", "revision": 0}
                    self.plan = args["plan"]
                    return {"accepted": True}
                if name == "read_state":
                    return {"snapshotJson": "{}", "tasks": [dict(self.task)] if self.task else [],
                            "inventories": [json.loads(json.dumps(v)) for v in self.inventory.values()]}
                if name == "dispatch_next":
                    self.action_number += 1
                    self.pending = {"number": self.action_number}
                    return {"accepted": True, "action": {"id": "action:" + str(self.action_number), "revision": 1}}
                if name == "observe_arrival":
                    return {"accepted": True}
                if name == "apply_purchase_receipt":
                    buyer = self.inventory[self.ids["buyer"]]
                    seller = self.inventory[self.ids["seller"]]
                    buyer["walletGold"] = args["buyerGoldAfter"]
                    seller["walletGold"] = args["sellerGoldAfter"]
                    buyer["items"].append({"uid": args["itemUid"], "nativePayload": args["nativePayload"]})
                    return {"accepted": True}
                if name == "restore_snapshot":
                    return {"accepted": True}
                if name == "apply_transfer_receipt":
                    buyer = self.inventory[self.ids["buyer"]]
                    recipient = self.inventory[self.ids["recipient"]]
                    item = next(x for x in buyer["items"] if x["uid"] == args["itemUid"])
                    buyer["items"].remove(item)
                    recipient["items"].append(item)
                    return {"accepted": True}
                if name == "reconcile":
                    if self.action_number == 2:
                        self.task["phase"] = "Completed"
                    return {"accepted": True}
                if name == "close":
                    return {"accepted": True}
                raise AssertionError("Unexpected Core command: " + name)

            def close(self):
                pass

        fake_core = FakeCore()
        model_calls = []
        tool_choices = iter(["read_own_inventory", "read_shop_stock", "ready"])

        def fake_model(url, payload, ledger, metadata, timeout, frozen):
            model_calls.append((metadata["stage"], payload))
            stage = metadata["stage"]
            if stage == "route":
                choice = "SOCIAL" if scenario["expected"]["route"] == "SOCIAL" else "ACTION_REQUEST"
                return {"status_code": 200, "response_json": {"answers": {"route": {"choice": choice}}}}
            if stage == "tool_choice":
                choice = next(tool_choices)
                return {"status_code": 200, "response_json": {"answers": {"tool": {"choice": choice}}}}
            if stage == "interpretation":
                value = {"intent": "request", "steps": [
                    {"kind": "Purchase", "targetRef": "shop_stock_item_42", "quantity": 1},
                    {"kind": "Transfer", "targetRef": "give_item_42_to_recipient", "quantity": 1}],
                    "budgetGold": None, "payer": "npc", "conditions": [], "missing": []}
                content = json.dumps(value)
                return {"status_code": 200, "response_json": {"choices": [{"finish_reason": "stop",
                        "message": {"content": content}}]}}
            if stage == "post_plan_decision":
                choice = "not_request" if scenario["expected"]["route"] == "SOCIAL" else "accept"
                return {"status_code": 200, "response_json": {"answers": {"decision": {"choice": choice}}}}
            if stage == "receipt_reply":
                state = json.loads(payload["messages"][1]["content"])
                value = {"reply": "The fixture receipt shows completion.", "completed": True,
                         "buyerGold": 21, "sellerGold": 9,
                         "itemOwner": fake_core.ids["recipient"], "itemUid": state["acquiredUid"]}
                return {"status_code": 200, "response_json": {"choices": [{"finish_reason": "stop",
                        "message": {"content": json.dumps(value)}}]}}
            raise AssertionError("Unexpected model stage: " + stage)

        with tempfile.TemporaryDirectory() as temp:
            run_dir = Path(temp)
            with patch.object(pilot, "CoreSession", side_effect=lambda binary, path: fake_core), \
                 patch.object(pilot, "_model_call", side_effect=fake_model):
                result = pilot._run_arm(scenario, False, "http://127.0.0.1:8093", "http://127.0.0.1:8080",
                                        {"d3": "fake-d3", "qwen": "fake-qwen"}, Path("unused"),
                                        run_dir, 2, {}, 0)
        self.assertTrue(all('"expected"' not in json.dumps(payload) for _, payload in model_calls))
        return result, fake_core, model_calls

    def test_run_arm_completes_purchase_transfer_from_model_proposal_and_fake_receipts(self):
        result, core, calls = self._run_with_fakes(pilot.SCENARIOS[0])
        self.assertEqual(result["status"], "complete", result.get("error"))
        self.assertTrue(result["coreAccepted"])
        self.assertTrue(result["simulationConsent"])
        self.assertFalse(result["realNpcConsent"])
        self.assertEqual(result["execution"]["task"]["phase"], "Completed")
        self.assertTrue(result["execution"]["snapshotRestoredBeforeTransfer"])
        self.assertEqual(result["replyScore"], {"replyValid": True, "completedCorrect": True,
                         "goldCorrect": True, "itemOwnerCorrect": True, "itemUidCorrect": True})
        accept_calls = [args for name, args in core.commands if name == "accept_task"]
        self.assertEqual(len(accept_calls), 1)
        self.assertEqual(accept_calls[0]["plan"], result["acceptance"]["plan"])
        self.assertEqual(accept_calls[0]["plan"]["steps"][0]["target"]["personId"], core.ids["seller"])
        self.assertEqual(accept_calls[0]["plan"]["steps"][1]["target"]["personId"], core.ids["recipient"])
        self.assertEqual([stage for stage, _ in calls], ["route", "tool_choice", "tool_choice", "tool_choice",
                                                          "interpretation", "post_plan_decision", "receipt_reply"])

    def test_run_arm_social_control_never_accepts_or_interprets(self):
        result, core, calls = self._run_with_fakes(pilot.SCENARIOS[2])
        self.assertEqual(result["status"], "complete", result.get("error"))
        self.assertEqual(result["route"], "SOCIAL")
        self.assertTrue(result["interpreterSkipped"])
        self.assertIsNone(result["interpretation"])
        self.assertFalse(result["coreAccepted"])
        self.assertFalse(result["simulationConsent"])
        self.assertEqual(result["decision"], "not_request")
        self.assertEqual(result["score"]["outcomeCorrect"], True)
        self.assertEqual([stage for stage, _ in calls], ["route", "post_plan_decision"])
        self.assertFalse(any(name == "accept_task" for name, _ in core.commands))


if __name__ == "__main__":
    unittest.main()
