"""Bake a coherent fractured bedrock material from a continuous rock-face scan.

NumPy/Pillow are offline dependencies. Color, height and fine normals share
the same original scan coordinates; macro normals derive from the final height.
"""
from pathlib import Path
from terrain_bake_output import publish_bake
import hashlib
import json

import numpy as np
from PIL import Image


root = Path(__file__).resolve().parents[1]
source = root / 'Assets/textures/terrain/cliff_side'
output = root / 'Assets/textures/terrain/stone_pattern'
output.mkdir(exist_ok=True)
SIZE = 4096
TILE_METRES = 12.0
AMPLITUDE = .50
MIN_LOD = 1
paths = {k: source / f'cliff_side_{v}_2k.png' for k, v in
         [('color', 'diff'), ('height', 'disp'), ('normal', 'nor_gl'),
          ('roughness', 'rough'), ('ao', 'ao')]}
height = np.asarray(Image.open(paths['height']), dtype=np.float32) / 65535
frequency = np.fft.fftfreq(2048)
frequency_squared = frequency[:, None]**2 + frequency[None, :]**2


def lowpass(value, sigma=8):
    kernel = np.exp(-2 * np.pi**2 * sigma**2 * frequency_squared)
    return np.fft.ifft2(np.fft.fft2(value) * kernel).real.astype(np.float32)


height = lowpass(height, sigma=.75)
normal = np.asarray(Image.open(paths['normal']).convert('RGB'), dtype=np.float32) / 255 * 2 - 1
slope = normal[..., :2] / np.maximum(normal[..., 2:3], .1)
fine = slope - np.stack([lowpass(slope[..., i]) for i in range(2)], axis=-1)
fine /= np.maximum(np.linalg.norm(fine, axis=-1, keepdims=True), 1)
fine *= .5
rough = np.asarray(Image.open(paths['roughness']), dtype=np.float32) / 65535
ao = np.asarray(Image.open(paths['ao']), dtype=np.float32) / 65535
# Preserve scanned angular fractures: translate patches, never warp their shape.
# All channels share minimum-error cuts through overlapping scan regions.
color = np.asarray(Image.open(paths['color']).convert('RGB'), np.float32) / 255
color = np.where(color <= .04045, color / 12.92, ((color + .055) / 1.055) ** 2.4)
scan = np.concatenate((color, height[..., None], rough[..., None], ao[..., None], fine), axis=2)
field = np.tile(scan, (2, 2, 1))
variants = []
for flip_y, flip_x in ((False, False), (False, True), (True, False), (True, True)):
    variant = scan[::(-1 if flip_y else 1), ::(-1 if flip_x else 1)].copy()
    if flip_x:
        variant[..., 6] *= -1
    if flip_y:
        variant[..., 7] *= -1
    variants.append(variant)
for block_y in range(2):
    for block_x in range(2):
        field[block_y * 2048:(block_y + 1) * 2048, block_x * 2048:(block_x + 1) * 2048] = variants[block_y * 2 + block_x]
PATCH, OVERLAP, SEED = 1536, 256, 20260920
rng = np.random.default_rng(SEED)
placements = []
axis = np.arange(PATCH)
edge = np.zeros((PATCH, PATCH), bool)
edge[:OVERLAP] = edge[-OVERLAP:] = True
edge[:, :OVERLAP] = edge[:, -OVERLAP:] = True


def cut_path(error):
    cost = error[0].copy()
    parent = np.empty(error.shape, np.int8)
    for row in range(1, len(error)):
        choices = np.stack((np.r_[np.inf, cost[:-1]], cost, np.r_[cost[1:], np.inf]))
        direction = choices.argmin(axis=0)
        parent[row] = direction - 1
        cost = error[row] + choices[direction, np.arange(len(cost))]
    path = np.empty(len(error), np.int32)
    path[-1] = cost.argmin()
    for row in range(len(error) - 1, 0, -1):
        path[row - 1] = path[row] + parent[row, path[row]]
    return path


# A toroidal canvas keeps both output edges part of the same composition.
for gy in range(4):
    for gx in range(4):
        px = gx * 1024 + int(rng.integers(-128, 129))
        py = gy * 1024 + int(rng.integers(-128, 129))
        xx, yy = (axis + px) % SIZE, (axis + py) % SIZE
        existing = field[yy[:, None], xx[None, :]]
        best = None
        for attempt in range(24):
            sx, sy = rng.integers(0, 2048, 2)
            variant_index = int(rng.integers(0, len(variants)))
            candidate = variants[variant_index]
            sample = candidate[(axis[::16, None] + sy) % 2048, (axis[None, ::16] + sx) % 2048]
            delta = sample - existing[::16, ::16]
            error = np.mean(delta[..., :3]**2, axis=2) + 2 * delta[..., 3]**2
            score = error[edge[::16, ::16]].mean()
            if best is None or score < best[0]:
                best = (score, int(sx), int(sy), variant_index)
        _, sx, sy, variant_index = best
        donor = variants[variant_index][(axis[:, None] + sy) % 2048, (axis[None, :] + sx) % 2048]
        delta = donor - existing
        error = np.mean(delta[..., :3]**2, axis=2) + 2 * delta[..., 3]**2
        left = cut_path(error[:, :OVERLAP])
        right = cut_path(error[:, -OVERLAP:]) + PATCH - OVERLAP
        top = cut_path(error[:OVERLAP, :].T)
        bottom = cut_path(error[-OVERLAP:, :].T) + PATCH - OVERLAP
        # Four-texel feather avoids single-pixel height discontinuities.
        mask = np.clip((axis[None, :] - left[:, None]) / 4 + .5, 0, 1)
        mask *= np.clip((right[:, None] - axis[None, :]) / 4 + .5, 0, 1)
        mask *= np.clip((axis[:, None] - top[None, :]) / 4 + .5, 0, 1)
        mask *= np.clip((bottom[None, :] - axis[:, None]) / 4 + .5, 0, 1)
        # Keep the outside of each patch unchanged, including cut endpoints.
        border = np.minimum(np.minimum(axis, PATCH - 1 - axis) / 4, 1)
        mask *= border[:, None] * border[None, :]
        field[yy[:, None], xx[None, :]] = existing * (1 - mask[..., None]) + donor * mask[..., None]
        placements.append({'output': [px, py], 'source': [sx, sy], 'variant': variant_index, 'error': float(best[0])})
        print(f'Quilted stone patch {len(placements)}/16', flush=True)
