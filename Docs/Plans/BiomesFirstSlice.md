# Eight-biome groundwork

2026-09-20. Goal active. Research: [biome study](../Research/BiomeTerrainGeneration.md).
Independent review must accept actual biome visuals and performance.

The subsequent [marsh slice](MarshFirstSlice.md) appends a ninth label and bounded
lowland conditioning in generator50. This document records the original eight
biomes; its pending performance/visual gates are not waived by the expansion.

## Scope and ownership

The latest user direction is explicit: biomes only, with no tree-system or asset
work. Biomes own temperature, moisture, habitat weights, eight labels, surface
rules and modest terrain variation. Forest/jungle expose habitat information for
vegetation work elsewhere. Prepared tree-population changes are withdrawn; they
must not be applied or counted in this task's acceptance.

Preserve the existing natural-height, mountain, erosion, drainage, cliff and cave
recipes. TerrainBiomes owns pure climate and classification. RegionalLandforms
continues to own natural/final exterior height and its conservative bounds.
Desert refinement follows the unchanged river result; the canonical SDF
and GPU mirror must consume that same exterior. Materials retain their existing
CPU contract and GPU mirror. No competing world store, terrain generator or
vegetation implementation.

Inputs are absolute coordinates, immutable settings and seed. No time, mutable
random stream, loaded-neighbor dependency or incidental iteration order. Pure
queries can run on workers; engine resources retain their existing owners.
Advance generator identity before changing the generated field. Preserve saves;
do not reinterpret existing edits against a new procedural base.

## Implemented authoring slice

Production TerrainBiomes sampling is integrated into the existing column and
landform-survey queries. The optional previewSeed surveys an immutable recipe
copy without switching or changing the active world; metadata distinguishes
SourceWorld and IsSeedPreview. This uses production generation functions.

The live candidate now uses generator49 and transfer protocol5. Climate, bounded
desert relief, material layers, surface tint and debug UI are integrated. Earlier
copies under .codex/biome-work/Code and Assets are stale preparation artifacts;
do not reapply them. Authoring output alone does not establish rendered quality.

## Climate and labels

Temperature and moisture use independently salted cubic bilinear value noise at
65536/32768-unit spacings, weighted .85/.15, clamped after1.5*N-.25. Defer elevation
cooling and domain warp in this first slice. Unconditional peak snow is removed;
cold climates can still cover peaks.

Snow support requires temperature<=.30; hot desert requires>=.70. Compatible
open/wooded habitats fill the gap. The complete temperature gradient is bounded
by1.5*1.5*sqrt(2)*(.85/65536+.15/32768) per unit. The .4 support gap exceeds7100
units. Verify climate presentation support<=102 units per side to retain at
least128m of intermediate habitat. Shoreline-query halo and climate-filter support
are different dependencies and must be checked separately.

Eight labels: ocean, plains, hills, mountains, desert, snow, forest and jungle.
Landform and habitat weights remain distinct: forest can cover hills. Weights
normalize and ties use fixed enum order. Marine context explicitly uses
SampleNatural; an inland river stays a water overlay within its regional habitat.
Dry support is required for land-biome visual sites.

## Terrain and surfaces

2026-09-20 user refinement: desert sand is a real material layer extending5–10
base cells below the unchanged exterior, spatially varying and biased toward10.
ProceduralSand owns minimum80/maximum160 world units, XY noise lattice2048 and
salt57191. Thickness=max-(max-min)*noise^3. One coherent seeded scalar per eligible
column; no per-frame work, new material, vertex data, or height change. The CPU
query and generation HLSL use this same formula and CPU-bound parameters.
Below this independent layer, resume existing dirt/stone strata. Keep shoreline
layer/deposit eligibility separate, so desert eligibility cannot enable unrelated
buried beach deposits. Explicit placed materials and solid/water/air precedence
remain authoritative. Material-only updates preserve saved density/edits;
transfer protocol5 rejects clients with the prior material recipe.

The user's final follow-up requires snow depth5–10cells too, using the same cubic
bias toward10 with independent salt62119. ProceduralVoxelMaterials owns the snow
recipe and shared CPU layer-depth formula. Generation HLSL mirrors that one
formula with CPU-bound parameters. Snow covers solid, unplaced material below
the canonical dry exterior and remains visible when excavated. Existing strata
and independently eligible shoreline deposits resume below. Air/water/placed
materials retain precedence. Density, height, collision and save data are unchanged.

Desert relief uses max.005*ReliefHeight, below the hard.02 ceiling;
shortest added noise lattice2048 units. Drainage natural heights stay exact.
Refinement is zero where conditioned height<=sea+64, mountain weight>=.5, or river
carving>=16 units. Smooth masks connect protected regions to eligible dry land.
These numerical supports do not claim every outer river valley is immutable.
Wet/dry signs and drainage nodes/segments/radii/flow must remain unchanged.
Propagate amplitude into local/global bounds and every actual field consumer.

Desert sand does not enable unrelated buried deposits. Snow follows
climate and dry-surface coverage rules. Preserve explicit placed material, strata,
water and cave semantics. Use existing material assets. Forest/jungle need
distinct climate domains and published habitat data; their biome surface
presentation is implemented and still needs final visual review.

Surface colour uses the existing grass/soil assets. Forest climate
has a muted earthy tint; jungle climate has a darker green tint and darker soil.
This evaluates the shared climate function per vertex, then interpolates colour;
it introduces no per-pixel climate sampling or vertex-layout change. Surface
colour follows climate independently of landform eligibility, and does not alter
canonical material IDs or placed-material precedence. Seed binding follows the
world reset. Its extra vertex work needs measurement and actual visual review.

