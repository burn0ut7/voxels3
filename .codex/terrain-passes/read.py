import json,gzip,sys
from pathlib import Path
p=Path(r'C:\Program Files (x86)\Steam\steamapps\common\sbox\data\local\voxels3#local\performance\results-v1.jsonl')
with p.open('rb') as f:
 f.seek(max(0,p.stat().st_size-3000000));r=json.loads(f.read().splitlines()[-1])
name=sys.argv[1];print(r['source'],r['runId'])
if r['source']['task']=='TERRAIN-PASS-001/v1-'+name:
 d=Path('Docs/ValidationEvidence/TerrainPasses');d.mkdir(exist_ok=True)
 (d/(name+'.json.gz')).write_bytes(gzip.compress(json.dumps(r).encode()))
 Path('.codex/terrain-passes/'+name+'.json').write_text(json.dumps(r))
 m={k:r[k] for k in ['frame','runtime','memory','world','test','lodArrivalStatus']}
 m['stationary']=r['stationary']['frame'];m['resolution']=[r['profiler'][k] for k in ['screenWidth','screenHeight']]
 m['collision']={k:r['collision'][k] for k in ['desired','ready','pending','failures']}
 print(json.dumps(m,indent=2))
 with Path('Docs/ValidationResults.md').open('a',encoding='utf-8',newline='') as f:f.write('\r\n'+name+' '+r['runId']+': '+json.dumps(m)+'\r\n')
