# S7: sweep iteration without directory copies — human review

2026-09-08. Accepted by the user after the measured B1/C1/C2 review. S6 accepted and pushed
as f23beef plus acceptance92a7051. S7 acceptance is recorded below; historical checkpoints remain preserved.

## Implemented candidate

SweepTerrainStorage now retains a concrete Dictionary enumerator over the existing
immutable snapshot directory, replacing a copied KeyValuePair array and separate
integer index. Snapshot identity changes still restart from the beginning; the
same dictionary order is used. End-of-directory wraps by obtaining a fresh struct
enumerator. Empty worlds and teardown clear the cursor and snapshot reference.
GetPageEnumerator exposes iteration without exposing dictionary mutation or boxing
through an interface. The source marker is renamed _terrainSweepSource so the new
cursor representation is initialized separately from the old snapshot marker on
hotload; migration behavior itself was not runtime-qualified here.

Limits remain min(8,pageCount) visits/update,1ms soft elapsed budget, cold-page skip,
current epoch/page identity validation,5s grace, saved-version protection, pending
read protection and independent reader/sample/file ownership. No storage format,
network, terrain input, capacity or eviction-policy changes. Runtime source change
is limited to TerrainField.cs,VoxelManager.Storage.cs,VoxelManager.cs; manifests
record exact files/hashes. This removes O(pageCount) copying/allocation per observed
snapshot replacement and one state field. It adds a few source lines for enumerator
wrap handling; do not describe it as a net line-count reduction.

The former array duplicated directory entries, not dense sample arrays. Total
managed allocation changes below include every system, frame counts and GC;
they are not an isolated measurement of directory-copy allocation. No profiler
scope was added and no isolated sweep allocation capture was available.

## Canonical route: SIMPLIFICATION-S7-001/v1

Same1404/235-page fixture, cold B1 PID10484/source92a7051,
run81662aa1bf464d079057a3f01e977a48; C1 PID84136/source c1-source.json,
run273c6f03517d4121b81c49774991502d. Same scenario settings in the ledger.

| Metric | Before | After |
| --- | ---: | ---: |
| frame.averageFps | 848.5067 | 841.8816 |
| frame.p95Milliseconds | 1.8027 | 1.8281 |
| frame.p99Milliseconds | 3.6522 | 3.7998 |
| frame.maximumMilliseconds | 23.3969 | 22.0846 |
| frame.p95GpuMilliseconds | 1.5583038 | 1.57547 |
| frame.p99GpuMilliseconds | 2.0279884 | 2.0308495 |
| frame.maximumGpuMilliseconds | 9.831905 | 11.202812 |
| runtime.averageManagedBytesAllocatedPerFrame | 30249.377 | 30304.822 |
| runtime.maximumGcPauseMilliseconds | 12.456 | 12.608 |
| runtime.exceptions | 0 | 0 |
| memory.peakProcessBytes | 4018368512 | 4019998720 |
| memory.peakGpuBytes | 2879324496 | 2828992848 |
| stationary.frame.averageFps | 929.1008 | 909.1815 |
| stationary.frame.p95Milliseconds | 1.566 | 1.6292 |
| stationary.frame.p99Milliseconds | 2.3176 | 2.4643 |
| collision.requestToReady.p95 | 3802.593 | 3926.4 |
| collision.requestToReady.p99 | 9403.266 | 9505.155 |
| meshing.scheduleToRenderable.p95Milliseconds | 83.4737 | 81.4834 |
| meshing.scheduleToRenderable.p99Milliseconds | 107.7174 | 100.6459 |
| meshing.scheduleToRenderable.maximumMilliseconds | 165.6511 | 164.656 |
| streaming.maximumSynchronousMilliseconds | 18.0155 | 7.8003 |
| streaming.maximumPlacementPreparationMilliseconds | 16.8378 | 7.6343 |
| streaming.peakGameplayMeshBacklog | 127 | 127 |
| hierarchy.maximumPlacementLevelLag | 2 | 4 |
| hierarchy.placementUnsafeCommits | 0 | 0 |

Moving FPS-0.78%,CPU p99+0.1476ms. Recorded percentile, allocation, memory and
collision-tail screens pass. GPU maximum9.832->11.203ms and placement lag2->4
worsened; retain these unexplained observations. Synchronous/preparation maxima
and publication tails improved, without a causal speedup claim from one pair.
Both4913ready,queues0,exceptions0,unsafe0,mismatches0,backlog127. Startup geometry
matches. Unlike prior comparisons, final centers also match(0,0,-1), with exact
regular topology39A8A70DBE72F9F5 and positions9998F7CFE92EB441. This is bounded
geometry evidence, not exhaustive seam or contact qualification.

