# Shared tree growth authoring

Initial authoring implementation, 2026-09-21. The deliverable is the Blender authoring
system, not a replacement catalog of hand-tuned models. One growth algorithm
owns all species; species profiles are data. Initial profiles cover oak, ash,
birch and spruce. They are visual growth hypotheses, not calibrated forestry
predictions or evidence that every species is supported.

## Ownership and model

`Tools/BlenderTrees/growth.py` owns the versioned recipe, species growth parameters
and deterministic seasonal graph. Inputs are species, seed, number of growth
seasons, potential height, growth/branch/light controls and fixed resource caps.
Nodes retain stable IDs, parent/axis identity, birth season, position, radius,
bud status and light exposure. The graph is canonical; preview curves, source
wood/leaves, collision, motion data and exported LODs are derived.

Each season evaluates canopy shading, distributes available resource through
the parent graph with leader preference, activates eligible lateral buds,
extends surviving tips towards light and away from occupied space, and updates
supporting radii. Persistently unproductive tips stop growing. Species changes
bud arrangement, dominance, extension, response to light/gravity, radial growth
and foliage retention. Age executes more seasons with the same seed; it does
not resize an already completed adult. Seasons are not yet calibrated years.

The biological mechanism is an approximation for authoring natural forms.
Directional canopy occupancy approximates light, spatial exclusion approximates
shoot competition, and pipe support/secondary increments approximate thickening.
No claim of physiological carbon balance, root/soil simulation, disease,
mechanical fracture, or complete collision-free mature wood is made.

The existing Tree Lab panel becomes the s&box Tree Growth panel. Its growth
preview and source geometry use the same graph. The old prescribed species
scaffolds and depth-recursive crown builder are superseded. Original stored
libraries and installed game assets remain immutable inputs for existing exports.
Manual primary guides are no longer the source of new growth; the preview shows
the generated topology. Stored legacy source geometry retains its existing export path. The connected
source format now uses the same exporter with continuous wood at every LOD.
The initial oak candidate has completed export and sample installation; native
game import, visuals and motion qualification remain pending.

## Boundaries and budgets

The user's2026-09-21 steering prioritizes the final visual result over authoring
optimization;60-70second builds are acceptable. Offline generation time and
process memory remain recorded observations rather than visual acceptance
gates, provided generation completes and the editor remains usable. Preserve
earlier measurements and failures. Geometry correctness and native game import
limits still apply; game rendering, LODs and motion require actual evaluation.

The independently reviewed `Review_Oak_18_271828_Source` is approved for an
initial in-game evaluation sample only. Its exact stored solid-union geometry
is preserved separately; it must not be rebuilt with the unaccepted spruce
cleanup candidate. Spruce junction shape and crown coverage remain rejected.

The solver is independent of Blender and game runtime. Blender owns user
controls, graph persistence, preview and mesh creation on its main thread.
Advance one season at a time during interactive preview; cancellation discards
the unfinished result. Never publish a partial graph as a completed specimen.
Source meshing also yields between bounded stages and polygon batches. It uses
a temporary collection and replaces the previous source only after completion.
Esc or Cancel Build removes temporary IDs; it leaves the finished source intact.
During a rebuild the previous foliage is hidden while the previous wood stays
visible; cancellation restores the previous full specimen. The fixed source-start
spruce rebuild fell from211.45s to62.66s with unchanged graph and needle counts.
Whole-process memory still exceeds8GiB, so this is not resource acceptance.
Native mesh publication remains atomic; the final large-spruce part stage was
measured separately, and cancellation after it retained the previous source
and removed temporary IDs. Mesh builds omit Blender undo
snapshots to avoid retaining several gigabytes per rebuild; save a separate
working file for historical source revisions.

Node/season caps are explicit errors, not hidden thinning. The initial hard cap was30,000nodes; candidate017raises the offline graph guard
to100,000nodes while retaining80seasons per specimen. The working source adapter uses
Blender's bundled Manifold Boolean to unite closed branch sweep volumes, then
rounds each connected intersection separately with a clamped native bevel.
Rounding copies each junction's complete incident faces into a local patch,
then stitches it back and refreshes vertex normals using their full global
face fans. Earlier patches copied incomplete boundary normals and failed
equivalence. The corrected patch matches both fixed oak and dense spruce
finished wood within 4e-9 m; complete oak meshing fell from 16.13 to 9.24 s.
Dense-source completion and scene memory remain in validation.
A specific spruce fork still showed a socket after local rounding: a0.133mm
spoke left by the Boolean restricted its requested7.992mm fillet. The current
candidate removes short edges incident to concave branch boundaries before
collecting junction groups. The threshold is3% of the minimum nearby immutable sweep-frame radius, an edge
length criterion rather than a cumulative surface-error guarantee. Native
Manifold leaves new intersection point attributes at zero, so cleanup cannot
read their unfinished bark_radius. The maximum sweep radius is only an admission
bound; the minimum of three nearest frames for both identities and current
endpoints further limits each operation. Complete
endpoint face fans must stay within the permitted pair of branch identities;
validity, manifold status, identity locality and current radii are rechecked
before each sequential operation. Cleanup counts and stage time are retained.
This candidate needs independent visual acceptance and dense resource checks.

