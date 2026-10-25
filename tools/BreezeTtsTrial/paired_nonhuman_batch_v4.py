#!/usr/bin/env python3
"""Run the Breeze side of the frozen 36-pair nonhuman race-voice v4 trial."""

from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
import os
import sys
import tempfile
import time
import wave
from pathlib import Path, PurePosixPath
from typing import Callable, Optional

try:
    from .prepare_nonhuman_corpus_v4 import (
        EXPECTED_V3_SHA256,
        MOOD_ORDER,
        VOICE_IDS,
        CorpusError,
        validate_corpus,
    )
    from .paired_mood_batch_v3 import (
        _record_wav,
        atomic_write_json,
        sha256_file,
        sha256_request_payload,
        utc_now,
    )
    from .server_sse_client import build_request, synthesize
except ImportError:
    from prepare_nonhuman_corpus_v4 import (
        EXPECTED_V3_SHA256,
        MOOD_ORDER,
        VOICE_IDS,
        CorpusError,
        validate_corpus,
    )
    from paired_mood_batch_v3 import (
        _record_wav,
        atomic_write_json,
        sha256_file,
        sha256_request_payload,
        utc_now,
    )
    from server_sse_client import build_request, synthesize


EXPECTED_CORPUS_SHA256 = "4b3cde2af0d0fd4b72186c3fe95033f3bef41fafe509567a24a9431dad7b139c"
EXPECTED_PAIRS = 36
GUIDANCE_SCALE = 4.0
DEFAULT_ENDPOINT = "http://127.0.0.1:8094"
DEFAULT_MODEL = "breeze"
MAX_WPM = 325


class BatchError(RuntimeError):
    """Raised when frozen input or resume state fails integrity checks."""


