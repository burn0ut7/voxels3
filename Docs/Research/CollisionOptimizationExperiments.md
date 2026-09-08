# Focused Collision Optimization Experiments

2026-09-08. Goal: find a measured collision performance or memory improvement
without material frame-pacing, geometry, contact, readiness or streaming regressions.
Current starting source: a6b20c8; current saved world revision971/160 pages.
This is a new experiment series, not acceptance of historical collision gates.

## Working agreement

The user's latest instruction supersedes the initial hold-without-revert request:
run short, targeted experiments; revert a failed candidate when appropriate;
preserve results and the user's original state. One changed mechanism at a time.
Use existing production diagnostics, stage timings and figure-eight entry point.
Do not add test-only paths, change terrain quality, expand worker count, or chase
small noisy differences with repeated tuning. Commit runtime changes only after
acceptance. If no high-value hypothesis remains, hold the last accepted state.

## Plan and stop rules

1. Preserve source identity, authored scene and saved terrain hashes. Record a
   fixed current-world scenario in the ledger before the first run.
2. Capture one normal-start baseline including startup collision/contact,
   canonical figure-eight, moving stage metrics, memory, allocations and tails.
3. Prototype one exact arithmetic shortcut in canonical cave bounds: evaluate
   cheese/thickness first; omit noodle bounds only when their maximum possible
   contribution is already dominated. Preserve unresolved arithmetic and all
   existing conservative rounding. No new field, cache, geometry or resolution.
4. Build/hotload, run the same scenario once. Reject immediately for correctness
   failure or no useful measured benefit. Record the patch and revert only this
   task's change. Do not tune the fixed workload to improve results.
5. Repeat only a promising candidate and its control as necessary to distinguish
   improvement from noise. A one-run difference is a screening result.
6. If rejected, inspect remaining measured costs briefly. Higher-value alternatives
   are edit rebuild amplification and bounded field reuse; smaller collision
   sections need evidence of native rebuild dominance before implementation.
   Do not repeat previously rejected block-size/gradient/cache experiments
   without new evidence. Cap this initial screen at two distinct prototypes.

## Evidence and ownership

Canonical field formulas remain owned by ProceduralTerrainSdf. The shortcut
would affect CPU field-bound consumers, so visual geometry/streaming equivalence
is also required. Collision retains two workers, reusable geometry, native
engine-thread publication and prior support until validated replacement.

The [Facepunch comparison](https://github.com/Facepunch/sbox-public/blob/a0b002cdbff9abfade9afd4cb205230a55120121/engine/Sandbox.Engine/Scene/Components/Terrain/Terrain.Collider.cs)
motivates reducing preparation and changed-region work. Heightfield rectangular
updates cannot directly replace our cave-capable mesh collision. No speedup is
inferred from C++ ownership or UpdateMesh naming.

The [collision profile](CollisionProfile20260907.md) supports prioritizing CPU
sampling/bounds; its historical timings are not this series' baseline.
The [ledger](../ValidationResults.md) owns immutable scenarios and run outcomes.
Record decisions here after each screen; no implementation is accepted yet.
