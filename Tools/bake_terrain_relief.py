"""Bake matched periodic terrain surfaces for the parallax height search.

Offline dependencies: NumPy 2.3.5, Pillow 12.3.0. Run from any directory.
All channels share the same height-priority patch weights. Source scans remain
unchanged; the manifest records the complete derived-asset recipe.
"""
from pathlib import Path
import hashlib
import json
import sys

import numpy as np
from PIL import Image, __version__ as pillow_version


PERIOD = 2
SOURCE_SIZE = 2048
SIZE = PERIOD * SOURCE_SIZE
ROOT = Path(__file__).resolve().parents[1]


def sample(source, u, v):
	"""Wrapped bilinear sampling in linear space at source texel centers."""
	x, y = (u % 1) * SOURCE_SIZE - .5, (v % 1) * SOURCE_SIZE - .5
	ix, iy = np.floor(x).astype(np.int32), np.floor(y).astype(np.int32)
	fx, fy = (x - ix)[..., None], (y - iy)[..., None]
	a = source[iy % SOURCE_SIZE, ix % SOURCE_SIZE] * (1 - fx) + source[iy % SOURCE_SIZE, (ix + 1) % SOURCE_SIZE] * fx
	b = source[(iy + 1) % SOURCE_SIZE, ix % SOURCE_SIZE] * (1 - fx) + source[(iy + 1) % SOURCE_SIZE, (ix + 1) % SOURCE_SIZE] * fx
	return (a * (1 - fy) + b * fy).astype(np.float32)


def encode(value):
	return np.rint(np.clip(value, 0, 1) * 255).astype(np.uint8)