## Validation

Record fixed scenarios and every result in [ValidationResults](../ValidationResults.md)
before runs. Source review is not numeric GPU parity or runtime acceptance.

BIOME-SURVEY-001/v1: seeds1337,2026,-7331; land.75, mountains.3, plains.6,
continental77724.09, mountainRegion18681.756, local5232.39, relief3072,
ruggedness.45, sea0. Grid[-262144,+262144]^2, spacing4096,129x129. Each biome in
each seed: dominant fraction>=.5%, largest four-connected core>=9 samples at
weight>=.75. All weights finite/nonnegative, sum error<=1e-5, zero climate
eligibility errors, identical repeated heights and heights within local bounds.
Report all fractions, components and core locations. Coarse sampling does not
prove fine transition widths.

All three seeds pass these authoring gates. Example evidence:
[summary](../ValidationEvidence/Biomes/seed-1337-authoring-v1/summary.json).
The river export reached its existing100000-segment ceiling and is truncated; it
cannot prove complete drainage equality. Nearest snow/forest/jungle climate-core
samples may be underwater after inland carving. Before visual runs, additionally
require dry exterior support for land views and report dry-core coverage. Retain
the original authoring result rather than interpreting it as visible land cover.

Choose nearest qualifying core by squared distance then X/Y, with actual dry
support for land sites and water for ocean. Freeze sites/support thresholds and
fine transects before captures. Native1600x900/FOV75, same lighting, two opposite
ground views plus elevated view. Inspect climate transitions, shores, material
boundaries and relief through actual field/render/collision paths. Maximum added
slope at neighboring16-unit samples<=.25 is a sampled gate, not a derivative proof.

Performance: retain the canonical figure-eight route/settings: origin
(-1.6258175,1.2225341,340), identity view, speed2500, distance50000, one loop,
clearance393.7008,2769x1529/FOV75, gameplay8, visual128,LOD0-5,extents4/4,
32x16 cells, grass64m, one visible interactive player, settled start and automatic
drain plus10s standing. Freeze clouds, source/assets and terrain identity.
Moving/standing FPS regression<=5%; p95/p99, per-frame allocation and memory<=10%;
no unexplained streaming/drain/completion/correctness regression. Require numeric
CPU/GPU agreement, reload and negative-coordinate checks. No hotloads, screenshots
or competing renders during timing.

The user authorized fresh matched worlds. BIOME-FIGURE8-001/v2 corrects v1's
impossible grounded-at-z340 preflight before any timed run. The generator48
baseline completed; final candidate results belong in the ledger. Older cloud
failures are historical evidence, not a biome baseline.

## B biome inspection (2026-09-20 addition)

The user authorized fresh maps and the in-game debug views. Preserve the previous
save through the existing save-before-switch path. BIOME-FIGURE8-001/v2 compares
fresh seed1337 worlds before and after integration with the canonical route.

The existing overlay now has a live location readout and an optional interactive
map. The user reported F9 intercepted by the editor and requested an ordinary key;
the BiomeDebug action now binds B, with hints following the configured binding.
Temperature/humidity are normalized generation controls, not
physical units. Map layers: dominant biome, blended biomes, temperature, humidity.
Use the production natural-landform and climate samplers with applied settings;
inland water does not turn a regional habitat into ocean. No tree changes.

The overlay owns its derived map texture, CPU pixel buffer, viewport and selection.
Bound each map to129x129 cell-center samples; fixed initial extent524288 units,
with zoom/pan and a player marker. Generate rows incrementally only while a map
request is active, no background world/river export. A recipe or world identity
change invalidates the image, selection and terrain colour overlay before rebuild.
The renderer consumes this same sampled texture in absolute XY; outside its stated
extent the regular terrain remains visible. The legend and map explain sampling
resolution. Full terrain recolouring uses the selected map layer, with no extra
biome implementation in the shader. Debug state is local and never changes saves.

The readout remains non-interactive while walking. Opening the map captures
mouse/brush input through the existing admin-input boundary, preserving B/Escape
to dismiss it. Closing disables recolouring and stops unfinished map work.
Texture lifetime belongs to the panel; the renderer must unbind before disposal.
Normal play has no map sampling, allocations or overlay texture reads.

Alternative rejected: per-pixel full landform sampling duplicates classification
and adds substantial debug shader cost. The bounded shared texture provides
explicit, inspectable approximation and matching map/terrain presentation.

Research committed/pushed as4b2ea0b. Climate sampling and authoring integration
are live and compile. All three seeds pass distribution/numeric authoring gates,
and have qualifying dry/marine cores. Eight paired production queries return
identical climate/weight/height/settings values and the expected labels.
Independent source review caught and corrected GPU-only buried-sand eligibility,
marine-input ambiguity and disabled-panel input capture. Snow/desert fixed-grid
distribution and exact-boundary production probes pass for the final5–10cell
recipes, as do explicit placed-dirt controls. Independent review approves local
snow/sand excavation appearance. All four map layers render with readable legends;
terrain recolouring applies and clears. Full eight-domain, input and lifecycle
acceptance remains outstanding. The first matched run passes frame/pacing/allocation
gates but fails process-memory gates; a fresh-editor repeat is pending, and current
clean-restart evidence includes a shutdown failure. Vegetation proposals will not
be applied.
