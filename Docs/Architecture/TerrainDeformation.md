# Terrain deformation implementation

Status: implementation in progress on `codex/terrain-deformation`. The local edit path has measured continuous and large-edit results; spatial, actor, multiplayer and lifecycle acceptance remain incomplete. No full feature acceptance is claimed yet. The
[research](../Research/TerrainDeformationSecondSlice.md) supplies alternatives
and rationale; this document owns implementation decisions and completion gates.

## Completion contract

The user authorized implementation of all nine research sections on 2026-09-07.
The goal remains incomplete until source, actual gameplay and performance
evidence satisfy each row. Runtime compilation alone cannot close a row.

| Research section | Required outcome | Current state |
| --- | --- | --- |
| 1. Current implementation | Recheck source ownership and establish a pre-change control with recorded effective settings. | Source reviewed; immediate pre-change source control recorded, original collision acceptance still pending. |
| 2. Working implementations | Apply bounded regional mutation and derived work; record departures from the researched design. | Decisions recorded below. |
| 3. Field and brush | One canonical correction store, finite bounded writes, shared sample ownership, progressive left dig/right build and actor/build validation. | Shared field, host queue and local tool implemented; 600 dig/build tool attempts completed in HOLD-001/v2. Remote tool validation pending. |
| 4. Local rebuilds | CPU/GPU composition, edit-aware bounds, regular/transition/support dependencies, revision-safe local publication and bounded admission. | CPU/GPU composition, local block versions and coherent visual groups implemented; HOLD/LARGE numeric gates pass on two-worker source. Full seam/resource proof pending. |
| 5. Collision and remote gameplay | Correct replacement/contact, actor readiness, independent gameplay interests and edits to unloaded/underground regions. | Retained collision avoids edit holds; two bounded workers and player interests implemented. OCCUPIED, resting-body SUPPORT and corrected-plane UNDERFOOT functional checks passed. Distant rigid-body interest resumed the body; exact timing/revisit and broader spatial proof remain incomplete. |
| 6. Multiplayer and persistence | Host validation, bounded state transfer, coherent join/recovery, multiple interests and versioned save/load. | Host validation, player lifecycle and regional page transfer implemented; first late-join/incremental/filtering qualification recorded. Disk round-trip/truncation verified. Two successive regional canonical-density fingerprints agreed across host/client (CONVERGENCE-001/v1). User reports ordinary multiplayer works well. Reconnect, concurrent/remote input stress, failure recovery and full timing/visual proof remain pending; cold join did not reproduce the prior hotloaded-editor initialization error. |
| 7. Dedicated benchmark | Real production entry points; hundreds of continuous edits, large/distant/underground edits, boundaries, multiplayer and failure cases. Frozen criteria and retained run evidence. | Existing manager hosts explicit benchmark; HOLD/LARGE numeric gates passed and failures preserved. SPATIAL numeric, OVERLOAD and controlled SPRINT-LOOK gates also passed on recorded sources. Multiplayer/failure coverage, visible seams and latest-source regression acceptance remain pending. |
| 8. Implementation and optimization | One implementation per responsibility; measured optimization and unchanged figure-eight regression acceptance. | Local invalidation and two-worker collision measured. Matched clean-process figure-eight shows +83.36MiB process peak; full regression acceptance pending. |
| 9. Evidence gaps | Compile/API/resource verification, visible and physics results, measured latency/memory, explicit acceptance decision with remaining limitations. | Pending. |

## Decisions before implementation

- The manager owns one terrain field/store. The generator remains immutable base
  v5. Canonical density is base plus trilinear float correction at the existing
  16-unit sample spacing. Sparse pages own 32-cubed samples under floor-divided
  half-open global integer coordinates; halo copies are disposable inputs.
- Host requests enter one ordered bulk mutation boundary. Compute page changes
  from immutable snapshots and commit page versions atomically. Bound page
  memory, queued intents, snapshots and derivative candidates. Reject invalid or
  oversized work explicitly before mutation, rather than lose accepted edits.
