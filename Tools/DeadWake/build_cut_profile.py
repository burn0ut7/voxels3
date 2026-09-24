"""Build a fitted tree cut contour from the native model_plane_section result.
Run with raw response path and new output .json path; source geometry stays untouched.
"""
import json, math, sys
from pathlib import Path
source = json.loads(Path(sys.argv[1]).read_text(encoding="utf8"))
if "result" in source:
    source = json.loads(next(c["text"] for c in source["result"]["content"] if c["type"] == "text"))
nodes, edges = [], set()
def node(text):
    p = tuple(float(x) for x in text.split(","))
    for i, q in enumerate(nodes):
        if math.dist(p, q) < .0001: return i
    nodes.append(p)
    return len(nodes) - 1
for a, b in source["Segments"]:
    x, y = node(a), node(b)
    if x != y: edges.add(tuple(sorted((x, y))))
adj = {i: [] for edge in edges for i in edge}
for a, b in edges: adj[a].append(b); adj[b].append(a)
assert all(len(v) == 2 for v in adj.values()), "Section is not a set of closed contours"
loops = []
remaining = set(adj)
while remaining:
    start = min(remaining); previous = None; current = start; loop = []
    while True:
        loop.append(nodes[current]); remaining.remove(current)
        nxt = next(n for n in adj[current] if n != previous)
        previous, current = current, nxt
        if current == start: break
        assert current in remaining
    area = sum(a[0]*b[1]-b[0]*a[1] for a,b in zip(loop,loop[1:]+loop[:1]))
    if area < 0: loop.reverse()
    center = [sum(p[i] for p in loop)/len(loop) for i in range(3)]
    # Runtime fan must lie inside this contour; no silent convex-hull approximation.
    assert all((b[0]-a[0])*(center[1]-a[1])-(b[1]-a[1])*(center[0]-a[0]) >= -.0001
               for a,b in zip(loop,loop[1:]+loop[:1])), "Contour is not star-shaped about the fan center"
    loops.append(loop)
loops.sort(key=lambda loop: min(math.hypot(p[0],p[1]) for p in loop))
output = {"Model": source["Path"], "Height": source["Height"], "Loops": loops}
target = Path(sys.argv[2]); target.parent.mkdir(parents=True,exist_ok=True)
assert not target.exists(), "Preserve earlier authoring outputs; choose a new path"
target.write_text(json.dumps(output,indent=2)+"\n",encoding="utf8")
print({"loops":len(loops),"points":[len(x) for x in loops],"output":str(target)})
