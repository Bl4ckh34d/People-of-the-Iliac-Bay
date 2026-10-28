"""Pure protocol tests; no CLI, model load, or synthesis."""

import json
import subprocess
import tempfile
import unittest
import wave
from pathlib import Path
from unittest.mock import patch

import coordinator
import import_corpus_profiles


class CoordinatorProtocolTests(unittest.TestCase):
    def _run_prepared_request(self, root, request_id, *, fail_phase=None, cancel_after_visemes=False,
                              per_npc_voices=False, fast_utterance=False, require_overlap=False):
        import hashlib
        import inspect
        import sys
        from types import SimpleNamespace

        self.assertIn("prepare_presentation_before_playback",
            inspect.signature(coordinator.process_claimed).parameters,
            "process_claimed does not expose prepared-presentation mode")

        requests = root / "requests"
        working = root / "working"
        responses = root / "responses"
        requests.mkdir()
        working.mkdir()
        responses.mkdir()
        claimed = working / (request_id + ".claimed.request.json")
        exact_text = "Please keep your voice down."
        claimed.write_text(json.dumps(self.request_json(requestId=request_id, text=exact_text)), encoding="utf-8")
        profiles_path = root / "profiles.json"
        if not per_npc_voices:
            profiles_path.write_text(json.dumps({"schemaVersion": 1, "profiles": self.profiles()}), encoding="utf-8")
        model_hash = "b" * 64
        manifest_path = responses / (request_id + ".manifest.json")
        alignment_path = responses / (request_id + ".alignment.json")
        viseme_path = responses / (request_id + ".visemes.json")
        cancel_path = requests / (request_id + ".cancel.json")
        calls = []
        import threading
        preparation_barrier = threading.Barrier(2) if require_overlap else None

        def synthesize(command, **kwargs):
            self.assertGreaterEqual(kwargs["timeout"], 1)
            self.assertLessEqual(kwargs["timeout"], coordinator.PREPARED_PRESENTATION_REQUEST_TIMEOUT_SECONDS)
            wav_path = Path(command[command.index("--out") + 1])
            wav_path.parent.mkdir(parents=True, exist_ok=True)
            with wave.open(str(wav_path), "wb") as output:
                output.setnchannels(1)
                output.setsampwidth(2)
                output.setframerate(24000)
                from npc_voice_profiles import ANCHOR_TEXT
                is_anchor = command[command.index("--text") + 1] == ANCHOR_TEXT
                frames = (b"\xe8\x03\x18\xfc" * 48000) if is_anchor else (
                    b"\x00\x00" * 2400 if fast_utterance else b"\xe8\x03\x18\xfc" * 48000)
                output.writeframes(frames)
            return subprocess.CompletedProcess(command, 0, "", "")

        def fake_rhubarb(cli, audio, text, output_path, **kwargs):
            calls.append("visemes")
            if preparation_barrier is not None:
                preparation_barrier.wait(timeout=1)
            self.assertFalse(manifest_path.exists(), "WAV manifest appeared before viseme cues were ready")
            self.assertEqual(exact_text, text)
            self.assertEqual(2, kwargs["threads"])
            self.assertGreaterEqual(kwargs["timeout_seconds"], 1)
            self.assertLessEqual(kwargs["timeout_seconds"], coordinator.PREPARED_PRESENTATION_VISEME_TIMEOUT_SECONDS)
            with wave.open(str(audio), "rb") as audio_file:
                frame_count = audio_file.getnframes()
            if fail_phase == "visemes":
                raise RuntimeError("test viseme failure")
            audio_hash = coordinator.sha256_file(Path(audio))
            timeline = {"schemaVersion": 1, "status": "ready",
                "timelineKind": "recognizer-viseme-estimate", "requestId": request_id,
                "text": exact_text, "audioSampleRate": 24000, "audioFrames": frame_count,
                "cues": [{"startSample": 0, "endSample": frame_count, "shape": "X"}],
                "provenance": {"audioSha256": audio_hash,
                    "dialogHintSha256": hashlib.sha256(exact_text.encode("utf-8")).hexdigest()}}
            Path(output_path).write_text(json.dumps(timeline), encoding="utf-8")
            if cancel_after_visemes:
                cancel_path.write_text(json.dumps({"schemaVersion": 1, "requestId": request_id,
                    "conversationToken": "conversation-abc", "reason": "conversation-closed"}), encoding="utf-8")
            return timeline

        def fake_alignment(cli, model, audio, text, output_path, **kwargs):
            calls.append("alignment")
            if preparation_barrier is not None:
                preparation_barrier.wait(timeout=1)
            self.assertFalse(manifest_path.exists(), "WAV manifest appeared before word alignment was ready")
            self.assertEqual(exact_text, text)
            if fail_phase == "alignment":
                raise RuntimeError("test alignment failure")
            with wave.open(str(audio), "rb") as audio_file:
                frame_count = audio_file.getnframes()
            timeline = {"schemaVersion": 1, "status": "ready", "timelineKind": "forced-word-alignment",
                "requestId": request_id, "text": text, "audioSampleRate": 24000,
                "audioFrames": frame_count, "words": [{"text": text, "startSample": 0,
                    "endSample": frame_count}],
                "coverage": {"matchedTokens": 1, "transcriptTokens": 1},
                "provenance": {"requestId": request_id,
                    "audioSha256": coordinator.sha256_file(Path(audio)),
                    "transcriptSha256": hashlib.sha256(text.encode("utf-8")).hexdigest(),
                    "modelSha256": model_hash, "backend": "cpu", "threads": kwargs["threads"],
                    "phonemeAlignment": False}}
            Path(output_path).write_text(json.dumps(timeline), encoding="utf-8")
            return timeline

        class RecordingPostprocessor:
            def __init__(self):
                self.jobs = []
            def submit(self, job):
                self.jobs.append(job)
                return "queued"

        postprocessor = RecordingPostprocessor()
        viseme_module = SimpleNamespace(run_rhubarb=fake_rhubarb)
        fake_modules = {
            "tools.BreezeTtsTrial.rhubarb_visemes": viseme_module,
            "rhubarb_visemes": viseme_module,
            "tools.BreezeTtsTrial.align_words": SimpleNamespace(run_alignment=fake_alignment),
            "align_words": SimpleNamespace(run_alignment=fake_alignment),
        }
        with patch.dict(sys.modules, fake_modules), patch.object(coordinator.subprocess, "run", side_effect=synthesize):
            result = coordinator.process_claimed(claimed, request_id, root,
                root / "audiocpp_cli.exe", root / "model.gguf", "0" * 64, root / "breeze.json",
                profiles_path, root / "voice-bindings.json", root / "voices", None, "cuda", 300, "test-build",
                postprocessor=postprocessor, prepare_presentation_before_playback=True,
                rhubarb_cli=root / "rhubarb.exe", aligner_cli=root / "audiocpp_cli.exe",
                aligner_model=root / "aligner.gguf", aligner_sha256=model_hash,
                per_npc_voices=per_npc_voices)
        return result, calls, postprocessor, manifest_path, alignment_path, viseme_path, cancel_path

    def test_personal_voice_and_prepared_presentation_compose_without_legacy_registry(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            result, calls, worker, manifest, alignment, visemes, _ = self._run_prepared_request(
                root, "personal-prepared", per_npc_voices=True)
            self.assertEqual("ready", result["status"], result)
            self.assertCountEqual(["visemes", "alignment"], calls)
            self.assertTrue(json.loads(manifest.read_text())["presentationReady"])
            self.assertTrue(alignment.is_file())
            self.assertTrue(visemes.is_file())
            self.assertEqual([], worker.jobs)
            self.assertFalse((root / "profiles.json").exists())
            self.assertFalse((root / "voice-bindings.json").exists())
            evidence = json.loads((root / "measurements" / "personal-prepared.evidence.json").read_text())
            self.assertTrue(evidence["personalVoice"]["anchorCreated"])
            self.assertIsNotNone(evidence["voiceReferenceSha256"])

    def test_prepared_playback_fails_before_viseme_or_manifest_when_rate_exceeds_limit(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            result, calls, worker, manifest, alignment, visemes, _ = self._run_prepared_request(
                root, "too-fast", fast_utterance=True, per_npc_voices=True)
            self.assertEqual("failed", result["status"])
            self.assertEqual([], calls, "over-limit audio reached the prepared playback helper")
            self.assertFalse(manifest.exists())
            self.assertFalse((root / "responses" / "too-fast.wav").exists())
            self.assertTrue((root / "responses" / "too-fast.failed.json").is_file())
            self.assertEqual([], worker.jobs)
            self.assertFalse(alignment.exists())
            self.assertFalse(visemes.exists())

    def test_prepared_presentation_flag_requires_only_rhubarb_before_ready(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            rhubarb = root / "rhubarb.exe"
            rhubarb.write_bytes(b"test")
            try:
                args = coordinator.make_parser().parse_args([
                    "--cli", str(root / "audiocpp_cli.exe"),
                    "--model", str(root / "model.gguf"), "--model-sha256", "a" * 64,
                    "--spec", str(root / "breeze.json"),
                    "--profiles", str(root / "profiles.json"),
                    "--bindings", str(root / "bindings.json"),
                    "--voice-root", str(root / "voices"), "--root", str(root / "bridge"),
                    "--watch", "--server-endpoint", "http://127.0.0.1:8094",
                    "--rhubarb-cli", str(rhubarb),
                    "--prepare-presentation-before-playback"])
            except SystemExit:
                self.fail("coordinator parser does not accept prepared-presentation mode")
            with patch.object(coordinator, "verify_model_assets", side_effect=RuntimeError("verification-reached")) as verify_assets:
                with self.assertRaises(RuntimeError) as _raised_182:
                    coordinator.run(args)
                self.assertTrue(('verification-reached' in str(_raised_182.exception)), str(_raised_182.exception))
            verify_assets.assert_called_once()

    def test_prepared_jobs_overlap_but_audio_waits_for_both(self):
        with tempfile.TemporaryDirectory() as temporary:
            result, calls, _, manifest, alignment, visemes, _ = self._run_prepared_request(
                Path(temporary), "prepared-overlap", require_overlap=True)
            self.assertIs(result["presentationReady"], True)
            self.assertCountEqual(["visemes", "alignment"], calls)
            self.assertTrue(alignment.is_file() and visemes.is_file() and manifest.is_file())

    def test_prepared_request_publishes_visemes_and_alignment_before_audio(self):
        with tempfile.TemporaryDirectory() as temporary:
            result, calls, postprocessor, manifest, alignment, visemes, _ = self._run_prepared_request(
                Path(temporary), "prepared-success")
            self.assertEqual("ready", result["status"])
            self.assertCountEqual(["visemes", "alignment"], calls)
            self.assertTrue(alignment.is_file())
            self.assertTrue(visemes.is_file())
            self.assertTrue(manifest.is_file())
            manifest_value = json.loads(manifest.read_text(encoding="utf-8"))
            self.assertIs(manifest_value.get("presentationReady"), True)
            self.assertEqual([], postprocessor.jobs)

    def test_alignment_failure_cleans_up_parallel_visemes_after_workers_finish(self):
        with tempfile.TemporaryDirectory() as temporary:
            result, calls, _, manifest, alignment, visemes, _ = self._run_prepared_request(
                Path(temporary), "alignment-failure", fail_phase="alignment", require_overlap=True)
            self.assertCountEqual(["visemes", "alignment"], calls)
            self.assertIs(result["presentationReady"], False)
            self.assertTrue(manifest.is_file())
            self.assertFalse(alignment.exists())
            self.assertFalse(visemes.exists())

    def test_prepared_viseme_failure_publishes_audio_for_rms_fallback(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            result, calls, postprocessor, manifest, alignment, visemes, _ = self._run_prepared_request(
                root, "prepared-failure", fail_phase="visemes")
            self.assertEqual("ready", result["status"])
            self.assertIn("visemes", calls)
            self.assertTrue(set(calls).issubset({"visemes", "alignment"}))
            self.assertTrue(manifest.exists())
            self.assertIs(json.loads(manifest.read_text(encoding="utf-8"))["presentationReady"], False)
            self.assertTrue((root / "responses" / "prepared-failure.wav").is_file())
            self.assertFalse((root / "responses" / "prepared-failure.failed.json").exists())
            self.assertFalse(alignment.exists())
            self.assertFalse(visemes.exists())
            self.assertEqual([], postprocessor.jobs)
            evidence = json.loads((root / "measurements" / "prepared-failure.evidence.json").read_text())
            self.assertEqual("bounded-fallback", evidence["presentationStatus"])
            self.assertEqual("RuntimeError", evidence["presentationErrorType"])

    def test_prepared_request_cancelled_during_visemes_publishes_no_audio_or_failure_terminal(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            result, calls, postprocessor, manifest, alignment, visemes, cancel = self._run_prepared_request(
                root, "prepared-cancelled", cancel_after_visemes=True)
            self.assertEqual("cancelled", result["status"])
            self.assertIn("visemes", calls)
            self.assertTrue(set(calls).issubset({"visemes", "alignment"}))
            self.assertTrue(cancel.is_file())
            self.assertFalse(manifest.exists())
            self.assertFalse((root / "responses" / "prepared-cancelled.wav").exists())
            self.assertFalse((root / "responses" / "prepared-cancelled.failed.json").exists())
            self.assertFalse(alignment.exists())
            self.assertFalse(visemes.exists())
            self.assertEqual([], postprocessor.jobs)

    def test_prepared_request_deadline_is_shared_and_under_native_response_timeout(self):
        self.assertTrue(hasattr(coordinator, "PREPARED_PRESENTATION_REQUEST_TIMEOUT_SECONDS"),
            "coordinator has no bounded prepared-presentation deadline")
        self.assertTrue(hasattr(coordinator, "prepared_helper_timeout"),
            "coordinator has no shared-budget helper timeout calculation")
        self.assertEqual(240, coordinator.PREPARED_PRESENTATION_REQUEST_TIMEOUT_SECONDS)
        self.assertLess(coordinator.PREPARED_PRESENTATION_REQUEST_TIMEOUT_SECONDS, 300)
        with patch.object(coordinator.time, "monotonic", side_effect=[10.0, 200.25, 280.0]):
            self.assertEqual(coordinator.PREPARED_PRESENTATION_ALIGNMENT_TIMEOUT_SECONDS,
                coordinator.prepared_helper_timeout(280.0,
                    coordinator.PREPARED_PRESENTATION_ALIGNMENT_TIMEOUT_SECONDS, "alignment"))
            self.assertEqual(79, coordinator.prepared_helper_timeout(280.0,
                coordinator.PREPARED_PRESENTATION_VISEME_TIMEOUT_SECONDS, "viseme"))
            with self.assertRaises(coordinator.RequestError) as _raised_245:
                coordinator.prepared_helper_timeout(280.0, 90, "viseme")
            self.assertTrue(('request deadline' in str(_raised_245.exception)), str(_raised_245.exception))

    def test_live_stream_scheduler_retires_failed_and_cancelled_ids_before_later_stream(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            requests = root / "requests"
            responses = root / "responses"
            measurements = root / "measurements"
            requests.mkdir()
            responses.mkdir()
            measurements.mkdir()

            for stream_id in ("a-failed", "b-cancelled", "z-pending"):
                stream_input = requests / stream_id
                stream_input.mkdir()
                (stream_input / "begin.json").write_text(json.dumps({"streamId": stream_id}), encoding="utf-8")

            failed_stream = responses / "a-failed"
            failed_stream.mkdir()
            (failed_stream / "stream.json").write_text(json.dumps({"schemaVersion": 1,
                "streamId": "a-failed", "status": "failed", "errorCode": "PermissionError"}), encoding="utf-8")
            failed_evidence = measurements / "a-failed.failed.json"
            failed_evidence.write_text(json.dumps({"status": "failed", "message": "kept"}), encoding="utf-8")

            cancelled_input = requests / "b-cancelled"
            (cancelled_input / "cancel.json").write_text("{}", encoding="utf-8")
            cancelled_stream = responses / "b-cancelled"
            cancelled_stream.mkdir()
            (cancelled_stream / "stream.json").write_text(json.dumps({"schemaVersion": 1,
                "streamId": "b-cancelled", "status": "audio_complete"}), encoding="utf-8")
            processed = set()

            selected = coordinator.next_live_stream_to_process(requests, root, processed)

            self.assertEqual("z-pending", selected.name)
            self.assertEqual({"a-failed", "b-cancelled"}, processed)
            self.assertEqual({"schemaVersion": 1, "streamId": "a-failed", "status": "failed"},
                json.loads((measurements / "a-failed.coordinator-processed.json").read_text(encoding="utf-8")))
            self.assertEqual({"schemaVersion": 1, "streamId": "b-cancelled", "status": "cancelled"},
                json.loads((measurements / "b-cancelled.coordinator-processed.json").read_text(encoding="utf-8")))
            self.assertEqual({"status": "failed", "message": "kept"},
                json.loads(failed_evidence.read_text(encoding="utf-8")))

    def test_live_stream_processed_marker_survives_watch_iteration_restart(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            requests = root / "requests"
            measurements = root / "measurements"
            requests.mkdir()
            measurements.mkdir()
            stream_input = requests / "stream-failed"
            stream_input.mkdir()
            (stream_input / "begin.json").write_text("{}", encoding="utf-8")
            processed = set()

            coordinator.record_live_stream_result(root,
                {"schemaVersion": 1, "streamId": "stream-failed", "status": "failed",
                 "error": "FileExistsError", "message": "existing response"}, processed)
            self.assertIsNone(coordinator.next_live_stream_to_process(requests, root, set()))
            self.assertIsNone(coordinator.next_live_stream_to_process(requests, root, set()))
            marker = json.loads((measurements / "stream-failed.coordinator-processed.json").read_text(encoding="utf-8"))
            self.assertEqual("failed", marker["status"])

    def test_live_pcm_processor_is_byte_chunk_invariant_and_preserves_sample_clock(self):
        import array
        import hashlib
        import math
        import audio_loudness

        samples = array.array("h", [int(3000 * math.sin(index * 0.07)) for index in range(2400)])
        raw = samples.tobytes()

        class Writer:
            def __init__(self):
                self.data = bytearray()

            def writeframesraw(self, value):
                self.data.extend(value)

        def run(widths):
            normalizer = audio_loudness.StreamingPcmNormalizer(sample_rate=24000, channels=1)
            published = bytearray()
            writer = Writer()
            processor = coordinator._LivePcmStreamNormalizer(normalizer,
                lambda chunk, _sentence: published.extend(chunk))
            processor.begin_sentence()
            offset = 0
            index = 0
            while offset < len(raw):
                width = widths[index % len(widths)]
                processor.process_delta(raw[offset:offset + width], 0, writer)
                offset += width
                index += 1
            processor.finish_sentence()
            self.assertEqual(bytes(published), bytes(writer.data))
            evidence = processor.evidence(sample_frames=len(writer.data) // 2, wav_sha256="f" * 64)
            self.assertEqual(hashlib.sha256(raw).hexdigest(), evidence["inputPcmSha256"])
            self.assertEqual(hashlib.sha256(writer.data).hexdigest(), evidence["outputPcmSha256"])
            self.assertEqual(2400, evidence["sampleFrames"])
            self.assertEqual(len(raw), evidence["outputPcmBytes"])
            return bytes(writer.data)

        self.assertEqual(run([2]), run([1, 7, 3, 1025, 11]))

        normalizer = audio_loudness.StreamingPcmNormalizer(sample_rate=24000, channels=1)
        published = bytearray()
        writer = Writer()
        processor = coordinator._LivePcmStreamNormalizer(normalizer,
            lambda chunk, _sentence: published.extend(chunk))
        midpoint = len(raw) // 2
        for sequence, segment in enumerate((raw[:midpoint], raw[midpoint:])):
            processor.begin_sentence()
            offset = 0
            while offset < len(segment):
                chunk = segment[offset:offset + 7]
                processor.process_delta(chunk, sequence, writer)
                offset += len(chunk)
            processor.finish_sentence()
        continuous = audio_loudness.StreamingPcmNormalizer(sample_rate=24000, channels=1)
        self.assertEqual(continuous.process_pcm16(raw), bytes(writer.data))

    def test_q8_breeze_artifact_hash_is_accepted_by_generic_asset_verification(self):
        from unittest.mock import patch
        q8_hash = "b17b89b1c21107c44e8d556ab0a856d7acd06bdd0c914e68e0e57f6e33f555a3"
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            model = root / "breeze-tts-2-q8_0.gguf"
            spec = root / "breeze_tts.json"
            model.write_bytes(b"offline placeholder; exact SHA supplied by pinned manifest")
            spec.write_bytes(b"pinned spec placeholder")
            original = coordinator.sha256_file

            def pinned_hash(path):
                if Path(path) == model:
                    return q8_hash
                if Path(path) == spec:
                    return coordinator.PINNED_SPEC_SHA256
                return original(Path(path))

            with patch.object(coordinator, "sha256_file", side_effect=pinned_hash):
                coordinator.verify_model_assets(q8_hash, model, spec)

    def test_alignment_preview_sidecar_is_staged_and_bound_before_manifest(self):
        import hashlib
        import sys
        from types import SimpleNamespace

        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            responses = root / "responses"
            responses.mkdir()
            request = self.request_json(requestId="align-preview-001")
            wav_path = responses / "align-preview-001.wav"
            with wave.open(str(wav_path), "wb") as output:
                output.setnchannels(1)
                output.setsampwidth(2)
                output.setframerate(24000)
                output.writeframes(b"\x00\x00" * 24)
            audio_hash = coordinator.sha256_file(wav_path)
            text = request["text"]
            text_hash = hashlib.sha256(text.encode("utf-8")).hexdigest()
            model_hash = "a" * 64
            timeline = {"schemaVersion": 1, "status": "ready",
                "timelineKind": "forced-word-alignment", "text": text,
                "audioSampleRate": 24000, "audioFrames": 24,
                "alignerSampleRate": 16000,
                "coverage": {"matchedTokens": 1, "transcriptTokens": 1},
                "words": [{"text": "Please", "alignedText": "Please",
                    "textStartUtf16": 0, "textLengthUtf16": 6,
                    "startSample": 0, "endSample": 24}],
                "provenance": {"requestId": "align-preview-001", "audioSha256": audio_hash,
                    "transcriptSha256": text_hash, "modelSha256": model_hash,
                    "backend": "cpu", "threads": 1, "phonemeAlignment": False}}

            def fake_alignment(cli, model, audio, exact_text, output_path, **kwargs):
                self.assertFalse((responses / "align-preview-001.manifest.json").exists())
                self.assertEqual(text, exact_text)
                Path(output_path).write_text(json.dumps(timeline), encoding="utf-8")
                return timeline

            fake_module = SimpleNamespace(run_alignment=fake_alignment)
            with patch.dict(sys.modules, {"align_words": fake_module,
                    "tools.BreezeTtsTrial.align_words": fake_module}):
                status = coordinator.publish_preview_alignment(
                    request, wav_path, {"sampleRate": 24000, "sampleFrames": 24},
                    responses / "align-preview-001.manifest.json", root / "requests",
                    root / "audiocpp.exe", root / "aligner.gguf", model_hash)

            self.assertEqual("ready", status)
            published = json.loads((responses / "align-preview-001.alignment.json").read_text(encoding="utf-8"))
            self.assertEqual(text, published["text"])
            self.assertEqual(model_hash, published["provenance"]["modelSha256"])
            self.assertFalse((responses / "align-preview-001.manifest.json").exists())

    def test_rhubarb_preview_sidecar_is_staged_and_published_before_manifest(self):
        import hashlib
        import tempfile
        import wave
        import sys
        from types import SimpleNamespace
        from pathlib import Path

        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            responses = root / "responses"
            responses.mkdir()
            request = self.request_json(requestId="preview-001")
            wav_path = responses / "preview-001.wav"
            with wave.open(str(wav_path), "wb") as output:
                output.setnchannels(1)
                output.setsampwidth(2)
                output.setframerate(24000)
                output.writeframes(b"\x00\x00" * 24)
            audio_hash = coordinator.sha256_file(wav_path)
            text = request["text"]
            timeline = {"schemaVersion": 1, "status": "ready",
                "timelineKind": "recognizer-viseme-estimate", "requestId": "preview-001",
                "text": text, "audioSampleRate": 24000, "audioFrames": 24,
                "cues": [{"startSample": 0, "endSample": 24, "shape": "X"}],
                "provenance": {"audioSha256": audio_hash,
                    "dialogHintSha256": hashlib.sha256(text.encode("utf-8")).hexdigest()}}

            def fake_rhubarb(cli, audio, exact_text, output_path):
                self.assertFalse((responses / "preview-001.manifest.json").exists())
                self.assertEqual(text, exact_text)
                Path(output_path).write_text(json.dumps(timeline), encoding="utf-8")
                return timeline

            fake_module = SimpleNamespace(run_rhubarb=fake_rhubarb)
            with patch.dict(sys.modules, {"rhubarb_visemes": fake_module}):
                status = coordinator.publish_preview_visemes(
                    request, wav_path, {"sampleRate": 24000, "sampleFrames": 24},
                    responses / "preview-001.manifest.json", root / "requests", root / "rhubarb.exe")

            self.assertEqual("ready", status)
            published = json.loads((responses / "preview-001.visemes.json").read_text(encoding="utf-8"))
            self.assertEqual("preview-001", published["requestId"])
            self.assertFalse((responses / "preview-001.manifest.json").exists())

    def delivery_context(self, **overrides):
        value = {
            "schemaVersion": 1, "personId": "person-001", "mood": -72,
            "dispositionKnown": True, "disposition": 82,
            "trustKnown": False, "trust": 0,
            "fear": 91, "resentment": 74, "encounters": 8,
            "traits": {
                "warmth": "warm", "patience": "patient", "curiosity": "inquisitive",
                "privacy": "open", "manner": "courteous", "outlook": "pessimistic",
                "emotional stability": "sensitive", "ambition": "driven", "integrity": "honest",
                "courage": "steadfast", "dutifulness": "conscientious", "vocabulary": "plain",
                "eloquence": "articulate", "status attitude": "egalitarian", "humor": "wry",
                "self esteem": "grounded", "sensitivity": "attuned",
            },
        }
        value.update(overrides)
        return value

    def request_json(self, **overrides):
        value = {
            "schemaVersion": 1, "mode": "full-wav-pilot", "requestId": "request-001",
            "conversationToken": "conversation-abc", "personId": "person-001",
            "displayName": "Alding the Keeper", "text": "Please keep your voice down.",
            "textReadyMonotonicTicks": 123456, "timestampFrequency": 10000000,
        }
        value.update(overrides)
        return value

    def profiles(self):
        return {f"profile-{i:02d}": {"instruction": f"Stable voice {i}.", "seed": i,
                "perceivedGender": "female" if i % 2 == 0 else "male"} for i in range(12)}

    def test_authored_corpus_conversion_keeps_perceived_gender_age_and_no_fake_refs(self):
        corpus_path = Path(__file__).with_name("corpus.json")
        corpus = json.loads(corpus_path.read_text(encoding="utf-8"))
        first = import_corpus_profiles.convert_corpus(corpus)
        second = import_corpus_profiles.convert_corpus(corpus)
        self.assertEqual(first, second)
        self.assertEqual(12, len(first["profiles"]))
        profile = first["profiles"]["npcvoice-01"]
        self.assertIn("Perceived gender presentation: female", profile["instruction"])
        self.assertIn("Adult age presentation: late 20s", profile["instruction"])
        self.assertEqual("female", profile["perceivedGender"])
        self.assertEqual(set(("instruction", "seed", "perceivedGender")), set(profile))
        self.assertTrue(all("voiceRef" not in row for row in first["profiles"].values()))

    def test_live_stream_emits_chunks_and_retains_a_combined_replay_wav(self):
        import hashlib
        import struct
        import sys
        from types import SimpleNamespace

        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            request_root = root / "requests" / "stream-001"
            request_root.mkdir(parents=True)
            response_root = root / "responses"
            voice_root = root / "voices"
            voice_root.mkdir()
            profile_path = root / "profiles.json"
            profiles = self.profiles()
            profile_path.write_text(json.dumps({"schemaVersion": 1, "profiles": profiles}), encoding="utf-8")
            bindings_path = root / "bindings.json"
            begin = {"schemaVersion": 1, "mode": "streaming-live-pcm-v1",
                "streamId": "stream-001", "conversationToken": "conversation-1",
                "personId": "person-001", "displayName": "Alding", "voiceGender": "female",
                "deliveryContext": None, "voiceIdentity": {"schemaVersion": 1,
                    "sex": {"value": "female", "known": True, "source": "native.static-npc"},
                    "race": {"value": "Argonian", "known": True, "source": "native.static-npc"},
                    "age": {"value": "unknown", "confidence": "unknown", "source": "native.no-stable-key"},
                    "origin": {"value": "unknown", "known": False, "source": "native.not-provided"}},
                "sampleRate": 24000, "channels": 1}
            (request_root / "begin.json").write_text(json.dumps(begin), encoding="utf-8")
            sentence = {"schemaVersion": 1, "streamId": "stream-001",
                "conversationToken": "conversation-1", "personId": "person-001",
                "sequence": 0, "exactText": "Keep your voice down.",
                "textReadyMonotonicTicks": 1234567, "timestampFrequency": 10000000}
            (request_root / "0000.json").write_text(json.dumps(sentence), encoding="utf-8")
            complete = dict(begin)
            complete["finalTranscript"] = sentence["exactText"]
            (request_root / "complete.json").write_text(json.dumps(complete), encoding="utf-8")

            pcm = struct.pack("<2400h", *([6000] * 2400))

            def fake_synthesize(_endpoint, _model, _text, _instruction, _reference,
                                _reference_text, output_wav, _timeout, *, seed,
                                guidance_scale, on_pcm_delta, cancel_event):
                self.assertEqual(4.0, guidance_scale)
                self.assertIsInstance(seed, int)
                offset = 0
                widths = (1, 7, 3, 1025, 11)
                index = 0
                while offset < len(pcm):
                    width = widths[index % len(widths)]
                    on_pcm_delta(pcm[offset:offset + width])
                    offset += width
                    index += 1
                with wave.open(output_wav, "wb") as writer:
                    writer.setnchannels(1)
                    writer.setsampwidth(2)
                    writer.setframerate(24000)
                    writer.writeframes(pcm)
                return {"synthesisMs": 4.5, "firstAudioMs": 1.2,
                        "audioDeltaCount": 1, "serverTiming": None}

            with patch.dict(sys.modules, {"server_sse_client": SimpleNamespace(synthesize=fake_synthesize)}):
                class FailingFinalPostprocessor:
                    def __init__(self):
                        self.jobs = []

                    def submit(self, job):
                        self.jobs.append(job)
                        if job.sentence_sequence is None:
                            raise RuntimeError("final replay sidecar worker unavailable")
                        return "queued"

                postprocessor = FailingFinalPostprocessor()
                result = coordinator.process_live_stream(request_root, root, root / "model.gguf",
                    "a" * 64, root / "spec.json", profile_path, bindings_path, voice_root,
                    None, 30, "http://127.0.0.1:8094", poll_interval=0.001,
                    postprocessor=postprocessor)

            self.assertEqual("audio_complete", result["status"])
            self.assertEqual(2, len(postprocessor.jobs))
            self.assertTrue((response_root / "stream-001.wav").is_file())
            self.assertTrue((response_root / "stream-001.manifest.json").is_file())
            self.assertTrue((response_root / "stream-001" / "sentences" / "stream-001.0000.wav").is_file())
            live_stream = json.loads((response_root / "stream-001" / "stream.json").read_text())
            self.assertEqual("audio_complete", live_stream["status"])
            self.assertEqual(0, live_stream["consumedSentenceSequence"])
            evidence = json.loads((root / "measurements" / "stream-001.evidence.json").read_text())
            self.assertEqual("failed-submit", evidence["finalPostprocessStatus"])
            self.assertEqual("Argonian", evidence["voiceIdentity"]["race"]["value"])
            self.assertEqual("queued", evidence["sentences"][0]["postprocessStatus"])
            self.assertEqual(1234567, evidence["sentences"][0]["textReadyMonotonicTicks"])
            self.assertEqual(10000000, evidence["sentences"][0]["timestampFrequency"])
            normalization = evidence["livePcmNormalization"]
            self.assertEqual(2400, normalization["sampleFrames"])
            self.assertEqual(hashlib.sha256(pcm).hexdigest(), normalization["inputPcmSha256"])
            chunks = sorted((response_root / "stream-001" / "chunks").glob("*.pcm"))
            normalized_pcm = b"".join(chunk.read_bytes() for chunk in chunks)
            self.assertNotEqual(pcm, normalized_pcm)
            self.assertEqual(hashlib.sha256(normalized_pcm).hexdigest(), normalization["outputPcmSha256"])
            with wave.open(str(response_root / "stream-001" / "sentences" / "stream-001.0000.wav"), "rb") as sentence_wav:
                self.assertEqual(normalized_pcm, sentence_wav.readframes(sentence_wav.getnframes()))
            with wave.open(str(response_root / "stream-001.wav"), "rb") as replay:
                self.assertEqual(2400, replay.getnframes())
                self.assertEqual(normalized_pcm, replay.readframes(replay.getnframes()))

    def test_live_stream_cancel_during_terminal_publish_leaves_no_stale_replay_metadata(self):
        import struct
        import sys
        from types import SimpleNamespace
        from live_pcm_spool import LivePcmSpool, StreamCancelled

        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            request_root = root / "requests" / "stream-cancel-terminal"
            request_root.mkdir(parents=True)
            voice_root = root / "voices"
            voice_root.mkdir()
            profile_path = root / "profiles.json"
            profile_path.write_text(json.dumps({"schemaVersion": 1, "profiles": self.profiles()}), encoding="utf-8")
            bindings_path = root / "bindings.json"
            begin = {"schemaVersion": 1, "mode": "streaming-live-pcm-v1",
                "streamId": "stream-cancel-terminal", "conversationToken": "conversation-1",
                "personId": "person-001", "displayName": "Alding", "voiceGender": "female",
                "deliveryContext": None, "sampleRate": 24000, "channels": 1}
            (request_root / "begin.json").write_text(json.dumps(begin), encoding="utf-8")
            sentence = {"schemaVersion": 1, "streamId": begin["streamId"],
                "conversationToken": begin["conversationToken"], "personId": begin["personId"],
                "sequence": 0, "exactText": "Keep your voice down."}
            (request_root / "0000.json").write_text(json.dumps(sentence), encoding="utf-8")
            complete = dict(begin, finalTranscript=sentence["exactText"])
            (request_root / "complete.json").write_text(json.dumps(complete), encoding="utf-8")
            pcm = struct.pack("<2400h", *([6000] * 2400))

            def fake_synthesize(_endpoint, _model, _text, _instruction, _reference,
                                _reference_text, output_wav, _timeout, *, seed,
                                guidance_scale, on_pcm_delta, cancel_event):
                on_pcm_delta(pcm)
                with wave.open(output_wav, "wb") as writer:
                    writer.setnchannels(1)
                    writer.setsampwidth(2)
                    writer.setframerate(24000)
                    writer.writeframes(pcm)
                return {"synthesisMs": 1, "firstAudioMs": 1,
                    "audioDeltaCount": 1, "serverTiming": None}

            original_finish = LivePcmSpool.finish

            def cancel_at_audio_complete(spool, status, **kwargs):
                if status == "cancelled":
                    raise OSError("terminal marker could not be rewritten")
                if status == "audio_complete":
                    marker = {"schemaVersion": 1, "streamId": spool.stream_id,
                        "conversationToken": spool.conversation_token, "personId": spool.person_id}
                    spool.cancel_path.write_text(json.dumps(marker), encoding="utf-8")
                return original_finish(spool, status, **kwargs)

            with patch.dict(sys.modules, {"server_sse_client": SimpleNamespace(synthesize=fake_synthesize)}), \
                    patch.object(LivePcmSpool, "finish", new=cancel_at_audio_complete):
                result = coordinator.process_live_stream(request_root, root, root / "model.gguf",
                    "a" * 64, root / "spec.json", profile_path, bindings_path, voice_root,
                    None, 30, "http://127.0.0.1:8094", poll_interval=0.001)

            self.assertEqual("cancelled", result["status"])
            self.assertFalse((root / "responses" / (begin["streamId"] + ".wav")).exists())
            self.assertFalse((root / "responses" / (begin["streamId"] + ".manifest.json")).exists())
            self.assertFalse((root / "measurements" / (begin["streamId"] + ".evidence.json")).exists())

    def test_corpus_conversion_retains_only_verified_generated_reference(self):
        corpus = {"schemaVersion": 1, "voiceProfiles": []}
        for index in range(12):
            corpus["voiceProfiles"].append({"profileId": f"p-{index:02d}", "perceivedGender": "female",
                "adultAgePresentation": "adult", "identityDescriptor": "stable identity"})
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            voice_root = root / "voices"
            voice_root.mkdir()
            voice_file = voice_root / "voice.wav"
            voice_file.write_bytes(b"trusted-reference")
            import hashlib
            reference = {"status": "generated", "assetId": "voice.wav", "transcript": "Exact words.",
                         "sha256": hashlib.sha256(voice_file.read_bytes()).hexdigest()}
            corpus["voiceProfiles"][0]["voiceReference"] = reference
            output = import_corpus_profiles.convert_corpus(corpus, voice_root)
            self.assertEqual("voice.wav", output["profiles"]["p-00"]["voiceRef"])
            self.assertEqual("Exact words.", output["profiles"]["p-00"]["referenceText"])
            self.assertEqual(reference["sha256"], output["profiles"]["p-00"]["voiceReferenceSha256"])
            self.assertEqual("female", output["profiles"]["p-00"]["perceivedGender"])
            reference["sha256"] = "0" * 64
            with self.assertRaises(import_corpus_profiles.CorpusError):
                import_corpus_profiles.convert_corpus(corpus, voice_root)

    def test_corpus_refresh_preserves_verified_existing_voice_references(self):
        corpus = {"schemaVersion": 1, "voiceProfiles": []}
        for index in range(12):
            corpus["voiceProfiles"].append({"profileId": f"p-{index:02d}", "perceivedGender": "male",
                "adultAgePresentation": "adult", "identityDescriptor": "stable identity",
                "voiceReference": {"status": "not-generated", "assetId": None, "transcript": None, "sha256": None}})
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            voice_root = root / "voices"
            voice_root.mkdir()
            voice_file = voice_root / "p-00.wav"
            voice_file.write_bytes(b"parent-generated-reference")
            import hashlib
            old_profile = {"voiceRef": "p-00.wav", "referenceText": "Reference transcript.",
                           "voiceReferenceSha256": hashlib.sha256(voice_file.read_bytes()).hexdigest()}
            existing = {"p-00": old_profile}
            with self.assertRaises(import_corpus_profiles.CorpusError):
                import_corpus_profiles.convert_corpus(corpus, existing_profiles=existing)
            output = import_corpus_profiles.convert_corpus(corpus, voice_root, existing_profiles=existing)
            self.assertEqual(old_profile, {key: output["profiles"]["p-00"][key] for key in old_profile})

    def test_native_request_preserves_final_text_and_identity_fields(self):
        request = coordinator.parse_request(self.request_json())
        self.assertEqual("request-001", request.request_id)
        self.assertEqual("conversation-abc", request.conversation_token)
        self.assertEqual("person-001", request.person_id)
        self.assertEqual("Please keep your voice down.", request.text)
        self.assertEqual(123456, request.text_ready_ticks)
        self.assertIsNone(request.voice_gender)

    def test_request_accepts_optional_typed_voice_gender(self):
        for gender in ("female", "male", "unknown"):
            with self.subTest(gender=gender):
                request = coordinator.parse_request(self.request_json(voiceGender=gender))
                self.assertEqual(gender, request.voice_gender)

    def test_request_preserves_validated_voice_identity_only_as_metadata(self):
        identity = {"schemaVersion": 1,
            "sex": {"value": "female", "known": True, "source": "native.static-npc"},
            "race": {"value": "Argonian", "known": True, "source": "native.static-npc"},
            "age": {"value": "unknown", "confidence": "unknown", "source": "native.no-stable-key"},
            "origin": {"value": "unknown", "known": False, "source": "native.not-provided"}}
        request = coordinator.parse_request(self.request_json(voiceIdentity=identity))
        self.assertEqual(identity, request.voice_identity)
        with self.assertRaises(coordinator.RequestError) as _raised_764:
            coordinator.parse_request(self.request_json(voiceIdentity={"origin": "invented"}))
        self.assertTrue(('voiceIdentity' in str(_raised_764.exception)), str(_raised_764.exception))

    def test_sample_kind_defaults_to_native_dialogue_and_accepts_controlled_contrast(self):
        self.assertEqual("native-dialogue", coordinator.parse_request(self.request_json()).sample_kind)
        request = coordinator.parse_request(self.request_json(sampleKind="controlled-voice-contrast",
            controlledDirection={"emotion": "neutral", "deliveryMode": "normal"},
            contrastLabel="neutral",
            deliveryContext=self.delivery_context()))
        self.assertEqual("controlled-voice-contrast", request.sample_kind)

    def test_request_rejects_unknown_sample_kind(self):
        with self.assertRaises(coordinator.RequestError) as _raised_776:
            coordinator.parse_request(self.request_json(sampleKind="qwen-generated"))
        self.assertTrue(('sampleKind' in str(_raised_776.exception)), str(_raised_776.exception))

    def test_controlled_direction_is_typed_and_only_allowed_for_controlled_contrasts(self):
        controls = {"emotion": "neutral", "deliveryMode": "whisper"}
        request = coordinator.parse_request(self.request_json(sampleKind="controlled-voice-contrast",
            controlledDirection=controls, contrastLabel="whisper", deliveryContext=self.delivery_context()))
        self.assertEqual(controls, request.controlled_direction)
        with self.assertRaises(coordinator.RequestError) as _raised_784:
            coordinator.parse_request(self.request_json(controlledDirection=controls))
        self.assertTrue(('controlledDirection' in str(_raised_784.exception)), str(_raised_784.exception))
        with self.assertRaises(coordinator.RequestError) as _raised_786:
            coordinator.parse_request(self.request_json(contrastLabel="whisper"))
        self.assertTrue(('contrastLabel' in str(_raised_786.exception)), str(_raised_786.exception))
        with self.assertRaises(coordinator.RequestError) as _raised_788:
            coordinator.parse_request(self.request_json(sampleKind="controlled-voice-contrast",
                controlledDirection={"emotion": "joy", "deliveryMode": "yell"},
                deliveryContext=self.delivery_context()))
        self.assertTrue(('controlledDirection' in str(_raised_788.exception)), str(_raised_788.exception))
        with self.assertRaises(coordinator.RequestError) as _raised_792:
            coordinator.parse_request(self.request_json(sampleKind="controlled-voice-contrast",
                controlledDirection=controls, contrastLabel="whisper"))
        self.assertTrue(('deliveryContext' in str(_raised_792.exception)), str(_raised_792.exception))
        with self.assertRaises(coordinator.RequestError) as _raised_795:
            coordinator.parse_request(self.request_json(sampleKind="controlled-voice-contrast",
                controlledDirection=controls, contrastLabel="other", deliveryContext=self.delivery_context()))
        self.assertTrue(('contrastLabel' in str(_raised_795.exception)), str(_raised_795.exception))

    def test_delivery_context_is_optional_and_strictly_parsed(self):
        legacy = coordinator.parse_request(self.request_json())
        self.assertIsNone(legacy.delivery_context)
        context = self.delivery_context()
        request = coordinator.parse_request(self.request_json(deliveryContext=context))
        self.assertEqual(context, request.delivery_context)
        self.assertEqual("person-001", request.delivery_context["personId"])

    def test_delivery_context_rejects_identity_ranges_shapes_and_trait_vocab_errors(self):
        cases = [
            {"personId": "other-person"}, {"schemaVersion": 1.0}, {"extra": "rejected"},
            {"mood": 101}, {"mood": True},
            {"dispositionKnown": 1}, {"disposition": -1}, {"trustKnown": False, "trust": 101},
            {"fear": 101}, {"resentment": -1}, {"encounters": -1},
            {"traits": {"warmth": "kind"}},
            {"traits": dict(self.delivery_context()["traits"], courage="cowardly")},
        ]
        for changes in cases:
            with self.subTest(changes=changes), self.assertRaises(coordinator.RequestError):
                coordinator.parse_request(self.request_json(deliveryContext=self.delivery_context(**changes)))

    def test_personality_prosody_uses_structured_values_without_assuming_unknown_trust(self):
        context = self.delivery_context()
        directions = coordinator.personality_directions(context)
        joined = " ".join(directions).lower()
        self.assertNotIn("low spirits", joined)
        self.assertIn("warm", joined)
        self.assertIn("fear present but contained", joined)
        self.assertIn("not hesitant", joined)
        self.assertNotIn("resentment controls delivery", joined)
        self.assertNotIn("cool and slightly sharp", joined)
        self.assertNotIn("distrust", joined)
        self.assertNotIn("emotion: anger", joined)
        self.assertIn("does not imply comfort, trust or friendliness", joined)
        self.assertNotIn("at ease", joined)

    def test_native_numeric_emotion_uses_fear_resentment_mood_then_trust_precedence(self):
        cases = (
            ({"fear": 80, "resentment": 95, "mood": 90, "trustKnown": True, "trust": 0}, "fear"),
            ({"fear": 0, "resentment": 80, "mood": 90, "trustKnown": True, "trust": 0}, "anger"),
            ({"fear": 0, "resentment": 0, "mood": -70, "trustKnown": True, "trust": 100}, "grief"),
            ({"fear": 0, "resentment": 0, "mood": 0, "trustKnown": True, "trust": 10}, "concern"),
            ({"fear": 0, "resentment": 0, "mood": 70, "trustKnown": True, "trust": 10}, "joy"),
            ({"fear": 0, "resentment": 0, "mood": 0, "trustKnown": False, "trust": 0}, "neutral"),
        )
        for overrides, expected in cases:
            with self.subTest(expected=expected):
                context = self.delivery_context(**overrides)
                direction = coordinator.native_context_direction(context)
                self.assertEqual(expected, direction.emotion)
                self.assertEqual("normal", direction.delivery_mode)
                self.assertEqual("native_numeric_context", direction.provenance)

    def test_native_emotion_instruction_names_race_identity_without_test_override_language(self):
        context = self.delivery_context(mood=0, fear=90, resentment=95, trustKnown=True, trust=0)
        direction = coordinator.native_context_direction(context)
        profile = {"instruction": "Adult female Argonian. Brazilian Portuguese-influenced English; dry sibilants.",
            "race": "Argonian", "seed": 84}
        instruction = coordinator.instruction_for(direction, profile, context)
        self.assertIn("Adult female Argonian", instruction)
        self.assertIn("Brazilian Portuguese-influenced English", instruction)
        self.assertIn("Temporary emotion (native numeric context): fear.", instruction)
        self.assertIn("preserve stable voice identity", instruction.lower())
        self.assertNotIn("controlled", instruction.lower())

    def test_prepared_playback_rejects_speech_above_325_wpm(self):
        text = "One two three four five."
        boundary_duration = 300 / 325
        self.assertEqual(325.0, coordinator.speech_rate_wpm(text, boundary_duration))
        with self.assertRaises(coordinator.RequestError) as _raised_868:
            coordinator.validate_speech_rate(text, 300 / 326)
        self.assertTrue(('325 WPM' in str(_raised_868.exception)), str(_raised_868.exception))

    def test_familiar_warm_npc_with_resentment_keeps_identity_but_sounds_cool(self):
        context = self.delivery_context(mood=0, fear=0, resentment=90,
            disposition=90, encounters=12)
        directions = " ".join(coordinator.personality_directions(context)).lower()
        self.assertIn("saved personality baseline: warm", directions)
        self.assertIn("resentment controls delivery", directions)
        self.assertIn("cool and slightly sharp", directions)
        self.assertIn("does not imply comfort, trust or friendliness", directions)
        self.assertNotIn("at ease", directions)

    def test_neutral_mood_does_not_become_anger_and_controlled_direction_composes(self):
        context = self.delivery_context(mood=0, fear=0, resentment=0, encounters=0,
            traits=dict(self.delivery_context()["traits"], courage="cautious"))
        personal = coordinator.personality_directions(context)
        self.assertFalse(any("angry" in item.lower() for item in personal))
        direction = coordinator.TrialDirection("fear", "whisper", "controlled_trial")
        profile = {"instruction": "Identity unchanged.", "seed": 89}
        instruction = coordinator.instruction_for(direction, profile, context)
        self.assertIn("Identity unchanged.", instruction)
        self.assertIn("Personal vocal delivery", instruction)
        self.assertIn("Controlled emotion override (controlled_trial): fear.", instruction)
        self.assertIn("Delivery mode: whisper (controlled_trial).", instruction)
        self.assertIn("prosody only", instruction)
        self.assertNotIn("Emotional delivery: neutral.", instruction)
        neutral_instruction = coordinator.instruction_for(
            coordinator.TrialDirection("neutral", "normal", "controlled_voice_contrast"), profile, context)
        self.assertIn("no fixed emotion override", neutral_instruction)
        self.assertNotIn("Emotional delivery: neutral.", neutral_instruction)
        request = coordinator.parse_request(self.request_json(deliveryContext=context, text="Do not change these words."))
        reference = Path("stable-reference.wav")
        command = coordinator.build_cli_command(Path("audiocpp_cli.exe"), Path("breeze.gguf"),
            Path("breeze.json"), "cuda", request,
            {"instruction": "Identity unchanged.", "seed": 89, "referenceText": "Reference words."},
            reference, Path("out.wav"), direction)
        self.assertEqual("Do not change these words.", command[command.index("--text") + 1])
        self.assertEqual("89", command[command.index("--seed") + 1])
        self.assertEqual(str(reference), command[command.index("--voice-ref") + 1])
        self.assertEqual("Reference words.", command[command.index("--reference-text") + 1])


    def test_request_rejects_unrecognized_voice_gender(self):
        with self.assertRaises(coordinator.RequestError) as _raised_912:
            coordinator.parse_request(self.request_json(voiceGender="woman"))
        self.assertTrue(('voiceGender' in str(_raised_912.exception)), str(_raised_912.exception))

    def test_request_rejects_missing_or_untrusted_fields_and_oversize_text(self):
        with self.assertRaises(coordinator.RequestError):
            coordinator.parse_request({"schemaVersion": 1})
        with self.assertRaises(coordinator.RequestError):
            coordinator.parse_request(self.request_json(requestId="../escape"))
        with self.assertRaises(coordinator.RequestError):
            coordinator.parse_request(self.request_json(text="x" * (coordinator.MAX_TEXT_CHARS + 1)))
        # Unknown request fields are ignored so native bridge additions remain compatible;
        # dangerous model/profile paths are never consumed from the request.
        request = coordinator.parse_request(self.request_json(modelPath="C:/untrusted.gguf", profileId="attacker"))
        self.assertEqual("person-001", request.person_id)

    def test_request_filename_must_match_native_request_id(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "different.request.json"
            path.write_text(json.dumps(self.request_json()), encoding="utf-8")
            with self.assertRaises(coordinator.RequestError):
                coordinator.read_request(path, "different")

    def test_parse_failure_publishes_terminal_only_with_valid_identity_envelope(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            working = root / "working"
            working.mkdir()
            claimed = working / "request-001.claimed.request.json"
            invalid = self.request_json(voiceGender="Female")
            claimed.write_text(json.dumps(invalid), encoding="utf-8")

            result = coordinator.process_claimed(claimed, "request-001", root,
                root / "audiocpp_cli.exe", root / "model.gguf", "0" * 64,
                root / "breeze.json", root / "profiles.json", root / "bindings.json",
                root / "voices", None, "cuda", 10, "test-build")

            self.assertEqual("failed", result["status"])
            terminal_path = root / "responses" / "request-001.failed.json"
            self.assertTrue(terminal_path.is_file(), "safe parse failure must publish a bound terminal response")
            terminal = json.loads(terminal_path.read_text(encoding="utf-8"))
            self.assertEqual({"schemaVersion": 1, "requestId": "request-001",
                "conversationToken": "conversation-abc", "personId": "person-001", "status": "failed"}, terminal)
            self.assertFalse((root / "responses" / "request-001.manifest.json").exists())

            mismatched = root / "working" / "request-002.claimed.request.json"
            mismatched.write_text(json.dumps(self.request_json(requestId="different-id")), encoding="utf-8")
            coordinator.process_claimed(mismatched, "request-002", root,
                root / "audiocpp_cli.exe", root / "model.gguf", "0" * 64,
                root / "breeze.json", root / "profiles.json", root / "bindings.json",
                root / "voices", None, "cuda", 10, "test-build")
            self.assertFalse((root / "responses" / "request-002.failed.json").exists(),
                "unmatched request identity must not publish a terminal response")

    def test_synthesis_failure_publishes_bound_terminal_response(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            working = root / "working"
            working.mkdir()
            claimed = working / "request-001.claimed.request.json"
            claimed.write_text(json.dumps(self.request_json()), encoding="utf-8")
            profiles_path = root / "profiles.json"
            profiles_path.write_text(json.dumps({"schemaVersion": 1, "profiles": self.profiles()}), encoding="utf-8")
            (root / "voices").mkdir()

            with patch.object(coordinator.subprocess, "run", return_value=subprocess.CompletedProcess([], 1, "", "failed")):
                result = coordinator.process_claimed(claimed, "request-001", root,
                    root / "audiocpp_cli.exe", root / "model.gguf", "0" * 64,
                    root / "breeze.json", profiles_path, root / "bindings.json",
                    root / "voices", None, "cuda", 10, "test-build")

            self.assertEqual("failed", result["status"])
            terminal_path = root / "responses" / "request-001.failed.json"
            self.assertTrue(terminal_path.is_file(), "synthesis failure must publish a bound terminal response")
            terminal = json.loads(terminal_path.read_text(encoding="utf-8"))
            self.assertEqual("conversation-abc", terminal["conversationToken"])
            self.assertEqual("person-001", terminal["personId"])
            self.assertEqual("failed", terminal["status"])

    def test_person_profile_binding_is_persistent_and_trusted(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "voice-bindings.json"
            profiles = self.profiles()
            first = coordinator.assign_profile("person-001", profiles, path)
            self.assertEqual(first, coordinator.assign_profile("person-001", profiles, path))
            second = coordinator.assign_profile("person-002", profiles, path)
            self.assertNotEqual(first, second)
            path.write_text(json.dumps({"schemaVersion": 1, "bindings": {"person-001": "unknown"}}), encoding="utf-8")
            with self.assertRaises(coordinator.RequestError):
                coordinator.assign_profile("person-001", profiles, path)

    def test_known_voice_gender_filters_profiles_and_binding_stays_stable(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "voice-bindings.json"
            profiles = self.profiles()
            first = coordinator.assign_profile("female-resident", profiles, path, "female")
            self.assertEqual("female", profiles[first]["perceivedGender"])
            self.assertEqual(first, coordinator.assign_profile("female-resident", profiles, path, "female"))
            second = coordinator.assign_profile("male-resident", profiles, path, "male")
            self.assertEqual("male", profiles[second]["perceivedGender"])

    def test_incompatible_existing_binding_fails_with_manual_repair_guidance(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "voice-bindings.json"
            path.write_text(json.dumps({"schemaVersion": 1,
                "bindings": {"Gwen-Cienne": "profile-01"}}), encoding="utf-8")
            with self.assertRaises(coordinator.RequestError) as _raised_1017:
                coordinator.assign_profile("Gwen-Cienne", self.profiles(), path, "female")
            self.assertTrue(('remove only this personId binding' in str(_raised_1017.exception)), str(_raised_1017.exception))
            saved = json.loads(path.read_text(encoding="utf-8"))
            self.assertEqual("profile-01", saved["bindings"]["Gwen-Cienne"])

    def test_direction_defaults_and_trial_override_provenance(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "directions.json"
            default = coordinator.load_direction("person-001", path)
            self.assertEqual(("neutral", "normal", "baseline_default"),
                             (default.emotion, default.delivery_mode, default.provenance))
            path.write_text(json.dumps({"schemaVersion": 1, "directions": {
                "person-001": {"emotion": "grief", "deliveryMode": "whisper", "provenance": "controlled_trial"}
            }}), encoding="utf-8")
            trial = coordinator.load_direction("person-001", path)
            self.assertEqual(("grief", "whisper", "controlled_trial"),
                             (trial.emotion, trial.delivery_mode, trial.provenance))
            path.write_text(json.dumps({"schemaVersion": 1, "directions": {
                "person-001": {"emotion": "grief", "deliveryMode": "whisper", "provenance": "model_inferred"}
            }}), encoding="utf-8")
            with self.assertRaises(coordinator.RequestError):
                coordinator.load_direction("person-001", path)

    def test_profile_reference_cannot_escape_trusted_voice_root(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            profile = {"instruction": "Steady voice.", "seed": 42,
                       "voiceRef": "../outside.wav", "referenceText": "Example."}
            with self.assertRaises(coordinator.RequestError):
                coordinator.load_voice_profile({"profile-01": profile}, "profile-01", root / "voices")

    def test_profiles_require_typed_perceived_gender(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "profiles.json"
            profiles = self.profiles()
            del profiles["profile-00"]["perceivedGender"]
            path.write_text(json.dumps({"schemaVersion": 1, "profiles": profiles}), encoding="utf-8")
            with self.assertRaises(coordinator.RequestError) as _raised_1054:
                coordinator.load_profiles(path)
            self.assertTrue(('re-import corpus profiles' in str(_raised_1054.exception)), str(_raised_1054.exception))

    def test_cancelled_request_before_claim_is_detected(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            marker = root / "requests" / "request-001.cancel.json"
            marker.parent.mkdir()
            marker.write_text(json.dumps({"schemaVersion": 1, "requestId": "request-001",
                "conversationToken": "conversation-abc", "reason": "conversation-closed"}), encoding="utf-8")
            self.assertTrue(coordinator.is_cancelled(root, "request-001", "conversation-abc"))

    def test_cancel_during_synthesis_removes_exact_ephemeral_outputs(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            requests = root / "requests"
            requests.mkdir()
            working = root / "working"
            working.mkdir()
            claimed = working / "request-001.claimed.request.json"
            claimed.write_text(json.dumps(self.request_json()), encoding="utf-8")
            profiles_path = root / "profiles.json"
            profiles_path.write_text(json.dumps({"schemaVersion": 1, "profiles": self.profiles()}), encoding="utf-8")
            bindings_path = root / "voice-bindings.json"
            cli, model, spec = root / "audiocpp_cli.exe", root / "model.gguf", root / "breeze.json"
            voice_root = root / "voices"

            def synthesize(command, **kwargs):
                wav_path = Path(command[command.index("--out") + 1])
                wav_path.parent.mkdir(parents=True, exist_ok=True)
                with wave.open(str(wav_path), "wb") as output:
                    output.setnchannels(1)
                    output.setsampwidth(2)
                    output.setframerate(22050)
                    output.writeframes(b"\x00\x00")
                (requests / "request-001.cancel.json").write_text(json.dumps({
                    "schemaVersion": 1, "requestId": "request-001",
                    "conversationToken": "conversation-abc", "reason": "conversation-closed"
                }), encoding="utf-8")
                return subprocess.CompletedProcess(command, 0, "", "")

            with patch.object(coordinator.subprocess, "run", side_effect=synthesize):
                result = coordinator.process_claimed(claimed, "request-001", root, cli, model, "0" * 64,
                    spec, profiles_path, bindings_path, voice_root, None, "cuda", 10, "test-build")
            self.assertEqual("cancelled", result["status"])
            self.assertFalse((root / "responses" / "request-001.wav").exists())
            self.assertFalse((root / "responses" / "request-001.manifest.json").exists())
            self.assertFalse((root / "measurements" / "request-001.evidence.json").exists())
            self.assertTrue((requests / "request-001.cancel.json").exists())
            self.assertFalse(claimed.exists())

    def test_successful_request_evidence_keeps_exact_text_instruction_and_seed(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            requests = root / "requests"
            requests.mkdir()
            working = root / "working"
            working.mkdir()
            claimed = working / "request-002.claimed.request.json"
            claimed.write_text(json.dumps(self.request_json(
                requestId="request-002", voiceGender="female", text="Exact fictional line.",
                deliveryContext=self.delivery_context(), sampleKind="controlled-voice-contrast",
                controlledDirection={"emotion": "neutral", "deliveryMode": "whisper"},
                contrastLabel="whisper"
            )), encoding="utf-8")
            profile_rows = self.profiles()
            profiles_path = root / "profiles.json"
            profiles_path.write_text(json.dumps({"schemaVersion": 1, "profiles": profile_rows}), encoding="utf-8")
            bindings_path = root / "voice-bindings.json"
            directions_path = root / "directions.json"
            directions_path.write_text(json.dumps({"schemaVersion": 1, "directions": {
                "person-001": {"emotion": "concern", "deliveryMode": "normal", "provenance": "controlled_trial"}
            }}), encoding="utf-8")

            def synthesize(command, **kwargs):
                wav_path = Path(command[command.index("--out") + 1])
                wav_path.parent.mkdir(parents=True, exist_ok=True)
                with wave.open(str(wav_path), "wb") as output:
                    output.setnchannels(1)
                    output.setsampwidth(2)
                    output.setframerate(22050)
                    output.writeframes(b"\x00\x00")
                return subprocess.CompletedProcess(command, 0, "", "")

            with patch.object(coordinator.subprocess, "run", side_effect=synthesize):
                result = coordinator.process_claimed(claimed, "request-002", root,
                    root / "audiocpp_cli.exe", root / "model.gguf", "0" * 64, root / "breeze.json",
                    profiles_path, bindings_path, root / "voices", directions_path, "cuda", 10, "test-build")
            self.assertEqual("ready", result["status"])
            evidence = json.loads((root / "measurements" / "request-002.evidence.json").read_text(encoding="utf-8"))
            self.assertEqual("Exact fictional line.", evidence["finalizedText"])
            self.assertEqual("female", evidence["voiceGender"])
            self.assertEqual(profile_rows[evidence["profileId"]]["instruction"], evidence["profileInstruction"])
            self.assertEqual(profile_rows[evidence["profileId"]]["seed"], evidence["seed"])
            self.assertEqual(4, evidence["guidanceScale"])
            self.assertEqual("coordinator_default", evidence["guidanceScaleProvenance"])
            self.assertEqual("neutral", evidence["emotion"])
            self.assertIn("no fixed emotion override", evidence["instruction"])
            self.assertEqual("whisper", evidence["deliveryMode"])
            self.assertEqual("controlled_voice_contrast", evidence["directionProvenance"])
            self.assertEqual(self.delivery_context(), evidence["deliveryContext"])
            self.assertEqual("controlled-voice-contrast", evidence["sampleKind"])
            self.assertEqual(str((root / "model.gguf").resolve()), evidence["modelPath"])
            self.assertIsNone(evidence["modelSizeBytes"], "offline protocol fixture has no model artifact")
            self.assertEqual("whisper", evidence["contrastLabel"])
            self.assertEqual("delivery_context_v1", evidence["personalityProvenance"])
            self.assertIn("fear present but contained and alert, not hesitant",
                          " ".join(evidence["personalityProsody"]))
            self.assertIn("Personal vocal delivery", evidence["instruction"])
            self.assertIn("no fixed emotion override", evidence["instruction"])
            self.assertNotIn("Controlled emotion override", evidence["instruction"])

    def test_opt_in_promotes_first_eligible_female_native_sample_and_saves_exact_base_context(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / "requests").mkdir()
            claimed = root / "working" / "request-reference.claimed.request.json"
            claimed.parent.mkdir()
            context = self.delivery_context(mood=0, fear=0, resentment=0, encounters=2)
            claimed.write_text(json.dumps(self.request_json(requestId="request-reference",
                voiceGender="female", text="A sufficiently long fictional reference line.",
                deliveryContext=context)), encoding="utf-8")
            profile_rows = self.profiles()
            profiles_path = root / "profiles.json"
            profiles_path.write_text(json.dumps({"schemaVersion": 1, "profiles": profile_rows}), encoding="utf-8")
            bindings_path = root / "bindings.json"
            bindings_path.write_text(json.dumps({"schemaVersion": 1,
                "bindings": {"person-001": "profile-00"}}), encoding="utf-8")
            voice_root = root / "voices"

            def synthesize(command, **kwargs):
                wav_path = Path(command[command.index("--out") + 1])
                wav_path.parent.mkdir(parents=True, exist_ok=True)
                with wave.open(str(wav_path), "wb") as output:
                    output.setnchannels(1)
                    output.setsampwidth(2)
                    output.setframerate(22050)
                    output.writeframes(b"\x01\x00" * (22050 * 3))
                return subprocess.CompletedProcess(command, 0, "", "")

            original_promote = coordinator._promote_first_reference

            def assert_terminal_then_promote(*args, **kwargs):
                self.assertTrue((root / "responses" / "request-reference.manifest.json").is_file(),
                                "reference promotion must follow accepted WAV manifest")
                return original_promote(*args, **kwargs)

            with patch.object(coordinator.subprocess, "run", side_effect=synthesize), \
                 patch.object(coordinator, "_promote_first_reference", side_effect=assert_terminal_then_promote):
                result = coordinator.process_claimed(claimed, "request-reference", root,
                    root / "audiocpp_cli.exe", root / "model.gguf", "0" * 64, root / "breeze.json",
                    profiles_path, bindings_path, voice_root, None, "cuda", 10, "test-build",
                    promote_reference_profile="profile-00")

            self.assertEqual("ready", result["status"])
            self.assertEqual(context, json.loads((voice_root / "profile-00.base-context.json").read_text(encoding="utf-8")))
            updated = json.loads(profiles_path.read_text(encoding="utf-8"))["profiles"]["profile-00"]
            self.assertEqual("promoted", json.loads((root / "measurements" / "request-reference.reference-promotion.json").read_text(encoding="utf-8"))["status"])
            ref_path = voice_root / updated["voiceRef"]
            self.assertTrue(ref_path.is_file())
            self.assertEqual(updated["voiceReferenceSha256"], coordinator.sha256_file(ref_path))
            self.assertEqual("A sufficiently long fictional reference line.", updated["referenceText"])
            audit = json.loads((root / "measurements" / "request-reference.reference-promotion.json").read_text(encoding="utf-8"))
            self.assertNotIn("referenceText", audit)
            evidence = json.loads((root / "measurements" / "request-reference.evidence.json").read_text(encoding="utf-8"))
            self.assertIsNone(evidence["voiceReferenceSha256"], "first sample did not use reference it created")

    def test_opt_in_short_greeting_skips_promotion_and_default_stays_off(self):
        parser = coordinator.make_parser()
        self.assertIsNone(parser.parse_args(["--cli", "cli", "--model", "model", "--model-sha256", "0" * 64,
            "--spec", "spec", "--profiles", "profiles", "--bindings", "bindings", "--voice-root", "voices",
            "--root", "root"]).promote_reference_profile)
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / "requests").mkdir()
            claimed = root / "working" / "request-short.claimed.request.json"
            claimed.parent.mkdir()
            claimed.write_text(json.dumps(self.request_json(requestId="request-short", voiceGender="female",
                deliveryContext=self.delivery_context())), encoding="utf-8")
            profile_rows = self.profiles()
            profiles_path = root / "profiles.json"
            profiles_path.write_text(json.dumps({"schemaVersion": 1, "profiles": profile_rows}), encoding="utf-8")
            bindings_path = root / "bindings.json"
            bindings_path.write_text(json.dumps({"schemaVersion": 1,
                "bindings": {"person-001": "profile-00"}}), encoding="utf-8")

            def synthesize(command, **kwargs):
                wav_path = Path(command[command.index("--out") + 1])
                wav_path.parent.mkdir(parents=True, exist_ok=True)
                with wave.open(str(wav_path), "wb") as output:
                    output.setnchannels(1)
                    output.setsampwidth(2)
                    output.setframerate(22050)
                    output.writeframes(b"\x00\x00" * (22050 // 2))
                return subprocess.CompletedProcess(command, 0, "", "")

            with patch.object(coordinator.subprocess, "run", side_effect=synthesize):
                result = coordinator.process_claimed(claimed, "request-short", root,
                    root / "audiocpp_cli.exe", root / "model.gguf", "0" * 64, root / "breeze.json",
                    profiles_path, bindings_path, root / "voices", None, "cuda", 10, "test-build",
                    promote_reference_profile="profile-00")
            self.assertEqual("ready", result["status"])
            audit = json.loads((root / "measurements" / "request-short.reference-promotion.json").read_text(encoding="utf-8"))
            self.assertEqual("skipped", audit["status"])
            self.assertEqual("sample-shorter-than-3-seconds", audit["reason"])
            self.assertNotIn("voiceRef", json.loads(profiles_path.read_text(encoding="utf-8"))["profiles"]["profile-00"])

    def test_cli_uses_real_text_and_keeps_identity_and_direction_separate(self):
        request = coordinator.parse_request(self.request_json())
        direction = coordinator.TrialDirection("grief", "whisper", "controlled_trial")
        command = coordinator.build_cli_command(
            Path("audiocpp_cli.exe"), Path("breeze.gguf"), Path("breeze_tts.json"),
            "cuda", request, {"instruction": "Steady voice.", "seed": 42}, None,
            Path("output.wav"), direction)
        self.assertEqual("Please keep your voice down.", command[command.index("--text") + 1])
        instruction = command[command.index("--instruct") + 1]
        self.assertIn("Stable voice identity:", instruction)
        self.assertIn("Emotional delivery: grief", instruction)
        self.assertIn("Delivery mode: whisper", instruction)
        self.assertEqual("guidance_scale=4", command[command.index("--request-option") + 1])
        self.assertNotIn("--emotion", command)
        self.assertNotIn("--voice-ref", command)

    def test_server_sse_uses_trusted_profile_and_records_warm_stream_metrics(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / "requests").mkdir()
            claimed = root / "working" / "request-server.claimed.request.json"
            claimed.parent.mkdir()
            context = self.delivery_context()
            claimed.write_text(json.dumps(self.request_json(requestId="request-server",
                deliveryContext=context)), encoding="utf-8")
            profile_rows = self.profiles()
            profile = profile_rows["profile-00"]
            voice_root = root / "voices"
            voice_root.mkdir()
            reference = voice_root / "profile-00.wav"
            reference.write_bytes(b"trusted reference")
            import hashlib
            profile.update({"voiceRef": "profile-00.wav", "referenceText": "Trusted transcript.",
                "voiceReferenceSha256": hashlib.sha256(reference.read_bytes()).hexdigest()})
            profiles_path = root / "profiles.json"
            profiles_path.write_text(json.dumps({"schemaVersion": 1, "profiles": profile_rows}), encoding="utf-8")
            bindings_path = root / "bindings.json"
            bindings_path.write_text(json.dumps({"schemaVersion": 1,
                "bindings": {"person-001": "profile-00"}}), encoding="utf-8")

            def synthesize(endpoint, model, text, instruction, reference_wav, reference_text,
                           output_wav, timeout_seconds, *, seed, guidance_scale, cancel_event):
                self.assertEqual("http://127.0.0.1:8094", endpoint)
                self.assertEqual("breeze", model)
                self.assertEqual("Please keep your voice down.", text)
                self.assertIn("Stable voice identity:", instruction)
                self.assertEqual(str(reference), reference_wav)
                self.assertEqual("Trusted transcript.", reference_text)
                self.assertEqual(0, seed)
                self.assertEqual(4.0, guidance_scale)
                self.assertEqual(300, timeout_seconds)
                with wave.open(str(output_wav), "wb") as output:
                    output.setnchannels(1)
                    output.setsampwidth(2)
                    output.setframerate(24000)
                    output.writeframes(b"\x00\x00" * 2400)
                return {"synthesisMs": 321.5, "firstAudioMs": 88.25,
                    "audioDeltaCount": 3, "serverTiming": {"prefill": 12}}

            class RecordingPostprocessor:
                def __init__(self):
                    self.jobs = []

                def submit(self, job):
                    self.jobs.append(job)
                    return "queued"

            postprocessor = RecordingPostprocessor()

            with patch("server_sse_client.synthesize", side_effect=synthesize) as server_call, \
                    patch.object(coordinator.subprocess, "run") as cli_call:
                result = coordinator.process_claimed(claimed, "request-server", root,
                    root / "audiocpp_cli.exe", root / "model.gguf", "0" * 64, root / "breeze.json",
                    profiles_path, bindings_path, voice_root, None, "cuda", 300, "server-sse-test",
                    server_endpoint="http://127.0.0.1:8094", postprocessor=postprocessor)

            self.assertEqual("ready", result["status"])
            server_call.assert_called_once()
            cli_call.assert_not_called()
            evidence = json.loads((root / "measurements" / "request-server.evidence.json").read_text(encoding="utf-8"))
            self.assertEqual("server-sse", evidence["backend"])
            self.assertEqual("audio.cpp-server-sse", evidence["backendKind"])
            self.assertTrue(evidence["warmServerSse"])
            self.assertEqual(321.5, evidence["serverSynthesisMs"])
            self.assertEqual(88.25, evidence["serverFirstAudioMs"])
            self.assertEqual(3, evidence["serverAudioDeltaCount"])
            self.assertEqual({"prefill": 12}, evidence["serverTiming"])
            self.assertEqual(1, len(postprocessor.jobs))
            postprocess_job = postprocessor.jobs[0]
            self.assertEqual("request-server", postprocess_job.stream_id)
            self.assertEqual("Please keep your voice down.", postprocess_job.text)
            self.assertEqual(root / "responses" / "request-server.wav", postprocess_job.audio_path)
            self.assertEqual(root / "requests" / "request-server.cancel.json", postprocess_job.cancel_path)
            self.assertEqual(root / "responses", postprocess_job.output_dir)
            manifest = json.loads((root / "responses" / "request-server.manifest.json").read_text(encoding="utf-8"))
            self.assertEqual({"schemaVersion", "requestId", "conversationToken", "personId", "encoding",
                "wavFile", "sampleRate", "channels", "sampleFrames", "voiceIdentity"}, set(manifest))

    def test_server_endpoint_accepts_loopback_and_rejects_remote_hosts(self):
        self.assertEqual("http://127.0.0.1:8094", coordinator.validate_server_endpoint("http://127.0.0.1:8094"))
        self.assertEqual("http://[::1]:8094", coordinator.validate_server_endpoint("http://[::1]:8094"))
        for endpoint in ("https://127.0.0.1:8094", "http://example.com:8094",
                         "http://127.0.0.1:8094/path", "http://user@127.0.0.1:8094"):
            with self.subTest(endpoint=endpoint), self.assertRaises(coordinator.RequestError):
                coordinator.validate_server_endpoint(endpoint)

    def test_watch_readiness_is_atomic_and_published_after_backend_and_directories_are_ready(self):
        class StopWatch(Exception):
            pass

        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary) / "bridge"
            ready_file = Path(temporary) / "coordinator" / "ready.json"
            model_hash = "a" * 64
            config_id = "b" * 64
            args = coordinator.make_parser().parse_args([
                "--cli", str(Path(temporary) / "audiocpp_cli.exe"),
                "--model", str(Path(temporary) / "model.gguf"),
                "--model-sha256", model_hash,
                "--spec", str(Path(temporary) / "breeze.json"),
                "--profiles", str(Path(temporary) / "profiles.json"),
                "--bindings", str(Path(temporary) / "bindings.json"),
                "--voice-root", str(Path(temporary) / "voices"),
                "--root", str(root), "--watch", "--server-endpoint", "http://127.0.0.1:8094",
                "--ready-file", str(ready_file),
                "--ready-config-id", config_id])

            def verified_assets(*_args):
                self.assertFalse(ready_file.exists())
                self.assertFalse(root.exists())
                return None

            def stop_after_ready(_interval):
                self.assertTrue(all((root / name).is_dir() for name in
                    ("requests", "working", "responses", "measurements")))
                raise StopWatch()

            with patch.object(coordinator, "verify_model_assets", side_effect=verified_assets), \
                    patch.object(coordinator.time, "sleep", side_effect=stop_after_ready):
                with self.assertRaises(StopWatch):
                    coordinator.run(args)

            readiness = json.loads(ready_file.read_text(encoding="utf-8"))
            self.assertEqual({"schemaVersion": 1, "status": "ready", "processId": __import__("os").getpid(),
                "root": str(root.resolve()), "modelSha256": model_hash,
                "serverEndpoint": "http://127.0.0.1:8094",
                "configIdentity": config_id}, readiness)
            self.assertFalse(list(ready_file.parent.glob("*.tmp")))

    def test_readiness_requires_watch_and_sha256_config_identity(self):
        base = ["--cli", "cli", "--model", "model", "--model-sha256", "a" * 64,
            "--spec", "spec", "--profiles", "profiles", "--bindings", "bindings",
            "--voice-root", "voices", "--root", "root", "--ready-file", "ready.json"]
        args = coordinator.make_parser().parse_args(base + ["--watch", "--ready-config-id", "not-a-sha"])
        with self.assertRaises(coordinator.RequestError) as _raised_1415:
            coordinator.run(args)
        self.assertTrue(('64-character SHA-256' in str(_raised_1415.exception)), str(_raised_1415.exception))

        args = coordinator.make_parser().parse_args(base + ["--watch", "--ready-config-id", "a" * 64,
            "--await-native-root"])
        with self.assertRaises(coordinator.RequestError) as _raised_1420:
            coordinator.run(args)
        self.assertTrue(('cannot be combined' in str(_raised_1420.exception)), str(_raised_1420.exception))


if __name__ == "__main__":
    unittest.main(verbosity=2)
