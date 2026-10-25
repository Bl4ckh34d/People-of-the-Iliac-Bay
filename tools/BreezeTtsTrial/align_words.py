from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import subprocess
import tempfile
import unicodedata
import wave
from pathlib import Path
from typing import Any, Dict, Iterable, List, Optional, Tuple


ALIGNER_FAMILY = "qwen3_forced_aligner"
ALIGNER_MODEL_REPOSITORY = "audio-cpp/audio.cpp-gguf"
ALIGNER_MODEL_REVISION = "a70a4ecb70ee38ef43a5aeae9fbefb512778e262"
ALIGNER_MODEL_FILE = "Qwen3-ForcedAligner-0.6B-GGUF/qwen3-forced-aligner-0.6b-q8_0.gguf"
ALIGNER_MODEL_SIZE_BYTES = 1129966496
ALIGNER_MODEL_SHA256 = "75209490b11cec2b0db749ca5f4ff92266f58efd30f7fd04d9eb2a3ac9cc929f"
ALIGNER_SAMPLE_RATE = 16000
TIMELINE_KIND = "forced-word-alignment"


def _is_word_char(value: str) -> bool:
    return any(ch.isalnum() for ch in value)


def _transcript_tokens(text: str) -> List[Tuple[str, int, int]]:
    """Return normalized lexical tokens and original Python-string offsets."""
    result: List[Tuple[str, int, int]] = []
    start: Optional[int] = None
    for index, char in enumerate(text):
        if char.isalnum():
            if start is None:
                start = index
            continue
        internal = (char in ("'", "’") and index > 0 and index + 1 < len(text)
                    and _is_word_char(text[index - 1]) and _is_word_char(text[index + 1]))
        decimal_point = (char == "." and index > 0 and index + 1 < len(text)
                         and text[index - 1].isdigit() and text[index + 1].isdigit())
        if internal or decimal_point:
            if start is None:
                start = index
            continue
        if start is not None:
            value = unicodedata.normalize("NFKC", text[start:index]).casefold()
            result.append((value, start, index))
            start = None
    if start is not None:
        value = unicodedata.normalize("NFKC", text[start:]).casefold()
        result.append((value, start, len(text)))
    return result


def _positive_int(value: Any) -> bool:
    return isinstance(value, int) and not isinstance(value, bool) and value > 0


def _sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def read_wave_clock(path: Path) -> Dict[str, int]:
    try:
        with wave.open(str(path), "rb") as source:
            rate = source.getframerate()
            frames = source.getnframes()
            channels = source.getnchannels()
            sample_width = source.getsampwidth()
            if not _positive_int(rate) or not _positive_int(frames):
                raise ValueError("audio WAV has invalid sample rate or frame count")
            if channels < 1 or sample_width < 1:
                raise ValueError("audio WAV has invalid channel or sample-width metadata")
            return {"sample_rate": rate, "frames": frames,
                    "channels": channels, "sample_width_bytes": sample_width}
    except (wave.Error, EOFError) as exc:
        raise ValueError("audio input must be a readable PCM WAV") from exc


