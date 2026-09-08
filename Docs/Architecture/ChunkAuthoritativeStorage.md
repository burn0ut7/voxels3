# Authoritative chunk storage: prototype plan

Date: 2026-09-08. Status: regional storage is implemented with bounded validation evidence. The user explicitly directed that performance be measured and judged afterward, without exact performance pass/fail restrictions. Continue correctness work and present measurements for review; do not hold this prototype on old FPS, latency or collection-time targets. Preserve historical results and unresolved correctness coverage. Regional storage means item2 below, not the deferred storage-scale slice.

This document owns the staged implementation plan and proposed regional lifecycle contract. [Chunk streaming research](../Research/ChunkStreamingStorage.md) owns evidence and alternatives; [terrain deformation](TerrainDeformation.md) owns live tool behavior and existing acceptance status; [voxel foundation](VoxelChunkFoundation.md) owns field/spatial conventions; [validation results](../ValidationResults.md) owns executable scenarios and outcomes.

## Goal and first acceptance boundary

Implement and qualify **Slice 1: persisted regional edit state with real unload/reload**, using the current playable world and existing live deformation. Stop at a reviewable acceptance result for this slice. Subsequent slices require user acceptance of its features and performance; they are not part of the initial implementation goal.

Historical changes mean the current terrain state resulting from accepted edits, including edits made before a client arrived or before a saved world was reopened. This is not an undo feature, audit log or requirement to replay every brush operation.

The observable first-slice experience is: dig/build through the existing tool, see the change live on host and one client, save the committed state, travel far enough for the edited pages to leave every required interest, release their resident sample memory, return and recover the edit from storage, then reopen the saved world and continue editing it. A late-joining/reconnecting client obtains the same history through the normal regional transfer path.

## Current implementation handoff

The prototype now saves the current procedural configuration and absolute edited
sample state in the custom binary regional store. It does not replay brush
history. Autosave runs after30seconds of dirty state; inspector Save now and
Clear / Reset world use the same authoritative state. Normal Play teardown saves
committed changes, and the next Play loads the selected previous save. Reset
persists the empty correction state. Existing multiplayer terrain delivery uses
this store; the user accepted multiplayer behavior and ended further multiplayer
tests. There is no separate terrain-edit persistence path.

Latest bounded evidence: three visible HISTORY trips preserved exact fingerprints
and center collision contacts, with equal settled sample totals on every return.
An older save completed after a newer edit and correctly left that newer revision
unsaved until its own save. Two subsequent reloads completed in3978/4024ms;
editing after reopen persisted correctly. These results qualify those bounded
behaviors, not the entire slice. See the latest ledger entries and their raw
visible2-history evidence.

The latest comparable full-capacity route measured802FPS with frame p99 3.84ms;
the earlier spatial-query regression was corrected from49 to780FPS. Active-load
and active-page-read Stop/reopen checks preserved saved state and released all
reservations. Later sample accounting fell to exactly the current34 pages,
establishing eventual collection in that observation. These are bounded results,
not promises for every workload or a new performance gate.

Mutation memory-admission denial and recovery are now observed in the full-page
save/edit pressure case:62 edits committed once, an older save left newer edits
unsaved, and the deferred final edit recovered with reservations0. Largest sampled
retained+reserved total was511.625MiB. Its final request took32.3seconds to commit;
this responsiveness cost is reported for judgment, not hidden by a pass label.
Read-pump denial is now directly observed at the512MiB cap, but same-session
recovery was not observed through the last103.7second sample; an extra live tool
request entered after64.3seconds. Normal Stop/Play allowed original-world recovery.
The travel/replacement run preserved the correct field, but old reads finished
before commit, so direct stale-read discard remains unexercised. Native live-player
control now works after an installed editor active-scene lookup repair; the tooling
blocker is resolved. Remaining coverage is read-pump recovery and direct stale-read
discard. These bounded results do not establish permanent deadlock. Exact latency
and sample-release timing are measurements for review under the user's updated
decision. Actual eviction, correct recovery, protected dirty state and bounded
memory remain requirements. Preserve engine failures and earlier failed results;
do not repeat accepted multiplayer or save round trips without a relevant reason.

## Current starting evidence

Planning inspected clean source `c72444d0bba187c5af8d9ff96282b9762c8ddf77`. Recheck source and live editor identity at execution start.

- `TerrainField` owns immutable correction pages and the existing ordered commit boundary; current snapshots retain page dictionaries with a 2,048-page cap. Real disk-backed page eviction is not implemented in the inspected path.
- `TerrainFieldCodec` and `voxel_terrain_save/load` already provide versioned whole-snapshot serialization/replacement. `TERRAIN-DEFORMATION-PERSISTENCE-001/v1` established an exact round trip and rejection of a one-byte-truncated save. Its strict timing and full visual proof remain incomplete. Repeating only that result is not the new slice.
- Existing regional manifests and absolute page delivery cover incremental changes and initial transfers. `CONVERGENCE-001/v1` matched host/client density fingerprints after two revisions. `RECONNECT-001/v1` was defined but deferred. Transfer/lifecycle acceptance remains incomplete.
- The latest recorded figure-eight `b94bb84e65d34a72a21a8f67c78120c5` is not an accepted overall baseline: its memory comparison was confounded by repeated editor hotloads. Preserve the unresolved finding and establish comparable evidence before accepting the storage prototype.

