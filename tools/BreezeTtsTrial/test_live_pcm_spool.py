import json
import errno
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

try:
    import tools.BreezeTtsTrial.live_pcm_spool as live_pcm_spool
    from tools.BreezeTtsTrial.live_pcm_spool import (LivePcmSpool, read_sentence, read_stream_begin,
        validate_pending_sentence_queue)
except ModuleNotFoundError:
    import live_pcm_spool
    from live_pcm_spool import (LivePcmSpool, read_sentence, read_stream_begin,
        validate_pending_sentence_queue)


class LivePcmSpoolTests(unittest.TestCase):
    def test_stream_state_replace_retries_windows_reader_sharing_denial(self):
        class SharingDenied(PermissionError):
            def __init__(self):
                super().__init__(5, "access denied")
                self.winerror = 5

        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "stream.json"
            original_replace = live_pcm_spool.os.replace
            attempts = []

            def replace(source, destination):
                attempts.append((source, destination))
                if len(attempts) < 3:
                    raise SharingDenied()
                return original_replace(source, destination)

            with patch.object(live_pcm_spool.os, "replace", side_effect=replace), \
                    patch.object(live_pcm_spool.time, "sleep") as sleep:
                live_pcm_spool.atomic_json(path, {"status": "open"})

            self.assertEqual(3, len(attempts))
            self.assertEqual(2, sleep.call_count)
            self.assertEqual({"status": "open"}, json.loads(path.read_text(encoding="utf-8")))
            self.assertEqual([], list(Path(temporary).glob("*.tmp")))

    def test_stream_state_replace_bounds_sharing_denial_retries_and_cleans_temp(self):
        class SharingDenied(PermissionError):
            def __init__(self):
                super().__init__(5, "access denied")
                self.winerror = 5

        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "stream.json"
            attempts = []

            def replace(_source, _destination):
                attempts.append(1)
                raise SharingDenied()

            with patch.object(live_pcm_spool.os, "replace", side_effect=replace), \
                    patch.object(live_pcm_spool.time, "sleep") as sleep:
                with self.assertRaises(PermissionError):
                    live_pcm_spool.atomic_json(path, {"status": "open"})

            self.assertGreater(len(attempts), 1)
            self.assertEqual(len(attempts) - 1, sleep.call_count)
            self.assertFalse(path.exists())
            self.assertEqual([], list(Path(temporary).glob("*.tmp")))

    def test_stream_begin_and_sentences_require_exact_bindings_and_contiguous_sequence(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary) / "stream-001"
            directory.mkdir()
            begin_path = directory / "begin.json"
            begin_path.write_text(json.dumps({"schemaVersion": 1, "mode": "streaming-live-pcm-v1",
                "streamId": "stream-001", "conversationToken": "conversation-1",
                "personId": "person-1", "displayName": "Alding", "voiceGender": "female",
                "deliveryContext": None, "voiceIdentity": {"schemaVersion": 1,
                    "sex": {"value": "female", "known": True, "source": "native.static-npc"},
                    "race": {"value": "Argonian", "known": True, "source": "native.static-npc"},
                    "age": {"value": "unknown", "confidence": "unknown", "source": "native.no-stable-key"},
                    "origin": {"value": "unknown", "known": False, "source": "native.not-provided"}},
                "sampleRate": 24000, "channels": 1}), encoding="utf-8")
            begin = read_stream_begin(begin_path, "stream-001")
            sentence_path = directory / "0000.json"
            sentence_path.write_text(json.dumps({"schemaVersion": 1, "streamId": "stream-001",
                "conversationToken": "conversation-1", "personId": "person-1", "sequence": 0,
                "exactText": "The road is quiet."}), encoding="utf-8")
            self.assertEqual("The road is quiet.", read_sentence(sentence_path, begin, 0))
            with self.assertRaises(ValueError) as _raised_89:
                read_sentence(sentence_path, begin, 1)
            self.assertTrue(('contiguous' in str(_raised_89.exception)), str(_raised_89.exception))

    def test_pending_sentence_queue_enforces_eight_and_480_character_bounds(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary) / "stream-001"
            directory.mkdir()
            begin_path = directory / "begin.json"
            begin_path.write_text(json.dumps({"schemaVersion": 1, "mode": "streaming-live-pcm-v1",
                "streamId": "stream-001", "conversationToken": "conversation-1",
                "personId": "person-1", "displayName": "Alding", "voiceGender": "female",
                "deliveryContext": None, "sampleRate": 24000, "channels": 1}), encoding="utf-8")
            begin = read_stream_begin(begin_path, "stream-001")
            for sequence in range(8):
                row = {"schemaVersion": 1, "streamId": "stream-001",
                    "conversationToken": "conversation-1", "personId": "person-1",
                    "sequence": sequence, "exactText": "x" * 60}
                (directory / ("{:04d}.json".format(sequence))).write_text(json.dumps(row), encoding="utf-8")
            self.assertEqual(8, validate_pending_sentence_queue(directory, begin, 0))
            ninth = {"schemaVersion": 1, "streamId": "stream-001",
                "conversationToken": "conversation-1", "personId": "person-1",
                "sequence": 8, "exactText": "x"}
            ninth_path = directory / "0008.json"
            ninth_path.write_text(json.dumps(ninth), encoding="utf-8")
            with self.assertRaises(ValueError) as _raised_113:
                validate_pending_sentence_queue(directory, begin, 0)
            self.assertTrue(('eight segments' in str(_raised_113.exception)), str(_raised_113.exception))
            ninth_path.unlink()
            row = json.loads((directory / "0007.json").read_text())
            row["exactText"] += "x"
            (directory / "0007.json").write_text(json.dumps(row), encoding="utf-8")
            with self.assertRaises(ValueError) as _raised_119:
                validate_pending_sentence_queue(directory, begin, 0)
            self.assertTrue(('480 aggregate' in str(_raised_119.exception)), str(_raised_119.exception))

    def test_publishes_sample_aligned_chunks_with_contiguous_frame_offsets(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary) / "stream-001"
            spool = LivePcmSpool(directory, "stream-001", "conversation-1", "person-1")
            spool.publish_delta(b"\x01\x02\x03", sentence_sequence=0)
            spool.publish_delta(b"\x04", sentence_sequence=0)
            spool.publish_delta(bytes(9600), sentence_sequence=1)

            first = directory / "chunks" / "00000000.pcm"
            second = directory / "chunks" / "00000001.pcm"
            third = directory / "chunks" / "00000002.pcm"
            fourth = directory / "chunks" / "00000003.pcm"
            self.assertEqual(b"\x01\x02", first.read_bytes())
            self.assertEqual(b"\x03\x04", second.read_bytes())
            self.assertEqual(4800, third.stat().st_size)
            self.assertEqual(4800, fourth.stat().st_size)
            first_meta = json.loads(first.with_suffix(".json").read_text(encoding="utf-8"))
            third_meta = json.loads(third.with_suffix(".json").read_text(encoding="utf-8"))
            self.assertEqual((0, 1), (first_meta["firstSampleFrame"], first_meta["sampleFrames"]))
            self.assertEqual((2, 2400), (third_meta["firstSampleFrame"], third_meta["sampleFrames"]))
            self.assertEqual(4802, json.loads((directory / "stream.json").read_text())["totalSampleFrames"])

    def test_acknowledgement_must_match_stream_and_cannot_ack_future_chunk(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary) / "stream-001"
            spool = LivePcmSpool(directory, "stream-001", "conversation-1", "person-1")
            spool.publish_delta(bytes(2), sentence_sequence=0)
            ack = {"schemaVersion": 1, "streamId": "other", "conversationToken": "conversation-1",
                   "personId": "person-1", "consumedChunkSequence": 0}
            (directory / "ack.json").write_text(json.dumps(ack), encoding="utf-8")
            with self.assertRaises(ValueError) as _raised_152:
                spool._acked_sequence()
            self.assertTrue(('binding' in str(_raised_152.exception)), str(_raised_152.exception))
            ack["streamId"] = "stream-001"
            ack["consumedChunkSequence"] = 1
            (directory / "ack.json").write_text(json.dumps(ack), encoding="utf-8")
            with self.assertRaises(ValueError) as _raised_157:
                spool._acked_sequence()
            self.assertTrue(('ahead' in str(_raised_157.exception)), str(_raised_157.exception))

    def test_ack_read_retries_transient_windows_replace_race(self):
        class SharingDenied(PermissionError):
            def __init__(self):
                super().__init__(5, "access denied")
                self.winerror = 32

        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary) / "stream-001"
            spool = LivePcmSpool(directory, "stream-001", "conversation-1", "person-1")
            spool.publish_delta(bytes(2), sentence_sequence=0)
            ack = {"schemaVersion": 1, "streamId": "stream-001",
                   "conversationToken": "conversation-1", "personId": "person-1",
                   "consumedChunkSequence": 0}
            spool.ack_path.write_text(json.dumps(ack), encoding="utf-8")
            original_read_text = Path.read_text
            attempts = []

            def read_text(path, *args, **kwargs):
                if path == spool.ack_path:
                    attempts.append(1)
                    if len(attempts) < 3:
                        raise SharingDenied()
                return original_read_text(path, *args, **kwargs)

            with patch.object(Path, "read_text", read_text), \
                    patch.object(live_pcm_spool.time, "sleep") as sleep:
                self.assertEqual(0, spool._acked_sequence())

            self.assertEqual(3, len(attempts))
            self.assertEqual(2, sleep.call_count)

    def test_ack_read_retries_windows_eacces_without_winerror(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary) / "stream-001"
            spool = LivePcmSpool(directory, "stream-001", "conversation-1", "person-1")
            spool.publish_delta(bytes(2), sentence_sequence=0)
            ack = {"schemaVersion": 1, "streamId": "stream-001",
                   "conversationToken": "conversation-1", "personId": "person-1",
                   "consumedChunkSequence": 0}
            spool.ack_path.write_text(json.dumps(ack), encoding="utf-8")
            original_read_text = Path.read_text
            attempts = []

            def read_text(path, *args, **kwargs):
                if path == spool.ack_path:
                    attempts.append(1)
                    if len(attempts) == 1:
                        raise PermissionError(errno.EACCES, "access denied")
                return original_read_text(path, *args, **kwargs)

            with patch.object(live_pcm_spool.os, "name", "nt"), \
                    patch.object(Path, "read_text", read_text), \
                    patch.object(live_pcm_spool.time, "sleep") as sleep:
                self.assertEqual(0, spool._acked_sequence())

            self.assertEqual(2, len(attempts))
            sleep.assert_called_once()

    def test_ack_read_does_not_retry_posix_permission_error_without_winerror(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary) / "stream-001"
            spool = LivePcmSpool(directory, "stream-001", "conversation-1", "person-1")
            spool.publish_delta(bytes(2), sentence_sequence=0)
            spool.ack_path.write_text(json.dumps({"schemaVersion": 1,
                "streamId": "stream-001", "conversationToken": "conversation-1",
                "personId": "person-1", "consumedChunkSequence": 0}), encoding="utf-8")
            error = PermissionError(errno.EACCES, "permission denied")
            original_read_text = Path.read_text
            attempts = []

            def read_text(path, *args, **kwargs):
                if path == spool.ack_path:
                    attempts.append(1)
                    raise error
                return original_read_text(path, *args, **kwargs)

            with patch.object(live_pcm_spool.os, "name", "posix"), \
                    patch.object(Path, "read_text", read_text), \
                    patch.object(live_pcm_spool.time, "sleep") as sleep:
                with self.assertRaises(ValueError) as raised:
                    spool._acked_sequence()
                self.assertTrue(('errno=13' in str(raised.exception)), str(raised.exception))

            self.assertEqual(1, len(attempts))
            self.assertIs(error, raised.exception.__cause__)
            sleep.assert_not_called()

    def test_ack_read_does_not_retry_or_hide_invalid_json(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary) / "stream-001"
            spool = LivePcmSpool(directory, "stream-001", "conversation-1", "person-1")
            spool.ack_path.write_text("{", encoding="utf-8")
            with patch.object(live_pcm_spool.time, "sleep") as sleep:
                with self.assertRaises(ValueError) as raised:
                    spool._acked_sequence()
                self.assertTrue(('JSON' in str(raised.exception)), str(raised.exception))
            self.assertIsInstance(raised.exception.__cause__, json.JSONDecodeError)
            sleep.assert_not_called()

    def test_terminal_state_is_written_and_partial_pcm_sample_is_rejected(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary) / "stream-001"
            spool = LivePcmSpool(directory, "stream-001", "conversation-1", "person-1")
            spool.publish_delta(b"\x01", sentence_sequence=0)
            with self.assertRaises(ValueError) as _raised_262:
                spool.finish("audio_complete", final_wav_file="stream-001.wav")
            self.assertTrue(('partial' in str(_raised_262.exception)), str(_raised_262.exception))

    def test_sentence_completion_is_published_for_input_queue_backpressure(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary) / "stream-001"
            spool = LivePcmSpool(directory, "stream-001", "conversation-1", "person-1")
            spool.publish_delta(b"\x00\x00", sentence_sequence=0)
            spool.mark_sentence_consumed(0)
            state = json.loads((directory / "stream.json").read_text(encoding="utf-8"))
            self.assertEqual(0, state["consumedSentenceSequence"])


if __name__ == "__main__":
    unittest.main()
