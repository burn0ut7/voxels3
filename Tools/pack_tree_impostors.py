"""Pack native imported-tree color, signed object normal, occlusion and depth."""
from pathlib import Path
import argparse
import hashlib
import json
import re
import numpy as np
from PIL import Image
from scipy.ndimage import distance_transform_edt

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / '.codex/tree-build/imported-impostors'
OUTPUT = ROOT / 'Assets/textures/trees/impostors'
MATERIALS = ROOT / 'Assets/materials/trees/impostors'


def linear(value):
    return np.where(value <= .04045, value / 12.92, ((value + .055) / 1.055) ** 2.4)


def reduce_frame(directory, row, view, size):
    def reduce(channel):
        return np.asarray(Image.fromarray(channel.astype(np.float32)).resize((size, size), Image.Resampling.BOX))
    color = np.asarray(Image.open(directory / f'{row}-{view}-Albedo.png').convert('RGBA'), np.float32) / 255
    normal = np.asarray(Image.open(directory / f'{row}-{view}-NormalMap.png').convert('RGBA'), np.float32) / 255
    depth = np.asarray(Image.open(directory / f'{row}-{view}-Depth.png').convert('RGBA'), np.float32) / 255
    occlusion = np.asarray(Image.open(directory / f'{row}-{view}-AmbientOcclusion.png').convert('RGBA'), np.float32) / 255
    alpha = color[:, :, 3]
    coverage = reduce(alpha)
    rgb = np.stack([reduce(linear(color[:, :, c]) * alpha) for c in range(3)], axis=2)
    rgb /= np.maximum(coverage[:, :, None], 1e-6)
    rgb = np.where(rgb <= .0031308, rgb * 12.92, 1.055 * np.maximum(rgb, 0) ** (1 / 2.4) - .055)
    source_vector = normal[:, :, :3] * 2 - 1
    # Preserve both hemispheres. Two-sided surfaces can carry a normal pointing
    # away from the view; reversing it changes the captured lighting response.
    vector = np.stack([reduce(source_vector[:, :, c] * alpha) for c in range(3)], axis=2)
    vector /= np.maximum(np.linalg.norm(vector, axis=2, keepdims=True), 1e-6)
    # Native NormalMap writes encoded normals directly; the custom depth is
    # captured through Albedo, which writes sRGB. Decode only that depth pass.
    height = reduce(linear(depth[:, :, 0]) * alpha) / np.maximum(coverage, 1e-6)
    # Reprojection starts on the center plane, outside the captured silhouette
    # for foreground branches. Carry the nearest surface depth through empty
    # texels so the first correction can reach those branches. Color coverage
    # remains unchanged; a center-plane depth outside a narrow dilation clips
    # branch tips as perspective grows near the mesh/impostor handoff.
    covered = coverage > 1e-5
    if not np.any(covered):
        raise ValueError(f'Empty tree capture: {directory}/{row}-{view}')
    nearest = distance_transform_edt(~covered, return_distances=False, return_indices=True)
    height = height[tuple(nearest)]
    # Native AO debug explicitly cancels the target's sRGB encoding, like
    # NormalMap. Preserve it as linear numeric data, unlike the depth pass.
    ao = reduce(occlusion[:, :, 0] * alpha) / np.maximum(coverage, 1e-6)
    packed = np.concatenate((rgb, vector * .5 + .5, ao[:, :, None], height[:, :, None]), axis=2).astype(np.float64)
    valid = coverage > 1e-5
    for _ in range(16):
        padded = np.pad(packed * valid[:, :, None], ((1, 1), (1, 1), (0, 0)))
        mask = np.pad(valid.astype(np.float32), 1)
        total = padded[:-2, 1:-1] + padded[2:, 1:-1] + padded[1:-1, :-2] + padded[1:-1, 2:]
        count = mask[:-2, 1:-1] + mask[2:, 1:-1] + mask[1:-1, :-2] + mask[1:-1, 2:]
        new = (~valid) & (count > 0)
        packed[new] = total[new] / count[new, None]
        valid |= new
    packed[:, :, 7] = height
    return [np.uint8(np.clip(value * 255 + .5, 0, 255)) for value in
        (packed[:, :, :3], coverage, packed[:, :, 3:6], packed[:, :, 7], packed[:, :, 6])]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('keys', nargs='*')
    args = parser.parse_args()
    catalog = json.loads((ROOT / 'Assets/models/tree_lab/catalog.json').read_text())
    keys = args.keys or [entry['Key'] for entry in catalog['Specimens']]
    for key in keys:
        assert re.fullmatch(r'[a-z0-9_]{1,100}', key), 'Expected an installed tree key'
        manifest = json.loads((ROOT / 'Assets/models/tree_lab' / key / 'manifest.json').read_text())
        assert manifest['complete'] and manifest.get('asset_key', key) == key
    rows = {}
    wood_metadata = {}
    # Validate all selected sources before replacing any of their outputs.
    for key in keys:
        folder = ROOT / 'Assets/models/tree_lab' / key
        source = json.loads((folder / 'manifest.json').read_text())
        roles = {m.get('role') for m in source.get('render_models', [])}
        if roles == {'wood', 'foliage'}:
            wood = json.loads((folder / 'distant_wood.json').read_text())
            assert wood['Version'] == 1 and wood['Specimen'] == key
            assert wood['SourceSHA256'] == hashlib.sha256((folder / wood['SourceFile']).read_bytes()).hexdigest()
            assert wood['ToolSHA256'] == hashlib.sha256((ROOT / 'Tools/BlenderTrees/bake_distant_wood.py').read_bytes()).hexdigest()
            for name, expected in wood['Outputs'].items():
                assert hashlib.sha256((folder / name).read_bytes()).hexdigest() == expected, f'Stale trunk mesh: {name}'
            wood_metadata[key] = {'DistantWood': wood}
        for row in range(4):
            directory = SOURCE / key
            metadata = json.loads((directory / f'row-{row}.json').read_text())
            assert metadata['Version'] == 2 and metadata['Specimen'] == key and metadata['ElevationRow'] == row
            assert metadata['CapturePixels'] == 1024 and metadata['OutputPixels'] in (256, 1024)
            assert metadata['Azimuths'] in (8, 16) and metadata['Elevations'] == [-15, 15, 45, 75]
            assert metadata['Wind'] == 'disabled on object and material copies'
            multipart = len(metadata['RenderModels']) > 1
            assert metadata['Lod'] == (2 if multipart else 0)
            assert metadata['LeafRetention'] == ('runtime minimum' if multipart else 'full')
            assert metadata['LeafFacing'] == 'source material feature preserved for all passes'
            source_manifest_path = f'Assets/models/tree_lab/{key}/manifest.json'
            assert source_manifest_path in metadata['SourceHashes'], 'Missing manifest provenance'
            source_manifest = json.loads((ROOT / source_manifest_path).read_text())
            expected_models = [entry['filename'] for entry in source_manifest.get('render_models',
                [{'filename': f'{key}.vmdl'}])]
            assert metadata['RenderModels'] == expected_models, 'Incomplete tree capture'
            for path, expected in metadata['SourceHashes'].items():
                assert hashlib.sha256((ROOT / path).read_bytes()).hexdigest() == expected, f'Stale bake {key}/{row}: {path}'
            if row:
                for field in ('Center', 'Diameter', 'SourceHashes', 'OutputPixels', 'Azimuths', 'RenderModels', 'LeafFacing', 'Lod', 'LeafRetention'):
                    assert metadata[field] == rows[key, 0][field], f'Inconsistent {key}/{row}: {field}'
            for view in range(metadata['Azimuths']):
                for mode in ('Albedo', 'NormalMap', 'Depth', 'AmbientOcclusion'):
                    with Image.open(directory / f'{row}-{view}-{mode}.png') as image:
                        assert image.size == (1024, 1024) and image.mode == 'RGBA'
            rows[key, row] = metadata
    OUTPUT.mkdir(parents=True, exist_ok=True)
    MATERIALS.mkdir(parents=True, exist_ok=True)
    for key in keys:
        metadata = rows[key, 0]
        size = metadata['OutputPixels']
        textures = [Image.new(mode, (size * metadata['Azimuths'], size * 4)) for mode in ('RGB', 'L', 'RGB', 'L', 'L')]
        for row in range(4):
            for view in range(metadata['Azimuths']):
                arrays = reduce_frame(SOURCE / key, row, view, size)
                for texture, values in zip(textures, arrays):
                    texture.paste(Image.fromarray(values), (view * size, row * size))
        manifest_path = OUTPUT / (key + '.json')
        pending_files = []
        hashes = {}
        for suffix, image in zip(('color', 'coverage', 'normal', 'depth', 'occlusion'), textures):
            path = OUTPUT / f'{key}_{suffix}.png'
            pending = path.with_suffix('.png.installing')
            image.save(pending, format='PNG')
            pending_files.append((pending, path))
            hashes[path.name] = hashlib.sha256(pending.read_bytes()).hexdigest()
        source = json.loads((ROOT / 'Assets/models/tree_lab' / key / 'manifest.json').read_text())
        height = source.get('motion', {}).get('position_scale_meters', 0) / 4 / .0254
        if height <= 0: height = max(lod['bounds_inches'][1][2] for lod in source['lods'])
        center = ' '.join(f'{value:.9g}' for value in metadata['Center'])
        material = MATERIALS / f'{key}.vmat'
        pending_material = material.with_suffix('.vmat.installing')
        pending_material.write_text('Layer0\n{\n\tshader "shaders/trees/tree_impostor.shader"\n\tF_ALPHA_TEST 1\n'
            + ''.join(f'\tTexture{parameter} "textures/trees/impostors/{key}_{suffix}.png"\n' for parameter,suffix in
                (('Color','color'),('Coverage','coverage'),('ObjectNormal','normal'),('Depth','depth'),('Occlusion','occlusion')))
            + f'\tg_flTreeTilePixels {size}\n\tg_flTreeHeight {height:.9g}\n'
            + f'\tg_flTreeAzimuths {metadata["Azimuths"]}\n'
            + f'\tg_vTreeFrameCenter "[{center}]"\n\tg_flTreeDiameter {metadata["Diameter"]:.9g}\n'
            + '\tg_flAlphaTestReference 0.3\n}\n',newline='\r\n')
        pending_files.append((pending_material, material))
        result = {**metadata, **wood_metadata.get(key, {}), 'Version':4, 'NormalEncoding':'signed-object-rgb',
            'PackerSHA256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
            'MaterialSHA256':hashlib.sha256(pending_material.read_bytes()).hexdigest(), 'OutputHashes':hashes,
            'Packing':'linear alpha-weighted albedo/depth/AO, signed object normalRGB, separate AO,16px color edge dilation, full nearest-surface depth padding'}
        pending = manifest_path.with_suffix('.pending.json')
        pending.write_text(json.dumps(result,indent=2)+'\n')
        for staged, destination in pending_files:
            if destination.exists() and staged.read_bytes() == destination.read_bytes():
                staged.unlink()
            else:
                staged.replace(destination)
        pending.replace(manifest_path)
        print('Packed', key, flush=True)


if __name__ == '__main__':
    main()
