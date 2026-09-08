# Proposed final read-admission diagnostic

Status: proposal only; no diagnostic hook has been added or run.

The regional store and normal pressure workloads have passed bounded correctness
checks. Real512MiB read denial was observed earlier, but capacity did not become
available during that observation. Later changed-source pressure runs finished
without read denial. Repeating unchanged timing to chase a counter is not valid
coverage. The remaining claim is specifically that queued reads resume correctly
when reserved sample capacity becomes available.

## Exact proposed change

Temporarily add a host-only diagnostic hold/release control to the existing
`VoxelManager.Storage.cs`, backed only by `TerrainFieldPage.TryReserveSamples`
and its existing `SampleReservation.Dispose`. It holds unused reservations from
the existing512MiB budget; it does not allocate fake voxel pages, change the cap,
replace the sampler/store, modify the mutation path or force collection.

- Start in an unchanged private saved world with all queues idle. Preserve and
  audit the latest user save separately.
- The hold operation reserves available capacity in bounded batches no larger
  than `MaximumTransactionPages`, retaining those reservation objects in the
  manager. Record retained plus reserved accounting. A second hold is rejected.
- Move the actual player into saved cold terrain through the existing player
  transform control. Require real `readCapacityDeferrals` to increase while
  queued requests remain intact and no page/read error is reported.
- Release those reservations using the existing disposal path. Require a positive
  loaded-page delta, queued reads and reservations returning to zero, settled
  derived geometry, unchanged world/edit counters and the exact saved fingerprint.
- Release in teardown/finally as well, so cancelled testing cannot strand capacity.
  No diagnostic mutation is saved to the terrain store.
- Remove the temporary control and its teardown cleanup before committing any
  shipping code; verify the removal and final compiler state. Keep the observation
  evidence and explicitly identify its controlled-reservation condition.

Only the temporary orchestration and teardown cleanup would touch runtime code.
The production reservation, queue, retry, disk decode and integration routines
would execute unchanged. Final user state and scene hashes would be verified again.

## What this would and would not prove

It would verify real read-admission denial and same-session retry after capacity
release, under controlled reservation pressure. It would not prove how quickly
.NET collects retired arrays, eliminate GC pauses, or turn the earlier103.7second
retained-memory wait into an automatic recovery pass. Those timing/retention
limitations remain explicit under the user's performance policy.

## Required project-rule exception

AGENTS.md says: "Do not add separate test projects, frameworks, files, scenes,
components, mocks, synthetic implementations, alternate paths, or test-only hooks."
This proposal deliberately requires a temporary test-only hook, so it must not be
implemented without an explicit user override of that rule. No such override has
been supplied yet. The sbox skill itself does not require this approval.
