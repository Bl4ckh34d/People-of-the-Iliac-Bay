#!/usr/bin/env python3
"""Small offline-buffering client for the pinned audio.cpp Breeze SSE API."""

from __future__ import annotations

import argparse
import base64
import binascii
import json
import math
import os
import queue
import sys
import threading
import time
import urllib.error
import urllib.request
import uuid
import wave
from pathlib import Path
from typing import Callable, Iterable, Iterator, Optional, Tuple, Union


EventValue = Union[bytes, dict, None]
MAX_PCM_BYTES = 64 * 1024 * 1024
MAX_SSE_LINE_BYTES = 2 * 1024 * 1024
SSE_READ_QUEUE_LINES = 4
CANCEL_POLL_SECONDS = 0.05


class SynthesisCancelled(RuntimeError):
    """Raised after the owning request asks a live SSE stream to stop."""


class _CancellableResponseReader:
    """Read SSE on one daemon thread while the owner polls cancellation."""

    def __init__(self, request: urllib.request.Request, timeout_seconds: int,
                 cancel_event: Optional[threading.Event]) -> None:
        self._queue = queue.Queue(maxsize=SSE_READ_QUEUE_LINES)
        self._cancel_event = cancel_event or threading.Event()
        self._stop = threading.Event()
        self._response_lock = threading.Lock()
        self._response = None
        self._timeout_seconds = timeout_seconds
        self._thread = threading.Thread(target=self._run, args=(request,),
                                        name="breeze-sse-reader", daemon=True)
        self._thread.start()

    def _put(self, item) -> bool:
        while not self._stop.is_set() and not self._cancel_event.is_set():
            try:
                self._queue.put(item, timeout=CANCEL_POLL_SECONDS)
                return True
            except queue.Full:
                continue
        return False

    def _set_read_timeout(self, response) -> None:
        # urllib uses http.client.HTTPResponse; set only the connected socket's
        # read timeout. The short urlopen timeout bounds connection setup.
        candidates = [getattr(getattr(getattr(response, "fp", None), "raw", None), "_sock", None),
                      getattr(getattr(response, "fp", None), "_sock", None)]
        for candidate in candidates:
            if candidate is not None and hasattr(candidate, "settimeout"):
                candidate.settimeout(self._timeout_seconds)
                return

    def _run(self, request: urllib.request.Request) -> None:
        response = None
        try:
            response = urllib.request.urlopen(request, timeout=min(self._timeout_seconds, 1.0))
            with self._response_lock:
                self._response = response
                cancelled = self._stop.is_set() or self._cancel_event.is_set()
            if cancelled:
                response.close()
                return
            if response.status != 200:
                self._put(("error", RuntimeError("audio.cpp returned HTTP {}".format(response.status))))
                return
            self._set_read_timeout(response)
            while not self._stop.is_set() and not self._cancel_event.is_set():
                try:
                    line = response.readline(MAX_SSE_LINE_BYTES + 1)
                except TypeError:
                    # Small injected readers used by offline tests may expose
                    # the no-argument file-like signature only.
                    line = response.readline()
                if len(line) > MAX_SSE_LINE_BYTES:
                    raise ValueError("SSE line exceeds the 2 MiB response limit")
                if not line:
                    self._put(("end", None))
                    return
                acknowledged = threading.Event()
                if not self._put(("line", line, acknowledged)):
                    return
                while (not acknowledged.wait(CANCEL_POLL_SECONDS) and
                       not self._stop.is_set() and not self._cancel_event.is_set()):
                    pass
        except Exception as exc:
            if not self._stop.is_set() and not self._cancel_event.is_set():
                self._put(("error", exc))
        finally:
            if response is not None:
                with self._response_lock:
                    if self._response is response:
                        self._response = None
                try:
                    response.close()
                except Exception:
                    pass

    def close(self) -> None:
        self._stop.set()
        with self._response_lock:
            response = self._response
            self._response = None
        if response is not None:
            try:
                response.close()
            except Exception:
                pass

    def iter_lines(self):
        while True:
            if self._cancel_event.is_set():
                self.close()
                raise SynthesisCancelled("audio.cpp synthesis was cancelled")
            try:
                item = self._queue.get(timeout=CANCEL_POLL_SECONDS)
            except queue.Empty:
                if not self._thread.is_alive():
                    if self._cancel_event.is_set():
                        raise SynthesisCancelled("audio.cpp synthesis was cancelled")
                    raise RuntimeError("audio.cpp SSE reader stopped without a terminal event")
                continue
            kind, value = item[0], item[1:]
            if kind == "line":
                # The producer waits for this ACK before reading the next SSE
                # line, so a slow spool callback applies real transport backpressure.
                line, acknowledged = value
                try:
                    yield line
                finally:
                    acknowledged.set()
            elif kind == "end":
                return
            else:
                raise value[0]


