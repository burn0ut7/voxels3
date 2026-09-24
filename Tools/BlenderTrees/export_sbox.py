"""Derive animated s&box models from stored Tree Lab specimens inside Blender.

Run export_specimen(label) in the native Blender session. All work uses a
temporary scene; the authoring objects, guides, materials and .blend stay intact.
Outputs are staged outside Assets until install_exports() is explicitly called.
"""

import bpy
import bmesh
import json
import math
import hashlib
import shutil
import time
import runpy
from pathlib import Path
from mathutils import Vector, Euler
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
STAGING = ROOT / '.codex/tree-export'
PREFIX = 'OAK_STUDY_'
UNITS = 1 / .0254
# Blender FBX_SCALE_NONE writes a further x100 into object transforms, even
# with apply_unit_scale=False. ModelDoc imports those raw transformed values.
FBX_IMPORT_SCALE = .01
# Native26.09.15 import crashes at a saturated22-bit mesh table. Keep at most
# 4.05million triangle corners per LOD, below its4,194,303-entry sentinel.
# Leaf coverage stays fixed; only the number of subdivisions within a blade varies.
MAX_LOD_TRIANGLES = runpy.run_path(str(Path(__file__).with_name('native_limits.py')))['MAX_LOD_TRIANGLES']
LABELS = [f'{species}_{stage}_Open_Grown_{seed}'
	for species, seed in [('Oak', 1701), ('Ash', 2701), ('Spruce', 3701), ('Birch', 4701)]
	for stage in ('Juvenile', 'Mature')] + ['Oak_Large_Open_Grown_1701']
PARTS = ('Trunk', 'Branches', 'Roots', 'RootTips', 'Twigs')
SUPPORTED_SURFACE_METHODS = ('solid_union',)


def select(objects, active=None):
	for obj in bpy.context.selected_objects:
		obj.select_set(False)
	for obj in objects:
		obj.hide_set(False)
		obj.select_set(True)
	bpy.context.view_layer.objects.active = active or objects[-1]


def mesh_object(scene, name, vertices, faces, uv=None, material=None):
	mesh = bpy.data.meshes.new(name)
	mesh.from_pydata(vertices, [], faces)
	mesh.update()
	obj = bpy.data.objects.new(name, mesh)
	scene.collection.objects.link(obj)
	if uv is not None:
		mesh.uv_layers.new(name='UVMap').data.foreach_set('uv', np.asarray(uv).reshape(-1))
	if material:
		mesh.materials.append(material)
	for face in mesh.polygons:
		face.use_smooth = True
	return obj


def simplify(obj, triangle_budget):
	triangles = sum(len(p.vertices) - 2 for p in obj.data.polygons)
	if triangles <= triangle_budget:
		return
	select([obj])
	modifier = obj.modifiers.new('Export triangle budget', 'DECIMATE')
	modifier.ratio = triangle_budget / triangles
	modifier.use_collapse_triangulate = True
	bpy.ops.object.modifier_apply(modifier=modifier.name)


def split_parts(scene, wood):
	# Keep the normals calculated across the continuous wood surface, even after
	# exporting its semantic parts as separate meshes.
	normals = np.empty(len(wood.data.loops) * 3, np.float32)
	wood.data.corner_normals.foreach_get('vector', normals)
	attribute = wood.data.attributes.new('export_normal', 'FLOAT_VECTOR', 'CORNER')
	attribute.data.foreach_set('vector', normals)
	objects = []
	for index, part in enumerate(PARTS):
		obj = wood.copy()
		obj.data = wood.data.copy()
		obj.name = part
		scene.collection.objects.link(obj)
		bm = bmesh.new()
		bm.from_mesh(obj.data)
		layer = bm.faces.layers.int['export_part']
		bmesh.ops.delete(bm, geom=[face for face in bm.faces if face[layer] != index], context='FACES')
		bm.to_mesh(obj.data)
		bm.free()
		if len(obj.data.polygons):
			normals = np.empty(len(obj.data.loops) * 3, np.float32)
			obj.data.attributes['export_normal'].data.foreach_get('vector', normals)
			obj.data.normals_split_custom_set(normals.reshape(-1, 3))
			obj.data.attributes.remove(obj.data.attributes['export_normal'])
			objects.append(obj)
		else:
			mesh = obj.data
			bpy.data.objects.remove(obj, do_unlink=True)
			bpy.data.meshes.remove(mesh)
	return objects


def dummy_material(name):
	mat = bpy.data.materials.new(name)
	mat.use_nodes = True
	return mat


def isolate_lod(scene, wood, lod):
	obj = wood.copy()
	obj.data = wood.data.copy()
	obj.name = 'StructuralWood'
	scene.collection.objects.link(obj)
	bm = bmesh.new()
	bm.from_mesh(obj.data)
	layer = bm.faces.layers.int['export_lod']
	bmesh.ops.delete(bm, geom=[face for face in bm.faces if face[layer] != lod], context='FACES')
	bm.to_mesh(obj.data)
	bm.free()
	assert len(obj.data.polygons), 'Missing baked wood LOD'
	return obj


def unwrap_and_bake(scene, lows, high, material, folder, prefix, size):
	select(lows)
	bpy.ops.object.join()
	wood = bpy.context.object
	wood.data.materials.clear()
	wood.data.materials.append(material)
	for polygon in wood.data.polygons:
		polygon.material_index = 0
	# UV0 owns the unique bake. UV1 retains Blender's branch-aligned grain at
	# full tile resolution, so small limbs do not depend on tiny atlas islands.
	detail_uv = np.empty(len(wood.data.loops) * 2, np.float32)
	wood.data.uv_layers['UVMap'].data.foreach_get('uv', detail_uv)
	weights = np.empty(len(wood.data.loops), np.float32)
	wood.data.attributes['bark_parent_weight'].data.foreach_get('value', weights)
	assert np.isfinite(detail_uv).all() and np.isfinite(weights).all(), 'Invalid bark coordinates or collar weights'
	# The trunk and child charts can disagree at the same welded vertex. A
	# corner-only mask exposes triangle-shaped chart changes at their seam.
	# Share a soft collar across the surface, keeping the unique bake below it.
	vertices = np.empty(len(wood.data.loops), np.int32)
	wood.data.loops.foreach_get('vertex_index', vertices)
	mask = np.ones(len(wood.data.vertices), np.float32)
	np.minimum.at(mask, vertices, 1 - np.clip(weights, 0, 1))
	core = mask.copy()
	edges = np.empty(len(wood.data.edges) * 2, np.int32)
	wood.data.edges.foreach_get('vertices', edges)
	edges = edges.reshape(-1, 2)
	for _ in range(2):
		expanded = mask.copy()
		np.minimum.at(expanded, edges[:, 0], mask[edges[:, 1]])
		np.minimum.at(expanded, edges[:, 1], mask[edges[:, 0]])
		mask = expanded
	degree = np.ones(len(mask), np.float32)
	np.add.at(degree, edges.reshape(-1), 1)
	for _ in range(3):
		smooth = mask.copy()
		np.add.at(smooth, edges[:, 0], mask[edges[:, 1]])
		np.add.at(smooth, edges[:, 1], mask[edges[:, 0]])
		mask = np.minimum(core, smooth / degree)
	colors = np.ones((len(weights), 4), np.float32)
	colors[:, 3] = mask[vertices]
	color = wood.data.color_attributes.new(name='BarkDetailMask', type='FLOAT_COLOR', domain='CORNER')
	color.data.foreach_set('color', colors.reshape(-1))
	wood.data.color_attributes.active_color = color
	wood.data.color_attributes.render_color_index = 0
	for layer in list(wood.data.uv_layers):
		wood.data.uv_layers.remove(layer)
	wood.data.uv_layers.new(name='UVMap')
	select([wood])
	bpy.ops.object.mode_set(mode='EDIT')
	bpy.ops.mesh.select_all(action='SELECT')
	bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=.0005, area_weight=1)
	bpy.ops.object.mode_set(mode='OBJECT')
	wood.data.uv_layers.new(name='BarkDetail').data.foreach_set('uv', detail_uv)
	wood.data.uv_layers.active_index = 0
	wood.data.uv_layers[0].active_render = True
	for kind, suffix in [('DIFFUSE', 'color'), ('NORMAL', 'normal'), ('ROUGHNESS', 'roughness')]:
		bake(scene, high, wood, folder / f'{prefix}_{suffix}.png', kind, size)
	return wood


