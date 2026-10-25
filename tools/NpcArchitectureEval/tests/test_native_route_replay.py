import hashlib
import json
import subprocess
import sys
import tempfile
import threading
import unittest
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

import run_native_route_replay as runner


def template():
    context = {
        "semanticProtocol": "native-speech-act-route-context-v1",
        "npcSnapshot": {"name": "Pryner", "clock": "09:07", "candidates": [{"ref": "wait_here", "kind": "Wait"}]},
        "pendingOffer": None,
        "activeCommitments": [],
        "attributedHistory": [],
        "historicalMemory": {"label": "historical", "text": "old context"},
    }
    return {
        "state": json.dumps({"protocolVersion": "npc-decision-v2", "purpose": "npc-speech-act-route-v1",
                             "requestId": "native-capture", "actorId": "resident:1", "originalTurn": "captured text",
                             "context": json.dumps(context, ensure_ascii=False, separators=(",", ":")),
                             "snapshotRevision": 9, "candidateSetRevision": 1}, ensure_ascii=False, separators=(",", ":")),
        "questions": {"decision": {"type": "choice", "instructions": "Classify current speech.", "criteria": {
            "SOCIAL": "Conversation, report or hypothetical.",
            "ACTION_REQUEST": "New instruction to NPC.",
            "OFFER_RESPONSE": "Accept or reject live offer.",
            "COMMITMENT_UPDATE": "Change live commitment.",
        }}},
    }


def case(case_id="case-1", route="ACTION_REQUEST", category="direct", **overrides):
    value = {"id": case_id, "category": category, "playerText": "Please wait here.",
             "attributedHistory": [{"speaker": "npc", "text": "I can wait."}],
             "commitments": [{"id": "task-7", "kind": "task", "status": "running"}],
             "pendingOffer": {"id": "offer-2", "steps": [{"kind": "Follow"}]},
             "expectedRoutes": [route], "notes": "control gold only"}
    value.update(overrides)
    return value


def suite_file(path, cases):
    digest = runner.cases_sha256(cases)
    path.write_text(json.dumps({"schemaVersion": 1, "caseCount": len(cases), "casesSha256": digest,
                                "cases": cases}, ensure_ascii=False), encoding="utf-8")
    return digest


def v2_template():
    value = template()
    state = json.loads(value["state"])
    state["purpose"] = "npc-speech-act-route-v2"
    context = json.loads(state["context"])
    context["semanticProtocol"] = "native-speech-act-route-context-v2"
    state["context"] = json.dumps(context, ensure_ascii=False, separators=(",", ":"))
    value["state"] = json.dumps(state, ensure_ascii=False, separators=(",", ":"))
    value["questions"]["decision"]["criteria"] = {
        "SOCIAL": "Conversation, information or described/quoted/hypothetical events without a new instruction to this NPC.",
        "ACTION_REQUEST": "A new instruction or request to this NPC, including indirect, conditional or unsupported requests that need interpretation.",
    }
    return value


def route_vocabulary():
    return {"purpose": "npc-speech-act-route-v2", "contextProtocol": "native-speech-act-route-context-v2",
            "routes": {"SOCIAL": "Conversation, information or described/quoted/hypothetical events without a new instruction to this NPC.",
                       "ACTION_REQUEST": "A new instruction or request to this NPC, including indirect, conditional or unsupported requests that need interpretation.",
                       "OFFER_RESPONSE": "The player accepts or rejects a specific currently pending NPC offer. A historical offer is not pending.",
                       "COMMITMENT_UPDATE": "The player currently asks to cancel or modify a specific active commitment. Describing a past cancellation does not count."}}


