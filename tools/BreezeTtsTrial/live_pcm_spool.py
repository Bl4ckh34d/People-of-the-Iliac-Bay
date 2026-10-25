#!/usr/bin/env python3
"""Atomic, bounded PCM chunk spool for a live Unity reader.

Chunk sequence and sample-frame offsets are transport metadata only; they do
not represent word alignment or phoneme timing.
"""

from __future__ import annotations

import hashlib
import errno
import json
import os
from protocol_text import valid_source
import time
import uuid
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Dict, Optional


CHUNK_FRAMES = 2400  # 100 ms at 24 kHz mono PCM16
MAX_UNACKED_BYTES = 1024 * 1024
MAX_SENTENCE_CHARS = 480
MAX_PENDING_SENTENCES = 8
MAX_PENDING_CHARS = 480
MAX_ATOMIC_REPLACE_ATTEMPTS = 8
ACK_READ_RETRY_ATTEMPTS = 4
ACK_READ_RETRY_DELAY_SECONDS = 0.01


def _is_transient_ack_read_error(error: OSError) -> bool:
    """Recognize only the short Windows atomic-replace read races."""
    winerror = getattr(error, "winerror", None)
    return (isinstance(error, FileNotFoundError) or winerror in {5, 32, 33} or
            (os.name == "nt" and isinstance(error, PermissionError) and
             error.errno == errno.EACCES and winerror is None))


def validate_voice_identity(value: Any) -> Optional[Dict[str, Any]]:
    """Validate identity metadata for evidence; never use it as a synthesis command."""
    if value is None:
        return None
    if not isinstance(value, dict) or set(value) != {"schemaVersion", "sex", "race", "age", "origin"}:
        raise ValueError("voiceIdentity must use the closed schema-1 object")
    if isinstance(value["schemaVersion"], bool) or value["schemaVersion"] != 1:
        raise ValueError("voiceIdentity schemaVersion must equal 1")
    for field in ("sex", "race", "origin"):
        row = value[field]
        if not isinstance(row, dict) or set(row) != {"value", "known", "source"}:
            raise ValueError("voiceIdentity.{} has an invalid shape".format(field))
        if not isinstance(row["known"], bool):
            raise ValueError("voiceIdentity.{}.known must be boolean".format(field))
        item = row["value"]
        if not isinstance(item, str) or not item or len(item) > 128 or any(ord(ch) < 32 for ch in item):
            raise ValueError("voiceIdentity.{}.value is invalid".format(field))
        if not isinstance(row["source"], str) or not valid_source(row["source"]):
            raise ValueError("voiceIdentity.{}.source is invalid".format(field))
        if row["known"] == (item == "unknown"):
            raise ValueError("voiceIdentity.{} known flag conflicts with unknown value".format(field))
    if value["sex"]["value"] not in {"male", "female", "unknown"}:
        raise ValueError("voiceIdentity.sex.value is invalid")
    age = value["age"]
    if not isinstance(age, dict) or set(age) != {"value", "confidence", "source"}:
        raise ValueError("voiceIdentity.age has an invalid shape")
    if age["value"] not in {"adult", "child", "young-adult", "mature", "elderly", "unknown"}:
        raise ValueError("voiceIdentity.age.value is invalid")
    if age["confidence"] not in {"high", "medium", "low", "unknown"}:
        raise ValueError("voiceIdentity.age.confidence is invalid")
    if age["value"] == "unknown" and age["confidence"] != "unknown":
        raise ValueError("unknown age requires unknown confidence")
    if not isinstance(age["source"], str) or not valid_source(age["source"]):
        raise ValueError("voiceIdentity.age.source is invalid")
    return value


@dataclass(frozen=True)
class StreamBegin:
    stream_id: str
    conversation_token: str
    person_id: str
    display_name: str
    voice_gender: Optional[str]
    delivery_context: Optional[Dict[str, Any]]
    voice_identity: Optional[Dict[str, Any]]


