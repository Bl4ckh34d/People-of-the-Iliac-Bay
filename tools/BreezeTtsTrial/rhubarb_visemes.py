from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import subprocess
import tempfile
import wave
from pathlib import Path
from typing import Any, Dict, Iterable, List, Optional

try:
    from tools.BreezeTtsTrial.align_words import _sha256_file, read_wave_clock
except ModuleNotFoundError:
    from align_words import _sha256_file, read_wave_clock


SUPPORTED_SHAPES = frozenset("ABCDEFGHX")
RECOGNIZERS = frozenset(("pocketSphinx", "phonetic"))
RHUBARB_VERSION = "1.14.0"
RHUBARB_WINDOWS_ASSET = "Rhubarb-Lip-Sync-1.14.0-Windows.zip"
RHUBARB_WINDOWS_SIZE_BYTES = 87374842
RHUBARB_WINDOWS_URL = (
    "https://github.com/DanielSWolf/rhubarb-lip-sync/releases/download/v1.14.0/"
    "Rhubarb-Lip-Sync-1.14.0-Windows.zip"
)
TIMELINE_KIND = "recognizer-viseme-estimate"


def _finite_nonnegative(value: Any) -> bool:
    return (not isinstance(value, bool) and isinstance(value, (int, float))
            and math.isfinite(value) and value >= 0)


def _same_path(first: str, second: Path) -> bool:
    try:
        first_path = Path(first).resolve()
        second_path = second.resolve()
    except (OSError, RuntimeError):
        return False
    return os.path.normcase(os.path.normpath(str(first_path))) == os.path.normcase(os.path.normpath(str(second_path)))


def validate_rhubarb(dialog_text: str, raw: Any, audio: Dict[str, int],
                     audio_path: Path, request_id: str) -> Dict[str, Any]:
    """Validate Rhubarb's estimated mouth-cue sequence and map seconds to WAV frames."""
    if not isinstance(dialog_text, str) or not dialog_text.strip():
        raise ValueError("dialog text must be non-empty")
    if not isinstance(request_id, str) or not request_id.strip():
        raise ValueError("request ID must be non-empty")
    if not isinstance(audio, dict):
        raise ValueError("audio clock is invalid")
    rate = audio.get("sample_rate")
    frames = audio.get("frames")
    if (isinstance(rate, bool) or not isinstance(rate, int) or rate <= 0 or
            isinstance(frames, bool) or not isinstance(frames, int) or frames <= 0):
        raise ValueError("audio clock is invalid")
    if not isinstance(raw, dict) or not isinstance(raw.get("metadata"), dict):
        raise ValueError("Rhubarb JSON metadata is missing")
    metadata = raw["metadata"]
    sound_file = metadata.get("soundFile")
    duration = metadata.get("duration")
    if not isinstance(sound_file, str) or not _same_path(sound_file, audio_path):
        raise ValueError("Rhubarb metadata audio path does not match request WAV")
    if not _finite_nonnegative(duration):
        raise ValueError("Rhubarb metadata duration is invalid")

    audio_duration = frames / float(rate)
    if duration > audio_duration + (0.5 / rate):
        raise ValueError("Rhubarb metadata duration exceeds audio duration")
    if audio_duration - duration >= 0.011:
        raise ValueError("Rhubarb metadata duration does not cover audio duration")

    raw_cues = raw.get("mouthCues")
    if not isinstance(raw_cues, list) or not raw_cues:
        raise ValueError("Rhubarb output has no mouth cues")

    cues: List[Dict[str, Any]] = []
    previous_end_sample = 0
    previous_end_seconds = 0.0
    for index, cue in enumerate(raw_cues):
        if not isinstance(cue, dict):
            raise ValueError("Rhubarb mouth cue must be an object")
        start = cue.get("start")
        end = cue.get("end")
        shape = cue.get("value")
        if not _finite_nonnegative(start) or not _finite_nonnegative(end):
            raise ValueError("Rhubarb cue times must be finite and non-negative")
        if not isinstance(shape, str) or shape not in SUPPORTED_SHAPES:
            raise ValueError("Rhubarb mouth shape is unsupported")
        if start > previous_end_seconds + 0.0051:
            raise ValueError("Rhubarb cues are not contiguous")
        if start < previous_end_seconds - 0.0051:
            raise ValueError("Rhubarb cues overlap")
        start_sample = int(math.floor(float(start) * rate + 0.5))
        end_sample = int(math.floor(float(end) * rate + 0.5))
        if index == 0 and start_sample != 0:
            raise ValueError("first Rhubarb cue must start at audio sample zero")
        if start_sample != previous_end_sample or end_sample <= start_sample:
            raise ValueError("converted Rhubarb cues are not contiguous positive spans")
        if end_sample > frames:
            raise ValueError("Rhubarb cue exceeds audio bounds")
        cues.append({"startSample": start_sample, "endSample": end_sample, "shape": shape})
        previous_end_sample = end_sample
        previous_end_seconds = float(end)

    last_end = float(raw_cues[-1]["end"])
    if abs(last_end - float(duration)) > 0.0051:
        raise ValueError("last Rhubarb cue does not match metadata duration")
    if (frames - previous_end_sample) / float(rate) >= 0.011:
        raise ValueError("last Rhubarb cue does not cover audio duration")

    return {
        "schemaVersion": 1,
        "status": "ready",
        "timelineKind": TIMELINE_KIND,
        "requestId": request_id,
        "text": dialog_text,
        "audioSampleRate": rate,
        "audioFrames": frames,
        "cues": cues,
        "provenance": {
            "engine": "Rhubarb Lip Sync",
            "engineVersion": RHUBARB_VERSION,
            "engineAsset": RHUBARB_WINDOWS_ASSET,
            "engineAssetUrl": RHUBARB_WINDOWS_URL,
            "engineAssetSizeBytes": RHUBARB_WINDOWS_SIZE_BYTES,
            "recognizer": "pocketSphinx-or-phonetic",
            "recognitionKind": "recognizer-estimate",
            "dialogRole": "recognition-hint-not-forced-transcript",
            "cueResolutionSeconds": 0.01,
            "audioSha256": _sha256_file(audio_path),
            "dialogHintSha256": hashlib.sha256(dialog_text.encode("utf-8")).hexdigest(),
            "phonemeAlignment": False,
        },
    }


