# Simple voxel physics and terrain stability

Date: 2026-09-07. Audience: Voxels3 gameplay and engine development.
Status: research and proposed design only. No runtime implementation, benchmark, or performance acceptance is established here.

## Recommendation

Use **one material-driven, rooted support graph**, updated after terrain edits and processed within a shared work budget. This is the selected research direction, subject to the correctness and performance gates below. Each analyzed terrain cell participates in the same support calculation. A floating scrap has no support path; an excessive pillar or overhang exhausts the support transmitted along that path. All failures enter the same existing terrain mutation boundary.

Keep terrain static until that solver confirms failure. The initial response is bounded removal with dust or non-colliding falling flecks. Fragment size changes the amount of presentation, not the failure algorithm. Physical debris is an optional later presentation capability and must not become another terrain-stability solver.

The user's clarification on 2026-09-07 requires one solver, one derived graph/state owner, one dirty-work queue and budget, one failure criterion, and one collapse commit path. Connectivity traversal, material lookup, and removal preparation are operations inside that system, not independently scheduled systems with their own competing decisions. Do not first ship a separate island-cleanup implementation and then layer dirt, cave-in, and stress solvers over it.

Material strength and stability parameters belong in shared **material definitions**, looked up by the voxel's material ID. Do not store copies of those numbers in each voxel. The user's clarification on 2026-09-07 establishes this as the preferred ownership model; per-voxel overrides would require a concrete gameplay need and supporting evidence.

The examples below supply evidence about boundary handling, material rules, scheduling, and alternatives. They are not a list of subsystems to combine. Select the rooted support model because it gives the requested behaviors one decision path; do not describe it as a proven best-performing implementation before measuring it. If it cannot meet the required gameplay, replace the selected solver at the design gate rather than accumulating corrective solvers.

**A nearby-neighbor count alone cannot meet the request.** A floating cluster can give every member plenty of neighbors. A dirt pillar can remain vertically connected forever. A vast roof can still touch rock around its perimeter. The same rooted support calculation must distinguish these cases through path availability, material properties, and contact geometry.

## 1. What the player should experience

| Situation | Proposed behavior | Required analysis |
| --- | --- | --- |
| Mining leaves a tiny floating scrap | Remove it after a short delay; dust, optional aggregated resource drop | Prove the entire fragment is disconnected |
| A one-cell-wide dirt pole grows upward | Upper dirt crumbles; height and lateral confinement matter | Finite support with material/contact-dependent transmission |
| Dirt extends sideways from a cliff | Short reach only; unsupported growth collapses | Material-specific lateral support |
| A broad dirt hill is built | Supported slopes remain possible | Wider bases and lateral confinement must improve stability |
| Mining widens a stone chamber | Small spans survive; excessive spans shed roof material in stages | Rock support distance and roof geometry |
| A large island loses its last connection | Schedule failure even if it crosses chunks or lies partly outside view | Cross-region connectivity and invalidation |
| Several players cause simultaneous cave-ins | Collapse responds more slowly while rendering stays responsive | Global work, memory, mutation, and debris limits |

Treat delay as part of the design: dust or cracks can signal a confirmed pending failure. Do not signal a specific collapse while analysis is merely incomplete. The user's allowance for disappearing material makes persistent rubble and exact falling masses optional, which substantially reduces the initial integration scope.

## 2. Current Voxels3 constraints

Source audit: clean working tree at `c72444d0bba187c5af8d9ff96282b9762c8ddf77` on `codex/terrain-deformation`, inspected 2026-09-07. Later source revisions must be rechecked.

