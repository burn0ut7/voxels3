# Nearby complete seam cache

Current status, 2026-09-15: the user requested implementation of the tested
water-plus-prediction build and update of main after reviewing its allocation
and drain exceptions. The current integration is adopted on that basis; the
earlier prototype outcomes below remain historical evidence. This does not
claim universal coverage, multiplayer validation or passing every old threshold.

Status: v2 tested and unaccepted in the user-authorized 2026-09-13
before/after experiment. Baseline is prediction-v2; its 82-file manifest matched
source before this experiment. V1 fast results are retained as a failed trial.

VoxelManager's existing prediction interest owns both regular meshes and exact
transition cache membership. Replace the projected LOD0/1 layout-face selection
with all six faces of nearby coarse owners at every enabled adjacent LOD pair.
At each coarse level, find the ancestors of the current player's 27 immediate
LOD0 chunks, expand by one coarse neighbor in every direction, and repeat at
the far forecast position; merge overlaps. Keep these packages when stationary.
Regular owners join the same cache interest. The existing LOD0 forecast corridor,
0.75second horizon and eight-base-chunk distance cap remain.

The strict spatial cap is 128 owners per coarse level, six faces each: at maximum
supported coarse LOD6, at most 4608 faces plus 849 regular forecast/current interests
(81 LOD0 plus 768 coarse). Canonical LOD5 caps 3840 faces. These are maximum key counts,
not worst-case byte guarantees. Measure retained transition geometry payload;
192MiB is the experimental payload acceptance budget, not a hard allocator cap.
Whole arena/process/GPU memory must also be compared with baseline.

Use existing exact descriptors, regional edit revisions, empty classification,
GPU lanes and transition visibility. Separate output shaders remain unchanged.
Only selected active faces draw. No scene objects, alternative mesher or terrain
truth are added. New owners/face masks are not synchronously meshed. Independent
regular/seam service cursors alternate within0.5ms soft CPU budget,48inspections
per frame; preserve progress across moving forecasts so larger face sets do not
starve their later entries. Immediate/edit dependencies retain existing priority.

Retention uses existing prediction guards in topology cleanup; descriptor
identity rejects stale edits, and departing packages release only speculative
interest. Session reset clears membership/cursors. A complete package means its
owner and all six faces are resident for the current field, not merely queued.
Diagnostics report package count/completion, resident faces, retained geometry
bytes, sampled peak payload, local seam readiness checks/misses and misses outside
the current package membership. These counters count checks, not unique jobs.
Existing publication
still checks actual required seams and never shows an incomplete refinement.
This bounded region does not guarantee every possible local refinement lies
within warmed packages during movement; record misses rather than claim universal
coverage. V1 used a rounded 27-owner cube, which omitted a required negative-side
neighbor. V2 derives bounds from actual floor-based LOD0 parent coordinates.

Compare unchanged LOCAL-COVERAGE-001/v2 fast and standard routes. Targets:
near27 first visible<=0.1s, drain<=10s, zero exact seam/mesh/coverage errors,
no unexplained pacing/allocation/memory regression, retained payload<=192MiB,
and all stationary package owners/faces resident after drain. Preserve failures.

Basis: Godot Voxel Tools builds all enabled transition directions alongside its
regular mesh and selects them by shader mask in
[VoxelMesherTransvoxel::build](https://github.com/Zylann/godot_voxel/blob/master/meshers/transvoxel/voxel_mesher_transvoxel.cpp).
Adopt complete face membership and retention for this experiment; retain our
separate GPU output stages and coarse-owned exact face keys. This does not import
Godot's buffer layout or promise equal performance. Full-world face preparation
and mandatory all-face waits at publication are rejected for this bounded slice:
they would expand work or introduce a new visible barrier.

## Measured decision

The [performance catalog](../ValidationEvidence/LodSeamCache/PerformanceCatalog.md)
retains both before routes, failed v1 fast and corrected v2 fast/standard.
V2 fast first visibility improved from 1.3663s to 1.0284s, but standard regressed
from 0.0218s to 0.3408s. Drain rose from 32.707s to 54.988s fast and 20.453s to
41.034s standard. Both corrected 88-mesh audits, exact seam checks and sampled
structural coverage checks passed. Final packages were complete and retained
2.45 MiB of transition geometry. Standard allocations increased about 71%.

The larger cache is not accepted as an improvement. Source remains available as
an uncommitted prototype with an isolated patch in the catalog. These measurements
do not prove that all-face generation itself is ineffective: this experiment
retains the existing separate count/readback/output pipeline and prepares a
bounded neighborhood, not every dependency of every intermediate refinement.
Increasing the neighborhood again is not justified by these results. A further
design would need to reduce generation/requeue work or change how complete
refinement dependencies are prepared, rather than simply request more faces.
Edited worlds, multiplayer, teleports and every inactive face's geometry remain
outside this runtime validation; residency is not a complete geometry audit.

## Completed prediction scheduling

The manager retains bounded sets of current-interest regular/seam IDs whose
exact current-field residency has been verified. Completed slots skip descriptor
construction; service sleeps when all retained entries are proven complete.
Regular presentation changes and transition publication/removal invalidate the
affected proof through mesher callbacks. Field epoch/revision/content changes
and session reset clear proofs; membership changes intersect them with retained
interest. Pending jobs never count as completed. Field rebases invalidate proofs
through the field identity change. These sets are derived scheduling state.

The existing48-slot alternating order,0.5ms soft budget,forecast cadence,look-ahead,
retention and priority remain. Initial proof acquisition adds a resident lookup.
There is no additional generation path or global event bus. Global publication
revision gating was rejected because unrelated completions would clear all proofs.
Evidence and exact before/after patches: [completed-work experiments](../ValidationEvidence/IdleWork/Experiment.md).
