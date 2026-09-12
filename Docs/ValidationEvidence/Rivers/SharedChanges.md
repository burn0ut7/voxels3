# Shared uncommitted source

The validated working tree contains changes from concurrent terrain and material work. Git HEAD still has generator13. A commit of the current files would include more than river changes.

| File | River changes | Other changes present |
| --- | --- | --- |
| Code/Voxels/Generation/RegionalLandforms.cs | RiverWorld composition and river bounds | TerrainErosion, MountainMasses and upland sampling |
| Code/Voxels/ProceduralTerrainSdf.cs | River region/column sampling and river bounds | TerrainCliffs composition; combined generator version37 |
| Code/Voxels/Materials/ProceduralVoxelMaterials.cs | River height and water height in material queries | Snow, mountain stone and grass slope rules |
| Code/Voxels/Materials/GpuVoxelMaterials.cs | River atlas ownership/binding | Snow, stone and grass slope attributes |
| Assets/shaders/voxels/voxel_regional_landforms.hlsl | River include and composition | Erosion and mountain-mass includes/composition |
| Assets/shaders/voxels/voxel_materials.hlsl | River surface/water samples | Snow, stone and grass slope selection |
| Assets/shaders/voxels/voxel_sdf_v30.hlsl | Uses composed landform | New cliff composition; replaces old v13 include |
| Assets/shaders/voxels/voxel_persistent_geometry_cs.shader and voxel_transition_geometry_cs.shader | River resource integration | Depend on the replacement SDF include |

Additional unrelated dirty files include the authored scene, material palette, editor camera helper, architecture notes, validation history, and generated shader output. Generated output must not be hand-edited or staged as river source.

The exact full source used for the latest repeat is recorded in atlas-repeat-source-before.json. It matches all87files in atlas-reuse-source-before.json. A river-only staged reconstruction on the old terrain would be a different, unvalidated source tree; the current runtime result cannot prove that reconstruction's behavior.
