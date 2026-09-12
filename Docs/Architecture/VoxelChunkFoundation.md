# Voxel Chunk Foundation

## Scope

This document owns terrain state, spatial conventions, procedural generation,
CPU render preparation, and manager diagnostics. The
[GPU meshing document](GpuVoxelMeshing.md) owns visual placement, transitions,
GPU scheduling, allocation, publication, and drawing. Exact validation workloads
and acceptance decisions belong to the [ledger](../ValidationResults.md).

A [terrain collision prototype](TerrainCollision.md) is implemented but not yet
fully accepted. [Terrain deformation](TerrainDeformation.md) now owns the
in-progress live edit contract: one shared correction field, local tool and
GPU/collision invalidation, multiple player collision interests, and versioned
disk commands are implemented. Networking transport, persistence validation and
full feature acceptance remain incomplete. The linked deformation document and
validation ledger distinguish current measured behavior from remaining work;
[research](../Research/TerrainDeformationSecondSlice.md) remains a proposal source.

## Canonical Ownership and Data Flow

- [`VoxelManager`](../../Code/Voxels/VoxelManager.cs) owns one analytic gameplay
  interest cube, bounded LOD0 render-preparation sets and queues, configuration,
  spatial conversions, diagnostics, and one GPU mesher. There is no populated
  dictionary of authoritative gameplay chunk objects.
- [`VoxelChunk`](../../Code/Voxels/VoxelChunk.cs) is an immutable coordinate,
  dimension, and terrain-settings view. It evaluates logical samples on demand,
  owns no density/material arrays, and has no engine resources or network identity.
- [`ProceduralTerrainSdf`](../../Code/Voxels/ProceduralTerrainSdf.cs) owns the
  unedited field and conservative bounds. GPU extraction evaluates the matching
  field from an immutable descriptor; geometry remains derived data.
- `GameplayRadius` defines an inclusive, viewer-centered cube in all three axes.
  Collision uses the union of independently centered player cubes as described
  in the deformation contract; visual streaming still has one target.
  Its membership and overlap are computed analytically. A chunk view is created
  on demand for a query or a render-preparation consumer. Mutable per-coordinate
  storage requires a feature that actually owns such state.
