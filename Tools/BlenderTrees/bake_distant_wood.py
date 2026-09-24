"""Derive a small opaque distant wood mesh in the visible Blender session.

runpy.run_path(__file__)['start'](key) queues the bake. Run this script with
<key> --install outside Blender to publish the validated derived resources.
"""
import hashlib
import json
import re
import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
BUDGETS = {'Trunk': 160}


def digest(path):
	return hashlib.sha256(path.read_bytes()).hexdigest()


def start(key):
	import bpy
	assert not bpy.app.background, 'Use a visible, interactive Blender session'
	assert re.fullmatch(r'[a-z0-9_]{1,100}', key)
	output = ROOT / '.codex/tree-distant-wood' / key
	output.mkdir(parents=True, exist_ok=True)
	def bake():
		import traceback
		import runpy
		original = bpy.context.window.scene
		scene = bpy.data.scenes.new('Distant wood authoring')
		bpy.context.window.scene = scene
		try:
			source = ROOT / 'Assets/models/tree_lab' / key
			manifest = json.loads((source / 'manifest.json').read_text())
			wood = [m for m in manifest['render_models'] if m['role'] == 'wood']
			assert len(wood) == 1
			filename = wood[0]['mesh_files'][2]
			input_hash = digest(source / filename)
			assert input_hash == manifest['files'][filename]['sha256']
			tool_hash = digest(Path(__file__))
			bpy.ops.import_scene.fbx(filepath=str(source / filename))
			exporter = runpy.run_path(str(Path(__file__).with_name('export_sbox.py')))
			parts = []
			counts = {}
			for obj in list(scene.objects):
				part = obj.name.split('.')[0]
				if obj.type != 'MESH' or part not in BUDGETS:
					continue
				for attempt in range(3):
					exporter['simplify'](obj, BUDGETS[part] - 4)
					counts[part] = sum(len(p.vertices) - 2 for p in obj.data.polygons)
					if counts[part] <= BUDGETS[part]:
						break
				assert counts[part] <= BUDGETS[part], (part, counts[part], BUDGETS[part])
				parts.append(obj)
			assert set(counts) == set(BUDGETS), counts
			exporter['select'](parts)
			bpy.ops.object.join()
			obj = bpy.context.object
			assert len(obj.data.materials) == 1, 'Structural wood must share bark material'
			material_name = obj.data.materials[0].name
			bpy.ops.export_scene.fbx(filepath=str(output / 'distant_wood.fbx'), use_selection=True,
				object_types={'MESH'}, global_scale=1, apply_unit_scale=False, apply_scale_options='FBX_SCALE_NONE',
				axis_forward='Y', axis_up='Z', use_mesh_modifiers=True, use_triangles=True,
				mesh_smooth_type='FACE', add_leaf_bones=False, bake_anim=False, path_mode='STRIP',
				use_custom_props=False, colors_type='LINEAR')
			model = {'rootNode': {'_class': 'RootNode', 'children': [
				{'_class': 'RenderMeshList', 'children': [{'_class': 'RenderMeshFile',
					'name': 'LOD0', 'filename': f'models/tree_lab/{key}/distant_wood.fbx',
					'import_scale': .01, 'import_rotation': [0.0, -90.0, 0.0]}]},
				{'_class': 'LODGroupList', 'children': [{'_class': 'LODGroup', 'switch_threshold': 0.0, 'meshes': ['LOD0']}]},
				{'_class': 'MaterialGroupList', 'children': [{'_class': 'DefaultMaterialGroup',
					'remaps': [{'from': material_name + '.vmat', 'to': f'models/tree_lab/{key}/{key}_bark.vmat'}],
					'use_global_default': False}]}]}}
			header = '<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:modeldoc30:version{8c2d7a91-9c42-4bf0-883a-5a3b1762d4f1} -->\n'
			(output / 'distant_wood.vmdl').write_text(header + exporter['kv'](model) + '\n')
			assert digest(source / filename) == input_hash and digest(Path(__file__)) == tool_hash
			report = {'Version': 1, 'Specimen': key, 'SourceFile': filename, 'SourceSHA256': input_hash,
				'ToolSHA256': tool_hash, 'TriangleBudgets': BUDGETS, 'Triangles': counts,
				'OmittedParts': ['Roots', 'Branches', 'RootTips', 'Twigs'], 'Outputs': {name: digest(output / name)
					for name in ['distant_wood.fbx', 'distant_wood.vmdl']}}
			(output / 'distant_wood.json').write_text(json.dumps(report, indent=2) + '\n')
			(output / 'status.json').write_text(json.dumps({'complete': True, 'triangles': counts}))
		except Exception:
			(output / 'status.json').write_text(json.dumps({'complete': False, 'error': traceback.format_exc()}))
		finally:
			bpy.context.window.scene = original
			for obj in list(scene.objects):
				data = obj.data
				kind = obj.type
				bpy.data.objects.remove(obj, do_unlink=True)
				if kind == 'MESH' and data.users == 0:
					bpy.data.meshes.remove(data)
			bpy.data.scenes.remove(scene)
		return None
	bpy.app.timers.register(bake, first_interval=.1)
	return str(output / 'status.json')


def install(key):
	assert re.fullmatch(r'[a-z0-9_]{1,100}', key)
	output = ROOT / '.codex/tree-distant-wood' / key
	source = ROOT / 'Assets/models/tree_lab' / key
	report = json.loads((output / 'distant_wood.json').read_text())
	assert report['Specimen'] == key and report['ToolSHA256'] == digest(Path(__file__))
	assert report['SourceSHA256'] == digest(source / report['SourceFile'])
	assert all(digest(output / name) == expected for name, expected in report['Outputs'].items())
	for name in [*report['Outputs'], 'distant_wood.json']:
		shutil.copyfile(output / name, source / name)
	print('Installed', key, sum(report['Triangles'].values()), 'wood triangles')


if __name__ == '__main__':
	import argparse
	parser = argparse.ArgumentParser(description=__doc__)
	parser.add_argument('key')
	parser.add_argument('--install', action='store_true', required=True)
	install(parser.parse_args().key)
