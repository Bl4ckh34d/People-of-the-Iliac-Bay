import hashlib
import json
import tempfile
import unittest
from pathlib import Path

from run_serial_evaluation import _json_lines, run_serial_evaluation, validate_prepared_row


def prepared_row(request_id: str, option_id: str = "ask_terms") -> dict:
    context = {
        "npc": {"id": "npc:test-01", "disposition": 12, "personalitySeed": 7,
                "mood": {"value": -4, "acceptedTurnSequence": 2, "stateRevision": 3},
                "relationship": {"revision": 1, "lastGameTime": 10, "trustKnown": False,
                                 "resentment": 3, "interpersonalFear": 2}},
        "world": {"gameTimeSeconds": 10, "scene": "test-market", "npcKnowledge": []},
        "history": [], "playerText": "Should we leave?", "availableTools": [],
        "options": [{"id": "ask_terms", "description": "Ask for terms."},
                    {"id": "decline", "description": "Decline."}],
    }
    core_request_id = "d848e2c2-02f7-4916-9424-7e3b5ae7c2f1"
    d3_state = {
        "protocolVersion": "npc-decision-v2", "purpose": "npc-factor-choice-review",
        "requestId": core_request_id, "actorId": "npc:test-01", "originalTurn": "Should we leave?",
        "context": json.dumps(context, separators=(",", ":")),
        "snapshotRevision": 0, "candidateSetRevision": 0,
    }
    d3_request = {"state": json.dumps(d3_state, separators=(",", ":")),
                  "questions": {"decision": {"type": "choice", "instructions": "Choose one.",
                                                "criteria": {item["id"]: item["description"] for item in context["options"]}}}}
    d3_body = json.dumps(d3_request, separators=(",", ":"), ensure_ascii=False)
    return {
        "recordType": "prepared-multi-turn-request", "contextSchemaVersion": 2,
        "corpusId": "npc-complex-interactions-v2", "corpusSha256": "a" * 64,
        "requestId": request_id, "episodeId": "ep", "armId": "arm", "turnId": "t1",
        "inputSha256": "b" * 64, "modelInput": context, "options": context["options"], "d3Request": d3_request,
        "d3RequestBody": d3_body,
        "d3RequestSha256": hashlib.sha256(d3_body.encode("utf-8")).hexdigest(),
        "coreDecisionBinding": {"requestId": core_request_id, "protocol": "npc-decision-v2",
                                 "snapshotRevision": 0, "candidateSetRevision": 0,
                                 "personId": "npc:test-01"},
    }


