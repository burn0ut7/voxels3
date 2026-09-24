# The Dead Wake - inventory icons v1

- Date: 2026-09-24.
- Active reference: [inventory-v1.png](../inventory-v1.png), reopened before asset production.
- Supporting tool identity: [opening-crafted-v5.png](../../../../Docs/ValidationEvidence/DeadWake/opening-crafted-v5.png), previously opened and passed to axe generation.
- Source/tool: built-in `image_gen.imagegen`. Three independent icon generation calls; one targeted axe-head correction.
- Active assets: [wood-v1.png](wood-v1.png), [stone-v1.png](stone-v1.png), [stone-axe-v1.png](stone-axe-v1.png).
- Runtime copies: [wood.png](../../../../Assets/ui/deadwake/wood.png), [stone.png](../../../../Assets/ui/deadwake/stone.png), [stone-axe.png](../../../../Assets/ui/deadwake/stone-axe.png).
- All native outputs: 1254 x 1254 PNG with alpha, despite requested 1024 x 1024. Originals are preserved unchanged; no resizing, cropping, recoloring, alpha modification, or procedural image editing was used.
- Actual accepted images were saved in Art, reopened and visually inspected, then copied byte-identically to runtime paths.

## Defining traits and scope

Natural realistic material icons for dark charcoal inventory slots: a compact three-piece raw wood bundle, one chipped neutral-gray stone chunk, and one single-sided primitive stone hatchet. Items are centered on transparent backgrounds with visible padding and no labels, stack counts, borders, scenic backdrop, or ground shadow plane. The axe's first draft incorrectly showed two prominent blade ends; the corrected asset has one main blade and a short blunt poll, retaining wood and three close cord wraps.

The icons supplement the inventory reference; they do not replace the active overall concept or redesign the runtime tool. The wood bundle is an inventory resource icon, not authorization for a new ground-log gathering loop. The user correction requiring standing-tree wood remains in force.

## Alpha and integrity checks

System.Drawing identified all files as Format32bppArgb. Every pixel's alpha was inspected without changing the image. Transparent, partial-alpha, and opaque counts are recorded below. Generated object interiors are mostly alpha 251-254 rather than uniformly 255; sampled dominant nonzero alpha values are listed to avoid incorrectly reporting fully opaque interiors. This is native tool output.

| Icon | Bytes | Alpha 0 pixels | Alpha 1-254 pixels | Alpha 255 pixels | Nonzero-alpha border pixels | Dominant sampled nonzero alpha |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Wood | 1,299,548 | 1,118,385 | 453,878 | 253 | 0 | 252, 253, 251 |
| Stone | 1,646,170 | 980,101 | 592,127 | 288 | 24, all alpha 1 | 253, 252, 251 |
| Stone axe | 729,431 | 1,326,423 | 245,702 | 391 | 0 | 253, 252, 254 |

Visible alpha >16 bounding boxes in pixels (min X, min Y, max X, max Y): wood (107,186,1160,1109); stone (125,217,1140,1099); axe (225,95,1086,1196). The axe lower margin is approximately 4.5%, less than the requested roughly 10-15%, but the complete handle remains visible and uncut. Transparent margins differ between icons; runtime layout should contain each image within its slot. The stone's 24 edge pixels at alpha 1 are negligible native background residue, recorded rather than silently altered.

| Icon | SHA-256 |
| --- | --- |
| Wood | `FD3E39ECA3C8C004BEB671368282896CD0F9EB55DCA01FA97B391CA5EAD2FF86` |
| Stone | `E661054DB8B9773BCA798159C69CD16B9F6768DCD4CAA005FDEBF79723E0E910` |
| Stone axe | `520E213670641C264807130F99D5F9964C977099912F117F2829083FE9F0493F` |

Full-resolution inspection confirms distinct readable silhouettes and expected material identities. Appearance at actual small UI sizes, dark-panel compositing/fringing, texture import, memory cost, and integration remain for the implementing agent's rendered UI checks. No runtime code or editor controls were changed in this asset task.

## Preserved sources and history

All generated sources remain under `C:/Users/Gray/.codex/generated_images/01a0d1b5-cca4-7ae0-8d1d-e6a427834e12/`.

- Wood: `exec-b4d64d6d-c3d7-400b-a90b-3d345e7d534d.png`.
- Stone: `exec-94acf98a-2d3a-4e45-b154-0e983fc7f73a.png`.
- Rejected double-ended axe draft, never imported: `exec-3cb0f38e-9a7f-4bc6-845c-d5662374ee70.png`.
- Accepted corrected axe: `exec-9ed50217-32c5-4197-ac56-96a01909e80c.png`.

## Exact wood prompt

```text
Use case: product-mockup
Asset type: one standalone raster inventory item icon for The Dead Wake, square 1024x1024 target, transparent-background PNG with real alpha.
Input images: Image 1 is the accepted inventory visual concept. Match its realistic natural material icons, soft neutral light, clear simple silhouettes and suitability for small dark charcoal inventory slots. DO NOT include the UI, scene, text, slot, frame or any background from the reference.
Style: grounded realistic game asset illustration / physically plausible 3D render, detailed natural materials, moderate contrast, softly lit from upper left, readable at tiny inventory size. No cartoon, pixel art, thick outline, glossy plastic, bright saturation, sci-fi, stylized fantasy embellishment.
Composition: ONE centered item or specified compact item bundle, complete silhouette within the square, about 15 percent transparent padding around outer extent. Slight natural three-quarter view, isolated without ground or backdrop. No cast drop shadow outside the object, no pedestal, no contact-shadow plane.
Transparency: genuinely transparent background everywhere outside the object, alpha 0. Do not draw a checkerboard, white background, black background, gradient, scenic background or square tile. Preserve clean antialiased object edges.
Avoid: labels, text, numbers, stack counts, logos, watermark, border, frame, UI, selection outlines, glow, hands, people, packaging, extra objects.
Subject: RAW WOOD inventory resource. A small compact bundle of THREE short natural raw wood lengths, roughly parallel, diagonally oriented from lower left to upper right, touching/overlapping as one readable icon. Warm brown weathered grain and rough bark on sides, simple pale cut end grain visible at nearer lower-left ends. No cord or binding, no metal, no leaves/roots, no plank construction. These are the resource icon, not a new world log pickup. Match the small wood icon in Image 1.
```

