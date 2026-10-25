import json
import unittest

from qwen_choice_adapter import build_qwen_choice_request, convert_qwen_choice_response


def d3_request():
    state = '{"requestId":"abc","context":"same scene"}'
    value = {
        "state": state,
        "questions": {"decision": {
            "type": "choice",
            "instructions": "Choose one eligible option.",
            "criteria": {"wait": "Wait here", "leave": "Leave now"},
        }},
    }
    return json.dumps(value, separators=(",", ":")).encode("utf-8")


def qwen_response(content, model="qwen-local"):
    return json.dumps({
        "model": model,
        "choices": [{"message": {"role": "assistant", "content": content}}],
    }, separators=(",", ":")).encode("utf-8")


class QwenChoiceAdapterTests(unittest.TestCase):
    def test_request_preserves_state_and_offered_criteria_with_schema_enum(self):
        body = json.loads(build_qwen_choice_request(d3_request(), "Qwen/Test"))

        self.assertEqual(body["model"], "Qwen/Test")
        self.assertEqual(body["temperature"], 0)
        self.assertFalse(body["stream"])
        self.assertEqual(body["response_format"]["type"], "json_schema")
        schema = body["response_format"]["json_schema"]["schema"]
        self.assertEqual(schema["properties"]["choice"]["enum"], ["wait", "leave"])
        self.assertEqual(schema["required"], ["choice"])
        self.assertEqual(schema["additionalProperties"], False)
        user = json.loads(body["messages"][1]["content"])
        original = json.loads(d3_request())
        self.assertEqual(user["state"], original["state"])
        self.assertEqual(user["criteria"], original["questions"]["decision"]["criteria"])
        self.assertEqual(user["instructions"], original["questions"]["decision"]["instructions"])
        self.assertNotIn("oracle", body["messages"][1]["content"].lower())

    def test_conversion_uses_explicit_one_hot_wire_encoding_not_model_confidence(self):
        converted = json.loads(convert_qwen_choice_response(
            d3_request(), qwen_response('{"choice":"leave"}')))

        self.assertEqual(converted, {
            "model": "qwen-local",
            "answers": {"decision": {"type": "choice", "choice": "leave",
                                     "probabilities": {"wait": 0.0, "leave": 1.0}}},
        })

    def test_rejects_malformed_missing_and_unoffered_choices(self):
        for content in ("not json", "{}", '{"choice":"attack"}'):
            with self.subTest(content=content):
                with self.assertRaises(ValueError):
                    convert_qwen_choice_response(d3_request(), qwen_response(content))

    def test_rejects_missing_model_or_multiple_completions(self):
        missing_model = qwen_response('{"choice":"wait"}')
        value = json.loads(missing_model)
        del value["model"]
        with self.assertRaises(ValueError):
            convert_qwen_choice_response(d3_request(), json.dumps(value).encode())

        value = json.loads(missing_model)
        value["choices"].append(value["choices"][0])
        with self.assertRaises(ValueError):
            convert_qwen_choice_response(d3_request(), json.dumps(value).encode())

    def test_requested_model_identity_cannot_be_silently_replaced(self):
        with self.assertRaises(ValueError):
            convert_qwen_choice_response(d3_request(), qwen_response('{"choice":"wait"}', "other-model"),
                                         expected_model="qwen-local")
        result = convert_qwen_choice_response(d3_request(),
                    qwen_response('{"choice":"wait"}', r'C:\models\qwen-local.gguf'),
                    expected_model="qwen-local.gguf")
        self.assertEqual(json.loads(result)["model"], r'C:\models\qwen-local.gguf')


if __name__ == "__main__":
    unittest.main()
