"""Build coherent periodic grass, sand and snow terrain materials.

Run from any directory with NumPy 2.3.5 and Pillow 12.3.0. These are offline
build dependencies only. The existing source scans remain the art inputs. Select --material grass|sand|snow.
"""
from pathlib import Path
from terrain_bake_output import publish_bake
import argparse
import hashlib
import json

import numpy as np
from PIL import Image, __version__ as pillow_version


# One owner for lattice placement and each material's physical bake metadata.
PATTERN_PERIOD = 2
SAND_PATTERN_PERIOD = 8
SOURCE_TEXELS_PER_TILE = 2048
SETTINGS = {
    "grass": ("Assets/textures/grass/grass004", "Grass004", 4096, 1.4, .020),
    "sand": ("Assets/textures/terrain/ground101", "Ground101", 4096, 1.0, .120),
    "snow": ("Assets/textures/terrain/snow007a", "Snow007A", 2048, 1.0, .060),
}
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--material", choices=SETTINGS, required=True)
material = parser.parse_args().material
PERIOD = SAND_PATTERN_PERIOD if material == "sand" else PATTERN_PERIOD
source_path, source_name, SIZE, TILE_METRES, AMPLITUDE_METRES = SETTINGS[material]
MINIMUM_HEIGHT_LOD = 2
COMBINED_HEIGHT_SIGMA_TEXELS = {"grass": 0, "sand": 0, "snow": 8}[material]
FINE_NORMAL_GAIN = .18 if material == "sand" else .5
root = Path(__file__).resolve().parents[1]
source_directory = root / source_path
output_directory = root / f"Assets/textures/terrain/{material}_pattern"
output_directory.mkdir(parents=True, exist_ok=True)
paths = {
    name: source_directory / f"{source_name}_2K-PNG_{suffix}.png"
    for name, suffix in [
        ("color", "Color"), ("normal", "NormalGL"),
        ("roughness", "Roughness"), ("ao", "AmbientOcclusion"),
        ("height", "Displacement")
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
source_height = np.asarray(Image.open(paths["height"]), dtype=np.float32) / 65535
source = np.concatenate([color, normal, roughness[..., None], occlusion[..., None], source_height[..., None]], axis=2)
height, width = source.shape[:2]
del color, normal, roughness, occlusion
outputs = {
    "color": np.empty((SIZE, SIZE, 3), np.uint8),
    "normal": np.empty((SIZE, SIZE, 3), np.uint8),
    "roughness": np.empty((SIZE, SIZE), np.uint8),
    "ao": np.empty((SIZE, SIZE), np.uint8),
    "height": np.empty((SIZE, SIZE), np.uint16),
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
    if material == "grass":
        # Sharpen overlap without erasing support at triangular cell centers.
        blend *= blend
    blend /= blend.sum(axis=2, keepdims=True)
    u = lattice_x + .5 * lattice_y
    v = lattice_y * np.float32(.8660254037844386)
    total = np.zeros((*lattice_x.shape, 9), np.float32)
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
        local_u = u - vertex_x - .5 * vertex_y
        local_v = v - vertex_y * np.float32(.8660254037844386)
        if material == "snow":
            # Rotate coherent snow patches to break repeated scalloped bands.
            angle = hashed.astype(np.float64) * (2 * np.pi / 4294967296.)
            c, sn = np.cos(angle).astype(np.float32), np.sin(angle).astype(np.float32)
            sampled = sample(c * local_u - sn * local_v + offset_x,
                             sn * local_u + c * local_v + offset_y)
            nx, ny = sampled[..., 3].copy(), sampled[..., 4].copy()
            sampled[..., 3], sampled[..., 4] = c * nx - sn * ny, sn * nx + c * ny
        else:
            sampled = sample(local_u + offset_x, local_v + offset_y)
        total += sampled * blend[..., patch, None]
    rgb = total[..., :3]
    rgb = np.where(rgb <= .0031308, rgb * 12.92, 1.055 * np.maximum(rgb, 0) ** (1 / 2.4) - .055)
    outputs["color"][first:first + len(lattice_x)] = encode(rgb)
    # Preserve the blended vector length. Triplanar reconstruction normalizes
    # the final surface normal, just as it does for the stochastic materials.
    outputs["normal"][first:first + len(lattice_x)] = encode(total[..., 3:6] * .5 + .5)
    outputs["roughness"][first:first + len(lattice_x)] = encode(total[..., 6])
    outputs["ao"][first:first + len(lattice_x)] = encode(total[..., 7])
    baked_height = total[..., 8]
    if material == "sand":
        # A continuous periodic ripple field replaces the scan's isolated pits.
        # Integer frequencies preserve wrap continuity over the entire 8-cell bake.
        # Vary crest direction and strength slowly, retaining quiet stretches.
        tx, ty = lattice_x / PERIOD, lattice_y / PERIOD
        tau = 2 * np.pi
        phase = tau * .75 * (24 * ty + 1.1 * np.sin(tau * tx)
                       + .35 * np.sin(tau * 3 * tx)
                       + .15 * np.sin(tau * 5 * tx)
                       + .45 * np.sin(tau * (2 * tx + ty))
                       + .12 * np.sin(tau * (5 * tx - 2 * ty))
                       + .12 * np.sin(tau * (3 * tx + 7 * ty))
                       + .04 * np.sin(tau * (5 * tx + 13 * ty))
                       + .06 * np.sin(tau * (7 * tx + 19 * ty))
                       + .05 * np.sin(tau * (9 * tx - 11 * ty)))
        envelope = np.clip(.5 + .24 * np.sin(tau * (tx - ty))
                           + .16 * np.sin(tau * (3 * tx + 2 * ty))
                           + .10 * np.cos(tau * (5 * tx - 3 * ty)), 0, 1)
        envelope = envelope * envelope * (3 - 2 * envelope)
        # Keep crest height slowly varying. Modulating it by local phase
        # derivatives introduced pinched knuckles at narrow ridge segments.
        ridge_height = .95 * (.02 + .19 * envelope)
        # Local calm patches taper ridge segments away instead of carrying
        # every crest across the whole field. Smooth transitions avoid cut ends.
        breaks = (.5 + .28 * np.sin(tau * (4 * tx + ty))
                  + .20 * np.sin(tau * (3 * tx - 5 * ty))
                  + .12 * np.sin(tau * (7 * tx + 3 * ty)))
        breaks = np.clip((breaks - .15) / .35, 0, 1)
        ridge_height *= breaks * breaks * (3 - 2 * breaks)
        ripple = .90 * np.cos(phase) + .10 * np.cos(2 * phase)
        baked_height = .5 + ridge_height * ripple
    outputs["height"][first:first + len(lattice_x)] = np.rint(np.clip(baked_height, 0, 1) * 65535).astype(np.uint16)

if material == "sand":
    # Restore fine scanned grain without strengthening the broad mottled color.
    grain_color = outputs["color"].astype(np.float32) / 255
    grain_color = np.where(grain_color <= .04045, grain_color / 12.92,
                           ((grain_color + .055) / 1.055) ** 2.4)
    grain_frequency = np.fft.fftfreq(SIZE)
    grain_kernel = np.exp(-2 * np.pi**2 * 2.5**2 * (
        grain_frequency[:, None]**2 + grain_frequency[None, :]**2))
    for channel in range(3):
        blurred = np.fft.ifft2(np.fft.fft2(grain_color[..., channel]) * grain_kernel).real
        grain_color[..., channel] += .5 * (grain_color[..., channel] - blurred)
    grain_color = np.clip(grain_color, 0, 1)
    outputs["color"] = encode(np.where(grain_color <= .0031308, grain_color * 12.92,
                                       1.055 * grain_color ** (1 / 2.4) - .055))

# Keep flowing sand and snow accumulation, softening narrow relief creases.
# Filter combined height before deriving normals so both describe the same form.
if COMBINED_HEIGHT_SIGMA_TEXELS > 0:
    frequency = np.fft.fftfreq(SIZE)
    kernel = np.exp(-2 * np.pi**2 * COMBINED_HEIGHT_SIGMA_TEXELS**2 * (frequency[:, None]**2 + frequency[None, :]**2))
    filtered_height = outputs["height"].astype(np.float32) / 65535
    filtered_height = np.fft.ifft2(np.fft.fft2(filtered_height) * kernel).real
    outputs["height"] = np.rint(np.clip(filtered_height, 0, 1) * 65535).astype(np.uint16)

# Derive the macro surface from the exact quantized height and runtime mip.
# Texture T=(U-.577350269*V,1.154700538*V)/(tile*period).
# Chain-rule gradients below return slopes in the original physical U,V chart.
mip_scale = 2**MINIMUM_HEIGHT_LOD
low_size = SIZE // mip_scale
low = (outputs["height"].astype(np.float32) / 65535).reshape(
    low_size, mip_scale, low_size, mip_scale).mean(axis=(1, 3))
gx = (np.roll(low, -1, 1) - np.roll(low, 1, 1)) * (low_size / 2)
gy = (np.roll(low, -1, 0) - np.roll(low, 1, 0)) * (low_size / 2)
macro = np.stack([-gx, -.577350269 * gx + 1.154700538 * gy], axis=-1)
macro *= AMPLITUDE_METRES / (TILE_METRES * PERIOD)
coordinate = (np.arange(SIZE, dtype=np.float32) + .5) / mip_scale - .5
lower = np.floor(coordinate).astype(np.int32)
fraction = coordinate - lower
horizontal = macro[:, lower % low_size] * (1 - fraction[None, :, None]) + macro[:, (lower + 1) % low_size] * fraction[None, :, None]
macro = horizontal[lower % low_size] * (1 - fraction[:, None, None]) + horizontal[(lower + 1) % low_size] * fraction[:, None, None]
# Retain only bounded high-frequency detail from the already aligned normal map.
original = outputs["normal"].astype(np.float32) / 255 * 2 - 1
fine = original[..., :2] / np.maximum(original[..., 2:3], .1)
frequency = np.fft.fftfreq(SIZE)
kernel = np.exp(-2 * np.pi**2 * 8**2 * (frequency[:, None]**2 + frequency[None, :]**2))
for axis in range(2):
    fine[..., axis] -= np.fft.ifft2(np.fft.fft2(fine[..., axis]) * kernel).real.astype(np.float32)
fine /= np.maximum(np.linalg.norm(fine, axis=-1, keepdims=True), 1)
combined = np.concatenate([macro + FINE_NORMAL_GAIN * fine, np.ones((SIZE, SIZE, 1), np.float32)], axis=-1)
combined /= np.linalg.norm(combined, axis=-1, keepdims=True)
outputs["normal"] = encode(combined * .5 + .5)

include = root / "Assets/shaders/voxels/voxel_terrain_pattern.hlsl"
metadata = (
    "// Generated by Tools/bake_terrain_patterns.py; do not hand-edit.\n"
    f"#define TerrainPatternPeriod {PATTERN_PERIOD:.1f}\n"
    f"#define TerrainSandPatternPeriod {SAND_PATTERN_PERIOD:.1f}\n"
)
for name, (_, _, size, tile, amplitude) in SETTINGS.items():
    prefix = "Terrain" + name.capitalize()
    metadata += (
        f"#define {prefix}TileMetres {tile:.1f}\n"
        f"#define {prefix}AmplitudeMetres {amplitude:.3f}\n"
        f"#define {prefix}SourceTexels {size:.1f}\n"
        f"#define {prefix}MinimumLod {MINIMUM_HEIGHT_LOD:.1f}\n"
    )
publish_bake(
    {output_directory / f"{material}_{name}.png": pixels for name, pixels in outputs.items()},
    include, metadata.replace("\n", "\r\n").encode())
manifest = {
    "heightSource": "authored curved ripples, 18 bands per period, variable spacing, local bends and smoothly tapered ridge breaks" if material == "sand" else "coherent source displacement",
    "colorGrain": "linear highpass sigma2.5 gain1.5" if material == "sand" else "unaltered",
    "patchRotation": "continuous deterministic" if material == "snow" else "none",
    "combinedHeightGaussianSigmaTexels": COMBINED_HEIGHT_SIGMA_TEXELS,
    "tileMetres": TILE_METRES, "amplitudeMetres": AMPLITUDE_METRES,
    "minimumHeightLod": MINIMUM_HEIGHT_LOD,
    "normalRecipe": f"quantized height box mip2, lattice chain rule, GL encoding; fine residual sigma8 cap1 gain{FINE_NORMAL_GAIN}",
    "fineNormalGain": FINE_NORMAL_GAIN,
    "size": SIZE, "period": PERIOD, "blendExponent": 8 if material == "grass" else 4,
    "blendCutoff": .001, "cutoffExponent": 4,
    "numpy": np.__version__, "pillow": pillow_version,
    "generatorSha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
    "publisherSha256": hashlib.sha256((Path(__file__).parent / "terrain_bake_output.py").read_bytes()).hexdigest(),
    "source": {name: {"path": path.relative_to(root).as_posix(),
                      "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}
               for name, path in paths.items()},
    "output": {path.name: hashlib.sha256(path.read_bytes()).hexdigest()
               for path in sorted(output_directory.glob("*.png"))},
    "shaderIncludeSha256": hashlib.sha256(include.read_bytes()).hexdigest(),
}
(output_directory / "manifest.json").write_bytes((json.dumps(manifest, indent=2) + "\n").replace("\n", "\r\n").encode())
print(f"Baked {SIZE} x {SIZE} {material} pattern, lattice period {PERIOD}.")
