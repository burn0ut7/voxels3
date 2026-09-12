# Selective erosion prototype

Implemented candidate H for generator22, pending runtime qualification. This is a prototype, not acceptance.
The authorizing task requests selective exterior erosion and unchanged before/after
figure-eight performance. EROSION-001/v2 in the validation ledger owns the current .3-mountain scenario.

RegionalLandforms remains the owner of exterior height. TerrainErosion adds a
bounded offset using Rune Skovbo Johansen's advanced erosion/Phacelle approach:
blended directional waves, partial normalization, straight internal gully slopes,
and stacked fading. An analytic derivative of the combined mountain masses supplies stable guidance
without neighboring height evaluations or fine-noise-driven turns. The compact kernel blends
four hashed global lattice pivots with smooth bilinear weights; this is a modified
Rune-inspired kernel, not the reference Gaussian kernel. Two octaves use gain0.5
and wavelength0.20*LocalLandformScale. There is no recursive height evaluation.

Only mountain-dominant regions participate: mountain weight<=0.5 has zero offset,
with a smooth fade from0.5 to1.0. Hills and plains retain the base height exactly; a continuous land mask and height above
sea level suppress the coast and submerged terrain. Plains have no independent
amplitude. Neutral initial fade target preserves flat extrema without relying on
absolute altitude to label peaks or valleys. This does not generate drainage graphs,
transport sediment, or model the erosion of cave walls or player edits.

Inputs are world XY, explicit seed, version and the existing immutable recipe.
There is no new mutable state or cache. CPU lattice sampling, queries and collision;
GPU regular/transition extraction; material surface depth; and water coverage all
consume the same exterior definition. Conservative region bounds and global vertical
support must include the maximum possible offset. Cave composition and the canonical
edit/storage boundary follow exterior generation unchanged.

The existing version/recipe save selector separates generator18 from generator13 and earlier diagnostic/candidate versions14ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã¢â‚¬Å“17.
Old saves are preserved, not migrated or reinterpreted. Networking's existing identity
checks remain the compatibility boundary. Coordinate hashes are integer-based;
float arithmetic/trigonometric CPU/GPU agreement still needs measured qualification.
No claim of universal bitwise hardware determinism is made.

Budget: bounded cell neighborhood per octave, no allocations per sample, no global
simulation, no extra render passes, no new frame scheduling or resources. Measure
complete runtime cost because material and water shaders also evaluate exterior height.
Use the existing build-local XY lattice reuse; introduce no persistent height cache.

Alternatives: full hydraulic simulation needs neighborhood state and iterative passes;
Dendry addresses drainage topology; Grenier2024 is a related phasor-noise enhancement.
Rune is selected provisionally for local evaluation and control of branching detail.

Sources and transfer limits:
- https://blog.runevision.com/2026/03/fast-and-gorgeous-erosion-filter.html
  Algorithm description, not s&box performance or cross-hardware determinism evidence.
- https://github.com/lpmitchell/AdvancedTerrainErosion
  C# algorithm reference only; Unity/Burst dependencies are not adopted. Retain MPL2
  attribution on adapted erosion files and record the inspected upstream revision.
- https://onlinelibrary.wiley.com/doi/full/10.1111/cgf.14992
  Comparable procedural enhancement, not globally coherent drainage.
- https://github.com/mgaillard/Noise
  Dendry branching reference; no runtime integration selected.
Current strength: mountain0.035*ReliefHeight; hills/plains0; gully weight0.7.
Local exposure fades in over mass0.10..0.50 and out over mass0.9..1.0.
This is height within the broad mountain shape, in addition to the sea-level mask.
Land onset0.65..1; coast onset64..320 units above SeaLevel.

Candidate A used a sixteen-cell Gaussian kernel and four additional base-height
evaluations for the gradient. The unchanged figure-eight fell from778.28 to368.87
FPS; stationary rendering fell from940.35 to403.53FPS. Candidate A is rejected.
Candidate B retained hill erosion and was rejected by the user visually before its
benchmark started. Candidate C removes it and gates on mountain dominance.
Both terrain material columns and water consume the canonical field; recurring
fragment-stage evaluation motivated candidate B. Its raw results and source archive
remain in the validation evidence. No material semantics were relaxed to reduce cost.
Upstream reference revision57e79cc331d17e2a45c897bea9c4c022d1fc5551.


