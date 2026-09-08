# Regional landforms: first-slice implementation plan

Date: 2026-09-08. Status: planned, no runtime implementation or validation run.
Branch: `codex/terrain-biome-generation`, created from `794b14f` on
`codex/terrain-deformation`. This plan is the selected first-slice scope;
[biome terrain research](../Research/BiomeTerrainGeneration.md) owns comparisons,
design rationale and the later biome roadmap.

## 1. Deliverable and scope boundary

Deliver a playable, tunable regional landform demonstration with broad ocean
basins/coastal shelves, usable plains, rolling hills, foothills and substantial
mountain regions with varied ridges and saddles. The landscape should be interesting
from player height and distant views before biome materials or trees are added.

Replace the existing exterior terrain generation completely. Keep the current
noodle/cheese cave recipe, including its world-space noise and surface-relative
depth envelope, until a separately selected cave overhaul. Preserve terrain edits,
storage, replication, CPU collision and GPU rendering as consumers of the canonical
field. No alternate old/new generator selector or fallback implementation.

Included: new landform algorithm; small designer-facing control set; deep-module
boundaries; conservative bounds; CPU/GPU equivalence work; settings/version
propagation; safe recipe application; authored scene settings necessary for this
slice; production diagnostics and in-world acceptance through existing entry points.

Excluded: climate execution, biome assignment, forests/grass placement, biome
materials, new cave shapes, exterior overhang generation, hydrological routing,
water rendering/simulation, lakes/river features, roads, buildings, cities,
geological simulation, a node editor, and a global prebaked heightmap.

Ocean basins are unfilled depressions below a recipe-owned reference Z=0. This
slice cannot demonstrate swimming, sea surfaces, connected oceans, lake levels
or drainage correctness. Flatland means a region of low slopes and useful extent,
not individually positioned planes. Existing cave subtraction can still create
volumetric interiors; the new exterior itself is a height function.

## 2. Source handoff and replacement inventory

Recheck these owners at implementation start; source locations are current at
`794b14f`, not promises that unrelated work will remain unchanged.

| Owner | Required change or preservation |
| --- | --- |
| [ProceduralTerrainSdf.cs](../../Code/Voxels/ProceduralTerrainSdf.cs), `ProceduralTerrainSettings`, `SampleSurfaceHeight`, `SampleWorld` | Replace the single simplex exterior and old base/frequency/amplitude settings. Preserve the cave algorithm, sign and composition. Retain/evolve build-local `LatticeSampler` reuse. |
| Same owner, `ClassifyDensityRangeBroadPhase`, conservative range methods | Replace amplitude-only vertical assumptions with new landform bounds, then combine retained caves and canonical edit bounds. No corner-only emptiness proof. |
| [VoxelManager.cs](../../Code/Voxels/VoxelManager.cs) | Replace surface inspector controls, validate/apply one immutable settings value, advance revisions and recreate requests through current lifecycle. Keep algorithm logic outside the manager. |
| [GpuSdfDescriptor.cs](../../Code/Voxels/GpuSdfDescriptor.cs) | Include every new setting in descriptor identity, equality and stale-work rejection for regular and transition requests. |
| [GpuTerrainContracts.cs](../../Code/Voxels/GpuTerrainContracts.cs), [GpuVoxelMesher.cs](../../Code/Voxels/GpuVoxelMesher.cs), request packing | Existing four-component `Terrain` packing is insufficient. Specify one exact expanded C#/HLSL layout with offsets, types, stride and bounded request bytes before editing. Update both request paths together. |
| [voxel_sdf_v5.hlsl](../../Assets/shaders/voxels/voxel_sdf_v5.hlsl) and shader consumers | Replace exterior implementation and versioned includes coherently. Retain cave constants/semantics. Its existing header says version 4 despite the v5 runtime; remove that inconsistency while establishing the new canonical version. |
| [TerrainFieldCodec.cs](../../Code/Voxels/TerrainFieldCodec.cs), `WriteIdentity`/`ReadIdentity` | Update serialized settings, fixed header/identity byte budgets and generator identity. Reject incompatible/truncated input before replacing active state. |
| [TerrainFieldStore.cs](../../Code/Voxels/TerrainFieldStore.cs), [VoxelManager.Storage.cs](../../Code/Voxels/VoxelManager.Storage.cs) | Keep the existing store; integrate the expanded identity and safe new-world/restore lifecycle. Do not silently load v5 edits under new terrain. |
| [TerrainReplicationManifest.cs](../../Code/Voxels/TerrainReplicationManifest.cs), [VoxelManager.Replication.cs](../../Code/Voxels/VoxelManager.Replication.cs) | Update identity sizing/validation through the existing host/client path. All participants must use the same recipe. |
| [VoxelChunk.cs](../../Code/Voxels/VoxelChunk.cs), [TerrainField.cs](../../Code/Voxels/TerrainField.cs), collision consumers | Preserve canonical sampling, edit composition and regional ownership; update settings plumbing rather than adding another sampler. |
| [basic_example.scene](../../Assets/scenes/basic_example.scene) | Replace authored obsolete surface fields with the selected landform settings when implementing; preserve unrelated scene settings and objects. |

