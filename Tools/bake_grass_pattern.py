"""Build the periodic grass pattern used by the terrain material.

Run from any directory with NumPy 2.3.5 and Pillow 12.3.0. These are offline
build dependencies only. The source Grass004 maps remain the art inputs.
"""
from pathlib import Path
import hashlib
import json

import numpy as np
from PIL import Image, __version__ as pillow_version


# This owns the lattice period; the shader consumes the generated constant.
PERIOD = 2
SOURCE_TEXELS_PER_TILE = 2048
SIZE = PERIOD * SOURCE_TEXELS_PER_TILE

root = Path(__file__).resolve().parents[1]
source_directory = root / "Assets/textures/grass/grass004"
output_directory = root / "Assets/textures/terrain/grass_pattern"
output_directory.mkdir(parents=True, exist_ok=True)
paths = {
    name: source_directory / f"Grass004_2K-PNG_{suffix}.png"
    for name, suffix in [
        ("color", "Color"), ("normal", "NormalGL"),
        ("roughness", "Roughness"), ("ao", "AmbientOcclusion")
    ]
}
color = np.asarray(Image.open(paths["color"]).convert("RGB"), dtype=np.float32) / 255
color = np.where(color <= .04045, color / 12.92, ((color + .055) / 1.055) ** 2.4)
normal = np.asarray(Image.open(paths["normal"]).convert("RGB"), dtype=np.float32) / 255 * 2 - 1
normal /= np.maximum(np.linalg.norm(normal, axis=2, keepdims=True), 1e-8)
roughness = np.asarray(Image.open(paths["roughness"]).convert("L"), dtype=np.float32) / 255
occlusion = np.asarray(Image.open(paths["ao"]).convert("L"), dtype=np.float32) / 255
assert color.shape[:2] == normal.shape[:2] == roughness.shape == occlusion.shape
assert color.shape[:2] == (SOURCE_TEXELS_PER_TILE, SOURCE_TEXELS_PER_TILE)
source = np.concatenate([color, normal, roughness[..., None], occlusion[..., None]], axis=2)
height, width = source.shape[:2]
del color, normal, roughness, occlusion
outputs = {
    "color": np.empty((SIZE, SIZE, 3), np.uint8),
    "normal": np.empty((SIZE, SIZE, 3), np.uint8),
    "roughness": np.empty((SIZE, SIZE), np.uint8),
    "ao": np.empty((SIZE, SIZE), np.uint8),
}


def sample(u, v):
    """Wrap and bilinearly sample the source at texel centers, in linear space."""
    x = (u % 1) * width - .5
    y = (v % 1) * height - .5
    ix = np.floor(x).astype(np.int32)
    iy = np.floor(y).astype(np.int32)
    fx = (x - ix).astype(np.float32)[..., None]
    fy = (y - iy).astype(np.float32)[..., None]
    a = source[iy % height, ix % width] * (1 - fx) + source[iy % height, (ix + 1) % width] * fx
    b = source[(iy + 1) % height, ix % width] * (1 - fx) + source[(iy + 1) % height, (ix + 1) % width] * fx
    return a * (1 - fy) + b * fy


def encode(value):
    return np.rint(np.clip(value, 0, 1) * 255).astype(np.uint8)


for first in range(0, SIZE, 64):
    lattice_x = np.broadcast_to((np.arange(SIZE, dtype=np.float32) + .5) * PERIOD / SIZE,
                               (min(64, SIZE - first), SIZE))
    lattice_y = np.broadcast_to(((np.arange(first, min(first + 64, SIZE), dtype=np.float32) + .5)
                                * PERIOD / SIZE)[:, None], lattice_x.shape)
    cell_x, cell_y = np.floor(lattice_x), np.floor(lattice_y)
    fraction_x, fraction_y = lattice_x - cell_x, lattice_y - cell_y
    lower = fraction_x + fraction_y <= 1
    blend = np.stack([
        np.where(lower, 1 - fraction_x - fraction_y, fraction_x + fraction_y - 1),
        np.where(lower, fraction_x, 1 - fraction_x),
        np.where(lower, fraction_y, 1 - fraction_y)
    ], axis=2)
    blend *= blend
    blend *= blend
    blend = np.maximum(blend - .001, 0)
    blend /= blend.sum(axis=2, keepdims=True)
    u = lattice_x + .5 * lattice_y
    v = lattice_y * np.float32(.8660254037844386)
    total = np.zeros((*lattice_x.shape, 8), np.float32)
    for patch in range(3):
        if patch == 0:
            vertex_x, vertex_y = cell_x + ~lower, cell_y + ~lower
        elif patch == 1:
            vertex_x, vertex_y = cell_x + lower, cell_y + ~lower
        else:
            vertex_x, vertex_y = cell_x + ~lower, cell_y + lower
        # Hash wrapped lattice vertices, then sample relative to the vertex.
        # Both edges evaluate the same periodic function, without a UV seam.
        key_x = (vertex_x.astype(np.int32) % PERIOD).astype(np.uint32)
        key_y = (vertex_y.astype(np.int32) % PERIOD).astype(np.uint32)
        hashed = (key_x * np.uint32(0x8DA6B343)) ^ (key_y * np.uint32(0xD8163841))
        hashed ^= hashed >> 16
        hashed *= np.uint32(0x7FEB352D)
        hashed ^= hashed >> 15
        offset_x = (hashed & 65535).astype(np.float32) / 65536
        offset_y = (hashed >> 16).astype(np.float32) / 65536
        total += sample(u - vertex_x - .5 * vertex_y + offset_x,
                        v - vertex_y * np.float32(.8660254037844386) + offset_y) * blend[..., patch, None]
    rgb = total[..., :3]
    rgb = np.where(rgb <= .0031308, rgb * 12.92, 1.055 * np.maximum(rgb, 0) ** (1 / 2.4) - .055)
    outputs["color"][first:first + len(lattice_x)] = encode(rgb)
    # Preserve the blended vector length. Triplanar reconstruction normalizes
    # the final surface normal, just as it does for the stochastic materials.
    outputs["normal"][first:first + len(lattice_x)] = encode(total[..., 3:6] * .5 + .5)
    outputs["roughness"][first:first + len(lattice_x)] = encode(total[..., 6])
    outputs["ao"][first:first + len(lattice_x)] = encode(total[..., 7])

for name, pixels in outputs.items():
    target = output_directory / f"grass_{name}.png"
    temporary = target.with_suffix(".tmp")
    Image.fromarray(pixels).save(temporary, format="PNG", compress_level=6)
    temporary.replace(target)

include = root / "Assets/shaders/voxels/voxel_grass_pattern.hlsl"
include.write_bytes((
    "// Generated by Tools/bake_grass_pattern.py; do not hand-edit.\r\n"
    f"static const float TerrainGrassPatternPeriod = {PERIOD}.0;\r\n"
).encode())
manifest = {
    "size": SIZE, "period": PERIOD, "blendExponent": 4, "blendCutoff": .001,
    "numpy": np.__version__, "pillow": pillow_version,
    "generatorSha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
    "source": {name: {"path": path.relative_to(root).as_posix(),
                      "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}
               for name, path in paths.items()},
    "output": {path.name: hashlib.sha256(path.read_bytes()).hexdigest()
               for path in sorted(output_directory.glob("*.png"))},
    "shaderIncludeSha256": hashlib.sha256(include.read_bytes()).hexdigest(),
}
(output_directory / "manifest.json").write_bytes((json.dumps(manifest, indent=2) + "\n").replace("\n", "\r\n").encode())
print(f"Baked {SIZE} x {SIZE} grass pattern, lattice period {PERIOD}.")
