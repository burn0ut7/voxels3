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

- Tests are executions of the shipping production behavior in the actual
  playable world. Invoke the same production entry point and data flow used by
  real gameplay; do not reimplement the algorithm in a harness.
- Do not create a separate test project, test framework, test directory, test
  file, test scene, test-only component, mock world, synthetic substitute,
  debug-only validation system, or alternate implementation. The validation
  ledger is documentation and contains no executable test logic.
- A startup, load, null, existence, or no-exception check alone is not a feature
  test. Cause the real operation to occur and measure its real outcome. For
  example, terrain-edit validation must apply the canonical edit through the
  gameplay mutation path and measure the resulting authoritative field change
  and required downstream world effects; merely loading the terrain is invalid.
- Test contracts and externally meaningful behavior, not private method shape.
- Prioritize spatial boundaries, negative coordinates, deterministic seeds,
  stale jobs, edit ordering, network convergence, and worst-case geometry.
- When fixing a reproducible defect, preserve its real-world reproduction as a
  fixed ledger scenario and rerun the production path after the fix.
- One run may record both correctness and performance measurements, but every
  claimed property needs a concrete metric and pass criterion.
- Do not assert that generated scene, project, or build files changed. Test the
  source behavior and inspect rendered/runtime results when visual or engine
  integration is part of the contract.

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

Every validation run must append a result to `Docs/ValidationResults.md`. Record
the scenario ID/version, date, project revision or identifiable source state,
engine build, hardware/environment, exact execution path, unchanged parameters,
raw measurements, derived metrics, pass criteria, pass/fail outcome, and any
remaining unmeasured risks. Link durable profiler captures or logs when they
exist; never use an untracked console observation as the sole evidence.

If the production behavior cannot be executed in the real world, report the
validation as not run and incomplete. Do not replace it with a synthetic test or
claim coverage from world loading alone.
