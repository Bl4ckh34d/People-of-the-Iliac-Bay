import hashlib
import json
import tempfile
import unittest
from pathlib import Path
from urllib.parse import urlsplit

from preflight_appraisal_tokens import preflight_bytes, write_report


MODEL = r"C:\models\qwen.gguf"
ENDPOINT = "http://127.0.0.1:8080/completion"


def sha256(data):
    if isinstance(data, str):
        data = data.encode("utf-8")
    return hashlib.sha256(data).hexdigest()


def prepared_row(prompt="exact Core prompt", n_predict=1024, tokens=None):
    body = json.dumps({"prompt": prompt, "n_predict": n_predict}, separators=(",", ":"))
    return {
        "recordType": "prepared-appraisal-preflight",
        "caseId": "case-1",
        "bodyPrompt": prompt,
        "bodyPromptSha256": sha256(prompt),
        "completionRequestBody": body,
        "completionRequestSha256": sha256(body),
        "n_predict": n_predict,
    }


def prepared_bytes(*rows):
    return ("\n".join(json.dumps(row, separators=(",", ":")) for row in rows) + "\n").encode("utf-8")


class FakeResponse:
    def __init__(self, status, body):
        self.status = status
        self.body = body if isinstance(body, bytes) else body.encode("utf-8")

    def __enter__(self):
        return self

    def __exit__(self, *_args):
        return False

    def read(self):
        return self.body


class FakeServer:
    def __init__(self, token_count=12, model=MODEL, n_ctx=4096, props_extra=None,
                 tokenize_body=None, tokenize_status=200):
        self.token_count = token_count
        self.model = model
        self.n_ctx = n_ctx
        self.props_extra = props_extra or {}
        self.tokenize_body = tokenize_body
        self.tokenize_status = tokenize_status
        self.calls = []

    def __call__(self, request, timeout=0):
        url = request.full_url
        body = request.data
        self.calls.append((url, body, timeout, request.get_method()))
        path = urlsplit(url).path
        if path == "/health":
            assert request.get_method() == "GET"
            return FakeResponse(200, '{"status":"ok"}')
        if path == "/props":
            assert request.get_method() == "GET"
            props = {"model_path": self.model, "n_ctx": self.n_ctx,
                     "default_generation_settings": {}}
            props.update(self.props_extra)
            return FakeResponse(200, json.dumps(props))
        if path == "/tokenize":
            assert request.get_method() == "POST"
            if self.tokenize_body is not None:
                return FakeResponse(self.tokenize_status, self.tokenize_body)
            return FakeResponse(self.tokenize_status,
                                json.dumps({"tokens": list(range(self.token_count))}))
        raise AssertionError("Unexpected endpoint: " + url)


