#!/usr/bin/env python3
"""Render a fixed 20-sample English-accent audition against an existing server.

HTTP only: this script does not start/stop/inspect the server or model process,
and it never plays generated audio.
"""
from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import html
import json
from protocol_text import valid_identifier
import sys
import time
from pathlib import Path
from typing import Callable, Optional
from urllib.parse import quote

try:
    from .server_sse_client import synthesize
except ImportError:
    from server_sse_client import synthesize

EXPECTED_COUNT = 20
MANIFEST_NAME = "accent_voice_batch.json"
GALLERY_NAME = "gallery.html"


def load_inputs(corpus_path: str, reference_manifest_path: str) -> tuple[dict, dict]:
    corpus = json.loads(Path(corpus_path).read_text(encoding="utf-8"))
    if not isinstance(corpus, dict) or corpus.get("schemaVersion") != 1:
        raise ValueError("unsupported accent corpus schema")
    samples = corpus.get("samples")
    controls = corpus.get("trialControls")
    if not isinstance(samples, list) or len(samples) != EXPECTED_COUNT:
        raise ValueError("accent corpus must contain exactly 20 samples")
    if not isinstance(controls, dict) or controls.get("guidance_scale") != 4:
        raise ValueError("accent corpus must pin guidance_scale=4")
    if controls.get("sameTextForEverySample") is not True:
        raise ValueError("accent corpus must pin identical fixed text")
    if not isinstance(corpus.get("fixedText"), str) or not corpus["fixedText"].strip():
        raise ValueError("accent corpus fixedText must be non-empty")

    reference_manifest = json.loads(Path(reference_manifest_path).read_text(encoding="utf-8"))
    if reference_manifest.get("status") != "completed":
        raise ValueError("reference batch must be completed")
    if reference_manifest.get("fixedText") != corpus["fixedText"]:
        raise ValueError("fixed text differs from the speaker-reference batch")
    references = {row.get("sampleId"): row for row in reference_manifest.get("samples", [])}
    if len(references) != EXPECTED_COUNT:
        raise ValueError("reference batch must contain exactly 20 unique samples")

    seen = set()
    for sample in samples:
        sid = sample.get("sampleId")
        if not isinstance(sid, str) or not valid_identifier(sid) or sid in seen:
            raise ValueError("sample IDs must be unique safe filename components")
        seen.add(sid)
        if sample.get("seed") is None or isinstance(sample["seed"], bool) or not isinstance(sample["seed"], int):
            raise ValueError("each accent sample needs the reused integer seed")
        ref = references.get(sample.get("referenceSampleId"))
        if not ref or ref.get("status") != "succeeded":
            raise ValueError("missing successful fixed speaker reference for {}".format(sid))
        if ref.get("seed") != sample["seed"]:
            raise ValueError("seed mismatch for {}".format(sid))
        wav = Path(ref.get("outputWav", "")).resolve()
        if not wav.is_file():
            raise FileNotFoundError("speaker reference WAV is missing: {}".format(wav))
        expected_sha = ref.get("wavSha256")
        actual_sha = hashlib.sha256(wav.read_bytes()).hexdigest()
        if not expected_sha or actual_sha != expected_sha:
            raise ValueError("speaker reference WAV hash mismatch: {}".format(wav))
        sample["resolvedReferenceWav"] = str(wav)
        sample["referenceWavSha256"] = actual_sha
    return corpus, reference_manifest


