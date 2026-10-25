import json
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from adapters import payload_for, read_choice


class AdapterTests(unittest.TestCase):
    def setUp(self):
        self.state = {'npc': {'id': 'n1'}, 'world': {'time': '10:00'}, 'history': [],
                      'playerText': 'Wait here', 'availableTools': [],
                      'options': [{'id': 'x', 'description': 'Wait'}, {'id': 'y', 'description': 'Clarify'}, {'id': 'z', 'description': 'Refuse'}]}

    def test_model_inputs_are_same_and_order_reverses(self):
        for reverse in [False, True]:
            d3 = payload_for('d3', self.state, 'm', reverse)
            qwen = payload_for('qwen', self.state, 'm', reverse)
            choices = d3['questions']['decision']['criteria']
            qstate = json.loads(qwen['messages'][1]['content'])
            self.assertEqual(list(choices), [o['id'] for o in qstate['options']])
            self.assertEqual(d3['state'], json.dumps({k: v for k, v in self.state.items() if k != 'options'}, ensure_ascii=False, separators=(',', ':')))
            self.assertEqual(list(choices), ['z', 'y', 'x'] if reverse else ['x', 'y', 'z'])
        self.assertEqual(self.state['options'][0]['id'], 'x')

    def test_response_errors_do_not_become_model_decisions(self):
        self.assertEqual(read_choice('d3', {'status_code': 400, 'response_json': {}}), (None, 'http_400'))
        self.assertEqual(read_choice('qwen', {'status_code': 200, 'response_json': {'choices': [{'message': {'content': 'broken'}}]}}), (None, 'malformed_choice'))
        self.assertEqual(read_choice('d3', {'status_code': 200, 'response_json': {'answers': {'decision': {'choice': 'y'}}}}), ('y', None))


if __name__ == '__main__':
    unittest.main()