def load_corpus(corpus_path: Path | str) -> tuple[dict, str]:
    path = Path(corpus_path).resolve()
    raw = path.read_bytes()
    try:
        corpus = json.loads(raw.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise BatchError("v4 corpus is not valid UTF-8 JSON") from exc
    digest = hashlib.sha256(raw).hexdigest()
    if digest != EXPECTED_CORPUS_SHA256:
        raise BatchError("corpus SHA-256 differs from frozen v4 pin")
    if corpus.get("schemaVersion") != 4 or corpus.get("status") != "frozen":
        raise BatchError("corpus must be frozen schemaVersion 4")
    if corpus.get("sources", {}).get("v3CorpusSha256") != EXPECTED_V3_SHA256:
        raise BatchError("v3 source SHA differs from frozen v3 pin")
    try:
        validate_corpus(corpus)
    except CorpusError as exc:
        raise BatchError(str(exc)) from exc
    if corpus.get("summary", {}).get("breezeJobs") != EXPECTED_PAIRS:
        raise BatchError("v4 corpus must pin 36 Breeze jobs")
    return corpus, digest


def build_jobs(corpus: dict) -> list[dict]:
    return [
        {"voice": voice, "mood": mood, "pairId": mood["pairId"]}
        for voice in corpus["voices"]
        for mood in voice["moods"]
    ]


def build_request_payload(voice: dict, mood: dict, model: str = DEFAULT_MODEL) -> dict:
    return build_request(
        model,
        voice["fixedText"],
        mood["instruction"],
        str(Path(mood["referenceWav"]).resolve()),
        voice["fixedText"],
        seed=mood["seed"],
        guidance_scale=GUIDANCE_SCALE,
    )


def find_mood(manifest: dict, voice_id: str, mood_id: str) -> dict:
    for voice in manifest.get("voices", []):
        if voice.get("id") == voice_id:
            for mood in voice.get("moods", []):
                if mood.get("id") == mood_id:
                    return mood
    raise KeyError(f"{voice_id}:{mood_id} is absent from manifest")


def _new_manifest(corpus: dict, corpus_path: Path, corpus_sha: str,
                  output_root: Path, endpoint: str, model: str) -> dict:
    voices = []
    for voice in corpus["voices"]:
        moods = []
        for mood in voice["moods"]:
            relative = PurePosixPath("outputs") / PurePosixPath(mood["breezeOutputRelativePath"])
            output = output_root.joinpath(*relative.parts).resolve()
            try:
                output.relative_to(output_root.resolve())
            except ValueError as exc:
                raise BatchError("Breeze output path escapes output root") from exc
            payload = build_request_payload(voice, mood, model)
            moods.append({
                "id": mood["id"],
                "label": mood["label"],
                "pairId": mood["pairId"],
                "instruction": mood["instruction"],
                "raceDirection": mood["raceDirection"],
                "status": "pending",
                "url": mood["breezeUrl"],
                "outputWav": str(output),
                "outputRelativePath": relative.as_posix(),
                "wavSha256": None,
                "wavBytes": None,
                "durationSeconds": None,
                "wordsPerMinute": None,
                "seed": mood["seed"],
                "referenceWav": str(Path(mood["referenceWav"]).resolve()),
                "referenceSha256": mood["referenceWavSha256"],
                "guidanceScale": GUIDANCE_SCALE,
                "requestPayload": payload,
                "requestPayloadSha256": sha256_request_payload(payload),
                "runtimeMetadata": None,
                "warnings": [],
                "attempts": [],
                "error": None,
            })
        voices.append({
            "id": voice["id"],
            "label": f"{voice['race']} {voice['gender']}",
            "race": voice["race"],
            "gender": voice["gender"],
            "accent": voice["accent"],
            "raceDirection": voice["raceDirection"],
            "seed": voice["seed"],
            "fixedText": voice["fixedText"],
            "sourceInstruction": voice["sourceInstruction"],
            "raceNamedSourceInstruction": voice["raceNamedSourceInstruction"],
            "referenceSampleId": voice["referenceSampleId"],
            "referenceWav": str(Path(voice["referenceWav"]).resolve()),
            "referenceSha256": voice["referenceWavSha256"],
            "moods": moods,
        })
    return {
        "schemaVersion": 4,
        "trialId": "breeze-race-nonhuman-v4",
        "engine": "Breeze TTS-2 audio.cpp SSE",
        "status": "pending",
        "createdUtc": utc_now(),
        "updatedUtc": utc_now(),
        "endpoint": endpoint,
        "model": model,
        "guidanceScale": GUIDANCE_SCALE,
        "corpusId": corpus["corpusId"],
        "corpusPath": str(corpus_path.resolve()),
        "corpusSha256": corpus_sha,
        "sourceV3CorpusSha256": EXPECTED_V3_SHA256,
        "fixedText": corpus["fixedText"],
        "summary": {
            "expected": EXPECTED_PAIRS,
            "completed": 0,
            "failed": 0,
            "unattempted": EXPECTED_PAIRS,
            "attempted": 0,
        },
        "voices": voices,
        "runs": [],
    }


def _all_rows(manifest: dict) -> dict[str, dict]:
    rows = {}
    for voice in manifest.get("voices", []):
        for mood in voice.get("moods", []):
            pair_id = mood.get("pairId")
            if not isinstance(pair_id, str) or pair_id in rows:
                raise BatchError("manifest has missing or duplicate pairId")
            rows[pair_id] = mood
    return rows


def _validate_existing_manifest(manifest: dict, corpus: dict, corpus_path: Path,
                                corpus_sha: str, output_root: Path,
                                endpoint: str, model: str) -> None:
    if manifest.get("schemaVersion") != 4 or manifest.get("trialId") != "breeze-race-nonhuman-v4":
        raise BatchError("existing output root has incompatible manifest")
    if manifest.get("corpusSha256") != corpus_sha or manifest.get("corpusPath") != str(corpus_path.resolve()):
        raise BatchError("existing manifest corpus pin differs; refusing mixed trial")
    if manifest.get("endpoint") != endpoint or manifest.get("model") != model:
        raise BatchError("existing manifest endpoint or model differs")
    if manifest.get("guidanceScale") != GUIDANCE_SCALE:
        raise BatchError("existing manifest guidance scale differs")
    rows = _all_rows(manifest)
    jobs = build_jobs(corpus)
    if set(rows) != {job["pairId"] for job in jobs}:
        raise BatchError("existing manifest pair set differs from frozen corpus")
    for job in jobs:
        voice, mood, pair_id = job["voice"], job["mood"], job["pairId"]
        row = rows[pair_id]
        payload = build_request_payload(voice, mood, model)
        expected_relative = PurePosixPath("outputs") / mood["breezeOutputRelativePath"]
        expected_output = output_root.joinpath(*expected_relative.parts).resolve()
        if row.get("requestPayloadSha256") != sha256_request_payload(payload):
            raise BatchError("saved request payload differs for " + pair_id)
        if Path(row.get("outputWav", "")).resolve() != expected_output:
            raise BatchError("saved output path differs for " + pair_id)
        if row.get("outputRelativePath") != expected_relative.as_posix():
            raise BatchError("saved relative output path differs for " + pair_id)
        if (row.get("url") != mood["breezeUrl"] or row.get("instruction") != mood["instruction"]
                or row.get("seed") != mood["seed"]
                or row.get("referenceWav") != str(Path(mood["referenceWav"]).resolve())
                or row.get("referenceSha256") != mood["referenceWavSha256"]
                or row.get("guidanceScale") != GUIDANCE_SCALE):
            raise BatchError("saved prompt or input pin differs for " + pair_id)
        if row.get("status") not in {"pending", "running", "completed", "failed", "interrupted"}:
            raise BatchError("invalid status for " + pair_id)


def _refresh_summary(manifest: dict) -> None:
    rows = list(_all_rows(manifest).values())
    completed = sum(row.get("status") == "completed" for row in rows)
    failed = sum(row.get("status") == "failed" for row in rows)
    pending = sum(row.get("status") == "pending" for row in rows)
    manifest["summary"] = {
        "expected": EXPECTED_PAIRS,
        "completed": completed,
        "failed": failed,
        "unattempted": pending,
        "attempted": EXPECTED_PAIRS - pending,
    }


def _recover_existing_outputs(manifest: dict, corpus: dict) -> None:
    for job in build_jobs(corpus):
        voice, mood, pair_id = job["voice"], job["mood"], job["pairId"]
        row = find_mood(manifest, voice["id"], mood["id"])
        output = Path(row["outputWav"]).resolve()
        if not output.exists():
            if row["status"] == "completed":
                raise BatchError("completed WAV is missing: " + str(output))
            if row["status"] in {"running", "interrupted"}:
                if row.get("attempts") and row["attempts"][-1].get("status") == "running":
                    attempt = row["attempts"][-1]
                    attempt["status"] = "interrupted"
                    attempt["finishedUtc"] = utc_now()
                    attempt["error"] = "resumed without committed WAV"
                row["status"] = "pending"
                row.setdefault("warnings", []).append("prior run ended before WAV was committed")
            continue
        payload = build_request_payload(voice, mood, manifest["model"])
        if row.get("requestPayloadSha256") != sha256_request_payload(payload):
            raise BatchError("existing WAV lacks matching request pin for " + pair_id)
        try:
            details, wav_hash, size, wpm, warnings = _record_wav(output, voice["fixedText"])
        except Exception as exc:
            row["status"] = "failed"
            row["error"] = f"saved WAV validation failed: {type(exc).__name__}: {exc}"
            row.setdefault("warnings", []).append("Existing WAV preserved; runner will not overwrite or regenerate it.")
            continue
        if row["status"] == "completed":
            if row.get("wavSha256") != wav_hash:
                raise BatchError("completed WAV hash mismatch for " + pair_id)
            continue
        if row["status"] == "pending":
            raise BatchError("untracked WAV collision; refusing to claim: " + str(output))
        row.update({
            "status": "completed",
            "wavSha256": wav_hash,
            "wavBytes": size,
            "durationSeconds": details["durationSeconds"],
            "wordsPerMinute": wpm,
            "warnings": list(dict.fromkeys(row.get("warnings", []) + warnings + [
                "Recovered complete WAV from disk; synthesis timing was not recorded."
            ])),
            "error": None,
        })
        if row.get("attempts"):
            row["attempts"][-1]["status"] = "completed-recovered"
            row["attempts"][-1]["finishedUtc"] = row["attempts"][-1].get("finishedUtc") or utc_now()
            row["attempts"][-1]["wavSha256"] = wav_hash


def run_batch(corpus_path: Path | str, output_root: Path | str,
              endpoint: str = DEFAULT_ENDPOINT, model: str = DEFAULT_MODEL,
              timeout_seconds: int = 600, cases: Optional[list[str]] = None,
              retry_failed: bool = False,
              synthesize_fn: Optional[Callable] = None) -> dict:
    if timeout_seconds <= 0:
        raise ValueError("timeout_seconds must be positive")
    corpus_path = Path(corpus_path).resolve()
    output_root = Path(output_root).resolve()
    corpus, corpus_sha = load_corpus(corpus_path)
    jobs = build_jobs(corpus)
    valid_ids = {job["pairId"] for job in jobs}
    selected_ids = set(cases) if cases is not None else valid_ids
    unknown = selected_ids - valid_ids
    if unknown:
        raise BatchError("unknown pair ID: " + sorted(unknown)[0])
    selected = [job for job in jobs if job["pairId"] in selected_ids]
    manifest_path = output_root / "manifest.json"
    output_root.mkdir(parents=True, exist_ok=True)
    if manifest_path.exists():
        try:
            manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        except (UnicodeDecodeError, json.JSONDecodeError) as exc:
            raise BatchError("existing manifest is not valid UTF-8 JSON") from exc
        _validate_existing_manifest(manifest, corpus, corpus_path, corpus_sha, output_root, endpoint, model)
        _recover_existing_outputs(manifest, corpus)
    else:
        audio_root = output_root / "outputs"
        if audio_root.exists() and any(audio_root.rglob("*.wav")):
            raise BatchError("WAVs exist without a manifest; refusing to claim or overwrite")
        manifest = _new_manifest(corpus, corpus_path, corpus_sha, output_root, endpoint, model)

    render = synthesize_fn or synthesize
    run = {
        "startedUtc": utc_now(),
        "endpoint": endpoint,
        "model": model,
        "guidanceScale": GUIDANCE_SCALE,
        "timeoutSeconds": timeout_seconds,
        "selectedPairIds": [job["pairId"] for job in selected],
        "retryFailed": retry_failed,
    }
    manifest["runs"].append(run)
    manifest["status"] = "running"
    manifest["updatedUtc"] = utc_now()
    _refresh_summary(manifest)
    atomic_write_json(manifest_path, manifest)

    for job in selected:
        voice, mood, pair_id = job["voice"], job["mood"], job["pairId"]
        row = find_mood(manifest, voice["id"], mood["id"])
        output = Path(row["outputWav"])
        if row["status"] == "completed":
            continue
        if row["status"] == "failed" and not retry_failed:
            continue
        if output.exists():
            raise BatchError("refusing to overwrite existing WAV: " + str(output))

        payload = build_request_payload(voice, mood, model)
        payload_sha = sha256_request_payload(payload)
        attempt = {
            "attempt": len(row["attempts"]) + 1,
            "status": "running",
            "startedUtc": utc_now(),
            "requestPayloadSha256": payload_sha,
        }
        row.update({
            "status": "running",
            "requestPayload": payload,
            "requestPayloadSha256": payload_sha,
            "error": None,
        })
        row["attempts"].append(attempt)
        start = time.monotonic()
        manifest["updatedUtc"] = utc_now()
        _refresh_summary(manifest)
        atomic_write_json(manifest_path, manifest)
        try:
            metrics = render(
                endpoint, model, voice["fixedText"], mood["instruction"],
                str(Path(mood["referenceWav"]).resolve()), voice["fixedText"],
                str(output), timeout_seconds,
                seed=mood["seed"], guidance_scale=GUIDANCE_SCALE,
            )
            details, wav_hash, wav_size, wpm, warnings = _record_wav(output, voice["fixedText"])
            wall = time.monotonic() - start
            synthesis = (
                round(float(metrics["synthesisMs"]) / 1000.0, 6)
                if isinstance(metrics, dict) and isinstance(metrics.get("synthesisMs"), (int, float))
                else None
            )
            duration = details["durationSeconds"]
            runtime = {
                "backend": "audio.cpp SSE",
                "devicePlacement": "not measured by HTTP runner",
                "clientWallSeconds": round(wall, 6),
                "synthesisWallSeconds": synthesis,
                "firstAudioSeconds": (
                    round(float(metrics["firstAudioMs"]) / 1000.0, 6)
                    if isinstance(metrics, dict) and isinstance(metrics.get("firstAudioMs"), (int, float))
                    else None
                ),
                "audioSeconds": duration,
                "rtf": round(synthesis / duration, 6) if synthesis is not None and duration > 0 else None,
                "audioDeltaCount": metrics.get("audioDeltaCount") if isinstance(metrics, dict) else None,
                "serverTiming": metrics.get("serverTiming") if isinstance(metrics, dict) else None,
                "wav": details,
            }
            row.update({
                "status": "completed",
                "wavSha256": wav_hash,
                "wavBytes": wav_size,
                "durationSeconds": duration,
                "wordsPerMinute": wpm,
                "runtimeMetadata": runtime,
                "warnings": list(dict.fromkeys(row.get("warnings", []) + warnings)),
                "error": None,
            })
            attempt.update({
                "status": "completed",
                "finishedUtc": utc_now(),
                "elapsedSeconds": round(wall, 6),
                "wavSha256": wav_hash,
            })
        except Exception as exc:
            row["status"] = "failed"
            row["error"] = f"{type(exc).__name__}: {exc}"
            attempt.update({
                "status": "failed",
                "finishedUtc": utc_now(),
                "elapsedSeconds": round(time.monotonic() - start, 6),
                "error": row["error"],
            })
            if output.is_file():
                row["warnings"].append("Final WAV exists after failed validation; preserved for review.")
        finally:
            manifest["updatedUtc"] = utc_now()
            _refresh_summary(manifest)
            atomic_write_json(manifest_path, manifest)

    run["finishedUtc"] = utc_now()
    _refresh_summary(manifest)
    if manifest["summary"]["unattempted"] > 0:
        manifest["status"] = "partial"
    elif manifest["summary"]["failed"] > 0:
        manifest["status"] = "completed-with-errors"
    else:
        manifest["status"] = "completed"
    manifest["updatedUtc"] = utc_now()
    atomic_write_json(manifest_path, manifest)
    return manifest


def default_paths() -> tuple[Path, Path]:
    repo_root = Path(__file__).resolve().parents[2]
    return (
        repo_root / "work/ming3-tts-assessment-20261025/corpus-v4-nonhuman-paired.json",
        repo_root / "work/breeze-tts-20261021/race-accents-02/breeze-race-nonhuman-v4",
    )


def main(argv: Optional[list[str]] = None) -> int:
    corpus_default, output_default = default_paths()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--corpus", default=str(corpus_default))
    parser.add_argument("--output-root", default=str(output_default))
    parser.add_argument("--endpoint", default=DEFAULT_ENDPOINT)
    parser.add_argument("--model", default=DEFAULT_MODEL)
    parser.add_argument("--timeout", type=int, default=600)
    parser.add_argument("--case", action="append", dest="cases", help="pair ID voiceId:moodId; repeat to select cases")
    parser.add_argument("--retry-failed", action="store_true")
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args(argv)
    try:
        if args.validate_only:
            corpus, digest = load_corpus(args.corpus)
            print(json.dumps({
                "status": "valid",
                "corpusId": corpus["corpusId"],
                "corpusSha256": digest,
                "voices": len(corpus["voices"]),
                "moodsPerVoice": len(MOOD_ORDER),
                "expected": EXPECTED_PAIRS,
                "guidanceScale": GUIDANCE_SCALE,
                "smokePairs": corpus["smokePairs"],
            }, ensure_ascii=False))
            return 0
        manifest = run_batch(
            args.corpus, args.output_root, args.endpoint, args.model,
            timeout_seconds=args.timeout, cases=args.cases,
            retry_failed=args.retry_failed,
        )
        print(json.dumps({
            "status": manifest["status"],
            "manifest": str((Path(args.output_root) / "manifest.json").resolve()),
            "summary": manifest["summary"],
        }, ensure_ascii=False))
        return 0 if manifest["summary"]["failed"] == 0 else 1
    except (BatchError, OSError, ValueError) as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
