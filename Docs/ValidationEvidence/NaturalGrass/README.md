# Natural grass qualification

The user rejected sparse individual spikes. The replacement uses five shorter,
curved leaves per surface-rooted tuft, varied height/lean/color, and softer
two-sided lighting. The final candidate uses two opaque triangles per leaf.
Placement still consumes published LOD0 terrain and canonical grass weights.
There are no vegetation objects, textures, alpha cards, simulation or grass shadows.

## Fixed workload

GRASS-NATURAL-001/v2 is defined in the validation ledger before its baseline.
It retains the canonical figure-eight: seed 1337, generator 48, basic_example,
one player, gameplay radius 8, visual radius 128, LOD 0..5, extents 4/4,
start XY (-1.6258175,1.2225341), Z 340, zero eye angles, FOV 75,
2769x1529, speed 2500, distance 50000, one loop, 393.7008 clearance,
automatic drain and ten seconds stationary. Input remains enabled.

The user's saved terrain advanced to revision 3989 with 873 pages. This differs
from the historical grass benchmark (3984/868), so v1 screening is preserved
but not used as a matched baseline. The v2 before/after pair uses the current
saved world. Existing uncommitted terrain-texture fade changes are identical
between sources and excluded from this change. Hashes document that identity.

Gates retain at most 10% regression in FPS, p95/p99 frame time, peak process/GPU
memory and allocation rate; zero project exceptions/collision failures;
settled streaming and zero root overflow. Additional stationary whole-frame GPU
target: no more than 0.3 ms over the fresh baseline. No capture during timing.

## Preserved candidates

- `before-player.png` captures the original view and isolated dark spikes.
- `before-close.png` uses the fixed close camera (0,-250,267.93129), Euler
  (25,0,0), FOV 75, 1440x900. `final-close.png` repeats it after the final cold start.
- `candidate-close.png` is the rejected sparse, taller tuft preview.
- `dense-close.png` and `dense-low.png` show the denser three-triangle leaves.
  This candidate failed stationary p95: +12.88%, despite passing other gates.
  `dense.json.gz`, `dense-comparison.json` and `dense-source.patch.gz` preserve it.
- The final candidate keeps density and five leaves per tuft while reducing
  geometry from 15 to 10 triangles per tuft. The shape has a narrow root,
  bent wide middle, and tapered tip. Root storage remains 2 MiB per view.

Full raw results are losslessly compressed, with SHA-256 files. Preflight and
final snapshots preserve source identity, camera, world, settings and resolution.
The comparison JSON contains every recorded frame, memory and allocation gate.
Cold logs preserve unrelated missing stock resources instead of claiming an
error-free engine installation; project shader/parser/dispatch errors and the
Sentry crash marker are checked separately.

Only this hardware and one-player workload are measured. Fresh live terrain
edits, loaded multiplayer, other GPUs, and individual snow/sand/water examples
are not requalified by this appearance change.

## Accepted result

Run `4e7db047ad1c4c20a8f106204a825306` (`culled.json.gz`) passes every fixed gate.

| Measurement | Before | Final |
| --- | ---: | ---: |
| Moving FPS | 520.81 | 523.24 |
| Standing FPS | 486.06 | 466.13 |
| Standing p95 frame time | 2.9101 ms | 3.1323 ms |
| Standing p99 frame time | 3.9967 ms | 4.0104 ms |
| Standing whole-frame GPU | 1.54965 ms | 1.64244 ms |

The standing FPS cost is 4.10%; additional GPU time is 0.09279 ms. The small
moving FPS increase is observed variation, not an attributed acceleration.
All memory/allocation gates pass, with zero exceptions and collision failures,
4,913 collision regions ready and settled streaming. Peak 3,999 generated tufts,
zero overflow; the two draws submit at most 79,980 triangles in that route.

Reducing leaves from three triangles to two alone still failed stationary p95
(+11.91%); `optimized-*` preserves that result. The final change also rejects
conservative padded source-triangle bounds outside the view/range before root
sampling. It keeps visible tuft density and uses the existing guarded frustum
function. `image-comparison.json` records nearly the same small image variation
for culling versus no culling as for two unchanged final captures. Neither is
pixel-identical; the screenshots show no lost edge tufts.

`final-skyline.png` retains tips over the skyline. `range-6m.png` through
`range-32m.png` show thinning; 24 m marks are mostly subpixel and the central
32 m patch is bare of visible geometry. `final-player.png` records the restored
normal third-person view; it is not a matched camera for `before-player.png`.
The close images are the matched before/after comparison.