## Sustained edits: SIMPLIFICATION-S7-EDIT-001/v1

Existing tool-sweep/candidate-v2 entry point,600scheduled attempts at0.1s,10s
baseline and normal drain, identical saved empty world
 de7b3609-193a-469b-a308-166115394e0f (s7-edit-b1/c1 copies). Both cold processes:
B1 PID36672/runa5a6a6994b49415591f8d94f83bbf668;
C1 PID85688/run6205cda517514f69bbcb5228122054f4.

| Metric | Before | After |
| --- | ---: | ---: |
| committed | 450 | 450 |
| rejected | 0 | 0 |
| pending | 0 | 0 |
| failure | None | None |
| workAndDrain.frame.averageFps | 959.96405 | 941.72186 |
| workAndDrain.frame.p95Milliseconds | 1.1587 | 1.5066 |
| workAndDrain.frame.p99Milliseconds | 2.1961 | 2.6106 |
| workAndDrain.frame.maximumMilliseconds | 13.339 | 14.3425 |
| workAndDrain.frame.p95GpuMilliseconds | 1.0023117 | 1.3422966 |
| workAndDrain.frame.p99GpuMilliseconds | 1.3902187 | 1.7991066 |
| workAndDrain.runtime.averageManagedBytesAllocatedPerFrame | 30148.256 | 30271.363 |
| workAndDrain.runtime.managedBytesAllocated | 2023038480 | 1992703312 |
| workAndDrain.runtime.exceptions | 0 | 0 |
| workAndDrain.memory.peakProcessBytes | 4016963584 | 4072972288 |
| workAndDrain.memory.peakGpuBytes | 2805226536 | 2805106536 |
| peakPageBytes | 3276800 | 3276800 |
| controllerStateChanges | 0 | 0 |

Edit FPS-1.90%; CPU p95+0.3479ms,p99+0.4145ms; GPU p95+0.339985ms,
p99+0.408888ms. These exceed the canonical0.25ms/5% screening allowance when
applied to this edit workload. Allocations/frame increased0.41%,processpeak1.39%;
total allocations fell1.50% with fewer rendered frames, so this is not evidence
of an isolated allocator win. Sweep lifetime maximum1.1381->1.0604ms, both above
the existing1ms soft target; no per-call distribution or per-page coverage.

Both600observations,450commits,0rejections,0pending,0exceptions,0controller-state
changes; final revision450/25pages, all edit/visual/collision work drained.
However, exact final-field acceptance FAILS: B1 fingerprint
829AE8940F7DC9D7530E72FBB78B6B0E5524EDF5C42A86184A6873BB329C31DB,
C1 FD2785E24E674A1C4C18C9B4EBE7406F94EF013395DF1173F794CFDCD0694579.
Changed samples964963 versus964961. Source uses the live player's EyePosition
and terrain trace for each tool attempt (VoxelManager.DeformationBenchmark.cs:
204-206). Recorded player starts differ slightly:
B1(0.00132719474,0.141046852,2.1992414),
C1(0.00130371645,0.14575243,2.19791174), with about0.579units camera displacement
in each run. Thus identical schedules did not establish identical trace inputs.
That is a plausible comparability limitation, not proof explaining every density
or performance difference. Do not relabel the hash mismatch as a pass, tune
inputs, or claim a demonstrated storage corruption. Investigate with a suitably
fixed production-input scenario before feature acceptance.

## Qualification limits and retained work

S7's broader fairness question remains open: restarting every snapshot can delay
later entries under sufficiently frequent edits. This candidate preserves that
behavior. The25-page edit workload is not evidence of every eligible page being
visited under sustained full-directory edits. Continuing an old snapshot sweep
would alter current-version visitation and retained-page lifetimes; adding a new
registry or cache would expand ownership. Neither is implemented without evidence.

No edited pages evicted or read from disk during these two workloads. Prior
STORAGE-HISTORY-001/v1,REOPEN-READY-001/v1,FAILURE-001/v1 and stale-read results
remain historical evidence only; no new claim that this candidate reran or passed
three-trip eviction/reload, dirty failed-save protection, active reader overlap,
full-cap fairness, same-session pressure recovery, hotload migration or multiplayer.
The accepted storage prototype itself retains the documented pressure-recovery
gap. The current tests are insufficient for full S7 research acceptance.