- Keep the compact progressive radial brush and fixed 10 Hz logical tick
  proposed by research. UI preview is separate presentation. Both buttons means
  no edit. Tool rays require current local collision; authorized world-centered
  impacts do not require render residency.
- Continue using the existing GPU regular/transition extractors and CPU
  collision extractor. All classification paths must see the composed field.
  Invalidate exact sample dependencies, including normals and support patches.
- The initial implementation preloads correction values into the existing
  per-lane density buffer, then adds procedural base in the density stage.
  No seventeenth transition storage binding is added. C# upload calls compile;
  cold-start resource lifetime and visible results remain validation gates.
- Local publication groups retain old compatible geometry until replacement
  dependencies finish. Overlapping mutation admission is bounded so continual
  newer requests cannot starve completion. Ordinary streaming and transition
  work must receive service while editing.
- Separate field, visible and collision completion. Collision replacement and
  actor holds are measured, including failures. No performance acceptance is
  inherited from the currently unaccepted collision prototype.
- Replicate host-computed absolute sample values and bounded regional snapshots,
  using the same field store. World epochs, transaction/page versions and
  manifests establish ordering and coherent recovery. Save/load must preserve
  generator identity and correction values; disk persistence is included in
  this goal, rather than claiming RAM retention is persistence.
- No material painting, undo, host migration, new topology algorithm or
  speculative terrain prediction is required. These exclusions do not reduce
  the requested deformation, multiplayer or performance validation scope.

## Validation ownership

Exact scenarios and thresholds belong to [ValidationResults](../ValidationResults.md).
Preserve the user scene's unrelated 256-distance edit. Use explicit effective
runtime settings for the existing 512-distance control and disclose that setup
difference; never call a 256-distance result comparable to the 512 scenario.
Do not save runtime test settings into the user's scene.


## Initial local implementation checkpoint (historical)

This section records the first implementation stage. Later sections and the
completion table above supersede its pending-work and scheduling statements;
those statements do not describe the current runtime.

The canonical implementation is `TerrainField.cs`, with production orchestration
in `VoxelManager.Deformation.cs`. Trusted host sources use
`TryQueueTerrainEdit`; its boolean means queued, not committed. The bounded queue
holds32 intents and one worker builds an immutable page transaction. Admission
waits for prior edited visual/collision dependencies so held input cannot
continually supersede its own output. The first local tool uses Attack1/Attack2
at10Hz, radius128, strength64, reach2048; both buttons cancels input. It currently
runs on the host only. Build spheres plus one sample of interpolation support
are conservatively excluded from player body boxes at admission and commit.
Other dynamic actors and untrusted multiplayer tool validation remain pending.

Regular and transition candidates affected by a commit retain their old render
geometry until the sealed visual dependency group completes; publication and
its latency accounting happen together on the manager update. Streaming removes
retired dependencies from the group. During an edit, transitions get a service
opportunity after100ms even if regular work keeps arriving. These are initial
scheduling decisions to measure, not accepted latency guarantees. Collision
replacement retains existing support until the replacement body succeeds;
collision publication is separately tracked and is not atomically coupled to
visual publication.

Dependency versions currently use conservative page overlap, including each
consumer's sampling halo. This can rebuild more geometry than the exact changed
sample bounds, especially at page corners. Measure/refine this before claiming
minimal invalidation. Prepared empty LOD0 regions are reconsidered after edits;
stale warm completions cannot restore their earlier empty classification.
Finished mesh records retain only dependency identity, not an entire historical
field snapshot. Collision completion also drops its worker snapshot. Queue and
page limits bound admission, but retained candidate memory, copy/upload costs,
queue fairness and snapshot high-water behavior still require measurement.

Changing generator settings after edits or pending mutations is rejected and
restores the applied generator values, avoiding silent edit loss. Versioned
world replacement/load is not implemented yet. `voxel_terrain_edit` and
`voxel_terrain_edit_info` expose the same production world-impact path and its
current counters; the dedicated benchmark is still pending.