for material in sys.argv[1:] or ["dirt", "gray_rocks"]:
	source_directory = ROOT / "Assets/textures/terrain" / material
	output_directory = ROOT / "Assets/textures/terrain" / (material + "_relief")
	output_directory.mkdir(parents=True, exist_ok=True)
	paths = {name: source_directory / f"{material}_{suffix}_2k.png"
		for name, suffix in [("color", "diff"), ("normal", "nor_gl"),
			("roughness", "rough"), ("ao", "ao"), ("height", "disp")]}
	color = np.asarray(Image.open(paths["color"]).convert("RGB"), dtype=np.float32) / 255
	color = np.where(color <= .04045, color / 12.92, ((color + .055) / 1.055) ** 2.4)
	normal = np.asarray(Image.open(paths["normal"]).convert("RGB"), dtype=np.float32) / 255 * 2 - 1
	normal /= np.maximum(np.linalg.norm(normal, axis=2, keepdims=True), 1e-8)
	scalars = {}
	for name in ["roughness", "ao"]:
		pixels = np.asarray(Image.open(paths[name]))
		if pixels.ndim == 3:
			assert pixels.shape[2] == 3 and np.array_equal(pixels[..., 0], pixels[..., 1]) and np.array_equal(pixels[..., 0], pixels[..., 2])
			pixels = pixels[..., 0]
		assert pixels.ndim == 2 and pixels.dtype in (np.uint8, np.uint16)
		scalars[name] = pixels.astype(np.float32) / np.iinfo(pixels.dtype).max
	roughness, occlusion = scalars["roughness"], scalars["ao"]
	height = np.asarray(Image.open(paths["height"]))
	assert height.dtype == np.uint16 and height.shape == (SOURCE_SIZE, SOURCE_SIZE)
	height = height.astype(np.float32) / 65535
	assert color.shape == normal.shape == (SOURCE_SIZE, SOURCE_SIZE, 3)
	assert roughness.shape == occlusion.shape == height.shape
	source = np.concatenate([color, normal, roughness[..., None], occlusion[..., None], height[..., None]], axis=2)
	del color, normal, roughness, occlusion, height
	outputs = {"color": np.empty((SIZE, SIZE, 3), np.uint8),
		"normal": np.empty((SIZE, SIZE, 3), np.uint8),
		"roughness": np.empty((SIZE, SIZE), np.uint8),
		"ao": np.empty((SIZE, SIZE), np.uint8),
		"height": np.empty((SIZE, SIZE), np.uint16)}

	for first in range(0, SIZE, 32):
		lx = np.broadcast_to((np.arange(SIZE, dtype=np.float32) + .5) * PERIOD / SIZE,
			(min(32, SIZE - first), SIZE))
		ly = np.broadcast_to(((np.arange(first, min(first + 32, SIZE), dtype=np.float32) + .5)
			* PERIOD / SIZE)[:, None], lx.shape)
		cx, cy = np.floor(lx), np.floor(ly)
		fx, fy = lx - cx, ly - cy
		lower = fx + fy <= 1
		blend = np.stack([np.where(lower, 1 - fx - fy, fx + fy - 1),
			np.where(lower, fx, 1 - fx), np.where(lower, fy, 1 - fy)], axis=2)
		blend *= blend
		blend *= blend
		blend = np.maximum(blend - .001, 0)
		blend /= blend.sum(axis=2, keepdims=True)
		u, v = lx + .5 * ly, ly * np.float32(.8660254037844386)
		patches = []
		for patch in range(3):
			if patch == 0:
				vx, vy = cx + ~lower, cy + ~lower
			elif patch == 1:
				vx, vy = cx + lower, cy + ~lower
			else:
				vx, vy = cx + ~lower, cy + lower
			kx = (vx.astype(np.int32) % PERIOD).astype(np.uint32)
			ky = (vy.astype(np.int32) % PERIOD).astype(np.uint32)
			hashed = (kx * np.uint32(0x8DA6B343)) ^ (ky * np.uint32(0xD8163841))
			hashed ^= hashed >> 16
			hashed *= np.uint32(0x7FEB352D)
			hashed ^= hashed >> 15
			ox = (hashed & 65535).astype(np.float32) / 65536
			oy = (hashed >> 16).astype(np.float32) / 65536
			# Relative-to-vertex lookup plus wrapped keys makes both cache edges
			# evaluate the same continuous function, including negative tiles.
			patches.append(sample(source, u - vx - .5 * vy + ox,
				v - vy * np.float32(.8660254037844386) + oy))
		heights = np.stack([p[..., 8] for p in patches], axis=2)
		scores = heights + blend
		weights = np.maximum(scores - scores.max(axis=2, keepdims=True) + .15, 0) * blend
		weights /= np.maximum(weights.sum(axis=2, keepdims=True), 1e-8)
		total = sum(patches[i] * weights[..., i, None] for i in range(3))
		rgb = total[..., :3]
		rgb = np.where(rgb <= .0031308, rgb * 12.92, 1.055 * np.maximum(rgb, 0) ** (1 / 2.4) - .055)
		rows = slice(first, first + len(lx))
		outputs["color"][rows] = encode(rgb)
		# Keep blended vector length; final triplanar reconstruction normalizes.
		outputs["normal"][rows] = encode(total[..., 3:6] * .5 + .5)
		outputs["roughness"][rows] = encode(total[..., 6])
		outputs["ao"][rows] = encode(total[..., 7])
		outputs["height"][rows] = np.rint(np.clip(total[..., 8], 0, 1) * 65535).astype(np.uint16)
	for name, pixels in outputs.items():
		target = output_directory / f"{name}.png"
		temporary = target.with_suffix(".tmp")
		Image.fromarray(pixels).save(temporary, format="PNG", compress_level=6)
		temporary.replace(target)
	manifest = {"size": SIZE, "period": PERIOD, "sourceTexelsPerTile": SOURCE_SIZE,
		"blendExponent": 4, "blendCutoff": .001, "heightBlendWidth": .15,
		"heightBits": 16, "numpy": np.__version__, "pillow": pillow_version,
		"generatorSha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
		"source": {name: {"path": p.relative_to(ROOT).as_posix(),
			"sha256": hashlib.sha256(p.read_bytes()).hexdigest()} for name, p in paths.items()},
		"output": {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(output_directory.glob("*.png"))}}
	(output_directory / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", newline="\r\n")
	print(f"Baked {material}: {SIZE} square, period {PERIOD}, matched five channels.", flush=True)

(ROOT / "Assets/shaders/voxels/voxel_relief_pattern.hlsl").write_text(
	"// Generated by Tools/bake_terrain_relief.py; do not hand-edit.\n"
	f"#define TerrainReliefPatternPeriod {PERIOD}.0\n"
	f"#define TerrainReliefSourceTexels {SOURCE_SIZE}.0\n", newline="\r\n")
