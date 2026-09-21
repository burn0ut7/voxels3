"""Seasonal tree graph shared by Blender previews and s&box source geometry.

No bpy, engine state, global RNG, or per-species growth implementations.
Distances are metres. A season is a model step, not a calibrated botanical year.
"""

from bisect import bisect_right
from dataclasses import asdict, dataclass
import hashlib
import json
import math

VERSION = 1
MAX_NODES = 30000
MAX_SEASONS = 80


def add(a, b):
	return tuple(x + y for x, y in zip(a, b))


def scale(a, value):
	return tuple(x * value for x in a)


def dot(a, b):
	return sum(x * y for x, y in zip(a, b))


def cross(a, b):
	return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def unit(a):
	length = math.sqrt(dot(a, a))
	if length < 1e-9:
		return (0.0, 0.0, 1.0)
	return scale(a, 1.0 / length)


def noise(seed, node, season, channel):
	"""Specified 32-bit integer mixer, independent of Python/process hashes."""
	value = (seed ^ (node * 0x9E3779B9) ^ (season * 0x85EBCA6B) ^ (channel * 0xC2B2AE35)) & 0xFFFFFFFF
	value = ((value ^ (value >> 16)) * 0x7FEB352D) & 0xFFFFFFFF
	value = ((value ^ (value >> 15)) * 0x846CA68B) & 0xFFFFFFFF
	return ((value ^ (value >> 16)) & 0xFFFFFFFF) / 4294967296.0


@dataclass(frozen=True)
class Species:
	name: str
	apical_control: float
	bud_probability: float
	buds_per_node: int
	phyllotaxis: float
	bud_delay: int
	extension: float
	gravity_response: float
	shade_tolerance: float
	radial_growth: float
	leaf_retention: int
	terminal_loss: float
	leafing_wood_age: int = 3


# Architectural traits are parameters of the same solver, not separate recipes
# that draw a mature scaffold. These initial profiles require visual calibration.
SPECIES = {
	"Oak": Species("English oak", .60, .46, 1, 137.5, 1, .52, .18, .20, .0030, 1, .045),
	"Ash": Species("Common ash", .70, .36, 2, 90.0, 1, .66, .08, .24, .0025, 1, .025),
	"Birch": Species("Silver birch", .65, .43, 1, 137.5, 1, .62, .30, .29, .0019, 1, .035),
	"Spruce": Species("Norway spruce", .91, .57, 4, 92.0, 1, .50, .20, .13, .0027, 4, .002),
}


def supports_foliage(node, season, profile):
	"""Living shoots renew leaves; needle retention is a separate age limit."""
	return (node['parent'] >= 0 and not node['dead']
		and season - node['born'] < max(profile.leaf_retention, profile.leafing_wood_age))


@dataclass(frozen=True)
class Recipe:
	species: str = "Oak"
	seed: int = 1701
	age: int = 24
	height: float = 16.0
	spread: float = 1.0
	girth: float = 1.0
	lean: float = 0.0
	upward: float = .55
	droop: float = .35
	branch_density: float = 1.35
	branch_angle: float = 57.0
	character: float = .45
	fork_height: float = .24
	growth_direction: float = 0.0
	crown_bias: float = .25
	competition: float = .65
	light_response: float = .40
	resource: float = 1.0

	def __post_init__(self):
		if self.species not in SPECIES:
			raise ValueError('Unknown species profile')
		bounds = {'height': (1.5, 45), 'spread': (.35, 1.8), 'girth': (.3, 1.7),
			'lean': (-25, 25), 'upward': (0, 1), 'droop': (0, 1),
			'branch_density': (.55, 1.8), 'branch_angle': (20, 85), 'character': (0, 1),
			'fork_height': (.06, .65), 'growth_direction': (-180, 180), 'crown_bias': (0, 1),
			'competition': (0, 2), 'light_response': (0, 2), 'resource': (.1, 2)}
		for name, (low, high) in bounds.items():
			value = getattr(self, name)
			if not isinstance(value, (int, float)) or not math.isfinite(value) or not low - 1e-6 <= value <= high + 1e-6:
				raise ValueError(f'{name} must be between {low} and {high}')
		if type(self.age) is not int or not 1 <= self.age <= MAX_SEASONS:
			raise ValueError(f'Growth seasons must be an integer from 1 to {MAX_SEASONS}')
		if type(self.seed) is not int or not 0 <= self.seed <= 2147483647:
			raise ValueError('Seed must be an integer from 0 to 2147483647')


