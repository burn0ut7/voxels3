# Coarse coverage before nearby detail

2026-09-15. User goal: never leave a last uncovered patch beneath the player
while the outer coarse terrain is present. The adopted loader is fe6c5e5,
including the prior water/prediction optimizations; do not revert that adoption.

## Evidence boundary

HasRenderDescendant means any finer active leaf, not a complete replacement
for a coarse volume. Both exterior admission and local additions reject parents
with finer descendants to avoid overlap. Current immediate fine requests have
priority over uncovered exterior. These source facts identify possible races,
not proof that the user's particular hole was reproduced.

The first instrumented10000-speed figure-eight found zero missing cells among
1040near occupancy samples (including warmup and settlement). It averaged507.46FPS.
The moving screenshot shows coarse ground; it does not show the reported hole.
Its report arrived49.455s instead of intended30s. Preserve that limitation.

## Diagnostics

The existing VoxelManager owns an opt-in recorder (disabled by default): every250ms it checks27LOD0
cells around the current player through the active hierarchy. A fixed256-record
ring retains roughly64seconds; lifetime sample/missing counters remain available.
Begin/end occupancy gaps are logged. Fine/coarse cell counts and assigned regions
without resident records distinguish detail level from absent geometry. Empty
resident regions are legitimate and are not classified as missing geometry.
No SDF sampling, disk writes, scene objects or GPU readbacks occur in this sampler.
Maximum sampling time is reported, including its occasional log operation.

`voxel_coverage_report` writes a JSON report to the game's
`performance/coverage/` data directory and logs its full path. It includes the
recent ring, current local operations, all27near cells, current-field residency,
draw eligibility, queue/count/readback/emit/publication status, water readiness,
ancestor interests and coarse neighbors. Missing cells get conservative density
classification. Surrounding maximum-LOD roots are traversed with a per-root cap
of512nodes and32missingregions; a truncation flag prevents incomplete reports
from implying complete coverage. Missing maximal subregions show where coarse
fallback could be considered without overlapping an existing finer subtree.

`voxel_coverage_trace false` disables sampling; `true` enables it. Existing
`voxel_coverage_info true` separately audits overlap,balance and exact active seams.
Performance result schema29 embeds the recorder snapshot. Reports are explicit,
more expensive diagnostics; they are not emitted every frame. Coverage occupancy
and conservative bounds do not establish an actual visible surface hole. A false
draw-eligibility result can also reflect ordinary visibility/culling. Capture a
matching screenshot and inspect identity/readiness before interpreting it.

## Prototype ownership and alternatives

An uncovered immediate fine request first tries its largest structurally valid
coarse ancestor. The existing LocalReplacement owns that temporary operation and
its original fine requests. The existing parent/descendant and26-neighbor checks
must pass before admission and again before publication. Terrain,water and exact
seams still publish together. Fine requests resume after the coarse commit.
Uncovered exterior moves ahead of fine refinement in the existing priority tuple.

Only immediate27fine requests may add temporary coarse work. Background layout
requests, eight waiting operations,0.75mslocalbudget,CPU/GPU meshing,readbacks,
source identity and cache limits retain their current owners. No render overlap,
second mesh system,unbounded radius,or new thread is introduced. Admission stays
on the game thread; stale pending operations requeue through the existing path.
Final layout publication adopts/replaces the temporary coverage.

Alternatives: drawing a parent over partial children creates overlap and is
rejected; removing children before a parent is ready creates the very gap at
issue; increasing generation budgets risks FPS. More general coarse subregion
admission for missing areas outside the immediate requested cells is deferred
until reports establish that requirement. This prototype does not promise
continuous ground at arbitrary flight speed or with every terrain feature.

The ledger owns fixed criteria and run outcomes. Runtime acceptance is pending.

## Outcome

The coarse-first behavior was rejected and restored. No gap fix is claimed.
The tested route never exercised a temporary coarse fallback; at the recorded
moving point all27near cells had resident coarseLOD5coverage and the surrounding
root had no missing subregions. The user's specific edge-entry hole remains
unreproduced. Initial occupancy sampling alone also cannot exclude a presentation
problem or a hole outside its27-cell neighborhood.

| Run | FPS | p99ms | Near detail seconds | Drain seconds |
| --- | ---: | ---: | ---: | ---: |
| Initial instrumented control | 507.46 | 8.20 | 1.498 | 14.225 |
| Coarse-first prototype | 474.97 | 8.68 | 1.892 | 14.211 |
| Final diagnostics, recording on | 466.89 | 8.97 | 1.898 | 14.318 |
| Same source, recording off | 396.42 | 9.69 | 1.02 | 31.611 |

The slower recorder-off control does not establish that recording improves FPS.
Session variation is too large for a reliable causal overhead claim. All runs
remain preserved; no cherry-picked speedup or global performance acceptance.
Consequently recording ships disabled by default and explicit reports remain
available. The recorder-off route exercised that runtime setting through its
public command; the final one-line default change selects the same setting on
new sessions. The final code retains no prototype scheduling change.

When enabled, the largest measured sampler cost was0.3482ms; final diagnostics
measured0.3333ms. These costs cover only the bounded sampler,not the explicit
report command. The final diagnostics and recorder-off routes passed moving
coverage checks and final88/88mesh audits with zero stale/geometry failures,
zero exceptions and settled queues. This does not validate a missing-region
replacement: that path was not exercised and was removed.

For the next actual occurrence, enable `voxel_coverage_trace true` before flying,
then run `voxel_coverage_report` while the hole is visible and capture the view.
The report logs its saved file path. `voxel_coverage_trace false` stops sampling.
Investigate the largest missing subregion and its actual preparation/publication
state before changing topology ownership. Additional reproduction at the user's
actual speed,view distance,altitude and entry direction is still required.
