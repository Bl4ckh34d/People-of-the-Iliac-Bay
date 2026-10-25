import copy
import hashlib
import json
import base64
import tempfile
import unittest
from unittest.mock import patch
from pathlib import Path

from test_appraisal_preparation_contract import fixture_rows
from run_appraisal_chatml_experiment import (
    build_chatml_pairs,
    write_experiment_artifacts,
    execute_chatml_experiment,
    ChatMlExperimentError,
    _record_chatml_core,
)


MODEL_PATH = r"C:\Users\Daniel\Desktop\Code_Experiments\DFU\local-llm\models\abliterated\Qwen2.5-7B-Instruct-abliterated-v2.Q4_K_M.gguf"
MODEL_SHA256 = "f8aa43bd9b997f08ad1d2850b28061d69097d39c05d055ccd7004fb80ad61fc8"


def baseline_rows(prepared):
    result = []
    for row in prepared:
        body = row["completionRequestBody"]
        result.append({
            "recordType": "appraisal-model-response-v1",
            "caseId": row["caseId"],
            "requestPromptSha256": row["requestPromptSha256"],
            "rawRequest": body,
            "rawRequestSha256": row["completionRequestSha256"],
            "rawRequestBase64": base64.b64encode(body.encode()).decode(),
            "httpStatus": 200,
            "transportError": None,
            "modelMetadata": {"model": MODEL_PATH},
        })
    return result


