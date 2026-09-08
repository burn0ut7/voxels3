# S2: concrete page iteration — human review

2026-09-08. **Accepted by the user on 2026-09-08:** "I accept. Lets move onto S3". S1 acceptance is
recorded and pushed as cf8cea1. S2 implementation is committed as d4ce975. Acceptance includes the disclosed
comparison limits below; no additional run or completed human checklist is claimed.

## What changed

TerrainFieldSnapshot retains its dictionary privately and exposes the same
read-only interface to consumers. CopyLatticeCorrections iterates that concrete
dictionary directly, avoiding the interface enumerator on the GPU correction
upload path. The same dictionary, iteration order, pages and sample arithmetic
remain. No new cache, container or alternate path was added. Three small source
hunks in TerrainField.cs; net one added line.

The original S2 GetCorrectionRange/TryCaptureRegion finding was partly superseded
by the storage page index: those methods already use a struct enumerator. They
are unchanged. This candidate only addresses the remaining lattice upload loop.

## Before and after

SIMPLIFICATION-S2-001/v1, one canonical figure-eight per cold editor process,
same revision997/167-page world, authored spawn, settings and warmup. B1 source
cf8cea1, PID46164, run f566239c91d64596a26d88fa84528bdb; C1 PID37176,
run 78fc3e909e86469fa72ded0bec30f34a, exact source hash in c1-source.json.

| Metric | Before | After |
| --- | ---: | ---: |
| Moving average FPS | 791.73 | 826.87 |
| Frame p95 / p99 | 2.048 / 3.992 ms | 1.886 / 3.871 ms |
| Maximum frame | 28.597 ms | 24.699 ms |
| GPU p95 / p99 | 1.734 / 2.245 ms | 1.552 / 2.062 ms |
| Maximum GPU reading | 5.236 ms | 11.669 ms |
| Allocated bytes/frame | 30,670 | 30,359 |
| Total allocated bytes | 2,961,060,040 | 3,061,182,344 |
| Peak process memory | 3,991,465,984 bytes | 4,061,843,456 bytes |
| Peak GPU memory | 2,828,992,848 bytes | 2,879,324,496 bytes |
| Stationary FPS | 885.76 | 930.35 |
| Stationary frame p95 / p99 | 1.798 / 2.684 ms | 1.579 / 2.513 ms |
| Collision ready p95 / p99 | 4,004 / 9,493 ms | 3,835 / 9,371 ms |
| Schedule-to-renderable p95 / p99 | 86.834 / 107.527 ms | 83.170 / 108.883 ms |
| Maximum schedule-to-renderable | 175.296 ms | 188.128 ms |
| Maximum placement level lag | 4 | 4 |
| Maximum synchronous streaming | 11.729 ms | 9.043 ms |
| Total synchronous streaming | 1,986.767 ms | 2,159.283 ms |
| Maximum placement preparation | 16.730 ms | 7.965 ms |
| Maximum GC pause | 12.862 ms | 13.398 ms |

FPS increased4.44%, allocation/frame fell1.01%, process/GPU peaks rose1.76%/1.78%.
More frames were rendered (96,547 versus100,832), and total allocated bytes rose
3.38%; do not call this a demonstrated reduction in total allocation. This is
one pair, not a repeatability result or proof that S2 caused the FPS change.
No new allocation-stack recording was made; removal of the loop's interface
iteration is verified by source, not a measured per-call byte count.

Percentile/allocation-per-frame/memory screens are within declared allowances.
The GPU maximum more than doubled, maximum publication latency rose7.32%, and
total synchronous streaming rose8.68%. These remain disclosed, unattributed
observations. Do not call overall performance fully qualified.

## Correctness and limits

Both runtime/editor builds: zero warnings/errors. Cold candidate live compile
succeeded. Both timed runs: zero exceptions/native failures; after full drain,
all4,913collision regions ready, no visual/transition/placement work pending,
player grounded and released. All167saved page payloads retain their hashes;
world revision997 and page count unchanged. Normal shutdown advances checkpoint
numbers. Existing editor prefab-destruction shutdown error occurred before B1
and after B1; logs preserved. Candidate editor shutdown has not been tested.

Startup regular fingerprints match: CFC3EADBA35BABC5 / C84BDA6FD612E0EB.
Route transition fingerprints match:18B28FD82D91047C / D45A74DD17279E1C;
zero transition mismatches and unsafe commits. Peak gameplay backlog127both.

Final regular whole-view fingerprints differ. B1's player settled at
(-0.833,-0.458,-31.801), center(-1,-1,-1),1118collision bodies; C1 at
(10.477,18.691,-29.891), center(0,0,-1),1130bodies. These diagnostics cover
different spatial sets and cannot establish final geometry equivalence.
Startup positions differed by less than0.001unit through normal physics settling;
no manual recentering was used. This pair does not meet the final matching-view
comparison gate; it is an incomplete comparison, not proof of a geometry defect.
A1000x562player-view screenshot showed rendered edited terrain and the player;
it does not qualify all seams, live edits, storage or multiplayer behavior.

## Human checks before approval

- Dig and build across page faces, edges and corners on both sides of world zero.
  Look for missing edits, cracks, stale patches, or delayed updates.
- Walk close to edited terrain, travel far away and return. Check LOD seams and
  that edits retain their shape as detail changes.
- Stand, walk and jump on edited surfaces. Check for sinking, unexpected holds,
  hovering, or collision disagreeing with the visible surface.
- Save/reload edited terrain. With another player available, verify both players
  see the same changes and retain them after reconnecting.
- Watch for new stutters while streaming. The GPU maximum and final-placement
  comparison above remain unresolved by this single pair.

Paused here as requested. Human approval is required before S2 acceptance,
commit/push or S3. No claim is made that the human checks have been performed.
Raw results and comparison.json are beside this document; the scenario and
chronological decision are in ../../ValidationResults.md.
