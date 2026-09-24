"""Queue the oak collection through the existing visible Tree Growth operators.

Run start() inside the visible Blender Tree Lab session. Growth and source
meshing remain modal/cancellable; the existing native export runs per specimen.
Completed specimens are saved independently and are never regenerated on resume.
"""
import bpy
import hashlib
import json
import runpy
import time
import traceback
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TOOLS = ROOT / 'Tools/BlenderTrees'
PREFIX = 'OAK_STUDY_'


def start(plan_path=None, status_path=None):
	plan_path = Path(plan_path) if plan_path else TOOLS / 'oak_variations.json'
	status_path = Path(status_path) if status_path else ROOT / '.codex/oak-dozen/status.json'
	if bpy.context.scene.tree_lab.growing:
		raise RuntimeError('An authoring job is already running')
	if bpy.app.driver_namespace.get('tree_variants_running'):
		raise RuntimeError('The oak collection is already running')
	plan = json.loads(plan_path.read_text())
	base = json.loads((ROOT / 'Assets/models/tree_lab' / plan['base_specimen'] / 'manifest.json').read_text())
	assert plan['variants'] and len({item['seed'] for item in plan['variants']}) == len(plan['variants'])
	assert base['complete'] and base['species'] == 'Oak'
	queue = iter(plan['variants'])
	state = {'phase': 'next', 'completed': [], 'started': time.time(), 'plan': str(plan_path), 'plan_sha256': hashlib.sha256(plan_path.read_bytes()).hexdigest()}
	status_path.parent.mkdir(parents=True, exist_ok=True)
	bpy.app.driver_namespace['tree_variants_running'] = True

	def record():
		pending = status_path.with_suffix('.pending.json')
		pending.write_text(json.dumps(state, indent=2) + '\n', encoding='utf-8')
		pending.replace(status_path)

	def retire():
		# Remove only the two named specimens created by this job. Reference
		# libraries, studio objects, and other user-created data remain untouched.
		for label in (state['label'], state['label'] + '_Source'):
			prefix = PREFIX + label
			objects = [obj for obj in bpy.data.objects if obj.name.startswith(prefix + '_')]
			collections = [bpy.data.collections.get(prefix + suffix) for suffix in ('', '_Guides', '_Collision')]
			bpy.data.batch_remove(ids=[*objects, *(c for c in collections if c is not None)])
			for database in (bpy.data.meshes, bpy.data.curves, bpy.data.node_groups, bpy.data.materials):
				orphans = [item for item in database if item.name.startswith(prefix + '_') and item.users == 0]
				if orphans:
					bpy.data.batch_remove(ids=orphans)

	def tick():
		try:
			p = bpy.context.scene.tree_lab
			if p.cancel_requested:
				state['phase'] = 'cancelled'
				bpy.app.driver_namespace['tree_variants_running'] = False
				record()
				return None
			if state['phase'] == 'next':
				entry = next(queue, None)
				if entry is None:
					state['phase'] = 'complete'
					bpy.app.driver_namespace['tree_variants_running'] = False
					record()
					return None
				settings = {'overhead_light': False, **base['source_settings'], **entry['settings'], 'seed': entry['seed']}
				key = f"oak_growth_{settings['age']}_{settings['form']}_{entry['seed']}_dense".lower()
				label = f"Growth_Oak_{settings['age']}_{settings['form']}_{entry['seed']}"
				state.update(key=key, label=label, name=entry['name'], specimen_started=time.time())
				folder = TOOLS / 'Variants' / key
				manifest_path = ROOT / '.codex/tree-export' / key / 'manifest.json'
				if manifest_path.exists():
					manifest = json.loads(manifest_path.read_text())
					assert manifest['complete'] and manifest['seed'] == entry['seed']
					assert (folder / 'source.blend').is_file() and (folder / 'recipe.tree.json').is_file()
					for name, expected in manifest['files'].items():
						assert hashlib.sha256((manifest_path.parent / name).read_bytes()).hexdigest() == expected['sha256']
					state['completed'].append(key)
					record()
					return .1
				assert not folder.exists(), f'Preserve existing incomplete source for review: {folder}'
				existing = bpy.data.collections.get(PREFIX + label)
				assert existing is None or (existing.get('growth_preview') and not existing.get('protected_reference')), 'Existing source specimen must be preserved'
				p.loading = True
				try:
					for name, value in settings.items():
						setattr(p, name, value)
				finally:
					p.loading = False
				if existing is not None:
					module = runpy.run_path(str(TOOLS / 'build_oak_studies.py'))['growth_module']()
					graph = json.loads(existing['growth_graph'])
					module.validate(graph)
					recipe = module.Recipe(**{key: getattr(p, key) for key in module.Recipe.__dataclass_fields__})
					if module.Recipe(**graph['recipe']) != recipe:
						raise ValueError('Existing growth preview differs from the queued recipe')
					p.active_tree = label
					assert bpy.ops.tree_lab.generate('INVOKE_DEFAULT') == {'RUNNING_MODAL'}
					state['phase'] = 'meshing'
				else:
					assert bpy.ops.tree_lab.grow('INVOKE_DEFAULT') == {'RUNNING_MODAL'}
					state['phase'] = 'growing'
			elif state['phase'] == 'growing' and not p.growing:
				assert p.active_tree == state['label'] and bpy.data.collections[PREFIX + p.active_tree].get('growth_preview'), p.progress
				assert bpy.ops.tree_lab.generate('INVOKE_DEFAULT') == {'RUNNING_MODAL'}
				state['phase'] = 'meshing'
			elif state['phase'] == 'meshing' and not p.growing:
				assert p.active_tree == state['label'] + '_Source', p.progress
				collection = bpy.data.collections[PREFIX + p.active_tree]
				assert collection.get('source_ready') and collection.get('surface_method') == 'solid_union'
				collection['display_name'] = state['name']
				folder = TOOLS / 'Variants' / state['key']
				folder.mkdir(parents=True)
				assert bpy.ops.tree_lab.growth_file(filepath=str(folder / 'recipe.tree.json')) == {'FINISHED'}
				collections = {bpy.data.collections[PREFIX + p.active_tree + suffix] for suffix in ('', '_Guides', '_Collision')}
				bpy.data.libraries.write(str(folder / 'source.blend'), collections, fake_user=True, compress=True)
				state['leaves'] = int(bpy.data.objects[PREFIX + p.active_tree + '_Leaves']['leaf_count'])
				state['bark_candidate_fallbacks'] = int(bpy.data.objects[PREFIX + p.active_tree + '_Leaves'].get('bark_candidate_fallbacks', 0))
				state['embedded_bark_attachments'] = int(bpy.data.objects[PREFIX + p.active_tree + '_Leaves'].get('embedded_bark_attachments', 0))
				state['phase'] = 'exporting'
				record()
				return .5
			elif state['phase'] == 'exporting':
				exporter = runpy.run_path(str(TOOLS / 'export_sbox.py'))
				with (status_path.parent / (state['key'] + '.log')).open('w', encoding='utf-8') as log:
					import contextlib
					with contextlib.redirect_stdout(log):
						manifest = exporter['export_specimen'](state['label'] + '_Source', asset_key=state['key'], catalog_eligible=False)
				assert manifest['complete']
				state['completed'].append(state['key'])
				state['last_seconds'] = time.time() - state['specimen_started']
				retire()
				state['phase'] = 'next'
			state['progress'] = p.progress
			record()
			return 1.0
		except Exception:
			state['phase'] = 'failed'
			state['error'] = traceback.format_exc()
			bpy.app.driver_namespace['tree_variants_running'] = False
			record()
			return None

	bpy.context.scene.tree_lab.cancel_requested = False
	bpy.app.timers.register(tick, first_interval=.1)
	record()
