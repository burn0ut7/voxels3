"""Bake deterministic, periodic linear cloud volumes (x-fastest R8 payloads)."""

from pathlib import Path
import hashlib
import json

import numpy as np


OUTPUT = Path(__file__).resolve().parents[1] / "Assets" / "textures" / "clouds"
SEED = 41873


def perlin(size, frequency, seed):
    rng = np.random.default_rng(seed)
    gradients = rng.normal(size=(frequency, frequency, frequency, 3)).astype(np.float32)
    gradients /= np.linalg.norm(gradients, axis=-1, keepdims=True)
    z, y, x = np.indices((size, size, size), dtype=np.float32) * (frequency / size)
    grid = [x.astype(np.int32), y.astype(np.int32), z.astype(np.int32)]
    frac = [x - grid[0], y - grid[1], z - grid[2]]
    fade = [f * f * f * (f * (f * 6 - 15) + 10) for f in frac]
    result = np.zeros_like(x)
    for dz in (0, 1):
        for dy in (0, 1):
            for dx in (0, 1):
                g = gradients[(grid[2] + dz) % frequency,
                              (grid[1] + dy) % frequency,
                              (grid[0] + dx) % frequency]
                dot = g[..., 0] * (frac[0] - dx) + g[..., 1] * (frac[1] - dy) + g[..., 2] * (frac[2] - dz)
                weight = (fade[0] if dx else 1 - fade[0]) * (fade[1] if dy else 1 - fade[1]) * (fade[2] if dz else 1 - fade[2])
                result += dot * weight
    return np.clip(result * 0.9 + 0.5, 0, 1)


def worley(size, frequency, seed):
    rng = np.random.default_rng(seed)
    features = rng.random((frequency, frequency, frequency, 3), dtype=np.float32)
    z, y, x = np.indices((size, size, size), dtype=np.float32) * (frequency / size)
    ix, iy, iz = x.astype(np.int32), y.astype(np.int32), z.astype(np.int32)
    fx, fy, fz = x - ix, y - iy, z - iz
    closest = np.full_like(x, 10)
    for dz in (-1, 0, 1):
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                f = features[(iz + dz) % frequency, (iy + dy) % frequency, (ix + dx) % frequency]
                distance = (dx + f[..., 0] - fx) ** 2 + (dy + f[..., 1] - fy) ** 2 + (dz + f[..., 2] - fz) ** 2
                np.minimum(closest, distance, out=closest)
    return np.clip(1 - np.sqrt(closest), 0, 1)


def layout():
    # Constant elevation within each cloudy group. The separation mask hides
    # interpolation across different elevations, keeping bases locally level.
    size, frequency = 256, 6
    rng = np.random.default_rng(SEED + 300)
    centers = rng.uniform(0.2, 0.8, (frequency, frequency, 2)).astype(np.float32)
    heights = rng.random((frequency, frequency), dtype=np.float32)
    radii = rng.uniform(0.65, 1.0, (frequency, frequency, 2)).astype(np.float32)
    angles = rng.uniform(0, np.pi * 2, (frequency, frequency)).astype(np.float32)
    y, x = (np.indices((size, size), dtype=np.float32) + 0.5) * (frequency / size)
    ix, iy = x.astype(np.int32), y.astype(np.int32)
    fx, fy = x - ix, y - iy
    closest = np.full_like(x, 100)
    second = np.full_like(x, 100)
    elevation = np.zeros_like(x)
    occupancy = np.ones_like(x)
    occupied = (np.random.default_rng(SEED + 302).random((frequency, frequency)) >= 0.35).astype(np.float32)
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            cy, cx = (iy + dy) % frequency, (ix + dx) % frequency
            delta_x = dx + centers[cy, cx, 0] - fx
            delta_y = dy + centers[cy, cx, 1] - fy
            cosine, sine = np.cos(angles[cy, cx]), np.sin(angles[cy, cx])
            u = (delta_x * cosine + delta_y * sine) / radii[cy, cx, 0]
            v = (-delta_x * sine + delta_y * cosine) / radii[cy, cx, 1]
            distance = np.sqrt(u * u + v * v)
            nearer = distance < closest
            second = np.where(nearer, closest, np.minimum(second, distance))
            elevation = np.where(nearer, heights[cy, cx], elevation)
            occupancy = np.where(nearer, occupied[cy, cx], occupancy)
            closest = np.minimum(closest, distance)
    coverage = np.clip((1.15 - closest) / 0.8, 0, 1)
    coverage = coverage * coverage * (3 - 2 * coverage) * occupancy
    separation = np.clip((second - closest - 0.04) / 0.22, 0, 1)
    separation = separation * separation * (3 - 2 * separation)
    return np.stack((coverage, elevation, separation, np.ones_like(x)), axis=-1)


def main():
    OUTPUT.mkdir(parents=True, exist_ok=True)
    base_perlin = sum(weight * perlin(128, frequency, SEED + frequency)
                      for frequency, weight in ((4, 0.40), (8, 0.40), (16, 0.20)))
    billows = sum(weight * worley(128, frequency, SEED + 100 + frequency)
                  for frequency, weight in ((4, 0.40), (8, 0.40), (16, 0.20)))
    # Dilate the connected signal with rounded cellular lobes.
    shape = billows + (1 - billows) * base_perlin
    # Store successively band-limited erosion at the same coordinates. Removing
    # unresolved octaves keeps the broad billows instead of discarding all detail.
    erosion_octaves = [worley(32, frequency, SEED + 200 + frequency) for frequency in (4, 8, 16)]
    low, middle, high = erosion_octaves
    full = low * 0.625 + middle * 0.25 + high * 0.125
    medium = low * 0.625 + middle * 0.25 + high.mean() * 0.125
    broad = low * 0.625 + middle.mean() * 0.25 + high.mean() * 0.125
    center = 0.5 - full.mean()
    detail = np.clip(np.stack((full + center, medium + center, broad + center,
                               np.full_like(full, 0.5)), axis=-1), 0, 1)
    records = []
    for name, volume in (("shape", shape), ("erosion", detail)):
        payload = np.rint(np.clip(volume, 0, 1) * 255).astype(np.uint8).tobytes()
        path = OUTPUT / (name + ".bin")
        path.write_bytes(payload)
        records.append({"file": path.name, "size": list(volume.shape[:3]), "format": "linear RGBA8" if name == "erosion" else "linear R8",
                        "channels": ["full", "without frequency16", "frequency4 only", "mean"] if name == "erosion" else ["density"], "bytes": len(payload),
                        "sha256": hashlib.sha256(payload).hexdigest()})
        print(name, volume.shape, len(payload), float(volume.min()), float(volume.max()))
    groups = np.rint(layout() * 255).astype(np.uint8).tobytes()
    (OUTPUT / "layout.bin").write_bytes(groups)
    manifest = {"version": 3, "seed": SEED, "format": "linear data, x then y then z, per-volume channels, no mip payload", "volumes": records,
                "layout": {"file": "layout.bin", "size": [256, 256], "format": "linear RGBA8, x then y",
                           "channels": ["coverage", "elevation", "separation", "unused"],
                           "bytes": len(groups), "sha256": hashlib.sha256(groups).hexdigest()}}
    (OUTPUT / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