Search all call sites of the old settings and shader field before acceptance.
Keep seed/hash and simplex primitives used by caves unchanged. Shared numerical
primitives may move to one internal owner if both modules need them; no copied
utility implementations or legacy surface settings aliases.

## 3. Small, deep implementation modules

The first slice needs three algorithm responsibilities plus immutable settings.
Proposed names are concrete planning targets, not required public APIs:

| Responsibility | Contract | Ownership |
| --- | --- | --- |
| `RegionalLandforms` | Evaluate exterior height and compact landform weights; bound exterior height over an AABB's XY footprint. | All continental/province/local fields, response curves, parameter semantics and derived XY scratch. Pure evaluation; no engine resources, tasks or shared mutable cache. |
| `TerrainCaves` | Compose the retained cave contribution using world position, exterior height and seed; propagate its bounds. | Existing cave constants, seeds, depth envelope and proven skips. This substantial boundary allows a later overhaul without changing Landforms. No plugin interface or cave variants. |
| `ProceduralTerrainSdf` | Canonical unedited density query and bound; build-local lattice evaluation. | Composes Landforms and Caves, preserving sign/units and the single CPU field entry point. GPU mirrors the same specification. |

Keep implementation under `Code/Voxels/Generation/` for new generation modules,
with corresponding shader includes under `Assets/shaders/voxels/`. Existing
integration owners may remain in place. This directory arrangement is a proposal;
responsibility/state separation is the requirement, not moving unrelated files.

One immutable settings value contains seed plus landform settings; one owner
defines defaults, valid ranges, reference level, hidden constants and canonical
serialization order. The manager presents controls and hands validated settings
to the field. It does not know mountain masks, noise salts, cave thresholds or
coastal profile coefficients. No climate/population stubs are introduced.

The dependency graph is acyclic: configuration + global XY -> Landforms;
Landforms height + world XYZ + seed -> cave composition -> canonical base field;
base field + authoritative edits -> existing render/collision paths. A future
climate consumer can use the returned exterior height/weights without changing
field ownership. Future feature plans will supply bounded inputs before final
composition and must not query a final field that depends on themselves.

Pure jobs capture immutable settings and source epoch/revisions. Existing
schedulers own admission/cancellation; modules must not launch hidden tasks.
Consumers release scratch/input references after completion or cancellation.
Engine resource publication stays at supported integration boundaries. One-line
wrappers, per-noise classes and partial files sharing a giant manager's state do
not meet the deep-module requirement.

## 4. Designer controls and proposed starting values

These are proposed initial defaults and allowed ranges, not measured good values
or executable ledger scenarios. Finalize once before the first runtime acceptance
run and record the exact recipe. During exploratory tuning, retain each trial's
settings/results rather than quietly modifying a frozen scenario.

Display eight shaping controls grouped as Distribution, Scale and Shape, plus
World Seed. Keep explanatory tooltips in terms of the resulting world.

