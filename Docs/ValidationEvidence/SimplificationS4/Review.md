# S4: identity-only codec — human review

2026-09-08. **Implemented and tested; awaiting human approval.** S3 acceptance
is committed/pushed as a2f357a. S4 is uncommitted; S5 has not started.

## Simplification

Replaced WriteSnapshot/ReadSnapshot with WriteIdentity/ReadIdentity. All current
callers wrote/read empty-page world headers; whole-page sorting, serialization,
decoding and duplicate-page handling in that obsolete container were unused.
Page blocks remain canonical for checkpoint payloads and network transfer.

TerrainFieldIdentity is a readonly value of settings, revision, world ID and local
epoch. Checkpoints retain epoch for stale-save/replacement handling but it is
not serialized. This removes empty snapshot/dictionary construction in regional
fingerprints, manifests, checkpoint saving, reading and replacement. Four source
files:28lines added/54removed,net26removed. No new persistence fallback, version
migration, sampler, test hook or alternate implementation.

The existing96-byte identity block retains exact magic/version/settings/revision/
zero-page-count/world-ID ordering and SHA256. Header page count is now required
to be zero by the identity reader itself. Header/budget/settings/world-ID/revision,
checksum/truncation/trailing-data checks remain. Checkpoint/manifest directory
validators remain; page-block encoding/decoding is unchanged. Removing cancellation
parameters here does not remove an executed empty-header cancellation check:
previous cancellation was only inside the removed nonempty-page loops. Surrounding
storage and fingerprint cancellation boundaries remain.

## Performance pair

SIMPLIFICATION-S4-001/v1, saved world999/167pages,private s4-perf999. Same canonical
figure-eight/settings, cold editor processes. B1 sourcea2f357a/PID33948,
run5072c50ac4e6499880c94ddbcf1b0967. C1 PID39872,
run933cac137857432d900042308c03c5ad; exact4-file hashes in c1-source.json.

| Metric | Before | After |
| --- | ---: | ---: |
| Moving FPS | 810.45 | 845.53 |
| Frame p95 / p99 (ms) | 1.952 / 4.039 | 1.810 / 3.687 |
| Maximum frame (ms) | 134.761 | 46.193 |
| GPU p95 / p99 (ms) | 1.661 / 2.156 | 1.560 / 2.023 |
| Maximum GPU reading (ms) | 9.190 | 12.202 |
| Stationary FPS | 888.43 | 914.86 |
| Stationary frame p95 / p99 (ms) | 1.723 / 2.614 | 1.600 / 2.467 |
| Allocated bytes/frame | 30,445 | 30,163 |
| Peak process bytes | 4,022,575,104 | 4,007,522,304 |
| Peak GPU bytes | 2,879,324,496 | 2,879,324,496 |
| Collision ready p95 / p99 (ms) | 4,111 / 9,592 | 3,657 / 9,251 |
| Publication p95 / p99 (ms) | 86.363 / 110.591 | 84.165 / 120.576 |
| Maximum publication (ms) | 162.350 | 175.512 |
| Maximum synchronous streaming (ms) | 19.067 | 17.986 |
| Maximum placement preparation (ms) | 17.706 | 17.623 |
| Maximum placement lag | 4 | 2 |

MovingFPS +4.33%, allocation/frame -0.93%, processpeak -0.37%. Declared frame/GPU
percentile, memory, allocation/frame and collisionready screens pass. Preserve
worse GPUmaximum (+32.77%) and publicationp99 (+9.03%)/maximum (+8.35%). This
single pair does not establish a speedup or explain the outliers. The benefit
being accepted is deleted unused capability, with compatibility preserved.

No exceptions or engine errors during timed runs. Both drained all visual/
transition/placement work and reached4913collision-ready regions. Peakbacklog127,
zero unsafecommits. Startup and final regular fingerprints match; final
18C827BA6C1B1844/6F1FA10A1262AC94 at center(0,0,-1). This does not establish
identical player contact or exhaustive seam correctness.

## Compatibility checks through production paths

- Cold candidate startup loaded an old checkpoint and all167pages.
- Normal saves before/after retain exactly the same96identity bytes and167
  directory records. Identity SHA256 af9a423539e32413a821f32668f0f1c0afc600afc8f0fb460287d0985b39c947.
  All original page payload hashes remain unchanged. Sequence numbers differ
  normally; compare identity/directory bytes, not whole checkpoint file hashes.
- Regional fingerprint at center0,0,0 radius1024 matched before, after and after
  explicitly reopening s4-before:0D20FBF41FDADFDB8E4F3EB08D0ACFD13D147DEBA3BDEDACD19523F9E1A5260B.
- Normal load rejected all five private malformed checkpoints: nonzero identity
  page count; corrupted inner checksum; truncated identity; extra identity byte;
  changed seed. Outer checksum was recomputed to reach inner validators where
  relevant. Trailing-byte case rejects at caller identity-length guard; direct
  nonseekable-stream trailing behavior is source-reviewed only.
- Failed loads left world999/167pages,epoch1 and active s4-after checkpoint
  unchanged. Expected request.failed error logs are preserved separately from
  clean timed runs. A subsequent valid s4-before load committed999/167pages with
  epoch2 and the same fingerprint, exercising replacement's identity update.

Initial builds exposed one remaining empty-snapshot checkpoint replacement;
updated it to identity including epoch. Final runtime/editor builds pass with
zero warnings/errors and cold live compile succeeds. Failed builds retained.
Baseline editor shutdown again reached Source2Shutdown and an Error window;
log retained. Candidate full editor shutdown is not qualified by this test.

Manifest envelope and directory algorithms are unchanged and now call the same
byte-verified identity codec. No second peer was connected: actual manifest
exchange, late join and reconnect are NOT runtime-qualified by these checks.
Neither malformed page payloads nor every world-ID/version/negative-revision
case was replayed; unchanged validators were inspected. Keep this distinction.

## Human acceptance checks

- Save/reload your edited world and confirm edits and terrain shape remain.
- Dig/build, travel away and back, and walk/jump on edited surfaces. Look for
  stale terrain, cracks, collision disagreement, holds or new streaming stutters.
- Join with a second player, compare visible edits, then disconnect/reconnect.
  Check world convergence and absence of transfer/load errors. This matters
  because manifests use the narrowed identity codec.
- Review the GPU/publication outliers above before accepting the performance
  comparison. No causal speedup is claimed.

Latest999user world restored to its original slot. Candidate stays uncommitted;
pause for human approval before S4 acceptance or S5. All raw results/builds,
checkpoint byte evidence, fingerprints and failure logs are beside this review.
