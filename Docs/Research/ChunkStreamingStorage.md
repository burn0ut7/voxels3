# Server-authoritative chunk streaming, generation and storage

Research date: 2026-09-07. Audience: Voxels3 implementation and architecture work.

Execution plan: [authoritative storage prototype](../Architecture/ChunkAuthoritativeStorage.md) narrows the first implementation goal to integrated live deformation, persisted regional history and testable unload/reload, with acceptance before subsequent slices.

Status: research and recommended direction, **not an implemented contract or performance acceptance**. Scope covers expensive procedural SDF generation, edited terrain, durable storage, multiplayer interest, joining, unloading, recovery and distant LOD. No runtime code was changed or benchmark run for this document.

## 1. Recommended direction

Use a **server-owned, spatially indexed terrain store with a deterministic client-generation option**. The server owns the procedural recipe, saved changes and gameplay decisions; clients may reconstruct the baseline without supplying authoritative terrain. The server loads existing state before generating required gameplay regions and reuses expensive results. Keep resident memory, generation, disk work, network traffic and derived geometry separately bounded.

**Required integration direction:** evolve the existing terrain field and multiplayer edit implementation into this single chunk lifecycle system. Chunk loading, unloading, persistence and delivery handle the existing edits as part of canonical regional state. Do not add a chunk streamer beside an independently maintained multiplayer deformation store or state publisher. The ownership contract below is prescribed for implementation; it does not claim that consolidation is already complete.

The recommended end state is:

1. **Keep generation authority on the server; share deterministic reconstruction.** Evaluate client generation, especially for distant visuals, while the server independently generates/loads terrain needed for gameplay. Cache expensive results. A server-defined recipe can establish terrain before every sample has been materialized; no client result establishes server state.
2. **Store current regional state plus transactional recovery information.** Use compressed page checkpoints and a bounded recovery journal or database transactions. Do not require replaying the world's entire brush history to join or reopen it.
3. **Replicate changes to a known baseline; send state when needed.** Compatible clients may generate the baseline and apply server-owned regional changes. Use authoritative page snapshots for incompatible or divergent reconstruction. For distant terrain, compare client generation with server-derived coarse data; neither route should expand the entire fine-resolution volume.
4. **Use one revisioned synchronization protocol.** Initial join, movement, reconnect, cache validation and repair use snapshots and absolute state updates with explicit base/target versions. Client-generated terrain never repairs or establishes server state.
5. **Treat storage capability as an implementation gate.** SQLite is the leading embedded backend candidate, conditional on a supported s&box binding and filesystem contract. If those are unavailable, a supported external persistence service is preferable for strong durability to casually implementing a database in game code.

