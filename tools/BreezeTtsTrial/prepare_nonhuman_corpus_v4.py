#!/usr/bin/env python3
"""Freeze paired v4 prompts for Orc, Argonian, and Khajiit voice auditions."""

from __future__ import annotations

import argparse
import copy
import datetime as dt
import hashlib
import json
import tempfile
from pathlib import Path, PurePosixPath
from typing import Any


MOOD_ORDER = ("neutral", "happy", "angry", "sad", "afraid", "guarded")
VOICE_IDS = (
    "orc-female", "orc-male", "argonian-female", "argonian-male",
    "khajiit-female", "khajiit-male",
)
EXPECTED_V3_SHA256 = "676ba29d2e4d02f9760b01dd9c3e904c304bac003dacba7a4e6e7fb9cdde6636"
RACE_DIRECTIONS = {
    "Orc": (
        "Use an unmistakably Elder Scrolls Orc voice with heavier, coarse throat and chest resonance. "
        "Keep every word intelligible; do not add separate grunts. "
        "Preserve the established Northern English accent distinctly non-American; "
        "do not substitute an American accent or lose the reference speaker identity."
    ),
    "Argonian": (
        "Use an unmistakably reptilian Elder Scrolls Argonian voice with a dry sibilant consonant edge "
        "and croaking resonance. Keep every word intelligible; do not add separate hiss sounds. "
        "Preserve the established Brazilian Portuguese-influenced English accent distinctly non-American; "
        "do not substitute an American accent or lose the reference speaker identity."
    ),
    "Khajiit": (
        "Use an unmistakably feline Elder Scrolls Khajiit voice with subtle purr-like, raspy voiced resonance. "
        "Keep every word intelligible; do not add meows. "
        "Preserve the established Arabic-influenced English accent distinctly non-American; "
        "do not substitute an American accent or lose the reference speaker identity."
    ),
}
ACCENT_BY_RACE = {
    "Orc": "Northern English",
    "Argonian": "Brazilian Portuguese-influenced English",
    "Khajiit": "Arabic-influenced English",
}
CORPUS_ID = "race-nonhuman-paired-v4"
BREEZE_URL_PREFIX = "breeze-race-nonhuman-v4/outputs"
MING_URL_PREFIX = "ming3-audition/outputs/prompt-nonhuman-v4"
MING_OUTPUT_PREFIX = "prompt-nonhuman-v4"
SMOKE_PAIRS = [
    "orc-female:neutral",
    "orc-male:neutral",
    "argonian-female:neutral",
    "argonian-male:neutral",
    "khajiit-female:neutral",
    "khajiit-male:neutral",
]


class CorpusError(ValueError):
    """Raised when source pins or v4 corpus rows violate the frozen contract."""


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _utc_now() -> str:
    return dt.datetime.now(dt.timezone.utc).isoformat(timespec="milliseconds").replace("+00:00", "Z")


def _name_race_in_opening(instruction: str, gender: str, race: str) -> str:
    prefix = "Adult " + gender
    end = len(prefix)
    if gender not in ("female", "male") or not instruction.startswith(prefix) or (len(instruction)>end and (instruction[end].isalnum() or instruction[end]=='_')):
        raise CorpusError("source instruction must begin with matching Adult gender: " + instruction)
    return instruction[:end] + " " + race + instruction[end:]



