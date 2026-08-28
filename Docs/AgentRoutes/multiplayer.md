# Multiplayer Route

Use this route for authority, replication, RPCs, edit ordering, prediction,
joining, interest management, and transfer of world state.

## Project Constraints

The s&box project is configured for multiplayer, 1-64 players, and a 50 Hz tick
rate. There is no project-specific voxel networking implementation yet.

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
clients with mismatched generator/configuration versions by exercising the real
production multiplayer flow in the actual playable world. Use fixed world seed,
clients, positions, edit sequence, timing, latency conditions, and operation
counts for each versioned scenario. Measure final authoritative/client state,
convergence time, bandwidth, and processing work against criteria defined before
execution, then append every result to `Docs/ValidationResults.md` under the
policy in `Docs/AgentRoutes/performance-and-testing.md`.
