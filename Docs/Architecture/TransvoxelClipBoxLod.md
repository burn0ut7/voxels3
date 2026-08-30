# Transvoxel Clip-Box LOD

## Decision and Scope

Voxel terrain rendering uses one viewer-centered hierarchy of nested three-
dimensional clip boxes. Every render region contains the configured
`CellsPerAxis` cubed regular cells. Level `L` samples the canonical world-space
SDF at `CellSize * 2^L`, so adjacent active levels always have Transvoxel's
required 2:1 spacing relationship.

This slice owns visual regular and transition geometry only. Collision, live
edits, persistence, replication, multi-origin interest, HZB occlusion,
crossfading, and geomorphing remain outside it.

## Canonical Ownership and Data Flow

`VoxelManager` owns configuration, the single clip-box selection, desired
coverage, revisioning, and atomic refinement commits. Authoritative `VoxelChunk`
objects exist only for the LOD0 full-detail box. A coarse render region is a
disposable descriptor that samples the same deterministic world-space SDF; it
never stores another terrain truth.

`GpuVoxelMesher` remains the sole render mesher. It owns regular and transition
request scheduling, scratch lanes, exact arena allocation, resident derived
geometry, per-record descriptors, visibility, and indirect drawing. Regular
requests use the existing density/classify/scan/count/allocate/emit pipeline
with their request's cell spacing. Transition requests use dedicated small
classification/count and emission shaders and the official MIT-licensed Eric
Lengyel tables. GPU count metadata is the only meshing readback. Geometry is
emitted directly into the shared persistent arenas.

The mutable flow is:

```text
viewer position + validated radii
              |
              v
      VoxelManager clip selection
              |
       revisioned render identities
              |
              v
      GpuVoxelMesher requests
              |
    count readback + exact allocation
              |
              v
  regular/transition arena candidates
              |
              v
  atomic coverage refinement commit
```

## Spatial Contract

- `ViewRadiusChunks` is a LOD0-chunk half-extent in `4..128`.
- `FullDetailRadiusChunks` is an even LOD0-chunk half-extent, at least two and
  no greater than the view radius.
- Boxes are half open. Full-detail radius four covers eight LOD0 regions on
  every axis.
- `CellsPerAxis` is even and lies in `4..64`; odd sizes are rejected.
- `EffectiveMaximumLod` is
  `floor(log2(ViewRadiusChunks / FullDetailRadiusChunks))`.
- Level `L` cell spacing is `CellSize * 2^L` and its region extent is
  `CellsPerAxis * CellSize * 2^L`.
- Normal boxes have a half-extent of `FullDetailRadiusChunks` level regions.
  The outermost half-extent is widened to
  `ceil(ViewRadiusChunks / 2^EffectiveMaximumLod)` when required, and the
  snapped effective bounds are exposed in diagnostics.
- Every child placement is snapped to complete parent-region boundaries. The
  containing viewer chunk center selects the nearest valid aligned box center;
  the selector does not always bias toward the negative grid direction. Level
  `L` therefore remains within `2^L - 0.5` LOD0 chunks of the viewer chunk
  center on each axis while retaining the same `2^(L+1)`-chunk placement
  cadence. Integer world-to-level conversion uses checked mathematical floor
  division, including at negative coordinates.
- A render identity contains `(LOD, level-grid coordinate, mesh kind, face)`.
  All of those fields participate in equality, revisions, stale checks, and
  deterministic diagnostic digests.

At the selected default `(full detail 4, view 16)`, levels 0 through 2 contain
1,536 resident regular regions, 1,408 active regular regions, and 192
logical transition faces. At view radius 128, levels 0 through 5 contain 3,072
resident regular regions, 2,752 active regular regions, and 480
logical transition faces.

## Coverage and Movement

Every level retains its complete resident box. LOD0 is active as a filled cube
and each coarser level is active as a hollow shell. There is no coarse-only
publication mode. Startup prepares the complete hierarchy before its first
publication. Every later placement, including a teleport, keeps the previously
published hierarchy unchanged until the complete target hierarchy and all its
seams are ready, then replaces it in one atomic commit. Entering and leaving
slabs are diffed so unchanged region meshes remain cached.

Placement changes use a geometry-clipmap delta rather than replaying the
resident hierarchy. For each level, the selector decomposes `new - old` and
`old - new` into at most six disjoint half-open slabs. Only entering regular
regions and newly required transition faces are offered to the mesher; the
overlap is neither rescheduled nor retagged. Transition changes are the exact
symmetric difference of the old and new coarse/fine boundaries. Cached face
geometry remains attached to its coarse owner until that owner leaves both the
published and pending placements.

