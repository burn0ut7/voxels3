# Simple water and gas simulation

Date: 2026-09-07. Research proposal only; no fluid implementation, engine
integration test, or performance acceptance is established by this document.

## September 9 sequencing update

The user next requested generated sea-level water using the registered Water
material. [Sea-level water generation](SeaLevelWaterGeneration.md) owns that
new proposed first slice and its cave policy. It puts static surface-water
generation before the finite-fluid prototype below. The transport, conservation,
and networking analysis here remains future-simulation research, not current
implemented behavior or a prerequisite for static initialization.

## Recommendation

Start with finite water amounts on a sparse, fixed-resolution 3D grid and
bounded neighbor transfers. Derive smooth water presentation from that state.
Keep ocean waves as surface animation. Add gas concentrations later using the
same spatial boundary information, but different transport rules.

This prioritizes buckets, filling, draining, caves and editable terrain. It
accepts approximate motion and delayed settling. It does not promise realistic
turbulence, breaking waves, air compression or small droplet simulation.

## Current project evidence

The inspected working tree is on `codex/terrain-deformation` with substantial
uncommitted implementation work. These observations describe that working tree,
not a clean committed release:

- [TerrainField](../../Code/Voxels/TerrainField.cs) owns correction pages,
  immutable snapshots, revisioned commits and 16-unit sample spacing.
- [VoxelManager.Deformation](../../Code/Voxels/VoxelManager.Deformation.cs)
  exposes host-gated `TryQueueTerrainEdit` and commits prepared changes before
  invalidating affected geometry. Fluid boundary invalidation should consume
  the committed field and affected bounds at this boundary.
- [Foundation](../Architecture/VoxelChunkFoundation.md) records 32 terrain cells
  per chunk, 512-unit chunk width and negative-solid/positive-air convention.
  Fluid quantities would be new authoritative state, not another terrain SDF.
- [Networking source](../../Code/Voxels/VoxelManager.Networking.cs) contains
  host/broadcast RPCs. Some architecture scope text still describes transport
  as incomplete. Neither documentation nor API presence proves multiplayer
  acceptance, and no existing fluid protocol is established here.

Installed `Sandbox.Engine.xml` evidence from the sbox skill snapshot, engine
`26.09.01c`, documents `Sandbox.WaterVolume`: trigger-collider integration,
fluid density, linear/angular drag, fluid velocity, wave amplitude/frequency,
surface offset and a surface-plane query. Staging schema dated September 6 also
lists WaterVolume. These are candidate buoyancy/current integration facilities;
they do not demonstrate arbitrary voxel containment, water-volume conservation,
dynamic fluid meshing, rendering, or compatibility with this proposed field.
Compilation and actual object/swimming behavior remain unverified.

## Alternatives and limits

| Approach | Good fit | Main limit here |
| --- | --- | --- |
| Authored surfaces and trigger volumes | Decorative lakes, oceans, buoyancy | Does not by itself fill a bucket or drain a dug basin. |
| 2D shallow-water height field | Surface rivers and spreading across ground | One water column per horizontal location cannot alone describe stacked cave pools or arbitrary ceilings. |
| 3D amounts with local transfer rules | Finite water, tunnels, containment and terrain edits | Approximate pressure, coarse boundaries and slow equalization need explicit treatment. |
| Full 3D velocity/pressure or particle solver | Detailed liquid and smoke motion | Larger solver, boundary, rendering and multiplayer scope than the first slice needs. |

