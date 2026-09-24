# The Dead Wake

Living game direction and prototype roadmap. Created September 24, 2026.

**Status:** design and development plan, not a claim that the game described here
is implemented. Prototype branch: `codex/the-dead-wake-prototype`, created from
`main`. The existing technical project identity remains `voxels3` until a rename
is separately qualified; the game's public name is **The Dead Wake**.

## The promise

Wake with nothing in a vast, destructible wilderness. Learn the land, scavenge
its ruined settlements, mine and craft better equipment, and build a home worth
defending. Regular zombie assaults test what you have learned and built. Survive,
repair, venture farther, and return stronger for the next wake.

The world should invite the curiosity and freedom of Minecraft, with smooth voxel
terrain rather than a block-shaped landscape. Seven Days to Die is the main
reference for the survival, technology, scavenging and recurring base-defense
loop. Foundation and socket building takes inspiration from Rust and Project
Zomboid. These are directions from the user, not specifications to copy their
content, interfaces, exact schedules or rules.

## User requirements

- A large, interesting smooth voxel world. Believable terrain is desirable;
  exploration, striking landforms and useful play spaces matter more than strict
  geological realism.
- Mostly realistic, high-fidelity visual quality across environments, characters,
  equipment, construction, effects and UI. Compromise deliberately where gameplay,
  functionality, performance or fun benefits.
- Water simulation, simple gravity for voxel cubes, and mining of ores and other
  resources.
- Start with nothing and progress through equipment, resource and technology
  tiers.
- Use a traditional Minecraft/Seven Days to Die style slot inventory and hotbar.
  Wood comes from standing trees; the Pack-counter and ground-log loop is rejected.
- Soft RPG progression through use: doing an activity makes the character better
  at that activity.
- Build on foundations with a coherent node/socket system.
- Recurring waves of zombies attack the player's base.
- Cities and other settlements with quests, NPCs and traders who send players on
  jobs.
- Pursue implementation through independent critique and iteration, on an
  isolated branch rather than directly on main.

Everything below is a proposed starting design unless explicitly marked as a
requirement or source-confirmed existing behavior. Balance values are provisional
until frozen for a named experiment in the validation ledger.

## Design pillars

1. **An interesting place to live.** Every region should offer recognizable
   landmarks, reasons to settle, resources to seek and risks to navigate.
2. **Preparation changes the outcome.** Tools, routes, fortifications and supplies
   should matter more than a hidden character score.
3. **The world remembers.** Excavation, construction, depletion and damage have
   persistent consequences, owned by the authoritative world.
4. **Learn by surviving.** Skill growth supports the activity the player enjoys;
   it should not turn repetitive safe actions into mandatory homework.
5. **Pressure with breathing room.** Scavenging and building have space to be
   satisfying before a clearly communicated attack tests them.
6. **Readable realism.** Materials and atmosphere feel grounded. Threats, targets,
   build validity and interaction feedback remain legible in motion.

## Core loops

| Timescale | Player decisions | Desired payoff |
| --- | --- | --- |
| Seconds | Observe, move, aim, gather, strike, block, interact | Responsive and understandable actions |
| Minutes | Choose a route, collect scarce supplies, craft a tool, improve shelter | A visible new capability or solved problem |
| One expedition | Prepare, enter a POI or mine, weigh carrying capacity against danger, return | Useful salvage and a story worth remembering |
| One defense cycle | Scout, build, stockpile, defend, repair | Construction choices demonstrated under pressure |
| Many cycles | Specialize, unlock workstations, reach tougher regions, cooperate | New options rather than only larger numbers |

Revised opening: find standing trees and loose stone. Gather a limited supply of
branches from a tree by hand to bootstrap an axe, then chop trees for larger wood
yields. Store materials and tools in a conventional slot inventory/hotbar and craft
through its visible recipe panel. Next establish a small foundation and defensible
entrance before the first announced assault. A tool must never require an ingredient
obtainable only with that same tool. Early failure needs a viable recovery path.

## Gameplay systems to flesh out

### World and exploration

Design a seeded regional world with forests, plains, rocky uplands, valleys,
wetlands, rivers and coastlines. Mix traversable building space with cliffs,
overhangs, caves and memorable silhouettes. A beautiful region also needs routes,
resource contrasts, shelter opportunities and places to investigate.

