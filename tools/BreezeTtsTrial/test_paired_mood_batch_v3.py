from __future__ import annotations

import hashlib
import json
import sys
import tempfile
import unittest
import wave
from pathlib import Path
from unittest.mock import patch


HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import paired_mood_batch_v3 as batch  # noqa: E402


FIXED_TEXT = "Good day, traveller. The road ahead is long, but you are welcome to rest here for a while."
MOOD_ORDER = ("neutral", "happy", "angry", "sad", "afraid", "guarded")
VOICE_IDS = [
    "breton-female", "breton-male", "imperial-female", "imperial-male",
    "nord-female", "nord-male", "redguard-female", "redguard-male",
    "dunmer-female", "dunmer-male", "altmer-female", "altmer-male",
    "bosmer-female", "bosmer-male", "orc-female", "orc-male",
    "khajiit-female", "khajiit-male", "argonian-female", "argonian-male",
]


def make_wav(path: Path, seconds: int = 2) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(path), "wb") as output:
        output.setnchannels(1)
        output.setsampwidth(2)
        output.setframerate(24000)
        output.writeframes(b"\0\0" * 24000 * seconds)


def fixture_corpus(root: Path) -> tuple[Path, dict]:
    reference_root = root / "references"
    voices = []
    for index, voice_id in enumerate(VOICE_IDS):
        reference = reference_root / (voice_id + ".wav")
        make_wav(reference)
        reference_hash = hashlib.sha256(reference.read_bytes()).hexdigest()
        neutral_clause = (
            "Keep the delivery natural and neutral."
            if voice_id == "argonian-female"
            else "Keep delivery natural and neutral."
        )
        source_instruction = (
            "An adult speaker with stable accent and texture. "
            + neutral_clause
            + " Preserve the reference speaker timbre."
        )
        moods = []
        for mood_id in MOOD_ORDER:
            if mood_id == "neutral":
                clause = neutral_clause
            else:
                article = "the " if neutral_clause.startswith("Keep the ") else ""
                clause = "Keep {}delivery natural, yet clearly {}.".format(
                    article, "fearful" if mood_id == "afraid" else mood_id
                )
            instruction = source_instruction
            if mood_id != "neutral":
                instruction = source_instruction.replace(neutral_clause, clause, 1)
            moods.append({
                "id": mood_id,
                "label": mood_id.title(),
                "instruction": instruction,
                "sourceDeliveryClause": neutral_clause,
                "deliveryClause": clause,
                "seed": 71001 + index,
                "referenceWav": str(reference),
                "referenceWavSha256": reference_hash,
                "pairId": voice_id + ":" + mood_id,
                "breezeOutputRelativePath": "{}/{}.wav".format(voice_id, mood_id),
                "breezeUrl": "breeze-paired-v3/outputs/{}/{}.wav".format(voice_id, mood_id),
                "mingOutputRelativePath": "{}/{}.wav".format(voice_id, mood_id),
                "mingUrl": "ming3-audition/outputs/prompt-paired-v3/{}/{}.wav".format(voice_id, mood_id),
            })
        voices.append({
            "id": voice_id,
            "label": voice_id.replace("-", " ").title(),
            "race": voice_id.split("-")[0].title(),
            "gender": voice_id.rsplit("-", 1)[1],
            "accent": "English accent",
            "seed": 71001 + index,
            "referenceSampleId": "race-" + voice_id,
            "referenceWav": str(reference),
            "referenceWavSha256": reference_hash,
            "fixedText": FIXED_TEXT,
            "sourceInstruction": source_instruction,
            "sourceSampleId": "accent-{}-v2".format(voice_id),
            "historicalBreezeV2": {
                "outputWav": str(root / (voice_id + "-v2.wav")),
                "url": "../{}-v2.wav".format(voice_id),
                "wavSha256": "a" * 64,
            },
            "moods": moods,
        })
    corpus = {
        "schemaVersion": 3,
        "corpusId": "race-voice-paired-v3",
        "status": "frozen",
        "fixedText": FIXED_TEXT,
        "moodOrder": list(MOOD_ORDER),
        "summary": {
            "voiceCount": 20,
            "moodCount": 6,
            "pairCount": 120,
            "breezeJobs": 120,
            "mingJobs": 120,
            "totalModelJobs": 240,
        },
        "comparisonContract": {"engineDifferences": {"breezeGuidanceScale": 4.0}},
        "sources": {},
        "smokePairs": [
            {"voiceId": "orc-female", "moodId": "happy"},
            {"voiceId": "argonian-female", "moodId": "angry"},
        ],
        "voices": voices,
    }
    corpus_path = root / "corpus-v3.json"
    corpus_path.write_text(json.dumps(corpus, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return corpus_path, corpus


class PairedMoodBatchV3Tests(unittest.TestCase):
    def test_validates_all_pairs_and_builds_exact_breeze_request(self):
        with tempfile.TemporaryDirectory() as temporary:
            corpus_path, corpus = fixture_corpus(Path(temporary))
            loaded, corpus_sha = batch.load_corpus(corpus_path)

            self.assertEqual(corpus, loaded)
            self.assertEqual(hashlib.sha256(corpus_path.read_bytes()).hexdigest(), corpus_sha)
            self.assertEqual(120, len(batch.build_jobs(loaded)))

            voice = next(row for row in loaded["voices"] if row["id"] == "orc-female")
            mood = next(row for row in voice["moods"] if row["id"] == "happy")
            payload = batch.build_request_payload(voice, mood)
            self.assertEqual(FIXED_TEXT, payload["input"])
            self.assertEqual(mood["instruction"], payload["options"]["instruct"])
            self.assertEqual(71015, payload["options"]["seed"])
            self.assertEqual(4.0, payload["options"]["guidance_scale"])
            self.assertEqual(FIXED_TEXT, payload["reference_text"])
            self.assertEqual(str(Path(voice["referenceWav"]).resolve()), payload["voice_ref"]["path"])

    def test_smoke_run_writes_two_records_and_resume_never_rerenders_completed_wavs(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            corpus_path, _ = fixture_corpus(root)
            output_root = root / "breeze-paired-v3"
            calls = []

            def fake_synthesize(endpoint, model, text, instruction, reference_wav,
                                reference_text, output_wav, timeout_seconds, *, seed,
                                guidance_scale):
                calls.append((Path(output_wav), seed, instruction))
                make_wav(Path(output_wav))
                return {"synthesisMs": 1000.0, "firstAudioMs": 100.0,
                        "audioDeltaCount": 2, "serverTiming": {"ttft_ms": 90}}

            cases = ["orc-female:happy", "argonian-female:angry"]
            first = batch.run_batch(
                corpus_path, output_root, cases=cases, synthesize_fn=fake_synthesize
            )
            second = batch.run_batch(
                corpus_path, output_root, cases=cases, synthesize_fn=fake_synthesize
            )

            self.assertEqual(2, len(calls))
            self.assertEqual("partial", first["status"])
            self.assertEqual(120, first["summary"]["expected"])
            self.assertEqual(2, second["summary"]["completed"])
            self.assertEqual(118, second["summary"]["unattempted"])
            self.assertTrue((output_root / "manifest.json").is_file())
            for voice_id, mood_id in (("orc-female", "happy"), ("argonian-female", "angry")):
                row = batch.find_mood(second, voice_id, mood_id)
                self.assertEqual("completed", row["status"])
                self.assertEqual(64, len(row["wavSha256"]))
                self.assertEqual("breeze-paired-v3/outputs/{}/{}.wav".format(voice_id, mood_id), row["url"])
                self.assertEqual(FIXED_TEXT, row["requestPayload"]["reference_text"])

    def test_resume_rejects_a_different_corpus_hash_before_synthesis(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            corpus_path, corpus = fixture_corpus(root)
            output_root = root / "breeze-paired-v3"
            calls = []

            def fake_synthesize(*args, **kwargs):
                calls.append(args)
                make_wav(Path(args[6]))
                return {"synthesisMs": 1.0, "firstAudioMs": 0.5}

            batch.run_batch(
                corpus_path, output_root, cases=["orc-female:happy"],
                synthesize_fn=fake_synthesize,
            )
            corpus["corpusId"] = "changed"
            corpus_path.write_text(json.dumps(corpus), encoding="utf-8")

            with self.assertRaises(batch.BatchError) as _raised_204:
                batch.run_batch(
                corpus_path, output_root, cases=["orc-female:happy"],
                    synthesize_fn=fake_synthesize,
                )
            self.assertTrue(('corpus SHA-256 differs' in str(_raised_204.exception)), str(_raised_204.exception))
            self.assertEqual(1, len(calls))

    def test_resume_recovers_hash_valid_wav_after_manifest_write_interruption(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            corpus_path, _ = fixture_corpus(root)
            output_root = root / "breeze-paired-v3"
            calls = []

            def fake_synthesize(*args, **kwargs):
                calls.append(args)
                make_wav(Path(args[6]))
                return {"synthesisMs": 1000.0, "firstAudioMs": 100.0}

            batch.run_batch(
                corpus_path, output_root, cases=["orc-female:happy"],
                synthesize_fn=fake_synthesize,
            )
            manifest_path = output_root / "manifest.json"
            manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
            row = batch.find_mood(manifest, "orc-female", "happy")
            row["status"] = "running"
            row["wavSha256"] = None
            row["wavBytes"] = None
            row["durationSeconds"] = None
            row["runtimeMetadata"] = None
            row["attempts"][-1]["status"] = "running"
            row["attempts"][-1].pop("finishedUtc", None)
            batch.atomic_write_json(manifest_path, manifest)

            recovered = batch.run_batch(
                corpus_path, output_root, cases=["orc-female:happy"],
                synthesize_fn=fake_synthesize,
            )

            row = batch.find_mood(recovered, "orc-female", "happy")
            self.assertEqual(1, len(calls))
            self.assertEqual("completed", row["status"])
            self.assertEqual("completed-recovered", row["attempts"][-1]["status"])
            self.assertTrue(any("Recovered complete WAV" in warning for warning in row["warnings"]))

    def test_atomic_manifest_write_retries_windows_sharing_errors(self):
        with tempfile.TemporaryDirectory() as temporary:
            target = Path(temporary) / "manifest.json"
            actual_replace = batch.os.replace
            calls = 0

            def flaky_replace(source, destination):
                nonlocal calls
                calls += 1
                if calls < 3:
                    raise PermissionError("sharing violation")
                actual_replace(source, destination)

            with patch.object(batch.os, "replace", side_effect=flaky_replace):
                batch.atomic_write_json(target, {"ok": True})

            self.assertEqual(3, calls)
            self.assertEqual({"ok": True}, json.loads(target.read_text(encoding="utf-8")))


if __name__ == "__main__":
    unittest.main(verbosity=2)
