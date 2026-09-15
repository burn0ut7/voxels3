# Grass texture sources

These are source texture candidates, not yet bound to the voxel terrain shader.
The playable terrain still uses its existing palette/checker shading.

## Dense grass: ambientCG Grass 004

- Source: https://ambientcg.com/a/Grass004
- Creator: ambientCG / Lennart Demes
- License: CC0 1.0; https://docs.ambientcg.com/license/
- Download: https://ambientcg.com/get?file=Grass004_2K-PNG.zip
- Resolution: 2048 x 2048, lossless PNG.
- Physical coverage: approximately 1.4 x 1.4 metres per tile.
- Maps: Color, NormalGL, NormalDX, Roughness, AmbientOcclusion, Displacement.
- Intended use: dense green ground cover.
- Provenance caveat: the asset page labels the technique procedural, while the
  v2 API reports height field photogrammetry. Do not describe this as a verified
  scan. The original downloaded pixels are preserved.

## Leafy ground: Poly Haven Leafy Grass

- Source: https://polyhaven.com/a/leafy_grass
- Creator: Charlotte Baglioni
- License: CC0; https://polyhaven.com/license
- File manifest: https://api.polyhaven.com/files/leafy_grass
- Resolution: 4096 x 4096, original provider JPGs.
- Physical coverage: 2 x 2 metres per tile.
- Maps: diffuse, OpenGL normal, roughness, ambient occlusion, displacement.
- Intended use: natural mixed grass with fallen leaves and exposed earth.
- All five files matched the provider manifest's MD5 checksums on 2026-09-15.

## Integration notes

Color/diffuse uses sRGB; normal, roughness, AO and height are linear data.
Use one normal convention after verifying the consuming shader; do not bind
both normal versions. Height is a source map, not permission to change the
canonical terrain SDF or collision. No height amplitude is assumed here.

These sets support tiled ground shading. Texture maps alone do not provide
standing grass silhouettes. Near-camera blades require a separate foliage slice.

## Inspection

Downloaded 2026-09-15. Both color maps were visually inspected. Their intended
appearances differ: dense green blades versus leafy, patchy ground. Neither
material has been accepted in the playable world; tiling seams, normal direction,
physical scale, grazing-angle appearance and performance remain unverified.
No runtime source, scene or shader references were changed in this asset slice,
so no figure-eight run was performed. Files are supplied for material selection
and subsequent integration. No AI-generated images were used.