- LOD0 preparation is bounded by visual coverage and its warm shell, including
  committed/staged placement dependencies. Gameplay radius cannot expand this
  render set. Visual tier selection and placement are defined only in
  [level-indexed placement](GpuVoxelMeshing.md#canonical-level-indexed-placement).
- An explicit `StreamingTarget` takes precedence. Otherwise the manager resolves
  one non-proxy `PlayerController`; absent or ambiguous player resolution
  leaves the manager GameObject as the streaming origin. The performance runner
  separately requires a valid local player.

The flow is field/settings -> analytic gameplay membership and bounded visual
requests -> immutable SDF descriptors -> derived GPU geometry. A future mutation
system must update the authoritative field and invalidate affected derivatives;
meshes and render caches must not become a second world-state model.

## Spatial Contract

The manager validates the production layout as 32 cells per axis at 16 world
units per cell. Each chunk spans 512 units and has 33 logical samples per axis,
including shared boundary samples. These values are owned by the manager's
configuration validation; changing inspector values does not establish a new
supported layout.

Visual configuration also has a 32,768-coordinate preparation budget, owned by
the manager. It counts the LOD0 warm cube when enabled plus all enabled coarse
cache cubes. The supported seven-level 4/8 layout needs 25,907 coordinates, so
all default visual-radius tiers through 512 and the reduced 2/6 layout remain
available. Oversized operands are rejected before checked volume calculation
and enumeration; invalid requests retain the applied placement. This bounds CPU
coordinate work, not worst-case GPU geometry memory. Committed/staged sets are
bounded multiples of the same request budget.

`WorldToChunkCoordinate` uses floor division, including for negative positions.
A chunk's global sample origin is its integer coordinate multiplied by cells per
axis. Every consumer must query shared positions identically.

Negative density is solid, positive density is air, and zero is the surface.
`VoxelChunk` delegates 16-bit material IDs to the immutable catalog and procedural
strata module described in [Voxel materials](VoxelMaterials.md). Materials have no
separate mutable world: the dirt tool now stores explicit material overrides beside
the immutable density corrections. Density retains geometric detail; logical cell
emptiness for digging is defined separately in the material tool contract.

Gameplay interest has no fixed world-Z floor or ceiling. Its supported radius
and defaults are owned by `VoxelManager`; scene-authored settings belong to the
scene, and fixed test settings belong to the ledger. Logical loaded counts do
not mean that the same number of objects or meshes have been allocated.

## Procedural Generator Version 18 (qualification in progress)

The landform slice is implemented but not accepted yet. Its fixed workloads and
remaining gates are tracked in [the ledger](../ValidationResults.md#landform-001v1--regional-exterior-qualification-defined-before-runtime).
[RegionalLandforms](../../Code/Voxels/Generation/RegionalLandforms.cs) owns the
immutable recipe, pure XY height/landform weights and conservative local height
bounds. Its eight controls are LandAmount, MountainAmount, PlainsAmount,
ContinentalScale, MountainRegionScale, LocalLandformScale, ReliefHeight and
Ruggedness, plus WorldSeed. Amounts bias continuous eligibility, not guaranteed
world-area percentages. The source owns exact defaults, ranges, hierarchy and
coefficients; the [plan](../Plans/RegionalLandformsFirstSlice.md) records the recipe
and alternatives. Ocean basins are unfilled depressions relative to fixed Z=0.
There is no climate, vegetation, water, POI or selectable legacy generator.

The [mountains-only erosion prototype](../Plans/SelectiveErosionPrototype.md) adds
a bounded, seeded directional offset only where the mountain weight exceeds0.5.
Broad-mass guidance and two layers of smooth lattice wave blends supply
local detail, fading out at mountain bases and crests. A smoother dominant-mountain
profile replaces the narrow cliff step. Seeded mountain groups with shared broad foundations and subsidiary peaks
produce peaks and saddles. The generator24 transition profile removes the old
folded ridge even outside the erosion mask; locations with a nonzero mountain
contribution can therefore change height. Pure hill/plain terms are unchanged.
CPU queries/collision, GPU extraction, soil depth and water coverage consume the
same field. Generator25 has a separate save identity; prior versions remain intact.
Qualification, including performance, remains pending in EROSION-001/v2 (.3 mountain amount).

[TerrainCaves](../../Code/Voxels/Generation/TerrainCaves.cs) owns retained version-9
carving and its bounds: retained noodle/cheese composition and controls, 512-unit overburden,
32768-unit maximum surface-relative depth, and independent smooth 3D region mask
at wavelength16384/cutoff0.36. Cave dimensions do not scale with exterior relief.
New height changes the envelope's position; it cannot promise unchanged final
cave openings. [TerrainNoise](../../Code/Voxels/Generation/TerrainNoise.cs) owns
shared deterministic integer hashes and the version-13 simplex numerical contract:
world inputs quantized to1/256unit with nearest-even rounding, signed64-bit cell
and rank selection, then retained floating-point kernel contributions. This
changes numerical field values and uses a separate versioned save selector;
version12 pages are not silently migrated. A near-zero CPU/GPU sign mismatch
remains unqualified; see the [correction design](../Plans/CaveNoiseParityCorrection.md).

Cave noise bounds use the local gradient only when all eight quantized box
corners prove one convex simplex cell/order, evaluated through equivalent
linear interval extrema. They include quantization and
arithmetic margins plus outward endpoint rounding; otherwise use[-1,1]. This
is a topology proof followed by an analytic interval, not a corner-sign emptiness
assumption. Its performance qualification remains pending.

[ProceduralTerrainSdf](../../Code/Voxels/ProceduralTerrainSdf.cs) composes the
negative-solid exterior `z-height` with caves, owns generator version25 and exposes
full-field sampling/classification. It retains build-local XY reuse in
LatticeSampler. No shared mutable generation cache or extra scheduler exists.
The full classifier propagates height and cave intervals over a closed AABB;
uncertainty remains potentially surface-containing. Coarse clipbox preparation
uses this complete authoritative bound, including corrections. Its first cheap
rejection uses global exterior support [-.7526,1.0926]*ReliefHeight and retained
cave depth. The superseded broad-only public classifier has been removed.
No corner-only emptiness proof or underground heightfield assumption is allowed.

The [GPU mirror](../../Assets/shaders/voxels/voxel_sdf_v22.hlsl) includes separate
noise, landform and cave modules, with matching hashes, salts and recipes.
Negative coordinates use floor. Published bounds include numerical padding;
shared recipes do not establish bitwise CPU/GPU equivalence without measurement.
Generation remains an implicit field, not Euclidean signed distance.

Inspector controls are staged; [generation lifecycle](../../Code/Voxels/VoxelManager.Generation.cs)
handles Apply to new world. The idle host saves the current revision before
switching to a fresh world identity, then cancels/rebuilds derived work and
replaces old collision. Mutation/benchmark/save activity and connected guests
reject application. A recipe switch during a guest session is outside this
slice; mismatched guest settings remain explicit protocol rejection. Startup
and new-world placement use conservative local surface/edit bounds, then the
existing collision readiness path supports landing. The initial playable check
observed a grounded player; broader spawn cases remain pending.

The codec includes all eight landform controls plus seed and generator version,
and water recipe version/SeaLevel, in its format2 88-byte identity header
(124 bytes including block framing/hash). See [surface water](SurfaceWater.md). Recipe-specific
last-world selectors hash this canonical identity with fixed zero revision/UUID.
Older selectors and worlds remain on disk; no migration or reinterpretation is
attempted. Chunk descriptors, save identity and replication retain complete
settings. Authoring surveys export the active unedited recipe on a bounded worker,
without changing the field or substituting a reference generator.

## CPU Preparation and Lifecycle

`OnLoad` creates the mesher, applies configuration, starts bounded render
preparation, and returns a completed task. It does not wait for all gameplay
coordinates to materialize or for GPU terrain to settle. `OnUpdate` drives
preparation integration, placement readiness, mesher processing, and diagnostics.

Adjacent target moves update the bounded render window by entering/leaving
slabs. Initialization, configuration changes, larger jumps, or an invalid
incremental precondition rebuild that same set. The manager retains coordinates
needed by committed/staged placement and sorts missing preparation nearest-first
with explicit coordinate tie breaks.

Movement retains completed, unintegrated preparation whose coordinate is still
required. The existing missing-coordinate set selects those results; completed
and pending queues retain matching FIFO order, followed by nearest-first missing
coordinates for the new worker. No second preparation cache is introduced.
Content reset clears both queues. `StartWarmGeneration` owns cancellation and
revision advancement for movement and preserves retained results even when no
new classification is required.

One serialized background preparation chain receives immutable coordinates,
dimensions, terrain settings, revision, and cancellation token. It classifies
LOD0 regions and creates transient chunk views only for potential surfaces.
It constructs managed data off-thread, then explicitly returns to the main
thread to enqueue results. A new request cancels the previous revision and
waits for the preceding task before continuing, preventing overlapping workers.

The manager integrates bounded result batches on its update thread under the
source-owned time budget. It rechecks desired/placement membership, marks
prepared coordinates, and schedules potential surfaces with gameplay or warm
priority. Proven empty regions need no geometry. Coarse preparation, GPU
publication, and whole-placement readiness belong to the meshing document.

Configuration reset and destruction cancel outstanding preparation. Revision
checks reject old completions. No worker mutates scene state, owns GPU resources,
or creates a separate terrain implementation.

## Design Rationale

- The current implicit field needs neither dense arrays nor separate solid/air
  storage modes. Both would add storage for values already derivable on demand.
- Cubic gameplay interest follows the region topology in all three axes. Fixed
  world-Z bounds were removed because they imposed an unrelated floor/ceiling.
- The current cell scale balances detail against region, boundary, and eventual
  edit costs. A different scale requires a measured design and scenario change.
- Surface-relative caves preserve overburden under hills and valleys. Extra
  octaves, warping, worm carvers, and a general noise graph were unnecessary for
  this recipe. Earlier cave experiments remain in the ledger.
- Recursive per-cell range proof was rejected after background batches took
  seconds without yielding. Uncertainty must remain conservative.
- One task per chunk and synchronous bulk generation create unbounded scheduling
  or frame work. More concurrency needs evidence that CPU preparation is the
  bottleneck; it cannot solve a GPU publication dependency by itself.
- [Visual scaling rationale](../Research/VisualClipboxScaling.md) explains the
  separate gameplay and rendering ranges and the removed eager-allocation path.

## Debug Contract

`World Status` presents frame performance, chunk status, streaming performance,
and process memory. Readable status refreshes at the source-owned cadence;
logical chunk counts are not GPU-resident mesh counts. Legacy stream-generation
bookkeeping is removed from schema 22. Status distinguishes logical membership,
prepared regions, pending preparation, and GPU residents. Range-application time
is explicitly named and is not a mesh-availability or streaming-settle metric.
Use the performance result's meshing, queue, and placement measurements for that
question.

Schema 23 additionally records maximum moving-window placement anchor lag using
the inspection command's shared calculation. This is a distance in each level's
own regions, distinct from publication route lag in LOD0-sized chunks. It resets
at test start and survives drain/stationary capture without per-frame logging.

`voxel_chunk_info x y z` checks analytic gameplay membership and constructs a
bounded query view to report generator identity, density bounds, sample/material,
and boundary samples. `voxel_lod_info` observes manager-owned level/pair state.
It includes cached per-level and combined geometry fingerprints and transition
mismatch counters, so configuration rejection and restoration can be inspected
without reading geometry back from the GPU.
Neither creates independent world state or changes the streaming origin.

Verbose logging is opt-in; per-chunk load/unload spam and runtime loaded-chunk
bounds/labels are absent. Warnings, errors, explicit read-only diagnostics, and
performance begin/save records remain sparse unconditional evidence. Process
working set and engine GPU memory are labeled by scope, not attributed to chunks.

## Performance Overview

The inspector's `Run Performance Test` button and editor MCP
`run_performance_test` call `VoxelManager.StartPerformanceTest`. They share the
manager's player-movement and measurement lifecycle. The former standalone
`player_figure_eight` tool and toggle-button instructions are obsolete.

The runner moves a valid local player along a lemniscate centered on its starting
X/Y, ten meters (10 / 0.0254 world units) vertically above the procedural
exterior height at each route position, including the initial placement and final
center crossing. RegionalLandforms supplies that height from the applied recipe;
this benchmark follows the unedited exterior, including the seabed, independently
of collision mesh readiness. It does not follow water, cave floors or edits.
The player origin receives the clearance; the eye is higher by its normal offset.
Speed uses the horizontal local tangent, distance defines X
reach, and Y reach is half that distance. It counts complete loops itself;
external polling or elapsed sleeps do not choose the measured boundary.

After movement ends, it records the moving window, waits for preparation,
regular/transition work, and placement to settle, captures settled visibility,
waits two further render-sequence advances, and measures a stationary window.
The final result is saved only after stationary visibility completes. Merely
finishing the moving loop does not mean a run was saved or accepted.

The per-frame sampler records bounded scalar frame, GPU, allocation, GC,
exception, queue, and work counters; memory uses a slower cadence. Percentiles
use frame duration so high tails describe stalls. Copying/sorting profiler
history occurs at completed windows. Capacity exhaustion is reported rather
than silently allocating. Timing and capacity constants are owned by the manager;
[PerformanceTestResult.cs](../../Code/Voxels/PerformanceTestResult.cs) owns the
serialized field layout. Do not maintain a second schema changelog here.

Real profiler scopes cover range rebuilding, placement preparation, result
integration, mesher processing, and draw-command commits. Scalar work reporting
counts integrated and retained preparation results, discarded completed results,
coarse-cache coordinates scanned, skipped complete levels, and placement time.
Retention counts are reuse events, not a second count of newly classified
coordinates. Each saved run emits one sparse `performance.work` summary;
per-coordinate diagnostic logging remains opt-in.

One completed result appends to `performance/results-v1.jsonl` in
`FileSystem.Data`. It includes a run ID, capture time, caller-supplied task and
revision, effective configuration, and separate moving/stationary measurements.
The inspector supplies default context when blank; callers must use labels that
identify the actual measured source. The runtime does not inspect Git or launch
processes. File I/O occurs after measurement and one local manager writes at a
time. JSON Lines keeps appends independent of historical file size.

The [validation ledger](../ValidationResults.md) alone owns fixed scenario
parameters, baseline selection, thresholds, and acceptance decisions. The
[project instructions](../../AGENTS.md#figure-eight-performance-acceptance) own
when this primary test must run. Runtime result files are raw evidence; the
ledger records the reproducible comparison and decision.

## Retained-sample percentile contract

S3 (2026-09-08, explicitly accepted by the user): PerformanceSampleTails shares only
nearest-rank p50/p95/p99 and retained maximum for sorted spans; empty tails are
zero. It owns no storage. GPU, collision and manager keep their collection,
copy/sort, invalid-value, overflow, mean and reset policies. GPU averages all
valid observed values including overflow; collision averages retained values.
CPU frame maximum remains null when truncated. Serialized result fields and
moving/stationary/trailing-profiler window meanings do not change. See
SIMPLIFICATION-S3-001/v1 in the ledger for performance failures and human gate.

## Configuration decision consolidation (S5 accepted)

OnUpdate resolves a validated visual configuration or retains the current target
configuration after rejection, then shares gameplay-radius and target-movement
handling. Valid data changes still rebuild first. Rejected visual settings do
not prevent a valid gameplay radius change or normal streaming with retained
visual settings. Target selection, player discovery, actor interests and update
cadence are unchanged. S5 was accepted by the user on 2026-09-08; see its ledger run.

### Exact cave-envelope early exits

The water/loading follow-up mirrors the CPU surface-dominance early exit in the
GPU cave function. Both CPU and GPU also skip all cave noise when the depth
envelope is at most-1024: normalized raw caves and the regional mask are both
bounded above this value. CPU cave interval evaluation uses the same conservative
condition. This preserves the selected field; it changes no world identity or
cave settings. Loading-speed acceptance remains pending a comparable workload;
WATER-LOAD-004 was invalidated by player movement. The first-512 shortcut was
superseded during review because it omitted the regional mask's lower bound.


## Debug overlay

The first slice adds a local, read-only screen overlay in the playable scene.
The DebugOverlay input action (default F9), listed as Toggle Debug Overlay in Other controls, toggles it, initially hidden. It displays the local non-proxy player's world
XYZ in engine world units (Z up) and logical base chunk XYZ. The terrain manager's
TryGetChunkCoordinate query delegates to its existing applied-layout floor
conversion, preserving negative-coordinate and boundary semantics. It reports
unavailable before a streaming center exists; the overlay never uses the camera
or streaming target as a substitute for a missing player.

The generator has no biome identity yet. The row explicitly says
"Biome: Not implemented" until a future generation slice supplies that contract.
Landform weights are not biome names.

VoxelDebugOverlay owns only local visibility, cached component references and
display strings. On the engine thread it checks the DebugOverlay action each frame, then samples and
formats at most once per 0.1 seconds while visible. Hidden overlays do no player
or terrain queries or string formatting. No terrain mutation, mesh work, worker,
replication or persistent state is introduced. The panel has no pointer events
and does not change camera or player controls. BuildHash rebuilds only when
visibility or displayed text changes.

This uses one scene ScreenPanel and one Razor component. Future rows should
extend this component and query their existing state owners. A provider registry,
new biome classifier and per-frame text rebuilding are unnecessary for this slice.
Budget: <=10 refreshes/second while visible, zero hidden data refreshes; compare
the canonical figure-eight with the overlay visible before runtime acceptance.


### Selective cliff prototype (generator24)

`TerrainCliffs` adds bounded local excavations to the existing negative-solid
surface/cave field. Its inputs are world position, immutable recipe, cached
mountain weight and incoming density. The result is max(incoming,min(cut,mask));
mask=(mountainWeight-.75)*ReliefHeight limits exposed cuts to mountain-dominant
terrain. An incoming-density upper-bound check skips work without changing the
max/min result. There is no new persistent state, edit path or render-only shape.
CPU scalar/lattice and GPU density entry points apply the same operation.

Seeded rotated elliptical cuts occupy51/256 candidate cells. Spacing is
1.3*LocalLandformScale, radii .099...153 and .063 of spacing, center jitter +/- .18
cells, centers at SeaLevel+(.28...58)*ReliefHeight, half-height .078...13*ReliefHeight.
A64-unit recess is largest at mid-height and tapers toward the roof and floor.
At the smallest supported spacing2662.4, maximum horizontal support is less than
.22cells; omitted cells are at least1.32cells away. Nine neighbors cover every
surface-affecting cut. Bounds project the AABB onto each ellipse, bound vertical
extent, include the full recess and a1-unit rounding allowance. Cell-crossing
boxes use the global cut upper bound. The old lower density bound stays valid
because excavation only increases density.

Height remains the exterior reference for surface water and procedural strata;
cuts stay above sea level and their faces expose existing stone below the soil.
No surface-angle material override was added. The build-local cache stores height
and mountain weight as one Vector2 per XY column. Mountain group heights now
range .65..1, spacing1.6, foundation/summit weights.25/.75, with wider central peaks.
The folded contour-ridge transition was removed in favor of gentle q-squared
relief. Erosion, snow thresholds and authored MountainAmount.3 are preserved.

Generator24 keeps these worlds separate from previous recipes. Performance and
visual qualification are recorded under CLIFF-001/v1 and EROSION-001/v2 in the
validation ledger. This remains a prototype; regular oval cuts, coarse-LOD ledge
loss and enclosed cut cavities are known design risks, not accepted appearance.


K retains generator24 and its shapes. Before evaluating a cut, the sampler bounds
its best possible horizontal contribution from the query's distance to the cell's
jitter box, maximum major radius and fixed minor radius. It includes the full
128-unit recess plus1-unit rounding allowance and skips only a contribution that
cannot exceed the accumulated density. Height bounds additionally return the
mountain-weight upper interval; density classification caps cuts with that same
mask. Both changes prune work rather than changing the terrain recipe.


### Mountain diversity (generator25)

The current MountainMasses uses four deterministic summit families selected from
existing group hash bits: pointed main peak with low shoulders, broad massif,
offset twin peaks, or elongated ridge with unequal subsidiary summits. Continuous
seeded group width, aspect, height and rotation remain. Foundation share is .20
for pointed/twin, .50 for massif, and .35 for ridge; peak union takes the remainder.
Every peak support stays inside its group ellipse, retaining the existing nine-cell
lookup and conservative interval composition. No additional noise queries or
mutable state were introduced. CPU analytic guidance and GPU mirror use the same
weights and layouts; previous generator worlds remain separately identified.

A smoothly supported erosion multiplier is the maximum across groups of
(1-r^2)^2*(.15+.85*hashByte/255), clamped by compact group support. Existing erosion
exposure is multiplied by this value. The multiplier remains[0,1], so the previous
maximum offset bound is conservative. PeakFraction now includes individual and
group height factors for varying snow presence; see VoxelMaterials. Cliffs retain
the generator24 geometry and bounded evaluation optimization. MountainAmount.3
and the remaining authored recipe are unchanged. Same-run visual selection and
performance qualification are owned by MOUNTAIN-DIVERSITY-001/v1 in the ledger.

### Regional uplands (generator27)

RegionalLandforms adds continuous supporting relief across mountain regions,
including the gaps between MountainMasses groups. The eligibility uses the
existing mountainPreference before its peak-region cubic weighting, faded by
Smooth((preference-.1)/.9) and coastal Smooth((land-.65)/.35). A separate
four-hash value field (salt0xA24BAED5, scale1.35*MountainRegionScale) modulates
support height, with the existing p field providing broader shoulder variation.
Uplift fraction is mask*(.12+.56*Smooth((elevation-.15)/.7))*(.7+.3*p), added to
landHeight before the continental blend. It is in[0,.68]; the global maximum
height fraction is1.72 plus bounded erosion. BoundHeight propagates the same
field and composition using conservative intervals.

Inputs remain immutable settings, seed and XY; there is no new stored state.
Scalar/lattice/GPU, material strata and cave depth receive the same new surface.
Existing erosion uses its local summit gradient; the broad support is not a new
hydraulic simulation. Mountain eligibility and authored amount.3 stay unchanged.
The existing cliff positions are not elevated with the terrain, so some become
buried. This slice changes range foundations, not summit primitives or overhang
construction. Runtime qualification belongs to UPLAND-001/v1 in the ledger.

N2's zero-weight calculation skips were tested and rejected after a worse
benchmark. The retained implementation is N's original regional uplift formula
and evaluation order, verified by exact CPU/GPU source hashes. The shader header
now refers to the descriptor as version owner. See the ledger for failed runs;
this remains an uncommitted prototype with unresolved performance acceptance.

### Continuous mountain flow candidate (generator29)

MountainMasses now contains a continuous domain-warped ridge/shoulder field,
replacing the three-cone groups. Two slow cubic value fields bend coordinates and
control character/height; two rotated detail fields form connected ridges and
spurs. A continuous blend introduces broad shoulders. The canonical output is
mass[0,1], analytic XY gradient, local mass snow score, and erosion strength
.25+.75*warpY. Interval bounds propagate the same transforms; existing regional
uplands and MountainAmount.3 are retained. River integration owns its separate
surface adjustment and derivative cache; the generator revision invalidates
older terrain-derived identities.

This is an UNACCEPTED candidate. Hot compilation passed but the first combined
Play view was mostly empty and density audit reported large CPU/GPU disagreement.
Cold-start validation, cause resolution, visual flow criteria and performance
acceptance remain pending. FLOW-001/v1 in the ledger owns evidence; do not treat
the source design or older gallery as proof of this candidate's appearance.


### Rolling hills candidate (generator44)

RegionalLandforms replaces fine hill mounds with overlapping smooth rises.
Hill shape samples0.75*LocalLandformScale;70%of that field blends with30%of
the existing local shape. Extra hill height is0.28times this blend, on the
plains base0.035+0.015*q. With the authored5232.39local scale, hill-shape
sampling spans3924.2925units. Maximum extra relief is860.16units at3072relief;
actual crest elevations vary with the noise and landform weights.

The existing plains preference multiplied by smooth((patch-0.25)*2.5), then
squared, determines hill weight outside mountains. The independent patch field
spans5*LocalLandformScale, leaving broad quiet areas between groups. Ruggedness
no longer introduces tiny hill mounds. Cubic interpolation retains continuous
first derivatives. The rejected generator43 used1.5local-scale hills and0.42
extra relief; user feedback identified a single overly dominant hill. Version44
halves that shape scale and lowers the amplitude by one third while overlapping
independent shapes for unequal crests and intervening shallow valleys.

CPU scalar sampling, conservative interval classification and GPU mirror share
the same recipe. No extra noise sample, buffer, mutable owner or alternate
runtime path is added. Hill profile maximum0.33remains inside the existing
global height envelope. Pure plains/mountain/ocean and uplift formulas remain;
mixed terrain, derived rivers and surface-relative caves can change.

Generator44 uses existing versioned selectors, saves and network compatibility
checks. Versions42and43remain preserved, without migration or reinterpretation.
HILLS-001/v1 owns qualification. Current status: applied, managed compilation
and local survey checks pass; inspected views show lower rolling crests.
Full performance, density and clean-start qualification remain pending.