## Persistence and transfer format decision

Implement one bounded page codec for disk snapshots and host state transfer.
A snapshot header identifies format, generator version, sample spacing, page
axis, generator parameters, revision and page count. Pages are sorted z/y/x;
each contains absolute float corrections, coordinate and revision. Select dense
floats or sorted sparse ushort-index/float pairs by encoded size, without
quantization. SHA-256 checksummed length-prefixed blocks detect corruption;
network authority still comes from authenticated host delivery, not the checksum.
Readers reject unsupported headers, malformed sizes, nonfinite values, excessive
page counts, duplicate/out-of-range keys, unsorted sparse indices, invalid page
revisions and trailing data before committing anything. Disk writes stream one
page block at a time from an immutable snapshot, keeping encoded scratch bounded
near128KiB. Full state decoding is bounded by the existing256MiB field cap.

Loading requires an explicit host operation with matching generator settings;
it stages the complete validated replacement before the canonical commit. The
same downstream invalidation path must cover removed pages as well as restored
ones. Recovery and network reception must never mutate published arrays. Save
publication, cancellation, file failure and load/rejoin tests remain gates.


The first disk implementation uses explicit host commands `voxel_terrain_save
<slot>` and `voxel_terrain_load <slot>`. A slot is1-32 lowercase letters/digits/
hyphens/underscores under the engine data filesystem's `terrain/` directory.
Saving captures the committed immutable snapshot at invocation; later edits do
not change that file. One save worker streams checksummed blocks. CreateNew
refuses to overwrite an existing slot, preserving previous saves during write
failures. An interrupted new file is rejected by the reader; it is not silently
accepted or substituted for another save. Reusable overwrite slots, autosave and
save-file housekeeping are not implemented.

Loading uses the same ordered mutation task and commit path as brushes. It
requires no pending mutations/save/edited derivatives, validates the whole file,
rebases changed pages to a new local revision and adds zero tombstones for
removed corrections. It rejects generator mismatch, excessive combined page
retention and any dynamic body intersecting the conservative changed-sample
bounds. This last rule is deliberately conservative and can reject a broad
restore even if that body's immediate terrain would be unchanged. No actor is
teleported and no camera setting is changed. Runtime save/load, cancellation,
corruption and restart-retention verification remain pending. Multiplayer state
transfer is not connected yet; a shared codec is not proof of replication.


## Dedicated benchmark implementation checkpoint

`voxel_deformation_benchmark <scenario> <task> <revision>` explicitly starts the
manager's deformation-only runner. Normal tool input cannot start it. The runner
shares the normal tool ray/admission function for tool scenarios and the trusted
world-impact mutation entry point for large/distant/underground/boundary work.
It never changes the camera, player position or streaming origin. A fresh
revision0 field, fully settled visual/collision caches, an idle mutation boundary,
no other benchmark, and the player at spawn are required. Primary figure-eight
and deformation benchmark starts exclude one another; save/load is rejected
while benchmarking.

The initial candidate includes stationary/sweeping tool attempts600times at10Hz,
an overload attempt stream600times at40Hz, and12alternating world edits at2Hz
for each large/distant/underground/boundary scenario. It retains every attempt,
including rejection, late scheduling and unfinished commit/derivative records.
Ten seconds of no-edit baseline precede the workload; after drain it records a
further10seconds. Results append to the engine data filesystem's
`performance/deformation-results-v1.jsonl`. Frame/memory sampling reuses the
existing performance lifecycle rather than another sampler implementation.

