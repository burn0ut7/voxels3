# Rocks Ground02 derived relief normal

Unaccepted BF prototype. Original source/license in ../rocks_ground_02/README.md. GLRGB8 output combines periodic central differences of height box mip2 (4-texel boxes,512-square grid),2 m tile,120 mm interval, with original fine normal-slope residual after mip2 removal. Residual vector length capped at1;4.85296% of texels affected. Periodic bilinear upsampling. Shader flips GLgreen. Manifest records source/output hashes.

This approximates engine height filtering and has not qualified visual/performance quality. Original source maps remain unchanged.

BS tests `rocks_ground_02_normal_08cm.png` at an 80 mm authored interval. The
periodic box-mip2 height derivative and fine residual recipe match the original
120 mm bake; only macro amplitude changes. Original source images are unchanged.
See [08cm-manifest.json](08cm-manifest.json) for exact hashes and parameters.
The experimental recipe is `.codex/terrain-relief/bake_rocks_ground02_normals_08cm.py`.
This is an unaccepted visual scale experiment, not a calibrated scan measurement.

BT tests `rocks_ground_02_normal_06cm.png` at a 60 mm interval with the same
recipe and unchanged source images. See [06cm-manifest.json](06cm-manifest.json).
The experimental recipe is `.codex/terrain-relief/bake_rocks_ground02_normals_06cm.py`.
Like BS, this is an unaccepted scale experiment, not physical scan calibration.
