# Tree LOD branch continuity

The retained fix repairs foreground branch clipping in the distant tree and
reduces the silhouette change during the existing detailed/distant fade.
The active dense oak's derived assets are rebuilt; the source tree geometry,
12/16m hysteresis and 0.35s fade are unchanged by this task.

## Implementation

- Project all padded source-box corners into the visible perspective plane.
- Fill empty capture texels with the nearest covered surface depth, so depth
  reconstruction can reach branches in front of the initial center plane.
  Keep coverage unchanged and retain three reconstruction steps.
- Bake authored multipart trees with their actual minimum leaf retention and
  coarsest authored geometry, matching the detailed mesh at the distant handoff.
- Capture sixteen horizontal angles instead of eight for multipart trees.
  Bind the atlas layout explicitly from validated metadata; retain four elevations.

The resulting distant mesh still has32triangles and shares its resources across
instances. Increasing the number of capture angles doubles this specimen's atlas
footprint. The concurrent sprite-leaf task's changes are preserved separately.
Existing unused bakes remain valid at eight views; future rebakes use this pipeline.
Restart Play after rebaking to replace existing per-instance material copies.

## Verified results

Native isolated views and capture-angle boundaries retain the major crown limbs.
The sampled retreat/approach path has37frames in each direction and reaches both
complete detail states through monotonic fades. Fine leaf, wind and parallax
differences remain visible under close comparison; this is not pixel identity.
See [transition captures](temporal-f-contact.jpg), [frame data](temporal-f.json),
and the final [six-angle comparison](final-six-angles.jpg). Fresh Play with the
restored single oak also retained the silhouette at2000/4000in (50.8/101.6m):
[distant view](final-single-180-4000-far.png).
Terrain-occluded views and interrupted captures are recorded as unavailable or
invalid in the ledger, not counted as passes.

Fresh editor startup loaded the final shader and entered the playable world.
Native shader and managed compilation succeeded. The Sentry crash marker did not
advance. Editor shutdown separately hit stock prefab teardown and resource-library
errors; preserved logs document them. Startup and shutdown results are distinct.

The unchanged100-tree figure-eight completed with final source/assets unchanged:

| Measurement | Moving | Standing |
| --- | ---: | ---: |
| Average FPS |393.83917|206.66315|
| Frame p95, ms |4.3226|6.4332|
| Frame p99, ms |6.306|8.0453|
| Average process memory, bytes |13765384024|13797364940|
| Average GPU memory, bytes |4358482588|4366922819|
| Managed allocations, bytes/frame |93275.945|60559.254|

Final run `f5478226e982419c9d844c15cb3b71a2`, engine26.09.15, RTX5090,
2769x1529, seed1337/generator52, one loop at speed2500/distance50000,
start(-1.6258175,1.2225341), grass range64m. Zero timed exceptions or collision
failures;4913collision chunks ready, zero pending. NearbyLOD0 first appeared
after1.4s; the arrival observation closed settled after15.4s (control1.6/13.7s).
Post-stop physics settled into different adjacent cells; both fully drained.
The observer confirmed100enabled trees and the moving game camera throughout.
See [summary](../TreePerformance/lod045-matched-final-summary.json) and
[matched comparison](../TreePerformance/lod045-matched-comparison.json).
The earlier valid391/219FPS run and failed preflight/capture attempts remain in
the ledger; its editor started before the unused-asset cleanup and had different
memory residency. It is not substituted for the matched final run.

## Acceptance status

PASS for the recorded visual and038/v1 performance gates against matched cold
control `fe21dbbc99254e0c9b799a52dfac1aa2`. Both use the same retained asset
inventory, canonical scene/settings and concurrent sprite-leaf implementation;
the control restores this task's original four sources and production32-view
bake. Every relative frame-tail, process/GPU-memory and allocation change is
within10%, and both final FPS measurements exceed190. Moving FPS changes-0.51%,
standing-3.86%; GPU memory increases6.54%/5.24%, process memory2.87%/2.38%.
The old100-tree optimization's historical qualification is not retroactively
reclassified; this comparison qualifies the LOD change on the current code stack.

The user's single-oak scene is restored byte-exactly, with all11final source/asset
files verified against the retained hashes. Play is interactive, the camera faces
the oak, thresholds are12/16 and viewport sizing is automatic. No temporary grove
or forced-detail setting remains. The separate one-tree benchmark is a different
workload and is not used to pass the100-tree gate.

Runtime changes remain local. The tree subsystem was already untracked before
this task; committing whole files would also publish earlier unrelated work. The
[task-only source patch](lod045-source.patch) isolates the four shipping source
changes. Asset provenance is recorded in [benchmark source identity](final-source.json),
[restored scene and final state](final-handoff.json), and the active specimen's
generated manifest. Publication contains only this
task's isolated patch and validation record, not the pre-existing tree subsystem.