These are candidate workloads, not yet accepted executable scenarios. Before a
first run, preflight sites and freeze complete parameters and gates in the
validation ledger. Current completion timestamps measure edited queue drain,
which is a conservative proxy; they do not identify first displayed terrain,
GPU scanout, per-region publication, or remote client latency. The output says
Unreviewed. No-op/nonvisible edits must not count as instant visible success.
Multiplayer, moving-player/revisit, persistence/corrupt-transfer and teardown
cases remain outstanding. An explicit stop or failed workload cancels its uncommitted requests, removes
only its own queued intents, and retains cancellation records. Scene teardown
attempts to append the interrupted result before resource cleanup. This path is
implemented but still needs the required lifetime/failure validation; missing
output must count as a failed run.


## Multiplayer implementation checkpoint

Terrain snapshots now carry persistent WorldId, included in the checksummed
save header. Brushes preserve it and loading adopts the saved identity. A
separate manager-owned session epoch identifies the network generation. This
separates saved-world identity from connection/session lifetime; session epoch
rotation on world replacement is still part of pending transfer integration.

The client tool request endpoint sends only epoch, monotonically increasing
client sequence, aim direction and dig/build intent. The host derives the eye
origin from the player owned by Rpc.Caller, performs the normal terrain tool
raycast, checks collision readiness and uses fixed host brush/reach rules and
build exclusions. Per-caller admission is rate limited; caller state is bounded
to64 connections with disconnected entries pruned. Duplicate/older sequences
never apply again or amplify reply traffic. Host-only admission replies are
filtered to the caller and mean queued, not committed.

The client send path remains gated on coherent replica readiness. Transfer and
interest synchronization are not connected yet, so this endpoint is not a claim
that multiplayer editing works. Next steps are bounded snapshot/patch manifests,
fragment reassembly, page-version checks, catch-up/epoch transitions, player
session ownership and multiple gameplay collision interests, followed by actual
connected-client validation. No meshes or client density values are accepted
as authoritative terrain state.


## Edit responsiveness correction (2026-09-07)

The user reported delayed held-tool updates, movement freezes when editing the
occupied chunk, and stale edits while sprinting/turning. The live pre-change
observation at09:28:58 had one radius128 tool edit with84visual and48collision
dependencies. This is an observation, not a fixed benchmark baseline.

Storage pages now retain64 derived revision stamps for8x8x8 sample blocks
(128world units per axis). A mutation compares old/new samples on the existing
worker and updates only changed blocks. Dependency queries include interpolation
support and take the maximum intersecting stamp. Density range bounds now use conservative intervals for the same intersecting
blocks, with full-page fallback for missing metadata. Missing metadata on old hotloaded/decoded pages falls
back to full-page revision. These stamps are derived cache validity metadata;
absolute density pages remain authoritative and the disk format is unchanged.
Each newly constructed page adds256bytes of stamp payload (array overhead extra).

Interactive tools admit only at an idle preparation boundary with no pending
coherent visual publication; they raycast the current aim then, so input cannot
retain an old target while waiting. Trusted queued world impacts retain their
existing ordered32request bound. New mutations wait for the coherent visual
group, not unrelated edited streaming or the entire collision queue. Tool ray
validation still requires current collision at the hit. This preserves a finite
publication boundary instead of perpetually superseding the visible result.

Physics readiness now accepts previously published collision during edit-only
replacement, including a previously empty result. Cold/unknown regions still
hold actors until published, and generator changes do not inherit this flag.
Existing support stays in place until the validated replacement swaps between
physics steps; movement/look settings are not touched solely by an edit rebuild.
Building excludes both player volumes and dynamic rigidbody bounds at admission
and commit. Delayed removal of old support is still possible until collision
publishes; this must be measured alongside visible latency and actor stability.

Repeated TerrainVertex generic draw-buffer cast exceptions occurred in hotloaded
sessions. Render-camera state now resets cached command lists at hotload and
marks them for reconstruction using the current types. This is a candidate
recovery, not proof that the engine can migrate every in-flight generic resource.
Clean-session runtime verification and deformation/figure-eight gates remain open.

The edit-only transition service opportunity is now16ms rather than100ms:
transition counting has multiple continuation stages, so the old interval could
add several100ms waits before a coherent edit appeared during regular streaming.
Existing batch limits and outer-service fairness remain in place. This is a
scheduler parameter change requiring measurement, not an accepted throughput claim.


