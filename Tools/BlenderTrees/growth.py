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
# Offline authoring guard; dense mature crowns require more than 30k nodes.
MAX_NODES = 100000
MAX_SEASONS = 80
PIPE_EXPONENT = 2.2


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
	lateral_buds: int = 1
	lateral_extension: float = 1.0
	internode_length: float = 1.0
	branch_planarity: float = 0.0
	foliage_by_length: bool = False
	shoot_radius: float = .003
	pipe_radius: float = .0028
	bud_acrotony: float = 0.0
	apical_maturity: float = 0.0


# Architectural traits are parameters of the same solver, not separate recipes
# that draw a mature scaffold. These initial profiles require visual calibration.
SPECIES = {
	"Oak": Species("English oak", .60, .46, 1, 137.5, 1, .52, .18, .20, .0030, 1, .045, internode_length=.12, foliage_by_length=True, bud_acrotony=1, apical_maturity=12),
	"Ash": Species("Common ash", .70, .36, 2, 90.0, 1, .66, .08, .24, .0025, 1, .025, lateral_buds=2, internode_length=.16, foliage_by_length=True, bud_acrotony=2),
	"Birch": Species("Silver birch", .65, .43, 1, 137.5, 1, .62, .30, .29, .0019, 1, .035, internode_length=.10, foliage_by_length=True, bud_acrotony=2),
	"Spruce": Species("Norway spruce", .54, .57, 4, 92.0, 1, .50, .20, .13, .0027, 6, 0, lateral_buds=1, lateral_extension=.8, internode_length=.12, branch_planarity=.9, foliage_by_length=True, shoot_radius=.0009, pipe_radius=.0014),
}


def supports_foliage(node, season, profile, support_radius):
	"""Recent shoots and living fine broadleaf twigs can carry renewed leaves."""
	if node['parent'] < 0 or node['dead']:return False
	if season - node['born'] < max(profile.leaf_retention, profile.leafing_wood_age):return True
	# This is a shared authoring approximation for persistent short-shoot
	# foliage. The whole segment must remain fine; old structural wood stays bare.
	return profile.leaf_retention == 1 and support_radius <= profile.shoot_radius * 4


def foliage_weight(node, nodes, profile):
	"""Foliage area follows bearing length, independent of bud-site subdivision."""
	area = math.dist(node['p'], nodes[node['parent']]['p']) / profile.extension if profile.foliage_by_length else 1.0
	return area * sum(end - start for start, end in foliage_ranges(node))


def foliage_ranges(node, exposure=None):
	"""Complement of recorded wood enclosure, in normalized shoot distance."""
	start = 0.0
	for low, high, _ in sorted(node.get('foliage_occlusion', ()) if exposure is None else exposure):
		if low > start:
			yield start, low
		start = max(start, high)
	if start < 1.0:
		yield start, 1.0


def segment_radii(nodes, index, child_counts):
	"""One taper rule for growth occupancy, foliage exposure and source wood."""
	node = nodes[index]; parent = nodes[node['parent']]
	start = node['radius'] if node['axis'] != parent['axis'] and child_counts[node['parent']] > 1 else parent['radius']
	return start, node['radius']


def shoot_curves(nodes, children):
	"""Shared nine-sample Hermite paths derived from the current graph.

	Exposure and meshing must use the same bend at an attachment. Checking
	only its straight chord can expose foliage still inside the parent wood.
	"""
	tangents = []
	for index, node in enumerate(nodes):
		incoming = node['direction']
		if children[index]:
			preferred = max(children[index], key=lambda child: (nodes[child]['axis'] == node['axis'], nodes[child]['radius'], -child))
			outgoing = unit(add(nodes[preferred]['p'], scale(node['p'], -1)))
			incoming = unit(add(incoming, outgoing))
		tangents.append(incoming)
	paths = [None]
	for node in nodes[1:]:
		parent = node['parent']; a = nodes[parent]['p']; b = node['p']; length = math.dist(a, b)
		m0 = scale(tangents[parent], length); m1 = scale(tangents[len(paths)], length)
		path = []
		for sample in range(9):
			t = sample / 8; t2 = t * t; t3 = t2 * t
			path.append(tuple(a[j] * (2*t3-3*t2+1) + m0[j] * (t3-2*t2+t) + b[j] * (-2*t3+3*t2) + m1[j] * (t3-t2) for j in range(3)))
		paths.append(path)
	return paths