Settle the playable scale, walking pace and landmark spacing through timed
expeditions. Avoid filling a technically huge world with repeated empty ground.
Start with a bounded region in the existing streamed world; world-size claims
must follow actual generation, streaming and multiplayer measurements.

Questions: how far can players see their next objective; which biomes change
survival decisions; how do roads, bridges and settlements respect terrain; what
prevent cliffs and rivers from making essential opening resources unreachable?

### Resources, mining and tools

Proposed early materials: wood, fiber, stone, soil, scrap and iron-bearing ore.
Later candidates: copper, coal, steel inputs, chemicals and electronic salvage.
Introduce only materials with distinct uses; do not implement this entire list as
an empty catalog.

Gather branches from standing trees and loose stone to bootstrap tools. Random
ground logs are not the wood-progression mechanic. Mine terrain through the existing
canonical edit path. Credit material only after a successful authoritative change,
never merely because input was held or an edit was queued. Decide extraction
quantity and material identity from the removed matter; separate cosmetic debris
from collectible value. Prevent duplicated yield from retries, overlapping tools,
placed-and-dug material, unloaded cells and save/reload.

Tools trade speed, stamina cost, noise and durability. Richer deposits should
encourage exploration and infrastructure, not just repeated digging anywhere.
Define ore distribution, deposit readability, tool requirements and exhaustion
before adding ore-specific rendering or recipes.

### Survival, combat and recovery

First qualify movement, health, stamina, melee reach, enemy telegraphs, hit
feedback, death and respawn. Food, thirst, temperature, illness and injury are
later candidates, introduced only where they create interesting decisions.

Proposed death rule: return to a valid claimed shelter or safe fallback, with a
recoverable inventory container and no permanent skill deletion. This is a design
candidate; test travel burden, repeated-death recovery and multiplayer abuse.
No survival rule should trap a new player in an unrecoverable empty spawn.

### Inventory, crafting and technology

Use a traditional slot inventory with 27 backpack slots and a nine-slot hotbar as
the initial layout. Support stack movement, splitting, merging and hotbar selection.
Use one authoritative inventory with bounded stacks, explicit item definitions
and transactional transfers. Crafting atomically consumes ingredients and adds the
result only when capacity and prerequisites allow it. Cancelled, duplicate or
invalid requests must not create items or eat ingredients. A later timed queue
needs explicit reservation, cancellation and disconnect semantics.

| Proposed tier | New capability | Primary source of advancement |
| --- | --- | --- |
| Improvised | Gather, repair basic tools, make a small shelter | Loose resources and simple recipes |
| Salvaged | Better melee, storage and dependable construction | Riskier scavenging and workbench |
| Forged | Effective mining, stronger defenses and metal tools | Ore processing and fuel |
| Powered | Lighting, traps, automation and advanced crafting | Power infrastructure and specialized salvage |
| Specialized | Alternative high-end survival/defense approaches | Difficult expeditions, mastery and cooperation |

Recipe access, tools, workstations and material supply must work together without
multiple arbitrary gates for the same action. Identify an actual new option for
each tier. Vehicles, large automation chains and industrial simulation are deferred
until the core expedition/defense cycle works.

### Learning by doing

Candidate skills: harvesting, mining, construction, melee, ranged combat, medicine
and scavenging. Improve through meaningful successful actions. Award no progress
for misses, cancelled transactions, empty mining or repeatedly dismantling and
rebuilding the same refunded object. Cap bonuses and prefer modest efficiency,
handling and recipe options over runaway damage multipliers.

Prototype one skill before a full skill tree. Measure time to the first meaningful
improvement, benefit per level, multiplayer fairness and whether normal play beats
grinding. Decide shared XP, death effects and respecialization before persistence.

### Foundation and socket construction

Place a valid initial foundation on supported terrain. Connect foundations,
floors, walls, doorways, doors, stairs and roofs through typed sockets with a
consistent footprint and orientation. Players need a ghost preview, rotation,
snap selection, cost, support feedback and a clear rejection reason.

The host validates reach, support, occupancy, ownership and cost at commit time.
Two requests for one socket cannot both succeed. Building must account for slopes,
terrain excavation, player trapping, enemy collision and objects crossing streamed
region boundaries. Structures have their own durable identity; they are not a
second terrain-density store.

