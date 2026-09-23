# Animated baked foliage

049 accepted, September 23, 2026. The user authorizes grouped leaf motion;
wind animation remains required. Runtime acceptance is recorded in the ledger.

The installed original leaf FBXs, UVs, materials and motion texture are inputs.
`Tools/BlenderTrees/bake_foliage.py` partitions all 544606 source leaves by primary
branch identity, then repeatedly splits the widest spatial group into 4096 patches.
Each patch retains a 3D center, one branch identity and a nearby source motion
weight. Source FBXs, original motion, wood, collision and the saved scene stay
unchanged. This is an authored foliage approximation, not exact leaf retention.

The tool selects 64 representative cluster shapes by leaf-count quantile and at
most 32 spatially spread blades per template. Original leaf artwork, material tint,
and resting leaf-facing pose produce 512 px color/coverage and normal images.
An 8 x 8 atlas is 4096 square. Each patch reuses its quantile's template on one 3 x 3
subdivided card. Four existing canopy model resources retain their prefab roles;
all three model LOD entries share the same derived mesh for each canopy piece.
There are 32,768 leaf triangles, 97.0% below the original selected 1,089,212.
This geometry count does not by itself establish a frame-rate improvement.

A derived numeric RGBA16 motion texture preserves the original branch table and
adds patch centers after the original leaf entries. The position scale remains
owned by the source motion metadata, never inferred from the new render bounds.
`tree_baked_foliage.shader` poses each center with the existing shared branch and
root wind. Small card ripples and angular flutter move the baked leaf images.
The actual render camera owns facing in visible, depth and shadow passes through
the existing TreeModelLod attributes. There is no per-leaf CPU animation or
runtime texture generation. Leaves within a patch move together.

The native distant baker captures each view with its actual camera frame and
waits for the baked color/normal/motion textures to load at the expected sizes.
Previously its fixed overhead leaf-view override did not follow capture angles.
All four distant rows must be rebuilt and packed after installing new cards;
restart Play to refresh material copies. Existing 12/16 m hysteresis and 0.35 s
handoff remain owned by TreeModelLod. No new population or network owner is added.

The visible Blender job imports one source part per timer tick, prepares a
private authoring scene, then invokes native asynchronous renders. Cancel Render
cancels the job; a staging `cancel` file stops it between stages. The original
scene is restored and temporary objects are removed. Outputs stay outside Assets
until the explicit install step verifies source hashes, tool hash, completeness
and output hashes. Installation writes dependencies first and publishes the new
manifest last. Distant resources reject mismatched hashes during publication.
Regenerating original source requires rebaking; stale results cannot install.

Rejected representations: 512 large crossed patches were fast but exposed flat
horizontal strips; 4096 unique 128 px pictures still lost fine leaf detail and used a
larger atlas. Small facing patches with reused sharper templates preserve useful
nearby detail at a bounded texture cost. They accept repeated shapes and coupled
motion. Whole-tree nearby impostors, a second population renderer, and permanent
stress-grid scene objects are outside this change. Qualification uses the same
049 close observations, 038 single-tree figure-eight and 048 forest placements.

Measured qualification: close-view combined depth/forward/shadow scopes fell
from 5.1843 to 3.1266 ms (39.7%); rolling FPS rose from 153.7 to 239.0.
The unchanged canonical figure-eight passed frame pacing, allocation, memory,
streaming and correctness gates. Visible wind passed fixed-camera tracking.
See the [049 report](../ValidationEvidence/TreeBake049/report.md) for the full
parameters, failed candidates, raw evidence and limits of these measurements.
