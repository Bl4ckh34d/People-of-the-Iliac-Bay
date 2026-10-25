from __future__ import annotations

import json
import tempfile
import unittest
import wave
from pathlib import Path

from paired_nonhuman_batch_v4 import (
    EXPECTED_CORPUS_SHA256,
    BatchError,
    build_request_payload,
    load_corpus,
    run_batch,
)


REPO_ROOT = Path(__file__).resolve().parents[2]
CORPUS = REPO_ROOT / "work/ming3-tts-assessment-20261025/corpus-v4-nonhuman-paired.json"


def find_row(manifest: dict, voice_id: str, mood_id: str) -> dict:
    return next(v for v in manifest["voices"] if v["id"] == voice_id)["moods"][0]


class PairedNonhumanBatchV4Tests(unittest.TestCase):
    def test_loads_pinned_v4_and_builds_matched_breeze_payload(self) -> None:
        corpus, digest = load_corpus(CORPUS)
        self.assertEqual(digest, EXPECTED_CORPUS_SHA256)
        self.assertEqual(corpus["summary"]["pairCount"], 36)
        voice = corpus["voices"][0]
        mood = voice["moods"][1]
        payload = build_request_payload(voice, mood)
        self.assertEqual(payload["input"], voice["fixedText"])
        self.assertEqual(payload["options"]["instruct"], mood["instruction"])
        self.assertEqual(payload["options"]["seed"], mood["seed"])
        self.assertEqual(payload["options"]["guidance_scale"], 4.0)
        self.assertEqual(payload["reference_text"], voice["fixedText"])
        self.assertEqual(payload["voice_ref"]["path"], str(Path(voice["referenceWav"]).resolve()))

    def test_smoke_run_records_receipt_and_completed_pair_is_not_replayed(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            output_root = Path(temp) / "breeze-race-nonhuman-v4"
            calls = []

            def fake_synthesize(endpoint, model, text, instruction, reference_wav,
                                reference_text, output_wav, timeout, *, seed, guidance_scale):
                calls.append((text, instruction, reference_wav, reference_text, seed, guidance_scale))
                Path(output_wav).parent.mkdir(parents=True, exist_ok=True)
                with wave.open(output_wav, "wb") as wav:
                    wav.setnchannels(1)
                    wav.setsampwidth(2)
                    wav.setframerate(24000)
                    wav.writeframes(b"\x01\x00" * 24000)
                return {"synthesisMs": 500, "firstAudioMs": 100, "audioDeltaCount": 2}

            manifest = run_batch(
                CORPUS,
                output_root,
                cases=["orc-female:neutral"],
                synthesize_fn=fake_synthesize,
            )
            self.assertEqual(len(calls), 1)
            self.assertEqual(manifest["summary"]["completed"], 1)
            row = find_row(manifest, "orc-female", "neutral")
            self.assertEqual(row["status"], "completed")
            self.assertEqual(row["url"], "breeze-race-nonhuman-v4/outputs/orc-female/neutral.wav")
            saved = json.loads((output_root / "manifest.json").read_text(encoding="utf-8"))
            self.assertEqual(saved["corpusSha256"], EXPECTED_CORPUS_SHA256)

            resumed = run_batch(
                CORPUS,
                output_root,
                cases=["orc-female:neutral"],
                synthesize_fn=fake_synthesize,
            )
            self.assertEqual(len(calls), 1)
            self.assertEqual(resumed["summary"]["completed"], 1)

    def test_refuses_modified_corpus(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            changed = Path(temp) / "changed.json"
            raw = json.loads(CORPUS.read_text(encoding="utf-8"))
            raw["voices"][0]["accent"] = "American English"
            changed.write_text(json.dumps(raw), encoding="utf-8")
            with self.assertRaises(BatchError):
                load_corpus(changed)


if __name__ == "__main__":
    unittest.main()
