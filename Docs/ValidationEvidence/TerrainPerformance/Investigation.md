# Terrain texture performance investigation

The tested optimization recovered about **20â€“23% stationary FPS** while retaining
textures, normal maps, parallax strength, anti-tiling and 16x texture filtering.
It is **not accepted or left active**: a cold run contained an unexplained 1.32 s
frame, and a repeat still had a worse maximum frame than the original. The original
shader was restored byte-for-byte and compiled successfully. The candidate is
preserved as [a small patch](candidate-c.patch), not an adopted implementation.

## Where the time goes

The terrain shader multiplies sampling across contributing materials, triplanar
projections and stochastic anti-tiling patches. Per active material, the source
allows up to 27 final map operations plus 225 intermediate height operations near
the camera. These are upper bounds with early-outs, not measured instruction
counts; anisotropic filtering can require additional hardware taps.

POM fades between 2 m and 8 m. Color, normal and packed surface sampling continues
beyond that distance. The canonical route puts the player 10 m above base terrain,
so its moving result alone cannot characterize close-ground parallax. Camera
position, walls and edits can bring surfaces closer.

In a fixed close view containing grass, dirt and stone, temporarily disabling only
POM increased FPS from approximately 306 to 393: about **0.72 ms/frame** of apparent
POM cost in that view. Session drift prevents treating that as a universal GPU
measurement. The detached-camera GPU counter stayed exactly 3.83 ms across all
variants despite changing FPS, so it was excluded from GPU conclusions.

The supplied capture resolves to:
`C:/Program Files (x86)/Steam/steamapps/profiler_captures/sbox_2026-09-15_23_16_30.json.gz`.
Its uncompressed companion is 11,601,707 bytes. It contains 44 threads and roughly
12 seconds of process sampling, not exact frame boundaries or direct GPU timings.
CPU estimates use `samples.threadCPUDelta` in nanoseconds. Inclusive scopes overlap
and must not be added as independent costs.

| CPU capture observation | Sampled CPU estimate |
| --- | ---: |
| All process threads combined | 8,199 ms |
| Main thread | 3,005 ms |
| Main-thread `VoxelManager.OnUpdate` | 390 ms inclusive |
| Main-thread pending-placement commit | 155 ms inclusive |
| Main-thread readiness capture | 146 ms inclusive |
| Terrain depth/shadow render callback, per worker | 48â€“57 ms inclusive |
| Water chunk render callback, per worker | 68â€“78 ms inclusive |

The seven render workers show monitor waits/spinning around shared terrain-depth
and water rendering state. These are additional submission costs, not proof that
textures caused the CPU costs. Removing their locks would race shared attributes
and resource lifetime. Readiness queries also consume CPU while streaming.

No matched pre-texture capture establishes the entire historical 600+ to 300â€“400
FPS regression. At those rates, the frame budget changes from 1.67 ms to
2.50â€“3.33 ms. Small absolute costs have large FPS effects. Current GPU memory use
was about 2 GiB on a 32 GiB RTX 5090, so capacity exhaustion is not suggested;
texture bandwidth or latency can still matter. One route sample reported 92% GPU
utilization, 2902 MHz graphics, 14001 MHz memory, 64 C and 373.67 W.

## Controlled results

Engine 26.09.15, RTX 5090 / driver 616.64, Ryzen 7 9800X3D. Saved world
`f5ce10f3-6d75-428e-b3dd-63dee14891c6`, revision 2844, seed 1337, generator 48.
Gameplay radius 8; visual radius 256; LOD 0â€“5; half extents 4/8; 32 cells at size 16.
Source: HEAD `2c6ec13` plus existing uncommitted terrain/material/water work.

The final pair uses the same recorded center `(-1.6258175, 1.2225341)`,
2769Ã—1529 resolution, speed 2500, distance 50000, one canonical figure-eight loop,
automatic settlement and a 10-second stationary window. The candidate repeat uses
the same parameters but a warmed session. Startup initialization was briefly
paused to assign the exact center; the measured route was unpaused and interactive.

