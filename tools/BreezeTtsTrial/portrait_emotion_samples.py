#!/usr/bin/env python3
"""Serial same-text/same-reference Breeze emotion samples; never starts a server."""

from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
import math
from protocol_text import valid_sha256, word_count
import shutil
import sys
import time
import wave
from pathlib import Path
from typing import Any

try:
    from .audio_loudness import normalize_pcm16_wave, sha256_file
    from .rhubarb_visemes import run_rhubarb
    from .server_sse_client import build_request, synthesize
except ImportError:  # Direct script execution from the repository root.
    from audio_loudness import normalize_pcm16_wave, sha256_file
    from rhubarb_visemes import run_rhubarb
    from server_sse_client import build_request, synthesize


ROOT = Path(__file__).resolve().parents[2]
DEFAULT_CONFIG = Path(__file__).with_name("portrait_emotion_samples.json")
MAX_WORDS_PER_MINUTE = 325


def sample_instruction(voice: dict[str, Any], sample: dict[str, Any]) -> str:
    # Keep frozen identity/seed/reference intact. Apply a common delivery ceiling.
    identity = voice["stableInstruction"]
    return (
        "Stable voice identity: {}. Emotional delivery: {}. Preserve the exact text, "
        "accent, natural speaking volume, and vocal identity; no added words or sound effects. "
        "Keep a measured conversational pace, at most {} words per minute, including when emotional."
    ).format(identity, sample["deliveryText"], MAX_WORDS_PER_MINUTE)


def measure_speech_pace(audio: Path, text: str) -> dict[str, Any]:
    """Gate average clip pace; this does not measure instantaneous articulation."""
    words = word_count(text)
    with wave.open(str(audio), "rb") as wav:
        duration = wav.getnframes() / wav.getframerate()
    if not words or duration <= 0:
        raise ValueError("speech pace requires non-empty text and audio")
    rate = 60 * words / duration
    if rate > MAX_WORDS_PER_MINUTE + 1e-6:
        raise ValueError("speech pace {:.1f} WPM exceeds limit {} WPM; preserve raw audio but do not publish".format(
            rate, MAX_WORDS_PER_MINUTE))
    return {"wordCount": words, "durationSeconds": duration,
            "wordsPerMinute": round(rate, 3), "maxWordsPerMinute": MAX_WORDS_PER_MINUTE,
            "method": "text-word-count / full-WAV-duration; average, not instantaneous rate"}


def load_and_validate(config_path: Path) -> tuple[dict[str, Any], Path]:
    config = json.loads(config_path.read_text(encoding="utf-8-sig"))
    if not isinstance(config, dict) or config.get("schemaVersion") != 1:
        raise ValueError("unsupported emotion-sample config schema")
    voice = config.get("voice")
    backend = config.get("backend")
    samples = config.get("samples")
    if not isinstance(voice, dict) or not isinstance(backend, dict):
        raise ValueError("config requires voice and backend objects")
    if not isinstance(samples, list) or [row.get("id") for row in samples] != [
            "neutral", "happy", "angry", "sad", "afraid", "guarded"]:
        raise ValueError("config must contain the six ordered emotion samples")
    text = config.get("exactText")
    reference_text = voice.get("referenceText")
    if not isinstance(text, str) or not text.strip():
        raise ValueError("sample text must be non-empty")
    if not isinstance(reference_text, str) or not reference_text.strip():
        raise ValueError("voice reference transcript must be non-empty")
    for row in samples:
        if not isinstance(row.get("deliveryText"), str) or not row["deliveryText"].strip():
            raise ValueError("each sample requires a deliveryText")
    seed = voice.get("seed")
    if isinstance(seed, bool) or not isinstance(seed, int) or seed < 0:
        raise ValueError("voice seed must be a non-negative integer")
    guidance = voice.get("guidanceScale")
    if (isinstance(guidance, bool) or not isinstance(guidance, (int, float)) or
            not math.isfinite(guidance) or guidance <= 0):
        raise ValueError("voice guidanceScale must be a finite positive number")
    for hash_field in ("referenceSha256", "profileSha256"):
        value = voice.get(hash_field)
        if not isinstance(value, str) or not valid_sha256(value):
            raise ValueError("voice {} must be a 64-character SHA-256".format(hash_field))
    if not isinstance(voice.get("profileId"), str) or not voice["profileId"].strip():
        raise ValueError("voice profileId must be non-empty")
    if backend.get("modelId") != "breeze" or backend.get("expectedModelFile") != "breeze-tts-2-q4_0.gguf":
        raise ValueError("backend config must target the pinned Breeze Q4 model")
    rhubarb_timeout = config.get("postprocess", {}).get("rhubarbTimeoutSeconds", 90)
    if isinstance(rhubarb_timeout, bool) or not isinstance(rhubarb_timeout, int) or rhubarb_timeout < 1:
        raise ValueError("Rhubarb timeout must be a positive integer")
    reference = (ROOT / voice["referenceWav"]).resolve()
    if not reference.is_file():
        raise FileNotFoundError("reference WAV does not exist: {}".format(reference))
    actual_hash = sha256_file(reference)
    if actual_hash.lower() != voice.get("referenceSha256", "").lower():
        raise ValueError("reference WAV SHA-256 does not match the pinned reference")
    source_audio = (ROOT / config["sourceAudio"]).resolve()
    if not source_audio.is_file() or sha256_file(source_audio).lower() != config.get("sourceAudioSha256", "").lower():
        raise ValueError("source sample WAV is missing or its hash changed")
    if actual_hash.lower() != config.get("sourceAudioSha256", "").lower():
        raise ValueError("voice reference and source sample hashes differ")
    return config, reference