## Multiple player collision interests

Collision residency will use the union of equal-radius chunk cubes around player
positions, independent of the visual streaming target. The host includes up to64
players (the authored session limit); clients include their locally controlled
player. With no player, the previous manager/streaming center fallback remains.
Duplicate centers and overlapping chunks share one region/job/body. Sorted
centers give stable ordering; dispatch uses minimum squared distance to any
player and coordinate tie breaks. The canonical worker, two-result queue,
geometry pool and engine-thread integration budget are unchanged. The maximum
union at radius8 is314432 chunks for64 fully separated players; this is a hard
spatial bound, not an acceptable memory/performance result. Measurement must
establish supported scale. No client can nominate arbitrary collision interests
through a network request. Additional distant dynamic actors/projectiles still
need explicit bounded gameplay interests; render distance must not provide them.


## Benchmark timing correction

The unrun candidate output is now candidate-v2. Input workloads are unchanged;
no candidate-v1 performance run has been accepted or used as a baseline. Every
committed non-no-op edit retains an outstanding observation until its derivative
and actor observations complete. A newer field commit no longer overwrites the
older collision/actor record. No-op edits leave derivative timings null.
The mesher records its coherent group publication revision/timestamp as runtime
status. VisualPublicationMilliseconds uses that timestamp (or a conservative
later publication if observation was delayed). It is engine-thread publication,
not scanout or proof of a visible change. Existing global edited-queue drain
metrics are retained explicitly as conservative proxies. PeakHeldBodies and the
collision hold-step delta expose involuntary movement holds during the workload.
Fixed scenarios and actual correctness/performance runs remain outstanding.

Host tool admission uses a two-token bucket replenished at10Hz. This bounds
sustained input while tolerating arrival jitter around the nominal100ms cadence;
the previous strict inter-arrival test could reject valid alternating ticks.
Sequence checks precede token accounting, so duplicates neither reapply nor
consume tokens. Transport/session readiness is still pending; this is not a
connected-client validation result.


## Measured large-edit sampling correction

LARGE-001/v1 initially failed its conservative collision-drain gate. Removing
unused revision walks was insufficient in the comparable second run. Pages now
also cache minimum/maximum correction for each existing8sample block, computed
on the mutation worker in the same validation pass. Range queries intersect
those intervals, still expanding for interpolation support and including zero;
full-page bounds remain the fallback for missing hotload metadata and the fast
path for full-page queries. No sample values, brush parameters, classification
sign convention, or meshing algorithm changed. This reduces false potential-
surface regions caused by treating a local correction extremum as present across
an entire512unit storage page. Added payload is512bytes/page for range arrays,
on top of256bytes/page revision arrays; page payload diagnostics currently count
float samples only, while process-memory measurements include metadata.


## Bounded collision worker parallelism

After three comparable large-event runs failed collision drain, two workers
replace the single worker. Each owns an instance of the same production mesher
and its scratch arrays; they share canonical immutable field inputs, one region
map, one ordered pending queue and one completed queue. Dispatch reserves result
capacity under the existing gate: completed+building<=2, so finishing concurrent
jobs cannot overflow the two-result bound. Native publication remains on the
engine thread with the existing0.5ms budget and at most one mesh body per update.
Cancellation/version identity and previous-support ownership remain unchanged.
No native physics calls move onto workers. Two workers are an explicit bounded
candidate, not evidence of64player capacity. Re-run identical large and hold
workloads and the canonical figure-eight before acceptance.


## Hosted player lifecycle and transfer boundary

The existing manager owns the hosted session's terrain authority and player join
orchestration. It implements the engine INetworkListener lifecycle, using the
authored PlayerController hierarchy as the player template. The local host keeps
that existing player; each remote connection gets one clone owned by that
connection, and disconnect destroys only that remote player's object. The host
owns the manager's network object. Duplicate active callbacks cannot spawn a
second player. No automatic public lobby or host migration is introduced.