class LightField:
	"""Directional canopy columns; each query excludes the local leaf cluster."""
	def __init__(self, nodes, season, profile, recipe):
		azimuth = math.radians(recipe.growth_direction)
		self.sun = unit((math.cos(azimuth), math.sin(azimuth), 1.5))
		self.cell = max(.25, recipe.height / 32)
		self.frames = []
		for direction in (self.sun, (0.0, 0.0, 1.0)):
			u = unit(cross(direction, (0.0, 1.0, 0.0)))
			v = unit(cross(direction, u))
			columns = {}
			for node in nodes[1:]:
				if not supports_foliage(node, season, profile):
					continue
				p = node["p"]
				x, y = math.floor(dot(p, u) / self.cell), math.floor(dot(p, v) / self.cell)
				depth = dot(p, direction)
				for dx, dy, weight in ((0, 0, 1.0), (-1, 0, .22), (1, 0, .22), (0, -1, .22), (0, 1, .22)):
					columns.setdefault((x + dx, y + dy), []).append((depth, weight))
			for key, samples in columns.items():
				samples.sort()
				depths, sums = [], [0.0]
				for depth, weight in samples:
					depths.append(depth)
					sums.append(sums[-1] + weight)
				columns[key] = (depths, sums)
			self.frames.append((direction, u, v, columns))
		self.extinction = recipe.competition * .55
		self.sun_weight = .5 + recipe.crown_bias * .4

	def sample(self, p):
		values = []
		for direction, u, v, columns in self.frames:
			key = (math.floor(dot(p, u) / self.cell), math.floor(dot(p, v) / self.cell))
			column = columns.get(key)
			if column is None:
				values.append(1.0)
				continue
			depths, sums = column
			index = bisect_right(depths, dot(p, direction) + self.cell * .65)
			values.append(math.exp(-self.extinction * (sums[-1] - sums[index])))
		return values[0] * self.sun_weight + values[1] * (1.0 - self.sun_weight)