Mountain-shape follow-up (user observation,2026-09-09): user approves the localized
appearance but finds the underlying mountains unusual. At that point the base source derived
crests from one folded value-noise field cubed, plus a sharp smooth threshold cliff
band. Region placement, hill/plains fields and mountain shape are independently
seeded; there is no explicit main/subsidiary ridge or valley hierarchy. A plausible
source-based diagnosis is that local erosion adds detail without repairing weak
large and intermediate shapes. This remains an inference, not a measured visual
root cause. The subsequent user request explicitly authorizes implementing improved shaping.

A heightmap is a sampled representation; our exterior already computes a heightfield
before full 3D cave composition. Replacing representation alone would not improve
the mountain recipe. SideFX's [workflow](https://www.sidefx.com/docs/houdini/model/terrain_workflow.html)
starts with large masses and adds obstacles/large subdivisions before fine erosion.
Far Cry5's developer [terrain presentation](https://media.gdcvault.com/gdc2018/presentations/TerrainRenderingFarCry5.pdf)
uses tiled heightmaps and adds procedural cliff meshes (PDF pages17,119,122).
These are references for shape layering and representation limits; their offline
asset workflows and GPU budgets are not adopted. A potential next slice would
improve broad mass, ridgeline continuity and valley variety while retaining the
mountains-only erosion mask, caves and deterministic coordinate inputs.


D2 implementation: mountain dominance smoothly blends the narrow cliff-band profile
into a broad cubic ridge plus a smaller secondary-noise form. Weight<=0.5 retains
exactly the previous base height. Reusing existing noises keeps the slice bounded;
no ridge graph, full hydraulic erosion or extra generation pass is introduced.
Erosion exposure vanishes at the local base and crest. Broad-ridge guidance avoids
fine bumps rotating the directional filter and avoids a nonzero erosion offset at
the ridge derivative's sign flip. Two larger detail layers replace three fine layers.
CPU queries, GPU regular/transition geometry, materials and water all use this field.
Bounds include the blended profile and exposure; maximum offset fraction is0.0525.
These are stateless adaptations of massing/secondary-form/fine-detail separation,
not a claim that an offline Houdini pipeline has been reproduced at runtime.

D1 was a temporary generator17 diagnostic with erosion strength0 and the original
base profile unchanged. The user found it too smooth; it is not the intended result.
D2 restores erosion while addressing its spatial concentration. All diagnostics,
failed performance and pending checks remain in the ledger. There is one active
implementation; the disabled diagnostic is not retained as a runtime option.


F mountain mass prototype (generator20): E's1.30 mountain-only width is retained.
The existing independent broad field p at2*LocalLandformScale modulates height
along the ridge: S=4*p*(1-p); summitEnvelope=.25+.75*S^4. The mountain target is
.10+.83*R^3*summitEnvelope*(.85+.15*d). The envelope lowers long crest segments
into saddles, and d varies summit height. It retains a quarter of the ridge's
vertical contribution at its deepest saddle; this is not a quarter of total height.
This local polynomial replaces the smaller additive secondary form. It is a
bounded artistic prototype, not a drainage graph, tectonic simulation, or an
explicit network of branching ridge primitives. Those alternatives would require
additional neighborhood evaluation and a broader bounds/performance redesign.

Erosion parameters and local/coast exposure remain unchanged. Its guiding slope
includes both the broad ridge and summit envelope derivatives; the d derivative
is deliberately excluded to prevent fine variation turning the filter. The smooth
envelope maximum avoids a new derivative-sign discontinuity. Mountain-only noise
count stays the same as E; the existing p noise also returns a derivative only in
eligible mountains. Conservative intervals propagate both factors; the target
height remains within.10...93 and global padded support remains unchanged.
The change can lower individual summits; it preserves the maximum height setting,
not every former peak elevation. User visual review and runtime qualification are
required before acceptance. CPU/GPU queries, collision, materials and water share
the version20 recipe. Historical E/D2 results remain in the validation ledger.


G (generator21) supersedes F after the user reported persistent waves and right-angle
turns. MountainMasses owns a compact nine-cell peak neighborhood, two integer hashes
per peak, seeded jitter, orientation, radii and height. Squared cones have a small
rounded tip and meet with the smooth union1-product(1-peak). This replaces folded
noise contours in the dominant-mountain target, rather than modulating their height.
The shape is an artistic overlapping-peak model, not simulated geology or drainage.
A complete watershed/ridge graph was rejected for this bounded prototype because
it requires a wider dependency domain and a separate generation/storage design.

