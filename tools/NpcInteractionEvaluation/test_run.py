import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import prepare
import run
from test_prepare import sample


class RunnerTests(unittest.TestCase):
    def test_preflight_is_network_free(self):
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / 'suite.json'
            source.write_text(json.dumps(sample()))
            prepared = Path(directory) / 'prepared'
            prepare.write(source, prepared)
            with patch.object(run, 'get_json', side_effect=AssertionError('network forbidden')):
                result = run.preflight(prepared, 'http://127.0.0.1:8080')
            self.assertEqual(len(result), 1)

    def test_modified_request_rejected_before_network(self):
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / 'suite.json'
            source.write_text(json.dumps(sample()))
            prepared = Path(directory) / 'prepared'
            prepare.write(source, prepared)
            (prepared / 'requests.jsonl').write_text('{}\n')
            with self.assertRaises(ValueError):
                run.preflight(prepared, 'http://127.0.0.1:8080')

    def test_external_endpoint_rejected(self):
        with self.assertRaises(ValueError):
            run.preflight(Path('unused'), 'https://example.com')

    def test_completion_rejects_truncation_and_wrong_identity(self):
        for response in [dict(model='wrong', content='Hello'), dict(model='test', content='Hello', truncated=True),
                         dict(model='test', content='Hello', stop_type='limit'), dict(model='test', content='')]:
            with self.assertRaises(ValueError):
                run.validate_completion(response, 'test')

    def test_reply_is_not_automatically_marked_semantically_correct(self):
        row = run.result_row('case', {'model': 'test', 'content': 'An invented river is nearby.'}, 'test', 10)
        self.assertEqual(row['semanticVerdict'], 'pending-human-review')
        self.assertFalse(row['nativeExecution'])

    def test_reasoning_closing_marker_in_final_answer_is_flagged(self):
        row=run.result_row('case',{'model':'test','content':'Let me think.</think>Hello.'},'test',10)
        self.assertTrue(row['reasoningMarkerPresent'])

    def test_chat_final_content_excludes_reasoning_and_requires_complete_answer(self):
        response={'model':'test','choices':[{'finish_reason':'stop','message':{'content':'Hello.','reasoning_content':'PRIVATE_REASONING'}}]}
        self.assertEqual(run.chat_completion(response,'test')['content'],'Hello.')
        response['choices'][0]['finish_reason']='length'
        with self.assertRaises(ValueError):
            run.chat_completion(response,'test')

    def test_open_reasoning_template_is_rejected_by_raw_mode(self):
        with self.assertRaises(ValueError):
            run.require_answer_template('assistant\n<think>')


if __name__ == '__main__':
    unittest.main()
