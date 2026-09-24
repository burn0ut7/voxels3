"""Seeded Blender tree library. Does not export or modify game assets."""
import bpy
import math
import random
import json
import hashlib
import importlib.util
import sys
from array import array
from pathlib import Path
from mathutils import Vector, Quaternion

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Tools/BlenderTrees"
EVIDENCE = ROOT / "Docs/ValidationEvidence/BlenderTrees"
PREFIX = "OAK_STUDY_"
TAPER = 1.15
BARK_TILE_METRES = 1.8
NEEDLES_PER_METRE = 2000
SPECIES = {
	"Oak": {"name":"English oak", "height":16, "leaf":"lobed", "bark":"bark_brown_02", "tile":1.8, "leaf_density":34.2},
	"Ash": {"name":"Common ash", "height":20, "leaf":"compound", "bark":"japanese_camphor_bark", "tile":1.8, "leaf_density":1.35},
	"Spruce": {"name":"Norway spruce", "height":18, "leaf":"needle", "bark":"pine_bark", "tile":2.0, "leaf_density":1.35},
	"Birch": {"name":"Silver birch", "height":16, "leaf":"triangular", "bark":"japanese_camphor_bark", "tile":1.8, "leaf_density":1.35},
}
TREE_SETTINGS = ("height","spread","girth","lean","upward","droop","branch_density","leaf_density","branch_angle","character","fork_height","growth_direction","overhead_light","crown_bias","root_spread","root_depth","age","competition","light_response","resource")


def growth_module():
	path = OUT / 'growth.py'
	spec = importlib.util.spec_from_file_location('voxels_tree_growth', path)
	module = importlib.util.module_from_spec(spec)
	sys.modules[spec.name] = module
	spec.loader.exec_module(module)
	return module


def growth_recipe(settings):
	module = growth_module()
	return module, module.Recipe(**{key: value for key, value in settings.items() if key in module.Recipe.__dataclass_fields__})


def publish_growth(graph, label, settings, generator_sha256=None):
	"""Publish a completed graph as a cheap, editable-library growth preview."""
	module = growth_module()
	module.validate(graph)
	name = PREFIX + label
	old = bpy.data.collections.get(name)
	if old and (old.get('protected_reference') or not old.get('growth_preview')):
		raise ValueError('Growth previews cannot replace stored source specimens')
	curve = bpy.data.curves.new(name + '_GrowthAxes', 'CURVE')
	curve.dimensions = '3D'
	curve.resolution_u = 1
	curve.bevel_depth = 1.0
	curve.bevel_resolution = 1
	curve.use_fill_caps = True
	for axis in graph['axes']:
		spline = curve.splines.new('POLY')
		spline.points.add(len(axis['nodes']) - 1)
		for index, (point, node_id) in enumerate(zip(spline.points, axis['nodes'])):
			node = graph['nodes'][node_id]
			point.co = (*node['p'], 1.0)
			# The attachment uses this branch's first segment radius, not its
			# parent's larger radius, so tiny laterals do not form swollen cones.
			radius_node = graph['nodes'][axis['nodes'][1]] if index == 0 and axis['parent'] >= 0 else node
			point.radius = radius_node['radius']
	collection = bpy.data.collections.new(name + '_Pending')
	bpy.context.scene.collection.children.link(collection)
	obj = bpy.data.objects.new(name + '_GrowthAxes', curve)
	collection.objects.link(obj)
	obj.color = (.26, .13, .055, 1)
	collection['growth_graph'] = json.dumps(graph, separators=(',', ':'), allow_nan=False)
	collection['growth_preview'] = True
	collection['seed'] = graph['recipe']['seed']
	collection['species'] = graph['recipe']['species']
	collection['stage'] = settings['stage']
	collection['form'] = settings['form']
	collection['settings'] = json.dumps(settings)
	collection['generator_sha256'] = generator_sha256 or hashlib.sha256((OUT / 'growth.py').read_bytes()).hexdigest()
	if old:
		for previous in list(old.objects):
			data = previous.data
			bpy.data.objects.remove(previous, do_unlink=True)
			if isinstance(data, bpy.types.Curve) and data.users == 0:
				bpy.data.curves.remove(data)
		bpy.data.collections.remove(old)
	collection.name = name
	return collection


def preset_settings(species="Oak",stage="Mature",form="Open_Grown"):
	values={"Open_Grown":(16,1,1,0,.55,.35,57,.45,.24,.25),"Woodland":(21,.8,.8,2,.8,.12,44,.35,.47,.2),"Weathered":(14,1.1,1.15,7,.4,.45,59,.65,.17,.55)}[form]
	keys=("height","spread","girth","lean","upward","droop","branch_angle","character","fork_height","crown_bias")
	settings=dict(zip(keys,values));settings.update(branch_density=1.35,leaf_density=SPECIES[species]['leaf_density'],overhead_light=False,growth_direction=-25 if form=="Weathered" else 0,root_spread=1,root_depth=1.2)
	if species!="Oak":
		settings["height"]=SPECIES[species]["height"]*(1.15 if form=="Woodland" else .9 if form=="Weathered" else 1)
		settings.update(branch_density=1.05)
		if species=="Ash":settings.update(upward=.68,droop=.16,branch_angle=48,fork_height=.30)
		elif species=="Spruce":settings.update(spread=1,upward=.28,droop=.5,branch_angle=72,fork_height=.10,character=.25,crown_bias=.12)
		elif species=="Birch":settings.update(girth=.78,upward=.6,droop=.75,branch_angle=48,fork_height=.28,character=.42)
	settings.update(age={"Juvenile":6,"Mature":24,"Large":30}[stage],competition=.65,light_response=.4,resource=1.0)
	# Age changes the number of seasons, not the adult scaffold proportions.
	return settings


def radius_at(radius,end,t,profile=None):
	t=max(0,min(1,t))
	if profile:
		x=t*(len(profile)-1);i=min(len(profile)-2,int(x))
		return profile[i]*(1-(x-i))+profile[i+1]*(x-i)
	return end+(radius-end)*(1-t)**TAPER


def point_on(points, t):
	"""Arc-length parameterization of an already sampled path."""
	lengths = [(b-a).length for a,b in zip(points,points[1:])]
	distance = max(0.0,min(1.0,t))*sum(lengths)
	for i,length in enumerate(lengths):
		if distance <= length or i == len(lengths)-1:
			return points[i].lerp(points[i+1], min(1.0,distance/max(length,1e-8))), (points[i+1]-points[i]).normalized()
		distance -= length
	return points[-1],(points[-1]-points[-2]).normalized()


def smooth_path(controls, steps=5):
	"""Centripetal root curves with monotone height between soil controls."""
	controls = [Vector(p) for p in controls]
	spans=[max((b-a).length**.5,1e-8) for a,b in zip(controls,controls[1:])]
	slopes=[(b.z-a.z)/span for a,b,span in zip(controls,controls[1:],spans)]
	heights=[slopes[0]]
	for i in range(1,len(controls)-1):
		left,right=slopes[i-1],slopes[i]
		# Fritsch-Butland harmonic slopes preserve shallow/deep transitions
		# without a descending root briefly turning upward between controls.
		w1=2*spans[i]+spans[i-1];w2=spans[i]+2*spans[i-1]
		heights.append((w1+w2)/(w1/left+w2/right) if left*right>0 else 0)
	heights.append(slopes[-1])
	result=[]
	for i in range(len(controls)-1):
		b=controls[i];c=controls[i+1]
		a=controls[i-1] if i else b*2-c
		d=controls[i+2] if i+2<len(controls) else c*2-b
		x=max((b-a).length**.5,1e-8);y=max((c-b).length**.5,1e-8);z=max((d-c).length**.5,1e-8)
		m0=(c-b)+y*((b-a)/x-(c-a)/(x+y))
		m1=(c-b)+y*((d-c)/z-(d-b)/(y+z))
		m0.z=heights[i]*y;m1.z=heights[i+1]*y
		for j in range(steps):
			t=j/steps
			result.append(b*(2*t**3-3*t*t+1)+m0*(t**3-2*t*t+t)+c*(-2*t**3+3*t*t)+m1*(t**3-t*t))
	result.append(controls[-1])
	return result


