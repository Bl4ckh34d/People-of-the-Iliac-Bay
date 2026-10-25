import unittest

try:
    from tools.BreezeTtsTrial.model_cycle import validate_record
except ModuleNotFoundError:
    from model_cycle import validate_record


def valid_record():
    qwen_args = ["-m", r"C:\models\dialogue.gguf", "-ngl", "99", "-c", "4096",
                 "--host", "127.0.0.1", "--port", "8080", "-np", "1"]
    return {
        "schemaVersion": 1,
        "activeModelPath": r"C:\models\dialogue.gguf",
        "activeModelConfigSha256Before": "a" * 64,
        "activeModelConfigSha256After": "a" * 64,
        "expectedQwenExecutable": r"C:\local-llm\bin\llama-server.exe",
        "expectedD3Executable": r"C:\decision\llama-server.exe",
        "expectedD3Pid": 202,
        "expectedDfuExecutable": r"C:\Games\Daggerfall Unity\DaggerfallUnity.exe",
        "expectedDfuPid": 303,
        "expectedTtsExecutable": r"C:\local-tts\audiocpp_cli.exe",
        "expectedTtsModelPath": r"C:\models\breeze.gguf",
        "expectedTtsModelSha256": "c" * 64,
        "before": {
            "qwen": {"pid": 101, "executable": r"C:\local-llm\bin\llama-server.exe",
                     "arguments": qwen_args, "modelPath": r"C:\models\dialogue.gguf",
                     "port": 8080, "contextSize": 4096, "listenerPid": 101,
                     "listenerAddresses": ["127.0.0.1"], "createdUtc": "2026-10-21T09:00:00Z", "running": True},
            "d3": {"pid": 202, "executable": r"C:\decision\llama-server.exe", "createdUtc": "2026-10-21T08:00:00Z", "running": True},
            "dfu": {"pid": 303, "executable": r"C:\Games\Daggerfall Unity\DaggerfallUnity.exe", "createdUtc": "2026-10-21T07:00:00Z", "running": True},
        },
        "ttsRuns": [
            {"process": {"pid": 404, "executable": r"C:\local-tts\audiocpp_cli.exe",
                         "modelPath": r"C:\models\breeze.gguf", "createdUtc": "2026-10-21T09:01:00Z"},
             "cacheCondition": "cold", "processStarted": 3.2, "processReady": 3.25,
             "modelLoadStarted": 3.3, "modelReady": 8.3, "synthesisCompleted": 10.0,
             "modelSha256": "c" * 64, "released": 10.2, "processStartupSeconds": 0.05, "modelLoadSeconds": 5.0},
            {"process": {"pid": 405, "executable": r"C:\local-tts\audiocpp_cli.exe",
                         "modelPath": r"C:\models\breeze.gguf", "createdUtc": "2026-10-21T09:02:00Z"},
             "cacheCondition": "warm-file-cache-new-process", "processStarted": 10.25,
             "processReady": 10.29, "modelLoadStarted": 10.3, "modelReady": 14.3,
             "synthesisCompleted": 15.0, "released": 15.2,
             "modelSha256": "c" * 64, "processStartupSeconds": 0.04, "modelLoadSeconds": 4.0},
        ],
        "after": {
            "qwen": {"pid": 105, "executable": r"C:\local-llm\bin\llama-server.exe",
                     "arguments": qwen_args, "modelPath": r"C:\models\dialogue.gguf",
                     "port": 8080, "contextSize": 4096, "listenerPid": 105,
                     "listenerAddresses": ["127.0.0.1"], "createdUtc": "2026-10-21T09:02:00Z", "running": True},
            "d3": {"pid": 202, "executable": r"C:\decision\llama-server.exe", "createdUtc": "2026-10-21T08:00:00Z", "running": True},
            "dfu": {"pid": 303, "executable": r"C:\Games\Daggerfall Unity\DaggerfallUnity.exe", "createdUtc": "2026-10-21T07:00:00Z", "running": True},
        },
        "gates": {
            "finalizedTextReady": True, "dispatchIdle": True,
            "timesSeconds": {"textFinalized": 1.0, "dispatchIdle": 1.2,
                             "qwenStopped": 2.0, "gpuFreeObserved": 3.0,
                             "audioReady": 15.25, "playbackStarted": 15.3,
                             "firstAudible": 15.4, "qwenStarted": 15.5,
                             "qwenReady": 19.5, "playbackEnded": 20.0},
        },
        "stoppedProcesses": [
            {"pid": 101, "role": "qwen", "createdUtc": "2026-10-21T09:00:00Z"},
            {"pid": 404, "role": "tts", "executable": r"C:\local-tts\audiocpp_cli.exe", "createdUtc": "2026-10-21T09:01:00Z"},
            {"pid": 405, "role": "tts", "executable": r"C:\local-tts\audiocpp_cli.exe", "createdUtc": "2026-10-21T09:02:00Z"},
        ],
        "gpuSamples": [
            {"atSeconds": 1.0, "label": "baseline", "freeMiB": 600},
            {"atSeconds": 3.0, "label": "after-qwen-stop", "freeMiB": 8000},
            {"atSeconds": 8.3, "label": "cold-tts-model-ready", "freeMiB": 1000},
            {"atSeconds": 10.2, "label": "after-cold-tts-release", "freeMiB": 8000},
            {"atSeconds": 14.3, "label": "warm-tts-model-ready", "freeMiB": 1000},
            {"atSeconds": 15.2, "label": "after-warm-tts-release", "freeMiB": 8000},
            {"atSeconds": 19.5, "label": "after-qwen-ready", "freeMiB": 1000},
        ],
        "loadMeasurements": {
            "qwenReloadSeconds": 4.0,
            "memoryResidencyClaim": "not-measured",
        },
    }


