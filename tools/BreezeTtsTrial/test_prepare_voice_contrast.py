"""Offline tests for controlled voice-contrast manifest preparation."""

import hashlib
import json
import tempfile
import unittest
from pathlib import Path

import coordinator
import prepare_voice_contrast


class PrepareVoiceContrastTests(unittest.TestCase):
    def base_context(self):
        return {
            "schemaVersion": 1, "personId": "Gwen-Cienne", "mood": 12,
            "dispositionKnown": True, "disposition": 70,
            "trustKnown": False, "trust": 0,
            "fear": 5, "resentment": 2, "encounters": 9,
            "traits": {
                "warmth": "warm", "patience": "patient", "curiosity": "inquisitive",
                "privacy": "selective", "manner": "courteous", "outlook": "pragmatic",
                "emotional stability": "even-tempered", "ambition": "content", "integrity": "principled",
                "courage": "steadfast", "dutifulness": "conscientious", "vocabulary": "precise",
                "eloquence": "articulate", "status attitude": "egalitarian", "humor": "wry",
                "self esteem": "grounded", "sensitivity": "attuned",
            },
        }

    def setup_trial(self, root):
        voice_root = root / "voices"
        voice_root.mkdir()
        ref = voice_root / "gwen-reference.wav"
        ref.write_bytes(b"verified reference fixture")
        rows = {}
        for index in range(12):
            profile_id = "npcvoice-{:02d}".format(index + 1)
            profile = {"instruction": "Profile {} identity.".format(index + 1), "seed": index + 100,
                       "perceivedGender": "female" if index % 2 == 0 else "male"}
            if index == 0:
                profile.update({"voiceRef": ref.name, "referenceText": "Persistent reference transcript.",
                    "voiceReferenceSha256": hashlib.sha256(ref.read_bytes()).hexdigest()})
            rows[profile_id] = profile
        profiles_path = root / "profiles.json"
        profiles_path.write_text(json.dumps({"schemaVersion": 1, "profiles": rows}), encoding="utf-8")
        bindings_path = root / "voice-bindings.json"
        bindings_path.write_text(json.dumps({"schemaVersion": 1,
            "bindings": {"Gwen-Cienne": "npcvoice-01"}}), encoding="utf-8")
        return profiles_path, bindings_path, voice_root

    def test_manifest_has_six_isolated_cases_same_text_traits_profile_seed_and_reference(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            profiles, bindings, voices = self.setup_trial(root)
            context = self.base_context()
            manifest = prepare_voice_contrast.build_manifest(context, profiles, bindings, voices)

        self.assertEqual("controlled-voice-contrast", manifest["manifestKind"])
        self.assertEqual("fixed-authored-text-not-submit-free-text", manifest["sourceKind"])
        self.assertEqual("Gwen-Cienne", manifest["personId"])
        self.assertEqual("npcvoice-01", manifest["expectedProfileId"])
        self.assertEqual(100, manifest["voiceSeed"])
        self.assertEqual("gwen-reference.wav", manifest["referencePath"])
        self.assertEqual(coordinator.canonical_json_hash(context["traits"]), manifest["baseTraitsSha256"])
        self.assertEqual(context, manifest["baseContext"])
        cases = manifest["cases"]
        self.assertEqual(["neutral", "positiveMood", "lowMood", "highFear", "highResentment", "whisper"],
                         [case["label"] for case in cases])
        self.assertEqual({(case["mood"], case["fear"], case["resentment"]) for case in cases},
                         {(0, 0, 0), (70, 0, 0), (-70, 0, 0), (0, 90, 0), (0, 0, 90)})
        self.assertTrue(all(case["controlledDirection"]["emotion"] == "neutral" for case in cases))
        self.assertEqual("whisper", cases[-1]["controlledDirection"]["deliveryMode"])
        self.assertTrue(all(case["controlledDirection"]["deliveryMode"] == "normal" for case in cases[:-1]))
        self.assertEqual(prepare_voice_contrast.FIXED_TEXT, manifest["fixedText"])

    def test_rejects_wrong_profile_binding_without_mutation(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            profiles, bindings, voices = self.setup_trial(root)
            original = json.loads(bindings.read_text(encoding="utf-8"))
            original["bindings"]["Gwen-Cienne"] = "npcvoice-08"
            bindings.write_text(json.dumps(original), encoding="utf-8")
            with self.assertRaises(prepare_voice_contrast.ContrastError) as _raised_83:
                prepare_voice_contrast.build_manifest(self.base_context(), profiles, bindings, voices)
            self.assertTrue(('npcvoice-01' in str(_raised_83.exception)), str(_raised_83.exception))
            self.assertEqual("npcvoice-08", json.loads(bindings.read_text(encoding="utf-8"))["bindings"]["Gwen-Cienne"])

    def test_rejects_missing_persistent_reference_and_wrong_person(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            profiles, bindings, voices = self.setup_trial(root)
            registry = json.loads(profiles.read_text(encoding="utf-8"))
            registry["profiles"]["npcvoice-01"].pop("voiceRef")
            registry["profiles"]["npcvoice-01"].pop("referenceText")
            registry["profiles"]["npcvoice-01"].pop("voiceReferenceSha256")
            profiles.write_text(json.dumps(registry), encoding="utf-8")
            with self.assertRaises(prepare_voice_contrast.ContrastError) as _raised_96:
                prepare_voice_contrast.build_manifest(self.base_context(), profiles, bindings, voices)
            self.assertTrue(('persistent reference' in str(_raised_96.exception)), str(_raised_96.exception))
            with self.assertRaises(prepare_voice_contrast.ContrastError):
                prepare_voice_contrast.build_manifest(self.base_context() | {"personId": "other"},
                    profiles, bindings, voices)


if __name__ == "__main__":
    unittest.main(verbosity=2)