class NativeRouteReplayTests(unittest.TestCase):
    def test_import_and_help_have_no_run_side_effects(self):
        with tempfile.TemporaryDirectory() as temp:
            before = set(Path(temp).iterdir())
            help_result = subprocess.run([sys.executable, "-B", str(ROOT / "run_native_route_replay.py"), "--help"],
                                          cwd=temp, capture_output=True, text=True, check=False)
            self.assertEqual(help_result.returncode, 0, help_result.stderr)
            self.assertIn("--template", help_result.stdout)
            self.assertEqual(set(Path(temp).iterdir()), before)
            imported = subprocess.run([sys.executable, "-B", "-c", "import run_native_route_replay"], cwd=ROOT,
                                      capture_output=True, text=True, check=False)
            self.assertEqual(imported.returncode, 0, imported.stderr)

    def test_payload_replay_preserves_native_facts_replaces_case_data_and_hides_gold(self):
        original = template()
        item = case()
        forward = runner.build_d3_payload(original, item, reverse=False)
        reverse = runner.build_d3_payload(original, item, reverse=True)
        state = json.loads(forward["state"])
        context = json.loads(state["context"])
        self.assertEqual(state["originalTurn"], item["playerText"])
        self.assertEqual(context["npcSnapshot"], json.loads(original["state"])["context"] and
                         json.loads(json.loads(original["state"])["context"])["npcSnapshot"])
        self.assertEqual(context["attributedHistory"], item["attributedHistory"])
        self.assertEqual(context["activeCommitments"], item["commitments"])
        self.assertEqual(context["pendingOffer"], item["pendingOffer"])
        self.assertEqual(context["historicalMemory"], {"label": "historical", "text": "old context"})
        keys = list(forward["questions"]["decision"]["criteria"])
        self.assertEqual(list(reversed(keys)), list(reverse["questions"]["decision"]["criteria"]))
        self.assertEqual(set(keys), {"SOCIAL", "ACTION_REQUEST", "OFFER_RESPONSE", "COMMITMENT_UPDATE"})
        serialized = json.dumps([forward, reverse], ensure_ascii=False)
        for secret in ("expectedRoutes", "control gold only", "ACTION_REQUEST\",\"expected"):
            self.assertNotIn(secret, serialized)
        self.assertEqual(original["state"], template()["state"], "Builder must not mutate captured template.")

    def test_payload_filters_commitments_and_bounds_whole_history_like_native_builder(self):
        item = case(attributedHistory=[{"speaker": "npc", "text": "old" * 2001},
                                       {"speaker": "player", "text": "latest"}],
                    commitments=[{"id": "running", "kind": "task", "status": "running"},
                                 {"id": "done", "kind": "task", "status": "completed"},
                                 {"id": "unknown", "kind": "other", "status": "active"},
                                 {"id": "active", "kind": "shared-activity", "status": "paused"}])
        payload = runner.build_d3_payload(template(), item, reverse=False)
        context = json.loads(json.loads(payload["state"])["context"])
        self.assertEqual(context["activeCommitments"], [item["commitments"][0], item["commitments"][3]])
        self.assertEqual(context["attributedHistory"], [item["attributedHistory"][1]])
        self.assertEqual(context["historyOmitted"], 1)

    def test_v2_menu_masks_match_live_offer_and_active_commitment_state(self):
        vocab = route_vocabulary()
        cases = [case("two", pendingOffer=None, commitments=[]), case("offer", pendingOffer={"id": "offer", "steps": []}, commitments=[]),
                 case("commitment", pendingOffer=None, commitments=[{"id": "t", "kind": "task", "status": "ready"}]),
                 case("four", pendingOffer={"id": "offer", "steps": []},
                      commitments=[{"id": "t", "kind": "task", "status": "ready"}])]
        expected = [["SOCIAL", "ACTION_REQUEST"], ["SOCIAL", "ACTION_REQUEST", "OFFER_RESPONSE"],
                    ["SOCIAL", "ACTION_REQUEST", "COMMITMENT_UPDATE"],
                    ["SOCIAL", "ACTION_REQUEST", "OFFER_RESPONSE", "COMMITMENT_UPDATE"]]
        for item, offered in zip(cases, expected):
            payload = runner.build_d3_payload(v2_template(), item, False, vocab)
            self.assertEqual(list(payload["questions"]["decision"]["criteria"]), offered)
            self.assertNotIn("expectedRoutes", json.dumps(payload))

    def test_v2_unavailable_route_is_rejected_by_d3_and_qwen_parsers(self):
        d3 = {"model": "d3-exact", "answers": {"decision": {"type": "choice", "choice": "OFFER_RESPONSE",
              "probabilities": {"SOCIAL": 0, "ACTION_REQUEST": 1}}}}
        parsed = runner.parse_d3_response(d3, "d3-exact", ["ACTION_REQUEST"], ["SOCIAL", "ACTION_REQUEST"])
        self.assertEqual(parsed["error"], "invalid_schema")
        qwen = {"model": "qwen-exact", "choices": [{"finish_reason": "stop",
                "message": {"content": '{"route":"COMMITMENT_UPDATE"}'}}]}
        parsed = runner.parse_qwen_response(qwen, "qwen-exact", ["ACTION_REQUEST"], ["SOCIAL", "ACTION_REQUEST"])
        self.assertEqual(parsed["error"], "invalid_completion")

    def test_v2_vocabulary_is_required_validated_hashed_and_frozen_while_v1_stays_four_choice(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp); vocab_path = root / "route-vocabulary.json"
            vocab_value = route_vocabulary()
            vocab_path.write_text(json.dumps(vocab_value, ensure_ascii=False), encoding="utf-8")
            vocab, digest = runner.load_route_vocabulary(vocab_path)
            self.assertEqual(vocab, vocab_value)
            self.assertEqual(len(digest), 64)
            with self.assertRaises(ValueError):
                runner.load_route_vocabulary(vocab_path, expected_purpose="npc-speech-act-route-v1")
        payload = runner.build_d3_payload(template(), case(), False)
        self.assertEqual(list(payload["questions"]["decision"]["criteria"]),
                         ["SOCIAL", "ACTION_REQUEST", "OFFER_RESPONSE", "COMMITMENT_UPDATE"])

    def test_v2_preflight_requires_vocabulary_and_freezes_hash_before_contact(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp); suite_path = root / "suite.json"
            item = case(pendingOffer=None, commitments=[])
            suite_file(suite_path, [item])
            template_path = root / "native-v2.json"
            template_path.write_text(json.dumps(v2_template(), ensure_ascii=False), encoding="utf-8")
            vocab_path = root / "route-vocabulary.json"
            vocab_path.write_text(json.dumps(route_vocabulary(), ensure_ascii=False), encoding="utf-8")
            run_path = root / "new-run"
            args = ["--template", str(template_path), "--suite", str(suite_path), "--run-dir", str(run_path),
                    "--expected-model", "d3-exact"]
            with self.assertRaises(ValueError) as _raised_179:
                runner._prepare(args)
            self.assertTrue(('route-vocabulary' in str(_raised_179.exception)), str(_raised_179.exception))
            prepared = runner._prepare(args + ["--route-vocabulary", str(vocab_path)])
            self.assertEqual(prepared[9], "npc-speech-act-route-v2")
            self.assertEqual(prepared[10], hashlib.sha256(vocab_path.read_bytes()).hexdigest())
            self.assertIn(str(vocab_path.resolve()), prepared[8])
            self.assertEqual(prepared[7][0]["offeredRoutes"], ["SOCIAL", "ACTION_REQUEST"])

            invalid_suite = root / "invalid-suite.json"
            suite_file(invalid_suite, [case(pendingOffer=None, commitments=[], expectedRoutes=["OFFER_RESPONSE"])])
            invalid_args = ["--template", str(template_path), "--suite", str(invalid_suite),
                            "--run-dir", str(root / "invalid-run"), "--expected-model", "d3-exact",
                            "--route-vocabulary", str(vocab_path)]
            with self.assertRaises(ValueError) as _raised_192:
                runner._prepare(invalid_args)
            self.assertTrue(('unavailable' in str(_raised_192.exception)), str(_raised_192.exception))

    def test_case_text_and_serialized_context_match_native_bounds_and_controls(self):
        too_long_turn = case(playerText="x" * 8193)
        with self.assertRaises(ValueError) as _raised_197:
            runner._validate_case(too_long_turn, set())
        self.assertTrue(('8192' in str(_raised_197.exception)), str(_raised_197.exception))
        controls = case(playerText="hello\x01")
        with self.assertRaises(ValueError) as _raised_200:
            runner._validate_case(controls, set())
        self.assertTrue(('control' in str(_raised_200.exception)), str(_raised_200.exception))
        lone_surrogate = case(playerText="bad\ud800")
        with self.assertRaises(ValueError) as _raised_203:
            runner._validate_case(lone_surrogate, set())
        self.assertTrue(('Unicode' in str(_raised_203.exception)), str(_raised_203.exception))

        huge_context_template = template()
        context = json.loads(json.loads(huge_context_template["state"])["context"])
        context["historicalMemory"]["text"] = "x" * 16300
        state = json.loads(huge_context_template["state"])
        state["context"] = json.dumps(context, separators=(",", ":"))
        huge_context_template["state"] = json.dumps(state, separators=(",", ":"))
        with self.assertRaises(ValueError) as _raised_212:
            runner.build_d3_payload(huge_context_template, case(), reverse=False)
        self.assertTrue(('context' in str(_raised_212.exception) and '16384' in str(_raised_212.exception)), str(_raised_212.exception))

    def test_suite_hash_is_verified_and_expected_routes_are_scoring_only(self):
        item = case()
        with tempfile.TemporaryDirectory() as temp:
            suite = Path(temp) / "suite.json"
            digest = suite_file(suite, [item])
            loaded, computed = runner.load_suite(suite, digest)
        self.assertEqual(loaded["cases"][0], item)
        self.assertEqual(computed, digest)
        with tempfile.TemporaryDirectory() as temp:
            suite = Path(temp) / "suite.json"
            digest = suite_file(suite, [item])
            with self.assertRaises(ValueError):
                runner.load_suite(suite, "0" * 64)

    def test_response_validation_separates_gold_match_wrong_model_probability_and_malformed(self):
        expected = ["ACTION_REQUEST"]
        good = {"model": "d3-exact", "answers": {"decision": {"type": "choice", "choice": "ACTION_REQUEST",
                "probabilities": {"SOCIAL": 0, "ACTION_REQUEST": 1, "OFFER_RESPONSE": 0, "COMMITMENT_UPDATE": 0}}}}
        parsed = runner.parse_d3_response(good, "d3-exact", expected)
        self.assertTrue(parsed["valid"])
        self.assertTrue(parsed["goldAgreement"])
        nonmaximal = {"model": "d3-exact", "answers": {"decision": {"type": "choice", "choice": "SOCIAL",
                       "probabilities": {"SOCIAL": 0.49, "ACTION_REQUEST": 0.51,
                                          "OFFER_RESPONSE": 0, "COMMITMENT_UPDATE": 0}}}}
        self.assertEqual(runner.parse_d3_response(nonmaximal, "d3-exact", expected)["error"],
                         "invalid_probabilities")
        within_core_tolerance = {"model": "d3-exact", "answers": {"decision": {"type": "choice", "choice": "SOCIAL",
                                   "probabilities": {"SOCIAL": 0.5, "ACTION_REQUEST": 0.5000005,
                                                      "OFFER_RESPONSE": 0, "COMMITMENT_UPDATE": 0}}}}
        self.assertTrue(runner.parse_d3_response(within_core_tolerance, "d3-exact", expected)["valid"])
        huge_probability = {"model": "d3-exact", "answers": {"decision": {"type": "choice", "choice": "ACTION_REQUEST",
                             "probabilities": {"SOCIAL": 0, "ACTION_REQUEST": 1, "OFFER_RESPONSE": 0,
                                                "COMMITMENT_UPDATE": 0}}}}
        huge_probability["answers"]["decision"]["probabilities"]["SOCIAL"] = 10 ** 10000
        self.assertEqual(runner.parse_d3_response(huge_probability, "d3-exact", expected)["error"],
                         "invalid_probabilities")
        for response, model, failure in (
                (good, "wrong", "model_mismatch"),
                ({**good, "answers": {"decision": {"type": "choice", "choice": "ACTION_REQUEST",
                  "probabilities": {"SOCIAL": 0.1, "ACTION_REQUEST": 0.1, "OFFER_RESPONSE": 0.1,
                                     "COMMITMENT_UPDATE": 0.1}}}}, "d3-exact", "invalid_probabilities"),
                ({"model": "d3-exact", "answers": {}}, "d3-exact", "invalid_schema")):
            result = runner.parse_d3_response(response, model, expected)
            self.assertFalse(result["valid"])
            self.assertEqual(result["error"], failure)

    def test_qwen_json_schema_reply_requires_exact_model_and_single_route_object(self):
        reply = {"model": "qwen-exact", "choices": [{"finish_reason": "stop",
                 "message": {"content": '{"route":"SOCIAL"}'}}]}
        parsed = runner.parse_qwen_response(reply, "qwen-exact", ["SOCIAL"])
        self.assertTrue(parsed["valid"])
        self.assertTrue(parsed["goldAgreement"])
        wrong_model = runner.parse_qwen_response(reply, "different-model", ["SOCIAL"])
        self.assertEqual(wrong_model["error"], "model_mismatch")
        malformed = {"model": "qwen-exact", "choices": [{"finish_reason": "stop",
                     "message": {"content": '{"route":"SOCIAL","route":"ACTION_REQUEST"}'}}]}
        self.assertEqual(runner.parse_qwen_response(malformed, "qwen-exact", ["SOCIAL"])["error"],
                         "invalid_completion")

    def test_control_false_action_is_distinct_from_abstention_invalid_and_no_result(self):
        control = case(category="quoted_control", route="SOCIAL")
        action = runner.score_route(control, "ACTION_REQUEST", None)
        abstention = runner.score_route(case(route="ACTION_REQUEST"), "SOCIAL", None)
        invalid = runner.score_route(control, None, "invalid_schema")
        no_result = runner.score_route(control, None, "transport_error")
        self.assertTrue(action["falseAction"])
        self.assertEqual(action["resultClass"], "false_action")
        self.assertTrue(abstention["abstention"])
        self.assertEqual(abstention["resultClass"], "abstention")
        self.assertEqual(invalid["resultClass"], "invalid")
        self.assertEqual(no_result["resultClass"], "no_result")

    def test_model_latency_over_native_budget_is_failure_even_when_outer_wall_time_is_short(self):
        item = case()
        response = {"status_code": 200, "transport_error": None, "malformed_json": False,
                    "response_json": {"model": "d3-exact", "answers": {"decision": {"type": "choice",
                    "choice": "ACTION_REQUEST", "probabilities": {"SOCIAL": 0, "ACTION_REQUEST": 1,
                    "OFFER_RESPONSE": 0, "COMMITMENT_UPDATE": 0}}}},
                    "response_raw": json.dumps({"model": "d3-exact", "answers": {"decision": {"type": "choice",
                    "choice": "ACTION_REQUEST", "probabilities": {"SOCIAL": 0, "ACTION_REQUEST": 1,
                    "OFFER_RESPONSE": 0, "COMMITMENT_UPDATE": 0}}}}), "latency_ms": 10001}
        result = runner._model_route_result("D3", "d3-exact", item, response, 5)
        self.assertEqual(result["latencyMs"], 10001)
        self.assertFalse(result["withinNativeBudget"])
        self.assertEqual(result["resultClass"], "over_native_budget")

    def test_raw_response_strict_json_rejects_duplicates_and_nonzero_underflow(self):
        base = '{"model":"d3-exact","answers":{"decision":{"type":"choice","choice":"ACTION_REQUEST","probabilities":{"SOCIAL":0,"ACTION_REQUEST":1,"OFFER_RESPONSE":0,"COMMITMENT_UPDATE":0}}}}'
        for raw in (base.replace('"model":"d3-exact"', '"model":"d3-exact","model":"d3-exact"'),
                    base.replace('"SOCIAL":0', '"SOCIAL":1e-400')):
            response = {"status_code": 200, "transport_error": None, "malformed_json": False,
                        "response_raw": raw, "response_json": json.loads(raw) if "model\":\"d3" in raw and "1e-400" not in raw else None,
                        "latency_ms": 1}
            parsed = runner._model_route_result("D3", "d3-exact", case(), response, 1)
            self.assertFalse(parsed["goldAgreement"])
            self.assertIn(parsed["error"], {"malformed_json", "invalid_probabilities", "invalid_schema"})

    def test_fresh_output_and_non_loopback_validate_before_contact(self):
        item = case()
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            suite = root / "suite.json"
            digest = suite_file(suite, [item])
            source_template = root / "template.json"
            source_template.write_text(json.dumps(template()), encoding="utf-8")
            existing = root / "exists"
            existing.mkdir()
            with patch.object(runner, "game_closed", return_value=None), patch.object(runner, "_get_json", side_effect=AssertionError("contacted")):
                with self.assertRaises(ValueError):
                    runner.run(["--template", str(source_template), "--suite", str(suite), "--run-dir", str(existing),
                                "--expected-model", "d3-exact"])
                fresh = root / "fresh"
                with self.assertRaises(ValueError):
                    runner.run(["--template", str(source_template), "--suite", str(suite), "--run-dir", str(fresh),
                                "--expected-model", "d3-exact", "--d3-url", "http://example.com"])
                self.assertFalse(fresh.exists())

    def test_active_game_guard_runs_before_output_or_service_contact(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp); suite = root / "suite.json"; item = case()
            suite_file(suite, [item]); template_path = root / "native.json"
            template_path.write_text(json.dumps(template()), encoding="utf-8")
            run_dir = root / "run"
            with patch.object(runner, "game_closed", side_effect=RuntimeError("game active")), \
                    patch.object(runner, "_get_json", side_effect=AssertionError("contacted")):
                with self.assertRaises(RuntimeError) as _raised_340:
                    runner.run(["--template", str(template_path), "--suite", str(suite), "--run-dir", str(run_dir),
                                "--expected-model", "d3-exact"])
                self.assertTrue(('game active' in str(_raised_340.exception)), str(_raised_340.exception))
            self.assertFalse(run_dir.exists())

    def test_fake_loopback_replays_every_frozen_arm_and_writes_raw_attempts(self):
        seen = []

        class Handler(BaseHTTPRequestHandler):
            def do_GET(self):
                manifest_path = run_dir / "manifest.json"
                self.server_test.assertTrue(manifest_path.is_file(), "manifest must be durable before endpoint metadata contact")
                frozen_manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
                self.server_test.assertEqual(len(frozen_manifest["payloads"]), 2)
                for frozen in frozen_manifest["payloads"]:
                    payload_path = run_dir / frozen["path"]
                    self.server_test.assertEqual(hashlib.sha256(payload_path.read_bytes()).hexdigest(), frozen["sha256"])
                if self.path.endswith("/v1/models"):
                    data = {"data": [{"id": "d3-exact"}]}
                else:
                    self.send_error(404)
                    return
                self.send_response(200); self.send_header("Content-Type", "application/json"); self.end_headers()
                self.wfile.write(json.dumps(data).encode())

            def do_POST(self):
                raw = self.rfile.read(int(self.headers["Content-Length"]))
                seen.append((self.path, raw))
                payload = json.loads(raw)
                state = json.loads(payload["state"])
                self.send_response(200); self.send_header("Content-Type", "application/json"); self.end_headers()
                body = {"model": "d3-exact", "answers": {"decision": {"type": "choice", "choice": "ACTION_REQUEST",
                        "probabilities": {"SOCIAL": 0, "ACTION_REQUEST": 1, "OFFER_RESPONSE": 0, "COMMITMENT_UPDATE": 0}}}}
                self.wfile.write(json.dumps(body).encode())

            def log_message(self, *_args):
                pass

        server = HTTPServer(("127.0.0.1", 0), Handler)
        thread = threading.Thread(target=server.serve_forever, daemon=True); thread.start()
        self.addCleanup(lambda: (server.shutdown(), thread.join(2), server.server_close()))
        Handler.server_test = self
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp); suite = root / "suite.json"; item = case()
            suite_file(suite, [item]); template_path = root / "native.json"
            template_path.write_text(json.dumps(template()), encoding="utf-8")
            run_dir = root / "run"
            with patch.object(runner, "game_closed", return_value=None):
                summary = runner.run(["--template", str(template_path), "--suite", str(suite),
                                      "--run-dir", str(run_dir), "--d3-url", f"http://127.0.0.1:{server.server_port}",
                                      "--expected-model", "d3-exact"])
            self.assertEqual(len(seen), 2)
            self.assertEqual([path for path, _ in seen], ["/v1/systemone", "/v1/systemone"])
            self.assertEqual(summary["totalAttempts"], 2)
            rows = [json.loads(line) for line in (run_dir / "results.jsonl").read_text(encoding="utf-8").splitlines()]
            self.assertEqual({row["arm"] for row in rows}, {"original", "reversed"})
            for row in rows:
                self.assertEqual(row["d3"]["statusCode"], 200)
                self.assertEqual(row["d3"]["route"], "ACTION_REQUEST")
                self.assertTrue(row["d3"]["goldAgreement"])
                self.assertIn("response_raw", row["d3"]["response"])
            manifest = json.loads((run_dir / "manifest.json").read_text(encoding="utf-8"))
            self.assertEqual(len(manifest["payloads"]), 2)
            self.assertTrue(all(len(entry["sha256"]) == 64 for entry in manifest["payloads"]))
            self.assertTrue(all("expectedRoutes" not in raw.decode() for _, raw in seen))

    def test_optional_qwen_uses_chat_completions_json_schema_route(self):
        seen = []
        class Handler(BaseHTTPRequestHandler):
            def do_GET(self):
                self.send_response(200); self.send_header("Content-Type", "application/json"); self.end_headers()
                self.wfile.write(b'{"data":[{"id":"same-exact"}]}')

            def do_POST(self):
                raw = self.rfile.read(int(self.headers["Content-Length"]))
                payload = json.loads(raw); seen.append((self.path, payload))
                if self.path == "/v1/systemone":
                    response = {"model": "same-exact", "answers": {"decision": {"type": "choice", "choice": "ACTION_REQUEST",
                                 "probabilities": {"SOCIAL": 0, "ACTION_REQUEST": 1, "OFFER_RESPONSE": 0, "COMMITMENT_UPDATE": 0}}}}
                else:
                    response = {"model": "same-exact", "choices": [{"finish_reason": "stop",
                                 "message": {"content": '{"route":"ACTION_REQUEST"}'}}]}
                self.send_response(200); self.send_header("Content-Type", "application/json"); self.end_headers()
                self.wfile.write(json.dumps(response).encode())

            def log_message(self, *_args): pass

        server = HTTPServer(("127.0.0.1", 0), Handler)
        thread = threading.Thread(target=server.serve_forever, daemon=True); thread.start()
        self.addCleanup(lambda: (server.shutdown(), thread.join(2), server.server_close()))
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp); suite = root / "suite.json"; suite_file(suite, [case()])
            template_path = root / "native.json"; template_path.write_text(json.dumps(template()), encoding="utf-8")
            run_dir = root / "run"
            with patch.object(runner, "game_closed", return_value=None):
                summary = runner.run(["--template", str(template_path), "--suite", str(suite), "--run-dir", str(run_dir),
                                      "--d3-url", f"http://127.0.0.1:{server.server_port}", "--expected-model", "same-exact",
                                      "--qwen-url", f"http://127.0.0.1:{server.server_port}",
                                      "--expected-qwen-model", "same-exact"])
            self.assertEqual(len(seen), 4)
            self.assertEqual([path for path, _ in seen], ["/v1/systemone", "/v1/chat/completions"] * 2)
            qwen_payload = seen[1][1]
            self.assertEqual(qwen_payload["model"], "same-exact")
            self.assertEqual(qwen_payload["response_format"]["type"], "json_schema")
            self.assertEqual(qwen_payload["response_format"]["json_schema"]["schema"]["properties"]["route"]["enum"],
                             ["SOCIAL", "ACTION_REQUEST", "OFFER_RESPONSE", "COMMITMENT_UPDATE"])
            self.assertNotIn("expectedRoutes", json.dumps(qwen_payload))
            self.assertEqual(summary["totalAttempts"], 4)
            self.assertEqual(summary["models"]["Qwen"]["goldAgreement"], 2)


if __name__ == "__main__":
    unittest.main()
