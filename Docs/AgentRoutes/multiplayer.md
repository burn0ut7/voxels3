# Multiplayer Route

Use this route for authority, replication, RPCs, edit ordering, prediction,
joining, interest management, and transfer of world state.

## Scope

Read the [foundation scope](../Architecture/VoxelChunkFoundation.md#scope) for
implementation status and the authored project settings for player/tick limits.
The rules below constrain multiplayer work; they do not describe an existing
voxel replication protocol.

## Authority

- The server is authoritative for accepted terrain edits and shared world state.
- Clients may predict presentation only when reconciliation semantics are
  designed and tested. Prediction must not become a second authoritative world.
- Validate edit requests against permissions, bounds, rate limits, and gameplay
  rules before mutating authoritative state.
- Give edits an explicit identity and ordering model sufficient for duplicate,
  delayed, reordered, and conflicting messages.

## Replication

- Replicate canonical inputs, edits, or compact state—not render meshes—unless
  measurement proves a different design is required.
- Procedural reconstruction is valid only when seed, configuration, generator
  version, and deterministic behavior match.
- Define interest management around the selected spatial partition. Do not send
  all world changes to all players by default.
- A joining client needs a single coherent path to the current authoritative
  state, including the procedural baseline and all relevant committed edits.
- Choose operation replication, snapshots, or a measured combination as one
  protocol design; do not accumulate unrelated fallback protocols.

## Failure Cases to Test

Validate late join, reconnect, duplicate delivery, reordered edits, invalid
requests, boundary-spanning edits, concurrent edits, unloaded-region edits, and
mismatched generator/configuration versions through the real multiplayer flow.
Measure authoritative/client convergence, bandwidth, and processing work. Follow
the [performance and testing route](performance-and-testing.md); record clients,
positions, edit order, timing, and latency conditions in each fixed scenario.
