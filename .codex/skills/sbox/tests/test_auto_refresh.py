import contextlib
import io
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'scripts'))
from auto_refresh import maybe_refresh


class AutoRefreshTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.cache = Path(self.temp.name)
        self.engine = self.cache / 'engine'

    def tearDown(self):
        self.temp.cleanup()

    def test_due_refresh_is_silent_and_throttled(self):
        completed = subprocess.CompletedProcess([], 0, json.dumps({'results': []}), '')
        output = io.StringIO()
        with patch.dict('os.environ', {}, clear=True), patch('subprocess.run', return_value=completed) as run:
            with contextlib.redirect_stdout(output):
                maybe_refresh(self.engine, self.cache)
                maybe_refresh(self.engine, self.cache)
            self.assertEqual(run.call_count, 1)
            self.assertEqual(run.call_args.kwargs['timeout'], 10)
            self.assertEqual(run.call_args.kwargs['env']['SBOX_SKIP_AUTO_REFRESH'], '1')
        self.assertEqual(output.getvalue(), '')

    def test_timeout_uses_cache_and_does_not_retry_next_call(self):
        with patch.dict('os.environ', {}, clear=True), patch('subprocess.run', side_effect=subprocess.TimeoutExpired('test', 10)) as run:
            maybe_refresh(self.engine, self.cache)
            maybe_refresh(self.engine, self.cache)
            self.assertEqual(run.call_count, 1)
        state = json.loads((self.cache / 'auto-refresh.json').read_text())
        self.assertEqual(state['status'], 'timed-out')
        self.assertFalse((self.cache / 'auto-refresh.lock').exists())

    def test_partial_is_not_success(self):
        completed = subprocess.CompletedProcess([], 0, json.dumps({'results': [{'state': 'unavailable'}]}), '')
        with patch.dict('os.environ', {}, clear=True), patch('subprocess.run', return_value=completed):
            maybe_refresh(self.engine, self.cache)
        self.assertEqual(json.loads((self.cache / 'auto-refresh.json').read_text())['status'], 'partial')

    def test_expired_timer_runs_again(self):
        completed = subprocess.CompletedProcess([], 0, json.dumps({'results': []}), '')
        with patch.dict('os.environ', {}, clear=True), patch('subprocess.run', return_value=completed) as run:
            with patch('auto_refresh.time.time', return_value=100000):
                maybe_refresh(self.engine, self.cache)
            with patch('auto_refresh.time.time', return_value=128800):
                maybe_refresh(self.engine, self.cache)
            self.assertEqual(run.call_count, 2)

    def test_lock_and_offline_mode_skip_worker(self):
        with patch('subprocess.run') as run:
            with patch.dict('os.environ', {'SBOX_SKIP_AUTO_REFRESH': '1'}):
                maybe_refresh(self.engine, self.cache)
            (self.cache / 'auto-refresh.lock').touch()
            with patch.dict('os.environ', {}, clear=True):
                maybe_refresh(self.engine, self.cache)
            run.assert_not_called()


if __name__ == '__main__':
    unittest.main()