def enclosed_intervals(a, b, radii, c, d, other_radii):
	"""Sufficient ball containment in a tapered canonical wood segment.

	Use the nearest centerline parameter, including finite endpoint balls.
	Both radii vary linearly. A nonnegative radius difference and the squared
	distance inequality prove enclosure in this graph model, not final bark.
	"""
	direction = add(b, scale(a, -1)); other = add(d, scale(c, -1))
	delta = add(a, scale(c, -1)); length2 = dot(other, other)
	if length2 <= 1e-18:
		return []
	t0 = dot(delta, other) / length2; t1 = dot(direction, other) / length2
	breaks = [0.0, 1.0]
	if abs(t1) > 1e-15:
		breaks.extend(s for s in (-t0 / t1, (1 - t0) / t1) if 0 < s < 1)
	breaks.sort(); result = []
	for low, high in zip(breaks, breaks[1:]):
		middle = t0 + t1 * (low + high) * .5
		x, y = (0.0, 0.0) if middle <= 0 else (1.0, 0.0) if middle >= 1 else (t0, t1)
		v = add(delta, scale(other, -x)); w = add(direction, scale(other, -y))
		r0 = other_radii[0] + (other_radii[1] - other_radii[0]) * x - radii[0]
		r1 = (other_radii[1] - other_radii[0]) * y - (radii[1] - radii[0])
		aa = dot(w, w) - r1 * r1; bb = 2 * (dot(v, w) - r0 * r1); cc = dot(v, v) - r0 * r0
		cuts = [low, high]
		if abs(r1) > 1e-15:
			root = -r0 / r1
			if low < root < high:cuts.append(root)
		epsilon = 1e-12 * max(abs(aa), abs(bb), abs(cc), 1e-24)
		if abs(aa) <= epsilon:
			roots = [-cc / bb] if abs(bb) > epsilon else []
		else:
			discriminant = bb * bb - 4 * aa * cc
			if discriminant < 0:roots = []
			else:
				q = -.5 * (bb + math.copysign(math.sqrt(discriminant), bb))
				roots = [q / aa, cc / q] if q else [-bb / (2 * aa)]
		cuts.extend(root for root in roots if low < root < high); cuts.sort()
		for start, end in zip(cuts, cuts[1:]):
			s = (start + end) * .5
			if r0 + r1 * s > 0 and (aa * s + bb) * s + cc < 0:
				result.append((start, end))
	return result


def foliage_exposure(nodes, season, profile):
	"""Yield derived enclosure intervals without changing the supplied graph."""
	# All radii are final for this season. Never query a mixture of old
	# and newly incremented radii during the bottom-up support traversal.
	children = [[] for _ in nodes]
	for index, node in enumerate(nodes[1:], 1):children[node['parent']].append(index)
	child_counts = [len(child) for child in children]
	space_cell = max(.12, profile.extension * .5)
	paths = shoot_curves(nodes, children); segments = {}; occupied = {}
	for index, path in enumerate(paths[1:], 1):
		start_radius, end_radius = segment_radii(nodes, index, child_counts)
		for span, (a, b) in enumerate(zip(path, path[1:])):
			radii = (start_radius + (end_radius-start_radius)*span/8, start_radius + (end_radius-start_radius)*(span+1)/8)
			key = (index, span); segments[key] = (a, b, radii)
			for cell in segment_cells(a, b, max(radii), space_cell):occupied.setdefault(cell, []).append(key)
		if index % 128 == 0:yield -1, []
	for index, node in enumerate(nodes):
		intervals = []
		if not supports_foliage(node, season, profile, max(segment_radii(nodes, index, child_counts)) if index else 0):
			yield index, intervals
			continue
		covered = {}
		for span in range(8):
			a, b, radii = segments[index, span]; candidates = set()
			for cell in segment_cells(a, b, max(radii), space_cell):candidates.update(occupied.get(cell, ()))
			for other_key in sorted(candidates):
				other_id = other_key[0]
				if nodes[other_id]['axis'] == node['axis']:continue
				c, d, other_radii = segments[other_key]
				if max(other_radii) <= min(radii):continue
				# Only candidate pairs whose radius-expanded boxes overlap
				# need the containment solve, even within a coarse grid cell.
				radius = max(other_radii)
				if any(max(a[j],b[j]) < min(c[j],d[j])-radius or min(a[j],b[j]) > max(c[j],d[j])+radius for j in range(3)):continue
				for low, high in enclosed_intervals(a, b, radii, c, d, other_radii):
					if high > low:covered.setdefault(other_id, []).append(((span+low)/8, (span+high)/8))
		# Coalesce adjacent subsegment results while keeping the enclosing
		# graph segment identity. Foliage uses the union across identities.
		for other_id, ranges in sorted(covered.items()):
			low, high = sorted(ranges)[0]
			for start, end in sorted(ranges)[1:]:
				if start <= high:high = max(high, end)
				else:intervals.append((low,high,other_id));low,high = start,end
			intervals.append((low,high,other_id))
		intervals.sort()
		yield index, intervals