def read_stream_begin(path: Path, expected_stream_id: str) -> StreamBegin:
    if path.stat().st_size > 64 * 1024:
        raise ValueError("stream begin file exceeds size limit")
    value = json.loads(path.read_text(encoding="utf-8-sig"))
    if not isinstance(value, dict):
        raise ValueError("stream begin must be a JSON object")
    required = {"schemaVersion", "mode", "streamId", "conversationToken", "personId",
                "displayName", "sampleRate", "channels"}
    allowed = required | {"voiceGender", "deliveryContext", "voiceIdentity"}
    if not required.issubset(value):
        raise ValueError("stream begin is missing required binding fields")
    if set(value) - allowed:
        raise ValueError("stream begin contains unsupported fields")
    if (isinstance(value["schemaVersion"], bool) or value["schemaVersion"] != 1 or
            value["mode"] != "streaming-live-pcm-v1"):
        raise ValueError("unsupported stream begin schema or mode")
    if value["sampleRate"] != 24000 or value["channels"] != 1:
        raise ValueError("stream format must be mono PCM16 at 24000 Hz")
    stream_id = value["streamId"]
    if stream_id != expected_stream_id or not stream_id or len(stream_id) > 128 or any(
            not (char.isalnum() or char in "._-") for char in stream_id):
        raise ValueError("streamId must match its safe request directory")
    for key, maximum in (("conversationToken", 128), ("personId", 240), ("displayName", 96)):
        if not isinstance(value[key], str) or not value[key].strip() or len(value[key]) > maximum:
            raise ValueError("stream begin {} must be non-empty".format(key))
    voice_gender = value.get("voiceGender")
    if voice_gender is not None and voice_gender not in {"female", "male", "unknown"}:
        raise ValueError("voiceGender is invalid")
    voice_identity = validate_voice_identity(value.get("voiceIdentity"))
    if voice_identity is not None and len(json.dumps(voice_identity, ensure_ascii=False)) > 4096:
        raise ValueError("voiceIdentity exceeds its size limit")
    delivery_context = value.get("deliveryContext")
    if delivery_context is not None and not isinstance(delivery_context, dict):
        raise ValueError("deliveryContext must be an object when provided")
    return StreamBegin(stream_id, value["conversationToken"], value["personId"], value["displayName"],
                       voice_gender, delivery_context, voice_identity)


def read_sentence(path: Path, begin: StreamBegin, expected_sequence: int) -> str:
    if path.stat().st_size > 16 * 1024:
        raise ValueError("stream sentence file exceeds size limit")
    value = json.loads(path.read_text(encoding="utf-8-sig"))
    if (not isinstance(value, dict) or isinstance(value.get("schemaVersion"), bool) or
            value.get("schemaVersion") != 1):
        raise ValueError("stream sentence has unsupported schema")
    if (value.get("streamId") != begin.stream_id or
            value.get("conversationToken") != begin.conversation_token or
            value.get("personId") != begin.person_id):
        raise ValueError("stream sentence binding does not match begin")
    sequence = value.get("sequence")
    if isinstance(sequence, bool) or sequence != expected_sequence:
        raise ValueError("stream sentence sequence is not contiguous")
    text = value.get("exactText")
    if not isinstance(text, str) or not text.strip() or len(text) > MAX_SENTENCE_CHARS:
        raise ValueError("exactText must be non-empty and within the sentence limit")
    if any(ord(char) < 32 and char not in "\n\r\t" for char in text):
        raise ValueError("exactText contains unsupported control characters")
    return text


def validate_pending_sentence_queue(stream_input: Path, begin: StreamBegin,
                                    next_sequence: int) -> int:
    """Fail closed if producer files exceed the agreed bounded FIFO."""
    pending = []
    for path in stream_input.glob("*.json"):
        if not path.stem.isdigit():
            continue
        sequence = int(path.stem)
        if sequence >= next_sequence:
            pending.append((sequence, path))
    pending.sort(key=lambda item: item[0])
    if pending and pending[0][0] != next_sequence:
        raise ValueError("pending sentence sequence has a gap")
    if len(pending) > MAX_PENDING_SENTENCES:
        raise ValueError("pending sentence queue exceeds eight segments")
    total_chars = 0
    for sequence, path in pending:
        total_chars += len(read_sentence(path, begin, sequence))
    if total_chars > MAX_PENDING_CHARS:
        raise ValueError("pending sentence queue exceeds 480 aggregate characters")
    return len(pending)


class StreamCancelled(RuntimeError):
    pass


class StreamBackpressureTimeout(TimeoutError):
    pass


def atomic_json(path: Path, value: Dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + "." + uuid.uuid4().hex + ".tmp")
    try:
        with temporary.open("w", encoding="utf-8", newline="\n") as stream:
            json.dump(value, stream, ensure_ascii=False, separators=(",", ":"))
            stream.write("\n")
            stream.flush()
            os.fsync(stream.fileno())
        delay = 0.01
        for attempt in range(MAX_ATOMIC_REPLACE_ATTEMPTS):
            try:
                os.replace(temporary, path)
                return
            except PermissionError as error:
                # Unity may briefly hold stream.json without delete sharing while
                # it polls. Retry only Windows access/share/lock violations; all
                # other permission failures still surface immediately.
                if getattr(error, "winerror", None) not in {5, 32, 33} or attempt + 1 >= MAX_ATOMIC_REPLACE_ATTEMPTS:
                    raise
                time.sleep(delay)
                delay = min(delay * 2, 0.08)
    finally:
        try:
            temporary.unlink(missing_ok=True)
        except OSError:
            pass