Source owners: [field](../../Code/Voxels/TerrainField.cs), [codec](../../Code/Voxels/TerrainFieldCodec.cs), [deformation orchestration](../../Code/Voxels/VoxelManager.Deformation.cs), [replication](../../Code/Voxels/VoxelManager.Replication.cs), [manifest](../../Code/Voxels/TerrainReplicationManifest.cs). Older prose saying transport is unconnected is superseded by the implemented source and later ledger observations, not proof that all multiplayer gates pass.

## Slice 1 contract

### One world, live and historical

Retain generator v5, current sample spacing, sign convention, correction semantics, brush behavior and CPU/GPU field interpretation. Do not introduce a baked field, new terrain recipe, material painting or second sampler in this slice.

The existing terrain field responsibility gains a regional lifecycle: identity, resident state, committed revision, pending save, persisted revision and page dependency pins. The immutable procedural base plus committed corrections still defines one canonical world. Disk records and client replicas represent versions of that world; they are not independently mutable terrain systems.

Existing gameplay edit requests still pass through host validation and the one ordered mutation boundary. Before a mutation, obtain all required region state and dependency samples; do not treat a nonresident edited page as a zero correction. Commit once, invalidate derived geometry once through the existing flow, mark affected regions for persistence, and publish their revisions through the existing multiplayer owner.

Loading an already committed region changes residency, not authoritative edit history. Preserve its stored revision. Restore/reopen uses explicit world/session identity so old jobs or packets cannot cross worlds. Disk completion for revision N cannot mark a newer revision N+1 saved. A load or incoming replica installation must not echo back as a newly authored gameplay edit.

### Bounded storage prototype

Choose and document one supported regional backend during the capability gate. Reuse existing versioned page payload encoding where possible. Prefer the smallest verified engine-supported implementation; do not install speculative native dependencies or implement both file and database backends. Record its transaction/checkpoint visibility, error reporting and recovery guarantees before coding its integration.

Support one private host-owned world store and a fixed bounded dataset first. Keep the current maximum world/page limits unless evidence requires a separately documented change. Define resident, dirty, in-flight I/O, decoded and retained-snapshot byte caps before the first runtime run. Do not replace the current page cap with unlimited historical RAM retention.

Live edit commit remains distinguishable from save completion. Slice 1 requires asynchronous persistence of committed regional state, an explicit save/flush completion signal, and successful reopening of acknowledged saved state. A save checkpoint covering a boundary-spanning edit must expose the whole transaction or retain the prior valid checkpoint; do not publish half an edit as a completed save. Failed writes keep dirty state protected and never report save success.

The prototype does not promise that every live edit is immediately power-loss durable. Edits not yet included in a completed save may be lost on forced termination. Record exactly what the chosen backend proves for process interruption and power loss; never infer it from stream disposal. Stronger per-edit durability and gameplay reward transactions are later work if needed. This limited save contract must be visible in status and acceptance, not hidden behind the word "accepted."

### Real unload/reload

Use the union of required gameplay, actor, render/LOD sampling, mutation, transfer and immutable-reader dependencies. Visual departure alone does not permit removal of a correction needed by a coarse mesh, collision job or outgoing snapshot. Regional readers must pin only actual dependencies rather than retaining the entire historical page dictionary.

Once a region has no required references and its relevant state is saved, retire its resident sample payload under a bounded warm-cache policy. Retain recoverable directory metadata. Re-entry consults storage before generation and rebuilds existing render/collision derivatives from the recovered field. A read failure or corruption is an error, not proof that terrain is unedited.

The acceptance evidence must distinguish a mesh disappearing from actual field-page memory release and a disk-backed reload. Keep storage reads/decodes off the engine thread where supported, integrate under an explicit budget, and reject stale completions using world/revision/lifetime checks.

### Minimum multiplayer integration

Qualify a host plus one client. The current manifest/page protocol is the sole source of live updates, late-join history, movement recovery and reconnect state. An out-of-interest peer need not receive an edit immediately, but its later regional load must include it. Page retrieval for transfers uses the same store and pins as local terrain consumers.

Fix any integration defect that makes this first-slice flow stale, unsafe or divergent. Do not defer a missing historical edit or duplicate application as "future sync." Broader sync optimization, arbitrary network-fault qualification, large player counts and prediction can be later slices. No new terrain-edit broadcast, brush replay service or parallel late-join snapshot store is added.

## Ordered execution plan

### Current implementation checkpoint