class SerialEvaluationTests(unittest.TestCase):
    def test_runs_d3_before_qwen_and_preserves_each_raw_stage(self):
        rows = [prepared_row("ep/arm/t1"), prepared_row("ep/arm/t2")]
        calls = []

        def transport(url, body):
            calls.append((url, body))
            return {"status": 200, "body": b'{"ok":true}', "elapsedMs": 5.0,
                    "headers": {"content-type": "application/json"}}

        def core_bridge(prepared_path, d3_path, plan_path, prepared):
            d3_rows = _json_lines(d3_path)
            self.assertEqual([r["requestId"] for r in prepared], [r["requestId"] for r in d3_rows])
            return [{"requestId": row["requestId"], "validated": True,
                     "selectedOptionId": "ask_terms", "qwenRequestBody": '{"prompt":"p"}'}
                    for row in prepared]

        with tempfile.TemporaryDirectory() as temp:
            prepared_path = Path(temp) / "prepared.jsonl"
            output_path = Path(temp) / "responses.jsonl"
            prepared_path.write_text("".join(json.dumps(row) + "\n" for row in rows), encoding="utf-8")
            run_serial_evaluation(prepared_path, output_path, "http://127.0.0.1:8093/v1/systemone",
                                  "http://127.0.0.1:8080/completion", transport=transport,
                                  core_bridge=core_bridge)
            output_rows = [json.loads(line) for line in output_path.read_text(encoding="utf-8").splitlines()]

        self.assertEqual([call[0] for call in calls], [
            "http://127.0.0.1:8093/v1/systemone", "http://127.0.0.1:8093/v1/systemone",
            "http://127.0.0.1:8080/completion", "http://127.0.0.1:8080/completion",
        ])
        self.assertEqual([row["requestId"] for row in output_rows], ["ep/arm/t1", "ep/arm/t2"])
        self.assertEqual(output_rows[0]["d3"]["rawRequest"], rows[0]["d3RequestBody"])
        self.assertEqual(output_rows[0]["d3"]["rawResponse"], '{"ok":true}')
        self.assertEqual(output_rows[0]["rawRequest"], '{"prompt":"p"}')
        self.assertEqual(output_rows[0]["selectedOptionId"], "ask_terms")

    def test_invalid_d3_core_result_skips_qwen_without_retry(self):
        rows = [prepared_row("ep/arm/t1")]
        calls = []

        def transport(url, body):
            calls.append(url)
            return {"status": 500 if len(calls) == 1 else 200, "body": b"error", "elapsedMs": 3.0, "headers": {}}

        def core_bridge(prepared_path, d3_path, plan_path, prepared):
            return [{"requestId": prepared[0]["requestId"], "validated": False,
                     "failure": "http_status", "selectedOptionId": None, "qwenRequestBody": None}]

        with tempfile.TemporaryDirectory() as temp:
            prepared_path = Path(temp) / "prepared.jsonl"
            output_path = Path(temp) / "responses.jsonl"
            prepared_path.write_text(json.dumps(rows[0]) + "\n", encoding="utf-8")
            run_serial_evaluation(prepared_path, output_path, "http://127.0.0.1:8093/v1/systemone",
                                  "http://127.0.0.1:8080/completion", transport=transport,
                                  core_bridge=core_bridge)
            result = json.loads(output_path.read_text(encoding="utf-8"))

        self.assertEqual(calls, ["http://127.0.0.1:8093/v1/systemone"])
        self.assertIsNone(result["selectedOptionId"])
        self.assertEqual(result["d3"]["httpStatus"], 500)
        self.assertEqual(result["failureStage"], "d3_validation")

    def test_rejects_changed_prepared_wire_hash_before_network(self):
        row = prepared_row("ep/arm/t1")
        row["d3RequestSha256"] = "0" * 64
        calls = []

        def transport(url, body):
            calls.append(url)
            raise AssertionError("network call must not happen")

        with tempfile.TemporaryDirectory() as temp:
            prepared_path = Path(temp) / "prepared.jsonl"
            output_path = Path(temp) / "responses.jsonl"
            prepared_path.write_text(json.dumps(row) + "\n", encoding="utf-8")
            with self.assertRaises(ValueError) as _raised_120:
                run_serial_evaluation(prepared_path, output_path, "http://127.0.0.1:8093/v1/systemone",
                                      "http://127.0.0.1:8080/completion", transport=transport,
                                      core_bridge=lambda *_: [])
            self.assertTrue(('D3 wire hash' in str(_raised_120.exception)), str(_raised_120.exception))
        self.assertEqual(calls, [])

    def test_rejects_oracle_nested_in_model_input(self):
        row = prepared_row("ep/arm/t1")
        row["modelInput"]["world"]["npcKnowledge"] = [{"oracle": "must never be sent"}]
        with self.assertRaises(ValueError) as _raised_129:
            validate_prepared_row(row)
        self.assertTrue(('oracle leaked' in str(_raised_129.exception)), str(_raised_129.exception))

    def test_rejects_legacy_row_without_full_typed_snapshot(self):
        row = prepared_row("ep/arm/t1")
        del row["modelInput"]
        with self.assertRaises(ValueError) as _raised_135:
            validate_prepared_row(row)
        self.assertTrue(('modelInput snapshot' in str(_raised_135.exception)), str(_raised_135.exception))


if __name__ == "__main__":
    unittest.main()
