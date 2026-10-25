"""Small, deterministic PCM16 loudness controls for the Breeze trial.

This module has no audio/model dependencies. Final WAV normalization is a single
whole-file gain pass; live PCM uses a stateful envelope so chunk boundaries do
not reset gain. Both preserve the sample clock and cap the output peak.
"""

from __future__ import annotations

import array
import hashlib
import json
import math
import os
import tempfile
import wave
from pathlib import Path
from typing import Any


_DEFAULT_TARGET_DBFS = -20.0
_DEFAULT_PEAK_DBFS = -3.0
_DEFAULT_MAX_BOOST_DB = 12.0
_DEFAULT_GATE_DBFS = -50.0


def sha256_file(path: str | os.PathLike[str]) -> str:
    digest = hashlib.sha256()
    with open(path, "rb") as source:
        for block in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def _dbfs(value: float) -> float | None:
    if value <= 0.0:
        return None
    return 20.0 * math.log10(value)


def _pcm16_bytes(samples: array.array) -> bytes:
    if samples.itemsize != 2:
        raise ValueError("this normalizer requires 16-bit samples")
    encoded = array.array("h", samples)
    if os.sys.byteorder != "little":
        encoded.byteswap()
    return encoded.tobytes()


def _decode_pcm16(data: bytes) -> array.array:
    if len(data) % 2:
        raise ValueError("PCM16 byte count must be even")
    samples = array.array("h")
    samples.frombytes(data)
    if os.sys.byteorder != "little":
        samples.byteswap()
    return samples


