# Material-aware cell edits with smooth geometry

Status: intended contract from the user's 2026-09-10 clarification, not an
implemented or validated feature. This changes the edit model; it does not
replace detailed procedural SDF generation with binary block generation.

Immediate tool subset implemented 2026-09-10, validation in progress: build stores
Dirt2 with corrections, and logical<=10%-solid cells are skipped by the dig ray
while retaining residual shape. See the current [material tool contract](../Architecture/VoxelMaterials.md).
This does not establish the general placement/water/fill model described below.

## Current gap

The inspected working tree stores float density corrections in TerrainFieldPage.
TerrainFieldCodec format2 persists those corrections, and replication uses that
same page codec. TerrainEditIntent contains a center, radius and strength, with
no material. ProceduralVoxelMaterials infers identity from the resulting field
and terrain recipe. Thus placing grass or water is not an authoritative,
persistent operation today. GeneratedWaterCells is a derived chunk input for
static water meshing; it does not implement arbitrary player-placed water.

Implementation ownership remains in [terrain state](../Architecture/VoxelChunkFoundation.md),
[materials](../Architecture/VoxelMaterials.md),
[deformation](../Architecture/TerrainDeformation.md) and
[water](../Architecture/SurfaceWater.md). This plan owns the requested change
across those boundaries until implemented.

## State and operations

World state must describe both what occupies a cell and its detailed shape.
Material identity cannot encode the exact surface: grass and stone can have the
same geometry, and a surface cell can be partly occupied. Keep continuous shape
information alongside discrete material identity, under one field snapshot and
one authoritative transaction boundary.

- Generation initializes material membership and detailed solid/liquid shape
  from the existing recipe. Preserve continuous density values and refined
  crossings; do not replace them with full/empty cells or regenerate shape from
  a material ID whenever a chunk loads.
- Gameplay submits material-aware placement/removal intent. The field operation
  converts the selected cell or smooth tool footprint into material and shape
  changes together. The material catalog selects solid, liquid or empty behavior;
  the edit operation supplies its spatial extent and fill/shape semantics.
- Changing the material of existing occupied terrain preserves its density.
  Building fills the requested volume; removing empties the requested volume.
  A grass assignment must not erase a cliff's detail merely to change its type.
- Preserve the smooth visible brush and surface extraction. Logical cell
  ownership does not require cube geometry or snapping the visible tool result
  to whole cells. Unaffected generated samples remain unchanged.
- Explicit placed materials override procedural assignment. Any later rule such
  as covered grass becoming dirt must make an explicit state change; a material
  query must not silently discard the player's assignment.

Use the existing fixed base spatial grid for material ownership, independent of
render LOD. A half-open cell volume and a shared SDF lattice sample are different
entities: material ownership is unique, while shape changes update the shared
samples needed by all adjacent cells. Floor division must agree at negative
coordinates. Coarse render cells aggregate this state rather than overwriting it.

Keep an explicit distinction between no override (inherit generation) and an
explicit Air assignment. Otherwise regeneration would refill excavations or
restore water the player removed. Material IDs remain stable catalog values.

## Water and derived consumers

Water placement records liquid occupancy/fill in the canonical state; it must
not turn water into solid density merely to obtain a mesh. Solid collision and
liquid coverage need distinct shape semantics in the same snapshot. A material
ID alone cannot specify a partial liquid level or its exposed boundary.

Generated rivers retain their existing carved beds, branches and common water
level. Placed liquid must be represented at its actual cell position, so the
current sea-level-only request selection cannot be the general placement path.
Local surface extraction reads occupied cells and neighboring shape/medium data,
emitting exposed material interfaces within the owning resident chunks. The
renderer does not infer rivers, refill empty cells, or create a global surface.
Flow simulation is a separate behavior and is not established by material-aware
placement alone. Solid/liquid replacement and displacement rules must be explicit
in the operation before either channel is changed.

CPU queries, terrain and water extraction, collision, saving and replication
consume the same committed material/shape snapshot. A material-only change
invalidates appearance, not unchanged collision geometry. Occupancy changes also
invalidate collision and neighboring solid/liquid interfaces as required. Region
versions must cover both channels; density-only equality cannot prove an unchanged
world or justify reusing an old material mesh after restore.

## Integration and budgets

Extend the existing ordered host mutation, immutable page snapshots, regional
invalidation and stale-result rejection. Do not add a second mutable material
world or separate material save/replication history. Prepare immutable updates
off-thread; atomically commit identity and shape before derived work is published.
Keep the existing engine-thread boundary for scene and GPU resources.

Persist and replicate explicit material/fill state together with shape changes,
including removals. Version the format and validate IDs and sizes. Existing
density-only saves must remain intact; define an explicit import/migration policy
before switching formats, rather than silently reinterpreting old pages.

Retain regional allocation, eviction and bounded worker admission. A dense ushort
label plane for 32 cubed cells alone costs 65,536 bytes, excluding override masks,
shape/fill values, snapshots and bookkeeping. Sparse or uniform compression may
avoid that cost in untouched regions, but the representation must be chosen from
measurements. No eager whole-world material arrays or per-cell scene objects.

The complete first runtime slice must carry a selected material through the real
tool, atomic field edit, CPU query, terrain/water mesh, disk reload and existing
host transfer. A material field added only to a tool or renderer is incomplete.
Generation retains its current geometric detail throughout the change. The
current procedural material inference remains the generated default, not a
competing source of truth for explicitly edited cells.

## Acceptance requirements

Predeclare exact scenarios in the validation ledger before runtime testing:
unchanged generated geometry; grass/stone reassignment with unchanged shape;
smooth build/remove with persistent identity; placed and removed water; negative
coordinates and shared boundaries; unload/reload and checkpoint restoration;
host/client agreement; and overlapping edits while derivatives are pending.
Verify material-only restores and explicit Air separately from density changes.
Use the unchanged canonical figure-eight for performance comparison, including
memory, allocations, streaming completion and frame tails. Material labels do
not automatically improve sampling resolution or cure distant channel gaps;
those visual requirements still need their own observed evidence.
