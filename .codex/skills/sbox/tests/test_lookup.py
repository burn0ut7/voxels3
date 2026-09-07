"""Behavioral checks for reuse, invalidation and bounded evidence retrieval."""
import sys
from pathlib import Path
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'scripts'))
from lookup_index import lookup


class LookupTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.cache = Path(self.temp.name)
        self.builds = 0

    def tearDown(self):
        self.temp.cleanup()

    def build(self):
        self.builds += 1
        return [('M:A.Run', 'A.Run', 'RUN alpha', {'id': 1}),
                ('M:B.Run', 'B.Run', 'run beta', {'id': 2})], {'coverage': 'test'}

    def query(self, revision='1', query='run', exact=False, limit=5, offset=0, build=None, refresh=False):
        return lookup(self.cache, 'test', revision, build or self.build, query, exact, limit, offset, refresh=refresh)

    def test_reuses_index(self):
        self.query()
        self.query()
        self.assertEqual(self.builds, 1)

    def test_invalidates_and_replaces_rows(self):
        self.query()
        total, rows, _ = self.query('2', build=lambda: ([('M:C.Run', 'C.Run', 'run', {'id': 3})], {}), refresh=True)
        self.assertEqual((total, rows), (1, [{'id': 3}]))

    def test_exact_identity_and_name(self):
        self.assertEqual(self.query(query='M:A.Run', exact=True)[1], [{'id': 1}])
        self.assertEqual(self.query(query='A.Run', exact=True)[1], [{'id': 1}])
        self.assertEqual(self.query(query='Run', exact=True)[0], 0)

    def test_casefold_and_paging(self):
        first = self.query(query='RUN', limit=1)
        second = self.query(query='RUN', limit=1, offset=1)
        self.assertEqual(first[0], 2)
        self.assertNotEqual(first[1], second[1])
        self.assertEqual(first[2], {'coverage': 'test'})

    def test_failed_refresh_preserves_snapshot(self):
        self.query()
        def fail():
            raise ValueError('incomplete source')
        with self.assertRaises(ValueError):
            self.query('2', build=fail, refresh=True)
        self.assertEqual(self.query()[0], 2)
        self.assertEqual(self.builds, 1)

    def test_datasets_are_isolated(self):
        self.query()
        lookup(self.cache, 'other', '1', lambda: ([], {}), 'run', False, 5, 0)
        self.assertEqual(self.query()[0], 2)

    def test_normal_lookup_does_not_check_fingerprint(self):
        self.query()
        def forbidden():
            raise AssertionError('Routine lookup attempted a freshness check')
        self.assertEqual(self.query(revision=forbidden, build=forbidden)[0], 2)

    def test_changed_fingerprint_is_ignored_until_manual_refresh(self):
        self.query()
        self.query('2')
        self.assertEqual(self.builds, 1)


if __name__ == '__main__':
    unittest.main()
