#!/usr/bin/env python3
"""Passive resource sampling for the Breeze TTS real-game trial.

This module reads GPU/process counters and appends JSONL records. It never
starts, stops, signals, or reconfigures the game or model processes.
"""

from __future__ import annotations

import argparse
import csv
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import subprocess
import sys
import time
from typing import Any, Dict, Iterable, List, Mapping, Optional


EVENT_NAMES = frozenset({"text_ready", "synthesis_started", "synthesis_finished", "model_reloaded"})
PROCESS_ROLES = frozenset({"qwen", "d3", "tts"})


class TelemetryError(ValueError):
    """Invalid telemetry configuration or event marker."""


def _number(value: str) -> Optional[int]:
    value = value.strip()
    if not value or value.upper() in {"N/A", "NA", "NOT SUPPORTED", "[N/A]"}:
        return None
    try:
        return int(value)
    except ValueError as error:
        raise TelemetryError("nvidia-smi returned a non-integer counter") from error


def _csv_rows(output: str, width: int) -> List[List[str]]:
    try:
        rows = [row for row in csv.reader(output.splitlines(), skipinitialspace=True) if row]
    except csv.Error as error:
        raise TelemetryError("nvidia-smi returned invalid CSV") from error
    if any(len(row) != width for row in rows):
        raise TelemetryError("nvidia-smi returned an unexpected CSV column count")
    return rows


def parse_gpu_csv(output: str) -> Dict[str, Any]:
    """Parse `index,memory.used,memory.total,utilization.gpu,temperature.gpu`."""
    devices = []
    for index, used, total, utilization, temperature in _csv_rows(output, 5):
        index_number = _number(index)
        if index_number is None:
            raise TelemetryError("nvidia-smi returned an unavailable GPU index")
        devices.append({
            "index": index_number,
            "memoryUsedMiB": _number(used),
            "memoryTotalMiB": _number(total),
            "utilizationPercent": _number(utilization),
            "temperatureC": _number(temperature),
        })
    return {"status": "available" if devices else "unavailable", "devices": devices}


def parse_process_gpu_csv(output: str) -> Dict[str, Any]:
    """Parse per-process VRAM, preserving WDDM `N/A` as unavailable."""
    processes = []
    unavailable_count = 0
    for pid, memory in _csv_rows(output, 2):
        pid_number = _number(pid)
        memory_number = _number(memory)
        if pid_number is None:
            raise TelemetryError("nvidia-smi returned an unavailable process ID")
        if memory_number is None:
            unavailable_count += 1
        processes.append({"pid": pid_number, "memoryUsedMiB": memory_number})
    if not processes:
        status = "unavailable"
    elif unavailable_count == len(processes):
        status = "unavailable"
    elif unavailable_count:
        status = "partial"
    else:
        status = "available"
    return {"status": status, "processes": processes}


def parse_cim_processes(output: str, requested: Mapping[int, str]) -> Dict[str, Any]:
    """Parse a PowerShell/CIM JSON response for explicitly requested PIDs."""
    expected = []
    for pid, role in requested.items():
        expected.append({"role": role, "pid": int(pid), "name": None, "rssBytes": None})
    by_pid = {row["pid"]: row for row in expected}
    try:
        parsed = json.loads(output) if output.strip() else []
    except (TypeError, json.JSONDecodeError):
        return {"status": "unavailable", "processes": expected}
    if isinstance(parsed, dict):
        parsed = [parsed]
    if not isinstance(parsed, list):
        return {"status": "unavailable", "processes": expected}
    for item in parsed:
        if not isinstance(item, dict):
            continue
        try:
            pid = int(item.get("ProcessId"))
            rss = int(item.get("WorkingSetSize"))
        except (TypeError, ValueError):
            continue
        row = by_pid.get(pid)
        if row is not None and rss >= 0:
            row["name"] = item.get("Name") if isinstance(item.get("Name"), str) else None
            row["rssBytes"] = rss
    available = sum(row["rssBytes"] is not None for row in expected)
    if available == len(expected) and expected:
        status = "available"
    elif available:
        status = "partial"
    else:
        status = "unavailable"
    return {"status": status, "processes": expected}


