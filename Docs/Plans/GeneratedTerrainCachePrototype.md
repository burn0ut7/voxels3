# Generated terrain cache prototype

User authorized 2026-09-09. Status: **rejected and removed at user request**.
The following is the historical experiment plan, not the current implementation.
Only existing authoritative edited-page persistence remains. See [results](../Research/GeneratedTerrainCacheExperiment.md).

## Experiment

Measure whether persisting exact generated density samples improves reopening
and revisiting this smooth voxel world. This is disposable derived data, separate
from authoritative settings and saved player edits. It is not yet a baked-world
format or a promise of faster loading. Existing generator remains canonical.

Cache the exact regular GPU halo and transition face/normal-halo arrays emitted
by the existing kernels. Cache CPU collision lattice base samples separately:
CPU and GPU results are not substituted for each other. Only fully unedited GPU
regions qualify in this prototype; edited regions continue the existing path.
CPU base samples may be combined with the current authoritative corrections.
Continuous edge refinement, conservative bounds, meshing and collision creation
remain unchanged and their costs must remain visible in results.

One manager-owned cache shares a bounded worker-backed file store among GPU and
CPU consumers. Identity includes cache/backend version, generator recipe, region,
LOD/spacing, sample layout and CPU/GPU provenance. No generated result can modify
world edits. Disk records validate identity, count, checksum and finite samples.
An invalid/missing disposable record regenerates, with a reported counter.
File work runs off render/main thread. GPU capture uses asynchronous readback;
cache misses do not introduce synchronous GPU downloads. Keep callbacks safe
through teardown, queue/memory/disk budgets explicit, and stale world jobs unable
to publish gameplay state. Warm cache retention is bounded; no infinite-world
accumulation in RAM. Prototype disk storage is capped rather than unbounded.

Expose a production cache enable setting and counters for hits, misses, pending
IO/captures, bytes, failures and skipped writes. Changing this setting takes
effect at Play startup. The off mode uses the same generation implementation.
Do not duplicate generators or meshers for tests.

## Validation

Use the current playable basic_example world at seed1337, LandAmount0.75,
MountainAmount0.35, PlainsAmount0.6, scales131072/32768/8192, relief3072,
ruggedness0.45, SeaLevel0,32cells16units,LOD0..6/4/8,gameplay8,host/no peers.
Preserve saved revision and user edits; verify identity before comparison.
Record source hashes, engine, cache state, timing start, player position,
collision/visual readiness, frame windows, memory and generated-cache counters.

Compare off baseline (three starts), enabled empty-cache population, enabled
saved-cache reopen (three starts), and retained in-memory state. Do not call
ordinary OS-cached file reads physically cold disk IO. Fresh cache means no
prototype records for the recipe; scene restart empties scene-owned RAM caches.
First-ready, full terrain drain and cache write drain are separate timings.
Cache hit counts must prove reuse; no-work resident observations do not prove
disk throughput. Match terrain fingerprints and real geometry diagnostics.
Report all runs, including interruptions, invalid prerequisites and regressions.
The canonical travel regression gate still applies before final acceptance;
its pending return correction is not silently applied by this prototype.

## Alternatives and limits

Persisting meshes would bypass different work and requires a separately keyed
geometry cache; it is deferred until this experiment measures the remaining cost.
A CPU-only cache would leave expensive GPU regeneration untouched, so it is not
sufficient coverage for this experiment. Baking one uniform density lattice and
interpolating arbitrary queries could change caves/seams and is outside scope.
Future river/road/POI layouts should have regional feature records; this slice
does not implement them or simulate unloaded entities.


## Implemented candidate details

The cache uses128MiB retained sample payloads,32768metadata entries,32 pending
read/write jobs and a combined32-job/capture admission check for GPU readbacks.
Individual arrays are limited to35^3 floats. Disk records under
`FileSystem.Data/generated-density-v1` are capped at2GiB; they are uncompressed.
Limits are prototype safety bounds, not a tuned infinite-world storage design.
Skipped captures/writes are counted and the canonical generator remains usable.
OS/driver staging allocations and GC-retained arrays are additional to retained
payload accounting and must be measured separately.

CPU collision lattices save only sampled positions, with NaN meaning unsampled;
those positions are evaluated by the original sampler if later needed. GPU files
require every sample finite. CPU/GPU namespaces differ. Checksums bind bytes to
recipe and spatial/layout key. No player-edit data or meshes enter this cache.
GPU cache hits skip the density-sampling dispatch when an entire batch is
cached; mixed batches regenerate. The final candidate restores the original
shader kernels byte-for-byte and adds no shader attributes or buffer bindings. Refinement still evaluates the
continuous generator, so this does not eliminate all terrain-function calls.
Corrupt records count as misses/errors and fall back to generation. Current
immutable-file prototype does not repair existing corrupt cache files in place.
Queued work may finish disposal-safe IO already begun, but cannot republish live
terrain or mutate edits. New work is rejected after cache disposal.
