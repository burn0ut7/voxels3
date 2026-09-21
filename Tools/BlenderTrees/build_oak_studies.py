"""Seeded Blender tree library. Does not export or modify game assets."""
import bpy
import math
import random
import json
import hashlib
import importlib.util
import sys
from pathlib import Path
from mathutils import Vector, Quaternion

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Tools/BlenderTrees"
EVIDENCE = ROOT / "Docs/ValidationEvidence/BlenderTrees"
PREFIX = "OAK_STUDY_"
TAPER = 1.15
BARK_TILE_METRES = 1.8
NEEDLES_PER_METRE = 950
NEEDLES_PER_SPRAY = 64
NEEDLE_SPRAY_LENGTH = NEEDLES_PER_SPRAY / NEEDLES_PER_METRE
SPECIES = {
	"Oak": {"name":"English oak", "height":16, "leaf":"lobed", "bark":"japanese_camphor_bark", "tile":1.8},
	"Ash": {"name":"Common ash", "height":20, "leaf":"compound", "bark":"japanese_camphor_bark", "tile":1.8},
	"Spruce": {"name":"Norway spruce", "height":18, "leaf":"needle", "bark":"pine_bark", "tile":2.0},
	"Birch": {"name":"Silver birch", "height":16, "leaf":"triangular", "bark":"japanese_camphor_bark", "tile":1.8},
}
TREE_SETTINGS = ("height","spread","girth","lean","upward","droop","branch_density","leaf_density","branch_angle","character","fork_height","growth_direction","crown_bias","root_spread","root_depth","age","competition","light_response","resource")


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
	settings=dict(zip(keys,values));settings.update(branch_density=1.35,leaf_density=1.9,growth_direction=-25 if form=="Weathered" else 0,root_spread=1,root_depth=1.2)
	if species!="Oak":
		settings["height"]=SPECIES[species]["height"]*(1.15 if form=="Woodland" else .9 if form=="Weathered" else 1)
		settings.update(branch_density=1.05,leaf_density=1.35)
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
	controls = [Vector(p) for p in controls]
	result=[]
	for i in range(len(controls)-1):
		a=controls[max(0,i-1)]; b=controls[i]; c=controls[i+1]; d=controls[min(len(controls)-1,i+2)]
		for j in range(steps):
			t=j/steps
			result.append(0.5*((2*b)+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t))
	result.append(controls[-1])
	return result


