"""Telemetry protocol tests; never launch the game or a model."""

import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import telemetry


class NvidiaParserTests(unittest.TestCase):
    def test_gpu_csv_keeps_aggregate_metrics(self):
        rows = telemetry.parse_gpu_csv("0, 8192, 12288, 34, 57\n1, 4096, 8192, 10, 44\n")
        self.assertEqual("available", rows["status"])
        self.assertEqual(2, len(rows["devices"]))
        self.assertEqual({"index": 0, "memoryUsedMiB": 8192, "memoryTotalMiB": 12288,
                          "utilizationPercent": 34, "temperatureC": 57}, rows["devices"][0])

    def test_gpu_parser_marks_n_a_metrics_as_unknown(self):
        rows = telemetry.parse_gpu_csv("0, N/A, 12288, N/A, 57\n")
        self.assertEqual("available", rows["status"])
        self.assertIsNone(rows["devices"][0]["memoryUsedMiB"])
        self.assertIsNone(rows["devices"][0]["utilizationPercent"])

    def test_per_process_wddm_n_a_is_unavailable_not_zero(self):
        result = telemetry.parse_process_gpu_csv("1234, N/A\n")
        self.assertEqual("unavailable", result["status"])
        self.assertIsNone(result["processes"][0]["memoryUsedMiB"])

    def test_partial_per_process_query_preserves_exact_rows_and_unknown_rows(self):
        result = telemetry.parse_process_gpu_csv("1234, 512\n5678, N/A\n")
        self.assertEqual("partial", result["status"])
        self.assertEqual(512, result["processes"][0]["memoryUsedMiB"])
        self.assertIsNone(result["processes"][1]["memoryUsedMiB"])

    def test_per_process_memory_is_reported_when_nvidia_smi_supports_it(self):
        result = telemetry.parse_process_gpu_csv("1234, 512\n5678, 2048\n")
        self.assertEqual("available", result["status"])
        self.assertEqual([1234, 5678], [row["pid"] for row in result["processes"]])
        self.assertEqual([512, 2048], [row["memoryUsedMiB"] for row in result["processes"]])


class ProcessMemoryTests(unittest.TestCase):
    def test_cim_rows_map_only_requested_processes(self):
        output = json.dumps([
            {"ProcessId": 10, "Name": "DaggerfallUnity.exe", "WorkingSetSize": "1000"},
            {"ProcessId": 20, "Name": "python.exe", "WorkingSetSize": "2000"},
            {"ProcessId": 99, "Name": "unrequested.exe", "WorkingSetSize": "9999"},
        ])
        result = telemetry.parse_cim_processes(output, {10: "dfu", 20: "qwen"})
        self.assertEqual("available", result["status"])
        self.assertEqual(["dfu", "qwen"], [row["role"] for row in result["processes"]])
        self.assertEqual([1000, 2000], [row["rssBytes"] for row in result["processes"]])

    def test_cim_permission_or_exit_failure_is_explicitly_unavailable(self):
        rows = telemetry.parse_cim_processes("[]", {10: "dfu"})
        self.assertEqual("unavailable", rows["status"])


class TimelineTests(unittest.TestCase):
    def test_event_record_has_monotonic_and_utc_time_and_appends_jsonl(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "telemetry.jsonl"
            with patch.object(telemetry.time, "monotonic", return_value=42.5):
                record = telemetry.append_event(path, "text_ready", requestId="req-1")
            self.assertEqual("event", record["recordType"])
            self.assertEqual("text_ready", record["event"])
            self.assertEqual(42.5, record["monotonicSeconds"])
            self.assertTrue(record["wallUtc"].endswith("Z"))
            saved = json.loads(path.read_text(encoding="utf-8"))
            self.assertEqual("req-1", saved["requestId"])

    def test_unknown_event_name_rejected(self):
        with tempfile.TemporaryDirectory() as temp:
            with self.assertRaises(telemetry.TelemetryError):
                telemetry.append_event(Path(temp) / "log.jsonl", "inferred_latency")

    def test_sample_records_owned_game_pid_and_named_rss_without_gpu_fabrication(self):
        with tempfile.TemporaryDirectory() as temp:
            telemetry_collector = telemetry.ResourceTelemetry(
                Path(temp) / "log.jsonl", game_pid=111, process_pids={"qwen": 222})
            with patch.object(telemetry, "query_gpu", return_value={"status": "unavailable", "devices": []}), \
                    patch.object(telemetry, "query_process_gpu", return_value={"status": "unavailable", "processes": []}), \
                    patch.object(telemetry, "query_process_rss", return_value={
                        "status": "available", "processes": [
                            {"role": "dfu", "pid": 111, "name": "DaggerfallUnity.exe", "rssBytes": 1000},
                            {"role": "qwen", "pid": 222, "name": "python.exe", "rssBytes": 2000},
                        ]}), \
                    patch.object(telemetry.time, "monotonic", return_value=9.0):
                row = telemetry_collector.sample()
            self.assertEqual(111, row["gamePid"])
            self.assertEqual(9.0, row["monotonicSeconds"])
            self.assertEqual("unavailable", row["gpu"]["processMemory"]["status"])
            self.assertEqual(2, len(row["processes"]))


if __name__ == "__main__":
    unittest.main(verbosity=2)
