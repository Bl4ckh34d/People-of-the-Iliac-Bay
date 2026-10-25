"""Stable, locally persisted Breeze voice identities keyed by native personId.

Identity is frozen when first observed. Mood and personality are intentionally
absent from this API; callers layer temporary delivery direction downstream.
Gender and age must come from trusted native metadata, never portrait inference.
"""

from __future__ import annotations

import array
import hashlib
import io
import json
import math
import os
import random
from protocol_text import valid_sha256
import secrets
import sys
import tempfile
import threading
import wave
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Mapping, Optional


SCHEMA_VERSION = 2
LEGACY_SCHEMA_VERSION = 1
GUIDANCE_SCALE = 4.0
ANCHOR_TEXT = "Good day. I have been in this town for many years. What brings you here today?"
SAMPLE_RATE = 24000
MIN_ANCHOR_SECONDS = 3.0
MAX_ANCHOR_SECONDS = 20.0
MIN_ANCHOR_ACTIVE_RMS_DBFS = -28.0
MAX_ANCHOR_ACTIVE_RMS_DBFS = -17.0
MAX_ANCHOR_PEAK_DBFS = -3.0
DIMENSIONS = (
    "pitch",
    "brightness",
    "resonance",
    "roughness",
    "breathiness",
    "vocalFry",
    "pace",
)
DESCRIPTOR_THRESHOLDS = (-2.0, -1.0, 1.0, 2.0)
_PERSON_ID_MAX = 240
_MAX_ANCHOR_BYTES = int(SAMPLE_RATE * 2 * MAX_ANCHOR_SECONDS) + 4096

_RACE_GUIDANCE = {
    "Breton": ("a clearly audible Southern English accent", "Keep pronunciation natural and unforced."),
    "Imperial": ("a clearly audible cultivated British English accent",
        "Use non-rhotic pronunciation, precise consonants, and measured cadence; stay refined, not theatrical."),
    "Nord": ("a clearly audible Scandinavian-influenced English accent", "Retain Scandinavian vowels and cadence."),
    "Redguard": ("a clearly audible Kenyan English accent", "Retain Kenyan vowels and cadence."),
    "Dunmer": ("a clearly audible Scottish English accent", "Retain Scottish vowels and cadence."),
    "Altmer": ("a clearly audible cultivated British English accent",
        "Use non-rhotic pronunciation, precise consonants, and measured cadence; stay refined, not theatrical."),
    "Bosmer": ("a clearly audible Welsh English accent", "Retain Welsh vowels and cadence."),
    "Orc": ("a clearly audible gritty Northern English accent",
        "Use coarse throat and chest resonance; keep words intelligible; no separate grunts."),
    "Khajiit": ("a clearly audible Arabic-influenced English accent",
        "Use subtle feline purr-like or raspy voiced resonance; keep words intelligible; no meows."),
    "Argonian": ("a clearly audible Brazilian Portuguese-influenced English accent",
        "Dry sibilants; croaking resonance; keep words intelligible; no separate hisses."),
    "Unknown": ("an unspecified English accent", "Do not invent a regional accent."),
}
_RACE_NAMES = {name.casefold(): name for name in _RACE_GUIDANCE if name != "Unknown"}
_NONHUMAN_RACES = {"Orc", "Khajiit", "Argonian"}
_PROMPT_DESCRIPTOR_LABELS = {
    "pitch": {
        "a distinctly deep contralto register": "deep contralto",
        "a lower alto register": "lower alto",
        "a natural middle female register": "middle female",
        "an upper mezzo-soprano register": "upper mezzo-soprano",
        "a distinctly high natural soprano register": "high soprano",
        "a distinctly deep bass register": "deep bass",
        "a lower baritone register": "lower baritone",
        "a natural middle male register": "middle male",
        "an upper tenor register": "upper tenor",
        "a distinctly high natural countertenor register": "high countertenor",
        "a distinctly low natural speaking register": "low speaking",
        "a lower speaking register": "lower speaking",
        "a balanced middle speaking register": "middle speaking",
        "an upper speaking register": "upper speaking",
        "a distinctly high natural speaking register": "high speaking",
    },
    "brightness": {
        "dark and mellow": "dark mellow", "warm and slightly dark": "warm, slightly dark",
        "balanced, warm and clear": "balanced, warm, clear", "light and bright": "light, bright",
        "very bright and ringing": "bright, ringing",
    },
    "resonance": {
        "full and strongly chest-resonant": "full, chest-resonant",
        "rounded and fairly full": "rounded, fairly full",
        "with balanced natural resonance": "balanced natural resonance",
        "light and forward-resonant": "light, forward-resonant",
        "slender and lightly nasal": "slender, lightly nasal",
    },
    "roughness": {
        "exceptionally smooth": "exceptionally smooth", "smooth and clear": "smooth, clear",
        "mostly clear with a little natural texture": "clear, lightly textured",
        "noticeably husky": "husky", "distinctly gravelly but still natural": "gravelly, natural",
    },
    "breathiness": {
        "firm and dry with very little breathiness": "firm, dry",
        "clear with little breathiness": "clear, low breathiness",
        "with balanced natural breath support": "balanced breath support",
        "with a softly airy edge": "softly airy", "distinctly breathy but clearly articulated": "breathy, articulated",
    },
    "vocalFry": {
        "without audible vocal fry": "no vocal fry", "with little habitual vocal fry": "little vocal fry",
        "with minimal habitual creak": "minimal creak",
        "with occasional audible creak at phrase endings": "occasional phrase-end creak",
        "with a pronounced creaky edge on low phrase endings": "low phrase-end creak",
    },
    "pace": {
        "a distinctly slow but natural conversational pace": "slow, natural conversation",
        "a slightly slower than average conversational pace": "slightly slow conversation",
        "a normal unhurried conversational pace": "unhurried conversation",
        "a slightly brisk conversational pace": "slightly brisk conversation",
        "a distinctly rapid but clearly articulated conversational pace": "rapid, articulated conversation",
    },
}