Runtime/editor builds and cold compile succeeded0warnings/errors. All original
and canonical fixture page payloads and tested source hashes unchanged. Normal
user world selector restored. Setup's nonexistent-checkpoint failure and resulting
edit_info exception occurred before any edit benchmark and are preserved in B1
editor log. Both measured edit runs used cold processes after corrected setup.
Existing shutdown Error windows after Source2Shutdown recurred on baseline and
candidate travel processes; logs retained, not counted as clean shutdowns.

## Human review checkpoint

Recommend HOLD approval: the code removes duplicate directory storage, but the
edit-tail slowdown and unmatched field output need qualification before accepting.
No additional tests, rollback, commit or further work is automatic at this gate.

Human checks: normal movement/camera and editing; save an edit, travel far enough
for storage eviction, return and confirm terrain/collision, then reopen and confirm
history remains. Look for stalls, missing edits, bad collision or storage errors.
These manual checks do not replace the unresolved timing/comparability evidence.
S7 stays uncommitted. This is not a claim that all simplification research is closed.

After restoration, live user activity advanced the normal world to revision1464/
242pages (60commits, saved checkpoint110, no pending edit work in the sampled
status). Preserve these newer edits. Payload equality above was checked at the
restoration boundary, not after subsequent user mutations. No human acceptance
or exhaustive feature-check completion is inferred from this activity.

## C2 repeats — 2026-09-08, user requested

Unchanged candidate source, same scenarios/fixtures/settings, separately cold
travelPID22276/run63fb97000c5e4c34a321f7642b98c8e5 and editPID86612/
run8c57311e60754c49b149bfe2956f975d. These later13:29/13:34 runs preserve the
prior11:36–11:54 evidence; no new baseline was run. No source modifications.

### Travel comparison

| Metric | B1 baseline | C1 first | C2 repeat |
| --- | ---: | ---: | ---: |
| frame.averageFps | 848.5067 | 841.8816 | 856.7985 |
| frame.p95Milliseconds | 1.8027 | 1.8281 | 1.7609 |
| frame.p99Milliseconds | 3.6522 | 3.7998 | 3.9848 |
| frame.maximumMilliseconds | 23.3969 | 22.0846 | 132.6772 |
| frame.p95GpuMilliseconds | 1.5583038 | 1.57547 | 1.4195442 |
| frame.p99GpuMilliseconds | 2.0279884 | 2.0308495 | 1.9447803 |
| frame.maximumGpuMilliseconds | 9.831905 | 11.202812 | 3.6945343 |
| runtime.averageManagedBytesAllocatedPerFrame | 30249.377 | 30304.822 | 30183.201 |
| runtime.maximumGcPauseMilliseconds | 12.456 | 12.608 | 12.092 |
| runtime.exceptions | 0 | 0 | 0 |
| memory.peakProcessBytes | 4018368512 | 4019998720 | 4108480512 |
| memory.peakGpuBytes | 2879324496 | 2828992848 | 2879324496 |
| stationary.frame.averageFps | 929.1008 | 909.1815 | 956.1724 |
| stationary.frame.p95Milliseconds | 1.566 | 1.6292 | 1.4879 |
| stationary.frame.p99Milliseconds | 2.3176 | 2.4643 | 2.2137 |
| collision.requestToReady.p95 | 3802.593 | 3926.4 | 3709.447 |
| collision.requestToReady.p99 | 9403.266 | 9505.155 | 9292.038 |
| meshing.scheduleToRenderable.p95Milliseconds | 83.4737 | 81.4834 | 77.1406 |
| meshing.scheduleToRenderable.p99Milliseconds | 107.7174 | 100.6459 | 96.5786 |
| meshing.scheduleToRenderable.maximumMilliseconds | 165.6511 | 164.656 | 159.3728 |
| streaming.maximumSynchronousMilliseconds | 18.0155 | 7.8003 | 9.3707 |
| streaming.maximumPlacementPreparationMilliseconds | 16.8378 | 7.6343 | 7.7801 |
| streaming.peakGameplayMeshBacklog | 127 | 127 | 127 |
| hierarchy.maximumPlacementLevelLag | 2 | 4 | 2 |
| hierarchy.placementUnsafeCommits | 0 | 0 | 0 |

