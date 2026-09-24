# The Dead Wake - tree felling reference v1

- Date: 2026-09-24.
- Active reference: [tree-felling-v1.png](tree-felling-v1.png).
- Source/tool: built-in `image_gen.imagegen`, single reference-assisted generation.
- Actual dimensions: 1672 x 941 pixels, approximately 16:9; requested 2048 x 1152 but retained native tool output without modification.
- Size: 2,372,979 bytes.
- SHA-256: `2B4A01E1CB1F98A75EFD11938919C73D05CBF643DBAC2C100B5377E5784E6D64`.
- Generated source: `C:/Users/Gray/.codex/generated_images/01a0d1b5-cca4-7ae0-8d1d-e6a427834e12/exec-06348f17-5311-4129-b0f3-82ee668f5db6.png`.
- Opened input references: [actual current tree](../../../Docs/ValidationEvidence/TreePerformance/single-tree046-verified-full.png), primary geometry/environment direction; [prototype-v1.png](prototype-v1.png), supporting natural art direction.
- Runtime asset paths: none. Reference is outside runtime imports.
- Version: v1; no earlier asset overwritten.

## Scope and defining traits

The user correction requires wood from standing trees and removes the ground-log gathering loop. This AI-generated storyboard expresses the resulting felling sequence using a full leafy broadleaf tree, matching the actual reference's asymmetric crown, trunk/branches, green grassy riverbank, rolling terrain, and daylight.

1. Final impact: axe contacts the lower trunk and the finite wood award appears once.
2. Fall away: the full tree inclines away from the labeled near-left player side, keeping crown and branches attached; a short rooted stump remains.
3. Rest on ground: the fallen trunk and branch structure rest against the terrain; the short stump remains at the original base. There is no second ground-log harvesting loop and no repeated award.

The image is visibly labeled as a visual concept and not gameplay capture. Observer-camera arrows and labels describe motion intent; they are not new gameplay HUD requirements. No player hands/arms, extra pickup objects, equipment, or additional systems are required.

## Inspection and limits

The actual saved image was reopened and inspected. Three distinct poses are visible; the reward appears only in the final-impact panel; the player side, fall direction, short stump, and ground-rest intent are readable. The broadleaf crown and branching trunk remain present through the depicted sequence.

Generated imagery approximates the real model; exact topology, branch silhouette, dimensions, collision shape, cut seam, stump fit, and rigid-body transforms must come from the actual asset. The small illustrative axe does not redefine the existing stone-hatchet design. The storyboard shows intended ground contact, not evidence of physically correct resting behavior.

Static poses cannot verify continuous motion, duration, impact timing, branch/crown collision, terrain clipping, safe fall direction under slopes/obstacles, physics stability, resource admission, full-inventory behavior, duplicate prevention, or performance. Those remain for in-world motion evidence and independent review. This reference does not endorse floating, terrain penetration, snapping, or a newly harvestable fallen-log phase.

## Exact generation prompt

```text
Use case: stylized-concept
Asset type: one coherent 3-panel labeled tree-felling motion storyboard for The Dead Wake. Saved reference, NOT captured gameplay.
Input images: Image 1 is the PRIMARY geometry/environment reference: the actual installed game's broadleaf oak-like tree, full asymmetric dense dark-green leafy crown, gray-brown branching trunk, on a grassy smooth rolling riverbank under readable daylight. Preserve THIS TREE'S recognizable full silhouette, branching structure and foliage across the sequence; do not substitute a pine, palm, leafless tree, cylindrical log or different tree. Image 2 is accepted overall natural-material and ivory-on-charcoal visual direction only; do not introduce its construction, HUD, town or quest features.
Primary request: landscape 16:9 storyboard target 2048x1152 with THREE equal side-by-side panels showing the SAME whole tree at the same location from a consistent elevated three-quarter observer camera. This is a useful labeled motion guide, not first-person gameplay. Keep the entire tree and its whole fallen extent visible by having it fall diagonally AWAY into the background/right; preserve ground scale and camera between panels. Same simple grassy bank, same stream behind it, same gently rolling landscape, bright blue partly cloudy sky and grounded game-rendered daylight from Image 1. No cinematic darkness. Natural colors and readable contact with the terrain.
Top heading exactly "THE DEAD WAKE — TREE FELLING REFERENCE". Subtitle exactly "VISUAL CONCEPT • SAME TREE THROUGHOUT".
Panel 1 label exactly "1  FINAL IMPACT". The reference tree remains fully upright, cut/notch low on trunk, compact gray stone axe blade touching the trunk at that cut from the near-left player side. The tool shaft may enter the image edge; NO hand or arm required. A few tiny wood flecks at visible contact. Small restrained text beside the cut exactly "WOOD ADDED". Caption exactly "Finite reward at the last strike". A tiny ground annotation at near-left exactly "PLAYER SIDE". Do not show a person.
Panel 2 label exactly "2  FALL AWAY". Same entire tree intact with all branches and leafy crown, now visibly leaning diagonally 40–55 degrees AWAY from the near-left player side toward the open background/right. Show the severed trunk rotating from the low cut point in a physically plausible arc. Short stump remains rooted at original position. Thin ivory curved guide arrow shows fall direction away from the player. Caption exactly "Continuous arc away from player". No reward popup in this panel; no new tree generation, teleport or disappearance.
Panel 3 label exactly "3  REST ON GROUND". The SAME intact leafy tree lies on its side diagonally away from viewer across the grass, trunk and main branches resting against terrain, not floating or buried, crown still attached and visible. A short bark-sided stump with pale cut top stays rooted at original base; fallen trunk begins beside it and extends away. Minor natural contact settling only, no explosion. Caption exactly "Ground contact • short stump remains". Secondary small caption exactly "No ground-log grind • no repeat reward". No scattered collectible logs, no interact prompt on the fallen tree, no wood popup.
Bottom note exactly "MOTION INTENT — NOT GAMEPLAY CAPTURE".
Constraints: physically plausible same-size full tree, retained foliage and branches in every panel, visible trunk/base and terrain contact, finite wood award ONLY in final-impact panel, no new gathering loop after felling. Same fixed camera/location/lighting; legible modest labels and dark neutral panel gutters. Concept matches current broadleaf tree art direction instead of making a newly designed asset. No inventory windows, Pack counters, fake health/wave system, building additions, character arms or complex effects. Tool/reference art is illustrative; do not claim existing behavior.
```