def fine_mesh(scene, source, lod, budget, material):
	"""Resample closed source tubes instead of collapsing them into thin shards."""
	mesh = source.data
	positions = np.empty(len(mesh.vertices) * 3, np.float32)
	mesh.vertices.foreach_get('co', positions)
	positions = positions.reshape(-1, 3)
	loops = np.empty(len(mesh.loops), np.int32)
	mesh.loops.foreach_get('vertex_index', loops)
	starts = np.empty(len(mesh.polygons), np.int32)
	mesh.polygons.foreach_get('loop_start', starts)
	minimum = np.minimum.reduceat(loops, starts)
	maximum = np.maximum.reduceat(loops, starts)
	boundaries = np.r_[0, np.where(minimum[1:] > np.maximum.accumulate(maximum)[:-1])[0] + 1, len(starts)]
	sweeps = []
	for first, end in zip(boundaries[:-1], boundaries[1:]):
		sides = mesh.polygons[int(end - 1)].loop_total
		points = positions[minimum[first]:maximum[end - 1] + 1]
		assert len(points) % sides == 0 and len(points) >= sides * 2, 'Invalid source tube'
		rings = points.reshape(-1, sides, 3)
		centers = rings.mean(axis=1)
		radii = np.linalg.norm(rings - centers[:, None], axis=2).mean(axis=1)
		lengths = np.linalg.norm(np.diff(centers, axis=0), axis=1)
		score = float(lengths.sum() * radii.mean())
		phase = float(mesh.uv_layers.active.data[int(starts[first])].uv.y)
		sweeps.append((score, rings, centers, radii, lengths, phase))
	sweeps.sort(key=lambda item: -item[0])
	vertices, faces, uvs, caps = [], [], [], []
	used = 0
	for _, rings, centers, radii, lengths, phase in sweeps:
		chosen = [0, len(rings) - 1]
		# Retain bends and radius changes. Blended attachments begin almost at a
		# point then widen; centerline-only sampling would erase that middle flare.
		for _ in range((7, 5, 3)[lod] - 2):
			best_error, best_index = 0.0, None
			for a, b in zip(chosen[:-1], chosen[1:]):
				if b - a <= 1:
					continue
				line = centers[b] - centers[a]
				delta = centers[a + 1:b] - centers[a]
				t = np.clip(delta @ line / max(float(line @ line), 1e-12), 0, 1)
				errors = np.maximum(np.linalg.norm(delta - t[:, None] * line, axis=1),
					np.abs(radii[a + 1:b] - (radii[a] * (1 - t) + radii[b] * t)))
				index = int(np.argmax(errors))
				if errors[index] > best_error:
					best_error, best_index = float(errors[index]), a + 1 + index
			if best_index is None or best_error < max(.0015, float(radii.max()) * .15):
				break
			chosen.append(best_index)
			chosen.sort()
		# Tiny cylindrical shoots need fewer radial samples than major tips.
		# Spend the saved vertices on more leaf-bearing branches, not on six
		# sides around sub-pixel twigs while omitting whole attachments.
		radial_budget = 3 if radii.max() < .012 else 4 if radii.max() < .03 else (6, 5, 4)[lod]
		sides = min(rings.shape[1], radial_budget)
		cost = (len(chosen) - 1) * sides * 2 + (sides - 2) * 2
		if used + cost > budget:
			continue
		used += cost
		start = len(vertices)
		v = np.r_[phase, phase + np.cumsum(lengths / np.maximum(math.tau * (radii[:-1] + radii[1:]) * .5, .001))]
		for ring in chosen:
			axis = rings[ring, 0] - centers[ring]
			axis /= max(float(np.linalg.norm(axis)), 1e-9)
			tangent = centers[min(ring + 1, len(rings) - 1)] - centers[max(0, ring - 1)]
			tangent /= max(float(np.linalg.norm(tangent)), 1e-9)
			across = np.cross(tangent, axis)
			across /= max(float(np.linalg.norm(across)), 1e-9)
			for j in range(sides):
				angle = math.tau * j / sides
				vertices.append(centers[ring] + radii[ring] * (axis * math.cos(angle) + across * math.sin(angle)))
		for row in range(1, len(chosen)):
			for j in range(sides):
				k = (j + 1) % sides
				faces.append((start + (row - 1) * sides + j, start + (row - 1) * sides + k,
					start + row * sides + k, start + row * sides + j))
				uvs.extend(((j / sides, v[chosen[row - 1]]), ((j + 1) / sides, v[chosen[row - 1]]),
					((j + 1) / sides, v[chosen[row]]), (j / sides, v[chosen[row]])))
		for row, reverse in ((0, True), (len(chosen) - 1, False)):
			order = list(reversed(range(sides))) if reverse else list(range(sides))
			caps.append(len(faces))
			faces.append(tuple(start + row * sides + j for j in order))
			uvs.extend((.5 + math.cos(math.tau * j / sides) * .5, .5 + math.sin(math.tau * j / sides) * .5) for j in order)
	obj = mesh_object(scene, '_TREE_EXPORT_Fine', vertices, faces, uvs, material)
	for index in caps:
		obj.data.polygons[index].use_smooth = False
	assert 0 < used <= budget
	return obj


