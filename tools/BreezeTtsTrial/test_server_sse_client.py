import unittest
import tempfile
import threading
import time
from pathlib import Path
from unittest.mock import patch
import wave

try:
    from tools.BreezeTtsTrial.server_sse_client import build_request, collect_audio_events, synthesize
    import tools.BreezeTtsTrial.server_sse_client as server_module
except ModuleNotFoundError:
    from server_sse_client import build_request, collect_audio_events, synthesize
    import server_sse_client as server_module


class ServerSseClientTests(unittest.TestCase):
    def test_cancel_closes_stalled_response_within_poll_bound(self):
        class StalledResponse:
            status = 200

            def __init__(self):
                self.closed = threading.Event()

            def readline(self, _limit=None):
                self.closed.wait(5)
                return b""

            def close(self):
                self.closed.set()

        with tempfile.TemporaryDirectory() as temporary:
            output = Path(temporary) / "cancelled.wav"
            cancel = threading.Event()
            response = StalledResponse()
            timer = threading.Timer(0.1, cancel.set)
            timer.start()
            started = time.monotonic()
            try:
                with patch.object(server_module.urllib.request, "urlopen", return_value=response):
                    with self.assertRaises(server_module.SynthesisCancelled):
                        synthesize("http://127.0.0.1:8094", "breeze", "Hello.", "Stable voice.",
                                   output_wav=str(output), cancel_event=cancel)
            finally:
                timer.cancel()
            self.assertLess(time.monotonic() - started, 1.0)
            self.assertTrue(response.closed.is_set())
            self.assertFalse(output.exists())

    def test_collects_pcm_events_across_arbitrary_transport_chunks(self):
        chunks = [
            b'data: {"type":"speech.audio.del',
            b'ta","audio":"AQID"}\r\n\r\ndata: {"type":"speech.audio.done","timing":{"ttft_ms":42}}\n\n',
            b'data: [DONE]\n\n',
        ]
        events = list(collect_audio_events(chunks))

        self.assertEqual(events[0][0], "speech.audio.delta")
        self.assertEqual(events[0][1], b"\x01\x02\x03")
        self.assertEqual(events[1][0], "speech.audio.done")
        self.assertEqual(events[2][0], "[DONE]")

    def test_rejects_stream_without_done_sentinel(self):
        chunks = [b'data: {"type":"speech.audio.delta","audio":"AQID"}\n\n']

        with self.assertRaises(ValueError) as _raised_66:
            list(collect_audio_events(chunks))
        self.assertTrue(('without [DONE]' in str(_raised_66.exception)), str(_raised_66.exception))

    def test_request_contains_npc_instruction_reference_seed_and_guidance(self):
        payload = build_request("breeze", "Good evening.", "Low, warm female voice.",
                                r"C:\\voices\\npc.wav", "A reference transcript.",
                                seed=71001)

        self.assertEqual(payload["response_format"], "pcm")
        self.assertEqual(payload["stream_format"], "sse")
        self.assertEqual(payload["options"]["instruct"], "Low, warm female voice.")
        self.assertEqual(payload["voice_ref"], {"type": "path", "path": r"C:\voices\npc.wav"})
        self.assertEqual(payload["reference_text"], "A reference transcript.")
        self.assertEqual(payload["options"]["seed"], 71001)
        self.assertEqual(payload["options"]["guidance_scale"], 4.0)

    def test_reference_free_voice_design_omits_reference_fields(self):
        payload = build_request("breeze", "Good evening.", "Adult woman, clear mid-register.")

        self.assertNotIn("voice_ref", payload)
        self.assertNotIn("reference_text", payload)
        self.assertEqual(payload["options"]["guidance_scale"], 4.0)
        self.assertNotIn("seed", payload["options"])

    def test_reference_pair_must_be_complete_and_guidance_valid(self):
        with self.assertRaises(ValueError) as _raised_91:
            build_request("breeze", "Hi.", "Voice.", r"C:\\voice.wav", None)
        self.assertTrue(('both supplied or both omitted' in str(_raised_91.exception)), str(_raised_91.exception))
        with self.assertRaises(ValueError) as _raised_93:
            build_request("breeze", "Hi.", "Voice.", guidance_scale=-0.1)
        self.assertTrue(('guidance_scale' in str(_raised_93.exception)), str(_raised_93.exception))
        self.assertEqual(build_request("breeze", "Hi.", "Voice.", guidance_scale=0)
                         ["options"]["guidance_scale"], 0.0)

    def test_synthesize_forwards_pcm_deltas_before_writing_complete_wav(self):
        class FakeResponse:
            status = 200

            def __init__(self):
                self.done_seen = False
                self.lines = iter([
                    b'data: {"type":"speech.audio.delta","audio":"AQ=="}\n\n',
                    b'data: {"type":"speech.audio.delta","audio":"Ag=="}\n\n',
                    b'data: {"type":"speech.audio.done","timing":{}}\n\n',
                    b'data: [DONE]\n\n',
                ])

            def __enter__(self):
                return self

            def __exit__(self, *_args):
                return False

            def readline(self):
                line = next(self.lines, b"")
                if b"speech.audio.done" in line:
                    self.done_seen = True
                return line

        received = []
        with tempfile.TemporaryDirectory() as temporary:
            output = Path(temporary) / "out.wav"
            fake_response = FakeResponse()
            with patch.object(server_module.urllib.request, "urlopen", return_value=fake_response):
                synthesize("http://127.0.0.1:8094", "breeze", "Hello.", "Stable voice.",
                           output_wav=str(output),
                           on_pcm_delta=lambda delta: received.append((delta, fake_response.done_seen)))
            self.assertEqual([(b"\x01", False), (b"\x02", False)], received)
            self.assertTrue(output.is_file())
            with wave.open(str(output), "rb") as reader:
                self.assertEqual(b"\x01\x02", reader.readframes(1))


if __name__ == "__main__":
    unittest.main()