Geometry validity and publication validity have separate revisions. A derived
mesh is valid only for its render identity, cell layout, generator settings,
generator version, and terrain-content revision. Clip placement revision never
participates in that geometry descriptor. It instead guards the prepared
selection and the atomic coverage-bank flip. Both coverage banks are kept in
sync after each successful commit, allowing the next placement to rewrite only
the resident, active, or transition-mask records named by the dirty slabs.
Content changes still invalidate and rebuild derived geometry; placement alone
does not.

A refinement publication is one manager-thread transaction. Per-record
membership, shell, and six-bit boundary-deformation state is prepared in an
inactive descriptor bank while the previous bank remains published. Once every
required regular region and seam is revision-ready, one command-list rebuild
flips the bank. This simultaneously activates
fine regular records, deactivates covered coarse records, enables transition
faces, and selects matching deformation masks without a synchronous bulk GPU
upload. Empty, solid, and air results are ready coverage. Stale placement or
content revisions never publish. Active neighbors may differ by at most one
level.

One coarse block has one reusable regular mesh and up to six cached transition
face meshes. Same-level adjacency disables a transition. Coarse/fine adjacency
activates the cached face and matching deformation bit. Face geometry remains
resident while its owning coarse block remains in the published or pending box.

## Rendering Contract

Far meshes are not physically welded. A level-L region already covers
`2^(3L)` LOD0 chunk volumes, and the renderer submits 256 independently culled
indexed records per arena call. Regular and transition records of every level
share compatible arenas, preserving culling granularity and avoiding per-level
mega-meshes or draw paths.

The vertex format remains a 24-byte position and normal pair. Every indirect
record sets `FirstInstance` to its global arena slot. A per-record descriptor
stores origin, extent, cell spacing, LOD, and both prepared coverage banks; the
terrain and visibility shaders index it through `SV_InstanceID` and the global
arena slot. Affected coarse
regular vertices use boundary width `cellSize / 4` and Lengyel's tangent-plane-
projected secondary displacement. Full-resolution transition-face vertices
remain fixed, and transition vertices are emitted in their final positions.

## Budgets and Diagnostics

Clip selection plus atomic integration has a `0.500 ms` main-thread budget.
Meshing retains three scratch lanes, batches of at most eight requests,
count-only readback, and direct persistent output. Normal rendering performs
zero SDF evaluations and zero geometry readbacks. Terrain remains below 25
arena submissions for the default and radius-128 scenarios.

Telemetry reports per-level desired, resident, active, and inactive regular
counts; regular and transition triangles and bytes; topology and position
digests; logical and active
transition faces; queue latency; visible records; arena submissions; publication
wait frames; coverage mismatches; stale publications; mask/face mismatches; and
adjacency violations. An opt-in bounded overlay draws one color-coded box per
level and never iterates per-region gizmos.

## Serious Alternatives

- Keeping the one-chunk warm shell was rejected because it would create a
  second residency and publication policy beside the clip hierarchy.
- Publishing a coarse-only hierarchy during startup, teleports, or large frame
  skips was rejected because point-sampled levels have different terrain
  profiles. Temporarily substituting one makes hills visibly rise or fall. The
  canonical path always publishes a complete hierarchy atomically.
- Physically combining far chunks or building level mega-meshes was rejected
  because coarse regions already reduce record count exponentially, while
  welding sacrifices independent frustum culling and turns local invalidation
  into large rebuilds without reducing the existing arena submission count.
- Separate per-LOD meshers or draw paths were rejected because the regular
  algorithm, vertex layout, allocator, visibility path, and material are shared
  responsibilities that must evolve together.
- A filtered SDF pyramid was rejected for this slice because it would introduce
  another derived field representation and filtering contract. Coarse levels
  point-sample the same canonical deterministic field at power-of-two lattice
  positions.
- Skirts were rejected because they hide rather than solve cracks and cannot
  provide a watertight volumetric boundary at faces, edges, and corners.
- Crossfading and geomorphing were deferred because they address popping, not
  topological watertightness, and are not required to validate the canonical
  transition path.
- Following the viewer continuously or snapping the outer level twice as often
  was rejected because either choice increases mesh invalidation without
  improving coverage. Nearest parent-aligned placement removes the directional
  bias while preserving the existing slab cardinality and update cadence.
- Re-offering every resident key to reuse checks was rejected because it makes
  placement cost proportional to resident volume and couples a publication
  revision to otherwise valid geometry. Dirty-slab scheduling plus sparse
  coverage-bank updates preserves the same residency and atomicity contracts
  while making movement work proportional to changed box surfaces.

## Source Provenance

The transition lookup data is pinned to Eric Lengyel's MIT-licensed official
Transvoxel repository commit
`51a494f03c5b024cd153b596bcc7152eb3cc93a6`. The imported HLSL table file keeps
the source URL, commit, copyright, and license notice beside the data. The 2:1
topology and boundary displacement follow the official Transvoxel algorithm and
dissertation equations 4.2–4.3; clip-box residency and snapping are adapted from
the nested-region model of geometry clipmaps and volumetric clip boxes.