def run_rhubarb(cli_path: str, audio_path: str, dialog_text: str,
                output_path: Optional[str] = None, *, recognizer: str = "pocketSphinx",
                threads: int = 2, timeout_seconds: int = 90) -> Dict[str, Any]:
    """Run offline CPU viseme estimation in the speech worker with two threads."""
    cli = Path(cli_path).resolve()
    audio = Path(audio_path).resolve()
    output = (Path(output_path).resolve() if output_path is not None else
              audio.with_name(audio.stem + ".visemes.json"))
    if not cli.is_file():
        raise FileNotFoundError("Rhubarb executable does not exist: {}".format(cli))
    if not audio.is_file():
        raise FileNotFoundError("audio WAV does not exist: {}".format(audio))
    if output.exists():
        raise FileExistsError("refusing to overwrite viseme timeline: {}".format(output))
    if not isinstance(dialog_text, str) or not dialog_text.strip():
        raise ValueError("dialog text must be non-empty")
    if recognizer not in RECOGNIZERS:
        raise ValueError("recognizer must be pocketSphinx or phonetic")
    if threads < 1 or timeout_seconds < 1:
        raise ValueError("threads and timeout_seconds must be positive")
    audio_clock = read_wave_clock(audio)
    output.parent.mkdir(parents=False, exist_ok=True)

    with tempfile.TemporaryDirectory(prefix=".rhubarb-", dir=str(output.parent)) as temporary:
        temporary_dir = Path(temporary)
        dialog_file = temporary_dir / "dialog.txt"
        raw_file = temporary_dir / "mouth-cues.json"
        dialog_file.write_text(dialog_text, encoding="utf-8")
        command = [str(cli), "--recognizer", recognizer, "--exportFormat", "json",
                   "--extendedShapes", "GHX", "--dialogFile", str(dialog_file),
                   "--output", str(raw_file), "--threads", str(threads), "--quiet", str(audio)]
        try:
            completed = subprocess.run(command, capture_output=True, text=True,
                                       encoding="utf-8", errors="replace",
                                       timeout=timeout_seconds, check=False)
        except subprocess.TimeoutExpired as exc:
            raise TimeoutError("Rhubarb viseme estimation exceeded configured timeout") from exc
        if completed.returncode != 0:
            detail = (completed.stderr or completed.stdout or "").strip()[-2000:]
            raise RuntimeError("Rhubarb failed with exit {}: {}".format(completed.returncode, detail))
        try:
            raw = json.loads(raw_file.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError) as exc:
            raise ValueError("Rhubarb did not produce valid JSON mouth cues") from exc
        timeline = validate_rhubarb(dialog_text, raw, audio_clock, audio, audio.stem)
        if recognizer == "phonetic":
            timeline["provenance"]["recognizer"] = "phonetic"
        else:
            timeline["provenance"]["recognizer"] = "pocketSphinx"
        temporary_output = temporary_dir / "timeline.json"
        temporary_output.write_text(json.dumps(timeline, ensure_ascii=False,
                                               separators=(",", ":")) + "\n",
                                    encoding="utf-8")
        # Publish only a complete validated timeline. A competing writer fails safely.
        os.link(str(temporary_output), str(output))
    return timeline


def main(argv: Optional[Iterable[str]] = None) -> int:
    parser = argparse.ArgumentParser(description="Opt-in offline Rhubarb mouth-cue timeline")
    parser.add_argument("--cli", required=True, help="Rhubarb executable")
    parser.add_argument("--audio", required=True, help="finalized WAV")
    parser.add_argument("--text", required=True, help="exact finalized dialog text; recognizer hint only")
    parser.add_argument("--output", help="new JSON output path; defaults beside WAV; existing files refused")
    parser.add_argument("--recognizer", choices=sorted(RECOGNIZERS), default="pocketSphinx")
    parser.add_argument("--threads", type=int, default=2)
    parser.add_argument("--timeout-seconds", type=int, default=90)
    args = parser.parse_args(argv)
    run_rhubarb(args.cli, args.audio, args.text, args.output,
                recognizer=args.recognizer, threads=args.threads,
                timeout_seconds=args.timeout_seconds)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
