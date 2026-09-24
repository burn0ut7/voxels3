"""Authored motion payload for the canonical Blender exporter.

The exporter calls this on its temporary meshes, in Blender meters, before FBX
conversion. No source specimen is modified and no separate runtime tree is built.
"""

import bpy
import json
import math
import struct
import zlib
import numpy as np
from mathutils import Vector
from mathutils.kdtree import KDTree


class TreeMotion:
	def __init__(self, construction, primary_count, height):
		self.records = json.loads(construction['sweeps'])
		assert 1 <= primary_count <= 256, 'Primary branch identity exceeds byte payload'
		self.height = float(height)
		assert self.height > 0
		self.primary_count = primary_count
		self.pivots = np.zeros((primary_count, 3), np.float32)
		self.lengths = np.ones(primary_count, np.float32)
		for index, record in enumerate(self.records[:primary_count]):
			points = np.asarray([frame['p'] for frame in record['frames']], np.float32)
			self.pivots[index] = points[0]
			self.lengths[index] = np.linalg.norm(np.diff(points, axis=0), axis=1).sum()
		self.pivots[0] = 0
		identities = []
		points = []
		for index, record in enumerate(self.records):
			ancestor = index
			seen = set()
			while ancestor >= primary_count:
				assert ancestor not in seen, 'Cycle in branch hierarchy'
				seen.add(ancestor)
				ancestor = int(self.records[ancestor]['parent'])
				assert 0 <= ancestor < len(self.records)
			for frame in record['frames']:
				points.append(frame['p'])
				identities.append(ancestor)
		self.identities = np.asarray(identities, np.uint16)
		self.tree = KDTree(len(points))
		for index, point in enumerate(points):
			self.tree.insert(Vector(point), index)
		self.tree.balance()
		self.leaf_entries = None
		self.leaf_axes = None
		self.texture_size = None

	def classify(self, positions):
		identity = np.asarray([self.identities[self.tree.find(Vector(p))[1]]
			for p in positions], np.uint16)
		# Underground geometry and the stiff lower stem never get branch motion.
		identity[np.asarray(positions)[:, 2] <= 0] = 0
		distance = np.linalg.norm(positions - self.pivots[identity], axis=1)
		weight = np.clip(distance / np.maximum(self.lengths[identity], .01), 0, 1)
		weight[identity == 0] = 0
		return identity, weight

	def tag_wood(self, obj):
		mesh = obj.data
		positions = np.empty(len(mesh.vertices) * 3, np.float32)
		mesh.vertices.foreach_get('co', positions)
		identity, weight = self.classify(positions.reshape(-1, 3))
		vertices = np.empty(len(mesh.loops), np.int32)
		mesh.loops.foreach_get('vertex_index', vertices)
		color = mesh.color_attributes.active_color
		colors = np.ones((len(mesh.loops), 4), np.float32)
		if color is not None:
			assert color.domain == 'CORNER', 'Preserve the existing bark collar mask'
			color.data.foreach_get('color', colors.reshape(-1))
		else:
			color = mesh.color_attributes.new(name='TreeMotion', type='FLOAT_COLOR', domain='CORNER')
		colors[:, 0] = identity[vertices] / 255.0
		colors[:, 1] = weight[vertices]
		colors[:, 2] = 0
		color.data.foreach_set('color', colors.reshape(-1))
		mesh.color_attributes.active_color = color
		mesh.color_attributes.render_color_index = list(mesh.color_attributes).index(color)

	def tag_leaves(self, obj, pivots, leaf_indices, normals, axes=None):
		"""One stable pivot per retained blade/cell, reused at every mesh LOD."""
		pivots = np.asarray(pivots, np.float32)
		identity, weight = self.classify(pivots)
		entries = np.column_stack((pivots, weight))
		if self.leaf_entries is None:
			width = 256
			height = 2 ** math.ceil(math.log2(max(2, math.ceil((len(pivots) + 256) / width))))
			# Half-precision UV centers are exact up to 1024 texels per axis.
			# Expand horizontally instead of aliasing dense crowns' pivot rows.
			while height > 1024 and width < 1024:
				width *= 2
				height //= 2
			assert height <= 1024, 'UV1 half precision cannot address this many leaf pivots'
			self.texture_size = (width, height)
			self.leaf_entries = entries
		else:
			assert np.array_equal(self.leaf_entries, entries), 'LOD changed attachment identity'
		mesh = obj.data
		vertices = np.empty(len(mesh.loops), np.int32)
		mesh.loops.foreach_get('vertex_index', vertices)
		index = np.asarray(leaf_indices, np.int32)[vertices]
		texel = index + 256
		width, height = self.texture_size
		coordinates = np.column_stack(((texel % width + .5) / width,
			(texel // width + .5) / height)).astype(np.float32)
		layer = mesh.uv_layers.get('TreePivot') or mesh.uv_layers.new(name='TreePivot')
		assert list(mesh.uv_layers).index(layer) == 1, 'Foliage motion must use UV1'
		layer.data.foreach_set('uv', coordinates.reshape(-1))
		colors = np.ones((len(mesh.loops), 4), np.float32)
		colors[:, 0] = identity[index] / 255.0
		colors[:, 1:3] = encode_directions(normals)[index]
		if axes is not None:
			axes = np.asarray(axes, np.float32)
			assert axes.shape == pivots.shape, 'Every leaf requires one authored long axis'
			if self.leaf_axes is None:
				self.leaf_axes = axes.copy()
			else:
				assert np.array_equal(self.leaf_axes, axes), 'LOD changed leaf orientation'
			layer = mesh.uv_layers.new(name='TreeLeafAxis')
			assert list(mesh.uv_layers).index(layer) == 2, 'Leaf facing must use UV2'
			layer.data.foreach_set('uv', encode_directions(axes)[index].reshape(-1))
		# Bounded column occupancy gives the canopy a darker interior. It is
		# computed once from the shared leaves, never separately for each LOD.
		cells = np.floor(pivots / .75).astype(np.int32)
		lo, hi = cells.min(axis=0), cells.max(axis=0)
		shape = hi - lo + 3
		assert int(np.prod(shape)) < 2000000, 'Unexpected canopy extent'
		grid = np.zeros(shape, np.float32)
		local = cells - lo + 1
		np.add.at(grid, tuple(local.T), 1)
		above = np.flip(np.cumsum(np.flip(grid, axis=2), axis=2), axis=2) - grid * .5
		coverage = above[tuple(local.T)]
		colors[:, 3] = np.clip(np.exp(-coverage[index] * .005), .38, 1)
		color = mesh.color_attributes.new(name='TreeMotion', type='FLOAT_COLOR', domain='CORNER')
		color.data.foreach_set('color', colors.reshape(-1))
		mesh.color_attributes.active_color = color
		mesh.color_attributes.render_color_index = 0

	def write(self, path):
		assert self.leaf_entries is not None
		count = len(self.leaf_entries)
		width, height = self.texture_size
		pixels = np.zeros((height * width, 4), np.float32)
		pixels[:self.primary_count, :3] = self.pivots
		pixels[:self.primary_count, 3] = np.clip(self.lengths / self.height, 0, 1)
		pixels[256:256 + count] = self.leaf_entries
		pixels[:, :3] = pixels[:, :3] / (self.height * 4) + .5
		assert np.isfinite(pixels).all() and pixels.min() >= 0 and pixels.max() <= 1
		# This is numeric RGBA data: Blender save_render unpremultiplies RGB
		# by the branch weight in alpha, even with CHANNEL_PACKED on5.2.2.
		# Write lossless16-bit PNG without color/alpha conversion. PNG rows
		# are top-down; Blender UV0 and the source arrays start bottom-left.
		encoded = np.rint(pixels * 65535).astype('>u2').reshape(height, width, 4)
		scanlines = b''.join(b'\x00' + row.tobytes() for row in encoded[::-1])
		output = bytearray(b'\x89PNG\r\n\x1a\n')
		for kind, data in ((b'IHDR', struct.pack('>IIBBBBB', width, height, 16, 6, 0, 0, 0)),
			(b'IDAT', zlib.compress(scanlines, 6)), (b'IEND', b'')):
			output.extend(struct.pack('>I', len(data)) + kind + data
				+ struct.pack('>I', zlib.crc32(kind + data)))
		path.write_bytes(output)
		# The native independent image decoder must recover the complete data,
		# including attachment positions whose motion weight is exactly zero.
		decoded = bpy.data.images.load(str(path), check_existing=False)
		try:
			decoded.colorspace_settings.name = 'Non-Color'
			decoded.alpha_mode = 'CHANNEL_PACKED'
			actual = np.empty(pixels.size, np.float32)
			decoded.pixels.foreach_get(actual)
			error = float(np.max(np.abs(actual.reshape(-1, 4) - pixels)))
			assert error <= 1.5 / 65535, f'Motion PNG changed numeric data: {error}'
		finally:
			bpy.data.images.remove(decoded)
		return {'version': 3 if width > 256 else 2 if self.leaf_axes is not None else 1,
			'leaf_axis_encoding': 'octahedral_uv2' if self.leaf_axes is not None else None,
			'primary_branches': self.primary_count,
			'leaf_pivots': count, 'texture_size': [width, height],
			'position_scale_meters': self.height * 4, 'png_max_error': error}


def encode_directions(directions):
	"""Shared octahedral encoding for blade normals and authored long axes."""
	directions = np.asarray(directions, np.float32)
	lengths = np.abs(directions).sum(axis=1, keepdims=True)
	assert np.isfinite(directions).all() and np.all(lengths > 1e-8), 'Invalid leaf orientation'
	directions = directions / lengths
	encoded = directions[:, :2].copy()
	back = directions[:, 2] < 0
	encoded[back] = (1 - np.abs(encoded[back, ::-1])) * np.where(encoded[back] >= 0, 1, -1)
	return encoded * .5 + .5
