# Terrain execution experiments, 16 September 2026

The existing packed, parallax-free material is the control (HEAD `7294e6f`,
shader SHA256 `17D02D4409B16BB0E8D8BD0B87B47DE25F47DA7290ADB087BCE8DE5177EF2A2E`).
Changes are evaluated independently; no draw-distance reduction counts as a
material optimization.

## Scenario identity

The saved scene had changed since the previous investigation: it starts with
visual radius 128 / maximum LOD 4, and the saved terrain revision is now 3448
instead of 2844. The initial v1 assumptions were incorrect. Preserve the user's
terrain edits and use the explicitly recorded `TERRAIN-EXECUTION-001/v2` scenario
in [the validation ledger](../../ValidationResults.md). Its workload uses the
same figure-eight, resolution, recipe and range as before, but establishes a new
baseline on revision 3448. Results cannot be attributed directly against the
older terrain revision.

Startup DPI scaling also changed effective render dimensions. The measured runs
below use verified 2769 × 1529, not merely the requested logical dimensions.
The generic component editor targets the editing scene before its play clone;
the project `set_terrain_visual_radius` operation explicitly targets the live
world. The editing scene's fields were restored and its disk bytes preserved.

## Exploratory loop unrolling

The only source change was `[unroll]` before the three-patch loop; hash
`F8BC365C6002BCAEC57B68D23F2D0766D47591D2B323BF4C5EBB628B48CA25F6`.
At radius 128 / LOD 4, the A/B/A moving results were 397.99 / 393.46 / 393.50 FPS;
GPU averages 1.9268 / 1.9569 / 1.9585 ms. Stationary FPS was
371.05 / 371.34 / 378.18. This is no demonstrated gain; unrolling was reverted.
These runs are invalid for the canonical range, and are not acceptance evidence.
Raw records: [A](baseline.json.gz), [B](unrolled.json.gz),
[repeat A](full-baseline.json.gz). The last filename reflects its attempted
label, not a successful range change.

The subsequent full-range [exploratory control](canonical-baseline.json.gz)
measured 393.79 / 357.04 FPS, but started before the saved-world mismatch was
resolved in the scenario definition. It is retained rather than relabeled.

## Current-world filtering comparison

The v2 [control](v2-baseline.json.gz) completed at 391.31 moving / 356.06 stationary
FPS, GPU 2.0714 / 2.4353 ms. Moving p95/p99/max frame times were
4.5724 / 9.6456 / 90.074 ms; stationary 4.1295 / 4.5607 / 9.8048 ms.
All 4913 collision regions were ready, none pending/failed, and no exceptions.
Allocation was 91.76 KB/frame; process/GPU peaks 6.224 / 2.181 GB.
The process peak includes earlier hot compiles, so adoption requires fresh-start
memory and shader-load validation.

The 8× candidate changes only the sampler's maximum anisotropy from 16 to 8.
Verified SHA256 is
`934DAC151038A517776C16CF19D1D97EB2E59074DF50E337FAD2619186938DAD`.
The raw revision label's `9C2D0083` suffix is a labeling error; it is not the
candidate source hash. Explicit shader compilation succeeded before measurement.

| Variant | Moving FPS | Moving GPU ms | Stationary FPS | Stationary GPU ms |
| --- | ---: | ---: | ---: | ---: |
| v2 control | 391.31 | 2.0714 | 356.06 | 2.4353 |
| [8× filtering](aniso8.json.gz) | 392.95 | 2.0636 | 367.67 | 2.3589 |
| [Patch cutoff .005](patch005.json.gz) | 384.92 | 2.1251 | 366.39 | 2.3675 |

8× filtering improved moving FPS by only 0.42%, insufficient to establish a
useful gain. Its stationary improvement is 3.26% in one run and was not qualified
as repeatable. It was rejected without claiming visual equivalence.

The third candidate restored 16× filtering and changed only the patch blend's
continuous zero-support cutoff from .001 to .005 before renormalization; the
projection cutoff remained .001. Candidate hash:
`440155A67A0E48E27F5813D1D9627914272DF25D1F5097586DA7D1AFC27F6EE9`.
The denominator remains positive because .005 is below the minimum largest
fourth-power barycentric weight, 1/81. The texture pattern changes slightly,
but remains continuous across lattice edges. The inspected near and grazing
views showed no obvious missing detail or seams. Mean absolute RGB differences
were [0.1494, 0.1537, 0.1131] and [0.0875, 0.0860, 0.0676] on a 0–255 scale.
See [near control](baseline-near.png), [near candidate](patch005-near.png),
[grazing control](baseline-grazing.png), [grazing candidate](patch005-grazing.png).
These limited views do not establish all-material or motion equivalence.

Patch cutoff .005 reduced moving FPS by 1.63% and increased GPU time by 2.59%.
Its stationary maximum frame rose 11.0% (10.8851 vs 9.8048 ms), despite better
average stationary FPS. No gain worth adopting was established; it was rejected.
Both candidates completed with zero exceptions, 4913/4913 collision regions
ready, no pending/failures, and settled queues. Process peaks rose to 7.086/7.599
GB after hot compiles versus the control's 6.224 GB; these are not clean-process
memory comparisons. GPU peaks remained about 2.181 GB.

## Decision

Restore the exact accepted shader `17D02D44`; retain 16× filtering, original
patch support, full nearby maps, and the existing 32–64 m distance fade.
No new runtime optimization is accepted from this round. There is no measured
500–600 FPS result. Preserve the raw records and failed comparisons rather than
report a marginal stationary change as a general improvement.

## Profiling coverage

The exploratory full-range end snapshot covers 200 frames, not the whole route.
Its stationary GPU mean is 2.4345 ms; CPU Render scope 2.3964 ms and Update
0.0629 ms. Render can include waits; these are not additive CPU/GPU costs or
proof of a specific graphics pass. Moving visibility averages 860 visible
chunks, with 13.78 API submissions and 7056.86 indirect records per frame.
GPU pass scopes were unavailable, so no pass-level attribution is claimed.

Final restored-source check: fresh visible editor startup and play succeeded;
shader hash matches control, crash marker did not advance, and no terrain
shader/parser/pipeline or managed exception was observed. Existing stock missing
resource warnings remain. The editor's normal shutdown again hit the known
prefab teardown error; only the saved/stopped process was terminated after
Source2Shutdown. This is not a clean-shutdown pass. The scene snapshot was
restored after save changed only generated prefab GUIDs. Final playable view was
inspected with no pending terrain or transition work and collision4913/4913 ready.
The user's saved radius128/LOD4 and free viewport sizing are restored for play.
This load check is not another timed benchmark. No candidate is retained.
