# Voxels3 Agent Instructions

## Start Here

- Use the `sbox` skill for s&box work before API research, planning, review, or
  implementation. If unavailable, say so and use verified repository evidence
  and authoritative s&box documentation; never invent APIs.
- Read the applicable routes below before planning or editing. Start with
  architecture for cross-domain work; read additional routes if scope expands
  and re-check them before final validation.
- The skill owns engine/tooling guidance; routes own project-specific rules.
  Surface conflicts explicitly. Distinguish implemented behavior from intended
  design, and verify current source before relying on documentation.

## Project Overview

Voxels3 is an s&box multiplayer voxel game with smooth signed-distance-field
(SDF) terrain, procedural generation, and planned live edits. Performance under
multiplayer load is a primary requirement.

Use the [documentation map](Docs/README.md) to find the owner of each subject.
Keep current contracts in architecture, test outcomes in the validation ledger,
and research proposals separate from implemented behavior.

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

## Research Reference Library

Use `Docs/smooth_procedural_voxel_terrain_resources.md` to find external research.
Read each source's transfer limits; research is evidence, not an adopted design.
Repository contracts, measured results, and verified engine behavior take
precedence.

Add durable sources that materially help the task under the narrowest relevant
entry, explaining their use and limits. Avoid duplicates and generic bookmarks.
When a source informs a design decision, record what the project adopts or
rejects and why in the relevant design note.

## Development Rules

- Plan the smallest complete slice. Add only what it requires; avoid speculative
  features, abstractions, helpers, and style-only refactors.
- Inspect ownership and data flow before editing. For a new subsystem, document
  inputs, outputs, state ownership, downstream effects, budgets, and serious
  design alternatives in the relevant design note.
- Keep one canonical implementation per responsibility. Remove superseded paths
  in the same change; do not retain duplicate implementations or compatibility
  layers. Share behavior only when it has the same responsibility and must
  evolve together.
- Redesign an existing system when a concrete defect or architectural conflict
  requires it. Explain why the broader change is necessary; poor style alone
  is not a reason to expand scope.
- Keep trivial, single-use operations inline rather than adding one-line helpers.

## Validation

- Validate shipping behavior through its real entry point in the playable world.
  Exercise and measure the claimed behavior, including relevant edge cases;
  startup, object existence, or absence of exceptions alone is insufficient.
- Do not add separate test projects, frameworks, files, scenes, components,
  mocks, synthetic implementations, alternate paths, or test-only hooks.
- Define measurable pass criteria and fixed scenario parameters in
  `Docs/ValidationResults.md` before the first run. Reuse existing scenarios
  unchanged for comparable runs; follow the performance route's versioning rules
  when a scenario is invalid or impossible. Never tune inputs to obtain a pass.
- Append each runtime validation run's scenario/version, exact parameters,
  source/environment, measurements, criteria, and result to that ledger. Preserve
  failures and history. Report what was verified and what remains unverified.
- Check documentation-only changes for accuracy, links, and consistency; they
  do not require an in-world run.

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

## Figure-Eight Performance Acceptance

- The canonical figure-eight is our main performance test. Run it in the real
  playable world for non-simple or potentially performance-affecting changes;
  skip only changes that cannot affect runtime behavior, such as documentation.
- Use the recorded scenario unchanged and compare with the latest accepted,
  comparable baseline; capture a pre-change baseline if none exists.
- Check frame rate, frame pacing/tail latency, chunk completion and streaming,
  memory, allocations, and correctness against the recorded criteria. Resolve
  material unexplained regressions before accepting, committing, or pushing.
- Append measurements and the comparison decision to `Docs/ValidationResults.md`.
  Changing the workload or accepting a regression requires documented evidence
  and explicit user approval; preserve history and baseline any new version.

## Git Commit Policy

After completing a task, commit and push only the task's changes. Use a commit
subject of at most five words and leave the commit body blank.
