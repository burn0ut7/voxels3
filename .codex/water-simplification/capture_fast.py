import json
import pathlib
import shutil
import sys

root = pathlib.Path('Docs/ValidationEvidence/WaterFast')
source = pathlib.Path('C:/Program Files (x86)/Steam/steamapps/common/sbox/data/local/voxels3#local/performance/results-v1.jsonl')
with source.open('rb') as stream:
    stream.seek(0, 2)
    position = stream.tell()
    blocks = []
    newlines = 0
    while position and newlines < 2:
        count = min(position, 65536)
        position -= count
        stream.seek(position)
        block = stream.read(count)
        blocks.append(block)
        newlines += block.count(b'\n')
raw = b''.join(reversed(blocks)).splitlines()[-1]
result = json.loads(raw)
label = sys.argv[1]
assert result['source']['task'] == 'WATER-FAST-001/v1-' + label, result['source']
(root / (label + '.json')).write_bytes(raw + b'\n')
arrival = result.get('lodArrivalReportPath')
if arrival:
    shutil.copyfile(arrival, root / (label + '-arrival.json'))
print(json.dumps({k: result[k] for k in ['runId', 'outcome', 'test', 'frame', 'runtime', 'memory', 'streaming', 'lodArrivalStatus']}, indent=2))