`TerrainFieldStore` now owns page-file/checkpoint I/O, and the existing save/load commands route through it. Page files reuse the existing absolute page blocks and use content hashes as immutable names. A flat checkpoints directory holds versioned `.vxi` indexes and `.vxc` completion records; it retains two completed checkpoints after save and preserves the latest before beginning another candidate. The older `.vxt` save orchestration is removed, without adding a legacy compatibility path. The codec's identity serialization is still shared with manifests and fingerprints.

The first production probe created 16 page records, reused them across three saves, cleaned the oldest checkpoint and read back identical regional content. See `BACKEND-CHECKPOINT-001/v1` in the ledger for exact results and limits. This does not establish power-loss durability, failed-write recovery, scale or real eviction. Current readback validates each checkpoint page sequentially and releases its decoded samples, then commits the metadata directory through the existing mutation boundary. Replacement comparison reads exact saved versions on a worker; regional consumers subsequently reload required samples through the bounded read pump. `TerrainField` now owns checkpoint metadata; restore preserves stored world/page revisions, and subsequent live edits advance that history once. A separate local epoch identifies job/cache lifetimes and rotates network transfer identity on restore, including same-world restore; it is absent from stored history. Per-page changed sample bounds keep restore actor checks out of unedited gaps. `STORAGE-REOPEN-PROBE-001/v1` matched both region fingerprints and a contact ray after another edit/save/Play restart, with zero authored edits attributed to restore. This is not a cold-process, multiplayer or full visual qualification. Epoch reset still broadly rebuilds render geometry. Published collision outside the replacement sample changes now retains its body and advances local metadata; pending old-epoch work is cancelled. The bounded REOPEN-READY regression reduced first-load collision rebuilds from4,913 to16 and repeated identical-load rebuilds to0, with full readiness observed below4seconds. Full HISTORY, stale-work and performance qualification remain outstanding. `CaptureRegion` narrows sample-reader dependencies. The canonical snapshot retains the bounded page metadata directory, while saved sample arrays can now be released independently.

I/O uses one worker-side gate to serialize backend reads/writes/cleanup. Saved page records now act as exact file-version handles: a weak registry lets cleanup preserve files still referenced by canonical metadata or pending work without retaining their sample arrays. Handle creation and cleanup run under the same worker I/O gate. Completed saves attach handles only to their exact source page versions; later edits retain their unsaved status. The store also bounds candidate file count before writing. The bounded ownership probe preserved all saved page records across two saves and exposed resident versus unreclaimed sample bytes. The bounded FAILURE-001 case verified blocked-save recovery, ignored incomplete candidates and corruption rejection without live-state replacement. The latest visible HISTORY run established save completion for revision3 while liveRevision4 correctly remained unsaved. Capacity-edge qualification remains open. Consumer deferral and asynchronous regional reads are integrated and eviction is enabled. Earlier HISTORY attempts failed fixed-window retention or lacked reopen timing. The latest visible three-route run preserved exact content/contact with equal settled sample totals, and subsequent reloads completed in3978/4024ms. Prior-Play retention and performance comparison remain unresolved. Capacity admission/retry remain unqualified. The resumed STORAGE-MULTIPLAYER sequence matched host/client saved-history, live-update and reconnect fingerprints through revision3 with acknowledgements inside30seconds. Remote tool input and targeted multiplayer visuals were not fully instrumented; the user subsequently accepted multiplayer behavior and instructed that those tests stop. A client shutdown error is preserved separately.

### Residency implementation boundary

Keep one page-version object for canonical metadata (revision, block ranges and block revisions) while allowing its resident sample reference to be released. A regional reader captures a separate immutable reference to the same sample array; it never copies the samples or mutates the page. Releasing the canonical resident reference cannot invalidate an already acquired reader. Replaced saved versions now release that reference at commit even when metadata views outlive the replacement. A late save completion also releases its obsolete source versions after attaching exact stored handles; unsaved versions are protected. The bounded OBSOLETE-PAGE replacement regression retained exact hashes and readiness, but does not establish the memory deadline or late-save overlap. Global directory snapshots must not themselves pin every historical sample version after eviction. Nonresident sample access fails explicitly until the existing consumer obtains a loaded regional view.

Track dense correction arrays at the single allocation boundary shared by brush creation/copy and codec decode. The prototype's 512 MiB combined sample limit includes old reader versions and decoded inputs, not just current pages. Weak allocation records observe arrays without keeping them alive; their reported bytes are conservatively unreclaimed until garbage collection confirms release. This is managed sample accounting, not a claim about process working set or GPU memory. All brush/copy/codec dense allocations pass through the same allocation cap. Brush jobs now reserve the bounding page count before dequeue, and regional disk batches reserve up to eight decoded pages before dequeue. Each consumed reservation becomes an allocated array under the same lock; other allocations include outstanding reservations in their cap check. Unused capacity is released by worker disposal, including cancellation. Capacity-deferred brush/read queues retry at most ten times per second and expose status. Unreserved restore/diagnostic/network decodes still reject capacity exhaustion explicitly; full admission coverage and capacity-edge qualification remain incomplete. No forced collection or alternate sampler is introduced.