def validate_alignment(exact_text: str, raw: Any,
                       audio: Dict[str, int], *,
                       aligner_sample_rate: Optional[int] = None,
                       aligner_sample_rate_source: str = "explicit") -> Dict[str, Any]:
    """Validate full word coverage and map aligner samples onto source WAV clock."""
    if not isinstance(exact_text, str) or not exact_text.strip():
        raise ValueError("exact transcript must be non-empty")
    if not isinstance(audio, dict) or not _positive_int(audio.get("sample_rate")) or not _positive_int(audio.get("frames")):
        raise ValueError("audio sample clock is invalid")
    if isinstance(raw, list):
        raw_words = raw
        rate = aligner_sample_rate
        rate_source = aligner_sample_rate_source
    elif isinstance(raw, dict):
        raw_words = raw.get("words")
        raw_rate = raw.get("sample_rate")
        if raw_rate is not None and not _positive_int(raw_rate):
            raise ValueError("alignment sample_rate is invalid")
        rate = raw_rate if raw_rate is not None else aligner_sample_rate
        rate_source = "raw-json" if raw_rate is not None else aligner_sample_rate_source
    else:
        raise ValueError("alignment output must be a JSON object or word array")
    if not _positive_int(rate):
        raise ValueError("alignment sample_rate is missing or invalid")
    if not isinstance(raw_words, list) or not raw_words:
        raise ValueError("alignment has no word timestamps")

    expected = _transcript_tokens(exact_text)
    if not expected:
        raise ValueError("exact transcript has no lexical tokens")
    if len(expected) != len(raw_words):
        raise ValueError("word coverage does not match exact transcript token count")

    aligner_rate = rate
    audio_rate = audio["sample_rate"]
    total_frames = audio["frames"]
    aligned_words: List[Dict[str, Any]] = []
    previous_end_aligner = 0
    previous_end_audio = 0

    for index, (expected_token, row) in enumerate(zip(expected, raw_words)):
        if not isinstance(row, dict) or not isinstance(row.get("word"), str):
            raise ValueError("word timestamps require word text")
        row_tokens = _transcript_tokens(row["word"])
        if len(row_tokens) != 1 or row_tokens[0][0] != expected_token[0]:
            raise ValueError("word coverage differs from exact transcript at index {}".format(index))
        start = row.get("start_sample")
        end = row.get("end_sample")
        if (not isinstance(start, int) or isinstance(start, bool) or
                not isinstance(end, int) or isinstance(end, bool) or
                start < previous_end_aligner or end <= start):
            raise ValueError("word timestamps are not monotonic positive spans")

        start_audio = int(math.floor(start * audio_rate / aligner_rate + 0.5))
        end_audio = int(math.floor(end * audio_rate / aligner_rate + 0.5))
        if start_audio < previous_end_audio or end_audio <= start_audio:
            raise ValueError("converted word timestamps are not monotonic positive spans")
        if start_audio < 0 or end_audio > total_frames:
            raise ValueError("word timestamp exceeds audio bounds")

        token_text, text_start, text_end = expected_token
        utf16_start = len(exact_text[:text_start].encode("utf-16-le")) // 2
        utf16_end = len(exact_text[:text_end].encode("utf-16-le")) // 2
        word: Dict[str, Any] = {
            "text": exact_text[text_start:text_end],
            "alignedText": row["word"],
            "textStartUtf16": utf16_start,
            "textLengthUtf16": utf16_end - utf16_start,
            "startSample": start_audio,
            "endSample": end_audio,
        }
        confidence = row.get("confidence")
        if confidence is not None:
            if (isinstance(confidence, bool) or not isinstance(confidence, (int, float)) or
                    not math.isfinite(confidence) or confidence < 0 or confidence > 1):
                raise ValueError("word confidence must be a finite value from 0 through 1")
            word["confidence"] = float(confidence)
        aligned_words.append(word)
        previous_end_aligner = end
        previous_end_audio = end_audio

    return {
        "schemaVersion": 1,
        "status": "ready",
        "timelineKind": TIMELINE_KIND,
        "text": exact_text,
        "audioSampleRate": audio_rate,
        "audioFrames": total_frames,
        "alignerSampleRate": aligner_rate,
        "alignerSampleRateSource": rate_source,
        "coverage": {"matchedTokens": len(expected), "transcriptTokens": len(expected)},
        "words": aligned_words,
    }


def _build_command(cli: Path, model: Path, audio: Path, transcript: str,
                   words_out: Path, language: str, threads: int) -> List[str]:
    if threads < 1:
        raise ValueError("threads must be at least one")
    return [str(cli), "--task", "align", "--family", ALIGNER_FAMILY,
            "--model", str(model), "--backend", "cpu", "--threads", str(threads),
            "--audio", str(audio), "--text", transcript,
            "--language", language, "--words-out", str(words_out)]