rgb = np.clip(field[..., :3], 0, 1)
rgb = np.where(rgb <= .0031308, rgb * 12.92, 1.055 * rgb ** (1 / 2.4) - .055)
colors = np.rint(rgb * 255).astype(np.uint8)
roughness = np.rint(np.clip(field[..., 4], 0, 1) * 255).astype(np.uint8)
occlusion = np.rint(np.clip(field[..., 5], 0, 1) * 255).astype(np.uint8)
heights = np.rint(np.clip(field[..., 3], 0, 1) * 65535).astype(np.uint16)
fine_slopes = field[..., 6:8]

# Use the same physical height footprint as the runtime's minimum mip.
mip_scale = 2**MIN_LOD
low_size = SIZE // mip_scale
low = (heights.astype(np.float32) / 65535).reshape(low_size, mip_scale, low_size, mip_scale).mean(axis=(1, 3))
step = TILE_METRES / low_size
macro = np.stack((-(np.roll(low, -1, 1) - np.roll(low, 1, 1)),
                   np.roll(low, -1, 0) - np.roll(low, 1, 0)), axis=-1) * (AMPLITUDE / (2 * step))
coordinate = (np.arange(SIZE, dtype=np.float32) + .5) / mip_scale - .5
lower = np.floor(coordinate).astype(np.int32)
fraction = coordinate - lower
horizontal = macro[:, lower % low_size] * (1 - fraction[None, :, None]) + macro[:, (lower + 1) % low_size] * fraction[None, :, None]
combined = horizontal[lower % low_size] * (1 - fraction[:, None, None]) + horizontal[(lower + 1) % low_size] * fraction[:, None, None] + fine_slopes
normals = np.concatenate([combined, np.ones((SIZE, SIZE, 1), np.float32)], axis=-1)
normals /= np.linalg.norm(normals, axis=-1, keepdims=True)
normal_pixels = np.rint(np.clip(normals * .5 + .5, 0, 1) * 255).astype(np.uint8)
# Generated physical metadata keeps the renderer and bake in agreement.
include = root / 'Assets/shaders/voxels/voxel_stone_pattern.hlsl'
include_bytes = (
    '// Generated by Tools/bake_stone_relief.py; do not edit independently.\n'
    f'#define TerrainStoneTileMetres {TILE_METRES:.1f}\n'
    f'#define TerrainStoneAmplitudeMetres {AMPLITUDE:.3f}\n'
    f'#define TerrainStoneSourceTexels {SIZE:.1f}\n'
    f'#define TerrainStoneMinimumLod {MIN_LOD:.1f}\n'
).replace('\n', '\r\n').encode()
publish_bake(
    {output / f'stone_{name}.png': pixels for name, pixels in
     [('color', colors), ('normal', normal_pixels), ('roughness', roughness), ('ao', occlusion), ('height', heights)]},
    include, include_bytes)
manifest = {'status': 'unaccepted prototype', 'size': SIZE,
            'tileMetres': TILE_METRES, 'amplitudeMetres': AMPLITUDE, 'minimumHeightLod': MIN_LOD,
            'mapping': 'periodic minimum-error quilting; translated/reflected scan patches; no coordinate warp',
            'source': 'Poly Haven Cliff Side CC0', 'seed': SEED,
            'patchSize': PATCH, 'overlap': OVERLAP, 'featherTexels': 4, 'placements': placements,
            'sourceHeightGaussianSigmaTexels': .75, 'fineNormalGain': .5,
            'heightAuthoring': 'matched scan height; no AO-derived recesses',
            'materialIntent': 'continuous fractured underground bedrock; broad faces and sharp breaks; no loose rubble',
            'generatorSha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
            'publisherSha256': hashlib.sha256((Path(__file__).parent / 'terrain_bake_output.py').read_bytes()).hexdigest(),
            'macroNormal': 'final quantized height at configured minimum mip; periodic central differences; GL encoding',
            'inputs': {str(p.relative_to(root)): hashlib.sha256(p.read_bytes()).hexdigest() for p in paths.values()},
            'outputs': {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in output.glob('*.png')}}
(output / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
print(f'Baked coherent {SIZE}px bedrock pattern; physical repeat {TILE_METRES}m.', flush=True)

