# Terrain Collision Prototype

Current source contains a CPU collision prototype. It is **not accepted**:
0.01-unit endpoint welding plus connected support patches pass startup and
static contact checks, with zero observed native failures in the latest route.
Frame tails, allocations, maximum publication time, benchmark comparability,
and dynamic contact across patch overlap remain unresolved. The
[validation ledger](../ValidationResults.md#terrain-collision-prototype-001v1---cpu-collision-path)
owns results and acceptance. The [research](../Research/TerrainCollisionFirstSlice.md)
owns alternatives and original assumptions.

## Ownership and field

`VoxelManager` owns one `VoxelCollisionWorld`, separately from `GpuVoxelMesher`.
Both consume the canonical procedural SDF and applied settings/content revision.
The collision owner has per-coordinate desired/readiness records and native
static bodies; these are disposable derivatives, never authoritative density.
No render geometry, GPU readback, visual transition mesh, or visual residency
dependency enters the collision path.

The prototype supports gameplay radii 0 through 8, including the authored radius
8 workload (4913 logical coordinates). Larger requests are rejected explicitly;
the old analytic allowance through 128 is not a safe collision budget. Default
manager radius remains 4; scene-authored values are unchanged. Full-resolution
collision uses 32 cells per axis at 16 world units, including beyond current
LOD0 visual coverage. Coarse visuals can differ there.

`VoxelCollisionMesher` extracts regular Transvoxel triangles into chunk-local
positions and 32-bit indices. `Tools/generate_collision_tables.py` generates its
C# table view from the existing canonical HLSL tables. Do not edit the generated
view or maintain separate topology inputs. Neither shader source nor GPU tables
change in this prototype.

Sampling preserves the renderer's corner order, negative-inside case bits,
near-zero density adjustment, edge interpolation, and regular triangle winding.
Shared boundaries use integer global samples. Collision-only edge intersections
within 0.01 world units of a lattice endpoint snap to that endpoint. Incident
edges reuse a corner index within each chunk; neighboring chunks independently
choose the same global corner. Surviving triangles therefore reconnect instead
of leaving the original tiny triangle boundary open. This user-approved
approximation moves vertices by at most the tolerance before floating-point
coordinate rounding; it does not promise identical microscopic footprints.
`VoxelCollisionMesher.VertexWeldTolerance` owns the tolerance. The extractor discards only exact
zero-area/repeated-index output, counts those discards, and rejects non-finite
density. CPU/GPU bitwise equivalence is not claimed.

After sampling dominated the initial measurements, the production extractor
uses four-cell parent bounds followed by two-cell child bounds through the
canonical conservative AABB query. Parent classifications are reused within
one build in a 512-byte workspace; no sample data survives as world authority.
Only strictly negative/positive ranges skip extraction. Uncertain blocks sample
their exact lattice positions; adjacent blocks reuse the same sampled values.
One worker owns the reusable density, sample-validity, edge-index, and block
scratch arrays. There is no recursive classifier, alternate SDF, CPU renderer,
or approximate density interpolation.

The canonical CPU SDF also evaluates its existing depth envelope before cave
noise. Where `envelope <= surfaceDensity`, the final
`max(surfaceDensity, min(caves, envelope))` is necessarily `surfaceDensity`.
Only irrelevant noise evaluations are skipped; settings, field formula and
generator version remain unchanged. GPU evaluation retains its equivalent recipe.

## Work and native resource lifecycle

One async worker chain uses `GameTask.WorkerThread`, cancellation tokens and a
`SemaphoreSlim` wakeup. It yields while no work exists or two completed results
await integration. The installed whitelist rejects `Monitor.Wait/PulseAll` and
volatile fields; those paths were removed before the first live run. Queue
exchange is lock-protected; no worker creates or changes scene/physics resources.

Interest updates retain still-needed records/completions, cancel leaving work,
and sort missing coordinates by squared distance then Z/Y/X. Content revision
changes mark requested collision unready, cancel old requests, and transfer
existing native support to new records for coordinates still in interest.
That support remains until a validated mesh or empty replacement succeeds;
failed builds retain it. Departure coordinates retire normally. Canceled/removed records cannot
publish, even if their coordinate re-enters: a new record owns the new request.

On the engine thread, manager updates retire obsolete bodies and integrate
completed results with an initial 0.5 ms admission budget, at most one native
construction per update. A native call is indivisible and can exceed the budget;
reported creation/retirement durations measure actual wrapper calls, not a
guarantee about deferred native work. Full-world destruction releases remaining
bodies and cancels the worker; it is not the moving-frame retirement path.

Each nonempty result creates one disabled static `PhysicsBody`, associates it
with the manager, applies the chunk origin once, calls `AddMeshShape`, verifies
valid mesh type, and adds tag `voxel_terrain` before enabling it. Empty geometry
marks readiness without allocating a body. Exactly three reusable CPU mesh buffers cover one worker and two completed
results. A buffer returns to the available queue only after native submission
or stale-result discard; no resident per-chunk CPU mesh is retained. Buffers
retain bounded chunk-topology capacity until world teardown. Native acceleration-structure memory is not available as a measured
per-body byte count; reported resident geometry bytes are only submitted payload, including bodies
queued for retirement until native removal. Teardown verifies released handle
validity before reporting zero remaining bodies.

An invalid native result is removed and reported as failed, never published as
empty or as its fallback sphere. Errors persist through performance-counter
reset. There is no automatic retry loop. Two observed one-triangle fragments
approximately 0.0002 units across triggered that failure before endpoint welding.
The user approved close-enough connected collision; the current candidate uses
0.01 world units (0.254 mm) after the proposed increase and continuation; welding
applies before native submission, including to surrounding triangles, rather
than treating a native failure as empty. WeldedIntersections counts affected
unique edges per chunk, including exact endpoints. DegenerateTriangles includes
triangles collapsed by welding. Native validation remains unchanged.

## Small-fragment support

Native rejection has been observed for isolated chunk triangles spanning about
0.0002 through 0.0195 units. The public wrapper does not expose a minimum
triangle threshold. Recentring the 0.0195-unit triangle failed too; that
unsuccessful representation change was removed. Do not treat observed sizes
as a universal engine cutoff or silently increase the weld tolerance.

When the entire nonempty chunk mesh has used-vertex bounds strictly smaller
than one cell on every axis, the same extractor rebuilds a connected field
neighborhood: floor/ceil those bounds to the lattice and expand by one cell.
This is at most 4^3 cells and can extend one cell beyond its owner chunk.
It preserves the small surface with neighboring terrain, rather than submitting
an isolated clipped fragment. Some triangles overlap neighboring chunk bodies;
contact behavior at that overlap still requires validation beyond static traces.
An empty rebuilt patch is an error. Truly isolated tiny features can still be
rejected by native physics; the shape-type check remains mandatory.

The original region owns, publishes, and retires the support patch. It does not
confer readiness on another region. Future local edits must also invalidate
owners whose one-cell support dependency intersects the edit. Current content
resets already invalidate all regions. No edit storage or mutation path is added.
`SupportPatchRebuilds` counts integrated patch builds; timing and density sample
counts include initial plus replacement extraction, while geometry counts refer
to the submitted output.

## Gameplay readiness

`IsTerrainCollisionReady(BBox)` checks every intersecting chunk, including
support outside the center chunk and negative coordinates. Outside requested
coverage, invalid bounds, and pending/failed records return false.

Before each physics step, the manager checks non-proxy moving rigidbodies,
including the player's existing Rigidbody, against their current bounds plus
one velocity step and a 16-unit margin. Unready bodies have motion held; their
player controller is disabled while held. Once ready, saved velocities and the
previously enabled controller are restored. Bodies outside single-origin
interest remain held. This is prototype simulation gating, not multi-origin
interest management or a guarantee for arbitrary scripted teleports.

The origin player has been observed grounded on the terrain manager with zero
velocity after release. Native ray traces cross the nine recorded seam probes.
Walking, cave traversal, extreme velocities/rotations, distant NPC simulation,
network ownership, full lifecycle/configuration transitions, and arbitrary
scripts that move bodies while held remain unverified. A triangle shell does
not provide volumetric recovery for an actor spawned inside solid terrain.

There is no live edit API or region-local mutation publication in this slice.
Current content resets invalidate the entire field and hold simulation until
new readiness; retaining old collision during incremental edits, waking resting
bodies, and resolving newly enclosed actors belong to the deformation slice.

## Diagnostics and acceptance

`voxel_collision_info` reports readiness, queues, native body counts, failures,
completed-result bytes, submitted geometry bytes, sampling/extraction/creation/
retirement/request-to-ready distributions, and current player grounding/support.
The support observation uses a terrain-only sweep of the actual controller body
box; a point ray under the actor origin is insufficient on sloped terrain.
`voxel_collision_trace x y [top] [bottom]` is a bounded read-only vertical
terrain ray. Neither command builds geometry or moves gameplay objects.

Schema 25 adds typed collision metrics and held physics-step counts to the
existing figure-eight result. Stage samples are bounded at 65536 and disclose
truncation. Stage distributions cover integrated, non-stale results; discarded
work is counted separately and is not claimed to have zero CPU cost. Do not
interpret creation wrapper time as the total subsequent physics-step cost.

The canonical figure-eight route, speed, distance, fixed Z, and visual workload
remain unchanged. Test startup additionally requires collision settlement.
That route deliberately positions a player and does not prove ordinary contact.
The latest candidate run has zero native failures, but still fails frame-tail,
allocation and maximum publication-time gates. Its stationary window still had
collision work pending. The runner forces the player through solid terrain at
Z=0 and lets physics move the player afterward. The user selected the unchanged
standard benchmark; its coordinate and collision-settlement comparability
limitations remain recorded. A fresh pre-collision standard run also shows
the large later FPS drop, so it cannot be attributed entirely to collision.
No successful collision acceptance run exists yet; preserve failures and pass
all predeclared gates before committing or describing the path as proven.

Production profiler snapshots include collision interest changes, engine-thread
integration, and actor readiness checks. Interest scope excludes the unchanged
interest fast return. The existing 200-frame history does not measure full-route
worker CPU or every streaming boundary. VoxelPerformanceProfiler builds its name
list on capture so editor hotload cannot preserve an obsolete static registry.
Candidate 9b in the validation ledger establishes emitted scope entries and
records the remaining performance failures.

The working-tree canonical CPU sampler now evaluates cheese/thickness before
noodle terms. A strict upper-bound comparison skips remaining noodle noise only
when it cannot affect the composed density. The unresolved path retains the
original arithmetic and generator version; no approximate field or GPU change
is introduced. Candidate 10 records matching startup geometry/contact results,
a modest sampling improvement, unchanged aggregate FPS, and unresolved frame
tails. This is not accepted performance or exhaustive numerical validation.

Candidate 12 retains hierarchical rejection as the current experimental path:
startup geometry/contact checks match, construction throughput improves over
candidate 10, and stale cancellation was observed. Final residency differs on Y
in that run, so aggregate geometry is not a comparable acceptance result. The
original frame-time gates remain unmet; see the ledger for measurements.

Replacement ownership was corrected in candidate 13. A successful replacement
removes the previous body in the same manager update after candidate validation
and activation; payload/body accounting drops the previous allocation. No physics
step is intended between activation and retirement. Failure leaves old support
owned by the unready request. Runtime configuration-revision validation is still
unverified: installed native component lookup selected the editor copy and the
attempt to disambiguate by closing the saved editor tab also stopped play.
Ordinary figure-eight streaming passed without collision failures, but performance
regressed and a 14.6522 ms creation exceeded the 10 ms gate. No acceptance claim.


Experimental result schema26 adds a bounded MeshCreation elapsed-time
distribution around completed AddMeshShape calls. Creation retains whole
body-setup/shape/publication timing. The worst whole call also reports its
coordinate, vertex/index counts and phase timings; failed calls do not enter
MeshCreation. These elapsed measurements include possible scheduling/GC pauses
and are not isolated native CPU time. Candidate17 verifies emitted metrics and
phase accounting, but does not pass performance acceptance.


Candidate18 uses the generator's build-local LatticeSampler to reuse its
XY-dependent surface term across vertical samples. One33x33 float array and
matching flags belong to the collision worker, reset on every build including
support patches. The generator owns the dependency and exposes only full density
to collision. Cave evaluation and composition share ordinary point-query code.
Nothing persists across chunks or field revisions. Additional array payload is
5445bytes, not authoritative terrain storage. Construction improved in the
recorded experiment; full acceptance remains unproven.
