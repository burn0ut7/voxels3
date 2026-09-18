# Material blending prototype evidence

Accepted candidate: F, 2026-09-18, engine 26.09.15, RTX 5090.
The validation ledger owns the fixed scenarios, failures and acceptance decision.

## Visual review

- `before-v2.png`: original sharp boundaries at the fixed shore view.
- `candidate-a.png` through `candidate-e.png`: iterations; B still had stepped
  outlines, C was too hazy, D cost too much. E uses nine quadratic-filter samples.
- `final-near.png`: F's final local blend; `final-far.png` and `final-return.png`
  exercise a real player-target detail-change roundtrip with the camera fixed.
- `candidate-e-oblique.png`: low-angle review of texture and grass taper. F keeps
  this mixture and only skips noise whose existing attenuation is zero.
- `before.png` is the invalid initial camera placement, retained as history.

Broad deposits remain recognizable; small visual patches may disappear or change
at coarser detail. Discrete gameplay material identity is unchanged. These stills
do not establish continuous motion quality or every material/LOD combination.

## Performance

Raw results are losslessly compressed `.json.gz`; summaries and comparison JSON
retain the main metrics. `*-cold.json` / `*-after.json` record source hashes and
live settings. All comparable runs use the ledger's PERF-001/v1 workload.

| Matched comparison | Moving FPS | Moving p95 / p99 | Standing p99 | Decision |
| --- | ---: | ---: | ---: | --- |
| First control → F | 494.47 → 483.95 | 3.0966 / 4.7571 → 3.2897 / 4.9745 ms | 4.3626 → 4.9427 ms | Fails standing p99 |
| Repeat control → F | 497.43 → 486.79 | 3.1727 / 4.8425 → 3.2884 / 4.9187 ms | 5.0285 → 4.2179 ms | Passes all gates |

The repeat was declared before execution to resolve standing-tail variability,
not run repeatedly until passing. Both comparisons are retained. Moving FPS cost
repeats at about 2.1%; the standing-tail regression does not reproduce, with the
unchanged control itself varying by more than the initial reported regression.
Memory/allocation limits pass; both pairs complete with zero measured exceptions
or collision failures. Earlier D/E failures remain in their files and the ledger.

`control.json.gz` is the initial invalid 96 m grass run, not the 64 m comparison.
Shader compile records retain warnings. Shutdown logs retain an engine prefab
destruction/close error; successful cold startups and timing are distinguished
from that shutdown issue. Native source hotload/preflight failures occurred before
timing and were not counted as completed runs.

These results qualify only blending relative to the same pending water, grass
height/range and texture-distance work. Those unrelated changes are not accepted
or included by the blending commit.

Engine logs are preserved losslessly as `.log.gz` files.
