import json
import tempfile
import unittest
from pathlib import Path

from test_run_serial_evaluation import prepared_row
from run_selector_comparison import run_comparison


class SelectorComparisonTests(unittest.TestCase):
    def write_input(self, root):
        path = root / "prepared.jsonl"
        path.write_text(json.dumps(prepared_row("ep/arm/t1")) + "\n", encoding="utf-8")
        return path

    def test_dry_run_validates_without_network_or_outputs(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            path = self.write_input(root)
            result = run_comparison(path, root / "paired", "test-model", transport=lambda *_: self.fail("network"))
            self.assertEqual(result["mode"], "preflight")
            self.assertEqual(result["preparedRows"], 1)
            self.assertFalse((root / "paired").exists())

    def test_same_inputs_both_arms_preserve_actual_selector_bytes(self):
        calls = []

        def transport(url, body):
            calls.append((url, body))
            if url.endswith("systemone"):
                response = b'{"model":"d3","answers":{"decision":{"type":"choice","choice":"decline"}}}'
            elif url.endswith("chat/completions"):
                response = json.dumps({"model": "test-model", "choices": [{"message": {"content": '{"choice":"ask_terms"}'}}]}).encode()
            else:
                response = b'{"content":"A grounded reply."}'
            return {"status": 200, "body": response, "elapsedMs": 2.0, "headers": {}}

        def bridge(prepared, decisions, plans, rows):
            evidence = json.loads(decisions.read_text().strip())
            choice = json.loads(evidence["rawResponse"])["answers"]["decision"]["choice"]
            return [{"requestId": row["requestId"], "validated": True, "selectedOptionId": choice,
                     "qwenRequestBody": '{"prompt":"same formatter, selected preference"}'} for row in rows]

        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            result = run_comparison(self.write_input(root), root / "paired", "test-model",
                                    execute=True, transport=transport, core_bridge=bridge)
            self.assertEqual(result["mode"], "executed")
            self.assertEqual([url.rsplit("/", 1)[-1] for url, _ in calls],
                             ["systemone", "completion", "completions", "completion"])
            manifest = json.loads((root / "paired" / "manifest.json").read_text())
            self.assertEqual(manifest["arms"][0]["preparedSha256"], manifest["arms"][1]["preparedSha256"])
            evidence = [json.loads(line) for line in (root / "paired" / "qwen-selector.raw.jsonl").read_text().splitlines()]
            self.assertEqual([row["phase"] for row in evidence], ["raw", "converted"])
            raw = evidence[-1]
            self.assertEqual(raw["httpStatus"], 200)
            self.assertIn('"choices"', raw["rawResponse"])
            self.assertEqual(json.loads(raw["canonicalDecisionResponse"])["answers"]["decision"]["choice"], "ask_terms")
            self.assertEqual(raw["inputD3RequestSha256"], prepared_row("ep/arm/t1")["d3RequestSha256"])

    def test_existing_output_refused_before_any_network(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            path = self.write_input(root)
            output = root / "paired"
            output.mkdir()
            with self.assertRaises(FileExistsError):
                run_comparison(path, output, "test-model", execute=True, transport=lambda *_: self.fail("network"))

    def test_invalid_qwen_choice_preserved_without_reply_or_retry(self):
        calls = []

        def transport(url, body):
            calls.append(url)
            if url.endswith("chat/completions"):
                response = b'{"model":"test-model","choices":[{"message":{"content":"{\\"choice\\":\\"invented\\"}"}}]}'
            elif url.endswith("systemone"):
                response = b'{"answers":{"decision":{"type":"choice","choice":"decline"}}}'
            else:
                response = b'{"content":"reply"}'
            return {"status": 200, "body": response, "elapsedMs": 1.0}

        def bridge(prepared, decisions, plans, rows):
            row = json.loads(decisions.read_text().strip())
            valid = row["httpStatus"] == 200
            return [{"requestId": rows[0]["requestId"], "validated": valid,
                     "selectedOptionId": "decline" if valid else None,
                     "qwenRequestBody": '{"prompt":"p"}' if valid else None}]

        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            result = run_comparison(self.write_input(root), root / "paired", "test-model",
                                    execute=True, transport=transport, core_bridge=bridge)
            self.assertEqual(len(calls), 3)
            self.assertEqual(result["arms"][1]["summary"]["failedRows"], 1)
            evidence = [json.loads(line) for line in (root / "paired" / "qwen-selector.raw.jsonl").read_text().splitlines()]
            raw = evidence[-1]
            self.assertIn("invented", raw["rawResponse"])
            self.assertIn("not offered", raw["conversionError"])

    def test_core_bridge_receives_only_selected_complete_episodes(self):
        def transport(url, body):
            raw = ({"model": "test-model", "choices": [{"message": {"content": '{"choice":"ask_terms"}'}}]}
                   if url.endswith("chat/completions") else
                   {"model": "d3", "answers": {"decision": {"type": "choice", "choice": "ask_terms"}}})
            return {"status": 200, "body": json.dumps(raw).encode(), "elapsedMs": 1}

        def bridge(prepared, decisions, plans, rows):
            self.assertEqual(len(prepared.read_text().splitlines()), 1)
            self.assertEqual(len(rows), 1)
            return [{"requestId": rows[0]["requestId"], "validated": True,
                     "selectedOptionId": "ask_terms", "qwenRequestBody": '{"prompt":"p"}'}]

        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            first, second = prepared_row("ep/arm/t1"), prepared_row("ep2/arm/t1")
            second["episodeId"] = "ep2"
            path = root / "prepared.jsonl"
            path.write_text(json.dumps(first) + "\n" + json.dumps(second) + "\n", encoding="utf-8")
            result = run_comparison(path, root / "paired", "test-model", execute=True,
                                    limit=1, transport=transport, core_bridge=bridge)
            self.assertEqual(result["preparedRows"], 1)


if __name__ == "__main__":
    unittest.main()
