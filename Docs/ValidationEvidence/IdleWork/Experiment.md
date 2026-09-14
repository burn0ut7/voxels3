# Completed-work scheduling experiments

2026-09-14. Three completed standard figure-eight runs show promising FPS and
CPU savings, but neither prototype receives full performance acceptance. All
runtime source was restored byte-for-byte to the entry manifest. This retains
the pre-existing water prototype and removes the new prediction experiment.
Only experiment documentation/evidence is eligible for this task's commit.

| Measurement | Water disabled control | Water prototype | Water + prediction prototype |
| --- | ---: | ---: | ---: |
| Moving FPS | 406.08 | 540.36 | 561.62 |
| Stationary FPS | 569.79 | 554.05 | 597.74 |
| Moving p95, ms | 4.851 | 3.299 | 3.188 |
| Moving p99, ms | 8.097 | 7.295 | 7.403 |
| Maximum frame, ms | 96.745 | 99.825 | 86.530 |
| Post-route drain, s | 13.596 | 10.372 | 10.237 |
| Allocated bytes, decimal GB | 4.621 | 6.071 | 5.978 |
| Allocated bytes/frame | 93,324 | 92,128 | 87,283 |
| Peak process memory, decimal GB | 3.368 | 3.413 | 3.539 |
| Peak GPU memory, decimal GB | 1.957 | 1.905 | 1.907 |
| Maximum placement preparation, ms | 10.257 | 9.118 | 12.678 |
| Stationary manager update, ms/frame, last 200 frames | 0.2212 | 0.0872 | 0.0229 |

Water improved measured moving FPS 33.1%; prediction added 3.9% versus water.
These are single sequential runs in one editor process, not repeated estimates
of causal effect. GPU timing and process-age/cache history also differ. The
stationary manager samples support the intended bookkeeping reduction, but are
short windows, not whole-route CPU totals. Do not add CPU and GPU timings.

The water run fails the unchanged total-allocation limit (+31.4%), although
allocation per frame decreases 1.3%. The higher frame count with similar bytes
per frame is consistent with that increase; it does not establish allocation
ownership or waive the limit. Combined allocation remains about 29.4% above
control. All three runs miss the 10-second drain criterion. Prediction passes
relative FPS/pacing/allocation/memory limits against water in this trial, but
does not resolve the absolute drain failure. No acceptance repeats were run
while those gates remained unresolved. A claim of zero end-user impact is not
established by these tests.

## Fixed workload and provenance

WATER-EVENTS-001/v2 and PREDICTION-IDLE-001/v1 were recorded before timing in
[the ledger](../../ValidationResults.md). Visible basic_example, one sbox-dev
process (61188), engine 26.09.08b, generator 47, seed 1337, physical 1847x959,
LOD0..5, near/cache/gameplay 4/8/8, radius256, 32 cells at base16; existing
recipe, shadows and other rendering settings retained. Saved world
547ae8f0-bf9e-45e0-8f36-27d0e8d95771, held-dig-v1 revision1990,125pages.
Normal settled spawn approximately (0.03236,-0.02326,257.14334), epoch1.
Speed2500, distance50000, one loop, clearance393.7008,10s stationary,240s cap.
Play restarted between runs; all actual queues, collision4913 and prediction
320owners/1920faces completed before warmup. Completed control warmed68.093s;
subsequent variants warmed at least that long. Source manifests remained
unchanged within each timed run. HEAD was b42d930 plus entry worktree changes.

The old pristine water-v1 world was subsequently edited by other work; v2 is
explicitly a different edited-world baseline. Never compare it continuously
with the old 443/555 FPS result or the invalid dual-instance result.

One control setup was aborted because only37.936s of its declared48.86s minimum
warmup had elapsed. The completed control's moving diagnostic arrived at52.384s
instead of planned30s; candidates matched52.385s. Preserve the missing30s
observation. A viewport DPI mismatch was corrected and verified before timing.