Inputs: worldXY, seed, MountainWidthScale*LocalLandformScale. Output: mass in[0,1]
and analytic world gradient. No mutable state or cache. RegionalLandforms blends
.10+.83*mass only above mountain weight.5, preserving every lower-weight sample.
CPU, GPU, collision, material and water consume the same version21 exterior.
Global support stays unchanged. Local bounds propagate peak intervals inside a
single cell; cell-crossing or larger rectangles fall back conservatively to[0,1].
Support radius<=1.15, jitter<=.18 and omitted-cell distance>=1.32 justify3x3 lookup.
The additional per-sample work and potentially looser bounds require measurement;
no performance improvement is claimed. The former noise derivative implementation
was removed because peak geometry now supplies the guidance gradient.

Erosion strength, two layers and wavelength remain as approved; local exposure is
Smooth((mass-.10)/.40)*Smooth((1-mass)/.10), plus the existing domain/land/coast masks.
This adapts local foot/crest suppression to the new base shape. It does not preserve
every old gully position or peak elevation. F remains archived as rejected history.

The existing detached-camera positioning tool also clears engine navigation target
and velocity after explicit placement. Installed FirstPersonCamera otherwise smooths
the camera back toward its retained target; this caused the invalid F view. This
editor-only fix does not change player controls or the playable terrain pipeline.


H (generator22) replaces G's one-peak-per-cell layout with a mountain group.
GroupSpacing=2.1 within MountainMasses, independent of the permanent .3 regional
MountainAmount. Groups use the same seeded3x3 neighborhood and compact support.
About18.75% of cells are inactive, leaving larger valleys between groups. Height
factor.22+.78*t^2 favors smaller groups with occasional high ones; major/minor
radii.60..1.15/.40...85 broaden the size range. Each group combines48% shared
foundation (1-r^2)^2 and52% union of an offset main summit and two smaller shoulder
summits. This puts subsidiary peaks on raised ground rather than separate bases.

Summit geometry stays within the foundation ellipse: the maximum shoulder center
radius sqrt(.4^2+.12^2) plus maximum subsidiary radius.52 is less than1. Group
support<=1.15 and jitter<=.18 therefore preserve the nine-cell lookup proof. All
constituents and smooth unions remain[0,1]; external height support is unchanged.
CPU analytic gradients and interval bounds include the same group composition.
There is no mutable cache or global drainage state. G erosion strength, two layers,
wavelength and exposure remain unchanged. Old generator21 saves remain separate.
The inherited v2 before benchmark attempts were interrupted/invalid; no comparable
before/after performance acceptance is claimed until a valid pair is recorded.

I proposal (generator23): correct H's flattened height distribution to .65..1
and spacing1.6, while retaining shared bases and three peaks. Replace the old
folded contour-ridge contribution with a gentle q-squared transition profile.
TerrainCliffs owns sparse seeded rotated elliptical excavations with near-vertical
walls, a64-unit maximum undercut, varied elevations and half-heights. Apply by
max(base-with-caves,cut), with mountainWeight.75 gating. No new mutable state;
seed, recipe and world coordinates are the inputs. Scalar and lattice CPU paths
and GPU extraction use identical composition. Height remains the exterior
reference for water and soil; the excavation exposes underlying stone naturally.
The lattice cache retains the whole landform sample to reuse mountain eligibility.
Conservative upper cut intervals extend density bounds; subtraction cannot lower
the old bound. Cuts fit a nine-cell neighborhood for supported settings.

This chooses bounded local excavations over unbounded 3D noise or global erosion.
A height-only remap cannot form roofs; surface-normal coloring alone cannot change
collision. This prototype changes the canonical field so physics and visible
geometry share the cliffs. Risks: increased generation cost, coarse LOD losing
small lips, enclosed cavities where a cut misses the outer slope, and overly
regular cliff placement. Real-world inspection and the unchanged performance
scenario qualify the implementation; no acceptance is assumed from compilation.


