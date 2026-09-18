# Coherent meadow wind

The existing GPU root-generation pass evaluates two travelling sine waves once
per retained tuft. A broad 20 m front travels at 4 m/s along world XY (0.8, 0.6);
a smaller 5 m crosswind ripple adds variation. Neighboring plants share the field.
The five leaves vary their response slightly while keeping identical timing.
Height-squared bending pins their roots, moves tips most, and lowers leaning tips.
The depth and color passes reuse exactly the same cached wind sample.

Wind and the existing variation fraction share the previous 32-byte root record.
Root capacity remains 65,536 / 2 MiB per view, with the same two draws, 30 vertices
per tuft and no added dispatch, texture, CPU plant updates or network state.
Conservative culling includes the maximum wind sweep. The shared include owns
the direction, wave parameters and deformation bound. Variation is quantized to
16 bits and bend stored as a half float; world placement is unchanged.

## Qualification

GRASS-WIND-001/v1 was defined before timing. The saved world advanced to revision
3991/pages873, so a fresh static-grass baseline uses that world with the unchanged
canonical route, 64 m range, FOV75 and verified physical2769x1529. The previous
3989 results remain historical; they do not substitute for the new baseline.
Raw results, source hashes, SHA256, preflight/final diagnostics and cold-start
evidence are retained here. No screenshots are taken during timed windows.

The static baseline has significant frame-time outliers (including a390 ms GC
pause) despite normal GPU times. Preserve them; do not describe a faster candidate
as evidence that adding wind speeds up the game. The measured GPU delta and
source's unchanged CPU/render resource responsibilities help interpret the cost.
An existing unload-save null reference preceded baseline timing and shader edits;
the current saved3991 world was read back intact.

Candidate `14f1601221e14b85b0aefc00d40f6623` passes every fixed comparison gate
against baseline `27c3142fec5e4c6092e3012d9186ad7b`. Moving: 478.95 FPS,
p95 3.8222 ms, p99 6.2923 ms, GPU 1.69167 ms (+0.03821 ms). Standing:
430.40 FPS, p95 3.0551 ms, p99 4.7453 ms, GPU 1.88533 ms (+0.16490 ms).
Moving process/GPU peaks rise 2.26%/3.23%; standing 4.63%/3.18%.
Allocation/frame decreases in both windows. There are zero timed exceptions,
4913 ready collision regions with no pending work or failures, settled streaming
and zero grass overflows. See [comparison](comparison.json) for all exact values.

Explicit shader compilation and visible cold startup succeeded on engine26.09.15.
Fresh PID61576 reports zero project compile errors; Sentry stayed at the previous
shutdown marker. Stock resource errors and a pre-timing pipeline-cache rename
warning are preserved in [cold findings](cold-findings.json); they are not
grass parser, pipeline-creation or dispatch failures.

Each fixed camera has 24 timestamped native frames covering 7–10 seconds.
The `*-wind.gif` files are sampled previews with their actual capture intervals,
not recordings of game frame rate. Full PNG frames 0/8/16/23 and SHA256/time
manifests are retained. Close views show shared tip motion with roots remaining
at the same ground points; meadow and skyline sequences retain leaf silhouettes,
bare-material boundaries and smooth spatial variation across the canopy.
This qualifies one player on the recorded RTX5090. Other hardware, multiplayer
load, fresh terrain edits and synchronized cross-client wind remain untested.

Native range0/32/64/128 and fixed overhead40/80/128m images were captured after
timing and inspected. Zero removes grass; increasing range reveals the expected
distant plants, with no central grass at the endpoint. `range-diagnostics.json`
retains tool results; its scalar readback covers range0 and earlier64m samples,
not a separately measured128m peak. Prior range-menu input verification was
stopped by physical Escape again and remains pending.