The engine's documented ownership and RPC rules are used directly:
[ownership](https://sbox.game/dev/doc/networking/ownership/),
[RPC messages](https://sbox.game/dev/doc/networking/rpc-messages/),
[network events](https://sbox.game/dev/doc/networking/network-events/).
Installed26.09.01c XML plus engine compilation verifies the called APIs; the
online staging schema alone is not acceptance. The editor's own private hosting
and new-instance route is the multiplayer validation entry point, not a mock.

The existing per-caller64state limit matches the authored64player limit. Host
spawn slots are assigned once in join order,128units apart alongY and256units
above the authored spawn for remote players, to avoid overlap and give cold
collision readiness time to establish support. This is a bounded hosted-game
spawn policy, not proof of safe arbitrary-position respawn. Normal local player
position, input and camera remain unchanged. Remote editing stays gated until
coherent terrain transfer completes; connection alone never sets ReplicaEpoch.

Implemented transfer candidate: host-selected spatial coverage and absolute page
versions, one coherent manifest plus changed page payloads per recipient, using
the existing codec/page model. Acknowledged versions determine differences;
clients never replay brushes or choose arbitrary interest regions. The manifest
identifies session epoch, WorldId, generator/settings, coverage and target page
versions. Stage a bounded transfer, validate all pages before the canonical
replacement boundary, and acknowledge only committed state. Interest movement,
late joins and recovery share that path. Outside received coverage, terrain is
unknown for readiness, rather than assumed unedited. Full-world broadcast,
unbounded per-client snapshots and per-frame complete snapshots are rejected:
they amplify distant changes and retain excessive memory under continuous edits.
The implementation budgets below and first live qualification now exist; full
convergence, fault handling and multiplayer performance acceptance remain open.


## Regional transfer implementation budgets

The manager owns a per-connection acknowledged manifest (coordinate/host page
revision only), up to64 peers and at most2 active outgoing transfers. A transfer
freezes only its changed page references and manifest; after acknowledgement,
newer edits coalesce into the next absolute-page difference. Accepted host field
mutations are never dropped or replayed. Unrelated distant revisions can update
the host's observed revision without sending payload. Coverage is a host-selected
cube centered on the coarsest chunk grid, padded beyond the configured render
extent by two coarsest chunks, one grid width, and gameplay radius/two chunks. This covers snapping,
normal/transition support and local tool reach; bounds are clamped to the finite
world. Client readiness additionally checks received coverage at use time.

Each transfer uses the existing checksummed field header and page encoding;
a bounded manifest lists all target page versions and which require payload.
Fragments are at most16KiB, one partially assembled block at a time, with at
most8 page blocks unacknowledged. One client decoder runs at a time; preparation
and final field replacement run off-thread, and the existing engine-thread
mutation boundary performs the sole commit/invalidation. Shared unchanged page
references bypass replacement's per-sample comparison; changed/removed pages
retain exact comparison and tombstone invalidation semantics.

Host delivery uses a4MiB/s global and2MiB/s per-peer token budget, burst caps,
at most8 fragments and0.5ms of dispatch work per update. Page encoding has at
most one worker per active transfer (two total); collision's two workers remain
separate. A staged client may retain at most2048 decoded pages (256MiB sample
payload), eight encoded page blocks (~1MiB), and one partial block; replacing
all pages can temporarily retain old+new canonical pages. Two host transfers
can retain distinct changed snapshots; this is bounded, not accepted worst-case
memory. A180second transfer deadline and explicit failure/recovery prevent silent
permanent pending state. Three failed transfers disconnect the affected peer.

The receiver validates epoch, ordering, sizes, page revisions, generator settings,
checksums, manifest completeness and cached-page versions before installation.
Only host RPCs carry state. Page acknowledgements bound flight; final acknowledgement
follows coherent visual publication and current collision at the local player.
Unrelated collision streaming is not an acknowledgement barrier; full edited-
collision completion remains a separate observation. Initial/new-world
state gates input/physics readiness and terrain drawing; advancing beyond received
coverage hides unknown terrain until a coherent covered state is installed.
Host migration is excluded and must not promote a partial replica into authority.
These are implementation budgets; actual latency, bandwidth, fairness, multi-player
capacity and all failure cases remain acceptance measurements, not assumptions.


After the first delivery qualification, wire protocol2 adds a SHA256 trailer to
the entire manifest, in addition to existing field-header/page checksums. The
receiver validates it before allocating page state or applying metadata. This
prevents otherwise-valid metadata corruption from evading page payload checks.
Protocol1 qualification is retained as historical evidence; corrected target/
local-collision-ACK/protocol2 source requires new runtime verification.

Hosted joins instantiate `prefabs/terrain_player.prefab`, also used by the
local authored scene player. The editor converted the original hierarchy while
preserving scene identity and references. The cached live host player is used
only for host ownership and initial spawn location; remote construction never
copies its temporary input, collision-hold, or motion state. No hidden template
actor participates in collision/replication interest. Early-join runtime
acceptance remains pending.

Distant dynamic-body collision interest uses the existing collision world and
immutable field. The pre-physics safety pass records chunks intersecting each
locally simulated body's bounds swept by its preserved velocity for at least
0.25seconds, padded16units. Player-covered chunks are omitted. On the next
update, the sorted union joins normal player interest and uses the same two
workers, publication budget, stale rejection and retirement. Held bodies retain
velocity for this request; their disabled motion must not remove their interest.
At most4096 additional chunk coordinates are admitted per pass. Oversized or
excess interest stays safely held and produces an explicit capacity warning;
this is a finite support limit, not permission to claim arbitrary actor capacity.
No separate actor collision mesh/cache is created. Physical ray/projectile
systems that do not own a Rigidbody must query the canonical field or arrange
collision interest themselves; this change covers Rigidbody simulation only.
Runtime distant-body release and resource/figure-eight acceptance remain pending.

Actor interest rejects nonfinite/out-of-world bounds using TerrainField's
canonical world-coordinate limit. This validation was tightened after the
first actor run; the run coordinates lie within both bounds.

Join exception investigation (engine26.09.01c): installed assembly inspection
confirms PlayerController.OnValidate calls EnsureComponentsCreated before
UpdateBody. EnsureComponentsCreated directly accesses Body; Body is marked
RequireComponent, and the engine normally resolves required components during
PostClone/deserialization. Null BodyCollisionTags is not the cause: ITagSet.SetFrom
explicitly accepts null. The precise failing reference/lifecycle remains
unproven; do not label the prefab join exception fixed based on this inspection.

Replication worker lifetime: each peer preparation/encoding generation and each
incoming decoder owns a cancellation token linked to manager teardown. Reset,
failure, disconnect and incoming supersession cancel abandoned work. Manifest
preparation checks each page; page encode/decode checks before and after its
bounded single-page unit. Tokens are captured before dispatch, and retired
results have no path to publication. This adds cancellation without duplicating
the disk/page codec or terrain mutation path. Runtime cancellation acceptance
remains pending.

Resource teardown audit: installed PhysicsBody.Remove unregisters the body and
invalidates its native handle, while GpuBuffer.Dispose guards its handle. No
duplicate removal was established from the collision ownership walk. Render
camera visibility retirement now resets recorded commands before releasing
buffers; detached command lists otherwise retain captured buffer references.
This cleanup does not prove the native double-free root cause or repair. The
native failure reproduces on the pre-deformation baseline too; acceptance
requires clean lifecycle evidence, not an inferred waiver.

The playable terrain scene no longer includes the original template Plane
renderer/collider. That flat collider supplied nondeformable support atZ0 after
a dig, preventing the player from entering the new hole. Terrain support now
comes from the canonical field-derived collision; UNDERFOOT-001 preserves
the concrete failure and corrected-source comparison.

Active edit-group dependencies receive queue priority within regular near,
regular outer and transition lanes. These are priority indexes over the same
canonical pending dictionaries; dequeue retains normal stale-request validation
and accounting. No additional worker, dispatch budget or publication path is
introduced. Ordinary streaming retains service after the finite edit group;
new interactive edits remain admission-gated until that group publishes.
SPRINT-001/v1 exposed visual-only tail latency (worst341.8ms vs field4.5ms/
collision35.3ms); FIFO streaming ahead of edit work is a concrete scheduling
conflict, but before/after runtime measurements are required to quantify cause.

The explicit host command `voxel_terrain_fingerprint x y z radius` diagnoses
regional density divergence across peers. It snapshots immutable canonical
content and hashes it off-thread, at most one worker per peer and one host
request per10seconds. Radius uses the existing32..1024unit bounds. Hash input
is the codec's empty world header with revision0, exact requested bounds, then
SHA256 of each intersecting nonzero page's canonical payload in z/y/x order,
with its revision bytes zeroed. Whole intersecting pages are compared; empty
pages/tombstones are equivalent. World identity, generator/settings, coordinates
and sample values remain significant; local revision numbers do not. Temporary
storage is one encoded page plus<=2048page digests, not another world buffer.
Host accepts at most one response per peer for its current10second request.
This is explicit read-only diagnosis, with no per-frame hash or terrain mutation.
Missing responses/coverage are not agreement, and a matching fingerprint does
not prove rendered mesh or floating-point GPU equivalence. Runtime evidence
must compare a settled common region and retain every peer response.

## Transfer recovery behavior and remaining qualification

Source review on 2026-09-07 covered `VoxelManager.Replication.cs`, the
`UpdateTerrainEdits` commit boundary, `OnDisconnected`, manager teardown and
`TerrainReplicationManifest.Decode`. The table describes implemented guards;
it does not claim packet-fault runtime acceptance.

| Case | Implemented response | Runtime evidence still required |
| --- | --- | --- |
| Duplicate completed fragment/block or already-applied transfer | Ignore completed manifest/page data and IDs at or below the last applied ID in the receiving epoch. Repeated page ACKs do not advance the sender twice. | Deliver duplicates through the actual transport and verify one canonical commit and bounded memory. |
| Fragment gap or page block out of order | Reject the transfer; clear receiver readiness and ask the host to retry absolute regional state. | Faulted delivery followed by convergence without retaining partial data. |
| Wrong checksum, generator/settings, page revision or incomplete manifest | Validate before replacement admission; report rejection. | Actual malformed/mismatched transfer rejection and successful recovery. |
| Supersession during replacement preparation | Cancel its edit token; the canonical commit checks cancellation after obtaining the prepared result and before TryCommit. Abandoned decode/encode tasks use linked cancellation. | Interrupt preparation, confirm the obsolete result cannot commit, and measure retained work. |
| Receive failure after a field commit | Mark authority lost and disconnect; do not continue presenting that partial result as authoritative. | Exercise a failure after commit and verify successful reconnect to host state. |
| Peer disconnect before completion | Remove caller, peer, send-order entry, spawn slot and player; cancel its transfer work. Returning connection starts without acknowledged cached pages. | RECONNECT-001/v1 plus interrupted-transfer cleanup and convergence. |
| Transfer timeout or repeated rejection | Deadline is180seconds; retry clears known pages/readiness; three failures kick the peer. Retired session epochs are bounded to64. | Deadline/retry/failure-budget behavior in the production network flow. |

Reliable ordered delivery is the expected normal transport behavior. Rejection
and retry are the recovery policy for gaps/out-of-order data; this protocol
does not buffer arbitrary reorderings. Density fingerprint agreement covers
canonical regional samples only. Visual seams, actor safety, time to readiness,
allocation/memory peaks and multi-peer fairness require their own observations.
