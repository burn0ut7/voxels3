# Distant shadow ring investigation

2026-09-15, engine26.09.08b, source5a77686dd0fc57455ef3eb8b94a8a6a174ad6c59.
See RING-SHADOW-001/v1 and RING-SHADOW-002/v1 in
[the validation ledger](../../ValidationResults.md) for settings, sequence,
failed controls, measurements, and limitations.

## Effective engine comparisons

- [Baseline](baseline.png): original distant black contour.
- [Contact shadows disabled](engine-contact-off.png): contour persists.
- [Cascaded sun shadows disabled](engine-csm-off.png): contour and character cast shadow disappear.
- [Shadow distance halved](csm-distance-10000.png): contour moves closer.
- [One cascade](single-cascade.png): contour persists near the original distance.
- [Original settings restored](restored.png): original contour and cast shadow return.
- [Restoration readback](restored-settings.txt).

This identifies the outer sun-shadow coverage boundary as the leading explanation
for the moving ring. It does not establish a shader-level defect or qualify a fix.

## Inconclusive component controls

These did not visibly change lighting, including disabling the light object,
so they cannot rule out shadows. All changed values were restored.

- [Component-ID contact off](contact-off.png)
- [Component-ID shadows off](shadows-off.png)
- [Game-object/type contact off](live-contact-off.png)
- [Game-object/type shadows off](live-shadows-off.png)
- [Both shadow flags off](both-off.png)
- [Light object off](light-object-off.png)

## Source evidence

Project source: `Code/Voxels/GpuVoxelMesher.cs:4504-4573` owns terrain depth and
shadow submissions; `Assets/shaders/voxels/voxel_terrain.shader:89` uses standard
engine shading. Terrain geometry was unchanged throughout the effective controls.

Installed engine source under `core/shaders/`:
`Shadows/DirectionalLightShadow.hlsl:41-58` selects cascade coverage using spheres;
`:121-134` returns outside coverage before calling cascade sampling.
`Shadows/ShadowFiltering.hlsl:84-95` derives receiver normal offset from pixel
position derivatives. Inspecting behavior at the coverage edge is a follow-up
candidate; these source observations do not prove that derivatives cause the ring.
