# Storage memory acceptance clarification

Date: 2026-09-08. Status: proposal only; user approval required before changing
STORAGE-HISTORY-001/v1 or declaring its failed memory gate superseded.

The implementation releases saved canonical page references after five seconds
without required interest, through a bounded sweep. Captured readers retain their
own immutable sample arrays while they need them. The existing global allocation
accounting uses weak references and continues to count an unreachable array until
garbage collection clears that reference. These are different lifetime events.

The frozen HISTORY scenario requires both payload release within ten seconds of
eligibility and the same current-plus-retained byte total ten seconds after each
route. The latter applies a wall-clock deadline to garbage collection as well as
terrain ownership. Preserve the failed runs: this proposal does not turn them into
passes or prove that every reader releases on time.

Microsoft documents that collection timing depends on allocations and memory
pressure; large objects are collected with generation 2. Our arrays contain
131072 bytes of samples each. The engine's effective GC configuration has not been
independently established here. These runtime facts explain why clearing the final
terrain reference is not proof of collection within ten seconds. They do not prove
the cause of every historical delayed-release observation.
[Microsoft GC fundamentals](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/fundamentals).

Evidence in the current implementation includes actual resident eviction and disk
re-entry with matching saved fingerprints, three visits without increasing settled
sample totals in the visible HISTORY run, ten capacity-fixture reloads without
reservation growth, and eventual reduction of all tracked samples to the current
34 pages (4456448 bytes). The latest eventual-collection observation establishes
no extra tracked sample arrays at that instant; it is not a timed release proof.
See [the ledger](../ValidationResults.md), especially the HISTORY failures,
STORAGE-RELOAD-PRESSURE-001/v1 and active-cancellation follow-up.

## Proposed decision

Keep the ten-second requirement for releasing eligible saved pages and finished
consumer references. Keep the 512 MiB combined allocated/reserved sample cap,
dirty-state protection, exact re-entry correctness and repeated-visit memory checks.
Remove only the requirement that garbage collection itself restore the exact
pre-route byte total within ten seconds. Report resident bytes, unreclaimed sample
bytes, reservations and observed collection timing separately. Never subtract
uncollected arrays from admission accounting to manufacture a pass.

If approved, issue a new scenario version with the same world, edits, route,
operation counts and observation windows, identifying this metric change explicitly.
Record a new baseline; retain all version1 outcomes. Actual ownership release still
needs evidence; eventual collection alone cannot meet its ten-second deadline.
If a complete collection occurs after all relevant work has ended and excess
sample arrays remain, investigate their owners rather than accepting them as GC
delay. No forced collection, unmanaged-memory rewrite, new sample pool or changed
terrain representation is proposed.

The reason for this revision is a mismatch between ownership and collector timing,
supported by implementation and runtime evidence. It is not an authorization to
relax slow save/load, frame-time or readiness gates. In-session stale-read rejection
and actual memory-admission denial/recovery remain separate unverified cases.

The project AGENTS.md requires explicit user approval when changing a workload or
accepting a regression. Until that approval, version1 remains unchanged and failed
or incomplete where recorded; the full goal remains open.
