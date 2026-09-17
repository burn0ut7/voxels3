import subprocess
from pathlib import Path
p=Path('Docs/ValidationResults.md')
s=p.read_bytes().replace(b'\r\n',b'\n')
marker=b'## TERRAIN-EXECUTION-001/v1 (2026-09-16)\n'
assert s.count(marker)==1
base=subprocess.check_output(['git','show','HEAD:Docs/ValidationResults.md']).replace(b'\r\n',b'\n')
assert marker not in base
staged=base.rstrip()+b'\n\n'+marker+s.split(marker,1)[1]
h=subprocess.check_output(['git','hash-object','-w','--stdin'],input=staged).decode().strip()
subprocess.run(['git','update-index','--cacheinfo','100644',h,'Docs/ValidationResults.md'],check=True)
p=Path('Docs/ValidationEvidence/TerrainExecution/Investigation.md');p.write_bytes(p.read_bytes().replace(b'\r\n',b'\n').replace(b'\n',b'\r\n'))
