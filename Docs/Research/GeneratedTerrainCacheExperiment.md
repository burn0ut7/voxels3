# Generated terrain persistence experiment

Date: 2026-09-09. **Rejected and removed at user request.**

The measurements and implementation descriptions below document the completed
experiment. Its cache class, setting and runtime integrations are now removed.
Existing recipe and edited-page persistence remains unchanged.

## Result

The final prototype had no demonstrated median loading improvement. It was
initially left disabled and subsequently removed. This rejects adopting this implementation as a
performance improvement, not generated-world persistence in general.

Final comparison in editor31784, with the same world/revision64 and settings:

| Case | Three full-drain times | Median |
| --- | --- | --- |
| Cache disabled | 30.468 / 30.485 / 31.375s | **30.485s** |
| Saved generated cache enabled | 50.922 / 30.578 / 27.187s | **30.578s** |

The cache-on median is0.3% slower, which does not establish a meaningful speed
change from three observations. Its first reopen is markedly slower. Later runs
improve as the session warms; do not claim that the best single run proves a
repeatable gain. All cache-on runs performed real reads, with13419–13482cache
hits,105misses and0writes/failures. GPU hit counts represent batches that actually
skipped sampling; CPU hits may still fill unsampled lattice positions.

Disabled frame windows were408–447FPS with p996.58–7.78ms. Enabled windows were
382–519FPS with p995.83–11.35ms. These are reported10-second windows, not whole
route percentiles. Final disabled process peaks were4444–4478MiB; enabled peaks
5086–5525MiB. Hotloads, repeated Play and garbage awaiting collection confound
steady-state attribution;128MiB retained cache is not its total process cost.

Retained-world restore comparisons are reported separately below: those requests
reuse existing geometry and are not disk-cache loading measurements.

### Earlier candidates and environment differences

The original preprototype baseline in editor36600 was24.172/25.032/25.844s.
The editor restart changed timing enough that this is historical context rather
than the denominator for the final same-editor comparison above.

The first shader-mask candidate populated an empty cache in30.687s and wrote
1.936GB under backpressure. Its first saved-cache reopen exceeded60s because
unready requests were moved to the queue tail and loaded arrays could be evicted
before use. Preserving queue heads eliminated excess repeat reads in the next
observation, but that candidate still exceeded60s. A later, more fully populated
cache loaded in38.781s. Those earlier results do not qualify the final candidate.
Failures remain recorded; later snapshots did not establish exact drain times for
the60s timeouts. Do not average the censored failures as60-second completions.

The final candidate removes the shader-mask change entirely and skips a whole
sampling dispatch when all requests in the batch are cached. Its GPU kernel
source is byte-identical to the preprototype source. Default remains disabled.

### Disabled-source control and warm restores

An additional consecutive source-control pair in the same editor measured
**25.719 seconds** with the exact preprototype CPU source and **25.735 seconds**
with the final prototype restored and disabled. That observation shows no
meaningful off-mode slowdown. It does not erase the earlier slower runs or
replace the required travel benchmark. Frame windows were 535.9 versus 537.1 FPS,
with p99 frame times of 5.49 versus 5.63 milliseconds.

Final same-session saved-world restores reused resident geometry:

| Case | Three restore commits | Median |
| --- | --- | --- |
| Cache disabled | 22.9382 / 33.0520 / 29.2028 ms | **29.2028 ms** |
| Cache enabled | 21.5884 / 30.0598 / 44.3619 ms | **30.0598 ms** |

Generated-cache counters did not change during these restores. Their similar
latency measures the existing resident-world restore path, not disk chunk loads.
Tool/poll observation bounds were 0.141 to 0.203 seconds; the table uses the
existing internal restore-commit measurement.

### What the timing means

Startup timing begins after the Play tool returns and ends at first observed
full terrain drain. It is not total application launch time, first visible terrain
time, per-chunk latency or physically cold disk performance. Editor and OS file
caches can stay warm across Play restarts. A fresh Play destroys scene-owned
sample and geometry caches. The cache population state and source changed
between early candidates; only the final mode pair is the selected comparison.

## What the prototype actually saves

One generated-cache owner serves CPU collision workers and GPU meshing. It saves
exact regular density halos and transition face/normal-halo samples produced by
the existing GPU kernels; CPU collision lattice samples have their own namespace.
CPU partially sampled lattices retain an explicit unsampled marker. CPU and GPU
sample values are never substituted for one another.

Unedited GPU regions qualify; edited GPU regions keep the existing field path.
CPU cached base values still receive current authoritative corrections. Recipe,
spatial coordinates, spacing/LOD, backend/layout and cache version select records.
Checksums protect record contents and identity. Cache failures fall back to the
same generator and are counted. No terrain edits, meshes, roads, entities or POIs
are introduced by this experiment.

The final candidate skips the original density-sampling dispatch for wholly
cached batches; mixed batches regenerate. GPU kernels match the original source.
The earlier shader-mask candidate and its results remain recorded separately. Continuous edge refinement
still samples the generator; geometry extraction, seam work and collision creation
still execute. Therefore these results are not a test of loading completed meshes.

The inspector setting **World Saving > Cache Generated Terrain** takes effect on
Play startup. The default is off. In the prototype, regular GPU hits require the whole
submitted batch to be cached; CPU hits may still fill previously unsampled cells. Cache files live separately from world saves in
`FileSystem.Data/generated-density-v1`. The uncompressed prototype caps disk use
at2GiB, retained sample payloads at128MiB, metadata at32768entries and worker
jobs at32. Allocation/driver staging and garbage awaiting collection are additional
memory costs. This first backend does not compact files or repair corrupt records
in place; it regenerates on invalid reads. It is not an infinite-world save format.

## Correctness and performance limits

Every completed enabled observation matched the original rendered topology
fingerprint473FFDE4AD1E3FE1 and position fingerprint92FAEEE7BEE60656. Observed
cache IO failures were0, and completed runs had4913collision regions ready with
zero collision failures. These checks support unchanged terrain for the measured
scene, not exhaustive boundary/edit/multiplayer or cache-corruption qualification.

Same-session restores kept all geometry resident and left generated-cache counters
unchanged. Their sub-second result demonstrates the value of existing RAM reuse;
it does not show rapid reloading after eviction. Evicted travel and the canonical
figure-eight acceptance remain unqualified. The pending test-return correction
was not applied. No performance regression is accepted for normal gameplay.

## Next decision

Prioritize retaining useful resident geometry and diagnosing seam/mesh completion
cost. If persistence is expanded, test compact regional data and shared feature
layouts for rivers/roads/POIs; do not extend this raw one-file-per-sample-array
prototype directly into a shipping world format. A separately versioned cache of
completed derived geometry could avoid more of the measured work, but its storage,
invalidation and GPU upload costs need their own bounded experiment.

Method and implementation scope: [prototype plan](../Plans/GeneratedTerrainCachePrototype.md).
Exact scenarios, failed runs and environment notes: [validation ledger](../ValidationResults.md).
Raw JSON observations: `Docs/ValidationEvidence/Water/cache-*`.
