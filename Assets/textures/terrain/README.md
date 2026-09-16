# Terrain texture sources

The optimized terrain material uses these original source maps:

- [Poly Haven Dirt](https://polyhaven.com/a/dirt): Charlotte Baglioni; 2 m tile.
- [Poly Haven Rock Face](https://polyhaven.com/a/rock_face): Greg Zaal (photography),
  Dario Barresi (processing); 2.38 m tile.
- [ambientCG Ground101](https://ambientcg.com/a/Ground101): fine sand; authored 1 m tile.
- [ambientCG Snow007A](https://ambientcg.com/a/Snow007A): clean snow; authored 1 m tile.

The source records identify these assets as CC0. Poly Haven provenance, original
URLs, byte sizes and hashes are preserved in [source-manifest.json](source-manifest.json).
That historical manifest also records superseded Sand01/Snow02 selections;
those unused sets are not included in this integration. Grass provenance remains
in [the grass source notes](../grass/README.md).

The 2048-square PNG maps were downloaded on 2026-09-15 and remain unmodified.
Each material references color, OpenGL normal, roughness, ambient occlusion and
height inputs. Parallax is disabled and height is unused by the optimized shader.
The material compiler packs color RGB (sRGB) with roughness alpha (linear), and
normal RGB with occlusion alpha (linear), into two BC7 textures per material.
Full nearby texture detail and 16x anisotropic filtering are retained. Full detail
through 32 m fades to the source mip averages by 64 m. Rebuild the shader and
fully compile the material when changing its packing layout.

Ground101 was downloaded from
https://ambientcg.com/get?file=Ground101_2K-PNG.zip (63,719,036 bytes).
The recorded API labels its source Height field photogrammetry and supplies no
physical dimensions. Its 1 m tile is authored, not a provider measurement.

Snow007A was downloaded from
https://ambientcg.com/get?file=Snow007A_2K-PNG.zip (61,557,689 bytes).
The recorded metadata has conflicting method identifiers (PBRProcedural versus
Height field photogrammetry), so scanned origin is not asserted. Its physical
dimensions are unspecified and its 1 m tile is authored.
