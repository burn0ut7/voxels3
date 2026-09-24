# The Dead Wake - prototype visual reference v1

- Active reference: `prototype-v1.png`.
- Date: 2026-09-24.
- Source/tool: built-in `image_gen.imagegen`, reference-assisted generation.
- Creative direction and visual inspection: requested Astra Extra High setup.
- Context reference: `../../../Docs/ValidationEvidence/TreePerformance/single-tree046-verified-full.png`, inspected before generation; used for temperate broadleaf foliage, green terrain, stream, and readable daylight.
- Source generated file: `C:/Users/Gray/.codex/generated_images/01a0d1b5-cca4-7ae0-8d1d-e6a427834e12/exec-2d3d7a02-854e-4c16-ad94-e829fd27d5ee.png`.
- Saved image dimensions: 1672 x 941 pixels (approximately 16:9; tool output differs from requested 1920 x 1080).
- Runtime candidate paths: `Code/Survival/DeadWakePropModels.cs`, `Code/UI/DeadWakeHud.razor` and `.scss`, `Assets/materials/deadwake/*.vmat`. Reference remains outside runtime imports.

## Status and defining traits

This is an AI-generated visual concept and HUD mockup, not captured gameplay and not proof of any implemented feature, rendering capability, content density, or performance. It establishes intended direction only. The visible “VISUAL CONCEPT” label preserves that distinction in the image.

Bright temperate valley, rolling smooth terrain, rocky slopes, grass and broadleaf trees; a winding stream and distant abandoned settlement with water tower provide landmarks. A modest plank foundation and two wall segments show early building scale. Natural raw timber, gray stone, olive greens, and crisp readable daylight support grounded survival play. The HUD uses restrained charcoal translucent panels, ivory text, thin borders, muted health/stamina bars, a five-slot hotbar, wave timing, and a compact objective card.

Intentional departures from the existing screenshot: richer terrain/vegetation/material detail, a denser landscape, ruined town, player-held tool, construction, and HUD are aspirational additions. The reference does not demonstrate crafting, resource progression, learning-by-doing, quests, zombie AI, structural gravity, or water simulation. The objective card is an illustrative layout; its sample unchecked actions are not a coherent implemented quest state.

## Exact generation prompt

```text
Use case: ui-mockup
Asset type: one 16:9 in-game visual concept and practical HUD mockup for the survival voxel game The Dead Wake. This is an aspirational concept, never a claimed gameplay screenshot.
Input images: Image 1 is a style/context reference only: existing project's green broadleaf tree, rolling grassy terrain, blue stream and readable daylight. Preserve this temperate natural direction, expand it into a richer imagined playable valley; do not reproduce its single-tree composition.
Primary request: create a polished, mostly realistic high-fidelity first-person game-view concept. Large interesting smooth voxel terrain, natural rolling hills and exposed rocky slopes, a winding shallow stream in a rugged temperate valley, distant ruined small town with modest recognizable roofs and water tower. Start-of-survival feeling, fun readable geography over strict realism.
Subject and composition: eye-level player viewpoint standing beside a newly built very simple timber foundation in the left foreground, two unfinished plank walls with readable posts and attachment corners, no finished luxury base; scruffy grass, a broadleaf tree and a few loose resources nearby. A modest hand-held stone axe may occupy lower right without covering the view. Open center view leads down the valley toward the town. Smooth terrain without visible cube grid, natural organic rock forms; detailed but plausible game rendering, not photography.
Lighting/mood: bright overcast afternoon with some warm sun, natural greens, gray stone, warm raw timber; readable shadow detail, gritty wear without extreme grime. Survival tension through abandoned settlement, not horror gore.
HUD: restrained, practical, crisp white sans-serif text with dark translucent small panels, generous padding and thin warm-gray borders. Top left small title exactly "THE DEAD WAKE", beneath it tiny label exactly "VISUAL CONCEPT". Top center compact strip exactly "DAY 1  |  NEXT WAVE 08:42". Lower left two narrow simple bars labeled exactly "HEALTH 100" and "STAMINA 100", muted red health and muted pale olive stamina. Bottom center exactly five evenly sized outlined tool slots numbered 1, 2, 3, 4, 5; slot 1 selected with a thin warm ivory border, simple clear stone axe, pick, hammer silhouettes then two empty slots. Right side compact task card with heading exactly "FIRST SHELTER", next lines exactly "Gather wood  12 / 20" and "Craft a stone axe" and "Place a foundation". A tiny unobtrusive crosshair center.
Constraints: one coherent 16:9 image, target 1920x1080; practical contemporary survival game visual language. Landscape takes most of the image. No cinematic letterboxing, no thick ornamental frames, no bloody screen edges, no science fiction or neon, no giant title overlay, no dense inventory windows, no feature checklist. Legible HUD typography, natural timber geometry, plausible trees and organic smooth terrain. This reference establishes intended art/UI direction only; do not include any claim of implementation.
```

## Version history

- v1: initial saved concept; no prior concept overwritten.

## Superseded opening prototype

The first candidate follows the charcoal/ivory layout, warm timber and stone tool.
Intentional scope departures: two real selection slots, real inventory quantities
and one crafting objective; health, stamina, construction and wave-clock widgets
are deferred with their gameplay owners. Supplies are finite session-only props.
No hand/arm animation is present yet. Current basic_example world retains its
existing terrain and one oak, rather than claiming the illustrated settlement
or dense vegetation. Rendering/motion qualification is still pending.

## Opening tool revisions

The oversized oval head and peg-like ties in `opening-crafted.png` were rejected.
Later candidates use a smaller chipped wedge, three closed cord loops and a
continuous stone-face UV. The generated [flint albedo](flint-albedo-v1.png) replaces
the visibly mottled loose-rock texture on this held tool; see [texture brief](flint-brief.md).
Runtime copies live in `Assets/textures/deadwake/` and `materials/deadwake/flint.vmat`.
`Docs/ValidationEvidence/DeadWake/opening-crafted-v5.png` and `motion-v5/` record the
revised silhouette, material, HUD and swing. The later dominant-plane edge-UV correction is rendered in the replacement
inventory candidate below; v5 does not establish that later change.
The mixed `motion-v4/` capture is excluded as a continuous final sequence; its
capture-note records the overwritten frames and remaining limitation.


## Active inventory and tree direction

The user rejected the Pack counters and ground-log gathering loop. The active
[inventory concept](inventory-v1.png), [tree-felling concept](tree-felling-v1.png)
and their briefs govern the replacement. It has 27 backpack cells, nine hotbar
cells, real stack operations, icons and adjacent paid crafting. Branches and trunk
wood come from the authored standing tree; scattered log harvesting is removed.

Actual `Docs/ValidationEvidence/DeadWake/Inventory/crafted-inventory-v2.png` records
the replacement inventory. `fixed-axe-contact-v1.webm` records the final input path's
continuous preparation/contact/recovery with timestamps; old feedback clears at
wind-up and the new reward appears at contact. `tree-final-fall-v3.webm`, the fitted
cut front/oblique images and `dropped-axe-v4.png` record the revised tree/drop finish.
Earlier captures remain history. Independent review qualified the bounded offline
opening; the ledger preserves exact visual, performance and scene-scale limits.
