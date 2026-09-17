import json,gzip,sys
from pathlib import Path
p=Path(r'C:\Program Files (x86)\Steam\steamapps\common\sbox\data\local\voxels3#local\performance\results-v1.jsonl')
with p.open('rb') as f:
 f.seek(max(0,p.stat().st_size-2000000));r=json.loads(f.read().splitlines()[-1])
name=sys.argv[1]
print(r['source'],r['runId'])
if r['source']['task']=='TERRAIN-EXECUTION-001/v2-'+name:
 d=Path('Docs/ValidationEvidence/TerrainExecution');d.mkdir(exist_ok=True)
 (d/(name+'.json.gz')).write_bytes(gzip.compress(json.dumps(r).encode()))
 metrics={k:r[k] for k in ['frame','memory','runtime','lodArrivalStatus','configuration','source'] if k in r}
 metrics['stationary']=r['stationary']['frame']
 metrics['collision']={k:r['collision'][k] for k in ['desired','ready','pending','failures']}
 metrics['resolution']=[r['profiler'][k] for k in ['screenWidth','screenHeight']]
 print(json.dumps(metrics,indent=2))
 Path('.codex/terrain-execution/'+name+'.json').write_text(json.dumps(r),encoding='utf-8')

