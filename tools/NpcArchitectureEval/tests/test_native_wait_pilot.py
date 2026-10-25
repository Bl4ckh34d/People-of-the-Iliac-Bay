import copy
import hashlib
import json
import subprocess
import sys
import tempfile
import unittest
from unittest import mock
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

import run_native_wait_pilot as pilot
import run_native_route_replay as route_replay


def _fixture_case():
    return {
        "id": "wait_test",
        "category": "direct_wait_explicit_duration",
        "playerText": "Wait here for twenty seconds.",
        "attributedHistory": [{"speaker": "npc", "text": "I will hold this place."}],
        "expected": {"routes": ["ACTION_REQUEST"], "intentOutcomes": ["request"],
                     "outcome": "completed", "durationSeconds": 20},
    }


def _native_template():
    context = {
        "semanticProtocol": "native-speech-act-route-context-v2",
        "npcSnapshot": {"actorId": "p:1", "snapshotRevision": 3,
                        "scene": "scene:fixture", "facts": ["native fact"]},
        "historicalMemory": [{"text": "native memory", "source": "observed"}],
    }
    state = {"purpose": "npc-speech-act-route-v2", "actorId": "p:1", "snapshotRevision": 3,
             "originalTurn": "captured turn",
             "context": json.dumps(context, ensure_ascii=False, separators=(",", ":"))}
    vocabulary = {"SOCIAL": "Social description", "ACTION_REQUEST": "New request",
                  "OFFER_RESPONSE": "Live offer response", "COMMITMENT_UPDATE": "Active commitment change"}
    return {"state": json.dumps(state, ensure_ascii=False, separators=(",", ":")),
            "questions": {"decision": {"type": "choice", "instructions": "Choose exact route.",
                         "criteria": {k: vocabulary[k] for k in ("SOCIAL", "ACTION_REQUEST")}}}}


def _candidate():
    target = {"kind": "Person", "personId": "player"}
    return {
        "schemaVersion": 1, "schema": "native-wait-candidate-v1",
        "purpose": "npc-speech-act-route-v2", "captureTimeGameSeconds": 17,
        "captureTimeUtc": "2026-10-20T10:00:00Z", "nativeEffectObserved": False,
        "binding": {"actorCategory": "native", "identityKind": "person", "personId": "p:1",
                    "ephemeralSpeakerToken": "speaker:1", "sessionId": "session:1",
                    "requestRevision": 5, "saveEpoch": 2, "archiveId": "archive:1",
                    "archiveHead": "head:1", "sceneId": "scene:fixture", "sceneRevision": 4},
        "snapshotRevision": 3,
        "candidate": {"id": "captured-wait", "kind": "Wait", "label": "Hold position here",
                      "target": target},
        "sourceCaptureSha256": "a" * 64,
    }


def _extraction(intent="request", amount=20, unit="seconds", **extra):
    return {"intent": intent,
            "steps": ([{"kind": "Wait", "targetRef": "wait_here", "amount": amount, "unit": unit}]
                      if intent == "request" else []),
            "budgetGold": None, "payer": "unspecified", "conditions": [], "missing": [], **extra}


