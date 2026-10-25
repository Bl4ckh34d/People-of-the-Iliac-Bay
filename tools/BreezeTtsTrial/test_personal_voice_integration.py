"""CPU-only production coordinator test of persistent identity across mood changes."""
import json
from pathlib import Path
import tempfile
import unittest
import wave
from unittest.mock import patch

import coordinator
from test_coordinator import CoordinatorProtocolTests
from npc_voice_profiles import ANCHOR_TEXT


class PersonalVoiceIntegrationTests(unittest.TestCase):
    def test_two_moods_reuse_one_neutral_anchor_and_seed(self):
        fixture = CoordinatorProtocolTests()
        calls = []
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / 'requests').mkdir()
            (root / 'working').mkdir()
            directions_path = root / 'directions.json'
            directions_path.write_text(json.dumps({'schemaVersion': 1, 'directions': {
                'npc-1': {'emotion': 'fear', 'deliveryMode': 'whisper', 'provenance': 'controlled_trial'}
            }}), encoding='utf-8')

            def synthesize(endpoint, model, text, instruction, reference_wav, reference_text,
                           output_wav, timeout_seconds, *, seed, guidance_scale, cancel_event):
                calls.append((text, instruction, reference_wav, reference_text, seed))
                with wave.open(str(output_wav), 'wb') as output:
                    output.setparams((1, 2, 24000, 0, 'NONE', 'not compressed'))
                    output.writeframes(b'\xe8\x03\x18\xfc' * 48000)
                return {'synthesisMs': 10, 'firstAudioMs': 1, 'audioDeltaCount': 1, 'serverTiming': {}}

            with patch('server_sse_client.synthesize', side_effect=synthesize):
                for index, mood in enumerate((-70, 70)):
                    request_id = 'personal-' + str(index)
                    context = fixture.delivery_context(mood=mood, fear=0, resentment=0,
                        trustKnown=True, trust=90)
                    document = fixture.request_json(requestId=request_id, deliveryContext=context,
                        voiceGender='female', voiceIdentity={
                            'schemaVersion': 1,
                            'sex': {'value': 'female', 'known': True, 'source': 'native.static-npc'},
                            'race': {'value': 'Argonian', 'known': True, 'source': 'native.static-npc'},
                            'age': {'value': 'adult', 'confidence': 'high', 'source': 'native.static-npc'},
                            'origin': {'value': 'unknown', 'known': False, 'source': 'native.not-provided'},
                        })
                    claimed = root / 'working' / (request_id + '.claimed.request.json')
                    claimed.write_text(json.dumps(document), encoding='utf-8')
                    result = coordinator.process_claimed(claimed, request_id, root,
                        root / 'cli.exe', root / 'model.gguf', '0' * 64, root / 'spec.json',
                        root / 'nonexistent-legacy-profiles.json', root / 'legacy-bindings.json',
                        root / 'voices', directions_path, 'cuda', 300, 'test',
                        server_endpoint='http://127.0.0.1:8094', per_npc_voices=True)
                    self.assertEqual('ready', result['status'], result)

            self.assertEqual(3, len(calls))
            anchor, sad, happy = calls
            self.assertEqual(ANCHOR_TEXT, anchor[0])
            self.assertIsNone(anchor[2])
            self.assertIsNone(anchor[3])
            self.assertEqual(anchor[4], sad[4])
            self.assertEqual(sad[4], happy[4])
            self.assertEqual(sad[2], happy[2])
            self.assertIsNotNone(sad[2])
            self.assertEqual(ANCHOR_TEXT, sad[3])
            self.assertNotEqual(sad[1], happy[1])
            self.assertIn('Adult female Argonian', sad[1])
            self.assertIn('Brazilian Portuguese-influenced English', sad[1])
            self.assertIn('Dry sibilants', sad[1])
            self.assertIn('Temporary emotion (native numeric context): grief.', sad[1])
            self.assertIn('Temporary emotion (native numeric context): joy.', happy[1])
            self.assertNotIn('controlled', sad[1].lower())
            self.assertIn('Adult female Argonian', anchor[1])
            self.assertNotIn('Temporary emotion', anchor[1])
            self.assertIn('low spirits', sad[1])
            self.assertIn('buoyant', happy[1])
            self.assertNotIn('low spirits', anchor[1])
            self.assertFalse((root / 'legacy-bindings.json').exists())
            saved = list((root / 'voices' / 'per-npc' / 'v2' / 'profiles').glob('*.json'))
            self.assertEqual(1, len(saved))
            profile = json.loads(saved[0].read_text())
            self.assertEqual('Argonian', profile['race'])
            self.assertEqual('female', profile['perceivedGender'])
            self.assertEqual(sad[4], profile['seed'])


if __name__ == '__main__':
    unittest.main()
