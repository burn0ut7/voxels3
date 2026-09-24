"""Generate one catalog variation through the installed Blender panel and export it.

Run build_entry(index) inside the visible Blender session. Source collections are
saved in individual local .blend libraries; the original authoring file is never
overwritten. Catalog recipes are versioned alongside the canonical generator.
"""

import bpy
import hashlib
import json
import runpy
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PREFIX = 'OAK_STUDY_'


def build_entry(index):
	entries = json.loads((Path(__file__).with_name('catalog_variations.json')).read_text())['specimens']
	entry = entries[index]
	label = f"{entry['species']}_{entry['stage']}_{entry['form']}_{entry['seed']}"
	return build_specimen(entry, ROOT / 'Tools/BlenderTrees/Variants' / label.lower())


def build_revision(key, revision):
	"""Regenerate stored controls into a new immutable source, preserving originals."""
	for value in (key, revision):
		assert value and all(c in 'abcdefghijklmnopqrstuvwxyz0123456789_-' for c in value), 'Invalid source key/revision'
	manifest = json.loads((ROOT / 'Assets/models/tree_lab' / key / 'manifest.json').read_text())
	assert manifest['label'].lower() == key
	recipes = json.loads((Path(__file__).with_name('recipes.json')).read_text())['trees']
	entry = next((item for item in recipes if item['name'] == PREFIX + manifest['label']), None)
	original = ROOT / 'Tools/BlenderTrees/Variants' / key / 'source.blend'
	if entry is None:
		entry = json.loads(original.with_name('recipe.json').read_text())
	if not original.exists():
		original = ROOT / 'Tools/BlenderTrees/tree_library.blend'
	entry = {field: entry[field] for field in ('species', 'stage', 'form', 'seed', 'settings')}
	entry['name'] = manifest.get('display_name', manifest['label'])
	provenance = {'revision': revision, 'original_path': original.relative_to(ROOT).as_posix(),
		'original_sha256': hashlib.sha256(original.read_bytes()).hexdigest()}
	return build_specimen(entry, ROOT / 'Tools/BlenderTrees/Revisions' / revision / key, provenance)


def build_specimen(entry, folder, provenance=None):
	label = f"{entry['species']}_{entry['stage']}_{entry['form']}_{entry['seed']}"
	assert not (folder / 'source.blend').exists(), 'Stored source libraries are immutable; use a new revision'
	assert all(bpy.data.collections.get(PREFIX + label + suffix) is None
		for suffix in ('', '_Guides', '_Collision')), 'Do not overwrite a stored specimen or its guides/proxies'
	window = bpy.context.window
	original_scene = window.scene
	studio = bpy.data.scenes[PREFIX + 'Studio']
	window.scene = studio
	p = studio.tree_lab
	prior = {prop.identifier: getattr(p, prop.identifier)
		for prop in p.bl_rna.properties if prop.identifier != 'rna_type'}
	objects = set(bpy.data.objects)
	previous_collections = set(bpy.data.collections)
	object_state = {obj: (obj.hide_get(), obj.hide_render, obj.select_get(), obj.location.copy(),
		obj.rotation_euler.copy(), obj.rotation_quaternion.copy(), tuple(obj.rotation_axis_angle), obj.scale.copy()) for obj in objects}
	active_object = bpy.context.view_layer.objects.active
	active_tree = studio.get('active_tree')
	meshes = set(bpy.data.meshes)
	materials = set(bpy.data.materials)
	groups = set(bpy.data.node_groups)
	curves = set(bpy.data.curves)
	images = set(bpy.data.images)
	started = time.time()
	try:
		generator = runpy.run_path(str(Path(__file__).with_name('build_oak_studies.py')))
		p.loading = True
		for key in ('species', 'stage', 'form', 'seed'):
			setattr(p, key, entry[key])
		settings = generator['preset_settings'](entry['species'], entry['stage'], entry['form'])
		overrides = entry.get('settings', {})
		assert set(overrides).issubset(generator['TREE_SETTINGS']), 'Unknown growth control'
		settings.update(overrides)
		for key, value in settings.items():
			setattr(p, key, value)
		p.loading = False
		assert bpy.ops.tree_lab.generate() == {'FINISHED'}
		collection = bpy.data.collections[PREFIX + label]
		collection['display_name'] = entry.get('name', label)
		folder.mkdir(parents=True, exist_ok=True)
		collections = {bpy.data.collections[PREFIX + label + suffix] for suffix in ('', '_Guides', '_Collision')}
		bpy.data.libraries.write(str(folder / 'source.blend'), collections, fake_user=True, compress=True)
		recipe = dict(entry, settings=json.loads(collection['settings']),
			generator_sha256=collection['generator_sha256'], label=label)
		if provenance:
			recipe.update(provenance)
		(folder / 'recipe.json').write_text(json.dumps(recipe, indent=2) + '\n', encoding='utf-8')
		assert bpy.ops.tree_lab.export_sbox() == {'FINISHED'}
		report = json.loads((ROOT / '.codex/tree-export' / label.lower() / 'manifest.json').read_text())
		result = dict(recipe, seconds=time.time() - started,
			triangles=[lod['triangles'] for lod in report['lods']],
			source_sha256=hashlib.sha256((folder / 'source.blend').read_bytes()).hexdigest())
		if provenance:
			assert hashlib.sha256((ROOT / provenance['original_path']).read_bytes()).hexdigest() == provenance['original_sha256']
		(folder / 'build.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
		print('CATALOG_VARIANT', json.dumps(result), flush=True)
		return result
	finally:
		# Only this operation's IDs are retired. Other specimens and materials stay.
		for obj in set(bpy.data.objects) - objects:
			bpy.data.objects.remove(obj, do_unlink=True)
		for collection in set(bpy.data.collections) - previous_collections:
			bpy.data.collections.remove(collection)
		for database, before in ((bpy.data.meshes, meshes), (bpy.data.curves, curves),
			(bpy.data.node_groups, groups), (bpy.data.materials, materials), (bpy.data.images, images)):
			for item in set(database) - before:
				if item.users == 0:
					database.remove(item)
		p.loading = True
		for key, value in prior.items():
			if key != 'loading':
				setattr(p, key, value)
		p.loading = prior['loading']
		for obj, (hidden, render_hidden, selected, location, euler, quaternion, axis_angle, scale) in object_state.items():
			obj.hide_set(hidden)
			obj.hide_render = render_hidden
			obj.select_set(selected)
			# Restore the original channels exactly. Matrix decomposition changed
			# zero Euler components into signed -0.0 and invalidated fingerprints.
			obj.location = location
			obj.rotation_euler = euler
			obj.rotation_quaternion = quaternion
			obj.rotation_axis_angle = axis_angle
			obj.scale = scale
		bpy.context.view_layer.objects.active = active_object
		if active_tree is not None:
			studio['active_tree'] = active_tree
		window.scene = original_scene
