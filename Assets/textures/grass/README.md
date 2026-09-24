# Grass texture source

Grass004 is the retained source for the baked terrain grass pattern.
`Tools/bake_terrain_patterns.py` reads its color, OpenGL normal, roughness,
ambient occlusion and displacement maps. The terrain material consumes the
derived maps in `textures/terrain/grass_pattern/`; it does not bind Grass004
directly. Keep these originals so the current pattern can be rebuilt.

- Source: https://ambientcg.com/a/Grass004
- Creator: ambientCG / Lennart Demes
- License: CC0 1.0; https://docs.ambientcg.com/license/
- Download: https://ambientcg.com/get?file=Grass004_2K-PNG.zip
- Resolution: 2048 x 2048, lossless PNG.
- Physical coverage: approximately 1.4 x 1.4 metres per source tile.
- Provenance caveat: the asset page labels the technique procedural, while the
  recorded API reports height field photogrammetry. Scanned origin is unverified.

Color is sRGB; normal, roughness, AO and displacement are linear data. Height
affects surface shading, not the canonical terrain SDF or collision. Standing
grass geometry belongs to [Meadow grass](../../../Docs/Architecture/StaticGrass.md).

The unused DirectX normal and unselected Poly Haven Leafy Grass alternative
were removed in the September 23 cleanup. Their original acquisition record is
available in Git history; neither is an input to the current baker. Runtime
qualification remains in [the validation ledger](../../../Docs/ValidationResults.md).
