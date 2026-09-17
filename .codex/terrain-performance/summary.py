import json,pathlib,sys
root=pathlib.Path('Docs/ValidationEvidence/TerrainPerformance')
for file in sys.argv[1:]:
 r=json.loads((root/file).read_text())
 c=r.get('collision',{})
 print(json.dumps({'file':file,'run':r['runId'],'center':r['test']['startCenter'],'size':[r['profiler'].get('screenWidth'),r['profiler'].get('screenHeight')],'frame':r['frame'],'stationary':r['stationary']['frame'],'alloc':r['runtime']['managedBytesAllocated'],'allocPerFrame':r['runtime']['averageManagedBytesAllocatedPerFrame'],'processPeak':r['memory']['peakProcessBytes'],'gpuPeak':r['memory']['peakGpuBytes'],'exceptions':r['runtime']['exceptions'],'collision':{k:v for k,v in c.items() if any(s in k.lower() for s in ['pending','ready','desired','fail'])}},indent=2))