The [Chentanez/Mueller paper](https://matthias-research.github.io/pages/publications/hfFluid.pdf)
uses shallow-water equations for large water bodies with added detail handling.
It supports considering a surface model for exposed water, not adopting it as
the sole representation of this cave world. Do not introduce overlapping water
owners or a second solver until a measured need justifies the coupling cost.

## Water state and movement

Proposed authoritative owner: one host-side water state associated with the
world identity. Store amount per cell, with derived open volume and shared-face
connectivity sampled from the committed terrain. Add placed walls and doors as
boundary inputs when construction exists; terrain alone cannot seal those rooms.
Do not put water into terrain material IDs or make render meshes authoritative.

Use gravity-biased conservative transfers, lateral leveling and a deliberate
pressure approximation. Pure downward/sideways rules cannot correctly equalize
water through a submerged U-bend. The
[W-Shadow implementation](https://w-shadow.com/blog/2009/09/01/simple-fluid-simulation/)
demonstrates local mass transfer with slight artificial compressibility to
produce upward redistribution. It is a 2D example, with acknowledged slow,
unrealistic settling; its performance and exact rules do not establish a 3D
solution here. Compare its pressure approach against a bounded connected-water
head solve before implementation; the first is the smaller candidate.

Compute transfers from a stable tick state, budget each donor's total outflow
and each receiver's capacity, then commit shared-face transfers once. Preserve
small residual amounts instead of deleting them when putting cells to sleep.
Artificial compression, if chosen, must keep conserved amount separate from
rendered fill height and have a defined range. Reject claims of physical pressure.

Begin evaluation at terrain-aligned 16-unit cells with fractional amounts; this
is a candidate, not an accepted resolution. A full cell is about 67 litres under
the engine's inch convention, so a bucket must be a fractional cell, never one
whole water voxel. Coarsening each axis halves resolution but reduces cell count
eightfold; it also loses small passages and puddle detail. Cell-center occupancy
alone is insufficient: thin-wall and shared-face connectivity must be verified.

Digging wakes water around the changed bounds. Adding solid terrain into water
needs a conservation policy: initially reject a placement that would displace
water until bounded displacement is supported. Never silently erase trapped
water. Unknown/unloaded boundaries are not drains; pause the frontier until its
neighbor is ready, or use an explicit accounted boundary exchange.

## Desired features

| Feature | Proposed behavior |
| --- | --- |
| Lake | Initially filled basin; settled state sleeps; opening a bank wakes flow. |
| River | Flow through terrain with an explicit finite upstream supply or declared source rate and downstream outlet. |
| Bucket | Atomic transfer between inventory amount and world amount; reject or return unplaced remainder. |
| Rain | Visual rain particles plus batched, area-based water input on exposed surfaces; no gameplay collision per droplet. |
| Ocean | Declared sea-level reservoir boundary; local exchanges recorded as sources/sinks, with cave connectivity respected. |
| Waves | Animated surface displacement and normals; large buoyancy motion samples a matching wave description. |
| Other liquids | Later properties for density, flow resistance and appearance; layering and mixing require additional rules. |

An ocean reservoir is intentionally an effectively unlimited supply, not a
finite simulated ocean. Do not fill every air cell below sea level: disconnected
caves remain dry until connected. Finite lakes must remain drainable. Reservoir
definitions and local cell ownership must be disjoint so water is not counted
twice. River initialization and procedural basin selection need a separate
generation decision before implementation.

[GPU Gems' water chapter](https://developer.nvidia.com/gpugems/gpugems/part-i-natural-effects/chapter-1-effective-water-simulation-physical-models)
describes summed waves, including Gerstner waves, and fine normal detail. This
supports inexpensive animated surfaces, not volume transport. Damp displacement
near shores and ceilings; visual wave crests do not cause overtopping unless
that gameplay coupling is explicitly implemented. Waterfalls may use derived
flow meshes/particles without giving those effects an independent water amount.

## Gas extension

Track per-species concentration/amount in available air volume. Exchange through
open faces using diffusion plus a tunable upward/downward drift. A sealed room
retains amount; opening a door permits exchange. Explicit vents, sources and
sinks account for changes. Ambient air may initially be implicit, so this does
not simulate total air pressure or an air pocket preventing a room from flooding.

Keep gameplay properties separate from rendering: rise/sink tendency, diffusion
rate, optional source decay/removal, color, opacity and gameplay effect. Colorless
gas still exists and affects gameplay. Smoke is suspended material carried by
air, so a drift parameter is a gameplay approximation, not complete gas physics.
If temperature-dependent cooling, circulation or convincing turbulent plumes
become essential, reassess a local velocity/temperature solver.

[GPU Gems' fluid chapter](https://developer.nvidia.com/gpugems/gpugems/part-vi-beyond-triangles/chapter-38-fast-fluid-dynamics-simulation-gpu)
describes velocity/pressure and scalar smoke-density/temperature fields with
buoyancy. That is a richer alternative, not necessary evidence for the proposed
simple diffusion/drift model. Particle or volumetric rendering should sample
authoritative concentration; particle lifetime must not determine whether a
sealed room remains hazardous. Rendering cost requires its own measurement.

## Scheduling, networking and budgets

Evaluate 10-20 fixed updates per second near players, interpolate presentation
each rendered frame, and consider 1-2 updates per second for distant active
regions. These are design targets, not measurements. Update priorities must
include every player, sources and connected flows that affect them. Sleeping
means settled, not merely invisible. Declare that distant changes may progress
more slowly; freezing a remote river supply would change nearby behavior.

Keep fixed small steps and cap work/backlog; do not replace delayed work with
an arbitrarily large timestep. Cross-region transfers need one owner and a
shared simulated-time boundary despite differing scheduling rates. Start with
one active tick rate plus sleeping before introducing multiple rates.

CPU host simulation is the initial candidate, with immutable worker inputs and
version checks at commit. Engine resources change only on supported threads.
Clients receive versioned, interest-filtered water changes and snapshots, not
meshes; joins must agree on terrain revision and fluid state. Inventory transfers
need request identity and duplicate protection. Persist sleeping fluid state and
source definitions; never reconstruct a drained lake as newly full.

Dense allocation around every player is not presumed cheap: 128 cubed cells
already occupy 32 MiB at an illustrative 16 bytes per cell, before double buffers,
boundaries, queues or geometry. Actual bytes per cell and active-cell limits
remain undecided. Measure simulation, terrain boundary sampling, surface updates,
transparency, network bytes and memory separately. Sleeping regions retain state
even when they perform no simulation work.

## Smallest useful implementation and validation gate

First implement one finite water type through the playable world: bucket input,
an excavated basin, a connecting channel and drainage after a terrain edit.
Include amount conservation, pressure/leveling behavior, sleep/wake and smooth
derived presentation. Add oceans/rain and then gas after this is measured.

Before coding, finalize the pressure rule, boundary sampling, work/memory caps,
authority/persistence contract and rendering path in the subsystem design owner.
Before any runtime run, define exact fixed scenario parameters and numeric pass
criteria in [ValidationResults](../ValidationResults.md), as required by the
performance route. This research defines no new executable benchmark or test hook.

Acceptance must exercise buckets, basin drainage, connected U-bends, thin walls,
chunk seams, unloaded frontiers, solid placement, source accounting, sleep/wake,
late joins and separated players. Measure mass error, response and settling time,
CPU/GPU frame tails, allocations, retained memory and network convergence. Run
the unchanged canonical figure-eight and compare its accepted baseline. Gas later
needs sealed-room retention, door exchange and rise/sink checks. No such runs
were performed for this documentation-only investigation.