def atomic_bytes(path: Path, value: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + "." + uuid.uuid4().hex + ".tmp")
    with temporary.open("wb") as stream:
        stream.write(value)
        stream.flush()
        os.fsync(stream.fileno())
    os.replace(temporary, path)


class LivePcmSpool:
    """Publish PCM chunks and enforce reader acknowledgement backpressure."""

    def __init__(self, stream_dir: Path, stream_id: str, conversation_token: str,
                 person_id: str, *, backpressure_timeout: float = 120.0,
                 max_unacked_bytes: int = MAX_UNACKED_BYTES,
                 cancel_path: Optional[Path] = None) -> None:
        if not stream_id or len(stream_id) > 128 or any(not (ch.isalnum() or ch in "._-") for ch in stream_id):
            raise ValueError("streamId is not a safe path component")
        if not conversation_token or not person_id:
            raise ValueError("stream binding fields must be non-empty")
        if max_unacked_bytes < CHUNK_FRAMES * 2:
            raise ValueError("max_unacked_bytes must hold at least one PCM chunk")
        self.stream_dir = stream_dir
        self.stream_id = stream_id
        self.conversation_token = conversation_token
        self.person_id = person_id
        self.chunks_dir = stream_dir / "chunks"
        self.stream_path = stream_dir / "stream.json"
        self.ack_path = stream_dir / "ack.json"
        self.backpressure_timeout = backpressure_timeout
        self.max_unacked_bytes = max_unacked_bytes
        self.cancel_path = cancel_path or (stream_dir / "cancel.json")
        self.next_sequence = 0
        self.total_frames = 0
        self.last_sentence_sequence = -1
        self.consumed_sentence_sequence = -1
        self._last_acknowledged_sequence = -1
        self._remainder = b""
        self._closed = False
        self.stream_dir.mkdir(parents=True, exist_ok=False)
        self.chunks_dir.mkdir()
        self._write_state("open")

    def _write_state(self, status: str, *, error_code: Optional[str] = None,
                     final_wav_file: Optional[str] = None) -> None:
        atomic_json(self.stream_path, {"schemaVersion": 1, "streamId": self.stream_id,
            "conversationToken": self.conversation_token, "personId": self.person_id,
            "encoding": "pcm_s16le", "sampleRate": 24000, "channels": 1,
            "status": status, "nextChunkSequence": self.next_sequence,
            "totalSampleFrames": self.total_frames,
            "consumedSentenceSequence": self.consumed_sentence_sequence,
            "errorCode": error_code,
            "finalWavFile": final_wav_file})

    def _check_cancelled(self) -> None:
        if self.cancel_path.exists():
            try:
                value = json.loads(self.cancel_path.read_text(encoding="utf-8-sig"))
            except (OSError, json.JSONDecodeError):
                raise StreamCancelled("stream cancellation marker is malformed")
            if (not isinstance(value, dict) or value.get("schemaVersion") != 1 or
                    value.get("streamId") != self.stream_id or
                    value.get("conversationToken") != self.conversation_token or
                    value.get("personId") != self.person_id):
                raise StreamCancelled("stream cancellation binding does not match")
            raise StreamCancelled("stream cancelled by native consumer")

    def _acked_sequence(self) -> int:
        if not self.ack_path.exists():
            return -1
        for attempt in range(ACK_READ_RETRY_ATTEMPTS):
            try:
                raw_ack = self.ack_path.read_text(encoding="utf-8-sig")
                break
            except OSError as error:
                if not _is_transient_ack_read_error(error):
                    raise ValueError("stream ack read failed (errno={}, winerror={}): {}".format(
                        error.errno, getattr(error, "winerror", None), error)) from error
                if attempt + 1 >= ACK_READ_RETRY_ATTEMPTS:
                    raise ValueError("stream ack read remained unavailable after {} attempts "
                                     "(errno={}, winerror={}): {}".format(
                                         ACK_READ_RETRY_ATTEMPTS, error.errno,
                                         getattr(error, "winerror", None), error)) from error
                time.sleep(ACK_READ_RETRY_DELAY_SECONDS)
        try:
            value = json.loads(raw_ack)
        except json.JSONDecodeError as error:
            raise ValueError("stream ack JSON is malformed: {}".format(error)) from error
        if (not isinstance(value, dict) or value.get("schemaVersion") != 1 or
                value.get("streamId") != self.stream_id or
                value.get("conversationToken") != self.conversation_token or
                value.get("personId") != self.person_id):
            raise ValueError("stream ack binding does not match")
        sequence = value.get("consumedChunkSequence")
        if isinstance(sequence, bool) or not isinstance(sequence, int) or sequence < -1:
            raise ValueError("consumedChunkSequence must be an integer >= -1")
        if sequence >= self.next_sequence:
            raise ValueError("stream ack is ahead of published chunks")
        if sequence < self._last_acknowledged_sequence:
            raise ValueError("stream ack sequence moved backwards")
        self._last_acknowledged_sequence = sequence
        return sequence

    def _prune_and_wait(self, incoming_bytes: int) -> None:
        deadline = time.monotonic() + self.backpressure_timeout
        while True:
            self._check_cancelled()
            acked = self._acked_sequence()
            outstanding = 0
            for path in self.chunks_dir.glob("*.pcm"):
                try:
                    sequence = int(path.stem)
                except ValueError:
                    continue
                if sequence <= acked:
                    path.unlink(missing_ok=True)
                    path.with_suffix(".json").unlink(missing_ok=True)
                else:
                    outstanding += path.stat().st_size
            if outstanding + incoming_bytes <= self.max_unacked_bytes:
                return
            if time.monotonic() >= deadline:
                raise StreamBackpressureTimeout("Unity did not acknowledge PCM before backpressure timeout")
            time.sleep(0.02)

    def publish_delta(self, pcm: bytes, sentence_sequence: int) -> None:
        if self._closed:
            raise RuntimeError("cannot publish to a terminal stream")
        if not isinstance(pcm, bytes) or not pcm:
            raise ValueError("PCM delta must be non-empty bytes")
        if isinstance(sentence_sequence, bool) or not isinstance(sentence_sequence, int) or sentence_sequence < 0:
            raise ValueError("sentence_sequence must be a non-negative integer")
        combined = self._remainder + pcm
        aligned_size = len(combined) & ~1
        aligned, self._remainder = combined[:aligned_size], combined[aligned_size:]
        max_chunk_bytes = CHUNK_FRAMES * 2
        for offset in range(0, len(aligned), max_chunk_bytes):
            chunk = aligned[offset:offset + max_chunk_bytes]
            if not chunk:
                continue
            self._prune_and_wait(len(chunk))
            sequence = self.next_sequence
            path = self.chunks_dir / ("{:08d}.pcm".format(sequence))
            first_frame = self.total_frames
            atomic_bytes(path, chunk)
            atomic_json(path.with_suffix(".json"), {"schemaVersion": 1,
                "streamId": self.stream_id, "conversationToken": self.conversation_token,
                "personId": self.person_id, "chunkSequence": sequence,
                "sentenceSequence": sentence_sequence, "firstSampleFrame": first_frame,
                "sampleFrames": len(chunk) // 2, "channels": 1, "sampleRate": 24000,
                "encoding": "pcm_s16le", "byteLength": len(chunk),
                "sha256": hashlib.sha256(chunk).hexdigest()})
            self.next_sequence += 1
            self.total_frames += len(chunk) // 2
            self.last_sentence_sequence = sentence_sequence
            self._write_state("open")

    def mark_sentence_consumed(self, sentence_sequence: int) -> None:
        if isinstance(sentence_sequence, bool) or not isinstance(sentence_sequence, int):
            raise ValueError("sentence sequence must be an integer")
        if sentence_sequence < self.consumed_sentence_sequence:
            raise ValueError("consumed sentence sequence cannot move backwards")
        if sentence_sequence > self.last_sentence_sequence:
            raise ValueError("cannot acknowledge a sentence before its PCM was published")
        self.consumed_sentence_sequence = sentence_sequence
        self._write_state("open")

    def finish(self, status: str, *, error_code: Optional[str] = None,
               final_wav_file: Optional[str] = None) -> None:
        if self._closed:
            return
        if status != "cancelled":
            self._check_cancelled()
        if self._remainder and status == "audio_complete":
            raise ValueError("PCM stream ended on a partial 16-bit sample")
        if status not in {"audio_complete", "failed", "cancelled"}:
            raise ValueError("unsupported terminal stream status")
        self._write_state(status, error_code=error_code, final_wav_file=final_wav_file)
        self._closed = True