These are project recommendations drawn from the evidence below, not a claim that one game already implements this exact architecture. Luanti provides the clearest inspectable server lifecycle; Godot Voxel directly documents saving expensive generated blocks. Both are better starting references here than an uncited claim about a closed-source game's internals. See [Luanti's emergence path](https://github.com/luanti-org/luanti/blob/b81bb3c68ac633f1df8dd8ed758644cabf4c1efd/src/emerge.cpp#L545-L585) and [Voxel Tools streams](https://voxel-tools.readthedocs.io/en/latest/streams/).

**Immediate next slice:** establish durable paging of the existing correction field, with real eviction/reload and late-join verification. **Subsequent generation decision:** measure deterministic client reconstruction plus authoritative regional updates against server-supplied samples. Materializing a new baked-field representation remains an option when its measured benefit justifies migration; it is not a prerequisite for server authority. Do not silently change today's field while calling the change only a save optimization.

### One terrain system: ownership and replacement contract

One logical terrain system owns the field, its regional residency and its committed revision history. Separate storage, generation, transport and meshing modules are appropriate implementation boundaries; they must consume the same state and cannot maintain independently writable versions of terrain.

| Responsibility | Single owner/contract | Integration requirement |
| --- | --- | --- |
| World field and sample semantics | Existing `TerrainField` responsibility, evolved for paging | One canonical sampler and spatial/page identity; immutable recipe plus committed corrections define one composed field in the current format |
| Gameplay edit admission and commit | Existing host validation and ordered mutation boundary | Dig/build tools, impacts and administrative edits enter this boundary; a client submits intent, never authoritative samples |
| Load/unload and persistence | Regional lifecycle of that same field | Load saved changes before declaring a region ready; pin affected pages for edits; save committed revisions and evict safely |
| Multiplayer terrain state | Existing manifest/page replication responsibility, integrated with regional interest | One publisher delivers joins, movement, edits, reconnects and repairs; no parallel edit-state broadcast or separate late-join save channel |
| Rendering, LOD and collision | Existing derived consumers | One commit/invalidation flow supplies affected bounds and revisions; no separate editable mesh or collision terrain |
| Client reconstruction | Replica population under the same field contract | Generated baselines and received pages install into one replica; prediction cannot become an independent saved or authoritative world |

For the current representation, procedural base and corrections are complementary parts of **one field definition**, not competing truths. Likewise, RAM, a durable checkpoint and a client replica are versioned representations of that field. Only the server's ordered commit boundary creates authoritative gameplay changes. Current, durable, received and rendered revision markers describe progress through the pipeline, not different authorities.

The complete edit lifecycle must be:

1. A validated request identifies all affected pages, including sampling dependencies. The regional lifecycle loads/generates and pins their canonical state even if no player currently renders them.
2. The existing mutation boundary prepares and commits one ordered transaction. Persistence and downstream delivery observe that transaction under the declared durability policy.
3. Interested clients receive the resulting regional revisions through the same manifest/page protocol used for loading. Render/collision consumers receive one bounded invalidation for the committed change.
4. A client outside interest receives no unnecessary terrain payload. When it enters later, its normal region load includes the latest committed changes; it does not replay a separate deformation history.
5. When interest leaves, the lifecycle retires derived resources and evicts eligible field pages only after persistence and outstanding readers permit it. Re-entry uses the same region path and reconstructs the same committed state.

Loading an already committed page changes residency, not terrain history: retain its authoritative revision and reject stale load results. A deliberate world restore is a separate epoch/restore operation through the same field boundary. Neither disk loading nor client receipt should manufacture a new gameplay edit and broadcast it back, causing duplicate mutation or replication loops.

**Replacement rule:** extend or refactor `TerrainField`, `TerrainFieldCodec`, the manager's deformation orchestration, and its manifest/networking/replication responsibilities as needed. Preserve existing tool validation and real gameplay entry points. Remove superseded whole-world retention, save/load orchestration and duplicate terrain-state delivery paths in the same implementation change that replaces them. Do not keep a compatibility layer that runs an old edit system alongside a new chunk system. An export/backup command, if retained, reads the same committed store and codec; it is not a second persistence authority.

Backend and generation alternatives in this research are decision candidates within that one system. Choose one backend and one canonical field interpretation for the implemented world format; do not ship all researched candidates as parallel terrain systems. Snapshot versus delta and generated baseline versus received payload are protocol encodings under one identity/revision model, not separate gameplay paths.

## 2. What Voxels3 currently implements

This audit used HEAD `f1319ed30f10ac4b3a7247ef8a8e04465637e753` on `codex/terrain-deformation` **plus the active uncommitted working tree**. Several terrain/network files were untracked at inspection. HEAD alone cannot reproduce this audit; source links refer to the local working tree. The implementation was changing in another task, so re-read the owners before implementation.

| Current evidence | Consequence for this research |
| --- | --- |
| [VoxelChunk](../../Code/Voxels/VoxelChunk.cs) is a lightweight field view; it does not own a saved density array. [VoxelManager](../../Code/Voxels/VoxelManager.cs) validates 32 cells per axis at 16 units per cell. | A logical chunk is not currently a resident database record. Avoid interpreting logical loaded counts as allocated chunk memory. |
| [TerrainFieldSnapshot.SampleWorld](../../Code/Voxels/TerrainField.cs) returns procedural base plus trilinear correction. Pages own 32³ correction samples; `TerrainField.TryCommit` replaces immutable snapshots. | The existing canonical field and mutation boundary are valuable. Persisting only corrections does not eliminate procedural evaluation. |
| `TerrainField` limits the current store to 2,048 pages and an edit transaction to 216 pages. Leaving a visual region does not itself page those host corrections to disk. | A finite in-memory correction store is not a persistent-world eviction system. Simply raising its limit will scale memory with explored edits. |
| [TerrainFieldCodec](../../Code/Voxels/TerrainFieldCodec.cs) serializes generator/settings, identity and checksummed correction pages, choosing sparse or dense float payloads. | This is a useful existing format boundary, but not a general compressed generated-terrain database. |
| [SaveTerrainCommand](../../Code/Voxels/VoxelManager.Deformation.cs) asynchronously writes a snapshot to `terrain/{slot}.vxt` using `CreateNew`. Load uses the canonical replacement path. | Existing named snapshots preserve a previous filename; they do not establish incremental regional saves, automatic checkpointing or power-loss durability. |
| [Replication](../../Code/Voxels/VoxelManager.Replication.cs) and [manifest](../../Code/Voxels/TerrainReplicationManifest.cs) already carry coverage, page revisions, world/settings identity and bounded fragments. | Extend this responsibility rather than introducing unrelated whole-world and chunk RPC systems. |
| Existing transfer limits include 16 KiB fragments, eight page blocks in flight, two active transfers, 2 MiB/s per peer, 4 MiB/s global and a 180-second deadline. | These are configured limits, not measured capacity guarantees. Two active transfers need fair rotation if many people join. |
| [GPU meshing](../Architecture/GpuVoxelMeshing.md) uses a level-indexed clipbox with regular/transition dependencies. Coarse samples currently come from procedural field evaluation. | Networking baked terrain changes a real input dependency of the renderer, including transitions and normals. |
| [Project configuration](../../voxels3.sbproj) declares 64 players and 50 Hz. [Terrain deformation](../Architecture/TerrainDeformation.md) records incomplete acceptance. | Use 64 as a stress target, not as established terrain-streaming support. This report does not close existing validation gates. |

The important implemented distinction is **implicit procedural base + explicit edited corrections + derived geometry**. Nothing in this research justifies a second mutable world inside the network or renderer.

## 3. What strong implementations actually show

“Best in class” here means useful, inspectable design evidence. No comparable benchmark establishes a universal winner across these engines.

### Luanti: the strongest server lifecycle reference

Luanti defines a mapblock as 16×16×16 nodes, used for database storage and client transfer; generation can work in larger mapchunks. Storage and generation units therefore need not be identical. This is block terrain, not a ready-made SDF layout. [Luanti map terminology](https://api.luanti.org/map-terminology-and-coordinates/).

At commit `b81bb3c68ac633f1df8dd8ed758644cabf4c1efd` (committer date 2026-09-04), `EmergeThread::getBlockOrStartGen` checks memory, then stored block data, before starting generation. It rechecks memory after a disk read so a raced load does not overwrite a newer block. [Pinned emergence implementation](https://github.com/luanti-org/luanti/blob/b81bb3c68ac633f1df8dd8ed758644cabf4c1efd/src/emerge.cpp#L545-L585).

`RemoteClient::GetNextBlocks` bounds simultaneous sends, visits distance shells and skips blocks already sent or in flight. `GotBlock` advances delivery bookkeeping only for a currently outstanding block; invalidation removes sent/in-flight knowledge. Client deletion notifications also reset send knowledge. These are delivery bookkeeping mechanisms, not proof that a client is honest. [Pinned client selection and acknowledgements](https://github.com/luanti-org/luanti/blob/b81bb3c68ac633f1df8dd8ed758644cabf4c1efd/src/server/clientiface.cpp#L115-L448), [packet handlers](https://github.com/luanti-org/luanti/blob/b81bb3c68ac633f1df8dd8ed758644cabf4c1efd/src/network/serverpackethandler.cpp#L542-L568).

`Map::timerUpdate` saves modified blocks before deleting them when save-before-unload applies, and skips deletion if `saveBlock` fails. The SQLite backend has explicit begin/end save operations. Do not infer power-loss guarantees merely from those calls. [Pinned unload implementation](https://github.com/luanti-org/luanti/blob/b81bb3c68ac633f1df8dd8ed758644cabf4c1efd/src/map.cpp#L286-L390), [SQLite implementation](https://github.com/luanti-org/luanti/blob/b81bb3c68ac633f1df8dd8ed758644cabf4c1efd/src/database/database-sqlite3.cpp#L93-L107).

**Adopt:** load-before-generate, stale-load rejection, per-recipient knowledge and explicit save-before-evict. **Do not copy:** node size, block payloads or synchronous lifecycle details without s&box measurements.

### Godot Voxel: directly addresses expensive generation

Voxel Tools' stream documentation explicitly permits saving every generated block when the generator is too expensive to rerun; its default otherwise saves modified blocks. Terrain performs asynchronous loading/saving. It warns that pending tasks outlive scene destruction and that changing a live stream's path can send old-world saves into a new world. [Streams and asynchronous save lifecycle](https://voxel-tools.readthedocs.io/en/latest/streams/).

The multiplayer documentation describes server-owned viewers. Its dated 2022 approach explicitly allows clients to generate unedited blocks locally and install server-supplied edited data; installing a replacement cancels pending local generation so it cannot overwrite the replacement. The page labels multiplayer experimental and says the documented approach does not support `VoxelLodTerrain`. It establishes a hybrid technique, not solved smooth multiplayer LOD or arbitrary generator-mismatch deltas. [Multiplayer documentation](https://voxel-tools.readthedocs.io/en/latest/multiplayer/).

The SQLite stream supports LOD-bearing coordinate keys and documents a key cache useful for sparse edited saves, not saves containing all generated blocks. The pinned implementation uses a write cache and explicit transaction failure recovery; a requested save and completed durable write remain different events. [SQLite stream API](https://voxel-tools.readthedocs.io/en/latest/api/VoxelStreamSQLite/), [source at `2ac9f5f8a8219bf499314cc0fad54ffc47df908f`](https://github.com/Zylann/godot_voxel/blob/2ac9f5f8a8219bf499314cc0fad54ffc47df908f/streams/sqlite/voxel_stream_sqlite.cpp).

**Adopt:** a block-addressable store, immutable world identity for asynchronous work, and saving expensive generated results. **Limit:** its Godot/C++ integration and LOD/network support cannot be transplanted as an s&box subsystem.

### Space Engineers: historical procedural baseline and synchronized changes

Keen's archived source at `54f2f0f3169cda687a25a438097902a43bdfa603` contains seeded asteroid generation whose inspected initialization/generation path is not restricted to the server. Generated asteroid storage is marked `Save = false`; its first range change switches saving on. Separate voxel operations use replicated events. This is historical game-source evidence for procedural reconstruction alongside persistent/networked changes. [Generator initialization](https://github.com/KeenSoftwareHouse/SpaceEngineers/blob/54f2f0f3169cda687a25a438097902a43bdfa603/Sources/Sandbox.Game/Game/World/Generator/MyProceduralWorldGenerator.cs#L261-L290), [asteroid creation and persistence](https://github.com/KeenSoftwareHouse/SpaceEngineers/blob/54f2f0f3169cda687a25a438097902a43bdfa603/Sources/Sandbox.Game/Game/World/Generator/MyProceduralAsteroidCellGenerator.cs#L147-L195), [voxel edit events](https://github.com/KeenSoftwareHouse/SpaceEngineers/blob/54f2f0f3169cda687a25a438097902a43bdfa603/Sources/Sandbox.Game/Game/MyVoxelBase.cs#L780-L806).

**Transfer limit:** these files do not establish a protocol that compares arbitrary client/server generated chunks and sends mismatch deltas, nor the current shipped game's behavior or security guarantees. Use this precedent to investigate reconstruction plus changes, not to claim deferred client verification eliminates authoritative server work.

### Minecraft-compatible region storage: study the format, not folklore

Cuberite provides inspectable Anvil storage. Its `cWSSAnvil` groups 32×32 chunk columns into region files, serializes NBT, compresses each chunk with zlib and writes sector-addressed payloads. Its allocator reuses an existing location when the payload fits and otherwise appends, explicitly noting wasted space. [Pinned implementation at `7fd3fa5c9345a3f1b949c0988c4849db00a68486`](https://github.com/cuberite/cuberite/blob/7fd3fa5c9345a3f1b949c0988c4849db00a68486/src/WorldStorage/WSSAnvil.cpp#L239-L338), [region header](https://github.com/cuberite/cuberite/blob/7fd3fa5c9345a3f1b949c0988c4849db00a68486/src/WorldStorage/WSSAnvil.h#L35-L47), [allocation](https://github.com/cuberite/cuberite/blob/7fd3fa5c9345a3f1b949c0988c4849db00a68486/src/WorldStorage/WSSAnvil.cpp#L4123-L4134).

**Adopt:** aggregate small spatial records while preserving independent access and compression. **Reject for now:** cloning Anvil's 2D column layout, sector allocator and compatibility baggage. Cuberite is an independent implementation; these observations do not establish the internals or current protocol of Mojang's server. No unverified Minecraft protocol packet sizes are adopted.

### Factorio: excellent transfer evidence, different replication model

Wube's 2016 map-transfer post explains that its then-current lockstep simulation required the complete map on clients. It describes congestion-control and recovery bugs in bulk transfer. [Friday Facts #136, Cube/Wube, 2016-04-29](https://www.factorio.com/blog/post/fff-136).

**Adopt:** bulk transfer is a controlled flow with acknowledgements and congestion considerations. **Do not adopt:** whole-world synchronization or custom transport simply because Factorio used them. Voxels3 can keep remote simulation on the server and stream local terrain interest. This post is historical, not a claim about Factorio's 2026 transport.

### Voxel Plugin and Unreal: useful boundaries and explicit limitations

Voxel Plugin 1.2 distinguishes replaying edit actions from transmitting voxel state; its legacy TCP route supported join/save synchronization. Current 2.0p8 documentation still says runtime edit replication requires manual synchronization. The 2.0p7 edit page warns that sending the whole save through RPCs can overflow limits. These are version-specific observations, not a combined feature promise. [Legacy multiplayer](https://docs.voxelplugin.com/1.2/core-systems/voxelworld/multiplayer), [2.0p8 multiplayer](https://docs.voxelplugin.com/knowledgebase/blueprints/multiplayer-support), [2.0p7 runtime edits](https://docs.voxelplugin.com/2.0p7/knowledgebase/blueprints/runtime-edits-and-sculpting).

Epic's World Partition describes multiple streaming sources, priorities, loaded versus activated states, and preloading a teleport destination before moving the player. These ideas transfer to readiness; actor streaming does not solve SDF persistence. [Epic World Partition documentation](https://dev.epicgames.com/documentation/en-us/unreal-engine/world-partition-in-unreal-engine).

Voxel Plugin's 2.0p8 page also describes deterministic generation across server and clients from identical inputs. This supports evaluating shared reconstruction while retaining the page's explicit limitation that runtime edits require manual synchronization. [2.0p8 generation and multiplayer](https://docs.voxelplugin.com/knowledgebase/blueprints/multiplayer-support).

**Synthesis:** separate world state, data residency, simulation readiness, visual readiness, persistence and delivery. The references agree on these responsibilities more strongly than on a particular database, chunk size or transport.

## 4. Authority and security contract

Everything in the remaining design sections is a **Voxels3 proposal**, unless explicitly labeled current evidence or attributed to an external source.

The server owns generation inputs, generated output, edit ordering, page versions, durable commits, gameplay collision and validation. Clients send intent: tool input or an action request. The server resolves the affected terrain using server state and checks identity, reach, line of sight, permissions, resources, actor intersection and rate/size limits.

A client must never send an authoritative chunk, density result, generator completion, hit result or inventory reward. Cache hashes and acknowledgements help decide what to send; they cannot prove that a modified client rendered or simulated terrain honestly. A lying client must only damage its own presentation, not change the shared world.

Client generation is compatible with server authority when gameplay is independently evaluated by the server. Recommend evaluating a shared deterministic baseline plus server-owned edits, particularly for distant visuals. Reconstruction requires compatible inputs/evaluation and exposes whatever recipe/seed is supplied. Keep hidden gameplay information in separately controlled server data if necessary; do not assume shared generation can conceal that same information.

### What client generation actually relieves

| Approach | Potential benefit | Remaining server cost |
| --- | --- | --- |
| Client generates, then server generates the same chunk and compares | Earlier provisional visuals and possibly smaller downloads | Essentially the same generation work, plus comparison/repair overhead |
| Compatible baseline generated locally; server sends committed changes | Less baseline bandwidth and network waiting | Independent generation/loading wherever server gameplay requires terrain |
| Client generates distant visual terrain; server materializes only gameplay dependencies | Can reduce server generation as well as bandwidth | Union of nearby player/actor/edit dependencies; still grows with disjoint exploration |
| Trusted generation workers or pre-generation | Moves or smooths expensive work outside the active simulation workload | Compute/storage still exist; trusted outputs, ordering and deployment must be qualified |

Latency hiding is not increased server throughput. If requests arrive faster than the server can prepare authoritative gameplay terrain, a growing queue eventually exhausts the client's prediction lead. Keep generation bounded, reuse cached results, deduplicate overlapping interests, prefetch and reserve service for gameplay. Many clients seeing the same region should share one server generation result; many exploring different regions are the difficult case.

A promising further split is server-owned macro generation data with deterministic client evaluation of visual detail. This helps only if macro data is reusable and fine evaluation is bounded; it does not permit gameplay-relevant details to exist solely in an untrusted client's result. These are proposed alternatives to measure, not implemented worker services or performance claims.

### Baseline updates versus generation disagreement

Prefer **changes relative to a known baseline** as the normal protocol: pin the generator/version/settings, generate locally, then apply the server's committed regional state. The server need not accept client samples or continuously compare the entire world to remain authoritative. It evaluates its own state for gameplay and controls all mutations.

Treat **arbitrary generation disagreement** as recovery. A different recipe or CPU/GPU floating-point behavior can produce widespread differences, so a tiny delta is not guaranteed. A hash detects disagreement but does not identify changed samples. A delta requires an identified compatible baseline; otherwise send bounded absolute pages, invalidate dependent meshes/collision, and disable incompatible local reconstruction for that session. Hashes and sampled checks are not a cheap proof that client-generated gameplay terrain is correct.

Locally generated distant terrain may be provisional presentation. Nearby traversal, digging and building still require server gameplay readiness and a coherent received edit revision. Guessing that an edited region is untouched can display solid ground over a saved tunnel. If server support is missing, hold progression or use explicitly reconciled prediction; do not grant persistent rewards or accept actions on unchecked client collision. Predicting farther ahead can hide bounded delays, but cannot guarantee safe play through sustained server overload.

This does not eliminate all cheating. A client can inspect anything it receives. Sending deep ore, caves or hidden structures permits information extraction even if editing is secure. Use separate server-only semantic records and restricted interest if hidden information matters; coarse visual terrain should omit unnecessary gameplay metadata. Perfect concealment of already-transmitted geometry is not promised.

The host process is the trust boundary for listen servers. Preventing the person running the host from cheating requires a trusted dedicated server, not different chunk messages. Host migration needs a complete authoritative store and writer fencing; a partially streamed client is never eligible just because it has a nearby cache.

## 5. Decide what a saved terrain page means

### Options and recommendation

| Representation | Benefit | Main cost or failure mode | Decision |
| --- | --- | --- | --- |
| Seed/recipe plus entire edit history | Small early saves | Regeneration and replay grow; historical tool semantics must remain executable | Reject as the sole long-lived state and join format |
| Pinned procedural base plus materialized correction pages | Fits today's field; supports shared reconstruction and sparse edit transfer | Expensive base still runs unless cached; compatible generator remains necessary | First persistence slice and leading hybrid candidate to measure |
| Materialized final density/material pages | Reads and joins avoid base generation; edits become ordinary page updates | Disk growth and a deliberate sampled-field contract | Conditional alternative when measurements justify baking/migration |
| Meshes as saved authoritative terrain | Fast display of exactly that mesh version | Poor editing/query representation; renderer-dependent; collision can diverge | Reject as canonical state; optional derived cache only after profiling |

A generator recipe remains the authoritative rule for **unmaterialized** coordinates. In the baseline-plus-corrections design, materialized base samples are caches; recipe plus committed corrections remain canonical. If the baked format is adopted, its saved final page becomes authoritative for that coordinate. Choose one field interpretation per world format. Do not keep an independently mutable final field and correction field as competing truths. A reconstructed client replica is never an authority source in either design.

### Baking is a semantic change, not just serialization

Today `SampleWorld(p)` evaluates the continuous procedural formula at `p` and adds interpolated corrections. Storing only values on the 16-unit lattice and then interpolating them generally does **not** reproduce that formula between lattice points. Normals, ray hits, classification and collision queries can change even when lattice values match. [Current sampling source](../../Code/Voxels/TerrainField.cs).

If the baked format is chosen, define a canonical lattice: server-generated finite float density samples, fixed coordinates and interpolation rules, with all fine gameplay consumers using the same contract. Initial precision stays float32. Before adoption, compare actual collision/contact, brush behavior, normals, thin features and LOD seams through the production world. The shared-baseline candidate can instead preserve the continuous field and cache immutable regional generation intermediates; compatible clients use its verified evaluator and receive authoritative changes.

Migration must read the old pinned recipe and correction pages, bake into a **new world-format version**, validate and retain the original backup. Do not run both field interpretations as selectable runtime fallbacks. No representation change is authorized or implemented by this report.

### Spatial ownership

Keep the existing 32³ owned-sample page as the starting candidate, subject to measurements. A render chunk's 33³ logical sample view obtains its positive boundary from neighboring owners. Use floor division for negative coordinates and a single global integer sample key; temporary halos are derived copies.

Separate these units:

- **Sample page:** canonical values and revision ownership.
- **Generation work region:** possibly larger, when biome, river or structure work benefits from shared calculations.
- **Storage segment/database page:** I/O grouping; independent of render geometry.
- **Visual LOD brick:** derived samples at an explicit level and source version.
- **Network fragment:** bounded transport unit, with no spatial authority.

A structure spanning pages needs deterministic ownership. Assign it to a stable feature region and feature ID, discover intersecting features using bounded dependencies, then write only the owning samples. Generation order, player count and thread scheduling must not change rivers, caves or structures. If a generator needs truly global erosion or drainage, precompute and persist a bounded macro dataset; do not disguise global work as a cheap chunk request.

## 6. Generation, loading and unloading lifecycle

### One bounded server pipeline

```text
server-approved player / actor / teleport interest
                       |
                region request table
                       |
          resident? -> use current revision
                       |
             regional store lookup
                /              \
        saved state           proven absent
             |                    |
        decode/verify       pinned generation
             \                    /
              version-checked publication
                       |
            canonical terrain revision
             /           |             \
       persistence   replication    derived collision/LOD
```

Missing, known-empty, ungenerated, nonresident and corrupt are distinct outcomes. A read error or corrupt edited record must never be interpreted as permission to regenerate over a player's work. Retry bounded transient failures; otherwise quarantine the record and hold dependent gameplay with a visible storage error.

Deduplicate work by world identity, page coordinate, representation version and generation stage. Multiple players share one resident page and one in-flight generation job. A cancelled viewer request removes that viewer's interest, not another player's job or an accepted save.

Worker inputs are immutable. Every completion checks world/session, job lifetime and relevant source revision. A disk read started before an edit cannot publish over the newer page. Engine resources are created or replaced only at supported engine-thread boundaries. Bound outstanding jobs, decoded bytes, compressed buffers, retained snapshots and integration time independently.

Prioritize missing collision/support around active actors, nearby edits, immediate visual dependencies, then forward prefetch and distant refinement. Reserve service for ongoing gameplay and existing clients so new joins cannot monopolize generation or upload. Age queued requests and rotate peer transfers; proximity alone can starve stationary or slow clients.

### Residency is a set of interests, not one loaded flag

Track references from players, simulated bodies, pending mutations, saves and in-flight immutable readers. Full-resolution simulation interest is the union across relevant actors. A headless server needs collision and field data there, not every player's GPU mesh. Visual interest may be larger and coarser. Remote visual interest alone should not activate every creature, fluid or machine in a distant region.

The authoritative directory belongs on disk with bounded metadata caching. Do not retain a dictionary entry or an immutable whole-world snapshot for every page ever explored. Regional snapshots must pin only their actual dependencies; otherwise adding an eviction queue will not release the pages. Generation status and durable revisions still need recoverable index records after RAM eviction.

Retain a bounded warm cache outside the active set with hysteresis: load at one boundary, release after a larger boundary or a fixed grace period. Account in bytes, not merely page count. A fast player returning to a region should normally reuse memory or disk rather than generate again.

A page may leave canonical RAM only when:

1. No simulation/mutation dependency requires it.
2. The state to be preserved has a confirmed durable representation, or is explicitly classified as safely reconstructible unedited cache data.
3. Immutable readers have released their versions, or those versions are counted in a separate bounded retention budget.
4. Its directory metadata still identifies where and at which revision it can be recovered.

Saving revision 10 must not mark a concurrently edited revision 11 clean. Track current and durable revisions separately. Dirty pages awaiting a failed save remain protected; if memory is exhausted, throttle generation/edits or stop admitting work instead of dropping accepted terrain changes.

Unloading a **client replica** is different: it can discard a page and later request/revalidate it. Dropping a mesh says nothing about whether its underlying data is still resident. Server-side interest and cache state must be reconciled so the server does not assume a client retains a discarded baseline forever.

### Prefetch and teleporting

Use measured end-to-end latency, not only disk time:

`lead distance >= allowed speed × p99(data + generation + network + decode + collision readiness) + safety margin`.

Prefer a measured total pipeline percentile to summing unrelated component percentiles. Example only: 1,024 units/s and a two-second measured pipeline imply at least 2,048 units, or four current chunks, before margin. This is arithmetic, not a proposed accepted workload.

A teleport has no finite travel lead time. Admit a destination interest, prepare its safety region, transmit it, and move/release the player only after the required server collision and client presentation readiness. Put a deadline and explicit failure outcome on this operation. Do not replace readiness with a fixed sleep.

## 7. Storage and durability

### Backend selection

| Candidate | Appropriate use | Cost/constraint | Recommendation |
| --- | --- | --- | --- |
| SQLite with compressed regional blobs | One authoritative writer; spatial lookup; atomic multi-page edits | Supported binding/VFS, checkpoint and backup management | Leading embedded candidate, pending s&box capability proof |
| Custom append-only region files with index/checkpoints | Runtime filesystem is available but embedded database is not | Must implement recovery, atomic visibility, compaction, indexing and bounded startup | Possible fallback, not presumed simpler or power-loss safe |
| External server-owned persistence service | Dedicated persistent worlds; game runtime lacks durable DB access | Deployment, authentication, latency and outage handling | Prefer over unproven in-process durability for a persistent service |
| One ordinary file per sample page | Small diagnostic/export datasets | Large file counts; awkward cross-page transactions | Avoid as the main long-lived world format |
| Object/blob storage | Immutable backups or large cold snapshots | Not by itself a low-latency transactional edit store | Later operational layer, not first implementation |

Luanti defaults to SQLite and supports other backends; its documented backend comparisons are qualitative and deployment-specific. Godot Voxel also exposes SQLite-backed streams. Neither establishes performance inside s&box. [Luanti database backends](https://docs.luanti.org/for-server-hosts/database-backends/), [VoxelStreamSQLite](https://voxel-tools.readthedocs.io/en/latest/api/VoxelStreamSQLite/).

### Suggested logical records

- **World manifest:** persistent world ID, world-format version, generator recipe/version and settings, material registry/version, sample spacing and sign convention, latest durable transaction and writer identity.
- **Canonical page:** world/dimension, signed integer coordinate, page revision, generation status, payload encoding, bounded compressed/uncompressed lengths, checksum and final field payload. In the first correction-only format, explicitly identify the payload as corrections.
- **Transaction/checkpoint:** accepted operation identity, commit sequence and all affected page revisions. Page updates and commit metadata become visible atomically.
- **Derived LOD/cache record:** level, coordinate, representation version and exact source dependencies or a content fingerprint. Disposable, never an independent editable world.

Persist non-voxel gameplay records separately when their responsibility differs: inventories, actors, plants, spawned structures or fluid simulation. If mining removes terrain and grants an item, both effects need a shared durable transaction or an explicit replay-safe transaction/outbox contract. Otherwise a crash can duplicate rewards or lose terrain while keeping the item.

### Recommended edit durability semantics

Distinguish **queued**, **committed in memory**, **durable**, **replicated**, **collision ready** and **visually ready**. These are observable stages, not interchangeable success labels.

For a persistent multiplayer world, recommend durable confirmation before an edit's persistent reward/final success. Compute the transaction off-thread from immutable versions, reserve/order it at the mutation boundary, persist its resulting page changes as one transaction, then publish the committed revision. Group commits may amortize storage barriers, but must have bounded delay and ordering. If a crash occurs after durability but before publication, recovery publishes the durable transaction once; if before durability, the request was not final-successful.

A lower-latency alternative publishes before durability and reports a documented rollback window. That is a product decision requiring explicit acceptance of potential lost edits. Do not quietly give autosave semantics while promising that every accepted edit survives power loss.

With a database, use its transaction log rather than implementing a redundant second durability journal by default. Store application idempotency/transaction metadata where recovery requires it. With custom files, include record framing, checksums, commit records, sequence numbers and bounded recovery checkpoints; only complete committed transactions become visible, including edits spanning several pages.

SQLite WAL permits concurrent readers with one writer and requires checkpoint management; long-lived readers can delay checkpoints. WAL is not suitable for multiple machines opening one database on a network filesystem. Use short DB reads and detached immutable page snapshots for network transfers. [SQLite WAL](https://www.sqlite.org/wal.html).

SQLite documents that WAL plus `synchronous=FULL` synchronizes on transaction commit; `NORMAL` can roll back recent transactions after OS crash or power loss. Guarantees still depend on a correct VFS/filesystem/device. Verify the actual configured mode and bundled version. [SQLite synchronous settings](https://sqlite.org/pragma.html#pragma_synchronous).

Version-specific finding: SQLite's current WAL documentation reports the WAL-reset fix in 3.51.3 and later, with specified backports. If SQLite is selected, verify the deployed library includes that fix; do not assume the installed package name establishes its version. [WAL-reset release qualification, section 11](https://www.sqlite.org/wal.html).

Backups must represent a coherent committed state. Use a supported online backup/snapshot mechanism or a controlled closed database, not an arbitrary live copy of just its main file. Retain manifests and recovery dependencies, then actually restore backups. [SQLite backup API](https://www.sqlite.org/backup.html).

### Failure and lifecycle rules

On disk full, write failure or persistence-service outage, preserve already durable data, surface the error and stop final-success acknowledgement for unpersisted edits. Do not let dirty backlog grow without bound. On shutdown, stop admission, complete or explicitly fail queued intents, flush/checkpoint within a bounded shutdown policy, and report whether durability completed.

Every asynchronous operation captures a fixed world/store handle. Loading a different save creates a new session epoch and retires the old handle only after its work completes or is safely cancelled. Never retarget a shared writer while old jobs are queued. Compaction writes a new recoverable checkpoint, atomically publishes its availability through the verified backend, and reclaims old records only after reader and backup dependencies are clear.

## 8. Multiplayer snapshots, updates and client caches

### One protocol, several bounded message types

Use the existing terrain replication owner as the single publisher for the chunk lifecycle. Multiplayer terrain edits become updates to those same subscribed regional revisions; chunk loading includes their committed state automatically. Client-to-host edit intent remains an input to the validated mutation boundary, not a second terrain-state synchronization protocol. Add typed records as needed within one protocol: session manifest, region manifest, page snapshot, absolute page update, coverage removal, acknowledgement and resynchronization request.

Each transfer identifies persistent world ID, session epoch, protocol/field/material versions, interest generation, transfer ID, page coordinate, base/target revision and lengths/checksums. World ID survives restart; epoch changes on restart/load so old messages cannot be mistaken for current ones. Revisions must not ambiguously wrap or be reused after restoring a backup.

Use application fragments inside the supported transport. A 16 KiB application fragment is **not** a 16 KiB UDP datagram or a proven safe engine maximum. Bound encoded and decoded bytes, outstanding fragments, assemblies, retries, timeouts and dispatch time. Reserve network capacity for player state and control traffic. Do not add raw TCP/HTTP merely because another engine preferred it; qualify relay, routing and authentication first.

### Join or new spatial interest

1. Authenticate the connection and choose the interest from server-approved player/actor state. A client may request a quality preference within server limits; it cannot force arbitrary world generation.
2. Freeze a coherent region manifest at a committed revision. Identify the compatible procedural baseline and complete edit coverage, or enumerate authoritative replacement pages and explicit known-empty records. Include halos and transition dependencies. An absence of listed edits establishes unedited terrain only within explicitly complete coverage.
3. Prepare a small gameplay safety region first. Compatible clients reconstruct the baseline and apply its committed changes; other cases receive sample pages. Follow with required coarse visual coverage and refinement according to bounded priorities. Do not wait for the entire distant horizon before allowing all nearby interaction, unless the existing publication dependencies actually require it.
4. Client stages and validates records off-thread, then installs a complete dependency group through its canonical field boundary. Missing data remains unknown; absence from an incomplete transfer never means air or unedited terrain.
5. Acknowledge installed data revisions. Report collision/visual readiness separately. Existing Voxels3 currently delays final acknowledgement for readiness; any split must preserve its safety gates while allowing obsolete network buffers to be released.
6. Coalesce changes that occurred during transfer into the next absolute update. If a retained baseline is unavailable, send a new snapshot through the same protocol. Never retain an unlimited edit backlog for one slow client.

A regional manifest can contain different page revision numbers and still be coherent if all are taken from one committed world cut. A transaction touching pages on both sides of an interest boundary sends the relevant state and required dependencies; the client need not receive the entire world transaction's unrelated spatial data.

Treat baseline reconstruction and page replacement as encodings within this one synchronization protocol. Both install through the same field boundary and revision checks. A local generation job captures its baseline identity and target revision; an authoritative replacement or newer edit invalidates the job before publication. Never let a late baseline completion erase an already installed edit. Reconstruction compatibility includes generator inputs, evaluation rules and field format, not just the world seed.

### Editing during transfer: concrete example

A snapshot pins page A at revision 100. The host commits a new edit producing A101 while A100 is being sent. Finish A100 as a bounded baseline, then send A100→A101, or supersede the transfer with an explicit new identity. Do not mix A101 fragments into A100.

For a brush crossing A and B, the manifest associates both affected page revisions with the transaction. Stage the required pages before exposing that dependency group; otherwise the client can temporarily display or collide with half an edit. If B is outside current interest, preserve enough transaction/coverage metadata to fetch the correct state when B enters it.

Use absolute changed sample values only when the client has the specified baseline; duplicates become harmless and gaps trigger repair. For dense changes or missing baselines, send an absolute page snapshot. This avoids depending on clients replaying brushes identically. Coalesce repeated changes to the newest state when intermediate history has no gameplay requirement; keep authoritative transactions and rewards intact on the server.

### Cache safely, without trusting it

Client disk cache keys include world ID, field/material format, coordinate/LOD and immutable content identity. The server validates cache claims against its authoritative directory and sends missing/different data. The client verifies payload length/checksum before installation; a hash identifies content, not trust in the client.

Keep client cache quotas, expiry and eviction local and bounded. A server restart does not require downloading unchanged immutable content again, but a fresh epoch/manifest must revalidate its applicability. A restore, new world or representation migration must never accidentally reuse an old coordinate-only cache entry. Client cache failure falls back to server transfer; server storage failure does not fall back to client authority.

## 9. Distant terrain and LOD are the major scaling constraint

The current visual system reaches far using coarse evaluation, not by allocating every fine voxel in that volume. Keep that property when generation becomes stored data.

**Revised candidate policy:** clients generate compatible distant baseline samples and apply server-owned changes; the server focuses materialization on actual gameplay dependencies. Compare this against bounded server-derived coarse bricks when generation is too expensive for clients, incompatible or cannot be reconstructed from shared inputs. Supply explicit support samples/rules for transitions and normals. Render/collision meshes remain derived data. The server can share immutable encoded bricks where it supplies them, but need not reproduce every visual-only sample merely to compare it with a client.

At shared lattice positions, fine/coarse samples must agree under the chosen field contract. Start from nested sampling rather than independently generated “similar-looking” coarse terrain. Filtering or averaging changes the represented surface and may lose caves; adopt it only with a defined restriction rule and measured seam/feature behavior. A sign summary may skip proven non-surface geometry, but cannot replace the density magnitude needed for interpolation near a surface.

There are two distinct coarse construction cases:

- **Already materialized or edited terrain:** derive coarse values from the selected canonical field with a spatial index over relevant persisted edits. A generating client needs complete authoritative edit coverage or a server-provided coarse replacement. Dirty exact dependent coarse/transition regions on commit, including nonresident ones; stale disk caches must fail dependency checks later.
- **Unexplored procedural terrain:** a compatible client or the server may evaluate only the nested positions required by a coarse brick, sharing immutable macro generation data. Later authoritative fine generation must produce the same values at shared positions under the chosen contract. This does not require immediate server evaluation of all distant samples and is not permission to generate a different coarse world.

If a future generator cannot evaluate coarse samples without first generating all fine children, do not recursively expand an enormous fine volume on demand. Precompute an authoritative multiresolution dataset, constrain first-visit visual distance, or redesign the generator's bounded evaluation. For some generators this is the decisive feasibility limit; compression cannot solve generation that was never bounded.

Retain old compatible geometry until a replacement's exact dependencies are ready. If incoming coverage ends, hold movement or present an explicit loading boundary according to readiness rules; do not render a locally guessed procedural surface as authoritative. The existing atomic clipbox handoff and edit publication groups must be accounted for before claiming gradual join refinement works.

## 10. Quantitative sizing and encoding decisions

These are **uncompressed payload calculations**, not measured RAM, bandwidth or compression results. They exclude metadata, halos, materials, old/new revisions, managed overhead, network buffers and meshes.

| Example | Arithmetic | Payload |
| --- | --- | --- |
| One owned density page | 32³ × 4 bytes | 128 KiB |
| One 33³ logical density view | 33³ × 4 bytes | About 140.38 KiB; 9.67% above owned samples |
| Radius-four inclusive fine cube | 9³ × 128 KiB | 91.125 MiB |
| 64 completely disjoint such cubes | 64 × 91.125 MiB | About 5.70 GiB |
| Current 2,048 correction-page limit | 2,048 × 128 KiB | 256 MiB for values alone |
| Dense data for 25,907 preparation coordinates | 25,907 × 128 KiB | About 3.16 GiB; an illustration, not the current allocation |
| A naive radius-512 fine cube | 1,025³ × 128 KiB | About 128.38 TiB |
| One million persisted dense pages | 1,000,000 × 128 KiB | About 122.07 GiB |

At the current 2 MiB/s per-peer application budget, 100 uncached dense pages contain 12.5 MiB and require at least **6.25 seconds** of payload service for one peer. Eight such joins contain 100 MiB in aggregate, requiring at least **25 seconds** at a 4 MiB/s global budget. These lower bounds exclude all other work and overhead; compression may reduce them, while shared bandwidth and scheduling increase delay. They explain why near-first staging and measured encoding matter.

Start with exact float32 state and the existing sparse/dense encoding responsibility. For final density pages, sparsity is relative to an identified baseline or exact repeated value, not “density equals zero”: zero is the surface. A page proven wholly solid/air is not necessarily constant-valued, and replacing it with an arbitrary constant can change a neighboring surface or later edit.

Evaluate lossless compression independently per page or small bounded group. LZ4 and Zstandard are reasonable benchmark candidates from their primary projects; published generic throughput is not a Voxels3 result and their native libraries are not verified s&box dependencies. [LZ4 project](https://github.com/lz4/lz4), [Zstandard project](https://github.com/facebook/zstd).

Measure total cold/warm pipeline cost on the same captured terrain: encode/decode p50/p95/p99, bytes, allocations, CPU, contention and client install time. Disk and wire may choose different codec settings but must decode to the same canonical values. Avoid lossy density quantization until geometric error, contact, seams and edits have explicit acceptance criteria. A 16-bit format halves raw density size; it does not automatically preserve topology.

The storage break-even question is:

`expected revisit count × (generation cost − load/decode cost) > initial encode/write cost`,

subject to positive saved time, disk budget, memory and latency limits. Evaluate generation regions and shared macro work, not only an isolated noise call. Save expensive generated output when this is beneficial; retain an explicit policy for evicting only safely reconstructible unedited cache data. Edited authoritative records are never garbage-collected merely because no player visits them.

## 11. s&box feasibility and integration gates

Official s&box documentation says ordinary `System.IO.File` access is restricted and supplies `FileSystem.Data` through a virtual filesystem. Therefore a native database library that expects arbitrary file handles/paths is not automatically usable in game code. [s&box filesystem](https://sbox.game/dev/doc/assets/file-system/).

Installed XML evidence in this session identified `Sandbox.BaseFileSystem.OpenWrite(string, FileMode)` for build metadata `26.09.01c`, `33901499107`, `build-pr`, `handsomematt`, `04/09/2026 17:37:31`. The current save source uses it. A bounded XML search found no SQLite match; **that does not prove SQLite is unavailable**. No compiler or runtime test established a database binding, random-write contract, durable flush, atomic rename/replace, or an external-service integration for this research.

Before choosing the backend, prove through supported game APIs: package/whitelist access, stream seek/read/write semantics, actual transaction durability, failure reporting, thread restrictions and packaged dedicated-server persistence location. If using a service, additionally prove authenticated server access, bounded requests, idempotency and outage behavior. Keep its credentials on the authority side.

Official RPC documentation provides host routing, host-only flags, caller identity and recipient filters; reliable RPC is the default. These support the existing terrain transport's structure, but do not establish bulk throughput, packet limits or readiness semantics. [s&box RPC messages](https://sbox.game/dev/doc/networking/rpc-messages).

Use real host/client sessions to establish payload envelopes, supported array serialization, relay behavior and interference with player traffic. Keep server gameplay collision independent of the host's visual viewer. No proposed database call, raw socket or compression package is presented here as a verified engine API.

## 12. Smallest complete implementation sequence

1. **Integrate existing edits into durable regional paging.** Preserve today's field semantics and validated tool entry points. Evolve their field/store and replication owners into the single chunk lifecycle: index persisted correction pages, load before mutation, save revisions transactionally, evict safely, and make edits/rejoin/movement use that same state and publisher. Remove the superseded retention, save/load and delivery paths as they are replaced; export snapshots may only read the same store for backup.
2. **Qualify the storage backend and recovery.** This belongs in the first slice, not after calling it durable. Prove interrupted writes, disk full, restart, cross-page edits, idempotency and backup restore using the production path.
3. **Generation strategy qualification.** Compare shared deterministic baseline plus authoritative changes against supplied samples using fixed cold/warm and disjoint-player workloads. Measure server CPU, network traffic, client generation/frame cost, time to visuals, time to safe gameplay and mismatch frequency separately. If baking wins, specify its field semantics and migration before changing representation.
4. **Hierarchical network terrain.** Extend the existing manifest/page transfer to identify reconstructible baselines, complete edit coverage and authoritative replacement/LOD data within one protocol. Partition bounded install groups around actual mesh/collision dependencies; preserve stale-job rejection and invalidation of persisted coarse caches.
5. **Scale with evidence.** Add measured prefetch, warm caches, compression and peer fairness as required. Do not begin with distributed world sharding, a custom transport, or a speculative storage framework.

These are sequencing recommendations, not permission to leave an advertised persistence feature without failure recovery. Each accepted shipping slice must have its own complete contract and measurable result.

## 13. Validation required before acceptance

No runtime scenario was executed for this documentation-only task. Before implementation runs, freeze exact scenario parameters and numeric thresholds in [ValidationResults](../ValidationResults.md), following [performance/testing rules](../AgentRoutes/performance-and-testing.md). Keep the canonical figure-eight unchanged and compare against its latest accepted comparable baseline; do not reuse unresolved deformation acceptance as a pass.

The following are scenario requirements, **not pre-approved executable scenarios or measured results**:

| Scenario family | Fixed parameters to record | Required evidence |
| --- | --- | --- |
| Cold discovery and warm revisit | World/recipe, route, seed, player speed, radii, cache state, duration and hardware | End-to-end readiness p50/p95/p99; disk hits; generation jobs; repeat expensive generation avoided where cached |
| Eviction/reload | Exact edits, pages, travel path, memory limit and grace period | Values/revisions preserved; resident bytes plateau; no dirty data lost; same negative-coordinate boundaries |
| Unified edit/chunk lifecycle | Existing dig/build entry points; a boundary-spanning edit; recipient inside/outside interest; unload/re-entry/restart sequence | One authoritative transaction, one state publication route, saved edits present through normal loading, no replay or double application, no second mutable field/store |
| Join under editing | Edit sequence/timing, initial player positions, peer count and bandwidth/latency/loss | Coherent field hashes; transaction visibility; time to safe play; peak staging/host retention |
| Concurrent overlap/disjoint players | Separate fixed 1/8/32/64-player cases, routes and interest settings | Shared-job reuse versus worst-case disjoint memory; per-peer service and starvation; aggregate bandwidth |
| Save/load failures | Committed edits, interruption phase, process-stop or storage-failure method, recovery expectations | Exactly the promised committed state; no half cross-page transaction, regeneration over corruption or duplicate reward |
| Cache/reconnect | Exact world/epoch/version changes, eviction and stale content | Reuse only after server validation; bounded repair; no trust escalation |
| Client baseline generation | Exact recipe, field format, hardware/backend, regions, server queue conditions and concurrent edits | Matching shared samples; no stale generation erases edits; bounded mismatch repair; separate visual latency and authoritative readiness |
| Sustained generation overload | Fixed disjoint exploration rate, generation concurrency, cache state and prediction lead | Bounded queue/memory; explicit progression limits; no false claim that provisional visuals increase server throughput |
| Boundary/LOD edits | Face/edge/corner cases, negative origin, exact tool sizes and enabled levels | Fine/coarse consistency; normals and transition seams; stale disk LOD rejected |
| Teleport and fast travel | Fixed source/destination, maximum speed and network conditions | No fall-through or unknown-terrain gameplay; bounded failure and useful loading status |
| Invalid input and stalled peer | Fixed oversized/truncated payloads, duplicate IDs, illegal edits, stalled ACK behavior | Rejection before unbounded allocation; server state unchanged; other peers still progress |
| Long-lived storage | Fixed explored/edited dataset, checkpoint/compaction policy and restore point | Bounded startup/recovery, disk amplification, checkpoint tails and successful restored world |

Universal correctness criteria can be fixed now: zero accepted half-transactions; zero client-authoritative terrain changes; zero lost edits that were reported durable; zero publication of stale versions; bounded declared memory/queues; exact canonical payload agreement at acknowledged revisions. Visual/collision equality must be evaluated under the selected field contract, not inferred from matching hashes alone.

Acceptance also requires a source/data-flow inventory showing one owner for each responsibility above, deletion of superseded paths, and runtime evidence that the existing multiplayer editing tool reaches the same region store used by loading/unloading. A new streamer passing isolated load tests while the old deformation store still operates independently is not completion. The implementation architecture owner must document the resulting ownership; this research alone does not establish that it exists.

Freeze performance thresholds against actual hardware and comparable baselines before running. Report frame pacing/tails, chunk readiness, allocations, CPU and GPU memory, process memory, network bytes, storage latency and failures separately. “No exceptions,” startup success, or a small successful save/load does not establish multiplayer scalability or crash durability.

## 14. Evidence strength, unresolved decisions and research limits

| Decision/claim | Evidence strength | Remaining gate |
| --- | --- | --- |
| Server load-before-generate and bounded per-client transfer | Strong primary implementation evidence | Voxels3 scheduling and runtime capacity |
| Saving expensive generated blocks is a practical design | Explicit first-party Voxel Tools documentation | Actual generation/load break-even |
| Shared baseline plus authoritative changes is a practical hybrid | Explicit dated Godot Voxel guidance, Voxel Plugin documentation and historical Space Engineers source | Voxels3 determinism, edit coverage, LOD and reconciliation |
| Client generation reduces server generation cost | Conditional architectural inference | Only when it removes server work, such as visual-only evaluation; independently checking the same full chunk does not remove that cost |
| Games routinely repair arbitrary generation mismatches using small deltas | **Not established** | Inspected examples establish related reconstruction/change techniques, not this exact protocol |
| Current Voxels3 corrections and snapshots need real paging | Current source evidence; no runtime capacity claim | Re-audit changing working tree and implement complete lifecycle |
| Materialized sampled terrain can avoid repeated base generation | Architectural inference | Explicit field-format migration and geometric acceptance |
| Fine-volume streaming is infeasible at far visual radii | Exact payload arithmetic | Measured sparse/coarse coverage and compression |
| SQLite is a strong embedded candidate | Primary transaction/durability docs and two voxel references | s&box integration, bundled version and real failure recovery |
| Transport will support desired player count | **Not established** | Real relay/dedicated sessions, bandwidth and fairness tests |
| A named engine is “fastest” or solves all smooth multiplayer needs | **Not established** | No comparable benchmark; not needed to select responsibilities |

Research used bounded searches for Luanti map storage/emergence, Voxel Tools streams/multiplayer/SQLite, Minecraft/Anvil primary implementation evidence, Factorio map transfer, Voxel Plugin replication limitations, SQLite WAL/durability/backups, s&box filesystem/RPCs and lossless compression candidates. The client-generation follow-up on 2026-09-07 re-read the explicit Godot hybrid and Voxel Plugin generation guidance and inspected Keen's archived source at the pinned revision above. These findings revise the earlier preference for universally server-supplied samples; no runtime behavior or acceptance changes. Follow-up checked consequential source claims against original code. A Microsoft Learn actor-storage page was inaccessible through the web tool; no consequential claim depends on it.

Sources are living documents unless a version/date/commit is specified. All were accessed 2026-09-07. Relevant dates include Factorio's 2016 post, the 2023 iteration on Voxel Tools' multiplayer page, Voxel Plugin's explicitly separate 1.2/2.0p7/2.0p8 pages, and the pinned GitHub revisions above. Local engine evidence is an installed XML snapshot, while the helper's online API snapshot was dated 2026-09-06; neither substitutes for packaged game validation.

Discovery stopped after the major design choices had primary support, contradictory support claims were version-qualified, and remaining gaps required engine/runtime measurements rather than more generic searches. No production-scale performance, power-loss test, full external code review, or source-code reuse/license approval is claimed. This is a design research document; implementation decisions must be adopted in the relevant architecture owner before coding.
