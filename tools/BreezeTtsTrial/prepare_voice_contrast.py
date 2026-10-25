#!/usr/bin/env python3
"""Prepare six labeled fixed-text Breeze voice-contrast cases; never synthesize audio."""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any, Dict

import coordinator


EXPECTED_PROFILE_ID = "npcvoice-01"
FIXED_TEXT = "You came back. I was beginning to wonder whether I would see you again."
CASE_INPUTS = (
    ("neutral", 0, 0, 0, "normal"),
    ("positiveMood", 70, 0, 0, "normal"),
    ("lowMood", -70, 0, 0, "normal"),
    ("highFear", 0, 90, 0, "normal"),
    ("highResentment", 0, 0, 90, "normal"),
    ("whisper", 0, 0, 0, "whisper"),
)


class ContrastError(ValueError):
    pass


def _read_json(path: Path, label: str) -> Any:
    with path.open("r", encoding="utf-8-sig") as stream:
        return json.load(stream)


def build_manifest(base_context_value: Any, profiles_path: Path, bindings_path: Path,
                   voice_root: Path, subject_name: str = "Gwen Cienne") -> Dict[str, Any]:
    """Validate trusted inputs and build deterministic, read-only listening-stage data."""
    if not isinstance(subject_name, str) or not subject_name.strip() or len(subject_name) > 96:
        raise ContrastError("subject name must be non-empty and bounded")
    if not isinstance(base_context_value, dict):
        raise ContrastError("base context must be a JSON object from the native bridge")
    raw_person_id = base_context_value.get("personId")
    context = coordinator.parse_delivery_context(base_context_value, raw_person_id)
    if context is None:
        raise ContrastError("native base context is required")

    profiles = coordinator.load_profiles(profiles_path)
    profile_id = EXPECTED_PROFILE_ID
    profile = profiles.get(profile_id)
    if profile is None or profile.get("perceivedGender") != "female":
        raise ContrastError("trusted registry must contain female profile npcvoice-01")
    try:
        full_registry = _read_json(profiles_path, "profiles")
        binding_document = _read_json(bindings_path, "bindings")
    except (OSError, json.JSONDecodeError) as exc:
        raise ContrastError("cannot read trusted profile or binding registry: {}".format(exc)) from exc
    if (not isinstance(binding_document, dict) or binding_document.get("schemaVersion") != 1 or
            not isinstance(binding_document.get("bindings"), dict)):
        raise ContrastError("voice-bindings.json must use schemaVersion 1")
    bound_profile = binding_document["bindings"].get(context["personId"])
    if bound_profile != profile_id:
        raise ContrastError("Gwen must already be bound to npcvoice-01; repair only the confirmed trial binding")
    try:
        loaded_profile, reference_path = coordinator.load_voice_profile(
            profiles, profile_id, voice_root.resolve())
    except coordinator.RequestError as exc:
        raise ContrastError("trusted profile/reference validation failed: {}".format(exc)) from exc
    if reference_path is None:
        raise ContrastError("npcvoice-01 must have a promoted persistent reference before preparing this contrast")
    if not (isinstance(loaded_profile.get("voiceRef"), str) and
            isinstance(loaded_profile.get("voiceReferenceSha256"), str)):
        raise ContrastError("npcvoice-01 persistent reference metadata is incomplete")
    if full_registry.get("profiles", {}).get(profile_id) != loaded_profile:
        raise ContrastError("trusted profile registry changed during validation")

    cases = [{
        "label": label,
        "mood": mood,
        "fear": fear,
        "resentment": resentment,
        "controlledDirection": {"emotion": "neutral", "deliveryMode": delivery_mode},
    } for label, mood, fear, resentment, delivery_mode in CASE_INPUTS]
    return {
        "schemaVersion": 1,
        "manifestKind": "controlled-voice-contrast",
        "sampleKind": "controlled-voice-contrast",
        "sourceKind": "fixed-authored-text-not-submit-free-text",
        "status": "prepared-no-synthesis",
        "subjectName": subject_name.strip(),
        "personId": context["personId"],
        "fixedText": FIXED_TEXT,
        "expectedProfileId": profile_id,
        "voiceSeed": loaded_profile["seed"],
        "referencePath": loaded_profile["voiceRef"],
        "referenceSha256": loaded_profile["voiceReferenceSha256"],
        "baseContext": context,
        "baseTraitsSha256": coordinator.canonical_json_hash(context["traits"]),
        "cases": cases,
    }


def write_manifest(path: Path, manifest: Dict[str, Any], replace: bool = False) -> None:
    if path.exists() and not replace:
        raise ContrastError("output exists; pass --replace only to replace a prepared manifest")
    coordinator.atomic_json(path, manifest)


def make_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base-context", type=Path, required=True,
        help="JSON deliveryContext snapshot captured from Gwen's live native request")
    parser.add_argument("--profiles", type=Path, required=True, help="trusted Breeze profiles.json")
    parser.add_argument("--bindings", type=Path, required=True, help="persistent voice-bindings.json")
    parser.add_argument("--voice-root", type=Path, required=True, help="trusted persistent voice-reference root")
    parser.add_argument("--output", type=Path, required=True, help="new controlled-contrast manifest path")
    parser.add_argument("--subject-name", default="Gwen Cienne", help="display label only")
    parser.add_argument("--replace", action="store_true", help="replace existing prepared manifest")
    return parser


def main() -> int:
    args = make_parser().parse_args()
    base_context = _read_json(args.base_context, "base context")
    manifest = build_manifest(base_context, args.profiles, args.bindings, args.voice_root, args.subject_name)
    write_manifest(args.output, manifest, args.replace)
    print("Prepared 6 controlled voice-contrast cases; no audio was generated.")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (ContrastError, coordinator.RequestError, OSError, json.JSONDecodeError) as error:
        print("Voice contrast preparation: " + str(error), file=sys.stderr)
        raise SystemExit(2)
