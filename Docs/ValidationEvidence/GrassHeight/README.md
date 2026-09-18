# Meadow height variation

The user requested slight taller, shorter and similar-height grass. The compute
shader now reuses the existing smooth color field for a patch height multiplier
from 1.15 in green areas to 0.85 in warmer areas. Intermediate areas retain the
same average height. Stable per-tuft variation narrows from 22–38 to 25–35 units
before applying that multiplier, keeping variation within coherent patches.
Per-leaf variation and wind remain unchanged. Maximum tuft/leaf lengths are
40.25/42.2625 units; the compute culling bounds include the taller leaves and
their full lean/wind envelope. This adds no noise call, root storage or draw.

GRASS-HEIGHT-001/v1 uses the canonical figure-eight parameters and gates from
the accepted color run `e0034107aafc4d3ebd1411d4093dfcc4`. The concurrently
requested water shading and this height change were initially tested as one
combined source candidate. Its deltas cannot be attributed to either change
alone. After that failure, height was qualified independently, as recorded below.
The water task owns the shared ledger; both tasks held production writes during timing.

The immediate explicit compile attempt after writing the shader returned the
known temporary mounted-source lookup error. `compile.json` preserves that
failed attempt. The water task retried the native compile after asset refresh
and reported success with no warnings; the retained
[successful compile](shared-grass-compile.json) independently
records Success=true and no output. The later isolated cold startup also records
successful grass and original-water shader compilation before restart.

The three `preview-*.png` images were captured through the real ejected runtime
camera at the fixed v1 positions and inspected. They show varied tips and gentle
canopy height changes while retaining long, dense coverage and attached roots.
No tall tips appear clipped in those views. They are hot-preview evidence;
final startup and performance qualification are separate. The water task's
bank camera was restored immediately afterward without moving the player or
changing the viewport resolution.

The final `final-*.png` views were captured and inspected after the coordinated
fresh visible editor startup, PID 87372. They confirm the same subtle canopy
variation, dense long coverage, attached roots and intact tips. The camera was
explicitly ejected before positioning; all camera setters and captures succeeded.
The source hash is unchanged from the hot preview. See
[visual source manifest](final-visual-source.json) and the shared
[cold-start log](shared-visual-cold.log.gz). The water task's earlier
startup-camera null reference occurred before these repaired successful views
and before timing; it is retained in its evidence rather than treated as a pass.

## Combined run: failed

Run `9bfe340e3a304f4da713fde305a1b357` used the fixed workload at 2769x1529 and
the unchanged final height source. Moving checks passed: 473.37735 FPS (-4.40%),
p95 3.8348 ms (+5.20%), p99 6.4708 ms (+2.62%). Standing checks failed:
p95 3.1352 ms (+20.91%), p99 5.2171 ms (+24.45%) and process peak 5341335552 B
(+10.53%). Standing FPS 408.6179 (-9.54%) and GPU 2.0538855 ms (+0.1438314 ms)
were within their limits. Timed exceptions were zero. See the retained
[raw combined run](shared-combined.json.gz) and
[comparison](shared-combined-comparison.json).

The result is an overall FAIL; the height change was not accepted from this
run. This candidate includes both water and height, so it does not
identify which change or environmental condition caused the failures. One
observed procedural difference is that the accepted baseline had no screenshot
captures after cold startup and before timing, while this candidate had grass
captures and water parameter/motion captures. This is a potential confound to
investigate, not proof of a cause or permission to discard the failed run.

## Height-only qualification

The water task expanded to a user-requested river-junction correction. The
tasks therefore separate source qualification: water's four source files were
checkpointed byte-for-byte and temporarily restored to their accepted 8970d34
versions. The height shader remains unchanged. The exact v1 workload and gates
continue to compare against e0034107, with no post-startup capture or compile
before timing. The combined failure above remains a failure; source isolation
does not establish what caused it. The shared ledger contains the pre-run
amendment. Water checkpoint hashes are retained for exact restoration afterward.

Isolated run `2d29e7277e1b40ab86bc4ff2778154f0` passed every unchanged gate.
Visible PID 89600 started at 13:19:23 after shader compilation, with no captures
or recompilation before timing. Both profiler windows record 2769x1529. The
world remained revision 3991/pages 873; only lifecycle checkpoint advanced to
159. All nine source hashes match the cold startup, including original water
and unchanged unrelated terrain. Sentry did not advance. The fresh log contains
eight stock resource warnings/errors and no grass parser/pipeline/dispatch error.

| Metric | Moving | Standing |
| --- | ---: | ---: |
| Average FPS | 475.64 (-3.94%) | 457.07 (+1.19%) |
| Frame p95 | 3.772 ms (+3.48%) | 2.7128 ms (+4.62%) |
| Frame p99 | 6.1758 ms (-2.06%) | 4.36 ms (+4.01%) |
| Average GPU | 1.73752 ms (+0.06561 ms) | 1.78031 ms (-0.12974 ms) |
| Process memory peak | -0.14% | +0.50% |
| GPU memory peak | -0.14% | +3.15% |
| Allocation per frame | +6.28% | -0.45% |

Timed exceptions, collision failures and grass overflows were zero. All 4913
collision regions were ready; visual/transition/placement work was settled.
Grass reported 6006 current tufts, 8075 peak candidates, capacity 65536 and
unchanged 2097152 root bytes. No writes, captures or native diagnostics occurred
during timing. This is fixed single-player RTX 5090 qualification, not a speedup
claim or evidence for the separate water changes, other hardware or multiplayer.

[Raw result](isolated.json.gz), [comparison](isolated-comparison.json),
[preflight](isolated-preflight.json), [final diagnostics](isolated-final.json),
[cold startup](isolated-cold.json), [source identity](isolated-source.json) and
[final log](isolated-final.log.gz) retain the measurements and conditions.

After saving the result, Play stopped and the four water checkpoint files were
restored byte-for-byte with verified hashes; see [restoration](water-restored.json).
At the water task's explicit request, editor restart and restoration of the
latest saved user pose were handed back to that task immediately. The earlier
combined evidence has immutable local copies described in
[shared evidence](shared-evidence.json), so this height record stands on its own.
