# Connected drainage basins

Status, 2026-09-09: the user explicitly approved the generator35 branching and
river appearance: "What ever you're doing with the branching looks SO GOOD!"
Preserve this network method and its curve recipe when adjusting banks. The user's
later width request explicitly refines the radius recipe below without rerouting.
Full runtime acceptance remains tracked in [the ledger](../ValidationResults.md).

2026-09-10 River11/Generator40 width revision: user reported repeated circular
swells in main branches and asked to prioritize their shape. Retain the exact
drainage tree, enabled outlet selection, centerline controls, segment count and
delta connectivity. Replace independent node width jitter and the42% per-reach
sine swell with one seeded XY simplex modulation of22% at8192-unit wavelength.
Shared endpoints use the same modulation. Interior width normalizes by the
interpolated endpoint modulation so tributary/delta endpoint radii still agree.
Catchment-based widening and headwater taper remain. This produces broad changes
over several reaches rather than a separate bulge per2048-unit reach. It supersedes
the historical River9 width recipe below; visual validation is pending.
Generator40 has a new saved-world identity; Generator39 worlds/edits are preserved,
not silently reinterpreted on the revised bed.

River12/Generator41 additionally scales the existing lateral displacement by
reachLength/(reachLength+6*maximumEndpointHalfWidth). Wide trunks therefore make
less of the short lateral wiggle that created overlapping rounded outer banks;
narrow streams retain more of it. Drainage nodes, Bezier controls, exact shared
endpoints, endpoint tangents, width modulation, branch selection and deltas stay
the same. This supersedes the historical requirement to keep the full lateral
amplitude below in response to the user's visible lobe report. River11's width-only
image is retained as an intermediate result; it still showed lobes at tight bends.

## The branching recipe to preserve

RiverDrainageBasin constructs a seeded 128 by 128 node drainage tree spanning
262144 world units. Nodes are 2048 units apart, jittered up to256 units on each
axis. Every naturally submerged node seeds an ascending priority flood. Each
land node chooses an already discovered parent; that ordering prevents cycles
and routes through local depressions instead of stopping at them. Flood heights
are routing metadata, not water elevations. Queue ordering is explicit: height,
distance, seeded hash, then index. Reverse order accumulates exact catchment.

Eight contributing nodes qualify a stream. A retained node must lie on a
qualifying source-to-water course of at least16 edges; short independent courses
are filtered, while tributaries can join long trunks. Shared parents create the
branching naturally. Each drainage edge becomes16 curved sections with matching
endpoints and tangent joins. Seeded lateral variation and varying endpoint radii
produce different courses. Preserve the current 0.06..0.14 edge-length lateral
amplitude and seeded width factor0.85..1.15. River9 raises interior width variation
to42%, with an independent seeded phase and0.75..1.5cycles per reach. A sin-squared
envelope fades the variation to zero at shared endpoints; neighboring sections
interpolate radii continuously. This gives each reach different broad and narrow
stretches while retaining the same centerline and connections.

River10 reduces frequency by40%: after computing the original drainage tree, each
basin ranks eligible submerged outlet roots by an independent seed/root hash
(index breaks ties) and retains60%, rounded to a whole system. Selection propagates
from each root to all its descendants. Retained rivers keep their complete
tributaries, widths and delta recipe; individual sections are never randomly
removed. This controls the number of outlet systems, not the total channel length
or water area. A small basin may differ from40% because rivers are indivisible.

Base half-width192 (20% wider than river8) scales with sqrt(clamp(catchment/48,0.16,9)). Headwaters taper
to40% of that width. At a coast-reaching mouth with at least96 contributing nodes,
up to two narrower distributaries branch to submerged neighboring nodes on
opposite sides of the main outlet. They must connect to that retained mouth;
there are no independent delta dots or inland decorative branches.

The accepted fixed survey measured43728 sections,334 tributary joins,18 coastal
splits and complete courses up to181820.616 units. It had no cycles or zero-length
sections. This is evidence for seed1337, not a guarantee of those counts in every
world. The raw survey is [retained here](../ValidationEvidence/Rivers/flat-basin-survey/recipe.json).

## Terrain and water ownership

All endpoints use exactly the configured SeaLevel, default0. The river mask
carves the canonical base exterior before caves, cliffs, meshing and collision.
Water cells belong to terrain chunks. Their exposed coplanar tops are batched
only for resident, active chunks; the same carved exterior clips each chunk face
into oceans, rivers and deltas. There is no independent world-sized visible plane.
Elevated water ribbons and per-node circles were removed. There is no mutable
fluid simulation.

Minimum composition joins overlapping valleys continuously and never raises
terrain. River13 replaces the former48-unit constant bed depth with width-biased,
seeded24..216-unit depths; see SurfaceWater's current contract. Bank blending is the current refinement:
shoulder width is clamp(1536 + max(0,naturalHeight-SeaLevel)*1.5,1536,6144), so steep surroundings get a wider transition than low plains. This changes
the terrain transition, not the accepted network or water footprint on dry land.
TerrainField remains the sole authority for subsequent solid edits.

## Bounds, lifetime and limits

Basins with no submerged seed produce no rivers. Basin boundaries are drainage
divides; this bounded implementation does not route across them. A submerged seed
can represent an inland lake. Do not claim global ocean connectivity or physical
hydrology. Unbounded global flooding was rejected for a streamed world; local
minimum routing was rejected because it discarded most inland catchments.

RiverWorld retains up to64 immutable basins per recipe, each16384 nodes, and8192
immutable geometry patches. Shared Lazy builds run outside locks; cancellation
of one consumer cannot poison shared results. Region snapshots admit4356 patches.
GPU transport stores unique endpoint pairs plus a shared spatial hierarchy (two texels per node), capped at8million float4 texels/128MiB per atlas. Cache eviction
changes residency only; explicit seed/configuration/version identify results.
The river's terrain bound is min(natural minimum, SeaLevel-216) to natural maximum;
classification does not construct drainage. Native CPU/GPU checks, clean-start
visual checks and the unchanged figure-eight remain required.
