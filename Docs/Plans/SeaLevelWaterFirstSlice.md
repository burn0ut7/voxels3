# Sea-level water first slice

Date: 2026-09-09. Status: domain, identity, material queries and flat rendering implemented as a
candidate; remaining qualification is open.
User direction: move on to water and stop the extended landform/cave investigation.
[Research](../Research/SeaLevelWaterGeneration.md) owns source comparisons.

## Playable result

Generate blue checker water in surface depressions below sea level, initially Z0.
Keep the seabed, existing landforms and current cave recipe. Water has its own
visible surface and medium identity; it is not solid collision geometry.
Static basin filling is the first slice. Flow, swimming, waves and aquifers are
subsequent work. Do not make unrelated cave parity repair a water prerequisite.

## Frozen domain

Let H be the pre-cave landform height, S the sea level and D the final authoritative
solid density including edits. D<=0 is solid. Otherwise H<z<S is water; remaining
space is air. The top boundary is geometrically at S. Sealed caves below H stay
dry. Solid additions exclude water. Digging below the original seabed does not
automatically flood the hole; this static reservoir limitation must be visible
in documentation and validation. No global flood fill or ocean-sized arrays.

## Implementation sequence

1. Add an immutable surface-water recipe and canonical domain module. Sea level
   is the single authoring control. Include its version and value in world
   identity, save selection and host transfer. Preserve existing saved worlds;
   no implicit migration or reset of the currently edited world.
2. Integrate medium/material queries at the captured world-field boundary.
   Reuse Water4 and the material catalog. Preserve solid density and collision.
   Apply water covering to grass selection consistently in CPU and rendering.
3. Render bounded sea-level tiles from the same water domain. Use existing
   engine-supported mesh/render APIs after checking installed evidence. Clip
   shorelines and solid occlusion; support viewing from below. Bound scheduling,
   publication and retained resources so tiles may arrive over time.
4. Integrate edits, recipe replacement, cancellation, disposal and stale-result
   rejection. Derived water surfaces must never own authoritative water state.
5. Record fixed playable scenarios before runs: dry land; wet column; exact
   sea level; dry underground cave; shoreline and tile boundaries; solid added
   into water; seabed breach; reload and guest agreement. Capture actual water
   appearance and verify unchanged solid collision. Preserve failures.
6. Measure representative ocean rendering and the canonical figure-eight.
   Report water-specific cost separately. Existing landform performance results
   remain historical; do not silently change its pending benchmark workload.

## Ownership and integration risks

Generation owns the pure candidate domain. TerrainField snapshots own final
solid/edit truth and recipe identity. Material assignment consumes both. A water
render adapter owns only bounded derived surfaces and engine resources. Manager
code coordinates lifecycle; it does not absorb the domain or tile builder.

Current material source is shared with the materials task; preserve those edits.
The active v13 world was observed at revision2. Preserve it before identity or
recipe changes. No cave numerical changes, new terrain algorithm, or density
remesher rewrite belongs to this water implementation. Current landform cave
parity and performance limitations remain recorded and unaccepted.

## Identity decision before implementation

SurfaceWater owns water recipe version1 and the default/range of SeaLevel
(default0, finite[-8192,8192], within the existing supported coordinate envelope).
ProceduralTerrainSettings carries SeaLevel as an immutable world input; density
functions ignore it. TerrainField format2 expands the shared identity header by
8 bytes for water version and sea level. Existing format1 saves are preserved
but rejected by the new reader; no automatic migration is implemented. Selector
hashes include this identity, so initial water play creates a separate world.
The current edited v13 world was explicitly saved to pre-water-v13 at revision24
before stopping play; normal teardown also retains its current checkpoint.
The water change does not increment the solid generator version13.

## Flat-surface adapter decision

For this static flat slice, use one bounded two-triangle surface patch centered
on the streaming target's outer-LOD lattice. Its extent covers the terrain cache
plus one anchor step. Pixel clipping evaluates the pre-cave landform height;
existing terrain depth occludes solid additions. This avoids CPU shoreline jobs,
tile seams and per-volume allocations. The medium query still owns volumetric
classification. Geometry is six retained vertices and one draw per visible view;
pixel noise/overdraw remains a measured cost, not a claimed speedup.

Use catalog Water4 colors, a world-anchored16-unit checker with derivative fading,
opaque two-sided rendering, and no collision. Visual waves/transparency and a
submerged volume effect are deferred. One patch is simpler than tiled shoreline
meshes for this slice; reevaluate only if measured clipping cost requires it.
The manager updates placement/immutable attributes; teardown deletes the scene
object. There are no asynchronous water jobs or stale tile publications.

Initial in-world results:387 canonical column samples have finite values and
zero medium-classification violations. Ocean nodes return Water4, exact sea
level returnsAir0, and seabed returnsStone3. The sampled deep column contained
no underground air, so this is not cave-water coverage. The first live shader
load raced compilation and cached a red fallback; normal play restart and shader
reload did not clear it. Clean editor restart with the compiled shader present
shows blue checker water. Evidence and deviations are in WATER-001/v1.
Full shoreline/edit/underwater, multiplayer and performance acceptance remains
open. Existing edited water session is preserved separately from pre-water-v13.

WATER-CAVE-002 confirms one known underground air sample remains dry; the ocean
depth column has no cave-air coverage. WATER-VIEW-002 framing changed during
interactive camera use, and WATER-BOUNDARY-003 encountered a newly authored
world/recipe before its queries. Neither is accepted. Preserve the current
authoring state rather than repeatedly repositioning the camera or restoring
old settings. These observations do not change the already verified blue color.
