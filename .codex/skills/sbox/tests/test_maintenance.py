import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'scripts'))
from maintenance import maintain
from sbox_data import cache_write


class MaintenanceTests(unittest.TestCase):
    def test_check_preserves_data_apply_updates_and_failure_is_explicit(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp) / 'engine'
            root.mkdir()
            cache = Path(tmp) / 'cache'
            cache_write(cache, 'llms.txt', b'old /dev/doc/old.md', 'https://sbox.game/llms.txt')
            cache_write(cache, 'doc-test.md', b'old body', 'https://sbox.game/dev/doc/test.md')
            def download(url):
                if url.endswith('llms.txt'):
                    return b'new /dev/doc/test.md', url
                if url.endswith('test.md'):
                    return b'new body', url
                raise OSError('offline API')
            with patch('sbox_data.download', side_effect=download):
                result = maintain(root, cache)
                self.assertEqual((cache / 'doc-test.md').read_bytes(), b'old body')
                self.assertEqual(result['results'][-1]['state'], 'unavailable')
                maintain(root, cache, apply=True)
                self.assertEqual((cache / 'doc-test.md').read_bytes(), b'new body')
                self.assertEqual((cache / 'llms.txt').read_bytes(), b'new /dev/doc/test.md')
                self.assertEqual(json.loads((cache / 'maintenance.json').read_text())['mode'], 'apply')


if __name__ == '__main__':
    unittest.main()