_FEMALE_DESCRIPTORS = {
    "pitch": (
        "a distinctly deep contralto register",
        "a lower alto register",
        "a natural middle female register",
        "an upper mezzo-soprano register",
        "a distinctly high natural soprano register",
    ),
    "brightness": (
        "dark and mellow",
        "warm and slightly dark",
        "balanced, warm and clear",
        "light and bright",
        "very bright and ringing",
    ),
    "resonance": (
        "full and strongly chest-resonant",
        "rounded and fairly full",
        "with balanced natural resonance",
        "light and forward-resonant",
        "slender and lightly nasal",
    ),
    "roughness": (
        "exceptionally smooth",
        "smooth and clear",
        "mostly clear with a little natural texture",
        "noticeably husky",
        "distinctly gravelly but still natural",
    ),
    "breathiness": (
        "firm and dry with very little breathiness",
        "clear with little breathiness",
        "with balanced natural breath support",
        "with a softly airy edge",
        "distinctly breathy but clearly articulated",
    ),
    "vocalFry": (
        "without audible vocal fry",
        "with little habitual vocal fry",
        "with minimal habitual creak",
        "with occasional audible creak at phrase endings",
        "with a pronounced creaky edge on low phrase endings",
    ),
    "pace": (
        "a distinctly slow but natural conversational pace",
        "a slightly slower than average conversational pace",
        "a normal unhurried conversational pace",
        "a slightly brisk conversational pace",
        "a distinctly rapid but clearly articulated conversational pace",
    ),
}

_MALE_PITCH = (
    "a distinctly deep bass register",
    "a lower baritone register",
    "a natural middle male register",
    "an upper tenor register",
    "a distinctly high natural countertenor register",
)
_UNKNOWN_PITCH = (
    "a distinctly low natural speaking register",
    "a lower speaking register",
    "a balanced middle speaking register",
    "an upper speaking register",
    "a distinctly high natural speaking register",
)
_GENERIC_DESCRIPTORS = {
    key: _FEMALE_DESCRIPTORS[key]
    for key in ("brightness", "resonance", "roughness", "breathiness", "vocalFry", "pace")
}
_STORE_LOCK = threading.RLock()
_SEED_CACHE_LOCK = threading.Lock()
_SEED_CACHE: dict[str, set[int]] = {}


class VoiceProfileError(ValueError):
    """Stored voice identity or neutral-anchor data failed validation."""


def _canonical_json_bytes(value: Any) -> bytes:
    return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":")).encode("utf-8")


def _profile_checksum(value: Mapping[str, Any]) -> str:
    unsigned = {key: item for key, item in value.items() if key != "recordSha256"}
    return hashlib.sha256(_canonical_json_bytes(unsigned)).hexdigest()


def _identifier(value: Any, name: str, maximum: int = 128) -> str:
    if not isinstance(value, str) or not value.strip() or len(value) > maximum:
        raise VoiceProfileError("{} must be a non-empty bounded identifier".format(name))
    if any(ord(char) < 32 or 0xD800 <= ord(char) <= 0xDFFF for char in value):
        raise VoiceProfileError("{} contains unsupported control or surrogate characters".format(name))
    return value


def _band(z: float, values: tuple[str, ...]) -> str:
    if z < DESCRIPTOR_THRESHOLDS[0]:
        index = 0
    elif z < DESCRIPTOR_THRESHOLDS[1]:
        index = 1
    elif z <= DESCRIPTOR_THRESHOLDS[2]:
        index = 2
    elif z <= DESCRIPTOR_THRESHOLDS[3]:
        index = 3
    else:
        index = 4
    return values[index]


def _descriptors(latent_z: Mapping[str, float], perceived_gender: str) -> dict[str, str]:
    descriptions = dict(_GENERIC_DESCRIPTORS)
    pitch_values = {
        "female": _FEMALE_DESCRIPTORS["pitch"],
        "male": _MALE_PITCH,
        "unknown": _UNKNOWN_PITCH,
    }[perceived_gender]
    descriptions["pitch"] = pitch_values
    return {dimension: _band(float(latent_z[dimension]), descriptions[dimension])
            for dimension in DIMENSIONS}