| Control | Proposed default | Proposed supported range | Contract |
| --- | --- | --- | --- |
| Land amount | 0.60 | 0–1 | Bias basin versus land occurrence; not an exact percentage. Endpoints select only basin/land profiles. |
| Mountain amount | 0.35 | 0–1 | Bias mountain eligibility on land; zero removes it, one fills eligible inland areas. |
| Plains amount | 0.60 | 0–1 | Bias plains versus hills within non-mountain land. Hills are the remainder. |
| Continental scale | 131,072 units / 256 chunks | 32,768–524,288 units | Characteristic broad land/basin scale. |
| Mountain region scale | 32,768 units / 64 chunks | 8,192–131,072 units | Characteristic province size/separation, not exact mountain spacing. |
| Local landform scale | 8,192 units / 16 chunks | 2,048–32,768 units | Shared scale of hill patches and local ridge structure. |
| Relief height | 3,072 units | 512–8,192 units | Shared vertical envelope; relative plains/hill/mountain/basin ratios remain internal. |
| Ruggedness | 0.45 | 0–1 | Bounded local detail and ridge sharpness; fixed octave-count ceiling. |
| World Seed | 1337 | Existing -16,777,216…16,777,216 | Reproducible arrangement; retain current GPU-exact transport restriction unless separately redesigned. |

Validate the hierarchy: ContinentalScale >= 2 * MountainRegionScale and
MountainRegionScale >= 2 * LocalLandformScale. Reject incompatible combinations
with a clear explanation; do not silently adjust another slider. Validate finite
numbers, positive spatial scales, full height/cave support and coordinate arithmetic
before admitting work. These ranges still need worst-case slope/geometry and
frame-cost qualification; they are not guarantees that all combinations run well.

Reference level stays Z=0. Do not add a separate ocean-amount slider or expose
per-octave frequency, gain, seed salts, spline knots, cave parameters or per-region
overrides. A separate mountain-height slider is deferred unless the shared relief
control proves insufficient for a distinct design need. Presets, if added later,
are settings values rather than selectable generator backends.

No percentage claims without area calibration. A control sweep must measure
weighted occupancy and actual surface outcomes; a mountain-amount increase can
widen existing provinces rather than create more disconnected regions. Scale
changes move/regroup features as well as resize them; they are not location locks.

## 5. Canonical recipe decisions to implement

