# Voxels3 Agent Instructions

## Purpose

This file is the entry point for work in this repository. Read this file first,
then read every route that applies to the requested change. Route files contain
domain-specific constraints; this file owns project-wide rules.

If a change crosses domains, start with the architecture route and then read the
other applicable routes. Do not infer an established implementation from a
route: distinguish current repository facts from intended design constraints.

## Mandatory Skill and Router Use

- Use the Sandbox skill for all s&box-specific work. Load and follow its current
  instructions before researching APIs, planning an implementation, reviewing
  s&box code, or making changes. Do not rely on remembered engine behavior when
  the skill can provide current, project-grounded guidance.
- If the Sandbox skill is unavailable in the active environment, state that
  limitation explicitly and continue only with verified repository evidence and
  authoritative s&box documentation. Never invent an API or silently substitute
  an unrelated skill.
- Router documentation is mandatory, not optional background reading. Before
  planning or modifying anything, read this file and every route selected by the
  Route Map. Follow their ownership, design-gate, validation, and performance
  requirements throughout the task.
- If the scope expands while working, stop and read the newly applicable route
  before continuing. Re-check the routes before final validation.
- The Sandbox skill supplies engine and tooling guidance; these router files
  supply Voxels3-specific architecture and development rules. Follow both. If
  they appear to conflict, surface the conflict and resolve it explicitly rather
  than choosing a silent workaround.

## Project Overview

Voxels3 is an s&box multiplayer game project for a 3D voxel world with smooth
signed-distance-field (SDF) terrain, procedural generation, and live terrain
edits. Performance is a primary product requirement: world generation,
meshing, rendering, collision, streaming, editing, and replication must remain
responsive under real multiplayer load.

The project is currently at an early foundation stage. The s&box project is
configured as a multiplayer game for 1-64 players at a 50 Hz tick rate. The first
canonical chunk foundation now provides implicit flat-SDF evaluation with
Grass/Air material IDs and no sample arrays, bounded single-origin chunk
streaming, inspector status, structured logs, and debug overlays. Surface
meshing, collision, live terrain
edits, persistence, multi-origin interest management, and project-specific
network replication are not implemented yet. See
`Docs/Architecture/VoxelChunkFoundation.md` for the exact current design.

## Repository Facts

- Runtime code is under `Code/`; editor-only code is under `Editor/`.
- Scenes and resources are under `Assets/`; configuration is under
  `ProjectSettings/`.
- Runtime and editor projects target .NET 10 with C# 14 and the `Sandbox` root
  namespace.
- Runtime code does not allow unsafe blocks. Nullable annotations are disabled.
- Follow `.editorconfig`: tabs, four-column indentation, CRLF, final newline,
  and braces.
- `.scene_c`, `.scene_d`, build output, and other ignored/generated artifacts
  are not source files. Do not hand-edit or test generated output.

## Route Map

Read the smallest set of routes that fully covers the work:

| Work area | Required route |
| --- | --- |
| System boundaries, ownership, data flow, or a cross-cutting feature | [`Docs/AgentRoutes/architecture.md`](Docs/AgentRoutes/architecture.md) |
| Voxel data, density fields, SDF sampling, terrain edits, chunks, or boundaries | [`Docs/AgentRoutes/terrain-sdf.md`](Docs/AgentRoutes/terrain-sdf.md) |
| Surface extraction, CPU/GPU meshing, render geometry, collision geometry, or LOD | [`Docs/AgentRoutes/meshing.md`](Docs/AgentRoutes/meshing.md) |
| Seeds, noise, biome/terrain functions, or deterministic world creation | [`Docs/AgentRoutes/procedural-generation.md`](Docs/AgentRoutes/procedural-generation.md) |
| Authority, replication, prediction, edit synchronization, joins, or persistence transfer | [`Docs/AgentRoutes/multiplayer.md`](Docs/AgentRoutes/multiplayer.md) |
| Hot paths, memory, threading, jobs, profiling, benchmarks, or tests | [`Docs/AgentRoutes/performance-and-testing.md`](Docs/AgentRoutes/performance-and-testing.md) |

