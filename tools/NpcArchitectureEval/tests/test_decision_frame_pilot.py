import copy
import json
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import run_decision_frame_pilot as pilot


def case():
    return {'id': 'wait', 'category': 'wait', 'playerText': 'Wait here for 20 seconds.',
            'attributedHistory': [], 'snapshot': {'candidates': [
                {'ref': 'wait_here', 'kind': 'Wait', 'label': 'Current position',
                 'target': {'Kind': 'Person', 'PersonId': 'player'}}]},
            'expected': {'conceptual': {'intent': 'request', 'steps': [
                {'kind': 'Wait', 'sourceQuote': 'Wait here for 20 seconds', 'targetText': 'here',
                 'durationAmount': 20, 'durationUnit': 'seconds'}], 'conditions': [], 'missing': []},
                         'grounding': 'plan'}}


def arguments():
    return {'arguments': [dict(case()['expected']['conceptual']['steps'][0], stepIndex=0)],
            'conditions': [], 'missing': [], 'constraints': [], 'frameDisagreement': False,
            'frameDisagreementReason': None, 'needsClarification': False, 'clarificationReason': None}


class ScriptedTransport:
    def __init__(self, selections, argument_value=None, wrong_model=False):
        self.selections = iter(selections)
        self.argument_value = arguments() if argument_value is None else argument_value
        self.calls = []
        self.wrong_model = wrong_model

    def __call__(self, stage, payload):
        self.calls.append((stage, copy.deepcopy(payload)))
        if stage == 'selection':
            selected = next(self.selections)
            answers = {}
            for key, question in payload['questions'].items():
                choice = selected if key == 'frame' else 'no'
                answers[key] = {'type': 'choice', 'choice': choice,
                                'probabilities': {k: float(k == choice) for k in question['criteria']},
                                'confidence': 1.0}
            outer = {'model': 'wrong' if self.wrong_model else 'mini', 'answers': answers}
        else:
            value = self.argument_value if stage == 'arguments' else {'bindings': [{'stepIndex': 0, 'candidateRef': 'wait_here'}]}
            outer = {'model': 'qwen', 'choices': [{'finish_reason': 'stop', 'message': {'content': json.dumps(value)}}]}
        return {'status_code': 200, 'latency_ms': 1, 'response_raw': json.dumps(outer), 'response_json': outer}


