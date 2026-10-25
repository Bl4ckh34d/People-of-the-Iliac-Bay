from __future__ import annotations

import hashlib
import io
import array
import json
import math
import sys
import statistics
import tempfile
import unittest
import wave
from pathlib import Path
from unittest.mock import patch

import npc_voice_profiles
from npc_voice_profiles import (
    ANCHOR_TEXT,
    DIMENSIONS,
    NpcVoiceProfileStore,
    VoiceProfileError,
)


def normalized_test_wav(seconds: int = 4) -> bytes:
    stream = io.BytesIO()
    samples = array.array("h", (
        round(5000 * math.sin(2 * math.pi * 440 * frame / 24000))
        for frame in range(24000 * seconds)
    ))
    if sys.byteorder != "little":
        samples.byteswap()
    with wave.open(stream, "wb") as output:
        output.setnchannels(1)
        output.setsampwidth(2)
        output.setframerate(24000)
        output.writeframes(samples.tobytes())
    return stream.getvalue()


def constant_wav(sample: int, seconds: int = 4) -> bytes:
    stream = io.BytesIO()
    samples = array.array("h", [sample]) * (24000 * seconds)
    if sys.byteorder != "little":
        samples.byteswap()
    with wave.open(stream, "wb") as output:
        output.setnchannels(1)
        output.setsampwidth(2)
        output.setframerate(24000)
        output.writeframes(samples.tobytes())
    return stream.getvalue()


def anchor_metadata(profile, request_id: str = "anchor-request-001") -> dict:
    return {
        "requestId": request_id,
        "profileId": profile.profile_id,
        "seed": profile.seed,
        "instruction": profile.instruction,
        "guidanceScale": 4.0,
        "text": ANCHOR_TEXT,
        "referenceAbsent": True,
        "voiceRef": None,
        "referenceText": None,
    }


