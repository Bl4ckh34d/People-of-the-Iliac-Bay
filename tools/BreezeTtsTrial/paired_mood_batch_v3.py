#!/usr/bin/env python3
"""Run the Breeze arm of the frozen race-voice v3 paired mood trial."""

from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
import os
from protocol_text import valid_identifier, valid_sha256, word_count as count_words
import sys
import tempfile
import time
import wave
from pathlib import Path, PurePosixPath
from typing import Callable, Optional

try:
    from .server_sse_client import build_request, synthesize
except ImportError:
    from server_sse_client import build_request, synthesize


MOOD_ORDER = ("neutral", "happy", "angry", "sad", "afraid", "guarded")
MOOD_WORDING = {
    "happy": "happy",
    "angry": "angry",
    "sad": "sad",
    "afraid": "fearful",
    "guarded": "guarded",
}
EXPECTED_VOICES = 20
EXPECTED_PAIRS = 120
GUIDANCE_SCALE = 4.0
DEFAULT_ENDPOINT = "http://127.0.0.1:8094"
DEFAULT_MODEL = "breeze"
MAX_REPLACE_ATTEMPTS = 8
INITIAL_REPLACE_DELAY_SECONDS = 0.05


class BatchError(RuntimeError):
    """Raised when frozen corpus or resume state fails integrity checks."""


def utc_now() -> str:
    return dt.datetime.now(dt.timezone.utc).isoformat(timespec="milliseconds").replace("+00:00", "Z")


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def sha256_request_payload(payload: dict) -> str:
    # Match the exact JSON serialization used by server_sse_client.synthesize.
    body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
    return hashlib.sha256(body).hexdigest()


def atomic_write_json(path: Path, value: dict) -> None:
    """Atomically update a JSON receipt, retrying Windows reader sharing locks."""
    path.parent.mkdir(parents=True, exist_ok=True)
    raw = json.dumps(value, ensure_ascii=False, indent=2) + "\n"
    temporary: Optional[Path] = None
    try:
        with tempfile.NamedTemporaryFile(
            mode="w", encoding="utf-8", newline="\n", dir=path.parent,
            prefix=path.name + ".", suffix=".tmp", delete=False,
        ) as stream:
            temporary = Path(stream.name)
            stream.write(raw)
            stream.flush()
            os.fsync(stream.fileno())
        for attempt in range(1, MAX_REPLACE_ATTEMPTS + 1):
            try:
                os.replace(temporary, path)
                break
            except PermissionError:
                if attempt == MAX_REPLACE_ATTEMPTS:
                    raise
                time.sleep(min(INITIAL_REPLACE_DELAY_SECONDS * (2 ** (attempt - 1)), 0.5))
    finally:
        if temporary is not None and temporary.exists():
            temporary.unlink()


def _require_hash(value: object, label: str) -> str:
    if not isinstance(value, str) or not valid_sha256(value, lowercase=True):
        raise BatchError("{} must be a lowercase SHA-256".format(label))
    return value


def _safe_relative_path(value: object, label: str, parts: int) -> PurePosixPath:
    if not isinstance(value, str) or "\\" in value:
        raise BatchError("{} must be a forward-slash relative path".format(label))
    path = PurePosixPath(value)
    if path.is_absolute() or len(path.parts) != parts or any(part in ("", ".", "..") for part in path.parts):
        raise BatchError("{} has unsafe path components".format(label))
    return path


def _validate_wav(path: Path) -> dict:
    if not path.is_file() or path.stat().st_size <= 44:
        raise BatchError("synthesizer returned without a non-empty WAV file: {}".format(path))
    try:
        with wave.open(str(path), "rb") as audio:
            channels = audio.getnchannels()
            sample_width = audio.getsampwidth()
            sample_rate = audio.getframerate()
            frames = audio.getnframes()
    except (wave.Error, EOFError) as exc:
        raise BatchError("output is not a valid PCM WAV: {}".format(path)) from exc
    if channels != 1 or sample_width != 2 or sample_rate != 24000 or frames <= 0:
        raise BatchError("output WAV must be non-empty 24 kHz mono PCM16: {}".format(path))
    duration = frames / float(sample_rate)
    return {
        "channels": channels,
        "sampleWidthBytes": sample_width,
        "sampleRate": sample_rate,
        "frames": frames,
        "durationSeconds": round(duration, 6),
    }


