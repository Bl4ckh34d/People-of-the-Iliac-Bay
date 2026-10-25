import json
import tempfile
import unittest
from unittest.mock import patch
from pathlib import Path

from test_appraisal_preparation_contract import fixture_rows, fixture_rows_v3
from run_appraisal_evaluation import run_appraisal_evaluation, _bridge_core, _preflight_core


class AppraisalEvaluationRunnerTests(unittest.TestCase):
    def test_core_preflight_and_response_bridge_use_release_no_build_binary(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            project = root / "NpcFactorEvaluation.csproj"
            prepared = root / "prepared.jsonl"
            responses = root / "responses.jsonl"
            output = root / "validated.jsonl"
            results = [type("Completed", (), {"returncode": 0, "stdout": "ok", "stderr": ""})(),
                       type("Completed", (), {"returncode": 0, "stdout": "ok", "stderr": ""})()]
            with patch("run_appraisal_evaluation.subprocess.run", side_effect=results) as run:
                _preflight_core(prepared, project, "v2")
                _bridge_core(prepared, responses, output, project, "v2")
            expected_preflight = [
                "dotnet", "run", "--configuration", "Release", "--no-build", "--project", str(project), "--",
                "validate-appraisal-prepared", "--prepared", str(prepared), "--fixture-version", "v2",
            ]
            expected_bridge = [
                "dotnet", "run", "--configuration", "Release", "--no-build", "--project", str(project), "--",
                "record-appraisal", "--prepared", str(prepared), "--responses", str(responses),
                "--output", str(output), "--fixture-version", "v2",
            ]
            self.assertEqual(run.call_args_list[0].args[0], expected_preflight)
            self.assertEqual(run.call_args_list[1].args[0], expected_bridge)

    def test_core_preflight_reports_stdout_and_stderr_on_failure(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            project = root / "NpcFactorEvaluation.csproj"
            completed = type("Completed", (), {
                "returncode": 1,
                "stdout": "prepared bytes differ from current Core",
                "stderr": "fixture version mismatch",
            })()
            with patch("run_appraisal_evaluation.subprocess.run", return_value=completed):
                with self.assertRaises(RuntimeError) as _raised_46:
                    _preflight_core(root / "prepared.jsonl", project, "v2")
                self.assertTrue(('prepared bytes differ' in str(_raised_46.exception) and 'fixture version mismatch' in str(_raised_46.exception)), str(_raised_46.exception))

    def test_cause_corrected_v2_fixture_is_a_separate_serial_run_contract(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            prepared = root / "prepared-v2.jsonl"
            output = root / "responses-v2.jsonl"
            validated = root / "validated-v2.jsonl"
            prepared.write_text("".join(json.dumps(row) + "\n" for row in fixture_rows("v2")), encoding="utf-8")
            calls = []

            def core_bridge(prepared_path, response_path, validated_path):
                rows = [json.loads(line) for line in response_path.read_text(encoding="utf-8").splitlines()]
                results = [{"caseId": row["caseId"], "coreValidated": False, "failureKind": "core_parser_rejected"}
                           for row in rows]
                validated_path.write_text("".join(json.dumps(row) + "\n" for row in results), encoding="utf-8")

            summary = run_appraisal_evaluation(prepared, output, validated,
                                               fixture_version="v2",
                                               transport=lambda url, body: calls.append((url, body)) or {
                                                   "status": 200,
                                                   "body": json.dumps({"content": "{}", "model": "offline-test"}).encode(),
                                                   "elapsedMs": 1.0, "error": None,
                                               },
                                               core_bridge=core_bridge,
                                               core_preflight=lambda *_args: None)
            self.assertEqual(summary["modelCalls"], 32)
            raw = [json.loads(line) for line in output.read_text(encoding="utf-8").splitlines()]
            self.assertEqual(len(raw), 32)
            self.assertEqual(len(calls), 32)

    def test_v3_manifest_runs_all_38_rows_once_without_splitting_or_retrying(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            prepared = root / "prepared-v3.jsonl"
            output = root / "responses-v3.jsonl"
            validated = root / "validated-v3.jsonl"
            prepared.write_text("".join(json.dumps(row) + "\n" for row in fixture_rows_v3()), encoding="utf-8")
            calls = []

            def core_bridge(prepared_path, response_path, validated_path):
                rows = [json.loads(line) for line in response_path.read_text(encoding="utf-8").splitlines()]
                results = [{"caseId": row["caseId"], "coreValidated": False, "failureKind": "core_parser_rejected"}
                           for row in rows]
                validated_path.write_text("".join(json.dumps(row) + "\n" for row in results), encoding="utf-8")

            summary = run_appraisal_evaluation(prepared, output, validated,
                                               fixture_version="v3",
                                               transport=lambda url, body: calls.append((url, body)) or {
                                                   "status": 200,
                                                   "body": json.dumps({"content": "{}", "model": "offline-test"}).encode(),
                                                   "elapsedMs": 1.0, "error": None,
                                               },
                                               core_bridge=core_bridge,
                                               core_preflight=lambda *_args: None)
            self.assertEqual(summary["preparedRows"], 38)
            self.assertEqual(summary["modelCalls"], 38)
            self.assertEqual(len(calls), 38)
            raw = [json.loads(line) for line in output.read_text(encoding="utf-8").splitlines()]
            self.assertEqual(len(raw), 38)

    def test_sends_each_prepared_body_once_and_preserves_failed_attempts(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            prepared = root / "prepared.jsonl"
            output = root / "responses.jsonl"
            validated = root / "validated.jsonl"
            prepared.write_text("".join(json.dumps(row) + "\n" for row in fixture_rows()), encoding="utf-8")
            calls = []
            preflights = []

            def transport(url, body):
                calls.append((url, body))
                if len(calls) == 1:
                    return {"status": None, "body": b"", "elapsedMs": 3.5, "error": "timeout"}
                return {
                    "status": 200,
                    "body": json.dumps({"content": "{\"raw\":true}", "model": "fixture-model"}).encode(),
                    "elapsedMs": 7.0,
                    "error": None,
                }

            def core_bridge(prepared_path, response_path, validated_path):
                rows = [json.loads(line) for line in response_path.read_text(encoding="utf-8").splitlines()]
                results = [{"caseId": row["caseId"], "coreValidated": False,
                            "failureKind": "transport_error" if row["transportError"] else "core_parser_rejected"}
                           for row in rows]
                validated_path.write_text("".join(json.dumps(row) + "\n" for row in results), encoding="utf-8")

            summary = run_appraisal_evaluation(prepared, output, validated, transport=transport,
                                               core_bridge=core_bridge,
                                               core_preflight=lambda prepared_path, project: preflights.append(prepared_path))
            self.assertEqual(summary["modelCalls"], 32)
            self.assertEqual(len(calls), 32)
            self.assertEqual(calls[0][0], "http://127.0.0.1:8080/completion")
            raw = [json.loads(line) for line in output.read_text(encoding="utf-8").splitlines()]
            self.assertEqual(len(raw), 32)
            self.assertEqual(raw[0]["transportError"], "timeout")
            self.assertEqual(raw[1]["responseText"], "{\"raw\":true}")
            self.assertTrue(raw[1]["rawRequestBase64"])
            self.assertEqual(summary["coreRejectedRows"], 32)
            self.assertEqual(preflights, [prepared.resolve()])

    def test_rejects_scenario_label_leak_before_any_http_call(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            prepared = root / "prepared.jsonl"
            output = root / "responses.jsonl"
            validated = root / "validated.jsonl"
            rows = fixture_rows()
            body = json.loads(rows[0]["completionRequestBody"])
            scenario_id = rows[0]["caseId"].rsplit("/", 1)[1]
            body["prompt"] += " opaque? " + scenario_id
            rows[0]["requestPrompt"] = body["prompt"]
            rows[0]["completionRequestBody"] = json.dumps(body, separators=(",", ":"))
            import hashlib
            rows[0]["requestPromptSha256"] = hashlib.sha256(body["prompt"].encode()).hexdigest()
            rows[0]["completionRequestSha256"] = hashlib.sha256(rows[0]["completionRequestBody"].encode()).hexdigest()
            prepared.write_text("".join(json.dumps(row) + "\n" for row in rows), encoding="utf-8")
            calls = []
            with self.assertRaises(ValueError) as _raised_167:
                run_appraisal_evaluation(prepared, output, validated,
                                         transport=lambda *args: calls.append(args),
                                         core_preflight=lambda *args: None)
            self.assertTrue(('scenario identifier leaked' in str(_raised_167.exception)), str(_raised_167.exception))
            self.assertEqual(calls, [])

    def test_refuses_non_loopback_model_endpoints(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            prepared = root / "prepared.jsonl"
            prepared.write_text("".join(json.dumps(row) + "\n" for row in fixture_rows()), encoding="utf-8")
            calls = []
            with self.assertRaises(ValueError) as _raised_179:
                run_appraisal_evaluation(prepared, root / "responses.jsonl", root / "validated.jsonl",
                                         endpoint="https://example.invalid/completion",
                                         transport=lambda *args: calls.append(args),
                                         core_preflight=lambda *args: None)
            self.assertTrue(('loopback' in str(_raised_179.exception)), str(_raised_179.exception))
            self.assertEqual(calls, [])

    def test_core_preflight_failure_stops_before_any_model_call(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            prepared = root / "prepared.jsonl"
            prepared.write_text("".join(json.dumps(row) + "\n" for row in fixture_rows()), encoding="utf-8")
            calls = []

            def preflight(*_args):
                raise RuntimeError("prepared bytes differ from current Core")

            with self.assertRaises(RuntimeError) as _raised_196:
                run_appraisal_evaluation(prepared, root / "responses.jsonl", root / "validated.jsonl",
                                         transport=lambda *args: calls.append(args),
                                         core_preflight=preflight)
            self.assertTrue(('current Core' in str(_raised_196.exception)), str(_raised_196.exception))
            self.assertEqual(calls, [])


if __name__ == "__main__":
    unittest.main()
