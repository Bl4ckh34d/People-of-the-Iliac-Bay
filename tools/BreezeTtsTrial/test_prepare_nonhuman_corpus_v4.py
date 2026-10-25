from __future__ import annotations

import copy
import json
import tempfile
import unittest
from pathlib import Path

from prepare_nonhuman_corpus_v4 import (
    MOOD_ORDER,
    RACE_DIRECTIONS,
    CorpusError,
    build_corpus,
    validate_corpus,
)


REPO_ROOT = Path(__file__).resolve().parents[2]
V3_PATH = REPO_ROOT / "work/ming3-tts-assessment-20261025/corpus-v3.json"
EXPECTED_VOICE_IDS = (
    "orc-female", "orc-male", "argonian-female", "argonian-male",
    "khajiit-female", "khajiit-male",
)


class PrepareNonhumanCorpusV4Tests(unittest.TestCase):
    def test_builds_exact_six_voice_36_pair_subset_from_v3_pins(self) -> None:
        v3 = json.loads(V3_PATH.read_text(encoding="utf-8"))
        corpus = build_corpus(v3, V3_PATH.as_posix())
        self.assertEqual(corpus["schemaVersion"], 4)
        self.assertEqual(corpus["moodOrder"], list(MOOD_ORDER))
        self.assertEqual([voice["id"] for voice in corpus["voices"]], list(EXPECTED_VOICE_IDS))
        self.assertEqual(corpus["summary"]["pairCount"], 36)
        self.assertEqual(corpus["summary"]["totalModelJobs"], 72)
        self.assertEqual(corpus["sources"]["v3CorpusSha256"], __import__("hashlib").sha256(V3_PATH.read_bytes()).hexdigest())

        originals = {voice["id"]: voice for voice in v3["voices"]}
        for voice in corpus["voices"]:
            original = originals[voice["id"]]
            self.assertEqual(voice["accent"], original["accent"])
            self.assertEqual(voice["seed"], original["seed"])
            self.assertEqual(voice["referenceWav"], original["referenceWav"])
            self.assertEqual(voice["referenceWavSha256"], original["referenceWavSha256"])
            self.assertEqual(voice["fixedText"], original["fixedText"])
            self.assertEqual(voice["sourceInstruction"], original["sourceInstruction"])
            suffix = " " + RACE_DIRECTIONS[voice["race"]]
            for actual, source in zip(voice["moods"], original["moods"]):
                self.assertEqual(actual["id"], source["id"])
                self.assertEqual(actual["sourceInstruction"], source["instruction"])
                self.assertTrue(actual["raceNamedSourceInstruction"].startswith(
                    f"Adult {voice['gender']} {voice['race']} "
                ))
                self.assertEqual(actual["instruction"], actual["raceNamedSourceInstruction"] + suffix)
                self.assertEqual(actual["seed"], source["seed"])
                self.assertEqual(actual["referenceWavSha256"], source["referenceWavSha256"])
                self.assertEqual(actual["pairId"], f"{voice['id']}:{actual['id']}")
                self.assertEqual(actual["breezeOutputRelativePath"], f"{voice['id']}/{actual['id']}.wav")
                self.assertEqual(actual["breezeUrl"], f"breeze-race-nonhuman-v4/outputs/{voice['id']}/{actual['id']}.wav")

        validate_corpus(corpus, V3_PATH.parent.parent.parent / "breeze-tts-20261021")

    def test_rejects_wrong_source_v3_schema(self) -> None:
        v3 = json.loads(V3_PATH.read_text(encoding="utf-8"))
        changed = copy.deepcopy(v3)
        changed["schemaVersion"] = 2
        with self.assertRaises(CorpusError):
            build_corpus(changed, V3_PATH.as_posix())


if __name__ == "__main__":
    unittest.main()