The integrated paging path now enables saved-page eviction. Mesh submissions, collision jobs and brush preparation acquire regional readers and defer when saved samples are missing. One worker reads batches of up to eight pages; the engine thread integrates at most eight results under a 1 ms soft budget. The sweep checks up to eight entries under the same soft budget and protects render/LOD, player/actor collision and mutation interests. Pin acquisition refreshes the five-second age. Reload preserves block-level metadata and authoritative revisions. Failed reads remain explicit and are not sampled as zero; capacity admission/reservation and retry recovery remain incomplete. The HISTORY workload has bounded correctness evidence; it is not an accepted overall performance result. Multiplayer testing is concluded under user acceptance.

### Capability decision for Slice 1

Use `FileSystem.Data` with immutable, checksummed absolute page records and a small versioned checkpoint directory. Reuse `TerrainFieldCodec.EncodePageBlock/DecodePageBlock`; do not add a second density encoding. The existing compiled save path already uses `CreateDirectory`, `OpenWrite(path, FileMode.CreateNew)`, `OpenRead` and stream disposal. Installed engine 26.09.01c evidence and the staging API schema also list directory enumeration and file deletion. Game-code compilation and real I/O still have to qualify each newly used operation. No atomic rename, native database binding or durable device flush is assumed.

Write changed page versions to new filenames, close them, then write a new checksummed checkpoint index containing world identity, generator settings, authoritative revision and the complete coordinate-to-page-version directory. Publish a separate small completion record last. Never overwrite the previous completed checkpoint in place. An interrupted unpublished candidate cannot replace it. A completed checkpoint whose referenced data fails validation produces an explicit load failure; do not silently turn missing/corrupt pages into procedural terrain. Keep the previous completed checkpoint until the next is complete and cleanup is safe. Cleanup must respect current reads and checkpoint references. This establishes application-level checkpoint completeness, subject to successful real I/O tests; it does not establish power-loss durability.

The field remains the canonical directory of committed versions. A directory entry can be known and nonresident. Metadata-only range/revision queries must remain available; sample readers require an acquired resident regional view. Before scheduling meshing, collision, brush preparation or transfer encoding, acquire the exact intersecting page dependencies, including each consumer's existing interpolation/normal halo. Such a view retains immutable sample payloads only for its region. Global directory snapshots must not indirectly retain all sample arrays. Missing saved dependencies defer the consumer and request a bounded read; they are never sampled as zero. Loading/pinning and eviction preserve world/page revisions and do not emit gameplay commits.

Save inputs identify immutable committed versions. Completion advances the persisted revision only for the exact saved versions, even if further edits committed meanwhile. Dirty or actively read versions cannot be evicted. Lifecycle cancellation/world identity reject late I/O completions. Main-thread integration publishes only complete decoded pages under a time budget; I/O and decoding run in the supported worker path. These are implementation obligations, not claims about current code.

Initial hard limits retain 2,048 known pages (256 MiB of current dense sample data) and 216 pages per mutation. Cap combined live, retained-reader and pending decoded sample payloads at 512 MiB, dirty current samples at 256 MiB, and one save plus one read worker. Bound read batches to eight pages (1 MiB decoded) and encoded I/O buffers to 4 MiB per worker. Admit work only when its worst-case reservation fits; if required interest cannot fit, hold/defer it visibly rather than evicting required data. Bound each main-thread I/O integration update to eight pages and a 1 ms soft time budget (an indivisible page may exceed it and must be measured). Saved pages without dependencies expire after five seconds; no extra warm-page count is promised. Limit store disk use to 1 GiB and retain at most two completed checkpoint directories plus one pending candidate. Failure to clean up or reserve space blocks a subsequent save visibly; it never deletes the last valid checkpoint. These are prototype capacity choices, not measured scalability claims.

Alternatives rejected for this slice: continuing full-world decoded saves would not solve residency; a new database binding is not yet proven available; an edit-operation journal would introduce replay semantics the current state codec does not need. Immutable regional records cost directory/file overhead and checkpoint cleanup work, which the fixed bounded prototype must measure before scaling.

### Integration boundaries verified before implementation

The following source paths currently retain broad field snapshots. Each must acquire only its required regional samples before real eviction can be enabled; changing persistence alone is insufficient.

| Consumer | Current retention boundary | Required regional dependency |
| --- | --- | --- |
| Logical chunk | `VoxelChunk` constructor stores `Field` | Chunk sample lattice and the existing descriptor sampling halo; broad-phase range queries use directory metadata |
| Regular/transition GPU work | Both `GpuSdfDescriptor.WithField` methods store `Field`; edited publication retains `_editedField` | Each descriptor's existing `SamplingBounds`, including transition-face and normal support; release sample references after dispatch/cancellation |
| Collision | `VoxelCollisionWorld.CreateRegion` and same-revision invalidation retain `Field` | Existing collision `SamplingBounds`, which includes one cell beyond the chunk for support fragments; retain through the actual worker, including cancellation drain |
| Network | `PrepareTerrainTransfer` selects page versions but keeps absolute page objects in `TerrainOutgoing.Pages` | Manifest coverage and changed page versions only; hold their exact immutable versions until transfer completion/cancellation |
| Mutation and diagnostics | Brush preparation and regional fingerprints capture `CurrentField` | Brush affected pages/interpolation support, or fingerprint bounds respectively; avoid whole-directory sample retention |

