# Rolling hills candidate

Prepared 2026-09-10. Applied on user follow-up; runtime qualification pending.

The current hill term combines low relief with 4096-unit detail and a 1024-unit
mound mask at default settings. Replace these with a 12288-unit hill shape and
40960-unit regional presence mask. Keep the existing plains-amount preference;
its product with the smooth regional mask supplies hill eligibility. Squaring
that eligibility preserves soft fringes and leaves more flatter terrain.

Hill profile: the plains base (0.035 + 0.015*q) plus 0.42*hillShape, multiplied
by the hill weight. Default maximum additional hill relief is1290.24units;
actual height depends on local shape and eligibility. Pure hills remain below
the mountain profile maximum0.93*ReliefHeight. No new noise samples, mutable
state, allocations, configuration controls or alternate runtime path are added.
Ruggedness no longer adds small mounds to hills. Plains, mountain, ocean and
upland profile formulas are retained; their mixed terrain and derived rivers
can move when hill height changes. CPU scalar, conservative interval bounds
and GPU mirror change together. The global height bound remains conservative:
the hill profile is at most0.47, below the mountain0.93upper envelope.

Saved worlds: bump generator42to43 using the existing versioned save selector,
codec and replication identity. Preserve all v42files; no migration or silent
reinterpretation. Check version is still42before applying; another terrain
change may consume43. Do not hotload across an active stored world; stop Play
before application and start a fresh generation afterward.

Rejected alternatives: raising relief globally changes mountains and ocean;
raising only the original hills preserves the offending short mounds. An
additional multi-octave terrain subsystem is unnecessary for this scope.

## Validation to declare in Docs/ValidationResults.md before any run

HILLS-001/v1: reuse TERRAIN-SHADOWS-001/v1 / RIVERS-018/v2 figure-eight inputs
unchanged, same finalized renderer for before/after; generator42then43is the
intentional content change. Capture before first because no current accepted
comparable baseline exists. Source must remain stable during measurements.
Require FPS within5%, frame tails and memory/allocations within10%, complete
streaming/collision readiness, zero new exceptions/correctness failures.
Do not waive existing renderer regressions or claim historical FPS as a clean A/B.

Shape survey: production export_landform_survey, minimumX/Y=-131072,
pointsPerAxis=129,spacing=2048; same active benchmark recipe before/after.
Require finite heights, exact repeatHeight, heights inside boundMin/Max, at
least one hill-dominant and one plain-dominant land sample (land>.95,
mountains<.05; hills>.5 or hills<.05). Compare hill-weight coverage and slopes;
require decreased hill-dominant coverage and report magnitude without tuning
the survey. Smaller fixed129x129/spacing256survey centered on the strongest
baseline hill sample provides comparable local shape and boundary observations;
record its exact coordinates before capture. Predeclare camera poses from
that location before either visual comparison. Inspect actual rendered rolling
slopes and flat gaps; numerical wavelength changes alone do not establish success.
Use existing voxel_density_audit for CPU/GPU sign/finite/bounds errors and
record exact coverage. No offline terrain clone, new tests or test hooks.

## Application

hills.patch contains only the intended hunks against the captured dirty sources.
Check the live files still match each before snapshot before applying. The
snapshot includes previous tasks' work; never stage or commit those whole
files indiscriminately. Preserve all unrelated changes. Recheck hill source,
GPU contract, routes, shader compile, screenshots and ledger before acceptance.

Status: implementation applied; compilation, runtime appearance, density audit,
performance comparison, commit and push pending coordinated editor access.
