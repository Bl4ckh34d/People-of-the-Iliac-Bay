"""CPU-only integration tests. The fake endpoint never loads an inference model."""
import json
import subprocess
import tempfile
import threading
import unittest
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DLL = ROOT / 'tools/GoalAttemptEvaluation/bin/Release/net8.0/GoalAttemptEvaluation.dll'


class TransportTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        subprocess.run(['dotnet', 'build', str(ROOT / 'tools/GoalAttemptEvaluation'), '-c', 'Release', '--nologo'], check=True, capture_output=True)

    def run_case(self, *, overflow=False, truncated=False, wrong_model=False, mode='native'):
        calls = []
        class Handler(BaseHTTPRequestHandler):
            def log_message(self, *args):
                pass

            def do_GET(self):
                obj = {'data': [{'id': 'fake-model'}]} if self.path == '/v1/models' else {'default_generation_settings': {'n_ctx': 4096}}
                self.respond(obj)

            def do_POST(self):
                body = json.loads(self.rfile.read(int(self.headers['Content-Length'])))
                calls.append((self.path, body))
                if self.path == '/apply-template':
                    self.respond({'prompt': 'NATIVE_TEMPLATE_ONLY'})
                elif self.path == '/tokenize':
                    self.respond({'tokens': [1] * (4096 if overflow else 8)})
                elif self.path == '/v1/chat/completions':
                    self.respond({'model':'wrong-model' if wrong_model else 'fake-model','choices':[{'finish_reason':'length' if truncated else 'stop','message':{'reasoning_content':'NOT_THE_FINAL_JSON','content':'{"intent":"none","steps":[]}'}}]})
                else:
                    self.respond({'model': 'wrong-model' if wrong_model else 'fake-model', 'content': '{"intent":"none","steps":[]}', 'truncated': truncated, 'stop_type': 'eos'})

            def respond(self, obj):
                raw = json.dumps(obj).encode()
                self.send_response(200)
                self.send_header('Content-Type', 'application/json')
                self.send_header('Content-Length', str(len(raw)))
                self.end_headers()
                self.wfile.write(raw)

        server = ThreadingHTTPServer(('127.0.0.1', 0), Handler)
        worker = threading.Thread(target=server.serve_forever, daemon=True)
        worker.start()
        try:
            with tempfile.TemporaryDirectory() as temp:
                fixture = Path(temp) / 'fixture.json'
                fixture.write_text(json.dumps({'factualContext': 'No native execution.', 'cases': [{'id': 'control', 'text': 'How are you?', 'history': '', 'expected': {'intent': 'None', 'kinds': [], 'steps': [], 'conditions': [], 'exclusions': [], 'goldSentinel': 'DO_NOT_SEND_GOLD'}}]}))
                output = Path(temp) / 'result'
                result = subprocess.run(['dotnet', str(DLL), str(fixture), str(output), 'fake-model', mode, str(server.server_port)], capture_output=True, text=True)
                self.assertEqual(result.returncode, 0, result.stderr)
                rows = json.loads((output / 'results.json').read_text())
                manifest = json.loads((output / 'manifest.json').read_text())
                return calls, rows[0], manifest
        finally:
            server.shutdown()
            server.server_close()
            worker.join()

    def test_uses_native_template_and_never_sends_gold(self):
        calls, row, manifest = self.run_case()
        self.assertTrue(row['finalPass'])
        self.assertEqual(manifest['templateMode'], 'native')
        self.assertEqual([p for p, _ in calls], ['/apply-template', '/tokenize', '/completion'])
        self.assertEqual(calls[-1][1]['prompt'], 'NATIVE_TEMPLATE_ONLY')
        self.assertFalse(calls[0][1]['chat_template_kwargs']['enable_thinking'])
        self.assertNotIn('DO_NOT_SEND_GOLD', json.dumps(calls))

    def test_context_overflow_never_calls_completion(self):
        calls, row, _ = self.run_case(overflow=True)
        self.assertFalse(row['finalPass'])
        self.assertIn('context budget', row['failure'])
        self.assertNotIn('/completion', [p for p, _ in calls])

    def test_thinking_transport_parses_only_final_content(self):
        calls,row,_=self.run_case(mode='thinking')
        self.assertTrue(row['finalPass'])
        self.assertEqual(calls[-1][0],'/v1/chat/completions')
        self.assertNotIn('grammar',calls[-1][1])
        self.assertEqual(calls[-1][1]['response_format']['type'],'json_schema')
        self.assertIn('intent',calls[-1][1]['response_format']['json_schema']['schema']['properties'])
        self.assertTrue(calls[-1][1]['chat_template_kwargs']['enable_thinking'])
        self.assertNotIn('DO_NOT_SEND_GOLD',json.dumps(calls))

    def test_thinking_transport_rejects_exhausted_budget(self):
        _,row,_=self.run_case(mode='thinking',truncated=True)
        self.assertFalse(row['finalPass'])
        self.assertIn('incomplete',row['failure'])

    def test_truncated_response_is_not_accepted(self):
        _, row, _ = self.run_case(truncated=True)
        self.assertFalse(row['finalPass'])
        self.assertIn('truncated', row['failure'])

    def test_response_identity_is_checked(self):
        _, row, _ = self.run_case(wrong_model=True)
        self.assertFalse(row['finalPass'])
        self.assertIn('model changed', row['failure'])


if __name__ == '__main__':
    unittest.main()
