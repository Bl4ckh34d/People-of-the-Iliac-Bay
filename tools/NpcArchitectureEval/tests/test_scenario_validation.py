import copy
import json
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from scenario_validation import (
    ScenarioReplay,
    aggregate_scores,
    apply_input_updates,
    score_choice,
    validate_suite,
)
from run_evaluation import _parse_args, load_cases


class ScenarioValidationTests(unittest.TestCase):
    def option(self, option_id, description=None):
        return {'id': option_id, 'description': description or f'Description for {option_id}'}

    def base_case(self):
        return {
            'id': 'root.multi',
            'capabilityIds': ['action.wait', 'knowledge.memory'],
            'provenance': 'must not reach model',
            'input': {
                'npc': {'id': 'npc-1'},
                'world': {'time': 1, 'nested': {'keep': True}},
                'history': [{'speaker': 'system', 'text': 'Initial fact.'}],
                'playerText': 'Root text ignored for a multi-turn scenario.',
                'availableTools': [],
                'options': [self.option('a'), self.option('b'), self.option('c')],
            },
            'expected': {
                'allowedChoices': ['b'],
                'forbiddenChoices': ['c'],
                'severity': 'major',
                'reason': 'root gold must not be replayed for multi-turn roots',
            },
            'turns': [
                {
                    'playerText': 'First player turn.',
                    'inputUpdates': {'world.nested.added': 2},
                    'expected': {'allowedChoices': ['b'], 'forbiddenChoices': ['c'], 'severity': 'major'},
                },
                {
                    'playerText': 'Second player turn.',
                    'inputUpdates': {
                        'world': {'nested': {'third': 3}},
                        'historyAppend': [{'speaker': 'system', 'text': 'Observed event.'}],
                    },
                    'expected': {'allowedChoices': ['a'], 'forbiddenChoices': ['c'], 'severity': 'critical'},
                },
            ],
        }

    def test_model_input_whitelists_fields_and_excludes_gold(self):
        replay = ScenarioReplay(self.base_case())
        prepared = replay.prepare_next()

        self.assertEqual(set(prepared.model_input), {'npc', 'world', 'history', 'playerText', 'availableTools', 'options'})
        self.assertNotIn('provenance', json.dumps(prepared.model_input))
        self.assertNotIn('expected', json.dumps(prepared.model_input))
        self.assertNotIn('reason', json.dumps(prepared.model_input))
        self.assertEqual(prepared.model_input['playerText'], 'First player turn.')

    def test_dot_path_and_nested_updates_merge_without_mutating_source(self):
        source = {'world': {'time': 1, 'nested': {'keep': True, 'old': 0}}}
        original = copy.deepcopy(source)

        updated = apply_input_updates(source, {
            'world.nested.added': 2,
            'world': {'nested': {'third': 3}, 'new': 'yes'},
        })

        self.assertEqual(updated['world'], {
            'time': 1,
            'nested': {'keep': True, 'old': 0, 'added': 2, 'third': 3},
            'new': 'yes',
        })
        self.assertEqual(source, original)

    def test_multiturn_history_appends_actual_choice_and_player_turns_only(self):
        replay = ScenarioReplay(self.base_case())
        first = replay.prepare_next()
        replay.record_choice('a')  # Deliberately differs from gold allowed choice b.
        second = replay.prepare_next()

        history = second.model_input['history']
        self.assertEqual(history[1], {'speaker': 'player', 'text': 'First player turn.'})
        self.assertEqual(history[2], {
            'speaker': 'decision_event',
            'text': 'Simulated proposed decision; no native effect confirmed: a — Description for a',
        })
        self.assertEqual(history[3], {'speaker': 'system', 'text': 'Observed event.'})
        self.assertNotIn('b — Description for b', json.dumps(history))
        self.assertNotIn('speaker": "npc', json.dumps(history))
        self.assertEqual(second.model_input['playerText'], 'Second player turn.')
        self.assertEqual(second.turn_index, 1)
        with self.assertRaises(RuntimeError):
            replay.prepare_next()
        replay.record_choice('a')
        self.assertIsNone(replay.prepare_next())

    def test_multiturn_uses_turns_not_root_expected(self):
        replay = ScenarioReplay(self.base_case())
        first = replay.prepare_next()
        self.assertEqual(first.expected['allowedChoices'], ['b'])
        replay.record_choice('b')
        second = replay.prepare_next()
        self.assertEqual(second.expected['allowedChoices'], ['a'])
        self.assertEqual(second.expected['severity'], 'critical')

    def test_validation_rejects_option_count_duplicates_unknown_refs_and_bad_severity(self):
        invalid_cases = []
        case = self.base_case()
        case['input']['options'] = [self.option('a'), self.option('a'), self.option('c')]
        invalid_cases.append(case)

        case = self.base_case()
        case['expected']['allowedChoices'] = ['missing']
        invalid_cases.append(case)

        case = self.base_case()
        case['turns'][1]['expected']['forbiddenChoices'] = ['not-offered']
        invalid_cases.append(case)

        case = self.base_case()
        case['turns'][0]['expected']['severity'] = 'urgent'
        invalid_cases.append(case)

        for case in invalid_cases:
            with self.subTest(case=case):
                with self.assertRaises(ValueError):
                    validate_suite({'schemaVersion': 1, 'cases': [case]})

        case = self.base_case()
        case['input']['options'] = [self.option('a'), self.option('b')]
        with self.assertRaises(ValueError):
            validate_suite({'schemaVersion': 1, 'cases': [case]})

    def test_score_classifies_allowed_forbidden_and_invalid_with_metrics(self):
        case = self.base_case()
        replay = ScenarioReplay(case)
        prepared = replay.prepare_next()
        allowed = score_choice(prepared, 'b')
        forbidden = score_choice(prepared, 'c')
        unexpected = score_choice(prepared, 'a')
        unoffered = score_choice(prepared, 'invented')

        self.assertEqual(allowed['classification'], 'allowed')
        self.assertEqual(forbidden['classification'], 'forbidden')
        self.assertEqual(unexpected['classification'], 'unexpected')
        self.assertIsNone(unexpected['invalid_reason'])
        self.assertEqual(unexpected['point'], 0)
        self.assertEqual(unoffered['classification'], 'invalid')
        self.assertEqual(unoffered['invalid_reason'], 'not_offered')
        metrics = aggregate_scores([allowed, forbidden, unexpected, unoffered])
        self.assertEqual(metrics['counts'], {'allowed': 1, 'forbidden': 1, 'unexpected': 1, 'invalid': 1})
        self.assertEqual(metrics['per_severity']['major']['count'], 4)
        self.assertIn('action', metrics['per_category'])
        self.assertIn('knowledge', metrics['per_category'])
        self.assertNotIn('passed', metrics)

    def test_real_evaluation_suites_validate(self):
        root = Path(__file__).resolve().parents[1] / 'fixtures'
        for name in ('social-world.json', 'action-combat.json', 'coverage-gaps.json'):
            suite = json.loads((root / name).read_text(encoding='utf-8-sig'))
            validate_suite(suite)

    def test_suite_selection_keeps_default_comparison_frozen_and_allows_extension_only(self):
        default_paths, default_cases = load_cases(None)
        self.assertEqual([path.name for path in default_paths], ['social-world.json', 'action-combat.json'])
        self.assertEqual(len(default_cases), 72)

        extension_path, extension_cases = load_cases(['tools/NpcArchitectureEval/fixtures/coverage-gaps.json'])
        self.assertEqual(len(extension_path), 1)
        self.assertEqual(extension_path[0].name, 'coverage-gaps.json')
        self.assertEqual(len(extension_cases), 22)

    def test_cli_accepts_repeatable_explicit_suites(self):
        args = _parse_args([
            '--run-dir', 'work/tmp',
            '--models', 'd3',
            '--d3-url', 'http://127.0.0.1:8093',
            '--suite', 'tools/NpcArchitectureEval/fixtures/coverage-gaps.json',
            '--suite', 'tools/NpcArchitectureEval/fixtures/social-world.json',
        ])
        self.assertEqual(args.suite, [
            'tools/NpcArchitectureEval/fixtures/coverage-gaps.json',
            'tools/NpcArchitectureEval/fixtures/social-world.json',
        ])
        self.assertEqual(args.models, ['d3'])


if __name__ == '__main__':
    unittest.main()