The existing GPU resident and candidate descriptors already clear `Field` in several publication paths; preserve that behavior. `_editedField` must remain a metadata/version source rather than accidentally pinning the entire historical payload. Directory range queries must preserve current block-level revision/min/max semantics so residency changes do not invalidate unrelated geometry. A regional view cannot silently accept an out-of-view sample request as an absent edit.

`PrepareReplacement` now preserves restored/received authoritative page and world revisions, removes pages absent from replacement, and carries an explicit local epoch for restore/session changes. Host gameplay edits alone increment authored-edit diagnostics. All replacements still use the one ordered commit boundary. The bounded Play-reopen probe verifies the restore/continue-edit portion; network epoch changes remain unqualified. Future residency-only installation must bypass replacement and must not increment history. Real paging remains part of Slice 1, not a second synchronization service.

| Step | Work | Exit gate |
| --- | --- | --- |
| 0. Establish control | Verify current source/editor, read latest ledger decisions, inspect current ownership and supported storage APIs. Select one backend, define its limited durability contract and exact capacity budgets. Freeze first-slice scenarios and metric definitions in the ledger before any runtime run. | Reproducible source/environment, explicit design decision, immutable workload/criteria and no invented engine APIs |
| 1. Baseline | Run a comparable cold-session canonical figure-eight and relevant existing deformation controls. Retain unresolved pre-existing failures rather than absorbing them into an assumed pass. | Usable before-change evidence; performance/correctness blockers identified and resolved or explicitly reported as preventing acceptance |
| 2. Regional storage | Extend the existing field/codec lifecycle with indexed regional persistence, immutable I/O inputs, saved revision tracking, checkpoint/reopen handling and bounded errors. Retire superseded save orchestration as it is replaced. | Shipping entry points read/write the one store; previous valid saved state survives incomplete replacement |
| 3. Live deformation and residency | Wire existing edit commits to regional dirty/save state; load/pin before edits, invalidate through existing consumers, release eligible saved page payloads, reload on demand. | Tool edits remain live; actual historical data survives eviction/reload; no second field or duplicated mutation path |
| 4. Existing multiplayer path | Make its regional manifests/transfers obtain current state from that same store; preserve epochs, page revisions, interest and readiness. | Host/client live edits, late join and reconnect converge after eviction/reload/reopen |
| 5. Qualify Slice 1 | Run correctness checks, existing deformation and unchanged figure-eight measurements through the real playable world. Inspect visible seams and physical support; record source, results, budgets, failures and comparisons. | Required correctness has evidence; performance is reported for user judgment, without automatic numeric rejection |
| 6. Acceptance handoff | Update actual architecture status, summarize features, limitations and measurements, commit/push only qualified task changes and present the result for user acceptance. | Slice 1 is reviewable; no automatic expansion into a subsequent slice |

The goal is not complete merely because the plan exists, compilation passes or a save can be read back. If runtime tools, a comparable baseline or unresolved engine failures prevent qualification, report the specific missing evidence and keep implementation acceptance open. Do not silently broaden into unrelated engine repair or waive a failed criterion.

## Validation contract

Exact executable parameters belong in [ValidationResults](../ValidationResults.md), not a second scenario definition here. Step 0 must freeze world/seed, edit positions/order/count, travel route, sample/page bounds, players, cache state, warmup/duration, engine/hardware, I/O budgets, timing thresholds and failure conditions before the first run. Existing scenarios remain unchanged; a new storage workload gets its own stable ID/version.

Required first-slice cases:

1. **Live-to-history:** existing dig/build input on host and remote player produces committed changes once; save completion includes those changes. Inspect both visible geometry and actual contact support.
2. **Boundary and eviction:** include a negative-coordinate face/edge boundary edit; leave all relevant interests through real player travel, observe resident page release, return, and verify values, revisions, mesh seams and collision from disk-loaded history.
3. **Save during further editing:** newer live changes cannot be overwritten by an older save/load completion; flush the latest state and reopen it. No false saved-revision advancement.
4. **Reopen and continue:** close/reopen the selected world via the production lifecycle, recover acknowledged history, then make a further live edit through the same tool and save path.
5. **Multiplayer history:** client receives existing edits on join, receives live edits, leaves/reconnects after another edit and obtains the latest state without replay or duplicate application. Reuse existing convergence/reconnect scenarios where applicable unchanged; add a storage-specific sequence for new behavior.
6. **Save/load failure:** incomplete or corrupt new saved data is rejected without replacing valid state; failed save does not evict dirty terrain or report success. Use actual production I/O and allowed external failure conditions, not mocks or test-only hooks.

