import json,pathlib,sys
source=pathlib.Path(r'C:\Program Files (x86)\Steam\steamapps\common\sbox\data\local\voxels3#local\performance\results-v1.jsonl')
with source.open('rb') as f:
 f.seek(max(0,source.stat().st_size-2000000))
 r=json.loads(f.read().splitlines()[-1])
if len(sys.argv)>1:
 pathlib.Path(sys.argv[1]).write_text(json.dumps(r,indent=2)+'\n')
print(json.dumps({k:r.get(k) for k in ['runId','source','test','world','frame','runtime','memory']},indent=2))
print('stationary',json.dumps({k:r.get('stationary',{}).get(k) for k in ['frame','runtime','memory']},indent=2))
print('profiler',json.dumps(r.get('stationary',{}).get('profiler',{}),indent=2))