class AppraisalTokenPreflightTests(unittest.TestCase):
    def test_exact_3072_token_boundary_passes_and_3073_fails(self):
        payload = prepared_bytes(prepared_row("fits"), prepared_row("too long", tokens=3073))
        server = FakeServer(token_count=0)
        counts = iter((3072, 3073))

        def opener(request, timeout=0):
            if urlsplit(request.full_url).path == "/tokenize":
                body = json.loads(request.data.decode("utf-8"))
                self.assertEqual(body, {"content": "fits" if "fits" in body["content"] else "too long",
                                        "add_special": True})
                return FakeResponse(200, json.dumps({"tokens": list(range(next(counts)))}))
            return server(request, timeout)

        report = preflight_bytes(payload, MODEL, ENDPOINT, opener=opener)
        cases = [row for row in report if row["recordType"] == "appraisal-token-preflight-case"]
        self.assertEqual([row["tokenCount"] for row in cases], [3072, 3073])
        self.assertEqual([row["status"] for row in cases], ["fit", "over_context_budget"])
        self.assertEqual(cases[0]["serverContextTokens"], 4096)
        self.assertEqual(cases[0]["requiredOutputTokens"], 1024)

    def test_wrong_model_fails_before_any_tokenize_call(self):
        server = FakeServer(model=r"C:\models\other.gguf")
        report = preflight_bytes(prepared_bytes(prepared_row()), MODEL, ENDPOINT, opener=server)
        self.assertFalse(report[-1]["allFit"])
        case = next(row for row in report if row["recordType"] == "appraisal-token-preflight-case")
        self.assertEqual(case["status"], "server_preflight_failed")
        self.assertIn("model_path", case["errors"][0])
        self.assertEqual(sum(urlsplit(call[0]).path == "/tokenize" for call in server.calls), 0)

    def test_exact_production_tokenizer_body_and_hashes_are_recorded(self):
        prompt = "exact\r\nCore prompt with unicode — and controls \b\f unchanged"
        server = FakeServer(token_count=41)
        report = preflight_bytes(prepared_bytes(prepared_row(prompt)), MODEL, ENDPOINT, opener=server)
        tokenize_calls = [call for call in server.calls if urlsplit(call[0]).path == "/tokenize"]
        self.assertEqual(len(tokenize_calls), 1)
        request_body = json.loads(tokenize_calls[0][1].decode("utf-8"))
        self.assertEqual(request_body, {"content": prompt, "add_special": True})
        self.assertIn(b"\\u0008\\u000C", tokenize_calls[0][1])
        result = next(row for row in report if row["recordType"] == "appraisal-token-preflight-case")
        self.assertEqual(result["tokenizeRequestSha256"], sha256(tokenize_calls[0][1]))
        self.assertEqual(result["tokenizeResponseSha256"], sha256(result["tokenizeResponse"]))
        self.assertEqual(result["promptSha256"], sha256(prompt))
        self.assertEqual(result["tokenCount"], 41)

    def test_invalid_rows_are_preserved_and_do_not_suppress_later_rows(self):
        bad = prepared_row()
        bad["bodyPromptSha256"] = "0" * 64
        wrong_type = prepared_row()
        wrong_type["bodyPrompt"] = 123
        payload = prepared_bytes(bad, wrong_type, prepared_row("last"))
        server = FakeServer(token_count=3)
        report = preflight_bytes(payload, MODEL, ENDPOINT, opener=server)
        cases = [row for row in report if row["recordType"] == "appraisal-token-preflight-case"]
        self.assertEqual(len(cases), 3)
        self.assertEqual(cases[0]["status"], "invalid_prepared_row")
        self.assertIn("does not match", cases[0]["errors"][0].lower())
        self.assertEqual(cases[1]["status"], "invalid_prepared_row")
        self.assertIn("bodyPrompt", cases[1]["errors"][0])
        self.assertEqual(cases[2]["status"], "fit")
        self.assertEqual(sum(urlsplit(call[0]).path == "/tokenize" for call in server.calls), 1)

    def test_wrong_n_predict_and_wrong_json_types_fail(self):
        bad_n = prepared_row(n_predict=1023)
        wrong_body_type = prepared_row()
        wrong_body_type["completionRequestBody"] = {"prompt": "exact Core prompt", "n_predict": 1024}
        bad_hash_type = prepared_row()
        bad_hash_type["bodyPromptSha256"] = 123
        server = FakeServer()
        report = preflight_bytes(prepared_bytes(bad_n, wrong_body_type, bad_hash_type), MODEL,
                                 ENDPOINT, opener=server)
        cases = [row for row in report if row["recordType"] == "appraisal-token-preflight-case"]
        self.assertTrue(all(row["status"] == "invalid_prepared_row" for row in cases))
        self.assertEqual(sum(urlsplit(call[0]).path == "/tokenize" for call in server.calls), 0)

    def test_bad_health_props_n_ctx_or_malformed_tokenizer_response_fail_closed(self):
        cases = [
            (FakeServer(props_extra={"n_ctx": "4096"}), "n_ctx"),
            (FakeServer(props_extra={"default_generation_settings": []}), "default_generation_settings"),
            (FakeServer(tokenize_body='{"tokens":"not a list"}'), "tokens"),
            (FakeServer(tokenize_body='not-json'), "JSON"),
        ]
        for server, expected in cases:
            with self.subTest(expected=expected):
                report = preflight_bytes(prepared_bytes(prepared_row()), MODEL, ENDPOINT, opener=server)
                case = next(row for row in report if row["recordType"] == "appraisal-token-preflight-case")
                self.assertIn(expected.lower(), " ".join(case["errors"]).lower())
                if expected in ("tokens", "JSON"):
                    self.assertEqual(case["status"], "tokenizer_error")
                else:
                    self.assertEqual(case["status"], "server_preflight_failed")

    def test_malformed_line_and_http_error_are_recorded_without_retries(self):
        payload = b'{"recordType":"prepared-appraisal-preflight","caseId":"bad","bodyPrompt":\n' + prepared_bytes(prepared_row("good"))
        server = FakeServer(tokenize_status=503, tokenize_body='temporarily unavailable')
        report = preflight_bytes(payload, MODEL, ENDPOINT, opener=server)
        cases = [row for row in report if row["recordType"] == "appraisal-token-preflight-case"]
        self.assertEqual(cases[0]["status"], "invalid_prepared_row")
        self.assertEqual(cases[1]["status"], "tokenizer_error")
        self.assertEqual(sum(urlsplit(call[0]).path == "/tokenize" for call in server.calls), 1)

    def test_report_output_refuses_overwrite(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "report.jsonl"
            path.write_text("keep", encoding="utf-8")
            with self.assertRaises(FileExistsError):
                write_report(path, [{"recordType": "test"}])
            self.assertEqual(path.read_text(encoding="utf-8"), "keep")


if __name__ == "__main__":
    unittest.main()
