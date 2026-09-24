# Fractured bedrock prototype

This material targets continuous underground rock: broad stone faces and sharp
fracture edges. Loose gravel and rubble are no longer the material intent.

The authoritative scan channels are the existing `../rock_face/` color, GL
normal,16-bit displacement, roughness and AO maps. `manifest.json` records all
input/output hashes and art settings. Regenerate with
`python Tools/bake_stone_relief.py` from the repository using NumPy and Pillow.
The baker also emits `voxel_stone_pattern.hlsl`; never tune its generated
physical settings independently of the bake.

All five maps share the original scan coordinates. Patch compositing was removed
because it fragmented the continuous bedrock into short overlapping ledges.
Broad scan height is reduced to45%;35% source-AO crevice depth sharpens recesses.
This is authored relief, not a physical scan-depth reconstruction. Macro normals
derive from the final16-bit height at the runtime minimum mip.
RockFace roughness and AO are16-bit and must not be clipped through an8-bit
Pillow conversion. The terrain shader retains15% of the source mineral color;
raw color PNG previews therefore appear warmer than its in-game base albedo.

Status: unaccepted prototype. Earlier rubble candidates and their failures are
recorded in Docs/ValidationResults.md. Performance testing is deferred by user
direction. Offline map inspection does not prove rendered parallax or quality.
