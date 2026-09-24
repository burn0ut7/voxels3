"""Validate staged Blender exports and install ordinary s&box model prefabs.

Usage: python Tools/BlenderTrees/install_exports.py [specimen_key ...] --install
Without --install this only validates inputs and prepares prefabs in staging.
"""

import argparse
import hashlib
import json
import math
import re
import runpy
import shutil
import uuid
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
STAGING = ROOT / '.codex/tree-export'
UNITS = 1 / .0254
MAX_LOD_TRIANGLES = runpy.run_path(str(Path(__file__).with_name('native_limits.py')))['MAX_LOD_TRIANGLES']


def guid(key, part):
	return str(uuid.uuid5(uuid.NAMESPACE_URL, f'voxels3/tree_lab/{key}/{part}'))


def component(key, part, kind, **properties):
	return {'__type': 'Sandbox.' + kind, '__guid': guid(key, part),
		'__enabled': True, 'Flags': 0, **properties}


def game_object(key, part, name, components=None, children=None, tags=''):
	return {'__guid': guid(key, part), '__version': 2, 'Flags': 0, 'Name': name,
		'Position': '0,0,0', 'Rotation': '0,0,0,1', 'Scale': '1,1,1',
		'Tags': tags, 'Enabled': True, 'NetworkMode': 0,
		'Components': components or [], 'Children': children or []}


