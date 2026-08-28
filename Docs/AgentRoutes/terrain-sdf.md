# Terrain and SDF Route

Use this route for voxel storage, density samples, signed-distance functions,
spatial regions, terrain queries, and live edits.

## Canonical Model

- Establish one authoritative terrain representation. Do not let meshing,
  physics, generation, and networking keep independently mutable voxel copies.
- Define the SDF sign convention, iso-surface value, units, coordinate spaces,
  sample placement, and boundary ownership once before implementing consumers.
- Keep the mathematical field continuous across region boundaries. Neighboring
  regions must sample shared positions identically.
- Treat smooth terrain as field data, not as block occupancy with smoothing
  patched on afterward.

## Live Edits

- All edit sources—gameplay, administrative tools, generation corrections, and
  replicated commands—must use one mutation API.
- An edit must report its precise affected bounds. Expand those bounds only by
  the sampling/meshing dependency radius actually required.
- Boundary edits must invalidate every affected neighbor. Avoid full-world or
  unconditional broad remeshing.
- Define ordering, conflict, and idempotency semantics before edits are sent over
  the network or persisted.
- Preserve the procedural base separately from edit data only if the selected
  storage design requires reconstruction; do not maintain two mutable truths.

## Spatial Data Decisions

Do not choose region size, sample precision, compression, sparse/dense storage,
or LOD layout by intuition alone. Base them on representative measurements of:

- memory per active region;
- sample and edit latency;
- boundary overhead;
- mesh build cost;
- network transfer size;
- expected active-world radius and player count.

## Validation

Validate sign/iso conventions, coordinate conversion, region boundaries,
negative coordinates, edit bounds, overlapping edits, and reproducibility.
Include seam cases where one real operation touches multiple regions. Run every
case through the production terrain path in the actual playable world, use its
fixed scenario parameters unchanged, measure the resulting state and downstream
effects, and append the run to `Docs/ValidationResults.md` under the policy in
`Docs/AgentRoutes/performance-and-testing.md`.
