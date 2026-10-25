#!/usr/bin/env python3
"""Render the fixed 20-sample race comparison against an already-running server.

This client performs HTTP requests only. It does not start, stop, or inspect the
server or model process, and it never plays generated audio.
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
except ImportError:  # Direct script execution from this directory.
    from server_sse_client import synthesize


EXPECTED_SAMPLE_COUNT = 20
MANIFEST_NAME = "race_voice_batch.json"
GALLERY_NAME = "gallery.html"


def _load_corpus(corpus_path: str) -> dict:
    with Path(corpus_path).open("r", encoding="utf-8") as source:
        corpus = json.load(source)
    if not isinstance(corpus, dict) or corpus.get("schemaVersion") != 1:
        raise ValueError("unsupported race voice corpus schema")
    controls = corpus.get("trialControls")
    samples = corpus.get("samples")
    if not isinstance(controls, dict) or not isinstance(samples, list):
        raise ValueError("corpus must contain trialControls and samples")
    if len(samples) != EXPECTED_SAMPLE_COUNT:
        raise ValueError("race voice corpus must contain exactly 20 samples")
    if controls.get("sameTextForEverySample") is not True:
        raise ValueError("corpus must pin sameTextForEverySample=true")
    if not isinstance(corpus.get("fixedText"), str) or not corpus["fixedText"].strip():
        raise ValueError("corpus fixedText must be non-empty")
    if controls.get("guidance_scale") != 4:
        raise ValueError("corpus must pin guidance_scale=4")

    seen = set()
    for index, sample in enumerate(samples):
        if not isinstance(sample, dict):
            raise ValueError("sample {} must be an object".format(index))
        sample_id = sample.get("sampleId")
        if not isinstance(sample_id, str) or not valid_identifier(sample_id):
            raise ValueError("sampleId must be a safe filename component")
        if sample_id in seen:
            raise ValueError("sampleId values must be unique")
        seen.add(sample_id)
        if not isinstance(sample.get("race"), str) or not sample["race"].strip():
            raise ValueError("sample {} has no race category".format(sample_id))
        if not isinstance(sample.get("genderPresentation"), str) or not sample["genderPresentation"].strip():
            raise ValueError("sample {} has no genderPresentation".format(sample_id))
        instruction = sample.get("voiceDesignInstruction")
        if not isinstance(instruction, str) or not instruction.strip():
            raise ValueError("sample {} has no voiceDesignInstruction".format(sample_id))
        seed = sample.get("seed")
        if isinstance(seed, bool) or not isinstance(seed, int) or seed < 0:
            raise ValueError("sample {} must have a non-negative integer seed".format(sample_id))
    return corpus


def _gallery_html(manifest: dict) -> str:
    groups = {}
    for row in manifest["samples"]:
        groups.setdefault(row["race"], []).append(row)
    lines = [
        "<!doctype html>",
        '<html lang="en"><head><meta charset="utf-8">',
        '<meta name="viewport" content="width=device-width, initial-scale=1">',
        "<title>Race voice comparison</title>",
        "<style>body{font:16px system-ui;max-width:960px;margin:2rem auto;padding:0 1rem}"
        "section{border-top:1px solid #bbb;padding:1rem 0}.sample{padding:.75rem 0}"
        "audio{display:block;width:min(100%,36rem)}.meta{color:#444}</style></head><body>",
        "<h1>Race voice comparison</h1>",
        "<p>Fictional voice-design directions; the same fixed text and Southern English accent are used for every sample. These are listening-trial choices, not claims about TES canon or real-world racial voice traits.</p>",
        "<p>Completed: {} of {}. Failed: {}. Audio plays only when selected.</p>".format(
            manifest["summary"]["succeeded"], len(manifest["samples"]), manifest["summary"]["failed"]),
    ]
    for race, rows in groups.items():
        lines.append("<section><h2>{}</h2>".format(html.escape(race)))
        for row in rows:
            title = "{} — {}".format(race, row["genderPresentation"])
            lines.append('<article class="sample"><h3>{}</h3>'.format(html.escape(title)))
            if row["status"] == "succeeded":
                rel = quote(Path(row["outputWav"]).name)
                lines.append('<audio controls preload="none" src="{}"></audio>'.format(html.escape(rel, quote=True)))
                lines.append('<p class="meta">Seed {} · guidance {} · {:.3f}s · first audio {:.3f}s</p>'.format(
                    row["seed"], row["guidanceScale"],
                    row["metrics"].get("synthesisMs", 0) / 1000,
                    row["metrics"].get("firstAudioMs", 0) / 1000))
            else:
                lines.append('<p class="meta">Render failed: {}</p>'.format(html.escape(row.get("error", "unknown error"))))
            lines.append("</article>")
        lines.append("</section>")
    notice = manifest.get("contentNotice")
    if notice:
        lines.append("<footer><p>{}</p></footer>".format(html.escape(notice)))
    lines.append("</body></html>")
    return "\n".join(lines) + "\n"


def run_batch(corpus_path: str, endpoint: str, model: str, output_dir: str,
              timeout_seconds: int = 600,
              synthesize_fn: Optional[Callable] = None) -> dict:
    """Render all corpus samples once, in order, without retries or playback."""
    corpus = _load_corpus(corpus_path)
    render = synthesize_fn or synthesize
    destination = Path(output_dir).resolve()
    destination.mkdir(parents=True, exist_ok=True)
    output_paths = [destination / (sample["sampleId"] + ".wav") for sample in corpus["samples"]]
    collisions = [str(path) for path in output_paths if path.exists()]
    if collisions:
        raise FileExistsError("refusing to overwrite existing output: {}".format(collisions[0]))
    for name in (MANIFEST_NAME, GALLERY_NAME):
        if (destination / name).exists():
            raise FileExistsError("refusing to overwrite existing output: {}".format(destination / name))

    started = dt.datetime.now(dt.timezone.utc).isoformat(timespec="milliseconds").replace("+00:00", "Z")
    manifest = {
        "schemaVersion": 1,
        "status": "running",
        "startedUtc": started,
        "endpoint": endpoint,
        "model": model,
        "corpusPath": str(Path(corpus_path).resolve()),
        "fixedText": corpus["fixedText"],
        "trialControls": corpus["trialControls"],
        "contentNotice": corpus.get("contentNotice"),
        "samples": [],
        "summary": {"total": len(corpus["samples"]), "succeeded": 0, "failed": 0,
                    "wallMs": None},
    }
    batch_start = time.monotonic_ns()
    try:
        for sample, output in zip(corpus["samples"], output_paths):
            row = {
                "sampleId": sample["sampleId"],
                "race": sample["race"],
                "genderPresentation": sample["genderPresentation"],
                "adultAgePresentation": sample.get("adultAgePresentation"),
                "seed": sample["seed"],
                "guidanceScale": float(corpus["trialControls"]["guidance_scale"]),
                "instruction": sample["voiceDesignInstruction"],
                "text": corpus["fixedText"],
                "status": "failed",
                "outputWav": str(output),
            }
            call_start = time.monotonic_ns()
            try:
                metrics = render(
                    endpoint, model, corpus["fixedText"], sample["voiceDesignInstruction"],
                    None, None, str(output), timeout_seconds,
                    seed=sample["seed"], guidance_scale=row["guidanceScale"])
                if not output.is_file() or output.stat().st_size <= 44:
                    raise ValueError("synthesizer returned without a non-empty WAV file")
                row["status"] = "succeeded"
                row["metrics"] = metrics
                row["elapsedMs"] = round((time.monotonic_ns() - call_start) / 1_000_000, 3)
                row["wavBytes"] = output.stat().st_size
                row["wavSha256"] = hashlib.sha256(output.read_bytes()).hexdigest()
                manifest["summary"]["succeeded"] += 1
            except Exception as exc:  # Record each one-shot result; never reroll.
                row["error"] = "{}: {}".format(type(exc).__name__, exc)
                row["elapsedMs"] = round((time.monotonic_ns() - call_start) / 1_000_000, 3)
                manifest["summary"]["failed"] += 1
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
        # Keep partial/successful evidence inspectable even if a caller interrupts the batch.
        (destination / MANIFEST_NAME).write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        (destination / GALLERY_NAME).write_text(_gallery_html(manifest), encoding="utf-8")
    return manifest


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--corpus", default=str(Path(__file__).with_name("race_voice_corpus.json")))
    parser.add_argument("--endpoint", default="http://127.0.0.1:8094")
    parser.add_argument("--model", default="breeze")
    parser.add_argument("--output-dir", required=True)
    parser.add_argument("--timeout-seconds", type=int, default=600)
    args = parser.parse_args(argv)
    if not 1 <= args.timeout_seconds <= 3600:
        parser.error("--timeout-seconds must be between 1 and 3600")
    try:
        manifest = run_batch(args.corpus, args.endpoint, args.model, args.output_dir,
                             args.timeout_seconds)
    except Exception as exc:
        print("race_voice_batch: {}: {}".format(type(exc).__name__, exc), file=sys.stderr)
        return 1
    print(json.dumps({"status": manifest["status"], "summary": manifest["summary"],
                      "manifest": str(Path(args.output_dir).resolve() / MANIFEST_NAME),
                      "gallery": str(Path(args.output_dir).resolve() / GALLERY_NAME)},
                     ensure_ascii=False, sort_keys=True))
    return 0 if manifest["summary"]["failed"] == 0 else 1


if __name__ == "__main__":
    raise SystemExit(main())
