# Terrain texture sources

`materials/voxels/voxel_terrain.vmat` binds baked grass, stone, sand and snow
patterns, the corrected `dry_mud_field_001` dirt maps, and gravel. Clay has its
own material. The original source maps remain authoring inputs or provenance;
being outside a material's direct dependencies does not make a bake input unused.

- `Tools/bake_terrain_patterns.py` consumes Grass004, Ground101 and Snow007A.
- `Tools/bake_stone_relief.py` consumes Cliff Side for the stone pattern.
- `Tools/bake_terrain_relief.py` retains the earlier relief bake workflow.
- `Tools/remove_dirt_tracks.py` records the aligned dirt-map correction.

Source records for the earlier selections identify them as CC0:

- [Poly Haven Dirt](https://polyhaven.com/a/dirt): Charlotte Baglioni; 2 m tile.
- [Poly Haven Rock Face](https://polyhaven.com/a/rock_face): Greg Zaal (photography),
  Dario Barresi (processing); 2.38 m tile.
- [ambientCG Ground101](https://ambientcg.com/a/Ground101): fine sand; authored 1 m tile.
- [ambientCG Snow007A](https://ambientcg.com/a/Snow007A): clean snow; authored 1 m tile.

Original URLs, byte sizes and hashes are preserved in
[source-manifest.json](source-manifest.json) and the individual map/bake manifests.
The source manifest also records superseded Sand01/Snow02 selections. Those sets
and duplicate download archives were removed from the experiment workspace in
the September 23 cleanup. Grass provenance remains in
[the grass source notes](../grass/README.md).

Current relief and packing behavior is owned by the material and shader source,
with its contract in [Voxel materials](../../../Docs/Architecture/VoxelMaterials.md).
The old no-parallax integration description is superseded. Qualification and
historical comparisons remain in [the validation ledger](../../../Docs/ValidationResults.md).

Ground101 was downloaded from
https://ambientcg.com/get?file=Ground101_2K-PNG.zip (63,719,036 bytes).
The recorded API labels its source Height field photogrammetry and supplies no
physical dimensions. Its 1 m tile is authored, not a provider measurement.

Snow007A was downloaded from
https://ambientcg.com/get?file=Snow007A_2K-PNG.zip (61,557,689 bytes).
The recorded metadata has conflicting method identifiers (PBRProcedural versus
Height field photogrammetry), so scanned origin is not asserted. Its physical
dimensions are unspecified and its 1 m tile is authored.