class Geometry:
	def __init__(self,tile=BARK_TILE_METRES):
		self.verts=[]; self.faces=[]; self.uv=[]; self.mats=[];self.part=0
		self.sweeps=[];self.branch_ids=[];self.active_branch=-1
		self.tile=tile
		self.radii=[];self.needle_rotations=[];self.needle_scales=[];self.needle_variants=[]
		self.attachments=[]

	def face(self, indices, uv, material=0):
		self.faces.append(indices); self.uv.extend(uv); self.mats.append(material)
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
			if root:r*=1+.18*math.exp(-t*20)
			previous_v=v
			if last_radius is not None:v+=(p-path[i-1]).length/(math.tau*(last_radius+r)*.5/repeats)
			last_radius=r
			frames.append({"p":tuple(p),"axis":tuple(axis),"d":distance,"r":r})
			for j in range(sides):
				a=2*math.pi*j/sides
				irregular=1+.036*math.sin(3*a+phase+t*2)+.021*math.sin(5*a-phase+t*4)
				self.verts.append(tuple(p+(axis*math.cos(a)+cross*math.sin(a))*r*irregular))
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
		for row in range(3):
			t=row/2
			for col in range(3):
				x=col-1
				fold=length*(.11*math.sin(math.pi*t)-.095*abs(x)*math.sin(math.pi*t)-.08*t*t)
				self.verts.append(tuple(base+axis*length*t+across*x*width*.5+normal*fold))
		for row in range(2):
			for col in range(2):
				indices=(start+row*3+col,start+row*3+col+1,start+(row+1)*3+col+1,start+(row+1)*3+col)
				uv=[((tile%2+x/2)*.5,(1-tile//2+y/2)*.5) for x,y in [(col,row),(col+1,row),(col+1,row+1),(col,row+1)]]
				self.face(indices,uv,rng.randrange(3) if row==0 and col==0 else self.mats[-1])
		self.attachments.append((start,len(self.verts),tuple(base)))

	def object(self,name,collection,materials):
		mesh=bpy.data.meshes.new(name)
		mesh.from_pydata(self.verts,[],self.faces); mesh.update()
		obj=bpy.data.objects.new(name,mesh); collection.objects.link(obj)
		for material in materials: mesh.materials.append(material)
		uv=mesh.uv_layers.new(name="UVMap")
		uv.data.foreach_set("uv",[v for pair in self.uv for v in pair])
		for polygon,material in zip(mesh.polygons,self.mats):
			polygon.use_smooth=True; polygon.material_index=material
		if len(self.radii)==len(self.verts):mesh.attributes.new("bark_radius","FLOAT","POINT").data.foreach_set("value",self.radii)
		if self.sweeps:
			attribute=mesh.attributes.new("branch_id","INT","FACE")
			attribute.data.foreach_set("value",self.branch_ids)
			obj["sweeps"]=json.dumps(self.sweeps)
		if self.attachments:obj['foliage_attachments']=json.dumps(self.attachments,separators=(',',':'))
		if self.needle_rotations:
			for name,values in (('needle_rotation',self.needle_rotations),('needle_scale',self.needle_scales)):
				mesh.attributes.new(name,'FLOAT_VECTOR','POINT').data.foreach_set('vector',[value for vector in values for value in vector])
			if self.needle_variants:mesh.attributes.new('needle_variant','INT','POINT').data.foreach_set('value',self.needle_variants)
			instance_needles(obj,materials[0],collection.get('stage','Mature'))
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

	def needle(self,base,direction,length,rng):
		self.attachments.append((len(self.verts),len(self.verts)+1,tuple(base)))
		self.verts.append(tuple(base));self.needle_rotations.append(tuple(direction.to_track_quat('Z','Y').to_euler()))
		width=rng.uniform(.00055,.0008);self.needle_scales.append((width,width,length))

	def needle_spray(self,base,direction,stretch,rng):
		self.attachments.append((len(self.verts),len(self.verts)+1,tuple(base)))
		self.verts.append(tuple(base));rotation=direction.to_track_quat('Z','Y')@Quaternion((0,0,1),rng.uniform(0,math.tau))
		self.needle_rotations.append(tuple(rotation.to_euler()));self.needle_scales.append((1,1,stretch));self.needle_variants.append(rng.randrange(8))


def instance_needles(obj,material,stage='Mature'):
	"""Keep true three-dimensional needles instanced in the native authoring mesh."""
	clustered=stage!='Juvenile';prototypes=[]
	for variant in range(8 if clustered else 1):
		name=material.name+(f'_NeedleSpray_{variant}' if clustered else '_NeedleSource');prototype=bpy.data.objects.get(name)
		if prototype is None:
			mesh=bpy.data.meshes.new(name);vertices=[];faces=[];rng=random.Random(83521+variant*1049)
			for needle in range(NEEDLES_PER_SPRAY if clustered else 1):
				start=len(vertices);base=Vector((0,0,0));rotation=Quaternion();width=length=1
				if clustered:
					base.z=NEEDLE_SPRAY_LENGTH*((needle+rng.uniform(.1,.9))/NEEDLES_PER_SPRAY-.5)
					angle=needle*2.399+rng.uniform(-.7,.7);direction=Vector((math.cos(angle),math.sin(angle),rng.uniform(.15,.65))).normalized()
					rotation=direction.to_track_quat('Z','Y');width=rng.uniform(.00055,.0008);length=rng.uniform(.019,.029)
				for z,radius in ((0,.6),(.42,1),(1,.035)):
					vertices.extend(tuple(base+rotation@Vector((math.cos(i*math.pi/2)*radius*width,math.sin(i*math.pi/2)*radius*width,z*length))) for i in range(4))
				for row in range(2):
					for i in range(4):faces.append(tuple(start+v for v in (row*4+i,row*4+(i+1)%4,(row+1)*4+(i+1)%4,(row+1)*4+i)))
				faces.extend((tuple(start+i for i in (3,2,1,0)),tuple(start+i for i in (8,9,10,11))))
			mesh.from_pydata(vertices,[],faces)
			collection=bpy.data.collections.get(PREFIX+'NeedleSources')
			if collection is None:
				collection=bpy.data.collections.new(PREFIX+'NeedleSources');bpy.context.scene.collection.children.link(collection)
			prototype=bpy.data.objects.new(name,mesh);collection.objects.link(prototype)
			prototype.hide_render=True;prototype.hide_set(True);prototype['construction_source']=True
		prototype.data.materials.clear();prototype.data.materials.append(material);prototypes.append(prototype)
	group=bpy.data.node_groups.get(material.name+'_Needles')
	if group is None:
		group=bpy.data.node_groups.new(material.name+'_Needles','GeometryNodeTree')
		group.interface.new_socket(name='Geometry',in_out='INPUT',socket_type='NodeSocketGeometry');group.interface.new_socket(name='Geometry',in_out='OUTPUT',socket_type='NodeSocketGeometry')
	n=group.nodes;l=group.links;n.clear();entry=n.new('NodeGroupInput');output=n.new('NodeGroupOutput');instances=n.new('GeometryNodeInstanceOnPoints')
	l.new(entry.outputs['Geometry'],instances.inputs['Points'])
	variants=n.new('GeometryNodeGeometryToInstance') if clustered else None
	for prototype in prototypes:
		info=n.new('GeometryNodeObjectInfo');info.inputs['Object'].default_value=prototype;info.transform_space='ORIGINAL'
		l.new(info.outputs['Geometry'],variants.inputs['Geometry'] if clustered else instances.inputs['Instance'])
	if clustered:
		l.new(variants.outputs['Instances'],instances.inputs['Instance']);instances.inputs['Pick Instance'].default_value=True
		index=n.new('GeometryNodeInputNamedAttribute');index.data_type='INT';index.inputs['Name'].default_value='needle_variant';l.new(index.outputs['Attribute'],instances.inputs['Instance Index'])
	for attribute,socket in (('needle_rotation','Rotation'),('needle_scale','Scale')):
		field=n.new('GeometryNodeInputNamedAttribute');field.data_type='FLOAT_VECTOR';field.inputs['Name'].default_value=attribute;l.new(field.outputs['Attribute'],instances.inputs[socket])
	l.new(instances.outputs['Instances'],output.inputs['Geometry'])
	modifier=obj.modifiers.get('Living three-dimensional needles') or obj.modifiers.new('Living three-dimensional needles','NODES');modifier.node_group=group
	obj['foliage_representation']='Instanced volumetric 64-needle sprays (768 vertices each); realize instances for mesh export' if clustered else 'Instanced 12-vertex closed square needles; realize instances for mesh export'
	if clustered:
		old=bpy.data.objects.get(material.name+'_NeedleSource')
		if old and old.get('construction_source'):bpy.data.objects.remove(old,do_unlink=True)


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


def build_tree(form="Open_Grown",seed=1701,offset=(0,0,0),height=16,spread=1.0,girth=1.0,lean=0.0,upward=.55,droop=.35,branch_density=1.35,leaf_density=1.9,branch_angle=57,character=.45,fork_height=.24,growth_direction=0,crown_bias=.25,root_spread=1,root_depth=1.2,label=None,species="Oak",stage="Mature",age=24,competition=.65,light_response=.4,resource=1.0,graph=None):
	settings={key:value for key,value in locals().copy().items() if key in TREE_SETTINGS or key in ('species','stage','form','seed')}
	module,recipe=growth_recipe(settings)
	if graph is None:
		job=module.Simulation(recipe)
		while job.step():pass
		graph=job.finish()
	module.validate(graph)
	if graph['recipe'] != module.asdict(recipe):raise ValueError('Growth controls changed; simulate again before building geometry')
	rng=random.Random(seed)
	leaf_rng=random.Random(seed+8191)
	scene=bpy.context.scene
	name=PREFIX+(label or f"{species}_{stage}_{form}_{seed}")
	if bpy.data.collections.get(name) and bpy.data.collections[name].get("protected_reference"):raise ValueError("This oak is a preserved reference. Generate a new named specimen to edit.")
	# Meshing scale follows grown wood, not the potential adult height. Otherwise
	# an eighteen-season sapling loses every side limb to adult voxel thresholds.
	resolution=min(1,max(.08,graph['nodes'][0]['radius']/.3))
	voxel_size=max(.001,min(.035,graph['nodes'][0]['radius']/6))
	# Primary limbs remain first for the existing s&box motion packing. The
	# complete graph retains all finer ancestry, independently of mesh grouping.
	# Curved reference sweeps retain every graph point for bark and motion.
	# The connected collar surface owns visible wood; foliage binds to that
	# finished surface after subdivision and bark displacement.
	children=[[] for _ in graph['nodes']]
	for index,node in enumerate(graph['nodes']):
		if node['parent']>=0:children[node['parent']].append(index)
	tangents=[]
	for index,node in enumerate(graph['nodes']):
		incoming=Vector(node['direction'])
		if children[index]:
			preferred=max(children[index],key=lambda child:(graph['nodes'][child]['axis']==node['axis'],graph['nodes'][child]['radius'],-child))
			outgoing=(Vector(graph['nodes'][preferred]['p'])-Vector(node['p'])).normalized()
			incoming=(incoming+outgoing).normalized()
		tangents.append(incoming)
	axis_paths=[];shoot_paths={}
	for axis in graph['axes']:
		nodes=[graph['nodes'][index] for index in axis['nodes']]
		radii=[node['radius'] for node in nodes]
		if axis['parent']>=0 and len(children[axis['attachment']])>1:radii[0]=radii[1]
		path=[];sampled_radii=[]
		for i,(start_id,end_id) in enumerate(zip(axis['nodes'],axis['nodes'][1:])):
			a=Vector(nodes[i]['p']);b=Vector(nodes[i+1]['p']);length=(b-a).length
			m0=tangents[start_id]*length;m1=tangents[end_id]*length
			span=[]
			for sample in range(9):
				t=sample/8;t2=t*t;t3=t2*t
				span.append(a*(2*t3-3*t2+1)+m0*(t3-2*t2+t)+b*(-2*t3+3*t2)+m1*(t3-t2))
				if sample<8:path.append(span[-1]);sampled_radii.append(radii[i]*(1-t)+radii[i+1]*t)
			shoot_paths[end_id]=span
		path.append(Vector(nodes[-1]['p']));sampled_radii.append(radii[-1])
		axis_paths.append((path,sampled_radii))
	primary=[0]+[i for i,axis in enumerate(graph['axes']) if axis['parent']==0 and axis_paths[i][1][0]>voxel_size*3]
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
	wood=Geometry(SPECIES[species]['tile']);leaves=Geometry();petioles=Geometry(SPECIES[species]['tile'])
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
	order=primary+[i for i in range(len(axis_paths)) if i not in primary]
	for position,axis_id in enumerate(order):
		path,radii=axis_paths[axis_id];radius,end=radii[0],radii[-1]
		parent_axis=graph['axes'][axis_id]['parent']
		while parent_axis>=0 and parent_axis not in axis_to_wood:parent_axis=graph['axes'][parent_axis]['parent']
		parent=axis_to_wood.get(parent_axis,0)
		wood.part=0 if axis_id==0 else 1
		wood.branch(path,radius,end,rng,16 if axis_id==0 else 10,axis_id==0,profiles[axis_id],parent,path_radii=radii,graph_axis=axis_id,round_tip=not children[graph['axes'][axis_id]['nodes'][-1]])
		axis_to_wood[axis_id]=wood.active_branch
		if position%64==63:yield f'Branch surfaces {position+1} / {len(order)}'
	yield 'Building roots'
	# Roots have their own random stream; changing their controls preserves the crown.
	root_rng=random.Random(seed+4093);root_paths=[]
	wood.part=2;root_count=root_rng.randint(5,7);root_phase=root_rng.uniform(0,math.tau)
	surface_nodes=[dict(node) for node in graph['nodes']];root_axis=len(graph['axes'])
	root_origin=min(trunk,key=lambda p:abs(p.z)).copy();root_origin.z=-.095 if stage=="Juvenile" else base_radius*.03
	def root_mesh(path,radius,end,sides,parent=0):
		nonlocal root_axis
		end=min(end,radius*.3)
		# Root ancestry is explicit. A secondary root can attach only to its
		# own parent's sampled path, never whichever root happens to be nearby.
		parent_ids,parent_sweep=parent if isinstance(parent,tuple) else ([0],0)
		attachment=min(parent_ids,key=lambda i:(Vector(surface_nodes[i]['p'])-path[0]).length_squared)
		wood.branch(path,radius,end,root_rng,sides,True,parent=parent_sweep)
		length=sum((b-a).length for a,b in zip(path,path[1:]));steps=max(3,math.ceil(length/max(radius*4,.06)))
		ids=[attachment];previous=attachment
		parameters={j/steps for j in range(1,steps+1) if all(abs(j/steps-critical)>.35/steps for critical in (.4,.66))}|{.4,.66,1.0}
		for t in sorted(parameters):
			index=len(surface_nodes);surface_nodes.append({'parent':previous,'axis':root_axis,'p':tuple(point_on(path,t)[0]),'radius':radius_at(radius,end,t)})
			ids.append(index);previous=index
		root_axis+=1
		return ids,wood.active_branch
	for i in range(root_count):
		a=root_phase+i*math.tau/root_count+root_rng.uniform(-.22,.22)
		length=base_radius*root_rng.uniform(4.5,6.5)*root_spread
		depth=root_depth*root_rng.uniform(.8,1.15)
		d=Vector((math.cos(a),math.sin(a),0));side=Vector((-d.y,d.x,0))
		turn=side*length*root_rng.uniform(-.13,.13)
		path=smooth_path([root_origin,root_origin+d*base_radius*.65+Vector((0,0,-base_radius*.13)),root_origin+d*length*.46+turn*.5+Vector((0,0,-.16-depth*.18)),root_origin+d*length*.8+turn+Vector((0,0,-depth*.64)),root_origin+d*length+turn*.8+Vector((0,0,-depth))],6)
		radius=base_radius*root_rng.uniform(.34,.47)
		root_parent=root_mesh(path,radius,.012,16);root_paths.append(path)
		for j in range(2):
			t=.4+j*.26;p,tangent=point_on(path,t)
			angle=a+(-1 if j==0 else 1)*root_rng.uniform(.5,.95)
			direction=Vector((math.cos(angle),math.sin(angle),0))
			reach=length*root_rng.uniform(.30,.46)
			departure=(tangent*.35+direction*.65).normalized()
			child=smooth_path([p,p+departure*reach*.28,p+direction*reach*.65+Vector((0,0,-depth*.18)),p+direction*reach+Vector((0,0,-depth*.35))],5)
			root_mesh(child,radius_at(radius,.012,t)*.62,.008,10,root_parent);root_paths.append(child)
	wood.part=1
	leaf_count=0;branch_count=len(paths)

	def shoot_foliage(path,radius):
		nonlocal leaf_count
		if species=="Spruce":
			length=sum((b-a).length for a,b in zip(path,path[1:]));foliated=min(length,.55 if stage=='Juvenile' else 1.2);count=max(8,round(foliated*NEEDLES_PER_METRE*leaf_density));start=max(0,1-foliated/max(length,.001))
			if stage!='Juvenile':
				groups=max(1,round(count/NEEDLES_PER_SPRAY));stretch=max(.3,min(1.4,foliated/(groups*NEEDLE_SPRAY_LENGTH)))
				for i in range(groups):
					p,tangent=point_on(path,start+(1-start)*(i+.5)/groups);leaves.needle_spray(p,tangent,stretch,leaf_rng)
				leaf_count+=groups*NEEDLES_PER_SPRAY;return
			for i in range(count):
				p,tangent=point_on(path,start+(1-start)*(i+leaf_rng.random()*.7)/count)
				ref=Vector((0,0,1)) if abs(tangent.z)<.9 else Vector((1,0,0));a=tangent.cross(ref).normalized();b=tangent.cross(a)
				angle=i*2.399+leaf_rng.uniform(-.7,.7);direction=(a*math.cos(angle)+b*math.sin(angle)+tangent*leaf_rng.uniform(.15,.65)).normalized()
				leaves.needle(p,direction,leaf_rng.uniform(.019,.029),leaf_rng);leaf_count+=1
			return
		if species=="Ash":
			shoot_length=sum((b-a).length for a,b in zip(path,path[1:]));count=max(1,min(5 if stage=='Juvenile' else 4,round(shoot_length/(.12 if stage=='Juvenile' else .16)*leaf_density)));phase=leaf_rng.uniform(0,math.tau)
			for i in range(count):
				leaf_start=len(leaves.verts);stem_start=len(petioles.verts)
				p,tangent=point_on(path,.15+.82*(i+.3)/count)
				ref=Vector((0,0,1)) if abs(tangent.z)<.9 else Vector((1,0,0));axis=tangent.cross(ref).normalized();other=tangent.cross(axis)
				angle=phase+i*math.pi+leaf_rng.uniform(-.25,.25);direction=(axis*math.cos(angle)+other*math.sin(angle)+tangent*.4+Vector((0,0,.15))).normalized()
				length=leaf_rng.uniform(.23,.33);end=p+direction*length;petioles.branch([p,end],.0014,.0005,leaf_rng,4)
				across=direction.cross(Vector((0,0,1))).normalized();normal=across.cross(direction).normalized();twist=leaf_rng.uniform(-.45,.45)
				across,normal=across*math.cos(twist)+normal*math.sin(twist),normal*math.cos(twist)-across*math.sin(twist)
				pairs=leaf_rng.randint(3,4)
				for pair in range(pairs):
					t=.18+pair*.67/pairs;anchor=p+direction*length*t
					for sign in (-1,1):
						leafdir=(across*sign+direction*leaf_rng.uniform(.28,.55)+normal*leaf_rng.uniform(-.15,.15)).normalized()
						leaves.blade(anchor,leafdir,leaf_rng.uniform(.09,.13)*(1-.2*t)*(1 if stage=='Juvenile' else 1.08),leaf_rng.uniform(-.25,.25),'compound',leaf_rng,normal,stage=='Juvenile');leaf_count+=1
				leaves.blade(end-direction*.025,direction,.12,leaf_rng.uniform(-.2,.2),'compound',leaf_rng,normal,stage=='Juvenile');leaf_count+=1
				leaves.attachments.append((leaf_start,len(leaves.verts),tuple(p)));petioles.attachments.append((stem_start,len(petioles.verts),tuple(p)))
			return
		count=round(leaf_rng.randint(9,14)*leaf_density)
		for i in range(count):
			t=.1+.88*(i+leaf_rng.random()*.45)/count
			p,tangent=point_on(path,t)
			a=i*2.399+leaf_rng.uniform(-.5,.5)
			side=Vector((math.cos(a),math.sin(a),leaf_rng.uniform(-.24,.56)))
			direction=(tangent*.32+side).normalized()
			length=leaf_rng.uniform(.16,.25)
			if species=="Birch":leaves.blade(p,direction,length*(.32 if stage=='Juvenile' else .46),leaf_rng.uniform(-.6,.6),'triangular',leaf_rng,detailed=stage=='Juvenile')
			else:leaves.leaf(p,direction,length,leaf_rng.uniform(-1.1,1.1),leaf_rng.randrange(4),leaf_rng)
			leaf_count+=1
		for i in range(3):
			p,tangent=point_on(path,.93+i*.02)
			direction=(tangent+Vector((leaf_rng.uniform(-.6,.6),leaf_rng.uniform(-.6,.6),leaf_rng.uniform(-.1,.5)))).normalized()
			if species=="Birch":leaves.blade(p,direction,leaf_rng.uniform(.05,.075) if stage=='Juvenile' else leaf_rng.uniform(.075,.11),leaf_rng.uniform(-.6,.6),'triangular',leaf_rng,detailed=stage=='Juvenile')
			else:leaves.leaf(p,direction,leaf_rng.uniform(.16,.23),leaf_rng.uniform(-1.5,1.5),leaf_rng.randrange(4),leaf_rng)
			leaf_count+=1

	# Foliage follows living recent shoots in the graph; no recursive twig
	# generator can create a second crown unrelated to the simulated branches.
	yield 'Placing foliage on living shoots'
	foliage_profile=module.Species(**graph['profile'])
	for axis in graph['axes']:
		for parent_id,node_id in zip(axis['nodes'],axis['nodes'][1:]):
			node=graph['nodes'][node_id]
			if module.supports_foliage(node,age,foliage_profile):
				shoot_foliage(shoot_paths[node_id],node['radius'])
			if node_id%128==0:yield 'Placing foliage on living shoots'
	branch_count=len(graph['axes'])
	yield 'Creating source meshes'
	wood_obj=wood.object(name+"_Wood",collection,[bark,bark,bark])
	leaf_obj=leaves.object(name+"_Leaves",collection,leafmats)
	if petioles.verts:petioles.object(name+'_Petioles',collection,[twigmat])
	collection['surface_skeleton']=json.dumps({'nodes':surface_nodes},separators=(',',':'))
	for obj in collection.objects: obj.location=offset
	wood_obj["form"]=form;wood_obj["seed"]=seed
	wood_obj["branch_count"]=branch_count;leaf_obj["leaf_count"]=leaf_count
	wood_obj["root_count"]=len(root_paths)
	collection["root_paths"]=json.dumps([[tuple(p) for p in path] for path in root_paths])
	collection["seed"]=seed;collection["form"]=form
	collection["species"]=species;collection["stage"]=stage;collection["primary_radius_threshold"]=voxel_size*3
	collection["bark_asset"]=SPECIES[species]["bark"];collection["bark_relief_factor"]=(.18 if stage=="Juvenile" else .35 if species=="Birch" else 1)
	collection["generator_sha256"]=hashlib.sha256((OUT/"build_oak_studies.py").read_bytes()+(OUT/"growth.py").read_bytes()+(OUT/'surface.py').read_bytes()).hexdigest()
	collection["settings"]=json.dumps(settings)
	build_proxies(name,paths,scene,offset,trunk_profile,resolution)
	for mesh in list(bpy.data.meshes):
		if mesh.name.startswith(name+'_') and mesh.users==0:bpy.data.meshes.remove(mesh)
	print(json.dumps({"form":form,"branches":branch_count,"roots":len(root_paths),"leaves":leaf_count,"wood_vertices":len(wood.verts),"leaf_vertices":len(leaves.verts)}))
	return collection


def fuse_wood(form="Open_Grown"):
	name=PREFIX+form
	obj=bpy.data.objects[name+"_Wood"]
	collection=bpy.data.collections[name]
	if collection.get("protected_reference"):raise ValueError("Preserved reference geometry cannot be rebuilt")
	obj["bark_asset"]=collection.get("bark_asset","japanese_camphor_bark")
	obj["bark_relief_factor"]=collection.get("bark_relief_factor",1)
	source=obj.copy();source.data=obj.data.copy();source.name=name+"_UV_Source"
	collection.objects.link(source);source.hide_render=True;source.hide_set(True)
	for other in bpy.context.selected_objects: other.select_set(False)
	obj.select_set(True);bpy.context.view_layer.objects.active=obj
	yield 'Building continuous branch collars'
	spec=importlib.util.spec_from_file_location('voxels_tree_surface',OUT/'surface.py');surface=importlib.util.module_from_spec(spec);spec.loader.exec_module(surface)
	bm,statistics=yield from surface.branch_surface(json.loads(collection['surface_skeleton']))
	mesh=bpy.data.meshes.new(name+'_ConnectedSurface')
	bm.to_mesh(mesh);bm.free();mesh.update()
	for material in obj.data.materials:mesh.materials.append(material)
	obj.data=mesh
	for key,value in statistics.items():collection[key]=value
	collection['welded_faces']=len(mesh.polygons)
	# Each convex triangle produces three smooth quads. Keep the final surface
	# within the same one-million-face authoring budget before allocating it.
	if sum(len(p.vertices) for p in mesh.polygons)>1000000:raise ValueError('Connected wood exceeds the 1,000,000-face surface budget')
	yield 'Rounding connected branch surface'
	subdivision=obj.modifiers.new('Curved branch transitions','SUBSURF');subdivision.levels=1
	# One finite Catmull-Clark step supplies the rounded junctions. Blender's
	# default infinite-limit projection allocates much larger patch tables on
	# this branching mesh; it is not required for a one-step authoring surface.
	subdivision.use_limit_surface=False
	bpy.ops.object.modifier_apply(modifier=subdivision.name)
	collection['detailed_faces']=len(obj.data.polygons);collection['surface_method']='shared_collars'
	yield 'Preparing bark projection'
	yield from bind_fused_uv(obj,source)
	yield 'Sampling bark relief'
	displace_bark(obj)
	# Bind each leaf (or complete compound leaf and petiole) to the final wood.
	# Surface smoothing must not leave attachment points on a different curve.
	yield 'Attaching foliage to the finished wood'
	from mathutils.bvhtree import BVHTree
	import numpy as np
	tree=BVHTree.FromObject(obj,bpy.context.evaluated_depsgraph_get());max_offset=0.0
	for suffix in ('_Leaves','_Petioles'):
		foliage=bpy.data.objects.get(name+suffix)
		if foliage is None or not foliage.get('foliage_attachments'):continue
		coordinates=np.empty(len(foliage.data.vertices)*3,np.float32);foliage.data.vertices.foreach_get('co',coordinates);coordinates=coordinates.reshape(-1,3)
		bound=[]
		for start,end,anchor in json.loads(foliage['foliage_attachments']):
			point,normal,_,distance=tree.find_nearest(Vector(anchor));delta=point-Vector(anchor)
			coordinates[start:end]+=np.array(delta);max_offset=max(max_offset,distance);bound.append((start,end,tuple(point)))
		foliage.data.vertices.foreach_set('co',coordinates.ravel());foliage.data.update();foliage['foliage_attachments']=json.dumps(bound,separators=(',',':'))
	collection['foliage_attachment_max_offset']=max_offset
	for polygon in obj.data.polygons: polygon.use_smooth=True
	# Retain editable branch source in a clearly labelled hidden object.
	source.hide_viewport=True
	yield from split_wood_parts(name,obj,source,collection)
	print(json.dumps({"fused":form,"vertices":len(obj.data.vertices),"polygons":len(obj.data.polygons)}))
	obj.hide_render=True;obj.hide_set(True);obj["construction_source"]=True


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
	groups=[[],[],[]]
	for polygon in obj.data.polygons:
		part=obj.data.attributes["wood_part"].data[polygon.index].value
		groups[part].append(polygon)
	for part,polygons in enumerate(groups):
		geo=Geometry();lookup={};normals=[];parent_uv=[];weights=[]
		for face_index,polygon in enumerate(polygons):
			if face_index%2048==0:yield f'Finishing wood part {part+1} / 3 ({face_index} / {len(polygons)})'
			indices=[];uv=[]
			for loop_index in polygon.loop_indices:
				index=obj.data.loops[loop_index].vertex_index
				if index not in lookup:
					lookup[index]=len(geo.verts);geo.verts.append(tuple(coordinates[index]))
					geo.radii.append(radii[index])
				indices.append(lookup[index]);uv.append(tuple(uv_values[loop_index]))
				normals.append(tuple(normal_values[loop_index]))
				parent_uv.extend(parent_values[loop_index])
				weights.append(weight_values[loop_index])
			geo.face(indices,uv)
		part_name=("Trunk","Branches","Roots")[part]
		result=geo.object(name+"_"+part_name,collection,[obj.data.materials[0]])
		result.location=obj.location;result["render_part"]=part_name.lower()
		result.data.normals_split_custom_set(normals)
		result.data.uv_layers.new(name="BarkParent").data.foreach_set("uv",parent_uv)
		result.data.attributes.new("bark_parent_weight","FLOAT","CORNER").data.foreach_set("value",weights)
		result.data.uv_layers.active_index=0


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
	mesh=bpy.data.meshes.new(PREFIX+"GroundMesh");mesh.from_pydata([(-1000,-1000,-.065),(1000,-1000,-.065),(1000,1000,-.065),(-1000,1000,-.065)],[],[(0,1,2,3)])
	ground=add("Ground",mesh)
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
