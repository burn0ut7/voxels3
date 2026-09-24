# Tree texture sources

Development assets, not visually accepted production art. The oak's near leaves
and bark were replaced after the September 19 research reset. Pine now also
uses matched external needle/bark sources. Ash now has individual leaflet art
and scanned bark. The current source replaces distant canopy drawings with
variant-specific tree bakes; the new assets and LOD transitions remain under review.

## Oak leaves

`oak_leaf_atlas.png` is a 1024x1024 import of an original transparent four-leaf
atlas made with the built-in image-generation tool on 2026-09-19. The unchanged
1254x1254 source and exact prompt are in
[Tools/TreeSources](../../../Tools/TreeSources/README.md). It is generated art,
not a botanical scan or a measured PBR material.

[bake_tree_textures.py](../../../Tools/bake_tree_textures.py) resamples the source
to a power-of-two size required by the engine's alpha-weighted mip compiler and
extracts `oak_leaf_opacity.png`. Native foliage reads alpha from the red channel
of `TextureTranslucency`, not the color image's alpha. Roughness is provisionally
166/255; transmission is uniform. No authored leaf normal/thickness texture is
present. Folded leaf geometry supplies broad surface normals.

## Oak bark

[Jolcham Oak Bark 01](https://polyhaven.com/a/jolcham_oak_bark_01), by Charlotte
Baglioni, is supplied by Poly Haven under [CC0](https://polyhaven.com/license).
The three files in `oak_bark/` are unchanged 2K JPEG downloads (2048x4096):
diffuse color, OpenGL tangent normal and roughness. Source URLs follow
`https://dl.polyhaven.org/file/ph-assets/Textures/jpg/2k/jolcham_oak_bark_01/`
plus the exact filename below. The listed scan area is 1m by 2m. This bark is a
visual candidate; it is not claimed to identify the generated oak's species.

| File | SHA256 |
| --- | --- |
| jolcham_oak_bark_01_diff_2k.jpg | 2EB8D1E79A64827AD70B20BC80BEC2171494A442310099AC88D15970A46E6C5F |
| jolcham_oak_bark_01_nor_gl_2k.jpg | F0C17CAEAFB7AAD681E7A376E754ED1593A6BF61ACC0BC71A45D9850F29E2AE3 |
| jolcham_oak_bark_01_rough_2k.jpg | 831EFB4BFEF79EE43270F99E0C0428B2723C34725341CA4357B102102B1D0EEC |

The baker references these checked-in inputs; it does not download or recreate
them. Native bark shading uses the matched normal and roughness maps. Texture
coordinates follow each curved limb's circumference and arc length. Close-view
relief and mapping remain under visual review.

## Conifer needles and bark

Earlier needle color/alpha/normal/roughness in `pine_tree_01/` are unchanged 2K maps from
[Pine Tree01](https://polyhaven.com/a/pine_tree_01), by Rob Tuytel (photography)
and Rico Cilliers (modeling), under CC0. Geometry samples the first needle-bearing
twig within the atlas in the rejected earlier candidate; cone/branch islands
were excluded. Repeated coarse fans remained visible in close review.

Current `pine_needle_tuft_atlas.png` and `pine_needle_tuft_opacity.png` are imports
of a four-tuft generated RGBA source in [TreeSources](../../../Tools/TreeSources/README.md#pine-needle-tufts).
The existing import script resamples to1024 and extracts alpha. Small folded
tufts surround real growth shoots; each fold follows its source root coordinate.
No scan normal map is
applied to unrelated generated art: normals are geometric, roughness uniform.
The current source is generated art and still needs close in-world acceptance.

`pine_bark/` contains unchanged matched 2K color, OpenGL normal and roughness from
[Pine Bark](https://polyhaven.com/a/pine_bark), by Dimitrios Savva, CC0. Its scan
area is approximately2m by2m. Swept wood texture coordinates use those dimensions.
Exact URLs, authors, byte counts and hashes are in
[conifer_sources.json](conifer_sources.json). Full multi-million-triangle source
tree meshes were not downloaded or imported; only small glTF manifests were
inspected for suitability. No runtime external download is required.

## Ash leaves and bark

`ash_leaf_atlas.png` is a 1024x1024 import of an original transparent four-leaflet
atlas made with the built-in image-generation tool on 2026-09-19. The original
1254x1254 RGBA image and exact prompt are recorded in
[Tools/TreeSources](../../../Tools/TreeSources/README.md). Import and alpha
extraction use the same path as oak. Roughness is provisionally 166/255 and
transmission is uniform; this is generated art, not a measured leaf material.

`ash_bark/` contains unchanged 2K diffuse, OpenGL normal and roughness JPEGs from
[Tree Bark 03](https://polyhaven.com/a/tree_bark_03), by Rob Tuytel, under CC0.
The listed scan area is 1m by 1m. The source does not identify a tree species;
this is a visual bark source, not a verified ash specimen. Exact source URLs,
dimensions, byte counts and hashes are in [ash_bark_sources.json](ash_bark_sources.json).

## Matched distant tree bakes

`Editor/TreeImpostorBaker.cs` captures the canonical near geometry in native
Albedo and NormalMap modes: eight azimuths and four elevations per shared variant.
The prepared baker freezes wind on private material copies and disables
distance-based alpha relaxation for capture. Its source alpha threshold remains
unchanged. Those controls still need native verification after the editor reopens;
existing far atlases predate them and the latest geometry.
`Tools/pack_tree_impostors.py` validates all source hashes, then packs256pixel
frames into2048x1024 color, coverage and object-normal atlases. Albedo is reduced
in linear space with alpha weighting; normals are averaged and normalized.
Sixteen pixels of edge dilation support texture filtering. The runtime shader
selects a view and applies scene lighting to the baked normals. These are derived
assets of the generated trees, not a second shape generator or unrelated tree art.

Run `python Tools/bake_tree_textures.py` to prepare the checked-in source textures
and six bark/spray material resources. The individual-needle geometry experiment
was rejected for dark, noisy coverage and near/mid mismatch. Pine now uses curved,
folded small needle tufts at matching LOD attachments. This prepared replacement
has not run in the playable world. Independent close review rejected the earlier
whole-shoot atlas's broad flat fans and pale stem ends; whole-tree coverage alone
does not establish realism. The checked-in far atlases currently predate the new form profiles
and must be rebaked before acceptance.
Shared metalness is zero. `ProceduralTreeGeometry` owns tree shape generation;
the baker does not implement a second tree generator. Compiled engine resources
are generated output and must not be edited.
