## Desert sand (2026-09-20, qualification pending)

Desert climate coverage adds a continuous vertical surface layer, independent of
the shoreline recipe below. Its thickness varies from5 to10 base cells (80–160
world units), biased toward the deeper end by `10 - 5 * noise^3` cells. The seeded
XY value noise uses a2048-unit lattice and salt57191. ProceduralSand owns the
parameters and CPU formula; GpuVoxelMaterials binds them to generation-only HLSL.
One additional noise sample per desert column, with no new buffers/vertex channels
or draw-time sampling. Below the sand, existing dirt/stone rules resume. Water,
air, placed-material precedence and terrain heights are unchanged. Desert sand
does not activate the independently selected buried shoreline deposits.

The material recipe uses transfer protocol5. Save density identity remains49;
existing explicit edits persist and remeshing derives the new material layer.
Validation is BIOME-DESERT-SAND-001/v1, plus the paired biome figure-eight.

## Shoreline sand and seeded material regions (2026-09-15, qualification pending)

Sand is stable catalog ID6, with warm pale-yellow checker colors. Generation
selects actual material nodes; water/air and explicit placed dirt keep priority.
Sand changes material identity only, without changing density or collision.

ProceduralSand owns the immutable recipe. SAND-001's continuous strips were
rejected by the user; SAND-002 replaces that implementation completely. Natural
height before river carving distinguishes coasts from lowered inland valleys:
the river influence rises from0 to1 over sea-relative natural heights32..160.
This blends river mouths smoothly, without another terrain or water query.

Two seeded simplex scales (primary and quarter wavelength, weights.75/.25)
create coherent gaps. Ocean wavelength2048/cutoff-.10 permits substantially
more coverage than river wavelength512/cutoff.40. These are noise thresholds,
not promised percentages of shoreline. Coverage above the cutoff modulates
dry-bank height and deposit depth. Ocean sand reaches at most48units above
water and96units into the exterior; river limits are12 and64 respectively.
Most patches are narrower/thinner than those limits. Dry sand also requires a
wet canonical exterior at one of four cardinal probes. Probe reach varies with
coverage strength, up to640units for oceans and48for rivers, with25%minimum.
This bounds horizontal beach width even on very flat terrain. It is conservative:
diagonal or narrow wet contacts may be missed, leaving gaps rather than inland sand.
Regular/transition river captures add the640-unit XY dependency halo so results
do not depend on a request's atlas boundary. Each selected layer is at
least48units (three base cells) deep. Ocean floors whose natural exterior is at
or below sea level can carry patches at any water depth. The sea-relative-256
floor limit remains for carved inland rivers. Deep ocean patches use the same
seeded ocean coverage and3..6-cell thickness; water and air retain priority.
Unselected dry banks retain their normal grass covering (or mountain stone/snow
where eligible). The user rejected the explicit river-bank dirt strip; its
grass-suppression rule is removed. Submerged or covered soil remains dirt, and
explicit dirt edits keep priority. Rivers have sparse sand among grassy banks.

Occasional buried sand requires a selected sandy column and surface-relative
depth144..256. No unrestricted inland or deep underground deposits exist.
Bounds are world units (base cell16); exact formulas live in ProceduralSand.
The four-probe check is a bounded proximity test, not an exact coastline-distance field.
Player additions above the original exterior remain dirt.

MaterialSpawnRegion is the material-independent foundation for buried deposits.
Explicit seed, salt, region dimensions and probability determine one
ellipsoidal deposit per eligible region. Floor partitions negative coordinates.
Two integer hashes choose occupancy, a jittered center, and radius; there are no
global RNG calls, traversal dependencies, caches, allocations or neighbor scans.
Centers0.4..0.6 and radii0.22..0.34 of region extent keep deposits inside their
own region. Region edges are therefore intentionally deposit-free; this is not
a vein generator. Buried regions are768x768x384, probability.15/salt29137.
The beach coverage recipe reuses TerrainNoise with salts17011/41333. Probability controls
region activation, not the final fraction of sand blocks. Material/depth/water
eligibility remains owned by the calling recipe. Future ores can reuse this
sampler with their own eligibility; no ore catalog entries or rules are added.

CPU logical queries and the generation-only HLSL mirror consume identical
parameters. Every regular/transition crossing still reconstructs the existing
eight material contributors. Sand contributes to normalization while the four
existing weights are stored explicitly; sand is their remaining weight to one.
Packing preserves the rounded explicit-weight sum, preventing false sand from
independent rounding. Dirt overrides attenuate all inherited weights through
the existing interpolation, including implicit sand. The draw shader blends
the fifth palette entry without any region/noise/height evaluation. Vertex and
edge buffer sizes remain unchanged (28-byte vertices); palette grows32 bytes.

This is immutable derived material generation. Workers only read recipe inputs;
the mesher owns engine-thread palette lifetime and generated geometry. A fresh
world/Play rebuild regenerates the material derivatives. No separate mutable
material state or save format is introduced. Like the prior snow rule, existing
worlds adopt the new derived palette/assignment while retaining density and
explicit dirt edits. Transfer protocol4 rejects peers running the earlier material
recipe through the existing manifest version check; save identity stays unchanged.
Multiplayer parity needs qualification and is not established by matching source.

Alternatives: per-node independent random rolls cause speckle; coherent regions
provide grouped deposits. A general biome/ore graph exceeds this slice. Extra
vertex channels would grow all geometry; an implicit fifth normalized weight
fits the existing payload. This representation supports five blended solids;
future additional types must revisit encoding explicitly, not overload an ID.

Budget: two simplex samples per eligible shoreline column, evaluated once for
both contributing Z nodes, plus at most two hashes per eligible buried-region
lookup. Eight contributors per crossing. Selected dry columns additionally make
up to four composed terrain/river queries (stopping at the first wet point), with
the640-unit atlas halo. No recurring CPU allocation. Ineligible columns skip noise.
These are operation/storage bounds,
not measured FPS claims. Canonical figure-eight, shader cold start, shoreline
appearance, material queries, edits and LOD correctness remain acceptance gates.