class NpcVoiceProfileStoreTests(unittest.TestCase):
    def test_race_and_native_gender_are_frozen_into_voice_identity_instruction(self) -> None:
        cases = (
            ("Breton", "female", "Adult female Breton", "Southern English"),
            ("Imperial", "male", "Adult male Imperial", "cultivated British English"),
            ("Nord", "female", "Adult female Nord", "Scandinavian-influenced English"),
            ("Redguard", "male", "Adult male Redguard", "Kenyan English"),
            ("Dunmer", "female", "Adult female Dunmer", "Scottish English"),
            ("Altmer", "male", "Adult male Altmer", "cultivated British English"),
            ("Bosmer", "female", "Adult female Bosmer", "Welsh English"),
            ("Orc", "female", "Adult female Orc", "Northern English"),
            ("Khajiit", "male", "Adult male Khajiit", "Arabic-influenced English"),
            ("Argonian", "female", "Adult female Argonian", "Brazilian Portuguese-influenced English"),
        )
        with tempfile.TemporaryDirectory() as directory:
            store = NpcVoiceProfileStore(Path(directory))
            for index, (race, gender, subject, accent) in enumerate(cases):
                with self.subTest(race=race, gender=gender):
                    profile = store.get_or_create("race-person-{}".format(index), gender,
                        age="adult", race=race)
                    self.assertEqual(race, profile.race)
                    self.assertTrue(profile.instruction.startswith(subject + "."))
                    self.assertIn(accent, profile.instruction)
                    self.assertEqual(race, profile.to_coordinator_profile()["race"])

    def test_nonhuman_race_prompts_preserve_intelligibility_without_added_vocal_events(self) -> None:
        cases = (
            ("Orc", "gritty", "throat", "no separate grunts"),
            ("Argonian", "Dry sibilants", "croaking resonance", "no separate hisses"),
            ("Khajiit", "purr-like", "raspy voiced resonance", "no meows"),
        )
        with tempfile.TemporaryDirectory() as directory:
            store = NpcVoiceProfileStore(Path(directory))
            for race, texture_a, texture_b, exclude in cases:
                with self.subTest(race=race):
                    profile = store.get_or_create("nonhuman-" + race, "female", age="adult", race=race)
                    self.assertIn(texture_a, profile.instruction)
                    self.assertIn(texture_b, profile.instruction)
                    self.assertIn(exclude, profile.instruction)
                    self.assertIn("intelligible", profile.instruction)

    def test_schema_one_migration_keeps_seed_and_descriptors_but_retires_old_anchor(self) -> None:
        person_id = "legacy-npc-argonian"
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            store = NpcVoiceProfileStore(root)
            person_key = hashlib.sha256(person_id.encode("utf-8")).hexdigest()
            legacy_profile_path = root / "per-npc" / "v1" / "profiles" / (person_key + ".json")
            legacy_anchor_path = root / "per-npc" / "v1" / "anchors" / person_key / "neutral.wav"
            latent_z = {dimension: 0.0 for dimension in DIMENSIONS}
            attributes, instruction = npc_voice_profiles._legacy_instruction(latent_z, "female", "adult")
            wav_bytes = normalized_test_wav()
            legacy_anchor_path.parent.mkdir(parents=True)
            legacy_anchor_path.write_bytes(wav_bytes)
            legacy = {
                "schemaVersion": 1,
                "profileId": "npc-voice-v1-" + person_key,
                "personIdSha256": person_key,
                "perceivedGender": "female",
                "agePresentation": "adult",
                "seed": 314159,
                "latentZ": latent_z,
                "attributes": attributes,
                "stableInstruction": instruction,
                "guidanceScale": 4.0,
                "anchor": {
                    "voiceRef": (Path("per-npc") / "v1" / "anchors" / person_key / "neutral.wav").as_posix(),
                    "referenceText": ANCHOR_TEXT,
                    "sha256": hashlib.sha256(wav_bytes).hexdigest(),
                    "byteCount": len(wav_bytes),
                },
            }
            legacy["recordSha256"] = npc_voice_profiles._profile_checksum(legacy)
            legacy_profile_path.parent.mkdir(parents=True)
            legacy_profile_path.write_text(json.dumps(legacy), encoding="utf-8")

            migrated = store.get_or_create(person_id, "female", age="unknown", race="Argonian")

            self.assertTrue(migrated.legacy_migrated)
            self.assertEqual(314159, migrated.seed)
            self.assertEqual(latent_z, migrated.latent_z)
            self.assertEqual("adult", migrated.age, "unknown current age must not erase trusted saved presentation")
            self.assertEqual("Argonian", migrated.race)
            self.assertTrue(migrated.anchor_required, "legacy Southern English anchor must not be reused for Argonian")
            self.assertTrue(legacy_profile_path.is_file())
            self.assertEqual(wav_bytes, legacy_anchor_path.read_bytes())
            saved = json.loads(migrated.profile_path.read_text(encoding="utf-8"))
            self.assertEqual(legacy["profileId"], saved["migratedFromProfileId"])
            self.assertEqual(migrated, store.get_or_create(person_id, "female", age="unknown", race="Argonian"))

    def test_distinct_person_ids_get_independent_persistent_profiles(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            store = NpcVoiceProfileStore(Path(directory))
            # Portrait data is intentionally absent from the identity API: two
            # people who happen to share one portrait still get separate voices.
            first = store.get_or_create("faction:guard:17", "female", age="adult")
            second = store.get_or_create("faction:guard:18", "female", age="adult")

            self.assertNotEqual(first.profile_id, second.profile_id)
            self.assertNotEqual(first.seed, second.seed)
            self.assertNotEqual(first.latent_z, second.latent_z)
            self.assertEqual(first, NpcVoiceProfileStore(Path(directory)).get_or_create(
                "faction:guard:17", "female", age="adult"))
            self.assertTrue(1 <= first.seed <= 2**31 - 1)

    def test_identity_metadata_mismatch_and_corrupt_profile_fail_closed(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            store = NpcVoiceProfileStore(Path(directory))
            original = store.get_or_create("person-1", "female", age="child")
            with self.assertRaises(VoiceProfileError) as _raised_177:
                store.get_or_create("person-1", "male", age="child")
            self.assertTrue(('gender' in str(_raised_177.exception)), str(_raised_177.exception))
            with self.assertRaises(VoiceProfileError) as _raised_179:
                store.get_or_create("person-1", "female", age="adult")
            self.assertTrue(('age' in str(_raised_179.exception)), str(_raised_179.exception))
            with self.assertRaises(VoiceProfileError) as _raised_181:
                store.get_or_create("person-1", "female", age="child", race="Argonian")
            self.assertTrue(('race' in str(_raised_181.exception)), str(_raised_181.exception))

            original.profile_path.write_text("{broken", encoding="utf-8")
            with self.assertRaises(VoiceProfileError) as _raised_185:
                store.get_or_create("person-1", "female", age="child")
            self.assertTrue(('corrupt' in str(_raised_185.exception)) or ('invalid' in str(_raised_185.exception)), str(_raised_185.exception))

    def test_profile_latents_follow_seven_independent_standard_normals(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            store = NpcVoiceProfileStore(Path(directory))
            rows = [store.get_or_create("distribution-person-{}".format(index), "unknown")
                    for index in range(500)]

            self.assertEqual(set(DIMENSIONS), set(rows[0].latent_z))
            for dimension in DIMENSIONS:
                values = [row.latent_z[dimension] for row in rows]
                self.assertLess(abs(statistics.mean(values)), 0.20, dimension)
                self.assertGreater(statistics.pstdev(values), 0.78, dimension)
                self.assertLess(statistics.pstdev(values), 1.22, dimension)
                tail_count = sum(abs(value) > 2 for value in values)
                self.assertGreaterEqual(tail_count, 3, dimension)
                self.assertLessEqual(tail_count, 50, dimension)
            self.assertTrue(all(set(row.latent_z) == set(DIMENSIONS) for row in rows))

    def test_gallery_cutoffs_and_gender_age_wording_are_frozen_in_profile(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            profile = NpcVoiceProfileStore(Path(directory)).get_or_create(
                "known-female", "female", age="adult", race="Breton")
            self.assertIn("clearly audible Southern English", profile.instruction)
            self.assertIn("Adult female Breton", profile.instruction)
            self.assertIn("Keep feminine identity", profile.instruction)
            self.assertLessEqual(len(profile.instruction), 512)
            self.assertEqual(4.0, profile.guidance_scale)
            self.assertIn(profile.attributes["pitch"], {
                "a distinctly deep contralto register", "a lower alto register",
                "a natural middle female register", "an upper mezzo-soprano register",
                "a distinctly high natural soprano register"})

            child = NpcVoiceProfileStore(Path(directory)).get_or_create(
                "known-child", "male", age="child", race="Breton")
            self.assertIn("Young male Breton", child.instruction)
            self.assertNotIn("adult man", child.instruction)

            unknown = NpcVoiceProfileStore(Path(directory)).get_or_create(
                "unknown-presentation", "unknown", age="unknown")
            self.assertIn("Person.", unknown.instruction)
            self.assertNotIn("British English", unknown.instruction)

    def test_extreme_descriptors_still_fit_coordinator_instruction_limit(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            store = NpcVoiceProfileStore(Path(directory))
            with patch("npc_voice_profiles.random.SystemRandom") as normal_rng:
                normal_rng.return_value.gauss.return_value = 10.0
                for race in (*npc_voice_profiles._RACE_NAMES.values(), "Unknown"):
                    for gender in ("female", "male", "unknown"):
                        for age in ("adult", "child", "unknown"):
                            profile = store.get_or_create(
                                "extreme-{}-{}-{}".format(race, gender, age), gender, age, race)
                            self.assertTrue(all(value == 10.0 for value in profile.latent_z.values()))
                            self.assertLessEqual(len(profile.instruction), 512)

    def test_gallery_descriptor_boundaries_are_exact(self) -> None:
        expected_pitch = (
            "a distinctly deep contralto register",
            "a lower alto register",
            "a natural middle female register",
            "an upper mezzo-soprano register",
            "a distinctly high natural soprano register",
        )
        cases = (
            (-2.000001, expected_pitch[0]),
            (-2.0, expected_pitch[1]),
            (-1.0, expected_pitch[2]),
            (1.0, expected_pitch[2]),
            (1.000001, expected_pitch[3]),
            (2.0, expected_pitch[3]),
            (2.000001, expected_pitch[4]),
        )
        with tempfile.TemporaryDirectory() as directory:
            store = NpcVoiceProfileStore(Path(directory))
            with patch("npc_voice_profiles.random.SystemRandom") as normal_rng:
                for index, (pitch_latent, expected) in enumerate(cases):
                    normal_rng.return_value.gauss.side_effect = [pitch_latent] + [0.0] * 6
                    profile = store.get_or_create("boundary-{}".format(index), "female", age="adult")
                    self.assertEqual(expected, profile.attributes["pitch"])

    def test_each_npc_anchor_is_own_reference_and_reused_with_verified_hash(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            voice_root = Path(directory)
            store = NpcVoiceProfileStore(voice_root)
            first = store.get_or_create("npc-one", "female")
            second = store.get_or_create("npc-two", "female")
            self.assertTrue(first.anchor_required)
            self.assertTrue(second.anchor_required)

            wav_bytes = normalized_test_wav()
            anchored = store.record_neutral_anchor(first, wav_bytes, anchor_metadata(first))
            self.assertFalse(anchored.anchor_required)
            self.assertEqual(hashlib.sha256(wav_bytes).hexdigest(), anchored.anchor_sha256)
            self.assertEqual(ANCHOR_TEXT, anchored.reference_text)
            self.assertEqual(wav_bytes, anchored.anchor_path.read_bytes())

            profile = anchored.to_coordinator_profile()
            self.assertEqual(anchored.instruction, profile["instruction"])
            self.assertEqual(anchored.seed, profile["seed"])
            self.assertEqual("female", profile["perceivedGender"])
            self.assertEqual(ANCHOR_TEXT, profile["referenceText"])
            self.assertEqual(anchored.anchor_sha256, profile["voiceReferenceSha256"])
            self.assertTrue((voice_root / profile["voiceRef"]).is_file())

            # Mood and personality are deliberately not accepted by profile lookup;
            # any delivery overlay stays downstream and cannot alter this identity.
            reloaded = NpcVoiceProfileStore(voice_root).get_or_create("npc-one", "female")
            self.assertEqual(anchored, reloaded)
            self.assertEqual(second.profile_id, NpcVoiceProfileStore(voice_root).get_or_create(
                "npc-two", "female").profile_id)
            self.assertTrue(NpcVoiceProfileStore(voice_root).get_or_create(
                "npc-two", "female").anchor_required)

            anchored.anchor_path.write_bytes(wav_bytes + b"tampered")
            with self.assertRaises(VoiceProfileError) as _raised_301:
                NpcVoiceProfileStore(voice_root).get_or_create("npc-one", "female")
            self.assertTrue(('hash' in str(_raised_301.exception)) or ('changed' in str(_raised_301.exception)) or ('corrupt' in str(_raised_301.exception)), str(_raised_301.exception))

    def test_anchor_requires_exact_neutral_design_request_and_valid_pcm_wav(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            store = NpcVoiceProfileStore(Path(directory))
            profile = store.get_or_create("anchor-owner", "unknown")
            wav_bytes = normalized_test_wav()

            bad_metadata = anchor_metadata(profile)
            bad_metadata["text"] = "A different utterance"
            with self.assertRaises(VoiceProfileError) as _raised_312:
                store.record_neutral_anchor(profile, wav_bytes, bad_metadata)
            self.assertTrue(('text' in str(_raised_312.exception)) or ('anchor' in str(_raised_312.exception)), str(_raised_312.exception))

            bad_metadata = anchor_metadata(profile)
            bad_metadata["voiceRef"] = "another-npc.wav"
            with self.assertRaises(VoiceProfileError) as _raised_317:
                store.record_neutral_anchor(profile, wav_bytes, bad_metadata)
            self.assertTrue(('reference' in str(_raised_317.exception)) or ('voiceRef' in str(_raised_317.exception)), str(_raised_317.exception))

            bad_metadata = anchor_metadata(profile)
            bad_metadata["seed"] += 1
            with self.assertRaises(VoiceProfileError) as _raised_322:
                store.record_neutral_anchor(profile, wav_bytes, bad_metadata)
            self.assertTrue(('seed' in str(_raised_322.exception)) or ('metadata' in str(_raised_322.exception)), str(_raised_322.exception))

            with self.assertRaises(VoiceProfileError) as _raised_325:
                store.record_neutral_anchor(profile, normalized_test_wav(seconds=2),
                                            anchor_metadata(profile))
            self.assertTrue(('duration' in str(_raised_325.exception)) or ('3' in str(_raised_325.exception)) or ('20' in str(_raised_325.exception)), str(_raised_325.exception))
            with self.assertRaises(VoiceProfileError) as _raised_328:
                store.record_neutral_anchor(profile, b"not a wav", anchor_metadata(profile))
            self.assertTrue(('24' in str(_raised_328.exception)) or ('sample' in str(_raised_328.exception)) or ('WAV' in str(_raised_328.exception)) or ('mono' in str(_raised_328.exception)), str(_raised_328.exception))

            with self.assertRaises(VoiceProfileError) as _raised_331:
                store.record_neutral_anchor(profile, constant_wav(0), anchor_metadata(profile))
            self.assertTrue(('silent' in str(_raised_331.exception)) or ('RMS' in str(_raised_331.exception)) or ('signal' in str(_raised_331.exception)) or ('quiet' in str(_raised_331.exception)), str(_raised_331.exception))
            with self.assertRaises(VoiceProfileError) as _raised_333:
                store.record_neutral_anchor(profile, constant_wav(32767), anchor_metadata(profile))
            self.assertTrue(('peak' in str(_raised_333.exception)) or ('clipp' in str(_raised_333.exception)) or ('normalized' in str(_raised_333.exception)) or ('dBFS' in str(_raised_333.exception)), str(_raised_333.exception))
            with self.assertRaises(VoiceProfileError) as _raised_335:
                store.record_neutral_anchor(profile, constant_wav(10000), anchor_metadata(profile))
            self.assertTrue(('RMS' in str(_raised_335.exception)) or ('normalized' in str(_raised_335.exception)) or ('loud' in str(_raised_335.exception)), str(_raised_335.exception))

    def test_anchor_cannot_be_recorded_for_another_person_or_overwritten(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            store = NpcVoiceProfileStore(Path(directory))
            first = store.get_or_create("anchor-first", "female")
            second = store.get_or_create("anchor-second", "female")
            wav_bytes = normalized_test_wav()

            with self.assertRaises(VoiceProfileError) as _raised_345:
                store.record_neutral_anchor(first, wav_bytes, anchor_metadata(second))
            self.assertTrue(('profile' in str(_raised_345.exception)) or ('identity' in str(_raised_345.exception)) or ('person' in str(_raised_345.exception)), str(_raised_345.exception))

            store.record_neutral_anchor(first, wav_bytes, anchor_metadata(first))
            different_audio = normalized_test_wav(seconds=5)
            with self.assertRaises(VoiceProfileError) as _raised_350:
                store.record_neutral_anchor(first, different_audio, anchor_metadata(first, "anchor-request-002"))
            self.assertTrue(('already' in str(_raised_350.exception)) or ('overwrite' in str(_raised_350.exception)) or ('anchor' in str(_raised_350.exception)), str(_raised_350.exception))


if __name__ == "__main__":
    unittest.main()