class Simulation:
	"""Incremental main-thread authoring job; publish only after finish()."""
	def __init__(self, recipe):
		self.recipe = recipe
		self.profile = SPECIES[recipe.species]
		self.season = 0
		self.axes = [{"parent": -1, "attachment": 0, "nodes": [0]}]
		azimuth, lean = math.radians(recipe.growth_direction), math.radians(recipe.lean)
		direction = (math.sin(lean) * math.cos(azimuth), math.sin(lean) * math.sin(azimuth), math.cos(lean))
		self.nodes = [self._node(-1, 0, (0.0, 0.0, 0.0), direction, 0, 0)]
		self.stats = {"blocked_shoots": 0, "dormant_buds": 0, "lost_tips": 0}
		self.occupancy = {}
		self.space_cell = max(.12, self.profile.extension * .5)

	def _node(self, parent, axis, position, direction, born, order):
		return {"parent": parent, "axis": axis, "p": position, "direction": direction,
			"born": born, "order": order, "radius": .003, "tip": True,
			"lateral_done": False, "dead": False, "starved": 0, "light": 1.0}

	def _key(self, p):
		return tuple(math.floor(value / self.space_cell) for value in p)

	def _room(self, p, parent):
		key = self._key(p)
		allowed = {parent}
		ancestor = self.nodes[parent]["parent"]
		for _ in range(3):
			if ancestor < 0:
				break
			allowed.add(ancestor)
			ancestor = self.nodes[ancestor]["parent"]
		for dx in (-1, 0, 1):
			for dy in (-1, 0, 1):
				for dz in (-1, 0, 1):
					for index in self.occupancy.get((key[0] + dx, key[1] + dy, key[2] + dz), ()):
						if index in allowed:
							continue
						node = self.nodes[index]
						delta = add(p, scale(node["p"], -1))
						clearance = max(.06, min(self.space_cell * .65, node["radius"] * 1.4))
						if dot(delta, delta) < clearance * clearance:
							return False
		return p[2] > .015

	def step(self):
		if self.season >= self.recipe.age:
			return False
		r, species = self.recipe, self.profile
		season = self.season + 1
		field = LightField(self.nodes, season, species, r)
		count = len(self.nodes)
		children = [[] for _ in range(count)]
		own = [0.0] * count
		lateral = [False] * count
		for i, node in enumerate(self.nodes):
			if node["parent"] >= 0:
				children[node["parent"]].append(i)
			node["light"] = field.sample(node["p"])
			if node["dead"]:
				continue
			age = season - node["born"]
			lateral[i] = (i > 0 and not node["lateral_done"] and age >= species.bud_delay
				and age <= species.bud_delay + 3)
			if node["axis"] == 0 and node["born"] < 1 + round(r.fork_height * 5):
				lateral[i] = False
			own[i] = node["light"] * ((1.0 if node["tip"] else 0.0) + (.25 if lateral[i] else 0.0))
		demand = own.copy()
		for i in range(count - 1, 0, -1):
			demand[self.nodes[i]["parent"]] += demand[i]
		allocation = [0.0] * count
		allocation[0] = demand[0] * r.resource
		local = [0.0] * count
		for i, node in enumerate(self.nodes):
			weighted = [(child, demand[child] * (species.apical_control if self.nodes[child]["axis"] == node["axis"] else 1 - species.apical_control)) for child in children[i]]
			total = own[i] + sum(weight for _, weight in weighted)
			if total <= 1e-9:
				continue
			local[i] = allocation[i] * own[i] / total
			for child, weight in weighted:
				allocation[child] = allocation[i] * weight / total
		for index in range(count):
			node = self.nodes[index]
			if node["dead"]:
				continue
			vigor = min(2.0, local[index])
			light = node["light"]
			if node["tip"]:
				if light < species.shade_tolerance or vigor < .12:
					node["starved"] += 1
					if node["starved"] >= 3:
						node["dead"] = True
						node["tip"] = False
						self.stats["lost_tips"] += 1
				else:
					node["starved"] = 0
					loss = species.terminal_loss * (1 if node["axis"] == 0 else .5)
					if season > 5 and noise(r.seed, index, season, 11) < loss:
						node["tip"] = False
						self.stats["lost_tips"] += 1
					else:
						self._extend(index, node["axis"], node["direction"], vigor, field, season)
			if lateral[index] and light > species.shade_tolerance and vigor > .07:
				probability = min(.9, species.bud_probability * r.branch_density * light * (.78 ** node["order"]))
				if noise(r.seed, index, season, 20) < probability:
					node["lateral_done"] = True
					u = unit(cross(node["direction"], (0, 1, 0)))
					v = unit(cross(node["direction"], u))
					for bud in range(species.buds_per_node):
						phase = math.radians(index * species.phyllotaxis) + math.tau * bud / species.buds_per_node
						phase += (noise(r.seed, index, season, 30 + bud) - .5) * r.character
						angle = math.radians(r.branch_angle)
						direction = unit(add(scale(node["direction"], math.cos(angle)), scale(add(scale(u, math.cos(phase)), scale(v, math.sin(phase))), math.sin(angle))))
						axis = len(self.axes)
						self.axes.append({"parent": node["axis"], "attachment": index, "nodes": [index]})
						if not self._extend(index, axis, direction, max(.35, vigor), field, season):
							self.axes.pop()
				else:
					self.stats["dormant_buds"] += 1
		# Supporting wood never gets thinner when a shaded shoot loses foliage.
		support = [0.0] * len(self.nodes)
		for i in range(len(self.nodes) - 1, -1, -1):
			node = self.nodes[i]
			if supports_foliage(node, season, species):
				support[i] += max(.1, node["light"])
			pipe = (.0028 ** 2.2 * max(.15, support[i])) ** (1 / 2.2)
			# Add one season's ring. Multiplying current foliage support by the
			# entire age retroactively thickens all previous rings as a crown grows.
			increment = species.radial_growth * min(1.0, support[i]) * r.girth
			node["radius"] = max(node["radius"] + increment, pipe * r.girth)
			if node["parent"] >= 0:
				support[node["parent"]] += support[i]
		self.season = season
		return True

	def _extend(self, parent, axis, direction, vigor, field, season):
		r, species = self.recipe, self.profile
		node = self.nodes[parent]
		order = node["order"] + (axis != node["axis"])
		remaining = max(.04, 1 - max(0.0, node["p"][2]) / r.height)
		length = species.extension * (.55 + .45 * min(vigor, 1.6)) * remaining ** .35
		length *= 1 / (1 + order * .20)
		# Mature lateral growth tends outward; active leaders respond upward.
		up = r.upward * (.32 if axis == 0 else .10)
		gravity = species.gravity_response * r.droop * min(1.0, (season - self.nodes[self.axes[axis]["attachment"]]["born"]) / 12)
		direction = add(direction, (0, 0, up - (gravity if axis != 0 else 0)))
		if axis != 0:
			direction = (direction[0] * r.spread, direction[1] * r.spread, direction[2])
		jitter = tuple((noise(r.seed, parent, season, 50 + axis * 3 + component) - .5) * r.character * .35 for component in range(3))
		direction = unit(add(direction, jitter))
		u = unit(cross(direction, (0, 1, 0)))
		v = unit(cross(direction, u))
		best = None
		for candidate in range(7):
			if candidate == 0:
				d = direction
			else:
				phase = (candidate - 1) * math.tau / 6
				d = unit(add(direction, scale(add(scale(u, math.cos(phase)), scale(v, math.sin(phase))), .32)))
			p = add(node["p"], scale(d, length))
			mid = add(node["p"], scale(d, length * .6))
			if not self._room(p, parent) or not self._room(mid, parent):
				continue
			light = field.sample(p)
			score = dot(d, direction) + r.light_response * light + r.crown_bias * .12 * dot(d, field.sun)
			if best is None or score > best[0]:
				best = (score, p, d, light)
		if best is None:
			self.stats["blocked_shoots"] += 1
			return False
		if len(self.nodes) >= MAX_NODES:
			raise ValueError(f"Growth exceeded {MAX_NODES:,} nodes; reduce seasons or resource supply")
		_, p, d, light = best
		index = len(self.nodes)
		child = self._node(parent, axis, p, d, season, order)
		child["light"] = light
		self.nodes.append(child)
		self.axes[axis]["nodes"].append(index)
		self.occupancy.setdefault(self._key(p), []).append(index)
		if axis == node["axis"]:
			node["tip"] = False
		return True

	def finish(self):
		if self.season != self.recipe.age:
			raise ValueError("An unfinished growth job cannot be published")
		graph = {"version": VERSION, "units": "metres", "recipe": asdict(self.recipe),
			"profile": asdict(self.profile), "season": self.season, "nodes": self.nodes,
			"axes": self.axes, "statistics": self.stats}
		validate(graph)
		graph["sha256"] = hashlib.sha256(json.dumps(graph, sort_keys=True, separators=(",", ":"), allow_nan=False).encode()).hexdigest()
		return graph


