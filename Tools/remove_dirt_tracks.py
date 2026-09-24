"""Replace scanned tire impressions with matched patches of the same clean soil.

Run with an untouched BZ texture directory and a separate output directory.
Requires NumPy and Pillow. All maps use identical donor coordinates and weights;
macro normals are rebuilt from the final quantized height, at the existing scale.
"""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter


parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("source", type=Path)
parser.add_argument("output", type=Path)
args = parser.parse_args()
assert args.source.resolve() != args.output.resolve(), "Keep an untouched source."
args.output.mkdir(parents=True, exist_ok=True)
size = 2048
prefix = "dry_mud_field_001_"
names = {
	"color": prefix + "diff_2k.png",
	"fine": prefix + "nor_gl_2k.png",
	"rough": prefix + "rough_2k.png",
	"ao": prefix + "ao_2k.png",
	"height": prefix + "height_smooth2_2k.png",
	"normal": prefix + "normal_12cm_tile3_smooth2.png",
}
inputs = {key: args.source / name for key, name in names.items()}
assert hashlib.sha256(inputs["height"].read_bytes()).hexdigest() == "e8e9fda4cb4228a3903eec58592deecdd2582eeaf200e8855f16eb5547b5020f", "Use the untouched BZ source height, not an already patched output."
color = np.asarray(Image.open(inputs["color"]).convert("RGB"), dtype=np.float32) / 255
assert color.shape == (size, size, 3)
color = np.where(color <= .04045, color / 12.92, ((color + .055) / 1.055) ** 2.4)
normal = np.asarray(Image.open(inputs["fine"]).convert("RGB"), dtype=np.float32) / 127.5 - 1
slopes = normal[..., :2] / np.maximum(normal[..., 2:3], .1)
frequency = np.fft.fftfreq(size)
kernel = np.exp(-2 * np.pi ** 2 * 8 ** 2 * (frequency[:, None] ** 2 + frequency[None, :] ** 2))
fine = slopes - np.stack([
	np.fft.ifft2(np.fft.fft2(slopes[..., axis]) * kernel).real for axis in range(2)
], axis=-1)
fine /= np.maximum(np.linalg.norm(fine, axis=-1, keepdims=True), 1)
rough = np.asarray(Image.open(inputs["rough"]).convert("L"), dtype=np.float32) / 255
ao = np.asarray(Image.open(inputs["ao"]).convert("L"), dtype=np.float32) / 255
height_pixels = np.asarray(Image.open(inputs["height"]))
assert height_pixels.shape == (size, size) and height_pixels.dtype == np.uint16
height = height_pixels.astype(np.float32) / 65535
source = np.concatenate([color, fine, rough[..., None], ao[..., None], height[..., None]], axis=-1)

# The main diagonal tread and the short impression entering the left edge.
# Coordinates are fractions of the original scan, including its wrap boundaries.
polygons = [
	[(.35, 0), (1, 0), (.88, .28), (.81, .50), (.73, .76), (.63, 1),
	 (0, 1), (0, .72), (.15, .50), (.23, .25)],
	[(0, .40), (.17, .45), (.22, .56), (.10, .65), (0, .64)],
]
mask_image = Image.new("L", (size, size))
draw = ImageDraw.Draw(mask_image)
for polygon in polygons:
	draw.polygon([(round(x * size), round(y * size)) for x, y in polygon], fill=255)
mask_image = mask_image.filter(ImageFilter.GaussianBlur(24))
mask = np.asarray(mask_image, dtype=np.float32)[..., None] / 255

# Transfer individual clods, not the donor rectangle's broad height bias: doing
# the latter creates raised rectangular patches. Continue the surrounding soil's
# broad height through the masked area, then add the donor's small-scale relief.
frequency_squared = frequency[:, None] ** 2 + frequency[None, :] ** 2
macro_kernel = np.exp(-2 * np.pi ** 2 * 192 ** 2 * frequency_squared)
known = 1 - mask[..., 0]
continued_height = np.fft.ifft2(np.fft.fft2(height * known) * macro_kernel).real
continued_height /= np.maximum(np.fft.ifft2(np.fft.fft2(known) * macro_kernel).real, 1e-8)
donor = source.copy()
donor[..., 7] -= np.fft.ifft2(np.fft.fft2(height) * np.exp(-2 * np.pi ** 2 * 48 ** 2 * frequency_squared)).real
donor[..., 7] += .5