## Prototype ownership

[Water patch](water.patch): manager-owned missing-request and dirty-owner sets
replace repeated completed-water scans. Existing worker count, priority tuple,
generation, field identity, publication callback order and rendering remain.
This was already in the entry worktree and remains an unaccepted prototype.

[Prediction patch](prediction.patch): two bounded sets remember current-interest
regular/seam IDs whose exact current-field residency has been verified. Existing
regular presentation callbacks invalidate affected regular proofs; transition
publication/removal gets a narrow callback invalidating its seam proof. Field
epoch/revision/content changes and session reset clear proofs; membership
changes intersect them with retained interest. Pending work is not proof of
completion. Descriptor rebases occur through field changes and invalidate proofs.

The48-slot alternating service order,0.5ms soft budget,forecast cadence,look-ahead,
cache retention,geometry and foreground priorities remain. Proven slots skip
descriptor construction; fully proven sets sleep until invalidated. Initial
proof acquisition adds a resident lookup, a possible moving-work overhead.
Global publication-version gating was rejected because unrelated completions
would invalidate all proofs. Full queue replacement was deferred to preserve
ordering. No global event bus or second terrain representation was added.

This follows the owner-local coalescing approach already researched in
[SurfaceWater](../../Architecture/SurfaceWater.md). There is no new engine API
or a claim that another engine's benchmark transfers to this project.

## Correctness observations

All three completed routes had zero measured exceptions/unsafe commits,
zero settled pending work, all prediction packages complete, and successful
88/88 mesh audits with zero stale, geometry or draw-argument failures.
Moving and final sampled coverage had zero overlap, imbalance, missing or extra
active seams. These sampled checks do not establish continuous correctness at
every frame. Water control/candidate fixed shoreline views matched.

Supplemental WATER-EVENTS-EDIT-001/v2 tested the combined candidate and control
through the production edit command at(-1024,-800,0),radius128,strength-512 then
+512. Each used a verified save copy of revision1990 before any brush. Candidate
used idle-work-edit; the attempted control overwrite was correctly rejected as
a newer revision existed, so control used fresh idle-work-control. No brush ran
against the original after that rejection. Candidate edits followed its route;
control edits followed a fresh settled Play session, so edit latencies are
observations rather than an isolated timing comparison.

| State | Owner LOD0(-3,-2,-1) | Owner LOD0(-2,-2,-1) |
| --- | --- | --- |
| Before / after inverse dig | 513 vertices, 12DC49E363757778 | 423 vertices, F4A67FED02D670D8 |
| Build | 705 vertices, 3AB3A65CBF6C9414 | 393 vertices, 795C03A99310C8B6 |

Control and candidate match these digests, have zero invalid/degenerate/reversed
water geometry, and return the sampled z=-32 medium from solid to water.
Candidate publication was72.4078/71.5621ms; control83.1313/64.052ms.
Candidate settled with zero terrain/water pairing errors and320/320 packages,
1920faces ready after edits. Build and inverse-dig screenshots match between
variants, including the small bank material mark present after inverse dig in
both. This is not a newly introduced difference from the optimization.

The regional fingerprint before/after is
CCDF71ECDF55B1F6557423FDE3F3DEA190848F7DFF67E35873EA7B6C6D1CE894.
Original held-dig-v1 revision1990/125pages and its selector were restored.
No multiplayer, long-session, fast-flight or universal visual acceptance claim.

## Evidence and next decision

Raw result/source/setup/arrival files, moving/final diagnostics, edit logs,
columns and images are in this directory; [comparison.json](comparison.json)
contains the compact measurement table. The archived patches make the tested
code changes reviewable without shipping unaccepted runtime changes.

The next acceptance work is to attribute and reduce per-frame allocation, then
repeat matched controls/candidates and meet the drain target. Rendering remains
material after manager bookkeeping shrinks; these experiments do not establish
a route back to900FPS or justify changing detail, shadow quality or collision.
