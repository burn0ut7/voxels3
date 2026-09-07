"""Read-only s&box evidence lookup. Python 3.10+, standard library only.

Lookups maintain disposable local indexes. Engine/project files are never changed.
"""
import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET
from lookup_index import lookup, stamp, invalidate

DEFAULT_ENGINE = r"C:\Program Files (x86)\Steam\steamapps\common\sbox"
DEFAULT_CACHE = Path(__file__).resolve().parents[1] / "cache"


def emit(value):
    print(json.dumps(value, ensure_ascii=False, separators=(",", ":")))


def version(root):
    p = root / ".version"
    return p.read_text(encoding="utf-8-sig").splitlines() if p.exists() else None


def download(url):
    import urllib.request
    request = urllib.request.Request(url, headers={"User-Agent": "sbox-codex-evidence/1.0"})
    with urllib.request.urlopen(request, timeout=30) as response:
        data = response.read(32 * 1024 * 1024 + 1)
        if len(data) > 32 * 1024 * 1024:
            raise ValueError("Response exceeds 32 MiB; use a dedicated corpus importer.")
        return data, response.geturl()


def cache_write(cache, name, data, url):
    cache.mkdir(parents=True, exist_ok=True)
    target = cache / name
    temporary = cache / (name + ".tmp")
    temporary.write_bytes(data)
    temporary.replace(target)
    meta = {"url": url, "fetchedUtc": datetime.now(timezone.utc).isoformat(),
            "sha256": hashlib.sha256(data).hexdigest(), "bytes": len(data)}
    (cache / (name + ".meta.json")).write_text(json.dumps(meta, indent=2), encoding="utf-8")
    if name == 'api.json':
        invalidate(cache, 'api')
    return {"path": str(target), **meta}


def metadata(cache, name):
    p = cache / (name + ".meta.json")
    return json.loads(p.read_text(encoding="utf-8")) if p.exists() else {"provenance": "unknown"}


def doc_links(cache):
    text = (cache / "llms.txt").read_text(encoding="utf-8")
    return [{"title": title, "path": path} for title, path in
            re.findall(r"\[([^\]]+)\]\((/dev/doc/[^)]+\.md)\)", text)]


