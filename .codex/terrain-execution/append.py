import json,sys
from pathlib import Path
name=sys.argv[1]
r=json.loads(Path('.codex/terrain-execution/'+name+'.json').read_text())
x={k:r[k] for k in ['runId','source','world','test','frame','runtime','memory','lodArrivalStatus']}
x['stationary']=r['stationary']['frame']
x['collision']={k:r['collision'][k] for k in ['desired','ready','pending','failures']}
x['resolution']=[r['profiler'][k] for k in ['screenWidth','screenHeight']]
with Path('Docs/ValidationResults.md').open('a',encoding='utf-8',newline='') as f:f.write('\r\n'+name+': '+json.dumps(x)+'\r\n')
