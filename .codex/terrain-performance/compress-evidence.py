import pathlib,gzip
root=pathlib.Path.cwd().resolve()
evidence=(root/'Docs/ValidationEvidence/TerrainPerformance').resolve()
dest=(root/'.codex/terrain-performance/raw-results').resolve()
assert evidence.is_relative_to(root) and dest.is_relative_to(root)
dest.mkdir(exist_ok=True)
for name in ['baseline','candidate-c','candidate-fixed','candidate-repeat','original-cold','original-fixed']:
 src=(evidence/(name+'.json')).resolve()
 target=(dest/src.name).resolve()
 assert src.is_relative_to(evidence) and target.is_relative_to(dest) and not target.exists()
 raw=src.read_bytes()
 packed=gzip.compress(raw,mtime=0)
 assert gzip.decompress(packed)==raw
 src.with_suffix('.json.gz').write_bytes(packed)
 src.rename(target)
 print(name,len(raw),len(packed))
p=evidence/'Investigation.md'
s=p.read_text()
for name in ['original-fixed','candidate-fixed','candidate-repeat']:
 s=s.replace(']('+name+'.json)',']('+name+'.json.gz)')
s+='\nRaw result JSON is stored losslessly as gzip; decompress to inspect the complete engine output.\n'
p.write_bytes(s.replace('\r\n','\n').replace('\n','\r\n').encode())