Use the [research hierarchy](../Research/BiomeTerrainGeneration.md#selected-regional-landform-hierarchy):
continental context, independent mountain provinces, independent plains/hills,
bounded profiles and limited local detail. Select one recipe, then remove the old
surface formula. Reuse deterministic primitives where appropriate; do not retain
v5's exterior by disguising its settings as another profile.

Required internal decisions before the first run:

- Specify a fixed small evaluation count per control/profile, exact seed salts,
  coordinate scales, response curves and normalization. Start with one noise
  contribution per broad control and at most two local detail octaves. No unbounded
  warp/erosion chain or per-voxel allocations.
- Define smooth, nonnegative landform weights with a unit sum, coast gating and
  explicit endpoint behavior. Bias must be monotonic in its eligibility weight
  for fixed coordinates/other settings. Do not re-seed when a slider changes.
- Define ordered and bounded profile envelopes: ocean below reference, land above,
  plains low-relief, hills stronger, mountains broad uplift plus ridged structure.
  Flatten plains' complete regional profile, not only the highest-frequency noise.
- Specify every curve's knots, endpoint clamps, value range and derivative bound.
  Ensure continuous height and avoid accidental derivative jumps/overshoot at
  control thresholds; sharp ridges are deliberate local shape, not region seams.
- Account for weight gradients and profile differences in slope/bound derivation.
  Keep detail small enough that increasing ruggedness does not erase plains or
  shift the continent/province eligibility masks.
- Persistently name/version these internal coefficients beside their owner.
  Exact coefficient tuning is implementation work; this plan does not pretend
  an unrendered formula is a visually accepted landscape.

### Retained cave contract

Preserve the existing constants: noodle wavelengths 6,144/6,912 units; width
variation wavelength 16,384; cheese wavelength 8,192; density scale 512; surface
depth interval 512–8,192; noodle threshold `0.056 + 0.016 * thickness`; cheese
threshold `0.48 - 0.12 * thickness`. Keep the existing seed salts, gradients,
hash wraparound, 3D noise coordinates, max/min composition and valid skip proofs.
Do not multiply cave dimensions by ReliefHeight.

The final cave result can change where the new exterior changes its depth
envelope. Preserve the algorithm and raw world-space cave fields, not a promise
of identical cave openings or final densities at every old-world position. An
identical world-space point with a different surface-relative depth is not an
equivalent cave-composition input. Validate cave behavior with this distinction.

### Bounds and CPU/GPU obligations

Retain negative-solid density and the existing edit composition. `z-height` is
an implicit field, not exact Euclidean distance; do not normalize by slope.
Build global profile bounds first, then conservative local interval bounds through
curves, masks and cave composition. Uncertainty stays potentially surface-containing.
Loose global bounds are a safe fallback, not evidence of adequate streaming cost.

Every regular and transition GPU request must use the same settings and arithmetic
recipe as the CPU, at all LODs. Specify padded request layouts once and verify
both packers and shader declarations. Preserve dedicated geometry emit shaders;
follow the [existing shader parser constraints](../Architecture/GpuVoxelMeshing.md#sbox-vfx-shader-parser-gotcha)
and verify cold-start shader loading as well as hot compilation.

No CPU height/density atlas upload replaces the canonical GPU field. Reuse CPU
XY evaluation within existing bounded lattice workspaces. Investigate GPU repeated
XY work only if measurements justify derived scratch reuse. No added per-frame
SDF evaluation in the terrain draw material, no octave changes by LOD, no new
renderer/collision implementation.

## 6. Recipe application, identity and saved worlds

Stage inspector edits and apply a complete valid recipe once. Scrubbing a slider
must not trigger full-world regeneration for every intermediate value. Reuse an
existing supported editor action/property workflow after checking installed APIs;
no custom settings framework is needed. Expose effective settings and generation
status so the user can distinguish requested from applied values.

Adopt a new generator revision, expected v6 if v5 remains current at implementation.
Expand the existing settings identity and its fixed-size serialization budgets
for CPU/GPU, disk and network together. Include every field-shaping parameter and
internal recipe revision. Preserve exact seed transport and canonical float order.

Start a separate explicitly named landform demo world/save identity. Keep existing
v5 save files intact and reject mismatches clearly. Removing the old generator
from runtime does not require deleting its saves; Git retains the prior code.
Do not auto-migrate old edits or reset the currently selected save. Before an
implementation changes the selected live world, make its new-world action and
destination concrete; this planning task changes neither.

Applying a different recipe creates a new compatible generation session/identity
through the existing lifecycle, not a field-settings mutation under old edited
pages. Save/restore and replication use that same identity. Reject or defer
application when the existing lifecycle cannot safely complete outstanding edits
or saves; never drop accepted edits. Invalid settings leave active world unchanged.

All jobs/descriptors capture settings, epoch and relevant content revisions.
Retain valid old published resources only while consistent with the active world
and readiness policy; never mix new field collision with arbitrarily old terrain
or permit a player to enter missing collision. Document the handoff explicitly at
implementation, including cancellation, incompatible resources and player placement.

The old spawn height may now lie underground or above a basin. Resolve a bounded
safe spawn against the canonical field and current collision using the existing
player lifecycle; wait for support instead of spawning into missing geometry.
Do not force a flat spawn island into the terrain recipe or move authored objects
silently. Record the resulting live-player starting conditions for measurements.

## 7. Implementation sequence and completion gates

1. **Baseline and recipe freeze.** Inspect branch/source, engine identity and
   effective scene settings. Read applicable routes again. Record fixed scenarios
   in the ledger before any runtime run. Capture a comparable v5 figure-eight
   control if absent; preserve prior accepted storage results and limitations.
2. **Landforms and cave boundary.** Implement the new Landforms owner, isolate
   retained cave responsibility, compose through the existing SDF boundary, and
   derive conservative bounds. Define settings/defaults in one place. No standalone
   synthetic generator or temporary alternate runtime recipe.
3. **Complete integration before visual acceptance.** Update regular/transition
   GPU code/layout, manager application, identity/codec/network settings and scene
   authoring. Remove old surface controls/formula/includes and stale comments.
   Check compilation and cold start through installed s&box tooling.
4. **Playable shape and control review.** Execute the fixed terrain/parameter
   observations below; inspect screenshots from player height and elevated views.
   Report weak profiles, repetition, unreachable landforms and unintended coupling.
   Do not add climate or trees to conceal terrain deficiencies.
5. **Lifecycle, field and performance qualification.** Run production edit,
   collision, reload and relevant multiplayer recipe checks plus canonical
   figure-eight. Measure the complete pipeline; resolve correctness defects and
   unexplained regressions before accepting runtime implementation.
6. **Handoff and stop.** Update current contracts in foundation/GPU owners and
   append ledger results. Present the landform world, controls, measurements and
   limitations for review. Climate/biomes and cave overhaul are separate scopes.

No runtime step has been executed by writing this document. Documentation review
and branch creation are complete only when links/consistency are checked and the
documentation changes are committed/pushed.

## 8. Validation design

Use the real playable world, canonical field, current player/figure-eight and
existing production diagnostic entry points. No test scenes, test projects,
synthetic terrain, reference mesher, alternate sampler or test-only hooks. Any
needed observability belongs as bounded read-only production diagnostics in its
current owner; it cannot become a second generation path or benchmark trigger.

The following proposed parameter study must be copied into exact versioned ledger
scenarios before execution. It does not replace the existing figure-eight workload.

| Study | Fixed proposal | Measurements / decision |
| --- | --- | --- |
| Regional distribution | Seeds 1337, 42, 9001; defaults above; XY square [-131,072,131,072] in each axis, spacing 4,096, endpoints included (4,225 positions/seed). Query canonical production exterior/weights with bounded work. | Weighted land/mountain/plains/hill occupancy; below-reference fraction; height range/quantiles. This coarse grid does not establish local continuity or global proportions. |
| Amount controls | Same seeds/domain; vary one amount at a time through 0, 0.25, 0.5, 0.75, 1; all others default. | Eligibility weights have no pointwise reversal beyond frozen numeric tolerance; endpoint behavior is exact where specified. Report actual surface class fractions separately. |
| Scale controls | Same seeds/domain; each spatial scale at 0.5x, 1x, 2x default, separately; each satisfies hierarchy constraints. | Report profile transect correlation lengths and connected dominant-region statistics on the fixed sample grid. Larger scale should produce larger characteristic features in aggregate; no exact per-seed gap promise. |
| Shape controls | Relief at 1,536/3,072/6,144 and ruggedness at 0/0.45/1, separately; same inputs otherwise. | Height spread, slopes and roughness on fixed finer transects; regional eligibility masks unchanged. Inspect whether plains remain usable and mountains remain distinct. |
| Detailed transects | Seeds above, Y=-32,768/0/32,768, X=-131,072…131,072 at spacing 512; add local fine samples around fixed profile/region boundaries selected before timing. | Height continuity, slope distributions, ridge/saddle structure and parameter effects. Freeze local coordinates and finer spacing in the ledger before the corresponding run. |
| Playable views | From the default seed/domain, choose the first qualifying basin, plains, hill, mountain, foothill and coast positions in a specified lexicographic scan. Freeze classification thresholds, positions, cameras, route and duration before acceptance capture. | All six views visibly distinct, no region walls/cracks or repetitive fine bumps dominating plains; record any missing class instead of swapping to a favorable seed. Human appearance judgment is separate from numeric gates. |

The study is deliberately finite. Thresholds for class membership, useful plains
slope/extent, ridge prominence and acceptable CPU/GPU density/position error must
be written before running acceptance, using the production cell scale and actual
consumer tolerances. These are still implementation preparation decisions; do not
claim the plan contains a completed executable benchmark or make up passing values.

Additional required checks:

- Shared positive/negative region samples and LOD interfaces agree; regular and
  transition geometry has no observed cracks in frozen views. Record near-zero
  CPU/GPU signs and collision contact/surface differences rather than hiding them
  in averaged density errors.
- Bounds contain every exercised production sample and emitted surface, with
  zero observed false-empty results; analytical review supplies the conservative
  argument that finite sampling cannot prove. Measure extra admitted empty work.
- Retained cave constants/arithmetic are unchanged by source comparison; exercise
  normal cave terrain and depth-envelope transitions beneath different landforms.
  Compare raw cave components only at equal world positions and matching inputs;
  do not demand unchanged combined fields under changed exterior heights.
- Dig/build across a boundary, retain collision support, save/reopen the new
  recipe and verify exact settings plus edited-state fingerprints. Reject old
  or modified identity without replacing live state. Verify host/client recipe
  agreement and mismatch handling through the existing multiplayer flow, without
  reopening unrelated accepted storage tests.
- Freeze rapid reconfiguration/teleport cases before runs. Confirm old jobs cannot
  publish into new identities, invalid parameters preserve the last applied state,
  queues/memory remain bounded, and safe player readiness is explicit.
- Qualify near-origin, negative and near-limit coordinates. Existing edit support
  is bounded by +/-1,048,576 units; that is a ceiling to investigate, not proof of
  arbitrary-distance precision. Avoid coordinate redesign in this slice unless
  evidence makes it necessary for its declared supported envelope.

### Performance and workload comparability

Keep existing scheduler worker/lane limits and integration caps initially. Prefer
slower distant generation over competing with frame-critical work. Measure CPU
generation throughput and p95/p99, classification effectiveness, GPU dispatch
time, frame pacing, collision build/publication, allocations/GC, queue age, mesh
completion, geometry memory and retained job memory. Moving, cold-start and
stationary windows answer different questions and must be reported separately.

The current preparation integration budget is already 0.5 ms; it is not a spare
budget for added code. Larger mountains increase occupied vertical regions and
meshes even with identical sampling throughput. A CPU admission cap does not cap
GPU execution or native physics cooking. Profile before adding workers, caches,
extra dispatches or detail octaves. Resident and pending data must scale with
active interests and fixed caps, not travel history.

Run the canonical figure-eight with its recorded route/settings unchanged where
possible. The authorized new generator necessarily changes terrain content; label
v5 versus new-generator runs as a content-change comparison, not a byte-equivalent
optimization. If the scenario pins generator identity, document a new scenario
version justified by this explicitly requested generator replacement, retain all
old evidence and baseline the new version. Do not alter speed/radius/view distance
or other workload inputs to obtain a pass. Record exact source, seed, settings,
engine/hardware, save state and profile environment for each run.

The ledger already identifies the current figure-eight's forced Z=0 route and
return-position limitations. New high terrain can make that path underground;
the unchanged run can measure its workload but cannot prove ordinary surface
traversal or safe return. Use separate fixed playable observations for those
claims. If the old route is substantively invalid for the intended landform test,
document why and obtain explicit approval for a revised route/scenario; do not
quietly change its height or call different paths comparable.

The storage-specific measure-first exception in the performance route does not
automatically apply to landforms. Preserve relevant existing criteria; expose
unexplained regressions and seek explicit acceptance for any proposed relaxation.
No claim of "fast" or "best in class" precedes measured playable results.

## 9. Definition of done

The first slice is complete when one new exterior recipe produces the reviewed
landform variety, all eight controls have meaningful documented effects, retained
caves and edits remain correct, CPU/GPU/bounds/lifecycle integration is qualified,
and the comparable performance evidence has an explicit acceptance decision.
Old surface generation is absent from production, unrelated systems are preserved,
and the manager contains orchestration rather than generation algorithms.

Climate, trees, water and cave overhaul are not required for this completion.
Current status: research and plan only; all runtime gates remain pending.