def run_alignment(cli_path: str, model_path: str, audio_path: str,
                  exact_text: str, output_path: Optional[str] = None, *, language: str = "English",
                  threads: int = 1, timeout_seconds: int = 180,
                  expected_model_sha256: Optional[str] = ALIGNER_MODEL_SHA256) -> Dict[str, Any]:
    """Run opt-in offline CPU alignment on finalized audio; never called by playback."""
    cli = Path(cli_path).resolve()
    model = Path(model_path).resolve()
    audio = Path(audio_path).resolve()
    output = (Path(output_path).resolve() if output_path is not None else
              audio.with_name(audio.stem + ".alignment.json"))
    for label, path in (("audio.cpp CLI", cli), ("aligner model", model), ("audio WAV", audio)):
        if not path.is_file():
            raise FileNotFoundError("{} does not exist: {}".format(label, path))
    if output.exists():
        raise FileExistsError("refusing to overwrite alignment output: {}".format(output))
    if not language.strip():
        raise ValueError("language must be non-empty")
    if timeout_seconds < 1:
        raise ValueError("timeout_seconds must be positive")
    wave_clock = read_wave_clock(audio)
    if (expected_model_sha256 is not None and
            expected_model_sha256.lower() == ALIGNER_MODEL_SHA256.lower() and
            model.stat().st_size != ALIGNER_MODEL_SIZE_BYTES):
        raise ValueError("aligner model size does not match selected official Q8 artifact")
    model_hash = _sha256_file(model)
    if expected_model_sha256 is not None and model_hash.lower() != expected_model_sha256.lower():
        raise ValueError("aligner model SHA-256 does not match expected pin")

    output.parent.mkdir(parents=False, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix=".align-", dir=str(output.parent)) as temporary:
        raw_path = Path(temporary) / "words.json"
        command = _build_command(cli, model, audio, exact_text, raw_path, language, threads)
        try:
            completed = subprocess.run(command, capture_output=True, text=True,
                                       encoding="utf-8", errors="replace",
                                       timeout=timeout_seconds, check=False)
        except subprocess.TimeoutExpired as exc:
            raise TimeoutError("CPU word alignment exceeded configured timeout") from exc
        if completed.returncode != 0:
            detail = (completed.stderr or completed.stdout or "").strip()[-2000:]
            raise RuntimeError("audio.cpp alignment failed with exit {}: {}".format(
                completed.returncode, detail))
        try:
            raw = json.loads(raw_path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError) as exc:
            raise ValueError("audio.cpp did not produce valid word timestamp JSON") from exc
        timeline = validate_alignment(exact_text, raw, wave_clock,
            aligner_sample_rate=ALIGNER_SAMPLE_RATE,
            aligner_sample_rate_source="pinned-model-config")
        timeline["provenance"] = {
            "engine": "audio.cpp",
            "engineCommit": "507495611b22dc4590e0e5609e05e8096384e33f",
            "requestId": audio.stem,
            "family": ALIGNER_FAMILY,
            "modelRepository": ALIGNER_MODEL_REPOSITORY,
            "modelRevision": ALIGNER_MODEL_REVISION,
            "modelFile": ALIGNER_MODEL_FILE,
            "modelPath": str(model),
            "modelSha256": model_hash,
            "backend": "cpu",
            "threads": threads,
            "language": language,
            "audioSha256": _sha256_file(audio),
            "transcriptSha256": hashlib.sha256(exact_text.encode("utf-8")).hexdigest(),
            "phonemeAlignment": False,
        }
        temporary_output = Path(temporary) / "timeline.json"
        temporary_output.write_text(json.dumps(timeline, ensure_ascii=False,
                                               separators=(",", ":")) + "\n",
                                    encoding="utf-8")
        # Hard-link publishes only a complete validated timeline and fails if a competing writer won.
        os.link(str(temporary_output), str(output))
    return timeline


def main(argv: Optional[Iterable[str]] = None) -> int:
    parser = argparse.ArgumentParser(description="Opt-in offline CPU word alignment for finalized TTS audio")
    parser.add_argument("--cli", required=True, help="pinned audiocpp_cli executable")
    parser.add_argument("--model", required=True, help="local Qwen3 Forced Aligner GGUF")
    parser.add_argument("--audio", required=True, help="finalized PCM WAV")
    parser.add_argument("--text", required=True, help="exact finalized speech text")
    parser.add_argument("--output", help="new JSON output path; defaults beside WAV; existing files are refused")
    parser.add_argument("--language", default="English")
    parser.add_argument("--threads", type=int, default=1)
    parser.add_argument("--timeout-seconds", type=int, default=180)
    parser.add_argument("--expected-model-sha256", default=ALIGNER_MODEL_SHA256)
    args = parser.parse_args(argv)
    run_alignment(args.cli, args.model, args.audio, args.text, args.output,
                  language=args.language, threads=args.threads,
                  timeout_seconds=args.timeout_seconds,
                  expected_model_sha256=args.expected_model_sha256)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
