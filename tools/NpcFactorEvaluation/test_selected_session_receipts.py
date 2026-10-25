"""Offline integration tests: captured output must belong to its exact request.

Build tools/NpcFactorEvaluation first. No model or game process is used.
"""
import hashlib
import json
import subprocess
import tempfile
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
DLL = REPO / "tools/NpcFactorEvaluation/bin/Debug/net8.0/NpcFactorEvaluation.dll"
SUITE = REPO / "work/selected-framework-contrast-prep/suite-v1.json"


def sha(data):
    return hashlib.sha256(data).hexdigest()


class SelectedSessionReceiptTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(dir=REPO / "work")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name) / "session"
        # Each test freezes today's code into its own derived suite. Preserve the
        # historical suite and its source pins; never silently update old evidence.
        suite = json.loads(SUITE.read_text())
        for pin in suite["sourcePins"]:
            pin["sha256"] = sha((REPO / pin["path"]).read_bytes())
        self.suite = Path(self.temp.name) / "suite-current.json"
        self.suite.write_text(json.dumps(suite))
        self.process = subprocess.Popen(
            ["dotnet", str(DLL), "selected-session", str(self.suite), str(self.root)],
            cwd=REPO, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
            text=True, encoding="utf-8")
        self.addCleanup(self.close_process)
        self.assertEqual(self.read()["status"], "ready")
        result = self.command(op="decisions")
        self.assertEqual(result["status"], "decisions-ready", result)
        self.rows = [json.loads(line) for line in Path(result["requestPath"]).read_text().splitlines()]
        self.row = self.rows[0]

    def close_process(self):
        if self.process.poll() is None:
            self.command(op="quit")
        self.process.communicate(timeout=10)

    def read(self):
        line = self.process.stdout.readline()
        if not line:
            raise AssertionError("Session exited before response: " + self.process.stderr.read())
        return json.loads(line)

    def command(self, **value):
        self.process.stdin.write(json.dumps(value) + "\n")
        self.process.stdin.flush()
        return self.read()

    def receipt(self, row=None, status=200, model=None):
        row = row or self.row
        run = self.root / ("run-" + row["caseId"])
        case = run / "cases" / sha(row["caseId"].encode())
        case.mkdir(parents=True)
        body = row["requestBody"].encode()
        (case / "request.json").write_bytes(body)
        wire = json.loads(row["requestBody"])
        question = next(iter(wire["questions"]))
        raw = json.dumps({"model": model or row["expectedModel"], "answers": {
            question: {"type": "choice", "choice": "ACCEPT",
                       "probabilities": {"ACCEPT": 0.8, "DECLINE": 0.2}}}}).encode()
        path = case / "response.bin"
        path.write_bytes(raw)
        case_hash = sha(json.dumps(row, sort_keys=True, ensure_ascii=False, separators=(",", ":")).encode())
        start = {"event": "attempt_started", "caseId": row["caseId"], "caseHash": case_hash,
                 "lane": row["lane"], "requestPath": (case / "request.json").relative_to(run).as_posix(),
                 "requestSha256": sha(body)}
        end = {"event": "completed" if status == 200 else "failed", "caseId": row["caseId"],
               "caseHash": case_hash, "lane": row["lane"], "httpStatus": status,
               "responsePath": path.relative_to(run).as_posix(), "responseSha256": sha(raw),
               "transportError": None, "responseMetadata": {"model": model or row["expectedModel"]}}
        (run / "owner.json").write_text(json.dumps({"schema": "selected-capture-run-v1",
            "sourceHashes": {row["caseId"]: row["sourceHashes"]}}))
        self.journal(run, [start, end])
        return path, run, start, end

    def journal(self, run, events):
        (run / "journal.jsonl").write_text("".join(json.dumps(e) + "\n" for e in events))

    def validate(self, path, case_id=None):
        return self.command(op="validateDecision", caseId=case_id or self.row["caseId"], responsePath=str(path))

    def rejected(self, result):
        self.assertEqual(result["status"], "error", result)
        self.assertFalse((self.root / "core-validation" / (self.row["caseId"] + ".json")).exists())

    def test_valid_receipt_accepts_frozen_request_once(self):
        path, _, _, _ = self.receipt()
        result = self.validate(path)
        self.assertTrue(result.get("accepted"), result)
        self.assertEqual(self.validate(path)["status"], "error")

    def test_http_500_with_valid_decision_never_validates(self):
        self.rejected(self.validate(self.receipt(status=500)[0]))

    def test_other_case_response_never_validates(self):
        self.rejected(self.validate(self.receipt(row=self.rows[1])[0]))

    def test_modified_response_never_validates(self):
        path, _, _, _ = self.receipt()
        path.write_bytes(path.read_bytes() + b" ")
        self.rejected(self.validate(path))

    def test_changed_request_hash_never_validates(self):
        path, run, start, end = self.receipt()
        start["requestSha256"] = "0" * 64
        self.journal(run, [start, end])
        self.rejected(self.validate(path))

    def test_wrong_model_never_validates(self):
        self.rejected(self.validate(self.receipt(model="wrong-model")[0]))

    def test_missing_terminal_never_validates(self):
        path, run, start, _ = self.receipt()
        self.journal(run, [start])
        self.rejected(self.validate(path))

    def test_duplicate_terminal_never_validates(self):
        path, run, start, end = self.receipt()
        self.journal(run, [start, end, end])
        self.rejected(self.validate(path))

    def test_wrong_source_owner_never_validates(self):
        path, run, _, _ = self.receipt()
        owner = json.loads((run / "owner.json").read_text())
        owner["sourceHashes"][self.row["caseId"]] = {"wrong.cs": "0" * 64}
        (run / "owner.json").write_text(json.dumps(owner))
        self.rejected(self.validate(path))

    def test_pair_without_transport_receipt_cannot_be_parsed(self):
        result = self.command(op="pairs")
        row = json.loads(Path(result["requestPath"]).read_text().splitlines()[0])
        path = self.root / "unbound-pair.json"
        path.write_text(json.dumps({"content": "{}", "model": row["expectedModel"]}))
        result = self.command(op="validatePair", caseId=row["caseId"], responsePath=str(path))
        self.assertEqual(result["status"], "error", result)
        self.assertFalse((self.root / "core-validation" / (row["caseId"] + ".json")).exists())

    def test_continuity_without_transport_receipt_cannot_advance(self):
        episode = "QW-C01-two-turn-help"
        result = self.command(op="startEpisode", episodeId=episode)
        self.assertEqual(result["status"], "episode-ready", result)
        case_id = result["caseId"]
        path = self.root / "unbound-episode.json"
        path.write_text(json.dumps({"content": "{}"}))
        result = self.command(op="commitEpisode", episodeId=episode, responsePath=str(path))
        self.assertEqual(result["status"], "error", result)
        self.assertFalse((self.root / "core-validation" / (case_id + ".json")).exists())


if __name__ == "__main__":
    unittest.main()
