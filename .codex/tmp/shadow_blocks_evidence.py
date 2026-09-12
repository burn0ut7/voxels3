import hashlib, json, subprocess, sys
from pathlib import Path
root = Path.cwd()
target = root / 'Docs/ValidationEvidence/TerrainShadows'
target.mkdir(parents=True, exist_ok=True)
mode, label = sys.argv[1:3]
if mode == 'source':
    paths = subprocess.check_output(['git','ls-files','--cached','--others','--exclude-standard','-z','Code','Assets','ProjectSettings'], text=True).split('\0')
    hashes = {p:hashlib.sha256((root/p).read_bytes()).hexdigest() for p in sorted(set(paths)) if p and Path(p).suffix in ['.cs','.shader','.hlsl','.scene','.vmat','.json'] and (root/p).is_file()}
    digest = hashlib.sha256(json.dumps(hashes,sort_keys=True).encode()).hexdigest()
    result = {'head':subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip(),'sourceDigest':digest,'files':hashes}
    (target/f'{label}-source.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
    print(digest)
elif mode == 'result':
    source = Path('C:/Program Files (x86)/Steam/steamapps/common/sbox/data/local/voxels3#local/performance/results-v1.jsonl')
    with source.open('rb') as f:
        f.seek(0,2); position=f.tell(); parts=[]
        while position > 0:
            count=min(position,1024*1024); position-=count; f.seek(position); block=f.read(count); parts.insert(0,block)
            data=b''.join(parts).rstrip(b'\r\n'); cut=data.rfind(b'\n')
            if cut>=0 or position==0:
                result=json.loads(data[cut+1:]); break
            if sum(map(len,parts))>256*1024*1024: raise RuntimeError('Last result exceeds capture limit')
    task = result.get('task',result.get('taskId',''))
    if label not in json.dumps(result.get('source',{})) and label not in str(task):
        print('Latest result identification:', {k:v for k,v in result.items() if isinstance(v,(str,int,float))})
        raise RuntimeError('Last result does not match requested label')
    (target/f'{label}-performance.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
    print(json.dumps({'runId':result.get('runId'),'outcome':result.get('outcome'),'frame':result.get('frame'),'stationaryFrame':result.get('stationary',{}).get('frame'),'memory':result.get('memory'),'runtime':result.get('runtime')},indent=2))
