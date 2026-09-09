# Cell-aware digging and dirt placement

Status: research and recommended behavior, 2026-09-09. This note does not
implement the tool rework or change the current terrain contract.

## Recommended interaction

- Holding dig progressively removes terrain. When an edited cell has at most
  10% of its full cell volume remaining, complete its removal. This is a starting
  gameplay threshold, not a value established by another game's implementation.
- One placement click fills one targeted empty cell with ordinary dirt. Holding
  repeats against the updated terrain. Preserve smooth boundaries with adjacent
  cells. A broad placement brush is deferred until precise repairs work well.
- Keep intact neighboring cells and their material identities. Placing dirt in a
  hole in stone must retain dirt after remeshing, streaming, saving and joining.
- Do not globally remove naturally thin terrain. Terminal cleanup applies to
  cells affected by an admitted digging operation.
- Once cleanup completes, the remaining physical terrain must be gone. Small
  cosmetic chips could remain later, but a separately stored empty flag must not
  leave an invisible or unplaceable collidable remnant.

The user delegated the placement size decision. Single-cell placement provides
predictable repairs before adding a larger tool footprint. Water remains a
registered material only, as previously requested.

## Primary-source evidence

[Voxel Tools' smooth-terrain documentation](https://voxel-tools.readthedocs.io/en/latest/smooth_terrain/)
explains signed distance fields and their zero surface. A distance sample is not
the fraction of a cell that remains solid. Its
[VoxelTool API](https://voxel-tools.readthedocs.io/en/latest/api/VoxelTool/)
separates adding/removing SDF terrain from material painting and exposes
progressive spherical editing. Adopt the separation of shape and identity;
do not copy its engine integration or interpret its strength as measured volume.

[Space Engineers' published constants](https://keensoftwarehouse.github.io/SpaceEngineersModAPI/api/VRage.Voxels.MyVoxelConstants.html)
define empty content as 0, full content as 255, and an isosurface level of 127.
Its [storage data channels](https://keensoftwarehouse.github.io/SpaceEngineersModAPI/api/VRage.Voxels.MyStorageDataTypeEnum.html)
include content and material. This supports independently representing amount
and identity. The isosurface threshold is not evidence of a 90% mining cleanup
rule, nor does a content byte establish geometric volume in Voxels3.

[Roblox Terrain](https://create.roblox.com/docs/reference/engine/classes/Terrain)
exposes solid occupancy between 0 and 1 separately from solid material. This is
a useful interface comparison, not proof that its representation or rendering
can be transplanted into our SDF implementation.

[Deep Rock Galactic's developer FAQ](https://www.deeprockgalactic.com/faq-test-page)
describes destructible procedural caves and mining. No inspected primary source
establishes a 90% or 95% cell cutoff. Use the game as an interaction reference;
do not attribute the proposed numerical rule or internal data model to it.

## Current repository constraints

`Code/Voxels/VoxelManager.Deformation.cs` currently applies a spherical brush
with radius 128 and strength magnitude 64 at a 0.1-second input interval.
Placement is the negative-strength version of that brush.
`Code/Voxels/TerrainField.cs` stores floating-point corrections to the procedural
field. Negative field values are solid; positive values are air. Samples are
16 world units apart. Neither brush strength nor a sample's sign measures the
remaining volume of its extraction cell, whose eight corners are shared.

`Code/Voxels/TerrainFieldCodec.cs` encodes density corrections, not explicit
placed-material identities. The current material shader reconstructs natural
layers from procedural terrain. Merely changing the placement brush would
therefore fail the requirement that restored terrain remains dirt everywhere.

## Implementation boundary and unresolved decisions

Keep the canonical terrain field as the owner. Cell-content queries must derive
from a captured field revision, rather than introduce independently mutable
occupancy flags. A bounded interior sampling estimate is a candidate; its
resolution and cost need measurements on thin wedges and partially occupied
surface cells. Do not equate a sample count's numerical resolution with its
geometric accuracy.

Terminal removal needs a field operation that actually clears the target cell.
Setting its center or eight corners positive is not by itself a proof that the
continuous procedural field plus interpolated correction is positive throughout
its interior. Resolve this before shipping the cutoff, rather than hiding
triangles or disabling collision independently.

Shape edits and explicit material overrides must publish atomically through the
existing authoritative mutation path. Their revision must govern derived meshes,
collision, GPU material data, fingerprints, persistence and replication. Preserve
host-side targeting, rate limits, actor clearance and stale-result rejection.
The exact override encoding and old-save migration policy remain design work;
historic density-only saves cannot identify which old additions were dirt.

Avoid a material-specific editing implementation for each block type. The edit
contract selects a registered material ID; the material module owns definitions
and fallback rules. Page allocation and transfer budgets must include identity
data: a dense ushort channel costs 64 KiB for a 32-cubed page, or 128 MiB across
2048 pages before copies and GPU representations. Compare sparse storage with
representative edits before choosing an encoding.

## Acceptance work

Before runtime changes are tested, record fixed scenarios and criteria in
`Docs/ValidationResults.md`. Cover gradual removal and terminal cleanup; thin
remnants; initial partial surface cells; repeated targeting; dirt placed into
stone; actor clearance; negative coordinates and shared chunk boundaries;
save/reload and host/client convergence. Measure edit latency, allocations,
memory, network payloads and the canonical figure-eight against a comparable
baseline. This research note has no runtime validation result. Earlier material
shader restart and performance checks remain pending independently.
