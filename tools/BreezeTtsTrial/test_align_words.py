import hashlib
import json
import tempfile
import unittest
import wave
from pathlib import Path
from subprocess import CompletedProcess
from unittest.mock import patch

try:
    from tools.BreezeTtsTrial.align_words import run_alignment, validate_alignment
except ModuleNotFoundError:
    from align_words import run_alignment, validate_alignment


class AlignmentValidationTests(unittest.TestCase):
    def setUp(self):
        self.audio = {"sample_rate": 24000, "frames": 24000}

    def test_converts_aligner_offsets_to_audio_sample_clock_and_covers_exact_words(self):
        raw = {
            "sample_rate": 16000,
            "words": [
                {"word": "Hello,", "start_sample": 0, "end_sample": 4000, "confidence": 0.9},
                {"word": "world!", "start_sample": 4000, "end_sample": 8000, "confidence": 0.8},
            ],
        }
        result = validate_alignment("Hello, world!", raw, self.audio)
        self.assertEqual(result["status"], "ready")
        self.assertEqual(result["timelineKind"], "forced-word-alignment")
        self.assertEqual(result["words"][0]["startSample"], 0)
        self.assertEqual(result["words"][0]["endSample"], 6000)
        self.assertEqual(result["words"][0]["textStartUtf16"], 0)
        self.assertEqual(result["words"][1]["textStartUtf16"], 7)
        self.assertEqual(result["words"][1]["textLengthUtf16"], 5)
        self.assertEqual(result["words"][1]["endSample"], 12000)
        self.assertEqual(result["audioSampleRate"], 24000)

    def test_rejects_missing_or_extra_aligned_words(self):
        raw = {"sample_rate": 24000, "words": [
            {"word": "Hello", "start_sample": 0, "end_sample": 1000}
        ]}
        with self.assertRaises(ValueError) as _raised_43:
            validate_alignment("Hello brave world", raw, self.audio)
        self.assertTrue(('coverage' in str(_raised_43.exception)), str(_raised_43.exception))

    def test_rejects_nonmonotonic_or_out_of_range_spans(self):
        raw = {"sample_rate": 24000, "words": [
            {"word": "Hello", "start_sample": 500, "end_sample": 1500},
            {"word": "world", "start_sample": 1000, "end_sample": 2000},
        ]}
        with self.assertRaises(ValueError) as _raised_51:
            validate_alignment("Hello world", raw, self.audio)
        self.assertTrue(('monotonic' in str(_raised_51.exception)), str(_raised_51.exception))
        raw["words"][1]["start_sample"] = 22000
        raw["words"][1]["end_sample"] = 25000
        with self.assertRaises(ValueError) as _raised_55:
            validate_alignment("Hello world", raw, self.audio)
        self.assertTrue(('audio bounds' in str(_raised_55.exception)), str(_raised_55.exception))

    def test_rejects_unknown_or_malformed_alignment_schema(self):
        with self.assertRaises(ValueError) as _raised_59:
            validate_alignment("Hello", {"words": []}, self.audio)
        self.assertTrue(('sample_rate' in str(_raised_59.exception)), str(_raised_59.exception))
        with self.assertRaises(ValueError) as _raised_61:
            validate_alignment("Hello", {"sample_rate": 24000, "words": []}, self.audio)
        self.assertTrue(('word timestamps' in str(_raised_61.exception)), str(_raised_61.exception))

    def test_accepts_real_cli_top_level_array_using_explicit_pinned_model_clock(self):
        raw = [
            {"word": "today", "start_sample": 70400, "end_sample": 78080, "confidence": 0},
        ]
        audio = {"sample_rate": 24000, "frames": 119040}
        result = validate_alignment("today", raw, audio, aligner_sample_rate=16000)
        self.assertEqual(result["alignerSampleRate"], 16000)
        self.assertEqual(result["alignerSampleRateSource"], "explicit")
        self.assertEqual(result["words"][0]["startSample"], 105600)
        self.assertEqual(result["words"][0]["endSample"], 117120)

    def test_rejects_top_level_array_without_explicit_timestamp_rate(self):
        raw = [{"word": "Hello", "start_sample": 0, "end_sample": 1000}]
        with self.assertRaises(ValueError) as _raised_77:
            validate_alignment("Hello", raw, self.audio)
        self.assertTrue(('sample_rate' in str(_raised_77.exception)), str(_raised_77.exception))

    def test_rejects_model_clock_spans_that_exceed_source_wav(self):
        raw = [{"word": "Hello", "start_sample": 0, "end_sample": 16001}]
        with self.assertRaises(ValueError) as _raised_82:
            validate_alignment("Hello", raw, self.audio, aligner_sample_rate=16000)
        self.assertTrue(('audio bounds' in str(_raised_82.exception)), str(_raised_82.exception))


    def test_text_offsets_use_utf16_code_units_for_unity_strings(self):
        raw = {"sample_rate": 24000, "words": [
            {"word": "Hi", "start_sample": 0, "end_sample": 1000}
        ]}
        result = validate_alignment("😀 Hi", raw, self.audio)
        self.assertEqual(result["words"][0]["textStartUtf16"], 3)
        self.assertEqual(result["words"][0]["textLengthUtf16"], 2)

    def test_runner_forces_cpu_and_atomically_publishes_ready_timeline(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            cli = root / "audiocpp_cli.exe"
            model = root / "aligner.gguf"
            wav_path = root / "request-17.wav"
            cli.write_bytes(b"mock cli")
            model.write_bytes(b"mock pinned model")
            with wave.open(str(wav_path), "wb") as audio:
                audio.setnchannels(1)
                audio.setsampwidth(2)
                audio.setframerate(24000)
                audio.writeframes(b"\x00\x00" * 24000)
            model_sha = hashlib.sha256(model.read_bytes()).hexdigest()
            seen = {}

            def fake_run(command, **_kwargs):
                seen["command"] = command
                words_path = Path(command[command.index("--words-out") + 1])
                words_path.write_text(json.dumps([
                    {"word": "Hi", "start_sample": 0, "end_sample": 8000}
                ]), encoding="utf-8")
                return CompletedProcess(command, 0, "", "")

            with patch("tools.BreezeTtsTrial.align_words.subprocess.run", side_effect=fake_run):
                result = run_alignment(str(cli), str(model), str(wav_path), "Hi.",
                                       expected_model_sha256=model_sha)
            command = seen["command"]
            self.assertEqual(command[command.index("--backend") + 1], "cpu")
            self.assertEqual(command[command.index("--threads") + 1], "1")
            self.assertEqual(result["provenance"]["requestId"], "request-17")
            self.assertEqual(result["provenance"]["phonemeAlignment"], False)
            self.assertEqual(result["alignerSampleRate"], 16000)
            self.assertEqual(result["alignerSampleRateSource"], "pinned-model-config")
            self.assertTrue((root / "request-17.alignment.json").is_file())
            with self.assertRaises(FileExistsError):
                run_alignment(str(cli), str(model), str(wav_path), "Hi.",
                               expected_model_sha256=model_sha)


if __name__ == "__main__":
    unittest.main()
