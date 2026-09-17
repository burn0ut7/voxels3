# Standstill optimization evidence

The [investigation](../../Research/TerrainStandstillOptimization.md) explains
decisions. `TERRAIN-STANDSTILL-OPT-001` in the
[ledger](../../ValidationResults.md) declares scenarios, preserves failed runs
and records acceptance. v1, v2 and v3 camera configurations are separate
comparisons; do not join their FPS values into one improvement claim.

## Final comparison

- `v2-small-cache-control.json`, `v2-combined.json`, and
  `v2-combined-restored-warm.json`: fixed-view A/B/A screening, about +29% FPS.
- `v3-route-original.json.gz`, `v3-route-candidate.json.gz`, and
  `v3-route-comparison.json`: fresh-process canonical figure-eight comparison.
- `v3-original-idle.json`, `v3-candidate-idle.json`: three matched stationary
  windows each, 471.10 to 563.97 FPS (+19.71%). The nominal 20% target is narrowly
  missed in this view; route regression and correctness gates pass.
- `production-source-hashes.json`: exact final runtime source identity.
- `v3-original-clean-cold-ready.json`, `v3-candidate-cold-ready.json`, associated
  cold-world records, final console setting records, and
  `final-world-inspection.json`: world, geometry, configuration and readiness.

## Appearance and startup

`v2-original-*` and `v2-combined-*` PNGs show the paired close character/terrain
shadows, cut-face material detail, and distant landscape. The grazing-grass name
predates inspection: that camera faces an existing dirt cut, with some grass at
its edges. `v3-original-full.png` and `v3-candidate-full.png` show actual
fresh-session rendering at the matched return view. The full-view v2 combined
image retains close grass detail, with a rearranged finite repeating pattern.

`v2-fog-control-full.png`, `v2-fog-blend.png`, and
`fog-image-comparison.json` isolate fog: 99.497% of pixels match exactly and the
RGB mean difference is 0.001712/255. This image tolerance is not applied to the
deliberately rearranged grass pattern or distant shadow precision.

Fresh-log archives and findings retain the stock blue-noise/header and missing
content errors present in both processes. They contain no final project shader,
parser, pipeline or managed exception failures. The existing Sentry crash marker
did not advance. `original-close-dialog.json` and
`original-cold-close-dialog.json` preserve the native mimalloc double-free error
on editor shutdown; clean startup/rendering passed, normal shutdown is not fixed.

## Screening and failed operations

Other JSON records retain the constant-material, unlit, defined-input,
diffuse-only, flat-output, larger grass bake, shadow-count/resolution, and native
GPU-scope experiments. GPU overlay phases are excluded from ordinary FPS
comparisons. Shader/material patches are compressed research evidence only;
they are not runtime alternatives. The production grass manifest and build
utility own the retained asset generation.

Failed mounted-path compiles, the first production include lookup failure, the
disturbed v1 camera comparison, and the insufficiently warmed first restored
sample are preserved. See the ledger for why each is excluded from matched gain
claims. Never use the 265.6 FPS restored outlier as a favorable baseline.
