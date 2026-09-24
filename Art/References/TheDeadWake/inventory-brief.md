# The Dead Wake - inventory reference v1

- Date: 2026-09-24.
- Active inventory reference: [inventory-v1.png](inventory-v1.png).
- Source/tool: built-in `image_gen.imagegen`, single reference-assisted generation.
- Actual dimensions: 1672 x 941 pixels, approximately 16:9; tool-selected output differs from requested 2048 x 1152. No image resampling or edits were applied after generation.
- File size: 2,275,288 bytes.
- SHA-256: `2CCEB13E9AD553CDB03A705AFA3CA37BF0AE0C3E645231F7D8B3CDE23DC34500`.
- Generated source: `C:/Users/Gray/.codex/generated_images/01a0d1b5-cca4-7ae0-8d1d-e6a427834e12/exec-b6bf7932-eaea-45a3-b8fb-106deabb6ace.png`.
- Inputs reopened before generation: [prototype-v1.png](prototype-v1.png) for accepted world/material direction; [opening-crafted-v5.png](../../../Docs/ValidationEvidence/DeadWake/opening-crafted-v5.png) for current charcoal/ivory HUD style.
- Runtime asset paths: none. This task creates only a visual reference and its brief, outside runtime import folders.
- Active version: v1. Earlier overall/hatchet concepts remain preserved; this supersedes their inventory layout direction where they show Pack counters or a short hotbar.

## Corrected scope and defining traits

This AI-generated inventory-open mockup is an aspirational visual reference, not a screenshot of implemented functionality. The image explicitly labels itself a visual concept and its items/counts illustrative.

The corrected direction is a traditional survival slot inventory and nine-slot hotbar. A centered charcoal window uses generous spacing, ivory sans-serif text, understated warm-gray outlines, and clear selected-slot/recipe borders. World margins remain visible, with standing trees and natural terrain contextualizing the interface. The source concept's richer landscape remains aspirational and does not expand implementation scope.

The backpack has exactly nine columns and three rows (27 slots), with wood 12 and stone 6 shown in the first two slots and the remaining cells empty. The matching nine-slot hotbar sits directly below; keys 1 through 9 are legible, slot 1 is selected and holds a stone axe, and slots 2 through 9 are empty. A compact adjacent crafting card shows the stone axe recipe, wood 4/12 and stone 2/6 (required/available), and a clear CRAFT button. Header, Tab close hint, close X, and left/right-click footer instructions remain visible.

The user correction calls for wood from standing trees and removal of the ground-log gathering loop. The mockup does not demonstrate harvesting, but its environment uses standing trees and avoids a ground-log pickup presentation. It replaces the old Pack counter UI. It adds no health/wave displays, equipment slots, inventory weight, or extra recipe complexity.

## Inspection and implementation limits

The saved image was reopened and inspected. The 9 x 3 backpack and nine numbered hotbar cells are countable; displayed ingredient counts match sample inventory quantities; footer instructions and close controls are readable. The backdrop remains visible around the window.

A static mockup cannot verify picking up, placing, merging, splitting, placing one item, crafting transactions, keyboard focus, Tab dismissal, slot selection, stack limits, overflow behavior, tree harvesting, or mouse capture. These anticipated interactions need runtime implementation and verification. Small generated item icons are illustrative; the runtime axe icon should represent the actual existing tool consistently. This image does not supply standalone icon assets or authorize a tool redesign.

## Exact generation prompt

```text
Use case: ui-mockup
Asset type: one landscape 16:9 INVENTORY OPEN game UI mockup for The Dead Wake, target 2048x1152. AI visual concept only, not a gameplay screenshot.
Input images: Image 1 is the accepted art direction: natural realistic temperate valley, green broadleaf standing trees, daylight, raw timber and gray stone, ivory/charcoal UI. Image 2 is current HUD style context: clean practical sans-serif typography, restrained translucent charcoal panels and thin warm ivory selection borders. Replace its Pack counters/two-slot hotbar design with the new inventory; do not repeat its old HUD.
Scene/backdrop: readable bright temperate valley from the accepted concept, broadleaf STANDING trees nearby, organic grassy ground, natural stone and distant hills. Softly dim the world beneath the inventory, but preserve generous visible world margins. No fallen-log gathering, no ground log pile, no foundations or city required. No character hands or tool blocking inventory.
Primary request: polished coherent traditional survival slot inventory inspired by familiar Minecraft / 7 Days to Die interaction but in the project's grounded realistic visual style. One centered wide window, approximately 72 percent viewport width and 62 percent height. Matte translucent charcoal panel, subtle thin warm-gray borders, ivory text, comfortable clear spacing. No ornate frame, neon, sci-fi, bright gamey gradients, oversized cards.
Window header exactly "INVENTORY", top-right small "TAB  CLOSE" and a clear close "×". Tiny context label outside top left exactly "THE DEAD WAKE" with "VISUAL CONCEPT" beneath it.
Window left main section, label exactly "BACKPACK": a precisely regular item grid of EXACTLY NINE COLUMNS and THREE ROWS, 27 square slots. Each row has nine cells; all cells same size and evenly spaced. Thin understated borders and dark slot interiors. First row first slot has a small recognizable brown wood item icon with stack count "12" bottom-right; second slot a gray stone icon with stack count "6"; all remaining backpack slots empty. Do not add row labels/numbers or turn counts into separate counters.
Directly underneath backpack grid, label exactly "HOTBAR": ONE row of EXACTLY NINE square slots matching the nine backpack columns. Small key labels in top-left of each slot read exactly "1", "2", "3", "4", "5", "6", "7", "8", "9". First slot selected with a thin warm ivory border, containing a clear small stone axe icon with natural wood handle, gray flint head and brown cord; slots 2 through 9 empty. No duplicate detached bottom hotbar; this row is the hotbar while inventory is open.
Window right adjacent crafting section separated with a subtle vertical rule: heading exactly "CRAFTING". One selected recipe row with a tiny stone axe icon and text exactly "Stone axe". Beneath, larger but compact preview of the same stone axe, recipe title exactly "Stone axe", ingredient rows exactly "Wood  4 / 12" and "Stone  2 / 6", followed by small caption exactly "Required / available". Clear wide enabled button exactly "CRAFT". Keep recipe counts internally consistent with backpack sample counts. No other recipes or equipment slots.
Window footer small readable instructions in two lines exactly:
"LEFT CLICK  Pick up / place / merge"
"RIGHT CLICK  Split stack / place one"
Add a tiny unobtrusive bottom corner image label exactly "Illustrative items and counts".
Constraints: NO PACK PANEL, no resource total counters outside slots, no health bars, stamina, day/wave timer, quests, equipment paper doll, durability bars, inventory weight, search/filter UI, fake implemented feature claims, loot windows or decorative controls. Most slots empty. Nine columns must be countable. Crisp legible practical typography and clear wood/stone/axe icons. The scene's world stays visible in margins. This is a concept for the corrected inventory direction, not proof of implementation.
```
