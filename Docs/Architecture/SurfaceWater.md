# Static surface water

Status: implementation candidate, qualification in progress. No full performance,
multiplayer, edit or visual acceptance is claimed. The user paused the landform
acceptance work to prioritize this slice.

SurfaceWater owns water recipe version1, SeaLevel default0/range[-8192,8192],
and the pure WorldMedium classification. ProceduralTerrainSettings carries the
immutable level. Solid density<=0 wins; otherwise pre-cave height<z<SeaLevel is
Water and all remaining space is Air. Exact sea level is the geometric surface,
not an occupied interior sample. Water does not change density or collision.

ProceduralVoxelMaterials uses the captured field's final density and natural
height to return Water4 for wet air. Submerged natural soil returns dirt rather
than grass. The solid material shader applies the same sea-level soil rule;
its existing edited-density presentation limitations remain documented separately.
InspectTerrainColumn reports medium alongside canonical/base density.

SurfaceWaterRenderer owns one SceneCustomObject and batches six retained vertices
per published surface chunk. VoxelManager.Water derives their bounds from committed
terrain Active membership and resident records, including empty-solid records.
It samples only the sea-level chunk slice of each active LOD. Water shares terrain
snapping, fine/coarse holes, vertical bounds and committed load/unload placement;
there is no independently centered or padded water plane. One draw per visible
view shades a two-sided opaque blue16-unit checker using catalog Water4 colors.
Derivative fading suppresses distant checker aliasing. The pixel shader clips
where natural height>=SeaLevel. Solid render depth occludes terrain additions.
The manager supplies current settings and publication; reset/teardown deletes the
derived object. Water uses the same host/replica presentation-ready decision as
terrain. Geometry rebuilds only for changed published coverage; retained list and
vertex capacity avoids allocations once capacity suffices. Graphics.Draw submits
the batch per visible view. Actual cost and acceptance are recorded under
WATER-CHUNK-RANGE-001/v1; no new water collision or fluid simulation is introduced.

TerrainField format2 extends the shared identity header from80 to88 bytes with
water recipe version and sea level. The solid generator stays13. Save selectors
hash the complete identity; manifests use the same identity codec and byte budget.
Format1 inputs are rejected; density page representation is unchanged. No general
save migration is implemented. During first integration, external play resumption
preceded hotload; the retained world was explicitly saved separately to
water-hotload-v1. The original pre-water-v13 files were copied and their hashes
verified unchanged. See the ledger for this protocol deviation.

Static reservoirs do not flood holes dug below the original seabed. Adding solid
excludes water locally; removing it restores water only in the original domain.
The renderer presents the flat top, not a closed mesh around a breached seabed.
There are no currents, drainage, buckets, swimming, waves, transparency or underwater
fog. Those require subsequent work. A cave below the seabed remains dry.

[The water plan](../Plans/SeaLevelWaterFirstSlice.md) owns remaining work;
[research](../Research/SeaLevelWaterGeneration.md) owns alternatives and sources;
[the ledger](../ValidationResults.md) owns exact scenarios and failures.

## Chunk coverage correction design (2026-09-09)

The oversized quad has been removed. The canonical
field still owns solid/air/water classification; water does not become solid SDF
or acquire collision. Visible surface geometry is derived from committed chunk
membership and publication, just like the solid mesh cache. Empty solid chunks
can still contain water, so water cannot depend on a nonempty solid mesh handle.

Manager inputs: committed level Active sets and bounds, the existing level scale
conversion, mesher resident publication and current field settings. Enumerate only
the XY slice at sea level, selecting the chunk immediately below an exact sea-level
boundary (ceil(sea/chunkWidth)-1). This honors vertical coverage and the existing
fine-LOD holes without independently recomputing anchors. A chunk must be resident
(including a published empty-solid record) before its water surface is admitted.
Rebuild the candidate list on committed placement changes, and on resident changes
only while some active surface chunks are still awaiting first publication.

The material renderer consumes ordered world chunk bounds and settings through a
narrow read-only list. It builds six vertices per admitted chunk, retains capacity,
and batches the surfaces in one draw. The shader's local seabed clipping and solid
terrain depth occlusion remain in place; internal water-cell faces are not drawn.
No arbitrary large plane, independent distance, water streaming queue, per-voxel
object, new mutable water field, or new save/network representation is introduced.
A reset discards this derived presentation. Host/replica presentation readiness
must gate water through the same existing decision that gates terrain.

Expected work: at most one XY slice of each enabled level, bounded by the existing
32768-coordinate configuration admission cap; seven default levels need at most
64+6*256=1600 candidate surface chunks before holes. Ordered comparison avoids
rewriting unchanged geometry; buffers/lists grow only on capacity increases. No
work while placement and required publications are unchanged. Geometry publication
and rendering share a lock to keep vertices, count, bounds and attributes coherent.
Measure actual allocations, frame cost and draw cost before accepting the batch.

Alternatives rejected: merely reducing plane padding keeps separate snapping and
publication; one object per water cell creates unnecessary object/draw overhead;
forcing water into solid density gives it solid collision and loses distinct medium
boundaries. General fluid simulation and painted water are outside this correction.
