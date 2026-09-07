# Multiplayer FPS-drop profile review — 17:09:18

Reviewed 2026-09-07. Capture: `sbox_2026-09-07_17_09_18.json`, found under
`C:/Program Files (x86)/Steam/steamapps/profiler_captures/` in its same-named
folder. The user-supplied path contained extra separators; the timestamp matched.
Analysis is read-only; no runtime changes or acceptance test were performed.

## Finding

Player animation is the largest identified gameplay-side main-thread hotspot
in this capture. Terrain processing is also significant, particularly native
collision mesh construction and correction-page range queries. Terrain transfer
dispatch is smaller. This supports investigating player animation and allocation
pressure alongside terrain work; it does not establish a single cause of the
reported FPS drop when a second client starts.

## Capture scope and limits

The sampled process is editor `sbox-dev`, PID14584, main thread44684. Sampling
interval metadata is0.2ms. Main-thread samples span8.903–12001.2642ms, with
32,099 stack samples and approximately5409.77ms of recorded CPU deltas. Profile
metadata extends to13033.6053ms. CPU stack samples are not frame durations;
this export does not establish exact FPS, GPU time or a before/after FPS ratio.

The host log places the second player's join at17:08:27.3908, approximately
51seconds before capture start17:09:18. The trace therefore describes ongoing
multiplayer work, not the join transition itself. Terrain edits continue during
the capture: acknowledged revisions advance from377 with62 pages. No equivalent
pre-join JSON has been analyzed. The directory also contains a17:05:37 ETL/ETLX
capture, but those are not automatically a comparable baseline. A second local
client's CPU/GPU contention is outside this editor-only profile.

## Main-thread inclusive samples

| Work | Samples | Share of main-thread samples |
| --- | ---: | ---: |
| SceneAnimationSystem.UpdateAnimation | 5,606 | 17.46% |
| VoxelManager.OnUpdate | 3,591 | 11.19% |
| VoxelCollisionWorld.Integrate | 1,367 | 4.26% |
| TerrainFieldSnapshot.GetCorrectionRange | 685 | 2.13% |
| Networking.PreFrameTick | 426 | 1.33% |
| UpdateTerrainReplication | 151 | 0.47% |

These rows overlap: collision integration and correction queries are included
in broader terrain work. Do not sum the percentages. Functions are counted once
per sampled stack, avoiding recursive duplicate attribution. Animation stacks
include SkinnedModelRenderer.AnimationUpdate/native work and Parallel worker
coordination/spin waits. Collision integration includes PhysicsBody.AddMeshShape
(1287 samples). The transfer-dispatch row excludes other networking, ACK handling
and off-thread page encoding; it is not total multiplayer overhead.

## Allocation and collection observations

Allocation ticks total207,344,088bytes (about197.7MiB), with145,264,792bytes
attributed to the main thread. Allocation ticks are sampled; their associated
type is an approximate attribution, not exact per-type allocation accounting.
Notable attributed types include float/byte arrays, TimerQueueTimer,
DelayPromiseWithCancellation, RenderAttributes, correction-page enumerators,
and Parallel task-replica arrays. Correction-page enumerators account for about
7.13MiB of tick attribution across threads. Current source exposes Pages through
a read-only dictionary interface and enumerates it in GetCorrectionRange's
sparse-page branch, consistent with boxed enumeration appearing in the profile.

Three main-thread GC intervals are8.4624ms,8.9929ms and18.6243ms. Their depths
are0,0,1; the marker label GCMajor does not mean all were generation2 collections.
These pauses can affect frame pacing, but do not prove the sustained FPS drop.

## Next work justified by the evidence

1. Compare one versus two players at identical idle positions, then identical
   editing workloads. Separate another player's animation from starting another
   full renderer on the same PC. Measure frame/GPU times as well as CPU stacks.
2. Inspect player/clothing renderer counts and animation update policy. Preserve
   remote player appearance and animation correctness; do not disable animation
   merely to obtain a faster result.
3. Remove demonstrable allocation overhead in canonical correction-range
   queries, and inspect frequent task/timer allocation callers before changes.
4. Recheck native collision installation tails under simultaneous player edits.
   The existing two-worker/admission limits do not make native mesh creation
   preemptible.

No optimization or performance improvement is claimed from this review.

## Evidence

[Derived sample/allocation summary](../ValidationEvidence/TerrainDeformation/profile-multiplayer-170918-summary.json)
contains the original capture path and SHA256, per-thread counts and GC events.
[Host log excerpt](../ValidationEvidence/TerrainDeformation/profile-multiplayer-170918-log-excerpt.txt)
correlates the join, profile start and continuing terrain transfers.
