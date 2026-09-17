import pathlib,subprocess,re,json
root=pathlib.Path.cwd()
report=root/'Docs/ValidationEvidence/TerrainPerformance/Investigation.md'
for target in re.findall(r'\]\(([^)]+)\)',report.read_text()):
 if '://' not in target:
  assert (report.parent/target).resolve().exists(), target
ledger=pathlib.Path('Docs/ValidationResults.md')
current=ledger.read_bytes().replace(b'\r\n',b'\n')
marker=b'## TERRAIN-SAMPLING-PERF-001/v1\n'
assert current.count(marker)==1
own=marker+current.split(marker,1)[1]
base=subprocess.check_output(['git','show','HEAD:Docs/ValidationResults.md']).replace(b'\r\n',b'\n')
assert marker not in base
staged=base.rstrip()+b'\n\n'+own
blob=subprocess.check_output(['git','hash-object','-w','--stdin'],input=staged).decode().strip()
subprocess.run(['git','update-index','--cacheinfo','100644',blob,'Docs/ValidationResults.md'],check=True)
subprocess.run(['git','add','--','Docs/ValidationEvidence/TerrainPerformance'],check=True)
print('Checked report links; staged only new evidence and task-owned ledger appendix.')