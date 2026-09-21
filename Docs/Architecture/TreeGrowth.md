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
source format requires a matching game adapter before export qualification.

## Boundaries and budgets

The solver is independent of Blender and game runtime. Blender owns user
controls, graph persistence, preview and mesh creation on its main thread.
Advance one season at a time during interactive preview; cancellation discards
the unfinished result. Never publish a partial graph as a completed specimen.
Source meshing also yields between bounded stages and polygon batches. It uses
a temporary collection and replaces the previous source only after completion.
Esc or Cancel Build removes temporary IDs; it leaves the finished source intact.
Individual native modifiers remain atomic. Mesh builds omit Blender undo
snapshots to avoid retaining several gigabytes per rebuild; save a separate
working file for historical source revisions.

Node/season caps are explicit errors, not hidden thinning. The initial hard cap
is 30,000 nodes and 80 seasons per specimen. The current source adapter predicts
post-subdivision wood faces and rejects more than 1,000,000 before allocating
them. `surface.py` constructs convex local collars with open branch ports,
bridges shared ring vertices, checks a closed connected surface and applies one
finite Catmull-Clark subdivision (limit-surface projection disabled).
Coplanar panels dissolve before subdivision; straight
unclipped spans omit redundant midpoint rings. Both affect the rounded taper
and require visual review after topology checks. Port clearance can narrow a very acute or crowded local
join. It never removes a graph branch. Derived root nodes retain explicit
ancestry and join the same surface; roots remain artist-directed geometry.
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
not arbitrary floating-point platforms or future versions. Age presets select 6, 24 and 30 seasons; the wider 1–80 input range does not
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

[Palubicki et al. (2009)](https://algorithmicbotany.org/papers/selforg.sig2009.html)
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
all object positions. See TREE-GROWTH-003/v1 for source hashes, measurements
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

The independent reviewer accepted the fixed oak18 socket/disconnection repair
from three bare angles, a foliage close-up and an uncovered root view. It found
minor bark bands and uneven thickening, plus broader realism limits in straight
internodes, pointed tips and stylized roots. This is narrow visual acceptance,
not proof of botanical fidelity. TREE-GROWTH-006 records the connected mesher's
own measurements. Graph positions remain unchanged when rebuilding a saved tree;
new growth uses the shared foliage-renewal rule in light and radial support.

The large-spruce memory investigation isolated a costly default infinite-limit
projection in Blender subdivision. The finite subdivision mode reduced its
measured build from111.4s to78.9s with the same942,720 faces and1,006,656
needles. The full retry still peaked at13.875GiB private allocation
(6.293GiB working set) in the cumulative editor session. The earlier8GiB
private-memory target is not met and remains open; do not label the complete
resource acceptance as passed. See finite-memory-v1.json and the raw phase logs.
