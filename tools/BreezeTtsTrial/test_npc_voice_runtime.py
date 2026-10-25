import io
import tempfile
import unittest
import wave
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch

import npc_voice_runtime as runtime


class Profile:
    profile_id = 'npc-owned-test'
    anchor_required = True
    age = 'adult'
    race = 'Argonian'
    legacy_migrated = False

    def to_coordinator_profile(self):
        return {'instruction': 'Neutral individual voice.', 'seed': 123, 'perceivedGender': 'female',
            'race': self.race}


class Store:
    def __init__(self):
        self.profile = Profile()
        self.records = []
        self.identity = None

    def get_or_create(self, person_id, gender, age='unknown', race='Unknown'):
        self.identity = (person_id, gender, age, race)
        return self.profile

    def record_neutral_anchor(self, profile, audio, metadata):
        self.records.append((audio, metadata))
        self.profile.anchor_required = False
        return self.profile


class PersonalVoiceRuntimeTests(unittest.TestCase):
    def setUp(self):
        self.request = SimpleNamespace(request_id='request-1', person_id='npc-1', voice_gender='female',
            voice_identity={
                'sex': {'value': 'female', 'known': True},
                'race': {'value': 'Argonian', 'known': True},
                'age': {'value': 'adult', 'confidence': 'high'},
            },
            delivery_context={'mood': -80}, text='An angry utterance.')

    def test_existing_anchor_skips_synthesis_and_retains_profile(self):
        store = Store()
        store.profile.anchor_required = False
        with tempfile.TemporaryDirectory() as root, patch.object(runtime, 'NpcVoiceProfileStore', return_value=store):
            def forbidden(*args):
                self.fail('Existing voice must not synthesize a new anchor')
            result = runtime.resolve_personal_voice(self.request, Path(root), forbidden, lambda: False, lambda: 40)
        self.assertEqual('npc-owned-test', result[0])
        self.assertEqual(123, result[1]['seed'])
        self.assertFalse(result[2]['anchorCreated'])

    def test_native_voice_identity_gender_and_race_override_unknown_bridge_hints(self):
        store = Store()
        store.profile.anchor_required = False
        self.request.voice_gender = 'unknown'
        with tempfile.TemporaryDirectory() as root, patch.object(runtime, 'NpcVoiceProfileStore', return_value=store):
            runtime.resolve_personal_voice(self.request, Path(root), lambda *args: self.fail('anchor is reusable'),
                lambda: False, lambda: 40)
        self.assertEqual(('npc-1', 'female', 'adult', 'Argonian'), store.identity)

    def test_conflicting_known_native_and_bridge_genders_fail_closed(self):
        store = Store()
        self.request.voice_gender = 'male'
        with tempfile.TemporaryDirectory() as root, patch.object(runtime, 'NpcVoiceProfileStore', return_value=store):
            with self.assertRaises(ValueError) as _raised_74:
                runtime.resolve_personal_voice(self.request, Path(root), lambda *args: None,
                    lambda: False, lambda: 40)
            self.assertTrue(('gender' in str(_raised_74.exception) and 'conflict' in str(_raised_74.exception)) or ('conflict' in str(_raised_74.exception) and 'gender' in str(_raised_74.exception)), str(_raised_74.exception))
        self.assertIsNone(store.identity)

    def test_first_anchor_uses_neutral_identity_and_exact_gallery_text(self):
        store = Store()
        calls = []
        with tempfile.TemporaryDirectory() as root, patch.object(runtime, 'NpcVoiceProfileStore', return_value=store):
            def synthesize(profile, text, output, timeout):
                calls.append((profile, text, timeout))
                with wave.open(str(output), 'wb') as wav:
                    wav.setparams((1, 2, 24000, 0, 'NONE', 'not compressed'))
                    wav.writeframes((b'\xe8\x03\x18\xfc') * 48000)
            result = runtime.resolve_personal_voice(self.request, Path(root), synthesize, lambda: False, lambda: 27)
        self.assertEqual(1, len(calls))
        self.assertEqual(runtime.ANCHOR_TEXT, calls[0][1])
        self.assertNotIn('delivery_context', calls[0][0])
        self.assertEqual('Neutral individual voice.', calls[0][0]['instruction'])
        self.assertEqual(27, calls[0][2])
        self.assertEqual(('npc-1', 'female', 'adult', 'Argonian'), store.identity)
        self.assertEqual(1, len(store.records))
        self.assertEqual(runtime.ANCHOR_TEXT, store.records[0][1]['text'])
        self.assertIsNone(store.records[0][1]['voiceRef'])
        self.assertTrue(result[2]['anchorCreated'])
        with wave.open(io.BytesIO(store.records[0][0])) as wav:
            self.assertEqual(96000, wav.getnframes())

    def test_cancellation_after_synthesis_does_not_bind_anchor(self):
        store = Store()
        cancelled = False
        with tempfile.TemporaryDirectory() as root, patch.object(runtime, 'NpcVoiceProfileStore', return_value=store):
            def synthesize(*args):
                nonlocal cancelled
                cancelled = True
            with self.assertRaises(runtime.PersonalVoiceCancelled):
                runtime.resolve_personal_voice(self.request, Path(root), synthesize, lambda: cancelled, lambda: 20)
        self.assertEqual([], store.records)

    def test_expired_budget_never_synthesizes_or_commits(self):
        store = Store()
        with tempfile.TemporaryDirectory() as root, patch.object(runtime, 'NpcVoiceProfileStore', return_value=store):
            def forbidden(*args):
                self.fail('Expired budget must not start a helper')
            with self.assertRaises(TimeoutError):
                runtime.resolve_personal_voice(self.request, Path(root), forbidden, lambda: False, lambda: 0)
        self.assertEqual([], store.records)


if __name__ == '__main__':
    unittest.main()
