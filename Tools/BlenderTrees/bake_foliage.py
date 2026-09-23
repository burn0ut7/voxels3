"""Bake branch-local leaf cards in a visible Blender session.

runpy.run_path(__file__)['start'](asset_key) queues a cancellable native render.
After completion, run this file with <asset_key> --install outside Blender.
Original source FBXs are never replaced. Re-exporting the specimen invalidates
this derived representation; rerun this tool and the native distant atlas bake.
"""

import hashlib
import json
import math
import runpy
import shutil
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PATCHES = 4096
TEMPLATES = 64
TEMPLATE_LEAVES = 32
TILE = 512
COLUMNS = 8
ROWS = 8
BASIS = (((1, 0, 0), (0, 1, 0), (0, 0, 1)),)


def digest(path):
	return hashlib.file_digest(path.open('rb'), 'sha256').hexdigest()


def paths(key):
	assert key and all(c in 'abcdefghijklmnopqrstuvwxyz0123456789_' for c in key)
	return ROOT / 'Assets/models/tree_lab' / key, ROOT / '.codex/tree-foliage-bake' / key


def start(key):
	"""Queue authoring work without blocking the native MCP request."""
	import bpy
	assert not bpy.app.background, 'Use a visible, interactive Blender session'
	assert not bpy.app.is_job_running('RENDER'), 'Another render is active'
	assert not bpy.app.driver_namespace.get('tree_foliage_bake'), 'A foliage bake is active'
	job = FoliageBake(key)
	bpy.app.driver_namespace['tree_foliage_bake'] = job
	bpy.app.timers.register(job.tick, first_interval=0.1)
	return str(job.output / 'status.json')


