# Regional landforms: first-slice implementation plan

Date: 2026-09-08. Status: paused by user on 2026-09-09; acceptance remains pending.

The user explicitly redirected work to water after the prolonged cave parity
investigation. Preserve the current candidate and evidence. Do not continue
cave numerical changes or use completion of this plan as a prerequisite for
starting the water slice. This is a scope pause, not acceptance of the nine steps.
Branch: `codex/terrain-biome-generation`, created from `794b14f` on
`codex/terrain-deformation`. This plan is the selected first-slice scope;
[biome terrain research](../Research/BiomeTerrainGeneration.md) owns comparisons,
design rationale and the later biome roadmap.

Implementation starts from `8045b77`. Preserve generator-9 caves, including the
512–32768 depth envelope and the 16384-unit regional mask at cutoff 0.36.
The source handoff supersedes the original version-5 planning assumptions.
The current exterior uses generator version 12. Versions 10 and 11 below are
retained implementation history; their validation does not establish version-12
acceptance.

### Current recipe and acceptance state

The working tree now contains the version-13 cave parity candidate. Its exterior
is unchanged from version12; cave cell/rank selection uses integer arithmetic
and quantized inputs. Prior v12 results below remain historical evidence, not
v13 acceptance. The v12 save is preserved by the versioned recipe selector.


Version 12 implements the mountain/cliff candidate B described at the end of
this plan. It keeps the eight controls and adds one independent fine field at
LocalLandformScale/8 to the original five fields. MountainAmount is remapped by
`a*(2-a)` before eligibility. With mountain weight M, local shape q, fine field f
and ruggedness u, the ridge is `r=1-abs(2*q-1)` and the mountain profile is
`0.10 + 0.55*r^3 + 0.28*smooth((r-0.55)/0.12)*smooth(2*M)
+ 0.08*u*(f-0.5)*r`. Hills add `0.08*u*smooth((f-0.45)*4)` to their original
profile. The original plains, ocean profile and weight normalization remain.
Here `smooth(x)` clamps x to [0,1] and evaluates `x*x*(3-2*x)`.

The cliff contribution fades at mountain margins. Ridge crests are continuous
but the absolute-value ridge is not generally continuously differentiable at
its peak; the original version-10 smoothness claim below is historical.
The global height envelope remains `[-0.70,1.04]*ReliefHeight`, with conservative
interval propagation and outward rounding. CPU and GPU source owners are
[RegionalLandforms.cs](../../Code/Voxels/Generation/RegionalLandforms.cs) and
[voxel_regional_landforms.hlsl](../../Assets/shaders/voxels/voxel_regional_landforms.hlsl).

Single-host version rejection, same-world edit/save/reload and matching
cross-world restoration have passed their bounded version-12 scenarios.
Finite-range and coupled-scale invalid settings preserve the active world.
Earlier multiplayer and shader candidates failed with GPU memory faults; those
runs and their unresolved root-cause limits remain in the validation ledger.
The current G2/H/I implementation uses shared GPU topology buffers and rejects
degenerate triangles before index allocation. Its cold startup and four
104-region geometry audits (origin, edited plains, mountain and foothill) pass
with zero observed degenerates or other geometry errors. This does not establish
multiplayer or performance acceptance.

LANDFORM-DENSITY-012/v1 observes875regular samples across LOD0..6 with maximum
CPU/GPU error0.001953125, zero lattice error and zero sign/nonfinite/bounds
failures. Version2adds11closest stored-density probes, including two LOD3near-zero
probes with matching signs and error<=0.0000076293945. Version3also compares4542transition samples across all36face/LOD-pair
combinations, maximum error0.015625 with zero sign/nonfinite/bounds failures.
No transition near-zero samples occurred; broader near-zero coverage remains
unqualified. LANDFORM-CAVES-012/v1 adds3780CPU depth/envelope samples
across28columns, with123cave-air samples and zero recorded envelope failures;
source comparison preserves the original cave recipe. Cave traversal/deep GPU
coverage remain open. Elevated views show broad relief but do not establish the
required varied ridges and saddles; the local foothill view establishes only a
bounded visible cliff. Shape, broader seams/traversal, parameter extremes,
multiplayer and performance gates remain open. The
[validation ledger](../ValidationResults.md) owns exact scenarios and retained
failures. These open gates prevent completion of all nine sections.

