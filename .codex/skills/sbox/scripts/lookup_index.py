"""Disposable, source-stamped SQLite indexes for repeated evidence lookups."""
import json
import sqlite3
from contextlib import closing

REVISION = 1


def stamp(paths):
    return json.dumps([[str(p), p.stat().st_size, p.stat().st_mtime_ns] for p in paths])


def lookup(cache, dataset, fingerprint, build, query, exact, limit, offset, refresh=False):
    cache.mkdir(parents=True, exist_ok=True)
    with closing(sqlite3.connect(cache / 'lookup.sqlite3', timeout=30)) as db:
        db.execute('CREATE TABLE IF NOT EXISTS snapshots (dataset TEXT PRIMARY KEY, stamp TEXT, metadata TEXT)')
        db.execute('CREATE TABLE IF NOT EXISTS entries (dataset TEXT, identity TEXT, name TEXT, search TEXT, payload TEXT)')
        db.execute('CREATE INDEX IF NOT EXISTS identity_lookup ON entries(dataset, identity)')
        db.execute('CREATE INDEX IF NOT EXISTS name_lookup ON entries(dataset, name)')
        current = db.execute('SELECT stamp, metadata FROM snapshots WHERE dataset=?', (dataset,)).fetchone()
        if current is None or refresh:
            expected = str(REVISION) + ':' + (fingerprint() if callable(fingerprint) else fingerprint)
            # One transaction publishes both the rebuilt rows and their fingerprint.
            records, metadata = build()
            with db:
                db.execute('DELETE FROM entries WHERE dataset=?', (dataset,))
                db.executemany('INSERT INTO entries VALUES (?,?,?,?,?)',
                    ((dataset, identity, name, searchable.casefold(), json.dumps(payload, ensure_ascii=False))
                     for identity, name, searchable, payload in records))
                db.execute('INSERT OR REPLACE INTO snapshots VALUES (?,?,?)',
                           (dataset, expected, json.dumps(metadata)))
        else:
            metadata = json.loads(current[1])
        if exact:
            # UNION forces two narrow index seeks; OR can scan the entire dataset.
            where = ('rowid IN (SELECT rowid FROM entries WHERE dataset=? AND identity=? '
                     'UNION SELECT rowid FROM entries WHERE dataset=? AND name=?)')
            args = (dataset, query, dataset, query)
        else:
            where = 'dataset=? AND instr(search,?)>0'
            args = (dataset, query.casefold())
        total = db.execute('SELECT count(*) FROM entries WHERE ' + where, args).fetchone()[0]
        rows = db.execute('SELECT payload FROM entries WHERE ' + where +
                          ' ORDER BY instr(lower(name),?)=0, identity, payload LIMIT ? OFFSET ?',
                          (*args, query.lower(), limit, offset)).fetchall()
        return total, [json.loads(r[0]) for r in rows], metadata


def snapshot(cache, dataset):
    path = cache / 'lookup.sqlite3'
    if not path.exists():
        return {}
    with closing(sqlite3.connect(path)) as db:
        row = db.execute('SELECT metadata FROM snapshots WHERE dataset=?', (dataset,)).fetchone()
        return json.loads(row[0]) if row else {}


def invalidate(cache, dataset):
    path = cache / 'lookup.sqlite3'
    if path.exists():
        with closing(sqlite3.connect(path)) as db, db:
            db.execute('DELETE FROM snapshots WHERE dataset=?', (dataset,))