def fine_textures(scene, species, folder, source_material):
	"""Retain the parent bark palette on smaller limbs, with gentler relief."""
	material = source_material.copy()
	material.name = '_TREE_EXPORT_FineBark'
	nodes, links = material.node_tree.nodes, material.node_tree.links
	uv = nodes.new('ShaderNodeUVMap')
	uv.uv_map = 'UVMap'
	# Follow the authored scan, including separately preserved older sources.
	diffuse = next(node.image for node in nodes if node.type == 'TEX_IMAGE'
		and node.image and Path(node.image.filepath).name.endswith('_diff_4k.jpg'))
	asset = Path(diffuse.filepath).name.removesuffix('_diff_4k.jpg')
	shutil.copy2(ROOT / 'Tools/BlenderTrees/Textures' / f'{asset}_disp_2k.png', folder / 'fine_height.png')
	for node in material.node_tree.nodes:
		if node.type == 'TEX_IMAGE':
			# Retain source palette operations, but bake the raw periodic scan:
			# chart offsets belong to the mesh shader, not inside the tile itself.
			links.new(uv.outputs[0], node.inputs['Vector'])
		if node.type == 'NORMAL_MAP':
			node.inputs['Strength'].default_value = min(.22, node.inputs['Strength'].default_value)
	if species == 'Birch':
		# Embed both UV axes on a torus so the source's object-space procedural
		# marks repeat continuously; keep the sample above its dark root flare.
		separate = nodes.new('ShaderNodeSeparateXYZ')
		links.new(uv.outputs[0], separate.inputs[0])
		def calc(operation, a, b=None):
			node = nodes.new('ShaderNodeMath')
			node.operation = operation
			for index, value in enumerate((a, b)):
				if value is None:
					continue
				if isinstance(value, (int, float)):
					node.inputs[index].default_value = value
				else:
					links.new(value, node.inputs[index])
			return node.outputs[0]
		u = calc('MULTIPLY', separate.outputs['X'], math.tau)
		v = calc('MULTIPLY', separate.outputs['Y'], math.tau)
		radius = calc('ADD', .06, calc('MULTIPLY', .018, calc('COSINE', v)))
		coordinate = nodes.new('ShaderNodeCombineXYZ')
		links.new(calc('MULTIPLY', radius, calc('COSINE', u)), coordinate.inputs['X'])
		links.new(calc('MULTIPLY', radius, calc('SINE', u)), coordinate.inputs['Y'])
		links.new(calc('ADD', 3, calc('MULTIPLY', .018, calc('SINE', v))), coordinate.inputs['Z'])
		for node in list(nodes):
			if node.type == 'TEX_COORD':
				for link in list(node.outputs['Object'].links):
					links.new(coordinate.outputs[0], link.to_socket)
	# Sample above the root flare and retain mature bark on supporting branches.
	# Birch's source graph otherwise interprets a missing radius as a brown bud.
	obj = mesh_object(scene, '_TREE_EXPORT_FineTile', [(0, 0, 3), (1, 0, 3), (1, 1, 3), (0, 1, 3)],
		[(0, 1, 2, 3)], [(0, 0), (1, 0), (1, 1), (0, 1)], material)
	obj.data.uv_layers.new(name='BarkParent').data.foreach_set('uv', (0, 0, 1, 0, 1, 1, 0, 1))
	obj.data.attributes.new('bark_parent_weight', 'FLOAT', 'CORNER').data.foreach_set('value', (0, 0, 0, 0))
	obj.data.attributes.new('bark_radius', 'FLOAT', 'POINT').data.foreach_set('value', (.08,) * 4)
	obj.data.uv_layers.active_index = 0
	for kind, suffix in (('DIFFUSE', 'color'), ('NORMAL', 'normal'), ('ROUGHNESS', 'roughness')):
		bake(scene, [], obj, folder / f'fine_{suffix}.png', kind, 2048)
	mesh = obj.data
	bpy.data.objects.remove(obj, do_unlink=True)
	bpy.data.meshes.remove(mesh)
	bpy.data.materials.remove(material)


def bake(scene, high, low, path, kind, size):
	image = bpy.data.images.new('_TREE_EXPORT_' + kind, size, size, alpha=False)
	image.colorspace_settings.name = 'sRGB' if kind == 'DIFFUSE' else 'Non-Color'
	for mat in low.data.materials:
		node = mat.node_tree.nodes.new('ShaderNodeTexImage')
		node.image = image
		mat.node_tree.nodes.active = node
	select(high + [low], low)
	scene.render.bake.use_selected_to_active = bool(high)
	scene.render.bake.use_clear = True
	scene.render.bake.cage_extrusion = .045
	scene.render.bake.max_ray_distance = .09
	scene.render.bake.margin = 12
	scene.render.bake.use_pass_direct = False
	scene.render.bake.use_pass_indirect = False
	scene.render.bake.use_pass_color = True
	scene.render.bake.normal_g = 'POS_Y'  # Matches the existing s&box OpenGL normal assets.
	bpy.ops.object.bake(type=kind)
	image.filepath_raw = str(path)
	image.file_format = 'PNG'
	image.save()
	bpy.data.images.remove(image)
	print('BAKED', path.name, flush=True)


def foliage_textures(scene, source, species, stage, folder):
	if species == 'Oak':
		image = bpy.data.images.load(str(ROOT / 'Assets/textures/trees/oak_leaf_atlas.png'), check_existing=True)
		pixels = np.empty(len(image.pixels), np.float32)
		image.pixels.foreach_get(pixels)
		pixels = pixels.reshape(-1, 4)
		# Save through Blender so color management and alpha separation are explicit.
		for suffix in ('color', 'opacity'):
			output = bpy.data.images.new('_TREE_EXPORT_Oak_' + suffix, *image.size, alpha=False)
			if suffix == 'opacity':
				output.colorspace_settings.name = 'Non-Color'
				data = np.ones_like(pixels)
				data[:, :3] = pixels[:, 3:4]
			else:
				data = pixels.copy()
				data[:, 3] = 1
			output.pixels.foreach_set(data.reshape(-1))
			output.filepath_raw = str(folder / f'leaf_{suffix}.png')
			output.file_format = 'PNG'
			output.save()
			bpy.data.images.remove(output)
		return
	if species == 'Spruce':
		# Bake a 24-cm twig from the actual native needle spray prototypes.
		prototype = bpy.data.objects.get(source.data.materials[0].name + '_NeedleSpray_0')
		if prototype is None:
			# A juvenile stores individual needles rather than the mature spray.
			# Append only the protected native spray dependency, never the library scene.
			name = PREFIX + 'Spruce_Mature_Leaf_0_NeedleSpray_0'
			with bpy.data.libraries.load(str(ROOT / 'Tools/BlenderTrees/tree_library.blend'), link=False) as (available, loaded):
				assert name in available.objects, 'Stored spruce needle spray is missing'
				loaded.objects = [name]
			prototype = loaded.objects[0]
			scene.collection.objects.link(prototype)
		objects = []
		for i in range(13):
			obj = prototype.copy()
			obj.name = '_TREE_EXPORT_NeedlePatch'
			obj.hide_render = False
			obj.location = (math.sin(i * .4) * .008, 0, (i - 6) * .017)
			obj.rotation_euler = (0, 0, i * 2.399)
			scene.collection.objects.link(obj)
			objects.append(obj)
		camera_data = bpy.data.cameras.new('_TREE_EXPORT_Camera')
		camera = bpy.data.objects.new('_TREE_EXPORT_Camera', camera_data)
		scene.collection.objects.link(camera)
		camera.location = (0, -.8, 0)
		camera.rotation_euler = (Vector((0, 0, 0)) - camera.location).to_track_quat('-Z', 'Y').to_euler()
		camera_data.type = 'ORTHO'
		camera_data.ortho_scale = .26
		scene.camera = camera
		scene.render.resolution_x = scene.render.resolution_y = 512
		scene.render.resolution_percentage = 100
		scene.render.film_transparent = True
		scene.render.image_settings.file_format = 'PNG'
		scene.render.image_settings.color_mode = 'RGBA'
		scene.view_settings.view_transform = 'Standard'
		scene.world = bpy.data.worlds.new('_TREE_EXPORT_World')
		scene.world.use_nodes = True
		background = next(node for node in scene.world.node_tree.nodes if node.type == 'BACKGROUND')
		background.inputs['Color'].default_value = (.8, .8, .8, 1)
		background.inputs['Strength'].default_value = 1
		scene.render.filepath = str(folder / 'leaf_patch.png')
		bpy.ops.render.render(write_still=True)
		image = bpy.data.images.load(str(folder / 'leaf_patch.png'), check_existing=False)
		pixels = np.empty(len(image.pixels), np.float32)
		image.pixels.foreach_get(pixels)
		pixels = pixels.reshape(-1, 4)
		for suffix in ('color', 'opacity'):
			out = bpy.data.images.new('_TREE_EXPORT_Spruce_' + suffix, 512, 512, alpha=False)
			data = pixels.copy()
			if suffix == 'opacity':
				out.colorspace_settings.name = 'Non-Color'
				data[:, :3] = pixels[:, 3:4]
			data[:, 3] = 1
			out.pixels.foreach_set(data.reshape(-1))
			out.filepath_raw = str(folder / f'leaf_{suffix}.png')
			out.file_format = 'PNG'
			out.save()
			bpy.data.images.remove(out)
		bpy.data.images.remove(image)
		for obj in objects + [camera]:
			bpy.data.objects.remove(obj, do_unlink=True)
		return
	# Ash/birch use a curved blade with native procedural veins. Bake its UV
	# material once; actual exported blades retain the source outline and fold.
	mat = source.data.materials[0].copy()
	mat.name = '_TREE_EXPORT_LeafBake'
	obj = mesh_object(scene, '_TREE_EXPORT_LeafBake',
		[(-.05, 0, 0), (.05, 0, 0), (.05, .12, 0), (-.05, .12, 0)],
		[(0, 1, 2, 3)], [(0, 0), (1, 0), (1, 1), (0, 1)], mat)
	for kind, suffix in [('DIFFUSE', 'color'), ('NORMAL', 'normal')]:
		bake(scene, [], obj, folder / f'leaf_{suffix}.png', kind, 512)
	mesh = obj.data
	bpy.data.objects.remove(obj, do_unlink=True)
	bpy.data.meshes.remove(mesh)
	bpy.data.materials.remove(mat)


