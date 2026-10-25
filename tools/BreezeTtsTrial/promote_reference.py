#!/usr/bin/env python3
"""Validate and explicitly promote one synthesized WAV as a stable profile reference."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
from protocol_text import valid_identifier, valid_sha256
import shutil
import sys
import uuid
import wave
from pathlib import Path
from typing import Any, Dict


MAX_WAV_BYTES = 16 * 1024 * 1024
MAX_DURATION_SECONDS = 60


class PromotionError(ValueError):
    pass


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def _contained(root: Path, relative: str) -> Path:
    candidate = (root / relative).resolve()
    try:
        if os.path.commonpath((str(candidate), str(root.resolve()))) != str(root.resolve()):
            raise PromotionError("reference asset path escapes voice-root")
    except (OSError, ValueError):
        raise PromotionError("reference asset path must resolve inside voice-root")
    return candidate


def _read_json(path: Path, label: str) -> Any:
    try:
        with path.open("r", encoding="utf-8-sig") as stream:
            return json.load(stream)
    except (OSError, json.JSONDecodeError) as exc:
        raise PromotionError("cannot read {}: {}".format(label, exc))


def _wav_info(path: Path) -> Dict[str, Any]:
    size = path.stat().st_size
    if size < 44 or size > MAX_WAV_BYTES:
        raise PromotionError("source WAV is empty, truncated, or exceeds 16 MiB")
    try:
        with wave.open(str(path), "rb") as wav:
            frames, rate, channels = wav.getnframes(), wav.getframerate(), wav.getnchannels()
            width, compression = wav.getsampwidth(), wav.getcomptype()
    except (EOFError, OSError, wave.Error) as exc:
        raise PromotionError("source is not a valid WAV: {}".format(exc))
    if (compression != "NONE" or width != 2 or frames <= 0 or rate < 8000 or rate > 192000 or
            channels not in {1, 2} or frames > rate * MAX_DURATION_SECONDS):
        raise PromotionError("reference must be uncompressed PCM16 mono/stereo, 8–192 kHz, at most 60 seconds")
    return {"sampleRate": rate, "channels": channels, "sampleFrames": frames,
            "durationSeconds": round(frames / float(rate), 3)}


def _load_profile_registry(path: Path, profile_id: str, expected_gender: str) -> tuple[Dict[str, Any], Dict[str, Any]]:
    document = _read_json(path, "trusted profiles registry")
    if (not isinstance(document, dict) or document.get("schemaVersion") != 1 or
            not isinstance(document.get("profiles"), dict)):
        raise PromotionError("trusted profiles registry must use schemaVersion 1")
    profile = document["profiles"].get(profile_id)
    if not isinstance(profile, dict):
        raise PromotionError("expected profileId is missing from trusted profiles registry")
    if profile.get("perceivedGender") != expected_gender:
        raise PromotionError("expected perceived gender does not match trusted profile")
    if not isinstance(profile.get("instruction"), str) or not profile["instruction"].strip():
        raise PromotionError("trusted profile instruction is missing")
    seed = profile.get("seed")
    if isinstance(seed, bool) or not isinstance(seed, int) or seed < 0 or seed > 2**31 - 1:
        raise PromotionError("trusted profile seed is invalid")
    return document, profile


def _atomic_json(path: Path, value: Dict[str, Any]) -> None:
    temporary = path.with_name(path.name + "." + uuid.uuid4().hex + ".tmp")
    try:
        with temporary.open("w", encoding="utf-8", newline="\n") as stream:
            json.dump(value, stream, ensure_ascii=False, indent=2)
            stream.write("\n")
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)


def _atomic_copy(source: Path, destination: Path, expected_hash: str) -> None:
    destination.parent.mkdir(parents=True, exist_ok=True)
    temporary = destination.with_name(destination.name + "." + uuid.uuid4().hex + ".tmp")
    try:
        with source.open("rb") as incoming, temporary.open("xb") as outgoing:
            shutil.copyfileobj(incoming, outgoing, length=1024 * 1024)
            outgoing.flush()
            os.fsync(outgoing.fileno())
        if sha256_file(temporary) != expected_hash:
            raise PromotionError("source WAV changed while copying; reference was not promoted")
        os.replace(temporary, destination)
    finally:
        temporary.unlink(missing_ok=True)


def promote_reference(source_wav: Path, evidence_path: Path, profiles_path: Path, voice_root: Path,
                      profile_id: str, expected_gender: str, apply: bool = False,
                      replace: bool = False) -> Dict[str, Any]:
    if not valid_identifier(profile_id):
        raise PromotionError("profileId must be filename-safe")
    if not isinstance(expected_gender, str) or expected_gender not in {"female", "male"}:
        raise PromotionError("expected gender must be female or male")
    source_wav, evidence_path, profiles_path, voice_root = (
        source_wav.resolve(), evidence_path.resolve(), profiles_path.resolve(), voice_root.resolve())
    if not source_wav.is_file():
        raise PromotionError("source WAV does not exist")
    wav_info = _wav_info(source_wav)
    source_hash = sha256_file(source_wav)
    evidence = _read_json(evidence_path, "request evidence")
    if not isinstance(evidence, dict) or evidence.get("schemaVersion") != 1:
        raise PromotionError("request evidence must use schemaVersion 1")
    if evidence.get("profileId") != profile_id:
        raise PromotionError("evidence profileId does not match expected profileId")
    if evidence.get("voiceGender") != expected_gender:
        raise PromotionError("evidence voiceGender does not match expected profile gender")
    expected_wav_hash = evidence.get("wavSha256")
    if not isinstance(expected_wav_hash, str) or not valid_sha256(expected_wav_hash):
        raise PromotionError("request evidence has no valid WAV SHA-256")
    if expected_wav_hash.lower() != source_hash:
        raise PromotionError("source WAV SHA-256 does not match request evidence")
    transcript = evidence.get("finalizedText")
    if not isinstance(transcript, str) or not transcript.strip():
        raise PromotionError("request evidence has no exact finalizedText transcript")
    if not isinstance(evidence.get("instruction"), str) or not evidence["instruction"].strip():
        raise PromotionError("request evidence has no actual synthesis instruction")

    document, profile = _load_profile_registry(profiles_path, profile_id, expected_gender)
    if evidence.get("profileInstruction") != profile["instruction"] or evidence.get("seed") != profile["seed"]:
        raise PromotionError("evidence profile instruction or seed differs from trusted profile")

    asset_id = "references/{}-{}.wav".format(profile_id, source_hash[:16])
    destination = _contained(voice_root, asset_id)
    existing_keys = {key for key in ("voiceRef", "referenceText", "voiceReferenceSha256") if key in profile}
    if existing_keys and existing_keys != {"voiceRef", "referenceText", "voiceReferenceSha256"}:
        raise PromotionError("trusted profile has incomplete existing voice-reference metadata")
    if existing_keys and not replace:
        existing_path = profile["voiceRef"]
        if (not isinstance(existing_path, str) or not existing_path or Path(existing_path).is_absolute() or
                ".." in Path(existing_path).parts):
            raise PromotionError("existing trusted reference path is invalid")
        existing_file = _contained(voice_root, existing_path)
        existing_hash = profile["voiceReferenceSha256"]
        if (not isinstance(existing_hash, str) or not valid_sha256(existing_hash) or
                not existing_file.is_file() or sha256_file(existing_file) != existing_hash.lower()):
            raise PromotionError("existing trusted reference does not match its recorded file hash")
    already_current = (
        existing_keys == {"voiceRef", "referenceText", "voiceReferenceSha256"} and
        profile["referenceText"] == transcript and
        str(profile["voiceReferenceSha256"]).lower() == source_hash
    )
    if existing_keys and not already_current and not replace:
        raise PromotionError("profile already has a different reference; pass --replace after review")
    if destination.exists() and sha256_file(destination) != source_hash:
        raise PromotionError("content-addressed reference path exists with different bytes")

    result = {"status": "already-current" if already_current else ("ready" if not apply else "promoted"),
              "profileId": profile_id, "perceivedGender": expected_gender,
              "assetId": asset_id, "referenceText": transcript, "sha256": source_hash, **wav_info}
    if not apply or already_current:
        return result

    if not destination.exists():
        _atomic_copy(source_wav, destination, source_hash)
    profile["voiceRef"] = asset_id
    profile["referenceText"] = transcript
    profile["voiceReferenceSha256"] = source_hash
    _atomic_json(profiles_path, document)
    return result


def make_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-wav", type=Path, required=True, help="completed sample WAV to promote")
    parser.add_argument("--evidence", type=Path, required=True, help="matching request evidence JSON")
    parser.add_argument("--profiles", type=Path, required=True, help="trusted profiles.json")
    parser.add_argument("--voice-root", type=Path, required=True, help="persistent trusted reference root")
    parser.add_argument("--profile-id", required=True, help="expected profileId recorded in evidence")
    parser.add_argument("--expected-gender", choices=("female", "male"), required=True,
                        help="expected profile perceivedGender")
    parser.add_argument("--apply", action="store_true", help="copy WAV and update trusted registry")
    parser.add_argument("--replace", action="store_true", help="replace profile reference pointer; keep old asset")
    return parser


def main() -> int:
    args = make_parser().parse_args()
    result = promote_reference(args.source_wav, args.evidence, args.profiles, args.voice_root,
        args.profile_id, args.expected_gender, args.apply, args.replace)
    print(json.dumps(result, ensure_ascii=False, separators=(",", ":")))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (PromotionError, OSError, json.JSONDecodeError, wave.Error) as exc:
        print("Breeze reference promotion: " + str(exc), file=sys.stderr)
        raise SystemExit(2)
