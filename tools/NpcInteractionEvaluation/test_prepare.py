import copy
import json
import tempfile
import unittest
from pathlib import Path

import prepare


def sample():
    return {'schemaVersion': 1, 'language': 'en', 'cases': [{
        'id': 'family-count', 'area': 'household', 'roadmap': ['IMM-04'],
        'implementation': 'partial', 'mode': 'model-diagnostic',
        'input': {'speaker': {'id': 'npc:a'}, 'facts': [{'id': 'f1', 'text': 'Three people live in my household.', 'source': 'saved-household', 'certainty': 'known'}],
                  'history': [], 'playerText': 'How many people live with you?'},
        'oracle': {'required': ['Count three includes speaker.'], 'forbidden': ['ORACLE_SECRET'], 'allowed': ['Withhold politely.'],
                   'nativeChecks': ['No household mutation.']}}]}


class PreparationTests(unittest.TestCase):
    def test_oracle_and_internal_labels_never_enter_messages(self):
        rows = prepare.build(sample())
        payload = json.dumps(rows[0]['messages'])
        self.assertNotIn('ORACLE_SECRET', payload)
        self.assertNotIn('IMM-04', payload)
        self.assertNotIn('family-count', payload)
        self.assertIn('Three people', payload)

    def test_duplicate_ids_are_rejected(self):
        suite = sample()
        suite['cases'].append(copy.deepcopy(suite['cases'][0]))
        with self.assertRaises(ValueError):
            prepare.build(suite)

    def test_unknown_input_field_cannot_leak_hidden_truth(self):
        suite = sample()
        suite['cases'][0]['input']['hiddenQuestTruth'] = 'The keeper is a spy.'
        with self.assertRaises(ValueError):
            prepare.build(suite)

    def test_nested_speaker_oracle_cannot_leak(self):
        suite = sample()
        suite['cases'][0]['input']['speaker']['oracle'] = 'SECRET_ANSWER'
        with self.assertRaises(ValueError):
            prepare.build(suite)

    def test_invalid_certainty_is_rejected(self):
        suite = sample()
        suite['cases'][0]['input']['facts'][0]['certainty'] = 'guessed-as-certain'
        with self.assertRaises(ValueError):
            prepare.build(suite)

    def test_fresh_output_required_and_oracle_saved_separately(self):
        with tempfile.TemporaryDirectory() as temp:
            source = Path(temp) / 'suite.json'
            source.write_text(json.dumps(sample()))
            target = Path(temp) / 'prepared'
            prepare.write(source, target)
            self.assertNotIn('ORACLE_SECRET', (target / 'requests.jsonl').read_text())
            self.assertIn('ORACLE_SECRET', (target / 'review.jsonl').read_text())
            with self.assertRaises(FileExistsError):
                prepare.write(source, target)


if __name__ == '__main__':
    unittest.main()