A global bevel clamp was rejected because one tiny edge reduced the rounding
of every fork. Closure is checked before and after rounding. Global numerical welding was
rejected after it opened valid small junctions. This replacement remains in validation under
TREE-GROWTH-008; it has not inherited acceptance from the prior collar mesher.

The immutable sweep reference retains graph samples/radii, bark coordinates and
motion ancestry. Union operands omit redundant longitudinal rings only when
every omitted original ring corner lies within0.03 of its local stored radius
from the interpolated retained rings. End rings stay intact. Prepared operands,
the union, and each rounded-junction result use the4,000,000-face offline limit
owned by surface.py (candidate019; the old1,000,000-face failure is retained).
The native union itself is atomic and its allocation cannot be cancelled halfway.
Temporary operand objects, meshes and their collection are owned by the current
transaction and removed on completion or cancellation. The source sweep mesh is
shared rather than copied. Render-part extraction uses compact index arrays and
preserves source corner positions, both bark UV layers, radii, blending weights
and custom normals.

The previously accepted convex-collar/array-subdivision method reduced memory
and passed the fixed oak views, but dense spruce internodes exposed repeated
parent necks. Its measurements remain historical evidence in006; the working
source no longer contains that alternative implementation. The union removes
those necks, but its first memory runs exceed8GiB and the oak close-up exposed a
stopped-leader cap regression. Unrelated crossing branches can also be fused;
intersection provenance and growth clearance remain unresolved acceptance work.
Source face indices/counts, UVs, material indices and branch IDs are packed
while generating foliage. This removes temporary Python corner objects without
reducing leaf detail, count or variation. The native mesh receives those buffers
directly; positions retain their existing generation order.
[Blender's free normal storage](https://developer.blender.org/docs/release_notes/4.5/modeling/)
allows exact corner vectors; legacy fan-space encoding introduced measurable
rounding at part boundaries and is no longer used by this source builder.
Roots remain artist-directed swept geometry with explicit parent ancestry; the
obsolete derived collar skeleton has been removed. Degree-two successor chains transport one sweep frame through the bend,
retaining graph_axes ancestry. Primary motion groups branch from the continuous
main stem; a stopped trunk leader with one successor remains trunk wood.
This can change derived primary count/pivots for newly rebuilt sources. True
forks retain separate swept volumes whose start caps close inside the parent;
the attempted graph-collar expansion was rejected for protruding socket rims. Remaining
axis endpoints have rounded caps even when another axis succeeds them, keeping
a cut plane out of the bend. These corrections still need full acceptance.
Sweeps retain original graph samples/radii for bark projection and motion identity.
They are hidden construction data, not an overlapping second visible crown.
Leaf groups and compound petioles bind to the completed wood surface.

The earlier voxel-welded structural wood and separately capped fine tubes are
superseded. Native Skin was rejected after it produced disconnected components
on the fixed oak graph. Both failures and the independent visual review are
recorded in the validation ledger. Source generation still stores complete
ancestry before any eventual game simplification.

Seeds use an explicit stable integer mixer keyed by node, season and purpose.
Traversal is stable, and growth cannot depend on scene order, Blender time or
Python's process hash. Generator revision and every profile/control accompany
the graph. Repeatability is qualified for the tested implementation/environment,
not arbitrary floating-point platforms or future versions. Age presets select 6, 24 and 30 seasons; the wider 1ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â‚¬Å¾Ã‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¦ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Â¦Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€šÃ‚Â¦ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Â¦ÃƒÂ¢Ã¢â€šÂ¬Ã…â€œ80 input range does not
guarantee a recipe fits the node budget. A recipe change
invalidates the graph and all its derived geometry/motion/LOD/bakes.

Store complete ancestry before simplifying. The current game motion format
still groups descendants by primary limb; the full graph enables a later
measured extension without inventing parents from smoothed meshes. This change
does not claim to implement independent wind for every new graph axis or leaf
view correction. No runtime weather, networking or terrain state is introduced.

## Choices and validation

The Modular Tree evaluation established a useful meshing/module reference but
its GrowthFunction lacks spatial light competition and its public Python output
omits true parent links. Wrapping its presets would retain those limitations.
This slice implements our shared graph directly, keeping its complete ancestry
and using the existing Blender geometry/export responsibilities. No copied
Modular Tree addon code or native binary becomes a game dependency.

[Palubicki et al. (2009)](https://algorithmicbotany.org/papers/selforg.sig 2009.html)
informs the separation of local bud rules, environmental competition and
internal resource allocation. We adopt that organization, not a reproduction
of their solver or their visual/performance results. A crown-envelope-only
space-colonization generator was rejected as the sole age model because the
requested system needs persistent buds and seasonal development.

Validate the actual Blender operators with fixed species/age/seed inputs in
`Docs/ValidationResults.md`: repeatability, stable developmental prefixes,
parent connectivity, finite radii, response to shading, bounded execution,
cancellation and saved recipe/graph round trips. Inspect branch silhouettes
from multiple native viewport angles. No production assets are installed by
this authoring task, so game frame rate and existing forest acceptance are not
changed or re-qualified. Any later installed geometry/shader change requires
the unchanged canonical figure-eight and in-world visual/wind/LOD review.

The legacy catalog helper and schema-2 guide recipes are not replay-equivalent
under this generator. New specimens enter through Simulate Growth and the
versioned `.tree.json` workflow. Do not silently regenerate the old catalog.

The 18 fixed growth jobs, saved graph round-trip, corruption rejection and native
modal cancellation passed. Early source attempts exposed a small-root cutoff
defect and unbounded mesh expansion. Those failures remain in the ledger. The
recovered editor completed all twelve fixed species/age sources with the earlier
voxel mesher in 5.0-27.9 seconds. The existing motion reader resolved every eligible source; a one-axis
stem can be meshed but is rejected by that adapter's current 2-256-axis guard.
Cancellation preserves the previous geometry checksum and gallery exit restores
all object positions. See TREE-GROWTH-003/v 1 for source hashes, measurements
and native two-angle renders. Profiles remain sparse/angular in places,
especially evergreen foliage. This establishes a working authoring system,
not botanical fidelity or qualification of a replacement production catalog.

## Attachment and foliage correction

The close-up review found two defects in the initial adapter: uniformly sampling
structural tails discarded branch attachment bends, and their fallback taper
no longer matched simulated radii. Derived sweeps must keep graph control points
and radii exactly. Narrow attachment collars are internal overlap geometry;
they cannot replace or move graph nodes. Sweep records retain graph-axis identity
for traceable meshing and downstream attachment work.

Leaf retention and the age of leaf-bearing wood are different traits. Deciduous
foliage renews on viable established shoots, while evergreen needles persist on
recent cohorts. One shared foliage eligibility rule must serve canopy shading,
radial support and Blender foliage placement. Initial eligibility covers the
current plus two earlier living shoot cohorts for deciduous profiles and the
four retained cohorts for spruce. This is an authoring approximation to leaf
renewal, not a detailed bud/phenology model. The saved graph remains canonical;
changing the solver affects new growth, while rebuilding an old graph keeps
its branch positions and uses the current derived foliage rule.

## Connected-junction review

The independent reviewer accepted the fixed oak 18 socket/disconnection repair
from three bare angles, a foliage close-up and an uncovered root view. It found
minor bark bands and uneven thickening, plus broader realism limits in straight
internodes, pointed tips and stylized roots. This is narrow visual acceptance,
not proof of botanical fidelity. TREE-GROWTH-006 records the connected mesher's
own measurements. Graph positions remain unchanged when rebuilding a saved tree;
new growth uses the shared foliage-renewal rule in light and radial support.

TREE-GROWTH-007 revisited the remaining root waists. Coarser root sampling alone
did not solve them. Reserving longer lateral transitions and allowing acute
collars to use available span length substantially reduced the visible waists.
Independent review passed the exposed-root view and all three canopy angles;
the narrow root crown, stretched bark and angular internodes remain realism
limits. The canonical oak graph and 21,858 leaves stayed unchanged.

The earlier large-spruce memory investigation isolated a costly default infinite-limit
projection in Blender subdivision. The finite subdivision mode reduced its
measured build from 111.4s to 78.9s with the same 942,720 faces and 1,006,656
needles. The full retry still peaked at 13.875 GiB private allocation
(6.293 GiB working set) in the cumulative editor session. The earlier 8 GiB
private-memory target was not met in 006. See finite-memory-v 1.json and the raw
phase logs; those failures remain part of the history.

In 007 the array subdivision, shared sweep reference and compact part extraction
reduced the same saved spruce 30 build to 60.406s and a sampled 6.579 GiB private
peak (3.718 GiB working set), within the unchanged 8 GiB target. The final 936,194
wood faces include the junction refinement; its graph and 1,006,656 needles are
unchanged. Every render-part corner position, UV, radius, weight and free normal
matches the unified wood exactly. This is a cumulative editor-session source
measurement at one-second intervals, not a fresh-process or all-recipes memory
guarantee. See compact-memory-summary-v 1.json and part-parity-v 1.json.

The broader matrix then exposed a separate leaf-construction peak in birch 30.
Packing source buffers reduced that fixed build from a 10.442 GiB observed peak
to 7.522 GiB, completing in 46.419s. All 59,553 leaves retain their 5,657,535 vertices
and 4,287,816 faces with the same position hash. Fixed oak 18's complete geometry/
index/UV checksum and material assignments also remain identical. This source
detail is authoring geometry; it is not a qualified game LOD or forest budget.

## Resource allocation refinement (in validation)

The earlier solver reused one node's local resource share for its terminal and
all emerging lateral buds, then imposed a substantial minimum extension length.
The correction counts each eligible lateral bud in demand and partitions the
local allowance between the terminal and each lateral. Local buds use the same
apical/lateral weighting as established descendants. Unused allowance is not a
physiological storage pool; this remains a growth-signal model. Saturation limits
extension without redistributing surplus. Shoot length is proportional to its
own allowance, allowing short growth under weak supply instead of manufacturing
long initial shoots that immediately starve in later seasons.

The spruce allocation preference changes from0.91 to0.54. This moderates its
recurrent preference for continuing axes and is an authoring profile hypothesis,
not a measured Norway spruce constant. It follows the sensitivity illustrated by
[Palubicki et al.](https://algorithmicbotany.org/papers/selforg.sig2009.html).
[Metslaid et al.](https://research.fs.usda.gov/treesearch/50968) supports evaluating
shoot length, branch density and conical crown form together with light response;
it does not provide the numeric calibration used here. Species still share one
solver and saved source graphs retain their original positions/profile. Source
revision hashes distinguish newly simulated graphs; graph schema remains1.
Acceptance remains open until fixed species/age runs and native views pass.

The in-validation spruce profile now describes axillary bud spacing separately
from leader whorls. A seasonal extension can publish several same-age internodes;
only the annual leader endpoint bears a full whorl. Limb buds can favor a common
plane to form sprays. Profile parameters own spacing, limb-bud count, order-based
extension and planarity; there is no second species growth implementation.
Needle-area support and shading follow segment length so subdividing a seasonal
shoot does not invent foliage area. Broadleaf profiles retain one cluster per
node and their coarser current spacing. Spruce's healthy profile disables random
terminal loss; environmental starvation and spatial competition still operate.
The old fixed6cm exclusion was larger than viable short shoots. Clearance now
uses the existing wood radius plus the profile-owned initial-shoot radius, capped
by the local occupancy-cell search extent. This remains approximate centerline
competition, not a guarantee against mature branch intersections.

Initial shoot radius and pipe-support radius are also species data; the spruce
profile uses finer needle-bearing wood than the broadleaf defaults. Extensions
shorter than the initial bud diameter remain dormant. Old saved graphs retain
their stored radii. Native full-crown and connected-joint validation is still open.

The independent native-view review has not accepted this growth refinement.
Denser internodes expose beaded older-wood junctions in the current mesher, and
full spruce crowns remain sparse. Successful bud releases are tracked individually
so a failed short/spatially blocked attempt stays eligible without duplicating
siblings already released. This latest retry correction awaits native validation.
The growth-signal supply depends on the number of eligible buds; changing node
spacing is therefore an architectural change, not a sampling-only optimization.

## Dense-junction replacement (in validation)

The convex-port mesher fails when short internodes or tiny shoots restrict an
older, thicker parent junction. Changing the shared radius cannot represent
that solid intersection without pinching the parent. The next canonical path
unites the actual closed sweep volumes using Blender's bundled Manifold Boolean
solver, then rounds concave edges where different source branches intersect, with a
separate overlap clamp for each connected junction.
Source graph, stored sweep reference, branch IDs and bark projection remain
separate from the exterior union. Temporary operands belong to the transactional
build; cancellation and completion both remove them. There is no Python package
dependency or voxel resolution. This replaces the collar/global-subdivision path
in the working generator; full native visual/resource validation is pending.

Blender documents its bundled solver in the
[4.5 modeling release notes](https://developer.blender.org/docs/release_notes/4.5/modeling/).
The [Manifold algorithm notes](https://github.com/elalish/manifold/wiki/Manifold-Library)
distinguish topological manifoldness from geometric self-overlap. Our input
sweeps must be closed and the final union must be one connected closed component;
those checks do not substitute for inspecting branch intersections and silhouette.

Root guide interpolation uses centripetal Catmull-Rom spacing and reflected
endpoint controls. Uniform spacing made short root departures briefly rise
before the following long downward span. This changes only derived root curves;
the growth graph stays intact. Secondary root guides start along the parent
tangent before turning toward their unchanged outer controls. Primary roots retain
the bole buttress flare; secondary roots use round cross-sections without that
vertical elongation. The [Yuksel, Schaefer and Keyser analysis](https://www.cemyuksel.com/research/catmullrom_param/)
supports centripetal spacing to avoid within-segment cusps and intersections;
it does not guarantee monotonic height, absence of intersections between separate
roots, or acceptable visible joins. Those require native geometry/view checks.

Earlier local-patch bevel attempts changed final coordinates beyond the
recorded equivalence threshold and were rejected. The corrected version now
refreshes normals in the full mesh after stitching each patch; fixed native
geometry comparisons pass. Whole-scene memory still exceeds the budget.

## Needle attachment correction (in validation)

The candidate spruce profile retains six needle cohorts, informed by
[Klein et al.](https://besjournals.onlinelibrary.wiley.com/doi/10.1111/1365-2745.12621).
Its lateral-extension multiplier is now 0.8 rather than 0.6 per branch order;
that is an authoring calibration, not a measured biological ratio. The 2000/m
reference density follows the approximate shoot-length/count ratio in
[Fedorchak et al., Table 2](https://sciendo.com/2/v2/download/article/10.2478/eko-2024-0015.pdf).
These changes increased retained shoot length but the native full-tree render
still failed healthy crown coverage; the profile is not accepted. The source
library records each study's scope and transfer limits.

Retained living shoot paths from the saved graph own needle distribution.
Physical spacing accumulates along each axis, including across internode
boundaries; there is no minimum cluster per internode. A packed point stores
each needle's angular target, centerline, forward bias and physical dimensions.
The target uses interpolated shoot radius. The final displaced wood's triangle
BVH selects a nearby triangle; double-precision triangle/edge projection places
the base on it within four maximum shoot-endpoint radii. A missing
local attachment aborts the build. Direction follows the outward surface normal
plus the original forward bias. Target-to-base and centerline-to-base maxima are
recorded separately. Centerline rays were rejected after a whorl-aligned ray
travelled down another branch. Nearest projection can still bunch needles or
select neighbouring wood, so shaft exposure and worst-displacement close-ups
remain required validation. Attachment occurs in bounded
batches in the existing cancellable source transaction.

Native float nearest-point queries can drift along slender triangles and even
choose an adjacent face for an on-surface point. Verification therefore measures
double-precision distance to actual nearby rendered triangles. The refined
projection is shared by needles and broadleaf/petiole anchors; the source retains
the same physical attachment bound. Every needle in the fixed1,461,501-needle
source has a verified final bark triangle within2um. Two BVH-neighborhood misses
required exhaustive triangle distance checks; their actual distances were
0.104um and0.00854um. Candidate-query distances alone cannot prove a gap.

One closed 12-vertex prototype is instanced at those points for every age.
Density changes spacing only; it cannot stretch the prototype. This replaces
64-needle clusters whose single translated center buried inward-facing needles.
Packed arrays avoid a Python object and JSON attachment record per needle.
Native curve resampling was considered but would add a second procedural
attachment pipeline; the existing source point attributes remain the canonical
handoff. Bud/axis ancestry remains in the graph and wood sweeps. Game export,
LOD and motion qualification remain separate pending work. The larger point/instance count remains measured; current authoring acceptance
follows the output-first priority above.

The current needle build deliberately fails when a target has no local bark
attachment. The fixed spruce exposed a shoot inside an unrelated branch:
axis151 overlaps axis24, whose exterior is over four thin-shoot radii away.
Moving those needles to the unrelated limb would conceal a growth defect.
Segment-aware wood clearance and shared-curve contact exposure now allow a
completed individual-needle source on the fixed spruce in62.04s. Residual
crossings, healthy crown coverage and material-view memory remain unaccepted.

Launch clearance replaces endpoint-sphere probes with complete segment
capsules. The same simulation owns a spatial index of each existing
segment's radius-expanded bounds, rebuilt after each seasonal thickening and
updated immediately for new shoots. Each directional candidate is checked over
its entire length against nearby segments in stable index order. Shared
attachment segments and the existing short ancestor allowance remain exempt
for connected collars. The larger endpoint radius conservatively bounds taper;
there is no radius cap tied to occupancy cell size. This is still an approximate
straight-segment growth constraint: it does not guarantee that subsequent
thickening or the mesher's curved interpolation cannot create an intersection.
The original point sampling missed wood between graph nodes. The new index and
distance test replace that path, with no parallel collision implementation.
Same fixed recipes, resource caps, determinism and native source/crown checks
apply; the candidate is not accepted from graph completion alone.

The fixed crossing is now traced to later radial growth. An exact production
replay matches the native graph: the needle target clears the larger branch by
7.375 mm at season13, is inside it by season16, and is6.125 mm inside at18.
The curved versus straight centerline distance differs by only0.095 mm here.
The small shoot remains alive. Source attachment correctly rejects relocation.

The contact-exposure candidate records wholly enclosed intervals on each
living foliage-bearing shoot, with enclosing segment IDs. The complete radius
snapshot after each season owns these intervals. A piecewise quadratic checks
containment of the shoot's local cross-section ball inside the other segment's
linearly tapered swept balls, including finite endpoints. Spatial capsules only
select candidates. Same-axis wood is excluded. The first candidate also exempted
the local attachment collar, but its close-up showed buried shoots projected
into star-like needle bunches on the parent. Covered collar intervals now lose
foliage while keeping their wood and exposed continuation; ancestral wood is
checked by the same rule. One segment-radius function owns both true forks
and thicker single-successor continuations in growth and meshing. Straight-chord
exposure then failed where the derived curve bent back into the parent. The
shared `shoot_curves` function now owns the same nine-sample Hermite path for
exposure and meshing. The exposure index checks each tapered curved subsegment
and merges covered intervals while preserving the enclosing graph segment ID.
This model does not prove exposure of a partially covered circumference or
agreement with rounded and displaced final bark. The source retains its bounded
attachment failure when those representations disagree.

The next season's light and wood support use the exposed fraction. Meshing
uses those same intervals, mapping graph parameters through the derived curve;
needles retain continuous spacing and fixed size while recording candidates,
covered sites and actual output separately. Broadleaf candidates have independent
appearance streams, so suppressing a covered site cannot reshuffle surviving
leaves. Node/parameter attributes retain attachment provenance. No branch is killed or detached solely by this contact
rule, and exposed descendants remain eligible. An automatic subtree-death rule
was rejected: [dead-branch cover-up measurements in cherrybark oak](https://research.fs.usda.gov/treesearch/27767)
and [Douglas-fir branch mortality observations](https://research.fs.usda.gov/treesearch/29345)
do not establish death of live crossing shoots in spruce. Isotropic radius
clamping was also rejected because it would change the support model without
representing directional contact. This remains an authoring approximation in
validation, not a physiological mortality model or natural-crown acceptance.


Foliage density can be rebuilt independently on a completed, unprotected source.
The same graph-derived placement and final-bark binding serve full builds and
foliage-only updates. Temporary foliage publishes only after complete binding;
cancellation retains the previous source. Wood, guides, collision and graph
remain untouched. The density control extends to8 for output comparisons;
stored recipes retain their original density. Wind attachment and leaf-facing
behavior remain separate game validation requirements.


The canopy013 candidate uses the existing shared annual-extension subdivision
for broadleaf axillary sites (Oak.12m,Ash.16m,Birch.10m maxima). These initial
profile values are authoring hypotheses. Light extinction and pipe support use
length-weighted foliage, while the source distributes leaves by that same
physical length and places terminal sprays once per annualunit. Subdivision
therefore no longer multiplies a complete foliage spray at every graphnode.
Ash's per-segment leaf cap/minimum are removed for new length-based profiles so
density and length remain meaningful. Stored legacy profiles retain their count
and placement rules. New source geometry remains derived from the seasonal graph.

[Buck-Sorlin and Bell (2000)](https://doi.org/10.1093/forestry/73.1.1)
measured different shoot-length distributions by branch order and distinguished
median from subapical buds in Q.robur/Q.petraea. This supports representing more
than one bud site in an annual shoot. The current candidate does not reproduce
their full bud-zone distributions, four-bud subapical structure, or calibrated
growth rates; it does not claim the chosen internode maxima came from that study.
Preserved graphs keep their stored profile and geometry; regenerating growth
uses the current profile and records its hash. Full crown review remains required.

The first multiple-site oak improved coverage but failed visual review: too many
similarly ascending main limbs and a weak supporting trunk. The next candidate
weights lateral resource demand toward distal positions within each annual shoot.
Applying that preference to release probability as well produced insufficient
fine branching, so release still follows the existing light/species rule after
resource qualification. The species profile owns the acrotony exponent (currently
1Oak,2Ash,2Birch,0Spruce); these values remain uncalibrated. Supporting radii include the combined child wood under the same
2.2 pipe exponent, including old wood after leaf loss. Only new extension changes
direction toward a gravity-relative lateral inclination; old positions stay fixed.
The bud establishes each branch's inclination. Its future extension approaches
that inclination with age-dependent droop and positive upward bias; imposing one
world angle on every branch order produced parallel tiers. Branch age starts at
its first shoot, rather than its older attachment node. The existing branch angle,
droop and upward controls govern this response.

[Nauber et al. (2024)](https://doi.org/10.1093/treephys/tpae045) supports separating
bud priority, supporting wood and tropic response within a shared growth model.
We adopt those responsibilities, not its complete physiological model or fitted
parameters. Our bud weighting, pipe exponent and inclination response remain
authoring approximations requiring species/age and independent visual validation.
The first candidate exceeded the original node cap at season 22 of the age 30
check. That remained a failure throughout the 013/016 shape comparisons.
Candidate 017 separately revises the offline authoring budget because the denser
graph cannot complete the mature preset; it does not retroactively pass those
checks. Preserved sources and installed evaluation samples remain unchanged.

Primary motion groups now follow every lateral chain attached to the continuous
main stem. The old voxel-radius cutoff incorrectly removed all lateral groups
when the supporting trunk thickened. It is removed from connected source builds;
motion adapters also allow a single trunk group for young unbranched specimens.
The byte payload still limits export to256 structural groups. This is an export
contract, not proof of natural game motion. Camera-facing leaf correction is implemented as an unqualified shader candidate.
New broadleaf exports preserve each authored midrib in octahedral UV2, with the
same pivot and orientation identity at every LOD. The shader maps the folded
blade rigidly into a varied camera-relative frame about its wind-posed base.
Flutter follows this mapping; a bounded lean keeps the blade plane visible.
The mapping runs in world space after instance scaling. A rejected cylindrical
midrib roll caused pole flips and cancelled some flutter; that path is removed.

This changes leaf orientation throughout a camera orbit and may affect the
natural appearance of the crown. Independent source review and payload/numeric
checks permit native testing only. Native UV2 orientation, shader compilation,
wind, camera orbit, shadows and LOD appearance remain unverified while the game
editor is unavailable. The camera-up fallback is finite but does not promise
global continuity outside the normal visible perspective frustum. Existing
materials do not enable the new feature. Canopy013D has66059leaves and verified
attachments, but its middle-crown coverage still fails final visual acceptance.


The015 capture-only growth experiment was rejected: two versions lost all
living tips and a third produced only a3.16m,542-node specimen after18seasons.
Those source changes were removed in full. Direct inspection of Palubicki2009
section4.2 confirms that the baseline bud-light sum and proportional root signal
are consistent with its image-synthesis model. They are not a calibrated carbon
budget. No nominal leaf-capacity field or subapical cluster from015 is adopted.

Candidate016 adds species-owned apical_maturity to the shared solver. Zero
preserves constant apical control. Oak currently relaxes its .60 continuing-axis
weight linearly toward .50 over twelve elapsed seasons after each axis's first
actual shoot; the older attachment's birth is not used. All local terminal/lateral
buds and continuing/lateral children use the same node-axis weight. At .50,
demands receive equal weighting, not equal allocations. The root supply, foliage
eligibility, bud release, branch curves and leaf builder remain the before015
implementation. These values are authoring hypotheses under validation.

The primary paper discusses removing apical control during development using
its priority model (section4.2/Figures10-11). Our continuous relaxation of the BH
weight is an adaptation, not a reproduction or measured English-oak calibration.
Relaxation cannot reopen expired or spent buds. Native source review, exact age
prefix/replay, zero-maturity other-species parity and age30 cap checks determine
what can be accepted. The fixed013D recipe and5.7leaf-density control are retained.

016 native validation produced75770leaves,441610wood faces and63motion groups.
It preserves the graph exactly, has one closed connected wood surface, and all
leaf anchors meet the2um criterion under independent float64 triangle checks.
Same-input replay and age6 prefix/radius/support checks pass. Other species at
age6 match before015 geometry with maturity disabled. The independent reviewer
accepts improved middle/lower coverage, but rejects final oak quality: the top
is pointed and repeated ascending limbs retain narrow terminal sprays. Age30
still exceeds the30000node guard (attempted season23). The sample remains a
candidate. At that revision the default density remained1.9;
5.7 belonged to the comparison recipe. Runtime wind/facing qualification remains
pending in the visible game editor.

Candidate017 addresses a concrete authoring failure: the fixed oak cannot finish
the default mature24-season preset under the original30,000-node guard. The
100,000-node offline guard changes no growth rule, seed, foliage density, or
requested age. It allows the existing shared model to be evaluated at30seasons
before further silhouette tuning. This does not make the earlier30,000-node
scenarios pass. It is a new authoring-budget qualification, and node count is not
a memory guarantee. Live source meshing is conditional on measured graph size,
host available commit/physical memory and cancellable native authoring. Dense
source memory, motion-group capacity, topology, foliage, wind metadata and
independent visual review remain required; age80/all-seed coverage is not implied.


Candidate019 centralizes the offline surface capacity at4million faces. It plans
retained sweep rings and counts capped branch faces before allocating temporary
Boolean objects; the same indices then build the operands. Union and junction
rounding enforce that same limit. The3% radius-relative error bound is unchanged.
Temporary object/collection IDs are batch-retired, followed by unused mesh IDs;
source retirement also preserves shared data and protected specimens. Generator
close still precedes transaction retirement. This addresses017's late rejection
and slow cleanup; atomic native union still cannot yield during its operation.
The cap is not a memory guarantee. Native dense-source qualification is pending.
Game export remains separately capped at1.35million triangles; the346767-leaf
age30 candidate cannot currently export even without wood. More source capacity
does not imply game readiness or independent visual approval.


Dense019 exposed two source bookkeeping scans. Junction grouping now visits a
single sorted sequence of immutable edge indices, skipping edges already grouped;
its seed, DFS and rounding order are unchanged. The generator yields after every
256 groups. Final operand cleanup checks only the Boolean base for a replacement
mesh; other operand meshes are already owned. Native cancellation at the new
grouping yield preserves the saved source and removes all temporary IDs. A full
dense build still needs its recorded provenance: the first019 run loaded the
older grouping loop before this correction. Pending source objects are hidden
until publication, so incremental UV writes do not redraw an unfinished canopy.
The user's previous completed specimen or growth preview stays available.


The first019 native30-season source completed with346767 leaves,94 motion groups
and3277706 closed wood faces. Exact graph replay/prefix results belong to017;
native mesh integrity, all leaf attachments and the dense512x1024 motion payload
pass their recorded offline checks. An independent reviewer approves a source
prototype for future static game evaluation, while withholding final quality:
repeated ascending limbs, bare angular twigs and sculpted root flares remain.
The frozen source records its loaded code separately from subsequent grouping
and visibility fixes. Large-model export and actual game motion remain pending;
no library or catalog promotion follows from these source checks.


Candidate020 makes the reviewed5.7 foliage density the default for newly authored
oaks (previously1.9), across the three age presets and three growth habits.
The geometry species table owns density defaults. Ash, birch and spruce retain
their1.35 presets. The RNA field and preset callback use that owner; a direct
build_tree call with omitted density now also resolves the species value.
Consequently, omitted non-oak density changes from the old function-wide1.9
to its existing1.35 preset. Explicit densities and saved recipes remain intact.
No growth, leaf-placement, surface, or motion algorithm changes in020.

Native020 validation passed all36 preset combinations and eight actual build
entry calls up to the first pre-allocation yield. It also restored the original
Review source at1.9 and F at5.7, preserving scene settings, stored source
properties, geometry ID inventory, and visibility. Independent source review
found no actionable defect. Existing019 at5.7 remains the visual evidence;
020 is not a fresh mesh build or approval of every species/age appearance.
The fuller oak remains a prototype with the019 shape and game-export limits.

## Derived roots and soil contact (021/022, in validation)

Root geometry still derives from trunk size and its own seeded stream. It is
not a competing crown or a root/soil growth simulation. Candidate021 prolonged
the shallow root departure and used length fractions for its radial controls.
Native E18 preserved the graph, every crown sweep and the crown source geometry/
UV fingerprint, all75770leaves, one closed wood component, and leaf attachments
within2um. Independent review rejected the longer pointed radial wedges as a
naturalness fix. The failed source and matched images remain evidence.

Inspection also found the studio floor atZ=-.065 although tree planting uses
soilZ0. Studio creation now places its floor atZ0 and restores an existing floor
to that world height while retaining its horizontal coordinates. The live floor
is aligned; original reference library files remain unchanged. Validation keeps
both the old-floor comparison and a separate same-height before/after baseline.

Candidate022 replaces021's root shaping in the same builder. Lower departures,
less vertically stretched shoulders, and greater seeded variation in angular
spacing, length, curvature and girth target the repeated wedge appearance.
Root count and random-draw order are unchanged; the independent crown/foliage
streams and shared growth graph are unchanged. All radial controls use ordered
fractions of root length, including at minimum root spread. Secondary roots
inherit the resulting parent path and radius. Existing density/age/root controls
remain the user inputs; no new authoring control or alternative mesher is added.

UF/IFAS describes the swollen trunk base and shallow structural-root system;
we adopt that qualitative distinction, not numerical biological parameters.
The new dimensions remain authoring choices. Native geometry, matched-angle
independent visual review and checks at different sizes determine acceptance.


## Interior foliage renewal (024 candidate)

The three-season cutoff left old fine interior twigs bare. The shared eligibility
rule now allows living broadleaf segments to renew foliage while the maximum
canonical segment radius is at most four times the species shoot radius. Recent
shoot eligibility and evergreen needle retention stay as before. This is an
authoring approximation of persistent short-shoot foliage, not calibrated bud
physiology. Dead nodes and old structural wood do not gain this renewal.

The same predicate feeds growth light/pipe support and derived foliage. New
simulations therefore change; loading a stored graph still preserves its exact
positions, ancestry and checksum. One enclosure generator indexes and queries
canonical curved segments. It yields during source work and returns separate
intervals; the simulation explicitly commits them at each season end. Source
rebuilds recompute them without mutating a saved graph or its wood.

Oak's new density default is34.2; other species retain1.35, and saved density
values remain explicit. The density control and recipe loader accept up to64.
Leaf abundance is an authoring output control, independent of the existing
light-extinction coefficient; increasing it alone is not a physiological model.
Each broadleaf attachment records its graph node and support radius. Binding
to final bark checks a maximum displacement of max(4*radius,.002m), matching
the existing local needle bound. This limits distant snapping, but a nearest
surface check does not prove that intersecting close twigs have distinct surface
identity. Graph ancestry and existing exporter motion inputs remain available.

The native024 comparison produced279793 leaves and missed the requested increase.
The subsequent025 density34.2 rebuild produced544606 leaves (7.1876times75770),
including141979 on renewed older fine twigs. Every recorded bark anchor passed
the2um exacttriangle check. Stored graph, wood, guides and collision were unchanged.
Independent review approved initial game evaluation; final botanical shape and
game behavior remain unqualified. Source and comparisons are preserved separately.
No new game sample is accepted. More leaves increase export payload; the existing
1.35million-triangle import guard stays in force. Wind, leaf-view correction,
LOD transitions and runtime cost still require the visible game editor.


### Oak export samples and juvenile root attachment (027)

The mature dense oak (544606 leaves) and historical six-season sapling (5753
leaves) are exported and installed as evaluation prefabs. Both retain all leaves
across three LODs, with independent trunk collision pieces and branch interaction
volumes. See [Blender import](BlenderTreeImport.md) for packaging and native
validation limits. Independent review permits initial game evaluation, not final
botanical approval; repeated foliage sleeves/clumps remain on the historical
sapling. Its saved graph predates the current finer-internode species profile.

The sapling exposed a root attachment defect: a fixed juvenile origin 0.095m
below grade detached its root assembly from a 0.021m-radius bole starting at Z=0.
Juvenile collars now begin inside the bole at 1.1 base radii, then descend below
grade. The derived juvenile stem extends two base radii below its first graph
point to avoid tiny Boolean components where roots crossed the old end cap.
The stored graph, guide paths and collision inputs stay unchanged; derived trunk
flare and UV normalization can change above grade. Adult formulas are unchanged.
This repair applies to all juvenile species, but only the fixed historical oak
was rerun here: one closed component, no boundary/nonmanifold edges, all5753
leaf anchors within2um. Broader juvenile qualification remains incomplete.