def _run_capture(command: List[str], timeout: float = 3.0) -> subprocess.CompletedProcess:
    return subprocess.run(command, capture_output=True, text=True, timeout=timeout, check=False)


def query_gpu(runner=_run_capture) -> Dict[str, Any]:
    command = ["nvidia-smi", "--query-gpu=index,memory.used,memory.total,utilization.gpu,temperature.gpu",
               "--format=csv,noheader,nounits"]
    try:
        result = runner(command)
        if result.returncode != 0:
            return {"status": "unavailable", "devices": [], "reason": "nvidia-smi query failed"}
        return parse_gpu_csv(result.stdout)
    except (OSError, subprocess.SubprocessError, TelemetryError) as error:
        return {"status": "unavailable", "devices": [], "reason": str(error)}


def query_process_gpu(runner=_run_capture) -> Dict[str, Any]:
    command = ["nvidia-smi", "--query-compute-apps=pid,used_memory", "--format=csv,noheader,nounits"]
    try:
        result = runner(command)
        if result.returncode != 0:
            return {"status": "unavailable", "processes": [], "reason": "nvidia-smi process query failed"}
        parsed = parse_process_gpu_csv(result.stdout)
        if parsed["status"] == "unavailable" and parsed["processes"]:
            parsed["reason"] = "per-process GPU memory unavailable (often WDDM N/A)"
        return parsed
    except (OSError, subprocess.SubprocessError, TelemetryError) as error:
        return {"status": "unavailable", "processes": [], "reason": str(error)}


def query_process_rss(requested: Mapping[int, str], runner=_run_capture) -> Dict[str, Any]:
    if not requested:
        return {"status": "unavailable", "processes": [], "reason": "no named process PIDs supplied"}
    if os.name != "nt":
        return {"status": "unavailable", "processes": [
            {"role": role, "pid": int(pid), "name": None, "rssBytes": None} for pid, role in requested.items()
        ], "reason": "Windows CIM is unavailable on this platform"}
    pids = sorted(int(pid) for pid in requested)
    filter_value = " OR ".join("ProcessId = {}".format(pid) for pid in pids)
    script = "$ErrorActionPreference='Stop'; Get-CimInstance Win32_Process -Filter '{}' | " \
             "Select-Object ProcessId,Name,WorkingSetSize | ConvertTo-Json -Compress".format(filter_value)
    try:
        result = runner(["powershell.exe", "-NoProfile", "-NonInteractive", "-Command", script], timeout=4.0)
        if result.returncode != 0:
            parsed = parse_cim_processes("", requested)
            parsed["reason"] = "PowerShell/CIM query failed"
            return parsed
        return parse_cim_processes(result.stdout, requested)
    except (OSError, subprocess.SubprocessError) as error:
        parsed = parse_cim_processes("", requested)
        parsed["reason"] = str(error)
        return parsed


def _timestamp_pair() -> Dict[str, Any]:
    wall_utc = datetime.now(timezone.utc).isoformat(timespec="milliseconds").replace("+00:00", "Z")
    return {"wallUtc": wall_utc, "monotonicSeconds": time.monotonic()}


def append_jsonl(path: Path, record: Mapping[str, Any]) -> None:
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    encoded = (json.dumps(record, ensure_ascii=False, separators=(",", ":")) + "\n").encode("utf-8")
    descriptor = os.open(str(path), os.O_CREAT | os.O_APPEND | os.O_WRONLY, 0o666)
    try:
        written = os.write(descriptor, encoded)
        if written != len(encoded):
            raise OSError("short JSONL write")
    finally:
        os.close(descriptor)


