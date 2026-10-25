"""Offline config validation tests; never contact the TTS endpoint."""

from __future__ import annotations

import json
import tempfile
import unittest
import wave
from pathlib import Path
from unittest.mock import patch

from tools.BreezeTtsTrial.portrait_emotion_samples import load_and_validate, measure_speech_pace, sample_instruction, run


LONG_CONFIG = Path(__file__).with_name("portrait_emotion_samples_long.json")
LONG_MALE_CONFIG = Path(__file__).with_name("portrait_emotion_samples_long_male.json")


class PortraitEmotionConfigTests(unittest.TestCase):
    def test_every_mood_has_same_ceiling_and_preserves_voice_identity(self) -> None:
        voice = {"stableInstruction": "A low female voice. Use a distinctly rapid but clearly articulated conversational pace."}
        for mood in ("neutral", "happy", "angry", "sad", "afraid", "guarded"):
            instruction = sample_instruction(voice, {"deliveryText": mood})
            self.assertIn(voice["stableInstruction"], instruction)
            self.assertIn("325 words per minute", instruction)
            self.assertIn("A low female voice", instruction)

    def test_fast_generated_audio_is_rejected_before_visemes(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            audio = Path(directory) / "speech.wav"
            with wave.open(str(audio), "wb") as output:
                output.setparams((1, 2, 24000, 0, "NONE", "not compressed"))
                output.writeframes(b"\x01\x00" * 24000 * 3)
            with self.assertRaises(ValueError) as _raised_34:
                measure_speech_pace(audio, "one two three four five six seven eight nine ten eleven twelve thirteen fourteen fifteen sixteen seventeen")
            self.assertTrue(('speech pace ' in str(_raised_34.exception) and ' exceeds ' in str(_raised_34.exception) and '325' in str(_raised_34.exception)), str(_raised_34.exception))
            report = measure_speech_pace(audio, "one two three four five six seven eight nine ten eleven twelve thirteen fourteen fifteen sixteen")
            self.assertEqual(report["wordsPerMinute"], 320.0)
            self.assertEqual(report["maxWordsPerMinute"], 325)

    def test_generation_keeps_fast_raw_but_never_creates_final_cues(self) -> None:
        def fast_synthesis(*args, **kwargs):
            with wave.open(str(args[6]), "wb") as output:
                output.setparams((1, 2, 24000, 0, "NONE", "not compressed"))
                output.writeframes(b"\x01\x00" * 24000 * 3)
            return {"status": "succeeded"}

        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "samples"
            module = "tools.BreezeTtsTrial.portrait_emotion_samples"
            with patch(module + ".synthesize", side_effect=fast_synthesis), \
                 patch(module + ".normalize_pcm16_wave") as normalize, \
                 patch(module + ".run_rhubarb") as rhubarb:
                with self.assertRaises(ValueError) as _raised_53:
                    run(LONG_CONFIG, output, Path(__file__))
                self.assertTrue(('speech pace' in str(_raised_53.exception)), str(_raised_53.exception))
                normalize.assert_not_called()
                rhubarb.assert_not_called()
            manifest = json.loads((output / "manifest.json").read_text())
            self.assertEqual(manifest["status"], "completed-with-errors")
            self.assertEqual(manifest["samples"][0]["status"], "failed")
            self.assertTrue((output / "neutral.raw.wav").is_file())
            self.assertFalse((output / "neutral.visemes.json").exists())

    def test_generated_text_can_differ_from_pinned_reference_transcript(self) -> None:
        config, reference = load_and_validate(LONG_CONFIG)
        self.assertNotEqual(config["exactText"], config["voice"]["referenceText"])
        self.assertEqual(
            config["voice"]["referenceSha256"],
            "e8ca4c1b5f252462ef86350435cf5b0e150d9bcc15002a63763b245d43253b0e",
        )
        self.assertTrue(reference.is_file())

    def test_female_and_male_long_configs_validate(self) -> None:
        female, _ = load_and_validate(LONG_CONFIG)
        male, male_reference = load_and_validate(LONG_MALE_CONFIG)
        self.assertEqual(female["voice"]["profileId"], "npcvoice-01")
        self.assertEqual(male["voice"]["perceivedGender"], "male")
        self.assertNotEqual(male["exactText"], male["voice"]["referenceText"])
        self.assertTrue(male_reference.is_file())

    def test_blank_reference_transcript_is_rejected(self) -> None:
        config = json.loads(LONG_CONFIG.read_text(encoding="utf-8-sig"))
        config["voice"]["referenceText"] = "  "
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "invalid.json"
            path.write_text(json.dumps(config), encoding="utf-8")
            with self.assertRaises(ValueError) as _raised_86:
                load_and_validate(path)
            self.assertTrue(('reference transcript must be non-empty' in str(_raised_86.exception)), str(_raised_86.exception))

    def test_changed_reference_hash_is_rejected(self) -> None:
        config = json.loads(LONG_MALE_CONFIG.read_text(encoding="utf-8-sig"))
        config["voice"]["referenceSha256"] = "0" * 64
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "invalid-reference-hash.json"
            path.write_text(json.dumps(config), encoding="utf-8")
            with self.assertRaises(ValueError) as _raised_95:
                load_and_validate(path)
            self.assertTrue(('reference WAV SHA-256' in str(_raised_95.exception)), str(_raised_95.exception))


if __name__ == "__main__":
    unittest.main()
