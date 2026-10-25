"""CPU-only integration tests. The fake endpoint never loads an inference model."""
import json
import subprocess
import tempfile
import threading
import unittest
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DLL = ROOT / 'tools/GoalProtocolEvaluation/bin/Release/net8.0/GoalProtocolEvaluation.dll'


class TransportTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        subprocess.run(['dotnet', 'build', str(ROOT / 'tools/GoalProtocolEvaluation'), '-c', 'Release', '--nologo'], check=True, capture_output=True)

    def run_case(self, *, overflow=False, truncated=False, wrong_model=False, protocol='native', positive=False, wrong_target=False):
        calls = []
        answers = [json.dumps({'intent':'request','steps':[{'kind':'Purchase'},{'kind':'Wait'}]}),
            json.dumps({'intent':'request','steps':[
                {'kind':'Purchase','source':'Buy 2 apples','target':'','duration':None,'quantity':{'amount':2,'source':'2'},'missing':[]},
                {'kind':'Wait','source':'wait for 12 seconds','target':'','duration':{'amount':12,'unit':'game_seconds','source':'12 seconds'},'quantity':None,'missing':[]}],
                'conditions':[],'exclusions':[],'missing':[]}),
            json.dumps({'targets':['pears' if wrong_target else 'apples']})]
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
                else:
                    content = '{"intent":"none","steps":[]}' if protocol != 'revision' else '{"intent":"none","steps":[],"conditions":[],"exclusions":[],"missing":[]}'
                    if positive: content = answers.pop(0) if len(answers)>1 else answers[0]
                    self.respond({'model': 'wrong-model' if wrong_model else 'fake-model', 'content': content, 'truncated': truncated, 'stop_type': 'eos'})

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
                item={'id':'control','text':'How are you?','history':'','expected':{'intent':'None','kinds':[],'steps':[],'conditions':[],'exclusions':[],'goldSentinel':'DO_NOT_SEND_GOLD'}}
                if positive:
                    item['text']='Buy 2 apples, then wait for 12 seconds.'
                    item['expected'].update(intent='Request',kinds=['Purchase','Wait'],steps=[
                        {'targets':['apples'],'duration':None,'quantity':2},
                        {'targets':[''],'duration':{'amount':12,'unit':'GameSeconds'},'quantity':None}])
                fixture.write_text(json.dumps({'factualContext':'No native execution.','cases':[item]}))
                output = Path(temp) / 'result'
                result = subprocess.run(['dotnet', str(DLL), str(fixture), str(output), 'fake-model', protocol, str(server.server_port)], capture_output=True, text=True)
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

    def test_revision_checks_three_complete_proposals_without_gold(self):
        calls, row, manifest = self.run_case(protocol='revision')
        self.assertEqual(len(row['attempts']), 3)
        self.assertTrue(row['finalPass'])
        self.assertNotIn('DO_NOT_SEND_GOLD', json.dumps(calls))
        self.assertEqual(len([p for p, _ in calls if p == '/completion']), 3)
        self.assertIn('"amount"', calls[0][1]['messages'][1]['content'])

    def test_target_patch_preserves_typed_fields_without_gold(self):
        calls,row,_=self.run_case(protocol='targetpatch',positive=True)
        self.assertTrue(row['finalPass'])
        self.assertEqual(len(row['attempts']),2)
        self.assertNotIn('DO_NOT_SEND_GOLD',json.dumps(calls))
        proposal=json.loads(row['attempts'][-1]['proposal'])
        self.assertEqual(proposal['steps'][0]['quantity'],{'amount':2,'source':'2'})
        self.assertEqual(proposal['steps'][1]['duration'],{'amount':12,'unit':'game_seconds','source':'12 seconds'})

    def test_target_patch_cannot_invent_unquoted_target(self):
        _,row,_=self.run_case(protocol='targetpatch',positive=True,wrong_target=True)
        self.assertFalse(row['finalPass'])
        self.assertFalse(row['ready'])
        self.assertEqual(len(row['attempts']),3)

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
