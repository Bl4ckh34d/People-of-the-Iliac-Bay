"""Offline validator for manually captured DFU/Qwen/D3/Breeze switch records.

This module never enumerates, starts, or stops processes. It validates JSON supplied
by the operator after independent read-only process/listener inspection.
"""

import argparse
import json
import math
import ntpath
from pathlib import Path


TIME_ORDER = (
    "textFinalized",
    "dispatchIdle",
    "qwenStopped",
    "gpuFreeObserved",
    "audioReady",
    "playbackStarted",
    "firstAudible",
    "qwenStarted",
    "qwenReady",
)


def _fail(message):
    raise ValueError(message)


def _mapping(value, name):
    if not isinstance(value, dict):
        _fail(f"{name} must be an object")
    return value


def _pid(process, name):
    value = _mapping(process, name).get("pid")
    if isinstance(value, bool) or not isinstance(value, int) or value <= 0:
        _fail(f"{name} needs a positive PID")
    return value


def _same_path(left, right):
    if not isinstance(left, str) or not isinstance(right, str):
        return False
    return ntpath.normcase(ntpath.normpath(left)) == ntpath.normcase(ntpath.normpath(right))


def _require_process(process, name, expected_executable, expected_pid=None):
    value = _mapping(process, name)
    pid = _pid(value, name)
    if expected_pid is not None and pid != expected_pid:
        _fail(f"{name} PID changed")
    if value.get("running") is not True:
        _fail(f"{name} is not recorded as running")
    if not _same_path(value.get("executable"), expected_executable):
        _fail(f"{name} executable mismatch")
    created = value.get("createdUtc")
    if not isinstance(created, str) or not created.strip():
        _fail(f"{name} needs exact createdUtc identity")
    return value


def _arg_value(arguments, flag, name):
    if not isinstance(arguments, list) or any(not isinstance(item, str) for item in arguments):
        _fail(f"{name} arguments must be a string list")
    indexes = [index for index, item in enumerate(arguments) if item.lower() == flag.lower()]
    if len(indexes) != 1 or indexes[0] + 1 >= len(arguments):
        _fail(f"{name} must contain one {flag} argument")
    return arguments[indexes[0] + 1]


def _validate_qwen(process, name, expected_executable, active_model, expected_pid=None):
    value = _require_process(process, name, expected_executable, expected_pid)
    if not _same_path(value.get("modelPath"), active_model):
        _fail(f"{name} active model mismatch")
    if isinstance(value.get("port"), bool) or value.get("port") != 8080:
        _fail(f"{name} must use port 8080")
    if isinstance(value.get("contextSize"), bool) or value.get("contextSize") != 4096:
        _fail(f"{name} must use context 4096")
    if value.get("listenerPid") != value["pid"]:
        _fail(f"{name} listener PID does not match validated process PID")
    if value.get("listenerAddresses") != ["127.0.0.1"]:
        _fail(f"{name} must have one loopback listener only")
    arguments = value.get("arguments")
    if not _same_path(_arg_value(arguments, "-m", name), active_model):
        _fail(f"{name} command line active model mismatch")
    if _arg_value(arguments, "--port", name) != "8080":
        _fail(f"{name} command line port mismatch")
    if _arg_value(arguments, "-c", name) != "4096":
        _fail(f"{name} command line context mismatch")
    if _arg_value(arguments, "--host", name) != "127.0.0.1":
        _fail(f"{name} must bind loopback only")
    if _arg_value(arguments, "-np", name) != "1":
        _fail(f"{name} must use one slot")
    return value


def _finite_time(times, name):
    value = times.get(name)
    if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value) or value < 0:
        _fail(f"{name} must be a finite nonnegative monotonic timestamp")
    return float(value)