- [TerrainField](../../Code/Voxels/TerrainField.cs) owns immutable snapshots, correction pages, and version-checked commits. Sample spacing is 16 world units; pages hold 32 cubed samples. Canonical density adds trilinear corrections to the procedural field.
- [Foundation](../Architecture/VoxelChunkFoundation.md#spatial-contract) records 32 cells per chunk, 512-unit chunk width, 33 logical samples per axis, negative-solid/positive-air density, and zero at the surface. Use project units; do not silently equate a cell or world unit with a meter.
- [Deformation entry point](../../Code/Voxels/VoxelManager.Deformation.cs) exposes host-only `TryQueueTerrainEdit`. Its result means queued, not committed. A successful commit invalidates GPU and collision derivatives. The existing brush is radial density modification, not an arbitrary detached-component removal operation.
- [Collision source](../../Code/Voxels/VoxelCollisionWorld.cs) creates static mesh bodies and retains old collision until replacement succeeds. This makes collision publication part of collapse correctness, not merely a rendering concern.
- The inspected [VoxelChunk material query](../../Code/Voxels/VoxelChunk.cs) already exposes a material ID, currently derived as Air/Grass from density. Reuse that material identity for physics lookup. The missing part in this source snapshot is distinct dirt/stone/wood definitions and their selection, not per-voxel strength storage. If another material system has since been added, use its canonical IDs and definitions rather than introducing a competing registry.
- Regional terrain replication and persistence code already exist. [Deformation status](../Architecture/TerrainDeformation.md) still records incomplete acceptance gates. Do not describe networking as absent, or assume existing multiplayer acceptance covers collapse.
- Gameplay interest has no fixed world-Z foundation plane. A protected bedrock horizon would be a new world-design decision, not an existing anchor.

The existing density/material query supplies inputs to the single solver. Completing dirt and stone stability also needs shared material physics definitions, placement semantics, and a support policy. Reuse existing material queries and persistence wherever available; only newly introduced material identity changes need additional saved/networked representation.

## 3. Games and projects worth learning from

### Godot Voxel / Zylann: strongest direct smooth-terrain fragment reference

The documented `separate_floating_chunks` operation detects components entirely inside a supplied box, removes them from terrain, and creates rigid bodies with convex collision. Boundary-touching pieces are excluded because attachment outside the box is unknown. The API returns the bodies so callers can add lifetime behavior. [Voxel Tools API](https://voxel-tools.readthedocs.io/en/latest/api/VoxelToolLodTerrain/)

Pinned implementation evidence shows negative SDF samples used as solid, boundary rejection, and a 0.2-second frozen interval before enabling dynamics while terrain collision catches up. This is a timing workaround, not a readiness guarantee. [Floating chunk implementation](https://github.com/Zylann/godot_voxel/blob/2a276e180f2d59650566c52a68d2f2e364ff2329/edition/floating_chunks.cpp#L157)

The island finder connects face neighbors through its scan and has a 256-entry label/equivalence capacity with a crash condition on exhaustion. This limits intermediate labels, not a promise to support exactly 256 final fragments. [Island finder](https://github.com/Zylann/godot_voxel/blob/7fa532c407b44a8c9bdbe3840a7171e696c614a9/util/island_finder.h#L21)

**Transfer:** use bounded connectivity and explicit uncertainty. For Voxels3, wait for collision revision readiness rather than copying a fixed timer. Do not copy an assert-on-fragment-overflow policy. This system establishes detachment, not whether an attached roof is structurally strong.

### Vintage Story: strongest source-visible local cave-in example

Anego Studios' rock behavior runs collapse decisions on the server and gates them on cave-in/falling settings. Breaks and explosions trigger nearby checks. Its support search has a six-block horizontal distance bound and examines solid-face connections; vertical support examines up to four blocks below. Collapse selection has random extent/count and a twelve-block horizontal bound. Layers are scheduled 200 milliseconds apart and spawn falling-block entities. These are facts about the pinned file, not universal settings for every release or material. [Unstable rock source, 2026-04-03 revision](https://github.com/anegostudios/vssurvivalmod/blob/8eb9552972540749393fa7fa8206d28f582e8dca/BlockBehavior/BlockBehaviorUnstableRock.cs#L187)

**Transfer:** local support, material configuration, server decisions, and staged collapse are well matched to mining gameplay. Do not transplant its radii into 16-unit smooth terrain. A delayed layer can still contain many blocks: scheduling layers is not itself a hard per-frame work bound. Prefer deterministic failure thresholds initially; randomness can later vary presentation or server-recorded events.

### TerraFirmaCraft: clear separation between triggering and propagation

In the pinned 1.21.x `CollapseRecipe`, mining-trigger checks require server execution and a loaded surrounding area. Configured probabilities select actual versus warning-only collapse. The initial search box is at most 9 by 5 by 9 blocks; this is not the bound on the entire ensuing collapse. Follow-up positions go to a world tracker. Once a collapse begins, support is ignored by that propagation stage. [CollapseRecipe source](https://github.com/TerraFirmaCraft/TerraFirmaCraft/blob/a45b81f9f22e2d9af79f5050bf0025697ea5b990/src/main/java/net/dries007/tfc/common/recipes/CollapseRecipe.java#L73)

The versioned 1.20.x support documentation likewise distinguishes preventing a collapse from starting from stopping a collapse arriving from elsewhere. It exposes configurable vertical and horizontal support distances. [TFC support definitions](https://terrafirmacraft.github.io/Documentation/1.20.x/custom/#supports)

**Transfer:** distinguish the cause of work from its resulting presentation. **Recommended departure:** use the same support solve before and during collapse. Each committed removal dirties that same graph; subsequent batches require fresh failure results rather than an independent propagation rule that ignores support. The 1.20 documentation and 1.21 source are separate version evidence, not one frozen build.

### Luanti: simple falling material, useful lower-complexity comparison

Luanti's built-in falling code tests the `falling_node` group, checks the block below, converts eligible nodes to entities, and attempts to place them again on landing or produces drops. It explicitly avoids starting that fall when the below-node is not loaded. Its neighborhood traversal uses ordered direct neighbors. [Luanti falling implementation](https://github.com/luanti-org/luanti/blob/master/builtin/game/falling.lua)

**Transfer:** explicit material eligibility, unloaded-state handling, and a defined landing lifecycle. Below-only support does not prevent arbitrarily tall dirt columns. One entity per node also offers no useful upper bound on a large cave-in's physics load. Treat this as a simple reference, not the complete design.

### 7 Days to Die: strong gameplay comparison, limited algorithm evidence

The developer's store description explicitly includes buildings and terrain collapsing from structural damage or poor construction. This is a close product match for mining plus construction stability. [Developer feature description](https://store.steampowered.com/app/251570/7_Days_to_Die/)

The inspected primary material does not expose the current solver, support constants, scheduler, or measured costs. Community claims about exact span formulas and free vertical support were discovery leads, not evidence adopted here. Use the game to frame desired behavior; do not describe a guessed algorithm as its implementation.

### Teardown: useful destruction and multiplayer lessons

Dennis Gustafsson's March 2026 account describes deterministic destruction commands delivered in order, with separate state synchronization for object transforms and velocities. It also describes bandwidth failure in an earlier approach that sent altered voxel data, and a bounded replay history for joining games. [Teardown multiplayer technical account](https://blog.voxagon.se/2026/03/13/teardown-multiplayer.html)

**Transfer:** permanent terrain change and moving debris have different synchronization needs. Voxels3 should retain its existing canonical state protocol; copying Teardown's command replay would require proving our floating-point SDF operations deterministic. This source does not establish Teardown's exact connectivity solver, material stress rules, or a performance budget transferable to a streamed SDF world.

### NVIDIA Blast: the stronger structural model

Blast's documented stress solver works on a support graph, applies internal forces, and fractures overstressed bonds. Iteration and graph-reduction controls trade computational work against structural detail. The pinned header exposes `bondIterationsPerFrame` and `graphReductionLevel`. [Blast stress header](https://github.com/NVIDIAGameWorks/Blast/blob/9f4fc41dc5d857e3c7c3500fc71953e54d780a39/sdk/extensions/stress/include/NvBlastExtStressSolver.h), [official UE4 integration explanation](https://nvidiagameworks.github.io/Blast/1.1/authoring_docs/BlastUe4_QuickStart.html)

**Transfer:** use this as the alternative when accumulated load, thin supports, and bending matter enough to justify a solver. Its chunk/bond representation is not our mutable SDF. Continuously rebuilding bonds, deriving masses, and integrating native code would be substantial new work. The old UE4 integration is historical evidence, not an s&box package recommendation.

### Voxel Plugin: an important version trap

Current Voxel Plugin 2 migration documentation explicitly says legacy voxel physics is absent. Current sculpting documentation also says detecting and dropping unsupported pieces is unsupported. [Migration guide](https://docs.voxelplugin.com/getting-started/migrating-from-legacy), [runtime sculpting limits](https://docs.voxelplugin.com/knowledgebase/blueprints/runtime-edits-and-sculpting)

Do not choose the current plugin expecting a ready-made collapse system. Legacy examples remain research material, not proof of current availability.

## 4. Algorithm choices

The following assessment is engineering inference for Voxels3, not external benchmark data.

| Approach | Handles well | Does not solve | Decision |
| --- | --- | --- | --- |
| Count nearby occupied cells | Local crumbling hints | Floating clusters, support to ground, load | Never the only test |
| Downward falling / slope rules | Loose dirt, gravel, sand | Stone roofs, global detachment | Reject as an additional material-specific solver |
| Connected components / flood fill | Floating fragments | Attached but weak terrain | Traversal operation within the selected graph, not a standalone cleanup system |
| Rooted support score with directional loss | Limited height, overhang, material span | Accumulated load, realistic arches or bending | Selected single solver; geometry/material transfer must pass the scenario gates |
| Shared load/capacity graph | Overloaded narrow necks and foundations | Cheap implementation and easy parallel updates | Replacement candidate if required; never an additional failure solver |
| Per-voxel rigid bodies / bonded bodies | Rich motion after failure | Predictable worst-case work at terrain scale | Reject for initial terrain |
| Finite-element / continuum stress | More physical deformation and failure | Small implementation scope | Outside this request's first design |

A support-score solver can look convincing without being a weight solver. In particular, a wide platform may pass distance rules even though its total weight should overload one pillar. If that specific behavior becomes required, reconsider and replace the support model before acceptance. Do not add a separate load checker to compensate. Robustness here means consistent decisions, explicit limits, bounded work, and correct invalidation; it does not establish engineering realism.

## 5. Proposed analysis representation and support rules

### Material properties versus calculated support

| Information | Owner | Storage policy |
| --- | --- | --- |
| Which material occupies a location | Existing canonical material identity | Reuse the material ID; do not duplicate it for physics |
| Strength, cohesion, mass density, directional support loss, and collapse behavior | Shared definition for stone, grass/soil, wood, etc. | Store once per material type; all matching voxels use the same definition |
| Whether this particular location is connected and sufficiently supported | Analysis of material definitions plus current surrounding terrain | Compute when affected; retain temporary job state or bounded derived caches only where useful |

Two stone locations have the same material strength but can have different calculated support: one rests on a broad foundation, while the other hangs over a mined chamber. In the formula below, material transfer cap and directional loss come from the shared definition; the support value is a calculated result, not a saved voxel attribute.

No evidence gathered here establishes a benefit from duplicating fixed material properties in every voxel. Cached support may avoid repeating expensive traversal, but its value must be measured against memory and invalidation costs. Independent persistent damage, compaction, moisture, or reinforcement could justify location-specific state in a later feature; none is required for this proposal. Shared definition changes must invalidate affected analysis and use a consistent rules version across host, clients, and saved worlds.

### Derived grid, independent of visual LOD

Start analysis at the canonical 16-unit terrain scale near edits. Use sparse region records only where needed. Occupancy, component labels, support scores, and dirty flags are disposable derivatives of versioned density/material state. They must not become a second mutable terrain world.

A fixed analysis scale prevents a distant visual LOD from changing whether a roof stands. Coarsening from 16 to 32 units reduces uniform grid element count eightfold, but can erase a one-cell rock neck or close a tunnel. Consider coarse regions as acceleration summaries only after near-boundary refinement is defined.

Smooth terrain makes connectivity approximate. Negative sample adjacency is an inspectable starting point; it is not proof of connectivity of every interpolated or triangulated surface. Define zero handling, face contact, minimum effective neck width, and ambiguous mixed-cell refinement beside the canonical analysis classifier. A diagonal corner touch should not carry a mountain.

Use conservative classification: unresolved thin connections become **unknown**, not automatically severed. A gameplay minimum support thickness may deliberately reject thin visible necks, but should be documented as a rule and communicated through preview.

### One graph traversal and failure result

After a committed removal or addition, merge the changed samples and their interpolation dependencies into the solver's dirty queue. Traverse the affected support graph with a resumable frontier, tracking the dependencies needed to resolve root reachability and support. A single result contract reports supported, failed, or unresolved for the analyzed region and source revision.

- Complete affected solve finds no supporting root: support is zero and the ordinary failure criterion applies, including to tiny scraps.
- Search reaches a valid root or current boundary support: continue the same material-weighted support calculation; connectivity alone is not a pass.
- Search reaches unloaded state, region boundary without a valid summary, node quota, or label limit: unknown; preserve it and schedule continuation.

Never delete every component except the largest. A legitimate detached boulder can be larger than a nearby anchored column.

A small local box can bound one work batch, not define a second cleanup policy or the limit of structural influence. Use resumable cross-region traversal in the same graph. Consider region summaries only if measured traversal cost requires them; they must preserve the same decisions and multiple connections within a chunk. Do not maintain an independent coarse stability simulation. One occupied/not-occupied flag per chunk invents connections.

Deletion can split a component. An insertion-oriented union-find cache alone cannot repair that. Rebuild affected local labels and invalidate downstream reachability. Search can remain linear in the affected component in the worst case; spreading work over frames changes latency, not total complexity.

### One material and geometry transfer rule

Use a finite support reserve propagated from explicit roots for all analyzed terrain. One candidate rule is:

```text
support(root) = configured root reserve
support(cell) = max over valid neighbors:
    max(0, min(support(neighbor), transfer cap(materials, contact geometry))
        - loss(materials, direction, contact geometry))
failed(cell) = completed solve and support(cell) <= shared failure threshold
```

This is proposed gameplay mathematics. Upward loss must be positive for dirt, or a vertical dirt pole has no height limit. Horizontal loss should be substantially larger for dirt than stone. No non-root cell replenishes support simply because it touches other cells.

Use deterministic ordering/ties and positive loss along every possible cycle. After support removal, invalidate affected dependency chains and recompute from verified roots; do not keep circulating old cached values. Defer failure until the affected solve is complete for its source revisions.

Material definitions parameterize this same transfer rule. Contact geometry may account for contact width and local terrain thickness, so thin exposed dirt and broad bases transmit support differently. These are inputs to the same calculation, not a separate slope or dirt-collapse pass. Geometry alone must never seed support; even a thick disconnected cluster has no root. Keep transfer inputs fixed for the versioned solve, avoiding a second iterative confinement simulation.

The exact geometry-to-transfer function remains an implementation design gate. The simple maximum-path formula does not automatically produce convincing slopes or weight distribution. It must demonstrate both a failing narrow dirt pole and a surviving broad mound under one rule before acceptance. If it cannot, revise the one transfer model or select the load/capacity alternative; do not add exceptions that independently trigger collapse.

**Foundation policy is an implementation gate.** Preferred direction: geological roots with revisioned connectivity certificates, backed by a documented world-generation foundation rule. Player-added dirt and stone cannot create roots. A root certificate must name the terrain dependencies that justify it and be invalidated when mining cuts them. Unloaded chunk edges and all unedited rock are not automatically roots.

A protected bedrock horizon is simpler but changes the present unrestricted-Z gameplay contract. Treat it as a product alternative, not a silently adopted shortcut. Root verification belongs to the same graph and invalidation owner. If the foundation policy cannot be made correct and affordable, the unified design remains unaccepted; do not introduce a separate local cave-in fallback to hide the gap.

### Stone roofs and natural caves

Stone should transmit support farther laterally than dirt, with failure beginning at insufficiently supported roof regions. Recompute as the chamber widens or pillars disappear. Initially support distance and thickness are gameplay approximations; they do not reproduce arch mechanics.

Generated caves must not all self-destruct on first view. Select a policy before implementation: generation produces stable cave spans under the chosen rules, or generated geology carries explicit stability assumptions with dependencies invalidated by edits. The preferred long-term choice is compatible generation and support rules. Blanket immunity for procedural material would preserve mined-out floating islands.

Built beams, if later introduced, contribute through the same support graph and require supported endpoints/foundations. They cannot supply infinite strength merely by existing nearby.

## 6. Committing collapse and showing falling pieces

Use the existing manager-owned ordered mutation boundary. Extend its preparation inputs to describe the **exact failed component or bounded failure region**, instead of approximating an irregular fragment with a large sphere that can destroy neighboring supported rock.

SDF removal needs more than flipping a few sample signs. Define a bounded removal field/mask and interpolation halo that fully clears the failed geometry while preserving nearby retained surfaces. Changing shared samples may alter both sides of a seam. Validate the resulting real render and collision surfaces; no new validation-only mesher is warranted.

Recommended default lifecycle:

1. Analyze immutable density/material snapshots off the engine thread.
2. Produce a bounded proposed failure with source dependencies and reason.
3. Revalidate at the ordered host commit boundary; reject stale work.
4. Commit terrain removal and invalidate affected render/collision consumers.
5. Publish dust and bounded cosmetic fragments in coordination with the removal.
6. Enable colliding debris only after the relevant old terrain collision is gone and replacement support is ready.

A delay budget does not replace step 6. Otherwise a newly falling piece intersects the collider that still represents its old position.

All sizes use the same bounded removal operation. These are presentation options for its confirmed output, not three collapse implementations:

| Tier | Suggested role | Lifetime and authority |
| --- | --- | --- |
| Dust / non-colliding flecks | Tiny scraps and overload fallback | Client presentation; no gameplay collision |
| A few simplified physical pieces | Nearby medium collapses | Host authority if damaging or interactable; capped lifetime/count |
| Progressive terrain removal | Large cave-ins and islands | Bounded canonical edits; optional pooled presentation |

The initial selection is removal plus cosmetic dust/flecks for all sizes. Cosmetic pieces do not support terrain, deposit material, or cause further terrain damage. Each removal can expose new structural failure, but it does so only by queuing another update in the same solver. This permits a physical chain reaction without introducing a chain of different systems. Physical debris and persistent rubble are outside initial acceptance.

A large collapse should not require a giant movable concave terrain mesh or immediate convex decomposition. A simple hull may bridge empty concavities; choose small bounded shapes or cosmetic pieces where accuracy is unnecessary.

If persistent rubble is added later, landing becomes a new canonical deposit transaction. Removed volume, inventory drops, and deposits must not duplicate resources. For the first version, disappearing collapsed material with a separately defined resource-yield policy is sufficient. Do not create thousands of collectible entities.

## 7. Frame-rate protection

Godot Voxel's performance documentation explicitly discusses main-thread time slicing, bulk edits, collider creation, and queues retaining too much copied data. Its costs and thread restrictions are Godot-specific; the transferable lesson is to budget the entire derived-work pipeline. [Voxel Tools performance](https://voxel-tools.readthedocs.io/en/latest/performance/)

Proposed initial experiment values below are **unmeasured engineering targets**, not shipped defaults or accepted criteria:

| Resource or latency | Candidate target | Qualification |
| --- | --- | --- |
| Stability worker service | One additional worker initially; resumable batches of at most 4,096 cell/edge visits | Profile contention with the existing collision workers |
| Main-thread stability orchestration | 0.25 ms admission budget per frame | Measure full call durations; budgets cannot preempt native work |
| In-flight analysis snapshots | At most two | Account retained page memory, not just snapshot object size |
| Stability cache and scratch | 32 MiB initial hard allocation target | Evict recomputable completed data; unresolved jobs need bounded continuation state |
| New collapse mutations | At most one admitted batch per logical update | Also bound changed samples/pages and derivative work |
| Colliding debris | Zero in first slice; later test a global cap of 16 and one spawn per frame | Global across players, not 16 per player |
| Tiny scrap response | Target p95 under 250 ms after input stops | Includes field/visual/collision pipeline, not just search time |
| Ordinary cave-in start | Target p95 under 500 ms after a qualifying edit | End-to-end on fixed workload |
| Large collapse | Seconds are acceptable | Record oldest-job age and final completion; no indefinite abandonment |

Limit both elapsed service time and work-item counts. A queue holding ten enormous regions is not necessarily bounded in bytes or cost. Coalesce overlapping dirty regions; preserve aged work so one continuously mining player cannot starve another area.

When demand exceeds capacity: reduce cosmetic debris first, then increase analysis/collapse delay, then apply explicit edit admission backpressure before accepting more work. Never convert a quota hit into "unsupported," discard already accepted terrain edits, or let queue growth consume unbounded memory. Persist or conservatively requeue unresolved affected regions across unloading.

Measure sampling, classification, graph rebuild, queue wait, snapshot memory, mutation preparation, GPU publication, collider publication/retirement, physics contacts, and network transfer separately. Faster connectivity is irrelevant if native collider creation dominates frame tails.

Keep one diagnostic record per solver job: initiating edit ID, affected bounds, source/rules versions, visited nodes/edges, queue wait, compute time, retained bytes, result/reason, and commit/publication times. A failure explanation reports the material parameters and support path used by that same solve. There are no separate dirt, island, cave-in, and structural queues to reconcile. Existing rendering, collision, and replication remain downstream consumers with their existing owners.

Memory arithmetic illustrates why allocation layout matters. At 32 cubed cells, one bit per cell is 4 KiB; one byte is 32 KiB; one 32-bit label is 128 KiB. Across 1,000 dense regions, labels alone are 125 MiB. These are derived sizes excluding halos, containers, support values, and snapshots—not measured project memory.

## 8. Multiplayer, saving, and fairness

Only the host decides stability and commits collapse. Clients may show tentative building warnings but cannot independently remove terrain. Use the existing absolute-state/region transfer design; stability is another cause of canonical edits.

Attach a collapse event identity to presentation and resource effects so retransmission does not duplicate debris or drops. Revalidate if a player repairs a support during delayed analysis. A queued warning should cancel when its underlying failure no longer exists.

Derived support caches can be rebuilt. Material assignment, placed-versus-generated semantics where used, removed terrain, and resource consequences must survive save/load and join. Persist unresolved dirty regions or deterministically reconstruct them; otherwise leaving an area can permanently evade collapse.

Do not replay all historical cosmetic debris for a joining player. Transfer current terrain and only currently relevant gameplay debris. Any damaging physical fragment needs host-owned collision/damage; purely cosmetic fragments can vary by client.

Give active regions a fair share while prioritizing immediate player safety and existing streaming/collision work. A remote player repeatedly editing terrain must not starve the local player's collision readiness. Frame priority should reduce presentation quality and increase latency, not change the structural decision according to who is watching.

## 9. s&box integration evidence

The installed XML snapshot for engine `26.09.01c` documents `PhysicsBody.AddBoxShape`, `AddHullShape`, and body movement types Static, Keyframed, and Dynamic. The `BodyType` comment notes that a networked dynamic body reports Keyframed on the client. This is candidate API evidence, not a compile/whitelist, ownership, performance, or fragment-contact test.

Current static terrain uses `AddMeshShape` in [VoxelCollisionWorld](../../Code/Voxels/VoxelCollisionWorld.cs). Do not assume those static meshes can simply become arbitrary dynamic debris. The project already has a collision worker/publication boundary; reuse its verified ownership model, and verify new shape creation and networking through the real game before accepting physical debris.

No engine changes or live tests were made for this research.

## 10. Implementation sequence and acceptance gates

**Design gate:** settle root policy, topology classification, the single material/contact transfer function, failure threshold, and invalidation/convergence rules. Keep genuine accumulated-load behavior explicitly outside the selected approximation unless a requirement changes. This research selects a direction, not a proven finished solver.

**One complete production slice:** implement the same support graph for scraps, material height/overhang limits, cave spans, and cross-region support. Include one bounded queue, conservative unresolved state, stale-result rejection, one canonical removal path, and cosmetic presentation. Do not add per-voxel strength fields or temporary standalone cleanup code. Develop incrementally within this implementation; use the scenarios below to expose missing behavior rather than implementing another solver for each scenario.

**Acceptance:** verify material cases, region boundaries, unloaded dependencies, overload recovery, save/rejoin, and large-island completion through that same production path, then meet figure-eight and collapse performance gates. None of these becomes a separate simulation just because it has its own scenario.

**Optional later presentation:** physical debris only if it justifies its cost after the unified system passes. Persistent rubble, impact-driven terrain destruction, and additional dynamic material state require separate scope decisions; they are not promised extensions of the initial system. Any necessary future solver replacement removes the superseded implementation in the same change.

Before any runtime implementation run, define exact scenarios and immutable parameter sets in [ValidationResults](../ValidationResults.md), using the existing real-world entry points. This document does not invent passed runs or change the accepted figure-eight workload.

Required scenario families:

| Scenario | Fixed setup to record before execution | Observable pass condition |
| --- | --- | --- |
| Tiny scraps | Seed, position, exact tool strokes, fragment size including zero/threshold cases | Confirmed scraps disappear; supported neighbors stay |
| Seam and topology | Face/edge/corner seams, negative coordinates, diagonal and thin necks | No false severing, seams, or surviving phantom collision |
| Dirt construction | Same tool sequence for thin pole, sideways ledge, broad slope | Pole/ledge fail at declared rules; sufficiently supported hill remains |
| Stone mining | Progressive chamber widening, intact/removable pillar, repair during delay | Small supported span survives; designated larger failure resolves; stale failure cancels |
| Large island | Multi-chunk component with one connection and a remote alternate support | Last-support removal propagates; alternate support prevents false detachment |
| Unloaded boundary | Same edits with support initially outside residency, then revisit | Unknown remains safe; eventual result equals loaded case |
| Storm and starvation | Fixed simultaneous edit sequence, player count, duration, queue pressure | Memory caps hold; accepted work drains; streaming continues |
| Join/save/recovery | Fixed mid-collapse joins, duplicate messages, unload/save/reload | Canonical state converges; no duplicated resource effects |
| Debris, if enabled | Exact shapes/counts, actors above/below, collider timing | No activation inside old terrain; bounded bodies and contact cost |

Run the canonical figure-eight unchanged and compare against the latest accepted comparable baseline; capture a pre-change control where needed. Measure FPS, p50/p95/p99 frame time, worst stalls, chunk completion/streaming, peak memory, allocations/GC, and correctness. A pre-change control does not turn the currently incomplete collision acceptance into an accepted baseline.

For collapse scenarios also record input-to-decision, input-to-field-commit, visual/collision completion, queue depth/age, nodes visited, bytes retained, failed/stale jobs, debris count, and host/client convergence. Freeze numerical thresholds, hardware, engine/source revision, and workload before running. Report failures and unresolved regressions; changing workload or accepting regression requires the project's explicit approval process.

## 11. Evidence limits and research provenance

The algorithm recommendation is supported by source-visible examples, but **no inspected source supplies a comparable Voxels3 benchmark**. We cannot honestly promise a frame rate, memory cost, or maximum collapse size yet.

| Evidence family | Confidence and gap | Decision consequence |
| --- | --- | --- |
| Local detached-piece extraction | High for inspected Godot implementation; SDF topology transfer remains unresolved | Boundary-handling evidence for the single graph; no standalone cleanup slice |
| Local mining collapse | High for pinned Vintage Story/TFC source; different block geometry and versions | Adopt bounded triggers/staging, not constants |
| Genuine load support | High that Blast exposes graph stress controls; no integration measurement | Alternative, not first dependency |
| Closed-source game mechanics | Developer confirms 7 Days feature; exact solver unverified | Gameplay reference only |
| Current plugin availability | Current Voxel Plugin docs explicitly exclude physics | Do not recommend VP2 for this capability |
| Material/anchor model | New Voxels3 design; current source lacks needed semantics | Implementation gates, not completed design |
| Performance targets | Proposed only | Require fixed in-world measurements |

Searches covered voxel floating components, local rock/soil collapse, structural support graphs, Teardown destruction/networking, and Voxel Plugin physics availability. Follow-up retrieved actual Godot/Vintage Story code and licenses, TerraFirmaCraft collapse source, Blast stress controls, and installed s&box shape metadata. Highest-impact boundary, delayed-collider, server-trigger, and current-state claims were checked against original files.

Stop reason: the main algorithm families and implementation risks have primary evidence; further broad game lists would not resolve the remaining project-specific material, topology, foundation, and performance decisions. Those now require design choices and experiments. No claim of an exhaustive census or objective "best engine" ranking is made.

Access qualifications: the TFC Field Guide link surfaced in search but returned 404 on direct fetch; its support-volume values were not adopted. An older Voxel Plugin physics URL and two legacy Blast documentation URLs failed; current migration documentation, the working official integration guide, and pinned source replaced them. Luanti and live documentation links are moving references as accessed 2026-09-07.

Source provenance: Godot floating-chunk file revision 2026-03-19; Vintage Story rock file revision 2026-04-03; TFC pinned 1.21.x repository revision 2026-09-07; legacy Blast pinned repository revision 2019-09-17; Gustafsson article published 2026-03-13. Revision dates are not inferred release dates. Undated documentation is identified by version/path and access date.

Public source is not uniformly permissive: [Godot's inspected license](https://github.com/Zylann/godot_voxel/blob/3749ccbe31b0c3e5e1b8de6d2de769dcfc6a83b3/LICENSE.md) is MIT; [Vintage Story's](https://github.com/anegostudios/vssurvivalmod/blob/ac138f05ab0faf6346177997c74a09c2ee3a9f6a/license.txt) is proprietary; the TFC source header identifies EUPL-1.2; [the historical Blast root license](https://github.com/NVIDIAGameWorks/Blast/blob/9f4fc41dc5d857e3c7c3500fc71953e54d780a39/license.txt) contains restrictive NVIDIA terms. This research adopts design lessons, copies no implementation, and recommends no third-party binary integration.

Document verification: relative file links, cited local symbols, table structure, and fenced blocks were checked. No rendered visual review or runtime validation was performed for this Markdown-only change.