def prepare(key):
	# A specimen key is a single safe directory name, never an arbitrary path.
	if not key or any(c not in 'abcdefghijklmnopqrstuvwxyz0123456789_' for c in key):
		raise ValueError(f'Invalid specimen key: {key!r}')
	folder = STAGING / key
	manifest = json.loads((folder / 'manifest.json').read_text(encoding='utf-8'))
	assert manifest.get('complete') is True, 'Export did not complete'
	for name, expected in manifest['files'].items():
		assert Path(name).name == name and name not in ('.', '..')
		path = folder / name
		assert path.stat().st_size == expected['bytes'], f'Stale or partial asset: {name}'
		assert hashlib.sha256(path.read_bytes()).hexdigest() == expected['sha256'], f'Stale or partial asset: {name}'
	assert manifest.get('asset_key', manifest['label'].lower()) == key
	assert math.isclose(manifest['meters_to_inches'], UNITS, rel_tol=1e-9)
	assert manifest.get('modeldoc_import_scale') == .01
	assert manifest.get('modeldoc_import_rotation') == [0, -90, 0]
	counts = [lod['triangles'] for lod in manifest['lods']]
	assert len(counts) == 3 and counts[0] > counts[1] > counts[2] > 0, counts
	for lod in manifest['lods']:
		assert all(math.isfinite(x) for corner in lod['bounds_inches'] for x in corner)
		assert lod['bounds_inches'][1][2] > 80, 'Incorrect tree scale'
		assert 'Foliage' in lod['parts'] and 'Trunk' in lod['parts']
	models = manifest.get('render_models', [{'filename': key + '.vmdl',
		'mesh_files': [f'lod{i}.fbx' for i in range(3)], 'role': 'tree', 'leaf_range': None, 'lods': manifest['lods']}])
	assert models and models[0]['filename'] == key + '.vmdl'
	assert len({model['filename'] for model in models}) == len(models)
	if len(models) > 1:
		assert not manifest.get('catalog_eligible', True), 'Population catalog cannot load multipart specimens'
		assert models[0]['role'] == 'wood' and models[0]['leaf_range'] is None
		next_leaf = 0
		for model in models[1:]:
			assert model['role'] == 'foliage' and model['leaf_range'][0] == next_leaf
			assert model['leaf_range'][1] > next_leaf
			next_leaf = model['leaf_range'][1]
		assert all(lod['foliage']['retained_leaves'] == next_leaf for lod in manifest['lods'])
	for model in models:
		assert Path(model['filename']).name == model['filename'] and model['filename'].endswith('.vmdl')
		assert len(model['mesh_files']) == len(set(model['mesh_files'])) == len(model['lods']) == 3
		assert [lod['lod'] for lod in model['lods']] == [0, 1, 2]
		assert model['lods'][0]['triangles'] > model['lods'][1]['triangles'] > model['lods'][2]['triangles'] > 0
		for lod, filename in zip(model['lods'], model['mesh_files']):
			assert Path(filename).name == filename and filename.endswith('.fbx') and filename in manifest['files']
			assert lod['triangles'] <= MAX_LOD_TRIANGLES
			if 'native_triangle_budget' in lod:assert lod['triangles'] <= lod['native_triangle_budget']
			assert all(math.isfinite(x) for corner in lod['bounds_inches'] for x in corner)
		assert model['filename'] in manifest['files']
	for level in range(3):assert sum(model['lods'][level]['triangles'] for model in models) == counts[level]
	solid = [p for p in manifest['collision'] if p['role'] == 'solid_trunk']
	assert len(solid) == 9
	assert all(p['radius'] > 0 for p in manifest['collision'])
	model = f'models/tree_lab/{key}/{key}.vmdl'
	trunk = game_object(key, 'trunk', 'Solid trunk', [component(key, 'trunk-collider',
		'ModelCollider', Model=model, IsTrigger=False, Static=True)], tags='tree_trunk')
	# Runtime collision is trunk-only; branch proxies remain in authoring metadata.
	canopy = [game_object(key, f'canopy-{index}', f'Canopy {index + 1}',
		[component(key, f'canopy-renderer-{index}', 'ModelRenderer',
			Model=f"models/tree_lab/{key}/{piece['filename']}", LodOverride=None, CreateAttachments=False)])
		for index, piece in enumerate(models[1:])]
	root = game_object(key, 'root', manifest.get('display_name', f"{manifest['species']} - {manifest['stage']}"),
		[component(key, 'renderer', 'ModelRenderer', Model=model, LodOverride=None, CreateAttachments=False)],
		[trunk, *canopy], tags='tree')
	# Authored trees opt into the shared representation when a bake is installed.
	# Runtime validates its source hashes/membership and restores detail if stale.
	if (ROOT / f'Assets/textures/trees/impostors/{key}.json').is_file():
		root['Components'].append({**component(key, 'visual-lod', 'TreeModelLod',
			Specimen=key, DetailReturnMeters=12, DetailExitMeters=16, TransitionSeconds=.35),
			'__type': 'TreeModelLod'})
	prefab = {'RootObject': root, 'ResourceVersion': 2, 'ShowInMenu': False,
		'DontBreakAsTemplate': False, '__references': [], '__version': 2}
	(folder / f'{key}.prefab').write_text(json.dumps(prefab, indent=2) + '\n', encoding='utf-8')
	material_names = [key + '_bark', key + '_fine'] + [key + f'_leaf{i}' for i in range(1 if manifest['species'] == 'Spruce' else 3)]
	files = [folder / name for model in models for name in [model['filename'], *model['mesh_files']]]
	files.append(folder / f'{key}.prefab')
	textures = set()
	for material_name in material_names:
		path = folder / (material_name + '.vmat')
		files.append(path)
		for asset in re.findall(r'Texture\w+\s+"([^"]+)"', path.read_text()):
			assert asset.startswith(f'models/tree_lab/{key}/')
			textures.add(folder / asset.rsplit('/', 1)[1])
	files.extend(sorted(textures))
	assert all(p.is_file() and p.stat().st_size > 0 for p in files)
	assert len(files) == len(set(files)), 'Duplicate export dependency'
	assert set(manifest['files']) == {p.name for p in files if p.suffix != '.prefab'}, 'Manifest dependencies differ'
	manifest['prefab'] = {'bytes': (folder / f'{key}.prefab').stat().st_size,
		'sha256': hashlib.sha256((folder / f'{key}.prefab').read_bytes()).hexdigest(),
		'installer_sha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest()}
	manifest['blocking_trunk_pieces'] = 9
	manifest['nonblocking_branch_triggers'] = 0
	(folder / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
	return folder, files, manifest


def main():
	parser = argparse.ArgumentParser(description=__doc__)
	parser.add_argument('keys', nargs='*')
	parser.add_argument('--install', action='store_true')
	args = parser.parse_args()
	keys = args.keys or sorted(p.parent.name for p in STAGING.glob('*/manifest.json'))
	update_catalog = False
	for key in keys:
		folder, files, manifest = prepare(key)
		update_catalog |= manifest.get('catalog_eligible', True)
		if args.install:
			destination = ROOT / 'Assets/models/tree_lab' / key
			if not manifest.get('catalog_eligible', True):
				catalog_path = destination.parent / 'catalog.json'
				catalog = json.loads(catalog_path.read_text(encoding='utf-8')) if catalog_path.exists() else {}
				if any(entry['Key'] == key for entry in catalog.get('Specimens', ())):
					raise ValueError(f'Evaluation sample cannot replace a catalog asset: {key}')
				previous_path = destination / 'manifest.json'
				if previous_path.exists() and json.loads(previous_path.read_text(encoding='utf-8')).get('catalog_eligible', True):
					raise ValueError(f'Evaluation sample cannot replace a library asset: {key}')
			destination.mkdir(parents=True, exist_ok=True)
			# Incomplete replacement must not retain an old completion marker.
			(destination / 'manifest.json').unlink(missing_ok=True)
			for path in files + [folder / 'manifest.json']:
				# Adding the distant component changes only the prefab/manifest.
				# Replacing identical models and textures would make asset watchers
				# rebuild live resources during that final publication step.
				expected = manifest['prefab'] if path.suffix == '.prefab' else manifest['files'].get(path.name)
				installed = destination / path.name
				if (expected and installed.is_file() and installed.stat().st_size == expected['bytes']
					and hashlib.sha256(installed.read_bytes()).hexdigest() == expected['sha256']):
					continue
				# Asset watchers must never observe a partly copied FBX or manifest.
				pending = destination / (path.name + '.installing')
				shutil.copy2(path, pending)
				pending.replace(destination / path.name)
		print(json.dumps({'specimen': key, 'installed': args.install,
			'triangles': [x['triangles'] for x in manifest['lods']],
			'solid': manifest['blocking_trunk_pieces'], 'triggers': manifest['nonblocking_branch_triggers']}))
	if args.install and update_catalog:
		# The installed library is the one catalog owner; runtime does not maintain
		# a second hardcoded specimen list. Stable path ordering fixes selection.
		library = ROOT / 'Assets/models/tree_lab'
		entries = []
		for path in sorted(library.glob('*/manifest.json')):
			manifest = json.loads(path.read_text(encoding='utf-8'))
			if not manifest.get('catalog_eligible', True):continue
			assert manifest.get('complete') is True
			dependencies = dict(manifest['files'])
			dependencies[path.parent.name + '.prefab'] = manifest['prefab']
			for name, expected in dependencies.items():
				assert Path(name).name == name and name not in ('.', '..')
				asset = path.parent / name
				assert asset.stat().st_size == expected['bytes'], f'Incomplete installed asset: {asset}'
				assert hashlib.sha256(asset.read_bytes()).hexdigest() == expected['sha256'], f'Corrupt installed asset: {asset}'
			label = manifest['label'].split('_')
			entries.append({'Key': path.parent.name, 'Species': manifest['species'],
				'Stage': manifest['stage'], 'Form': manifest.get('form') or '_'.join(label[2:-1]),
				'Seed': manifest['seed'] if manifest.get('seed') is not None else int(label[-1])})
		pending = library / 'catalog.pending.json'
		pending.write_text(json.dumps({'Version': 1, 'Specimens': entries}, indent=2) + '\n', encoding='utf-8')
		pending.replace(library / 'catalog.json')
		print(json.dumps({'catalog_specimens': len(entries)}))


if __name__ == '__main__':
	main()
