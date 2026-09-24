# Coastline first slice

Status: implemented. Managed/shader compilation, production surveys and independent
first-slice beach/map review pass. Observed figure-eight numeric gates pass, with
a recorded survey-cache order limitation. Clean-editor-start acceptance and an
in-world placed-material override check remain pending; water speckles are
visible and their origin is unresolved. No final acceptance/commit is claimed.

Coastline is the tenth stable biome label. It marks the shallow margin between
the existing natural ocean and land, independently of climate. Terrain heights,
marsh pond placement, rivers, water level and existing saved density remain intact.

`TerrainBiomes.SampleCoastline` owns a deterministic affinity. Natural height
relative to sea level fades in over[-96,-32] and out over[32,128] world units.
The center and four cardinal samples at640units must include natural heights on
both sides of sea level; the two proximity factors fade over16units. These are
pre-river/pre-biome heights, so generated rivers and marsh pools cannot create
coastlines inland. This is a bounded local shoreline test, not a global ocean
connectivity or exact distance calculation; tiny coves/islands can be undersampled,
and naturally enclosed sea-level depressions can qualify. This matches the current
natural-basin ocean model rather than establishing global ocean connectivity.

Existing marsh weight takes priority. Coastline receives `(1-marsh)*affinity`;
the remaining original land/ocean weights receive the leftover share. The ten
weights sum to1. Compute affinity once per debug/survey point, not once per label.
Append the label and palette entry without renumbering existing biomes.

Coastline's sand uses the existing procedural material owner and48..96-unit
surface layer. It is selected when coast weight exceeds the existing coherent
coverage threshold; transitions retain the earlier shoreline sediment rules.
Marsh, snow and explicit placed-material priority remains unchanged. Strong
mountain columns suppress the new beach and old shoreline sand when coast coverage
wins. Existing desert sand, snow and gentle mountain grass rules retain priority;
this does not promise every mountainous coast is bare rock. Do not introduce gravel
placement, vegetation, extra material IDs, vertex channels or new textures.

CPU-owned band/reach values bind to the matching GPU material-generation helper.
The existing640-unit sampling halo already covers the new natural-height probes.
No per-pixel terrain queries are added. Most columns reject outside the altitude
band; eligible columns use at most four natural-height probes. Map generation,
material generation and total frame/streaming cost still require measurement.

Density generator52 and water8 remain unchanged. Protocol8 requires the new
procedural material behavior while retaining protocol7's gravel support. Saved
density/placed materials keep their existing identities; derived meshes/materials
rebuild through the existing lifecycle. No competing coastline/world cache exists.

An elevation-only label was rejected because it can mark flat inland lowlands
as coast. A global distance field or ocean flood fill is unnecessary for this
bounded first slice and would introduce cache/state ownership. Preserve the
current coast geometry instead of reshaping the continent to fit a new biome.

Acceptance requires production surveys with ten normalized weights, unchanged
height values, observed coast cores and inland river/marsh counterexamples;
matching CPU materials and actual GPU beaches; B-map/readout/legend agreement;
and independent rendered review. Freeze native scenarios in the validation ledger
before running. The canonical figure-eight remains required; unrelated tree
asset mutations must not contaminate a before/after pair. Prior marsh performance
and cold-start gaps are not waived by this slice.