Start with one foundation, one wall and one entrance, one material tier and bounded
piece counts, including costed repairs. Add upgrades, dismantling refunds, claim permissions,
stability and decorations only after placement and attack interaction work.

### Zombies and the wake

The wake is a recurring base-defense event. Its cadence is an open balance
decision; inspiration does not mandate a seven-day schedule. Provide a visible
warning, preparation period, bounded assault, recovery time and result feedback.
First prove a small assault before adding enemy variety.

Zombies perceive players and relevant activity, move over streamed terrain, and
attack reachable targets or obstructing structures. They must not damage a base
through arbitrary range, walk through walls or spawn visibly beside the player.
Use explicit budgets for active enemies, sensing, path requests and spawns.

Resolve how a base is designated, what happens without one, multiple distant
bases, players logging out mid-wave, inaccessible shelters, underground bases,
terrain edits invalidating paths and last-enemy stalls. Proposed offline rule:
simulation pauses when no players are active; validate save/rejoin rather than
assuming wall-clock attacks are desirable.

Candidate escalation combines elapsed cycles, party size and demonstrated
capability within declared caps. Avoid secretly making every upgrade pointless
by increasing enemy strength to match it exactly. Later archetypes must create
distinct tactical questions, not merely larger health pools.

### Water simulation

The existing water presentation is not proof of dynamic fluid simulation.
Desired gameplay includes filling and draining excavations, controlled flow,
flooding and useful water handling. Establish a bounded host-owned active-cell
simulation around changes, with conserved quantities, sleeping stable regions
and a defined unloaded-region policy. These are design candidates, not adopted
engine architecture or promises of full computational fluid dynamics.

Start with a small basin and breached wall in real terrain. Measure conservation,
settling, edit response, cross-region behavior, replication and costs. Decide
infinite ocean sources, rivers, underwater mining, buoyancy and construction seals
explicitly. Read [surface water ownership](Architecture/SurfaceWater.md) and
[water-generation research](Research/SeaLevelWaterGeneration.md) before design.

### Voxel gravity and structural stability

The requested simple voxel-cube gravity is distinct from smooth surface rendering.
Proposed first rule: a bounded set of loose unsupported material cells falls;
stable bedrock does not trigger unlimited world connectivity scans. Detached
simulation cells and their visuals must reconcile back into canonical world state.

Decide which materials fall, support rules, settle/sleep behavior, collectible
debris limits and crush damage. Keep terrain collapse separate from the structure
socket/support graph. Never equate decorative rigid-body cubes with completed
terrain gravity. Read [stability research](Research/TerrainStabilityAndCollapse.md)
before implementation and test undercutting, chains and multiplayer duplicates.

### Settlements, traders, NPCs and quests

Start with one authored rural POI and a trader encounter before procedural cities.
Progress toward hamlets, industrial sites, suburbs and dense urban centers with
readable districts, roads, interiors, loot ecology and navigation budgets.

Candidate jobs: retrieve supplies, clear a site, recover a survivor, repair local
infrastructure and investigate a landmark. Objectives need stable IDs, authority,
party credit, abandon/failure rules, inventory-safe rewards and save/rejoin.
Quest generation must not depend on inaccessible or already destroyed targets.

Traders exchange resources, sell missing essentials and offer directional goals.
Test whether trading supports exploration without replacing mining and crafting.
Define safe zones, NPC invulnerability, stock/restock and currency sinks later.
Protect player builds and belongings from any POI reset mechanism.

### Presentation, sound and accessibility

Use realistic material response, natural foliage, grounded lighting and purposeful
weathering. Preserve clean threat silhouettes and useful contrast. Avoid visual
noise from repetitive surface detail, oversized HUD panels and excessive effects.
Sound should communicate distance, surfaces, mining success, structural damage
and the approaching wake. Motion and combat require actual temporal review.

Create and save concepts before substantial visual production. The planned
reference is [prototype v1](../Art/References/TheDeadWake/prototype-v1.png), with
its [source and direction brief](../Art/References/TheDeadWake/brief.md). A saved concept is art
direction, never a screenshot of implemented play. Track deliberate departures
in its brief. Use readable text, scalable UI, remappable controls and warnings
that are not color-only. Evaluate mouse/keyboard first and record controller
coverage separately rather than implying it has been tested.

