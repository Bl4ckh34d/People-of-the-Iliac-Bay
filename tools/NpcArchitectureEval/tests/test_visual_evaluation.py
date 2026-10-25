import base64
import contextlib
import hashlib
import io
import json
import sys
import tempfile
import threading
import unittest
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import run_visual_evaluation as visual


class VisualEvaluationTests(unittest.TestCase):
    def setUp(self):
        self.received = []

        owner = self

        class Handler(BaseHTTPRequestHandler):
            def do_GET(self):
                body = json.dumps({"data": [{"id": "test-d3-model"}]}).encode("utf-8")
                self.send_response(200)
                self.send_header("Content-Type", "application/json")
                self.end_headers()
                self.wfile.write(body)

            def do_POST(self):
                body = self.rfile.read(int(self.headers.get("Content-Length", "0")))
                owner.received.append(json.loads(body.decode("utf-8")))
                criteria = owner.received[-1]["questions"]["decision"]["criteria"]
                choice = next(iter(criteria))
                response = json.dumps({"answers": {"decision": {"choice": choice}}}).encode("utf-8")
                self.send_response(200)
                self.send_header("Content-Type", "application/json")
                self.end_headers()
                self.wfile.write(response)

            def log_message(self, _format, *args):
                pass

        self.server = HTTPServer(("127.0.0.1", 0), Handler)
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
        def stop_server():
            self.server.shutdown()
            self.thread.join(2)
            self.server.server_close()
        self.addCleanup(stop_server)

    def test_exact_image_request_no_gold_leakage_order_pairs_and_audit_hashes(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            image_bytes = b"\x89PNG\r\n\x1a\nfrozen original bytes"
            (root / "sample.png").write_bytes(image_bytes)
            suite_path = root / "suite.json"
            suite_path.write_text(json.dumps({"cases": [{
                "id": "still-1",
                "imagePath": "sample.png",
                "state": "Classify the scene visible in the supplied image.",
                "question": "Which visible scene category best matches the image?",
                "criteria": {"outside_snow": "Outdoor snow", "inside_room": "Furnished indoor room", "unclear": "Cannot tell"},
                "expected": ["outside_snow"],
                "provenance": "synthetic test fixture",
            }]}), encoding="utf-8")
            run_dir = root / "run"
            url = f"http://127.0.0.1:{self.server.server_port}"
            with patch.object(visual, "game_closed", lambda: None), contextlib.redirect_stdout(io.StringIO()):
                visual.run(["--suite", str(suite_path), "--run-dir", str(run_dir), "--d3-url", url])

            self.assertEqual(len(self.received), 2)
            first, second = self.received
            self.assertEqual(list(first), ["state", "images", "questions"])
            self.assertEqual(list(first["questions"]["decision"]["criteria"]), ["outside_snow", "inside_room", "unclear"])
            self.assertEqual(list(second["questions"]["decision"]["criteria"]), ["unclear", "inside_room", "outside_snow"])
            self.assertEqual(base64.b64decode(first["images"][0].split(",", 1)[1]), image_bytes)
            self.assertNotIn("expected", json.dumps(self.received))
            self.assertNotIn("provenance", json.dumps(self.received))
            manifest = json.loads((run_dir / "manifest.json").read_text(encoding="utf-8"))
            self.assertEqual(manifest["images"][0]["sha256"], hashlib.sha256(image_bytes).hexdigest())
            rows = [json.loads(line) for line in (run_dir / "results.jsonl").read_text(encoding="utf-8").splitlines()]
            self.assertEqual([row["arm"] for row in rows], ["original", "reversed"])
            self.assertEqual([row["choice"] for row in rows], ["outside_snow", "unclear"])
            self.assertEqual([row["correct"] for row in rows], [True, False])
            summary = json.loads((run_dir / "summary.json").read_text(encoding="utf-8"))
            self.assertEqual(summary["attempts"], 2)
            self.assertEqual(summary["orderChanges"], 1)

    def test_response_errors_are_explicit(self):
        self.assertEqual(visual._read_choice({"status_code": None, "transport_error": "offline"}), (None, "transport_error"))
        self.assertEqual(visual._read_choice({"status_code": 200, "malformed_json": True}), (None, "malformed_json"))
        self.assertEqual(visual._read_choice({"status_code": 200, "response_json": {}}), (None, "malformed_choice_response"))


if __name__ == "__main__":
    unittest.main()
