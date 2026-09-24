# Experimental RockFace relief normal

Unaccepted prototype data for a 2.38 m tile and a 16 cm authored full height interval.
Derived from the unchanged Poly Haven Rock Face height and OpenGL normal maps in
`../rock_face/`; Greg Zaal (photography), Dario Barresi (processing), CC0.
See the original [source manifest](../source-manifest.json) and [source notes](../README.md).

The macro normal comes from periodic central differences of a box-filtered mip3
height field, bilinearly enlarged to 2048 square. This approximates the shader's
filtered search shape; it does not assert identical engine mip reconstruction.
Fine slope detail is the original normal slope minus its mip3 low-frequency slope.
Residual length is capped at1; about0.81% of source texels require that cap.
The result is encoded as an OpenGL RGB8 normal; the shader flips its green channel.
AO, color, roughness and height sources are unchanged. Input/output SHA-256 hashes
and numerical parameters are in [manifest.json](manifest.json).

This is a numerical material bake, not evidence of visual quality. It must pass a
new matched flat-control comparison before replacing the accepted material.
The experimental generator is `.codex/terrain-relief/bake_height_normals.py`;
move a retained generator into the maintained asset pipeline only after qualification.

BC additionally uses `rock_face_fine_surface.png`: linear RGB8 with R centered
luminance ratio and G/B encoded fine GL slopes. It is numerical material data,
not a normal-map RGB convention. Its provenance and SHA-256 are recorded in
[fine-manifest.json](fine-manifest.json). The same experimental generator emits
both files and verifies an existing bake before reusing it. BC remains unaccepted.

BQ additionally tests `rock_face_side_surface_mip5.png` with the same packing and
source inputs, but subtracts box mip5 rather than mip3 before periodic bilinear
upsampling. It retains intermediate rock detail on exposed faces. The source tile
remains 2.38 m and the residual slope cap remains 1. This changes shading detail,
not the height traced by parallax. See [side-mip5-manifest.json](side-mip5-manifest.json)
for exact hashes. The experimental recipe is
`.codex/terrain-relief/bake_side_surface_mip5.py`. BQ is unaccepted.