## Technical foundation and boundaries

Source inventory at e12bf1f4 on September 24, 2026 establishes the starting point
below. The later opening-loop candidate is tracked under Current progress.
Existing code is not the same as complete feature acceptance; historical limits
remain in the architecture documents and validation ledger.

| Area | Existing evidence | Missing for the proposed game |
| --- | --- | --- |
| Entry and movement | `voxels3.sbproj` selects `scenes/basic_example.scene`; `Assets/prefabs/terrain_player.prefab` uses the engine PlayerController | Survival state, equipment, combat and recovery |
| World | VoxelManager, procedural generation, material catalog, GPU terrain and collision | Game-driven resource ecology and settlement placement |
| Editing | `VoxelManager.Deformation.cs` queues and commits host terrain changes | Authoritative item yield, tool costs and skill rewards tied to outcomes |
| Networking/storage | Existing regional terrain replication and checkpoint owners | Player inventories, structures, NPCs and wave persistence/replication |
| Environment | Water, grass, trees, fog and clouds have source implementations | Dynamic water conservation and terrain gravity qualification |
| UI | Admin menu and biome/debug overlay | Inventory, crafting, build feedback and survival HUD |

There are no dedicated survival inventory, crafting, foundation-building or zombie
subsystems identified in the scoped starting-source inventory. Do not mistake the
existing terrain dig/build brush for survival crafting or socket construction.

Retain one canonical SDF field and mutation boundary. New gameplay owns item,
actor, structure and cycle records; derived meshes and UI consume those records.
All rewards and costs follow host-validated successful operations. Gameplay must
not accept a client-supplied quantity, material identity, cost or hit as truth.
The engine PlayerController remains movement/camera owner.

Use a separate prototype save namespace before gameplay writes any durable data.
Do not overwrite the established terrain checkpoint as a side effect of testing
the new loop. Version saves, define whole-session consistency and recovery, and
qualify migration before claiming persistence. Branch separation alone does not
isolate local save data.

Intended release direction is cooperative host-authoritative multiplayer.
Initial host/offline slices may establish behavior, but guest, late-join,
disconnect, retry and concurrent-action gates remain mandatory before describing
them as multiplayer-ready. The current project metadata's 64-player ceiling is
not a measured gameplay capacity target.

## Delivery sequence

Every row is a playable increment with a real-world exit condition. Later systems
may require revisiting earlier contracts; this is an order of proof, not a promise
to build every subsystem at once.

| Slice | Deliverable | Acceptance evidence |
| --- | --- | --- |
| 0 — Direction and isolation | Branch, this document, saved concept, source/acceptance inventory | Independent accuracy/completeness review and clear next slice |
| 1 — Empty hands to first tool | Traditional empty inventory/hotbar; branches from standing trees and loose stone; axe recipe; useful tree chopping | New spawn can complete the chain without admin grants; misses/retries/capacity cannot duplicate or lose resources |
| 2 — A place to defend | Supported foundation, snapped wall/entrance, costs, repair and obstruction | Place, reject, rotate, connect, walk through entrance; terrain/actor intersections and concurrent placement tested |
| 3 — First wake | Health/stamina, one zombie, combat, announced small recurring assault, death/recovery | Complete preparation/assault/recovery twice; kill, death, unreachable base and no-base cases |
| 4 — A persistent cooperative loop | Save/load of the established loop, player identities, guest interactions and convergence | Restart, late join, disconnect during transactions, invalid/duplicate requests and recovery |
| 5 — Reasons to leave home | Ore mining, workbench tier, one POI, one trader and a complete quest | Expedition produces an advancement; depleted/destroyed objectives cannot strand progress |
| 6 — A responsive world | Bounded flowing water and simple falling-material behavior | Conservation/support, boundary edits, settle/sleep and overload recovery under load |
| 7 — Breadth and polish | Regional settlements/cities, enemy roles, more tiers and soft RPG skills | Repeated complete cycles, content variety, measured budgets and audiovisual review |

First implementation target is Slice 1. Its visual assets/UI must follow the
saved reference; its ownership and budgets must be specified in the narrow
architecture note before code. Do not call Slices 1–3 complete merely because
buttons, counters or placeholder actors exist.