def validate(graph):
	"""Production data boundary used for newly generated and restored graphs."""
	if graph.get("version") != VERSION or graph.get("units") != "metres":
		raise ValueError("Unsupported growth graph version or units")
	recipe = Recipe(**graph['recipe'])
	if graph['season'] != recipe.age:
		raise ValueError('Growth graph does not contain the requested seasons')
	nodes, axes = graph["nodes"], graph["axes"]
	if not 1 < len(nodes) <= MAX_NODES or not axes:
		raise ValueError("Empty or oversized tree graph")
	for i, node in enumerate(nodes):
		if node["parent"] != -1 and not 0 <= node["parent"] < i:
			raise ValueError("Growth parents must precede their children")
		if (i == 0) != (node["parent"] == -1):
			raise ValueError("Growth graph must have exactly one root")
		if not 0 <= node["axis"] < len(axes) or not 0 <= node["born"] <= graph["season"]:
			raise ValueError("Invalid axis or birth season")
		if len(node['p']) != 3 or len(node['direction']) != 3:
			raise ValueError('Growth positions and directions require three coordinates')
		if not all(math.isfinite(v) for v in (*node["p"], *node["direction"], node["radius"])) or node["radius"] <= 0:
			raise ValueError("Non-finite geometry or non-positive branch radius")
	covered = {0}
	if axes[0]['attachment'] != 0 or nodes[0]['axis'] != 0:
		raise ValueError('Growth root must start the first axis')
	for i, axis in enumerate(axes):
		if axis["parent"] != -1 and not 0 <= axis["parent"] < i:
			raise ValueError("Invalid branch ancestry")
		if (i == 0) != (axis['parent'] == -1):
			raise ValueError('Growth axes must have exactly one root')
		if not all(type(index) is int and 0 <= index < len(nodes) for index in axis['nodes']):
			raise ValueError('Branch references an invalid node')
		if len(axis["nodes"]) < 2 or axis["nodes"][0] != axis["attachment"]:
			raise ValueError("Branch has no shoot or wrong attachment")
		if i > 0 and nodes[axis['attachment']]['axis'] != axis['parent']:
			raise ValueError('Branch attachment does not belong to its parent axis')
		for parent, child in zip(axis["nodes"], axis["nodes"][1:]):
			if nodes[child]["parent"] != parent or nodes[child]["axis"] != i:
				raise ValueError("Disconnected growth axis")
			if child in covered:raise ValueError('Growth node appears in multiple axes')
			covered.add(child)
	if len(covered) != len(nodes):raise ValueError('Growth nodes missing from branch axes')
	if 'sha256' in graph:
		payload = {key: value for key, value in graph.items() if key != 'sha256'}
		actual = hashlib.sha256(json.dumps(payload, sort_keys=True, separators=(',', ':'), allow_nan=False).encode()).hexdigest()
		if graph['sha256'] != actual:
			raise ValueError('Growth graph checksum does not match its contents')
