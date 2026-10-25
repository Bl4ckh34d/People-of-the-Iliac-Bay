import json
import sys
import tempfile
import unittest
from unittest.mock import patch
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from run_paired_frame_pilot import evaluate_pair, run_paired, _core_results
from test_decision_frame_pilot import case, arguments


class PairedFrameTests(unittest.TestCase):
    def test_core_bridge_requires_exact_result_ids_without_omissions_or_duplicates(self):
        proposals = [{"id": "case-a", "steps": []}, {"id": "case-b", "steps": []}]
        for returned in ([], [{"id": "case-a"}], [{"id": "case-a"}, {"id": "case-a"}],
                         [{"id": "case-a"}, {"id": "unknown"}]):
            with self.subTest(returned=returned):
                with patch("run_paired_frame_pilot.subprocess.run") as run:
                    run.return_value.returncode = 0
                    run.return_value.stdout = json.dumps(returned)
                    with self.assertRaises(ValueError) as _raised_22:
                        _core_results("unused.dll", proposals)
                    self.assertTrue(('Core result' in str(_raised_22.exception)), str(_raised_22.exception))

    def test_core_empty_proposals_still_require_array_response(self):
        with patch("run_paired_frame_pilot.subprocess.run") as run:
            run.return_value.returncode = 0
            run.return_value.stdout = "{}"
            with self.assertRaises(ValueError) as _raised_29:
                _core_results("unused.dll", [])
            self.assertTrue(('Core result' in str(_raised_29.exception)), str(_raised_29.exception))

    def transport(self, calls):
        def send(url, payload):
            calls.append((url, payload))
            if url.endswith("systemone"):
                state = json.loads(payload["state"])
                selected = "Wait" if not state["selectedPrefix"] else "END"
                outer = {"model": "mini", "answers": {key: {
                    "type": "choice", "choice": selected if key == "frame" else "no",
                    "probabilities": {k: float(k == (selected if key == "frame" else "no")) for k in question["criteria"]},
                } for key, question in payload["questions"].items()}}
            else:
                schema = payload["response_format"]["json_schema"]["name"]
                if schema == "decision_frame_selection_v1":
                    visible = json.loads(payload["messages"][1]["content"])
                    state = json.loads(visible["state"])
                    value = {key: ("Wait" if not state["selectedPrefix"] else "END") if key == "frame" else "no"
                             for key in visible["questions"]}
                elif "arguments" in schema:
                    value = arguments()
                else:
                    value = {"bindings": [{"stepIndex": 0, "candidateRef": "wait_here"}]}
                outer = {"model": "qwen", "choices": [{"finish_reason": "stop", "message": {"content": json.dumps(value)}}]}
            return {"status_code": 200, "latency_ms": 1, "response_raw": json.dumps(outer), "response_json": outer}
        return send

    def test_both_selectors_share_frame_data_and_grounder_without_gold(self):
        calls = []
        with tempfile.TemporaryDirectory() as directory:
            rows = evaluate_pair(case(), {"d3": "mini", "qwen": "qwen"}, Path(directory),
                                 transport=self.transport(calls))
            self.assertEqual([r["groundingOutcome"] for r in rows], ["plan", "plan"])
            self.assertEqual([r["planSteps"][0]["durationSeconds"] for r in rows], [20, 20])
            self.assertEqual([r["selector"] for r in rows], ["d3", "qwen"])
            d3 = next(p for u, p in calls if u.endswith("systemone"))
            qwen = next(p for u, p in calls if p.get("response_format", {}).get("json_schema", {}).get("name") == "decision_frame_selection_v1")
            visible = json.loads(qwen["messages"][1]["content"])
            self.assertEqual(d3["state"], visible["state"])
            self.assertEqual({key: {field: value for field, value in question.items() if field != "type"}
                              for key, question in d3["questions"].items()}, visible["questions"])
            self.assertNotIn("expected", json.dumps([p for _, p in calls]))
            self.assertEqual(len((Path(directory) / "attempts.jsonl").read_text().splitlines()), 8)

    def test_invalid_qwen_frame_stops_that_arm_without_parameter_call_or_reroll(self):
        calls = []
        normal = self.transport(calls)
        def send(url, payload):
            if payload.get("response_format", {}).get("json_schema", {}).get("name") == "decision_frame_selection_v1":
                calls.append((url, payload))
                return {"status_code": 200, "latency_ms": 1, "response_raw": '{"model":"qwen","choices":[]}'}
            return normal(url, payload)
        with tempfile.TemporaryDirectory() as directory:
            rows = evaluate_pair(case(), {"d3": "mini", "qwen": "qwen"}, Path(directory), transport=send)
            self.assertEqual(rows[1]["groundingOutcome"], "invalid")
            self.assertEqual(sum(p.get("response_format", {}).get("json_schema", {}).get("name") == "decision_frame_selection_v1" for _, p in calls), 1)
            raw = (Path(directory) / "attempts.jsonl").read_text()
            self.assertIn('choices', raw)

    def test_existing_run_directory_refused_before_transport(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            fixture = root / "suite.json"
            fixture.write_text(json.dumps({"cases": [case()]}))
            with self.assertRaises(FileExistsError):
                run_paired(fixture, root, {"d3": "mini", "qwen": "qwen"}, transport=lambda *_: self.fail("network"))

    def test_core_invalid_reference_fixture_fails_before_any_model_call(self):
        fixture = Path(__file__).resolve().parents[1] / "fixtures/paired-complex-frames-v1.json"
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaises(ValueError) as _raised_100:
                run_paired(fixture, Path(directory) / "run", {"d3": "mini", "qwen": "qwen"}, execute=True,
                           transport=lambda *_: self.fail("Invalid fixture must never contact a model"))
            self.assertTrue(('reference' in str(_raised_100.exception) and 'Core' in str(_raised_100.exception)), str(_raised_100.exception))


if __name__ == "__main__":
    unittest.main()