For ordinary s&box component, scene, editor, or settings work that does not touch
one of these domains, this file is sufficient. Keep editor-only dependencies out
of `Code/`.

## Development Rules

- Do not add speculative abstractions, general-purpose helpers, or extra
  features unless the current slice requires them.
- Carefully and comprehensively plan features before implementation. Avoid
  over-engineering, premature abstraction, and scope creep.
- Follow YAGNI. Never speculate about future features. Prefer a small,
  well-bounded change over re-architecting unless there is an articulable reason
  the larger change is necessary. Extraordinary changes require extensive
  justification.
- If an existing system is low quality or conflicts with these principles,
  remove it and redesign the feature properly from first principles. Do not
  preserve a bad design through compatibility layers.
- Choose the best long-term design, not merely the easiest short-term solution.
  When unsure, research and compare the strongest paths before choosing.
- Each feature must have one canonical system, with all related behavior flowing
  through it. Do not add fallbacks, secondary implementations, duplicate logic,
  compatibility layers, workarounds, or parallel paths.
- Move duplicated behavior into a shared package only when it is genuinely the
  same responsibility and must evolve together.
- Do not refactor for style alone. Do not create one-line functions for trivial
  operations used once; keep those operations in the main function.
- Validation must execute the actual production code through its real in-world
  entry point and exercise the complete behavior being claimed. Loading a world,
  starting without an exception, or confirming that an object exists is not a
  test of the feature.
- Do not create separate test projects, test systems, test scenes, test-only
  components, test files, mocks, synthetic implementations, or alternate code
  paths. Do not add test-only hooks to production code. Validate the production
  function itself in the actual playable world with a realistic example.
- Every validation run must produce measurable output and append its exact
  parameters, measurements, pass criteria, and result to the canonical
  [`Docs/ValidationResults.md`](Docs/ValidationResults.md) ledger. A subjective
  statement such as "it works" is not evidence.
- Each scenario has one canonical parameter set. Use exactly the same world,
  seed, coordinates, inputs, operation count, timing window, player count, and
  other relevant values on every comparable run. Do not tune, shift, randomize,
  reduce, or otherwise change parameters to obtain a passing result.
- A parameter may change only when an extraordinary, substantive issue makes the
  existing scenario invalid or impossible to execute. Document the issue and
  justification before the change, create a new scenario version, preserve the
  old definition and results, and establish a new baseline. Never rewrite prior
  measurements or compare incompatible scenario versions as if they were the
  same test.
- Test meaningful behavior, contracts, regressions, edge cases, and non-trivial
  transformations through that in-world production path. Do not test that a
  generated file changed; test the source behavior that produces the result.

## Required Working Method

1. Load the Sandbox skill, then identify the smallest complete feature slice and
   every router document it touches.
2. Read the selected routes completely before planning or editing.
3. Inspect the current source and write down the relevant ownership and data
   flow before changing code.
4. For a new subsystem, compare viable designs and record the chosen canonical
   path plus the rejected alternatives that were serious contenders.
5. Define correctness and performance acceptance criteria plus the exact fixed
   in-world scenario parameters before implementation. Record a new scenario in
   `Docs/ValidationResults.md` before its first run; reuse the existing scenario
   unchanged for regression work.
6. Implement only the selected slice. Remove superseded paths in the same
   change; do not leave dormant alternatives behind.
7. Re-check the selected routes, run the unchanged canonical scenario through
   the production code in the actual world, and append its measurable output to
   `Docs/ValidationResults.md`. Report what was and was not verified.

## Cross-Cutting Invariants

- World state has one authoritative representation. Render meshes, collision
  meshes, caches, and network payloads are derived data, not competing truth.
- A live edit enters through one canonical mutation path, identifies all
  affected spatial regions, and triggers only the required downstream work.
- Multiplayer authority and procedural determinism are explicit. Never depend
  on incidental iteration order, frame timing, or client-local random state.
- Performance claims require measurements. Preserve correctness first, then
  optimize the measured bottleneck without creating a second implementation.
- Constants that define spatial scale, sign conventions, coordinate transforms,
  or network protocol meaning must have one owner and be documented beside it.