def segment_cells(a, b, radius, cell_size):
	low=[math.floor((min(x,y)-radius)/cell_size) for x,y in zip(a,b)]
	high=[math.floor((max(x,y)+radius)/cell_size) for x,y in zip(a,b)]
	for x in range(low[0],high[0]+1):
		for y in range(low[1],high[1]+1):
			for z in range(low[2],high[2]+1):yield x,y,z

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
	overhead_light: bool = False
	crown_bias: float = .25
	competition: float = .65
	light_response: float = .40
	resource: float = 1.0

	def __post_init__(self):
		if type(self.overhead_light) is not bool:
			raise ValueError('Overhead light must be a boolean')
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
		self.sun = (0.0, 0.0, 1.0) if recipe.overhead_light else unit((math.cos(azimuth), math.sin(azimuth), 1.5))
		self.cell = max(.25, recipe.height / 32)
		self.frames = []
		child_counts = [0] * len(nodes)
		for node in nodes[1:]:child_counts[node['parent']] += 1
		for direction in (self.sun, (0.0, 0.0, 1.0)):
			u = unit(cross(direction, (0.0, 1.0, 0.0)))
			v = unit(cross(direction, u))
			columns = {}
			for index, node in enumerate(nodes[1:], 1):
				if not supports_foliage(node, season, profile, max(segment_radii(nodes, index, child_counts))):
					continue
				p = node["p"]
				x, y = math.floor(dot(p, u) / self.cell), math.floor(dot(p, v) / self.cell)
				depth = dot(p, direction)
				area = foliage_weight(node, nodes, profile)
				for dx, dy, weight in ((0, 0, 1.0), (-1, 0, .22), (1, 0, .22), (0, -1, .22), (0, 1, .22)):
					columns.setdefault((x + dx, y + dy), []).append((depth, weight * area))
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
		self.child_counts = [0]
		self.stats = {"blocked_shoots": 0, "dormant_buds": 0, "dormant_shoots": 0, "lost_tips": 0}
		self.occupancy = {}
		self.space_cell = max(.12, self.profile.extension * .5)

	def _node(self, parent, axis, position, direction, born, order):
		return {"parent": parent, "axis": axis, "p": position, "direction": direction,
			"born": born, "order": order, "radius": self.profile.shoot_radius, "tip": True,
			"lateral_done": False, "spent_buds": 0, "dead": False, "starved": 0, "light": 1.0,
			"foliage_occlusion": []}


	def _occupy(self, index):
		node=self.nodes[index];parent=self.nodes[node['parent']]
		radius=max(segment_radii(self.nodes,index,self.child_counts))
		for key in segment_cells(parent['p'],node['p'],radius,self.space_cell):
			self.occupancy.setdefault(key,[]).append(index)

	def _room(self, p, parent, axis):
		# Query complete swept segments. Testing only endpoint spheres left
		# holes between internodes through which another shoot could grow.
		if p[2]<=.015:return False
		candidate_radius=max(self.nodes[parent]['radius'],self.profile.shoot_radius) if axis==self.nodes[parent]['axis'] or not self.child_counts[parent] else self.profile.shoot_radius
		origin=self.nodes[parent]['p'];direction=add(p,scale(origin,-1));length2=dot(direction,direction)
		if length2<=1e-18:return False
		allowed = {parent}
		ancestor = self.nodes[parent]["parent"]
		for _ in range(3):
			if ancestor < 0:
				break
			allowed.add(ancestor)
			ancestor = self.nodes[ancestor]["parent"]
		candidates=set()
		for key in segment_cells(origin,p,candidate_radius,self.space_cell):candidates.update(self.occupancy.get(key,()))
		for index in sorted(candidates):
			node=self.nodes[index]
			if index in allowed or node['parent']==parent:continue
			start=self.nodes[node['parent']];other=add(node['p'],scale(start['p'],-1));delta=add(origin,scale(start['p'],-1))
			other_length2=dot(other,other);along=dot(direction,delta)
			if other_length2<=1e-18:
				s=max(0,min(1,-along/length2));t=0
			else:
				crossing=dot(direction,other);offset=dot(other,delta);denominator=length2*other_length2-crossing*crossing
				s=max(0,min(1,(crossing*offset-along*other_length2)/denominator)) if denominator>1e-12*length2*other_length2 else 0
				t=(crossing*s+offset)/other_length2
				if t<0:t=0;s=max(0,min(1,-along/length2))
				elif t>1:t=1;s=max(0,min(1,(crossing-along)/length2))
			separation=add(add(delta,scale(direction,s)),scale(other,-t))
			radius=max(segment_radii(self.nodes,index,self.child_counts))
			clearance=radius+candidate_radius
			if dot(separation,separation)<clearance*clearance:return False
		return True

	def step(self):
		if self.season >= self.recipe.age:
			return False
		r, species = self.recipe, self.profile
		season = self.season + 1
		# Secondary growth changes old wood radii. Rebuild the same spatial
		# index once per season; newly extended segments enter it immediately.
		self.occupancy.clear()
		for index in range(1,len(self.nodes)):self._occupy(index)
		field = LightField(self.nodes, season, species, r)
		count = len(self.nodes)
		children = [[] for _ in range(count)]
		own = [0.0] * count
		terminal_demand = [0.0] * count
		lateral_demand = [0.0] * count
		# Only the end of an annual leader shoot carries a trunk whorl.
		# Intermediate nodes provide ordinary axillary bud sites along it.
		annual_ends = [True] * count
		annual_distance = [0.0] * count
		for i, node in enumerate(self.nodes[1:], 1):
			parent = self.nodes[node['parent']]
			annual_distance[i] = math.dist(node['p'], parent['p'])
			if node['axis'] == parent['axis'] and node['born'] == parent['born']:
				annual_ends[node['parent']] = False
				annual_distance[i] += annual_distance[node['parent']]
		annual_length = annual_distance.copy()
		for i in range(count - 1, 0, -1):
			node = self.nodes[i]; parent = self.nodes[node['parent']]
			if node['axis'] == parent['axis'] and node['born'] == parent['born']:
				annual_length[node['parent']] = annual_length[i]
		# Distal buds of an annual shoot have stronger outgrowth potential.
		# Adding internodes must not make every basal axil a vigorous leader.
		bud_priority = [(distance / length) ** species.bud_acrotony if length else 1.0
			for distance, length in zip(annual_distance, annual_length)]
		bud_counts = [species.buds_per_node if node['axis'] == 0 and annual_ends[i] else species.lateral_buds for i, node in enumerate(self.nodes)]
		available_buds = [count - node['spent_buds'].bit_count() for count, node in zip(bud_counts, self.nodes)]
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
			terminal_demand[i] = node["light"] if node["tip"] else 0.0
			lateral_demand[i] = node["light"] * .25 * available_buds[i] * bud_priority[i] if lateral[i] else 0.0
			own[i] = terminal_demand[i] + lateral_demand[i]
		demand = own.copy()
		for i in range(count - 1, 0, -1):
			demand[self.nodes[i]["parent"]] += demand[i]
		allocation = [0.0] * count
		allocation[0] = demand[0] * r.resource
		terminal_resource = [0.0] * count
		lateral_resource = [0.0] * count
		# Older broadleaf axes progressively release apical control. Branch
		# age starts at their first actual shoot, not the older attachment.
		apical = []
		for axis in self.axes:
			birth = self.nodes[axis["nodes"][1]]["born"] if len(axis["nodes"]) > 1 else season
			maturity = min(1.0, max(0, season - birth) / species.apical_maturity) if species.apical_maturity else 0.0
			apical.append(species.apical_control + (.5 - species.apical_control) * maturity)
		for i, node in enumerate(self.nodes):
			control = apical[node["axis"]]
			weighted = [(child, demand[child] * (control if self.nodes[child]["axis"] == node["axis"] else 1 - control)) for child in children[i]]
			terminal_weight = terminal_demand[i] * control
			lateral_weight = lateral_demand[i] * (1 - control)
			total = terminal_weight + lateral_weight + sum(weight for _, weight in weighted)
			if total <= 1e-9:
				continue
			# Local buds and existing branches compete under the same rule.
			# A whorl shares its allocation; it cannot spend it once per bud.
			terminal_resource[i] = allocation[i] * terminal_weight / total
			lateral_resource[i] = allocation[i] * lateral_weight / total / max(1, available_buds[i])
			for child, weight in weighted:
				allocation[child] = allocation[i] * weight / total
		for index in range(count):
			node = self.nodes[index]
			if node["dead"]:
				continue
			vigor = min(2.0, terminal_resource[index])
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
			bud_vigor = min(2.0, lateral_resource[index])
			if lateral[index] and light > species.shade_tolerance and bud_vigor > .07:
				probability = min(.9, species.bud_probability * r.branch_density * light * (.78 ** node["order"]))
				if noise(r.seed, index, season, 20) < probability:
					u = unit(cross(node["direction"], (0, 1, 0)))
					v = unit(cross(node["direction"], u))
					for bud in range(bud_counts[index]):
						if node['spent_buds'] & (1 << bud):continue
						phase = math.radians(index * species.phyllotaxis) + math.tau * bud / bud_counts[index]
						phase += (noise(r.seed, index, season, 30 + bud) - .5) * r.character
						angle = math.radians(r.branch_angle)
						side = add(scale(u, math.cos(phase)), scale(v, math.sin(phase)))
						if node['axis'] != 0 and species.branch_planarity:
							across = unit(cross(node['direction'], (0, 0, 1)))
							across = scale(across, 1 if dot(side, across) >= 0 else -1)
							side = unit(add(scale(side, 1 - species.branch_planarity), scale(across, species.branch_planarity)))
						direction = unit(add(scale(node["direction"], math.cos(angle)), scale(side, math.sin(angle))))
						axis = len(self.axes)
						self.axes.append({"parent": node["axis"], "attachment": index, "nodes": [index]})
						if not self._extend(index, axis, direction, bud_vigor, field, season):
							self.axes.pop()
						else:
							node['spent_buds'] |= 1 << bud
					node['lateral_done'] = node['spent_buds'] == (1 << bud_counts[index]) - 1
				else:
					self.stats["dormant_buds"] += 1
		# Supporting wood never gets thinner when a shaded shoot loses foliage.
		support = [0.0] * len(self.nodes)
		wood_support = [0.0] * len(self.nodes)
		for i in range(len(self.nodes) - 1, -1, -1):
			node = self.nodes[i]
			if supports_foliage(node, season, species, max(segment_radii(self.nodes, i, self.child_counts)) if i else 0):
				support[i] += max(.1, node["light"]) * foliage_weight(node, self.nodes, species)
			pipe = (species.pipe_radius ** PIPE_EXPONENT * max(.15, support[i])) ** (1 / PIPE_EXPONENT)
			# Add one season's ring. Multiplying current foliage support by the
			# entire age retroactively thickens all previous rings as a crown grows.
			increment = species.radial_growth * min(1.0, support[i]) * r.girth
			# Existing child wood also needs support after its foliage is lost.
			# Independent annual rings otherwise make every old lateral almost
			# as thick as the trunk, irrespective of how many forks it carries.
			node["radius"] = max(node["radius"] + increment, pipe * r.girth,
				wood_support[i] ** (1 / PIPE_EXPONENT))
			if node["parent"] >= 0:
				support[node["parent"]] += support[i]
				wood_support[node["parent"]] += node["radius"] ** PIPE_EXPONENT
		for index, intervals in foliage_exposure(self.nodes, season, species):
			if index >= 0:self.nodes[index]['foliage_occlusion'] = intervals
		self.season = season
		return True

	def _extend(self, parent, axis, direction, vigor, field, season):
		r, species = self.recipe, self.profile
		node = self.nodes[parent]
		order = node["order"] + (axis != node["axis"])
		remaining = max(.04, 1 - max(0.0, node["p"][2]) / r.height)
		# Shoot length follows its own allocated growth signal. A fixed minimum
		# previously made weak buds grow long bare shoots despite low resources.
		length = species.extension * min(vigor, 1.6) * remaining ** .35
		length *= 1 / (1 + order * .20)
		length *= species.lateral_extension ** order
		if length < species.shoot_radius * 2:
			self.stats['dormant_shoots'] += 1
			return False
		# The leader grows upward. Laterals approach an inclination relative
		# to gravity; a constant upward increment made every limb stand up.
		axis_nodes = self.axes[axis]['nodes']
		branch_birth = self.nodes[axis_nodes[1]]['born'] if len(axis_nodes) > 1 else season
		gravity = species.gravity_response * r.droop * min(1.0, (season - branch_birth) / 12)
		if axis == 0:
			direction = add(direction, (0, 0, r.upward * .32))
		else:
			horizontal = unit((direction[0], direction[1], 0))
			# Keep the inclination established by this branch's own bud.
			# A shared angle for every order made secondary branches parallel
			# to main limbs and left the crown in narrow, repeated tiers.
			initial = self.nodes[axis_nodes[1]]['direction'] if len(axis_nodes) > 1 else direction
			angle = math.acos(max(-1.0, min(1.0, initial[2]))) + gravity * math.pi - r.upward * .2
			angle = max(.05, min(math.pi - .05, angle))
			target = (horizontal[0] * math.sin(angle), horizontal[1] * math.sin(angle), math.cos(angle))
			direction = unit(add(scale(direction, .75), scale(target, .25)))
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
			if not self._room(p, parent, axis):
				continue
			light = field.sample(p)
			score = dot(d, direction) + r.light_response * light + r.crown_bias * .12 * dot(d, field.sun)
			if best is None or score > best[0]:
				best = (score, p, d, light)
		if best is None:
			self.stats["blocked_shoots"] += 1
			return False
		segments = max(1, math.ceil(length / species.internode_length))
		if len(self.nodes) + segments > MAX_NODES:
			raise ValueError(f"Growth exceeded {MAX_NODES:,} nodes; reduce seasons or resource supply")
		_, p, d, light = best
		origin = node['p']
		for segment in range(1, segments + 1):
			position = add(origin, scale(add(p, scale(origin, -1)), segment / segments))
			index = len(self.nodes)
			child = self._node(parent, axis, position, d, season, order)
			child['tip'] = segment == segments
			child["light"] = field.sample(position)
			self.nodes.append(child)
			self.child_counts.append(0);self.child_counts[parent]+=1
			self.axes[axis]["nodes"].append(index)
			self._occupy(index)
			parent = index
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
		for low, high, enclosing in node.get('foliage_occlusion', ()):
			if not (0 <= low < high <= 1 and type(enclosing) is int and 0 < enclosing < len(nodes)):
				raise ValueError('Invalid foliage enclosure interval or segment')
			if nodes[enclosing]['axis'] == node['axis']:
				raise ValueError('A shoot cannot enclose its own foliage')
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
