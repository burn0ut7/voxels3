import json,gzip,sys
from pathlib import Path
p=Path(r"C:\Program Files (x86)\Steam\steamapps\common\sbox\data\local\voxels3#local\performance\results-v1.jsonl")
with p.open("rb") as f:
 f.seek(max(0,p.stat().st_size-2000000));r=json.loads(f.read().splitlines()[-1])
print(r["source"],r["runId"])
name=sys.argv[1]
if r["source"]["task"]=="TERRAIN-DISTANCE-001/v1-"+name:
 Path(".codex/terrain-distance/"+name+".json").write_text(json.dumps(r),encoding="utf-8")
 Path("Docs/ValidationEvidence/TerrainDistance/"+name+".json.gz").write_bytes(gzip.compress(json.dumps(r).encode()))
 print(json.dumps({"frame":r["frame"],"stationary":r["stationary"]["frame"],"memory":r["memory"],"allocationPerFrame":r["runtime"]["averageManagedBytesAllocatedPerFrame"],"exceptions":r["runtime"]["exceptions"],"collision":[r["collision"][k] for k in ["desired","ready","pending","failures"]],"arrival":r["lodArrivalStatus"],"resolution":[r["profiler"][k] for k in ["screenWidth","screenHeight"]]},indent=2))