def build_corpus(v3: dict[str, Any], v3_path: str, v3_sha256: str | None = None) -> dict[str, Any]:
    if not isinstance(v3, dict) or v3.get("schemaVersion") != 3 or v3.get("status") != "frozen":
        raise CorpusError("source corpus must be frozen schemaVersion 3")
    if v3.get("moodOrder") != list(MOOD_ORDER):
        raise CorpusError("source v3 moodOrder differs from fixed order")
    fixed_text = v3.get("fixedText")
    if not isinstance(fixed_text, str) or not fixed_text.strip():
        raise CorpusError("source v3 fixedText is missing")
    if v3_sha256 is None:
        source_path = Path(v3_path)
        if not source_path.is_file():
            raise CorpusError("source v3 file is required to calculate its raw SHA-256")
        v3_sha256 = sha256_file(source_path)
    if v3_sha256 != EXPECTED_V3_SHA256:
        raise CorpusError("source v3 SHA-256 differs from frozen input pin")

    sources_by_id = {row.get("id"): row for row in v3.get("voices", []) if isinstance(row, dict)}
    if any(voice_id not in sources_by_id for voice_id in VOICE_IDS):
        raise CorpusError("source v3 is missing one or more selected voices")

    source_rel = PurePosixPath(v3_path.replace("\\", "/"))
    if source_rel.is_absolute() or ".." in source_rel.parts:
        source_rel = PurePosixPath("work/ming3-tts-assessment-20261025/corpus-v3.json")

    result: dict[str, Any] = {
        "schemaVersion": 4,
        "corpusId": CORPUS_ID,
        "status": "frozen",
        "createdUtc": _utc_now(),
        "fixedText": fixed_text,
        "moodOrder": list(MOOD_ORDER),
        "summary": {
            "voiceCount": len(VOICE_IDS),
            "moodCount": len(MOOD_ORDER),
            "pairCount": len(VOICE_IDS) * len(MOOD_ORDER),
            "breezeJobs": len(VOICE_IDS) * len(MOOD_ORDER),
            "mingJobs": len(VOICE_IDS) * len(MOOD_ORDER),
            "totalModelJobs": 2 * len(VOICE_IDS) * len(MOOD_ORDER),
        },
        "sources": {
            "v3CorpusPath": source_rel.as_posix(),
            "v3CorpusSha256": v3_sha256,
            "accentVoiceBatchPath": v3["sources"]["accentVoiceBatchPath"],
            "accentVoiceBatchSha256": v3["sources"]["accentVoiceBatchSha256"],
            "promptSourcePolicy": "Each v4 instruction equals its exact v3 mood instruction plus one frozen raceDirection sentence block.",
        },
        "comparisonContract": {
            "status": "prompt-reference-seed-matched",
            "sharedInputs": {
                "text": True,
                "instruction": True,
                "referenceWav": True,
                "referenceWavSha256": True,
                "seed": True,
            },
            "promptPolicy": {
                "base": "Insert race name after Adult gender in the opening subject of each selected v3 mood instruction, preserve the remaining v3 text byte-for-byte, then append raceDirection byte-for-byte.",
                "raceDirection": "Names Elder Scrolls race, specifies nonhuman vocal quality, preserves established distinctly non-American accent and reference speaker identity.",
            },
            "engineDifferences": {
                "breezeGuidanceScale": 4.0,
                "mingGuidanceScale": None,
                "mingReferenceTranscriptForwarded": False,
            },
        },
        "smokePairs": list(SMOKE_PAIRS),
        "voices": [],
    }

    for voice_id in VOICE_IDS:
        source_voice = sources_by_id[voice_id]
        expected_race = voice_id.split("-", 1)[0].capitalize()
        if expected_race == "Khajiit":
            pass
        elif expected_race not in RACE_DIRECTIONS:
            raise CorpusError("unsupported target race in voice ID: " + voice_id)
        if source_voice.get("race") != expected_race:
            raise CorpusError("voice ID and source race differ: " + voice_id)
        if source_voice.get("accent") != ACCENT_BY_RACE[expected_race]:
            raise CorpusError("source accent differs from required preserved accent: " + voice_id)
        if source_voice.get("fixedText") != fixed_text:
            raise CorpusError("source voice text differs from frozen v3 text: " + voice_id)
        source_moods = source_voice.get("moods")
        if not isinstance(source_moods, list) or [mood.get("id") for mood in source_moods] != list(MOOD_ORDER):
            raise CorpusError("source voice moods differ from fixed order: " + voice_id)

        voice = copy.deepcopy(source_voice)
        voice["sourceSchemaVersion"] = 3
        voice["raceDirection"] = RACE_DIRECTIONS[expected_race]
        voice["raceNamedSourceInstruction"] = _name_race_in_opening(
            source_voice["sourceInstruction"], source_voice["gender"], expected_race
        )
        voice["moods"] = []
        for source_mood in source_moods:
            mood_id = source_mood["id"]
            mood = copy.deepcopy(source_mood)
            mood["sourceInstruction"] = source_mood["instruction"]
            mood["raceNamedSourceInstruction"] = _name_race_in_opening(
                source_mood["instruction"], source_voice["gender"], expected_race
            )
            mood["raceDirection"] = RACE_DIRECTIONS[expected_race]
            mood["instruction"] = mood["raceNamedSourceInstruction"] + " " + RACE_DIRECTIONS[expected_race]
            mood["pairId"] = f"{voice_id}:{mood_id}"
            mood["breezeOutputRelativePath"] = f"{voice_id}/{mood_id}.wav"
            mood["breezeUrl"] = f"{BREEZE_URL_PREFIX}/{voice_id}/{mood_id}.wav"
            mood["mingOutputRelativePath"] = f"{MING_OUTPUT_PREFIX}/{voice_id}/{mood_id}.wav"
            mood["mingUrl"] = f"{MING_URL_PREFIX}/{voice_id}/{mood_id}.wav"
            voice["moods"].append(mood)
        result["voices"].append(voice)

    validate_corpus(result)
    return result


