# Performance and Testing Route

Use this route for performance-sensitive implementation, profiling, benchmarks,
threading, allocations, and test strategy.

## Performance Method

- Define one fixed, realistic in-world workload and budget before optimizing:
  active regions, seed, coordinates, edits per second, players, view/streaming
  radius, terrain complexity, operation count, timing window, and target
  hardware. Record every applicable value in `Docs/ValidationResults.md`.
- Measure before and after. Report median and tail latency where stalls matter,
  not only aggregate throughput.
- Profile the complete pipeline: generation, field access, dirty propagation,
  meshing, upload/readback, collision, networking, and frame integration.
- Bound work per frame or schedule it explicitly. Avoid unbounded scans and
  synchronous rebuilding after live edits.
- Avoid recurring allocations and redundant data conversion in measured hot
  paths. Do not add pooling, custom containers, SIMD, or concurrency until the
  workload and profiler justify them.
- Never trade away determinism or correctness without an explicit product-level
  decision and evidence.

## Concurrency

- State which data may be accessed off the engine thread and who owns it.
- Prefer immutable job inputs or versioned snapshots over shared mutable access.
- Cancellation and stale-result rejection are required for work that may outlive
  the source revision or region lifetime.
- Apply engine resource changes only on APIs/threads supported by s&box.

## Test Strategy

Follow [project validation and figure-eight acceptance](../../AGENTS.md#validation).
The figure-eight remains the primary project-owned automated test trigger;
bounded read-only observations can support it. On 2026-09-07 the user explicitly
authorized a separate deformation benchmark, run only when testing deformation.
Its [research proposal](../Research/TerrainDeformationSecondSlice.md#7-dedicated-deformation-benchmark)
does not implement a trigger or replace the figure-eight regression requirement.
Both must exercise production behavior under fixed ledger scenarios. Do not use direct streaming-origin
mutation or diagnostic-only terrain implementations as substitute test paths.

Prioritize spatial boundaries, negative coordinates, deterministic seeds, stale
jobs, edit ordering, network convergence, and worst-case geometry as relevant to
the change. Preserve a defect's real-world reproduction as a fixed ledger
scenario. One run may cover correctness and performance, but each claimed
property needs a concrete metric and pass criterion. Inspect actual rendered
results when visual or engine integration is part of the contract.

## Fixed Scenario Policy

- Every scenario has a stable ID and immutable versioned parameter set in
  `Docs/ValidationResults.md`.
- All comparable baseline, before, after, and regression runs must use the exact
  recorded parameter values. This includes seed, world/scene, positions, input
  values, operation order and count, warmup, duration, player count, engine
  settings, and metric definitions wherever applicable.
- Do not cherry-pick favorable runs, change values between runs, weaken pass
  criteria after seeing results, or discard failures from the ledger.
- Hardware, engine build, project revision, and environmental differences must
  be recorded. They provide context; they do not authorize changing scenario
  inputs.
- Parameters may change only because an extraordinary, substantive issue makes
  the old scenario invalid or impossible—not because it is slow, inconvenient,
  flaky, or failing. Record the justification first, retain the old scenario and
  history, issue a new scenario version, and establish a clearly labeled new
  baseline. Results from different versions are not a continuous comparison.

## Validation Reporting

Use the [ledger's scenario and run template](../ValidationResults.md#scenario-definition-template)
for parameters, source/environment identity, raw and derived measurements,
criteria, decisions, and durable evidence links. Report unavailable in-world
validation as not run and incomplete; startup alone cannot replace it.