def build_request(model: str, text: str, instruction: str,
                  reference_wav: Optional[str] = None,
                  reference_text: Optional[str] = None,
                  seed: Optional[int] = None,
                  guidance_scale: float = 4.0) -> dict:
    if not model.strip() or not text.strip() or not instruction.strip():
        raise ValueError("model, text and instruction must be non-empty")
    has_reference_wav = reference_wav is not None
    has_reference_text = reference_text is not None
    if has_reference_wav != has_reference_text:
        raise ValueError("speaker reference WAV and transcript must be both supplied or both omitted")
    if has_reference_wav and (not reference_wav.strip() or not reference_text.strip()):
        raise ValueError("speaker reference WAV and transcript must be non-empty")
    if seed is not None and (isinstance(seed, bool) or not isinstance(seed, int) or seed < 0):
        raise ValueError("seed must be a non-negative integer")
    if isinstance(guidance_scale, bool) or not isinstance(guidance_scale, (int, float)) or not math.isfinite(guidance_scale) or guidance_scale < 0:
        raise ValueError("guidance_scale must be a finite non-negative number")
    payload = {
        "model": model,
        "input": text,
        "response_format": "pcm",
        "stream_format": "sse",
        "options": {
            "instruct": instruction,
            "guidance_scale": float(guidance_scale),
            "stream_lookahead_margin": 0,
            "stream_chunk_policy": "grow",
        },
    }
    if has_reference_wav:
        payload["voice_ref"] = {"type": "path", "path": str(Path(reference_wav).resolve())}
        payload["reference_text"] = reference_text
    if seed is not None:
        payload["options"]["seed"] = seed
    return payload


def _iter_sse_data(chunks: Iterable[bytes]) -> Iterator[str]:
    buffer = b""
    data_lines = []

    def dispatch() -> Optional[str]:
        if not data_lines:
            return None
        message = "\n".join(data_lines)
        data_lines.clear()
        return message

    for chunk in chunks:
        if not isinstance(chunk, bytes):
            raise TypeError("SSE transport chunks must be bytes")
        buffer += chunk
        while b"\n" in buffer:
            raw_line, buffer = buffer.split(b"\n", 1)
            if raw_line.endswith(b"\r"):
                raw_line = raw_line[:-1]
            try:
                line = raw_line.decode("utf-8")
            except UnicodeDecodeError as exc:
                raise ValueError("SSE stream contains invalid UTF-8") from exc
            if line == "":
                message = dispatch()
                if message is not None:
                    yield message
            elif line.startswith(":"):
                continue
            elif line.startswith("data:"):
                value = line[5:]
                data_lines.append(value[1:] if value.startswith(" ") else value)


def collect_audio_events(chunks: Iterable[bytes]) -> Iterator[Tuple[str, EventValue]]:
    saw_audio = False
    saw_done = False
    saw_sentinel = False
    for message in _iter_sse_data(chunks):
        if saw_sentinel:
            raise ValueError("SSE stream contains data after [DONE]")
        if message == "[DONE]":
            if not saw_done or not saw_audio:
                raise ValueError("SSE [DONE] arrived before complete audio")
            saw_sentinel = True
            yield "[DONE]", None
            continue
        try:
            event = json.loads(message)
        except json.JSONDecodeError as exc:
            raise ValueError("SSE data event is not valid JSON") from exc
        if not isinstance(event, dict) or not isinstance(event.get("type"), str):
            raise ValueError("SSE data event has no type")
        event_type = event["type"]
        if event_type == "speech.audio.delta":
            encoded = event.get("audio")
            if not isinstance(encoded, str):
                raise ValueError("speech.audio.delta has no base64 audio")
            try:
                pcm = base64.b64decode(encoded, validate=True)
            except (binascii.Error, ValueError) as exc:
                raise ValueError("speech.audio.delta contains invalid base64") from exc
            if not pcm:
                raise ValueError("speech.audio.delta is empty")
            saw_audio = True
            yield event_type, pcm
        elif event_type == "speech.audio.done":
            saw_done = True
            yield event_type, event.get("timing")
        elif event_type == "error" or event_type.endswith(".error"):
            raise ValueError("audio.cpp SSE error: {}".format(event.get("message", event)))
        else:
            raise ValueError("unexpected audio.cpp SSE event: {}".format(event_type))
    if not saw_sentinel:
        raise ValueError("SSE stream ended without [DONE]")