def write_materials(folder, key, species, height):
	base = f'models/tree_lab/{key}/'
	motion = f'\n\tF_TREE_MOTION 1\n\tTextureMotion "{base}motion.png"\n\tg_flTreeHeight {height * UNITS:.9g}'
	(folder / f'{key}_bark.vmat').write_text('Layer0\n{\n'
		'\tshader "shaders/trees/tree_lab_bark.shader"\n'
		+ '\n'.join(f'\tTexture{parameter} "{base}bark_{suffix}.png"' for parameter, suffix in
			(('Color', 'color'), ('Normal', 'normal'), ('Roughness', 'roughness')))
		+ f'\n\tTextureDetailColor "{base}fine_color.png"\n\tTextureDetailNormal "{base}fine_normal.png"'
		+ f'\n\tTextureDetailHeight "{base}fine_height.png"'
		+ f'\n\tg_flDetailStrength {0.3 if species == "Birch" else 1.0}'
		+ motion + '\n}\n', encoding='utf-8')
	fields = ['shader "shaders/trees/tree_lab_foliage.shader"', 'F_RENDER_BACKFACES 1', f'TextureColor "{base}leaf_color.png"',
		motion.strip()]
	if species != 'Spruce':
		fields.extend(('F_TREE_LEAF_FACING 1', 'g_flTreeLeafMinimumFacing 0.35'))
	if species in ('Oak', 'Spruce'):
		fields.extend(('F_ALPHA_TEST 1', f'TextureTranslucency "{base}leaf_opacity.png"', 'g_flAlphaTestReference 0.35'))
	else:
		fields.append(f'TextureNormal "{base}leaf_normal.png"')
	tints = ((1, 1.08, .82), (.84, .98, .7), (1.17, 1.2, .86)) if species == 'Oak' else ((1, 1, 1),) if species == 'Spruce' else ((1, 1, 1), (1.153, 1.153, 1.153), (1.306, 1.306, 1.306))
	for index, tint in enumerate(tints):
		values = fields + ['g_flTintColor "[' + ' '.join(str(x) for x in tint) + ']"']
		(folder / f'{key}_leaf{index}.vmat').write_text('Layer0\n{\n\t' + '\n\t'.join(values) + '\n}\n', encoding='utf-8')
	(folder / f'{key}_fine.vmat').write_text('Layer0\n{\n\tshader "shaders/trees/tree_lab_fine.shader"\n'
		+ '\n'.join(f'\tTexture{parameter} "{base}fine_{suffix}.png"' for parameter, suffix in
			(('Color', 'color'), ('Normal', 'normal'), ('Roughness', 'roughness')))
		+ motion + '\n}\n', encoding='utf-8')