J (generator24) is the current cliff candidate: same mountain geometry as I,
128-unit maximum undercut, cached Vector2(height,mountainWeight), and a safe
incoming-density bound for skipping irrelevant cuts. The earlier .75 hard skip
was corrected before any J Play or benchmark run. Cliff geometry and material
exposure are authoritative field effects; no cosmetic normal-based rock overlay.

MOUNTAIN-DIVERSITY-001/v1 predeclared2026-09-09: user goal is5..10 visually
interesting/distinct mountains in ONE unchanged playable world run. Target8,
minimum5. Same seed1337/.3 recipe as EROSION-001/v2; no seed/recipe changes between
accepted captures. Record world ID, generator, XY/camera poses and screenshots.
Require at least two snowy and two bare examples, a rock-cliff example, a gentle
example, and visibly different summit layouts (single/twin/elongated or clustered).
Color alone does not make two mountains distinct: judge silhouettes, shoulders,
slopes and neighboring range connections. Captures must show enough terrain for
that judgment; close rock-only images do not qualify. Manual camera exploration
is explicitly authorized. Document rejected/weak examples instead of counting them.

Candidate L(generator25) changes group summit layout using four seeded families
with continuous existing width/height/angle variation. Keep three bounded peak
slots, no new global noise or caches. Vary shared-foundation contribution by family.
Emit a group-weighted local erosion multiplier in[0,1]; existing maximum erosion
bound remains valid. PeakFraction becomes height-weighted summit profile, retaining
snow threshold.76/.75 but preventing every small subsidiary peak from getting snow.
Cliffs retain K geometry/optimization and independent seeded orientation/placement.

Current source matches all78 paths in cliff-K-source-after.json (checked before
edits), so K result637e2894e25745f4a9df9514bce7e890 is the comparable before run.
Reuse EROSION-001/v2 parameters and acceptance flags unchanged. Reuse fixed
EROSION-SHAPE-001/v2 surveys and production density audit; finite/repeat/bounds
checks, same-run column observations and screenshots must accompany judgment.
K's prior worst-stationary-frame flag and partial transition coverage remain history.

L(generator25) visual review selected six distinct formations in one unchanged
seed1337/.3 Play run: see ../ValidationEvidence/Erosion/MountainDiversityGallery.md.
The four summit families, unequal shared foundations, height-weighted snow score
and local erosion multiplier are implemented in canonical CPU and GPU paths.
Cliffs retain K's independent placement, 128-unit recess and conservative pruning.
Some cuts still have overly regular oval outlines and some unselected summits
remain repetitive. The selected sample meets the user's five-to-ten visual goal;
it is not a guarantee of global uniqueness. L figure-eight FPS is2.76% below K,
with a19.35% worst-moving-frame flag; full performance acceptance remains pending.

M(generator26) responds to oversized/frequent rock bowls: candidate occupancy
51/256 instead of128/256, horizontal radii45% of L, vertical half-height65%,
recess64 instead of128. Surviving centers/orientations and mountain generation
are unchanged. CPU/GPU pruning uses maximumradius.153/minorradius.063;
vertical envelope is SeaLevel+(.15...71)*Relief and global upper.13*Relief.
Reduced support still fits the existing nine-cell neighborhood. This controls
scale/frequency, not the ellipse outline; visual/runtime qualification is in
CLIFF-SCALE-001/v1. Prior generator saves remain version-separated.

N proposed regional elevation: the current peak groups share only local bases,
so valleys outside group support repeatedly return to low terrain. Add one
continuous regional support field in RegionalLandforms, using the existing
mountain preference before cubing, coast fade and a separately seeded field at
1.35*MountainRegionScale. A modest existing p-field modulation adds broad
shoulders. This raises valleys as well as summits across neighboring groups,
with varied elevation between regions. Inputs remain seed/recipe/XY; no mutable
state. All density/material/water consumers receive the canonical new height.
Caves retain their existing depth relative to that surface; cliff locations stay
unchanged and may be buried by uplift. Do not enlarge cliffs to compensate.

Chosen over increasing individual foundation radii (still isolated hills) or
raising the entire world (uniform bases and changed oceans). Formula and runtime
criteria are UPLAND-001/v1 in the validation ledger. One additional four-hash
noise field per eligible XY column; bound intervals include it. Global maximum
surface bound grows by.68*Relief; old generator saves are version-separated.
This prototype isolates supporting elevation. True projecting mountain bodies
and clear walk-under overhangs remain a separate, unfulfilled visual objective.

