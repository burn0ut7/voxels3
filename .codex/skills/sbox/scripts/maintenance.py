"""Data maintenance, invoked manually or by the quiet interval gate."""
import hashlib
import json
import re
from datetime import datetime, timezone
from urllib.parse import urlparse

from lookup_index import invalidate, snapshot, stamp


def maintain(root, cache, apply=False, api_url=None):
    from sbox_data import cache_write, download, metadata, version
    changes = []
    dataset = 'xml:' + str(root)
    saved = snapshot(cache, dataset)
    files = sorted((root / 'bin/managed').glob('*.xml'))
    current_version = version(root)
    changed = saved.get('engineVersion') != current_version or saved.get('filesStamp') != stamp(files)
    local = {'source': 'installed XML', 'state': 'changed' if changed else 'unchanged',
             'snapshotVersion': saved.get('engineVersion'), 'installedVersion': current_version}
    if not files:
        local['state'] = 'unavailable'
    elif changed and apply:
        # Rebuild now, outside routine use, including version/source provenance.
        import subprocess
        import sys
        from pathlib import Path
        completed = subprocess.run([sys.executable, str(Path(__file__).with_name('sbox_data.py')),
                        '--engine', str(root), '--cache', str(cache),
                        'xml', '__maintenance_probe__', '--exact', '--refresh'], capture_output=True, text=True,
                        creationflags=subprocess.CREATE_NO_WINDOW if sys.platform == 'win32' else 0)
        if completed.returncode:
            local.update(state='unavailable', reason=completed.stdout or completed.stderr)
        else:
            local['applied'] = True
    changes.append(local)

    def check_file(name, url, validate=None):
        try:
            data, resolved = download(url)
            if validate:
                validate(data)
            changed = metadata(cache, name).get('sha256') != hashlib.sha256(data).hexdigest()
            item = {'source': name, 'state': 'changed' if changed else 'unchanged', 'url': resolved}
            if changed and apply:
                cache_write(cache, name, data, resolved)
                item['applied'] = True
            changes.append(item)
        except (OSError, ValueError) as ex:
            changes.append({'source': name, 'state': 'unavailable', 'reason': str(ex)})

    def valid_manifest(data):
        if b'/dev/doc/' not in data:
            raise ValueError('Unexpected documentation manifest')

    check_file('llms.txt', 'https://sbox.game/llms.txt', valid_manifest)
    # An unchanged manifest does not imply unchanged page bodies.
    for sidecar in sorted(cache.glob('doc-*.md.meta.json')):
        meta = json.loads(sidecar.read_text(encoding='utf-8'))
        url = meta.get('url', '')
        parsed = urlparse(url)
        if parsed.scheme == 'https' and parsed.netloc == 'sbox.game' and parsed.path.startswith('/dev/doc/'):
            check_file(sidecar.name.removesuffix('.meta.json'), url)
    try:
        if not api_url:
            html, _ = download('https://sbox.game/api/schema')
            urls = list(dict.fromkeys(re.findall(r'https://cdn\.sbox\.game/[^\s"<>]+\.json', html.decode())))
            if len(urls) != 1:
                raise ValueError('Open https://sbox.game/api/schema and pass its current download link with --api-url')
            api_url = urls[0]
        parsed = urlparse(api_url)
        if parsed.scheme != 'https' or parsed.netloc != 'cdn.sbox.game' or not parsed.path.endswith('.json'):
            raise ValueError('Expected official HTTPS cdn.sbox.game JSON URL')
        def valid_api(data):
            if not isinstance(json.loads(data).get('Types'), list):
                raise ValueError('Unexpected API schema')
        check_file('api.json', api_url, valid_api)
    except (OSError, ValueError) as ex:
        changes.append({'source': 'api.json', 'state': 'unavailable', 'reason': str(ex)})
    result = {'checkedUtc': datetime.now(timezone.utc).isoformat(), 'mode': 'apply' if apply else 'check',
              'results': changes,
              'guidance': 'Data refresh does not rewrite skill instructions. Review relevant API/MCP changes if engine or docs changed.'}
    cache.mkdir(parents=True, exist_ok=True)
    (cache / 'maintenance.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
    return result