# These rectangles are clear of tread in every source map. Periodic patch centers
# share wrap indices, so the replacement itself has no outer tiling seam.
donor_rectangles = [(1536, 512, 2048, 2048), (0, 0, 512, 512)]
stride, patch_size = 256, 384
random = np.random.default_rng(20260920)
total = np.zeros_like(source, dtype=np.float64)
weights = np.zeros((size, size, 1), dtype=np.float64)
axis_weight = np.maximum(1 - np.abs((np.arange(patch_size) + .5) / (patch_size / 2) - 1), 0) ** 4
window = axis_weight[:, None] * axis_weight[None, :]
patches = []
for row in range(size // stride):
	for column in range(size // stride):
		donor_rectangle = donor_rectangles[int(random.integers(len(donor_rectangles)))]
		x = int(random.integers(donor_rectangle[0], donor_rectangle[2] - patch_size + 1))
		y = int(random.integers(donor_rectangle[1], donor_rectangle[3] - patch_size + 1))
		patches.append([column, row, x, y])
		patch = donor[y:y + patch_size, x:x + patch_size]
		weight = (window * np.exp(10 * patch[..., 7]))[..., None]
		xs = (column * stride + np.arange(patch_size) - patch_size // 2) % size
		ys = (row * stride + np.arange(patch_size) - patch_size // 2) % size
		total[ys[:, None], xs[None, :]] += patch * weight
		weights[ys[:, None], xs[None, :]] += weight
assert np.all(weights > 0)
replacement = total / weights
replacement[..., 7] += continued_height - .5
result = (source * (1 - mask) + replacement * mask).astype(np.float32)
assert np.isfinite(result).all()
height_pixels = np.rint(np.clip(result[..., 7], 0, 1) * 65535).astype(np.uint16)
height = height_pixels.astype(np.float32) / 65535

# Preserve BZ's 3 m chart, 120 mm interval and box-mip2 macro derivative.
low = height.reshape(512, 4, 512, 4).mean(axis=(1, 3))
macro = np.stack((-(np.roll(low, -1, 1) - np.roll(low, 1, 1)),
	np.roll(low, -1, 0) - np.roll(low, 1, 0)), axis=-1) * (.12 / (2 * 3 / 512))
coordinate = (np.arange(size, dtype=np.float32) + .5) / 4 - .5
lower = np.floor(coordinate).astype(np.int32)
fraction = coordinate - lower
horizontal = macro[:, lower % 512] * (1 - fraction[None, :, None]) + macro[:, (lower + 1) % 512] * fraction[None, :, None]
macro = horizontal[lower % 512] * (1 - fraction[:, None, None]) + horizontal[(lower + 1) % 512] * fraction[:, None, None]
normal = np.concatenate((macro + result[..., 3:5] * .5, np.ones((size, size, 1))), axis=-1)
normal /= np.linalg.norm(normal, axis=-1, keepdims=True)
color = result[..., :3]
color = np.where(color <= .0031308, color * 12.92, 1.055 * np.maximum(color, 0) ** (1 / 2.4) - .055)
outputs = {"color": color, "rough": result[..., 5], "ao": result[..., 6], "normal": normal * .5 + .5}
for key, pixels in outputs.items():
	Image.fromarray(np.rint(np.clip(pixels, 0, 1) * 255).astype(np.uint8)).save(args.output / names[key])
Image.fromarray(height_pixels).save(args.output / names["height"])
manifest = {
	"status": "tire marks removed; runtime qualification recorded in Docs/ValidationResults.md",
	"source": "https://polyhaven.com/a/dry_mud_field_001",
	"author": "Rob Tuytel (photography), Rico Cilliers (processing)",
	"license": "CC0", "licenseUrl": "https://polyhaven.com/license",
	"tileMetres": 3, "heightAmplitudeMetres": .12, "size": size,
	"method": "Matched clean-soil patches across color, fine normal, roughness, AO and height; final mip2 macro normal rebake",
	"seed": 20260920, "donorRectanglesPixels": donor_rectangles, "maskPolygons": polygons,
	"maskGaussianSigmaPixels": 24, "patchSize": patch_size, "stride": stride,
	"blendExponent": 4, "heightPriorityExponential": 10, "patches": patches,
	"heightContinuation": "normalized periodic Gaussian192 of unmasked height; donor highpass Gaussian48",
	"normalConvention": "OpenGL; existing 3m/120mm mip2 macro + sigma8-removed fine slopes capped1, gain0.5",
	"inputs": {path.name: hashlib.sha256(path.read_bytes()).hexdigest() for path in inputs.values()},
	"generatorSha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
	"outputs": {path.name: hashlib.sha256(path.read_bytes()).hexdigest() for path in args.output.glob("*.png")},
	"heightPercentiles": np.percentile(height, [0, 5, 50, 95, 100]).tolist(),
	"unmodifiedPixelFraction": float(np.mean(mask[..., 0] == 0)),
}
(args.output / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", newline="\r\n")
print(json.dumps({key: manifest[key] for key in ["heightPercentiles", "unmodifiedPixelFraction"]}))
