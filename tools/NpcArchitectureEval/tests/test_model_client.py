import json
import tempfile
import sys
import threading
import unittest
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from model_client import post_json


class ModelClientTests(unittest.TestCase):
    def serve(self, handler_fn):
        class Handler(BaseHTTPRequestHandler):
            def do_POST(self):
                body = self.rfile.read(int(self.headers.get('Content-Length', '0')))
                handled = handler_fn(self, body)
                if handled is None:
                    return
                status, response = handled
                self.send_response(status)
                self.send_header('Content-Type', 'application/json; charset=utf-8')
                self.end_headers()
                self.wfile.write(response)

            def log_message(self, _format, *args):
                pass

        server = HTTPServer(('127.0.0.1', 0), Handler)
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        def stop_server():
            server.shutdown()
            thread.join(2)
            server.server_close()

        self.addCleanup(stop_server)
        return f'http://127.0.0.1:{server.server_port}/probe'

    def test_utf8_request_and_response_preserve_apostrophes(self):
        seen = {}

        def handler(_request, body):
            seen['body'] = body
            return 200, '{"reply":"l’été d’Aiocnuall"}'.encode('utf-8')

        url = self.serve(handler)
        with tempfile.TemporaryDirectory() as temp:
            ledger = Path(temp) / 'ledger.jsonl'
            result = post_json(url, {'text': "l’été d’Aiocnuall"}, ledger, {'case_id': 'utf8'})

        self.assertIn('l’été d’Aiocnuall'.encode('utf-8'), seen['body'])
        self.assertEqual(result['status_code'], 200)
        self.assertEqual(result['response_json']['reply'], 'l’été d’Aiocnuall')

    def test_http_400_body_and_status_are_retained(self):
        url = self.serve(lambda _request, _body: (400, b'{"error":"bad request"}'))
        with tempfile.TemporaryDirectory() as temp:
            ledger = Path(temp) / 'ledger.jsonl'
            result = post_json(url, {'x': 1}, ledger, {})

        self.assertEqual(result['status_code'], 400)
        self.assertEqual(result['response_json'], {'error': 'bad request'})
        self.assertIsNone(result['transport_error'])

    def test_malformed_response_body_is_retained_and_marked(self):
        url = self.serve(lambda _request, _body: (200, b'{broken'))
        with tempfile.TemporaryDirectory() as temp:
            ledger = Path(temp) / 'ledger.jsonl'
            result = post_json(url, {'x': 1}, ledger, {})

        self.assertEqual(result['response_raw'], '{broken')
        self.assertTrue(result['malformed_json'])
        self.assertIsNone(result['response_json'])

    def test_non_loopback_url_is_rejected_before_network_and_ledger(self):
        with tempfile.TemporaryDirectory() as temp:
            ledger = Path(temp) / 'ledger.jsonl'
            with self.assertRaises(ValueError):
                post_json('http://example.com/v1', {'x': 1}, ledger, {})
            self.assertFalse(ledger.exists())

    def test_request_and_ledger_preserve_criteria_option_order(self):
        captured = []

        def handler(_request, body):
            captured.append(body)
            return 200, b'{"ok":true}'

        url = self.serve(handler)
        with tempfile.TemporaryDirectory() as temp:
            ledger = Path(temp) / 'ledger.jsonl'
            first = {'questions': {'action': {'criteria': {'wait': 'wait', 'follow': 'follow'}}}}
            second = {'questions': {'action': {'criteria': {'follow': 'follow', 'wait': 'wait'}}}}
            first_result = post_json(url, first, ledger, {'case_id': 'order', 'arm': 'first'})
            second_result = post_json(url, second, ledger, {'case_id': 'order', 'arm': 'second'})
            entries = [json.loads(line) for line in ledger.read_text(encoding='utf-8').splitlines()]

        first_text = captured[0].decode('utf-8')
        second_text = captured[1].decode('utf-8')
        self.assertIn('"wait":"wait","follow":"follow"', first_text)
        self.assertIn('"follow":"follow","wait":"wait"', second_text)
        self.assertNotEqual(captured[0], captured[1])
        self.assertNotEqual(first_result['request_sha256'], second_result['request_sha256'])
        self.assertEqual(entries[0]['request_sha256'], first_result['request_sha256'])
        self.assertEqual(entries[0]['payload']['questions']['action']['criteria'], {'wait': 'wait', 'follow': 'follow'})
        self.assertEqual(entries[2]['payload']['questions']['action']['criteria'], {'follow': 'follow', 'wait': 'wait'})

    def test_attempt_hash_is_written_before_http_and_result_follows(self):
        with tempfile.TemporaryDirectory() as temp:
            ledger = Path(temp) / 'ledger.jsonl'

            def handler(_request, body):
                entries = [json.loads(line) for line in ledger.read_text(encoding='utf-8').splitlines()]
                self.assertEqual([entry['kind'] for entry in entries], ['attempt'])
                self.assertEqual(entries[0]['request_sha256'], __import__('hashlib').sha256(body).hexdigest())
                return 200, b'{"ok":true}'

            url = self.serve(handler)
            result = post_json(url, {'text': 'hello'}, ledger, {'case_id': 'order'})
            entries = [json.loads(line) for line in ledger.read_text(encoding='utf-8').splitlines()]

        self.assertEqual([entry['kind'] for entry in entries], ['attempt', 'result'])
        self.assertEqual(entries[0]['request_sha256'], result['request_sha256'])
        self.assertEqual(entries[1]['status_code'], 200)

    def test_secret_fields_are_redacted_from_ledger(self):
        url = self.serve(lambda _request, _body: (200, b'{"ok":true}'))
        with tempfile.TemporaryDirectory() as temp:
            ledger = Path(temp) / 'ledger.jsonl'
            post_json(url, {'token': 'do-not-log', 'public': 'ok'}, ledger, {'api_key': 'also-secret'})
            text = ledger.read_text(encoding='utf-8')

        self.assertNotIn('do-not-log', text)
        self.assertNotIn('also-secret', text)
        self.assertIn('[REDACTED]', text)
    def test_redaction_keeps_generation_token_settings_and_hides_credentials(self):
        url = self.serve(lambda _request, _body: (200, b'{"ok":true}'))
        with tempfile.TemporaryDirectory() as temp:
            ledger = Path(temp) / 'ledger.jsonl'
            post_json(
                url,
                {
                    'max_tokens': 64,
                    'input_tokens': 12,
                    'token_count': 18,
                    'tokenizer': 'test-tokenizer',
                    'api_key': 'api-secret',
                },
                ledger,
                {'authorization': 'Bearer private'},
            )
            text = ledger.read_text(encoding='utf-8')

        self.assertIn('max_tokens', text)
        self.assertIn('input_tokens', text)
        self.assertIn('token_count', text)
        self.assertIn('test-tokenizer', text)
        self.assertNotIn('api-secret', text)
        self.assertNotIn('Bearer private', text)

    def test_redirect_is_returned_without_contacting_target(self):
        target_contacted = threading.Event()

        def target_handler(_request, _body):
            target_contacted.set()
            return 200, b'{"unexpected":true}'

        target_url = self.serve(target_handler)

        def redirect_handler(request, _body):
            request.send_response(302)
            request.send_header('Location', target_url)
            request.end_headers()
            request.wfile.write(b'redirect body')
            return None

        source_url = self.serve(redirect_handler)
        with tempfile.TemporaryDirectory() as temp:
            ledger = Path(temp) / 'ledger.jsonl'
            result = post_json(source_url, {'x': 1}, ledger, {})

        self.assertEqual(result['status_code'], 302)
        self.assertEqual(result['response_raw'], 'redirect body')
        self.assertFalse(target_contacted.wait(0.1))



if __name__ == '__main__':
    unittest.main()