class NativeWaitPilotTests(unittest.TestCase):
    def _input_files(self, temp):
        template = _native_template()
        template_path = temp / "template.json"
        template_raw = json.dumps(template, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
        template_path.write_bytes(template_raw)
        vocab_path = temp / "vocabulary.json"
        vocab = {"purpose": "npc-speech-act-route-v2", "contextProtocol": "native-speech-act-route-context-v2",
                 "routes": {"SOCIAL": "Social description", "ACTION_REQUEST": "New request",
                            "OFFER_RESPONSE": "Live offer response", "COMMITMENT_UPDATE": "Active commitment change"}}
        vocab_path.write_text(json.dumps(vocab), encoding="utf-8")
        candidate = _candidate()
        candidate["sourceCaptureSha256"] = hashlib.sha256(template_raw).hexdigest()
        candidate_path = temp / "candidate.json"
        candidate_path.write_text(json.dumps(candidate), encoding="utf-8")
        cases = [_fixture_case()]
        digest = route_replay.cases_sha256(cases)
        suite_path = temp / "suite.json"
        suite_path.write_text(json.dumps({"schemaVersion": 1, "casesSha256": digest, "cases": cases}), encoding="utf-8")
        run_parent = temp / "runs"
        run_parent.mkdir()
        run_dir = run_parent / "fresh"
        argv = ["--route-template", str(template_path), "--route-vocabulary", str(vocab_path),
                "--wait-candidate", str(candidate_path), "--suite", str(suite_path), "--cases-sha256", digest,
                "--run-dir", str(run_dir), "--expected-d3-model", "d3-exact", "--expected-qwen-model", "qwen-exact"]
        return template, candidate, cases[0], argv, run_dir

    def _entry(self, template, candidate, case):
        native = json.loads(json.loads(template["state"])["context"])
        model_case = pilot.build_wait_case(case, native, candidate)
        route_case = pilot._route_case(case)
        payload = route_replay.build_d3_payload(template, route_case, False,
            {"routes": {"SOCIAL": "Social description", "ACTION_REQUEST": "New request",
                        "OFFER_RESPONSE": "Live offer response", "COMMITMENT_UPDATE": "Active commitment change"}})
        d3_bytes = pilot._json_bytes(payload)
        interpret = pilot.build_interpreter_payload(model_case, "qwen-exact")
        interpret_bytes = pilot._json_bytes(interpret)
        return {"index": 0, "case": model_case, "goldCase": case, "nativePersonId": "p:1",
                "arm": "original", "reverse": False,
                "offeredRoutes": list(payload["questions"]["decision"]["criteria"]), "d3Payload": payload,
                "d3Bytes": d3_bytes, "d3Sha256": hashlib.sha256(d3_bytes).hexdigest(),
                "interpreterPayload": interpret, "interpreterBytes": interpret_bytes,
                "interpreterSha256": hashlib.sha256(interpret_bytes).hexdigest()}

    def _model_responses(self, extraction):
        d3 = {"model": "d3-exact", "answers": {"decision": {"type": "choice", "choice": "ACTION_REQUEST",
              "probabilities": {"SOCIAL": 0.01, "ACTION_REQUEST": 0.99}, "confidence": 0.99}}}
        qwen = {"model": "qwen-exact", "choices": [{"finish_reason": "stop", "message": {
            "content": json.dumps(extraction, ensure_ascii=False)}}]}
        return [{"status_code": 200, "response_raw": json.dumps(d3), "latency_ms": 5},
                {"status_code": 200, "response_raw": json.dumps(qwen), "latency_ms": 6}]

    def test_import_and_help_have_no_run_or_network_side_effects(self):
        script = str(ROOT / "run_native_wait_pilot.py")
        imported = subprocess.run([sys.executable, "-c", "import run_native_wait_pilot"], cwd=ROOT,
                                  capture_output=True, text=True, timeout=10)
        self.assertEqual(imported.returncode, 0, imported.stderr)
        help_result = subprocess.run([sys.executable, script, "--help"], cwd=ROOT,
                                     capture_output=True, text=True, timeout=10)
        self.assertEqual(help_result.returncode, 0, help_result.stderr)
        self.assertIn("--wait-candidate", help_result.stdout)

    def test_preflight_rejects_non_loopback_before_reading_inputs(self):
        argv = ["--route-template", "missing-template", "--route-vocabulary", "missing-vocab",
                "--wait-candidate", "missing-candidate", "--suite", "missing-suite", "--cases-sha256", "0" * 64,
                "--run-dir", "missing-run", "--d3-url", "http://192.0.2.1:8093",
                "--expected-d3-model", "d3", "--expected-qwen-model", "qwen"]
        with self.assertRaises(ValueError) as _raised_137:
            pilot._prepare(argv)
        self.assertTrue(('loopback' in str(_raised_137.exception)), str(_raised_137.exception))

    def test_timeout_above_native_budget_requires_explicit_diagnostic_option(self):
        argv = ["--route-template", "missing-template", "--route-vocabulary", "missing-vocab",
                "--wait-candidate", "missing-candidate", "--suite", "missing-suite", "--cases-sha256", "0" * 64,
                "--run-dir", "missing-run", "--timeout", "11", "--expected-d3-model", "d3",
                "--expected-qwen-model", "qwen"]
        with self.assertRaises(ValueError) as _raised_145:
            pilot._prepare(argv)
        self.assertTrue(('native 10-second budget' in str(_raised_145.exception)), str(_raised_145.exception))

    def test_preflight_rejects_existing_run_directory_before_network(self):
        with tempfile.TemporaryDirectory() as folder:
            template, _, _, argv, run_dir = self._input_files(Path(folder))
            run_dir.mkdir()
            with mock.patch.object(pilot, "get_json", side_effect=AssertionError("network contact")):
                with self.assertRaises(ValueError) as _raised_153:
                    pilot._prepare(argv)
                self.assertTrue(('fresh and non-existing' in str(_raised_153.exception)), str(_raised_153.exception))

    def test_game_closed_guard_runs_before_run_output_or_server_contact(self):
        with tempfile.TemporaryDirectory() as folder:
            _, _, _, argv, run_dir = self._input_files(Path(folder))
            with mock.patch.object(pilot, "game_closed", side_effect=RuntimeError("guard stopped run")), \
                    mock.patch.object(pilot, "get_json", side_effect=AssertionError("network contact")):
                with self.assertRaises(RuntimeError) as _raised_161:
                    pilot.run(argv)
                self.assertTrue(('guard stopped' in str(_raised_161.exception)), str(_raised_161.exception))
            self.assertFalse(run_dir.exists())

    def test_qwen_extraction_must_be_one_resolved_wait_before_core(self):
        with tempfile.TemporaryDirectory() as folder:
            template, candidate, case, _, _ = self._input_files(Path(folder))
            entry = self._entry(template, candidate, case)
            extraction = _extraction()
            extraction["steps"].append(copy.deepcopy(extraction["steps"][0]))
            responses = self._model_responses(extraction)
            with mock.patch.object(pilot, "post_json", side_effect=responses) as post, \
                    mock.patch.object(pilot, "game_closed"), \
                    mock.patch.object(pilot, "_assert_frozen"), \
                    mock.patch.object(pilot, "exercise_wait_in_core") as core:
                row = pilot._execute_entry(entry, None, "d3-exact", "qwen-exact", "http://127.0.0.1:8093",
                                           "http://127.0.0.1:8080", Path(folder), Path("unused.dll"), 10, {})
            self.assertEqual(post.call_count, 2)
            core.assert_not_called()
            self.assertEqual(row["materializedOutcome"], "invalid")
            self.assertFalse(row["accepted"])

    def test_action_route_calls_interpreter_once_and_only_core_plan_can_be_accepted(self):
        with tempfile.TemporaryDirectory() as folder:
            template, candidate, case, _, _ = self._input_files(Path(folder))
            entry = self._entry(template, candidate, case)
            responses = self._model_responses(_extraction())
            core_result = {"accepted": True, "dispatched": True, "actionCompleted": True,
                           "taskCompleted": True, "effectIdMatched": True,
                           "completionClaimAllowed": True, "fixtureReceipt": True,
                           "nativeEffectsObserved": False}
            with mock.patch.object(pilot, "post_json", side_effect=responses) as post, \
                    mock.patch.object(pilot, "game_closed"), \
                    mock.patch.object(pilot, "_assert_frozen"), \
                    mock.patch.object(pilot, "exercise_wait_in_core", return_value=core_result) as core:
                row = pilot._execute_entry(entry, None, "d3-exact", "qwen-exact", "http://127.0.0.1:8093",
                                           "http://127.0.0.1:8080", Path(folder), Path("unused.dll"), 10, {})
            self.assertEqual(post.call_count, 2)
            core.assert_called_once()
            self.assertTrue(row["accepted"])
            self.assertTrue(row["taskCompleted"])
            self.assertFalse(row["nativeEffectsObserved"])
            sent_interpreter = post.call_args_list[1].args[1]
            user = json.loads(sent_interpreter["messages"][1]["content"])
            self.assertNotIn("expected", user)
            self.assertEqual(user["attributedHistory"], case["attributedHistory"])

    def test_social_route_skips_interpreter_and_records_inferred_no_action(self):
        with tempfile.TemporaryDirectory() as folder:
            template, candidate, case, _, _ = self._input_files(Path(folder))
            entry = self._entry(template, candidate, case)
            d3 = {"model": "d3-exact", "answers": {"decision": {"type": "choice", "choice": "SOCIAL",
                  "probabilities": {"SOCIAL": 0.99, "ACTION_REQUEST": 0.01}, "confidence": 0.99}}}
            with mock.patch.object(pilot, "post_json", return_value={"status_code": 200, "response_raw": json.dumps(d3),
                                                                        "latency_ms": 4}) as post, \
                    mock.patch.object(pilot, "game_closed"), mock.patch.object(pilot, "_assert_frozen"), \
                    mock.patch.object(pilot, "exercise_wait_in_core") as core:
                row = pilot._execute_entry(entry, None, "d3-exact", "qwen-exact", "http://127.0.0.1:8093",
                                           "http://127.0.0.1:8080", Path(folder), Path("unused.dll"), 10, {})
            post.assert_called_once()
            core.assert_not_called()
            self.assertTrue(row["interpreterSkipped"])
            self.assertFalse(row["interpreterObserved"])
            self.assertEqual(row["materializedOutcome"], "no_action")

    def test_wait_case_preserves_exact_target_history_native_facts_and_no_live_state(self):
        native = json.loads(json.loads(_native_template()["state"])["context"])
        case = pilot.build_wait_case(_fixture_case(), native, _candidate())
        self.assertEqual(case["snapshot"]["candidates"], [
            {"ref": "wait_here", "kind": "Wait", "label": "Hold position here",
             "target": {"kind": "Person", "personId": "player"}}
        ])
        self.assertEqual(case["snapshot"]["npcSnapshot"], native["npcSnapshot"])
        self.assertEqual(case["snapshot"]["pendingOffer"], None)
        self.assertEqual(case["snapshot"]["activeCommitment"], None)
        self.assertEqual(case["attributedHistory"], _fixture_case()["attributedHistory"])

    def test_interpreter_payload_contains_facts_and_fixture_history_but_no_gold(self):
        native = json.loads(json.loads(_native_template()["state"])["context"])
        case = pilot.build_wait_case(_fixture_case(), native, _candidate())
        payload = pilot.build_interpreter_payload(case, "qwen-exact")
        user = json.loads(payload["messages"][1]["content"])
        self.assertEqual(user["latestPlayerTurn"]["text"], _fixture_case()["playerText"])
        self.assertEqual(user["attributedHistory"], _fixture_case()["attributedHistory"])
        self.assertEqual(user["npcSnapshot"], native["npcSnapshot"])
        self.assertEqual(user["availableCandidates"][0]["target"], _candidate()["candidate"]["target"])
        self.assertIsNone(user["pendingOffer"])
        self.assertIsNone(user["activeCommitment"])
        encoded = json.dumps(payload, ensure_ascii=False)
        for gold in ("expected", "durationSeconds", "intentOutcomes", "outcome"):
            self.assertNotIn(gold, encoded)
        schema = payload["response_format"]["json_schema"]["schema"]
        variants = schema["properties"]["steps"]["items"]["anyOf"]
        self.assertEqual(len(variants), 1)
        self.assertEqual(variants[0]["properties"]["kind"]["enum"], ["Wait"])

    def test_score_separates_route_interpretation_argument_and_effect_truth(self):
        case = _fixture_case()
        case["snapshot"] = {"candidates": [{"ref": "wait_here", "kind": "Wait", "label": "Hold position here",
                                             "target": {"kind": "Person", "personId": "player"}}]}
        extraction = _extraction()
        materialized = {"outcome": "plan", "steps": [{"kind": "Wait", "targetRef": "wait_here",
                          "durationSeconds": 20, "target": {"Kind": "Person", "PersonId": "player"}}]}
        row = pilot.score_stages(case, "ACTION_REQUEST", extraction, materialized,
                                 core_result={"taskCompleted": True, "effectIdMatched": True,
                                              "nativeEffectsObserved": False})
        self.assertTrue(row["routeCorrect"])
        self.assertTrue(row["interpretationCorrect"])
        self.assertTrue(row["argumentsCorrect"])
        self.assertTrue(row["taskCompleted"])
        self.assertFalse(row["nativeEffectsObserved"])
        self.assertFalse(row["falseAccepted"])

        wrong_intent = pilot.score_stages(case, "ACTION_REQUEST", _extraction("none"),
                                          {"outcome": "no_action", "steps": []},
                                          core_result={"taskCompleted": False, "effectIdMatched": False,
                                                       "nativeEffectsObserved": False})
        self.assertTrue(wrong_intent["falseAccepted"] is False)
        self.assertFalse(wrong_intent["taskCompleted"])

    def test_skipped_interpreter_is_not_scored_as_observed(self):
        row = pilot.score_stages(_fixture_case(), "SOCIAL", None,
                                 {"outcome": "no_action", "steps": []}, interpreter_skipped=True)
        self.assertTrue(row["interpreterSkipped"])
        self.assertFalse(row["interpreterObserved"])
        self.assertIsNone(row["interpretationCorrect"])
        self.assertEqual(row["routeClass"], "wrong_route")

    def test_candidate_requires_exact_native_capture_hash(self):
        with self.assertRaises(ValueError) as _raised_290:
            pilot.validate_candidate(_candidate(), "b" * 64)
        self.assertTrue(('capture hash' in str(_raised_290.exception)), str(_raised_290.exception))

    def test_captured_candidate_accepts_null_ephemeral_token_and_world_point_target(self):
        native_dir = ROOT.parents[1] / "work/native-wait-chain-20261020/native-capture"
        template_path = native_dir / "first-request.json"
        candidate_path = native_dir / "native-wait-candidate.json"
        if not template_path.is_file() or not candidate_path.is_file():
            self.skipTest("Native capture files are not present in this checkout")
        candidate = json.loads(candidate_path.read_text(encoding="utf-8"))
        item = pilot.validate_candidate(candidate, hashlib.sha256(template_path.read_bytes()).hexdigest())
        self.assertEqual(item["kind"], "Wait")
        self.assertEqual(item["target"]["kind"], "WorldPoint")
        self.assertIsNone(candidate["binding"]["ephemeralSpeakerToken"])

    def test_actual_captured_inputs_pass_full_preflight_without_contact(self):
        native_dir = ROOT.parents[1] / "work/native-wait-chain-20261020/native-capture"
        suite_path = ROOT / "fixtures/native-wait-chain-v1.json"
        if not all(path.is_file() for path in (native_dir / "first-request.json", native_dir / "route-vocabulary.json",
                                               native_dir / "native-wait-candidate.json", suite_path)):
            self.skipTest("Native Wait capture inputs are not present in this checkout")
        suite = json.loads(suite_path.read_text(encoding="utf-8"))
        with tempfile.TemporaryDirectory() as folder:
            run_dir = Path(folder) / "fresh-run"
            argv = ["--route-template", str(native_dir / "first-request.json"),
                    "--route-vocabulary", str(native_dir / "route-vocabulary.json"),
                    "--wait-candidate", str(native_dir / "native-wait-candidate.json"),
                    "--suite", str(suite_path), "--cases-sha256", suite["casesSha256"],
                    "--run-dir", str(run_dir), "--expected-d3-model", "d3-id",
                    "--expected-qwen-model", "qwen-id"]
            with mock.patch.object(pilot, "get_json", side_effect=AssertionError("network contact")):
                prepared = pilot._prepare(argv)
            self.assertEqual(len(prepared["entries"]), len(suite["cases"]) * 2)
            self.assertEqual(prepared["candidateItem"]["id"], 2)
            self.assertEqual(prepared["candidateItem"]["target"]["kind"], "WorldPoint")

    def test_core_receipt_gate_needs_completed_task_and_matching_effect(self):
        self.assertTrue(pilot.completion_claim_allowed({"taskPhase": "Completed", "effectId": "a:observed",
                                                        "completedEffectId": "a:observed"}))
        self.assertFalse(pilot.completion_claim_allowed({"taskPhase": "Executing", "effectId": "a:observed",
                                                         "completedEffectId": "a:observed"}))
        self.assertFalse(pilot.completion_claim_allowed({"taskPhase": "Completed", "effectId": "a:observed",
                                                         "completedEffectId": "b:observed"}))


if __name__ == "__main__":
    unittest.main()
