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

## Project integration qualification (RING-SHADOW-005/v1)

**Outcome: the full-offset version was rejected for a performance regression.**
The final repeat measured 488.15 FPS against the native-only original control's
523.15 FPS, exceeding the 5% loss budget. Its visual and geometry checks do not
override that failure. The source is preserved in
[rejected-full-offset.hlsl](Acceptance/rejected-full-offset.hlsl).
RING-SHADOW-006 is evaluating a narrower derivative-only correction; it is not yet
accepted. The details below describe the rejected full-offset version.

The implementation candidate is
`Assets/shaders/Shadows/DirectionalLightShadow.hlsl`, a project-mounted replacement
of the single engine include, pinned to the upstream revision above. The source
retains the upstream MIT notice. The installed engine file stays unchanged.

The initial project integration's production terrain shader compiled to SHA256
`D2A9DCC139A70227313F4F74F5C39B1B2D4CFCDA9F08C2173193E3FF5C885D03`:
exactly the refined, visually verified experiment's binary. This establishes that
the compiler resolves the project replacement. There is one selected implementation
of the include, with no second terrain renderer or shadow pass.

The final entry sources additionally contain a rebuild-reference comment. This
is intentional: the installed editor watches `.shader` changes but does not
recompile dependencies for an include-only edit. All three entry sources therefore
change with this integration and were explicitly rebuilt before its final cold
start. The final terrain artifact is
`D13062E1354807C39ED3D5F93956A0DD4C640D2DE9D029FC58AB02743DE6033E`.
The only difference from the initial project shader entry is the comment; the
receiver correction itself is unchanged. Full final source/artifact identities are
in [the hash manifest](Acceptance/final-source-and-artifact-hashes.json).

### Implementation review

- `GetVisibility` retains the uniform zero-cascade return, so no matrix or hardness
  lookup occurs when sun cascades are absent. Contact-shadow composition is unchanged.
- `FindCascade` selects coverage from the original receiver position. The normal
  offset is evaluated after selection and before the varying coverage return.
- A nonzero cascade count makes index zero valid. `max(cascade, 0)` gives pixels
  outside coverage a valid matrix/hardness entry while their neighbors evaluate
  derivatives; those pixels still return the original contact-shadow value.
- `SampleCascade` has one caller in the pinned engine source. Its offset was moved,
  not duplicated. Matrix projection, bias, PCF filtering and shadow texture lookup
  are unchanged. Outside-coverage pixels still skip the texture lookup.
- The include introduces no CPU state, geometry, allocations, threading, network
  authority or persistence changes. Its extra work outside coverage is arithmetic;
  performance must still be measured rather than assumed.
- Three project entry files include it through `common/shared.hlsl` and the engine
  light classes: `voxel_terrain.shader`, `voxel_terrain_depth.shader`, and
  `voxel_water.shader`, all under `Assets/shaders/voxels`. All three were explicitly
  rebuilt successfully. Terrain uses `ShadingModelStandard::Shade`; depth outputs
  depth/normals, and water outputs its checker color directly. The latter two still
  parse the shared lighting declarations. Precompiled engine materials are not
  retroactively rebuilt.

### Maintenance boundary

This narrow override pins the engine's directional-light constant-buffer layout
and API. Requalify it on an engine update, including a diff against upstream,
all three dependent shader compiles, a cold editor start, close sun/contact
shadows, the large-coordinate ring reproduction, and the canonical figure-eight.
Remove the override when the upstream correction passes those checks. Do not
expand it into a copied lighting stack. Engine source and generated shader binaries
are not part of the source change.

### Qualification evidence

Raw results, readiness, coverage, mesh audits, paired captures and setup failures
are retained in [Acceptance](Acceptance/). The validation ledger records exact
parameters and acceptance status. The original experiments above remain historical
diagnosis evidence; they do not establish performance acceptance of this integration.

## Derivative-only refinement (RING-SHADOW-006/v1, pending)

The revised candidate evaluates only the two position derivatives before coverage
selection and the varying return. It normalizes their cross product and performs
the matrix/hardness lookup and bias arithmetic only for pixels inside coverage.
The zero-cascade branch still returns before all of that work, and non-pixel
programs retain the original unmodified receiver position.

The offset formula is the pinned `ApplyShadowNormalOffset` formula. That engine
helper combines derivative evaluation with the offset calculation and has no
overload accepting derivatives, so the directional include applies the formula
locally using the earlier derivatives. This dependency must be checked together
with the upstream helper and PCF kernel radius on engine updates. Local-light
receivers continue to use the engine helper unchanged.

Compile, directly reproduced ring removal, normal shadow fidelity, cold-start and
canonical figure-eight acceptance are required before this refinement is adopted.