def gallery_html(manifest: dict) -> str:
    groups = {}
    for row in manifest["samples"]:
        groups.setdefault(row["race"], []).append(row)
    lines = [
        "<!doctype html>", '<html lang="en"><head><meta charset="utf-8">',
        '<meta name="viewport" content="width=device-width, initial-scale=1">',
        "<title>Race accent audition</title>",
        "<style>body{font:16px system-ui;max-width:960px;margin:2rem auto;padding:0 1rem}section{border-top:1px solid #bbb;padding:1rem 0}.sample{padding:.75rem 0}audio{display:block;width:min(100%,36rem)}.meta{color:#444}</style></head><body>",
        "<h1>Race accent audition</h1>",
        "<p>Exploratory English-accent directions. The original matching race/gender WAV is the fixed speaker reference; same text, seed, emotion, delivery, and CFG are used. Accent choices are not canon or production geography policy. Audio plays only when selected.</p>",
        "<p>Completed: {} of {}. Failed: {}. No autoplay.</p>".format(manifest["summary"]["succeeded"], len(manifest["samples"]), manifest["summary"]["failed"]),
    ]
    for race, rows in groups.items():
        lines.append("<section><h2>{}</h2>".format(html.escape(race)))
        for row in rows:
            lines.append('<article class="sample"><h3>{} — {} ({})</h3>'.format(html.escape(race), html.escape(row["genderPresentation"]), html.escape(row["accentLabel"])))
            if row["status"] == "succeeded":
                rel = quote(Path(row["outputWav"]).name)
                lines.append('<audio controls preload="none" src="{}"></audio>'.format(html.escape(rel, quote=True)))
                lines.append('<p class="meta">Seed {} · CFG {} · {:.3f}s · first audio {:.3f}s</p>'.format(row["seed"], row["guidanceScale"], row.get("metrics", {}).get("synthesisMs", 0) / 1000, row.get("metrics", {}).get("firstAudioMs", 0) / 1000))
            else:
                lines.append('<p class="meta">Render failed: {}</p>'.format(html.escape(row.get("error", "unknown error"))))
            lines.append("</article>")
        lines.append("</section>")
    lines.append("</body></html>")
    return "\n".join(lines) + "\n"


def run_batch(corpus_path: str, reference_manifest_path: str, endpoint: str,
              model: str, output_dir: str, timeout_seconds: int = 600,
              synthesize_fn: Optional[Callable] = None) -> dict:
    corpus, reference_manifest = load_inputs(corpus_path, reference_manifest_path)
    render = synthesize_fn or synthesize
    destination = Path(output_dir).resolve()
    destination.mkdir(parents=True, exist_ok=True)
    outputs = [destination / (sample["sampleId"] + ".wav") for sample in corpus["samples"]]
    collisions = [p for p in outputs if p.exists()]
    if collisions:
        raise FileExistsError("refusing to overwrite existing output: {}".format(collisions[0]))
    for name in (MANIFEST_NAME, GALLERY_NAME):
        if (destination / name).exists():
            raise FileExistsError("refusing to overwrite existing output: {}".format(destination / name))

    manifest = {
        "schemaVersion": 1, "status": "running",
        "startedUtc": dt.datetime.now(dt.timezone.utc).isoformat(timespec="milliseconds").replace("+00:00", "Z"),
        "endpoint": endpoint, "model": model,
        "corpusPath": str(Path(corpus_path).resolve()),
        "referenceManifestPath": str(Path(reference_manifest_path).resolve()),
        "referenceBatchStartedUtc": reference_manifest.get("startedUtc"),
        "fixedText": corpus["fixedText"], "trialControls": corpus["trialControls"],
        "contentNotice": corpus.get("contentNotice"), "samples": [],
        "summary": {"total": len(corpus["samples"]), "succeeded": 0, "failed": 0, "wallMs": None},
    }
    batch_start = time.monotonic_ns()
    try:
        for sample, output in zip(corpus["samples"], outputs):
            row = {k: sample[k] for k in ("sampleId", "race", "genderPresentation", "accentLabel", "seed", "referenceSampleId")}
            row.update({"guidanceScale": 4.0, "instruction": sample["voiceDesignInstruction"],
                        "text": corpus["fixedText"], "referenceWav": sample["resolvedReferenceWav"],
                        "referenceWavSha256": sample["referenceWavSha256"],
                        "status": "failed", "outputWav": str(output)})
            call_start = time.monotonic_ns()
            try:
                metrics = render(endpoint, model, corpus["fixedText"], sample["voiceDesignInstruction"],
                                 sample["resolvedReferenceWav"], corpus["fixedText"], str(output), timeout_seconds,
                                 seed=sample["seed"], guidance_scale=4.0)
                if not output.is_file() or output.stat().st_size <= 44:
                    raise ValueError("synthesizer returned without a non-empty WAV file")
                row["status"] = "succeeded"
                row["metrics"] = metrics
                row["wavBytes"] = output.stat().st_size
                row["wavSha256"] = hashlib.sha256(output.read_bytes()).hexdigest()
                manifest["summary"]["succeeded"] += 1
            except Exception as exc:
                row["error"] = "{}: {}".format(type(exc).__name__, exc)
                manifest["summary"]["failed"] += 1
            row["elapsedMs"] = round((time.monotonic_ns() - call_start) / 1_000_000, 3)
            manifest["samples"].append(row)
        manifest["summary"]["wallMs"] = round((time.monotonic_ns() - batch_start) / 1_000_000, 3)
        manifest["status"] = "completed" if manifest["summary"]["failed"] == 0 else "completed-with-errors"
        manifest["finishedUtc"] = dt.datetime.now(dt.timezone.utc).isoformat(timespec="milliseconds").replace("+00:00", "Z")
    except BaseException:
        manifest["summary"]["wallMs"] = round((time.monotonic_ns() - batch_start) / 1_000_000, 3)
        manifest["status"] = "interrupted"
        manifest["finishedUtc"] = dt.datetime.now(dt.timezone.utc).isoformat(timespec="milliseconds").replace("+00:00", "Z")
        raise
    finally:
        (destination / MANIFEST_NAME).write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        (destination / GALLERY_NAME).write_text(gallery_html(manifest), encoding="utf-8")
    return manifest