def synthesize(endpoint: str, model: str, text: str, instruction: str,
               reference_wav: Optional[str] = None,
               reference_text: Optional[str] = None,
               output_wav: Optional[str] = None,
               timeout_seconds: int = 600, *, seed: Optional[int] = None,
               guidance_scale: float = 4.0,
               on_pcm_delta: Optional[Callable[[bytes], None]] = None,
               cancel_event: Optional[threading.Event] = None) -> dict:
    if output_wav is None:
        raise ValueError("output_wav is required")
    output = Path(output_wav).resolve()
    if output.exists():
        raise FileExistsError("refusing to overwrite existing output: {}".format(output))
    if reference_wav is not None and not Path(reference_wav).is_file():
        raise FileNotFoundError("speaker reference WAV does not exist: {}".format(reference_wav))
    payload = build_request(model, text, instruction, reference_wav, reference_text,
                            seed=seed, guidance_scale=guidance_scale)
    request = urllib.request.Request(
        endpoint.rstrip("/") + "/v1/audio/speech",
        data=json.dumps(payload, ensure_ascii=False).encode("utf-8"),
        headers={"Content-Type": "application/json", "Accept": "text/event-stream"},
        method="POST",
    )

    start_ns = time.monotonic_ns()
    first_audio_ns = None
    pcm_bytes = 0
    writer_remainder = b""
    delta_count = 0
    server_timing = None
    reader = _CancellableResponseReader(request, timeout_seconds, cancel_event)
    output.parent.mkdir(parents=True, exist_ok=True)
    temporary_output = output.with_name(output.name + "." + uuid.uuid4().hex + ".tmp.wav")
    try:
        with wave.open(str(temporary_output), "wb") as writer:
            writer.setnchannels(1)
            writer.setsampwidth(2)
            writer.setframerate(24000)
            for event_type, value in collect_audio_events(reader.iter_lines()):
                if event_type == "speech.audio.delta":
                    if first_audio_ns is None:
                        first_audio_ns = time.monotonic_ns()
                    pcm_bytes += len(value)
                    delta_count += 1
                    if pcm_bytes > MAX_PCM_BYTES:
                        raise ValueError("PCM response exceeds 64 MiB trial limit")
                    if on_pcm_delta is not None:
                        # Callback is synchronous so a bounded consumer can
                        # apply backpressure before the next SSE event is read.
                        on_pcm_delta(value)
                    combined = writer_remainder + value
                    aligned_size = len(combined) & ~1
                    if aligned_size:
                        writer.writeframesraw(combined[:aligned_size])
                    writer_remainder = combined[aligned_size:]
                elif event_type == "speech.audio.done":
                    server_timing = value
        if pcm_bytes == 0 or pcm_bytes % 2 or writer_remainder:
            raise ValueError("PCM16 byte count must be non-zero and sample-aligned")
        os.replace(temporary_output, output)
    except urllib.error.HTTPError as exc:
        detail = exc.read(4096).decode("utf-8", errors="replace")
        raise RuntimeError("audio.cpp HTTP {}: {}".format(exc.code, detail)) from exc
    finally:
        reader.close()
        temporary_output.unlink(missing_ok=True)
    end_ns = time.monotonic_ns()
    return {
        "outputWav": str(output),
        "pcmBytes": pcm_bytes,
        "sampleRate": 24000,
        "channels": 1,
        "synthesisMs": round((end_ns - start_ns) / 1_000_000, 3),
        "firstAudioMs": (round((first_audio_ns - start_ns) / 1_000_000, 3)
                         if first_audio_ns is not None else None),
        "audioDeltaCount": delta_count,
        "serverTiming": server_timing,
    }


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--endpoint", default="http://127.0.0.1:8094")
    parser.add_argument("--model", default="breeze")
    parser.add_argument("--text", required=True)
    parser.add_argument("--instruction", required=True)
    parser.add_argument("--reference-wav",
                        help="path on the audio.cpp server host")
    parser.add_argument("--reference-text")
    parser.add_argument("--seed", type=int)
    parser.add_argument("--guidance-scale", type=float, default=4.0)
    parser.add_argument("--output-wav", required=True)
    parser.add_argument("--timeout-seconds", type=int, default=600)
    args = parser.parse_args(argv)
    if not 1 <= args.timeout_seconds <= 3600:
        parser.error("--timeout-seconds must be between 1 and 3600")
    if (args.reference_wav is None) != (args.reference_text is None):
        parser.error("--reference-wav and --reference-text must be supplied together")
    try:
        result = synthesize(args.endpoint, args.model, args.text, args.instruction,
                            args.reference_wav, args.reference_text,
                            args.output_wav, args.timeout_seconds,
                            seed=args.seed, guidance_scale=args.guidance_scale)
    except Exception as exc:
        print("server_sse_client: {}".format(exc), file=sys.stderr)
        return 1
    print(json.dumps(result, ensure_ascii=False, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
