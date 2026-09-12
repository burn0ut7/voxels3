# River terrain resolution

Design investigation, 2026-09-10. Not an implemented adaptive terrain system.
The current width/bend correction remains Generator41/River12. This work must
preserve that recipe, its connectivity and the existing generated-cell water path.

## Observed limitation

The regular terrain lattice and its case tables determine which surface edges
exist. Refining an existing edge crossing cannot introduce a missing channel
inside a coarse cell. GeneratedWaterCells can retain a finer water surface, but
does not refine solid terrain. A fragment discard would conceal this mismatch
without correcting the generated solid geometry and is not an acceptable fix.

Live `inspect_terrain_column` evidence is retained in
[river12-coarse-grid-columns.json](../ValidationEvidence/Rivers/river12-coarse-grid-columns.json).
At XY(-19158.5,-42300.85), the canonical bed is -47.15183 and Z=-16 is Water.
The surrounding256-unit lattice has alternating wet/dry corners at sea level:
(-19200,-42496) bed0.0034628012, (-18944,-42496) bed-38.26004,
(-19200,-42240) bed-46.055573, (-18944,-42240) bed0.86095536.
This is a narrow diagonal crossing with an ambiguous coarse face. It is evidence
of insufficient/ambiguous sampling, not by itself proof of which triangles the
GPU emitted. The sampled points do not prove an all-dry-corner omission; preserve
that distinction. The current fixed-bank screenshot continues to show interruptions.

## Existing ownership constraints

VoxelManager.PrepareLodPlacement constructs rectangular desired caches and active
sets with rectangular holes. Readiness and GpuVoxelMesher rendering already use
explicit set membership. LOD0 warm preparation/retention also uses active sets,
in addition to its separate gameplay/render desired cube. Transition faces are
still generated exclusively from the rectangular hole boundary. StagePlacement
currently detects change from rectangle metadata, not arbitrary leaf membership.
Changing only Active would therefore leave stale transition/readiness decisions.

## Complete implementation boundary

If feature-aware subdivision is selected, generate one nonoverlapping leaf
partition for both terrain and water, rather than an independent river overlay.
Start from the current clipbox coverage; refine only where the generated river
footprint requires smaller solid samples. Retain the32-cell chunk implementation
and existing regular/transition kernels. Plan off the engine thread from immutable
recipe/placement inputs. Cancel obsolete work and reject stale completion.

Refinement must include the sea/bed band on both sides of a shared Z face and
balance neighboring leaves to at most one LOD difference. Derive transition faces
from actual coarse/fine adjacency. Shared positions retain the canonical field
and existing vertex identities. Capture active/cache deltas after refinement;
membership changes must mark a placement changed even when rectangle bounds are
unchanged. Stage regular terrain, transition geometry and generated water together;
commit only after all required data is resident. Integrate refined LOD0 leaves
through the existing warm preparation and retention paths.

Measure required leaf count, preparation memory, completion latency and transition
count in the real workload before adoption. Respect the existing32768-coordinate
preparation budget; exceeding it is a failure requiring redesign, not permission
to drop branches, reduce view distance or silently fall back to missing channels.
No budget increase or acceptance regression is authorized here. Full subdivision
everywhere and shader-only holes are rejected alternatives. An asymptotic face
decider may address ambiguous cases, but cannot recover a channel whose entire
coarse corner set misses it; it is not a complete replacement for adequate data.

## Validation required

Use the real playable world and canonical figure-eight, with the current recipe
and unchanged workload. Verify sampled wet locations against actual rendered
channels, negative-coordinate boundaries, 2:1 transitions, edits and chunk
retirement. Require no loss of branches or added cracks, bounded ownership, and
recorded frame/memory/completion measurements. The existing visual and performance
failures remain unresolved until those checks succeed.