def append_event(path: Path, event: str, **fields: Any) -> Dict[str, Any]:
    """Append a marker from the actual event callback, using its call-site time."""
    if event not in EVENT_NAMES:
        raise TelemetryError("unsupported telemetry event name")
    if any(key in fields for key in ("schemaVersion", "recordType", "event", "wallUtc", "monotonicSeconds")):
        raise TelemetryError("event fields cannot replace telemetry envelope fields")
    record = {"schemaVersion": 1, "recordType": "event", "event": event, **_timestamp_pair(), **fields}
    append_jsonl(path, record)
    return record


class ResourceTelemetry:
    def __init__(self, output: Path, game_pid: int, process_pids: Optional[Mapping[str, int]] = None,
                 gpu_query=query_gpu, process_gpu_query=query_process_gpu,
                 rss_query=query_process_rss) -> None:
        if isinstance(game_pid, bool) or not isinstance(game_pid, int) or game_pid <= 0:
            raise TelemetryError("game PID must be a positive integer")
        process_pids = process_pids or {}
        normalized = {"dfu": game_pid}
        for role, pid in process_pids.items():
            if role not in PROCESS_ROLES:
                raise TelemetryError("named process role must be qwen, d3, or tts")
            if isinstance(pid, bool) or not isinstance(pid, int) or pid <= 0:
                raise TelemetryError("named process PID must be a positive integer")
            normalized[role] = pid
        if len(set(normalized.values())) != len(normalized):
            raise TelemetryError("each process role must use a distinct PID")
        self.output = Path(output)
        self.game_pid = game_pid
        self.requested_pids = {pid: role for role, pid in normalized.items()}
        self.gpu_query = gpu_query
        self.process_gpu_query = process_gpu_query
        self.rss_query = rss_query

    def sample(self) -> Dict[str, Any]:
        record = {
            "schemaVersion": 1,
            "recordType": "resource_sample",
            **_timestamp_pair(),
            "gamePid": self.game_pid,
            "gpu": {
                "aggregate": self.gpu_query(),
                "processMemory": self.process_gpu_query(),
            },
            "processes": self.rss_query(self.requested_pids),
        }
        append_jsonl(self.output, record)
        return record


def _parse_pid(value: str) -> int:
    if not (bool(value) and value.isascii() and value.isdecimal() and value[0] != chr(48)):
        raise argparse.ArgumentTypeError("PID must be a positive integer")
    return int(value)


def _parse_role_pid(value: str) -> tuple[str, int]:
    if "=" not in value:
        raise argparse.ArgumentTypeError("process PID must use role=PID")
    role, raw_pid = value.split("=", 1)
    if role not in PROCESS_ROLES:
        raise argparse.ArgumentTypeError("process role must be qwen, d3, or tts")
    try:
        pid = _parse_pid(raw_pid)
    except argparse.ArgumentTypeError as error:
        raise argparse.ArgumentTypeError(str(error))
    return role, pid


def main(argv: Optional[Iterable[str]] = None) -> int:
    parser = argparse.ArgumentParser(description="Passively sample GPU and named process resources to JSONL.")
    parser.add_argument("--game-pid", required=True, type=_parse_pid, help="owned Daggerfall Unity process ID")
    parser.add_argument("--process-pid", action="append", default=[], type=_parse_role_pid,
                        metavar="ROLE=PID", help="optional qwen, d3, or tts process; repeat as needed")
    parser.add_argument("--output", required=True, type=Path, help="append-only JSONL output path")
    parser.add_argument("--interval", type=float, default=1.0, help="sample interval in seconds (default: 1)")
    args = parser.parse_args(argv)
    if not (0.1 <= args.interval <= 3600.0):
        parser.error("--interval must be between 0.1 and 3600 seconds")
    process_pids: Dict[str, int] = {}
    for role, pid in args.process_pid:
        if role in process_pids:
            parser.error("each --process-pid role may appear only once")
        process_pids[role] = pid
    try:
        monitor = ResourceTelemetry(args.output, args.game_pid, process_pids)
        while True:
            monitor.sample()
            time.sleep(args.interval)
    except KeyboardInterrupt:
        return 0
    except (TelemetryError, OSError) as error:
        print("telemetry: {}".format(error), file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