def main(argv=None) -> int:
    here = Path(__file__).resolve().parent
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--corpus", default=str(here / "accent_voice_corpus.json"))
    parser.add_argument("--reference-manifest", default=str(Path(__file__).resolve().parents[2] / "work/breeze-tts-20261021/race-voices-01/race_voice_batch.json"))
    parser.add_argument("--endpoint", default="http://127.0.0.1:8094")
    parser.add_argument("--model", default="breeze")
    parser.add_argument("--output-dir")
    parser.add_argument("--timeout-seconds", type=int, default=600)
    parser.add_argument("--validate-only", action="store_true", help="verify corpus and fixed reference WAVs without sending requests")
    args = parser.parse_args(argv)
    if not args.validate_only and not args.output_dir:
        parser.error("--output-dir is required unless --validate-only is used")
    if not 1 <= args.timeout_seconds <= 3600:
        parser.error("--timeout-seconds must be between 1 and 3600")
    try:
        corpus, refs = load_inputs(args.corpus, args.reference_manifest)
        if args.validate_only:
            print(json.dumps({"status": "valid", "samples": len(corpus["samples"]), "referenceSamples": len(refs["samples"]), "guidanceScale": corpus["trialControls"]["guidance_scale"], "text": corpus["fixedText"]}, ensure_ascii=False))
            return 0
        manifest = run_batch(args.corpus, args.reference_manifest, args.endpoint, args.model, args.output_dir, args.timeout_seconds)
    except Exception as exc:
        print("accent_voice_batch: {}: {}".format(type(exc).__name__, exc), file=sys.stderr)
        return 1
    print(json.dumps({"status": manifest["status"], "summary": manifest["summary"], "manifest": str(Path(args.output_dir).resolve() / MANIFEST_NAME), "gallery": str(Path(args.output_dir).resolve() / GALLERY_NAME)}, ensure_ascii=False, sort_keys=True))
    return 0 if manifest["summary"]["failed"] == 0 else 1

if __name__ == "__main__":
    raise SystemExit(main())
