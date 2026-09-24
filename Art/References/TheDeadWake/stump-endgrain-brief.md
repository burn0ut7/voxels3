# The Dead Wake - stump end-grain albedo v1

- Date: 2026-09-24.
- Active texture master: [stump-endgrain-v1.png](stump-endgrain-v1.png).
- Runtime copy: [Assets/textures/deadwake/stump-endgrain-v1.png](../../../Assets/textures/deadwake/stump-endgrain-v1.png).
- Source/tool: built-in `image_gen.imagegen`, one reference-assisted generation.
- Input references opened before production: [tree-felling-v1.png](tree-felling-v1.png), accepted pale cut-face direction; [stump-inspection-v1.png](../../../Docs/ValidationEvidence/DeadWake/Inventory/stump-inspection-v1.png), actual bark-covered cut face requiring correction.
- Generated source: `C:/Users/Gray/.codex/generated_images/01a0d1b5-cca4-7ae0-8d1d-e6a427834e12/exec-40395713-9c7a-461a-b057-5189b786af08.png`.
- Actual dimensions: 1254 x 1254 pixels; native tool output differs from requested 2048 x 2048. No resizing or image manipulation was applied.
- Format: PNG, every pixel verified fully opaque (minimum alpha 255).
- File size: 3,507,269 bytes.
- SHA-256: `DB6600FBEB35AE715956EE3B4AEDA390913FE4A6B0020060FE287210AB0749B5`.
- Version: v1. New reference and runtime copy; existing textures and briefs preserved.

## Use and defining traits

This texture supplies natural pale warm tan oak end grain for the stump's freshly exposed top face, replacing the visual mismatch of bark across the cut. The image is filled edge-to-edge with wood: organic near-central growth rings, fine porous grain, radial fibers/rays, and restrained small checks. No bark rim, trunk sides, surrounding scene, labels, object silhouette, perspective, or transparent background are included.

The accepted image was first saved outside runtime imports and reopened for visual inspection, then copied byte-identically to the runtime texture path. It is intended for one cut-face UV region centered on its ring center, not as a repeating seamless bark texture. Side bark remains a separate surface responsibility for the implementing agent.

## Inspection and limits

The saved image shows plausible continuous end grain, pale cut-wood color and near-central rings with clear readable structure. Lighting is broadly even, without a directional cast shadow or background. The result is an AI-generated albedo asset rather than a measured/calibrated oak material. Small local pore/fiber contrasts may contain implied microrelief.

The texture alone does not verify correct material assignment, stump-cap UVs, texture scale, bark/cap seam, color under game lighting, collision geometry, cut shape, or runtime costs. Those require the implementing agent's actual rendered checks. The original texture-generation-only step made no material, geometry, code, editor-control, or commit changes.

## Exact generation prompt

```text
Use case: photorealistic-natural
Asset type: ONE original natural oak END-GRAIN ALBEDO/base-color texture for the freshly chopped top face of a small tree stump in The Dead Wake. Square 2048x2048 target.
Input images: Image 1 is the accepted felling concept showing pale natural cut wood above dark bark. Use ONLY this pale cut-wood material direction. Image 2 is the actual current stump with an incorrect bark-covered cut face; it explains the replacement need, NOT a texture to reproduce. Do not reproduce the bark, scene, UI, grass, axe, or object silhouette.
Primary request: Fill the ENTIRE square image, all edges and corners, with one uninterrupted opaque flat oak wood end-grain surface. Straight-down ORTHOGRAPHIC material scan. This is a small sampled square area within the cut cross-section of a large oak; the tree's outer edge and bark are completely OUTSIDE the image. No circular object outline and no empty background.
Material: pale warm natural tan/light brown freshly exposed oak wood, softly irregular annual growth rings centered near the middle of the image, rings continuing beyond the square edges. Natural uneven spacing and organic concentric shapes, distinct fine porous oak grain, subtle radial medullary rays, fine fibers and restrained shallow axe/tool roughness across the cut. A few tiny natural checks may appear, but no giant cracks, holes, rot or sawblade graphic marks. Believable neutral realistic material; grain readable at small in-game stump size without becoming a high-contrast illustrated target or dartboard. Natural low-to-moderate contrast between growth rings, subtle color variation.
Lighting: flat diffuse completely even illumination; ALBEDO color only. No directional shading, no broad highlights, baked shadows, ambient occlusion, glossy varnish, reflected light, depth gradient, vignette or edge darkening. Surface details represented by natural color variation; engine will supply shading.
Constraints: every pixel is wood surface, opaque PNG, square frame filled edge-to-edge, no transparency, no side bark, no bark rim, no full stump object, no trunk sides, no grass/soil/foliage, no surrounding scene, no perspective, no border or frame, no text/logo/watermark, no ruler. NOT a seamless repeating tile: single centered end-grain cross-section area for one stump top UV; opposite rings need not tile. Do not place a circular cutout on a black, white or transparent background.
```


Fitted contour revision: the early 24-sided radius-10 cap left a visible open
crescent at the imported bark edge. The active end-grain image is unchanged.
`Tools/DeadWake/build_cut_profile.py` derives four closed, star-shaped contours
from native LOD0 model/plane intersections at local Z14. The largest trunk
contour (76 vertices) also defines the stump; the other 20/24/23-vertex contours
close low branch intersections on the fallen model. No convex hull substitutes
for the actual mesh boundary. `Assets/models/deadwake/oak-cut-v1.json` stores the
contours; exact native input is preserved in
`Docs/ValidationEvidence/DeadWake/Inventory/oak-cut-section.json`. This geometric
fit intentionally follows the authored oak rather than forcing a perfect circle.
Felled root-wood LOD0 (with canopy detail retained) and wind-zero attributes keep that cut edge static. Runtime cap models
are small derived meshes, with no CPU extraction of the complete tree in play.
