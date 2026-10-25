import json
import tempfile
import unittest
import wave
from pathlib import Path

try:
    from tools.BreezeTtsTrial.race_voice_batch import run_batch
except ModuleNotFoundError:
    from race_voice_batch import run_batch


class RaceVoiceBatchTests(unittest.TestCase):
    def _corpus(self, path):
        samples = [
            {
                "sampleId": "race-{}-{}".format(race, gender),
                "race": race,
                "genderPresentation": gender,
                "adultAgePresentation": "adult",
                "seed": 71001 + index,
                "voiceDesignInstruction": "Adult {} with a neutral Southern English accent.".format(gender),
            }
            for index, (race, gender) in enumerate([
                ("Breton", "female"), ("Breton", "male"),
                ("Imperial", "female"), ("Imperial", "male"),
                ("Nord", "female"), ("Nord", "male"),
                ("Redguard", "female"), ("Redguard", "male"),
                ("Dunmer", "female"), ("Dunmer", "male"),
                ("Altmer", "female"), ("Altmer", "male"),
                ("Bosmer", "female"), ("Bosmer", "male"),
                ("Orc", "female"), ("Orc", "male"),
                ("Khajiit", "female"), ("Khajiit", "male"),
                ("Argonian", "female"), ("Argonian", "male"),
            ])
        ]
        path.write_text(json.dumps({
            "schemaVersion": 1,
            "fixedText": "Good day, traveller.",
            "trialControls": {"guidance_scale": 4, "sameTextForEverySample": True,
                              "accent": "Southern English (identical across all samples; accent is not varied by race)."},
            "samples": samples,
        }), encoding="utf-8")

    def test_renders_sequentially_writes_metadata_and_grouped_non_autoplay_gallery(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            corpus = root / "corpus.json"
            output = root / "out"
            self._corpus(corpus)
            calls = []

            def fake_synthesize(endpoint, model, text, instruction, reference_wav,
                                reference_text, output_wav, timeout_seconds=600,
                                *, seed=None, guidance_scale=4.0):
                calls.append((text, instruction, reference_wav, reference_text, seed, guidance_scale))
                path = Path(output_wav)
                path.parent.mkdir(parents=True, exist_ok=True)
                with wave.open(str(path), "wb") as writer:
                    writer.setnchannels(1)
                    writer.setsampwidth(2)
                    writer.setframerate(24000)
                    writer.writeframes(b"\0\0" * 24)
                return {"outputWav": str(path), "pcmBytes": 48, "sampleRate": 24000,
                        "channels": 1, "synthesisMs": 100, "firstAudioMs": 20,
                        "audioDeltaCount": 2, "serverTiming": {"ttft_ms": 20}}

            manifest = run_batch(str(corpus), "http://127.0.0.1:8094", "breeze",
                                 str(output), synthesize_fn=fake_synthesize)

            self.assertEqual(len(calls), 20)
            self.assertTrue(all(call[0] == "Good day, traveller." for call in calls))
            self.assertTrue(all(call[2:4] == (None, None) for call in calls))
            self.assertEqual([call[4] for call in calls], list(range(71001, 71021)))
            self.assertTrue(all(call[5] == 4.0 for call in calls))
            self.assertEqual(manifest["summary"]["succeeded"], 20)
            self.assertTrue((output / "race_voice_batch.json").is_file())
            gallery = (output / "gallery.html").read_text(encoding="utf-8")
            self.assertEqual(gallery.count("<audio controls"), 20)
            self.assertNotIn("autoplay", gallery.lower())
            self.assertIn("<h2>Breton</h2>", gallery)

    def test_duplicate_or_unsafe_sample_ids_fail_before_any_call(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            corpus = root / "corpus.json"
            self._corpus(corpus)
            data = json.loads(corpus.read_text(encoding="utf-8"))
            data["samples"][1]["sampleId"] = data["samples"][0]["sampleId"]
            corpus.write_text(json.dumps(data), encoding="utf-8")
            calls = []
            with self.assertRaises(ValueError) as _raised_92:
                run_batch(str(corpus), "http://localhost:8094", "breeze", str(root / "out"),
                          synthesize_fn=lambda *args, **kwargs: calls.append(args))
            self.assertTrue(('unique' in str(_raised_92.exception)), str(_raised_92.exception))
            self.assertEqual(calls, [])

    def test_output_collision_fails_before_any_call(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            corpus = root / "corpus.json"
            self._corpus(corpus)
            output = root / "out"
            output.mkdir()
            (output / "race-breton-female.wav").write_bytes(b"preserve")
            calls = []
            with self.assertRaises(FileExistsError) as _raised_106:
                run_batch(str(corpus), "http://localhost:8094", "breeze", str(output),
                          synthesize_fn=lambda *args, **kwargs: calls.append(args))
            self.assertTrue(('refusing to overwrite' in str(_raised_106.exception)), str(_raised_106.exception))
            self.assertEqual(calls, [])

    def test_checked_in_race_corpus_runs_through_offline_fake_synth(self):
        corpus = Path(__file__).with_name("race_voice_corpus.json")
        with tempfile.TemporaryDirectory() as temp:
            calls = []

            def fake_synthesize(endpoint, model, text, instruction, reference_wav,
                                reference_text, output_wav, timeout_seconds=600,
                                *, seed=None, guidance_scale=4.0):
                calls.append(seed)
                path = Path(output_wav)
                with wave.open(str(path), "wb") as writer:
                    writer.setnchannels(1)
                    writer.setsampwidth(2)
                    writer.setframerate(24000)
                    writer.writeframes(b"\0\0" * 8)
                return {"synthesisMs": 1, "firstAudioMs": 0.5}

            manifest = run_batch(str(corpus), "http://127.0.0.1:8094", "breeze",
                                 temp, synthesize_fn=fake_synthesize)
            self.assertEqual(len(calls), 20)
            self.assertEqual(manifest["summary"]["succeeded"], 20)
            self.assertEqual({row["race"] for row in manifest["samples"]}, {
                "Breton", "Imperial", "Nord", "Redguard", "Dunmer",
                "Altmer", "Bosmer", "Orc", "Khajiit", "Argonian"})


if __name__ == "__main__":
    unittest.main()
