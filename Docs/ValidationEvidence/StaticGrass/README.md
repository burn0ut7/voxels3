# Static grass prototype qualification

Accepted: `GRASS-001/v1-reduced`, run `93f6ac8d3ec24cc6b3ca810b30bc481e`.
Static, opaque, single-triangle blades use published LOD0 terrain grass weights.
Density is approximately 43 blades/m² before material and distance rejection.
Full density reaches 6 m, thins to 30% at 12 m and 8% at 16 m, and ends at 20 m.
No wind, physics, per-blade objects or grass shadow pass.

| Metric | Control | Accepted prototype |
| --- | ---: | ---: |
| Moving FPS | 510.93 | 562.41 |
| Stationary FPS | 504.67 | 481.48 (-4.60%) |
| Stationary p95 / p99 | 2.8095 / 4.0268 ms | 2.9292 / 3.7523 ms |
| Stationary whole-frame GPU mean | 1.4712 ms | 1.7415 ms (+0.2703 ms) |
| Moving peak process memory | 4.867 GB | 4.884 GB |
| Moving peak GPU memory | 1.536 GB | 1.587 GB |
| Moving allocations/frame | 60,019 bytes | 58,053 bytes |
| Exceptions / collision failures | 0 / 0 | 0 / 0 |

All recorded 10% core regression gates and the additional 0.3 ms stationary GPU
budget pass. The observed moving improvement is not attributed to adding grass.
Peak grass count was 11,919 with no capacity overflows; roots occupy 2 MiB per
camera. The hard cap is 65,536 blades. This is one-player, one-hardware prototype
qualification, not a general multiplayer or maximum-throughput claim.

## Evidence

- [Comparison](comparison.json), [environment](environment.json),
  [source hashes](reduced-hashes.json), [preflight](reduced-preflight.json),
  [final diagnostics](reduced-collision.json), [grass counters](reduced-grass.json).
- [Control result](control-753d2673f36a4268a0dacf24ab3277c8.json.gz),
  [accepted result](reduced-result.json.gz), [accepted session](accepted-session.log.gz).
  Compressed files retain every original byte; [manifest](compressed-evidence.json)
  records uncompressed result hashes.
- [Close blades](grass-close.png), [unchanged repeated view](grass-close-still.png):
  decoded pixels are identical, including blade silhouettes.
- [6 m](grass-6m.png), [12 m](grass-12m.png), [20 m](grass-20m.png): the same center
  patch visibly thins and then has no blades. Camera readbacks are adjacent files.
  Grass/dirt/stone boundaries remain distinct.

The existing uncommitted 64–128 m terrain texture fade was held constant for both
control and grass runs. Its source hash is in the environment record; that change
is outside this grass commit. The ledger records the complete fixed workload.
Fresh live digging and dedicated snow/sand/water views were not separately run.
Grass derives its placement and invalidation from published terrain geometry;
these untested cases are not claimed as separately verified.

## Retained rejected candidates

`candidate-57e...` records the three-triangle version: stationary FPS fell 21.25%.
[Its patch](rejected-three-triangle.patch.gz), relative to the accepted shaders,
reconstructs the exact rejected shader hashes. Omitting only raster drawing
returned near control cost; [diagnostic patch](generation-only-screen.patch.gz)
and [screen](generation-only-screen.json) preserve that temporary experiment.
The omission is absent from production.

`final-*` names refer to the earlier single-triangle, full-density candidate,
not the accepted run. Its core gates passed but the extra stationary GPU delta
was 0.3405 ms, above the 0.3 ms target. Density was then reduced from one blade
per 24 to one per 36 square world units. World, route and acceptance gates did
not change. The original first candidate collision reply includes a stale
preflight view; the ledger and later explicit final-view record distinguish it.