def _measure(samples: array.array, channels: int, gate_dbfs: float, sample_rate: int) -> dict[str, Any]:
    if channels <= 0 or len(samples) % channels:
        raise ValueError("PCM sample count must contain complete frames")
    frames = len(samples) // channels
    peak = max((abs(value) for value in samples), default=0) / 32768.0
    gate = 10.0 ** (gate_dbfs / 20.0)
    active_sum = 0.0
    active_count = 0
    # Gate in 10 ms windows. A low background floor is not treated as speech.
    window_frames = max(1, sample_rate // 100)
    for start in range(0, frames, window_frames):
        end = min(frames, start + window_frames)
        total = 0.0
        count = (end - start) * channels
        for value in samples[start * channels:end * channels]:
            normalized = value / 32768.0
            total += normalized * normalized
        window_rms = math.sqrt(total / max(1, count))
        if window_rms >= gate:
            active_sum += total
            active_count += count
    active_rms = math.sqrt(active_sum / active_count) if active_count else 0.0
    return {
        "peak": peak,
        "peakDbfs": _dbfs(peak),
        "activeRms": active_rms,
        "activeRmsDbfs": _dbfs(active_rms),
        "activeFrames": active_count // channels,
    }


def normalize_pcm16_wave(
    path: str | os.PathLike[str],
    *,
    target_active_rms_dbfs: float = _DEFAULT_TARGET_DBFS,
    peak_ceiling_dbfs: float = _DEFAULT_PEAK_DBFS,
    max_boost_db: float = _DEFAULT_MAX_BOOST_DB,
    gate_dbfs: float = _DEFAULT_GATE_DBFS,
) -> dict[str, Any]:
    """Normalize a PCM16 WAV atomically in place and return hash-bound evidence.

    The gain is constant over the clip, so duration, sample rate, channels, and
    frame count stay identical. Non-PCM16 WAV input is rejected without change.
    """
    source_path = Path(path)
    if not source_path.is_file():
        raise FileNotFoundError(source_path)
    if max_boost_db < 0 or max_boost_db > 24:
        raise ValueError("max_boost_db must be between 0 and 24 dB")
    if peak_ceiling_dbfs > 0 or target_active_rms_dbfs > 0 or gate_dbfs > 0:
        raise ValueError("dBFS target, gate, and ceiling must not exceed 0 dBFS")

    input_hash = sha256_file(source_path)
    with wave.open(str(source_path), "rb") as source:
        params = source.getparams()
        if params.sampwidth != 2 or params.comptype != "NONE":
            raise ValueError("only uncompressed PCM16 WAV files are supported")
        raw = source.readframes(params.nframes)
    samples = _decode_pcm16(raw)
    before = _measure(samples, params.nchannels, gate_dbfs, params.framerate)
    if before["activeRms"] <= 0.0 or before["peak"] <= 0.0:
        gain = 1.0
        status = "silence"
    else:
        wanted = 10.0 ** (target_active_rms_dbfs / 20.0) / before["activeRms"]
        wanted = min(wanted, 10.0 ** (max_boost_db / 20.0))
        ceiling = 10.0 ** (peak_ceiling_dbfs / 20.0)
        gain = min(wanted, ceiling / before["peak"])
        status = "normalized"

    output_samples = array.array("h")
    for sample in samples:
        output_samples.append(max(-32768, min(32767, round(sample * gain))))
    output_bytes = _pcm16_bytes(output_samples)

    # Atomic replacement prevents readers/sidecar workers from seeing a partial WAV.
    fd, temp_name = tempfile.mkstemp(prefix=f".{source_path.stem}.", suffix=".wav.tmp", dir=source_path.parent)
    os.close(fd)
    try:
        with wave.open(temp_name, "wb") as output:
            output.setparams(params)
            output.writeframes(output_bytes)
        os.replace(temp_name, source_path)
    finally:
        if os.path.exists(temp_name):
            os.unlink(temp_name)

    after = _measure(output_samples, params.nchannels, gate_dbfs, params.framerate)
    return {
        "schemaVersion": 1,
        "normalizer": "pcm16-active-rms-v1",
        "status": status,
        "targetActiveRmsDbfs": target_active_rms_dbfs,
        "peakCeilingDbfs": peak_ceiling_dbfs,
        "maxBoostDb": max_boost_db,
        "gateDbfs": gate_dbfs,
        "appliedGainDb": 20.0 * math.log10(gain),
        "input": {
            "sha256": input_hash,
            "sampleRate": params.framerate,
            "channels": params.nchannels,
            "sampleWidthBytes": params.sampwidth,
            "frames": params.nframes,
            "peakDbfs": before["peakDbfs"],
            "activeRmsDbfs": before["activeRmsDbfs"],
            "activeFrames": before["activeFrames"],
        },
        "output": {
            "sha256": sha256_file(source_path),
            "sampleRate": params.framerate,
            "channels": params.nchannels,
            "sampleWidthBytes": params.sampwidth,
            "frames": params.nframes,
            "peakDbfs": after["peakDbfs"],
            "activeRmsDbfs": after["activeRmsDbfs"],
            "activeFrames": after["activeFrames"],
        },
    }


class StreamingPcmNormalizer:
    """Stateful causal gain smoother for consecutive little-endian PCM16 chunks.

    Retain one instance per audio stream. This applies a 120 ms RMS envelope,
    100 ms gain attack, and 500 ms gain release, with a hard sample peak cap.
    It adds no buffering or latency and never resets on an SSE chunk boundary.
    """

    def __init__(
        self,
        *,
        sample_rate: int,
        channels: int,
        target_active_rms_dbfs: float = _DEFAULT_TARGET_DBFS,
        peak_ceiling_dbfs: float = _DEFAULT_PEAK_DBFS,
        max_boost_db: float = _DEFAULT_MAX_BOOST_DB,
        gate_dbfs: float = _DEFAULT_GATE_DBFS,
        envelope_seconds: float = 0.12,
        attack_seconds: float = 0.10,
        release_seconds: float = 0.50,
    ) -> None:
        if sample_rate <= 0 or channels <= 0:
            raise ValueError("sample_rate and channels must be positive")
        if not (0 <= max_boost_db <= 24):
            raise ValueError("max_boost_db must be between 0 and 24 dB")
        self.sample_rate = sample_rate
        self.channels = channels
        self.target_rms = 10.0 ** (target_active_rms_dbfs / 20.0)
        self.peak_ceiling = 10.0 ** (peak_ceiling_dbfs / 20.0)
        self.max_gain = 10.0 ** (max_boost_db / 20.0)
        self.gate = 10.0 ** (gate_dbfs / 20.0)
        self.envelope_alpha = math.exp(-1.0 / max(1.0, sample_rate * envelope_seconds))
        self.attack_alpha = math.exp(-1.0 / max(1.0, sample_rate * attack_seconds))
        self.release_alpha = math.exp(-1.0 / max(1.0, sample_rate * release_seconds))
        self._energy = 0.0
        self._gain = 1.0
        self.applied_gain_db = 0.0

    def process_pcm16(self, data: bytes) -> bytes:
        samples = _decode_pcm16(data)
        if len(samples) % self.channels:
            raise ValueError("PCM chunk must contain complete interleaved frames")
        result = array.array("h")
        for start in range(0, len(samples), self.channels):
            frame = samples[start:start + self.channels]
            energy = sum((value / 32768.0) ** 2 for value in frame) / self.channels
            self._energy = self.envelope_alpha * self._energy + (1.0 - self.envelope_alpha) * energy
            level = math.sqrt(self._energy)
            if level >= self.gate:
                desired = min(self.max_gain, self.target_rms / max(level, 1e-12))
            else:
                desired = min(self._gain, 1.0)
            alpha = self.attack_alpha if desired > self._gain else self.release_alpha
            self._gain = alpha * self._gain + (1.0 - alpha) * desired
            peak = max((abs(value) / 32768.0 for value in frame), default=0.0)
            frame_gain = min(self._gain, self.peak_ceiling / peak) if peak > 0 else self._gain
            self.applied_gain_db = 20.0 * math.log10(max(self._gain, 1e-12))
            for value in frame:
                result.append(max(-32768, min(32767, round(value * frame_gain))))
        return _pcm16_bytes(result)


def write_normalization_evidence(path: str | os.PathLike[str], evidence: dict[str, Any]) -> None:
    """Write optional evidence JSON atomically; audio caller controls publication order."""
    target = Path(path)
    target.parent.mkdir(parents=True, exist_ok=True)
    fd, temp_name = tempfile.mkstemp(prefix=f".{target.name}.", suffix=".tmp", dir=target.parent)
    try:
        with os.fdopen(fd, "w", encoding="utf-8", newline="\n") as stream:
            json.dump(evidence, stream, indent=2, sort_keys=True)
            stream.write("\n")
        os.replace(temp_name, target)
    finally:
        if os.path.exists(temp_name):
            os.unlink(temp_name)
