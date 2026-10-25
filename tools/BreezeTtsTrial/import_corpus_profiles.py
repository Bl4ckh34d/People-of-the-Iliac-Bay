#!/usr/bin/env python3
"""Convert the authored voice corpus into the coordinator's trusted registry."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import sys
import uuid
from pathlib import Path
from typing import Any, Dict, Optional


class CorpusError(ValueError):
    pass


def _contained_file(root: Path, relative: str) -> Path:
    candidate = (root / relative).resolve()
    try:
        if os.path.commonpath((str(candidate), str(root.resolve()))) != str(root.resolve()):
            raise CorpusError("voice reference escapes the configured voice root")
    except (OSError, ValueError):
        raise CorpusError("voice reference must resolve inside the configured voice root")
    if not candidate.is_file():
        raise CorpusError("generated voice reference asset is missing")
    return candidate


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def _verified_reference(reference: Dict[str, Any], voice_root: Optional[Path]) -> Dict[str, str]:
    asset_id, transcript, expected_hash = reference.get("voiceRef"), reference.get("referenceText"), reference.get("voiceReferenceSha256")
    if voice_root is None:
        raise CorpusError("--voice-root is required to preserve a generated voice reference")
    if not isinstance(asset_id, str) or not asset_id or Path(asset_id).is_absolute() or ".." in Path(asset_id).parts:
        raise CorpusError("generated reference asset path must be relative to voice-root")
    if not isinstance(transcript, str) or not transcript.strip() or not isinstance(expected_hash, str):
        raise CorpusError("generated reference needs an exact transcript and SHA-256")
    path = _contained_file(voice_root, asset_id)
    if _sha256(path).lower() != expected_hash.lower():
        raise CorpusError("generated reference SHA-256 does not match its file")
    return {"voiceRef": asset_id, "referenceText": transcript, "voiceReferenceSha256": expected_hash.lower()}


def convert_corpus(corpus: Any, voice_root: Optional[Path] = None,
                   existing_profiles: Optional[Dict[str, Any]] = None) -> Dict[str, Any]:
    if not isinstance(corpus, dict) or corpus.get("schemaVersion") != 1:
        raise CorpusError("corpus must use schemaVersion 1")
    rows = corpus.get("voiceProfiles")
    if not isinstance(rows, list) or len(rows) < 12:
        raise CorpusError("corpus must contain at least twelve authored voiceProfiles")
    profiles: Dict[str, Dict[str, Any]] = {}
    seeds: set[int] = set()
    for row in rows:
        if not isinstance(row, dict):
            raise CorpusError("voiceProfiles entries must be objects")
        profile_id = row.get("profileId")
        gender = row.get("perceivedGender")
        age = row.get("adultAgePresentation")
        descriptor = row.get("identityDescriptor")
        if not isinstance(profile_id, str) or not profile_id.strip() or profile_id in profiles:
            raise CorpusError("every voice profile needs a unique profileId")
        if not isinstance(gender, str) or gender not in {"female", "male"}:
            raise CorpusError("perceivedGender must be the typed value female or male")
        if not isinstance(age, str) or not age.strip():
            raise CorpusError("adultAgePresentation must be authored explicitly")
        if not isinstance(descriptor, str) or not descriptor.strip() or len(descriptor) > 700:
            raise CorpusError("identityDescriptor must be non-empty and bounded")
        seed = int(hashlib.sha256(profile_id.encode("utf-8")).hexdigest()[:8], 16) & 0x7FFFFFFF
        while seed in seeds:
            seed = (seed + 1) & 0x7FFFFFFF
        seeds.add(seed)
        profile: Dict[str, Any] = {
            "instruction": "Perceived gender presentation: {}. Adult age presentation: {}. Stable voice identity: {}".format(
                gender.strip(), age.strip(), descriptor.strip()),
            "seed": seed,
            "perceivedGender": gender,
        }
        reference = row.get("voiceReference")
        if reference is not None:
            if not isinstance(reference, dict):
                raise CorpusError("voiceReference must be an object")
            status = reference.get("status")
            if status == "not-generated":
                if any(reference.get(key) is not None for key in ("assetId", "transcript", "sha256")):
                    raise CorpusError("not-generated reference must not claim an asset, transcript, or hash")
                existing = (existing_profiles or {}).get(profile_id)
                if isinstance(existing, dict) and any(key in existing for key in ("voiceRef", "referenceText", "voiceReferenceSha256")):
                    profile.update(_verified_reference(existing, voice_root))
            elif status == "generated":
                asset_id, transcript, expected_hash = reference.get("assetId"), reference.get("transcript"), reference.get("sha256")
                if voice_root is None:
                    raise CorpusError("--voice-root is required for generated references")
                if not isinstance(asset_id, str) or not asset_id or Path(asset_id).is_absolute() or ".." in Path(asset_id).parts:
                    raise CorpusError("generated reference assetId must be a relative voice-root path")
                if not isinstance(transcript, str) or not transcript.strip() or not isinstance(expected_hash, str):
                    raise CorpusError("generated reference needs an exact transcript and SHA-256")
                path = _contained_file(voice_root, asset_id)
                if _sha256(path).lower() != expected_hash.lower():
                    raise CorpusError("generated reference SHA-256 does not match its file")
                profile["voiceRef"] = asset_id
                profile["referenceText"] = transcript
                profile["voiceReferenceSha256"] = expected_hash.lower()
            else:
                raise CorpusError("voiceReference status must be not-generated or generated")
        else:
            existing = (existing_profiles or {}).get(profile_id)
            if isinstance(existing, dict) and any(key in existing for key in ("voiceRef", "referenceText", "voiceReferenceSha256")):
                profile.update(_verified_reference(existing, voice_root))
        profiles[profile_id] = profile
    return {"schemaVersion": 1, "profiles": profiles}


def write_atomic(path: Path, value: Dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
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


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--corpus", type=Path, required=True, help="authored corpus.json")
    parser.add_argument("--output", type=Path, required=True, help="trusted coordinator profiles.json")
    parser.add_argument("--voice-root", type=Path, help="trusted root for already-generated reference WAVs")
    args = parser.parse_args()
    with args.corpus.open("r", encoding="utf-8-sig") as stream:
        corpus = json.load(stream)
    existing_profiles: Optional[Dict[str, Any]] = None
    output_path = args.output.resolve()
    if output_path.exists():
        with output_path.open("r", encoding="utf-8-sig") as stream:
            existing = json.load(stream)
        if not isinstance(existing, dict) or existing.get("schemaVersion") != 1 or not isinstance(existing.get("profiles"), dict):
            raise CorpusError("existing output is not a trusted schemaVersion 1 profile registry")
        existing_profiles = existing["profiles"]
    output = convert_corpus(corpus, args.voice_root.resolve() if args.voice_root else None, existing_profiles)
    write_atomic(output_path, output)
    print("Wrote {} trusted voice profiles; no audio was generated.".format(len(output["profiles"])))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (CorpusError, OSError, json.JSONDecodeError) as error:
        print("Voice corpus import: " + str(error), file=sys.stderr)
        raise SystemExit(2)
