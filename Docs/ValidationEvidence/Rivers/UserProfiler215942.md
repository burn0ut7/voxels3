# User profiler capture 215942

Source: `C:\Program Files (x86)\Steam\steamapps\profiler_captures\sbox_2026-09-09_21_59_42.json.gz`.
SHA256: `6499d43de9f08a59b3f87dc128c7b0c309edd0f58a9a41226421da14957d761c`.

The capture contains 8,405.7729 ms wall time, 7,698.758377 ms summed sampled CPU
time, 47 threads and 42,973 resolved samples. Metadata declares CPU deltas in
nanoseconds; the summary converts them to milliseconds. Sampling interval is0.2ms.

River atlas packing consumes4,348.467531ms inclusive (56.48% of sampled CPU).
The spatial-tree comparer alone consumes1,811.875653ms leaf CPU. Tree construction
accounts for3,857.30395ms inclusive and unique-segment extraction465.199046ms.
These inclusive values overlap and must not be added. Collision mesh construction
accounts for30.060466ms in this capture, so it does not explain this sampled load.

1,105 allocation tick markers report572,474,232bytes, including188,436,304bytes
attributed to Segment arrays and154,774,528bytes to segment hash entries. Allocation
ticks are sampled allocation accounting, not an exhaustive object inventory.
Two nonconcurrent GC markers span4.5487ms total; one is InducedLowMemory.
There is no GPU shader timing in this capture. The engine loaded profiler symbols
during the subsequent movement run, which remains recorded with that limitation.

Source inspection identifies isolated per-lane atlas reuse: lanes could reuse
their own texture but ignored completed covering data from other lanes and the
terrain/water presentation atlas. The fix caches completed numerical atlases and
reuses only exact matching patch footprints and recipes/versions, within64entries
and64MiB. A broader superset policy was rejected after controlled GPU/FPS regression.
GPU textures remain lane-owned; visible water remains resident-chunk-owned.
Counters and real-world validation are recorded in the validation ledger.