Correctness gates are zero lost acknowledged saved changes, zero half-transaction completed checkpoints, zero duplicate gameplay applications, zero stale overwrites, zero client-authoritative writes, exact canonical field agreement at the acknowledged common region, and no unexplained seam/support or lifecycle errors. Matching fingerprints alone does not prove visual or physical correctness.

Performance is measured for subsequent user judgment, as explicitly directed on
2026-09-08. Capture frame pacing/tails, chunk readiness, live-edit field/visual/
collision latency, save/reload latency, process/GPU memory, allocations, I/O bytes
and network traffic. Compare unchanged workloads and explain observed regressions;
do not automatically reject this prototype at an exact FPS, percentile, allocation
delta, readiness deadline or garbage-collection deadline. Runtime budgets and
memory safety caps remain implemented safeguards, not removed settings. Actual
page release, recoverable history and absence of unbounded retention remain
correctness requirements. Preserve old failed measurements; do not relabel them
as passes. New runs identify the revised assessment policy before execution.

Use the existing playable scene, production tool and accepted project test entry points. Do not add separate test scenes, test components, frameworks, synthetic terrain or test-only mutation paths. Necessary bounded production diagnostics belong with the existing owner and must observe real state.

## Deferred slices: choose after acceptance

- **Slice 2, storage scale and reliability:** broader paging datasets, regional compaction/backup policies, stronger durability guarantees and bounded long-lived metadata, as justified by Slice 1 measurements.
- **Slice 3, generation/transfer cost:** evaluate cached generation and shared deterministic client baselines against supplied state; preserve one field interpretation and one protocol. No baked-format migration is presumed.
- **Slice 4, multiplayer scale and recovery:** more peers, disjoint exploration, bulk fairness, wider packet-fault recovery and refined synchronization if measurements show the need.

These are candidate follow-ups, not promises to implement several systems or an authorization to advance before the user accepts the preceding slice. Select the next smallest complete slice from the observed result.

## Normal saving workflow (2026-09-07 user-requested first slice)

Host-owned edits now require a normal save lifecycle, not only a diagnostic
command. Default to autosaving dirty committed state every30seconds, with a
Save now inspector action and visible save status. Reuse the same checkpoint
worker and completion integration used by voxel_terrain_save; do not introduce
a separate autosave file format, copy of terrain or replication route. Clients
continue receiving committed state through the existing manifest/page protocol;
saving does not author or rebroadcast an edit.

Play now restores the last successfully saved slot on the host during OnLoad,
before normal streaming and player/session startup. A checksummed, bounded
`terrain/last-world.vxl` selection points to the existing checkpoint store; it
contains no terrain. Editor-scene instances neither initialize persistent terrain
nor save on teardown; only the playable host owns the last-world selection.
Missing selection means first use. Corrupt selection,
checkpoint, or incompatible generator settings fail loading rather than silently
starting and overwriting a fresh world. Existing saves without a selection can
be opened explicitly and saved once. Clients continue receiving server state.

Save now and autosave publish current committed state and remember its slot.
Normal scene teardown synchronously saves the final committed state under the
same storage lock before cancelling scene work. This can delay Stop for disk
I/O; queued/uncommitted brush requests and abrupt process termination are not
covered. Older worker saves cannot publish over a newer stored revision.
The small selection write is checksummed but not power-loss atomic; corruption
is an explicit load failure, not a fallback to a blank world.

Clear / Reset world sits in the same World Saving inspector group as Save now
(and is exposed as voxel_terrain_reset). It submits an empty correction field
with the same world/settings and a newer revision through the existing ordered
replacement path, invalidates derived terrain, then immediately saves it to the
same slot. Reset is host-only and requires the existing idle/actor safety checks.
It clears the active world, not every separately named save. Failed reset or save
is logged and never reported as successful. No edit replay or second terrain
representation is introduced. Full lifecycle/performance acceptance remains
subject to the validation ledger.


### Reusing uncollected immutable samples (bounded evidence, acceptance incomplete)

Three fixed trips recovered identical density and actually unloaded all saved
pages, but the weak allocation counter retained4MiB rather than2MiB at later
settling checks. It subsequently returned to2MiB without intervention. The
counter includes arrays not yet collected, not just active reader ownership;
each dense page occupies131072sample bytes. Re-entry previously allocated a
second equal-version array even when the released original still existed.

Each canonical saved page now retains only a weak reference to its released
array. Pinning that same page version may reattach the unchanged existing array;
a collected target still uses the ordinary queued disk read. The weak reference
does not extend array lifetime, change page/world revisions, change the file
format, or bypass corrupt-checkpoint validation on Open/ReadDirectory. This is
not a separate keyed cache or a second authoritative field: the canonical page
version owns the reference, and mutations create distinct page versions.
ReusedSamplePages exposes reuse separately from LoadedPages (actual disk reads).
Budget accounting still counts every uncollected allocation and reservations;
reuse makes no new dense allocation. No strong pool, forced GC, or relaxed
memory criterion is introduced. Explicit reader disposal across all asynchronous
consumers would be a broader ownership change and is not adopted without need.
The same HISTORY scenario must still prove actual disk recovery after collection,
correct hashes, capacity behavior, and bounded retirement; this change is not
accepted merely because it can reduce duplicate allocation.

