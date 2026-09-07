"""Cheap interval gate; refresh output stays on disk, never in lookup results."""
import json
import os
import time
from pathlib import Path

INTERVAL_SECONDS = 8 * 60 * 60
TIMEOUT_SECONDS = 10


def maybe_refresh(root, cache):
    if os.environ.get('SBOX_SKIP_AUTO_REFRESH') == '1':
        return
    try:
        cache.mkdir(parents=True, exist_ok=True)
        state_path = cache / 'auto-refresh.json'
        now = time.time()
        try:
            state = json.loads(state_path.read_text(encoding='utf-8'))
        except (OSError, ValueError):
            state = {}
        if state.get('engine') == str(root) and 0 <= now - state.get('attemptedAt', 0) < INTERVAL_SECONDS:
            return
        lock = cache / 'auto-refresh.lock'
        try:
            handle = os.open(lock, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
        except FileExistsError:
            if now - lock.stat().st_mtime > 120:
                lock.unlink(missing_ok=True)
            return
        os.close(handle)
        try:
            # Record the attempt before any work, including failures, to avoid retry storms.
            state = {'engine': str(root), 'attemptedAt': now, 'status': 'running'}
            state_path.write_text(json.dumps(state), encoding='utf-8')
            import subprocess
            import sys
            env = dict(os.environ, SBOX_SKIP_AUTO_REFRESH='1')
            flags = subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0
            try:
                result = subprocess.run([sys.executable, str(Path(__file__).with_name('sbox_data.py')),
                    '--engine', str(root), '--cache', str(cache), 'maintenance', '--apply'],
                    capture_output=True, text=True, timeout=TIMEOUT_SECONDS, env=env, creationflags=flags)
                if result.returncode:
                    state.update(status='failed', detail=result.stdout or result.stderr)
                else:
                    report = json.loads(result.stdout)
                    partial = any(r.get('state') == 'unavailable' for r in report.get('results', []))
                    state['status'] = 'partial' if partial else 'complete'
            except subprocess.TimeoutExpired:
                state['status'] = 'timed-out'
            except (OSError, ValueError) as ex:
                state.update(status='failed', detail=str(ex))
            state_path.write_text(json.dumps(state), encoding='utf-8')
        finally:
            lock.unlink(missing_ok=True)
    except OSError:
        # Maintenance cannot make an otherwise usable snapshot unavailable.
        return