def leaf_mesh(scene, source, species, stage, lod, materials, motion, triangle_budget):
	"""Shared leaf selection, retaining each sampled blade's root and outline.

	No random state is consumed here. Every level keeps the same selected blades
	and outline. Lower levels remove the center fold, never canopy coverage.
	"""
	mesh = source.data
	positions = np.empty(len(mesh.vertices) * 3, np.float32)
	mesh.vertices.foreach_get('co', positions)
	positions = positions.reshape(-1, 3)
	if species == 'Spruce':
		return spruce_mesh(scene, source, stage, lod, materials[0], positions, motion)
	if species == 'Oak':
		rows, columns = 2, 3
	elif species == 'Ash':
		rows, columns = (12, 5) if stage == 'Juvenile' else (10, 3)
	else:
		rows, columns = (14, 5) if stage == 'Juvenile' else (18, 5)
	stride = (rows + 1) * columns
	assert len(positions) % stride == 0
	count = len(positions) // stride
	full_detail_leaves = count
	if species == 'Oak' and lod == 0:
		assert triangle_budget >= count * 4, 'Folded full canopy exceeds native import budget'
		full_detail_leaves = min(count, (triangle_budget - count * 4) // 4)
	# Keep authored leaf scale and attachment. Far savings belong to the shared
	# depth impostor, not a sparse canopy of enlarged leaf substitutes.
	indices = np.arange(count)
	compensation = 1.0
	verts, faces, uvs, pivots, normals, axes, leaf_indices = [], [], [], [], [], [], []
	face_materials = []
	source_uv = mesh.uv_layers.active.data if species == 'Oak' else None
	for index in indices:
		blade = positions[index * stride:(index + 1) * stride]
		base = blade[(columns - 1) // 2]
		start = len(verts)
		pivots.append(base)
		if species == 'Oak':
			# Keep the full atlas footprint and raised center. A diamond clips
			# the outer oak lobes even when the alpha texture is correct.
			chosen = list(range(9))
			uv0 = source_uv[int(index) * 16].uv.copy()
			u, v = math.floor(uv0.x * 2 + 1e-5) * .5, math.floor(uv0.y * 2 + 1e-5) * .5
			local_uv = tuple((u + column * .25, v + row * .25) for row in range(3) for column in range(3))
			triangles = tuple(tri for row in range(2) for column in range(2)
				for a in (row * 3 + column,) for tri in ((a, a + 1, a + 4), (a, a + 4, a + 3)))
			# Distribute simpler folds evenly in authoring order when a large
			# specimen would overflow native import. Keep all attachment identities.
			simple_fold = lod == 1 or (lod == 0 and
				(index + 1) * full_detail_leaves // count == index * full_detail_leaves // count)
			if simple_fold:
				chosen = [0, 2, 4, 6, 8]
				local_uv = tuple(local_uv[i] for i in chosen)
				triangles = ((0, 1, 2), (1, 4, 2), (4, 3, 2), (3, 0, 2))
			elif lod == 2:
				chosen = [0, 2, 6, 8]
				local_uv = tuple(local_uv[i] for i in chosen)
				triangles = ((0, 1, 3), (3, 2, 0))
			verts.extend(base + (blade[chosen] - base) * compensation)
			for tri in triangles:
				faces.append(tuple(start + x for x in tri))
				uvs.extend(local_uv[x] for x in tri)
		else:
			# Retain the tapered outline as geometry, avoiding rectangular leaf sheets.
			middle = round(rows * (.43 if species == 'Ash' else .38))
			chosen = [(columns - 1) // 2, middle * columns,
				middle * columns + (columns - 1) // 2, middle * columns + columns - 1,
				rows * columns + (columns - 1) // 2]
			local_uv = ((.5, 0), (0, middle / rows), (.5, middle / rows), (1, middle / rows), (.5, 1))
			triangles = ((0, 2, 1), (0, 3, 2), (1, 2, 4), (2, 3, 4))
			if lod > 0:
				chosen = [chosen[i] for i in (0, 1, 3, 4)]
				local_uv = tuple(local_uv[i] for i in (0, 1, 3, 4))
				triangles = ((0, 3, 1), (0, 2, 3))
			verts.extend(base + (blade[chosen] - base) * compensation)
			for tri in triangles:
				faces.append(tuple(start + x for x in tri))
				uvs.extend(local_uv[x] for x in tri)
		leaf_indices.extend([len(pivots) - 1] * len(chosen))
		face_materials.extend([mesh.polygons[int(index) * rows * (columns - 1)].material_index] * len(triangles))
		middle_row = (rows // 2) * columns
		wide = blade[middle_row + columns - 1] - blade[middle_row]
		long = blade[rows * columns + (columns - 1) // 2] - base
		normal = np.cross(wide, long)
		normals.append(normal / max(np.linalg.norm(normal), 1e-9))
		axes.append(long / max(np.linalg.norm(long), 1e-9))
	obj = mesh_object(scene, '_TREE_EXPORT_Leaves', verts, faces, uvs, materials[0])
	motion.tag_leaves(obj, pivots, leaf_indices, normals, axes)
	for material in materials[1:]:
		obj.data.materials.append(material)
	obj.data.polygons.foreach_set('material_index', np.asarray(face_materials, np.int32))
	obj['source_leaves'] = count
	obj['retained_leaves'] = len(indices)
	obj['coverage_scale'] = compensation
	obj['canopy_selection'] = 'shared_across_lods'
	if species == 'Oak' and lod == 0:
		obj['eight_triangle_leaves'] = int(full_detail_leaves)
		obj['four_triangle_leaves'] = int(count - full_detail_leaves)
	return obj



def foliage_piece(scene, source, first_leaf, last_leaf, texture_size):
	"""Copy whole tagged leaves without changing their global motion payload."""
	mesh = source.data
	width, height = texture_size
	uv = np.empty((len(mesh.loops), 2), np.float32)
	mesh.uv_layers['TreePivot'].data.foreach_get('uv', uv.ravel())
	sizes = np.empty(len(mesh.polygons), np.int32)
	mesh.polygons.foreach_get('loop_total', sizes)
	assert np.all(sizes == 3), 'Foliage partition requires triangle faces'
	ids = (np.floor(uv[::3, 0] * width).astype(np.int32)
		+ np.floor(uv[::3, 1] * height).astype(np.int32) * width - 256)
	assert np.all(np.diff(ids) >= 0), 'Leaf face order changed before partition'
	start, end = np.searchsorted(ids, (first_leaf, last_leaf))
	assert end > start and ids[start] == first_leaf and ids[end - 1] == last_leaf - 1
	assert np.array_equal(np.unique(ids[start:end]), np.arange(first_leaf, last_leaf)), 'Missing leaf in partition'
	loops = slice(int(start) * 3, int(end) * 3)
	indices = np.empty(len(mesh.loops), np.int32)
	mesh.loops.foreach_get('vertex_index', indices)
	used, remapped = np.unique(indices[loops], return_inverse=True)
	positions = np.empty((len(mesh.vertices), 3), np.float32)
	mesh.vertices.foreach_get('co', positions.ravel())
	data = bpy.data.meshes.new('_TREE_EXPORT_FoliagePiece')
	data.vertices.add(len(used)); data.vertices.foreach_set('co', positions[used].ravel())
	data.loops.add(len(remapped)); data.loops.foreach_set('vertex_index', remapped)
	data.polygons.add(int(end - start))
	data.polygons.foreach_set('loop_start', np.arange(end - start, dtype=np.int32) * 3)
	data.polygons.foreach_set('loop_total', np.full(end - start, 3, np.int32))
	data.polygons.foreach_set('use_smooth', np.ones(end - start, bool))
	materials = np.empty(len(mesh.polygons), np.int32)
	mesh.polygons.foreach_get('material_index', materials)
	for material in mesh.materials:data.materials.append(material)
	data.polygons.foreach_set('material_index', materials[start:end])
	data.update(calc_edges=True)
	for layer in mesh.uv_layers:
		layer.data.foreach_get('uv', uv.ravel())
		data.uv_layers.new(name=layer.name).data.foreach_set('uv', uv[loops].ravel())
		check = np.empty((len(remapped), 2), np.float32)
		data.uv_layers[-1].data.foreach_get('uv', check.ravel())
		assert np.array_equal(check, uv[loops]), 'Partition changed UV payload'
	for layer in mesh.color_attributes:
		assert layer.domain == 'CORNER' and layer.data_type == 'FLOAT_COLOR'
		colors = np.empty((len(mesh.loops), 4), np.float32)
		layer.data.foreach_get('color', colors.ravel())
		target = data.color_attributes.new(name=layer.name, type='FLOAT_COLOR', domain='CORNER')
		target.data.foreach_set('color', colors[loops].ravel())
		check = np.empty((len(remapped), 4), np.float32); target.data.foreach_get('color', check.ravel())
		assert np.array_equal(check, colors[loops]), 'Partition changed motion color'
	data.color_attributes.active_color = data.color_attributes[mesh.color_attributes.active_color.name]
	data.color_attributes.render_color_index = mesh.color_attributes.render_color_index
	normals = np.empty((len(mesh.loops), 3), np.float32)
	mesh.corner_normals.foreach_get('vector', normals.ravel())
	data.normals_split_custom_set(normals[loops])
	check = np.empty((len(data.vertices), 3), np.float32); data.vertices.foreach_get('co', check.ravel())
	assert np.array_equal(check, positions[used]), 'Partition moved leaf vertices'
	assert np.array_equal(used[remapped], indices[loops]), 'Partition changed triangles'
	obj = bpy.data.objects.new('Foliage', data); scene.collection.objects.link(obj)
	obj.matrix_world = source.matrix_world.copy()
	obj['retained_leaves'] = last_leaf - first_leaf
	obj['leaf_range'] = [first_leaf, last_leaf]
	return obj


def spruce_mesh(scene, source, stage, lod, material, positions, motion):
	"""Represent short needle-bearing shoot regions with intersecting foliage cards."""
	rotation = np.empty(len(positions) * 3, np.float32)
	source.data.attributes['needle_rotation'].data.foreach_get('vector', rotation)
	rotation = rotation.reshape(-1, 3)
	# Shared needle-bearing regions keep the same crown at every distance.
	# Increasing cell size per LOD enlarged the needles and opened coarse holes.
	cell = .045 if stage == 'Juvenile' else .18
	keys = np.floor(positions / cell).astype(np.int32)
	_, first, inverse, counts = np.unique(keys, axis=0, return_index=True, return_inverse=True, return_counts=True)
	centers = np.zeros((len(first), 3), np.float64)
	np.add.at(centers, inverse, positions)
	centers /= counts[:, None]
	verts, faces, uvs = [], [], []
	for center, index in zip(centers, first):
		rotation_matrix = Euler(rotation[index], 'XYZ').to_matrix()
		axis = rotation_matrix @ Vector((0, 0, 1))
		if stage == 'Juvenile':
			# Needle directions are radial, so a local vertical card avoids inheriting
			# an individual needle's orientation as the entire shoot's axis.
			axis = Vector((0, 0, 1))
		across = axis.cross(Vector((1, 0, 0)))
		if across.length < .01:
			across = axis.cross(Vector((0, 1, 0)))
		across.normalize()
		other = axis.cross(across)
		half = cell * .82 + .02
		center = Vector(center)
		for tangent in (across, other):
			start = len(verts)
			verts.extend(tuple(center + tangent * x * half + axis * y * half) for x, y in ((-1, -1), (1, -1), (1, 1), (-1, 1)))
			faces.append(tuple(start + x for x in range(4)))
			uvs.extend(((0, 0), (1, 0), (1, 1), (0, 1)))
	obj = mesh_object(scene, '_TREE_EXPORT_Leaves', verts, faces, uvs, material)
	motion.tag_leaves(obj, centers, np.repeat(np.arange(len(centers)), 8), np.tile((0, 0, 1), (len(centers), 1)))
	obj['source_points'] = len(positions)
	obj['foliage_cells'] = len(first)
	obj['canopy_selection'] = 'shared_across_lods'
	obj['foliage_cell_meters'] = cell
	return obj


def kv(value):
	if isinstance(value, dict):
		return '{\n' + '\n'.join(f'{key} = {kv(item)}' for key, item in value.items()) + '\n}'
	if isinstance(value, list):
		return '[ ' + ', '.join(kv(item) for item in value) + ' ]'
	return json.dumps(value)


def write_model(folder, key, materials, collision, model):
	meshes = [{'_class': 'RenderMeshFile', 'name': f'LOD{lod}',
		'filename': f"models/tree_lab/{key}/{model['mesh_files'][lod]}", 'import_scale': FBX_IMPORT_SCALE,
		'import_rotation': [0.0, -90.0, 0.0]}
		for lod in range(3)]
	lods = [{'_class': 'LODGroup', 'switch_threshold': threshold, 'meshes': [f'LOD{lod}']}
		for lod, threshold in enumerate((0.0, 20.0, 65.0))]
	shapes = []
	for item in collision:
		if item['role'] != 'solid_trunk':
			continue
		# Nine simple cylinders are derived from the authoring proxy rings.
		shapes.append({'_class': 'PhysicsShapeCylinder', 'name': item['name'],
			'surface_prop': 'wood', 'collision_tags': 'solid', 'radius': item['radius'] * UNITS,
			'point0': [x * UNITS for x in item['point0']], 'point1': [x * UNITS for x in item['point1']]})
	root = {'rootNode': {'_class': 'RootNode', 'children': [
		{'_class': 'RenderMeshList', 'children': meshes},
		{'_class': 'LODGroupList', 'children': lods},
		{'_class': 'PhysicsShapeList', 'children': shapes},
		{'_class': 'MaterialGroupList', 'children': [{'_class': 'DefaultMaterialGroup',
			'remaps': [{'from': actual + '.vmat', 'to': f'models/tree_lab/{key}/{canonical}.vmat'} for actual, canonical in materials],
			'use_global_default': False}]}]}}
	header = '<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:modeldoc30:version{8c2d7a91-9c42-4bf0-883a-5a3b1762d4f1} -->\n'
	(folder / model['filename']).write_text(header + kv(root) + '\n', encoding='utf-8')


def refresh_fine_materials(key):
	"""Rebake the parent-matched twig palette without changing completed geometry."""
	assert key and all(c in 'abcdefghijklmnopqrstuvwxyz0123456789_' for c in key)
	folder = STAGING / key
	manifest_path = folder / 'manifest.json'
	report = json.loads(manifest_path.read_text(encoding='utf-8'))
	assert report.get('complete') is True and report['label'].lower() == key
	for name, expected in report['files'].items():
		assert Path(name).name == name and name not in ('.', '..')
		assert (folder / name).stat().st_size == expected['bytes']
		assert hashlib.sha256((folder / name).read_bytes()).hexdigest() == expected['sha256'], name
	changed = {f'fine_{channel}.png' for channel in ('color', 'normal', 'roughness', 'height')}
	assert changed.issubset(report['files'])
	reference = next(label for label in LABELS if label.startswith(f"{report['species']}_{report['stage']}_"))
	source_material = bpy.data.objects[PREFIX + reference + '_Wood'].data.materials[0]
	exporter_hash = hashlib.sha256(Path(__file__).read_bytes()).hexdigest()
	window = bpy.context.window
	original_scene = window.scene
	scene = bpy.data.scenes.new('_TREE_MATERIAL_REFRESH')
	window.scene = scene
	scene.render.engine = 'CYCLES'
	scene.cycles.samples = 8
	scene.cycles.device = 'GPU'
	manifest_path.replace(folder / f'previous-manifest-{time.time_ns()}.json')
	try:
		fine_textures(scene, report['species'], folder, source_material)
		assert hashlib.sha256(Path(__file__).read_bytes()).hexdigest() == exporter_hash, 'Exporter changed during material refresh'
		for name, expected in report['files'].items():
			actual = {'bytes': (folder / name).stat().st_size,
				'sha256': hashlib.sha256((folder / name).read_bytes()).hexdigest()}
			if name in changed:
				report['files'][name] = actual
			else:
				assert actual == expected, f'Material refresh changed unrelated dependency: {name}'
		report['fine_branches'] = 'closed_source_tubes_with_parent_matched_bark_palette'
		report['material_refresh'] = {'exporter_sha256': exporter_hash, 'palette_reference': reference,
			'changed_dependencies': sorted(changed), 'geometry_unchanged': True}
		pending = folder / 'manifest.pending.json'
		pending.write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
		pending.replace(manifest_path)
		print('MATERIAL_REFRESH', key, flush=True)
	finally:
		window.scene = original_scene
		for obj in list(scene.objects):
			mesh = obj.data if obj.type == 'MESH' else None
			bpy.data.objects.remove(obj, do_unlink=True)
			if mesh and mesh.users == 0:
				bpy.data.meshes.remove(mesh)
		bpy.data.scenes.remove(scene)


def export_specimen(label, texture_size=4096, asset_key=None, catalog_eligible=True):
	started = time.time()
	exporter_hash = hashlib.sha256(Path(__file__).read_bytes()).hexdigest()
	collection = bpy.data.collections[PREFIX + label]
	species, stage = collection['species'], collection['stage']
	key = asset_key or label.lower()
	if not key or any(c not in 'abcdefghijklmnopqrstuvwxyz0123456789_' for c in key):
		raise ValueError('Export asset key must be a safe lowercase directory name')
	connected = collection.get('surface_method') == 'solid_union'
	if collection.get('growth_graph') and not connected:
		raise ValueError('This growth source format is not supported by the exporter')
	if connected and species == 'Spruce':
		raise ValueError('Individual-needle export is not qualified yet; preserve the Blender source')
	leaf_count = int(bpy.data.objects[PREFIX + label + '_Leaves'].get('leaf_count', 0))
	wood_triangles = sum(len(p.vertices) - 2 for p in bpy.data.objects[PREFIX + label + '_Wood'].data.polygons)
	multipart = connected and species == 'Oak' and wood_triangles + leaf_count * 8 > MAX_LOD_TRIANGLES
	if multipart and catalog_eligible:raise ValueError('Multipart trees require evaluation export; population catalog expects one model')
	if connected and wood_triangles > MAX_LOD_TRIANGLES:raise ValueError('Source wood exceeds the native model limit; preserve the source')
	models = [{'filename': key + '.vmdl', 'mesh_files': [f'lod{i}.fbx' for i in range(3)],
		'role': 'wood' if multipart else 'tree', 'leaf_range': None, 'lods': []}]
	if multipart:
		leaves_per_piece = MAX_LOD_TRIANGLES // 8
		for index, first in enumerate(range(0, leaf_count, leaves_per_piece)):
			models.append({'filename': f'{key}_canopy{index:02}.vmdl',
				'mesh_files': [f'canopy{index:02}_lod{i}.fbx' for i in range(3)],
				'role': 'foliage', 'leaf_range': [first, min(first + leaves_per_piece, leaf_count)], 'lods': []})
	folder = STAGING / key
	folder.mkdir(parents=True, exist_ok=True)
	manifest_path = folder / 'manifest.json'
	if manifest_path.exists():
		manifest_path.replace(folder / f'previous-manifest-{time.time_ns()}.json')
	window = bpy.context.window or bpy.context.window_manager.windows[0]
	original_scene = window.scene
	scene = bpy.data.scenes.new('_TREE_EXPORT')
	window.scene = scene
	scene.render.engine = 'CYCLES'
	scene.cycles.samples = 8
	scene.cycles.device = 'GPU'
	scene.unit_settings.system = 'NONE'
	created_objects, created_meshes, created_materials = [], [], []
	try:
		wood_material = dummy_material(key + '_bark')
		fine_material = dummy_material(key + '_fine')
		leaf_materials = [dummy_material(key + f'_leaf{i}') for i in range(1 if species == 'Spruce' else 3)]
		created_materials.extend([wood_material, fine_material] + leaf_materials)
		leaf_source = bpy.data.objects[PREFIX + label + '_Leaves']
		foliage_textures(scene, leaf_source, species, stage, folder)
		uv_source = bpy.data.objects[PREFIX + label + '_UV_Source']
		height = max(v.co.z for v in leaf_source.data.vertices)
		TreeMotion = runpy.run_path(str(Path(__file__).with_name('export_motion.py')))['TreeMotion']
		motion = TreeMotion(uv_source, len(bpy.data.collections[PREFIX + label + '_Guides'].objects), height)
		write_materials(folder, key, species, height)
		lows, high, fine = [], [], []
		budgets = (5000, 8000, 1500, 1500, 18000) if stage == 'Juvenile' else (18000, 26000, 6000, 4000, 72000)
		for index in (() if connected else (3, 4)):
			part, budget = PARTS[index], budgets[index]
			source = bpy.data.objects[PREFIX + label + '_' + part]
			for lod, ratio in enumerate((1.0, .3, .075)):
				level = fine_mesh(scene, source, lod, max(24, int(budget * ratio)), fine_material)
				for name, value in (('export_part', index), ('export_lod', lod)):
					attribute = level.data.attributes.new(name, 'INT', 'FACE')
					attribute.data.foreach_set('value', np.full(len(level.data.polygons), value, np.int32))
				fine.append(level)
			print('RESAMPLED_CLOSED_TUBES', label, part, flush=True)
		# The authoring construction mesh is the welded surface from which the
		# three visible parts were cut. Simplify it as one continuous surface:
		# independent part decimation moved shared boundaries and opened cracks.
		source = bpy.data.objects[PREFIX + label + '_Wood']
		source_parts = np.empty(len(source.data.polygons), np.int32)
		source.data.attributes['wood_part'].data.foreach_get('value', source_parts)
		part_ids = set(np.unique(source_parts).tolist())
		assert {0, 1}.issubset(part_ids) and part_ids.issubset({0, 1, 2}), 'Invalid source wood parts'
		# Some saplings have only fine roots: their thick Roots object is empty.
		# Require every part actually authored, without fabricating thick roots.
		expected_parts = {PARTS[index] for index in part_ids} | (set() if connected else {'RootTips', 'Twigs'})
		h = source.copy()
		h.name = '_TREE_EXPORT_High'
		h.hide_render = False
		h.hide_viewport = False
		h.location = (0, 0, 0)
		scene.collection.objects.link(h)
		high.append(h)
		low = h.copy()
		low.data = source.data.copy()
		low.name = '_TREE_EXPORT_Structural'
		scene.collection.objects.link(low)
		low.vertex_groups.clear()
		low.data.attributes['wood_part'].name = 'export_part'
		# Connected sources already contain all fine limbs. Preserve the reviewed
		# LOD0 wood and derive lower levels from this same continuous surface.
		if not connected:
			simplify(low, sum(budgets[:3]))
		base_triangles = sum(len(p.vertices) - 2 for p in low.data.polygons)
		bind_uv = runpy.run_path(str(ROOT / 'Tools/BlenderTrees/build_oak_studies.py'))['bind_fused_uv']
		uv_source = bpy.data.objects[PREFIX + label + '_UV_Source']
		for lod, ratio in enumerate((1.0, .3, .075)):
			level = low
			if lod:
				level = low.copy()
				level.data = low.data.copy()
				scene.collection.objects.link(level)
				simplify(level, max(24, int(base_triangles * ratio)))
			# Collapse interpolation crosses cylindrical seams at forks. Reproject
			# the final faces onto the original branch frames before baking.
			mesh = level.data
			protected = [('vertices', 'co', len(mesh.vertices) * 3, np.float32),
				('loops', 'vertex_index', len(mesh.loops), np.int32),
				('export_part', 'value', len(mesh.polygons), np.int32)]
			before = []
			for name, field, count, dtype in protected:
				data = mesh.attributes[name].data if name == 'export_part' else getattr(mesh, name)
				values = np.empty(count, dtype)
				data.foreach_get(field, values)
				before.append(values)
			mesh.uv_layers.active = mesh.uv_layers['UVMap']
			if mesh.attributes.get('bark_depth'):
				mesh.attributes.remove(mesh.attributes['bark_depth'])
			for progress in bind_uv(level, uv_source):
				print('PROJECTING_BARK', label, lod, progress, flush=True)
			for (name, field, count, dtype), original in zip(protected, before):
				data = mesh.attributes[name].data if name == 'export_part' else getattr(mesh, name)
				values = np.empty(count, dtype)
				data.foreach_get(field, values)
				assert np.array_equal(values, original), f'UV projection changed structural {field}'
			attribute = level.data.attributes.get('export_lod') or level.data.attributes.new('export_lod', 'INT', 'FACE')
			attribute.data.foreach_set('value', np.full(len(level.data.polygons), lod, np.int32))
			bm = bmesh.new()
			bm.from_mesh(level.data)
			boundary_edges = sum(not edge.is_manifold for edge in bm.edges)
			bm.free()
			assert boundary_edges == 0, f'Open structural wood at LOD{lod}: {boundary_edges} edges'
			lows.append(level)
			print('CLOSED_STRUCTURAL_LOD', label, lod, len(level.data.polygons), flush=True)
		wood = unwrap_and_bake(scene, lows, high, wood_material, folder, 'bark', texture_size)
		fine_textures(scene, species, folder, source.data.materials[0])
		if fine:
			select(fine)
			bpy.ops.object.join()
			fine_wood = bpy.context.object
			select([fine_wood, wood], wood)
			bpy.ops.object.join()
		wood.name = '_TREE_EXPORT_Wood'
		wood['bake_exporter_sha256'] = exporter_hash
		bpy.data.libraries.write(str(folder / 'wood_export.blend'), {wood})
		for obj in high:
			bpy.data.objects.remove(obj, do_unlink=True)
		report = {'label': label, 'asset_key': key, 'catalog_eligible': catalog_eligible,
			'species': species, 'stage': stage, 'form': collection.get('form'), 'seed': collection.get('seed'),
			'source_graph_sha256': json.loads(collection['growth_graph'])['sha256'] if collection.get('growth_graph') else None,
			'source_foliage_generator_sha256': collection.get('foliage_generator_sha256'),
			'source_settings': json.loads(collection['settings']),
			'display_name': collection.get('display_name', f'{species} - {stage}'),
			'source_generator_sha256': collection.get('generator_sha256'),
			'exporter_sha256': exporter_hash,
			'native_limits_sha256': hashlib.sha256(Path(__file__).with_name('native_limits.py').read_bytes()).hexdigest(),
			'fine_branches': 'included_in_continuous_source_wood' if connected else 'closed_source_tubes_with_parent_matched_bark_palette',
			'lod0_wood': 'preserved_source_geometry' if connected else 'simplified_source_geometry',
			'structural_wood': 'continuous_closed_lods_with_shared_boundary_normals',
			'structural_uv': 'reprojected_from_original_branch_frames_after_decimation',
			'bark_collar_mask': 'shared_vertex_minimum_preserved_core_two_ring_expansion_three_ring_smoothing',
			'source_parts': sorted(expected_parts),
			'bark_detail': 'branch_aligned_uv1_reuses_young_bark_tiles',
			'meters_to_inches': UNITS, 'modeldoc_import_scale': FBX_IMPORT_SCALE,
			'modeldoc_import_rotation': [0.0, -90.0, 0.0], 'lods': [], 'collision': [], 'render_models': models}
		for lod in range(3):
			w = isolate_lod(scene, wood, lod)
			motion.tag_wood(w)
			wood_triangles = sum(len(p.vertices) - 2 for p in w.data.polygons)
			leaf = leaf_mesh(scene, leaf_source, species, stage, lod, leaf_materials, motion,
				max(MAX_LOD_TRIANGLES - wood_triangles, leaf_count * 8) if multipart else MAX_LOD_TRIANGLES - wood_triangles)
			leaf.name = 'Foliage'
			parts = split_parts(scene, w)
			assert {obj.name for obj in parts} == expected_parts, f'Missing authored semantic parts at LOD{lod}'
			assert sum(len(p.vertices) - 2 for p in w.data.polygons) == sum(
				len(p.vertices) - 2 for obj in parts for p in obj.data.polygons), 'Part split lost triangles'
			select(parts + [leaf])
			# Coordinates are explicitly inches, with Blender's native Z-up axes.
			for obj in parts + [leaf]:
				obj.scale = (UNITS,) * 3
			bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
			for obj in parts + [leaf]:
				coordinates = np.empty(len(obj.data.vertices) * 3, np.float32)
				obj.data.vertices.foreach_get('co', coordinates)
				assert np.isfinite(coordinates).all(), f'Non-finite geometry: {obj.name}'
				for layer in obj.data.uv_layers:
					uv = np.empty(len(layer.data) * 2, np.float32)
					layer.data.foreach_get('uv', uv)
					assert np.isfinite(uv).all(), f'Non-finite UV: {obj.name}'
			export_objects = parts + [leaf]
			triangles = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in export_objects)
			exported_triangles = 0
			for model_index, model in enumerate(models):
				piece = None
				if not multipart:group = export_objects
				elif model_index == 0:group = parts
				else:
					piece = foliage_piece(scene, leaf, *model['leaf_range'], motion.texture_size)
					group = [piece]
				select(group)
				count = sum(len(p.vertices) - 2 for obj in group for p in obj.data.polygons)
				assert count <= MAX_LOD_TRIANGLES, 'Native import triangle budget exceeded'
				bpy.ops.export_scene.fbx(filepath=str(folder / model['mesh_files'][lod]), use_selection=True,
					object_types={'MESH'}, global_scale=1.0, apply_unit_scale=False,
					apply_scale_options='FBX_SCALE_NONE', axis_forward='Y', axis_up='Z',
					use_mesh_modifiers=True, use_triangles=True, mesh_smooth_type='FACE', add_leaf_bones=False,
					bake_anim=False, path_mode='STRIP', use_custom_props=False, colors_type='LINEAR')
				model['lods'].append({'lod': lod, 'triangles': count, 'native_triangle_budget': MAX_LOD_TRIANGLES,
					'vertices': sum(len(obj.data.vertices) for obj in group),
					'bounds_inches': [[min(v.co[a] for obj in group for v in obj.data.vertices) for a in range(3)],
						[max(v.co[a] for obj in group for v in obj.data.vertices) for a in range(3)]]})
				exported_triangles += count
				if piece:
					data = piece.data; bpy.data.objects.remove(piece, do_unlink=True); bpy.data.meshes.remove(data)
			assert exported_triangles == triangles, 'Model partition lost or duplicated triangles'

			report['lods'].append({'lod': lod, 'vertices': sum(len(o.data.vertices) for o in export_objects),
				'triangles': triangles, 'native_triangle_budget_per_model': MAX_LOD_TRIANGLES,
				'parts': [o.name for o in parts] + ['Foliage'],
				'foliage': dict(leaf.items()), 'bounds_inches': [[min(v.co[a] for o in export_objects for v in o.data.vertices) for a in range(3)],
					[max(v.co[a] for o in export_objects for v in o.data.vertices) for a in range(3)]]})
			for obj in export_objects + [w]:
				mesh = obj.data
				bpy.data.objects.remove(obj, do_unlink=True)
				bpy.data.meshes.remove(mesh)
		report['motion'] = motion.write(folder / 'motion.png')
		report['motion']['exporter_sha256'] = hashlib.sha256(Path(__file__).with_name('export_motion.py').read_bytes()).hexdigest()
		for obj in bpy.data.collections[PREFIX + label + '_Collision'].objects:
			verts = [v.co for v in obj.data.vertices]
			p0, p1 = sum(verts[:8], Vector()) / 8, sum(verts[8:], Vector()) / 8
			r0, r1 = (verts[0] - p0).length, (verts[8] - p1).length
			report['collision'].append({'name': obj.name, 'role': obj['collision_role'],
				'point0': list(p0), 'point1': list(p1), 'radius': (r0 + r1) * .5,
				'source_radii': [r0, r1]})
		canonical_materials = [key + '_bark', key + '_fine'] + [key + f'_leaf{i}' for i in range(len(leaf_materials))]
		for index, model in enumerate(models):
			write_model(folder, key, list(zip([mat.name for mat in [wood_material, fine_material] + leaf_materials], canonical_materials)), report['collision'] if index == 0 else [], model)
		assert hashlib.sha256(Path(__file__).read_bytes()).hexdigest() == exporter_hash, 'Exporter changed during export'
		texture_names = [f'{part}_{channel}.png' for part in ('bark', 'fine') for channel in ('color', 'normal', 'roughness')]
		texture_names += ['motion.png', 'fine_height.png', 'leaf_color.png', 'leaf_opacity.png' if species in ('Oak', 'Spruce') else 'leaf_normal.png']
		filenames = [name for model in models for name in [model['filename'], *model['mesh_files']]] + [name + '.vmat' for name in canonical_materials] + texture_names
		report['files'] = {name: {'bytes': (folder / name).stat().st_size,
			'sha256': hashlib.sha256((folder / name).read_bytes()).hexdigest()} for name in filenames}
		report['complete'] = True
		report['seconds'] = time.time() - started
		pending = folder / 'manifest.pending.json'
		pending.write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
		pending.replace(manifest_path)
		print('EXPORTED', label, report['lods'], flush=True)
		return report
	finally:
		window.scene = original_scene
		for obj in list(scene.objects):
			mesh = obj.data if obj.type == 'MESH' else None
			bpy.data.objects.remove(obj, do_unlink=True)
			if mesh and mesh.users == 0:
				bpy.data.meshes.remove(mesh)
		bpy.data.scenes.remove(scene)
		for mat in created_materials:
			if mat.users == 0:
				bpy.data.materials.remove(mat)