def _canonical_race(race: Any) -> str:
    if not isinstance(race, str) or not race.strip() or len(race) > 64:
        raise VoiceProfileError("native race must be a supported Elder Scrolls race or Unknown")
    value = race.strip()
    if value.casefold() == "unknown":
        return "Unknown"
    canonical = _RACE_NAMES.get(value.casefold())
    if canonical is None:
        raise VoiceProfileError("unsupported native race {!r}; refusing generic voice-profile reuse".format(value))
    return canonical


def _presentation(perceived_gender: str, age: str, race: str) -> tuple[str, str, str]:
    gender_word = {"female": "female", "male": "male", "unknown": "person"}[perceived_gender]
    race_suffix = " " + race if race != "Unknown" else ""
    if age == "child":
        subject = "Young {}{}".format(gender_word, race_suffix)
        pronoun = {"female": "female", "male": "male", "unknown": "child"}[perceived_gender]
    elif age == "adult" or race in _NONHUMAN_RACES:
        subject = "Adult {}{}".format(gender_word, race_suffix)
        pronoun = gender_word
    elif perceived_gender in {"female", "male"}:
        subject = "{}{}".format(gender_word.capitalize(), race_suffix)
        pronoun = gender_word
    else:
        subject = "Person{}".format(race_suffix)
        pronoun = "person"
    identity_gender = "feminine" if perceived_gender == "female" else (
        "masculine" if perceived_gender == "male" else "natural")
    return subject, identity_gender, pronoun


def _legacy_instruction(latent_z: Mapping[str, float], perceived_gender: str, age: str) -> tuple[dict[str, str], str]:
    attributes = _descriptors(latent_z, perceived_gender)
    if perceived_gender == "female":
        subject, pronoun = {"adult": "An adult woman", "child": "A young girl", "unknown": "A woman"}[age], "Her"
    elif perceived_gender == "male":
        subject, pronoun = {"adult": "An adult man", "child": "A young boy", "unknown": "A man"}[age], "His"
    else:
        subject, pronoun = {"adult": "An adult person", "child": "A child", "unknown": "A person"}[age], "Their"
    instruction = (
        "{} speaking British English with a neutral southern English accent. "
        "{} voice has {}. Timbre: {}, {}; {}, {}, {}. "
        "Use {}. Maintain this voice identity. "
        "Speak naturally without caricature, shouting, or added vocal events."
    ).format(subject, pronoun, attributes["pitch"], attributes["brightness"],
        attributes["resonance"], attributes["roughness"], attributes["breathiness"],
        attributes["vocalFry"], attributes["pace"])
    return attributes, instruction


def _instruction(latent_z: Mapping[str, float], perceived_gender: str, age: str, race: str) -> tuple[dict[str, str], str]:
    attributes = _descriptors(latent_z, perceived_gender)
    race = _canonical_race(race)
    subject, identity_gender, _ = _presentation(perceived_gender, age, race)
    accent, texture = _RACE_GUIDANCE[race]
    prompt_attributes = {
        key: _PROMPT_DESCRIPTOR_LABELS[key][value]
        for key, value in attributes.items()
    }
    instruction = (
        "{}. Speak English with {}. {} Voice: {}; {}; {}; {}; {}; {}; {}. "
        "Keep {} identity, accent, and exact words. No shouting or added vocal events."
    ).format(
        subject,
        accent,
        texture,
        prompt_attributes["pitch"],
        prompt_attributes["brightness"],
        prompt_attributes["resonance"],
        prompt_attributes["roughness"],
        prompt_attributes["breathiness"],
        prompt_attributes["vocalFry"],
        prompt_attributes["pace"],
        identity_gender,
    )
    if len(instruction) > 512:
        raise VoiceProfileError("generated stable voice instruction exceeds coordinator limit")
    return attributes, instruction