class ChatMlAppraisalPreparationTests(unittest.TestCase):
    def setUp(self):
        self.prepared = fixture_rows()
        self.baseline = baseline_rows(self.prepared)
        self.pin = {"path": MODEL_PATH, "sha256": MODEL_SHA256}

    def test_builds_only_24_cause_valid_pairs_and_changes_only_chatml_wrapper(self):
        variant_rows, manifest_rows = build_chatml_pairs(self.prepared, self.baseline, self.pin)
        self.assertEqual(len(variant_rows), 24)
        self.assertEqual(len(manifest_rows), 24)
        self.assertEqual(
            {row["caseId"].rsplit("/", 1)[1] for row in variant_rows},
            {"native-known-gift", "native-confirmed-help", "native-confirmed-no-show",
             "observed-direct-threat", "observed-quoted-threat", "observed-hypothetical-threat"},
        )
        prepared_by_id = {row["caseId"]: row for row in self.prepared}
        for variant, manifest in zip(variant_rows, manifest_rows):
            original = prepared_by_id[variant["caseId"]]
            body = json.loads(variant["completionRequestBody"])
            original_body = json.loads(original["completionRequestBody"])
            self.assertEqual(body["prompt"],
                             "<|im_start|>user\n" + original["requestPrompt"] +
                             "<|im_end|>\n<|im_start|>assistant\n")
            self.assertEqual({k: v for k, v in body.items() if k != "prompt"},
                             {k: v for k, v in original_body.items() if k != "prompt"})
            self.assertNotIn(original["humanReview"]["label"], body["prompt"])
            self.assertEqual(manifest["originalPromptSha256"], original["requestPromptSha256"])
            self.assertEqual(manifest["originalBodySha256"], original["completionRequestSha256"])
            self.assertEqual(manifest["variantBodySha256"], hashlib.sha256(
                variant["completionRequestBody"].encode()).hexdigest())
            self.assertEqual(manifest["modelSha256"], MODEL_SHA256)

    def test_rejects_baseline_body_or_metadata_that_does_not_match_prepared_v1(self):
        bad = copy.deepcopy(self.baseline)
        bad[0]["rawRequestSha256"] = "0" * 64
        with self.assertRaises(ChatMlExperimentError) as _raised_77:
            build_chatml_pairs(self.prepared, bad, self.pin)
        self.assertTrue(('baseline request hash' in str(_raised_77.exception)), str(_raised_77.exception))

    def test_rejects_baseline_model_that_does_not_match_pinned_model(self):
        bad = copy.deepcopy(self.baseline)
        bad[0]["modelMetadata"]["model"] = "different-model.gguf"
        with self.assertRaises(ChatMlExperimentError) as _raised_83:
            build_chatml_pairs(self.prepared, bad, self.pin)
        self.assertTrue(('pinned model' in str(_raised_83.exception)), str(_raised_83.exception))

    def test_rejects_duplicate_or_missing_baseline_case(self):
        bad = copy.deepcopy(self.baseline[:-1])
        with self.assertRaises(ChatMlExperimentError) as _raised_88:
            build_chatml_pairs(self.prepared, bad, self.pin)
        self.assertTrue(('exactly one response' in str(_raised_88.exception)), str(_raised_88.exception))

    def test_writes_fresh_paired_artifacts_and_refuses_overwrite(self):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "chatml"
            summary = write_experiment_artifacts(self.prepared, self.baseline, self.pin, output)
            self.assertEqual(summary["experimentCaseCount"], 24)
            self.assertFalse(summary["inferencePerformed"])
            requests = [json.loads(line) for line in (output / "chatml-requests.jsonl").read_text().splitlines()]
            manifest = [json.loads(line) for line in (output / "paired-manifest.jsonl").read_text().splitlines()]
            self.assertEqual(len(requests), 24)
            self.assertEqual(len(manifest), 24)
            with self.assertRaises(FileExistsError):
                write_experiment_artifacts(self.prepared, self.baseline, self.pin, output)

    def test_execute_sends_each_variant_once_then_records_separate_core_response_validation(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            prepared_path = root / "prepared.jsonl"
            baseline_path = root / "baseline.jsonl"
            prepared_path.write_text("".join(json.dumps(row) + "\n" for row in self.prepared), encoding="utf-8")
            baseline_path.write_text("".join(json.dumps(row) + "\n" for row in self.baseline), encoding="utf-8")
            output = root / "experiment"
            write_experiment_artifacts(self.prepared, self.baseline, self.pin, output)
            calls = []

            def transport(url, body):
                calls.append((url, body))
                return {"status": 200, "body": json.dumps({"content": "{}", "model": MODEL_PATH}).encode(),
                        "elapsedMs": 2.0, "headers": {}, "error": None}

            def recorder(_prepared, _requests, _manifest, raw_path, output_path):
                rows = [json.loads(line) for line in raw_path.read_text(encoding="utf-8").splitlines()]
                output_path.write_text("".join(json.dumps({"caseId": row["caseId"], "coreResponseValidated": False}) + "\n"
                                                       for row in rows), encoding="utf-8")

            result = execute_chatml_experiment(output, prepared_path, baseline_path, self.pin,
                                               transport=transport, core_preflight=lambda *_: None,
                                               core_recorder=recorder)
            self.assertEqual(result["modelCalls"], 24)
            self.assertEqual(result["coreRejected"], 24)
            self.assertEqual(len(calls), 24)
            raw = [json.loads(line) for line in (output / "raw-results.jsonl").read_text().splitlines()]
            self.assertEqual(len(raw), 24)
            self.assertTrue(all(json.loads(row["rawRequest"])["prompt"].startswith("<|im_start|>user\n") for row in raw))

    def test_execute_stops_after_first_wrong_model_identity_and_preserves_that_attempt(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            prepared_path = root / "prepared.jsonl"
            baseline_path = root / "baseline.jsonl"
            prepared_path.write_text("".join(json.dumps(row) + "\n" for row in self.prepared), encoding="utf-8")
            baseline_path.write_text("".join(json.dumps(row) + "\n" for row in self.baseline), encoding="utf-8")
            output = root / "experiment"
            write_experiment_artifacts(self.prepared, self.baseline, self.pin, output)
            calls = []

            def transport(url, body):
                calls.append((url, body))
                return {"status": 200, "body": json.dumps({"content": "{}", "model": "wrong.gguf"}).encode(),
                        "elapsedMs": 1.0, "headers": {}, "error": None}

            with self.assertRaises(ChatMlExperimentError) as _raised_151:
                execute_chatml_experiment(output, prepared_path, baseline_path, self.pin, transport=transport,
                                          core_preflight=lambda *_: None,
                                          core_recorder=lambda *_: self.fail("wrong model must stop before validation"))
            self.assertTrue(('different from pinned baseline' in str(_raised_151.exception)), str(_raised_151.exception))
            self.assertEqual(len(calls), 1)
            self.assertEqual(len((output / "raw-results.jsonl").read_text().splitlines()), 1)

    def test_core_recorder_explicitly_uses_release_no_build_binary(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            project = root / "NpcFactorEvaluation.csproj"
            expected = [
                "dotnet", "run", "-c", "Release", "--no-build", "--project", str(project), "--",
                "record-appraisal-chatml-experiment", "--prepared", str(root / "prepared.jsonl"),
                "--variant-requests", str(root / "variant.jsonl"), "--manifest", str(root / "manifest.jsonl"),
                "--responses", str(root / "responses.jsonl"), "--output", str(root / "validated.jsonl"),
                "--model-path", self.pin["path"], "--model-sha256", self.pin["sha256"],
            ]
            with patch("run_appraisal_chatml_experiment.subprocess.run", return_value=type(
                    "Completed", (), {"returncode": 0, "stdout": "accepted", "stderr": ""})()) as run:
                _record_chatml_core(root / "prepared.jsonl", root / "variant.jsonl", root / "manifest.jsonl",
                                    root / "responses.jsonl", root / "validated.jsonl", project, self.pin)
            self.assertEqual(run.call_args.args[0], expected)
            self.assertIn("-c", run.call_args.args[0])
            self.assertEqual(run.call_args.kwargs["cwd"], str(project.parent.parent.parent))

    def test_core_recorder_failure_includes_stdout_and_stderr(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            project = root / "NpcFactorEvaluation.csproj"
            failed = type("Completed", (), {
                "returncode": 1,
                "stdout": "The command is not present in the selected assembly.",
                "stderr": "Build output unavailable.",
            })()
            with patch("run_appraisal_chatml_experiment.subprocess.run", return_value=failed):
                with self.assertRaises(RuntimeError) as _raised_187:
                    _record_chatml_core(root / "prepared.jsonl", root / "variant.jsonl", root / "manifest.jsonl",
                                        root / "responses.jsonl", root / "validated.jsonl", project, self.pin)
                self.assertTrue(('selected assembly' in str(_raised_187.exception) and 'Build output unavailable' in str(_raised_187.exception)), str(_raised_187.exception))


if __name__ == "__main__":
    unittest.main()