class FoliageBake:
	def __init__(self, key):
		import bpy
		self.key = key
		self.source, self.output = paths(key)
		self.output.mkdir(parents=True, exist_ok=True)
		self.manifest = json.loads((self.source / 'manifest.json').read_text())
		assert self.manifest['complete']
		self.manifest_hash = digest(self.source / 'manifest.json')
		self.tool_hash = digest(Path(__file__))
		self.started = time.time()
		self.original_scene = bpy.context.window.scene
		self.scene = bpy.data.scenes.new('Baked foliage authoring')
		bpy.context.window.scene = self.scene
		self.parts = []
		self.materials = []
		self.images = []
		self.index = 0
		self.phase = 'import'
		self.render_done = False
		self.cancelled = False
		self.models = [m for m in self.manifest['render_models'] if m['role'] == 'foliage']
		assert len(self.models) == 4, 'This bake preserves the four installed canopy resources'
		self.inputs = {f'canopy{i:02}_lod2.fbx': digest(self.source / f'canopy{i:02}_lod2.fbx') for i in range(4)}
		for name in ['leaf_color.png', 'leaf_opacity.png', 'motion.png']:
			self.inputs[name] = digest(self.source / name)
		for name, sha in self.inputs.items():
			assert sha == self.manifest['files'][name]['sha256'], f'Stale input: {name}'
		self.status()

	def status(self, error=None):
		(self.output / 'status.json').write_text(json.dumps({'phase': self.phase,
			'index': self.index, 'seconds': time.time() - self.started, 'error': error}, indent=2))

	def cleanup(self):
		import bpy
		for handlers, callback in [(bpy.app.handlers.render_complete, self.completed),
			(bpy.app.handlers.render_cancel, self.cancel)]:
			if callback in handlers:
				handlers.remove(callback)
		bpy.context.window.scene = self.original_scene
		for obj in list(self.scene.objects):
			data = obj.data
			kind = obj.type
			bpy.data.objects.remove(obj, do_unlink=True)
			if data and data.users == 0:
				if kind == 'MESH':
					bpy.data.meshes.remove(data)
				elif kind == 'CAMERA':
					bpy.data.cameras.remove(data)
		bpy.data.scenes.remove(self.scene)
		for mat in self.materials:
			if mat.users == 0:
				bpy.data.materials.remove(mat)
		for image in self.images:
			if image.users == 0:
				bpy.data.images.remove(image)
		bpy.app.driver_namespace.pop('tree_foliage_bake', None)

	def completed(self, scene, *args):
		if scene == self.scene:
			self.render_done = True

	def cancel(self, scene, *args):
		if scene == self.scene:
			self.cancelled = True

	def tick(self):
		import bpy
		try:
			if self.cancelled or (self.output / 'cancel').exists():
				if bpy.app.is_job_running('RENDER'):
					return 0.5
				self.phase = 'cancelled'
				self.status()
				self.cleanup()
				return None
			if self.phase == 'import':
				self.read_part(self.index)
				self.index += 1
				if self.index == len(self.models):
					self.phase = 'prepare'
			elif self.phase == 'prepare':
				self.prepare()
				self.phase = 'color'
				self.render('color')
			elif self.phase in ('color', 'normal'):
				if not self.render_done or bpy.app.is_job_running('RENDER'):
					return 0.5
				if self.phase == 'color':
					self.phase = 'normal'
					self.render('normal')
				else:
					self.finish()
					self.phase = 'complete'
					self.status()
					self.cleanup()
					return None
			self.status()
			return 0.1
		except Exception:
			import traceback
			self.phase = 'failed'
			self.status(traceback.format_exc())
			self.cleanup()
			return None

	def read_part(self, index):
		import bpy
		import numpy as np
		before = set(bpy.data.objects)
		# Color.r is an integer branch payload, not display color. Import with
		# the same linear encoding used by the canonical source FBX exporter.
		bpy.ops.import_scene.fbx(filepath=str(self.source / f'canopy{index:02}_lod2.fbx'), colors_type='LINEAR')
		objects = set(bpy.data.objects) - before
		assert len(objects) == 1
		obj = objects.pop()
		mesh = obj.data
		assert all(abs(obj.matrix_world[r][c] - (1 if r == c else 0)) < 1e-5 for r in range(4) for c in range(4))
		v = np.empty((len(mesh.vertices), 3), np.float32)
		mesh.vertices.foreach_get('co', v.ravel())
		indices = np.empty(len(mesh.loops), np.int32)
		mesh.loops.foreach_get('vertex_index', indices)
		uv = np.empty((len(mesh.loops), 2), np.float32)
		mesh.uv_layers[0].data.foreach_get('uv', uv.ravel())
		motion = np.empty_like(uv)
		mesh.uv_layers['TreePivot'].data.foreach_get('uv', motion.ravel())
		axes = np.empty_like(uv)
		mesh.uv_layers['TreeLeafAxis'].data.foreach_get('uv', axes.ravel())
		colors = np.empty((len(mesh.loops), 4), np.float32)
		assert mesh.color_attributes[0].domain == 'CORNER'
		mesh.color_attributes[0].data.foreach_get('color', colors.ravel())
		normals = np.empty((len(mesh.loops), 3), np.float32)
		mesh.corner_normals.foreach_get('vector', normals.ravel())
		materials = np.empty(len(mesh.polygons), np.int32)
		mesh.polygons.foreach_get('material_index', materials)
		assert len(v) % 4 == 0 and len(indices) == len(v) // 4 * 6
		assert np.array_equal(indices.reshape(-1, 6) // 4, np.repeat(np.arange(len(v) // 4)[:, None], 6, axis=1))
		assert np.all(motion.reshape(-1, 6, 2) == motion[::6, None, :]), 'Each blade must have one motion anchor'
		self.parts.append({'vertices': v.reshape(-1, 4, 3), 'indices': indices,
			'uv': uv, 'normals': normals, 'axes': axes[::6].copy(), 'motion': motion[::6].copy(),
			'colors': colors[::6].copy(), 'materials': materials})
		for mat in mesh.materials:
			if mat not in self.materials:
				self.materials.append(mat)
		bpy.data.objects.remove(obj, do_unlink=True)
		bpy.data.meshes.remove(mesh)

	def mesh(self, name, vertices, triangles, uv):
		import bpy
		import numpy as np
		mesh = bpy.data.meshes.new(name)
		mesh.vertices.add(len(vertices))
		mesh.vertices.foreach_set('co', np.asarray(vertices, dtype=np.float32).ravel())
		mesh.loops.add(len(triangles) * 3)
		mesh.loops.foreach_set('vertex_index', np.asarray(triangles, dtype=np.int32).ravel())
		mesh.polygons.add(len(triangles))
		mesh.polygons.foreach_set('loop_start', np.arange(len(triangles), dtype=np.int32) * 3)
		mesh.polygons.foreach_set('loop_total', np.full(len(triangles), 3, np.int32))
		mesh.uv_layers.new(name='UVMap').data.foreach_set('uv', np.asarray(uv, dtype=np.float32).ravel())
		mesh.update()
		obj = bpy.data.objects.new(name, mesh)
		self.scene.collection.objects.link(obj)
		return obj

	def prepare(self):
		import bpy
		import numpy as np
		import heapq
		v = np.concatenate([p['vertices'] for p in self.parts])
		centers = v.mean(axis=1)
		colors = np.concatenate([p['colors'] for p in self.parts])
		motion = np.concatenate([p['motion'] for p in self.parts])
		width, height = self.manifest['motion']['texture_size']
		motion_image = bpy.data.images.load(str(self.source / 'motion.png'), check_existing=False)
		self.images.append(motion_image)
		motion_image.colorspace_settings.name = 'Non-Color'
		motion_image.alpha_mode = 'CHANNEL_PACKED'
		motion_pixels = np.empty(width * height * 4, np.float32)
		motion_image.pixels.foreach_get(motion_pixels)
		motion_pixels = motion_pixels.reshape(-1, 4)
		source_entries = np.floor(motion[:, 0] * width).astype(np.int32) + np.floor(motion[:, 1] * height).astype(np.int32) * width
		assert np.array_equal(source_entries, np.arange(len(v)) + 256), 'Source leaf identity changed'
		position_scale = self.manifest['motion']['position_scale_meters'] * self.manifest['meters_to_inches']
		pivots = (motion_pixels[source_entries, :3] - .5) * position_scale
		# Match the original leaf shader's resting camera-facing blade frame.
		# This captures recognizable leaf surfaces, not edge-on authored blades.
		def decode(encoded):
			xy = encoded * 2 - 1
			n = np.column_stack((xy, 1 - np.abs(xy).sum(axis=1)))
			back = n[:, 2] < 0
			n[back, :2] = (1 - np.abs(n[back, 1::-1])) * np.where(n[back, :2] >= 0, 1, -1)
			return n / np.linalg.norm(n, axis=1)[:, None]
		blade_axis = decode(np.concatenate([p['axes'] for p in self.parts]))
		blade_normal = decode(colors[:, 1:3])
		lean = blade_normal[:, 2] * .6
		blade_normal -= blade_axis * np.sum(blade_normal * blade_axis, axis=1)[:, None]
		blade_normal /= np.linalg.norm(blade_normal, axis=1)[:, None]
		across = np.cross(blade_axis, blade_normal)
		roll = np.mod(motion @ np.array((31.173, 79.731)), 1) * (2 * math.pi)
		target_across = np.column_stack((np.cos(roll), np.sin(roll), np.zeros(len(v))))
		target_axis = np.column_stack((-np.sin(roll) * np.cos(lean), np.cos(roll) * np.cos(lean), np.sin(lean)))
		target_normal = np.column_stack((np.sin(roll) * np.sin(lean), -np.cos(roll) * np.sin(lean), np.cos(lean)))
		offsets = v - pivots[:, None, :]
		v = pivots[:, None, :] + sum(target[:, None, :] * np.sum(offsets * axis[:, None, :], axis=2)[:, :, None]
			for axis, target in ((across, target_across), (blade_axis, target_axis), (blade_normal, target_normal)))
		normals = np.concatenate([p['normals'] for p in self.parts]).reshape(-1, 6, 3)
		normals = sum(target[:, None, :] * np.sum(normals * axis[:, None, :], axis=2)[:, :, None]
			for axis, target in ((across, target_across), (blade_axis, target_axis), (blade_normal, target_normal))).reshape(-1, 3)
		branches = np.rint(colors[:, 0] * 255).astype(np.int32)
		assert np.max(np.abs(colors[:, 0] * 255 - branches)) < 1e-4, 'Non-integer branch payload'
		assert branches.min() >= 0 and branches.max() < self.manifest['motion']['primary_branches']
		groups = []
		serial = 0
		for branch in np.unique(branches):
			ids = np.flatnonzero(branches == branch)
			heapq.heappush(groups, (-float(np.ptp(centers[ids], axis=0).max()), serial, ids))
			serial += 1
		while len(groups) < PATCHES:
			_, _, ids = heapq.heappop(groups)
			axis = np.ptp(centers[ids], axis=0).argmax()
			ids = ids[np.argsort(centers[ids, axis], kind='stable')]
			for half in (ids[:len(ids) // 2], ids[len(ids) // 2:]):
				assert len(half)
				heapq.heappush(groups, (-float(np.ptp(centers[half], axis=0).max()), serial, half))
				serial += 1
		groups = [g[2] for g in sorted(groups, key=lambda g: int(g[2].min()))]
		patch_for_leaf = np.empty(len(v), np.int32)
		self.patches = []
		for index, ids in enumerate(groups):
			patch_for_leaf[ids] = index
			minimum, maximum = v[ids].min(axis=(0, 1)), v[ids].max(axis=(0, 1))
			center = (minimum + maximum) * .5
			# A small square facing card encloses the patch's projected leaves.
			# The source depth remains in the bake, while its center stays in 3D.
			size = float((maximum - minimum)[:2].max() * 1.04)
			representative = ids[np.argmin(np.sum((centers[ids] - center) ** 2, axis=1))]
			entry = 256 + len(v) + index
			assert entry < len(motion_pixels), 'Motion texture has no room for patch centers'
			motion_pixels[entry, :3] = center / position_scale + .5
			motion_pixels[entry, 3] = motion_pixels[source_entries[representative], 3]
			self.patches.append({'center': center.tolist(), 'extent': [size, size, size],
				'branch': int(branches[representative]), 'source_leaves': len(ids),
				'representative': int(representative), 'motion_uv': [(entry % width + .5) / width, (entry // width + .5) / height],
				'occlusion': float(colors[ids, 3].mean())})
		patch_centers = np.array([p['center'] for p in self.patches], np.float32)
		extents = np.array([p['extent'] for p in self.patches], np.float32)
		# Reuse a bounded set of high-resolution cluster photographs. Unique
		# tiny tiles lost blade detail and multiplied texture memory per tree.
		ordered = sorted(range(PATCHES), key=lambda i: (len(groups[i]), i))
		template_patches = [ordered[min(PATCHES - 1, int((i + .5) * PATCHES / TEMPLATES))] for i in range(TEMPLATES)]
		selected, tile_for_leaf = [], []
		self.templates = []
		for tile, patch in enumerate(template_patches):
			ids = groups[patch]
			points = centers[ids]
			chosen = [int(np.argmin(np.sum((points - points.mean(axis=0)) ** 2, axis=1)))]
			distance = np.sum((points - points[chosen[0]]) ** 2, axis=1)
			for _ in range(min(TEMPLATE_LEAVES, len(ids)) - 1):
				distance[chosen] = -1
				next_index = int(distance.argmax())
				chosen.append(next_index)
				distance = np.minimum(distance, np.sum((points - points[next_index]) ** 2, axis=1))
			chosen = ids[chosen]
			selected.extend(chosen.tolist())
			tile_for_leaf.extend([tile] * len(chosen))
			self.templates.append({'patch': patch, 'leaves': chosen.tolist()})
		for rank, patch in enumerate(ordered):
			self.patches[patch]['tile'] = min(TEMPLATES - 1, rank * TEMPLATES // PATCHES)
		selected = np.array(selected, np.int32)
		tile_for_leaf = np.array(tile_for_leaf, np.int32)
		uv = np.concatenate([p['uv'] for p in self.parts])
		indices = []
		offset = 0
		for part in self.parts:
			indices.append(part['indices'] + offset)
			offset += len(part['vertices']) * 4
		indices = np.concatenate(indices).reshape(-1, 3)
		material_ids = np.concatenate([p['materials'] for p in self.parts])
		uv = uv.reshape(-1, 6, 2)[selected].reshape(-1, 2)
		normals = normals.reshape(-1, 6, 3)[selected].reshape(-1, 3)
		indices = (indices.reshape(-1, 2, 3)[selected] % 4 + np.arange(len(selected))[:, None, None] * 4).reshape(-1, 3)
		material_ids = material_ids.reshape(-1, 2)[selected].ravel()
		self.make_materials()
		for view, basis in enumerate(BASIS):
			basis = np.asarray(basis, np.float32)
			widths = (extents[template_patches] @ np.abs(basis).T)[:, :2]
			tiles = np.arange(TEMPLATES)
			xy = np.column_stack((tiles % COLUMNS + .5, tiles // COLUMNS + .5))
			projected = (v[selected] - patch_centers[np.asarray(template_patches)[tile_for_leaf], None, :]) @ basis.T
			projected[:, :, :2] /= widths[tile_for_leaf, None, :]
			projected[:, :, :2] += xy[tile_for_leaf, None, :]
			# Each tile has its own depth; scaling it preserves local occlusion.
			projected[:, :, 2] /= 1000
			obj = self.mesh(f'Atlas view {view}', projected.reshape(-1, 3), indices, uv)
			# Projection packs anisotropic bounds into square tiles. Its scale must
			# not change the original leaf normals captured in the card's frame.
			obj.data.normals_split_custom_set(normals @ basis.T)
			for mat in self.bake_materials:
				obj.data.materials.append(mat)
			obj.data.polygons.foreach_set('material_index', material_ids)
		self.export_cards()
		# Preserve the source's numeric RGBA16 encoding, including the unchanged
		# branch table. save_render would unpremultiply these numeric channels.
		import struct
		import zlib
		assert np.isfinite(motion_pixels).all() and motion_pixels.min() >= 0 and motion_pixels.max() <= 1
		encoded = np.rint(motion_pixels * 65535).astype('>u2').reshape(height, width, 4)
		scanlines = b''.join(b'\x00' + row.tobytes() for row in encoded[::-1])
		png = bytearray(b'\x89PNG\r\n\x1a\n')
		for kind, data in ((b'IHDR', struct.pack('>IIBBBBB', width, height, 16, 6, 0, 0, 0)),
			(b'IDAT', zlib.compress(scanlines, 6)), (b'IEND', b'')):
			png.extend(struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data)))
		(self.output / 'foliage_motion.png').write_bytes(png)
		self.parts.clear()
		camera = bpy.data.cameras.new('Foliage atlas camera')
		camera.type = 'ORTHO'
		camera.ortho_scale = ROWS
		camera.clip_start = .01
		camera.clip_end = 30
		obj = bpy.data.objects.new('Foliage atlas camera', camera)
		self.scene.collection.objects.link(obj)
		obj.location = (COLUMNS * .5, ROWS * .5, 10)
		self.scene.camera = obj
		self.scene.render.engine = 'BLENDER_EEVEE'
		self.scene.eevee.taa_render_samples = 32
		self.scene.eevee.use_shadows = False
		self.scene.render.resolution_x = COLUMNS * TILE
		self.scene.render.resolution_y = ROWS * TILE
		self.scene.render.resolution_percentage = 100
		self.scene.render.film_transparent = True
		self.scene.render.image_settings.file_format = 'PNG'
		self.scene.render.image_settings.color_mode = 'RGBA'
		self.scene.render.image_settings.color_depth = '8'
		self.scene.render.dither_intensity = 0
		self.scene.view_settings.look = 'None'
		bpy.app.handlers.render_complete.append(self.completed)
		bpy.app.handlers.render_cancel.append(self.cancel)

	def make_materials(self):
		import bpy
		color = bpy.data.images.load(str(self.source / 'leaf_color.png'), check_existing=False)
		opacity = bpy.data.images.load(str(self.source / 'leaf_opacity.png'), check_existing=False)
		opacity.colorspace_settings.name = 'Non-Color'
		self.images.extend((color, opacity))
		self.bake_materials = []
		for tint in ((1, 1.08, .82), (.84, .98, .7), (1.17, 1.2, .86)):
			mat = bpy.data.materials.new('Foliage bake emission')
			mat.use_nodes = True
			mat.surface_render_method = 'DITHERED'
			mat.use_transparent_shadow = False
			nodes, links = mat.node_tree.nodes, mat.node_tree.links
			nodes.clear()
			output = nodes.new('ShaderNodeOutputMaterial')
			mix = nodes.new('ShaderNodeMixShader')
			transparent = nodes.new('ShaderNodeBsdfTransparent')
			emission = nodes.new('ShaderNodeEmission')
			emission.name = 'Bake emission'
			texture = nodes.new('ShaderNodeTexImage')
			texture.image = color
			alpha = nodes.new('ShaderNodeTexImage')
			alpha.image = opacity
			clip = nodes.new('ShaderNodeMath')
			clip.operation = 'GREATER_THAN'
			clip.inputs[1].default_value = .35
			multiply = nodes.new('ShaderNodeMixRGB')
			multiply.name = 'Bake color'
			multiply.blend_type = 'MULTIPLY'
			multiply.inputs[0].default_value = 1
			multiply.inputs[2].default_value = (*tint, 1)
			links.new(texture.outputs['Color'], multiply.inputs[1])
			links.new(alpha.outputs['Color'], clip.inputs[0])
			links.new(clip.outputs[0], mix.inputs[0])
			links.new(transparent.outputs[0], mix.inputs[1])
			links.new(emission.outputs[0], mix.inputs[2])
			links.new(mix.outputs[0], output.inputs['Surface'])
			geometry = nodes.new('ShaderNodeNewGeometry')
			# Geometry.Normal is face-forward in the native two-sided shader.
			scale = nodes.new('ShaderNodeVectorMath')
			scale.operation = 'SCALE'
			scale.inputs[3].default_value = .5
			add = nodes.new('ShaderNodeVectorMath')
			add.name = 'Bake normal'
			add.operation = 'ADD'
			add.inputs[1].default_value = (.5, .5, .5)
			links.new(geometry.outputs['Normal'], scale.inputs[0])
			links.new(scale.outputs[0], add.inputs[0])
			self.materials.append(mat)
			self.bake_materials.append(mat)

	def export_cards(self):
		import bpy
		import numpy as np
		exporter = runpy.run_path(str(Path(__file__).with_name('export_sbox.py')))
		material = bpy.data.materials.new(self.key + '_baked')
		self.materials.append(material)
		self.card_models = []
		for piece in range(4):
			vertices, triangles, uv, motion, local, colors = [], [], [], [], [], []
			for patch_id in range(piece * PATCHES // 4, (piece + 1) * PATCHES // 4):
				patch = self.patches[patch_id]
				for view, basis in enumerate(BASIS):
					basis = np.asarray(basis, np.float32)
					center = np.array(patch['center'])
					size = np.array(patch['extent']) @ np.abs(basis).T
					tile = patch['tile']
					base = len(vertices)
					for y in range(3):
						for x in range(3):
							vertices.append(center + basis[0] * ((x / 2 - .5) * size[0]) + basis[1] * ((y / 2 - .5) * size[1]))
						for x in range(2):
							if y == 2:
								continue
							for tri in ((y * 3 + x, y * 3 + x + 1, (y + 1) * 3 + x + 1),
								(y * 3 + x, (y + 1) * 3 + x + 1, (y + 1) * 3 + x)):
								triangles.append(tuple(base + i for i in tri))
								for i in tri:
									s, t = (i % 3) / 2, (i // 3) / 2
									uv.append(((tile % COLUMNS + s) / COLUMNS, (tile // COLUMNS + t) / ROWS))
									motion.append(patch['motion_uv'])
									local.append((s, t))
									colors.append((patch['branch'] / 255, 0, 0, patch['occlusion']))
			obj = self.mesh('Baked canopy', vertices, triangles, uv)
			obj.data.materials.append(material)
			obj.data.uv_layers.new(name='TreePivot').data.foreach_set('uv', np.asarray(motion, np.float32).ravel())
			obj.data.uv_layers.new(name='PatchLocal').data.foreach_set('uv', np.asarray(local, np.float32).ravel())
			color = obj.data.color_attributes.new(name='BranchAndOcclusion', type='FLOAT_COLOR', domain='CORNER')
			color.data.foreach_set('color', np.asarray(colors, np.float32).ravel())
			obj.data.color_attributes.active_color = color
			obj.data.color_attributes.render_color_index = 0
			exporter['select']([obj])
			filename = f'baked_canopy{piece:02}.fbx'
			bpy.ops.export_scene.fbx(filepath=str(self.output / filename), use_selection=True,
				object_types={'MESH'}, global_scale=1, apply_unit_scale=False, apply_scale_options='FBX_SCALE_NONE',
				axis_forward='Y', axis_up='Z', use_mesh_modifiers=True, use_triangles=True,
				mesh_smooth_type='FACE', add_leaf_bones=False, bake_anim=False, path_mode='STRIP',
				use_custom_props=False, colors_type='LINEAR')
			model = dict(self.models[piece])
			model['mesh_files'] = [filename] * 3
			model['leaf_range'] = None
			model['patch_range'] = [piece * PATCHES // 4, (piece + 1) * PATCHES // 4]
			model['lods'] = [{'lod': lod, 'vertices': len(vertices), 'triangles': len(triangles),
				'bounds_inches': [np.min(vertices, axis=0).tolist(), np.max(vertices, axis=0).tolist()]} for lod in range(3)]
			exporter['write_model'](self.output, self.key, [(material.name, self.key + '_baked')], [], model)
			self.card_models.append(model)
			data = obj.data
			bpy.data.objects.remove(obj, do_unlink=True)
			bpy.data.meshes.remove(data)

	def render(self, channel):
		import bpy
		for mat in self.bake_materials:
			nodes, links = mat.node_tree.nodes, mat.node_tree.links
			links.new(nodes['Bake ' + channel].outputs[0], nodes['Bake emission'].inputs['Color'])
		self.scene.view_settings.view_transform = 'Standard' if channel == 'color' else 'Raw'
		self.scene.render.filepath = str(self.output / f'baked_{channel}.png')
		self.render_done = False
		result = bpy.ops.render.render('INVOKE_DEFAULT', write_still=True, scene=self.scene.name)
		assert 'RUNNING_MODAL' in result, result

	def finish(self):
		assert digest(self.source / 'manifest.json') == self.manifest_hash, 'Source changed during bake'
		assert digest(Path(__file__)) == self.tool_hash, 'Bake tool changed during bake'
		for name, sha in self.inputs.items():
			assert digest(self.source / name) == sha, f'Source changed during bake: {name}'
		report = {'version': 1, 'complete': True, 'source_manifest_sha256': self.manifest_hash,
			'inputs': self.inputs, 'tool_sha256': self.tool_hash, 'patches': self.patches,
			'templates': self.templates, 'template_count': TEMPLATES, 'template_leaf_limit': TEMPLATE_LEAVES,
			'patch_count': PATCHES, 'views_per_patch': 1, 'tile_pixels': TILE,
			'atlas_pixels': [COLUMNS * TILE, ROWS * TILE], 'leaf_triangles': PATCHES * 8,
			'render_models': self.card_models, 'seconds': time.time() - self.started}
		names = ['baked_color.png', 'baked_normal.png', 'foliage_motion.png']
		for model in self.card_models:
			names.extend([model['filename'], *set(model['mesh_files'])])
		report['outputs'] = {name: digest(self.output / name) for name in names}
		(self.output / 'bake.json').write_text(json.dumps(report, indent=2) + '\n')


def install(key):
	"""Validate a completed bake, pad tile edges, then publish derived assets."""
	import numpy as np
	from PIL import Image
	from scipy.ndimage import distance_transform_edt
	source, output = paths(key)
	report = json.loads((output / 'bake.json').read_text())
	assert report['complete'] and json.loads((output / 'status.json').read_text())['phase'] == 'complete'
	assert report['tool_sha256'] == digest(Path(__file__)), 'Tool changed since bake'
	assert report['source_manifest_sha256'] == digest(source / 'manifest.json'), 'Source manifest changed since bake'
	for name, sha in report['inputs'].items():
		assert digest(source / name) == sha, f'Stale bake input: {name}'
	for name, sha in report['outputs'].items():
		assert digest(output / name) == sha, f'Incomplete bake output: {name}'
	color = np.array(Image.open(output / 'baked_color.png').convert('RGBA'))
	normal = np.array(Image.open(output / 'baked_normal.png').convert('RGB'))
	assert list(reversed(color.shape[:2])) == report['atlas_pixels']
	opacity = color[:, :, 3].copy()
	# Native texture filtering needs color/normal values outside cutout edges.
	# Pad within each tile only; coverage remains the original render result.
	for tile in range(TEMPLATES):
		x, y = (tile % COLUMNS) * TILE, (ROWS - 1 - tile // COLUMNS) * TILE
		region = np.s_[y:y + TILE, x:x + TILE]
		valid = opacity[region] >= 128
		assert valid.any(), f'Empty patch view: {tile}'
		nearest = distance_transform_edt(~valid, return_distances=False, return_indices=True)
		color[region][~valid, :3] = color[region][nearest[0][~valid], nearest[1][~valid], :3]
		normal[region][~valid] = normal[region][nearest[0][~valid], nearest[1][~valid]]
	Image.fromarray(color[:, :, :3]).save(output / 'foliage_color.png')
	Image.fromarray(normal).save(output / 'foliage_normal.png')
	Image.fromarray(opacity).save(output / 'foliage_opacity.png')
	manifest = json.loads((source / 'manifest.json').read_text())
	height = manifest['motion']['position_scale_meters'] * manifest['meters_to_inches'] / 4
	material_name = key + '_baked.vmat'
	folder = f'models/tree_lab/{key}'
	(output / material_name).write_text('Layer0\n{\n'
		'\tshader "shaders/trees/tree_baked_foliage.shader"\n'
		'\tF_RENDER_BACKFACES 1\n\tF_TREE_MOTION 1\n\tF_ALPHA_TEST 1\n'
		f'\tTextureColor "{folder}/foliage_color.png"\n'
		f'\tTextureNormal "{folder}/foliage_normal.png"\n'
		f'\tTextureTranslucency "{folder}/foliage_opacity.png"\n'
		f'\tTextureMotion "{folder}/foliage_motion.png"\n'
		f'\tg_flTreeHeight {height:.6f}\n\tg_flAlphaTestReference 0.35\n}}\n')
	manifest['render_models'] = [m for m in manifest['render_models'] if m['role'] != 'foliage'] + report['render_models']
	for lod in manifest['lods']:
		active = [m['lods'][lod['lod']] for m in manifest['render_models']]
		lod['vertices'] = sum(m['vertices'] for m in active)
		lod['triangles'] = sum(m['triangles'] for m in active)
		lod['bounds_inches'] = [[min(m['bounds_inches'][0][axis] for m in active) for axis in range(3)],
			[max(m['bounds_inches'][1][axis] for m in active) for axis in range(3)]]
		lod['foliage'] = {'source_leaves': sum(p['source_leaves'] for p in report['patches']),
			'representation': 'baked_branch_patches', 'patches': PATCHES, 'triangles': report['leaf_triangles']}
	manifest['baked_foliage'] = {k: report[k] for k in ('version', 'inputs', 'tool_sha256',
		'patch_count', 'views_per_patch', 'tile_pixels', 'atlas_pixels', 'leaf_triangles', 'template_count', 'template_leaf_limit')}
	manifest['baked_foliage']['metadata'] = 'foliage_bake.json'
	(output / 'foliage_bake.json').write_text(json.dumps(report, indent=2) + '\n')
	names = ['foliage_bake.json', 'foliage_color.png', 'foliage_normal.png', 'foliage_opacity.png', 'foliage_motion.png', material_name]
	for model in report['render_models']:
		names.extend([model['filename'], *set(model['mesh_files'])])
	for name in names:
		path = output / name
		manifest['files'][name] = {'bytes': path.stat().st_size, 'sha256': digest(path)}
	# Copy dependencies before publishing the new manifest. The distant asset
	# rejects mismatched hashes until all four native capture rows are rebuilt.
	for name in names:
		shutil.copyfile(output / name, source / name)
	pending = source / 'manifest.pending.json'
	pending.write_text(json.dumps(manifest, indent=2) + '\n')
	pending.replace(source / 'manifest.json')
	print(json.dumps({'installed': key, 'leaf_triangles': report['leaf_triangles'], 'files': names}))


if __name__ == '__main__':
	import argparse
	parser = argparse.ArgumentParser(description=__doc__)
	parser.add_argument('key')
	parser.add_argument('--install', action='store_true', required=True)
	install(parser.parse_args().key)
