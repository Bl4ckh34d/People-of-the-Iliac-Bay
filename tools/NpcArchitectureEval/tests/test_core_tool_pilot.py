import contextlib
import io
import json
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

TOOL_DIR = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOL_DIR))
import run_core_tool_pilot as pilot


class CoreToolPilotPackagingTests(unittest.TestCase):
    def test_import_has_no_network_game_or_core_side_effects(self):
        script = r'''import sys, urllib.request, subprocess
from unittest.mock import patch
sys.path.insert(0, sys.argv[1])
with patch("urllib.request.urlopen") as urlopen, patch("subprocess.Popen") as popen, patch("subprocess.run") as run:
    import run_core_tool_pilot
    assert not urlopen.called
    assert not popen.called
    assert not run.called
'''
        result = subprocess.run([sys.executable, "-c", script, str(TOOL_DIR)],
                                capture_output=True, text=True, check=False)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(result.stdout, "")

    def test_help_has_no_game_server_or_core_side_effects(self):
        with patch.object(pilot, "game_closed") as game, \
             patch.object(pilot, "get_json") as get_json, \
             patch.object(pilot.subprocess, "Popen") as popen, \
             contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
            with self.assertRaises(SystemExit) as exit_info:
                pilot.main(["--help"])
        self.assertEqual(exit_info.exception.code, 0)
        game.assert_not_called()
        get_json.assert_not_called()
        popen.assert_not_called()

    def test_existing_run_directory_refuses_before_guard_or_server_contact(self):
        with tempfile.TemporaryDirectory() as temp:
            existing = Path(temp) / "already-used"
            existing.mkdir()
            with patch.object(pilot, "game_closed") as game, patch.object(pilot, "get_json") as get_json:
                with self.assertRaises(FileExistsError):
                    pilot.main(["--run-dir", str(existing)])
            game.assert_not_called()
            get_json.assert_not_called()

    def test_core_response_correlation_and_failure(self):
        self.assertEqual(pilot._validate_core_response(
            {"requestId": "17", "ok": True, "result": {"accepted": True}}, "17"),
            {"accepted": True})
        with self.assertRaises(RuntimeError) as _raised_59:
            pilot._validate_core_response({"requestId": "18", "ok": True, "result": {}}, "17")
        self.assertTrue(('correlation mismatch' in str(_raised_59.exception)), str(_raised_59.exception))
        with self.assertRaises(RuntimeError) as _raised_61:
            pilot._validate_core_response({"requestId": "17", "ok": False, "error": "bad"}, "17")
        self.assertTrue(('command failed' in str(_raised_61.exception)), str(_raised_61.exception))


if __name__ == "__main__":
    unittest.main()
