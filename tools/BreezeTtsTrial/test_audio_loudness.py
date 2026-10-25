from __future__ import annotations

import array
import math
import tempfile
import unittest
import wave
from pathlib import Path

try:
    import audio_loudness
except ModuleNotFoundError:
    try:
        from tools.BreezeTtsTrial import audio_loudness
    except ImportError:
        audio_loudness = None


def _write_pcm16(path: Path, samples: array.array, rate: int = 24000, channels: int = 1) -> None:
    with wave.open(str(path), "wb") as output:
        output.setnchannels(channels)
        output.setsampwidth(2)
        output.setframerate(rate)
        output.writeframes(samples.tobytes())


def _read_pcm16(path: Path) -> tuple[wave._wave_params, array.array]:
    with wave.open(str(path), "rb") as source:
        params = source.getparams()
        data = source.readframes(params.nframes)
    samples = array.array("h")
    samples.frombytes(data)
    return params, samples


def _sine(frames: int, amplitude: float, rate: int = 24000, channels: int = 1) -> array.array:
    result = array.array("h")
    for frame in range(frames):
        sample = round(math.sin(2 * math.pi * 440 * frame / rate) * amplitude * 32767)
        for _ in range(channels):
            result.append(sample)
    return result


def _dbfs(rms: float) -> float:
    return 20 * math.log10(max(rms, 1e-12))


class AudioLoudnessTests(unittest.TestCase):
    def setUp(self) -> None:
        self.assertIsNotNone(audio_loudness, "audio_loudness module has not been implemented")

    def setUp(self) -> None:
        self.assertIsNotNone(audio_loudness, "audio_loudness module has not been implemented")

    def test_quiet_wave_is_raised_toward_target_without_exceeding_peak_ceiling(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "quiet.wav"
            _write_pcm16(path, _sine(24000, 0.025))

            evidence = audio_loudness.normalize_pcm16_wave(path)

            params, samples = _read_pcm16(path)
            rms = math.sqrt(sum(value * value for value in samples) / len(samples)) / 32768
            peak = max(abs(value) for value in samples) / 32768
            self.assertEqual(params.nframes, 24000)
            self.assertGreater(evidence["appliedGainDb"], 0)
            self.assertGreater(_dbfs(rms), -23.5)
            self.assertLessEqual(peak, 10 ** (-3 / 20) + 1 / 32768)
            self.assertEqual(evidence["output"]["sha256"], audio_loudness.sha256_file(path))

    def test_loud_wave_is_attenuated_to_peak_ceiling_without_clipping(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "loud.wav"
            _write_pcm16(path, _sine(24000, 0.95))

            evidence = audio_loudness.normalize_pcm16_wave(path)

            _, samples = _read_pcm16(path)
            peak = max(abs(value) for value in samples) / 32768
            self.assertLess(evidence["appliedGainDb"], 0)
            self.assertLessEqual(peak, 10 ** (-3 / 20) + 1 / 32768)
            # RMS targeting may land well below the peak ceiling; it is a cap,
            # not a compressor target.
            self.assertGreater(peak, 0.10)

    def test_silence_is_not_boosted_and_wave_clock_is_preserved(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "silence.wav"
            _write_pcm16(path, array.array("h", [0] * (1200 * 2)), rate=16000, channels=2)

            evidence = audio_loudness.normalize_pcm16_wave(path)

            params, samples = _read_pcm16(path)
            self.assertEqual(evidence["status"], "silence")
            self.assertEqual(evidence["appliedGainDb"], 0)
            self.assertEqual((params.nchannels, params.sampwidth, params.framerate, params.nframes),
                (2, 2, 16000, 1200))
            self.assertEqual(set(samples), {0})

    def test_quiet_background_below_gate_does_not_trigger_boost(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "noise.wav"
            _write_pcm16(path, array.array("h", [20] * 24000))

            evidence = audio_loudness.normalize_pcm16_wave(path)

            self.assertEqual(evidence["status"], "silence")
            self.assertEqual(evidence["appliedGainDb"], 0)

    def test_streaming_gain_state_is_independent_of_chunk_boundaries(self) -> None:
        samples = _sine(24000, 0.04)
        raw = samples.tobytes()
        whole = audio_loudness.StreamingPcmNormalizer(sample_rate=24000, channels=1)
        whole_output = whole.process_pcm16(raw)

        chunked = audio_loudness.StreamingPcmNormalizer(sample_rate=24000, channels=1)
        chunked_output = b"".join(chunked.process_pcm16(raw[offset:offset + 1024])
            for offset in range(0, len(raw), 1024))

        self.assertEqual(chunked_output, whole_output)
        decoded = array.array("h")
        decoded.frombytes(whole_output)
        self.assertLessEqual(max(abs(value) for value in decoded) / 32768,
            10 ** (-3 / 20) + 1 / 32768)
        self.assertGreater(whole.applied_gain_db, 0)


if __name__ == "__main__":
    unittest.main()