CAVEPLAY-012/v1-v2 subsequently reproduces deep CPU/GPU discrepancies above
the0.05limit: regular0.16308594 and transition0.63684464. Selected cave support
and104-region geometry audits pass, but deep-field parity fails. Exact sample
coordinates are recorded in the ledger; source arithmetic/compilation requires
diagnosis before acceptance. No tolerance relaxation is adopted.

The [cave parity correction candidate](CaveNoiseParityCorrection.md) records
the implemented numerical candidate, identity consequences and unqualified costs.
It remains a candidate, not an accepted replacement or an acceptance exception.

Version13 cold startup and a deep104-region geometry audit pass. Its deep
CPU/GPU maximum errors are below0.05 in the observed4regularblocks and36faces,
but ONE near-zero transition sign mismatch remains at(528,2048,-28848).
The strict parity gate still fails. Its cell-aware cave-noise bounds reduced average sampling work in
CAVE-BOUNDS-013/v1, while sampling p99 remains above the global-bound baseline.
Canonical performance and broader acceptance gates remain unqualified. See INTEGER-CAVE-013/v1 for return-protocol deviations.

### Historical version-10 first implementation recipe

The eight controls in section 4 remain the selected inputs. Five bounded,
smooth bilinear value-noise fields use independent integer-hash salts: continental,
mountain province, plains preference, local shape and twice-frequency local detail.
Each field is in [0,1]; cubic interpolation has zero first derivative at lattice
boundaries. No domain warping or variable octave count is used in this slice.
For an amount `a` and field `n`, eligibility is
`smoothstep(1.2 - 1.4*a, 1.4 - 1.4*a, n)`. This gives exact empty/full endpoints
and monotonic eligibility without claiming percentages of world area.

Land weight L uses the continental field. Mountain weight M is cubed province
eligibility times `smoothstep(0.5, 1, L)`. Plains preference is `1-(1-P)^2`; plains weight is `(1-M)` times that preference; hills take
the remainder. With local noise q, detail d and province noise m, normalized
profiles are: plains `0.035 + 0.015*q`; hills
`0.08 + 0.24*(0.65*q + 0.35*d)`; mountains
`0.2 + 0.8*(1-(2*q-1)^2)^2*(0.4+0.6*m)` plus
`0.08*ruggedness*(d-0.5)*(1-(2*q-1)^2)`.
Ocean is `-0.06 - 0.64*(1-n)^2`. Weighted land and ocean profiles blend by L,
then multiply by ReliefHeight; Z=0 remains fixed. Global exterior bounds are
`[-0.70, 1.04] * ReliefHeight`. All profiles and blends are continuous with
continuous first derivatives; these bounds include the ruggedness term.

Bounds propagate noise intervals through this recipe, with outward padding.
They must not classify a region from corner samples alone. CPU height reuse stays
build-local; settings are immutable worker inputs. No shared mutable generation
cache or extra scheduler is introduced. Native runtime and visual acceptance,
including coefficient quality, remain required after integration.

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

Implementation started from `8045b77`; this inventory now names the replacement
owners. Recheck source when changing a downstream contract.