## Investigation and decision backlog

| Decision to resolve | Evidence or prototype needed | Needed before |
| --- | --- | --- |
| Intended party size and world extent | Comparable streaming, network and enemy-load measurements | Multiplayer capacity claims |
| Opening resources and gathering pace | Timed empty-spawn playthrough and resource exhaustion | Slice 1 acceptance |
| Inventory limits and recipe transactions | Full-capacity, cancellation and duplicate-request workflows | Crafting acceptance |
| Building footprint and support | Slopes, sockets, door clearance, collision and undercutting | Slice 2 acceptance |
| Enemy locomotion on changing voxels | Real terrain paths, edits and blocked routes | Slice 3 acceptance |
| Wake cadence and fair escalation | Repeated sessions, preparation duration and defense outcomes | Cycle balance |
| Death/recovery cost | Failed defense and repeated-death recovery | Combat acceptance |
| Skill rewards and anti-grind | Normal-use versus repetitive-action progression | Skill persistence |
| Ore identity and extraction quantity | Material-aware committed edits and conservation tests | Mining rewards |
| Save scope and atomicity | Mid-transaction stop/restart and corrupted-data recovery | Persistent progression |
| Fluid and gravity rules | Small bounded experiments with conserved state and timings | World-simulation rollout |
| POI/city population and resets | Authored site, navigation/load budgets and player-build protection | Procedural settlements |
| Art/audio sourcing | Approved concepts, original/licensed asset provenance and runtime cost | Production asset installation |

## Acceptance and independent iteration

Define each slice's measurable criteria and immutable scenarios in
[ValidationResults.md](ValidationResults.md) before its first runtime run. Use the
actual startup scene, player controls and authoritative paths. Retain failures.
Do not add parallel toy implementations or test-only scenes to stand in for play.

For runtime changes, run the canonical figure-eight against the latest accepted
comparable baseline, capturing a new pre-change control when needed. Record FPS,
frame tails, streaming/completion, memory, allocations and correctness. It tests
travel regressions; additional fixed workflows must qualify crafting, building,
combat, simulation and multiplayer claims. Never quietly change the workload or
relax failed thresholds. Historical subsystem exceptions do not automatically
apply to the new gameplay.

A fresh general-purpose reviewer who did not implement a change inspects the final
sources, requirements, references and raw results. Review includes real workflows,
edge cases, visible quality and relevant performance. Resolve material findings,
then have the same reviewer recheck the fix and affected regressions. Record
pass/fail/unverified/not-applicable coverage and a ready/changes-required/
verification-incomplete decision. Unverified required behavior blocks acceptance,
commit and push of that implementation.

Perfection in a single pass is not an acceptance criterion that can be honestly
proven. The working commitment is complete bounded increments, candid evidence,
independent challenge and continued iteration toward the full game. The ongoing
goal remains open while roadmap requirements remain outstanding.

## Current progress

- Branch created from clean `main`: `codex/the-dead-wake-prototype`.
- Vision, system structure, questions and implementation sequence: recorded here.
- Concept: saved and inspected; illustrative HUD state is documented in its brief.
- Independent documentation/concept review: ready after repair-order and
  line-ending corrections.
- Slice 1 now implements a traditional 27-slot backpack, nine-slot hotbar,
  stack transfers, crafting, and wood gathering from standing trees. It replaces
  the rejected Pack counters and ground-log loop. Native automated playtests and
  independent review cover the replacement's principal interactions and visuals.
  See [opening-loop ownership](Architecture/DeadWakeSurvival.md).
- Slice 1 is qualified as an offline opening prototype after independent gameplay,
  visual, lifecycle and performance review. The final unchanged figure-eight meets
  the existing gates against the predeclared current-environment control. Identical
  original-code controls also showed substantial temporal variation; this qualified
  comparison does not erase earlier failures or establish a feature-caused speedup.
  The ledger preserves all runs, scope limits and the comparison rationale.
- Qualification covers the authored one-oak scene, session-only inventory and the
  measured local player. Forest scale, persistence, co-op, building and defense
  remain later slices; the overall game is still in development.
- Editor display title is The Dead Wake; package identity and paths remain voxels3.
- No acceptance beyond this bounded opening slice is claimed. Exact outcomes
  belong in the validation ledger.
