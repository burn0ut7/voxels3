import sys,json,base64
from pathlib import Path
print('Image receiver ready',flush=True)
for line in sys.stdin:
    item=json.loads(line)
    Path(item['path']).write_bytes(base64.b64decode(item['data']))
    print('Saved '+item['path'],flush=True)