class ModelCycleTests(unittest.TestCase):
    def test_accepts_safe_qwen_tts_cycle_while_d3_and_dfu_stay_live(self):
        result = validate_record(valid_record())
        self.assertEqual(5.0, result["coldTtsModelLoadSeconds"])
        self.assertEqual(4.0, result["warmFileCacheTtsModelLoadSeconds"])
        self.assertEqual(4.0, result["qwenReloadSeconds"])
        self.assertTrue(result["qwenReloadDuringPlayback"])

    def test_rejects_wrong_qwen_listener_or_model_before_stop(self):
        record = valid_record()
        record["before"]["qwen"]["listenerPid"] = 999
        with self.assertRaises(ValueError) as _raised_94:
            validate_record(record)
        self.assertTrue(('listener' in str(_raised_94.exception)), str(_raised_94.exception))

        record = valid_record()
        record["before"]["qwen"]["modelPath"] = r"C:\models\other.gguf"
        with self.assertRaises(ValueError) as _raised_99:
            validate_record(record)
        self.assertTrue(('active model' in str(_raised_99.exception)), str(_raised_99.exception))

    def test_rejects_active_dispatch_before_qwen_stop(self):
        record = valid_record()
        record["gates"]["dispatchIdle"] = False
        with self.assertRaises(ValueError) as _raised_105:
            validate_record(record)
        self.assertTrue(('dispatch' in str(_raised_105.exception)), str(_raised_105.exception))

    def test_rejects_stopped_or_replaced_d3_and_dfu_processes(self):
        for role in ("d3", "dfu"):
            record = valid_record()
            record["after"][role]["pid"] += 1
            with self.subTest(role=role), self.assertRaises(ValueError) as _raised_112:
                validate_record(record)
            self.assertTrue(role in str(_raised_112.exception), str(_raised_112.exception))

    def test_rejects_changed_process_creation_identity_or_nonloopback_listener(self):
        record = valid_record()
        record["after"]["dfu"]["createdUtc"] = "2026-10-21T08:00:00Z"
        with self.assertRaises(ValueError) as _raised_118:
            validate_record(record)
        self.assertTrue(('createdUtc' in str(_raised_118.exception)), str(_raised_118.exception))

        record = valid_record()
        record["before"]["qwen"]["listenerAddresses"] = ["0.0.0.0"]
        with self.assertRaises(ValueError) as _raised_123:
            validate_record(record)
        self.assertTrue(('loopback' in str(_raised_123.exception)), str(_raised_123.exception))

    def test_rejects_any_stop_outside_exact_qwen_and_trial_tts_pids(self):
        record = valid_record()
        record["stoppedProcesses"].append({"pid": 999, "role": "unknown"})
        with self.assertRaises(ValueError) as _raised_129:
            validate_record(record)
        self.assertTrue(('unapproved process' in str(_raised_129.exception)), str(_raised_129.exception))

    def test_rejects_qwen_reload_with_changed_active_configuration(self):
        record = valid_record()
        record["after"]["qwen"]["contextSize"] = 8192
        with self.assertRaises(ValueError) as _raised_135:
            validate_record(record)
        self.assertTrue(('context 4096' in str(_raised_135.exception)), str(_raised_135.exception))

        record = valid_record()
        record["activeModelConfigSha256After"] = "b" * 64
        with self.assertRaises(ValueError) as _raised_140:
            validate_record(record)
        self.assertTrue(('config was not restored' in str(_raised_140.exception)), str(_raised_140.exception))

    def test_requires_distinct_cold_and_warm_file_cache_tts_processes(self):
        record = valid_record()
        record["ttsRuns"][1]["process"]["pid"] = record["ttsRuns"][0]["process"]["pid"]
        with self.assertRaises(ValueError) as _raised_146:
            validate_record(record)
        self.assertTrue(('distinct process' in str(_raised_146.exception)), str(_raised_146.exception))

        record = valid_record()
        record["ttsRuns"][1]["cacheCondition"] = "warm-model-resident"
        with self.assertRaises(ValueError) as _raised_151:
            validate_record(record)
        self.assertTrue(('warm-file-cache' in str(_raised_151.exception)), str(_raised_151.exception))

        record = valid_record()
        record["ttsRuns"][1]["modelSha256"] = "d" * 64
        with self.assertRaises(ValueError) as _raised_156:
            validate_record(record)
        self.assertTrue(('TTS model hash' in str(_raised_156.exception)), str(_raised_156.exception))

    def test_rejects_false_ram_residency_claim(self):
        record = valid_record()
        record["loadMeasurements"]["memoryResidencyClaim"] = "retained-in-ram"
        with self.assertRaises(ValueError) as _raised_162:
            validate_record(record)
        self.assertTrue(('residency' in str(_raised_162.exception)), str(_raised_162.exception))

    def test_rejects_nonmonotonic_switch_timing(self):
        record = valid_record()
        record["gates"]["timesSeconds"]["qwenStarted"] = 8.0
        with self.assertRaises(ValueError) as _raised_168:
            validate_record(record)
        self.assertTrue(('timing order' in str(_raised_168.exception)), str(_raised_168.exception))


if __name__ == "__main__":
    unittest.main()
