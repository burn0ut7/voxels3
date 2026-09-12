# User profiler capture 155312

Source: `C:\Program Files (x86)\Steam\steamapps\profiler_captures\sbox_2026-09-09_15_53_12.json`  
SHA-256: `c0f83e704a02cf0cc630559fde38b3bbb69d3126523b07ef8d1fe095e6583e69`

The capture spans `10,794.1032 ms` with a `0.2 ms` sample interval, `12,017.750325 ms` summed CPU time, and `42` threads. Summary inclusive CPU values include overlapping callers and must not be added: `VoxelCollisionMesher.Build` `5,110.420169 ms` (`42.5239%`), `ProceduralTerrainSdf+LatticeSampler.Sample` `2,891.755564 ms` (`24.0624%`), `RiverWorld+Region.SampleWorld` `2,471.002435 ms` (`20.5613%`), `RiverNetwork+Patch.SampleWorld` `2,466.999943 ms` (`20.5280%`), `RiverNetwork.SampleSegments` `2,463.003455 ms` (`20.4947%`), `TerrainFieldSnapshot.GetDensityRange` `2,051.259913 ms` (`17.0686%`), and `VoxelManager+<>c__DisplayClass190_0.<PrepareSurfaceWater>b__0()` `1,580.847462 ms` (`13.1543%`).

GC markers record four nonconcurrent major collections, all `reason=AllocSmall`, counts `561–564`; wall duration totals `19.1819 ms`, maximum `5.3823 ms`. Four suspend intervals total `0.1991 ms`, maximum `0.0878 ms`. There are `2,324` zero-duration allocation markers totaling `349,660,896 bytes`; maximum single allocation is `39,272,472 bytes` (`2,143` Small, `181` Large). Largest allocation totals are `Segment[]` `198,689,272 bytes`, `Vector4[]` `54,364,656 bytes`, and `Entry[RiverNetwork+Segment,System.Int32][]` `46,881,200 bytes`.

Four heap snapshots report total heap `3,674,792,240–3,713,510,304 bytes`, promoted bytes `13,418,720–24,918,784`, pinned objects `2`, and GC handles `91,499–91,653`. No GPU timing or per-frame allocation counter is recorded; CPU samples and GC wall markers are separate measurements.

## Follow-up measurements

The implementation now prunes river segment searches with a spatial index, reuses XY landform samples across density columns, and reuses each GPU preparation lane's river texture while its recipe and coverage remain valid. Initial water publication no longer waits for every distant terrain mesh. The approved branching geometry remains unchanged in the recorded segment comparison.

The fixed RIVERS-007/v1 run `25443ed1106e4588a15a65b843d82b3c` recorded 117.90327 FPS moving and 186.5212 FPS stationary, with all 4,913 collision regions ready and all work queues empty at completion. Compared with the preceding bootstrap run, managed allocations fell from 3,980,632,464 to 1,011,834,112 bytes (74.58%). These are allocations during the moving measurement window, not a direct repetition of the user's profiler capture. Stationary maximum frame time rose from 11.0155 to 23.2249 ms. See the [validation ledger](../../ValidationResults.md) and [raw result](atlas-reuse-result.json).

An unchanged-source cold repeat, `dc00c5e5809646a7a06f1df5fc5845b0`, recorded 117.12016 FPS moving and 188.1189 FPS stationary, all collision ready and queues empty. Moving allocations remained 72.248% below bootstrap. Worst stationary frame was 8.7451 ms; the earlier isolated spike did not recur. Moving and stationary FPS, frame tails, memory and allocations pass the recorded comparison thresholds in this repeat. Both results remain retained; the aggregate counters cannot identify the earlier spike's individual cause. [Repeat result](atlas-repeat-result.json).
