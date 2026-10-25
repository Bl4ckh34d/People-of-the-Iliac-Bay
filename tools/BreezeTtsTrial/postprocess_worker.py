"""Bounded, opt-in CPU alignment and viseme work for finalized stream WAVs."""

from __future__ import annotations

import hashlib
import json
import os
import queue
import tempfile
import threading
import uuid
from dataclasses import dataclass
from pathlib import Path
from typing import Optional


@dataclass(frozen=True)
class PostprocessSettings:
    aligner_cli: Optional[Path] = None
    aligner_model: Optional[Path] = None
    aligner_sha256: Optional[str] = None
    rhubarb_cli: Optional[Path] = None

    def validate(self) -> None:
        if (self.aligner_cli is None) != (self.aligner_model is None):
            raise ValueError("aligner CLI and model must be configured together")
        for label, path in (("aligner CLI", self.aligner_cli), ("aligner model", self.aligner_model),
                            ("Rhubarb CLI", self.rhubarb_cli)):
            if path is not None and not Path(path).is_file():
                raise FileNotFoundError("{} is not a local file: {}".format(label, path))
        if self.aligner_model is not None:
            if not isinstance(self.aligner_sha256, str) or len(self.aligner_sha256) != 64:
                raise ValueError("aligner SHA-256 is required when alignment is enabled")
            int(self.aligner_sha256, 16)

    @property
    def enabled(self) -> bool:
        return self.aligner_cli is not None or self.rhubarb_cli is not None


@dataclass(frozen=True)
class PostprocessJob:
    stream_id: str
    conversation_token: str
    person_id: str
    sentence_sequence: Optional[int]
    audio_path: Path
    text: str
    cancel_path: Path
    stream_dir: Path
    output_dir: Path


class BoundedPostprocessWorker:
    """At most one active CPU job and one pending item; never blocks playback."""

    def __init__(self, settings: PostprocessSettings, *, timeout_seconds: int = 180) -> None:
        settings.validate()
        if timeout_seconds < 1:
            raise ValueError("postprocessor timeout must be positive")
        self.settings = settings
        self.timeout_seconds = timeout_seconds
        self._jobs = queue.Queue(maxsize=1)
        self._thread = None
        if settings.enabled:
            self._thread = threading.Thread(target=self._run, name="breeze-cpu-postprocess", daemon=True)
            self._thread.start()

    def submit(self, job: PostprocessJob) -> str:
        if not self.settings.enabled:
            return "disabled"
        if not job.stream_dir.is_dir() or not job.output_dir.is_dir():
            return "closed"
        if self._cancelled(job):
            return "cancelled"
        try:
            self._jobs.put_nowait(job)
            return "queued"
        except queue.Full:
            return "skipped-busy"

    @staticmethod
    def _cancelled(job: PostprocessJob) -> bool:
        # Unity owns this tree. A removed directory means close/cleanup won.
        return (job.cancel_path.exists() or not job.stream_dir.is_dir() or
                not job.audio_path.is_file())

    def _run(self) -> None:
        while True:
            job = self._jobs.get()
            try:
                self._process(job)
            except Exception:
                # Optional postprocessing must not affect published speech.
                pass
            finally:
                self._jobs.task_done()

    def _process(self, job: PostprocessJob) -> None:
        if self._cancelled(job) or not job.audio_path.is_file():
            return
        try:
            from tools.BreezeTtsTrial.align_words import _sha256_file
        except ModuleNotFoundError:
            from align_words import _sha256_file

        audio_hash = _sha256_file(job.audio_path)
        base = job.audio_path.stem
        outputs = []
        with tempfile.TemporaryDirectory(prefix=".postprocess-", dir=str(job.output_dir)) as temp:
            temp_dir = Path(temp)
            if self.settings.aligner_cli is not None and not self._cancelled(job):
                try:
                    from tools.BreezeTtsTrial.align_words import run_alignment
                except ModuleNotFoundError:
                    from align_words import run_alignment
                timeline = run_alignment(str(self.settings.aligner_cli), str(self.settings.aligner_model),
                    str(job.audio_path), job.text, str(temp_dir / (base + ".alignment.json")),
                    threads=1, timeout_seconds=self.timeout_seconds,
                    expected_model_sha256=self.settings.aligner_sha256)
                timeline["streamBinding"] = self._binding(job, audio_hash, timeline)
                outputs.append((timeline, job.output_dir / (base + ".alignment.json")))
            if self.settings.rhubarb_cli is not None and not self._cancelled(job):
                try:
                    from tools.BreezeTtsTrial.rhubarb_visemes import run_rhubarb
                except ModuleNotFoundError:
                    from rhubarb_visemes import run_rhubarb
                timeline = run_rhubarb(str(self.settings.rhubarb_cli), str(job.audio_path), job.text,
                    str(temp_dir / (base + ".visemes.json")), threads=2,
                    timeout_seconds=min(self.timeout_seconds, 90))
                timeline["streamBinding"] = self._binding(job, audio_hash, timeline)
                outputs.append((timeline, job.output_dir / (base + ".visemes.json")))

            for index, (timeline, destination) in enumerate(outputs):
                if self._cancelled(job) or not destination.parent.is_dir():
                    return
                temporary = temp_dir / ("publish-{}.json".format(index))
                temporary.write_text(json.dumps(timeline, ensure_ascii=False,
                    sort_keys=True, separators=(",", ":")) + "\n", encoding="utf-8")
                try:
                    os.link(str(temporary), str(destination))
                except FileExistsError:
                    # Existing timelines are immutable; never overwrite them.
                    continue

    @staticmethod
    def _binding(job: PostprocessJob, audio_hash: str, timeline: dict) -> dict:
        return {"schemaVersion": 1, "streamId": job.stream_id,
            "conversationToken": job.conversation_token, "personId": job.person_id,
            "scope": "sentence" if job.sentence_sequence is not None else "final",
            "sentenceSequence": job.sentence_sequence,
            "textSha256": hashlib.sha256(job.text.encode("utf-8")).hexdigest(),
            "audioSha256": audio_hash, "sampleRate": timeline["audioSampleRate"],
            "sampleFrames": timeline["audioFrames"]}