| Metric | Original, fixed resolution | Candidate, fresh process | Candidate, repeat |
| --- | ---: | ---: | ---: |
| Moving FPS | 343.80 | 335.92 | 385.11 |
| Moving GPU mean, ms | 2.439 | 2.342 | 2.196 |
| Moving frame p95 / p99, ms | 5.34 / 9.86 | 5.64 / 9.77 | 4.04 / 8.90 |
| Maximum moving frame, ms | 80.57 | **1322.28** | **192.46** |
| Maximum moving GPU time, ms | 9.04 | **161.73** | 9.58 |
| Stationary FPS | 274.76 | **337.27** | **330.83** |
| Stationary GPU mean, ms | 3.290 | **2.671** | **2.727** |
| Peak process memory, GiB | 4.67 | 4.56 | 4.66 |
| Peak GPU memory, GiB | 2.10 | 2.06 | 2.06 |
| Moving allocations, GB | 3.931 | 4.082 | 4.141 |
| Allocation per frame, bytes | 93,763 | 99,699 | 88,185 |

All three finished with collision ready 4913/4913, zero pending collision work,
zero collision failures and zero recorded exceptions. Raw results:
[original](original-fixed.json.gz), [candidate](candidate-fixed.json.gz),
[repeat](candidate-repeat.json.gz). Stationary FPS improved 20.4â€“22.7%; moving gains
are not consistent enough to claim a dependable global uplift. The repeat did not
reproduce the 1.32 s stall but did not resolve its cause. Cold-run maximum GC pause
was 11.29 ms and streaming synchronous maximum was 9.55 ms; neither explains it.
Benchmark setup, driver/engine stalls and external scheduling remain unisolated.

## Candidate and visual tradeoff

The candidate adds explicit early-out/loop hints, skips material contributions
at or below 0.00001, and changes the POM ray budget from 12â€“24 to 8â€“16 steps.
The tiny weight threshold avoids full map sampling for negligible contributions,
including possible floating-point sand remainders. The POM budget trades some
intersection precision for fewer height queries; linear intersection refinement
and all authored amplitudes/fades remain. No maps, geometry, draw distance,
material types or normal-map detail were removed.

Fixed view: camera `(64,0,340)`, angles `(35,0,0)`, FOV 60. Screenshots are
1280Ã—720; timed rendering stayed at the larger fixed viewport size. Inspection
found no obvious new stepping, hard patch edges or broad smearing. Compared with
the original image, candidate C has mean absolute RGB difference **0.00992/255**;
only **0.02691%** of pixels differ by more than 2 in any channel. This does not
qualify every material, grazing angle or camera-motion case.

- [Original image](original.png)
- [Candidate image](candidate-c.png)
- [Image difference measurements](image-differences.json)
- [Fixed-view observations](close-samples.json)

Candidate A used only hints and negligible-weight skips. Its initial apparent
16% FPS uplift shrank when the original shader was restored; session drift was
material. Candidate B reduced only intermediate height-query anisotropy to 4x;
its additional benefit was too small to justify adoption. Candidate C keeps 16x
filtering everywhere and uses fewer ray steps. All variants and failed attempts
remain recorded in [the validation ledger](../../ValidationResults.md).

## Acceptance and remaining work

The original shader SHA256 is
`23469486F30E5BE10307B529929A679E7F30743E7D072280FAD969F0C205D01D`.
Candidate C is
`E4004F1B6FC51BE78C62535F9EB2E0165D3C83F910AA1F7D9164ACA812CEB2DC`.
The original scene source was also restored exactly. No runtime source change
from this investigation remains active. The editor remains in playable mode at
the benchmark resolution; the user's existing uncommitted work is preserved.

An engine shutdown error occurred with the original shader restored. Subsequent
saved, stopped editors required forced termination when normal close did not exit.
Fresh candidate startup loaded and rendered successfully, and the crash marker
did not advance past that earlier shutdown event. This is not a claim that the
engine's clean-shutdown problem was fixed.

Several preliminary runs were excluded or qualified: shader compilation during a
repeat, log-reading interference, and an automatic viewport/DPI change after
restart. The resolution API silently does nothing before the game viewport exists;
final runs set it after starting play and verify dimensions in their saved results.
These corrections and all failures are retained rather than overwritten.

The next implementation decision needs a trace that attributes the worst-frame
spike. Larger rendering gains would require measuring and reducing repeated
projection/anti-tiling samples, especially after parallax has faded out. This
report does not approve a quality reduction or claim a return to 600 FPS.

Raw result JSON is stored losslessly as gzip; decompress to inspect the complete engine output.