class Geometry:
	def __init__(self,tile=BARK_TILE_METRES):
		self.verts=[];self.face_vertices=array('i');self.face_sizes=array('i');self.uv=array('f');self.mats=array('i');self.part=0
		self.sweeps=[];self.branch_ids=array('i');self.active_branch=-1
		self.tile=tile
		self.radii=[];self.needle_batches=[];self.needle_distance=0;self.needle_length=0
		self.needle_candidate_count=0;self.needle_occluded_count=0;self.needle_exposed_length=0
		self.attachments=[];self.attachment_nodes=[];self.attachment_radii=[]

	def face(self, indices, uv, material=0):
		self.face_vertices.extend(indices);self.face_sizes.append(len(indices))
		self.uv.extend(value for pair in uv for value in pair);self.mats.append(material)
		self.branch_ids.append(self.active_branch)

	def branch(self, path, radius, end, rng, sides=10, root=False,profile=None,parent=0,start_blend=0,path_radii=None,graph_axis=-1,attachment_radius=0,round_tip=False):
		if round_tip:
			# A stopped axis can carry a successor at its last node. Keep that
			# junction inside a rounded wood volume, not on an exposed cut plane.
			path=list(path);path_radii=list(path_radii);tip=path[-1];tangent=(tip-path[-2]).normalized();tip_radius=path_radii[-1]
			for t in (.5,.8660254,1.0):
				path.append(tip+tangent*tip_radius*t);path_radii.append(tip_radius*max(.001,math.sqrt(max(0,1-t*t))))
			end=path_radii[-1]
		# Close a derived collar inside its parent. A full-radius start cap can
		# protrude through the parent surface at an oblique junction.
		if attachment_radius:
			path=list(path);first_length=(path[1]-path[0]).length
			inset=min(first_length*.25,attachment_radius*.6)
			if inset>1e-7:
				t=inset/first_length;path.insert(1,path[0].lerp(path[1],t))
				if path_radii is not None:
					path_radii=list(path_radii);path_radii.insert(1,path_radii[0]*(1-t)+path_radii[1]*t)
				start_blend=inset/sum((b-a).length for a,b in zip(path,path[1:]))
		start=len(self.verts); distance=0.0
		v=0;last_radius=None;repeats=max(1,round(math.tau*radius/self.tile))
		total=sum((b-a).length for a,b in zip(path,path[1:]))
		previous=None
		phase=rng.random()*6.28
		v=phase/math.tau*7
		self.active_branch=len(self.sweeps);frames=[]
		for i,p in enumerate(path):
			if i: distance+=(p-path[i-1]).length
			t=max(0.0,min(1.0,distance/max(total,.001)))
			tangent=(path[min(i+1,len(path)-1)]-path[max(0,i-1)]).normalized()
			if previous is None:
				axis=Vector((1,0,0)) if abs(tangent.x)<.8 else Vector((0,1,0))
				axis=(axis-tangent*axis.dot(tangent)).normalized()
			else:
				axis=(previous-tangent*previous.dot(tangent)).normalized()
			previous=axis
			cross=tangent.cross(axis).normalized()
			r=path_radii[i] if path_radii is not None else radius_at(radius,end,t,profile)
			growth_radius=r
			if start_blend:
				blend=min(1,t/start_blend);r*=max(.001,blend*blend*(3-2*blend))
			if root and (profile is not None or parent==0):r*=1+.32*math.exp(-t*20)
			elif not start_blend and graph_axis<0:
				# A branch collar grows into its parent instead of meeting it as
				# an unexpanded pipe. Keep the swelling local to the attachment.
				r*=1+.26*math.exp(-distance/max(radius*2.2,.015))
			previous_v=v
			if last_radius is not None:v+=(p-path[i-1]).length/(math.tau*(last_radius+r)*.5/repeats)
			last_radius=r
			frames.append({"p":tuple(p),"axis":tuple(axis),"d":distance,"r":r})
			for j in range(sides):
				a=2*math.pi*j/sides
				irregular=1+.036*math.sin(3*a+phase+t*2)+.021*math.sin(5*a-phase+t*4)
				radial=(axis*math.cos(a)+cross*math.sin(a))*r*irregular
				if root and profile is None and parent==0:
					# Structural root shoulders are upright buttresses near the
					# bole, becoming round buried roots farther from it.
					shoulder=math.exp(-distance/max(radius*1.5,.02))
					radial.x*=1-.18*shoulder;radial.y*=1-.18*shoulder
					radial.z*=1+.4*shoulder
				self.verts.append(tuple(p+radial))
				self.radii.append(growth_radius)
			if i:
				for j in range(sides):
					k=(j+1)%sides
					self.face((start+(i-1)*sides+j,start+(i-1)*sides+k,start+i*sides+k,start+i*sides+j),
						((j/sides*repeats,previous_v),((j+1)/sides*repeats,previous_v),((j+1)/sides*repeats,v),(j/sides*repeats,v)),self.part)
		self.face(tuple(start+j for j in reversed(range(sides))),[(0,0)]*sides,self.part)
		last=start+(len(path)-1)*sides
		self.face(tuple(last+j for j in range(sides)),[(0,0)]*sides,self.part)
		self.sweeps.append({"frames":frames,"repeats":repeats,"radius":radius,"end":end,"root":root,"parent":parent,"phase":phase,"graph_axis":graph_axis})

	def leaf(self, base, direction, length, roll, tile, rng):
		axis=direction.normalized()
		ref=Vector((0,0,1))
		if abs(axis.dot(ref))>.95: ref=Vector((1,0,0))
		across=axis.cross(ref).normalized()
		normal=across.cross(axis).normalized()
		across,normal=across*math.cos(roll)+normal*math.sin(roll),normal*math.cos(roll)-across*math.sin(roll)
		start=len(self.verts)
		width=length*rng.uniform(.62,.78)
		camber=rng.uniform(.13,.22);twist=rng.uniform(-.085,.085)
		for row in range(3):
			t=row/2
			for col in range(3):
				x=col-1
				fold=length*(camber*math.sin(math.pi*t)-camber*.72*abs(x)*math.sin(math.pi*t)-.13*t*t+twist*x*t*t)
				self.verts.append(tuple(base+axis*length*t+across*x*width*.5+normal*fold))
		for row in range(2):
			for col in range(2):
				indices=(start+row*3+col,start+row*3+col+1,start+(row+1)*3+col+1,start+(row+1)*3+col)
				uv=[((tile%2+x/2)*.5,(1-tile//2+y/2)*.5) for x,y in [(col,row),(col+1,row),(col+1,row+1),(col,row+1)]]
				self.face(indices,uv,rng.randrange(3) if row==0 and col==0 else self.mats[-1])
		self.attachments.append((start,len(self.verts),tuple(base)))

	def object(self,name,collection,materials):
		import numpy as np
		mesh=bpy.data.meshes.new(name)
		if self.needle_batches:
			points=np.concatenate([batch[0] for batch in self.needle_batches])
			mesh.vertices.add(len(points));mesh.vertices.foreach_set('co',points.ravel())
			for index,key in enumerate(('needle_centerline','needle_forward','needle_scale'),1):
				values=np.concatenate([batch[index] for batch in self.needle_batches])
				mesh.attributes.new(key,'FLOAT_VECTOR','POINT').data.foreach_set('vector',values.ravel())
			values=np.concatenate([batch[4] for batch in self.needle_batches])
			mesh.attributes.new('needle_radius','FLOAT','POINT').data.foreach_set('value',values)
			for index,key,kind in ((5,'growth_node','INT'),(6,'shoot_parameter','FLOAT')):
				values=np.concatenate([batch[index] for batch in self.needle_batches])
				mesh.attributes.new(key,kind,'POINT').data.foreach_set('value',values)
			mesh.attributes.new('needle_rotation','FLOAT_VECTOR','POINT')
			self.needle_batches.clear();mesh.update()
			obj=bpy.data.objects.new(name,mesh);collection.objects.link(obj)
			mesh.materials.append(materials[0]);obj['needle_bearing_length']=self.needle_length
			obj['needle_candidate_count']=self.needle_candidate_count;obj['needle_occluded_count']=self.needle_occluded_count
			obj['needle_exposed_length']=self.needle_exposed_length
			# Instances are added only after all bases have reached final bark.
			return obj
		# Broadleaf crowns contain millions of corners. Publish packed buffers
		# directly instead of flattening nested face and UV tuples into more lists.
		mesh.vertices.add(len(self.verts));mesh.vertices.foreach_set('co',np.asarray(self.verts,np.float32).ravel())
		mesh.loops.add(len(self.face_vertices));mesh.loops.foreach_set('vertex_index',self.face_vertices)
		sizes=np.asarray(self.face_sizes,np.int32);mesh.polygons.add(len(sizes))
		mesh.polygons.foreach_set('loop_start',np.cumsum(sizes,dtype=np.int32)-sizes)
		mesh.polygons.foreach_set('loop_total',sizes);mesh.polygons.foreach_set('use_smooth',np.ones(len(sizes),bool))
		mesh.polygons.foreach_set('material_index',self.mats);mesh.update(calc_edges=True)
		obj=bpy.data.objects.new(name,mesh); collection.objects.link(obj)
		for material in materials: mesh.materials.append(material)
		uv=mesh.uv_layers.new(name="UVMap")
		uv.data.foreach_set("uv",self.uv)
		if len(self.radii)==len(self.verts):mesh.attributes.new("bark_radius","FLOAT","POINT").data.foreach_set("value",self.radii)
		if self.sweeps:
			attribute=mesh.attributes.new("branch_id","INT","FACE")
			attribute.data.foreach_set("value",self.branch_ids)
			obj["sweeps"]=json.dumps(self.sweeps)
		if self.attachments:
			obj['foliage_attachments']=json.dumps(self.attachments,separators=(',',':'))
			obj['foliage_nodes']=json.dumps(self.attachment_nodes,separators=(',',':'))
			obj['foliage_support_radii']=json.dumps(self.attachment_radii,separators=(',',':'))
		return obj

	def blade(self,base,direction,length,roll,kind,rng,surface_normal=None,detailed=True):
		"""Actual curved, serrated blades; ash leaflets and birch leaves are not oak cards."""
		axis=direction.normalized();ref=Vector((0,0,1)) if abs(axis.z)<.9 else Vector((1,0,0))
		if surface_normal is not None:ref=surface_normal
		across=axis.cross(ref).normalized();normal=across.cross(axis).normalized()
		across,normal=across*math.cos(roll)+normal*math.sin(roll),normal*math.cos(roll)-across*math.sin(roll)
		rows=(14 if kind=="triangular" else 12) if detailed else 10;columns=5 if detailed else 3;start=len(self.verts);mat=rng.randrange(3)
		if kind=='triangular' and not detailed:rows=18;columns=5
		for row in range(rows+1):
			t=row/rows
			width=(math.sin(math.pi*t)**.8*.40 if kind=="compound" else min(t/.24,(1-t)/.76)*.77)
			if kind=='triangular' and detailed:width=math.sin(math.pi*t)**.85*.68*(1.2-.55*t)
			elif kind=='triangular':width=math.sin(math.pi*t)**.85*.78*(1.45-.75*t)
			width=max(.001,width)*(1 if row%2 else .965)
			for column in range(columns):
				col=column*2/(columns-1)-1
				fold=length*(.045*math.sin(math.pi*t)-.04*col*col*math.sin(math.pi*t)-.045*t*t)
				self.verts.append(tuple(base+axis*t*length+across*col*width*length*.5+normal*fold))
		for row in range(rows):
			for col in range(columns-1):
				self.face((start+row*columns+col,start+row*columns+col+1,start+(row+1)*columns+col+1,start+(row+1)*columns+col),((col/(columns-1),row/rows),((col+1)/(columns-1),row/rows),((col+1)/(columns-1),(row+1)/rows),(col/(columns-1),(row+1)/rows)),mat)
		if kind!='compound':self.attachments.append((start,len(self.verts),tuple(base)))

	def needle_shoot(self,path,radius,support_radius,density,rng,ranges,node_id):
		"""Sample physical spacing continuously across an axis's internodes."""
		import numpy as np
		if density<=0:return 0
		points=np.asarray(path,np.float64);spans=np.linalg.norm(np.diff(points,axis=0),axis=1)
		distances=np.concatenate(([0.],np.cumsum(spans)));length=float(distances[-1]);self.needle_length+=length
		count=max(0,math.floor((length-self.needle_distance)*density)+1)
		self.needle_candidate_count+=count
		for low,high in ranges:
			self.needle_exposed_length+=float(np.interp(high,np.linspace(0,1,len(points)),distances)-np.interp(low,np.linspace(0,1,len(points)),distances))
		positions=self.needle_distance+np.arange(count)/density
		self.needle_distance+=count/density-length
		if not count:return 0
		indices=np.minimum(np.searchsorted(distances,positions,side='right')-1,len(spans)-1)
		tangents=(points[indices+1]-points[indices])/np.maximum(spans[indices,None],1e-12)
		bases=points[indices]+tangents*(positions-distances[indices])[:,None]
		ref=np.zeros_like(tangents);ref[:,2]=1;ref[np.abs(tangents[:,2])>.9]=(1,0,0)
		across=np.cross(tangents,ref);across/=np.linalg.norm(across,axis=1)[:,None]
		other=np.cross(tangents,across);randoms=np.random.default_rng(rng.getrandbits(32))
		angles=np.arange(count)*2.399963229728653+randoms.uniform(0,math.tau)+randoms.uniform(-.35,.35,count)
		radial=across*np.cos(angles)[:,None]+other*np.sin(angles)[:,None]
		forward=tangents*randoms.uniform(.15,.65,count)[:,None]
		widths=randoms.uniform(.00055,.0008,count)
		scales=np.column_stack((widths,widths,randoms.uniform(.019,.029,count)))
		# Query the surface near this angular position, not a ray that can
		# follow an intersecting whorl limb far away from the living shoot.
		parameters=(indices+(positions-distances[indices])/np.maximum(spans[indices],1e-12))/len(spans)
		local_radius=radius[0]+(radius[1]-radius[0])*parameters
		targets=bases+radial*local_radius[:,None]
		exposed=np.zeros(count,dtype=bool)
		for low,high in ranges:exposed|=(parameters>=low)&(parameters<=high)
		kept=int(exposed.sum());self.needle_occluded_count+=count-kept
		if kept:
			self.needle_batches.append((targets[exposed].astype(np.float32),bases[exposed].astype(np.float32),forward[exposed].astype(np.float32),scales[exposed].astype(np.float32),np.full(kept,support_radius,np.float32),np.full(kept,node_id,np.int32),parameters[exposed].astype(np.float32)))
		return kept


def instance_needles(obj,material):
	"""One fixed physical needle per bark attachment, at every tree age."""
	name=obj.name+'_NeedleSource';mesh=bpy.data.meshes.new(name);vertices=[];faces=[]
	for z,radius in ((0,.6),(.42,1),(1,.035)):
		vertices.extend((math.cos(i*math.pi/2)*radius,math.sin(i*math.pi/2)*radius,z) for i in range(4))
	for row in range(2):
		for i in range(4):faces.append((row*4+i,row*4+(i+1)%4,(row+1)*4+(i+1)%4,(row+1)*4+i))
	faces.extend(((3,2,1,0),(8,9,10,11)));mesh.from_pydata(vertices,[],faces);mesh.materials.append(material)
	collection=bpy.data.collections.get(PREFIX+'NeedleSources')
	if collection is None:
		collection=bpy.data.collections.new(PREFIX+'NeedleSources');bpy.context.scene.collection.children.link(collection)
	prototype=bpy.data.objects.new(name,mesh);collection.objects.link(prototype)
	prototype.hide_render=True;prototype.hide_set(True);prototype['construction_source']=True
	group=bpy.data.node_groups.new(obj.name+'_Needles','GeometryNodeTree')
	group.interface.new_socket(name='Geometry',in_out='INPUT',socket_type='NodeSocketGeometry');group.interface.new_socket(name='Geometry',in_out='OUTPUT',socket_type='NodeSocketGeometry')
	n=group.nodes;l=group.links;entry=n.new('NodeGroupInput');output=n.new('NodeGroupOutput');instances=n.new('GeometryNodeInstanceOnPoints')
	l.new(entry.outputs['Geometry'],instances.inputs['Points'])
	info=n.new('GeometryNodeObjectInfo');info.inputs['Object'].default_value=prototype;info.transform_space='ORIGINAL'
	l.new(info.outputs['Geometry'],instances.inputs['Instance'])
	for attribute,socket in (('needle_rotation','Rotation'),('needle_scale','Scale')):
		field=n.new('GeometryNodeInputNamedAttribute');field.data_type='FLOAT_VECTOR';field.inputs['Name'].default_value=attribute;l.new(field.outputs['Attribute'],instances.inputs[socket])
	l.new(instances.outputs['Instances'],output.inputs['Geometry'])
	modifier=obj.modifiers.new('Living three-dimensional needles','NODES');modifier.node_group=group
	obj['foliage_representation']='Individually bark-bound instanced 12-vertex closed needles; realize instances for mesh export'


def material_setup(species="Oak",stage="Mature",height=16,tag=None):
	# A new seed/habit must never rewrite materials used by a preserved specimen.
	tag=tag or species+"_"+stage
	def fresh(name):
		mat=bpy.data.materials.get(PREFIX+name) or bpy.data.materials.new(PREFIX+name)
		mat.use_nodes=True; mat.node_tree.nodes.clear()
		return mat,mat.node_tree.nodes,mat.node_tree.links

	def image(path,noncolor=False):
		img=bpy.data.images.load(str(path),check_existing=True)
		if noncolor: img.colorspace_settings.name="Non-Color"
		return img

	bark,n,l=fresh(tag+"_Bark")
	output=n.new("ShaderNodeOutputMaterial"); shader=n.new("ShaderNodeBsdfPrincipled")
	l.new(shader.outputs["BSDF"],output.inputs["Surface"])
	uv=n.new("ShaderNodeUVMap");uv.uv_map="UVMap"
	parent_uv=n.new("ShaderNodeUVMap");parent_uv.uv_map="BarkParent"
	weight=n.new("ShaderNodeAttribute");weight.attribute_name="bark_parent_weight"
	paths=OUT/"Textures"
	def calculation(operation,a,b=None):
		node=n.new("ShaderNodeMath");node.operation=operation
		for index,value in enumerate((a,b)):
			if value is None:continue
			if isinstance(value,(int,float)):node.inputs[index].default_value=value
			else:l.new(value,node.inputs[index])
		return node.outputs[0]
	charts=[]
	for coordinate in (uv,parent_uv):
		separate=n.new("ShaderNodeSeparateXYZ");l.new(coordinate.outputs[0],separate.inputs[0])
		tile=calculation("MULTIPLY",separate.outputs["Y"],1/1.37)
		cell=calculation("FLOOR",tile);fraction=calculation("FRACT",tile)
		t=calculation("MINIMUM",1,calculation("MAXIMUM",0,calculation("DIVIDE",calculation("SUBTRACT",fraction,.65),.35)))
		blend=calculation("MULTIPLY",calculation("MULTIPLY",t,t),calculation("SUBTRACT",3,calculation("MULTIPLY",2,t)))
		vectors=[]
		for shift in (0,1):
			index=calculation("ADD",cell,shift);offset=n.new("ShaderNodeCombineXYZ")
			l.new(calculation("FRACT",calculation("ADD",calculation("MULTIPLY",index,.61803398875),.123)),offset.inputs["X"])
			l.new(calculation("FRACT",calculation("ADD",calculation("MULTIPLY",index,.41421356237),.37)),offset.inputs["Y"])
			vector=n.new("ShaderNodeVectorMath");vector.operation="ADD";l.new(coordinate.outputs[0],vector.inputs[0]);l.new(offset.outputs[0],vector.inputs[1]);vectors.append(vector.outputs[0])
		charts.append((coordinate,vectors,blend))
	for suffix,socket,nc in [("diff","Base Color",False),("rough","Roughness",True),("nor_gl","Normal",True)]:
		mixed=n.new("ShaderNodeMixRGB");l.new(weight.outputs["Fac"],mixed.inputs[0])
		for i,(coordinate,vectors,blend) in enumerate(charts):
			patch=n.new("ShaderNodeMixRGB");l.new(blend,patch.inputs[0])
			for index,vector in enumerate(vectors):
				tex=n.new("ShaderNodeTexImage");tex.image=image(paths/f"{SPECIES[species]['bark']}_{suffix}_{'4k' if suffix=='diff' else '2k'}.jpg",nc)
				l.new(vector,tex.inputs["Vector"]);l.new(tex.outputs["Color"],patch.inputs[index+1])
			if socket=="Normal":
				# Convert each tangent-space sample in its own UV frame before blending.
				normal=n.new("ShaderNodeNormalMap");normal.uv_map=coordinate.uv_map;normal.inputs["Strength"].default_value=.32 if stage=="Juvenile" or species=="Birch" else 1
				l.new(patch.outputs[0],normal.inputs["Color"]);l.new(normal.outputs["Normal"],mixed.inputs[i+1])
			else:l.new(patch.outputs[0],mixed.inputs[i+1])
		if socket=="Base Color":
			tone=n.new("ShaderNodeHueSaturation");tone.inputs["Saturation"].default_value=.55;tone.inputs["Value"].default_value=.85
			if species=="Spruce":tone.inputs["Saturation"].default_value=.42;tone.inputs["Value"].default_value=.75
			l.new(mixed.outputs[0],tone.inputs["Color"]);l.new(tone.outputs[0],shader.inputs[socket])
		elif socket=="Normal":
			normalize=n.new("ShaderNodeVectorMath");normalize.operation="NORMALIZE"
			l.new(mixed.outputs[0],normalize.inputs[0]);l.new(normalize.outputs[0],shader.inputs[socket])
		else:l.new(mixed.outputs[0],shader.inputs[socket])
	bark.diffuse_color=(.22,.18,.13,1)
	if stage=="Juvenile" and species!="Birch":
		soft=n.new("ShaderNodeMixRGB");soft.inputs[0].default_value=.65;soft.inputs[2].default_value=(.19,.16,.12,1)
		l.new(shader.inputs["Base Color"].links[0].from_socket,soft.inputs[1]);l.new(soft.outputs[0],shader.inputs["Base Color"])
	if species=="Birch":
		coordinates=n.new("ShaderNodeTexCoord");scale=n.new("ShaderNodeVectorMath");scale.operation="MULTIPLY";scale.inputs[1].default_value=(3,3,65)
		l.new(coordinates.outputs["Object"],scale.inputs[0]);noise=n.new("ShaderNodeTexNoise");noise.inputs["Scale"].default_value=1;noise.inputs["Detail"].default_value=2
		l.new(scale.outputs[0],noise.inputs["Vector"]);marks=calculation("GREATER_THAN",noise.outputs["Fac"],.68)
		if stage!='Juvenile':
			grain=n.new('ShaderNodeTexNoise');grain.inputs['Scale'].default_value=115;grain.inputs['Detail'].default_value=3;l.new(coordinates.outputs['Object'],grain.inputs['Vector'])
			warp=n.new('ShaderNodeVectorMath');warp.operation='MULTIPLY_ADD';warp.inputs[1].default_value=(.28,.28,1.2);warp.inputs[2].default_value=(-.14,-.14,-.6);l.new(grain.outputs['Color'],warp.inputs[0])
			warped=n.new('ShaderNodeVectorMath');warped.operation='ADD';l.new(scale.outputs[0],warped.inputs[0]);l.new(warp.outputs[0],warped.inputs[1]);l.new(warped.outputs[0],noise.inputs['Vector'])
			mark_noise=calculation('ADD',noise.outputs['Fac'],calculation('MULTIPLY',calculation('SUBTRACT',grain.outputs['Fac'],.5),.15))
			marks=calculation('MINIMUM',1,calculation('MAXIMUM',0,calculation('DIVIDE',calculation('SUBTRACT',mark_noise,.65),.10)))
		color=n.new("ShaderNodeMixRGB");color.inputs[1].default_value=(.62,.64,.58,1) if stage!="Juvenile" else (.4,.38,.3,1);color.inputs[2].default_value=(.055,.042,.033,1);l.new(marks,color.inputs[0])
		if stage!='Juvenile':
			patches=n.new('ShaderNodeTexNoise');patches.inputs['Scale'].default_value=5;patches.inputs['Detail'].default_value=3;l.new(coordinates.outputs['Object'],patches.inputs['Vector'])
			paper=n.new('ShaderNodeMixRGB');paper.inputs[1].default_value=(.49,.51,.455,1);paper.inputs[2].default_value=(.65,.665,.60,1);l.new(patches.outputs['Fac'],paper.inputs[0]);l.new(paper.outputs[0],color.inputs[1]);color.inputs[2].default_value=(.12,.105,.085,1)
			bump=n.new('ShaderNodeBump');bump.inputs['Strength'].default_value=.32;bump.inputs['Distance'].default_value=.0012;l.new(calculation('ADD',calculation('MULTIPLY',grain.outputs['Fac'],.22),calculation('MULTIPLY',marks,-.45)),bump.inputs['Height']);l.new(shader.inputs['Normal'].links[0].from_socket,bump.inputs['Normal']);l.new(bump.outputs[0],shader.inputs['Normal'])
		separate=n.new("ShaderNodeSeparateXYZ");l.new(coordinates.outputs["Object"],separate.inputs[0])
		base=calculation("MULTIPLY",calculation("MAXIMUM",0,calculation("SUBTRACT",1,calculation("DIVIDE",separate.outputs["Z"],2.1 if stage=="Large" else .85))),.8 if stage!="Juvenile" else .15)
		root_color=n.new("ShaderNodeMixRGB");l.new(base,root_color.inputs[0]);l.new(color.outputs[0],root_color.inputs[1]);root_color.inputs[2].default_value=(.10,.083,.066,1)
		l.new(root_color.outputs[0],shader.inputs["Base Color"])
		radius=n.new("ShaderNodeAttribute");radius.attribute_name="bark_radius"
		mature=calculation("MINIMUM",1,calculation("MAXIMUM",0,calculation("DIVIDE",calculation("SUBTRACT",radius.outputs["Fac"],.012),.045)))
		age_color=n.new("ShaderNodeMixRGB");l.new(mature,age_color.inputs[0]);age_color.inputs[1].default_value=(.14,.09,.047,1);l.new(root_color.outputs[0],age_color.inputs[2]);l.new(age_color.outputs[0],shader.inputs["Base Color"])
		if stage=='Juvenile':
			l.remove(age_color.inputs[0].links[0]);age_color.inputs[0].default_value=.78
			grain=n.new('ShaderNodeTexNoise');grain.inputs['Scale'].default_value=160;grain.inputs['Detail'].default_value=3;l.new(coordinates.outputs['Object'],grain.inputs['Vector'])
			tint=n.new('ShaderNodeMixRGB');tint.blend_type='MULTIPLY';tint.inputs[0].default_value=1;l.new(age_color.outputs[0],tint.inputs[1]);l.new(calculation('ADD',.82,calculation('MULTIPLY',grain.outputs['Fac'],.18)),tint.inputs[2]);l.new(tint.outputs[0],shader.inputs['Base Color'])
			bump=n.new('ShaderNodeBump');bump.inputs['Strength'].default_value=.18;bump.inputs['Distance'].default_value=.0006;l.new(grain.outputs['Fac'],bump.inputs['Height']);l.new(shader.inputs['Normal'].links[0].from_socket,bump.inputs['Normal']);l.new(bump.outputs[0],shader.inputs['Normal'])
	leaves=[]
	for i,mult in enumerate([(1.0,1.08,.82),(.84,.98,.7),(1.17,1.2,.86)]):
		mat,n,l=fresh(f"{tag}_Leaf_{i}"); mat.diffuse_color=(.13+i*.015,.25+i*.024,.048,1)
		out=n.new("ShaderNodeOutputMaterial");shader=n.new("ShaderNodeBsdfPrincipled")
		if species=='Spruce':
			coordinates=n.new('ShaderNodeTexCoord');noise=n.new('ShaderNodeTexNoise');noise.inputs['Scale'].default_value=7;noise.inputs['Detail'].default_value=2;l.new(coordinates.outputs['Object'],noise.inputs['Vector'])
			tint=n.new('ShaderNodeMixRGB');tint.inputs[1].default_value=(.025,.074,.025,1);tint.inputs[2].default_value=(.05,.135,.043,1);l.new(noise.outputs['Fac'],tint.inputs[0]);l.new(tint.outputs[0],shader.inputs['Base Color'])
			shader.inputs['Roughness'].default_value=.46;shader.inputs['Subsurface Weight'].default_value=.045;l.new(shader.outputs[0],out.inputs['Surface']);return bark,[mat],bark
		if species!="Oak":
			color=(.07,.18,.022,1) if species=="Ash" else ((.07,.19,.029,1) if stage=='Juvenile' else (.10,.25,.025,1))
			shader.inputs["Base Color"].default_value=tuple(c*(.85+i*.13) for c in color[:3])+(1,)
			shader.inputs["Roughness"].default_value=.52
			shader.inputs["Subsurface Weight"].default_value=.06
			uv=n.new("ShaderNodeTexCoord");separate=n.new("ShaderNodeSeparateXYZ");l.new(uv.outputs["UV"],separate.inputs[0])
			center=calculation("ABSOLUTE",calculation("SUBTRACT",separate.outputs["X"],.5));midrib=calculation("LESS_THAN",center,.011)
			phase=calculation("FRACT",calculation("SUBTRACT",calculation("MULTIPLY",separate.outputs["Y"],9),calculation("MULTIPLY",center,3.2)))
			secondary=calculation("LESS_THAN",calculation("MINIMUM",phase,calculation("SUBTRACT",1,phase)),.024)
			vein=calculation("MAXIMUM",midrib,calculation("MULTIPLY",secondary,.70))
			noise=n.new("ShaderNodeTexNoise");noise.inputs["Scale"].default_value=65;noise.inputs["Detail"].default_value=2;l.new(uv.outputs["UV"],noise.inputs["Vector"])
			grain=n.new("ShaderNodeMixRGB");grain.inputs[1].default_value=shader.inputs["Base Color"].default_value;grain.inputs[2].default_value=tuple(c*.72 for c in color[:3])+(1,);l.new(noise.outputs["Fac"],grain.inputs[0])
			veins=n.new("ShaderNodeMixRGB");l.new(grain.outputs[0],veins.inputs[1]);veins.inputs[2].default_value=(.15,.27,.052,1);l.new(calculation("MULTIPLY",vein,.65),veins.inputs[0]);l.new(veins.outputs[0],shader.inputs["Base Color"])
			bump=n.new("ShaderNodeBump");bump.inputs["Strength"].default_value=.22;bump.inputs["Distance"].default_value=.0004;l.new(calculation("ADD",vein,calculation("MULTIPLY",noise.outputs["Fac"],.15)),bump.inputs["Height"]);l.new(bump.outputs[0],shader.inputs["Normal"])
			translucent=n.new("ShaderNodeBsdfTranslucent");l.new(veins.outputs[0],translucent.inputs["Color"])
			mix=n.new("ShaderNodeMixShader");mix.inputs[0].default_value=.25;l.new(shader.outputs[0],mix.inputs[1]);l.new(translucent.outputs[0],mix.inputs[2]);l.new(mix.outputs[0],out.inputs["Surface"])
			leaves.append(mat);continue
		tex=n.new("ShaderNodeTexImage");tex.image=image(ROOT/"Assets/textures/trees/oak_leaf_atlas.png")
		tint=n.new("ShaderNodeMixRGB");tint.blend_type="MULTIPLY";tint.inputs[0].default_value=1;tint.inputs[2].default_value=(*mult,1)
		l.new(tex.outputs["Color"],tint.inputs[1]);l.new(tint.outputs[0],shader.inputs["Base Color"])
		shader.inputs["Roughness"].default_value=.57
		shader.inputs["Subsurface Weight"].default_value=.08
		shader.inputs["Subsurface Radius"].default_value=(.06,.12,.025)
		translucent=n.new("ShaderNodeBsdfTranslucent");l.new(tint.outputs[0],translucent.inputs["Color"])
		leaf_surface=n.new("ShaderNodeMixShader");leaf_surface.inputs[0].default_value=.22
		l.new(shader.outputs[0],leaf_surface.inputs[1]);l.new(translucent.outputs[0],leaf_surface.inputs[2])
		cutout=n.new("ShaderNodeMixShader");transparent=n.new("ShaderNodeBsdfTransparent")
		l.new(tex.outputs["Alpha"],cutout.inputs[0]);l.new(transparent.outputs[0],cutout.inputs[1]);l.new(leaf_surface.outputs[0],cutout.inputs[2])
		l.new(cutout.outputs[0],out.inputs["Surface"])
		mat.surface_render_method="DITHERED"
		leaves.append(mat)
	return bark,leaves,bark


def make_guides(name,paths,scene,offset):
	guide_name=name+"_Guides"
	old=bpy.data.collections.get(guide_name)
	if old:
		for obj in list(old.objects): bpy.data.objects.remove(obj,do_unlink=True)
		bpy.data.collections.remove(old)
	for curve in list(bpy.data.curves):
		if curve.name.startswith(name+"_Guide_") and curve.users==0:bpy.data.curves.remove(curve)
	collection=bpy.data.collections.new(guide_name);scene.collection.children.link(collection)
	for i,(path,radius,end) in enumerate(paths):
		curve=bpy.data.curves.new(name+f"_Guide_{i:02}","CURVE");curve.dimensions="3D"
		spline=curve.splines.new("BEZIER");spline.bezier_points.add(7)
		for j,point in enumerate(spline.bezier_points):
			point.co=point_on(path,j/7)[0];point.handle_left_type="AUTO";point.handle_right_type="AUTO"
		obj=bpy.data.objects.new(curve.name,curve);collection.objects.link(obj);obj.location=offset
		obj["root_radius"]=radius;obj["tip_radius"]=end;obj["order"]=i
		if i:
			trunk=paths[0][0];lengths=[(b-a).length for a,b in zip(trunk,trunk[1:])];travel=0;best=(float("inf"),0)
			for j,length in enumerate(lengths):
				d=trunk[j+1]-trunk[j];t=max(0,min(1,(path[0]-trunk[j]).dot(d)/max(d.length_squared,1e-9)))
				distance=(path[0]-trunk[j].lerp(trunk[j+1],t)).length_squared
				if distance<best[0]:best=(distance,(travel+t*length)/sum(lengths))
				travel+=length
			obj["trunk_attachment"]=best[1]
		obj.hide_render=True;obj.hide_set(True);obj.show_in_front=True
	return collection


def build_foliage(graph,settings):
	"""One graph-derived foliage path for full builds and density adjustments."""
	module=growth_module();species=settings['species'];stage=settings['stage']
	seed=settings['seed'];age=settings['age'];leaf_density=settings['leaf_density']
	leaves=Geometry();petioles=Geometry(SPECIES[species]['tile']);leaf_rng=random.Random(seed+8191)
	children=[[] for _ in graph['nodes']]
	for index,node in enumerate(graph['nodes']):
		if node['parent']>=0:children[node['parent']].append(index)
	child_counts=[len(child) for child in children]
	shoot_paths=[None]+[[Vector(p) for p in path] for path in module.shoot_curves(graph['nodes'],children)[1:]]
	leaf_count=0

	def shoot_foliage(path,radius,support_radius,ranges,node_id):
		nonlocal leaf_count
		if species=="Spruce":
			leaf_count+=leaves.needle_shoot(path,radius,support_radius,NEEDLES_PER_METRE*leaf_density,leaf_rng,ranges,node_id)
			return
		# Broadleaf placement uses arc distance; graph enclosure uses the
		# Hermite sample parameter retained by each derived shoot path.
		import numpy as np
		distances=np.concatenate(([0.],np.cumsum([(b-a).length for a,b in zip(path,path[1:])])))
		ranges=[tuple(float(np.interp(t,np.linspace(0,1,len(path)),distances))/max(float(distances[-1]),1e-12) for t in interval) for interval in ranges]
		# Each candidate owns its appearance stream, even when covered.
		node_rng=random.Random((seed<<32)^node_id^0xB5297A4D)
		shoot_length=float(distances[-1])
		node=graph['nodes'][node_id];parent=graph['nodes'][node['parent']]
		annual_base=not foliage_profile.foliage_by_length or parent['born']!=node['born'] or parent['axis']!=node['axis']
		if species=="Ash":
			count=round(shoot_length/(.12 if stage=='Juvenile' else .16)*leaf_density);phase=node_rng.uniform(0,math.tau)
			if not foliage_profile.foliage_by_length:count=max(1,min(5 if stage=='Juvenile' else 4,count))
			base=.15 if annual_base else .02
			for i in range(count):
				site_rng=random.Random(node_rng.getrandbits(64))
				t=base+(.97-base)*(i+.3)/count
				if not any(low<=t<=high for low,high in ranges):continue
				leaf_start=len(leaves.verts);stem_start=len(petioles.verts)
				p,tangent=point_on(path,t)
				ref=Vector((0,0,1)) if abs(tangent.z)<.9 else Vector((1,0,0));axis=tangent.cross(ref).normalized();other=tangent.cross(axis)
				angle=phase+i*math.pi+site_rng.uniform(-.25,.25);direction=(axis*math.cos(angle)+other*math.sin(angle)+tangent*.4+Vector((0,0,.15))).normalized()
				length=site_rng.uniform(.23,.33);end=p+direction*length;petioles.branch([p,end],.0014,.0005,site_rng,4)
				across=direction.cross(Vector((0,0,1))).normalized();normal=across.cross(direction).normalized();twist=site_rng.uniform(-.45,.45)
				across,normal=across*math.cos(twist)+normal*math.sin(twist),normal*math.cos(twist)-across*math.sin(twist)
				pairs=site_rng.randint(3,4)
				for pair in range(pairs):
					t=.18+pair*.67/pairs;anchor=p+direction*length*t
					for sign in (-1,1):
						leafdir=(across*sign+direction*site_rng.uniform(.28,.55)+normal*site_rng.uniform(-.15,.15)).normalized()
						leaves.blade(anchor,leafdir,site_rng.uniform(.09,.13)*(1-.2*t)*(1 if stage=='Juvenile' else 1.08),site_rng.uniform(-.25,.25),'compound',site_rng,normal,stage=='Juvenile');leaf_count+=1
				leaves.blade(end-direction*.025,direction,.12,site_rng.uniform(-.2,.2),'compound',site_rng,normal,stage=='Juvenile');leaf_count+=1
				leaves.attachments.append((leaf_start,len(leaves.verts),tuple(p)));petioles.attachments.append((stem_start,len(petioles.verts),tuple(p)))
			return
		# A growth node is a bud site, not necessarily an entire annual shoot.
		# Preserve old stored profiles while new internode profiles allocate
		# foliage by physical length instead of multiplying a spray per node.
		count=round(node_rng.randint(9,14)*leaf_density*(shoot_length/foliage_profile.extension if foliage_profile.foliage_by_length else 1))
		shoot_phase=node_rng.uniform(0,math.tau)
		base=(.30 if species=='Oak' else .1) if annual_base else .02
		for i in range(count):
			site_rng=random.Random(node_rng.getrandbits(64))
			# Leaf-bearing current growth forms terminal sprays; older interior
			# shoot bases stay visible between them.
			t=base+(.98-base)*(i+site_rng.random()*.45)/count
			if not any(low<=t<=high for low,high in ranges):continue
			p,tangent=point_on(path,t)
			a=shoot_phase+i*2.399+site_rng.uniform(-.65,.65)
			side=Vector((math.cos(a),math.sin(a),site_rng.uniform(-.24,.56)))
			direction=(tangent*.32+side).normalized()
			length=site_rng.uniform(.16,.25)
			if species=="Birch":leaves.blade(p,direction,length*(.32 if stage=='Juvenile' else .46),site_rng.uniform(-.6,.6),'triangular',site_rng,detailed=stage=='Juvenile')
			else:leaves.leaf(p,direction,length*.78,site_rng.uniform(-1.1,1.1),site_rng.randrange(4),site_rng)
			leaf_count+=1
		# One terminal spray per annual growth unit, not per internode.
		if foliage_profile.foliage_by_length and any(graph['nodes'][child]['axis']==node['axis'] and graph['nodes'][child]['born']==node['born'] for child in children[node_id]):return
		for i in range(3):
			site_rng=random.Random(node_rng.getrandbits(64))
			t=.93+i*.02
			if not any(low<=t<=high for low,high in ranges):continue
			p,tangent=point_on(path,t)
			direction=(tangent+Vector((site_rng.uniform(-.6,.6),site_rng.uniform(-.6,.6),site_rng.uniform(-.1,.5)))).normalized()
			if species=="Birch":leaves.blade(p,direction,site_rng.uniform(.05,.075) if stage=='Juvenile' else site_rng.uniform(.075,.11),site_rng.uniform(-.6,.6),'triangular',site_rng,detailed=stage=='Juvenile')
			else:leaves.leaf(p,direction,site_rng.uniform(.125,.18),site_rng.uniform(-1.5,1.5),site_rng.randrange(4),site_rng)
			leaf_count+=1

	# Foliage follows living shoots and fine twigs in the graph; no recursive twig
	# generator can create a second crown unrelated to the simulated branches.
	yield 'Placing foliage on living shoots'
	foliage_profile=module.Species(**graph['profile'])
	exposure=[[] for _ in graph['nodes']]
	for index,intervals in module.foliage_exposure(graph['nodes'],age,foliage_profile):
		if index>=0:exposure[index]=intervals
		if index<0 or index%128==0:yield 'Checking foliage exposure along the branches'
	for axis in graph['axes']:
		leaves.needle_distance=.5/max(NEEDLES_PER_METRE*leaf_density,1e-12)
		for parent_id,node_id in zip(axis['nodes'],axis['nodes'][1:]):
			node=graph['nodes'][node_id]
			radii=module.segment_radii(graph['nodes'],node_id,child_counts)
			if module.supports_foliage(node,age,foliage_profile,max(radii)):
				starts=(len(leaves.attachments),len(petioles.attachments))
				shoot_foliage(shoot_paths[node_id],radii,max(radii),list(module.foliage_ranges(node,exposure[node_id])),node_id)
				for geometry,start in zip((leaves,petioles),starts):
					count=len(geometry.attachments)-start
					geometry.attachment_nodes.extend([node_id]*count);geometry.attachment_radii.extend([max(radii)]*count)
			if node_id%128==0:yield 'Placing foliage on living shoots'
	return leaves,petioles,leaf_count


def build_tree(form="Open_Grown",seed=1701,offset=(0,0,0),height=16,spread=1.0,girth=1.0,lean=0.0,upward=.55,droop=.35,branch_density=1.35,leaf_density=None,branch_angle=57,character=.45,fork_height=.24,growth_direction=0,crown_bias=.25,root_spread=1,root_depth=1.2,label=None,species="Oak",stage="Mature",age=24,competition=.65,light_response=.4,resource=1.0,overhead_light=False,graph=None):
	if leaf_density is None:leaf_density=SPECIES[species]['leaf_density']
	settings={key:value for key,value in locals().copy().items() if key in TREE_SETTINGS or key in ('species','stage','form','seed')}
	module,recipe=growth_recipe(settings)
	if graph is None:
		job=module.Simulation(recipe)
		while job.step():pass
		graph=job.finish()
	module.validate(graph)
	if module.Recipe(**graph['recipe']) != recipe:raise ValueError('Growth controls changed; simulate again before building geometry')
	rng=random.Random(seed)
	scene=bpy.context.scene
	name=PREFIX+(label or f"{species}_{stage}_{form}_{seed}")
	if bpy.data.collections.get(name) and bpy.data.collections[name].get("protected_reference"):raise ValueError("This oak is a preserved reference. Generate a new named specimen to edit.")
	# Meshing scale follows grown wood, not the potential adult height. Otherwise
	# an eighteen-season sapling loses every side limb to adult voxel thresholds.
	resolution=min(1,max(.08,graph['nodes'][0]['radius']/.3))
	# Primary limbs remain first for the existing s&box motion packing. The
	# complete graph retains all finer ancestry, independently of mesh grouping.
	# Curved reference sweeps retain every graph point for bark and motion.
	# The union surface owns visible wood; foliage binds to that finished
	# surface after local intersection rounding and bark displacement.
	children=[[] for _ in graph['nodes']]
	for index,node in enumerate(graph['nodes']):
		if node['parent']>=0:children[node['parent']].append(index)
	child_counts=[len(child) for child in children]
	shoot_paths=module.shoot_curves(graph['nodes'],children)
	axis_paths=[]
	for axis in graph['axes']:
		path=[];sampled_radii=[]
		for node_id in axis['nodes'][1:]:
			r0,r1=module.segment_radii(graph['nodes'],node_id,child_counts)
			path.extend(Vector(p) for p in shoot_paths[node_id][:-1])
			sampled_radii.extend(r0+(r1-r0)*sample/8 for sample in range(8))
		path.append(Vector(shoot_paths[axis['nodes'][-1]][-1]));sampled_radii.append(graph['nodes'][axis['nodes'][-1]]['radius'])
		axis_paths.append((path,sampled_radii))
	shoot_paths=[None]+[[Vector(p) for p in path] for path in shoot_paths[1:]]
	# A stopped shoot with one successor is a continuous limb, not a fork.
	# Transport one cross-section frame through that bend instead of meeting
	# two independent caps there. The continuous main stem owns trunk motion.
	continuations={}
	for i,axis in enumerate(graph['axes']):
		following=children[axis['nodes'][-1]]
		if len(following)==1:
			child=graph['nodes'][following[0]]['axis']
			continuations[i]=child
	continued=set(continuations.values());axis_chains={};axis_heads={}
	for i in range(len(axis_paths)):
		if i in continued:continue
		chain=[i];path=list(axis_paths[i][0]);radii=list(axis_paths[i][1])
		while chain[-1] in continuations:
			child=continuations[chain[-1]];chain.append(child)
			path.extend(axis_paths[child][0][1:]);radii.extend(axis_paths[child][1][1:])
		axis_paths[i]=(path,radii);axis_chains[i]=chain
		for member in chain:axis_heads[member]=i
	# Wind ownership follows branch ancestry, not the obsolete voxel radius
	# cutoff: a thick supporting trunk must not erase its lateral motion groups.
	primary=[0]+[i for i,axis in enumerate(graph['axes']) if i not in continued and axis['parent']>=0 and axis_heads[axis['parent']]==0]
	paths=[(axis_paths[i][0],axis_paths[i][1][0],axis_paths[i][1][-1]) for i in primary]
	yield 'Building branch surfaces'
	make_guides(name,paths,scene,offset)
	old=bpy.data.collections.get(name)
	if old:
		for obj in list(old.objects): bpy.data.objects.remove(obj,do_unlink=True)
		bpy.data.collections.remove(old)
	collection=bpy.data.collections.new(name);scene.collection.children.link(collection);collection['stage']=stage
	collection['growth_graph']=json.dumps(graph,separators=(',',':'))
	collection['primary_count']=len(primary)
	bark,leafmats,twigmat=material_setup(species,stage,height,name.removeprefix(PREFIX))
	wood=Geometry(SPECIES[species]['tile'])
	trunk,base_radius,trunk_end=paths[0]
	# Resample radius by arc length because graph internodes need not be equal.
	profiles=[]
	for path,radii in axis_paths:
		lengths=[(b-a).length for a,b in zip(path,path[1:])];total=sum(lengths);profile=[]
		for sample in range(65):
			distance=total*sample/64;travel=0
			for index,length in enumerate(lengths):
				if distance<=travel+length or index==len(lengths)-1:
					t=max(0,min(1,(distance-travel)/max(length,1e-9)))
					profile.append(radii[index]*(1-t)+radii[index+1]*t);break
				travel+=length
		profiles.append(profile)
	trunk_profile=profiles[0]
	guide_objects=sorted(bpy.data.collections[name+'_Guides'].objects,key=lambda o:o['order'])
	guide_objects[0]['radius_profile']=trunk_profile
	axis_to_wood={}
	order=primary+[i for i in range(len(axis_paths)) if i not in primary and i not in continued]
	for position,axis_id in enumerate(order):
		path,radii=axis_paths[axis_id];radius,end=radii[0],radii[-1]
		if axis_id==0 and stage=='Juvenile':
			# Continue the young bole below grade through its root collar. Roots
			# crossing an exposed soil-level end cap create sliver solids there.
			path=[path[0]-(path[1]-path[0]).normalized()*radius*2,*path];radii=[radius,*radii]
		parent_axis=graph['axes'][axis_id]['parent']
		while parent_axis>=0 and parent_axis not in axis_to_wood:parent_axis=graph['axes'][parent_axis]['parent']
		parent=axis_to_wood.get(parent_axis,0)
		wood.part=0 if axis_id==0 else 1
		attachment=graph['nodes'][graph['axes'][axis_id]['attachment']]['radius'] if axis_id else 0
		wood.branch(path,radius,end,rng,16 if axis_id==0 else 10,axis_id==0,profiles[axis_id],parent,path_radii=radii,graph_axis=axis_id,attachment_radius=attachment,round_tip=True)
		wood.sweeps[-1]['graph_axes']=axis_chains[axis_id]
		for member in axis_chains[axis_id]:axis_to_wood[member]=wood.active_branch
		if position%64==63:yield f'Branch surfaces {position+1} / {len(order)}'
	yield 'Building roots'
	# Roots have their own random stream; changing their controls preserves the crown.
	root_rng=random.Random(seed+4093);root_paths=[]
	wood.part=2;root_count=root_rng.randint(5,7);root_phase=root_rng.uniform(0,math.tau)
	# Attach every root collar inside the bole. A fixed below-ground origin
	# leaves small juvenile roots disconnected from the trunk at soil level.
	root_origin=min(trunk,key=lambda p:abs(p.z)).copy();root_origin.z=base_radius*1.1
	root_drop=root_origin.z+(base_radius*.45 if stage=="Juvenile" else 0)
	def root_mesh(path,radius,end,sides,parent=0):
		end=min(end,radius*.3)
		wood.branch(path,radius,end,root_rng,sides,True,parent=parent,attachment_radius=wood.sweeps[parent]['radius'])
		return wood.active_branch
	for i in range(root_count):
		a=root_phase+i*math.tau/root_count+root_rng.uniform(-.42,.42)
		length=base_radius*root_rng.uniform(3.5,8)*root_spread
		depth=root_depth*root_rng.uniform(.8,1.15)
		d=Vector((math.cos(a),math.sin(a),0));side=Vector((-d.y,d.x,0))
		turn=side*length*root_rng.uniform(-.28,.28)
		radius=base_radius*root_rng.uniform(.36,.70) if stage!='Juvenile' else base_radius*root_rng.uniform(.34,.47)
		shallow=min(depth*.2,radius*.25)
		# Let the buttress become a shallow structural root before descending.
		# Length fractions keep controls ordered even at minimum root spread.
		path=smooth_path([root_origin,root_origin+d*length*.22+Vector((0,0,-root_drop-shallow*.4)),root_origin+d*length*.52+turn*.5+Vector((0,0,-root_drop-shallow)),root_origin+d*length*.8+turn+Vector((0,0,-root_drop-depth*.42)),root_origin+d*length+turn*.8+Vector((0,0,-root_drop-depth))],6)
		root_parent=root_mesh(path,radius,.012,16);root_paths.append(path)
		for j in range(2):
			t=.4+j*.26;p,tangent=point_on(path,t)
			angle=a+(-1 if j==0 else 1)*root_rng.uniform(.5,.95)
			direction=Vector((math.cos(angle),math.sin(angle),0))
			reach=length*root_rng.uniform(.30,.46)
			# Begin along the parent before turning away, so the secondary root
			# grows out of its supporting wood instead of forming an elbow lip.
			child=smooth_path([p,p+tangent*reach*.28,p+direction*reach*.65+Vector((0,0,-depth*.18)),p+direction*reach+Vector((0,0,-depth*.35))],5)
			root_mesh(child,radius_at(radius,.012,t)*.62,.008,10,root_parent);root_paths.append(child)
	wood.part=1
	leaves,petioles,leaf_count=yield from build_foliage(graph,settings)
	branch_count=len(graph['axes'])
	yield 'Creating source meshes'
	wood_obj=wood.object(name+"_Wood",collection,[bark,bark,bark])
	leaf_obj=leaves.object(name+"_Leaves",collection,leafmats)
	if petioles.verts:petioles.object(name+'_Petioles',collection,[twigmat])
	for obj in collection.objects: obj.location=offset
	wood_obj["form"]=form;wood_obj["seed"]=seed
	wood_obj["branch_count"]=branch_count;leaf_obj["leaf_count"]=leaf_count
	wood_obj["root_count"]=len(root_paths)
	collection["root_paths"]=json.dumps([[tuple(p) for p in path] for path in root_paths])
	collection["seed"]=seed;collection["form"]=form
	collection["species"]=species;collection["stage"]=stage;collection['primary_selection']='main_stem_laterals'
	collection["bark_asset"]=SPECIES[species]["bark"];collection["bark_relief_factor"]=(.18 if stage=="Juvenile" else .35 if species=="Birch" else 1)
	collection["generator_sha256"]=hashlib.sha256((OUT/"build_oak_studies.py").read_bytes()+(OUT/"growth.py").read_bytes()+(OUT/'surface.py').read_bytes()).hexdigest()
	collection["settings"]=json.dumps(settings)
	build_proxies(name,paths,scene,offset,trunk_profile,resolution)
	for mesh in list(bpy.data.meshes):
		if mesh.name.startswith(name+'_') and mesh.users==0:bpy.data.meshes.remove(mesh)
	print(json.dumps({"form":form,"branches":branch_count,"roots":len(root_paths),"leaves":leaf_count,"wood_vertices":len(wood.verts),"leaf_vertices":len(leaves.verts)}))
	return collection


def closest_triangle_points(points, triangles):
	"""Refine BVH candidates in double precision on slender branch triangles."""
	import numpy as np
	points=np.asarray(points,np.float64);triangles=np.asarray(triangles,np.float64)
	a,b,c=triangles[:,0],triangles[:,1],triangles[:,2];u=b-a;v=c-a;normal=np.cross(u,v)
	denominator=np.sum(normal*normal,axis=1)
	if not np.all(np.isfinite(denominator)&(denominator>1e-40)):
		raise ValueError('Foliage attachment selected a degenerate bark triangle')
	delta=points-a;safe=denominator
	s=np.sum(np.cross(delta,v)*normal,axis=1)/safe;t=np.sum(np.cross(u,delta)*normal,axis=1)/safe
	inside=(s>=0)&(t>=0)&(s+t<=1)&(denominator>1e-40)
	result=points-normal*(np.sum(delta*normal,axis=1)/safe)[:,None]
	distance2=np.where(inside,np.sum((points-result)**2,axis=1),np.inf)
	for start,end in ((a,b),(b,c),(c,a)):
		edge=end-start;fraction=np.clip(np.sum((points-start)*edge,axis=1)/np.maximum(np.sum(edge*edge,axis=1),1e-40),0,1)
		candidate=start+edge*fraction[:,None];candidate_distance2=np.sum((points-candidate)**2,axis=1);closer=candidate_distance2<distance2
		result[closer]=candidate[closer];distance2=np.minimum(distance2,candidate_distance2)
	return result,normal/np.sqrt(safe)[:,None]


def resolve_wood_tessellation(obj):
	"""Choose non-conflicting diagonals where joined n-gons share several arcs."""
	import bmesh
	import numpy as np
	mesh=bmesh.new();mesh.from_mesh(obj.data)
	try:
		mesh.verts.index_update();mesh.faces.index_update();mesh.normal_update()
		if any(not edge.is_manifold for edge in mesh.edges):raise ValueError('Wood is open before tessellation')
		fixed=0;previous=None
		while True:
			mesh.faces.index_update();mesh.faces.ensure_lookup_table()
			triangles=mesh.calc_loop_triangles()
			indices=np.array([[loop.vert.index for loop in triangle] for triangle in triangles],dtype=np.int32)
			owners=np.tile(np.array([triangle[0].face.index for triangle in triangles],np.int32),3)
			edges=np.sort(np.concatenate((indices[:,[0,1]],indices[:,[1,2]],indices[:,[2,0]])),axis=1)
			unique,counts=np.unique(edges,axis=0,return_counts=True)
			bad=unique[counts!=2]
			if not len(bad):break
			if previous is not None and len(bad)>=previous:raise ValueError('Wood tessellation repair did not reduce conflicting diagonals')
			previous=len(bad);pair=bad[0]
			candidates={mesh.faces[int(index)] for index in owners[np.all(edges==pair,axis=1)]}
			replacement=None
			for face in sorted(candidates,key=lambda item:(len(item.verts),item.index)):
				if len(face.verts)<=3:continue
				vertices=list(face.verts);ids={v.index for v in vertices}
				boundary={tuple(sorted((vertices[i-1].index,v.index))) for i,v in enumerate(vertices)}
				other=(owners!=face.index)&np.isin(edges[:,0],list(ids))&np.isin(edges[:,1],list(ids))
				reserved={tuple(edge) for edge in edges[other]}-boundary
				axis=max(range(3),key=lambda i:abs(face.normal[i]));axes=[i for i in range(3) if i!=axis]
				points={v:(float(v.co[axes[0]]),float(v.co[axes[1]])) for v in vertices}
				area=math.fsum(points[vertices[i-1]][0]*points[v][1]-points[v][0]*points[vertices[i-1]][1] for i,v in enumerate(vertices))
				if area==0:continue
				sign=1 if area>0 else -1
				extent=max(max(p[i] for p in points.values())-min(p[i] for p in points.values()) for i in range(2))
				epsilon=extent*extent*1e-12
				def turn(a,b,c):
					a,b,c=points[a],points[b],points[c]
					return sign*((b[0]-a[0])*(c[1]-a[1])-(b[1]-a[1])*(c[0]-a[0]))
				remaining=vertices.copy();result=[]
				while len(remaining)>3:
					for index,b in enumerate(remaining):
						a,c=remaining[index-1],remaining[(index+1)%len(remaining)]
						if tuple(sorted((a.index,c.index))) in reserved or turn(a,b,c)<=epsilon:continue
						if any(turn(a,b,p)>=-epsilon and turn(b,c,p)>=-epsilon and turn(c,a,p)>=-epsilon for p in remaining if p not in (a,b,c)):continue
						result.append((a,b,c));remaining.pop(index);break
					else:break
				if len(remaining)!=3 or turn(*remaining)<=epsilon:continue
				if any(tuple(sorted((remaining[i-1].index,v.index))) in reserved for i,v in enumerate(remaining)):continue
				result.append(tuple(remaining));replacement=(face,result);break
			if replacement is None:raise ValueError('Joined wood polygon has no non-conflicting triangulation')
			face,result=replacement;loops={loop.vert:loop for loop in face.loops}
			for vertices in result:
				triangle=mesh.faces.new(vertices);triangle.copy_from(face)
				for loop in triangle.loops:loop.copy_from(loops[loop.vert])
			mesh.faces.remove(face);fixed+=1
		if fixed:
			if any(not edge.is_manifold for edge in mesh.edges) or any(not vertex.is_manifold for vertex in mesh.verts):
				raise ValueError('Wood tessellation changed manifold topology')
			mesh.normal_update()
			mesh.to_mesh(obj.data);obj.data.update()
		return fixed
	finally:mesh.free()


def fuse_wood(form="Open_Grown"):
	name=PREFIX+form
	obj=bpy.data.objects[name+"_Wood"]
	collection=bpy.data.collections[name]
	if collection.get("protected_reference"):raise ValueError("Preserved reference geometry cannot be rebuilt")
	obj["bark_asset"]=collection.get("bark_asset","japanese_camphor_bark")
	obj["bark_relief_factor"]=collection.get("bark_relief_factor",1)
	source=obj.copy();source.name=name+"_UV_Source"
	collection.objects.link(source);source.hide_render=True;source.hide_set(True)
	for other in bpy.context.selected_objects: other.select_set(False)
	obj.select_set(True);bpy.context.view_layer.objects.active=obj
	yield 'Building continuous branch volumes'
	spec=importlib.util.spec_from_file_location('voxels_tree_surface',OUT/'surface.py');surface=importlib.util.module_from_spec(spec);spec.loader.exec_module(surface)
	bm,statistics=yield from surface.branch_surface(source)
	mesh=bpy.data.meshes.new(name+'_ConnectedSurface')
	bm.to_mesh(mesh);bm.free();mesh.update()
	for material in obj.data.materials:mesh.materials.append(material)
	obj.data=mesh
	for key,value in statistics.items():collection[key]=value
	collection['welded_faces']=len(mesh.polygons)
	collection['detailed_faces']=len(mesh.polygons);collection['surface_method']='solid_union'
	yield 'Preparing bark projection'
	yield from bind_fused_uv(obj,source)
	yield 'Sampling bark relief'
	displace_bark(obj)
	yield 'Checking rendered wood triangles'
	collection['resolved_wood_polygons']=resolve_wood_tessellation(obj)
	collection['foliage_attachment_max_offset']=yield from bind_foliage(obj,[bpy.data.objects.get(name+suffix) for suffix in ('_Leaves','_Petioles')])
	for polygon in obj.data.polygons: polygon.use_smooth=True
	# Retain editable branch source in a clearly labelled hidden object.
	source.hide_viewport=True
	yield from split_wood_parts(name,obj,source,collection)
	print(json.dumps({"fused":form,"vertices":len(obj.data.vertices),"polygons":len(obj.data.polygons)}))
	obj.hide_render=True;obj.hide_set(True);obj["construction_source"]=True


def bind_foliage(obj,foliage_objects):
	# Bind each leaf (or complete compound leaf and petiole) to the final wood.
	# Surface smoothing must not leave attachment points on a different curve.
	yield 'Attaching foliage to the finished wood'
	from mathutils.bvhtree import BVHTree
	import numpy as np
	# Refresh tessellation after relief before binding to the triangles that
	# are actually rendered. Cached pre-displacement quad diagonals can differ.
	obj.data.calc_loop_triangles()
	wood_coordinates=np.empty((len(obj.data.vertices),3),np.float32);obj.data.vertices.foreach_get('co',wood_coordinates.ravel())
	wood_triangles=np.empty((len(obj.data.loop_triangles),3),np.int32);obj.data.loop_triangles.foreach_get('vertices',wood_triangles.ravel())
	tree=BVHTree.FromPolygons(wood_coordinates.tolist(),wood_triangles.tolist(),all_triangles=True);max_offset=0.0
	triangle_min=wood_coordinates[wood_triangles].min(axis=1)
	triangle_max=wood_coordinates[wood_triangles].max(axis=1)
	wood_radii=np.empty(len(obj.data.vertices),np.float32);obj.data.attributes['bark_radius'].data.foreach_get('value',wood_radii)

	def nearest_bark(targets,limits):
		faces=np.array([tree.find_nearest(Vector(point))[2] for point in targets],np.int32)
		points,normals=closest_triangle_points(targets,wood_coordinates[wood_triangles[faces]])
		distances=np.linalg.norm(points-targets,axis=1)
		misses=np.flatnonzero(distances>limits)
		# Float BVH candidate selection can miss slender branch faces. Refine
		# only failed candidates against every triangle whose bounds can improve
		# that distance. The native candidate gives an upper bound, not proof
		# that its face is the actual nearest surface.
		for index in misses:
			target=np.asarray(targets[index],np.float64)
			gap=np.maximum(np.maximum(triangle_min-target,target-triangle_max),0)
			candidates=np.flatnonzero(np.sum(gap*gap,axis=1)<=(max(limits[index],distances[index])+1e-7)**2)
			if not len(candidates):continue
			triangles=wood_coordinates[wood_triangles[candidates]].astype(np.float64)
			normal=np.cross(triangles[:,1]-triangles[:,0],triangles[:,2]-triangles[:,0])
			valid=np.sum(normal*normal,axis=1)>1e-40
			triangles=triangles[valid];candidates=candidates[valid]
			if not len(triangles):continue
			refined,directions=closest_triangle_points(target[None,:],triangles)
			best=int(np.argmin(np.sum((refined-target)**2,axis=1)))
			points[index]=refined[best];normals[index]=directions[best];faces[index]=candidates[best]
		# A fine twig can emerge through an expanded trunk/collar. An anchor
		# inside that closed union binds to its surrounding wood, whose radius
		# is larger than the twig's. Exterior anchors keep the original bound;
		# a missing branch must not attach its foliage across an empty gap.
		inside=np.sum((targets-points)*normals,axis=1)<0
		surface_radii=wood_radii[wood_triangles[faces]].max(axis=1)
		allowed=np.where(inside,np.maximum(limits,surface_radii),limits)
		embedded=int(np.count_nonzero((np.linalg.norm(points-targets,axis=1)>limits)&inside))
		return points,normals,allowed,len(misses),embedded

	for foliage in foliage_objects:
		if foliage is not None and foliage.data.attributes.get('needle_centerline'):
			mesh=foliage.data;count=len(mesh.vertices)
			coordinates=np.empty((count,3),np.float32);mesh.vertices.foreach_get('co',coordinates.ravel())
			origins=np.empty_like(coordinates);mesh.attributes['needle_centerline'].data.foreach_get('vector',origins.ravel())
			forward=np.empty_like(coordinates);mesh.attributes['needle_forward'].data.foreach_get('vector',forward.ravel())
			radii=np.empty(count,np.float32);mesh.attributes['needle_radius'].data.foreach_get('value',radii)
			rotations=np.empty_like(coordinates);minimum_facing=1.;maximum_distance=0.;maximum_center_distance=0.;worst_attachment=None;fallbacks=0;embedded=0
			for start in range(0,count,4096):
				end=min(count,start+4096);targets=coordinates[start:end].copy()
				points,normals,allowed,fallback_count,enclosed=nearest_bark(targets,np.maximum(radii[start:end]*4,.002));fallbacks+=fallback_count;embedded+=enclosed
				distances=np.linalg.norm(points-targets,axis=1);invalid=np.flatnonzero(distances>allowed)
				if len(invalid):
					index=start+int(invalid[0]);raise ValueError(f'Needle {index} has no local bark attachment at {tuple(float(v) for v in coordinates[index])}')
				local=int(distances.argmax());index=start+local;distance=float(distances[local])
				if distance>maximum_distance:
					maximum_distance=distance;worst_attachment={'index':index,'target':targets[local].tolist(),'centerline':origins[index].tolist(),'base':points[local].tolist(),'support_radius':float(radii[index])}
				maximum_center_distance=max(maximum_center_distance,float(np.linalg.norm(points-origins[start:end],axis=1).max()))
				coordinates[start:end]=points
				directions=normals+forward[start:end];directions/=np.linalg.norm(directions,axis=1)[:,None]
				minimum_facing=min(minimum_facing,float(np.sum(directions*normals,axis=1).min()))
				rotations[start:end,0]=0;rotations[start:end,1]=np.arccos(np.clip(directions[:,2],-1,1));rotations[start:end,2]=np.arctan2(directions[:,1],directions[:,0])
				yield f'Attaching individual needles {end} / {count}'
			mesh.vertices.foreach_set('co',coordinates.ravel());mesh.attributes['needle_rotation'].data.foreach_set('vector',rotations.ravel());mesh.update()
			foliage['needle_minimum_outward_dot']=minimum_facing;foliage['needle_maximum_attachment_distance']=maximum_distance
			foliage['bark_candidate_fallbacks']=fallbacks
			foliage['embedded_bark_attachments']=embedded
			foliage['needle_maximum_center_distance']=maximum_center_distance;foliage['needle_worst_attachment']=json.dumps(worst_attachment)
			for key in ('needle_centerline','needle_forward','needle_radius'):mesh.attributes.remove(mesh.attributes[key])
			instance_needles(foliage,mesh.materials[0]);max_offset=max(max_offset,maximum_distance)
			continue
		if foliage is None or not foliage.get('foliage_attachments'):continue
		coordinates=np.empty(len(foliage.data.vertices)*3,np.float32);foliage.data.vertices.foreach_get('co',coordinates);coordinates=coordinates.reshape(-1,3)
		bound=[];attachments=json.loads(foliage['foliage_attachments'])
		radii=json.loads(foliage['foliage_support_radii'])
		if len(radii)!=len(attachments):raise ValueError('Leaf attachment support metadata is incomplete')
		maximum_distance=0.0;fallbacks=0;embedded=0
		for first in range(0,len(attachments),4096):
			batch=attachments[first:first+4096];anchors=np.array([a for _,_,a in batch],np.float64)
			points,_,allowed,fallback_count,enclosed=nearest_bark(anchors,np.maximum(np.asarray(radii[first:first+len(batch)])*4,.002));fallbacks+=fallback_count;embedded+=enclosed
			distances=np.linalg.norm(points-anchors,axis=1)
			invalid=np.flatnonzero(distances>allowed)
			if len(invalid):raise ValueError(f'Leaf {first+int(invalid[0])} has no local bark attachment')
			maximum_distance=max(maximum_distance,float(distances.max()))
			for (start,end,anchor),point in zip(batch,points):
				delta=point-anchor;coordinates[start:end]+=delta;max_offset=max(max_offset,float(np.linalg.norm(delta)));bound.append((start,end,point.tolist()))
			yield f'Attaching leaves {first+len(batch)} / {len(attachments)}'
		foliage['leaf_maximum_attachment_offset']=maximum_distance
		foliage['bark_candidate_fallbacks']=fallbacks
		foliage['embedded_bark_attachments']=embedded
		foliage.data.vertices.foreach_set('co',coordinates.ravel());foliage.data.update();foliage['foliage_attachments']=json.dumps(bound,separators=(',',':'))
	return max_offset


def rebuild_foliage(label,density,pending):
	"""Replace foliage transactionally while preserving completed wood and guides."""
	name=PREFIX+label;collection=bpy.data.collections[name]
	settings=json.loads(collection['settings']);settings['leaf_density']=density
	graph=json.loads(collection['growth_graph'])
	leaves,petioles,count=yield from build_foliage(graph,settings)
	temporary=bpy.data.collections.new(PREFIX+pending);bpy.context.scene.collection.children.link(temporary)
	previous=bpy.data.objects[name+'_Leaves']
	leaf=leaves.object(PREFIX+pending+'_Leaves',temporary,list(previous.data.materials));leaf['leaf_count']=count
	leaf.parent=previous.parent;leaf.matrix_parent_inverse=previous.matrix_parent_inverse.copy();leaf.matrix_world=previous.matrix_world.copy()
	if petioles.verts:
		previous_stems=bpy.data.objects.get(name+'_Petioles')
		if previous_stems is None:raise ValueError('Stored compound foliage is missing its petiole material')
		stem=petioles.object(PREFIX+pending+'_Petioles',temporary,list(previous_stems.data.materials))
		stem.parent=previous_stems.parent;stem.matrix_parent_inverse=previous_stems.matrix_parent_inverse.copy();stem.matrix_world=previous_stems.matrix_world.copy()
	maximum=yield from bind_foliage(bpy.data.objects[name+'_Wood'],list(temporary.objects))
	updates={'settings':json.dumps(settings),'foliage_attachment_max_offset':maximum,
		'foliage_generator_sha256':hashlib.sha256((OUT/'build_oak_studies.py').read_bytes()+(OUT/'growth.py').read_bytes()).hexdigest()}
	previous_values={key:collection.get(key) for key in updates}
	old_objects=[obj for suffix in ('_Leaves','_Petioles') if (obj:=bpy.data.objects.get(name+suffix)) is not None]
	old_names=[(obj,obj.name) for obj in old_objects]
	new_names=[(item,item.name) for database in (bpy.data.objects,bpy.data.meshes,bpy.data.node_groups) for item in database if item.name.startswith(PREFIX+pending+'_')]
	new_objects=list(temporary.objects);linked=[]
	yield 'Publishing completed foliage'
	# Old geometry stays alive until every fallible publication step succeeds.
	# A failed link, rename or property assignment rolls back before cancel
	# retires the still-owned pending resources.
	try:
		for obj in new_objects:collection.objects.link(obj);linked.append(obj)
		for obj,old_name in old_names:obj.name=PREFIX+pending+'_Retired'+old_name[len(name):]
		for item,old_name in new_names:item.name=name+old_name[len(PREFIX+pending):]
		for key,value in updates.items():collection[key]=value
	except Exception:
		for item,old_name in new_names:item.name=old_name
		for obj,old_name in old_names:obj.name=old_name
		for obj in linked:collection.objects.unlink(obj)
		for key,value in previous_values.items():
			if value is None:
				if key in collection:del collection[key]
			else:collection[key]=value
		raise
	for old in old_objects:
		mesh=old.data;groups=[modifier.node_group for modifier in old.modifiers if modifier.type=='NODES' and modifier.node_group]
		bpy.data.objects.remove(old,do_unlink=True)
		if mesh.users==0:bpy.data.meshes.remove(mesh)
		for group in groups:
			if group.users:continue
			prototypes=[node.inputs['Object'].default_value for node in group.nodes if node.type=='GEOMETRY_NODE_OBJECT_INFO']
			bpy.data.node_groups.remove(group)
			for prototype in prototypes:
				if prototype and prototype.get('construction_source') and prototype.users==1:
					mesh=prototype.data;bpy.data.objects.remove(prototype,do_unlink=True)
					if mesh.users==0:bpy.data.meshes.remove(mesh)
	bpy.data.collections.remove(temporary)


def bind_fused_uv(obj,source):
	"""Reproject onto swept branch frames, then unwrap each seam-crossing face."""
	from mathutils.bvhtree import BVHTree
	from mathutils.kdtree import KDTree
	bvh=BVHTree.FromPolygons([v.co for v in source.data.vertices],[list(p.vertices) for p in source.data.polygons])
	records=json.loads(source["sweeps"]);prepared=[]
	for record in records:
		frames=record["frames"];points=[Vector(f["p"]) for f in frames];axes=[Vector(f["axis"]) for f in frames]
		kd=KDTree(len(points))
		for i,p in enumerate(points):kd.insert(p,i)
		# Square scan: equal physical scale around and along each tapered limb.
		v=[0]
		for a,b in zip(frames,frames[1:]):
			v.append(v[-1]+(b["d"]-a["d"])/(math.tau*(a["r"]+b["r"])*.5/record["repeats"]))
		kd.balance();prepared.append((points,axes,v,kd,record["repeats"]))
	uv=obj.data.uv_layers.active or obj.data.uv_layers.new(name="UVMap")
	parent_uv=obj.data.uv_layers.get("BarkParent") or obj.data.uv_layers.new(name="BarkParent")
	weights=obj.data.attributes.get("bark_parent_weight") or obj.data.attributes.new("bark_parent_weight","FLOAT","CORNER")
	parts=obj.data.attributes.get("wood_part") or obj.data.attributes.new("wood_part","INT","FACE")
	cache={};depths=[0.0]*len(obj.data.vertices);radii=[0.0]*len(obj.data.vertices)
	def project(p,branch):
		points,axes,distances,kd,repeats=prepared[branch];_,nearest,_=kd.find(p);best=None
		for i in range(max(0,nearest-2),min(len(points)-1,nearest+2)):
			d=points[i+1]-points[i];t=max(0,min(1,(p-points[i]).dot(d)/max(d.length_squared,1e-12)))
			center=points[i].lerp(points[i+1],t);error=(p-center).length_squared
			if best is None or error<best[0]:best=(error,i,t,center,d.normalized())
		error,i,t,center,tangent=best;axis=axes[i].lerp(axes[i+1],t).normalized();axis=(axis-tangent*axis.dot(tangent)).normalized()
		across=tangent.cross(axis);delta=p-center;angle=math.atan2(delta.dot(across),delta.dot(axis))
		distance=distances[i]*(1-t)+distances[i+1]*t
		record=records[branch]
		radius=record["frames"][i]["r"]*(1-t)+record["frames"][i+1]["r"]*t
		return angle/(2*math.pi)*repeats,distance,math.sqrt(error)-radius,radius
	parents=[record["parent"] for record in records]
	vertical_offsets=[records[0]["phase"]/math.tau*7]
	for branch in range(1,len(records)):
		parent=parents[branch]
		vertical_offsets.append(project(prepared[branch][0][0],parent)[1]+vertical_offsets[parent])
	for polygon in obj.data.polygons:
		if polygon.index%4096==0:yield f'Projecting bark {polygon.index} / {len(obj.data.polygons)}'
		near=bvh.find_nearest(polygon.center);source_face=near[2]
		branch=source.data.attributes["branch_id"].data[source_face].value
		parts.data[polygon.index].value=source.data.polygons[source_face].material_index
		parent=parents[branch];repeats=prepared[branch][4];parent_repeats=prepared[parent][4];first_u=None;first_parent_u=None
		for loop_index in polygon.loop_indices:
			vertex=obj.data.loops[loop_index].vertex_index;key=(vertex,branch)
			if key not in cache:
				p=obj.data.vertices[vertex].co;u,v,_,radius=project(p,branch);pu,pv,outside,_=project(p,parent)
				v+=vertical_offsets[branch];pv+=vertical_offsets[parent]
				t=max(0,min(1,outside/max(.025,records[branch].get("radius",.3)*.45)))
				weight=0 if branch==0 else 1-t*t*(3-2*t)
				cache[key]=(u,v,pu,pv,weight)
				depths[vertex]=max(depths[vertex],min(.045,radius*.15)*obj.get("bark_relief_factor",1))
				radii[vertex]=max(radii[vertex],radius)
			u,v,pu,pv,weight=cache[key]
			if first_u is None:first_u=u
			else:u+=round((first_u-u)/repeats)*repeats
			uv.data[loop_index].uv=(u,v)
			if first_parent_u is None:first_parent_u=pu
			else:pu+=round((first_parent_u-pu)/parent_repeats)*parent_repeats
			parent_uv.data[loop_index].uv=(pu,pv);weights.data[loop_index].value=weight
	obj.data.uv_layers.active=uv
	obj.data.attributes.new("bark_depth","FLOAT","POINT").data.foreach_set("value",depths)
	attribute=obj.data.attributes.get("bark_radius") or obj.data.attributes.new("bark_radius","FLOAT","POINT")
	attribute.data.foreach_set("value",radii)


def displace_bark(obj):
	"""Real scanned height relief, evaluated before splitting shared wood boundaries."""
	import numpy as np
	mesh=obj.data;count=len(mesh.vertices);loops=len(mesh.loops)
	img=bpy.data.images.load(str(OUT/"Textures"/(obj.get("bark_asset","japanese_camphor_bark")+"_disp_2k.png")),check_existing=True);img.colorspace_settings.name="Non-Color"
	w,h=img.size;pixels=np.empty(w*h*4,dtype=np.float32);img.pixels.foreach_get(pixels);pixels=pixels.reshape(h,w,4)[:,:,0]
	# Filter scan detail to the mesh sampling footprint; the normal map retains finer grain.
	pyramid=[pixels]
	while min(pyramid[-1].shape)>16:
		level=pyramid[-1];pyramid.append(level.reshape(level.shape[0]//2,2,level.shape[1]//2,2).mean(axis=(1,3)))
	starts=np.empty(len(mesh.polygons),dtype=np.int32);totals=starts.copy()
	mesh.polygons.foreach_get("loop_start",starts);mesh.polygons.foreach_get("loop_total",totals)
	next_loop=np.arange(loops)+1;next_loop[starts+totals-1]=starts
	previous_loop=np.arange(loops)-1;previous_loop[starts]=starts+totals-1
	def sample(layer):
		uv=np.empty(loops*2,dtype=np.float32);layer.data.foreach_get("uv",uv);uv=uv.reshape(-1,2)
		footprint=np.maximum(np.linalg.norm(uv[next_loop]-uv,axis=1),np.linalg.norm(uv[previous_loop]-uv,axis=1))*w
		lod=np.clip(np.log2(np.maximum(1,footprint)),0,len(pyramid)-1);result=np.zeros(loops,dtype=np.float32)
		tile=uv[:,1]*np.float32(1/1.37);cell=np.floor(tile);t=np.clip((tile-cell-.65)/.35,0,1);blend=t*t*(3-2*t)
		for shift in (0,1):
			index=cell+shift;offset=np.column_stack(((index*np.float32(.61803398875)+.123)%1,(index*np.float32(.41421356237)+.37)%1));shifted=uv+offset
			patch_weight=blend if shift else 1-blend
			for index,level in enumerate(pyramid):
				contribution=np.maximum(0,1-np.abs(lod-index))*patch_weight;mask=contribution>0
				if not mask.any():continue
				lh,lw=level.shape;x=(shifted[mask,0]%1)*lw-.5;y=(shifted[mask,1]%1)*lh-.5
				ix=np.floor(x).astype(np.int32);iy=np.floor(y).astype(np.int32);fx=x-ix;fy=y-iy
				value=(level[iy%lh,ix%lw]*(1-fx)+level[iy%lh,(ix+1)%lw]*fx)*(1-fy)+(level[(iy+1)%lh,ix%lw]*(1-fx)+level[(iy+1)%lh,(ix+1)%lw]*fx)*fy
				result[mask]+=value*contribution[mask]
		return result
	weight=np.empty(loops,dtype=np.float32);mesh.attributes["bark_parent_weight"].data.foreach_get("value",weight)
	height=sample(mesh.uv_layers["UVMap"])*(1-weight)+sample(mesh.uv_layers["BarkParent"])*weight
	indices=np.empty(loops,dtype=np.int32);mesh.loops.foreach_get("vertex_index",indices)
	height=np.bincount(indices,weights=height,minlength=count)/np.maximum(1,np.bincount(indices,minlength=count))
	depth=np.empty(count,dtype=np.float32);mesh.attributes["bark_depth"].data.foreach_get("value",depth)
	coords=np.empty(count*3,dtype=np.float32);normals=np.empty(count*3,dtype=np.float32)
	mesh.vertices.foreach_get("co",coords);mesh.vertices.foreach_get("normal",normals)
	coords=coords.reshape(-1,3)+normals.reshape(-1,3)*((height-float(np.median(pixels)))*depth)[:,None]
	mesh.vertices.foreach_set("co",coords.ravel());mesh.update()
	obj["bark_relief_max_range_m"]=.045


def split_wood_parts(name,obj,source,collection):
	import numpy as np
	mesh=obj.data
	coordinates=np.empty(len(mesh.vertices)*3,np.float32);mesh.vertices.foreach_get('co',coordinates);coordinates=coordinates.reshape(-1,3)
	radii=np.empty(len(mesh.vertices),np.float32);mesh.attributes['bark_radius'].data.foreach_get('value',radii)
	normal_values=np.empty(len(mesh.loops)*3,np.float32);mesh.corner_normals.foreach_get('vector',normal_values);normal_values=normal_values.reshape(-1,3)
	uv_values=np.empty(len(mesh.loops)*2,np.float32);mesh.uv_layers.active.data.foreach_get('uv',uv_values);uv_values=uv_values.reshape(-1,2)
	parent_values=np.empty(len(mesh.loops)*2,np.float32);mesh.uv_layers['BarkParent'].data.foreach_get('uv',parent_values);parent_values=parent_values.reshape(-1,2)
	weight_values=np.empty(len(mesh.loops),np.float32);mesh.attributes['bark_parent_weight'].data.foreach_get('value',weight_values)
	part_ids=np.empty(len(mesh.polygons),np.int32);mesh.attributes['wood_part'].data.foreach_get('value',part_ids)
	vertex_ids=np.empty(len(mesh.loops),np.int32);mesh.loops.foreach_get('vertex_index',vertex_ids)
	starts=np.empty(len(mesh.polygons),np.int32);mesh.polygons.foreach_get('loop_start',starts)
	counts=np.empty(len(mesh.polygons),np.int32);mesh.polygons.foreach_get('loop_total',counts)
	for part,part_name in enumerate(('Trunk','Branches','Roots')):
		yield f'Finishing wood part {part+1} / 3'
		selected=np.flatnonzero(part_ids==part);sizes=counts[selected]
		offsets=np.cumsum(sizes,dtype=np.int32)-sizes;total=int(sizes.sum())
		loops=np.repeat(starts[selected]-offsets,sizes)+np.arange(total,dtype=np.int32)
		used,indices=np.unique(vertex_ids[loops],return_inverse=True)
		# Keep one compact index remap instead of millions of Python tuples
		# for positions, face corners, normals and the two bark UV layers.
		data=bpy.data.meshes.new(name+'_'+part_name)
		data.vertices.add(len(used));data.vertices.foreach_set('co',coordinates[used].ravel())
		data.loops.add(total);data.loops.foreach_set('vertex_index',indices.astype(np.int32))
		data.polygons.add(len(selected));data.polygons.foreach_set('loop_start',offsets)
		data.polygons.foreach_set('loop_total',sizes);data.polygons.foreach_set('use_smooth',np.ones(len(selected),bool))
		data.update(calc_edges=True);data.materials.append(mesh.materials[0])
		data.uv_layers.new(name='UVMap').data.foreach_set('uv',uv_values[loops].ravel())
		data.uv_layers.new(name='BarkParent').data.foreach_set('uv',parent_values[loops].ravel())
		data.attributes.new('bark_radius','FLOAT','POINT').data.foreach_set('value',radii[used])
		data.attributes.new('bark_parent_weight','FLOAT','CORNER').data.foreach_set('value',weight_values[loops])
		# Free corner normals preserve the source vectors without the legacy
		# 16-bit fan-space encoding, including at boundaries between parts.
		data.attributes.new('custom_normal','FLOAT_VECTOR','CORNER').data.foreach_set('vector',normal_values[loops].ravel())
		data.uv_layers.active_index=0
		result=bpy.data.objects.new(name+'_'+part_name,data);collection.objects.link(result)
		result.location=obj.location;result['render_part']=part_name.lower()


def build_proxies(name,paths,scene,offset,trunk_profile,interaction_scale=1):
	proxy_name=name+"_Collision"
	old=bpy.data.collections.get(proxy_name)
	if old:
		for obj in list(old.objects):bpy.data.objects.remove(obj,do_unlink=True)
		bpy.data.collections.remove(old)
	collection=bpy.data.collections.new(proxy_name);scene.collection.children.link(collection)
	def segment(start,end,r0,r1,label,role):
		direction=(end-start).normalized();axis=direction.cross(Vector((0,1,0))).normalized()
		if axis.length<.01:axis=direction.cross(Vector((1,0,0))).normalized()
		other=direction.cross(axis).normalized();verts=[]
		for point,radius in [(start-direction*.055*interaction_scale,r0),(end+direction*.055*interaction_scale,r1)]:
			for j in range(8):verts.append(point+(axis*math.cos(j*math.pi/4)+other*math.sin(j*math.pi/4))*radius)
		faces=[tuple(reversed(range(8))),tuple(range(8,16))]
		faces.extend((j,(j+1)%8,(j+1)%8+8,j+8) for j in range(8))
		mesh=bpy.data.meshes.new(label);mesh.from_pydata(verts,[],faces);mesh.update()
		obj=bpy.data.objects.new(label,mesh);collection.objects.link(obj);obj.location=offset
		obj["collision_role"]=role;obj["blocking"]=role=="solid_trunk";obj["convex"]=True
		if role=="branch_interaction":obj["suggested_gameplay"]= "nonblocking trigger; player slowdown hook"
		obj.display_type="WIRE";obj.show_in_front=True;obj.hide_render=True;obj.hide_set(True)
		obj.color=(.1,.9,.25,1) if role=="solid_trunk" else (1,.3,.06,1)
	trunk,radius,end=paths[0]
	for i in range(9):
		t0=i/9;t1=(i+1)/9;p0=point_on(trunk,t0)[0];p1=point_on(trunk,t1)[0]
		r0=radius_at(radius,end,t0,trunk_profile)*(1+.18*math.exp(-t0*20))*.98;r1=radius_at(radius,end,t1,trunk_profile)*(1+.18*math.exp(-t1*20))*.98
		segment(p0,p1,max(.02*interaction_scale,r0),max(.02*interaction_scale,r1),name+f"_COL_SOLID_Trunk_{i:02}","solid_trunk")
	for i,(path,radius,end) in enumerate(paths[1:]):
		for j in range(3):
			t0=j/3;t1=(j+1)/3
			segment(point_on(path,t0)[0],point_on(path,t1)[0],radius*(1-t0)+.38*interaction_scale,radius*(1-t1)+.45*interaction_scale,name+f"_COL_SOFT_Branch_{i:02}_{j}","branch_interaction")


def setup_studio():
	scene=bpy.data.scenes[PREFIX+"Studio"];bpy.context.window.scene=scene
	collection=bpy.data.collections.get(PREFIX+"Stage")
	if collection is None:
		collection=bpy.data.collections.new(PREFIX+"Stage");scene.collection.children.link(collection)
	def add(name,data):
		obj=bpy.data.objects.get(PREFIX+name)
		if obj is None: obj=bpy.data.objects.new(PREFIX+name,data);collection.objects.link(obj)
		return obj
	camera=add("Camera",bpy.data.cameras.new(PREFIX+"CameraData"));scene.camera=camera
	camera.data.type="ORTHO";camera.data.ortho_scale=21
	ground=bpy.data.objects.get(PREFIX+"Ground")
	if ground is None:
		mesh=bpy.data.meshes.new(PREFIX+"GroundMesh");mesh.from_pydata([(-1000,-1000,0),(1000,-1000,0),(1000,1000,0),(-1000,1000,0)],[],[(0,1,2,3)])
		ground=add("Ground",mesh)
	# The graph's root origin defines soil Z=0. A lowered preview floor
	# exposes the basal cap and makes attached roots appear to float.
	inverse=ground.matrix_world.inverted()
	for vertex in ground.data.vertices:
		point=ground.matrix_world@vertex.co;point.z=0;vertex.co=inverse@point
	ground.data.update()
	mat=bpy.data.materials.get(PREFIX+"GroundMat") or bpy.data.materials.new(PREFIX+"GroundMat")
	mat.diffuse_color=(.24,.255,.23,1);mat.use_nodes=True
	shader=next(n for n in mat.node_tree.nodes if n.type=="BSDF_PRINCIPLED");shader.inputs["Base Color"].default_value=(.24,.255,.23,1);shader.inputs["Roughness"].default_value=.92
	ground.data.materials.clear();ground.data.materials.append(mat)
	world=bpy.data.worlds.get(PREFIX+"World") or bpy.data.worlds.new(PREFIX+"World");world.use_nodes=True
	world.node_tree.nodes.get("Background").inputs[0].default_value=(.57,.67,.80,1)
	world.node_tree.nodes.get("Background").inputs[1].default_value=.65;scene.world=world
	for name,kind,location,energy,size in [("Key","AREA",(-10,-14,23),2600,12),("Fill","AREA",(12,2,15),1900,10)]:
		obj=add(name,bpy.data.lights.new(PREFIX+name+"Data",kind));obj.location=location;obj.data.energy=energy;obj.data.shape="DISK";obj.data.size=size
		obj.rotation_euler=(Vector((0,0,7))-obj.location).to_track_quat("-Z","Y").to_euler()
	sun=add("Sun",bpy.data.lights.new(PREFIX+"SunData","SUN"));sun.rotation_euler=(math.radians(24),math.radians(-22),math.radians(-35));sun.data.energy=2.1;sun.data.angle=math.radians(12)
	scene.render.engine="BLENDER_EEVEE";scene.render.resolution_x=1600;scene.render.resolution_y=1600;scene.render.resolution_percentage=100
	scene.cycles.transparent_max_bounces=128
	scene.render.image_settings.file_format="PNG";scene.render.film_transparent=False
	scene.view_settings.view_transform="AgX";scene.view_settings.look="AgX - Medium High Contrast"
	scene.render.image_settings.color_mode="RGBA"
	set_view(-55)
	for area in bpy.context.screen.areas:
		if area.type=="VIEW_3D":
			area.spaces.active.region_3d.view_perspective="CAMERA"
			area.spaces.active.shading.type="MATERIAL"
	print("Studio ready")


def set_view(azimuth=-55,center=(0,0,7.4),scale=19.5,elevation=8):
	scene=bpy.data.scenes[PREFIX+"Studio"];camera=scene.camera
	a=math.radians(azimuth);e=math.radians(elevation);target=Vector(center)
	distance=max(32,scale*1.5)
	camera.location=target+Vector((math.cos(a)*distance*math.cos(e),math.sin(a)*distance*math.cos(e),distance*math.sin(e)))
	camera.rotation_euler=(target-camera.location).to_track_quat("-Z","Y").to_euler();camera.data.ortho_scale=scale


def end_gallery():
	for obj in bpy.data.objects:
		if obj.name.startswith(PREFIX) and "gallery_origin" in obj:
			obj.location=obj["gallery_origin"];del obj["gallery_origin"]


def show_gallery():
	end_gallery()
	collections=sorted([c for c in bpy.data.collections if c.name.startswith(PREFIX) and "generator_sha256" in c],key=lambda c:(c.get("species","Oak"),c.get("stage","Mature"),c.name))
	labels=[c.name.removeprefix(PREFIX) for c in collections];show_forms(labels,True)
	columns=min(4,len(collections));rows=math.ceil(len(collections)/max(1,columns));spacing=27
	for index,collection in enumerate(collections):
		x=(index%columns-(columns-1)*.5)*spacing;y=(index//columns-(rows-1)*.5)*spacing
		related=[collection]+[c for suffix in ('_Guides','_Collision') if (c:=bpy.data.collections.get(collection.name+suffix))]
		for obj in {obj for c in related for obj in c.objects}:
			obj["gallery_origin"]=tuple(obj.location);obj.location+=Vector((x,y,0))
	for area in bpy.context.screen.areas:
		if area.type=='VIEW_3D':
			view=area.spaces.active.region_3d;view.view_perspective='PERSP';view.view_location=(0,0,5)
			view.view_rotation=Vector((1,-2,1.5)).to_track_quat('Z','Y');view.view_distance=max(columns*spacing,rows*spacing+25)
	print("Library gallery",len(collections),"specimens")


def show_forms(forms,foliage=True):
	end_gallery()
	for collection in bpy.data.collections:
		if collection.name.startswith(PREFIX) and collection.name not in (PREFIX+"Stage",):
			visible=collection.name.removeprefix(PREFIX) in forms
			for obj in collection.objects:
				if obj.get("collision_role") or (obj.type=="CURVE" and not collection.get("growth_preview")):obj.hide_render=True;obj.hide_set(True);continue
				if obj.name.endswith("_UV_Source") or obj.get("construction_source"):
					obj.hide_render=True;obj.hide_set(True);continue
				obj.hide_render=not visible or (not foliage and obj.name.endswith("_Leaves"))
				obj.hide_set(obj.hide_render)


def render(name,azimuth=-55,foliage=True,forms=("Open_Grown",),center=(0,0,7.4),scale=19.5,elevation=8):
	show_forms(forms,foliage);set_view(azimuth,center,scale,elevation)
	scene=bpy.data.scenes[PREFIX+"Studio"]
	scene.render.filepath=str(EVIDENCE/(name+".png"))
	bpy.ops.render.render(write_still=True)
	print("Rendered",scene.render.filepath)