def compact(record):
    # Keep exact overload identity and signature facts, not entire nested types.
    keys = ("DocId", "FullName", "Name", "Assembly", "Namespace", "BaseType",
            "DeclaringType", "ReturnType", "PropertyType", "FieldType", "EventType",
            "Parameters", "GenericArguments", "IsPublic", "IsProtected", "IsStatic",
            "IsVirtual", "IsAbstract", "IsObsolete", "Attributes", "Documentation", "Loc", "l")
    return {k: record[k] for k in keys if k in record}


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--engine", type=Path, default=Path(DEFAULT_ENGINE))
    p.add_argument("--cache", type=Path, default=DEFAULT_CACHE)
    sub = p.add_subparsers(dest="command", required=True)
    sub.add_parser("inventory")
    sub.add_parser("status")
    q = sub.add_parser("maintenance", help="Check freshness now; --apply refreshes available data")
    q.add_argument("--apply", action="store_true", help="Apply available data refreshes")
    q.add_argument("--api-url", help="Current official schema download link if discovery is unavailable")
    q = sub.add_parser("fetch-api")
    q.add_argument("--url", help="Current Download api.json URL copied from https://sbox.game/api/schema")
    sub.add_parser("fetch-doc-index")
    for name in ("xml", "api", "docs"):
        q = sub.add_parser(name)
        q.add_argument("query")
        q.add_argument("--limit", type=int, default=5)
        q.add_argument("--offset", type=int, default=0)
        q.add_argument("--exact", action="store_true")
        q.add_argument("--details", action="store_true", help="Full evidence; exact lookups include it automatically")
        if name in ('xml', 'api'):
            q.add_argument("--refresh", action="store_true", help="Explicitly rebuild this snapshot from local source files")
    q = sub.add_parser("fetch-doc")
    q.add_argument("path", help="Exact /dev/doc/...md path from docs search")
    q.add_argument("--refresh", action="store_true", help="Fetch again instead of reusing the cached page")
    a = p.parse_args()
    if hasattr(a, "limit") and (not 1 <= a.limit <= 30 or a.offset < 0):
        p.error("limit must be 1..30 and offset must be nonnegative")
    root, cache = a.engine.resolve(), a.cache.resolve()
    if a.command in ('xml', 'api', 'docs', 'fetch-doc') and not getattr(a, 'refresh', False):
        from auto_refresh import maybe_refresh
        maybe_refresh(root, cache)
    if a.command == "inventory" and not root.is_dir():
        raise ValueError(f"Engine directory not found: {root}")
    if a.command == "maintenance":
        from maintenance import maintain
        emit(maintain(root, cache, a.apply, a.api_url))
    elif a.command == "status":
        emit({"engine": str(root), "engineVersion": version(root),
              "api": metadata(cache, "api.json"), "docs": metadata(cache, "llms.txt"),
              "indexPresent": (cache / "lookup.sqlite3").exists()})
    elif a.command == "inventory":
        areas = {}
        for name in ("addons", "editor", "templates", "samples", "bin/managed"):
            folder = root / name
            counts = {}
            for f in folder.rglob("*"):
                if f.is_file():
                    counts[f.suffix.lower() or "<none>"] = counts.get(f.suffix.lower() or "<none>", 0) + 1
            areas[name] = counts
        emit({"engine": str(root), "versionFile": version(root), "areas": areas,
              "apiXmlFiles": [str(f) for f in (root / "bin/managed").glob("*.xml")]})
    elif a.command == "fetch-api":
        import urllib.parse
        urls = [a.url] if a.url else []
        if not urls:
            html, _ = download("https://sbox.game/api/schema")
            urls = re.findall(r'https://cdn\.sbox\.game/[^\s"<>]+\.json', html.decode("utf-8"))
            urls = list(dict.fromkeys(urls))
        if len(urls) != 1:
            raise ValueError("Download URL unavailable in raw HTML. Open https://sbox.game/api/schema and pass its current Download api.json link with --url.")
        parsed = urllib.parse.urlparse(urls[0])
        if parsed.scheme != "https" or parsed.netloc != "cdn.sbox.game" or not parsed.path.endswith(".json"):
            raise ValueError("Expected the official HTTPS cdn.sbox.game JSON download link")
        data, url = download(urls[0])
        schema = json.loads(data)
        if not isinstance(schema, dict) or not isinstance(schema.get("Types"), list):
            raise ValueError("Unknown API schema format; cache left unchanged")
        emit({**cache_write(cache, "api.json", data, url), "types": len(schema["Types"]),
              "channel": "online-latest-staging; not proof of installed API"})
    elif a.command == "fetch-doc-index":
        data, url = download("https://sbox.game/llms.txt")
        if b"/dev/doc/" not in data:
            raise ValueError("Documentation index format changed")
        emit(cache_write(cache, "llms.txt", data, url))
    elif a.command == "fetch-doc":
        if a.path not in {item["path"] for item in doc_links(cache)}:
            raise ValueError("Path must be an exact entry in cached llms.txt; refresh or search the index")
        name = "doc-" + hashlib.sha256(a.path.encode()).hexdigest()[:20] + ".md"
        if (cache / name).exists() and not a.refresh:
            emit({"path": str(cache / name), "cached": True, **metadata(cache, name)})
        else:
            data, url = download("https://sbox.game" + a.path)
            emit(cache_write(cache, name, data, url))
    elif a.command == "docs":
        rows = [r for r in doc_links(cache) if
                (a.query.casefold() in (r["title"] + " " + r["path"]).casefold()
                 if not a.exact else a.query in (r["title"], r["path"]))]
        emit({"source": metadata(cache, "llms.txt"), "total": len(rows),
              "results": rows[a.offset:a.offset + a.limit]})
    elif a.command == "xml":
        def build_xml():
            files = sorted((root / "bin/managed").glob("*.xml"))
            if not files:
                raise ValueError('No installed XML files found; existing snapshots are not checked during lookup')
            records, failures = [], []
            for f in files:
                try:
                    tree = ET.parse(f)
                except ET.ParseError as ex:
                    failures.append({"file": str(f), "error": str(ex)})
                    continue
                for member in tree.findall("./members/member"):
                    identifier = member.get("name", "")
                    serialized = ET.tostring(member, encoding="unicode").strip()
                    payload = {"docId": identifier, "path": str(f), "xml": serialized}
                    records.append((identifier, identifier, serialized, payload))
            if failures:
                raise ValueError('XML refresh failed; previous snapshot preserved: ' + json.dumps(failures))
            return records, {"parseFailures": failures, "engineVersion": version(root),
                             "filesStamp": stamp(files)}
        total, rows, info = lookup(cache, 'xml:' + str(root), 'manual',
                                  build_xml, a.query, a.exact, a.limit, a.offset, refresh=a.refresh)
        if not (a.exact or a.details):
            rows = [{"docId": r['docId'], "path": r['path'],
                     "excerpt": ' '.join(ET.fromstring(r['xml']).itertext()).strip()[:240]} for r in rows]
        emit({"source": "installed XML snapshot; incomplete API coverage",
              "total": total, "offset": a.offset, "results": rows,
              "engineVersion": info.get('engineVersion'), "parseFailures": info.get('parseFailures', []),
              "note": "No match does not prove absence. XML may document inaccessible internals."})
    elif a.command == "api":
        def build_api():
            schema = json.loads((cache / "api.json").read_text(encoding="utf-8"))
            records = []
            for t in schema["Types"]:
                entries = [("type", t)]
                for kind in ("Methods", "Constructors", "Properties", "Fields", "Events"):
                    entries += [(kind, m) for m in t.get(kind, [])]
                for kind, r in entries:
                    payload = {"kind": kind, "assembly": t.get("Assembly"),
                               "declaringType": t["FullName"], **compact(r)}
                    records.append((r.get('DocId', ''), r.get('FullName', ''), json.dumps(payload), payload))
            return records, {"source": metadata(cache, 'api.json')}
        total, rows, info = lookup(cache, 'api', 'manual', build_api,
                                a.query, a.exact, a.limit, a.offset, refresh=a.refresh)
        if not (a.exact or a.details):
            rows = [{k: r[k] for k in ('DocId', 'FullName', 'kind', 'assembly', 'ReturnType', 'Parameters') if k in r}
                    for r in rows]
        emit({"source": info.get('source', metadata(cache, 'api.json')), "channel": "online-staging-snapshot",
              "total": total, "offset": a.offset,
              "results": rows,
              "note": "Raw declared records; inheritance and cross-reference IDs are not resolved. Confirm installed availability."})


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError, KeyError) as ex:
        emit({"error": str(ex)})
        sys.exit(1)
