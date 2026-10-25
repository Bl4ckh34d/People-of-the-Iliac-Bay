"""Offline tests for explicit persistent voice-reference promotion."""

import hashlib
import json
import tempfile
import unittest
import wave
from pathlib import Path

import promote_reference


class PromoteReferenceTests(unittest.TestCase):
    transcript = "The evening bell rings over the quiet harbor.  Exact spacing stays."

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.voice_root = self.root / "voices"
        self.voice_root.mkdir()
        self.source = self.root / "sample.wav"
        with wave.open(str(self.source), "wb") as output:
            output.setnchannels(1)
            output.setsampwidth(2)
            output.setframerate(22050)
            output.writeframes(b"\x00\x00" * 2205)
        self.digest = hashlib.sha256(self.source.read_bytes()).hexdigest()
        self.evidence = self.root / "sample.evidence.json"
        self.evidence.write_text(json.dumps({
            "schemaVersion": 1,
            "requestId": "request-002",
            "profileId": "npcvoice-01",
            "voiceGender": "female",
            "finalizedText": self.transcript,
            "instruction": "Stable voice identity: Stable female voice. Emotional delivery: neutral. Delivery mode: normal.",
            "profileInstruction": "Stable female voice.",
            "seed": 7,
            "wavSha256": self.digest,
        }), encoding="utf-8")
        self.profiles = self.root / "profiles.json"
        self.profiles.write_text(json.dumps({"schemaVersion": 1, "profiles": {
            "npcvoice-01": {"instruction": "Stable female voice.", "perceivedGender": "female", "seed": 7}
        }}), encoding="utf-8")

    def tearDown(self):
        self.temporary.cleanup()

    def promote(self, **overrides):
        args = {
            "source_wav": self.source,
            "evidence_path": self.evidence,
            "profiles_path": self.profiles,
            "voice_root": self.voice_root,
            "profile_id": "npcvoice-01",
            "expected_gender": "female",
            "apply": False,
            "replace": False,
        }
        args.update(overrides)
        return promote_reference.promote_reference(**args)

    def test_preview_validates_exact_evidence_and_wav_without_writing(self):
        before = self.profiles.read_bytes()
        result = self.promote()
        self.assertEqual(self.digest, result["sha256"])
        self.assertEqual(self.transcript, result["referenceText"])
        self.assertFalse((self.voice_root / result["assetId"]).exists())
        self.assertEqual(before, self.profiles.read_bytes())

    def test_apply_copies_reference_and_atomically_updates_trusted_profile(self):
        result = self.promote(apply=True)
        profile = json.loads(self.profiles.read_text(encoding="utf-8"))["profiles"]["npcvoice-01"]
        self.assertEqual(result["assetId"], profile["voiceRef"])
        self.assertEqual(self.transcript, profile["referenceText"])
        self.assertEqual(self.digest, profile["voiceReferenceSha256"])
        self.assertEqual(self.source.read_bytes(), (self.voice_root / result["assetId"]).read_bytes())

    def test_rejects_evidence_for_a_different_profile(self):
        evidence = json.loads(self.evidence.read_text(encoding="utf-8"))
        evidence["profileId"] = "npcvoice-02"
        self.evidence.write_text(json.dumps(evidence), encoding="utf-8")
        with self.assertRaises(promote_reference.PromotionError) as _raised_82:
            self.promote()
        self.assertTrue(('evidence profileId' in str(_raised_82.exception)), str(_raised_82.exception))

    def test_rejects_source_that_does_not_match_evidence_hash(self):
        self.source.write_bytes(self.source.read_bytes() + b"extra")
        with self.assertRaises(promote_reference.PromotionError) as _raised_87:
            self.promote()
        self.assertTrue(('WAV SHA-256' in str(_raised_87.exception)), str(_raised_87.exception))

    def test_rejects_non_pcm16_wav(self):
        with wave.open(str(self.source), "wb") as output:
            output.setnchannels(1)
            output.setsampwidth(1)
            output.setframerate(22050)
            output.writeframes(b"\x00" * 2205)
        evidence = json.loads(self.evidence.read_text(encoding="utf-8"))
        evidence["wavSha256"] = hashlib.sha256(self.source.read_bytes()).hexdigest()
        self.evidence.write_text(json.dumps(evidence), encoding="utf-8")
        with self.assertRaises(promote_reference.PromotionError) as _raised_99:
            self.promote()
        self.assertTrue(('PCM16' in str(_raised_99.exception)), str(_raised_99.exception))

    def test_rejects_reference_longer_than_sixty_seconds(self):
        with wave.open(str(self.source), "wb") as output:
            output.setnchannels(1)
            output.setsampwidth(2)
            output.setframerate(8000)
            output.writeframes(b"\x00\x00" * 480001)
        evidence = json.loads(self.evidence.read_text(encoding="utf-8"))
        evidence["wavSha256"] = hashlib.sha256(self.source.read_bytes()).hexdigest()
        self.evidence.write_text(json.dumps(evidence), encoding="utf-8")
        with self.assertRaises(promote_reference.PromotionError) as _raised_111:
            self.promote()
        self.assertTrue(('at most 60 seconds' in str(_raised_111.exception)), str(_raised_111.exception))

    def test_rejects_known_voice_gender_that_conflicts_with_profile(self):
        evidence = json.loads(self.evidence.read_text(encoding="utf-8"))
        evidence["voiceGender"] = "male"
        self.evidence.write_text(json.dumps(evidence), encoding="utf-8")
        with self.assertRaises(promote_reference.PromotionError) as _raised_118:
            self.promote()
        self.assertTrue(('voiceGender' in str(_raised_118.exception)), str(_raised_118.exception))

    def test_existing_reference_requires_explicit_replace_and_old_file_stays(self):
        old = self.voice_root / "references" / "old-reference.wav"
        old.parent.mkdir()
        old.write_bytes(b"old-reference")
        profiles = json.loads(self.profiles.read_text(encoding="utf-8"))
        profiles["profiles"]["npcvoice-01"].update({
            "voiceRef": "references/old-reference.wav",
            "referenceText": "old transcript",
            "voiceReferenceSha256": hashlib.sha256(old.read_bytes()).hexdigest(),
        })
        self.profiles.write_text(json.dumps(profiles), encoding="utf-8")
        with self.assertRaises(promote_reference.PromotionError) as _raised_132:
            self.promote(apply=True)
        self.assertTrue(('--replace' in str(_raised_132.exception)), str(_raised_132.exception))
        result = self.promote(apply=True, replace=True)
        self.assertTrue(old.exists())
        self.assertEqual(result["assetId"], json.loads(self.profiles.read_text(encoding="utf-8"))[
            "profiles"]["npcvoice-01"]["voiceRef"])


if __name__ == "__main__":
    unittest.main(verbosity=2)