| Owner | Required change or preservation |
| --- | --- |
| [ProceduralTerrainSdf.cs](../../Code/Voxels/ProceduralTerrainSdf.cs), `ProceduralTerrainSettings`, `SampleSurfaceHeight`, `SampleWorld` | Replace the single simplex exterior and old base/frequency/amplitude settings. Preserve the cave algorithm, sign and composition. Retain/evolve build-local `LatticeSampler` reuse. |
| Same owner, `ClassifyDensityRangeBroadPhase`, conservative range methods | Replace amplitude-only vertical assumptions with new landform bounds, then combine retained caves and canonical edit bounds. No corner-only emptiness proof. |
| [VoxelManager.cs](../../Code/Voxels/VoxelManager.cs) | Replace surface inspector controls, validate/apply one immutable settings value, advance revisions and recreate requests through current lifecycle. Keep algorithm logic outside the manager. |
| [GpuSdfDescriptor.cs](../../Code/Voxels/GpuSdfDescriptor.cs) | Include every new setting in descriptor identity, equality and stale-work rejection for regular and transition requests. |
| [GpuTerrainContracts.cs](../../Code/Voxels/GpuTerrainContracts.cs), [GpuVoxelMesher.cs](../../Code/Voxels/GpuVoxelMesher.cs), request packing | Existing four-component `Terrain` packing is insufficient. Specify one exact expanded C#/HLSL layout with offsets, types, stride and bounded request bytes before editing. Update both request paths together. |
| [voxel_sdf_v13.hlsl](../../Assets/shaders/voxels/voxel_sdf_v13.hlsl) and shader consumers | Replace exterior implementation and versioned includes coherently. Retain cave constants/semantics. Version10 now composes separate noise, landform and retained-cave includes; all consumers must compile the matching request layout. |
| [TerrainFieldCodec.cs](../../Code/Voxels/TerrainFieldCodec.cs), `WriteIdentity`/`ReadIdentity` | Update serialized settings, fixed header/identity byte budgets and generator identity. Reject incompatible/truncated input before replacing active state. |
| [TerrainFieldStore.cs](../../Code/Voxels/TerrainFieldStore.cs), [VoxelManager.Storage.cs](../../Code/Voxels/VoxelManager.Storage.cs) | Keep the existing store; integrate the expanded identity and safe new-world/restore lifecycle. Do not silently load older-generator edits under new terrain. |
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
| Ruggedness | 0.45 | 0–1 | Bounded mountain detail amplitude; leaves eligibility and base ridge shape unchanged. |
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
depth interval 512–32,768 and regional mask wavelength 16,384/cutoff 0.36; noodle threshold `0.056 + 0.016 * thickness`; cheese
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

Adopt generator revision 10, replacing the version-9 exterior.
Expand the existing settings identity and its fixed-size serialization budgets
for CPU/GPU, disk and network together. Include every field-shaping parameter and
internal recipe revision. Preserve exact seed transport and canonical float order.

Start a separate explicitly named landform demo world/save identity. Keep existing
older-generator save files intact and reject mismatches clearly. Removing the old generator
from runtime does not require deleting its saves; Git retains the prior code.
Do not auto-migrate old edits or reset the currently selected save. Before an
implementation changes the selected live world, make its new-world action and
destination concrete. The implemented action saves the previous world before
creating a new UUID; the recipe-specific selector preserves earlier selections.

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
   in the ledger before any runtime run. Capture a comparable prior-generator figure-eight
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

Implementation and validation results are recorded below and in the ledger.
Code completion alone does not satisfy the runtime acceptance gates.

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
prior-generator versus new-generator runs as a content-change comparison, not a byte-equivalent
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
Current status: implementation and qualification in progress. Modules, full
settings transport, staged save-before-switch application, safe spawn and bounded
authoring survey are implemented. Both projects and native compilation pass.
Cave arithmetic was compared with the accepted version-9 source. Three default
seeds and 75 control variants pass recorded numerical checks; repeat application
of seed1337 produces the identical survey. Invalid ranges/hierarchy and staging
preserve the active recipe.

R1 exposed stale compiled emitter layout; rebuilding the owning shader resolved
initial gaps. R4 exposed stranded transition scratch after rapid recipe changes;
R5 repairs reset ownership and drains all visual/collision work with grounded
support. These failures and their evidence remain in the ledger.

