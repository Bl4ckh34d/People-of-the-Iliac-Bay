#!/usr/bin/env python3
"""Request-file coordinator for the pinned audio.cpp Breeze TTS trial.

Consumes the native bridge's finalized request unchanged, then publishes its
schema-1 WAV manifest. It derives prosody only from typed NPC state, never dialogue text.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
from protocol_text import valid_identifier, valid_sha256
import subprocess
import sys
import threading
import time
import ipaddress
import uuid
import wave
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Dict, Optional, Tuple
from urllib.parse import urlsplit, urlunsplit


EXPECTED_VERSION = "audio.cpp 0.9.2-drama3-base-small"
EXPECTED_COMMIT = "50749561"
PINNED_CLI_SHA256 = "ee1ae0b3dce0918f33dd9446e55c21f50120bcf4501a98a9a9a1ef4b7eaa91a9"
PINNED_SPEC_SHA256 = "f6e2587131fd2ca3ddc3e60b4450a58c685fcc7c1f6898748a5eab23003ca37e"
MAX_REQUEST_BYTES = 64 * 1024
MAX_TEXT_CHARS = 2048
MAX_PROFILE_INSTRUCTION_CHARS = 512
MAX_REFERENCE_TEXT_CHARS = 4000
DEFAULT_GUIDANCE_SCALE = 4
MAX_NATIVE_SPEECH_WPM = 325.0
PREPARED_PRESENTATION_REQUEST_TIMEOUT_SECONDS = 240
PREPARED_PRESENTATION_ALIGNMENT_TIMEOUT_SECONDS = 180
PREPARED_PRESENTATION_VISEME_TIMEOUT_SECONDS = 90
EMOTIONS = {"neutral", "joy", "anger", "concern", "grief", "fear"}
DELIVERY_MODES = {"normal", "whisper"}
CONTRAST_LABELS = {"neutral", "positiveMood", "lowMood", "highFear", "highResentment", "whisper"}
TRAIT_VALUES = {
    "warmth": {"warm", "reserved", "guarded", "affectionate"},
    "patience": {"patient", "brisk", "easily wearied", "unhurried"},
    "curiosity": {"inquisitive", "practical", "observant", "self-absorbed"},
    "privacy": {"open", "private", "selective"},
    "manner": {"plainspoken", "courteous", "dry-humored", "earnest"},
    "outlook": {"optimistic", "pragmatic", "pessimistic", "cynical"},
    "emotional stability": {"even-tempered", "sensitive", "volatile", "resilient"},
    "ambition": {"content", "aspiring", "driven", "status-seeking"},
    "integrity": {"honest", "deceitful", "self-serving", "principled", "sly", "manipulative"},
    "courage": {"cautious", "bold", "reckless", "steadfast"},
    "dutifulness": {"independent", "conscientious", "obliging", "self-directed"},
    "vocabulary": {"plain", "precise", "colorful", "earthy", "coarse"},
    "eloquence": {"plain", "direct", "articulate", "eloquent"},
    "status attitude": {"egalitarian", "deferential", "status-conscious", "condescending"},
    "humor": {"playful", "wry", "solemn", "mischievous"},
    "self esteem": {"insecure", "grounded", "confident"},
    "sensitivity": {"thick-skinned", "attuned", "highly sensitive"},
}
DELIVERY_CONTEXT_FIELDS = {"schemaVersion", "personId", "mood", "dispositionKnown", "disposition",
                           "trustKnown", "trust", "fear", "resentment", "encounters", "traits"}


class RequestError(ValueError):
    pass


class RequestCancelled(RequestError):
    pass


@dataclass(frozen=True)
class NativeRequest:
    request_id: str
    conversation_token: str
    person_id: str
    display_name: str
    text: str
    text_ready_ticks: Optional[int]
    timestamp_frequency: Optional[int]
    voice_gender: Optional[str] = None
    delivery_context: Optional[Dict[str, Any]] = None
    sample_kind: str = "native-dialogue"
    controlled_direction: Optional[Dict[str, str]] = None
    contrast_label: Optional[str] = None
    voice_identity: Optional[Dict[str, Any]] = None


@dataclass(frozen=True)
class TrialDirection:
    emotion: str
    delivery_mode: str
    provenance: str


class _LivePcmStreamNormalizer:
    """Normalize complete PCM16 samples once, before both live spool and replay WAV."""

    def __init__(self, normalizer: Any, publish_delta) -> None:
        self.normalizer = normalizer
        self.publish_delta = publish_delta
        self.byte_remainder = b""
        self.input_digest = hashlib.sha256()
        self.output_digest = hashlib.sha256()
        self.input_bytes = 0
        self.output_bytes = 0
        self._sentence_input_digest = None
        self._sentence_output_digest = None
        self._sentence_input_bytes = 0
        self._sentence_output_bytes = 0

    def begin_sentence(self) -> None:
        if self.byte_remainder:
            raise RequestError("PCM fragment carry crossed a sentence boundary")
        self._sentence_input_digest = hashlib.sha256()
        self._sentence_output_digest = hashlib.sha256()
        self._sentence_input_bytes = 0
        self._sentence_output_bytes = 0

    def process_delta(self, delta: bytes, sentence_sequence: int, wav_writer: Any) -> None:
        if not isinstance(delta, bytes) or not delta:
            raise RequestError("server PCM delta must be non-empty bytes")
        combined = self.byte_remainder + delta
        aligned_size = len(combined) & ~1
        aligned = combined[:aligned_size]
        self.byte_remainder = combined[aligned_size:]
        if not aligned:
            return
        normalized = self.normalizer.process_pcm16(aligned)
        if len(normalized) != len(aligned):
            raise RequestError("stream normalizer changed PCM byte count")
        if self.output_bytes + len(normalized) > 64 * 1024 * 1024:
            raise RequestError("live conversation PCM exceeds 64 MiB limit")
        self.publish_delta(normalized, sentence_sequence)
        wav_writer.writeframesraw(normalized)
        self.input_digest.update(aligned)
        self.output_digest.update(normalized)
        self.input_bytes += len(aligned)
        self.output_bytes += len(normalized)
        if self._sentence_input_digest is None or self._sentence_output_digest is None:
            raise RequestError("PCM sentence processor was not initialized")
        self._sentence_input_digest.update(aligned)
        self._sentence_output_digest.update(normalized)
        self._sentence_input_bytes += len(aligned)
        self._sentence_output_bytes += len(normalized)

    def finish_sentence(self) -> Dict[str, Any]:
        if self.byte_remainder:
            raise RequestError("server PCM sentence ended on a partial 16-bit sample")
        if self._sentence_input_digest is None or self._sentence_output_digest is None:
            raise RequestError("PCM sentence processor was not initialized")
        result = {"inputBytes": self._sentence_input_bytes,
            "inputPcmSha256": self._sentence_input_digest.hexdigest(),
            "outputBytes": self._sentence_output_bytes,
            "outputPcmSha256": self._sentence_output_digest.hexdigest()}
        self._sentence_input_digest = None
        self._sentence_output_digest = None
        return result

    def evidence(self, *, sample_frames: int, wav_sha256: str) -> Dict[str, Any]:
        if self.output_bytes != sample_frames * 2:
            raise RequestError("normalized live PCM frames differ from final replay WAV")
        return {"schemaVersion": 1, "normalizer": "pcm16-streaming-envelope-v1",
            "status": "ready", "sampleRate": 24000, "channels": 1,
            "sampleWidthBytes": 2, "sampleFrames": sample_frames,
            "inputPcmBytes": self.input_bytes,
            "inputPcmSha256": self.input_digest.hexdigest(),
            "outputPcmBytes": self.output_bytes,
            "outputPcmSha256": self.output_digest.hexdigest(),
            "outputMatchesLiveSpool": True, "wavSha256": wav_sha256,
            "appliedGainDbAtEnd": round(self.normalizer.applied_gain_db, 6)}


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def canonical_json_hash(value: Any) -> str:
    encoded = json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":")).encode("utf-8")
    return hashlib.sha256(encoded).hexdigest()


def validate_id(value: Any, field: str, maximum: int = 128, filename_safe: bool = False) -> str:
    if not isinstance(value, str) or not value.strip() or len(value) > maximum:
        raise RequestError(f"{field} must be a non-empty identifier within its native length limit")
    if any(ord(char) < 32 or 0xD800 <= ord(char) <= 0xDFFF for char in value):
        raise RequestError(f"{field} contains unsupported control or surrogate characters")
    if filename_safe and not valid_identifier(value):
        raise RequestError(f"{field} must be safe as a filename component")
    return value


def parse_request(value: Any) -> NativeRequest:
    if not isinstance(value, dict):
        raise RequestError("request must be a JSON object")
    required = {"schemaVersion", "mode", "requestId", "conversationToken", "personId", "displayName", "text",
                "textReadyMonotonicTicks", "timestampFrequency"}
    if not required.issubset(value):
        raise RequestError("native request is missing required bridge fields")
    if isinstance(value["schemaVersion"], bool) or value["schemaVersion"] != 1 or value["mode"] != "full-wav-pilot":
        raise RequestError("unsupported native request schema or mode")
    request_id = validate_id(value["requestId"], "requestId", filename_safe=True)
    conversation_token = validate_id(value["conversationToken"], "conversationToken")
    person_id = validate_id(value["personId"], "personId", maximum=240)
    display_name, text = value["displayName"], value["text"]
    if not isinstance(display_name, str) or not display_name.strip() or len(display_name) > 96:
        raise RequestError("displayName must be non-empty and bounded")
    if not isinstance(text, str) or not text.strip() or len(text) > MAX_TEXT_CHARS:
        raise RequestError("finalized text must be non-empty and within the utterance limit")
    if any(ord(char) < 32 and char not in "\n\r\t" for char in text):
        raise RequestError("text contains unsupported control characters")
    ticks = value["textReadyMonotonicTicks"]
    frequency = value["timestampFrequency"]
    for name, number in (("textReadyMonotonicTicks", ticks), ("timestampFrequency", frequency)):
        if isinstance(number, bool) or not isinstance(number, int) or number < 0:
            raise RequestError(f"{name} must be a non-negative integer")
    if frequency == 0:
        raise RequestError("timestampFrequency must be positive")
    voice_gender = value.get("voiceGender")
    if voice_gender is not None and (not isinstance(voice_gender, str) or
                                     voice_gender not in {"female", "male", "unknown"}):
        raise RequestError("voiceGender must be female, male, or unknown when provided")
    delivery_context = parse_delivery_context(value.get("deliveryContext"), person_id)
    sample_kind = value.get("sampleKind", "native-dialogue")
    if not isinstance(sample_kind, str) or sample_kind not in {"native-dialogue", "controlled-voice-contrast"}:
        raise RequestError("sampleKind must be native-dialogue or controlled-voice-contrast")
    if sample_kind == "controlled-voice-contrast" and delivery_context is None:
        raise RequestError("deliveryContext is required for controlled-voice-contrast")
    controlled_direction_value = value.get("controlledDirection")
    controlled_direction: Optional[Dict[str, str]] = None
    contrast_label = value.get("contrastLabel")
    from live_pcm_spool import validate_voice_identity
    try:
        voice_identity = validate_voice_identity(value.get("voiceIdentity"))
    except ValueError as exc:
        raise RequestError(str(exc)) from exc
    if voice_identity is not None and len(json.dumps(voice_identity, ensure_ascii=False)) > 4096:
        raise RequestError("voiceIdentity exceeds its size limit")
    if sample_kind == "controlled-voice-contrast":
        if (not isinstance(controlled_direction_value, dict) or
                set(controlled_direction_value) != {"emotion", "deliveryMode"}):
            raise RequestError("controlledDirection with emotion and deliveryMode is required for controlled contrasts")
        emotion, delivery_mode = controlled_direction_value["emotion"], controlled_direction_value["deliveryMode"]
        if (not isinstance(emotion, str) or emotion not in EMOTIONS or
                not isinstance(delivery_mode, str) or delivery_mode not in DELIVERY_MODES):
            raise RequestError("controlledDirection has unsupported emotion or deliveryMode")
        controlled_direction = {"emotion": emotion, "deliveryMode": delivery_mode}
        if not isinstance(contrast_label, str) or contrast_label not in CONTRAST_LABELS:
            raise RequestError("contrastLabel must identify one controlled-voice-contrast case")
    elif controlled_direction_value is not None:
        raise RequestError("controlledDirection is only valid for controlled-voice-contrast")
    elif contrast_label is not None:
        raise RequestError("contrastLabel is only valid for controlled-voice-contrast")
    return NativeRequest(request_id, conversation_token, person_id, display_name, text, ticks, frequency,
                         voice_gender, delivery_context, sample_kind, controlled_direction, contrast_label,
                         voice_identity)


def _bounded_int(value: Any, field: str, minimum: int, maximum: Optional[int] = None) -> int:
    if isinstance(value, bool) or not isinstance(value, int) or value < minimum or (maximum is not None and value > maximum):
        limit = "{}..{}".format(minimum, maximum) if maximum is not None else "at least {}".format(minimum)
        raise RequestError("deliveryContext.{} must be an integer {}".format(field, limit))
    return value


THREE_TRAIT_VALUES = {key: set(values) for key, values in TRAIT_VALUES.items()}
THREE_TRAIT_VALUES.update({"greed": {"greedy", "unmaterialistic"}, "generosity": {"generous", "stingy"},
    "compassion": {"compassionate", "callous", "cruel"}, "forgiveness": {"forgiving", "vindictive"},
    "humility": {"humble", "arrogant"}})
for _axis, _extra in (("warmth", "unfriendly"), ("patience", "impatient"), ("manner", "rude"),
                      ("courage", "cowardly"), ("dutifulness", "irresponsible")):
    THREE_TRAIT_VALUES[_axis].add(_extra)


def parse_delivery_context(value: Any, person_id: str) -> Optional[Dict[str, Any]]:
    if value is None:
        return None
    if not isinstance(value, dict) or set(value) != DELIVERY_CONTEXT_FIELDS:
        raise RequestError("deliveryContext must contain the exact schema-1 snapshot fields")
    if isinstance(value["schemaVersion"], bool) or not isinstance(value["schemaVersion"], int) or value["schemaVersion"] not in (1, 2):
        raise RequestError("deliveryContext.schemaVersion must equal 1 or 2")
    context_person_id = validate_id(value["personId"], "deliveryContext.personId", maximum=240)
    if context_person_id != person_id:
        raise RequestError("deliveryContext.personId must match native personId")
    _bounded_int(value["mood"], "mood", -100, 100)
    for field in ("dispositionKnown", "trustKnown"):
        if not isinstance(value[field], bool):
            raise RequestError("deliveryContext.{} must be boolean".format(field))
    for field in ("disposition", "trust", "fear", "resentment"):
        _bounded_int(value[field], field, 0, 100)
    _bounded_int(value["encounters"], "encounters", 0)
    traits = value["traits"]
    vocabulary = TRAIT_VALUES if value["schemaVersion"] == 1 else THREE_TRAIT_VALUES
    if not isinstance(traits, dict):
        raise RequestError("deliveryContext.traits must be an object")
    if value["schemaVersion"] == 1 and set(traits) != set(TRAIT_VALUES):
        raise RequestError("deliveryContext.traits must contain all 17 saved personality keys")
    if value["schemaVersion"] == 2 and (len(traits) != 3 or not set(traits).issubset(vocabulary)):
        raise RequestError("deliveryContext.traits must contain exactly three supported keys")
    for trait, trait_value in traits.items():
        if not isinstance(trait_value, str) or trait_value not in vocabulary[trait]:
            raise RequestError("deliveryContext.traits.{} has unsupported saved value".format(trait))
    # Rebuild fields in canonical insertion order so evidence is stable and detached from caller mutation.
    return {"schemaVersion": value["schemaVersion"], "personId": context_person_id, "mood": value["mood"],
        "dispositionKnown": value["dispositionKnown"], "disposition": value["disposition"],
        "trustKnown": value["trustKnown"], "trust": value["trust"], "fear": value["fear"],
        "resentment": value["resentment"], "encounters": value["encounters"],
        "traits": {trait: traits[trait] for trait in vocabulary if trait in traits}}


def read_request(path: Path, expected_request_id: str) -> NativeRequest:
    if path.stat().st_size > MAX_REQUEST_BYTES:
        raise RequestError("request file exceeds size limit")
    with path.open("r", encoding="utf-8-sig") as stream:
        request = parse_request(json.load(stream))
    if expected_request_id != request.request_id:
        raise RequestError("request filename must equal requestId")
    return request


def safe_failure_binding(path: Path, expected_request_id: str) -> Optional[Dict[str, str]]:
    """Read only the bounded identity envelope needed to publish a terminal failure."""
    try:
        if path.stat().st_size > MAX_REQUEST_BYTES:
            return None
        with path.open("r", encoding="utf-8-sig") as stream:
            value = json.load(stream)
        if (not isinstance(value, dict) or isinstance(value.get("schemaVersion"), bool) or
                value.get("schemaVersion") != 1 or value.get("mode") != "full-wav-pilot"):
            return None
        request_id = validate_id(value.get("requestId"), "requestId", filename_safe=True)
        conversation_token = validate_id(value.get("conversationToken"), "conversationToken")
        person_id = validate_id(value.get("personId"), "personId", maximum=240)
        if request_id != expected_request_id:
            return None
        return {"requestId": request_id, "conversationToken": conversation_token,
                "personId": person_id}
    except (OSError, UnicodeError, ValueError, TypeError):
        return None


def path_is_within(path: Path, root: Path) -> bool:
    try:
        return os.path.commonpath((str(path.resolve()), str(root.resolve()))) == str(root.resolve())
    except (OSError, ValueError):
        return False


def load_profiles(profiles_path: Path) -> Dict[str, Dict[str, Any]]:
    with profiles_path.open("r", encoding="utf-8-sig") as stream:
        document = json.load(stream)
    profiles = document.get("profiles") if isinstance(document, dict) and document.get("schemaVersion") == 1 else None
    if not isinstance(profiles, dict) or len(profiles) < 12:
        raise RequestError("trusted profiles.json must contain at least twelve profiles")
    for profile_id, profile in profiles.items():
        validate_id(profile_id, "profileId")
        if not isinstance(profile, dict) or set(profile) - {
                "instruction", "seed", "perceivedGender", "voiceRef", "referenceText", "voiceReferenceSha256"}:
            raise RequestError("trusted voice profile has an invalid shape")
        instruction, seed = profile.get("instruction"), profile.get("seed")
        if not isinstance(instruction, str) or not instruction.strip() or len(instruction) > MAX_PROFILE_INSTRUCTION_CHARS:
            raise RequestError("voice profile instruction is missing or too long")
        if isinstance(seed, bool) or not isinstance(seed, int) or seed < 0 or seed > 2**31 - 1:
            raise RequestError("voice profile seed must be a non-negative 31-bit integer")
        perceived_gender = profile.get("perceivedGender")
        if not isinstance(perceived_gender, str) or perceived_gender not in {"female", "male"}:
            raise RequestError("trusted profile lacks valid perceivedGender; re-import corpus profiles before running trial")
    return profiles


def assign_profile(person_id: str, profiles: Dict[str, Dict[str, Any]], bindings_path: Path,
                   voice_gender: Optional[str] = None) -> str:
    """Persist one trusted profile per stable personId; never accept request profile IDs."""
    if voice_gender is not None and (not isinstance(voice_gender, str) or
                                     voice_gender not in {"female", "male", "unknown"}):
        raise RequestError("voiceGender must be female, male, or unknown when provided")
    requested_gender = voice_gender if voice_gender in {"female", "male"} else None
    if bindings_path.exists():
        with bindings_path.open("r", encoding="utf-8-sig") as stream:
            doc = json.load(stream)
        if not isinstance(doc, dict) or doc.get("schemaVersion") != 1 or not isinstance(doc.get("bindings"), dict):
            raise RequestError("voice-bindings.json must use schemaVersion 1")
        bindings = doc["bindings"]
    else:
        bindings = {}
    existing = bindings.get(person_id)
    if existing is not None:
        if existing not in profiles:
            raise RequestError("saved voice binding points to an unknown profile")
        bound_gender = profiles[existing].get("perceivedGender")
        if requested_gender is not None and bound_gender != requested_gender:
            raise RequestError(
                "saved voice binding for personId {!r} points to {!r} ({}) but native voiceGender is {}. "
                "Do not auto-reroll; verify this trial binding, remove only this personId binding, then retry."
                .format(person_id, existing, bound_gender or "untyped gender", requested_gender))
        return existing
    ids = sorted(profiles)
    eligible = [profile_id for profile_id in ids if requested_gender is None or
                profiles[profile_id].get("perceivedGender") == requested_gender]
    if not eligible:
        raise RequestError("no trusted voice profile matches native voiceGender {}; re-import corpus profiles"
                           .format(requested_gender))
    unused = [profile_id for profile_id in eligible if profile_id not in bindings.values()]
    pool = unused or eligible
    offset = int(hashlib.sha256(person_id.encode("utf-8")).hexdigest()[:16], 16) % len(pool)
    profile_id = pool[offset]
    bindings[person_id] = profile_id
    bindings_path.parent.mkdir(parents=True, exist_ok=True)
    atomic_json(bindings_path, {"schemaVersion": 1, "bindings": bindings})
    return profile_id


def load_voice_profile(profiles: Dict[str, Dict[str, Any]], profile_id: str, voice_root: Path) -> Tuple[Dict[str, Any], Optional[Path]]:
    profile = profiles.get(profile_id)
    if profile is None:
        raise RequestError("unknown trusted voice profile")
    reference_text = profile.get("referenceText")
    voice_ref_value = profile.get("voiceRef")
    reference_hash = profile.get("voiceReferenceSha256")
    voice_ref: Optional[Path] = None
    if voice_ref_value is not None:
        if not isinstance(voice_ref_value, str) or not voice_ref_value:
            raise RequestError("voiceRef must be a path relative to voiceRoot")
        if not isinstance(reference_text, str) or not reference_text.strip() or len(reference_text) > MAX_REFERENCE_TEXT_CHARS:
            raise RequestError("referenceText is required with voiceRef")
        voice_ref = (voice_root / voice_ref_value).resolve()
        if not path_is_within(voice_ref, voice_root) or not voice_ref.is_file():
            raise RequestError("voiceRef must identify a file inside voiceRoot")
        if not isinstance(reference_hash, str) or not valid_sha256(reference_hash):
            raise RequestError("voiceRef requires an exact 64-character SHA-256")
        if sha256_file(voice_ref).lower() != reference_hash.lower():
            raise RequestError("voiceRef SHA-256 does not match its file")
    elif reference_text is not None:
        raise RequestError("referenceText is only valid with voiceRef")
    elif reference_hash is not None:
        raise RequestError("voiceReferenceSha256 is only valid with voiceRef")
    return profile, voice_ref


def load_direction(person_id: str, directions_path: Optional[Path]) -> TrialDirection:
    if directions_path is None or not directions_path.exists():
        return TrialDirection("neutral", "normal", "baseline_default")
    with directions_path.open("r", encoding="utf-8-sig") as stream:
        document = json.load(stream)
    if not isinstance(document, dict) or document.get("schemaVersion") != 1 or not isinstance(document.get("directions"), dict):
        raise RequestError("directions file must use schemaVersion 1")
    row = document["directions"].get(person_id)
    if row is None:
        return TrialDirection("neutral", "normal", "baseline_default")
    if not isinstance(row, dict) or set(row) != {"emotion", "deliveryMode", "provenance"}:
        raise RequestError("direction entry must contain emotion, deliveryMode, and provenance only")
    emotion, delivery, provenance = row["emotion"], row["deliveryMode"], row["provenance"]
    if (not isinstance(emotion, str) or emotion not in EMOTIONS or
            not isinstance(delivery, str) or delivery not in DELIVERY_MODES or
            not isinstance(provenance, str) or provenance != "controlled_trial"):
        raise RequestError("direction values must be valid controlled-trial overrides")
    return TrialDirection(emotion, delivery, provenance)


def native_context_direction(context: Optional[Dict[str, Any]]) -> TrialDirection:
    """Derive temporary emotion from the native numeric snapshot; identity stays profile-owned."""
    if context is None:
        return TrialDirection("neutral", "normal", "native_numeric_context")
    if context["fear"] >= 65:
        emotion = "fear"
    elif context["resentment"] >= 65:
        emotion = "anger"
    elif context["mood"] <= -50:
        emotion = "grief"
    elif context["mood"] <= -15:
        emotion = "concern"
    elif context["mood"] >= 15:
        emotion = "joy"
    elif context["trustKnown"] and context["trust"] <= 25:
        emotion = "concern"
    else:
        emotion = "neutral"
    return TrialDirection(emotion, "normal", "native_numeric_context")


def speech_rate_wpm(text: str, duration_seconds: float) -> float:
    if not isinstance(text, str) or not text.strip():
        raise RequestError("speech rate requires non-empty finalized text")
    if (isinstance(duration_seconds, bool) or not isinstance(duration_seconds, (int, float)) or
            not math.isfinite(duration_seconds) or duration_seconds <= 0):
        raise RequestError("speech rate requires a positive finite WAV duration")
    return len(text.split()) * 60.0 / duration_seconds


def validate_speech_rate(text: str, duration_seconds: float) -> Dict[str, Any]:
    word_count = len(text.split())
    rate = speech_rate_wpm(text, duration_seconds)
    if rate > MAX_NATIVE_SPEECH_WPM + 1e-9:
        raise RequestError("synthesized speech rate {:.3f} WPM exceeds 325 WPM playback limit".format(rate))
    return {"wordCount": word_count, "wordsPerMinute": round(rate, 3),
            "maxWordsPerMinute": MAX_NATIVE_SPEECH_WPM}


def personality_directions(context: Dict[str, Any]) -> Tuple[str, ...]:
    traits = context["traits"]
    stable = []
    current = []
    fear_high = context["fear"] >= 65
    resentment_high = context["resentment"] >= 65
    mood = context["mood"]
    mood_active = not fear_high and not resentment_high
    mood_signal_active = False
    if mood_active:
        if mood <= -50:
            current.append("low spirits, quiet and subdued, not anger")
            mood_signal_active = True
        elif mood <= -15:
            current.append("slightly subdued and low-key, not anger")
            mood_signal_active = True
        elif mood >= 50:
            current.append("buoyant and lively")
            mood_signal_active = True
        elif mood >= 15:
            current.append("gently upbeat")
            mood_signal_active = True
    if context["dispositionKnown"]:
        if context["disposition"] <= 25:
            current.append("interpersonally reserved disposition")
        elif context["disposition"] >= 75:
            if fear_high:
                current.append("warm interpersonal disposition; fear controls delivery")
            elif resentment_high:
                current.append("warm interpersonal disposition; resentment controls delivery")
            else:
                current.append("warm interpersonal disposition")
    if context["trustKnown"] and mood_active and not mood_signal_active:
        if context["trust"] <= 25:
            current.append("careful and measured interpersonal tone")
        elif context["trust"] >= 75:
            current.append("relaxed interpersonal tone")
    if fear_high:
        if traits.get("courage") in {"bold", "steadfast"}:
            current.append("fear present but contained and alert, not hesitant")
        elif traits.get("courage") == "reckless":
            current.append("fear present but masked by impulsive boldness")
        else:
            current.append("tense and hesitant, with fear kept in the voice")
    if resentment_high and not fear_high:
        current.append("cool and slightly sharp, without adding words or friendliness")
    if context["encounters"] == 0:
        current.append("measured with an unfamiliar conversational partner")
    elif context["encounters"] >= 5:
        current.append("familiar partner; familiarity does not imply comfort, trust or friendliness")

    trait_labels = {
        "warmth": {"warm": "warm", "affectionate": "affectionate", "reserved": "reserved", "guarded": "guarded"},
        "patience": {"patient": "patient", "unhurried": "unhurried", "brisk": "brisk", "easily wearied": "easily wearied"},
        "manner": {"plainspoken": "plainspoken", "courteous": "courteous", "dry-humored": "dry-humored", "earnest": "earnest"},
        "outlook": {"optimistic": "optimistic", "pragmatic": "pragmatic", "pessimistic": "pessimistic", "cynical": "cynical"},
        "emotional stability": {"even-tempered": "even-tempered", "sensitive": "sensitive", "volatile": "volatile", "resilient": "resilient"},
        "eloquence": {"plain": "plain-spoken", "direct": "direct", "articulate": "articulate", "eloquent": "eloquent"},
        "status attitude": {"egalitarian": "egalitarian", "deferential": "deferential", "status-conscious": "status-conscious", "condescending": "condescending"},
        "humor": {"playful": "playful", "wry": "wry", "solemn": "solemn", "mischievous": "mischievous"},
        "self esteem": {"insecure": "insecure", "grounded": "grounded", "confident": "confident"},
        "sensitivity": {"thick-skinned": "thick-skinned", "attuned": "attuned", "highly sensitive": "highly sensitive"},
    }
    for trait, mapping in trait_labels.items():
        if trait in traits:
            stable.append(mapping.get(traits[trait], traits[trait]))
    # Greed, honesty and similar semantic motives do not imply a particular voice sound.
    # Preserve all traits in the receipt; only supplied prosody-relevant traits shape delivery.
    return ("saved personality baseline: " + (", ".join(stable) if stable else "no supplied prosody trait"),
            "current delivery: " + ("; ".join(current) if current else "neutral") +
            "; current state cues override baseline prosody for this utterance only")


def instruction_for(direction: TrialDirection, profile: Dict[str, Any],
                    delivery_context: Optional[Dict[str, Any]] = None) -> str:
    identity = "Stable voice identity: " + profile["instruction"].strip().rstrip(". ")
    if delivery_context is None:
        if direction.provenance == "native_numeric_context":
            return " ".join((identity,
                "Temporary emotion (native numeric context): " + direction.emotion + ".",
                "Preserve stable voice identity, gender, accent, and exact words.",
                "Delivery mode: normal (native dialogue)."))
        # Keep old native requests bit-for-bit neutral apart from their existing trial direction.
        return " ".join((identity, "Emotional delivery: " + direction.emotion + ".",
                         "Delivery mode: " + direction.delivery_mode + "."))
    directions = "; ".join(personality_directions(delivery_context))
    parts = [identity + ".",
        "Personal vocal delivery (prosody only; preserve exact text): " + directions + ".",
        "Saved traits form stable baseline. Current mood, fear and resentment control temporary shading when cues conflict. "
        "Preserve voice identity and exact words."]
    if direction.provenance == "native_numeric_context":
        parts.append("Temporary emotion (native numeric context): " + direction.emotion +
            ". Preserve stable voice identity, gender, accent, and exact words.")
    elif direction.emotion == "neutral":
        parts.append("Let typed mood and personality determine emotional shading; apply no fixed emotion override.")
    else:
        parts.append("Controlled emotion override (" + direction.provenance + "): " + direction.emotion + ".")
    delivery_source = "native dialogue" if direction.provenance == "native_numeric_context" else direction.provenance
    parts.extend(("Delivery mode: " + direction.delivery_mode + " (" + delivery_source + ").",))
    return " ".join(parts)


def build_cli_command(cli: Path, model: Path, spec: Path, backend: str,
                      request: NativeRequest, profile: Dict[str, Any], voice_ref: Optional[Path],
                      wav_path: Path, direction: TrialDirection) -> list[str]:
    command = [str(cli), "--task", "tts", "--family", "breeze_tts", "--model", str(model),
        "--backend", backend, "--model-spec-override", str(spec), "--text", request.text,
        "--out", str(wav_path), "--out-format", "pcm16", "--seed", str(profile["seed"]),
        "--instruct", instruction_for(direction, profile, request.delivery_context),
        "--request-option", "guidance_scale={}".format(DEFAULT_GUIDANCE_SCALE),
        "--metrics", "--log", "--load-progress", "1"]
    if voice_ref is not None:
        command.extend(("--voice-ref", str(voice_ref), "--reference-text", profile["referenceText"]))
    return command


def validate_server_endpoint(endpoint: str) -> str:
    if not isinstance(endpoint, str) or not endpoint.strip():
        raise RequestError("server endpoint must be a non-empty loopback HTTP URL")
    try:
        parsed = urlsplit(endpoint.strip())
        host = parsed.hostname
        port = parsed.port
    except ValueError as exc:
        raise RequestError("server endpoint is malformed") from exc
    if (parsed.scheme.lower() != "http" or not host or parsed.username is not None or
            parsed.password is not None or parsed.path not in ("", "/") or parsed.query or parsed.fragment):
        raise RequestError("server endpoint must be a base loopback HTTP URL without credentials or path")
    try:
        loopback = ipaddress.ip_address(host).is_loopback
    except ValueError:
        loopback = host.lower() == "localhost"
    if not loopback:
        raise RequestError("server endpoint host must be loopback")
    authority_host = "[{}]".format(host.lower()) if ":" in host else host.lower()
    authority = authority_host + (":" + str(port) if port is not None else "")
    return urlunsplit(("http", authority, "", "", ""))


def verify_model_assets(expected_model_hash: str, model: Path, spec: Path) -> None:
    if not model.is_file() or not spec.is_file():
        raise RequestError("model or Breeze model spec is missing")
    if sha256_file(spec).lower() != PINNED_SPEC_SHA256:
        raise RequestError("Breeze model spec SHA-256 does not match the pinned trial file")
    if not valid_sha256(expected_model_hash):
        raise RequestError("an exact 64-character expected model SHA-256 is required")
    if sha256_file(model).lower() != expected_model_hash.lower():
        raise RequestError("model SHA-256 does not match the explicit pin")


def verify_backend(cli: Path, expected_model_hash: str, model: Path, spec: Path, backend: str) -> str:
    if not cli.is_file() or cli.name.lower() != "audiocpp_cli.exe":
        raise RequestError("CLI must be the pinned audiocpp_cli.exe binary")
    version = subprocess.run([str(cli), "--version"], check=True, capture_output=True, text=True, timeout=10).stdout
    if EXPECTED_VERSION not in version or EXPECTED_COMMIT not in version:
        raise RequestError("audio.cpp binary version or commit does not match the pinned trial build")
    if sha256_file(cli).lower() != PINNED_CLI_SHA256:
        raise RequestError("CLI SHA-256 does not match the pinned trial binary")
    verify_model_assets(expected_model_hash, model, spec)
    if backend not in {"cuda", "cpu"}:
        raise RequestError("backend must be cuda or cpu")
    return version.strip()


def atomic_json(path: Path, value: Dict[str, Any]) -> None:
    temp = path.with_name(path.name + "." + uuid.uuid4().hex + ".tmp")
    with temp.open("w", encoding="utf-8", newline="\n") as stream:
        json.dump(value, stream, ensure_ascii=False, indent=2)
        stream.write("\n")
        stream.flush()
        os.fsync(stream.fileno())
    os.replace(temp, path)


def wave_metadata(path: Path) -> Dict[str, Any]:
    if path.stat().st_size < 44 or path.stat().st_size > 16 * 1024 * 1024:
        raise RequestError("backend produced an empty, truncated, or oversized WAV")
    with wave.open(str(path), "rb") as wav:
        frames, rate, channels = wav.getnframes(), wav.getframerate(), wav.getnchannels()
        if (wav.getcomptype() != "NONE" or wav.getsampwidth() != 2 or frames <= 0 or
                rate < 8000 or rate > 192000 or channels not in {1, 2} or frames > rate * 60):
            raise RequestError("backend WAV must be non-empty uncompressed PCM16 mono or stereo")
        return {"sampleRate": rate, "channels": channels, "sampleFrames": frames,
                "durationSeconds": round(frames / float(rate), 3)}


def pcm16_wave_digest(path: Path) -> Dict[str, Any]:
    """Hash exact PCM payload without loading a potentially long sentence into memory."""
    digest = hashlib.sha256()
    with wave.open(str(path), "rb") as wav:
        if wav.getcomptype() != "NONE" or wav.getsampwidth() != 2:
            raise RequestError("stream sentence WAV must be uncompressed PCM16")
        frames = wav.getnframes()
        rate, channels = wav.getframerate(), wav.getnchannels()
        if rate != 24000 or channels != 1 or frames <= 0:
            raise RequestError("stream sentence WAV must be PCM16 mono at 24 kHz")
        read_frames = 0
        while read_frames < frames:
            block = wav.readframes(min(8192, frames - read_frames))
            if not block:
                break
            if len(block) % 2:
                raise RequestError("stream WAV contains a partial PCM16 sample")
            digest.update(block)
            read_frames += len(block) // 2
        if read_frames != frames:
            raise RequestError("stream sentence WAV ended before its declared frame count")
        return {"sampleRate": rate, "channels": channels, "sampleFrames": frames,
                "pcmSha256": digest.hexdigest(), "pcmBytes": frames * 2}


def claim_request(source: Path, working: Path) -> Optional[Tuple[Path, str]]:
    request_id = source.name.removesuffix(".request.json")
    claimed = working / (request_id + "." + str(os.getpid()) + "." + uuid.uuid4().hex + ".request.json")
    try:
        os.replace(source, claimed)
        return claimed, request_id
    except FileNotFoundError:
        return None
    except OSError:
        if not source.exists():
            return None
        raise


def cancellation_marker(root: Path, request_id: str) -> Path:
    return root / "requests" / (request_id + ".cancel.json")


LIVE_TERMINAL_STATUSES = {"audio_complete", "failed", "cancelled"}


def _live_processed_marker(root: Path, stream_id: str) -> Path:
    return root / "measurements" / (stream_id + ".coordinator-processed.json")


def _read_live_processed_status(root: Path, stream_id: str) -> Optional[str]:
    marker = _live_processed_marker(root, stream_id)
    try:
        if not marker.is_file() or marker.stat().st_size > 4096:
            return None
        value = json.loads(marker.read_text(encoding="utf-8-sig"))
    except (OSError, json.JSONDecodeError):
        return None
    if (not isinstance(value, dict) or value.get("schemaVersion") != 1 or
            value.get("streamId") != stream_id or value.get("status") not in LIVE_TERMINAL_STATUSES or
            set(value) != {"schemaVersion", "streamId", "status"}):
        return None
    return value["status"]


def _read_live_response_terminal_status(root: Path, stream_id: str) -> Optional[str]:
    state_path = root / "responses" / stream_id / "stream.json"
    try:
        if not state_path.is_file() or state_path.stat().st_size > 64 * 1024:
            return None
        value = json.loads(state_path.read_text(encoding="utf-8-sig"))
    except (OSError, json.JSONDecodeError):
        return None
    if (isinstance(value, dict) and value.get("schemaVersion") == 1 and
            value.get("streamId") == stream_id and value.get("status") in LIVE_TERMINAL_STATUSES):
        return value["status"]
    return None


def record_live_stream_result(root: Path, result: Dict[str, Any], processed_ids=None) -> None:
    """Durably retire a terminal live stream without touching its evidence artifacts."""
    stream_id, status = result.get("streamId"), result.get("status")
    if not isinstance(stream_id, str) or not valid_identifier(stream_id) or status not in LIVE_TERMINAL_STATUSES:
        raise RequestError("live stream result has an invalid identity or terminal status")
    measurements = root / "measurements"
    measurements.mkdir(parents=True, exist_ok=True)
    if status == "cancelled":
        cancelled_path = measurements / (stream_id + ".cancelled.json")
        if not cancelled_path.exists():
            atomic_json(cancelled_path, {"schemaVersion": 1, "streamId": stream_id,
                "status": "cancelled", "reason": "native-cancel-marker-present"})
    atomic_json(_live_processed_marker(root, stream_id), {
        "schemaVersion": 1, "streamId": stream_id, "status": status})
    if processed_ids is not None:
        processed_ids.add(stream_id)


def next_live_stream_to_process(requests: Path, root: Path, processed_ids=None) -> Optional[Path]:
    """Retire already-terminal/cancelled directories and return the next live input."""
    if processed_ids is None:
        processed_ids = set()
    live_streams = sorted(path for path in requests.iterdir()
        if path.is_dir() and (path / "begin.json").is_file())
    for stream_input in live_streams:
        stream_id = stream_input.name
        if stream_id in processed_ids:
            continue
        recorded_status = _read_live_processed_status(root, stream_id)
        if recorded_status is not None:
            processed_ids.add(stream_id)
            continue
        # Cancellation takes precedence even if a previous attempt already
        # left a response directory or terminal stream state behind.
        if (stream_input / "cancel.json").exists():
            record_live_stream_result(root, {"streamId": stream_id, "status": "cancelled"}, processed_ids)
            continue
        terminal_status = _read_live_response_terminal_status(root, stream_id)
        if terminal_status is not None:
            record_live_stream_result(root, {"streamId": stream_id, "status": terminal_status}, processed_ids)
            continue
        return stream_input
    return None


def is_cancelled(root: Path, request_id: str, conversation_token: Optional[str] = None) -> bool:
    marker = cancellation_marker(root, request_id)
    if not marker.exists():
        return False
    try:
        if marker.stat().st_size > 4096:
            return True
        with marker.open("r", encoding="utf-8-sig") as stream:
            value = json.load(stream)
        if (not isinstance(value, dict) or value.get("schemaVersion") != 1 or
                value.get("requestId") != request_id or
                (conversation_token is not None and value.get("conversationToken") != conversation_token)):
            return True
        return True
    except (OSError, json.JSONDecodeError):
        # A present but malformed cancellation marker fails closed for this request ID.
        return True


def _start_cancel_event(cancel_check):
    event = threading.Event()
    stop = threading.Event()

    def monitor():
        while not stop.wait(0.025):
            try:
                if cancel_check():
                    event.set()
                    return
            except Exception:
                # Invalid/malformed markers are fail-closed by the caller's check.
                event.set()
                return

    thread = threading.Thread(target=monitor, name="breeze-cancel-monitor", daemon=True)
    thread.start()
    return event, stop, thread


def _synthesize_with_cancel_check(synthesize, args, kwargs, cancel_check):
    cancel_event, stop, monitor = _start_cancel_event(cancel_check)
    try:
        return synthesize(*args, **kwargs, cancel_event=cancel_event)
    except Exception as exc:
        if type(exc).__name__ != "SynthesisCancelled":
            raise
        cancelled = cancel_check()
        if not cancelled:
            raise RuntimeError("SSE reader stopped without an owner cancellation") from exc
        raise RequestCancelled("synthesis was cancelled by its native owner") from exc
    finally:
        stop.set()
        monitor.join(timeout=0.1)


def prepared_helper_timeout(deadline: float, maximum_seconds: int, phase: str) -> int:
    """Return a helper timeout within the shared prepared-presentation budget."""
    remaining = deadline - time.monotonic()
    timeout = min(maximum_seconds, int(remaining))
    if timeout < 1:
        raise RequestError("prepared presentation request deadline expired before " + phase)
    return timeout


def prepared_presentation_deadline(started: Optional[float] = None) -> float:
    """Create the single shared 240-second production request deadline."""
    if started is None:
        started = time.monotonic()
    return started + PREPARED_PRESENTATION_REQUEST_TIMEOUT_SECONDS


def _stream_cancelled(spool) -> bool:
    try:
        spool._check_cancelled()
        return False
    except Exception:
        return True


def _promote_first_reference(request: NativeRequest, profile: Dict[str, Any], profile_id: str,
                             source_wav: Path, evidence_path: Path, voice_root: Path,
                             profiles_path: Path, measurements: Path,
                             wav_info: Dict[str, Any], wav_hash: str) -> None:
    """Promote only an explicitly selected, first eligible native female sample."""
    audit_path = measurements / (request.request_id + ".reference-promotion.json")
    reason = None
    if request.sample_kind != "native-dialogue":
        reason = "sample-is-not-native-dialogue"
    elif request.voice_gender != "female" or profile.get("perceivedGender") != "female":
        reason = "female-native-gender-and-profile-required"
    elif request.delivery_context is None:
        reason = "typed-delivery-context-required"
    elif wav_info["durationSeconds"] < 3.0:
        reason = "sample-shorter-than-3-seconds"
    elif any(key in profile for key in ("voiceRef", "referenceText", "voiceReferenceSha256")):
        reason = "profile-already-has-reference-metadata"

    base_context_path = voice_root / (profile_id + ".base-context.json")
    context_hash = canonical_json_hash(request.delivery_context) if request.delivery_context is not None else None
    if reason is None and base_context_path.exists():
        try:
            with base_context_path.open("r", encoding="utf-8-sig") as stream:
                existing_context = json.load(stream)
        except (OSError, json.JSONDecodeError) as exc:
            raise RequestError("cannot read existing base delivery context: " + str(exc))
        if existing_context != request.delivery_context:
            raise RequestError("existing profile base delivery context differs; refusing to overwrite")

    audit: Dict[str, Any] = {"schemaVersion": 1, "requestId": request.request_id,
        "personId": request.person_id, "profileId": profile_id, "status": "skipped" if reason else "pending",
        "reason": reason, "sampleKind": request.sample_kind, "voiceGender": request.voice_gender,
        "wavSha256": wav_hash, "wavDurationSeconds": wav_info["durationSeconds"],
        "evidenceFile": evidence_path.name, "baseContextFile": base_context_path.name if reason is None else None,
        "baseContextSha256": context_hash if reason is None else None}
    if reason is None:
        from promote_reference import promote_reference
        outcome = promote_reference(source_wav, evidence_path, profiles_path,
            voice_root, profile_id, "female", apply=True, replace=False)
        # Keep transcript only in the trusted profile reference metadata; do not duplicate it in audit.
        outcome.pop("referenceText", None)
        audit.update(outcome)
        audit["status"] = outcome["status"]
        if not base_context_path.exists():
            atomic_json(base_context_path, request.delivery_context)
    atomic_json(audit_path, audit)


def publish_preview_visemes(request: Any, wav_path: Path, wav_info: Dict[str, Any],
                            response_manifest: Path, requests_root: Path,
                            rhubarb_cli: Optional[Path] = None,
                            timeout_seconds: Optional[int] = None) -> str:
    """Synchronously stage one exact-text Rhubarb estimate before WAV manifest publication."""
    if rhubarb_cli is None:
        raise RequestError("--preview-visemes-before-playback requires --rhubarb-cli")
    request_id = request.request_id if isinstance(request, NativeRequest) else request["requestId"]
    conversation_token = (request.conversation_token if isinstance(request, NativeRequest)
                          else request.get("conversationToken"))
    text = request.text if isinstance(request, NativeRequest) else request["text"]
    output_path = response_manifest.parent / (request_id + ".visemes.json")
    if output_path.exists():
        raise RequestError("viseme preview sidecar already exists")
    if is_cancelled(requests_root.parent, request_id, conversation_token):
        raise RequestCancelled("request was cancelled before viseme preview synthesis")
    try:
        from rhubarb_visemes import run_rhubarb
    except ImportError:
        from tools.BreezeTtsTrial.rhubarb_visemes import run_rhubarb
    import tempfile
    with tempfile.TemporaryDirectory(prefix=".viseme-preview-", dir=str(response_manifest.parent)) as stage_dir:
        staged_path = Path(stage_dir) / (request_id + ".visemes.json")
        if timeout_seconds is None:
            timeline = run_rhubarb(str(rhubarb_cli), str(wav_path), text, str(staged_path))
        else:
            timeline = run_rhubarb(str(rhubarb_cli), str(wav_path), text, str(staged_path),
                                   threads=2, timeout_seconds=timeout_seconds)
        if not staged_path.is_file():
            raise RequestError("Rhubarb preview returned without a staged sidecar")
        try:
            staged_timeline = json.loads(staged_path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError) as exc:
            raise RequestError("Rhubarb preview sidecar is not valid JSON") from exc
        expected_text_hash = hashlib.sha256(text.encode("utf-8")).hexdigest()
        for candidate in (timeline, staged_timeline):
            if (not isinstance(candidate, dict) or candidate.get("status") != "ready" or
                    candidate.get("timelineKind") != "recognizer-viseme-estimate" or
                    candidate.get("requestId") != request_id or candidate.get("text") != text or
                    candidate.get("audioSampleRate") != wav_info["sampleRate"] or
                    candidate.get("audioFrames") != wav_info["sampleFrames"] or
                    not isinstance(candidate.get("cues"), list) or not candidate["cues"] or
                    not isinstance(candidate.get("provenance"), dict) or
                    candidate["provenance"].get("audioSha256") != sha256_file(wav_path) or
                    candidate["provenance"].get("dialogHintSha256") != expected_text_hash):
                raise RequestError("Rhubarb preview failed exact WAV/transcript binding validation")
        if timeline != staged_timeline:
            raise RequestError("Rhubarb preview return value differs from staged sidecar")
        if is_cancelled(requests_root.parent, request_id, conversation_token):
            raise RequestCancelled("request was cancelled before viseme preview publication")
        os.link(str(staged_path), str(output_path))
    return "ready"


def publish_preview_alignment(request: Any, wav_path: Path, wav_info: Dict[str, Any],
                              response_manifest: Path, requests_root: Path,
                              aligner_cli: Optional[Path], aligner_model: Optional[Path],
                              aligner_sha256: Optional[str],
                              timeout_seconds: int = PREPARED_PRESENTATION_ALIGNMENT_TIMEOUT_SECONDS) -> str:
    """Synchronously stage exact-text forced word alignment before manifest publication."""
    if aligner_cli is None or aligner_model is None:
        raise RequestError("--preview-alignment-before-playback requires --aligner-cli and --aligner-model")
    request_id = request.request_id if isinstance(request, NativeRequest) else request["requestId"]
    conversation_token = (request.conversation_token if isinstance(request, NativeRequest)
                          else request.get("conversationToken"))
    text = request.text if isinstance(request, NativeRequest) else request["text"]
    output_path = response_manifest.parent / (request_id + ".alignment.json")
    if output_path.exists():
        raise RequestError("alignment preview sidecar already exists")
    if is_cancelled(requests_root.parent, request_id, conversation_token):
        raise RequestCancelled("request was cancelled before alignment preview synthesis")
    try:
        from tools.BreezeTtsTrial.align_words import run_alignment
    except ModuleNotFoundError:
        from align_words import run_alignment
    import tempfile
    with tempfile.TemporaryDirectory(prefix=".alignment-preview-", dir=str(response_manifest.parent)) as stage_dir:
        staged_path = Path(stage_dir) / (request_id + ".alignment.json")
        timeline = run_alignment(str(aligner_cli), str(aligner_model), str(wav_path), text,
            str(staged_path), threads=1, timeout_seconds=timeout_seconds,
            expected_model_sha256=aligner_sha256)
        if not staged_path.is_file():
            raise RequestError("alignment preview returned without a staged sidecar")
        try:
            staged_timeline = json.loads(staged_path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError) as exc:
            raise RequestError("alignment preview sidecar is not valid JSON") from exc
        expected_text_hash = hashlib.sha256(text.encode("utf-8")).hexdigest()
        for candidate in (timeline, staged_timeline):
            provenance = candidate.get("provenance") if isinstance(candidate, dict) else None
            coverage = candidate.get("coverage") if isinstance(candidate, dict) else None
            model_hash = provenance.get("modelSha256") if isinstance(provenance, dict) else None
            matched_tokens = coverage.get("matchedTokens") if isinstance(coverage, dict) else None
            transcript_tokens = coverage.get("transcriptTokens") if isinstance(coverage, dict) else None
            if (not isinstance(candidate, dict) or candidate.get("status") != "ready" or
                    candidate.get("timelineKind") != "forced-word-alignment" or
                    candidate.get("text") != text or
                    candidate.get("audioSampleRate") != wav_info["sampleRate"] or
                    candidate.get("audioFrames") != wav_info["sampleFrames"] or
                    not isinstance(candidate.get("words"), list) or not candidate["words"] or
                    not isinstance(coverage, dict) or
                    matched_tokens != transcript_tokens or
                    isinstance(matched_tokens, bool) or not isinstance(matched_tokens, int) or matched_tokens < 1 or
                    not isinstance(provenance, dict) or provenance.get("requestId") != request_id or
                    provenance.get("audioSha256") != sha256_file(wav_path) or
                    provenance.get("transcriptSha256") != expected_text_hash or
                    not isinstance(aligner_sha256, str) or
                    not isinstance(model_hash, str) or model_hash.lower() != aligner_sha256.lower() or
                    provenance.get("backend") != "cpu" or provenance.get("threads") != 1 or
                    provenance.get("phonemeAlignment") is not False):
                raise RequestError("alignment preview failed exact WAV/transcript/model binding validation")
        if timeline != staged_timeline:
            raise RequestError("alignment preview return value differs from staged sidecar")
        if is_cancelled(requests_root.parent, request_id, conversation_token):
            raise RequestCancelled("request was cancelled before alignment preview publication")
        os.link(str(staged_path), str(output_path))
    return "ready"


def process_claimed(claimed: Path, request_id: str, root: Path, cli: Path, model: Path, model_hash: str,
                    spec: Path, profiles_path: Path, bindings_path: Path, voice_root: Path,
                    directions_path: Optional[Path], backend: str, timeout: int, binary_version: str,
                    promote_reference_profile: Optional[str] = None,
                    server_endpoint: Optional[str] = None,
                    postprocessor=None, preview_visemes_before_playback: bool = False,
                    rhubarb_cli: Optional[Path] = None,
                    preview_alignment_before_playback: bool = False,
                    aligner_cli: Optional[Path] = None, aligner_model: Optional[Path] = None,
                    aligner_sha256: Optional[str] = None,
                    prepare_presentation_before_playback: bool = False,
                    per_npc_voices: bool = False) -> Dict[str, Any]:
    responses, measurements = root / "responses", root / "measurements"
    logs, temp_dir = measurements / "logs", measurements / ".partial"
    for directory in (responses, measurements, logs, temp_dir):
        directory.mkdir(parents=True, exist_ok=True)
    started = time.monotonic()
    prepared_deadline = (prepared_presentation_deadline(started)
                         if prepare_presentation_before_playback else None)
    request: Optional[NativeRequest] = None
    partial_wav: Optional[Path] = None
    published_wav: Optional[Path] = None
    published_visemes: Optional[Path] = None
    published_alignment: Optional[Path] = None
    try:
        if server_endpoint:
            server_endpoint = validate_server_endpoint(server_endpoint)
        request = read_request(claimed, request_id)
        response_manifest = responses / (request.request_id + ".manifest.json")
        final_wav = responses / (request.request_id + ".wav")
        if response_manifest.exists() or final_wav.exists():
            raise RequestError("requestId already has a response")
        if prepare_presentation_before_playback:
            if preview_visemes_before_playback or preview_alignment_before_playback:
                raise RequestError("prepared presentation mode cannot be combined with preview-only flags")
            if rhubarb_cli is None:
                raise RequestError("prepared presentation requires the validated Rhubarb helper")
        personal_voice_evidence = None
        if per_npc_voices:
            from npc_voice_runtime import resolve_personal_voice, PersonalVoiceCancelled
            voice_deadline = prepared_deadline if prepared_deadline is not None else started + timeout
            cancel_check = lambda: is_cancelled(root, request.request_id, request.conversation_token)

            def synthesize_neutral_anchor(baseline, text, output, remaining):
                if server_endpoint:
                    from server_sse_client import synthesize
                    _synthesize_with_cancel_check(synthesize,
                        (server_endpoint, "breeze", text, baseline["instruction"],
                         None, None, str(output), remaining),
                        {"seed": baseline["seed"], "guidance_scale": DEFAULT_GUIDANCE_SCALE}, cancel_check)
                else:
                    neutral_request = NativeRequest(request.request_id, request.conversation_token,
                        request.person_id, request.display_name, text, None, None)
                    neutral_command = build_cli_command(cli, model, spec, backend, neutral_request,
                        baseline, None, output, TrialDirection("neutral", "normal", "personal_neutral_anchor"))
                    # Match the gallery's neutral identity instruction exactly, without utterance delivery.
                    neutral_command[neutral_command.index("--instruct") + 1] = baseline["instruction"]
                    completed = subprocess.run(neutral_command, check=False, capture_output=True,
                        text=True, timeout=remaining)
                    if completed.returncode != 0:
                        raise RequestError("neutral anchor synthesis failed")

            try:
                profile_id, profile, personal_voice_evidence = resolve_personal_voice(request, voice_root,
                    synthesize_neutral_anchor, cancel_check,
                    lambda: prepared_helper_timeout(voice_deadline, timeout, "personal voice preparation"))
            except PersonalVoiceCancelled as error:
                raise RequestCancelled(str(error)) from error
            profile, voice_ref = load_voice_profile({profile_id: profile}, profile_id, voice_root)
        else:
            profiles = load_profiles(profiles_path)
            profile_id = assign_profile(request.person_id, profiles, bindings_path, request.voice_gender)
            profile, voice_ref = load_voice_profile(profiles, profile_id, voice_root)
        if request.sample_kind == "controlled-voice-contrast":
            direction = TrialDirection(request.controlled_direction["emotion"],
                request.controlled_direction["deliveryMode"], "controlled_voice_contrast")
        else:
            direction = native_context_direction(request.delivery_context)
        # Bridge cancellation is advisory and checked before work and before publication.
        if is_cancelled(root, request.request_id, request.conversation_token):
            raise RequestCancelled("request was cancelled before synthesis")
        partial_wav = temp_dir / (request.request_id + "." + uuid.uuid4().hex + ".wav")
        command = build_cli_command(cli, model, spec, backend, request, profile, voice_ref, partial_wav, direction)
        actual_instruction = command[command.index("--instruct") + 1]
        backend_kind = "audio.cpp-server-sse" if server_endpoint else "audio.cpp-cli"
        recorded_backend = "server-sse" if server_endpoint else backend
        from telemetry import append_event
        telemetry_path = measurements / "events.jsonl"
        append_event(telemetry_path, "text_ready", requestId=request.request_id,
            conversationToken=request.conversation_token, personId=request.person_id,
            sourceTextReadyMonotonicTicks=request.text_ready_ticks,
            sourceTimestampFrequency=request.timestamp_frequency,
            observedAtCoordinator=True)
        append_event(telemetry_path, "synthesis_started", requestId=request.request_id,
            conversationToken=request.conversation_token, personId=request.person_id,
            backend=recorded_backend, backendKind=backend_kind, profileId=profile_id, emotion=direction.emotion,
            deliveryMode=direction.delivery_mode)
        server_metrics = None
        synthesis_timeout = timeout
        if prepared_deadline is not None:
            synthesis_timeout = prepared_helper_timeout(prepared_deadline, timeout, "speech synthesis")
        try:
            if server_endpoint:
                from server_sse_client import synthesize
                server_metrics = _synthesize_with_cancel_check(synthesize,
                    (server_endpoint, "breeze", request.text, actual_instruction,
                     str(voice_ref) if voice_ref else None,
                     profile.get("referenceText") if voice_ref else None, str(partial_wav), synthesis_timeout),
                    {"seed": profile["seed"], "guidance_scale": DEFAULT_GUIDANCE_SCALE},
                    lambda: is_cancelled(root, request.request_id, request.conversation_token))
                synthesis_exit_code = 0
                cli_result = None
            else:
                cli_result = subprocess.run(command, check=False, capture_output=True, text=True, timeout=synthesis_timeout)
                synthesis_exit_code = cli_result.returncode
        except subprocess.TimeoutExpired:
            append_event(telemetry_path, "synthesis_finished", requestId=request.request_id,
                conversationToken=request.conversation_token, personId=request.person_id,
                result="timeout")
            raise
        except Exception as exc:
            append_event(telemetry_path, "synthesis_finished", requestId=request.request_id,
                conversationToken=request.conversation_token, personId=request.person_id,
                result="failed", error=type(exc).__name__)
            raise
        append_event(telemetry_path, "synthesis_finished", requestId=request.request_id,
            conversationToken=request.conversation_token, personId=request.person_id,
            result="success" if synthesis_exit_code == 0 else "failed", exitCode=synthesis_exit_code)
        log_path = logs / (request.request_id + ".log")
        with log_path.open("w", encoding="utf-8", newline="\n") as stream:
            if server_metrics is not None:
                stream.write(json.dumps(server_metrics, ensure_ascii=False, sort_keys=True) + "\n")
            else:
                stream.write(cli_result.stdout or "")
                if cli_result.stderr:
                    stream.write("\n--- stderr ---\n" + cli_result.stderr)
        if synthesis_exit_code != 0:
            raise RequestError("pinned CLI exited with code " + str(synthesis_exit_code))
        if not partial_wav.is_file():
            raise RequestError("pinned CLI returned success without a WAV")
        if is_cancelled(root, request.request_id, request.conversation_token):
            raise RequestCancelled("request was cancelled before publication")
        os.replace(partial_wav, final_wav)
        published_wav = final_wav
        try:
            from tools.BreezeTtsTrial.audio_loudness import normalize_pcm16_wave
        except ModuleNotFoundError:
            from audio_loudness import normalize_pcm16_wave
        loudness_normalization = normalize_pcm16_wave(final_wav)
        wav_info = wave_metadata(final_wav)
        speech_rate = validate_speech_rate(request.text, wav_info["durationSeconds"]) if per_npc_voices else None
        loudness_output = loudness_normalization.get("output", {})
        if (loudness_output.get("sampleRate") != wav_info["sampleRate"] or
                loudness_output.get("channels") != wav_info["channels"] or
                loudness_output.get("frames") != wav_info["sampleFrames"] or
                loudness_output.get("sha256") != sha256_file(final_wav)):
            raise RequestError("loudness normalizer output binding does not match finalized WAV")
        wav_hash = loudness_output["sha256"]
        manifest = {"schemaVersion": 1, "requestId": request.request_id,
            "conversationToken": request.conversation_token, "personId": request.person_id,
            "encoding": "pcm_s16le", "wavFile": final_wav.name,
            "sampleRate": wav_info["sampleRate"], "channels": wav_info["channels"],
            "voiceIdentity": request.voice_identity,
            "sampleFrames": wav_info["sampleFrames"]}
        evidence = {"schemaVersion": 1, "requestId": request.request_id,
            "conversationToken": request.conversation_token, "personId": request.person_id,
            "displayName": request.display_name, "finalizedTextSha256": hashlib.sha256(request.text.encode("utf-8")).hexdigest(),
            "finalizedText": request.text, "profileInstruction": profile["instruction"],
            "voiceIdentity": request.voice_identity,
            "instruction": actual_instruction, "seed": profile["seed"],
            "profileId": profile_id, "voiceGender": request.voice_gender,
            "race": profile.get("race"),
            "sampleKind": request.sample_kind,
            "contrastLabel": request.contrast_label,
            "deliveryContext": request.delivery_context,
            "guidanceScale": DEFAULT_GUIDANCE_SCALE,
            "guidanceScaleProvenance": "coordinator_default",
            "personalityProsody": list(personality_directions(request.delivery_context)) if request.delivery_context else [],
            "personalityProvenance": "delivery_context_v1" if request.delivery_context else "neutral_legacy_default",
            "emotion": direction.emotion, "deliveryMode": direction.delivery_mode,
            "directionProvenance": direction.provenance, "profileSha256": canonical_json_hash(profile),
            "speechRate": speech_rate,
            "voiceReferenceSha256": sha256_file(voice_ref) if voice_ref else None,
            "wavSha256": wav_hash, "modelSha256": model_hash.lower(), "modelFile": model.name,
            "modelPath": str(model.resolve()),
            "modelSizeBytes": model.stat().st_size if model.is_file() else None,
            "loudnessNormalization": loudness_normalization,
            "backend": recorded_backend, "backendKind": backend_kind,
            "audioCppVersion": ("audio.cpp server SSE; server version not queried" if server_endpoint else binary_version),
            "warmServerSse": bool(server_endpoint), "serverEndpoint": server_endpoint,
            "serverSynthesisMs": server_metrics["synthesisMs"] if server_metrics else None,
            "serverFirstAudioMs": server_metrics["firstAudioMs"] if server_metrics else None,
            "serverAudioDeltaCount": server_metrics["audioDeltaCount"] if server_metrics else None,
            "serverTiming": server_metrics["serverTiming"] if server_metrics else None,
            "textReadyMonotonicTicks": request.text_ready_ticks, "timestampFrequency": request.timestamp_frequency,
            "elapsedMilliseconds": round((time.monotonic() - started) * 1000),
            "wavDurationSeconds": wav_info["durationSeconds"]}
        if personal_voice_evidence is not None:
            evidence["personalVoice"] = personal_voice_evidence
        if prepare_presentation_before_playback:
            assert prepared_deadline is not None
            try:
                viseme_timeout = prepared_helper_timeout(prepared_deadline,
                    PREPARED_PRESENTATION_VISEME_TIMEOUT_SECONDS, "viseme preparation")
                publish_preview_visemes(request, final_wav, wav_info, response_manifest,
                    root / "requests", rhubarb_cli, timeout_seconds=viseme_timeout)
                published_visemes = responses / (request.request_id + ".visemes.json")
                if not published_visemes.is_file():
                    raise RequestError("prepared viseme timeline was not published")
                if time.monotonic() >= prepared_deadline:
                    raise RequestError("prepared presentation request deadline expired before manifest publication")
                evidence["visemePreviewStatus"] = "ready"
                evidence["visemePreviewKind"] = "recognizer-viseme-estimate"
                if aligner_cli is not None and aligner_model is not None:
                    alignment_timeout = prepared_helper_timeout(prepared_deadline,
                        PREPARED_PRESENTATION_ALIGNMENT_TIMEOUT_SECONDS, "word alignment")
                    publish_preview_alignment(request, final_wav, wav_info, response_manifest,
                        root / "requests", aligner_cli, aligner_model, aligner_sha256,
                        timeout_seconds=alignment_timeout)
                    published_alignment = responses / (request.request_id + ".alignment.json")
                    if not published_alignment.is_file():
                        raise RequestError("prepared word alignment was not published")
                    evidence["alignmentPreviewStatus"] = "ready"
                    evidence["alignmentPreviewKind"] = "forced-word-alignment"
                    manifest["presentationReady"] = True
                else:
                    evidence["alignmentPreviewStatus"] = "unavailable"
                    manifest["presentationReady"] = False
            except RequestCancelled:
                raise
            except Exception as viseme_error:
                # A missing preparation sidecar must not publish a prepared
                # manifest. Preserve the WAV for the bounded visual fallback.
                (responses / (request.request_id + ".visemes.json")).unlink(missing_ok=True)
                (responses / (request.request_id + ".alignment.json")).unlink(missing_ok=True)
                published_visemes = None
                published_alignment = None
                evidence["presentationStatus"] = "bounded-fallback"
                evidence["presentationErrorType"] = type(viseme_error).__name__
                evidence["presentationError"] = str(viseme_error)[:400]
                manifest["presentationReady"] = False
        if preview_visemes_before_playback:
            if rhubarb_cli is None:
                raise RequestError("--preview-visemes-before-playback requires --rhubarb-cli")
            publish_preview_visemes(request, final_wav, wav_info, response_manifest,
                                    root / "requests", rhubarb_cli)
            published_visemes = responses / (request.request_id + ".visemes.json")
            evidence["visemePreviewStatus"] = "ready"
            evidence["visemePreviewKind"] = "recognizer-viseme-estimate"
        if preview_alignment_before_playback:
            if aligner_cli is None or aligner_model is None:
                raise RequestError("--preview-alignment-before-playback requires --aligner-cli and --aligner-model")
            publish_preview_alignment(request, final_wav, wav_info, response_manifest,
                root / "requests", aligner_cli, aligner_model, aligner_sha256)
            published_alignment = responses / (request.request_id + ".alignment.json")
            evidence["alignmentPreviewStatus"] = "ready"
            evidence["alignmentPreviewKind"] = "forced-word-alignment"
        if is_cancelled(root, request.request_id, request.conversation_token):
            raise RequestCancelled("request was cancelled before manifest publication")
        atomic_json(measurements / (request.request_id + ".evidence.json"), evidence)
        if is_cancelled(root, request.request_id, request.conversation_token):
            raise RequestCancelled("request was cancelled before manifest publication")
        atomic_json(response_manifest, manifest)
        if is_cancelled(root, request.request_id, request.conversation_token):
            raise RequestCancelled("request was cancelled during manifest publication")
        if postprocessor is not None and not (prepare_presentation_before_playback or
                preview_visemes_before_playback or preview_alignment_before_playback):
            from postprocess_worker import PostprocessJob
            postprocessor.submit(PostprocessJob(request.request_id, request.conversation_token,
                request.person_id, None, final_wav, request.text,
                cancellation_marker(root, request.request_id), responses, responses))
        if promote_reference_profile == profile_id:
            _promote_first_reference(request, profile, profile_id, final_wav,
                measurements / (request.request_id + ".evidence.json"), voice_root, profiles_path, measurements,
                wav_info, wav_hash)
        claimed.unlink(missing_ok=True)
        return {"status": "ready", **manifest}
    except Exception as error:
        if isinstance(error, RequestCancelled):
            if partial_wav is not None:
                partial_wav.unlink(missing_ok=True)
            if published_wav is not None:
                published_wav.unlink(missing_ok=True)
            if published_visemes is not None:
                published_visemes.unlink(missing_ok=True)
            if published_alignment is not None:
                published_alignment.unlink(missing_ok=True)
            (responses / (request_id + ".manifest.json")).unlink(missing_ok=True)
            claimed.unlink(missing_ok=True)
            return {"schemaVersion": 1, "requestId": request_id, "status": "cancelled"}
        error_row = {"schemaVersion": 1, "requestId": request_id, "status": "failed",
                     "error": type(error).__name__, "elapsedMilliseconds": round((time.monotonic() - started) * 1000)}
        atomic_json(measurements / (request_id + ".failed.json"), error_row)
        if published_wav is not None:
            published_wav.unlink(missing_ok=True)
        if published_visemes is not None:
            published_visemes.unlink(missing_ok=True)
        if published_alignment is not None:
            published_alignment.unlink(missing_ok=True)

        failure_binding = ({"requestId": request.request_id,
            "conversationToken": request.conversation_token, "personId": request.person_id}
            if request is not None else safe_failure_binding(claimed, request_id))
        if failure_binding is not None:
            failure_path = responses / (request_id + ".failed.json")
            success_manifest_path = responses / (request_id + ".manifest.json")
            success_wave_path = responses / (request_id + ".wav")
            if (not failure_path.exists() and not success_manifest_path.exists() and
                    not success_wave_path.exists()):
                terminal = {"schemaVersion": 1, **failure_binding, "status": "failed"}
                try:
                    atomic_json(failure_path, terminal)
                except OSError:
                    pass

        failed_dir = measurements / "failed-requests"
        failed_dir.mkdir(parents=True, exist_ok=True)
        if claimed.exists():
            os.replace(claimed, failed_dir / (request_id + "." + uuid.uuid4().hex + ".request.json"))
        return error_row
    finally:
        if partial_wav is not None:
            partial_wav.unlink(missing_ok=True)


def process_live_stream(stream_input: Path, root: Path, model: Path, model_hash: str,
                        spec: Path, profiles_path: Path, bindings_path: Path,
                        voice_root: Path, directions_path: Optional[Path],
                        timeout: int, server_endpoint: str, poll_interval: float = 0.02,
                        promote_reference_profile: Optional[str] = None,
                        postprocessor=None) -> Dict[str, Any]:
    """Consume ordered sentence files and publish live PCM plus a replay WAV."""
    from live_pcm_spool import (LivePcmSpool, StreamCancelled, read_sentence, read_stream_begin,
                                validate_pending_sentence_queue)
    try:
        from tools.BreezeTtsTrial.audio_loudness import StreamingPcmNormalizer
    except ModuleNotFoundError:
        from audio_loudness import StreamingPcmNormalizer

    stream_id = stream_input.name
    spool = None
    final_wav: Optional[Path] = None
    try:
        if server_endpoint:
            server_endpoint = validate_server_endpoint(server_endpoint)
        begin_path = stream_input / "begin.json"
        begin = read_stream_begin(begin_path, stream_id)
        spool = LivePcmSpool(root / "responses" / stream_id, stream_id,
            begin.conversation_token, begin.person_id, backpressure_timeout=timeout,
            cancel_path=stream_input / "cancel.json")
        live_pcm = _LivePcmStreamNormalizer(
            StreamingPcmNormalizer(sample_rate=24000, channels=1), spool.publish_delta)
        sentence_dir = spool.stream_dir / "sentences"
        sentence_dir.mkdir()
        if not server_endpoint:
            raise RequestError("streaming-live-pcm-v1 requires the warm audio.cpp SSE endpoint")
        context = parse_delivery_context(begin.delivery_context, begin.person_id)
        profiles = load_profiles(profiles_path)
        profile_id = assign_profile(begin.person_id, profiles, bindings_path, begin.voice_gender)
        profile, voice_ref = load_voice_profile(profiles, profile_id, voice_root)
        direction = load_direction(begin.person_id, directions_path)
        responses, measurements = root / "responses", root / "measurements"
        measurements.mkdir(parents=True, exist_ok=True)
        telemetry_path = measurements / "events.jsonl"
        from telemetry import append_event
        pcm_all = bytearray()
        transcript_parts = []
        sentence_rows = []
        sentence_sequence = 0
        stream_started = time.monotonic()
        deadline = stream_started + timeout

        def check_terminal_marker(filename: str) -> Optional[Dict[str, Any]]:
            path = stream_input / filename
            if not path.exists():
                return None
            if path.stat().st_size > 64 * 1024:
                raise RequestError("stream terminal marker exceeds size limit")
            value = json.loads(path.read_text(encoding="utf-8-sig"))
            if (not isinstance(value, dict) or value.get("schemaVersion") != 1 or
                    value.get("streamId") != stream_id or
                    value.get("conversationToken") != begin.conversation_token or
                    value.get("personId") != begin.person_id):
                raise RequestError("stream terminal marker binding does not match begin")
            return value

        while True:
            if (stream_input / "cancel.json").exists():
                raise RequestCancelled("stream was cancelled by Unity")
            if time.monotonic() >= deadline:
                raise TimeoutError("live TTS stream exceeded its time limit")

            failed = check_terminal_marker("failed.json")
            if failed is not None:
                heard = failed.get("heardText", "")
                failure_kind = failed.get("failureKind", "native-generation-failed")
                if not isinstance(heard, str) or len(heard) > MAX_TEXT_CHARS:
                    raise RequestError("failed marker heardText is invalid")
                if not isinstance(failure_kind, str) or not failure_kind:
                    raise RequestError("failed marker failureKind is invalid")
                spool.finish("failed", error_code=failure_kind)
                atomic_json(root / "measurements" / (stream_id + ".failed.json"), {
                    "schemaVersion": 1, "streamId": stream_id, "status": "failed",
                    "errorCode": failure_kind, "heardText": heard,
                    "emittedSentenceCount": sentence_sequence})
                return {"schemaVersion": 1, "streamId": stream_id, "status": "failed",
                        "errorCode": failure_kind, "heardText": heard}

            sentence_path = stream_input / ("{:04d}.json".format(sentence_sequence))
            validate_pending_sentence_queue(stream_input, begin, sentence_sequence)
            if sentence_path.is_file():
                text = read_sentence(sentence_path, begin, sentence_sequence)
                sentence_value = json.loads(sentence_path.read_text(encoding="utf-8-sig"))
                ready_ticks = sentence_value.get("textReadyMonotonicTicks")
                frequency = sentence_value.get("timestampFrequency")
                if ready_ticks is not None and (isinstance(ready_ticks, bool) or not isinstance(ready_ticks, int) or ready_ticks < 0):
                    raise RequestError("sentence textReadyMonotonicTicks is invalid")
                if frequency is not None and (isinstance(frequency, bool) or not isinstance(frequency, int) or frequency <= 0):
                    raise RequestError("sentence timestampFrequency is invalid")
                if (ready_ticks is None) != (frequency is None):
                    raise RequestError("sentence source timestamp and frequency must be supplied together")
                if sentence_sequence >= 256:
                    raise RequestError("live stream exceeds the 256 sentence limit")
                request = NativeRequest(stream_id, begin.conversation_token, begin.person_id,
                    begin.display_name, text, ready_ticks, frequency, begin.voice_gender,
                    context, "native-dialogue")
                command = build_cli_command(Path("audiocpp_cli.exe"), model, spec, "cuda", request,
                    profile, voice_ref, Path("unused.wav"), direction)
                actual_instruction = command[command.index("--instruct") + 1]
                partial_wav = measurements / ".partial" / (stream_id + ".{:04d}.wav".format(sentence_sequence))
                partial_wav.parent.mkdir(parents=True, exist_ok=True)
                append_event(telemetry_path, "text_ready", requestId=stream_id,
                    conversationToken=begin.conversation_token, personId=begin.person_id,
                    sentenceSequence=sentence_sequence, sourceTextReadyMonotonicTicks=ready_ticks,
                    sourceTimestampFrequency=frequency, observedAtCoordinator=True)
                append_event(telemetry_path, "synthesis_started", requestId=stream_id,
                    conversationToken=begin.conversation_token, personId=begin.person_id,
                    sentenceSequence=sentence_sequence, backend="server-sse",
                    backendKind="audio.cpp-server-sse", profileId=profile_id,
                    emotion=direction.emotion, deliveryMode=direction.delivery_mode)
                segment_metrics = None
                sentence_wav = sentence_dir / ("{}.{}.wav".format(stream_id,
                    "{:04d}".format(sentence_sequence)))
                normalized_sentence_temp = sentence_wav.with_name(
                    "." + sentence_wav.name + "." + uuid.uuid4().hex + ".tmp.wav")
                try:
                    live_pcm.begin_sentence()
                    with wave.open(str(normalized_sentence_temp), "wb") as normalized_writer:
                        normalized_writer.setnchannels(1)
                        normalized_writer.setsampwidth(2)
                        normalized_writer.setframerate(24000)
                        try:
                            from server_sse_client import synthesize
                        except ModuleNotFoundError:
                            from tools.BreezeTtsTrial.server_sse_client import synthesize
                        segment_metrics = _synthesize_with_cancel_check(synthesize,
                            (server_endpoint, "breeze", text, actual_instruction,
                             str(voice_ref) if voice_ref else None,
                             profile.get("referenceText") if voice_ref else None, str(partial_wav), timeout),
                            {"seed": profile["seed"], "guidance_scale": DEFAULT_GUIDANCE_SCALE,
                             "on_pcm_delta": lambda delta: live_pcm.process_delta(
                                 delta, sentence_sequence, normalized_writer)},
                            lambda: _stream_cancelled(spool))
                    sentence_normalization = live_pcm.finish_sentence()
                    raw_pcm = pcm16_wave_digest(partial_wav)
                    if (raw_pcm["sampleFrames"] * 2 != sentence_normalization["inputBytes"] or
                            raw_pcm["pcmSha256"] != sentence_normalization["inputPcmSha256"]):
                        raise RequestError("raw SSE PCM deltas differ from the sentence WAV")
                    normalized_pcm = pcm16_wave_digest(normalized_sentence_temp)
                    if (normalized_pcm["sampleFrames"] * 2 != sentence_normalization["outputBytes"] or
                            normalized_pcm["pcmSha256"] != sentence_normalization["outputPcmSha256"] or
                            normalized_pcm["sampleFrames"] != raw_pcm["sampleFrames"]):
                        raise RequestError("normalized sentence WAV differs from live PCM publication")
                    os.replace(normalized_sentence_temp, sentence_wav)
                    with wave.open(str(sentence_wav), "rb") as wav:
                        segment_pcm = wav.readframes(wav.getnframes())
                    if len(pcm_all) + len(segment_pcm) > 64 * 1024 * 1024:
                        raise RequestError("live conversation PCM exceeds 64 MiB limit")
                    pcm_all.extend(segment_pcm)
                finally:
                    partial_wav.unlink(missing_ok=True)
                    normalized_sentence_temp.unlink(missing_ok=True)
                append_event(telemetry_path, "synthesis_finished", requestId=stream_id,
                    conversationToken=begin.conversation_token, personId=begin.person_id,
                    sentenceSequence=sentence_sequence, result="success")
                postprocess_status = "disabled"
                if postprocessor is not None:
                    from postprocess_worker import PostprocessJob
                    postprocess_status = postprocessor.submit(PostprocessJob(stream_id,
                        begin.conversation_token, begin.person_id, sentence_sequence,
                        sentence_wav, text, stream_input / "cancel.json",
                        spool.stream_dir, sentence_dir))
                sentence_path.unlink(missing_ok=True)
                spool.mark_sentence_consumed(sentence_sequence)
                transcript_parts.append(text)
                sentence_rows.append({"sequence": sentence_sequence,
                    "textSha256": hashlib.sha256(text.encode("utf-8")).hexdigest(),
                    "textReadyMonotonicTicks": ready_ticks,
                    "timestampFrequency": frequency,
                    "synthesisMs": segment_metrics["synthesisMs"],
                    "firstAudioMs": segment_metrics["firstAudioMs"],
                    "audioDeltaCount": segment_metrics["audioDeltaCount"],
                    "serverTiming": segment_metrics["serverTiming"],
                    "sampleFrames": sentence_normalization["outputBytes"] // 2,
                    "inputPcmSha256": sentence_normalization["inputPcmSha256"],
                    "normalizedPcmSha256": sentence_normalization["outputPcmSha256"],
                    "postprocessStatus": postprocess_status})
                sentence_sequence += 1
                continue

            completed = check_terminal_marker("complete.json")
            if completed is not None:
                final_text = completed.get("finalTranscript")
                if not isinstance(final_text, str) or not final_text.strip() or len(final_text) > MAX_TEXT_CHARS:
                    raise RequestError("complete marker finalTranscript is invalid")
                if "".join(transcript_parts) != final_text:
                    raise RequestError("finalTranscript does not exactly match accepted sentence text")
                if not pcm_all or len(pcm_all) % 2:
                    raise RequestError("stream has no complete PCM16 audio")
                frames = len(pcm_all) // 2
                if (spool.total_frames != frames or live_pcm.output_bytes != len(pcm_all) or
                        live_pcm.output_digest.hexdigest() != hashlib.sha256(pcm_all).hexdigest()):
                    raise RequestError("replay WAV PCM differs from normalized live spool PCM")
                responses.mkdir(parents=True, exist_ok=True)
                final_wav = responses / (stream_id + ".wav")
                if final_wav.exists() or (responses / (stream_id + ".manifest.json")).exists():
                    raise RequestError("streamId already has a response")
                with wave.open(str(final_wav), "wb") as writer:
                    writer.setnchannels(1)
                    writer.setsampwidth(2)
                    writer.setframerate(24000)
                    writer.writeframes(pcm_all)
                wav_info = {"sampleRate": 24000, "channels": 1, "sampleFrames": frames,
                            "durationSeconds": round(frames / 24000.0, 3)}
                wav_hash = sha256_file(final_wav)
                manifest = {"schemaVersion": 1, "requestId": stream_id,
                    "conversationToken": begin.conversation_token, "personId": begin.person_id,
                    "encoding": "pcm_s16le", "wavFile": final_wav.name,
                    "sampleRate": 24000, "channels": 1,
                    "voiceIdentity": begin.voice_identity, "sampleFrames": frames}
                evidence = {"schemaVersion": 1, "requestId": stream_id,
                    "conversationToken": begin.conversation_token, "personId": begin.person_id,
                    "displayName": begin.display_name,
                    "finalizedTextSha256": hashlib.sha256(final_text.encode("utf-8")).hexdigest(),
                    "finalizedText": final_text, "voiceIdentity": begin.voice_identity,
                    "profileInstruction": profile["instruction"], "instruction": actual_instruction if sentence_rows else None,
                    "seed": profile["seed"], "profileId": profile_id, "voiceGender": begin.voice_gender,
                    "deliveryContext": context, "guidanceScale": DEFAULT_GUIDANCE_SCALE,
                    "guidanceScaleProvenance": "coordinator_default",
                    "personalityProsody": list(personality_directions(context)) if context else [],
                    "personalityProvenance": "delivery_context_v1" if context else "neutral_legacy_default",
                    "emotion": direction.emotion, "deliveryMode": direction.delivery_mode,
                    "directionProvenance": direction.provenance,
                    "profileSha256": canonical_json_hash(profile),
                    "voiceReferenceSha256": sha256_file(voice_ref) if voice_ref else None,
                    "wavSha256": wav_hash, "modelSha256": model_hash.lower(), "modelFile": model.name,
                    "modelPath": str(model.resolve()),
                    "modelSizeBytes": model.stat().st_size if model.is_file() else None,
                    "livePcmNormalization": live_pcm.evidence(sample_frames=frames, wav_sha256=wav_hash),
                    "backend": "server-sse", "backendKind": "audio.cpp-server-sse",
                    "audioCppVersion": "audio.cpp server SSE; server version not queried",
                    "warmServerSse": True, "serverEndpoint": server_endpoint,
                    "sentences": sentence_rows, "textReadyMonotonicTicks": None,
                    "timestampFrequency": None,
                    "elapsedMilliseconds": round((time.monotonic() - stream_started) * 1000),
                    "wavDurationSeconds": wav_info["durationSeconds"]}
                atomic_json(measurements / (stream_id + ".evidence.json"), evidence)
                atomic_json(responses / (stream_id + ".manifest.json"), manifest)
                spool.finish("audio_complete", final_wav_file=final_wav.name)
                final_postprocess_status = "disabled"
                if postprocessor is not None:
                    try:
                        from postprocess_worker import PostprocessJob
                        final_postprocess_status = postprocessor.submit(PostprocessJob(stream_id,
                            begin.conversation_token, begin.person_id, None, final_wav, final_text,
                            stream_input / "cancel.json", spool.stream_dir, responses))
                    except Exception as error:
                        # Sidecars are optional; once audio_complete is published, a
                        # worker failure must not revoke or invalidate the replay WAV.
                        final_postprocess_status = "failed-submit"
                        evidence["finalPostprocessStatus"] = final_postprocess_status
                        evidence["finalPostprocessError"] = type(error).__name__
                        try:
                            atomic_json(measurements / (stream_id + ".evidence.json"), evidence)
                        except Exception:
                            pass
                if promote_reference_profile == profile_id:
                    try:
                        promotion_request = NativeRequest(stream_id, begin.conversation_token, begin.person_id,
                            begin.display_name, final_text, None, None, begin.voice_gender, context)
                        _promote_first_reference(promotion_request, profile, profile_id, final_wav,
                            measurements / (stream_id + ".evidence.json"), voice_root, profiles_path,
                            measurements, wav_info, wav_hash)
                    except Exception:
                        # Reference promotion is opt-in audit work, not part of audio validity.
                        pass
                if "finalPostprocessStatus" not in evidence:
                    evidence["finalPostprocessStatus"] = final_postprocess_status
                    try:
                        atomic_json(measurements / (stream_id + ".evidence.json"), evidence)
                    except Exception:
                        pass
                return {"schemaVersion": 1, "streamId": stream_id, "status": "audio_complete",
                        "wavFile": final_wav.name, "sampleFrames": frames}
            time.sleep(poll_interval)
    except (StreamCancelled, RequestCancelled):
        if spool is not None:
            try:
                spool.finish("cancelled", error_code="conversation-closed")
            except Exception:
                # Best effort terminal marker; response cleanup still must run.
                pass
        if final_wav is not None:
            final_wav.unlink(missing_ok=True)
        responses = root / "responses"
        measurements = root / "measurements"
        (responses / (stream_id + ".manifest.json")).unlink(missing_ok=True)
        (measurements / (stream_id + ".evidence.json")).unlink(missing_ok=True)
        return {"schemaVersion": 1, "streamId": stream_id, "status": "cancelled"}
    except Exception as error:
        if spool is not None:
            try:
                spool.finish("failed", error_code=type(error).__name__)
            except Exception:
                pass
        if final_wav is not None:
            final_wav.unlink(missing_ok=True)
        responses = root / "responses"
        measurements = root / "measurements"
        (responses / (stream_id + ".manifest.json")).unlink(missing_ok=True)
        (measurements / (stream_id + ".evidence.json")).unlink(missing_ok=True)
        try:
            measurements.mkdir(parents=True, exist_ok=True)
            atomic_json(measurements / (stream_id + ".failed.json"), {
                "schemaVersion": 1, "streamId": stream_id, "status": "failed",
                "error": type(error).__name__, "message": str(error)[:512]})
        except Exception:
            pass
        return {"schemaVersion": 1, "streamId": stream_id, "status": "failed",
                "error": type(error).__name__, "message": str(error)[:512]}


def run(args: argparse.Namespace) -> int:
    if args.timeout < 1 or args.poll_interval <= 0:
        raise RequestError("timeout and polling interval must be positive")
    if getattr(args, "await_native_root", False) and not args.watch:
        raise RequestError("--await-native-root requires --watch")
    ready_file_arg = getattr(args, "ready_file", None)
    ready_config_id = getattr(args, "ready_config_id", None)
    if ready_file_arg and not args.watch:
        raise RequestError("--ready-file requires --watch")
    if ready_file_arg and getattr(args, "await_native_root", False):
        raise RequestError("--ready-file cannot be combined with --await-native-root")
    if ready_config_id is not None:
        if not ready_file_arg or not valid_sha256(ready_config_id):
            raise RequestError("--ready-config-id requires --ready-file and a 64-character SHA-256")
        ready_config_id = ready_config_id.lower()
    cli, model, spec = args.cli.resolve(), args.model.resolve(), args.spec.resolve()
    root, voice_root = args.root.resolve(), args.voice_root.resolve()
    from postprocess_worker import PostprocessSettings
    postprocess_settings = PostprocessSettings(
        aligner_cli=args.aligner_cli.resolve() if args.aligner_cli else None,
        aligner_model=args.aligner_model.resolve() if args.aligner_model else None,
        aligner_sha256=args.aligner_model_sha256,
        rhubarb_cli=args.rhubarb_cli.resolve() if args.rhubarb_cli else None)
    postprocess_settings.validate()
    prepare_presentation = getattr(args, "prepare_presentation_before_playback", False)
    if prepare_presentation:
        if (getattr(args, "preview_visemes_before_playback", False) or
                getattr(args, "preview_alignment_before_playback", False)):
            raise RequestError("--prepare-presentation-before-playback cannot be combined with preview-only flags")
        if not postprocess_settings.rhubarb_cli:
            raise RequestError("--prepare-presentation-before-playback requires the Rhubarb helper")
    if getattr(args, "preview_visemes_before_playback", False) and not postprocess_settings.rhubarb_cli:
        raise RequestError("--preview-visemes-before-playback requires --rhubarb-cli")
    if getattr(args, "preview_alignment_before_playback", False) and not postprocess_settings.aligner_cli:
        raise RequestError("--preview-alignment-before-playback requires --aligner-cli and --aligner-model")
    if (getattr(args, "preview_alignment_before_playback", False) or
            getattr(args, "preview_visemes_before_playback", False)):
        if postprocess_settings.aligner_cli and not getattr(args, "preview_alignment_before_playback", False):
            raise RequestError("preview mode with alignment configured also requires --preview-alignment-before-playback")
        if postprocess_settings.rhubarb_cli and not getattr(args, "preview_visemes_before_playback", False):
            raise RequestError("preview mode with Rhubarb configured also requires --preview-visemes-before-playback")
    postprocessor = None
    worker_settings = postprocess_settings
    if prepare_presentation:
        worker_settings = PostprocessSettings(
            aligner_cli=postprocess_settings.aligner_cli,
            aligner_model=postprocess_settings.aligner_model,
            aligner_sha256=postprocess_settings.aligner_sha256)
    if worker_settings.enabled:
        from postprocess_worker import BoundedPostprocessWorker
        postprocessor = BoundedPostprocessWorker(worker_settings, timeout_seconds=180)
    if args.server_endpoint:
        args.server_endpoint = validate_server_endpoint(args.server_endpoint)
        verify_model_assets(args.model_sha256, model, spec)
        binary_version = "audio.cpp server SSE; server version not queried"
    else:
        binary_version = verify_backend(cli, args.model_sha256, model, spec, args.backend)
    requests, working = root / "requests", root / "working"
    if getattr(args, "await_native_root", False):
        # Preverify the warm coordinator before the guarded game starter runs.
        # The native bridge creates the root; leave its fresh-directory gate intact.
        deadline = time.monotonic() + args.timeout
        while not requests.is_dir():
            if time.monotonic() >= deadline:
                raise RequestError("native request directory did not appear before timeout")
            time.sleep(args.poll_interval)
    for directory in (requests, working, root / "responses", root / "measurements"):
        directory.mkdir(parents=True, exist_ok=True)
    if ready_file_arg:
        ready_file = Path(ready_file_arg).resolve()
        ready_file.parent.mkdir(parents=True, exist_ok=True)
        atomic_json(ready_file, {
            "schemaVersion": 1,
            "status": "ready",
            "processId": os.getpid(),
            "root": str(root),
            "modelSha256": args.model_sha256.lower(),
            "serverEndpoint": args.server_endpoint,
            "configIdentity": ready_config_id,
        })
    processed = 0
    processed_live_ids = set()
    while True:
        stream_input = next_live_stream_to_process(requests, root, processed_live_ids)
        if stream_input is not None:
            result = process_live_stream(stream_input, root, model, args.model_sha256.lower(), spec,
                args.profiles.resolve(), args.bindings.resolve(), voice_root,
                args.directions.resolve() if args.directions else None, args.timeout,
                args.server_endpoint, args.poll_interval,
                promote_reference_profile=args.promote_reference_profile,
                postprocessor=postprocessor)
            record_live_stream_result(root, result, processed_live_ids)
            print(json.dumps(result, ensure_ascii=False, separators=(",", ":")))
            processed += 1
            if not args.watch:
                return 0 if result["status"] == "audio_complete" else 2
            continue
        candidates = sorted(path for path in requests.glob("*.request.json") if path.is_file())
        claim = None
        for source in candidates:
            request_id = source.name.removesuffix(".request.json")
            if cancellation_marker(root, request_id).exists():
                continue
            claim = claim_request(source, working)
            if claim is not None:
                break
        if claim is None:
            if args.watch:
                time.sleep(args.poll_interval)
                continue
            return 0 if processed else 1
        claimed, request_id = claim
        result = process_claimed(claimed, request_id, root, cli, model, args.model_sha256.lower(), spec,
            args.profiles.resolve(), args.bindings.resolve(), voice_root,
            args.directions.resolve() if args.directions else None, args.backend, args.timeout, binary_version,
            args.promote_reference_profile, server_endpoint=args.server_endpoint,
            postprocessor=postprocessor,
            preview_visemes_before_playback=getattr(args, "preview_visemes_before_playback", False),
            rhubarb_cli=postprocess_settings.rhubarb_cli,
            preview_alignment_before_playback=getattr(args, "preview_alignment_before_playback", False),
            aligner_cli=postprocess_settings.aligner_cli,
            aligner_model=postprocess_settings.aligner_model,
            aligner_sha256=postprocess_settings.aligner_sha256,
            prepare_presentation_before_playback=prepare_presentation,
            per_npc_voices=getattr(args, "per_npc_voices", False))
        print(json.dumps(result, ensure_ascii=False, separators=(",", ":")))
        processed += 1
        if not args.watch:
            return 0 if result["status"] == "ready" else 2


def make_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cli", type=Path, required=True, help="pinned audiocpp_cli.exe from versioned runtime")
    parser.add_argument("--model", type=Path, required=True, help="fully downloaded Breeze GGUF model")
    parser.add_argument("--model-sha256", required=True, help="exact expected model SHA-256")
    parser.add_argument("--spec", type=Path, required=True, help="pinned model_specs/breeze_tts.json")
    parser.add_argument("--profiles", type=Path, required=True, help="trusted local profile registry JSON")
    parser.add_argument("--bindings", type=Path, required=True, help="durable local personId-to-profileId JSON")
    parser.add_argument("--directions", type=Path, help="optional coordinator-only controlled-trial direction overrides")
    parser.add_argument("--voice-root", type=Path, required=True, help="root for trusted voice-reference WAVs")
    parser.add_argument("--per-npc-voices", action="store_true",
        help="persist an individual Gaussian voice and neutral reference for each native personId")
    parser.add_argument("--promote-reference-profile", help="explicitly promote first eligible native female dialogue WAV for this profile")
    parser.add_argument("--root", type=Path, required=True, help="native bridge request/response root")
    parser.add_argument("--backend", choices=("cuda", "cpu"), default="cuda")
    parser.add_argument("--server-endpoint", help="optional loopback audio.cpp SSE endpoint; omitted uses pinned CLI")
    parser.add_argument("--aligner-cli", type=Path, help="opt-in local audio.cpp CLI for CPU forced alignment")
    parser.add_argument("--aligner-model", type=Path, help="opt-in local Qwen3 Forced Aligner GGUF; never downloaded")
    parser.add_argument("--aligner-model-sha256",
        default="75209490b11cec2b0db749ca5f4ff92266f58efd30f7fd04d9eb2a3ac9cc929f",
        help="expected local aligner model SHA-256")
    parser.add_argument("--rhubarb-cli", type=Path, help="opt-in local Rhubarb executable; never downloaded")
    parser.add_argument("--preview-visemes-before-playback", action="store_true",
        help="test-only: synchronously stage and validate Rhubarb cues before publishing a full-WAV response")
    parser.add_argument("--preview-alignment-before-playback", action="store_true",
        help="test-only: synchronously stage and validate forced word alignment before publishing a full-WAV response")
    parser.add_argument("--prepare-presentation-before-playback", action="store_true",
        help="production full-WAV mode: require Rhubarb cues before playback; run configured word alignment asynchronously")
    parser.add_argument("--timeout", type=int, default=300)
    parser.add_argument("--watch", action="store_true", help="poll canonical requests until interrupted")
    parser.add_argument("--await-native-root", action="store_true", help="preverify, then wait for the native bridge to create requests before watch mode")
    parser.add_argument("--ready-file", type=Path,
        help="atomically publish coordinator readiness after backend validation and request-root creation")
    parser.add_argument("--ready-config-id", help="launcher-supplied lowercase SHA-256 identity for the ordered coordinator configuration")
    parser.add_argument("--poll-interval", type=float, default=0.25)
    return parser


if __name__ == "__main__":
    try:
        raise SystemExit(run(make_parser().parse_args()))
    except (RequestError, OSError, json.JSONDecodeError, subprocess.SubprocessError) as exc:
        print("Breeze TTS coordinator: " + str(exc), file=sys.stderr)
        raise SystemExit(2)