### Teardown reference cleanup (2026-09-08)

After cancellation, final save and downstream disposal, the manager releases
saved canonical payloads and clears its field, task/result, sweep and warm-result
references. Captured readers owned by finishing workers retain their immutable
arrays independently. This removes manager-owned retention after the component
is destroyed; it does not force garbage collection or establish that the engine
has released all references. Compilation passed. The single post-change route
preceded the first corrected teardown, so it does not validate cross-Play
retirement. Repeated lifecycle validation remains incomplete.

### Descriptor allocation correction (2026-09-08, unqualified)

The user's full-distance edited-world route exposed materially worse frame
times, allocation volume and post-route stationary FPS. Descriptor binding now
skips regional snapshot creation for dependency revision0, reuses matching bound
views, and checks stale epochs/revisions without constructing temporary views.
The existing nonzero-edit pinning and procedural-only GPU paths remain the
canonical paths. This removes identifiable unnecessary allocations; it is not
yet a measured fix for the whole regression. The allocator still only trims
empty trailing arenas; persistent arena fragmentation is a separate lead, not
a proven leak or permission for an allocator redesign.

Latest targeted full-distance observation after the descriptor correction:
736.1FPS and4.19GB allocations versus the earlier manual run's411.2FPS and
19.60GB. These are differing source/start/revision conditions, so the improvement
is evidence consistent with the fix, not a matched causal attribution. The
current run's arena count returned22 to14 and queues drained, while post-route
stationary FPS was861.1 versus835.3 before. Player return/support differed, so
that stationary comparison remains unqualified. The benchmark currently forces
Z0 without restoring starting height; a correction is awaiting explicit approval
under the project's fixed-workload policy.

## User acceptance and transition â€” 2026-09-08

The user accepted the current performance as reasonable, though below the desired
level, and explicitly requested moving to the next main slice. Stop repeating
Slice1 performance acceptance runs. The benchmark return-position patch remains
proposed and unapplied; performance acceptance does not authorize that separate
benchmark change. Cross-Play cleanup, capacity-edge/cancellation coverage, and
exact release timing retain their recorded limits. No claim of complete
original-gate coverage is made. Existing regression evidence remains available
for future work. The subsequent clarification below selects Regional storage
from the ordered plan; generated untouched terrain remains deferred.

## Next requested slice: Regional storage (ordered plan item2)

The user clarified that the accepted work is the baseline and requested Regional
storage next, using the ordered implementation-plan numbering. Use that naming;
do not substitute the separate future-slice list or begin generated-terrain
caching. Existing page/checkpoint groundwork remains the single implementation.

First bounded increment: remove duplicate reads of unchanged persisted pages
during saves. A saved page already has an immutable coordinate/revision/hash
record. Verify its bytes once through the existing store, reuse that record, and
write a block only when absent from the destination. Newly edited pages still
encode their current samples. Keep current format, quotas, checksums, one I/O
gate, ordered commits and completion-record-last publication. Corrupt existing
records must still fail explicitly, and an older save must not mark newer edits
saved. Validate unchanged saves, changed-page saves and existing failure behavior
through the production save path. This is not full generated-chunk persistence.


### Regional storage increment: immutable page reuse

The existing canonical store now verifies each unchanged same-slot page once
per checkpoint, reuses its coordinate/revision/content-hash record and writes
no page payload for that version. A different destination still validates its
existing copy; absent destination pages are copied from the checked source.
Dirty versions encode current samples and write only their new content blocks.
The index and completion record continue to publish one coherent world revision.
This is an optimization of the existing regional backend, not a new save format
or an additional authoritative state. Unchanged saves still verify all referenced
page bytes; this does not make save work proportional only to dirty pages.

The successful checkpoint.io diagnostic separates reused/written page counts
and page payload bytes read/written. It excludes directory/index/selection I/O.
The three bounded production checks passed: unchanged30-page save read312894
bytes and wrote0 page bytes; a copied-slot checksum error prevented publication;
one live edit wrote only8 new pages/13074bytes while reusing30 existing pages.
Original revision99 and its selected save were restored afterward. Exact cases,
source identity and limitations are in the validation ledger. The canonical
figure-eight regression observation is recorded separately; these checks do not
qualify power-loss durability, capacity edges or all earlier lifecycle limits.


Final-source visible relaunch recovered revision99 automatically and repeated
unchanged-page save successfully. One unchanged figure-eight route completed
at796FPS, frame p994.15ms, zero exceptions, with14 arenas before/after and all
queues settled. The user was running another game concurrently and the Steam
session changed, so this is qualified regression evidence, not a causal speedup.
This bounded regional-storage increment is ready for user review. The original
capacity/lifecycle limitations remain recorded; do not automatically advance
to generated-terrain caching or the separate deferred scalability proposals.


### Native shutdown logging and cleanup