Nine detailed transects also pass their sampled bounds/repeat checks. A single-host
boundary dig/build survives save/reload with an identical edited fingerprint and
grounded support. All five frozen views settle, but their subdued silhouettes
have not qualified mountain ridge/saddle quality. A GPU mesh audit records zero
index, finite-value, bounds or identity defects and flags degenerate transition
triangles; similar flags exist in earlier audits, and this remains an open finding.

R6 adds60 exact shared chunk-face matches across origin and four near-limit
sites; the negative near-origin teleport did not settle and remains a failed
observation. A bounded real GPU-buffer diagnostic covers875 samples across all
seven LODs with max CPU/GPU error0.01638031 and exact scalar/lattice agreement.
Transition direct-density and near-zero coverage remain open.

Acceptance still requires stronger visual review, remaining boundary/CPU-GPU
coverage, multiplayer checks, final cold start and
performance qualification. The exploratory figure-eight had a large GC hitch and
an invalid post-run collision state after returning underground; it is a failure,
not an accepted baseline. A safe-return scenario revision awaits user approval.
No commit/push or declaration of all nine sections complete precedes acceptance.


### Completion audit after R6

This is a status map, not a relaxation of the preceding requirements.

| Section | Evidence established | Required work still open |
| --- | --- | --- |
| 1. Deliverable/scope | One exterior recipe; caves retained; excluded features absent. | Reviewed, distinct mountain ridges/saddles and foothills; stronger player/distant visual review. |
| 2. Replacement inventory | Old surface controls/formula/include removed; CPU/GPU/save identities use full settings. | Final call-site/source check after remaining changes. |
| 3. Deep modules | Separate pure Landforms, Caves and SDF composition; immutable settings; existing schedulers retained. | Final review of any remaining integration changes. |
| 4. Controls | Three seeds,75 variations; amount endpoints/monotonicity; relief scaling; scale correlation and patch statistics; invalid ranges/hierarchy rejected. | Combined supported-range extremes and busy/guest rejection; no claim of globally calibrated percentages. |
| 5. Recipe/bounds | Source cave comparison; sampled conservative bounds; regular GPU875-sample parity across seven LODs. | Transition direct-density/near-zero coverage, deeper cave observations and remaining coordinate/interval cases. |
| 6. Lifecycle/identity | Staged save-before-switch; repeated reset repair; same-world boundary edit save/reload fingerprints;194 legacy metadata hashes unchanged; version-12 matching cross-world restore and incompatible old-version rejection. | Guest/matching/late-join evidence. |
| 7. Integration sequence | Implementation builds and native compilation pass; R3 cold-start evidence; failures retained. | Final current-source cold start, complete playable review and accepted performance run. |
| 8. Validation | Fixed scenarios and partial numerical, rendering, collision, reset and storage evidence recorded. | Multiplayer, failed negative-site settling observation, full remaining shape/bounds coverage, benchmark-return decision and performance acceptance. |
| 9. Done/handoff | Current contracts and provisional status documented. | Every preceding open gate, final review, task-only commit and push. |

Scale-study evidence now includes finite-window correlation and connected-patch
statistics. Mountain/local scale produce larger characteristic features in
aggregate; the largest continental scale exceeds the measured correlation window
on five of six axes. Earlier RMS changes alone were insufficient for this claim.
The user authorized resuming live validation after the R6 pause. R7 subsequently
qualified the reported ridge view. The separate benchmark safe-return change still
requires the requested user decision; resuming work did not approve a new workload.


R7 resolves the user's reported vertical LOD ledge in its original camera view.
Canonical density probes tied it to coarse endpoint interpolation through the
retained cave field. Shared bounded edge refinement is implemented without
changing generator/cave arithmetic or adding buffers. The same-view screenshot
and cold-start/collision evidence pass that local reproduction. Full edited mesh,
normal, transition and performance qualification remains pending; the nine-section
completion audit above is still provisional. See the GPU architecture owner for
the numerical method and its coarse-topology limits.