def validate_corpus(corpus: dict[str, Any], reference_base: Path | None = None) -> None:
    if corpus.get("schemaVersion") != 4 or corpus.get("corpusId") != CORPUS_ID or corpus.get("status") != "frozen":
        raise CorpusError("unsupported or unfrozen v4 corpus")
    if corpus.get("moodOrder") != list(MOOD_ORDER):
        raise CorpusError("v4 mood order differs from fixed order")
    if len(corpus.get("voices", [])) != len(VOICE_IDS):
        raise CorpusError("v4 corpus must contain exactly six voices")
    if [voice.get("id") for voice in corpus["voices"]] != list(VOICE_IDS):
        raise CorpusError("v4 voice IDs or order differ from frozen selection")
    summary = corpus.get("summary", {})
    expected_summary = {
        "voiceCount": 6, "moodCount": 6, "pairCount": 36,
        "breezeJobs": 36, "mingJobs": 36, "totalModelJobs": 72,
    }
    if any(summary.get(key) != value for key, value in expected_summary.items()):
        raise CorpusError("v4 summary must pin six voices and 36 paired jobs")
    sources = corpus.get("sources", {})
    if sources.get("v3CorpusSha256") != EXPECTED_V3_SHA256:
        raise CorpusError("v4 source SHA pin differs from frozen v3 input")

    for voice in corpus["voices"]:
        voice_id = voice["id"]
        race = voice["race"]
        if voice.get("accent") != ACCENT_BY_RACE.get(race):
            raise CorpusError("preserved accent differs from required voice accent: " + voice_id)
        if voice.get("raceDirection") != RACE_DIRECTIONS.get(race):
            raise CorpusError("race direction differs from frozen prompt: " + voice_id)
        expected_named_source = _name_race_in_opening(voice.get("sourceInstruction", ""), voice["gender"], race)
        if voice.get("raceNamedSourceInstruction") != expected_named_source:
            raise CorpusError("voice opening subject does not name race: " + voice_id)
        ref = Path(voice.get("referenceWav", ""))
        if not ref.is_absolute() and reference_base is not None:
            ref = (reference_base / ref).resolve()
        if not ref.is_file() or sha256_file(ref) != voice.get("referenceWavSha256"):
            raise CorpusError("reference WAV missing or hash-mismatched: " + voice_id)
        moods = voice.get("moods")
        if not isinstance(moods, list) or [mood.get("id") for mood in moods] != list(MOOD_ORDER):
            raise CorpusError("v4 voice must contain all six moods in order: " + voice_id)
        for mood in moods:
            mood_id = mood["id"]
            if mood.get("seed") != voice.get("seed"):
                raise CorpusError(f"{voice_id}:{mood_id} seed differs from voice seed")
            if mood.get("referenceWav") != voice.get("referenceWav") or mood.get("referenceWavSha256") != voice.get("referenceWavSha256"):
                raise CorpusError(f"{voice_id}:{mood_id} reference differs from pinned voice reference")
            if mood.get("pairId") != f"{voice_id}:{mood_id}":
                raise CorpusError(f"{voice_id}:{mood_id} pairId differs from canonical ID")
            expected_named = _name_race_in_opening(mood.get("sourceInstruction", ""), voice["gender"], race)
            if mood.get("raceNamedSourceInstruction") != expected_named:
                raise CorpusError(f"{voice_id}:{mood_id} opening subject does not name race")
            if mood.get("instruction") != expected_named + " " + RACE_DIRECTIONS[race]:
                raise CorpusError(f"{voice_id}:{mood_id} instruction differs from v3 prompt plus race direction")
            if mood.get("breezeOutputRelativePath") != f"{voice_id}/{mood_id}.wav":
                raise CorpusError(f"{voice_id}:{mood_id} Breeze relative output path is not canonical")
            if mood.get("breezeUrl") != f"{BREEZE_URL_PREFIX}/{voice_id}/{mood_id}.wav":
                raise CorpusError(f"{voice_id}:{mood_id} Breeze URL is not canonical")
            if mood.get("mingOutputRelativePath") != f"{MING_OUTPUT_PREFIX}/{voice_id}/{mood_id}.wav":
                raise CorpusError(f"{voice_id}:{mood_id} Ming relative output path is not canonical")
            if mood.get("mingUrl") != f"{MING_URL_PREFIX}/{voice_id}/{mood_id}.wav":
                raise CorpusError(f"{voice_id}:{mood_id} Ming URL is not canonical")