## Exact stone prompt

```text
Use case: product-mockup
Asset type: one standalone raster inventory item icon for The Dead Wake, square 1024x1024 target, transparent-background PNG with real alpha.
Input images: Image 1 is the accepted inventory visual concept. Match its realistic natural material icons, soft neutral light, clear simple silhouettes and suitability for small dark charcoal inventory slots. DO NOT include the UI, scene, text, slot, frame or any background from the reference.
Style: grounded realistic game asset illustration / physically plausible 3D render, detailed natural materials, moderate contrast, softly lit from upper left, readable at tiny inventory size. No cartoon, pixel art, thick outline, glossy plastic, bright saturation, sci-fi, stylized fantasy embellishment.
Composition: ONE centered item or specified compact item bundle, complete silhouette within the square, about 15 percent transparent padding around outer extent. Slight natural three-quarter view, isolated without ground or backdrop. No cast drop shadow outside the object, no pedestal, no contact-shadow plane.
Transparency: genuinely transparent background everywhere outside the object, alpha 0. Do not draw a checkerboard, white background, black background, gradient, scenic background or square tile. Preserve clean antialiased object edges.
Avoid: labels, text, numbers, stack counts, logos, watermark, border, frame, UI, selection outlines, glow, hands, people, packaging, extra objects.
Subject: STONE inventory resource. ONE compact irregular roughly cuboid natural gray stone chunk with subtly chipped facets and fine mineral grain; neutral medium gray/offwhite, convincing shallow material detail, softly lit top plane and readable darker side planes. Angled three-quarter view matching the small stone icon in Image 1. No separate pebbles, stone pile, gem, crystal, molten material, moss or decoration.
```

## Exact initial axe prompt

```text
Use case: product-mockup
Asset type: one standalone raster inventory item icon for The Dead Wake, square 1024x1024 target, transparent-background PNG with real alpha.
Input images: Image 1 is the accepted inventory visual concept. Match its realistic natural material icons, soft neutral light, clear simple silhouettes and suitability for small dark charcoal inventory slots. DO NOT include the UI, scene, text, slot, frame or any background from the reference.
Style: grounded realistic game asset illustration / physically plausible 3D render, detailed natural materials, moderate contrast, softly lit from upper left, readable at tiny inventory size. No cartoon, pixel art, thick outline, glossy plastic, bright saturation, sci-fi, stylized fantasy embellishment.
Composition: ONE centered item or specified compact item bundle, complete silhouette within the square, about 15 percent transparent padding around outer extent. Slight natural three-quarter view, isolated without ground or backdrop. No cast drop shadow outside the object, no pedestal, no contact-shadow plane.
Transparency: genuinely transparent background everywhere outside the object, alpha 0. Do not draw a checkerboard, white background, black background, gradient, scenic background or square tile. Preserve clean antialiased object edges.
Avoid: labels, text, numbers, stack counts, logos, watermark, border, frame, UI, selection outlines, glow, hands, people, packaging, extra objects.
Input Image 2: actual current stone-hatchet screenshot. Use its compact SINGLE stone wedge head, simple wood handle and three brown cord wraps as item identity; no UI or grass from Image 2.
Subject: STONE AXE tool icon. ONE compact primitive stone hatchet, diagonally across the square with butt at lower left and head at upper right. Straight naturally worn brown wooden handle; one SINGLE asymmetrical gray flint wedge head fitted across the top with the sharpened cutting end extending to one side. Three tight brown cord loops secure the junction. Head is ONE solid blade, not two separate stones, not a double-bit battle axe. Natural rough flint and timber, clear silhouette, same identity as the actual current tool. No metal, screws, pegs, extra wrapping down the handle, leather grip, decorative carvings or fantasy shape.
```

## Exact axe correction prompt

```text
Use case: precise-object-edit
Asset type: transparent square inventory icon.
Image 1 is an axe icon draft with an INCORRECT DOUBLE-BLADED HEAD. Image 2 shows the correct current single-sided compact stone-hatchet identity.
EDIT IMAGE 1: preserve the natural wooden handle, diagonal composition, realistic gray stone and brown cord materials, clean alpha transparency, complete item, and square canvas. Correct ONLY the head geometry and binding to a SINGLE-SIDED STONE HATCHET.
REMOVE the ENTIRE large stone blade that projects to the LOWER RIGHT of the wooden shaft. That lower-right blade must disappear completely and reveal transparent background. Keep just one broad flint wedge projecting to the UPPER LEFT side of the handle at its top, as in Image 2. The opposite side must have only a short blunt stone poll nearly flush with the handle; it must NOT be a second blade, wedge, wing or protruding stone chunk. The cutting edge belongs solely on the left side. One solid shallow irregular trapezoid gray stone head, secured close to top of handle with THREE snug brown cord wraps. This is a little primitive HATCHET, not a battle axe or double-bit axe.
No extra head, no crossed stones, no symmetry across the shaft. Preserve grounded real material detail. Keep at least roughly 10 percent transparent margin around the complete item. NO text, labels, numbers, border, backdrop, shadow plane, hands or scenery. Background must remain actual alpha-zero transparency, not white/black/checkerboard pixels.
```