The materials task's before-change figure-eight now supplies contextual R7
performance evidence: all28 overlapping terrain source hashes match R7.
Run6a49418e30944a98a914166907a420ba has improved typical GPU times versus C6,
but worse frame/GPU maxima, +12.06% allocations/frame and only201/4913 collision
chunks ready at completion. This is not acceptance. The ledger preserves exact
metrics, environment/source limits and the invalid underground post-route state.

### September 9: requested mountain, valley and cliff revision

The user reviewed the terrain as too smooth and plains-dominated, and requested
recognizable distant mountains, valleys, small mounds and mountains with cliffs.
This is an explicit shape-quality requirement for the unfinished first slice.
The earlier numerical checks do not establish that visual requirement.

Current source diagnosis: mountain eligibility is cubed, suppressing partially
eligible regions. The ridge expression 1-(2q-1)^2, squared again for height, has
a broad rounded maximum. Ruggedness contributes only a small mountain-only
perturbation at half the local landform scale. These choices explain limited
shape vocabulary; raising global ReliefHeight alone steepens everything without
introducing a new feature hierarchy.

Proposed v11 candidate, pending implementation and qualification:

- Keep the eight existing controls and defaults. Remap mountain amount through
  a*(2-a) before its existing eligibility function, preserving exact zero/one
  endpoints while increasing default mountain opportunity. Keep smooth regional
  weights; this is an eligibility bias, not an area-percentage promise.
- Replace the broad ridge with r=1-abs(2q-1) and use r cubed for sharper crests and
  deeper intervening valleys. Add a smooth localized rise over a narrow ridge
  interval to produce steep cliff bands, not vertical chunk-boundary steps.
- Add bounded finer-scale terrain signals for hills/mounds and mountain breakup.
  Keep substantial flat plains instead of applying strong roughness everywhere.
  Ruggedness should visibly control local breakup; mountain amount controls its
  regional opportunity, and relief continues to control vertical scale.
- Implement CPU sampling, the GPU mirror and conservative interval evaluation
  together. Carry every new field and nonlinear transform through interval
  arithmetic. Preserve the global height envelope or update all its consumers.
- Bump generator identity for the changed exterior; preserve v10 save data and
  avoid interpreting its edits against new terrain. Retain cave arithmetic.
  The retained cave depth follows the new landform surface as it already does.
- These remain single-valued heightfield cliffs. Undercut cliffs and overhangs
  require a later volumetric exterior feature; neither caves nor noise labels
  make that feature implemented.

Validation must retain the existing source/settings comparison and failed runs.
Before changing runtime source, finish or explicitly interrupt the concurrent
materials benchmark so its before/after measurements are not silently mixed.
Record fresh source identities; the material shader also consumes the landform
function, so measure its downstream shading cost.

Use the existing three-seed surveys, frozen landform view locations and transects
for before/after coverage. Add a predeclared dense mountain transect to measure
crest prominence and steep runs once its location is selected from the unchanged
baseline survey; do not choose a favorable new-version location after the run.
Require actual distant/ground-level views showing ridges, saddles, valleys,
foothills and cliff faces, finite and conservative fields, no LOD seams or collision
support failures, repeatability, reset/save compatibility, and the canonical
figure-eight performance review. Do not equate a larger height range with visual
success or accept a cliff screenshot while other tested views develop cracks.

The current player-camera capture was taken during the separate materials test
and shows an underground view. It is not a settled exterior before-image.

### Historical version-11 candidates (superseded by version 12)