def load_corpus(corpus_path: Path | str) -> tuple[dict, str]:
    path = Path(corpus_path).resolve()
    raw = path.read_bytes()
    try:
        corpus = json.loads(raw.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise BatchError("v3 corpus is not valid UTF-8 JSON") from exc
    if not isinstance(corpus, dict) or corpus.get("schemaVersion") != 3:
        raise BatchError("unsupported paired mood corpus schema")
    if corpus.get("status") != "frozen":
        raise BatchError("paired mood corpus must be frozen")
    if not isinstance(corpus.get("corpusId"), str) or not corpus["corpusId"].strip():
        raise BatchError("paired mood corpus needs a corpusId")
    fixed_text = corpus.get("fixedText")
    if not isinstance(fixed_text, str) or not fixed_text.strip():
        raise BatchError("paired mood corpus needs fixedText")
    if corpus.get("moodOrder") != list(MOOD_ORDER):
        raise BatchError("paired mood corpus moodOrder differs from v3 contract")
    summary = corpus.get("summary")
    expected_summary = {
        "voiceCount": EXPECTED_VOICES,
        "moodCount": len(MOOD_ORDER),
        "pairCount": EXPECTED_PAIRS,
        "breezeJobs": EXPECTED_PAIRS,
    }
    if not isinstance(summary, dict) or any(summary.get(key) != value for key, value in expected_summary.items()):
        raise BatchError("paired mood corpus summary must pin 20 voices, six moods and 120 Breeze jobs")
    contract = corpus.get("comparisonContract")
    if not isinstance(contract, dict):
        raise BatchError("paired mood corpus lacks comparisonContract")
    differences = contract.get("engineDifferences")
    if not isinstance(differences, dict) or differences.get("breezeGuidanceScale") != GUIDANCE_SCALE:
        raise BatchError("paired mood corpus must pin Breeze guidance_scale=4.0")

    voices = corpus.get("voices")
    if not isinstance(voices, list) or len(voices) != EXPECTED_VOICES:
        raise BatchError("paired mood corpus must contain exactly 20 voices")
    seen_voice_ids: set[str] = set()
    seen_pair_ids: set[str] = set()
    for voice in voices:
        if not isinstance(voice, dict):
            raise BatchError("voice rows must be objects")
        voice_id = voice.get("id")
        if not isinstance(voice_id, str) or not valid_identifier(voice_id) or voice_id in seen_voice_ids:
            raise BatchError("voice IDs must be unique safe filename components")
        seen_voice_ids.add(voice_id)
        for key in ("race", "gender", "accent", "referenceSampleId", "sourceSampleId", "sourceInstruction"):
            if not isinstance(voice.get(key), str) or not voice[key].strip():
                raise BatchError("voice {} needs non-empty {}".format(voice_id, key))
        if isinstance(voice.get("seed"), bool) or not isinstance(voice.get("seed"), int) or voice["seed"] < 0:
            raise BatchError("voice {} needs a non-negative integer seed".format(voice_id))
        if voice.get("fixedText") != fixed_text:
            raise BatchError("voice {} text differs from fixedText".format(voice_id))
        reference = Path(str(voice.get("referenceWav", ""))).resolve()
        expected_ref_hash = _require_hash(voice.get("referenceWavSha256"), "voice reference hash")
        if not reference.is_file() or sha256_file(reference) != expected_ref_hash:
            raise BatchError("voice {} reference WAV is missing or hash-mismatched: {}".format(voice_id, reference))
        if isinstance(voice.get("historicalBreezeV2"), dict):
            prior = voice["historicalBreezeV2"]
            _require_hash(prior.get("wavSha256"), "historical Breeze v2 WAV hash")
            if not isinstance(prior.get("url"), str) or not prior["url"].strip():
                raise BatchError("voice {} historical Breeze v2 URL is missing".format(voice_id))

        moods = voice.get("moods")
        if (not isinstance(moods, list) or len(moods) != len(MOOD_ORDER)
                or any(not isinstance(mood, dict) for mood in moods)
                or [mood.get("id") for mood in moods] != list(MOOD_ORDER)):
            raise BatchError("voice {} must contain six moods in fixed order".format(voice_id))
        neutral = moods[0]
        source_clause = neutral.get("sourceDeliveryClause")
        if not isinstance(source_clause, str) or voice["sourceInstruction"].count(source_clause) != 1:
            raise BatchError("voice {} source instruction must contain one source delivery clause".format(voice_id))
        article = "the " if source_clause.startswith("Keep the ") else ""
        for mood in moods:
            mood_id = mood["id"]
            if not isinstance(mood.get("label"), str) or not isinstance(mood.get("instruction"), str):
                raise BatchError("{}:{} lacks label or instruction".format(voice_id, mood_id))
            pair_id = mood.get("pairId")
            if pair_id != "{}:{}".format(voice_id, mood_id) or pair_id in seen_pair_ids:
                raise BatchError("{}:{} has invalid or duplicate pairId".format(voice_id, mood_id))
            seen_pair_ids.add(pair_id)
            if mood.get("seed") != voice["seed"]:
                raise BatchError("{}:{} seed differs from voice seed".format(voice_id, mood_id))
            mood_reference = Path(str(mood.get("referenceWav", ""))).resolve()
            if mood_reference != reference or mood.get("referenceWavSha256") != expected_ref_hash:
                raise BatchError("{}:{} reference differs from frozen voice reference".format(voice_id, mood_id))
            delivery_clause = mood.get("deliveryClause")
            if not isinstance(delivery_clause, str) or not delivery_clause.strip():
                raise BatchError("{}:{} deliveryClause is missing".format(voice_id, mood_id))
            if mood.get("sourceDeliveryClause") != source_clause:
                raise BatchError("{}:{} sourceDeliveryClause differs from voice baseline".format(voice_id, mood_id))
            if mood_id == "neutral":
                if delivery_clause != source_clause or mood["instruction"] != voice["sourceInstruction"]:
                    raise BatchError("{} neutral prompt must remain byte-exact".format(voice_id))
            else:
                expected_clause = "Keep {}delivery natural, yet clearly {}.".format(
                    article, MOOD_WORDING[mood_id]
                )
                if delivery_clause != expected_clause:
                    raise BatchError("{}:{} has unexpected corrected delivery clause".format(voice_id, mood_id))
                expected_instruction = voice["sourceInstruction"].replace(source_clause, delivery_clause, 1)
                if mood["sourceDeliveryClause"] != source_clause or mood["instruction"] != expected_instruction:
                    raise BatchError("{}:{} must change only the delivery clause".format(voice_id, mood_id))
            local_rel = _safe_relative_path(mood.get("breezeOutputRelativePath"), "Breeze output path", 2)
            if local_rel.parts != (voice_id, mood_id + ".wav"):
                raise BatchError("{}:{} has a non-canonical Breeze output path".format(voice_id, mood_id))
            gallery_url = "breeze-paired-v3/outputs/{}/{}.wav".format(voice_id, mood_id)
            if mood.get("breezeUrl") != gallery_url:
                raise BatchError("{}:{} has a non-canonical Breeze gallery URL".format(voice_id, mood_id))

    if len(seen_pair_ids) != EXPECTED_PAIRS:
        raise BatchError("paired mood corpus must contain exactly 120 unique pairs")
    return corpus, hashlib.sha256(raw).hexdigest()


def build_jobs(corpus: dict) -> list[dict]:
    jobs = []
    for voice in corpus["voices"]:
        for mood in voice["moods"]:
            jobs.append({"voice": voice, "mood": mood, "pairId": mood["pairId"]})
    return jobs


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
    raise KeyError("{}:{} is absent from manifest".format(voice_id, mood_id))


def _new_manifest(corpus: dict, corpus_path: Path, corpus_sha: str,
                  output_root: Path, endpoint: str, model: str) -> dict:
    voices = []
    for voice in corpus["voices"]:
        moods = []
        for mood in voice["moods"]:
            output_rel = PurePosixPath("outputs") / PurePosixPath(mood["breezeOutputRelativePath"])
            output_path = output_root.joinpath(*output_rel.parts).resolve()
            try:
                output_path.relative_to(output_root.resolve())
            except ValueError as exc:
                raise BatchError("Breeze output path escapes output root") from exc
            payload = build_request_payload(voice, mood, model)
            moods.append({
                "id": mood["id"],
                "label": mood["label"],
                "pairId": mood["pairId"],
                "instruction": mood["instruction"],
                "sourceDeliveryClause": mood["sourceDeliveryClause"],
                "deliveryClause": mood["deliveryClause"],
                "status": "pending",
                "url": mood["breezeUrl"],
                "outputWav": str(output_path),
                "outputRelativePath": output_rel.as_posix(),
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
            })
        prior = voice.get("historicalBreezeV2", {})
        voices.append({
            "id": voice["id"],
            "label": voice.get("label", "{} {}".format(voice["race"], voice["gender"])),
            "race": voice["race"],
            "gender": voice["gender"],
            "accent": voice["accent"],
            "seed": voice["seed"],
            "fixedText": voice["fixedText"],
            "sourceInstruction": voice["sourceInstruction"],
            "referenceSampleId": voice["referenceSampleId"],
            "referenceWav": str(Path(voice["referenceWav"]).resolve()),
            "referenceSha256": voice["referenceWavSha256"],
            "historicalBreezeV2": prior,
            "moods": moods,
        })
    return {
        "schemaVersion": 1,
        "trialId": "breeze-race-paired-v3",
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


def _all_manifest_moods(manifest: dict) -> dict[str, dict]:
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
    if manifest.get("schemaVersion") != 1 or manifest.get("trialId") != "breeze-race-paired-v3":
        raise BatchError("existing output root has an incompatible manifest")
    if manifest.get("corpusSha256") != corpus_sha:
        raise BatchError("corpus SHA-256 differs from existing manifest; refusing mixed trial")
    if manifest.get("corpusPath") != str(corpus_path.resolve()):
        raise BatchError("corpus path differs from existing manifest")
    if manifest.get("endpoint") != endpoint or manifest.get("model") != model:
        raise BatchError("endpoint or model differs from existing manifest")
    if manifest.get("guidanceScale") != GUIDANCE_SCALE:
        raise BatchError("guidance scale differs from existing manifest")
    saved = _all_manifest_moods(manifest)
    jobs = build_jobs(corpus)
    if set(saved) != {job["pairId"] for job in jobs}:
        raise BatchError("existing manifest pair set differs from frozen corpus")
    for job in jobs:
        voice = job["voice"]
        mood = job["mood"]
        row = saved[job["pairId"]]
        payload = build_request_payload(voice, mood, model)
        if row.get("requestPayloadSha256") != sha256_request_payload(payload):
            raise BatchError("saved request payload differs for {}".format(job["pairId"]))
        expected_rel = PurePosixPath("outputs") / PurePosixPath(mood["breezeOutputRelativePath"])
        expected_output = output_root.joinpath(*expected_rel.parts).resolve()
        if Path(str(row.get("outputWav", ""))).resolve() != expected_output:
            raise BatchError("saved output path differs for {}".format(job["pairId"]))
        if row.get("outputRelativePath") != expected_rel.as_posix():
            raise BatchError("saved output relative path differs for {}".format(job["pairId"]))
        if (row.get("url") != mood["breezeUrl"] or row.get("instruction") != mood["instruction"]
                or row.get("seed") != mood["seed"]
                or row.get("referenceWav") != str(Path(mood["referenceWav"]).resolve())
                or row.get("referenceSha256") != mood["referenceWavSha256"]
                or row.get("guidanceScale") != GUIDANCE_SCALE):
            raise BatchError("saved prompt or URL differs for {}".format(job["pairId"]))
        if row.get("status") not in ("pending", "running", "completed", "failed", "interrupted"):
            raise BatchError("saved pair {} has invalid status".format(job["pairId"]))


def _refresh_summary(manifest: dict) -> None:
    rows = list(_all_manifest_moods(manifest).values())
    completed = sum(row.get("status") == "completed" for row in rows)
    failed = sum(row.get("status") == "failed" for row in rows)
    unattempted = sum(row.get("status") == "pending" for row in rows)
    attempted = EXPECTED_PAIRS - unattempted
    manifest["summary"] = {
        "expected": EXPECTED_PAIRS,
        "completed": completed,
        "failed": failed,
        "unattempted": unattempted,
        "attempted": attempted,
    }


def _record_wav(path: Path, text: str) -> tuple[dict, str, int, float, list[str]]:
    details = _validate_wav(path)
    wav_hash = sha256_file(path)
    size = path.stat().st_size
    word_count = count_words(text)
    wpm = (word_count * 60.0 / details["durationSeconds"])
    warnings = []
    if wpm > 325:
        warnings.append("speech rate exceeds 325 words per minute")
    return details, wav_hash, size, round(wpm, 3), warnings


def _recover_or_validate_saved_rows(manifest: dict, corpus: dict,
                                    output_root: Path) -> None:
    for job in build_jobs(corpus):
        voice, mood = job["voice"], job["mood"]
        row = find_mood(manifest, voice["id"], mood["id"])
        output = Path(row["outputWav"]).resolve()
        if not output.exists():
            if row["status"] == "completed":
                raise BatchError("completed WAV is missing: {}".format(output))
            if row["status"] in ("running", "interrupted"):
                attempt = row["attempts"][-1] if row.get("attempts") else None
                if attempt is not None and attempt.get("status") == "running":
                    attempt["status"] = "interrupted"
                    attempt["finishedUtc"] = utc_now()
                    attempt["error"] = "runner resumed without a committed WAV"
                row["status"] = "pending"
                row.setdefault("warnings", []).append("previous attempt was interrupted before WAV commit")
            elif row["status"] not in ("pending", "failed"):
                raise BatchError("{} has unsupported resume status {}".format(job["pairId"], row["status"]))
            continue

        if row.get("requestPayloadSha256") != sha256_request_payload(build_request_payload(voice, mood, manifest["model"])):
            raise BatchError("existing output lacks matching request pin for {}".format(job["pairId"]))
        details, wav_hash, size, wpm, warnings = _record_wav(output, voice["fixedText"])
        previous_hash = row.get("wavSha256")
        if row["status"] == "completed":
            if previous_hash != wav_hash:
                raise BatchError("completed WAV hash mismatch for {}".format(job["pairId"]))
            if row.get("durationSeconds") is not None and abs(float(row["durationSeconds"]) - details["durationSeconds"]) > 0.001:
                raise BatchError("completed WAV duration mismatch for {}".format(job["pairId"]))
            continue
        if row["status"] not in ("running", "failed", "interrupted"):
            raise BatchError("untracked WAV collision for {}".format(job["pairId"]))
        row.update({
            "status": "completed",
            "wavSha256": wav_hash,
            "wavBytes": size,
            "durationSeconds": details["durationSeconds"],
            "wordsPerMinute": wpm,
            "warnings": list(dict.fromkeys(row.get("warnings", []) + warnings + [
                "Recovered complete WAV from disk; synthesis timing was not recorded."
            ])),
        })
        if row.get("attempts"):
            last = row["attempts"][-1]
            last["status"] = "completed-recovered"
            last["finishedUtc"] = last.get("finishedUtc") or utc_now()
            last["wavSha256"] = wav_hash


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
    selected_ids = set(cases or [job["pairId"] for job in jobs])
    valid_ids = {job["pairId"] for job in jobs}
    unknown = selected_ids - valid_ids
    if unknown:
        raise BatchError("unknown pair ID: {}".format(sorted(unknown)[0]))
    selected = [job for job in jobs if job["pairId"] in selected_ids]
    manifest_path = output_root / "manifest.json"
    output_root.mkdir(parents=True, exist_ok=True)
    if manifest_path.exists():
        try:
            manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        except (UnicodeDecodeError, json.JSONDecodeError) as exc:
            raise BatchError("existing manifest is not valid UTF-8 JSON") from exc
        _validate_existing_manifest(manifest, corpus, corpus_path, corpus_sha, output_root, endpoint, model)
        _recover_or_validate_saved_rows(manifest, corpus, output_root)
    else:
        audio_root = output_root / "outputs"
        if audio_root.exists() and any(audio_root.rglob("*.wav")):
            raise BatchError("output WAVs exist without a manifest; refusing to claim or overwrite them")
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
            raise BatchError("refusing to overwrite existing WAV: {}".format(output))

        payload = build_request_payload(voice, mood, model)
        payload_hash = sha256_request_payload(payload)
        attempt = {
            "attempt": len(row["attempts"]) + 1,
            "status": "running",
            "startedUtc": utc_now(),
            "requestPayloadSha256": payload_hash,
        }
        row["status"] = "running"
        row["requestPayload"] = payload
        row["requestPayloadSha256"] = payload_hash
        row["attempts"].append(attempt)
        run_start = time.monotonic()
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
            wall_seconds = time.monotonic() - run_start
            client_synthesis_seconds = None
            if isinstance(metrics, dict) and isinstance(metrics.get("synthesisMs"), (int, float)):
                client_synthesis_seconds = round(float(metrics["synthesisMs"]) / 1000.0, 6)
            audio_seconds = details["durationSeconds"]
            runtime = {
                "backend": "audio.cpp SSE",
                "devicePlacement": "not measured by HTTP runner",
                "clientWallSeconds": round(wall_seconds, 6),
                "synthesisWallSeconds": client_synthesis_seconds,
                "firstAudioSeconds": (
                    round(float(metrics["firstAudioMs"]) / 1000.0, 6)
                    if isinstance(metrics, dict) and isinstance(metrics.get("firstAudioMs"), (int, float))
                    else None
                ),
                "audioSeconds": audio_seconds,
                "rtf": round(client_synthesis_seconds / audio_seconds, 6)
                if client_synthesis_seconds is not None and audio_seconds > 0 else None,
                "audioDeltaCount": metrics.get("audioDeltaCount") if isinstance(metrics, dict) else None,
                "serverTiming": metrics.get("serverTiming") if isinstance(metrics, dict) else None,
                "wav": details,
            }
            row.update({
                "status": "completed",
                "wavSha256": wav_hash,
                "wavBytes": wav_size,
                "durationSeconds": audio_seconds,
                "wordsPerMinute": wpm,
                "runtimeMetadata": runtime,
                "warnings": list(dict.fromkeys(row.get("warnings", []) + warnings)),
                "error": None,
            })
            attempt.update({
                "status": "completed",
                "finishedUtc": utc_now(),
                "elapsedSeconds": round(wall_seconds, 6),
                "wavSha256": wav_hash,
            })
        except Exception as exc:
            row["status"] = "failed"
            row["error"] = "{}: {}".format(type(exc).__name__, exc)
            attempt.update({
                "status": "failed",
                "finishedUtc": utc_now(),
                "elapsedSeconds": round(time.monotonic() - run_start, 6),
                "error": row["error"],
            })
            if output.is_file():
                row["warnings"].append("A final WAV exists after a failed post-synthesis check; preserved for review.")
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
    corpus = repo_root / "work/ming3-tts-assessment-20261025/corpus-v3.json"
    output = repo_root / "work/breeze-tts-20261021/race-accents-02/breeze-paired-v3"
    return corpus, output


def main(argv: Optional[list[str]] = None) -> int:
    default_corpus, default_output = default_paths()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--corpus", default=str(default_corpus))
    parser.add_argument("--output-root", default=str(default_output))
    parser.add_argument("--endpoint", default=DEFAULT_ENDPOINT)
    parser.add_argument("--model", default=DEFAULT_MODEL)
    parser.add_argument("--timeout", type=int, default=600)
    parser.add_argument("--case", action="append", dest="cases", help="pair ID voiceId:moodId; repeat to select smoke cases")
    parser.add_argument("--retry-failed", action="store_true", help="retry failed pairs while preserving prior attempt records")
    parser.add_argument("--validate-only", action="store_true", help="verify frozen corpus and reference pins without contacting Breeze")
    args = parser.parse_args(argv)
    try:
        if args.validate_only:
            corpus, corpus_sha = load_corpus(args.corpus)
            print(json.dumps({
                "status": "valid",
                "corpusId": corpus["corpusId"],
                "corpusSha256": corpus_sha,
                "voices": len(corpus["voices"]),
                "moodsPerVoice": len(MOOD_ORDER),
                "expected": EXPECTED_PAIRS,
                "guidanceScale": GUIDANCE_SCALE,
            }, ensure_ascii=False))
            return 0
        manifest = run_batch(
            args.corpus, args.output_root, args.endpoint, args.model,
            timeout_seconds=args.timeout, cases=args.cases,
            retry_failed=args.retry_failed,
        )
        print(json.dumps({
            "status": manifest["status"],
            "manifest": str(Path(args.output_root).resolve() / "manifest.json"),
            **manifest["summary"],
        }, ensure_ascii=False))
        return 1 if manifest["summary"]["failed"] else 0
    except Exception as exc:
        print("{}: {}".format(type(exc).__name__, exc), file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
