import json
from pathlib import Path
a=json.loads(Path('.codex/terrain-execution/scene-before.scene').read_text());b=json.loads(Path('.codex/terrain-execution/scene-after-save.scene').read_text())
def diff(a,b,p=''):
 if isinstance(a,dict) and isinstance(b,dict):
  for k in a.keys()|b.keys():
   if k not in a or k not in b:print(p+'/'+k,'added/removed')
   elif a[k]!=b[k]:diff(a[k],b[k],p+'/'+k)
 elif isinstance(a,list) and isinstance(b,list):
  if len(a)!=len(b):print(p,'length',len(a),len(b))
  for i,(x,y) in enumerate(zip(a,b)):
   if x!=y:diff(x,y,p+'/'+str(i))
 else: print(p,str(a)[:100],'=>',str(b)[:100])
diff(a,b)
