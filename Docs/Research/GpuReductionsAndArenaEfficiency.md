# GPU scans and arena memory efficiency

Voxels3 engineering decision study | 7 September 2026 | Scope: regular GPU extraction and shared geometry arena capacity.

## Decision and evidence boundary

Research supports two isolated prototypes: a cooperative regular group-total scan,
and 24 MiB vertex arenas paired with the existing 16 MiB index arenas. Neither is
accepted from source inspection alone. Test outcomes below determine what remains
implemented. Field, topology, transitions, region dimensions, render lifetime and
workload stay fixed. CPU allocation optimization, visibility gating and extractor
replacement are outside this investigation.

The source baseline is `aab4bc8`. The workload remains
[GPU-MESHING-512-001/v1](../ValidationResults.md#gpu-meshing-512-001v1---gpu-simplification-investigation):
authored radius 512 / gameplay 8 scene, generator 5 / seed 1337, 32 cells, base cell size 16,
levels 0-6, half extents 4/8, one player, speed 2500 / distance 50000/one Figure Eight,
normal drain and ten stationary seconds. Hardware is Ryzen 7 9800X3D / RTX 5090,
engine 26.09.01c. Results do not establish lower-tier, edited or multiplayer behavior.

## Reductions and scans

The [regular shader](../../Assets/shaders/voxels/voxel_persistent_geometry.hlsl)
already uses a 256-value exclusive uint tree scan for local cell and edge offsets.
It needs 1 KiB of shared memory and 18 group barriers per call. Stage 5 then leaves
255 threads idle while lane 0 serially scans 422 edge-group and 128 cell-group totals.
Those totals are the narrowest parallelization target: count, prefix order and
output layout can remain identical.

A work-efficient tree scan and multiple consecutive inputs per thread are established
algorithmic techniques. The relevant transfer is the algorithm, not historical CUDA
hardware timing or memory-bank padding constants. [Harris, Sengupta and Owens,
GPU Gems 3 chapter 39, NVIDIA, 2007](https://developer.nvidia.com/gpugems/gpugems3/part-vi-gpu-computing/chapter-39-parallel-prefix-sum-scan-cuda).

S1 will load two consecutive edge totals per lane, zero-pad beyond 422, scan their
sum, then reconstruct both original-order prefixes. It will scan 128 padded cell
totals using the same helper. No new buffer or dispatch is necessary. All lanes
must participate, and a barrier must separate reading the first shared scan from
reinitializing that array for the second. Group barriers in divergent control flow
have undefined behavior. [Microsoft, GroupMemoryBarrierWithGroupSync, updated
2023-09-14](https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/groupmemorybarrierwithgroupsync).

The additions are bounded integers; no floating-point density or interpolation
reassociation occurs. This supports exact output equivalence, but only a real
emitted-buffer audit can validate rewritten offsets: unchanged count-stage geometry
fingerprints alone cannot prove that emitters used correct prefixes.

The counterargument is substantial: two tree scans and the reuse barrier add 37
synchronization points to a stage that previously had none. With only a few regions
per batch, synchronization can outweigh parallelism. Acceptance therefore requires
at least 5 percent improvement in both moving GPU p95 and p99, reproduced in a
confirming run, alongside all existing non-regression/correctness gates. CPU
submission and count-readback time are not kernel duration.

Group-local reductions of active-count/topology-XOR atomics remain a second option.
They can reduce contention but add neutral-lane participation and barriers; fewer
atomics alone does not establish a win. NVIDIA's reduction comparisons illustrate
this tradeoff rather than a universal fastest implementation. [Justin Luitjens,
Faster Parallel Reductions on Kepler, NVIDIA, 2014-02-13](https://developer.nvidia.com/blog/faster-parallel-reductions-kepler/).

Stage 2's 32768 cells align with 256-thread groups. Stage 6's 107811 edge slots do not:
the remainder is 35. A naive group XOR there would mix region fingerprints. Region-
aligned indexing or a segmented reduction would be required, expanding the change.
Transition totals number only 45, so transition scans are not bundled with S1.

Installed `core/shaders/common/thirdparty/bend_sss_gpu.hlsl` contains
`WaveActiveAnyTrue` and `WaveGetLaneCount`. This corrects an earlier overly broad
absence-of-evidence statement. It does not verify wave prefix/XOR support through
this project's VFX path. Microsoft specifies Shader Model 6 for WavePrefixSum;
S1 avoids needing that capability. [Microsoft, WavePrefixSum, updated 2021-01-30](https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/waveprefixsum).

## Arena memory: capacity is not live data

The accepted prior control reserved 672 MiB across 14 shared arenas:448 MiB for
vertices and 224 MiB for indices. Settled geometry including transitions occupied
377.805 MiB: 202.807 MiB vertex and 174.998 MiB index. Occupancy was 45.27 percent and
78.12 percent respectively; 6371 of 7168 record slots were allocated. The roughly
1.99 MiB of transitions does not explain the spare vertex capacity. These figures
come from the [prior production evidence](../ValidationEvidence/GpuMeshing512/b80cd7b7de3947c79c45d436b45b996d.json)
and the mesher's accounting, not a driver-memory estimate.

[Acquire and GeometryArena](../../Code/Voxels/GpuVoxelMesher.cs) require a free
record and contiguous vertex/index ranges in the same arena. The existing
[range allocator](../../Code/Voxels/GpuTerrainRangeAllocator.cs) is address-ordered
first fit with coalescing. Its global fragmentation statistic is not a percentage
of all committed memory wasted, nor does it identify which constraint forced growth.

VMA's statistics distinguish allocated blocks, used allocations and device usage;
detailed free-range reporting requires traversal. Apply those distinctions to
Voxels3's own buffers, without importing a Vulkan allocator into s&box. [AMD
GPUOpen/VMA maintainers, Statistics, accessed 2026-09-07](https://gpuopen-librariesandsdks.github.io/VulkanMemoryAllocator/html/statistics.html).

The initial schema 25 probe observed growth events and independent slot,
vertex/index free-capacity and contiguous-range rejections. Both control runs
observed nine slot-limited and five index-limited existing arenas at the one growth
event, with no vertex or fragmentation rejection. That supports testing M1; it is
not a census of every allocation or a lifetime per-arena peak.

Those controls failed publication-tail gates. A smaller reporting revision removed
the growth-time traversal, logging and rejection fields entirely. It retains O(1)
peak-count updates on arena creation, counts initialized at test start, and usage
and allocated-byte reporting at pre-trim/final settlement only. That control also
had timing failures on different metrics, so reporting overhead versus environment
remains unresolved. All failed results are preserved; final acceptance must also
pass the original unchanged control's gates.

Peak capacity matters: `TrimEmptyTrailingArenas` currently runs at Figure Eight
settlement, not continuously in ordinary play. Final post-trim count can hide a
larger moving allocation. There is no separate retired geometry-range queue;
retired visibility buffers are another resource category. This study leaves all
release and render-sequence rules intact.

M1 reduces only vertex arena bytes 32 MiB to 24 MiB, retaining index 16 MiB and 512 slots.
At 14 arenas this predicts 560 MiB, a 112 MiB or 16.67 percent reduction. A 15-arena
configuration would use 600 MiB but add potential draw/record work; the hypothesis
requires no increase in peak/final arena count or maximum indirect submissions.
It must save at least 10 percent of both peak and settled geometry arena capacity,
retain exact geometry, and pass the same timing/allocation gates. Per-arena growth
observations must first support testing the reduction.

Compaction is not the first choice. Free bytes can exist without a sufficiently
large contiguous range, but moving live GPU data also requires copy completion and
updated resource references. [AMD GPUOpen/VMA maintainers, Defragmentation,
accessed 2026-09-07](https://gpuopen-librariesandsdks.github.io/VulkanMemoryAllocator/html/defragmentation.html).
Likewise, per-LOD pools may strand capacity, and linear allocators depend on lifetime
orders this terrain does not guarantee. [AMD GPUOpen/VMA maintainers, Custom memory
pools, accessed 2026-09-07](https://gpuopen-librariesandsdks.github.io/VulkanMemoryAllocator/html/custom_memory_pools.html).

## Measurement gaps and research stopping point

| Claim | Confidence | Remaining test |
| --- | --- | --- |
| Stage 5 can preserve uint prefix order with the existing helper | High | Real geometry audit and cold shader loading |
| Cooperative stage 5 is faster | Unset | Fixed Figure Eight candidate and confirmation |
| Vertex arenas have excess aggregate capacity in this workload | High | Growth constraints and peak arena counts |
| 24 MiB avoids extra arenas and reduces total reservation | Medium | Real allocation, streaming and camera validation |
| Current count-readback timing identifies a GPU kernel | Unsupported | Installed API search did not establish a callable per-stage timestamp scope |

Installed XML describes GpuProfilerStats as scene-system timestamps, with smoothed
and decayed-max values intended for display. Current metrics have no meshing scope.
The inspected Graphics/ComputeShader surface did not establish a custom GPU timing
scope; absence in these sources is not proof no engine capability exists. No GPU
flush, extra readback or profiler-only mesher is added. The [official ComputeShader
API](https://sbox.game/api/Sandbox.ComputeShader/) corroborates dispatch capability,
not arbitrary per-stage timestamp control.

Discovery covered current shaders/allocator/lifetime, NVIDIA cooperative scan and
reduction techniques, Microsoft synchronization/wave semantics and VMA allocation
statistics/pool/compaction guidance. Follow-up checked subgroup-boundary correctness,
shared-array reuse and capacity denominators. Further broad searching is unlikely
to resolve the remaining performance questions; those now require production runs.

## Test outcomes

S1 is rejected. Moving GPU p95/p99 were 1.3404/1.8172 ms, versus
1.3411/1.7905 ms in the nearest-source control: effectively unchanged p95 and
1.49% worse p99, missing both required 5% benefits. It also failed CPU and
publication-tail gates. The original serial stage is restored, with no alternate
runtime path. [Raw result and comparison](../ValidationEvidence/GpuReductionsArena512/194a4c1736eb403c92849f2d39f52529-comparison.json).

All 6,203 recorded metadata checks passed. A separate audit read the actual buffers
of 104 regions: no invalid indices, non-finite/out-of-bounds positions, record
identity, oversized-triangle or draw-state failures. Its 46 flagged transition
regions had the existing degenerate-triangle issue; equal-distance selection is
not a fixed-subset comparison. Cold-start qualification of the rejected algorithm
was not pursued. [Emitted-buffer observation](../ValidationEvidence/GpuReductionsArena512/194a4c1736eb403c92849f2d39f52529-audit.md).

M1 reproduced a 16.67% capacity saving in two complete runs: peak 720 -> 600 MiB
and settled 672 -> 560 MiB, with the same 15 peak / 14 final arenas and maximum
15 draw submissions. The cold run passed earlier controls and emitted-buffer
checks. Its same-process size-restored control was interrupted before saving, so
final acceptance remains incomplete. A native shutdown access violation in the
previous editor also requires separate qualification; it did not occur during the
cold M1 run. [Interruption and shutdown evidence](../ValidationEvidence/GpuReductionsArena512/interrupted-control.md).

All definitions, failures and decisions remain in the [validation ledger](../ValidationResults.md).
No memory optimization is accepted yet. The current size is restored to 32 MiB
pending the final matched comparison; lean reporting remains an unaccepted working
change until final validation completes.

## Remaining decisions

Finish the matched 32/24 MiB comparison and qualify shutdown separately before
retaining M1. The measured reservation reduction is real, but an interrupted
control is not permission to waive acceptance. No FPS improvement is claimed.

For subsequent GPU work, stage 2's group-aligned active-count and topology-XOR
atomics are a narrower reduction candidate than stage 6's unaligned regions.
Investigate it only with neutral-lane participation, exact integer fingerprints,
and the same emitted-buffer and Figure Eight checks. S1's result rejects this
particular short tree-scan rewrite; it does not establish that every cooperative
reduction or wave-assisted scan is unhelpful. Per-kernel attribution and verified
wave-prefix compiler support remain useful missing evidence.

Arena compaction, per-LOD pools and a GPU-owned allocator remain unsupported by
these observations. None is needed to obtain M1's measured capacity saving, and
each adds ownership or lifetime responsibilities. Edited terrain, multiplayer,
lower-tier GPUs, long ordinary-play memory retention and simultaneous camera
isolation are outside the demonstrated workload.
