import hashlib
import json
import tempfile
import unittest
import wave
from pathlib import Path
from subprocess import CompletedProcess
from unittest.mock import patch

try:
    from tools.BreezeTtsTrial.rhubarb_visemes import run_rhubarb, validate_rhubarb
except ModuleNotFoundError:
    from rhubarb_visemes import run_rhubarb, validate_rhubarb


class RhubarbVisemeTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.audio_path = Path(self.temporary.name) / "speech.wav"
        with wave.open(str(self.audio_path), "wb") as stream:
            stream.setnchannels(1)
            stream.setsampwidth(2)
            stream.setframerate(24000)
            stream.writeframes(b"\x00\x00" * 24000)
        self.audio = {"sample_rate": 24000, "frames": 24000, "channels": 1}
        self.raw = {
            "metadata": {"soundFile": str(self.audio_path.resolve()), "duration": 1.0},
            "mouthCues": [
                {"start": 0.0, "end": 0.1, "value": "X"},
                {"start": 0.1, "end": 0.8, "value": "D"},
                {"start": 0.8, "end": 1.0, "value": "X"},
            ],
        }

    def test_converts_contiguous_valid_cues_to_wav_sample_clock(self):
        result = validate_rhubarb("Hello, world!", self.raw, self.audio,
                                  self.audio_path, "request-1")
        self.assertEqual(result["status"], "ready")
        self.assertEqual(result["timelineKind"], "recognizer-viseme-estimate")
        self.assertEqual(result["requestId"], "request-1")
        self.assertEqual(result["cues"][0], {"startSample": 0, "endSample": 2400, "shape": "X"})
        self.assertEqual(result["cues"][-1]["endSample"], 24000)
        self.assertEqual(result["provenance"]["dialogHintSha256"],
                         hashlib.sha256(b"Hello, world!").hexdigest())

    def test_rejects_cue_gaps_overlap_unknown_shape_and_uncovered_audio(self):
        raw = json.loads(json.dumps(self.raw))
        raw["mouthCues"][1]["start"] = 0.11
        with self.assertRaises(ValueError) as _raised_50:
            validate_rhubarb("Hi", raw, self.audio, self.audio_path, "request-1")
        self.assertTrue(('contiguous' in str(_raised_50.exception)), str(_raised_50.exception))
        raw = json.loads(json.dumps(self.raw))
        raw["mouthCues"][1]["value"] = "Z"
        with self.assertRaises(ValueError) as _raised_54:
            validate_rhubarb("Hi", raw, self.audio, self.audio_path, "request-1")
        self.assertTrue(('mouth shape' in str(_raised_54.exception)), str(_raised_54.exception))
        raw = json.loads(json.dumps(self.raw))
        raw["mouthCues"][-1]["end"] = 0.98
        raw["metadata"]["duration"] = 0.98
        with self.assertRaises(ValueError) as _raised_59:
            validate_rhubarb("Hi", raw, self.audio, self.audio_path, "request-1")
        self.assertTrue(('audio duration' in str(_raised_59.exception)), str(_raised_59.exception))

    def test_rejects_unmatched_audio_and_empty_dialog(self):
        raw = json.loads(json.dumps(self.raw))
        raw["metadata"]["soundFile"] = "other.wav"
        with self.assertRaises(ValueError) as _raised_65:
            validate_rhubarb("Hi", raw, self.audio, self.audio_path, "request-1")
        self.assertTrue(('audio path' in str(_raised_65.exception)), str(_raised_65.exception))
        with self.assertRaises(ValueError) as _raised_67:
            validate_rhubarb(" ", self.raw, self.audio, self.audio_path, "request-1")
        self.assertTrue(('dialog text' in str(_raised_67.exception)), str(_raised_67.exception))

    def test_runner_uses_one_cpu_thread_and_publishes_bound_output_atomically(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            cli = root / "rhubarb.exe"
            audio = root / "request-42.wav"
            cli.write_bytes(b"mock cli")
            with wave.open(str(audio), "wb") as stream:
                stream.setnchannels(1)
                stream.setsampwidth(2)
                stream.setframerate(24000)
                stream.writeframes(b"\x00\x00" * 24000)
            seen = {}

            def fake_run(command, **_kwargs):
                seen["command"] = command
                output = Path(command[command.index("--output") + 1])
                dialog = Path(command[command.index("--dialogFile") + 1])
                self.assertEqual(dialog.read_text(encoding="utf-8"), "Hello.")
                output.write_text(json.dumps({
                    "metadata": {"soundFile": str(audio.resolve()), "duration": 1.0},
                    "mouthCues": [
                        {"start": 0.0, "end": 0.1, "value": "X"},
                        {"start": 0.1, "end": 0.8, "value": "D"},
                        {"start": 0.8, "end": 1.0, "value": "X"},
                    ],
                }), encoding="utf-8")
                return CompletedProcess(command, 0, "", "")

            with patch("tools.BreezeTtsTrial.rhubarb_visemes.subprocess.run", side_effect=fake_run):
                result = run_rhubarb(str(cli), str(audio), "Hello.")
            command = seen["command"]
            self.assertEqual(command[command.index("--threads") + 1], "1")
            self.assertEqual(command[command.index("--recognizer") + 1], "pocketSphinx")
            self.assertEqual(command[-1], str(audio.resolve()))
            self.assertEqual(result["requestId"], "request-42")
            self.assertEqual(result["provenance"]["recognitionKind"], "recognizer-estimate")
            self.assertEqual(result["provenance"]["dialogHintSha256"],
                             hashlib.sha256(b"Hello.").hexdigest())
            output = root / "request-42.visemes.json"
            self.assertTrue(output.is_file())
            self.assertEqual(json.loads(output.read_text(encoding="utf-8"))["status"], "ready")
            with self.assertRaises(FileExistsError):
                run_rhubarb(str(cli), str(audio), "Hello.")


if __name__ == "__main__":
    unittest.main()
