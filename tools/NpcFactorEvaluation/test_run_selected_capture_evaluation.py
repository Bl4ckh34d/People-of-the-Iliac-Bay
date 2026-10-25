import hashlib
import json
import tempfile
import threading
import unittest
from unittest.mock import patch
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

from run_selected_capture_evaluation import run_selected_capture_evaluation


class StubHandler(BaseHTTPRequestHandler):
    posts = []
    model_path = "C:/models/qwen.gguf"
    response_model = "C:/models/qwen.gguf"

    def log_message(self, *_args):
        pass

    def do_GET(self):
        payload = ({"status": "ok"} if self.path == "/health" else
                   {"model_path": self.model_path} if self.path == "/props" else {})
        body = json.dumps(payload).encode()
        self.send_response(200 if self.path in {"/health", "/props"} else 404)
        self.send_header("Content-Type", "application/json")
        self.end_headers()
        self.wfile.write(body)

    def do_POST(self):
        body = self.rfile.read(int(self.headers.get("Content-Length", "0")))
        self.posts.append((self.path, body))
        payload = {"content": "raw generated output", "model": self.response_model,
                   "tokens_evaluated": 11, "tokens_predicted": 4}
        result = json.dumps(payload).encode()
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.end_headers()
        self.wfile.write(result)


class SelectedCaptureEvaluationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.server = ThreadingHTTPServer(("127.0.0.1", 0), StubHandler)
        cls.server_thread = threading.Thread(target=cls.server.serve_forever, daemon=True)
        cls.server_thread.start()
        cls.base_url = f"http://127.0.0.1:{cls.server.server_port}"

    @classmethod
    def tearDownClass(cls):
        cls.server.shutdown()
        cls.server.server_close()
        cls.server_thread.join(timeout=2)

    def setUp(self):
        StubHandler.posts = []
        StubHandler.model_path = "C:/models/qwen.gguf"
        StubHandler.response_model = StubHandler.model_path
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.repo = self.root / "repo"
        self.repo.mkdir()
        self.source = self.repo / "fixture.json"
        self.source.write_text("fixture", encoding="utf-8")
        self.input_path = self.root / "cases.jsonl"
        self.output_dir = self.root / "run"

    def tearDown(self):
        self.temp.cleanup()

    def write_cases(self, rows):
        self.input_path.write_text("".join(json.dumps(row, separators=(",", ":")) + "\n" for row in rows),
                                   encoding="utf-8")

    def row(self, case_id="case-1", prompt="say hello"):
        return {"caseId": case_id, "lane": "qwen", "prompt": prompt,
                "expectedModel": "C:/models/qwen.gguf",
                "sourceHashes": {"fixture.json": hashlib.sha256(b"fixture").hexdigest()}}

    def run_capture(self):
        return run_selected_capture_evaluation(
            self.input_path, self.output_dir, repo_root=self.repo,
            qwen_endpoint=self.base_url + "/completion",
            d3nano_endpoint=self.base_url + "/v1/systemone", timeout=2)

    def test_saves_one_exact_request_and_raw_response_with_latency_metadata(self):
        row = self.row()
        self.write_cases([row])
        summary = self.run_capture()
        self.assertEqual(summary["sent"], 1)
        self.assertEqual(len(StubHandler.posts), 1)
        self.assertEqual(StubHandler.posts[0][0], "/completion")
        self.assertEqual(json.loads(StubHandler.posts[0][1]), {"prompt": "say hello"})
        events = [json.loads(line) for line in (self.output_dir / "journal.jsonl").read_text().splitlines()]
        self.assertEqual([event["event"] for event in events], ["attempt_started", "completed"])
        terminal = events[-1]
        self.assertEqual(Path(self.output_dir, terminal["responsePath"]).read_bytes(),
                         json.dumps({"content": "raw generated output", "model": StubHandler.model_path,
                                     "tokens_evaluated": 11, "tokens_predicted": 4}).encode())
        self.assertEqual(terminal["responseMetadata"]["tokens_predicted"], 4)
        self.assertGreaterEqual(terminal["latencyMs"], 0)

    def test_d3nano_sends_exported_wire_body_verbatim(self):
        row = {"caseId": "d3-case", "lane": "d3nano",
               "requestBody": '{"state": "snapshot", "questions": {}}',
               "expectedModel": "C:/models/qwen.gguf",
               "sourceHashes": {"fixture.json": hashlib.sha256(b"fixture").hexdigest()}}
        self.write_cases([row])
        run_selected_capture_evaluation(
            self.input_path, self.output_dir, repo_root=self.repo,
            qwen_endpoint=self.base_url + "/completion",
            d3nano_endpoint=self.base_url + "/v1/systemone", timeout=2)
        self.assertEqual(StubHandler.posts, [
            ("/v1/systemone", b'{"state": "snapshot", "questions": {}}')])

    def test_source_hash_mismatch_stops_before_post(self):
        self.write_cases([self.row()])
        self.source.write_text("changed", encoding="utf-8")
        with self.assertRaises(ValueError) as _raised_120:
            self.run_capture()
        self.assertTrue(('source hash mismatch' in str(_raised_120.exception)), str(_raised_120.exception))
        self.assertEqual(StubHandler.posts, [])

    def test_rerun_skips_terminal_case_and_never_posts_twice(self):
        self.write_cases([self.row()])
        self.run_capture()
        summary = self.run_capture()
        self.assertEqual(summary["skippedTerminal"], 1)
        self.assertEqual(len(StubHandler.posts), 1)

    def test_changed_response_model_is_failed_and_preserved_without_retry(self):
        self.write_cases([self.row()])
        StubHandler.response_model = "swapped-model"
        first = self.run_capture()
        second = self.run_capture()
        events = [json.loads(line) for line in (self.output_dir / "journal.jsonl").read_text().splitlines()]
        self.assertEqual(first["failed"], 1)
        self.assertEqual(first["sent"], 0)
        self.assertEqual(second["skippedTerminal"], 1)
        self.assertEqual(events[-1]["event"], "failed")
        self.assertIn("model", events[-1]["transportError"])
        self.assertEqual(json.loads((self.output_dir / events[-1]["responsePath"]).read_bytes())["model"], "swapped-model")
        self.assertEqual(len(StubHandler.posts), 1)

    def test_missing_response_model_is_failed(self):
        self.write_cases([self.row()])
        StubHandler.response_model = None
        self.assertEqual(self.run_capture()["failed"], 1)

    def test_terminal_resume_does_not_need_running_servers(self):
        self.write_cases([self.row()])
        self.run_capture()
        with patch("run_selected_capture_evaluation._http_request", side_effect=AssertionError("offline resume made HTTP call")):
            self.assertEqual(self.run_capture()["skippedTerminal"], 1)

    def test_crash_after_response_write_resumes_offline_without_retry(self):
        self.write_cases([self.row()])
        self.run_capture()
        journal = self.output_dir / "journal.jsonl"
        journal.write_text(journal.read_text().splitlines()[0] + "\n", encoding="utf-8")
        with patch("run_selected_capture_evaluation._http_request", side_effect=AssertionError("uncertain attempt made HTTP call")):
            self.assertEqual(self.run_capture()["uncertain"], 1)
            self.assertEqual(self.run_capture()["skippedTerminal"], 1)
        self.assertEqual(len(StubHandler.posts), 1)

    def test_changed_case_hash_refuses_resume(self):
        self.write_cases([self.row()])
        self.run_capture()
        self.write_cases([self.row(prompt="changed request")])
        with self.assertRaises(ValueError) as _raised_170:
            self.run_capture()
        self.assertTrue(('input hash mismatch' in str(_raised_170.exception)), str(_raised_170.exception))
        self.assertEqual(len(StubHandler.posts), 1)

    def test_loaded_model_identity_mismatch_stops_before_post(self):
        self.write_cases([self.row()])
        StubHandler.model_path = "C:/models/other.gguf"
        with self.assertRaises(ValueError) as _raised_177:
            self.run_capture()
        self.assertTrue(('model identity mismatch' in str(_raised_177.exception)), str(_raised_177.exception))
        self.assertEqual(StubHandler.posts, [])

    def test_case_id_path_escape_is_rejected(self):
        self.write_cases([self.row(case_id="../escape")])
        with self.assertRaises(ValueError) as _raised_183:
            self.run_capture()
        self.assertTrue(('caseId' in str(_raised_183.exception)), str(_raised_183.exception))
        self.assertFalse((self.root / "escape").exists())
        self.assertEqual(StubHandler.posts, [])

    def test_interrupted_started_attempt_is_marked_uncertain_without_retry(self):
        self.write_cases([self.row()])
        self.output_dir.mkdir()
        (self.output_dir / "owner.json").write_text(json.dumps({
            "inputSha256": hashlib.sha256(self.input_path.read_bytes()).hexdigest(),
            "qwenEndpoint": self.base_url + "/completion",
            "d3nanoEndpoint": self.base_url + "/v1/systemone",
            "schema": "selected-capture-run-v1",
            "sourceHashes": {"case-1": {"fixture.json": hashlib.sha256(b"fixture").hexdigest()}},
        }), encoding="utf-8")
        (self.output_dir / "journal.jsonl").write_text(json.dumps({
            "event": "attempt_started", "caseId": "case-1",
            "caseHash": hashlib.sha256(json.dumps(self.row(), ensure_ascii=False,
                                                  sort_keys=True, separators=(",", ":")).encode()).hexdigest(),
        }) + "\n", encoding="utf-8")
        summary = self.run_capture()
        events = [json.loads(line) for line in (self.output_dir / "journal.jsonl").read_text().splitlines()]
        self.assertEqual(summary["uncertain"], 1)
        self.assertEqual([event["event"] for event in events], ["attempt_started", "uncertain"])
        self.assertEqual(StubHandler.posts, [])

    def test_orphan_request_artifact_uncertainty_remains_resumable_without_post(self):
        row = self.row()
        self.write_cases([row])
        self.output_dir.mkdir()
        (self.output_dir / "owner.json").write_text(json.dumps({
            "schema": "selected-capture-run-v1",
            "inputSha256": hashlib.sha256(self.input_path.read_bytes()).hexdigest(),
            "qwenEndpoint": self.base_url + "/completion",
            "d3nanoEndpoint": self.base_url + "/v1/systemone",
            "sourceHashes": {"case-1": {"fixture.json": hashlib.sha256(b"fixture").hexdigest()}},
        }), encoding="utf-8")
        case_dir = self.output_dir / "cases" / hashlib.sha256(b"case-1").hexdigest()
        case_dir.mkdir(parents=True)
        (case_dir / "request.json").write_text('{"prompt":"say hello"}', encoding="utf-8")

        first = self.run_capture()
        second = self.run_capture()

        events = [json.loads(line) for line in (self.output_dir / "journal.jsonl").read_text().splitlines()]
        self.assertEqual(first["uncertain"], 1)
        self.assertEqual(second["skippedTerminal"], 1)
        self.assertEqual([event["event"] for event in events], ["uncertain"])
        self.assertEqual(StubHandler.posts, [])

    def test_journal_case_hash_mismatch_refuses_resume(self):
        self.write_cases([self.row()])
        self.run_capture()
        journal = self.output_dir / "journal.jsonl"
        events = [json.loads(line) for line in journal.read_text().splitlines()]
        events[-1]["caseHash"] = "0" * 64
        journal.write_text("".join(json.dumps(event) + "\n" for event in events), encoding="utf-8")
        with self.assertRaises(ValueError) as _raised_240:
            self.run_capture()
        self.assertTrue(('case hash mismatch' in str(_raised_240.exception)), str(_raised_240.exception))
        self.assertEqual(len(StubHandler.posts), 1)

    def test_source_path_escape_is_rejected(self):
        row = self.row()
        row["sourceHashes"] = {"../fixture.json": hashlib.sha256(b"fixture").hexdigest()}
        self.write_cases([row])
        with self.assertRaises(ValueError) as _raised_248:
            self.run_capture()
        self.assertTrue(('escapes repository root' in str(_raised_248.exception)), str(_raised_248.exception))
        self.assertEqual(StubHandler.posts, [])

    def test_output_owner_mismatch_refuses_resume(self):
        self.write_cases([self.row()])
        self.run_capture()
        owner_path = self.output_dir / "owner.json"
        owner = json.loads(owner_path.read_text(encoding="utf-8"))
        owner["qwenEndpoint"] = self.base_url + "/different"
        owner_path.write_text(json.dumps(owner), encoding="utf-8")
        with self.assertRaises(ValueError) as _raised_259:
            self.run_capture()
        self.assertTrue(('ownership or input hash mismatch' in str(_raised_259.exception)), str(_raised_259.exception))
        self.assertEqual(len(StubHandler.posts), 1)

    def test_timeout_is_terminal_and_never_retried(self):
        self.write_cases([self.row()])
        calls = []

        def timed_out(endpoint, body, timeout):
            calls.append((endpoint, body, timeout))
            return {"status": None, "body": b"", "elapsedMs": 17.0,
                    "headers": {}, "error": "TimeoutError: test timeout"}

        run_selected_capture_evaluation(
            self.input_path, self.output_dir, repo_root=self.repo,
            qwen_endpoint=self.base_url + "/completion",
            d3nano_endpoint=self.base_url + "/v1/systemone", timeout=2, transport=timed_out)
        result = self.run_capture()
        events = [json.loads(line) for line in (self.output_dir / "journal.jsonl").read_text().splitlines()]
        self.assertEqual(len(calls), 1)
        self.assertEqual(result["skippedTerminal"], 1)
        self.assertEqual(events[-1]["event"], "failed")
        self.assertEqual(events[-1]["transportError"], "TimeoutError: test timeout")


if __name__ == "__main__":
    unittest.main()
