# Terrain Occlusion Research

Date: 2026-09-08. Status: research and experiment constraints only; no runtime
implementation or new performance result is established here.

## Objective and hard constraint

Reduce GPU work for cave surfaces hidden by ground, terrain behind mountains
and ridgelines, and underground surfaces hidden by cave walls. Preserve visible
entrances, overhangs, tunnels, edits, and every enabled LOD transition.

**Do not render terrain twice to obtain occlusion depth.** The user reports that
the previous experiment required an additional depth pass and that its cost
eliminated the rendering savings. On 2026-09-08 the user explicitly rejected
repeating that approach. Treat this as a project constraint, not another
candidate to benchmark by default.

Specifically, reject a terrain depth prepass followed by another terrain draw
for shading/culling, or an equivalent duplicate terrain submission disguised as
an occlusion pass. A depth copy or reduction is not itself a second geometry
draw, but its bandwidth, dispatch, and synchronization costs still count. This
distinction is not permission to introduce a separate proxy-depth renderer.
Revisiting the rejected design requires explicit user approval supported by new
evidence; a predicted speedup is insufficient.

The recollection is user-supplied historical evidence. The inspected repository
contains the pre-execution `TERRAIN-HZB-001/v1` definition, but no recovered HZB
candidate source or measured outcomes. Do not invent timings or attribute the
earlier loss to a particular implementation bug. Preserve the older ledger
definition as history; it does not override this newer constraint.

## Current implementation and evidence limits

The [GPU meshing contract](../Architecture/GpuVoxelMeshing.md) owns implemented
behavior. Source inspection on this date established:

- The authoritative field supplies regular and transition meshes. Conservative
  field bounds skip provably solid/air regions; hidden cave surfaces remain
  potential geometry. This is not line-of-sight occlusion.
- `Assets/shaders/voxels/voxel_chunk_visibility_cs.shader` checks active records,
  nonzero geometry, and conservative camera-frustum bounds. It writes indirect
  instance counts, with no depth-based occlusion test.
- `Code/Voxels/GpuVoxelMesher.cs` submits arena buffers through indexed-indirect
  draws attached at `Stage.AfterOpaque`. Individual regions are not separate
  engine scene objects. Arena allocation order is not a visibility ordering.
- `Assets/shaders/voxels/voxel_terrain.shader` enables back-face culling and depth
  writes. Depth rejection can save hidden fragment shading, but it does not
  eliminate the preceding vertex and triangle work or resident mesh storage.
- Occlusion during drawing alone does not save extraction, collision, streaming,
  or resident geometry memory. Those require separate measured responsibilities.

The historical accepted frustum candidate recorded approximately 76.6% fewer
nonzero draws and full-frame GPU average falling from 1.138 ms to 0.531 ms.
See the [validation ledger](../ValidationResults.md), final GPU frustum candidate
2026-08-28. That older workload is evidence that one culling approach succeeded,
not a current cave-occlusion baseline or expected speedup.

Current hidden-surface GPU cost has not been isolated. Count rejected triangles
and records separately from GPU time saved; fewer draws do not prove a win.

## What Facepunch provides

Public source inspected at commit
`a0b002cdbff9abfade9afd4cb205230a55120121`:

