# Original foliage atlases

## Oak

Created 2026-09-19 with the built-in image-generation tool. The selected original
is [oak_leaf_atlas.png](oak_leaf_atlas.png), 1254x1254 RGBA, SHA256
`52F833D0939D1FF17244FE29D3F04A48E5EE9CCA052819E7CF3BCAB640800556`.
The workspace copy is the canonical input; no runtime path depends on Codex's
generated-image directory. The baker imports it at 1024x1024 and extracts alpha.
This is generated source art, not photography or measured material data.

### Exact generation prompt

Use case: photorealistic-natural. Asset type: physically plausible oak leaf
color/opacity texture atlas for a high-quality real-time game, not an illustration
of a tree. Create one square high-resolution image with a genuinely transparent
background and exactly four separate green English oak leaves in a precise
2-by-2 atlas grid, one leaf centered inside each quarter of the image. Each leaf
points straight UP with its narrow stalk at the bottom, takes approximately 82%
of its cell height and 62% of its cell width, and has generous transparent space
all around. All four leaves are fully visible, separate and non-overlapping,
with subtle natural differences in lobes and coloration. Mature fresh summer
oak leaves: rounded irregular lobes, natural midrib and branching veins, muted
olive-to-medium green, tiny restrained blemishes, fine photographic surface
texture. Orthographic top-down flattened specimen appearance, true shape,
neutral diffuse cross-polarized-style even lighting; no cast shadows, no
directional highlights, no dramatic crease shading, no glossy glare. This is an
ALBEDO source: small coloration veins allowed, broad baked shadows avoided.
Petioles should be very short and thin. No branches, no extra leaves, no frame,
no labels, no text, no grid lines, no background color, no checkerboard painted
into the image. Keep all four tiles uniform scale and orientation so the atlas
can be used directly on individually bent leaf meshes. Preserve transparent alpha.

## Ash

Created 2026-09-19 with the built-in image-generation tool. The unchanged original
is [ash_leaf_atlas.png](ash_leaf_atlas.png), 1254x1254 RGBA, SHA256
`8FF4811CB82369D70E193BDFC98C0CD875F7D124961D3DA25B240832BE13D290`.
The exact submitted prompt is [ash_leaf_prompt.txt](ash_leaf_prompt.txt).
This workspace copy is the canonical import source; the baker resamples to
1024x1024 and extracts alpha. These are generated individual leaflets for
geometric compound leaves, not a photograph or a measured PBR material.

## Pine shoots

Created2026-09-19 using the built-in image-generation tool. The original
[pine_shoot_atlas.png](pine_shoot_atlas.png) is1254x1254 RGBA, SHA256
`DB682C6C6FB355A1729F1E233563028A174925078E3F69815E461EDCEF4E31D8`.
The exact prompt is [pine_shoot_prompt.txt](pine_shoot_prompt.txt). The atlas
contains four distinct terminal shoots. The import preserves this source,
resamples a1024x1024 runtime copy and extracts its existing alpha channel.
This is the retained original from the rejected whole-shoot candidate C. Its
broad fans and pale stem ends failed close review; the importer no longer uses it.

## Pine needle tufts

Prepared2026-09-19 with the built-in image-generation tool. Original
[pine_needle_tuft_atlas.png](pine_needle_tuft_atlas.png) is1254x1254 RGBA, SHA256
`1260D0B6F7A1487F1B5D245212446C5765667C435B4C29930472E3B1FD10ED67`.
The exact prompt is [pine_needle_tuft_prompt.txt](pine_needle_tuft_prompt.txt).
The importer resamples to1024 and extracts the same alpha into the separate
opacity texture required by native foliage. No creative cleanup or repaint is
performed by the importer. PineNeedleTuft records the source basal coordinates;
the small folded meshes attach radially around real curved growth shoots.
Geometry supplies normals and roughness is uniform, not measured PBR data.

Source preview showed fringes and gray fill. Read-only alpha inspection found
that many conspicuous gray pixels have alpha2-3/255, below the material's0.4
cutoff. This does not prove clean filtered edges or acceptable mipmaps. The
independent reviewer permits a native trial of the smaller foliage approach;
actual tuft appearance, needle scale, attachment, coverage and performance are
unverified. A cleanup edit and a later needle-pair atlas were explored but are
not consumed. The original tuft source is the sole candidate runtime input.