def _write_manifest(path: Path, manifest: dict[str, Any]) -> None:
    temp = path.with_suffix(path.suffix + ".tmp")
    temp.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    temp.replace(path)


def run(config_path: Path, output_dir: Path, rhubarb_cli: Path,
        timeout_seconds: int = 600) -> dict[str, Any]:
    config, reference = load_and_validate(config_path)
    if not rhubarb_cli.is_file():
        raise FileNotFoundError("Rhubarb executable does not exist: {}".format(rhubarb_cli))
    output_dir = output_dir.resolve()
    if output_dir.exists() and any(output_dir.iterdir()):
        raise FileExistsError("refusing to reuse non-empty output directory: {}".format(output_dir))
    output_dir.mkdir(parents=True, exist_ok=True)
    expected = [output_dir / (row["id"] + suffix) for row in config["samples"]
                for suffix in (".wav", ".raw.wav", ".visemes.json")]
    collisions = [str(path) for path in expected if path.exists()]
    if collisions:
        raise FileExistsError("refusing to overwrite output: {}".format(collisions[0]))
    manifest_path = output_dir / "manifest.json"
    if manifest_path.exists():
        raise FileExistsError("refusing to overwrite manifest: {}".format(manifest_path))

    backend, voice = config["backend"], config["voice"]
    manifest: dict[str, Any] = {
        "schemaVersion": 1,
        "status": "running",
        "configPath": str(config_path.resolve()),
        "exactText": config["exactText"],
        "exactTextSha256": hashlib.sha256(config["exactText"].encode("utf-8")).hexdigest(),
        "profileId": voice["profileId"],
        "profileSha256": voice["profileSha256"],
        "seed": voice["seed"],
        "guidanceScale": voice["guidanceScale"],
        "referenceWav": str(reference),
        "referenceSha256": sha256_file(reference),
        "expectedModelFile": backend["expectedModelFile"],
        "expectedModelSha256": backend["expectedModelSha256"],
        "endpoint": backend["endpoint"],
        "maxWordsPerMinute": MAX_WORDS_PER_MINUTE,
        "samples": [],
    }
    start = time.monotonic_ns()
    try:
        for sample in config["samples"]:  # Deliberately serial; no retries.
            audio = output_dir / (sample["id"] + ".wav")
            raw_audio = output_dir / (sample["id"] + ".raw.wav")
            visemes = output_dir / (sample["id"] + ".visemes.json")
            instruction = sample_instruction(voice, sample)
            row: dict[str, Any] = {
                "id": sample["id"],
                "emotionLabel": sample["emotion"],
                "deliveryText": sample["deliveryText"],
                "instruction": instruction,
                "status": "failed",
                "wav": str(audio),
                "rawWav": str(raw_audio),
                "visemes": str(visemes),
            }
            try:
                request_payload = build_request(
                    backend["modelId"], config["exactText"], instruction,
                    str(reference), voice["referenceText"],
                    seed=voice["seed"], guidance_scale=voice["guidanceScale"])
                row["requestPayload"] = request_payload
                row["requestPayloadSha256"] = hashlib.sha256(
                    json.dumps(request_payload, ensure_ascii=False, sort_keys=True,
                               separators=(",", ":")).encode("utf-8")).hexdigest()
                row["requestReferenceSha256"] = sha256_file(reference)
                row["synthesis"] = synthesize(
                    backend["endpoint"], backend["modelId"], config["exactText"], instruction,
                    str(reference), voice["referenceText"], str(audio), timeout_seconds,
                    seed=voice["seed"], guidance_scale=voice["guidanceScale"])
                # Preserve exact synthesized bytes before the in-place normalizer.
                if raw_audio.exists():
                    raise FileExistsError("refusing to overwrite raw WAV: {}".format(raw_audio))
                shutil.copyfile(audio, raw_audio)
                row["rawWavSha256"] = sha256_file(raw_audio)
                # A mood prompt can accelerate speech despite a calm identity.
                # Reject before final processing/cues rather than silently
                # publishing an over-fast clip or changing its speaker pitch.
                row["speechPace"] = measure_speech_pace(audio, config["exactText"])
                row["loudnessNormalization"] = normalize_pcm16_wave(
                    audio,
                    target_active_rms_dbfs=config["postprocess"]["targetActiveRmsDbfs"],
                    peak_ceiling_dbfs=config["postprocess"]["peakCeilingDbfs"],
                    max_boost_db=config["postprocess"]["maxBoostDb"],
                    gate_dbfs=config["postprocess"]["gateDbfs"],
                )
                row["wavSha256"] = sha256_file(audio)
                row["rhubarb"] = run_rhubarb(
                    str(rhubarb_cli), str(audio), config["exactText"], str(visemes),
                    recognizer=config["postprocess"]["rhubarbRecognizer"],
                    threads=config["postprocess"]["rhubarbThreads"],
                    timeout_seconds=config["postprocess"].get("rhubarbTimeoutSeconds", 90))
                row["status"] = "succeeded"
            except Exception as exc:
                row["error"] = "{}: {}".format(type(exc).__name__, exc)
                manifest["samples"].append(row)
                manifest["status"] = "completed-with-errors"
                _write_manifest(manifest_path, manifest)
                raise
            manifest["samples"].append(row)
            _write_manifest(manifest_path, manifest)
        manifest["status"] = "completed"
    except BaseException:
        if manifest["status"] == "running":
            manifest["status"] = "interrupted"
        raise
    finally:
        manifest["elapsedMs"] = round((time.monotonic_ns() - start) / 1_000_000, 3)
        _write_manifest(manifest_path, manifest)
    return manifest


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config", type=Path, default=DEFAULT_CONFIG)
    parser.add_argument("--output-dir", type=Path)
    parser.add_argument("--rhubarb-cli", type=Path)
    parser.add_argument("--timeout-seconds", type=int, default=600)
    parser.add_argument("--validate-only", action="store_true",
                        help="verify config and pinned WAV hashes without contacting the server")
    args = parser.parse_args(argv)
    try:
        config, reference = load_and_validate(args.config.resolve())
        if args.validate_only:
            print(json.dumps({"status": "config-valid", "referenceWav": str(reference),
                              "referenceSha256": sha256_file(reference),
                              "sampleCount": len(config["samples"])}, sort_keys=True))
            return 0
        if args.output_dir is None or args.rhubarb_cli is None:
            parser.error("--output-dir and --rhubarb-cli are required unless --validate-only is used")
        if not 1 <= args.timeout_seconds <= 3600:
            parser.error("--timeout-seconds must be between 1 and 3600")
        manifest = run(args.config.resolve(), args.output_dir, args.rhubarb_cli.resolve(),
                       args.timeout_seconds)
    except Exception as exc:
        print("portrait_emotion_samples: {}: {}".format(type(exc).__name__, exc), file=sys.stderr)
        return 1
    print(json.dumps({"status": manifest["status"], "sampleCount": len(manifest["samples"]),
                      "manifest": str((args.output_dir.resolve() / "manifest.json"))}, sort_keys=True))
    return 0 if manifest["status"] == "completed" else 1


if __name__ == "__main__":
    raise SystemExit(main())
