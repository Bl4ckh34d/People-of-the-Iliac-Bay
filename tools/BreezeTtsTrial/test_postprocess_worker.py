import tempfile
import unittest
from pathlib import Path
import json
import hashlib
from unittest.mock import patch

try:
    from tools.BreezeTtsTrial.postprocess_worker import (BoundedPostprocessWorker,
        PostprocessJob, PostprocessSettings)
except ModuleNotFoundError:
    from postprocess_worker import (BoundedPostprocessWorker, PostprocessJob, PostprocessSettings)


class PostprocessWorkerTests(unittest.TestCase):
    def test_postprocessing_is_off_by_default_and_requires_explicit_local_pairs(self):
        settings = PostprocessSettings()
        settings.validate()
        self.assertFalse(settings.enabled)
        with self.assertRaises(ValueError) as _raised_20:
            PostprocessSettings(aligner_cli=Path("aligner.exe")).validate()
        self.assertTrue(('configured together' in str(_raised_20.exception)), str(_raised_20.exception))
        with self.assertRaises(FileNotFoundError):
            PostprocessSettings(rhubarb_cli=Path("missing-rhubarb.exe")).validate()

    def test_cancel_after_helper_returns_never_publishes_sidecar(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            cli = root / "aligner.exe"
            model = root / "aligner.gguf"
            audio = root / "stream-1.0000.wav"
            for path in (cli, model, audio):
                path.write_bytes(b"fixture")
            stream_dir = root / "live" / "stream-1"
            output_dir = stream_dir / "sentences"
            output_dir.mkdir(parents=True)
            cancel_path = root / "requests" / "stream-1" / "cancel.json"
            cancel_path.parent.mkdir(parents=True)
            settings = PostprocessSettings(cli, model, "a" * 64)
            worker = BoundedPostprocessWorker(settings)
            job = PostprocessJob("stream-1", "token-1", "person-1", 0, audio,
                "Exact sentence.", cancel_path, stream_dir, output_dir)
            timeline = {"schemaVersion": 1, "status": "ready",
                "timelineKind": "forced-word-alignment", "text": "Exact sentence.",
                "audioSampleRate": 24000, "audioFrames": 42, "words": [],
                "provenance": {"requestId": audio.stem}}

            def completes_after_close(*_args, **_kwargs):
                cancel_path.write_text("{}", encoding="utf-8")
                return timeline

            try:
                target = "tools.BreezeTtsTrial.align_words.run_alignment"
                with patch(target, side_effect=completes_after_close):
                    worker._process(job)
            except ModuleNotFoundError:
                with patch("align_words.run_alignment", side_effect=completes_after_close):
                    worker._process(job)
            self.assertFalse((output_dir / (audio.stem + ".alignment.json")).exists())

    def test_alignment_sidecar_publishes_exact_stream_and_audio_binding(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            cli, model, audio = root / "aligner.exe", root / "aligner.gguf", root / "stream-1.0000.wav"
            for path in (cli, model, audio):
                path.write_bytes(b"fixture")
            stream_dir = root / "live" / "stream-1"
            output_dir = stream_dir / "sentences"
            output_dir.mkdir(parents=True)
            cancel_path = root / "requests" / "stream-1" / "cancel.json"
            settings = PostprocessSettings(cli, model, "a" * 64)
            worker = BoundedPostprocessWorker(settings)
            job = PostprocessJob("stream-1", "token-1", "person-1", 0, audio,
                "Exact sentence.", cancel_path, stream_dir, output_dir)
            timeline = {"schemaVersion": 1, "status": "ready",
                "timelineKind": "forced-word-alignment", "text": "Exact sentence.",
                "audioSampleRate": 24000, "audioFrames": 42, "words": [],
                "provenance": {"requestId": audio.stem}}
            try:
                from tools.BreezeTtsTrial import align_words as _align_words
                patch_target = "tools.BreezeTtsTrial.align_words.run_alignment"
            except ModuleNotFoundError:
                import align_words as _align_words
                patch_target = "align_words.run_alignment"
            with patch(patch_target, return_value=timeline):
                worker._process(job)
            published = json.loads((output_dir / (audio.stem + ".alignment.json")).read_text(encoding="utf-8"))
            self.assertEqual({"schemaVersion": 1, "streamId": "stream-1",
                "conversationToken": "token-1", "personId": "person-1", "scope": "sentence",
                "sentenceSequence": 0,
                "textSha256": "17332f2166aebb783e8e502af8e54150e5e4444b01613e73ab81d63f563be458",
                "audioSha256": hashlib.sha256(b"fixture").hexdigest(),
                "sampleRate": 24000, "sampleFrames": 42},
                published["streamBinding"])

    def test_binding_keeps_exact_conversation_sentence_and_sample_clock(self):
        job = PostprocessJob("stream-1", "token-1", "person-1", 3, Path("a.wav"),
            "Hello.", Path("cancel.json"), Path("stream"), Path("sentences"))
        binding = BoundedPostprocessWorker._binding(job, "b" * 64,
            {"audioSampleRate": 24000, "audioFrames": 3200})
        self.assertEqual({"schemaVersion": 1, "streamId": "stream-1",
            "conversationToken": "token-1", "personId": "person-1", "scope": "sentence",
            "sentenceSequence": 3, "textSha256": "2d8bd7d9bb5f85ba643f0110d50cb506a1fe439e769a22503193ea6046bb87f7",
            "audioSha256": "b" * 64, "sampleRate": 24000, "sampleFrames": 3200}, binding)


if __name__ == "__main__":
    unittest.main()
