import json
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

import decision_frame_protocol as frame_protocol
import frame_qwen_adapter as adapter
import run_native_route_replay as route_replay


class FrameQwenAdapterTests(unittest.TestCase):
    def setUp(self):
        self.payload = frame_protocol.selection_payload({
            "playerText": "Buy three Steel Tantos, then give one to me if stock allows.",
            "attributedHistory": [],
            "expected": {"secret": "GOLD_SENTINEL"},
        })
        self.model = "qwen-exact"

    def response(self, selected=None, *, model=None, finish_reason="stop", content=None):
        selected = selected or {"frame": "Purchase", "constraints": "yes"}
        content = content if content is not None else json.dumps(selected)
        return json.dumps({"model": model or self.model, "choices": [{
            "finish_reason": finish_reason,
            "message": {"role": "assistant", "content": content},
        }]})

    def test_build_preserves_exact_state_questions_and_offered_ids(self):
        request = adapter.build_payload(self.payload, self.model)
        self.assertEqual(request["model"], self.model)
        self.assertFalse(request["stream"])
        self.assertEqual(request["temperature"], 0)
        content = json.loads(request["messages"][1]["content"])
        self.assertEqual(content, {
            "state": self.payload["state"],
            "questions": {key: {"instructions": value["instructions"],
                                "criteria": value["criteria"]}
                          for key, value in self.payload["questions"].items()},
        })
        self.assertNotIn("GOLD_SENTINEL", json.dumps(request))
        schema = request["response_format"]["json_schema"]["schema"]
        self.assertEqual(set(schema["properties"]), {"frame", "constraints"})
        self.assertEqual(schema["required"], ["frame", "constraints"])
        self.assertFalse(schema["additionalProperties"])
        for question_id, question in self.payload["questions"].items():
            self.assertEqual(schema["properties"][question_id], {
                "type": "string", "enum": list(question["criteria"]),
            })

    def test_build_supports_later_single_question_payload(self):
        later = frame_protocol.selection_payload({"playerText": "Wait."}, ["Wait"], True)
        request = adapter.build_payload(later, self.model)
        schema = request["response_format"]["json_schema"]["schema"]
        self.assertEqual(schema["required"], ["frame"])
        self.assertEqual(set(schema["properties"]), {"frame"})

    def test_convert_emits_validator_accepted_canonical_envelope(self):
        canonical = adapter.convert_response(self.payload, self.response(), self.model)
        self.assertEqual(canonical["model"], self.model)
        self.assertEqual(set(canonical), {"model", "answers"})
        self.assertEqual(set(canonical["answers"]), {"frame", "constraints"})
        for question_id, answer in canonical["answers"].items():
            selected = "Purchase" if question_id == "frame" else "yes"
            options = self.payload["questions"][question_id]["criteria"]
            self.assertEqual(answer, {
                "type": "choice", "choice": selected,
                "probabilities": {key: 1.0 if key == selected else 0.0 for key in options},
            })
        transition = frame_protocol.parse_selection_response(canonical["answers"])
        self.assertEqual(transition["selectedKinds"], ["Purchase"])
        self.assertTrue(transition["hasConstraints"])

    def test_convert_supports_later_response_with_exact_question_keys(self):
        later = frame_protocol.selection_payload({"playerText": "Wait."}, ["Wait"], True)
        canonical = adapter.convert_response(later, self.response({"frame": "END"}), self.model)
        self.assertEqual(set(canonical["answers"]), {"frame"})
        self.assertEqual(frame_protocol.parse_selection_response(
            canonical["answers"], ["Wait"], True)["outcome"], "request")

    def test_build_rejects_wrong_input_types_and_malformed_questions(self):
        for bad_payload, bad_model in (
            (None, self.model),
            ({"state": 1, "questions": self.payload["questions"]}, self.model),
            ({"state": "{}", "questions": []}, self.model),
            ({"state": "{}", "questions": {"x": {"type": "text", "instructions": "x", "criteria": {"a": "A"}}}}, self.model),
            ({"state": "{}", "questions": {"x": {"type": "choice", "instructions": "x", "criteria": {"a": 1}}}}, self.model),
            (self.payload, ""),
        ):
            with self.subTest(payload=bad_payload):
                with self.assertRaises(ValueError):
                    adapter.build_payload(bad_payload, bad_model)

    def test_response_rejects_model_completion_and_key_mismatches(self):
        failures = [
            self.response(model="different-model"),
            self.response(finish_reason="length"),
            self.response({"frame": "Purchase"}),
            self.response({"frame": "Purchase", "constraints": "yes", "extra": "x"}),
            self.response({"frame": "not-offered", "constraints": "yes"}),
            self.response(content="[]"),
            json.dumps({"model": self.model, "choices": [{"finish_reason": "stop", "message": {"content": 1}}]}),
            json.dumps({"model": self.model, "choices": [{"finish_reason": "stop", "message": {"content": "{}"}},
                                                             {"finish_reason": "stop", "message": {"content": "{}"}}]}),
        ]
        for raw in failures:
            with self.subTest(raw=raw):
                with self.assertRaises(ValueError):
                    adapter.convert_response(self.payload, raw, self.model)

    def test_duplicate_json_keys_rejected_at_outer_and_completion_levels(self):
        valid = self.response()
        duplicate_outer = valid.replace('"model": "qwen-exact"',
                                         '"model": "qwen-exact", "model": "qwen-exact"')
        duplicate_completion = self.response(content='{"frame":"Purchase","frame":"Wait","constraints":"yes"}')
        for raw in (duplicate_outer, duplicate_completion):
            with self.subTest(raw=raw):
                with self.assertRaises(ValueError):
                    adapter.convert_response(self.payload, raw, self.model)

    def test_converter_uses_route_replay_strict_json_helper(self):
        self.assertIs(adapter._strict_json_loads, route_replay._strict_json_loads)


if __name__ == "__main__":
    unittest.main()