class DecisionFramePilotTests(unittest.TestCase):
    def test_complete_frame_and_parameters_reach_real_grounder_without_gold_in_payload(self):
        transport = ScriptedTransport(['Wait', 'END'])
        row = pilot.evaluate_case(case(), {'d3': 'mini', 'qwen': 'qwen'}, transport)
        self.assertEqual(row.get('groundingOutcome'), 'plan')
        self.assertEqual(row.get('planSteps', [{}])[0].get('durationSeconds'), 20)
        self.assertFalse(row.get('falsePlan'))
        self.assertEqual([s for s, _ in transport.calls], ['selection', 'selection', 'arguments', 'grounding'])
        self.assertNotIn('expected', json.dumps([p for _, p in transport.calls]))
        self.assertNotIn('wait_here', json.dumps([p for s, p in transport.calls if s != 'grounding']))

    def test_none_stops_without_qwen_and_is_scored_against_request_gold(self):
        transport = ScriptedTransport(['NONE'])
        row = pilot.evaluate_case(case(), {'d3': 'mini', 'qwen': 'qwen'}, transport)
        self.assertEqual(row.get('groundingOutcome'), 'no_action')
        self.assertTrue(row.get('missedPlan'))
        self.assertEqual(len(transport.calls), 1)

    def test_model_drift_aborts_without_parameter_or_grounding_call(self):
        transport = ScriptedTransport(['Wait'], wrong_model=True)
        row = pilot.evaluate_case(case(), {'d3': 'mini', 'qwen': 'qwen'}, transport)
        self.assertEqual(row.get('groundingOutcome'), 'invalid')
        self.assertEqual(len(transport.calls), 1)

    def test_model_cannot_hide_frame_disagreement_by_returning_valid_wait(self):
        value = arguments()
        value.update(frameDisagreement=True, frameDisagreementReason='The frame omitted an action.')
        transport = ScriptedTransport(['Wait', 'END'], value)
        row = pilot.evaluate_case(case(), {'d3': 'mini', 'qwen': 'qwen'}, transport)
        self.assertEqual(row.get('groundingOutcome'), 'clarify')
        self.assertEqual(len(transport.calls), 3)

    def test_wrong_duration_is_counted_as_false_plan_even_when_grounding_outcome_matches(self):
        value = arguments()
        value['arguments'][0]['durationAmount'] = 40
        transport = ScriptedTransport(['Wait', 'END'], value)
        row = pilot.evaluate_case(case(), {'d3': 'mini', 'qwen': 'qwen'}, transport, reverse=True)
        self.assertTrue(row.get('falsePlan'))
        self.assertEqual(row.get('planSteps', [{}])[0].get('durationSeconds'), 40)
        self.assertEqual(list(transport.calls[0][1]['questions']['frame']['criteria'])[-1], 'NONE')

    def test_reverse_arm_reverses_grounding_candidates_without_mutating_case(self):
        fixture = case()
        fixture['snapshot']['candidates'].append({
            'ref': 'wait_other', 'kind': 'Wait', 'label': 'Other position',
            'target': {'Kind': 'Person', 'PersonId': 'npc'}})
        original_refs = [candidate['ref'] for candidate in fixture['snapshot']['candidates']]
        transport = ScriptedTransport(['Wait', 'END'])
        pilot.evaluate_case(fixture, {'d3': 'mini', 'qwen': 'qwen'}, transport, reverse=True)
        grounding_payload = next(payload for stage, payload in transport.calls if stage == 'grounding')
        grounding_body = json.loads(grounding_payload['messages'][1]['content'])
        refs = [candidate['ref'] for candidate in grounding_body['currentCandidates']]
        self.assertEqual(refs, list(reversed(original_refs)))
        self.assertEqual([candidate['ref'] for candidate in fixture['snapshot']['candidates']], original_refs)

    def test_guard_runtime_error_propagates_instead_of_becoming_invalid_result(self):
        def guarded(stage, payload):
            raise RuntimeError('frozen input changed')

        with self.assertRaises(RuntimeError) as _raised_111:
            pilot.evaluate_case(case(), {'d3': 'mini', 'qwen': 'qwen'}, guarded)
        self.assertTrue(('frozen input changed' in str(_raised_111.exception)), str(_raised_111.exception))

    def test_eight_steps_require_explicit_end_or_overflow_instead_of_silent_truncation(self):
        transport = ScriptedTransport(['Wait'] * 8 + ['MORE_NEEDED'])
        row = pilot.evaluate_case(case(), {'d3': 'mini', 'qwen': 'qwen'}, transport)
        self.assertEqual(row.get('groundingOutcome'), 'clarify')
        self.assertEqual(len(row.get('frameKinds', [])), 8)
        self.assertEqual(len(transport.calls), 9)

    def test_malformed_d3_json_aborts_without_more_calls(self):
        transport = ScriptedTransport(['Wait'])
        original = transport.__call__

        def malformed(stage, payload):
            response = original(stage, payload)
            response['response_raw'] = '{"model":"mini","model":"mini","answers":{}}'
            return response

        row = pilot.evaluate_case(case(), {'d3': 'mini', 'qwen': 'qwen'}, malformed)
        self.assertEqual(row.get('groundingOutcome'), 'invalid')
        self.assertEqual(row.get('groundingReason'), 'invalid_outer_json')

    def test_timeout_aborts_without_reusing_or_extending_partial_frame(self):
        transport = ScriptedTransport(['Wait'])
        original = transport.__call__

        def timeout(stage, payload):
            response = original(stage, payload)
            response['latency_ms'] = pilot.TIMEOUT_MS + 1
            return response

        row = pilot.evaluate_case(case(), {'d3': 'mini', 'qwen': 'qwen'}, timeout)
        self.assertEqual(row.get('groundingOutcome'), 'invalid')
        self.assertEqual(row.get('groundingReason'), 'over_timeout')
        self.assertEqual(len(transport.calls), 1)

    def test_malformed_qwen_arguments_abort_before_grounding(self):
        transport = ScriptedTransport(['Wait', 'END'])
        original = transport.__call__

        def malformed(stage, payload):
            response = original(stage, payload)
            if stage == 'arguments':
                response['response_raw'] = '{"model":"qwen","model":"qwen","choices":[]}'
            return response

        row = pilot.evaluate_case(case(), {'d3': 'mini', 'qwen': 'qwen'}, malformed)
        self.assertEqual(row.get('groundingOutcome'), 'invalid')
        self.assertEqual(row.get('groundingReason'), 'invalid_outer_json')
        self.assertEqual([stage for stage, _ in transport.calls], ['selection', 'selection', 'arguments'])

    def test_qwen_timeout_aborts_before_grounding(self):
        transport = ScriptedTransport(['Wait', 'END'])
        original = transport.__call__

        def timeout(stage, payload):
            response = original(stage, payload)
            if stage == 'arguments':
                response['transport_error'] = 'request timed out'
            return response

        row = pilot.evaluate_case(case(), {'d3': 'mini', 'qwen': 'qwen'}, timeout)
        self.assertEqual(row.get('groundingOutcome'), 'invalid')
        self.assertEqual(row.get('groundingReason'), 'over_timeout')
        self.assertEqual([stage for stage, _ in transport.calls], ['selection', 'selection', 'arguments'])


if __name__ == '__main__':
    unittest.main()