def validate_record(record):
    """Validate a process-free observation record. Return measured switch timings."""
    _mapping(record, "record")
    if record.get("schemaVersion") != 1:
        _fail("unsupported record schemaVersion")

    active_model = record.get("activeModelPath")
    expected_qwen = record.get("expectedQwenExecutable")
    expected_d3 = record.get("expectedD3Executable")
    expected_d3_pid = record.get("expectedD3Pid")
    expected_dfu = record.get("expectedDfuExecutable")
    expected_dfu_pid = record.get("expectedDfuPid")
    expected_tts = record.get("expectedTtsExecutable")
    expected_tts_model = record.get("expectedTtsModelPath")
    expected_tts_hash = record.get("expectedTtsModelSha256")
    if not all(isinstance(value, str) and value.strip() for value in
               (active_model, expected_qwen, expected_d3, expected_dfu, expected_tts, expected_tts_model)):
        _fail("active model, expected TTS model and executable paths are required")
    if not isinstance(expected_tts_hash, str) or len(expected_tts_hash) != 64 or any(c not in "0123456789abcdefABCDEF" for c in expected_tts_hash):
        _fail("expectedTtsModelSha256 must be a verified SHA-256 hex digest")
    for value, label in ((expected_d3_pid, "expectedD3Pid"), (expected_dfu_pid, "expectedDfuPid")):
        if isinstance(value, bool) or not isinstance(value, int) or value <= 0:
            _fail(f"{label} must be a positive PID")
    if ntpath.basename(expected_tts).lower() != "audiocpp_cli.exe":
        _fail("expected TTS executable must be the inspected audio.cpp CLI")

    before = _mapping(record.get("before"), "before")
    after = _mapping(record.get("after"), "after")
    before_config_hash = record.get("activeModelConfigSha256Before")
    after_config_hash = record.get("activeModelConfigSha256After")
    for value, label in ((before_config_hash, "activeModelConfigSha256Before"), (after_config_hash, "activeModelConfigSha256After")):
        if value != "absent" and (not isinstance(value, str) or len(value) != 64 or any(c not in "0123456789abcdefABCDEF" for c in value)):
            _fail(f"{label} must be a SHA-256 hex digest or absent")
    if before_config_hash != after_config_hash:
        _fail("active model config was not restored exactly")
    qwen_before = _validate_qwen(before.get("qwen"), "before.qwen", expected_qwen, active_model)
    qwen_after = _validate_qwen(after.get("qwen"), "after.qwen", expected_qwen, active_model)
    if qwen_after["pid"] == qwen_before["pid"]:
        _fail("Qwen restart must have a new PID after the validated stop")
    if qwen_after["createdUtc"] == qwen_before["createdUtc"]:
        _fail("Qwen restart must have a new process creation time")

    for role, expected_executable, expected_pid in (("d3", expected_d3, expected_d3_pid), ("dfu", expected_dfu, expected_dfu_pid)):
        first = _mapping(before.get(role), f"before.{role}")
        last = _mapping(after.get(role), f"after.{role}")
        first_pid = _pid(first, f"before.{role}")
        if first_pid != expected_pid:
            _fail(f"before.{role} PID does not match the independently verified expected PID")
        _require_process(first, f"before.{role}", expected_executable, expected_pid)
        after_process = _require_process(last, f"after.{role}", expected_executable, expected_pid)
        if after_process["createdUtc"] != first["createdUtc"]:
            _fail(f"after.{role} createdUtc changed")

    runs = record.get("ttsRuns")
    if not isinstance(runs, list) or len(runs) != 2:
        _fail("record cold and warm-file-cache TTS runs separately")
    expected_conditions = ["cold", "warm-file-cache-new-process"]
    tts_pids = set()
    tts_model = None
    run_times = []
    for index, (run, expected_condition) in enumerate(zip(runs, expected_conditions)):
        run = _mapping(run, f"ttsRuns[{index}]")
        if run.get("cacheCondition") != expected_condition:
            _fail("TTS runs must distinguish cold load from warm-file-cache new-process load")
        process = _mapping(run.get("process"), f"ttsRuns[{index}].process")
        pid = _pid(process, f"ttsRuns[{index}].process")
        if pid in tts_pids:
            _fail("each TTS load measurement must use a distinct process")
        tts_pids.add(pid)
        if not _same_path(process.get("executable"), expected_tts):
            _fail("TTS process executable mismatch")
        model_path = process.get("modelPath")
        if not _same_path(model_path, expected_tts_model):
            _fail("TTS model path mismatch")
        if run.get("modelSha256") != expected_tts_hash:
            _fail("TTS model hash does not match verified expected model hash")
        if tts_model is None:
            tts_model = model_path
        elif not _same_path(model_path, tts_model):
            _fail("cold and warm TTS runs must use the same model path")
        if not isinstance(process.get("createdUtc"), str) or not process["createdUtc"].strip():
            _fail(f"ttsRuns[{index}] process needs exact createdUtc identity")
        times = {name: _finite_time(run, name) for name in
                 ("processStarted", "processReady", "modelLoadStarted", "modelReady", "synthesisCompleted", "released")}
        ordered = ("processStarted", "processReady", "modelLoadStarted", "modelReady", "synthesisCompleted", "released")
        if any(times[ordered[i]] > times[ordered[i + 1]] for i in range(len(ordered) - 1)):
            _fail(f"ttsRuns[{index}] timing order is invalid")
        process_duration = run.get("processStartupSeconds")
        model_duration = run.get("modelLoadSeconds")
        if any(isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value) or value < 0
               for value in (process_duration, model_duration)):
            _fail(f"ttsRuns[{index}] load durations must be finite and nonnegative")
        if abs(process_duration - (times["processReady"] - times["processStarted"])) > 0.01:
            _fail(f"ttsRuns[{index}] process startup duration does not match timestamps")
        if abs(model_duration - (times["modelReady"] - times["modelLoadStarted"])) > 0.01:
            _fail(f"ttsRuns[{index}] model load duration does not match timestamps")
        run_times.append(times)
    if run_times[1]["processStarted"] < run_times[0]["released"]:
        _fail("warm-file-cache TTS process must start after cold process release")

    stopped = record.get("stoppedProcesses")
    if not isinstance(stopped, list):
        _fail("stoppedProcesses must be a list")
    stopped_by_pid = {}
    for item in stopped:
        item = _mapping(item, "stoppedProcesses entry")
        pid = _pid(item, "stoppedProcesses entry")
        if pid in stopped_by_pid:
            _fail("duplicate PID in stoppedProcesses")
        stopped_by_pid[pid] = item
    expected_stopped = {qwen_before["pid"]} | tts_pids
    if set(stopped_by_pid) != expected_stopped:
        _fail("unapproved process stop: only exact Qwen PID and recorded audio.cpp trial PIDs may stop")
    if stopped_by_pid[qwen_before["pid"]].get("role") != "qwen":
        _fail("Qwen stop record has wrong role")
    if stopped_by_pid[qwen_before["pid"]].get("createdUtc") != qwen_before["createdUtc"]:
        _fail("Qwen stop record process creation identity mismatch")
    for run in runs:
        process = run["process"]
        pid = process["pid"]
        tts_stop = stopped_by_pid[pid]
        if tts_stop.get("role") != "tts" or not _same_path(tts_stop.get("executable"), expected_tts):
            _fail("TTS stop record does not match exact trial child")
        if tts_stop.get("createdUtc") != process["createdUtc"]:
            _fail("TTS stop record process creation identity mismatch")
        if pid in {qwen_before["pid"], qwen_after["pid"], _pid(before["d3"], "before.d3"), _pid(before["dfu"], "before.dfu")}:
            _fail("trial TTS PID collides with protected process PID")

    gates = _mapping(record.get("gates"), "gates")
    if gates.get("finalizedTextReady") is not True:
        _fail("finalized TTS text must be ready before switching")
    if gates.get("dispatchIdle") is not True:
        _fail("Qwen dispatch must be idle before switching")
    times = _mapping(gates.get("timesSeconds"), "gates.timesSeconds")
    values = {name: _finite_time(times, name) for name in TIME_ORDER}
    if any(values[TIME_ORDER[i]] > values[TIME_ORDER[i + 1]] for i in range(len(TIME_ORDER) - 1)):
        _fail("model-cycle timing order is invalid")
    playback_end = _finite_time(times, "playbackEnded")
    if playback_end < values["firstAudible"]:
        _fail("playback end must follow first audible sample")
    if values["qwenStopped"] < values["dispatchIdle"] or values["gpuFreeObserved"] < values["qwenStopped"]:
        _fail("model-cycle timing order is invalid before TTS launch")
    if values["gpuFreeObserved"] > run_times[0]["processStarted"] or run_times[-1]["released"] > values["audioReady"]:
        _fail("model-cycle timing order is invalid around TTS and Qwen")
    if values["audioReady"] < run_times[0]["synthesisCompleted"] or values["audioReady"] < run_times[-1]["synthesisCompleted"]:
        _fail("audio must be ready only after recorded synthesis completes")

    loads = _mapping(record.get("loadMeasurements"), "loadMeasurements")
    for metric in ("qwenReloadSeconds",):
        value = loads.get(metric)
        if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value) or value < 0:
            _fail(f"{metric} must be a finite nonnegative duration")
    if abs(loads["qwenReloadSeconds"] - (values["qwenReady"] - values["qwenStarted"])) > 0.01:
        _fail("Qwen reload duration does not match timestamps")
    if loads.get("memoryResidencyClaim") != "not-measured":
        _fail("system-RAM model residency is not measured; record not-measured")
    if record.get("gpuSamples") is None or not isinstance(record["gpuSamples"], list) or not record["gpuSamples"]:
        _fail("GPU free-memory samples and timestamps are required")
    required_gpu_labels = {"baseline", "after-qwen-stop", "cold-tts-model-ready", "after-cold-tts-release",
                           "warm-tts-model-ready", "after-warm-tts-release", "after-qwen-ready"}
    gpu_labels = set()
    gpu_samples_by_label = {}
    last_gpu_time = -1.0
    for sample in record["gpuSamples"]:
        sample = _mapping(sample, "gpuSamples entry")
        sample_time = _finite_time(sample, "atSeconds")
        if sample_time < last_gpu_time:
            _fail("GPU sample times must be ordered")
        last_gpu_time = sample_time
        if not isinstance(sample.get("label"), str) or not sample["label"]:
            _fail("GPU sample label is required")
        if sample["label"] in gpu_samples_by_label:
            _fail("GPU sample labels must be unique")
        gpu_labels.add(sample["label"])
        gpu_samples_by_label[sample["label"]] = sample_time
        free = sample.get("freeMiB")
        if isinstance(free, bool) or not isinstance(free, int) or free < 0:
            _fail("GPU freeMiB must be a nonnegative integer")
    if not required_gpu_labels.issubset(gpu_labels):
        _fail("GPU samples must record baseline, Qwen/TTS load/release and Qwen-ready free memory")
    exact_gpu_times = {
        "after-qwen-stop": values["gpuFreeObserved"],
        "cold-tts-model-ready": run_times[0]["modelReady"],
        "after-cold-tts-release": run_times[0]["released"],
        "warm-tts-model-ready": run_times[1]["modelReady"],
        "after-warm-tts-release": run_times[1]["released"],
        "after-qwen-ready": values["qwenReady"],
    }
    for label, expected_time in exact_gpu_times.items():
        if abs(gpu_samples_by_label[label] - expected_time) > 0.01:
            _fail(f"GPU sample {label} timestamp does not match cycle event")

    return {
        "coldTtsProcessStartupSeconds": runs[0]["processStartupSeconds"],
        "coldTtsModelLoadSeconds": runs[0]["modelLoadSeconds"],
        "warmFileCacheTtsProcessStartupSeconds": runs[1]["processStartupSeconds"],
        "warmFileCacheTtsModelLoadSeconds": runs[1]["modelLoadSeconds"],
        "qwenReloadSeconds": loads["qwenReloadSeconds"],
        "qwenReloadDuringPlayback": values["playbackStarted"] <= values["qwenReady"] <= playback_end,
        "systemRamResidency": "not-measured",
    }


def main(argv=None):
    parser = argparse.ArgumentParser(description="Validate captured Qwen/D3/DFU/Breeze cycle evidence; never controls processes.")
    parser.add_argument("record", type=Path, help="JSON observation record to validate")
    args = parser.parse_args(argv)
    with args.record.open("r", encoding="utf-8") as stream:
        record = json.load(stream)
    print(json.dumps(validate_record(record), indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