A native editor close exposed success-only logging after final save and collision
disposal calling an already destroyed ConsoleWidget. The resulting exception
interrupted component destruction and opened an error dialog. These shutdown
success logs are removed; failed-save/body diagnostics are attempted but cannot
throw through teardown if the editor logging sink is unavailable. Normal runtime
save completion/status remain unchanged. No persistence/disposal ordering changes.
The first targeted rerun exposed the second collision logger; both results remain
in the ledger. With both corrected, native close exited in7.59seconds, checkpoint26
and all30 page checksums were valid, and no manager/console logging exception was
recorded. Automatic reopen and route evidence follow in the validation ledger.


Automatic reopen after the corrected close recovered original99/30pages with
zero authored changes. The unchanged route completed at807FPS/p994.04ms,
zero exceptions,14 arenas and settled queues. This closes the observed shutdown
logging interruption for the tested case. Capacity denial/retry, active-read
cancellation/stale overlap and the strict sample-retirement deadline remain
unqualified; the original full goal is not marked complete.


### Spatial lookup at the supported page cap — implemented

The2048-page capacity fixture exposed directory scans in broad range queries
and regional captures, despite its bulk edits lying outside current view. Replaced
those scans with one immutable spatial index of page coordinates, derived when
canonical snapshot membership changes. Balanced bounds nodes use a flat preorder
array with subtree end offsets: queries skip disjoint subtrees without recursion,
per-query stacks or heap allocations. Keep direct dictionary lookup for tiny
dense query boxes. Page ranges and revisions still come from the canonical page
objects; the index owns only coordinates/bounds and cannot author terrain.

Regional readers share that immutable coordinate index but retain only their
existing regional page dictionary/sample pins. The shared index must hold no
page/sample references, otherwise a regional reader could retain unrelated dirty
history again. Query bounds keep the existing interpolation halo and acquired
reader-range validation. New world snapshots derive a fresh index; captures reuse
it. No spatial constant changes, new field, disk format, generated cache or
network protocol. Compared alternatives: scanning bins still scales linearly for
widely scattered edits; a mutable global cache needs extra invalidation. A small
immutable hierarchy supports the existing bounded2048-page world with explicit
snapshot lifetime and one lookup responsibility.

The unchanged full-capacity figure-eight improved from49.26 to780.04FPS;
p99 frame time fell63.11 to3.99ms and startup classification342.04 to4.77ms.
The saved-field fingerprint matched exactly, and a negative-boundary live edit
retained its fingerprint and collision contact after save/reload. Total route
allocations rose as streaming recovered; per-frame allocations and process/GPU
peaks fell. See STORAGE-SPATIAL-QUERY-001/v1 and STORAGE-SPATIAL-EDIT-001/v1 in
[the validation ledger](../ValidationResults.md) for raw evidence and limitations.
This qualifies the query-performance fix, not full lifecycle acceptance: restore
still invalidates broad render dependencies, and strict readiness deadlines,
512MiB denial/retry and active-read cancellation remain unqualified.

### Identical restore retains published geometry

An exact replacement comparison with zero changed samples now allows the GPU
mesher to retain published geometry that matches the prior field, updating only
its derived epoch/revision metadata. Queued/in-flight work retains the existing
stale-result checks. Prepared empty LOD0 regions are not activated by an unchanged
replacement. A matching world revision alone never authorizes reuse: same-revision
saves with different samples still take the normal invalidation path.

The bounded reload fell from27923 visual rebuild dependencies and14-second
observed readiness to0 dependencies and readiness in the first one-second
sample; geometry digests, field fingerprint and collision contact matched.
The unchanged full-capacity figure-eight measured802FPS/p993.84ms versus the
accepted780FPS/p993.99ms. See STORAGE-IDENTICAL-RESTORE-001/v2 and
STORAGE-IDENTICAL-CHANGED-001/v2 in [the ledger](../ValidationResults.md).
Changed restores still broadly invalidate visuals; the strict general restore,
capacity-edge, cancellation-overlap and sample-retirement gates remain open.

### Bounded active-I/O teardown evidence

Normal Stop was exercised while a large checkpoint load was preparing and while
a live edit had64 regional page reads pending with655360 bytes still reserved.
Reopen retained the prior saved revisions, returned reservations to0, and did
not apply either the cancelled replacement or queued edit. Saved page identities
and fingerprints matched. See STORAGE-ACTIVE-RESTORE-CANCEL-001/v1 and
STORAGE-ACTIVE-PAGE-CANCEL-001/v1 in the ledger. These qualify the observed
teardown/reopen paths; exact per-page cancellation, in-session stale-completion
rejection, memory-limit denial/retry and strict retirement timing remain distinct
unqualified coverage. No new lifetime or state representation was introduced.

The existing terrain status also exposes cumulative per-field stale read completion
and read capacity deferral counts. The first increments at the canonical epoch/page
identity rejection before installation; the second at a failed batch reservation.
They survive in-session restores and reset with a new field. They do not include
queued requests dropped before dispatch or reads abandoned during manager teardown.
These observations support lifecycle diagnosis without changing admission or mutation.