Final status: N regional uplands retained, exact CPU/GPU hash match to N measured
source; N2 optimization reverted. N2 also overlapped new river-source additions,
so its timing is not an isolated comparison. Final combined-project restart
verification is incomplete during concurrent river work. See validation ledger;
no performance acceptance, commit or push is claimed for this prototype.

O continuous mountain flow replaces local cone instances. The existing Regional-
Landforms mountain coverage and regional support determine where mountains occur;
MountainMasses supplies continuous ridges/shoulders, their gradient, snow score
and erosion exposure. Inputs seed/XY/scale are immutable. Rotated coordinates and
two slow domain warps bend ridge contours; two ridge scales form connected crests
and spurs, while a slow character blend introduces broad elevated shoulders.
One canonical CPU implementation plus its GPU mirror and conservative interval
bounds own this responsibility. Output mass remains[0,1], preserving downstream
height ceiling. The world generator revision must advance when adopted so river
and saved-world derivatives cannot silently use the old mountain surface.

This targets the repeated cone structure directly; adjusting cone widths or only
raising their bases cannot satisfy the active goal. Unwarped folded value noise
was rejected earlier for straight grid contours, so the new candidate rotates,
warps and combines fields before shaping. Bounds carry transformed intervals;
analytic gradients carry the warp Jacobian. Risks include overly broad shelves,
remaining contour artifacts, and looser classification intervals; use real-world
survey, visual and performance evidence before acceptance. No result is assumed
from this design or from staging the implementation.

### Crest continuity correction (generator45, 2026-09-12)

Implemented on CPU and GPU. Clean restart, sampled field and mesh checks passed;
exact reported-ridge visual acceptance remains open. User deferred performance
validation; EROSION-CREST-001/v1 records the scope and retained evidence.
The input mountain ridges previously used max(0,1-abs(4*noise-2)) squared.
Their height is continuous, but their derivative jumps across the fold. Rune's
slope-based erosion fade cannot suppress a nonzero slope that reverses without
approaching zero. Fixed mass-height exposure additionally does not identify each
shorter local crest. This identifies a source-level continuity defect; it does not
attribute every observed mesh spike to erosion.

MountainMasses owns a narrow C1 rounding of the absolute value: for folded
coordinate x and width w=0.08, use x*x/(2*w)+w/2 inside abs(x)<w and abs(x)
outside. Value and first derivative match at +/-w; derivative passes continuously
through zero at the crest. Both weighted ridge fields use the exact derivative
clamp(x/w,-1,1). Shelf blending, warp chain derivatives and height modulation
remain part of the combined analytic gradient. This supplies a continuous input
to the existing slope fade without an extra erosion eligibility mask. Rounding
slightly lowers the original sharp crest; outside the rounding band both ridge
height and derivative retain their previous definition. This is a landform-input
correction, not Rune's optional rounding of generated gully octaves.

CPU and GPU use the same expression. The CPU interval Ridge evaluates the rounded
absolute value at nearest/farthest distances; its monotonicity preserves enclosing
bounds. Global bounds remain conservative because the rounded absolute value is
never smaller than abs(x), and erosion's maximum offset is unchanged. No new
samples, octaves, allocations, caches, mutable state or rendering passes are added.
CPU queries/collision, GPU geometry/materials and river generation consume the
canonical field as before. Local crests are handled independently of altitude;
existing mountains-only eligibility, exposure and coastline protection remain.

Generator45 separates the new base field from generator44 save identities and
network compatibility. Existing edited worlds are preserved and not reinterpreted
or migrated. Multiplayer convergence on the new generator still needs qualification.
Alternatives: an altitude cutoff misses shorter ridges; smoothing just the guidance
would make it disagree with the input height; a new crest mask could hide the fold
but leaves the discontinuous input derivative. Rounded input height with its exact
derivative repairs that mismatch together. Full erosion-kernel replacement and
hill-coverage changes remain separate work.

Reference: [Rune's preserving peaks, fade approach and straight gullies sections](https://blog.runevision.com/2026/03/fast-and-gorgeous-erosion-filter.html).
The article explains slope-shaped fading around extrema and discontinuity masking;
it is not evidence that an arbitrary pre-folded input ridge satisfies those
conditions or that this adaptation has passed engine/performance validation.
