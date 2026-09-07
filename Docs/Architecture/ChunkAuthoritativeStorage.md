# Authoritative chunk storage: prototype plan

Date: 2026-09-07. Status: planned; no storage/paging implementation or acceptance is established by this document.

This document owns the staged implementation plan and proposed regional lifecycle contract. [Chunk streaming research](../Research/ChunkStreamingStorage.md) owns evidence and alternatives; [terrain deformation](TerrainDeformation.md) owns live tool behavior and existing acceptance status; [voxel foundation](VoxelChunkFoundation.md) owns field/spatial conventions; [validation results](../ValidationResults.md) owns executable scenarios and outcomes.

## Goal and first acceptance boundary

Implement and qualify **Slice 1: persisted regional edit state with real unload/reload**, using the current playable world and existing live deformation. Stop at a reviewable acceptance result for this slice. Subsequent slices require user acceptance of its features and performance; they are not part of the initial implementation goal.

Historical changes mean the current terrain state resulting from accepted edits, including edits made before a client arrived or before a saved world was reopened. This is not an undo feature, audit log or requirement to replay every brush operation.

The observable first-slice experience is: dig/build through the existing tool, see the change live on host and one client, save the committed state, travel far enough for the edited pages to leave every required interest, release their resident sample memory, return and recover the edit from storage, then reopen the saved world and continue editing it. A late-joining/reconnecting client obtains the same history through the normal regional transfer path.

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

| Step | Work | Exit gate |
| --- | --- | --- |
| 0. Establish control | Verify current source/editor, read latest ledger decisions, inspect current ownership and supported storage APIs. Select one backend, define its limited durability contract and exact capacity budgets. Freeze first-slice scenarios and metric definitions in the ledger before any runtime run. | Reproducible source/environment, explicit design decision, immutable workload/criteria and no invented engine APIs |
| 1. Baseline | Run a comparable cold-session canonical figure-eight and relevant existing deformation controls. Retain unresolved pre-existing failures rather than absorbing them into an assumed pass. | Usable before-change evidence; performance/correctness blockers identified and resolved or explicitly reported as preventing acceptance |
| 2. Regional storage | Extend the existing field/codec lifecycle with indexed regional persistence, immutable I/O inputs, saved revision tracking, checkpoint/reopen handling and bounded errors. Retire superseded save orchestration as it is replaced. | Shipping entry points read/write the one store; previous valid saved state survives incomplete replacement |
| 3. Live deformation and residency | Wire existing edit commits to regional dirty/save state; load/pin before edits, invalidate through existing consumers, release eligible saved page payloads, reload on demand. | Tool edits remain live; actual historical data survives eviction/reload; no second field or duplicated mutation path |
| 4. Existing multiplayer path | Make its regional manifests/transfers obtain current state from that same store; preserve epochs, page revisions, interest and readiness. | Host/client live edits, late join and reconnect converge after eviction/reload/reopen |
| 5. Qualify Slice 1 | Run frozen correctness, existing deformation and unchanged figure-eight checks through the real playable world. Inspect visible seams and physical support; record source, results, budgets, failures and comparison decisions. | All required first-slice gates pass with complete evidence; no material unexplained regression |
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

Performance gates include existing canonical figure-eight criteria and relevant frozen deformation latency gates, plus measured page residency and I/O backlog against the fixed caps. Capture frame pacing/tails, chunk readiness, live-edit field/visual/collision latency, save/reload latency, process/GPU memory, allocations, I/O bytes and network traffic. Prove released pages no longer contribute resident sample bytes after bounded references retire, and repeated visits do not grow retained memory without bound. Fix material unexplained regressions before acceptance, commit or push of runtime changes.

Use the existing playable scene, production tool and accepted project test entry points. Do not add separate test scenes, test components, frameworks, synthetic terrain or test-only mutation paths. Necessary bounded production diagnostics belong with the existing owner and must observe real state.

## Deferred slices: choose after acceptance

- **Slice 2, storage scale and reliability:** broader paging datasets, regional compaction/backup policies, stronger durability guarantees and bounded long-lived metadata, as justified by Slice 1 measurements.
- **Slice 3, generation/transfer cost:** evaluate cached generation and shared deterministic client baselines against supplied state; preserve one field interpretation and one protocol. No baked-format migration is presumed.
- **Slice 4, multiplayer scale and recovery:** more peers, disjoint exploration, bulk fairness, wider packet-fault recovery and refined synchronization if measurements show the need.

These are candidate follow-ups, not promises to implement several systems or an authorization to advance before the user accepts the preceding slice. Select the next smallest complete slice from the observed result.
