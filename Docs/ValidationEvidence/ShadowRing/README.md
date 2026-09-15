# Distant shadow ring investigation

The follow-up below isolates the receiver derivative ordering defect and records
a tested, restored engine patch. The initial investigation is preserved first.

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

## Follow-up: source-level cause isolated

RING-SHADOW-003/v1 and004/v1 preserved sun shadows. The original directional
shadow shader returned for pixels outside cascade coverage before computing
`ddx`/`ddy` for the receiver normal offset. Those operations depend on neighboring
pixels, including pixels on the other side of the boundary.

Moving the existing normal-offset calculation before that return eliminated the
ring. Restoring the original shader brought it back. Shadow distance, cascade
count, contact shadows and filtering stayed at their original settings. This is
strong evidence for the receiver derivative/control-flow defect in the engine
shader. No terrain geometry change was needed.

### Comparisons

- [Parked original](parked-baseline.png): contour crosses the hill.
- [Filtering off](parked-filter-off.png): contour remains.
- [Stronger raster bias](parked-strong-bias.png): contour remains.
- [Unchanged shader rebuilt](unchanged-recompiled.png): contour remains; identical compiled hash.
- [Sampling moved before the return](uniform-sampling.png): contour disappears.
- [Only receiver offset moved before the return](derivative-before-boundary.png): contour disappears, with original conditional texture sampling retained.
- [Refined candidate, second view](candidate-ground-check.png): ring absent; player still airborne.
- [Original restored, second view](original-restored-after-candidate.png): ring returns.
- [Unchanged shadow settings during candidate](candidate-settings.txt).

Initial moving views are also retained in further-baseline.png, filter-off.png
and strong-caster-bias.png; they are not comparable before/after pairs. Runtime
placement observations are in further-state.txt and parked-state.txt. See the
ledger for exact hashes, parameters and limits.

### Proposed correction, not yet adopted

[Exact tested engine patch](receiver-derivative-order.patch) targets
`game/core/shaders/Shadows/DirectionalLightShadow.hlsl` in the official engine
source tree (`core/shaders/Shadows/DirectionalLightShadow.hlsl` when installed).
The patch uses zero context (git apply --unidiff-zero); its application check
passed against the original installed source.
It moves the existing receiver offset computation before the per-pixel coverage
return. The shadow-map fetch still happens only inside coverage. It introduces
no replacement terrain renderer or duplicate shadow implementation.

The tested patch is retained as evidence. Installed engine source and compiled
terrain shader were restored byte-for-byte after the experiment. No runtime fix
is included in this repository commit. Other engine receivers were not recompiled.
The character stayed airborne during candidate captures, so close ground-shadow
fidelity is still pending, along with cold-start and figure-eight qualification.
A permanent integration should correct the canonical engine shader and qualify
all affected receivers; copying the engine shading system into a terrain-specific
fork is not adopted.

### Authoritative source references

- [Pinned directional shadow shader](https://github.com/Facepunch/sbox-public/blob/804420939f467a3fb13c534a5c63a415dd55bd58/game/core/shaders/Shadows/DirectionalLightShadow.hlsl#L121-L135): original coverage return and sampling order.
- [Pinned shadow receiver offset](https://github.com/Facepunch/sbox-public/blob/804420939f467a3fb13c534a5c63a415dd55bd58/game/core/shaders/Shadows/ShadowFiltering.hlsl#L82-L93): neighboring-pixel derivative calculation.
- [Earlier engine shadow divergence correction](https://github.com/Facepunch/sbox-public/commit/4e43c04ebbb91f9ac34e8fab93d3b650e82487e6): historical context for receiver-bias changes; its existence does not establish that the boundary case is fixed.

The installed files were inspected directly and the candidate was exercised in
the playable world. Upstream source alone is not proof of installed behavior.