def _validate_wav(data: bytes) -> tuple[int, float]:
    if not isinstance(data, bytes) or not data or len(data) > _MAX_ANCHOR_BYTES:
        raise VoiceProfileError("neutral anchor WAV must be non-empty and within the 20-second size bound")
    try:
        with wave.open(io.BytesIO(data), "rb") as wav:
            channels = wav.getnchannels()
            width = wav.getsampwidth()
            rate = wav.getframerate()
            frames = wav.getnframes()
            compression = wav.getcomptype()
            duration = frames / rate if rate else 0.0
            expected_data_bytes = frames * channels * width
            pcm_frames = wav.readframes(frames)
    except (wave.Error, EOFError, OSError, ZeroDivisionError) as exc:
        raise VoiceProfileError("neutral anchor must be a readable PCM WAV") from exc
    if compression != "NONE" or channels != 1 or width != 2 or rate != SAMPLE_RATE:
        raise VoiceProfileError("neutral anchor must be uncompressed mono PCM16 at 24 kHz")
    if frames <= 0 or expected_data_bytes != frames * 2 or len(pcm_frames) != expected_data_bytes:
        raise VoiceProfileError("neutral anchor WAV has invalid PCM frame data")
    if not math.isfinite(duration) or not MIN_ANCHOR_SECONDS <= duration <= MAX_ANCHOR_SECONDS:
        raise VoiceProfileError("neutral anchor duration must be between 3 and 20 seconds")
    samples = array.array("h")
    samples.frombytes(pcm_frames)
    if sys.byteorder != "little":
        samples.byteswap()
    peak = max((abs(sample) for sample in samples), default=0) / 32768.0
    gate = 10.0 ** (-50.0 / 20.0)
    active_sum = 0.0
    active_count = 0
    window_samples = max(1, rate // 100)
    for start in range(0, len(samples), window_samples):
        window = samples[start:start + window_samples]
        total = sum((sample / 32768.0) ** 2 for sample in window)
        rms = math.sqrt(total / len(window)) if window else 0.0
        if rms >= gate:
            active_sum += total
            active_count += len(window)
    active_rms = math.sqrt(active_sum / active_count) if active_count else 0.0
    peak_ceiling = 10.0 ** (MAX_ANCHOR_PEAK_DBFS / 20.0)
    if peak > peak_ceiling + 1.0 / 32768.0:
        raise VoiceProfileError("neutral anchor exceeds the normalized -3 dBFS peak ceiling")
    if active_rms < 10.0 ** (MIN_ANCHOR_ACTIVE_RMS_DBFS / 20.0):
        raise VoiceProfileError("neutral anchor is silent or too quiet; active RMS is below -28 dBFS")
    if active_rms > 10.0 ** (MAX_ANCHOR_ACTIVE_RMS_DBFS / 20.0):
        raise VoiceProfileError("neutral anchor active RMS is above the normalized -17 dBFS upper bound")
    return frames, duration


def _validate_anchor_metadata(metadata: Any, profile: "NpcVoiceProfile") -> dict[str, Any]:
    expected_keys = {
        "requestId", "profileId", "seed", "instruction", "guidanceScale",
        "text", "referenceAbsent", "voiceRef", "referenceText",
    }
    if not isinstance(metadata, dict) or set(metadata) != expected_keys:
        raise VoiceProfileError("neutral anchor request metadata has an invalid shape")
    _identifier(metadata.get("requestId"), "requestId")
    if metadata["profileId"] != profile.profile_id:
        raise VoiceProfileError("neutral anchor profileId does not match the owning person identity")
    if isinstance(metadata["seed"], bool) or metadata["seed"] != profile.seed:
        raise VoiceProfileError("neutral anchor seed does not match the frozen profile")
    if metadata["instruction"] != profile.instruction:
        raise VoiceProfileError("neutral anchor instruction does not match the frozen profile")
    if isinstance(metadata["guidanceScale"], bool) or metadata["guidanceScale"] != GUIDANCE_SCALE:
        raise VoiceProfileError("neutral anchor requires fixed guidance scale 4.0")
    if metadata["text"] != ANCHOR_TEXT:
        raise VoiceProfileError("neutral anchor text differs from the fixed gallery phrase")
    if metadata["referenceAbsent"] is not True or metadata["voiceRef"] is not None or metadata["referenceText"] is not None:
        raise VoiceProfileError("neutral anchor design request must be created without a voice reference")
    return dict(metadata)


@dataclass(frozen=True)
class NpcVoiceProfile:
    """One immutable identity record, optionally bound to its own neutral WAV."""

    person_id_sha256: str
    profile_id: str
    perceived_gender: str
    age: str
    race: str
    seed: int
    latent_z: Mapping[str, float]
    attributes: Mapping[str, str]
    instruction: str
    voice_root: Path
    profile_path: Path
    anchor_path: Optional[Path] = None
    anchor_sha256: Optional[str] = None
    reference_text: Optional[str] = None
    anchor_request_id: Optional[str] = None
    guidance_scale: float = GUIDANCE_SCALE
    legacy_migrated: bool = False

    @property
    def profileId(self) -> str:  # Keep bridge-style spelling easy for adapters.
        return self.profile_id

    @property
    def anchor_required(self) -> bool:
        return self.anchor_path is None

    def to_coordinator_profile(self) -> dict[str, Any]:
        result: dict[str, Any] = {
            "instruction": self.instruction,
            "seed": self.seed,
            "perceivedGender": self.perceived_gender,
            "agePresentation": self.age,
            "race": self.race,
            "legacyProfileMigrated": self.legacy_migrated,
        }
        if self.anchor_path is not None:
            relative = self.anchor_path.resolve().relative_to(self.voice_root.resolve()).as_posix()
            result.update({
                "voiceRef": relative,
                "referenceText": self.reference_text,
                "voiceReferenceSha256": self.anchor_sha256,
            })
        return result


class NpcVoiceProfileStore:
    """Versioned, atomic profile store beneath a caller-provided voice root."""

    def __init__(self, voice_root: Path | str):
        self.voice_root = Path(voice_root).expanduser().resolve()
        self.store_root = self.voice_root / "per-npc" / "v2"
        self.legacy_store_root = self.voice_root / "per-npc" / "v1"
        self.profiles_root = self.store_root / "profiles"
        self.anchors_root = self.store_root / "anchors"

    def _legacy_profile_path(self, person_key: str) -> Path:
        return self.legacy_store_root / "profiles" / (person_key + ".json")

    def _paths(self, person_key: str) -> tuple[Path, Path]:
        return (
            self.profiles_root / (person_key + ".json"),
            self.anchors_root / person_key / "neutral.wav",
        )

    def _person_key(self, person_id: str) -> str:
        validated = _identifier(person_id, "personId", _PERSON_ID_MAX)
        return hashlib.sha256(validated.encode("utf-8")).hexdigest()

    def get_or_create(self, person_id: str, perceived_gender: str, age: str = "unknown",
                      race: str = "Unknown") -> NpcVoiceProfile:
        if not isinstance(perceived_gender, str) or perceived_gender not in {"female", "male", "unknown"}:
            raise VoiceProfileError("perceived gender must be female, male, or unknown")
        if not isinstance(age, str) or age not in {"adult", "child", "unknown"}:
            raise VoiceProfileError("age presentation must be adult, child, or unknown")
        race = _canonical_race(race)
        person_key = self._person_key(person_id)
        profile_path, anchor_path = self._paths(person_key)
        with _STORE_LOCK:
            if profile_path.exists():
                profile = self._load_profile(profile_path, person_key)
                self._check_metadata(profile, perceived_gender, age, race)
                return profile

            if anchor_path.exists():
                raise VoiceProfileError("orphan neutral anchor exists without a saved profile; refusing reroll")
            legacy_path = self._legacy_profile_path(person_key)
            used_seeds = self._used_seeds()
            migrated_from = None
            if legacy_path.exists():
                legacy = self._load_legacy_profile(legacy_path, person_key)
                if legacy["perceivedGender"] != perceived_gender:
                    raise VoiceProfileError("saved legacy profile gender conflicts with native metadata")
                if legacy["agePresentation"] != age:
                    if age == "unknown":
                        age = legacy["agePresentation"]
                    else:
                        raise VoiceProfileError("saved legacy profile age conflicts with native metadata")
                latent_z = dict(legacy["latentZ"])
                seed = legacy["seed"]
                migrated_from = legacy["profileId"]
            else:
                latent_z = {dimension: random.SystemRandom().gauss(0.0, 1.0)
                            for dimension in DIMENSIONS}
                seed = self._new_seed(used_seeds)
            attributes, instruction = _instruction(latent_z, perceived_gender, age, race)
            profile_id = "npc-voice-v2-" + person_key
            document = {
                "schemaVersion": SCHEMA_VERSION,
                "profileId": profile_id,
                "personIdSha256": person_key,
                "perceivedGender": perceived_gender,
                "agePresentation": age,
                "race": race,
                "seed": seed,
                "latentZ": latent_z,
                "attributes": attributes,
                "stableInstruction": instruction,
                "guidanceScale": GUIDANCE_SCALE,
                "migratedFromProfileId": migrated_from,
                "anchor": None,
            }
            document["recordSha256"] = _profile_checksum(document)
            self.profiles_root.mkdir(parents=True, exist_ok=True)
            try:
                self._write_json(document, profile_path, replace=False)
            except FileExistsError:
                profile = self._load_profile(profile_path, person_key)
                self._check_metadata(profile, perceived_gender, age, race)
                return profile
            used_seeds.add(seed)
            return self._load_profile(profile_path, person_key)

    def record_neutral_anchor(
        self,
        profile: NpcVoiceProfile,
        wav_bytes: bytes,
        request_metadata: dict[str, Any],
    ) -> NpcVoiceProfile:
        if not isinstance(profile, NpcVoiceProfile) or profile.voice_root.resolve() != self.voice_root:
            raise VoiceProfileError("neutral anchor profile belongs to another voice store")
        person_key = profile.person_id_sha256
        profile_path, anchor_path = self._paths(person_key)
        with _STORE_LOCK:
            current = self._load_profile(profile_path, person_key)
            if current.profile_id != profile.profile_id or current.seed != profile.seed:
                raise VoiceProfileError("neutral anchor profile identity is stale or mismatched")
            if current.instruction != profile.instruction or current.perceived_gender != profile.perceived_gender:
                raise VoiceProfileError("neutral anchor profile identity metadata changed")
            metadata = _validate_anchor_metadata(request_metadata, current)
            frames, duration = _validate_wav(wav_bytes)
            wav_hash = hashlib.sha256(wav_bytes).hexdigest()

            if current.anchor_path is not None:
                if current.anchor_sha256 == wav_hash and current.anchor_request_id == metadata["requestId"]:
                    return current
                raise VoiceProfileError("neutral anchor already exists; refusing overwrite or reroll")
            if anchor_path.exists():
                raise VoiceProfileError("orphan neutral anchor exists; refusing overwrite or replacement")

            try:
                self._write_bytes(wav_bytes, anchor_path, replace=False)
            except FileExistsError as exc:
                raise VoiceProfileError("neutral anchor already exists; refusing overwrite or replacement") from exc
            document = self._read_document(profile_path, person_key)
            document["anchor"] = {
                "voiceRef": anchor_path.relative_to(self.voice_root).as_posix(),
                "referenceText": ANCHOR_TEXT,
                "sha256": wav_hash,
                "byteCount": len(wav_bytes),
                "sampleFrames": frames,
                "durationSeconds": duration,
                "request": metadata,
            }
            document["recordSha256"] = _profile_checksum(document)
            self._write_json(document, profile_path, replace=True)
            return self._load_profile(profile_path, person_key)

    def _check_metadata(self, profile: NpcVoiceProfile, perceived_gender: str, age: str, race: str) -> None:
        if profile.perceived_gender != perceived_gender:
            raise VoiceProfileError(
                "saved profile gender is {}; native metadata says {}; refusing automatic reroll".format(
                    profile.perceived_gender, perceived_gender))
        if profile.age != age and age != "unknown":
            raise VoiceProfileError(
                "saved profile age presentation is {}; native metadata says {}; refusing automatic reroll".format(
                    profile.age, age))
        if profile.race != race:
            raise VoiceProfileError(
                "saved profile race is {}; native metadata says {}; refusing automatic reroll".format(
                    profile.race, race))

    def _used_seeds(self) -> set[int]:
        key = str(self.store_root)
        with _SEED_CACHE_LOCK:
            seeds = _SEED_CACHE.get(key)
            if seeds is None:
                seeds = set()
                for profiles_root, loader in (
                        (self.legacy_store_root / "profiles", self._load_legacy_profile),
                        (self.profiles_root, self._load_profile)):
                    root_seeds = set()
                    if profiles_root.exists():
                        for path in sorted(profiles_root.glob("*.json")):
                            person_key = path.stem
                            if not valid_sha256(person_key, lowercase=True):
                                raise VoiceProfileError("profile store contains an unsafe profile filename")
                            profile = loader(path, person_key)
                            seed = profile["seed"] if isinstance(profile, dict) else profile.seed
                            if seed in root_seeds:
                                raise VoiceProfileError("saved NPC voice profiles repeat a seed")
                            root_seeds.add(seed)
                            seeds.add(seed)
                _SEED_CACHE[key] = seeds
            return seeds

    @staticmethod
    def _new_seed(used: set[int]) -> int:
        for _ in range(100):
            candidate = secrets.randbelow(2**31 - 1) + 1
            if candidate not in used:
                return candidate
        raise VoiceProfileError("could not allocate a distinct positive 31-bit NPC voice seed")

    def _read_document(self, path: Path, expected_person_key: str) -> dict[str, Any]:
        try:
            with path.open("r", encoding="utf-8") as stream:
                document = json.load(stream)
        except (OSError, UnicodeError, json.JSONDecodeError, TypeError) as exc:
            raise VoiceProfileError("saved NPC voice profile is corrupt or unreadable") from exc
        self._validate_document(document, expected_person_key)
        return document

    def _load_legacy_profile(self, path: Path, expected_person_key: str) -> dict[str, Any]:
        try:
            with path.open("r", encoding="utf-8") as stream:
                document = json.load(stream)
        except (OSError, UnicodeError, json.JSONDecodeError, TypeError) as exc:
            raise VoiceProfileError("saved legacy NPC voice profile is corrupt or unreadable") from exc
        expected_keys = {
            "schemaVersion", "profileId", "personIdSha256", "perceivedGender",
            "agePresentation", "seed", "latentZ", "attributes", "stableInstruction",
            "guidanceScale", "anchor", "recordSha256",
        }
        if not isinstance(document, dict) or set(document) != expected_keys:
            raise VoiceProfileError("saved legacy NPC voice profile has an invalid shape")
        if (isinstance(document.get("schemaVersion"), bool) or
                document["schemaVersion"] != LEGACY_SCHEMA_VERSION or
                document.get("personIdSha256") != expected_person_key or
                document.get("profileId") != "npc-voice-v1-" + expected_person_key):
            raise VoiceProfileError("saved legacy NPC voice profile has an invalid version or identity")
        gender, age = document.get("perceivedGender"), document.get("agePresentation")
        if gender not in {"female", "male", "unknown"} or age not in {"adult", "child", "unknown"}:
            raise VoiceProfileError("saved legacy NPC voice profile has invalid gender or age metadata")
        seed = document.get("seed")
        if isinstance(seed, bool) or not isinstance(seed, int) or not 1 <= seed <= 2**31 - 1:
            raise VoiceProfileError("saved legacy NPC voice profile has an invalid positive 31-bit seed")
        latent_z = document.get("latentZ")
        if (not isinstance(latent_z, dict) or set(latent_z) != set(DIMENSIONS) or
                any(isinstance(value, bool) or not isinstance(value, (int, float)) or
                    not math.isfinite(value) for value in latent_z.values())):
            raise VoiceProfileError("saved legacy NPC voice profile has invalid Gaussian latent values")
        attributes, instruction = _legacy_instruction(latent_z, gender, age)
        if document.get("attributes") != attributes or document.get("stableInstruction") != instruction:
            raise VoiceProfileError("saved legacy NPC voice profile does not match its frozen latent values")
        if isinstance(document.get("guidanceScale"), bool) or document.get("guidanceScale") != GUIDANCE_SCALE:
            raise VoiceProfileError("saved legacy NPC voice profile has an unsupported guidance scale")
        if document.get("anchor") is not None and not isinstance(document.get("anchor"), dict):
            raise VoiceProfileError("saved legacy neutral anchor metadata has an invalid shape")
        checksum = document.get("recordSha256")
        if (not isinstance(checksum, str) or not valid_sha256(checksum, lowercase=True) or
                checksum != _profile_checksum(document)):
            raise VoiceProfileError("saved legacy NPC voice profile checksum is invalid")
        return document

    def _validate_document(self, document: Any, expected_person_key: str) -> None:
        expected_keys = {
            "schemaVersion", "profileId", "personIdSha256", "perceivedGender",
            "agePresentation", "race", "seed", "latentZ", "attributes", "stableInstruction",
            "guidanceScale", "migratedFromProfileId", "anchor", "recordSha256",
        }
        if not isinstance(document, dict) or set(document) != expected_keys:
            raise VoiceProfileError("saved NPC voice profile has an invalid shape")
        if (isinstance(document.get("schemaVersion"), bool) or document["schemaVersion"] != SCHEMA_VERSION or
                document.get("personIdSha256") != expected_person_key or
                document.get("profileId") != "npc-voice-v2-" + expected_person_key):
            raise VoiceProfileError("saved NPC voice profile has an invalid version or identity")
        gender, age = document.get("perceivedGender"), document.get("agePresentation")
        if gender not in {"female", "male", "unknown"} or age not in {"adult", "child", "unknown"}:
            raise VoiceProfileError("saved NPC voice profile has invalid trusted gender or age metadata")
        race = _canonical_race(document.get("race"))
        if race != document.get("race"):
            raise VoiceProfileError("saved NPC voice profile race is not canonical")
        migrated_from = document.get("migratedFromProfileId")
        if migrated_from is not None and migrated_from != "npc-voice-v1-" + expected_person_key:
            raise VoiceProfileError("saved NPC voice profile has an invalid legacy source identity")
        seed = document.get("seed")
        if isinstance(seed, bool) or not isinstance(seed, int) or not 1 <= seed <= 2**31 - 1:
            raise VoiceProfileError("saved NPC voice profile has an invalid positive 31-bit seed")
        latent_z = document.get("latentZ")
        if (not isinstance(latent_z, dict) or set(latent_z) != set(DIMENSIONS) or
                any(isinstance(value, bool) or not isinstance(value, (int, float)) or
                    not math.isfinite(value) for value in latent_z.values())):
            raise VoiceProfileError("saved NPC voice profile has invalid Gaussian latent values")
        attributes, instruction = _instruction(latent_z, gender, age, race)
        if document.get("attributes") != attributes or document.get("stableInstruction") != instruction:
            raise VoiceProfileError("saved NPC voice profile descriptors do not match its frozen latent values")
        if isinstance(document.get("guidanceScale"), bool) or document.get("guidanceScale") != GUIDANCE_SCALE:
            raise VoiceProfileError("saved NPC voice profile has an unsupported guidance scale")
        checksum = document.get("recordSha256")
        if not isinstance(checksum, str) or not valid_sha256(checksum, lowercase=True) or checksum != _profile_checksum(document):
            raise VoiceProfileError("saved NPC voice profile checksum is invalid")
        self._validate_saved_anchor(document.get("anchor"), document, expected_person_key)

    def _validate_saved_anchor(self, anchor: Any, document: Mapping[str, Any], person_key: str) -> None:
        if anchor is None:
            return
        expected_keys = {
            "voiceRef", "referenceText", "sha256", "byteCount", "sampleFrames",
            "durationSeconds", "request",
        }
        if not isinstance(anchor, dict) or set(anchor) != expected_keys:
            raise VoiceProfileError("saved neutral anchor metadata has an invalid shape")
        expected_rel = (Path("per-npc") / "v2" / "anchors" / person_key / "neutral.wav").as_posix()
        if anchor.get("voiceRef") != expected_rel or anchor.get("referenceText") != ANCHOR_TEXT:
            raise VoiceProfileError("saved neutral anchor binding does not match its owning NPC")
        digest = anchor.get("sha256")
        if not isinstance(digest, str) or not valid_sha256(digest, lowercase=True):
            raise VoiceProfileError("saved neutral anchor SHA-256 is invalid")
        wav_path = self.voice_root / expected_rel
        if not wav_path.is_file() or wav_path.resolve().parent != wav_path.parent.resolve():
            raise VoiceProfileError("saved neutral anchor file is missing or escapes its profile directory")
        try:
            wav_bytes = wav_path.read_bytes()
        except OSError as exc:
            raise VoiceProfileError("saved neutral anchor file is unreadable") from exc
        if hashlib.sha256(wav_bytes).hexdigest() != digest:
            raise VoiceProfileError("saved neutral anchor WAV hash changed")
        frames, duration = _validate_wav(wav_bytes)
        if (isinstance(anchor.get("byteCount"), bool) or anchor.get("byteCount") != len(wav_bytes) or
                isinstance(anchor.get("sampleFrames"), bool) or anchor.get("sampleFrames") != frames or
                isinstance(anchor.get("durationSeconds"), bool) or anchor.get("durationSeconds") != duration):
            raise VoiceProfileError("saved neutral anchor WAV metadata does not match its file")
        request = anchor.get("request")
        expected_request = {
            "requestId", "profileId", "seed", "instruction", "guidanceScale",
            "text", "referenceAbsent", "voiceRef", "referenceText",
        }
        if not isinstance(request, dict) or set(request) != expected_request:
            raise VoiceProfileError("saved neutral anchor request metadata has an invalid shape")
        _identifier(request.get("requestId"), "requestId")
        if (request.get("profileId") != document["profileId"] or
                isinstance(request.get("seed"), bool) or request.get("seed") != document["seed"] or
                request.get("instruction") != document["stableInstruction"] or
                isinstance(request.get("guidanceScale"), bool) or request.get("guidanceScale") != GUIDANCE_SCALE or
                request.get("text") != ANCHOR_TEXT or
                request.get("referenceAbsent") is not True or request.get("voiceRef") is not None or
                request.get("referenceText") is not None):
            raise VoiceProfileError("saved neutral anchor request is not bound to its frozen profile")

    def _load_profile(self, path: Path, expected_person_key: str) -> NpcVoiceProfile:
        document = self._read_document(path, expected_person_key)
        _, anchor_path = self._paths(expected_person_key)
        anchor = document["anchor"]
        if anchor is not None:
            resolved = anchor_path.resolve()
            if os.path.commonpath((str(resolved), str(self.voice_root))) != str(self.voice_root):
                raise VoiceProfileError("saved neutral anchor path escapes voice root")
            return NpcVoiceProfile(
                person_id_sha256=expected_person_key,
                profile_id=document["profileId"],
                perceived_gender=document["perceivedGender"],
                age=document["agePresentation"],
                race=document["race"],
                seed=document["seed"],
                latent_z=dict(document["latentZ"]),
                attributes=dict(document["attributes"]),
                instruction=document["stableInstruction"],
                voice_root=self.voice_root,
                profile_path=path,
                anchor_path=resolved,
                anchor_sha256=anchor["sha256"],
                reference_text=anchor["referenceText"],
                anchor_request_id=anchor["request"]["requestId"],
                guidance_scale=document["guidanceScale"],
                legacy_migrated=document["migratedFromProfileId"] is not None,
            )
        if anchor_path.exists():
            raise VoiceProfileError("orphan neutral anchor exists without profile binding; refusing reroll")
        return NpcVoiceProfile(
            person_id_sha256=expected_person_key,
            profile_id=document["profileId"],
            perceived_gender=document["perceivedGender"],
            age=document["agePresentation"],
            race=document["race"],
            seed=document["seed"],
            latent_z=dict(document["latentZ"]),
            attributes=dict(document["attributes"]),
            instruction=document["stableInstruction"],
            voice_root=self.voice_root,
            profile_path=path,
            guidance_scale=document["guidanceScale"],
            legacy_migrated=document["migratedFromProfileId"] is not None,
        )

    @staticmethod
    def _write_json(value: dict[str, Any], path: Path, *, replace: bool) -> None:
        path.parent.mkdir(parents=True, exist_ok=True)
        payload = json.dumps(value, ensure_ascii=False, sort_keys=True, indent=2) + "\n"
        NpcVoiceProfileStore._write_bytes(payload.encode("utf-8"), path, replace=replace)

    @staticmethod
    def _write_bytes(payload: bytes, path: Path, *, replace: bool) -> None:
        path.parent.mkdir(parents=True, exist_ok=True)
        handle, temporary_name = tempfile.mkstemp(prefix=path.name + ".", suffix=".tmp", dir=str(path.parent))
        temporary = Path(temporary_name)
        try:
            with os.fdopen(handle, "wb") as stream:
                stream.write(payload)
                stream.flush()
                os.fsync(stream.fileno())
            if replace:
                os.replace(temporary, path)
            else:
                # Hard-link creation publishes complete bytes and cannot replace
                # a profile/anchor already committed by another caller.
                os.link(temporary, path)
        finally:
            try:
                temporary.unlink()
            except FileNotFoundError:
                pass