def default_paths() -> tuple[Path, Path]:
    repo_root = Path(__file__).resolve().parents[2]
    return (
        repo_root / "work/ming3-tts-assessment-20261025/corpus-v3.json",
        repo_root / "work/ming3-tts-assessment-20261025/corpus-v4-nonhuman-paired.json",
    )


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    source_default, output_default = default_paths()
    parser.add_argument("--source-v3", type=Path, default=source_default)
    parser.add_argument("--output", type=Path, default=output_default)
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args()
    try:
        raw = args.source_v3.read_bytes()
        v3 = json.loads(raw.decode("utf-8"))
        corpus = build_corpus(v3, args.source_v3.as_posix(), sha256_bytes(raw))
        validate_corpus(corpus)
        if args.validate_only:
            print(json.dumps({
                "status": "valid",
                "corpusId": corpus["corpusId"],
                "voices": len(corpus["voices"]),
                "pairs": corpus["summary"]["pairCount"],
                "sourceV3Sha256": corpus["sources"]["v3CorpusSha256"],
            }))
            return 0
        if args.output.exists():
            raise CorpusError("refusing to replace existing frozen corpus: " + str(args.output))
        args.output.parent.mkdir(parents=True, exist_ok=True)
        raw_output = (json.dumps(corpus, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
        with tempfile.NamedTemporaryFile(dir=args.output.parent, prefix=args.output.name + ".", suffix=".tmp", delete=False) as stream:
            temporary = Path(stream.name)
            stream.write(raw_output)
            stream.flush()
        temporary.replace(args.output)
        print(json.dumps({
            "status": "frozen",
            "corpusId": corpus["corpusId"],
            "corpusPath": str(args.output.resolve()),
            "corpusSha256": sha256_bytes(raw_output),
            "voices": len(corpus["voices"]),
            "pairs": corpus["summary"]["pairCount"],
            "sourceV3Sha256": corpus["sources"]["v3CorpusSha256"],
        }))
        return 0
    except (CorpusError, OSError, UnicodeDecodeError, json.JSONDecodeError) as exc:
        parser.exit(2, f"error: {exc}\n")


if __name__ == "__main__":
    raise SystemExit(main())
