# Marsh first slice

Status: source implementation and managed builds complete; independent source
findings resolved. No rendered, shader or performance acceptance yet.

## Contract

Marsh is the ninth stable biome label. It occupies warm, humid lowlands around
the existing sea-level water table. This slice does not add perched lakes,
fluid simulation, vegetation, or assets. The existing terrain remains the base.

`TerrainBiomes` owns deterministic climate, marsh eligibility and bounded bed
conditioning. Eligibility uses temperature .30â€“.50, moisture .60â€“.85,
mountain exclusion, and natural height relative to sea level: zero outside
[-128,192], full altitude eligibility in [-32,64]. Its weight takes a normalized
share of the prior land/ocean labels. A marsh label describes a habitat containing
both dry banks and pools; it is not a promise that every column is underwater.

The bed approaches sea level plus a coherent -24..24-unit, 512-unit-wavelength
field. Absolute displacement is bounded by .02 times ReliefHeight (61.44 units
with the default recipe); existing river cuts suppress this conditioning.
Desert displacement remains .005 times ReliefHeight. Bounds include the larger
limit. CPU and GPU use the same recipe. Generator52 and replication protocol6
reject mismatched identities. The existing last-world selector includes the full
identity and generator version, so this recipe opens its own selector while
prior world saves remain untouched. Explicit old-identity loads are rejected.

The user approved the initial appearance and requested larger water features.
Generator52 expands the existing seeded basins with the monotonic profile
`n*(.5+.5*n)` before mapping noise to target height. The profile never raises
the previous target, so existing wet columns cannot become dry from this change.
Ponds grow outward and can join while basin centers retain their original layout.
Target depth range, climate eligibility, water appearance and displacement limits
stay the same; local bottoms can become lower within those limits. Generator51's
plain wavelength increase was rejected because it relocated features and reduced
wet coverage in the fixed survey. Actual extents remain clipped by natural terrain.

Marsh soil uses the existing Dirt material for the shallow 144-unit stratum,
ahead of shoreline sand. Exposed hummocks above sea+12 retain Grass. Explicit
placed materials still win. Dampness changes soil color/roughness and grass
ground tint in the existing terrain shader; no new material channel is added.
Terrain presentation uses climate and surface altitude, while classification
uses natural altitude. These are deliberately separate from solid material IDs.

## Water ownership and cost

`SurfaceWater.Resolve` remains the medium owner. Generated water cells use the
same refined pre-cave bed as the SDF/material queries. Existing chunk scheduling,
publication, cancellation, edit invalidation, convex coverage and rendering stay
in place. Non-river hidden pools participate in interior sampling down to the
existing 16-unit lattice, conservatively pruned by natural-height bounds and
regional climate bounds. Pools below this sampling resolution are not promised.

The optional water appearance atlas stores normalized flow XY and canonical
marsh weight in RGBA8888 (four bytes per sample, previously eight). Byte128 represents
zero flow exactly; quantization error per axis is at most 1/254. Samples on dry
corners retain habitat rather than dropping it when the corner is above water. A page
is 528x528x4 = 1,115,136 bytes; previously 2,230,272. Additional marsh pages can
still increase total ownership and require measurement.

The shader uses marsh weight and existing river strength to
blend olive-brown tint, shorter transmission distance and calm ripples. Full
marsh influence has zero advection. Deep water and strong river flow retain
their existing appearance. Camera-ray depth controls optical transmission only;
it cannot change habitat tint or advection. No per-pixel terrain generation is
introduced. Habitat interpolation still follows the existing33x33 water lattice;
fine interior-only pools at coarse LOD require rendered review for lost tint.

Perched pools were rejected for this slice because they require new reservoir
ownership and vertical scheduling. RGBA32F was rejected because it doubles the
existing per-sample footprint. Exact new mud assets and plant populations are
outside the request; rendered review must expose any limitations of reused dirt
or surrounding vegetation instead of assuming high fidelity from source alone.

## Acceptance

Use existing production survey, density/mesh audits, playable-world water checks
and canonical figure-eight. Freeze scenario parameters in the validation ledger
before running. Require visible pools and mud, readable reflections/depth,
continuous banks at LOD boundaries, no loss of nearby river behavior, ninth-biome
debug-map agreement, and independent review of actual eye-height, downward,
transition and distant rendered views. Preserve the earlier eight-biome memory
failure; it is not a passing marsh baseline. Runtime acceptance is blocked while
the visible editor is unavailable after the separately reported memory failure.
