# Terrain Collision: First-Slice Research

Date: 2026-09-07. Status: research and proposed design; collision is not implemented.
No collision performance or runtime correctness result is claimed by this document.

## Recommendation and scope

Start with background CPU extraction of full-resolution static triangle meshes
inside the existing single-origin gameplay cube. Keep GPU visual extraction
independent. Both consume the canonical terrain field; neither mesh is world
state. This is the initial production candidate, not a demonstrated fastest
implementation. Native collision construction may dominate its cost.

The first implementation slice should provide contact for players, NPC bodies,
and ordinary physics objects within that cube. Exclude mining/deformation,
navigation generation or updates, edit replication, persistence, and multiple
gameplay origins. Do not promise that distant actors retain collision after
leaving the supported simulation region. NPC navigation is a separate consumer:
s&box builds navigation from the physics world, but collision alone does not
implement navigation lifecycle or pathfinding ([navigation documentation][nav]).

## Current repository evidence

Inspected source base: `f4e809e748dfedde9dbf6fd7a818800b0b58313f`, with existing
uncommitted scene, GPU research/shader, and validation changes present. This note
does not accept those experiments or use their measurements as collision results.

| Responsibility | Current evidence and implication |
| --- | --- |
| World state | [VoxelChunk](../../Code/Voxels/VoxelChunk.cs) is an immutable view over [ProceduralTerrainSdf](../../Code/Voxels/ProceduralTerrainSdf.cs), not a stored density array. Generator version 5 includes surface, tunnels, and caverns. Collision must sample that same field. |
| Coordinates and interest | [VoxelManager](../../Code/Voxels/VoxelManager.cs) owns floor-based chunk conversion, 32 cells per axis, 16 world units per cell, and 512-unit chunks. Default `GameplayRadius=4` means 729 logical coordinates, not 729 allocated colliders. |
| Visual ownership | [GPU meshing](../Architecture/GpuVoxelMeshing.md) describes independent clipbox placement, GPU sampling/extraction, persistent geometry, and zero production geometry readback. [GpuVoxelMesher](../../Code/Voxels/GpuVoxelMesher.cs) owns those derivatives. |
| CPU competition | The manager already runs a serialized background render-preparation chain and bounded main-thread integration. A collision worker still competes for CPU, memory bandwidth, and engine-thread time; GPU/CPU separation does not eliminate contention. |
| Implementation status | [Voxel foundation](../Architecture/VoxelChunkFoundation.md#scope) explicitly excludes collision, live edits, project-specific replication, and multi-origin interest. No terrain collision owner exists in runtime source. |

Full resolution means the existing 16-unit sample grid and its extracted
piecewise-linear surface, not exact collision against the continuous analytic
SDF. The default visual LOD0 coverage has 512 active coordinates; the gameplay
cube has 729 and uses a different snapping policy. Full-resolution collision
can therefore differ from displayed coarse triangles near the gameplay edge.
It must not change resolution as a camera's visual LOD changes. Expanding fine
visual coverage to contain gameplay is a separate render-policy decision with
its own performance validation, not an implicit part of this recommendation.

### Geometry contract to preserve

Use the repository's [regular tables](../../Assets/shaders/voxels/transvoxel_regular_tables.hlsl),
[classification](../../Assets/shaders/voxels/voxel_persistent_geometry.hlsl),
[vertex emission](../../Assets/shaders/voxels/voxel_emit_vertices.hlsl), and
[index emission](../../Assets/shaders/voxels/voxel_emit_indices.hlsl) as the
current extraction contract. The [Transvoxel reference][transvoxel] explains
the algorithm; the checked-in tables and their [license](../../Assets/shaders/voxels/LICENSE-Transvoxel.txt)
establish the imported data to preserve.

- Negative density is solid. Extraction changes magnitudes below `1e-6` to
  signed `1e-6`; exact zero takes the positive branch. Case bits use `< 0`.
  Do not substitute the material rule `density <= 0` for surface classification.
- Preserve corner order and edge identity. Interpolation is clamped
  `d0 / (d0 - d1)` when the denominator magnitude exceeds `1e-6`, otherwise
  `0.5`, using the adjusted densities. Regular triangles retain table index
  order; do not import the transition mesher's separate winding adjustment.
- Sample shared boundaries from identical integer global coordinates, including
  negative coordinates. Preserve the CPU generator's float operations and
  settings. Matching recipes does not prove bitwise CPU/GPU equivalence near
  sign changes; seam/contact and visible alignment checks remain required.
- Produce indexed chunk-local positions and 32-bit indices; apply the canonical
  chunk origin once through body placement. Physics needs neither render vertex
  metadata nor shading normals. A `33^3` sample workspace suffices for regular
  positions; the render pipeline's `35^3` gradient halo is not required here.
- Reject non-finite positions and invalid indices before native submission.
  Skip truly zero-area/repeated-index triangles and count them; do not silently
  remove small valid triangles using an arbitrary simplification tolerance.
  Verify native winding/contact behavior before shipping, rather than assuming
  renderer front faces prove physics behavior.

Do not create a CPU reference renderer or alternate procedural generator. A CPU
collision extractor is a distinct production responsibility permitted by the
[meshing route](../AgentRoutes/meshing.md). The GPU document's rejection of CPU
density fields/topology concerns replacing or duplicating the visual path.
Clarify that scope when implementing collision: transient collision workspace
must not become render input or another mutable world representation. Preserve
one topology data source when making the existing tables available to C#; do
not introduce independently maintained algorithm variants.

## Alternatives and decision

| Candidate | Benefits | Costs and decision |
| --- | --- | --- |
| CPU regular-cell extraction, static triangle shapes | No render completion/readback dependency; bounded near-gameplay work; viable architecture for a headless physics consumer; local future rebuilds. | Repeats field evaluation on CPU; competes with existing preparation; requires parity and native cooking measurements. Recommend first. Dedicated-server operation is not yet tested. |
| GPU extraction plus geometry readback | Can reuse exact emitted triangles where compatible geometry is resident; avoids CPU field extraction. | Needs transfer buffers, synchronization, revision/lifetime retention, and CPU physics construction anyway. Visual LOD0 does not cover all gameplay, so direct reuse is insufficient. A separate full-resolution GPU collision request still depends on graphics support. Reject as the initial path, not as universally slower. |
| Heightfield collision | Compact representation for a single elevation surface. | Cannot represent the existing tunnels, overhangs, and cavern interiors. Reject for general terrain. |
| Convex hulls, boxes, or simplified meshes | May reduce triangle cost for some workloads. | Hulls fill concavities; decomposition and approximation introduce cost and fidelity policy. Reject in the full-resolution first slice. |
| Direct SDF character queries/custom solver | Potentially useful for specialized traces or movement. | Does not by itself integrate ordinary rigidbody and NPC contacts into engine physics; creates substantial solver responsibility. Reject as the general collision replacement. |

[Voxel Tools' performance documentation][voxel-performance] reports costly
collision acceleration-structure creation and main-thread integration in its
Godot implementation. Adopt the lesson to measure extraction and native shape
creation separately, and bound pending data. Do not adopt its timings, relative
cost multiplier, thread-safety restrictions, or worker-count policy as s&box
facts. Its discussion of thin mesh surfaces also motivates real-world tunneling
checks; this is not proof of s&box's exact contact behavior.

## Engine evidence and limits

Installed XML snapshot: `Sandbox.Engine.xml` under the default s&box
`bin/managed` directory. Engine identity returned by the skill lookup was
`26.09.01c`, `33901499107`, `build-pr`, `handsomematt`,
`04/09/2026 17:37:31`. These are raw build fields, not an inferred Steam branch.

| Evidence | Established behavior | Limit |
| --- | --- | --- |
| Installed XML: `PhysicsBody.AddMeshShape` | List and span overloads accept `Vector3` vertices and integer indices. Comments describe mesh shapes as unsuitable for physical simulation of the mesh itself, and creation as fallible. | Use static terrain contacted by dynamic bodies. XML is incomplete and can include inaccessible APIs; compile availability remains to be checked in game code. |
| Installed XML: `PhysicsShape.UpdateMesh` | Both overloads describe mesh recreation. | No cheap refit, asynchronous cooking, or time bound is promised. |
| Installed XML: `PhysicsShape.Remove`, `PhysicsBody.Remove` | Shape removal releases its handle; body removal removes the body. | Scene teardown, replacement ordering, and contact behavior need runtime verification. Never reuse removed handles. |
| Official [creation API][add-mesh] and [update API][update-mesh] | Public documentation of the mesh entry points. | Online staging can differ from installed binaries. |
| Staging schema snapshot | Downloaded `2026-09-07T01:56:48.794719+00:00`, [release schema][schema], SHA-256 `e5892a154eefecc65628179ea3af9e007191617ddff6b73f20ae15ca138ed725`; exposes the two overloads and `PhysicsShape.IsMeshShape`. | Schema presence is not installed compiler or runtime proof. |

Pinned Facepunch source, commit `9de061bb0fe2dc73ff29a134a0041928f2a47166`:

- [PhysicsBody.cs][body-source]: empty creation inputs return null; invalid
  indices throw. After the native call the wrapper warns for an invalid result
  or unexpected sphere result, then returns the result. Therefore validate the
  actual created shape type/validity; non-null alone is insufficient.
- [PhysicsShape.cs][shape-source]: updating a non-mesh throws; empty input
  returns without clearing the shape; valid input enters a native update call.
  A rebuilt empty region therefore requires explicit removal, not an empty
  `UpdateMesh` call. `Remove` invalidates use of that released shape.

These are public managed-wrapper observations, not version-matched native
implementation evidence. Neither synchronous wrapper code nor lack of a thread
assert proves worker-thread safety, when native work finishes, or how expensive
its acceleration structures are. Keep creation, attachment, replacement, and
removal on the engine thread initially. Confirm supported lifecycle timing,
input-buffer ownership, static-body setup, tags/surface handling, and game-code
availability before shipping. The engine's internal unsafe implementation does
not require unsafe blocks in project code.

## Proposed first-slice ownership and flow

The following are proposed internal contracts, not existing or newly added APIs.

| Boundary | Minimum contract |
| --- | --- |
| Manager to collision owner | Applied terrain settings and spatial layout, current gameplay bounds, content epoch and per-coordinate build identity, cancellation/lifetime information. |
| Worker request | Immutable coordinate, sample origin, dimensions, settings, source identity. No scene objects, GPU buffers, or shared mutable samples. |
| Worker result | Same identity; mesh, proven/no-extracted-surface empty result, or explicit failure; positions/indices and sampling/extraction/output-size measurements. Empty is not failure. |
| Collision owner to gameplay | Readiness query for all chunk coordinates intersecting an actor's required swept bounds. Distinguish ready mesh, ready empty, pending, failed, and outside supported interest. |
| Collision owner to diagnostics | Desired/ready counts, queue ages and bytes, stage latency distributions, stale/canceled/failure counts, native calls and residency. Extend existing production diagnostics when implemented. |

`VoxelManager` owns the interest/configuration decision and one collision
subsystem. That subsystem alone owns desired collision records, pending work,
completed buffers, static bodies/shapes, and their retirement. Begin with one
static body/mesh per surface-containing chunk for simple independent lifetime;
do not combine the whole gameplay cube into one cook/replacement unit. Empty
regions retain readiness metadata without a collider. Conservative field bounds
may reject definitely air/solid chunks; underground does not imply solid.

### Scheduling and budgets

Use one serialized collision worker chain initially, separate from visual
preparation, with cancellation checks between bounded cell slabs. Do not launch
a task per requested chunk or consume all CPU cores. Start with at most two
completed, unintegrated mesh results; pause admission when that queue is full.
Keep one pending request per desired coordinate and replace superseded requests.
Reprioritize on interest changes by distance to the existing origin, then stable
coordinate order; retain still-needed valid completions instead of restarting
all work after every adjacent move.

Initial supported implementation workload is the existing radius 4 cube.
The current manager accepts radii through 128; that analytic allowance is not
a safe collider allocation budget. Reject unsupported collision configurations
explicitly and retain the last applied valid configuration. Do not silently
reduce the requested gameplay coverage. Broader radius support requires measured
capacity limits before allocation/enumeration.

Initial publication scheduling proposal: admit at most one chunk construction
per frame under a separate 0.5 ms admission budget, then measure the actual call
duration and any deferred physics cost. This is a starting experimental limit,
not an accepted performance target or hard frame-time bound. A single native
call can exceed it. Track retirement in that budget too. If a single chunk cook
exceeds acceptable tails, investigate smaller collision partitions or a verified
engine cooking facility; do not hide it by increasing a budget or claiming that
background extraction solves the stall. Extra workers, pooling, or subdivision
require measurements of the bottleneck before adoption.

One `33^3` float lattice is 143,748 bytes (about 140.4 KiB), excluding managed
overhead. Indexed regular-grid edges have a maximum of `3 * 32 * 33^2 = 104,544`
positions; the existing regular table layout allows at most five triangles per
cell, or 491,520 indices. Float3 positions and 32-bit indices alone therefore
have a conservative per-chunk ceiling of 3,220,608 bytes, before scratch, array
capacity, native cooking, and retained collision storage. These are arithmetic
bounds, not typical memory measurements. Avoid allocating these maxima for
every requested coordinate; bounded in-flight work does not bound native
resident memory by itself.

### Readiness, publication, and retirement

- Initial load: build nearest-first; a visible surface or a logical loaded count
  does not enable terrain-dependent simulation. Gate activation on required
  collision readiness, and expose incomplete/failure status.
- Ordinary movement: retain unchanged collision, enqueue entering regions,
  and remove obsolete requests. Do not let terrain-dependent actors advance
  into unready swept bounds. A priority queue alone is not a safety guarantee.
  The initial design has no speculative outside-cube prefetch; stalls at the
  frontier are an explicit measurable limitation, not permission to fall through.
- Teleports: prepare destination interest through the real gameplay transition
  and keep the arriving actor inactive until its required destination bounds
  are ready. Keep departure support until departure simulation is suspended.
  No synchronous whole-cube rebuild or direct diagnostic origin mutation.
- Outside interest: pause/deactivate terrain-dependent simulation before
  retiring its supporting shapes, including sleeping rigidbodies. Do not leave
  remote actors simulating on missing ground. Supporting independent distant
  actors requires the deferred multi-origin slice.
- Replacement: check source epoch, coordinate identity, and desired membership
  before building native resources and again before publication. Keep the old
  valid collider while the candidate is constructed and validated. Publish and
  retire the old shape at a supported physics-safe engine-thread boundary with
  no simulation step observing a gap or duplicate active collision. Confirm
  that boundary and inactive-candidate behavior in engine integration.
- Empty replacement removes the old shape explicitly. A failed candidate leaves
  the old resource intact, marks the requested revision unready, and reports the
  error; never interpret failure as air or repeatedly retry every frame.
- Configuration reset makes previous collision stale relative to the new field;
  retain it only for already-suspended transition safety, not as ready collision
  for new terrain. Cancel incompatible work. Destruction cancels the worker,
  rejects all late completions, releases owned bodies, and disposes buffers.

The readiness consumer is real gameplay integration required by the collision
slice, not a test hook. Its exact player/rigidbody suspension and restoration
mechanism must be verified against engine APIs before implementation acceptance.
Collision creation alone cannot establish this safety contract.

### Future deformation boundary

Keep identity checks and chunk-local replacement now. A later canonical mutation
path must report dirty sample bounds and invalidate every dependent chunk,
including shared-face/edge/corner neighbors. This note does not introduce that
path, dense edit storage, networking, or a speculative general job framework.
Future edit latency includes acceptance, sampling, extraction, cooking, physics
publication, and visual convergence. Measure those stages before setting an
instant-edit promise. Excavation under resting bodies, terrain appearing inside
bodies, and synchronizing changed visual/collision revisions need separate
deformation behavior and tests; retaining old collision alone is not a solution.

## Implementation measurement and acceptance gates

Before runtime implementation tests, define exact scenarios and thresholds in
[ValidationResults](../ValidationResults.md), using its existing template and
the [performance route](../AgentRoutes/performance-and-testing.md). This research
does not select convenient values from unrun tests or add fabricated ledger runs.

| Gate | Required evidence before implementation acceptance |
| --- | --- |
| Extraction | Sampling/classification and extraction p50/p95/p99/max, completed chunks per second, triangle/vertex counts, buffer capacity and allocations. Include empty, solid, surface, and cave regions. |
| Native integration | Creation and retirement call latency, publication latency, and physics-frame tails, including deferred work. Measure candidate/old overlap memory and allocation spikes. |
| Streaming | Request-to-ready tails, oldest request age, resident/desired coverage, queue count and byte maxima, readiness stalls, cancellation/stale counts. Queues must drain after movement stops and respect configured bounds. |
| Fidelity/contact | Zero observed missing seams, invalid geometry submissions, stale publications, and actor advances into unready terrain. Measure trace/contact alignment in full-resolution visual overlap with a predeclared world-unit tolerance; report coarse-LOD differences separately. |
| Frame and memory regression | Run the unchanged canonical figure-eight against the latest accepted comparable source/environment baseline, or capture a pre-change baseline. Preserve its FPS, frame pacing, streaming, memory, allocation, and correctness gates. No material unexplained regression is accepted. |
| Lifetime | No owned native resources remaining after world teardown; no monotonic resident/buffer growth across fixed revisits; failures never publish as successful empty regions. |

Fix seed/settings, scene, coordinates and route, warmup/duration, player count,
actor sizes/masses/speeds, tick/CCD settings, visual/gameplay ranges, worker and
queue limits, hardware, engine build, source revision, tolerances, and acceptance
thresholds before the first run. Reuse the figure-eight unchanged as the sole
project-owned automated trigger. Additional checks must use real playable-world
entry points and bounded observations, without separate test projects, synthetic
terrain, test-only scenes/components, or alternate extraction paths.

Required contact scenarios include walking up/down slopes; a rigidbody resting
and rolling; entering tunnels/caverns; traversing chunk faces, edges, corners,
and negative coordinates; frontier movement; initial loading and a real teleport
if the shipping gameplay offers one; configuration reset while work is pending;
and teardown/revisit cleanup. Include representative fast/small bodies to expose
tunneling rather than assuming a triangle surface has volume. Do not invent a
teleport API just to mark a scenario passed; document unavailable entry points.

Blocking implementation questions are installed API/compiler access, native
publication ordering, measured per-chunk cooking limits, CPU/GPU surface error,
and the actual gameplay readiness integration. Resolve them in the production
implementation path and ledger before calling the slice complete. Multiple
players, navigation, mining, and dedicated-server behavior remain unverified
unless separately exercised; single-origin research is not multiplayer proof.

## Research validation

This documentation change was checked against the linked runtime/shader source,
installed XML evidence, staging API metadata, pinned engine wrappers, and the
project routes. Runtime code and current architecture implementation status are
unchanged. No in-world collision tests, benchmarks, or new runtime acceptance
results were run for this document.

[add-mesh]: https://sbox.game/api/Sandbox.PhysicsBody/AddMeshShape
[update-mesh]: https://sbox.game/api/Sandbox.PhysicsShape/UpdateMesh
[nav]: https://sbox.game/dev/doc/gameplay/navigation/
[schema]: https://cdn.sbox.game/releases/2026-09-06-06-25-53.zip.json
[body-source]: https://github.com/Facepunch/sbox-public/blob/9de061bb0fe2dc73ff29a134a0041928f2a47166/engine/Sandbox.Engine/Systems/Physics/PhysicsBody.cs
[shape-source]: https://github.com/Facepunch/sbox-public/blob/9de061bb0fe2dc73ff29a134a0041928f2a47166/engine/Sandbox.Engine/Systems/Physics/PhysicsShape.cs
[voxel-performance]: https://voxel-tools.readthedocs.io/en/latest/performance/#physics
[transvoxel]: https://transvoxel.org/