- [TerrainClipmap.cs](https://github.com/Facepunch/sbox-public/blob/a0b002cdbff9abfade9afd4cb205230a55120121/engine/Sandbox.Engine/Scene/Components/Terrain/TerrainClipmap.cs)
  builds reusable grid blocks in nested LOD rings.
- [TerrainClipmapSceneObject.cs](https://github.com/Facepunch/sbox-public/blob/a0b002cdbff9abfade9afd4cb205230a55120121/engine/Sandbox.Engine/Scene/Components/Terrain/TerrainClipmapSceneObject.cs)
  tests patch bounds against the camera frustum on the CPU and draws the surviving
  instances; shadow passes draw the full patch set. Its term "meshlet" describes
  instanced grid blocks, not proof of task/mesh-shader availability.
- [Terrain.Rendering.cs](https://github.com/Facepunch/sbox-public/blob/a0b002cdbff9abfade9afd4cb205230a55120121/engine/Sandbox.Engine/Scene/Components/Terrain/Terrain.Rendering.cs)
  supplies heightmap data. That representation does not contain our volumetric
  cave walls. The inspected terrain path has no terrain-to-terrain occlusion test.

Adopt the lessons of reusable geometry, bounded LOD, and inexpensive visibility
work. Do not replace volumetric terrain with a heightfield or copy the CPU work
division merely because it belongs to Facepunch. Public source is not proof of
the installed native renderer's exact behavior or game-code access.

[SceneCullingBox](https://sbox.game/api/Sandbox.SceneCullingBox/) explicitly hides
scene objects inside/outside volumes. It does not discover terrain visibility or
filter records in our custom indirect buffers. No automatic engine switch for
our region-level occlusion was verified. The public static-meshlet roadmap is
not a shipped integration contract for this renderer.

Installed XML snapshot `26.09.01c` documents `CommandList.GrabDepthTexture` and
compute/indirect facilities already used by production. A depth-copy API does
not establish that its contents include terrain at the required time. Verify
current-view dimensions, reverse-Z convention, render ordering, lifetime,
resource transitions, and whitelist access before any implementation.

## Candidate directions without a second terrain depth pass

These are hypotheses, not an approved implementation backlog. Select one only
after measuring the cost it could remove.

| Direction | Potential benefit | Required evidence and limits |
| --- | --- | --- |
| Guard unused visibility diagnostics | Avoid counter atomics outside measurement windows. | Current shader updates frame counters even when aggregation is disabled. Measure separately; this is visibility overhead reduction, not occlusion. |
| Approximate front-to-back order in existing draws | Nearby ground or mountains establish depth earlier, reducing hidden shading in the same draw pass. | Keep arena ownership; account for sorting, uploads, and command rebuilds. Does not skip vertex work, and distance-to-bounds is only an approximation. |
| Reuse depth already produced by normal rendering | A depth hierarchy could reject hidden regions without drawing terrain again. | Same-frame depth before our terrain draw lacks that draw's terrain. Prior-frame depth is stale under movement, edits, and publication. No safe integration is established. |
| Conservative solid-volume occlusion | Derived regions proven fully solid could reject terrain behind ground or mountains without rasterizing another depth pass. | Must prove complete screen-space occlusion of candidate bounds, including near-plane cases; sparse rays or a chunk center test are insufficient. Never use a surface chunk's whole box as a solid occluder. Cost of producing/querying proofs must be bounded. |
| Cave connectivity and portal visibility | Avoid considering sealed cavities or branches hidden beyond openings. | Connectivity alone does not establish visibility; an entire connected cave network may still be mostly hidden. Coarse sampling must never seal a real opening. Exterior mountain occlusion needs another geometric proof. Potentially substantial topology and edit-invalidation work. |
| Smaller visibility groups within existing meshes | Separate visible ground from hidden cave triangles in the same chunk. | Tighter bounds can improve later tests but do not prove occlusion alone. Extra records, extraction metadata, and indirect processing can erase gains. Preserve one mesher and allocator. |

For depth reuse, merely saying "use last frame" is insufficient. Occlusion must
be valid for the current camera and current published occluders. Camera motion,
cuts, resized views, uncovered pixels, missing depth, edits, and recycled mesh
allocations must invalidate uncertain results before they hide visible geometry.
Rendering all uncertain regions is correct but may erase savings during the
figure-eight; stationary-only improvement cannot establish moving acceptance.
Do not repair disocclusion by adding the prohibited duplicate geometry pass.

For solid-volume or portal methods, derive metadata from the canonical edited
field with conservative guarantees. The procedural density function is not
established as an exact Euclidean distance field: arbitrary sphere-tracing steps
or a few SDF samples cannot prove a region solid or an entire object hidden.
Avoid evaluating the full procedural field per region every render frame.

## Ownership, invalidation, and budgets for a future slice

- Inputs: current camera/view identity, published regular/transition descriptors,
  conservative bounds, and any explicitly revisioned derived occlusion metadata.
- Output: visibility of existing draw records only. Keep meshing, authoritative
  terrain, collision, networking, and streaming membership unchanged.
- Owner: extend the mesher's existing per-view visibility lifecycle. Metadata is
  disposable derived state, never another terrain truth or replicated authority.
- Invalidation: consume existing publication/removal and field-change ownership;
  reject stale epochs, revisions, and allocation identities. Removing an
  occluder can reveal distant terrain, so invalidate its dependent visibility,
  not just candidates intersecting the edit. New views start conservatively.
- Threading: engine-supported GPU work and resource lifetime only; no synchronous
  visibility readback, CPU wait for GPU results, or per-frame geometry readback.
- Scale: account for all enabled LODs, transition records, allocated record slots,
  active views, and moving publication. A main-camera mask cannot be reused for
  future shadows or other cameras without their own visibility proof.
- Budget: before coding, record a numerical CPU, GPU, metadata-memory, and
  update-work ceiling from a fresh comparable baseline. Break-even requires
  saved rendering time to exceed every added test, copy, reduction, barrier,
  upload, and invalidation cost by a margin larger than measured run variation.

No numerical budget or winning candidate is claimed by this research document.

## Experiment and acceptance plan

First attribute terrain drawing versus extraction, visibility, and other GPU
work in the playable world. Determine whether hidden geometry processing or
hidden shading is material. If suitable GPU timing is unavailable, report the
attribution gap rather than substituting draw counts for timings.

Before the first candidate run, define exact parameters and measurable gates in
[ValidationResults.md](../ValidationResults.md). Reuse the applicable canonical
figure-eight unchanged and compare against the latest accepted comparable
baseline, or establish one first. The older HZB workload is not automatically
comparable to today's multilevel terrain and engine.

Supplemental correctness coverage must include above-ground views over cave
roofs, a mountain hiding another mountain, ridgeline reveal, entering/leaving a
cave, narrow openings and overhangs, inside/near candidate bounds, rapid turns,
camera cuts, negative coordinates, LOD seams, streaming publication, and edits
that open or close an occluder. Record exact seeds, coordinates, trajectories,
camera settings, edits, warmup and durations before execution. These are coverage
requirements, not already-defined executable scenarios or permission to add
test-only scenes/components/hooks.

Record full-frame GPU average/p95/p99, frame pacing, available terrain/visibility
GPU timings, CPU overhead, memory, allocations, tested/rejected regions and
triangles, indirect records/submissions, invalidation rates, chunk completion,
and backlog. Preserve failed runs. Require a repeatable net gain beyond noise,
no material tail-latency or streaming regression, and no false occlusion,
flicker, delayed reveal, or lost transition coverage. Reject candidates that
need the forbidden second terrain depth draw to meet correctness or performance.

## External evidence and adopted limits

[NVIDIA GPU Gems, Efficient Occlusion Culling](https://developer.nvidia.com/gpugems/gpugems/part-v-performance-and-practicalities/chapter-29-efficient-occlusion-culling)
explains geometry rejection versus early depth rejection, synchronization costs,
front-to-back order, and loose bounding boxes. Adopt those cost distinctions.
Its legacy query APIs and tolerance for temporal popping are not adopted; it
does not establish modern s&box timings or a suitable public query API.

The Facepunch sources above support the heightfield/clipmap comparison only.
Neither those sources nor this proposal demonstrates a faster Voxels3 occlusion
implementation. The next decision is which measured cost warrants one bounded
experiment under the no-duplicate-depth constraint.