Travel FPS+0.98% against B1, but CPU p99+0.3326ms (+9.11%) FAILS0.25ms/5%
allowance. Maximum frame132.6772ms is a substantial unexplained stall. Recorded
GCmax12.092ms and synchronousmax9.3707ms do not themselves identify its cause;
no causal attribution or noise dismissal. Other percentile/memory/allocation/
collision screens pass. Lag returned to2. Both start and final regular geometry
match B1/C1; finalcenter(0,0,-1),topology39A8A70DBE72F9F5,positions9998F7CFE92EB441.
All4913ready,queues0,exceptions0,unsafe0,transitionmismatches0,backlog127.

### Edit comparison

| Metric | B1 baseline | C1 first | C2 repeat |
| --- | ---: | ---: | ---: |
| committed | 450 | 450 | 450 |
| rejected | 0 | 0 | 0 |
| pending | 0 | 0 | 0 |
| failure | None | None | None |
| workAndDrain.frame.averageFps | 959.96405 | 941.72186 | 955.3119 |
| workAndDrain.frame.p95Milliseconds | 1.1587 | 1.5066 | 1.232 |
| workAndDrain.frame.p99Milliseconds | 2.1961 | 2.6106 | 2.3776 |
| workAndDrain.frame.maximumMilliseconds | 13.339 | 14.3425 | 13.2708 |
| workAndDrain.frame.p95GpuMilliseconds | 1.0023117 | 1.3422966 | 0.9543896 |
| workAndDrain.frame.p99GpuMilliseconds | 1.3902187 | 1.7991066 | 1.3051033 |
| workAndDrain.runtime.averageManagedBytesAllocatedPerFrame | 30148.256 | 30271.363 | 30201.258 |
| workAndDrain.runtime.managedBytesAllocated | 2023038480 | 1992703312 | 2016779696 |
| workAndDrain.runtime.exceptions | 0 | 0 | 0 |
| workAndDrain.memory.peakProcessBytes | 4016963584 | 4072972288 | 3987288064 |
| workAndDrain.memory.peakGpuBytes | 2805226536 | 2805106536 | 2805226536 |
| peakPageBytes | 3276800 | 3276800 | 3276800 |
| controllerStateChanges | 0 | 0 | 0 |

Edit FPS-0.48% against B1; CPU p95+0.0733ms,p99+0.1815ms within the canonical
screening allowance. GPU tails improved; memory/allocation screens pass. C1's
larger editing tail increase did not reproduce. Sweep lifetime max0.7443ms
(B11.1381,C11.0604); no isolated allocation or per-page coverage measurement.
600attempts,450commits,0rejections/pending/failure/exceptions/controller changes;
revision450/25pages; changed samples964956 (B1964963,C1964961).
Fingerprint ABA3BDE31C846D573B8F5B070B93AA5404236284C1B74274F2F754486F74D14C,
different from both B1 and C1. C1/C2 identical code producing different final
fields establishes repeat variability in this live-tool workload; it does not
prove the S7 change cannot cause a separate defect. Exact-equality criterion is
still not met. C2 playerStart(0.00130386546,0.145752251,2.19791174), camera
start(-255.997147,0.145752251,78.1978455),displacement0.5789259,angle0. Existing
live trace-input limitation persists; no workload tuning or replacement tests.

No timed errors in either run. Cold compile passed. Source and original/canonical
fixture payload hashes unchanged at restore; current user selector restored.
Pre-repeat/travel shutdown Error windows after Source2Shutdown preserved in logs.
No fresh storage-lifecycle or human-check coverage. S7 remains uncommitted and
NOT accepted. Recommend a fresh pre-S7 baseline comparison to investigate the
travel stall and tail variability before acceptance. Further work requires the
user's decision at the requested post-test checkpoint. Prior failed results and
broader S7 qualification gaps remain open.

## Human acceptance — 2026-09-08

User: "I accept" after the C2 report. Accept the tested directory-iteration change
with the disclosed132.6772ms travel stall, failed travel p99 screen, varying
live-tool fingerprints and broader storage qualification gaps. This is explicit
product acceptance with limitations, not a claim that those checks passed or that
all suggested human tests were performed. Source hashes still match c1-source.json.
No further benchmark, source modification or baseline checkout was requested.
Earlier HOLD recommendations and uncommitted statements describe their historical
checkpoints. The accepted change is now ready for commit/push.