The September9 mountain/cliff revision is now implemented in the pure landform
sampler, its GPU mirror and conservative intervals. Settings/defaults remain the
same eight controls. Mountain eligibility uses a*(2-a) before the existing
eligibility curve. Ridge r=1-abs(2q-1) produces a sharper crest; its profile is
0.10+0.55*r^3+0.28*smooth((r-0.55)/0.12)+0.08*ruggedness*(f-0.5)*r.
The extra independent fine field f has scale local/8 and salt0xD1B54A35.
Hills retain their broad profile plus0.08*ruggedness*smooth((f-0.45)*4).
Plains and ocean profiles are unchanged. All weights still sum to one.

The new mountain maximum is at most0.97 and hills at most0.40, so the existing
global[-0.70,1.04]*relief height envelope remains conservative. Absolute-value
intervals and the new fine field are propagated through the local bound.
Cave arithmetic, density sign, request layout and the eight settings do not
change. Generator11 selects a separate saved-world identity; v10 edits must not
be interpreted against the new exterior. The superseded v10 GPU include is
removed, and compute roots now include v11. Materials consume the same landform
function and require combined performance qualification.

.NET build passes0 warnings/errors. Live validation is pending the cold shader
restart; an initial transition shader write hit the existing mapped-resource
error. No acceptance follows from compilation. The earlier Frozen first
implementation recipe above describes v10 history, not the current candidate.

Candidate B now attenuates the cliff rise by smooth(2*M). This removes the two
candidate-A pure-plains slope failures while preserving full-mountain cliffs.
Three-seed sampled bounds/repeat/plains-slope checks and both detailed cliff
transects pass. The current player is grounded with4913 collision chunks ready
and no pending work. Cave CPU/GPU files match R7 exactly. See v11b-source.json
and the ledger for source identity and all retained failures. Default mountain
coverage is now45-53% of strongly inland survey samples and remains subject to
visual review. No performance/overall acceptance follows from these results.

### Version12 identity and edited-world qualification

The chosen candidate-B formula now has generator identity12. Version11 prototype
saves remain untouched and are not reinterpreted. The GPU include and compute
roots use12. Numeric terrain values match the v11B default survey exactly;
this is an identity repair, not another height recipe.

LANDFORM-IDENTITY-012/v1 verifies rejection of the old v10 edited slot with no
active-world replacement, and new v12 boundary dig/build, save and same-world
reload with an identical edited fingerprint. Grounded collision support and
all4913-ready/zero-pending state pass at that site. The current world is v12
b60920a5-70ed-408d-afe1-5eabee43a7b9,revision2,saved aslandform-v12-edits.
LANDFORM-CROSSWORLD-012/v2 now verifies matching cross-world restoration with
the original UUID, six edited pages and identical fingerprint after two fresh
world applications. LANDFORM-INVALID-012/v1 verifies finite-range and coupled-scale
rejection without changing that world. Final shape review, broader geometry/parity,
multiplayer and performance acceptance remain open. Earlier v10/v11 results are
retained history and must not be relabeled as current v12 acceptance.


### Current I/H selected-site qualification

LANDFORM-SITES-012/v1 and FOOTVIEW-012/v1 pass the recorded edited-plains,
mountain-top, mountain-foot and return-origin geometry/settling observations.
Each104-region audit has zero degenerates and zero other listed geometry errors;
all collision/visual queues settle with grounded support and saved edits intact.
The foot screenshot shows a substantial steep face; broad ridges/saddles and
full seam/traversal coverage remain to be reviewed. These bounded checks do not
replace multiplayer or the canonical figure-eight performance requirement.
The editor currently exposes66native tools but omits13project controls; a
source-timestamp refresh did not restore them. Native player/scene observations
remain available while custom camera/benchmark tooling recovery is outstanding.

MCP-REGISTRY-001/v3 restored all79tools through an editor metadata hotload and
exact source restoration. Custom controls are available again; cold-start
discovery reliability is not established. The pending benchmark decision is
[the proposed return correction](FigureEightReturnProposal.md), not yet applied.
