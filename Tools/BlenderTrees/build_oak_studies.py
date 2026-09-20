"""Blender oak authoring study. Does not export or modify game assets."""
import bpy
import math
import random
import json
import hashlib
from pathlib import Path
from mathutils import Vector

ROOT = Path(r"C:\Users\Gray\Documents\s&box projects\voxels3")
OUT = ROOT / "Tools/BlenderTrees"
EVIDENCE = ROOT / "Docs/ValidationEvidence/BlenderTrees"
PREFIX = "OAK_STUDY_"
TAPER = 1.15
BARK_TILE_METRES = 1.8


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
	def __init__(self):
		self.verts=[]; self.faces=[]; self.uv=[]; self.mats=[];self.part=0
		self.sweeps=[];self.branch_ids=[];self.active_branch=-1

	def face(self, indices, uv, material=0):
		self.faces.append(indices); self.uv.extend(uv); self.mats.append(material)
		self.branch_ids.append(self.active_branch)

	def branch(self, path, radius, end, rng, sides=10, root=False,profile=None,parent=0):
		start=len(self.verts); distance=0.0
		v=0;last_radius=None;repeats=max(1,round(math.tau*radius/BARK_TILE_METRES))
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
			r=radius_at(radius,end,t,profile)
			if root:r*=1+.18*math.exp(-t*20)
			previous_v=v
			if last_radius is not None:v+=(p-path[i-1]).length/(math.tau*(last_radius+r)*.5/repeats)
			last_radius=r
			frames.append({"p":tuple(p),"axis":tuple(axis),"d":distance,"r":r})
			for j in range(sides):
				a=2*math.pi*j/sides
				irregular=1+.036*math.sin(3*a+phase+t*2)+.021*math.sin(5*a-phase+t*4)
				self.verts.append(tuple(p+(axis*math.cos(a)+cross*math.sin(a))*r*irregular))
			if i:
				for j in range(sides):
					k=(j+1)%sides
					self.face((start+(i-1)*sides+j,start+(i-1)*sides+k,start+i*sides+k,start+i*sides+j),
						((j/sides*repeats,previous_v),((j+1)/sides*repeats,previous_v),((j+1)/sides*repeats,v),(j/sides*repeats,v)),self.part)
		self.face(tuple(start+j for j in reversed(range(sides))),[(0,0)]*sides,self.part)
		last=start+(len(path)-1)*sides
		self.face(tuple(last+j for j in range(sides)),[(0,0)]*sides,self.part)
		self.sweeps.append({"frames":frames,"repeats":repeats,"radius":radius,"end":end,"root":root,"parent":parent,"phase":phase})

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

	def object(self,name,collection,materials):
		mesh=bpy.data.meshes.new(name)
		mesh.from_pydata(self.verts,[],self.faces); mesh.update()
		obj=bpy.data.objects.new(name,mesh); collection.objects.link(obj)
		for material in materials: mesh.materials.append(material)
		uv=mesh.uv_layers.new(name="UVMap")
		uv.data.foreach_set("uv",[v for pair in self.uv for v in pair])
		for polygon,material in zip(mesh.polygons,self.mats):
			polygon.use_smooth=True; polygon.material_index=material
		if name.endswith("_Wood"):
			attribute=mesh.attributes.new("branch_id","INT","FACE")
			attribute.data.foreach_set("value",self.branch_ids)
			obj["sweeps"]=json.dumps(self.sweeps)
		return obj


def material_setup():
	def fresh(name):
		mat=bpy.data.materials.get(PREFIX+name) or bpy.data.materials.new(PREFIX+name)
		mat.use_nodes=True; mat.node_tree.nodes.clear()
		return mat,mat.node_tree.nodes,mat.node_tree.links

	def image(path,noncolor=False):
		img=bpy.data.images.load(str(path),check_existing=True)
		if noncolor: img.colorspace_settings.name="Non-Color"
		return img

	bark,n,l=fresh("Scanned_Oak_Bark")
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
				tex=n.new("ShaderNodeTexImage");tex.image=image(paths/f"japanese_camphor_bark_{suffix}_{'4k' if suffix=='diff' else '2k'}.jpg",nc)
				l.new(vector,tex.inputs["Vector"]);l.new(tex.outputs["Color"],patch.inputs[index+1])
			if socket=="Normal":
				# Convert each tangent-space sample in its own UV frame before blending.
				normal=n.new("ShaderNodeNormalMap");normal.uv_map=coordinate.uv_map;normal.inputs["Strength"].default_value=1
				l.new(patch.outputs[0],normal.inputs["Color"]);l.new(normal.outputs["Normal"],mixed.inputs[i+1])
			else:l.new(patch.outputs[0],mixed.inputs[i+1])
		if socket=="Base Color":
			tone=n.new("ShaderNodeHueSaturation");tone.inputs["Saturation"].default_value=.55;tone.inputs["Value"].default_value=.85
			l.new(mixed.outputs[0],tone.inputs["Color"]);l.new(tone.outputs[0],shader.inputs[socket])
		elif socket=="Normal":
			normalize=n.new("ShaderNodeVectorMath");normalize.operation="NORMALIZE"
			l.new(mixed.outputs[0],normalize.inputs[0]);l.new(normalize.outputs[0],shader.inputs[socket])
		else:l.new(mixed.outputs[0],shader.inputs[socket])
	bark.diffuse_color=(.22,.18,.13,1)
	leaves=[]
	for i,mult in enumerate([(1.0,1.08,.82),(.84,.98,.7),(1.17,1.2,.86)]):
		mat,n,l=fresh(f"Oak_Leaf_{i}"); mat.diffuse_color=(.13+i*.015,.25+i*.024,.048,1)
		out=n.new("ShaderNodeOutputMaterial");shader=n.new("ShaderNodeBsdfPrincipled")
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
	return bark,leaves


def procedural_scaffold(seed,form,height,spread,girth,lean,upward,droop,character=.45,fork_height=.24,growth_direction=0,crown_bias=.25):
	"""Seeded growth skeleton; no stored specimen coordinates."""
	rng=random.Random(seed)
	woodland=form=="Woodland";weathered=form=="Weathered"
	stem_height=height*(.87 if woodland else .60 if weathered else .75)
	base=height*(.033 if weathered else .023 if woodland else .031)*girth
	phase=rng.uniform(0,2*math.pi)
	wind=Vector((math.cos(math.radians(growth_direction)),math.sin(math.radians(growth_direction)),0))
	trunk=[];wander=Vector((0,0,0));heading=Vector((0,0,0))
	for i in range(10):
		t=i/9;z=stem_height*t-.10
		heading=heading*.25+Vector((rng.uniform(-1,1),rng.uniform(-1,1),0))*height*character*.030
		wander+=heading*(.4+.6*t)
		p=Vector((0,0,z))+wander+wind*(math.tan(math.radians(lean))*z+crown_bias*height*.08*t*t)
		if i==0:p=Vector((0,0,-base*.9))
		trunk.append(p)
	trunk=smooth_path(trunk,5)
	count=rng.randint(7,10)+(1 if woodland else 0)
	limbs=[];weights=[]
	for i in range(count):
		t=fork_height+(i+rng.uniform(.05,.8))/count*(.88-fork_height)
		start,tangent=point_on(trunk,t)
		a=phase+i*2.399+rng.uniform(-.6,.6)
		vigor=rng.uniform(1-character*.65,1+character*.55)
		reach=height*spread*(.19 if woodland else .43 if weathered else .32)*rng.uniform(.73,1.12)*(1-.27*t)*vigor
		out=Vector((math.cos(a),math.sin(a),0));side=Vector((-out.y,out.x,0))
		end=start+out*reach+Vector((0,0,max(height*.075,height*(.62+rng.uniform(-.19,.15)*(1+character))-start.z)))
		# Mature limbs change direction through modest growth turns, not waves.
		bend=side*rng.uniform(-.20,.20)*reach*character
		start_direction=(out+Vector((0,0,rng.uniform(.22,.65)+upward*.2-droop*.12))).normalized()
		end_direction=(out*.55+Vector((0,0,rng.uniform(.45,.85)))).normalized()
		length=(end-start).length
		control1=start+start_direction*length*.30
		control2=end-end_direction*length*.27
		growth_turn=side*rng.uniform(-.045,.045)*reach*character
		controls=[start]
		for j in range(1,7):
			s=j/6
			p=start*(1-s)**3+control1*3*(1-s)**2*s+control2*3*(1-s)*s*s+end*s**3
			p+=bend*math.sin(math.pi*s)+growth_turn*max(0,1-abs(s-.55)/.25)
			p+=wind*crown_bias*height*.16*s*s
			controls.append(p)
		weights.append((reach/height)**1.7*rng.uniform(.75,1.2)*(1-.55*t))
		limbs.append((smooth_path(controls,5),0,.0035))
	total=sum(weights)/.995
	limbs=[(path,base*math.sqrt(weight/total),end) for (path,_,end),weight in zip(limbs,weights)]
	return [(trunk,base,.004),*limbs]


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


def read_guides(name):
	from mathutils.geometry import interpolate_bezier
	paths=[]
	for obj in sorted(bpy.data.collections[name+"_Guides"].objects,key=lambda o:o["order"]):
		points=obj.data.splines[0].bezier_points;path=[]
		for a,b in zip(points,points[1:]):path.extend(interpolate_bezier(a.co,a.handle_right,b.handle_left,b.co,7)[:-1])
		path.append(points[-1].co.copy())
		if paths and "trunk_attachment" in obj:
			anchor=point_on(paths[0][0],obj["trunk_attachment"])[0];delta=anchor-path[0]
			path=[p+delta*max(0,1-i/8) for i,p in enumerate(path)]
		paths.append((path,obj["root_radius"],obj["tip_radius"]))
	return paths


def build_tree(form="Open_Grown",seed=1701,offset=(0,0,0),height=16,spread=1.0,girth=1.0,lean=0.0,upward=.55,droop=.35,branch_density=1.35,leaf_density=1.9,branch_angle=57,character=.45,fork_height=.24,growth_direction=0,crown_bias=.25,root_spread=1,root_depth=1.2,use_guides=False,label=None):
	rng=random.Random(seed)
	leaf_rng=random.Random(seed+8191)
	scene=bpy.data.scenes.get(PREFIX+"Studio")
	if scene is None: scene=bpy.data.scenes.new(PREFIX+"Studio")
	bpy.context.window.scene=scene
	name=PREFIX+(label or form)
	if use_guides and not bpy.data.collections.get(name+"_Guides"):raise ValueError("Generate a tree before rebuilding its guides")
	paths=read_guides(name) if use_guides else procedural_scaffold(seed,form,height,spread,girth,lean,upward,droop,character,fork_height,growth_direction,crown_bias)
	if not use_guides:
		make_guides(name,paths,scene,offset)
		bpy.context.view_layer.update()
		paths=read_guides(name)
	old=bpy.data.collections.get(name)
	if old:
		for obj in list(old.objects): bpy.data.objects.remove(obj,do_unlink=True)
		bpy.data.collections.remove(old)
	collection=bpy.data.collections.new(name);scene.collection.children.link(collection)
	bark,leafmats=material_setup()
	wood=Geometry();fine=Geometry();leaves=Geometry();root_tips=Geometry()
	trunk,base_radius,trunk_end=paths[0]
	guide_objects=sorted(bpy.data.collections[name+"_Guides"].objects,key=lambda o:o["order"])
	attachments=[(guide["trunk_attachment"],radius*radius) for guide,(_,radius,_) in zip(guide_objects[1:],paths[1:])]
	last_attach=max(t for t,_ in attachments);trunk_profile=[]
	for i in range(65):
		t=i/64;flow=max(base_radius*base_radius-sum(area for _,area in attachments),base_radius*base_radius*.003)
		flow*=max(0,min(1,(1-t)/max(.01,1-last_attach)))**1.7
		for attach,area in attachments:
			f=max(0,min(1,(t-attach+.035)/.07));flow+=area*(1-f*f*(3-2*f))
		trunk_profile.append(max(trunk_end,math.sqrt(flow)))
	guide_objects[0]["radius_profile"]=trunk_profile
	wood.branch(trunk,base_radius,trunk_end,rng,24,True,trunk_profile)
	wood.part=1
	for path,radius,end in paths[1:]:wood.branch(path,radius,end,rng,16)
	# Preserve narrow living tips below the welded wood's voxel resolution.
	for i,(path,radius,end) in enumerate(paths):
		start=next((j/100 for j in range(100) if radius_at(radius,end,j/100,trunk_profile if i==0 else None)<=.055),.99)
		tail=[point_on(path,start+(1-start)*i/16)[0] for i in range(17)]
		fine.branch(tail,radius_at(radius,end,start,trunk_profile if i==0 else None),end,rng,8)
	# Roots have their own random stream; changing their controls preserves the crown.
	root_rng=random.Random(seed+4093);root_paths=[]
	wood.part=2;root_count=root_rng.randint(5,7);root_phase=root_rng.uniform(0,math.tau)
	root_origin=min(trunk,key=lambda p:abs(p.z)).copy();root_origin.z=base_radius*.03
	def root_mesh(path,radius,end,sides,parent=0):
		# Stop voxel wood above its resolution; continuous swept tips complete the root.
		if radius<=.07:
			root_tips.branch(path,radius,end,root_rng,10)
			return parent
		cut=max(0,1-((.05-end)/(radius-end))**(1/TAPER))
		stem=[point_on(path,cut*j/24)[0] for j in range(25)]
		wood.branch(stem,radius,.05,root_rng,sides,True,parent=parent)
		start=max(0,1-((.07-end)/(radius-end))**(1/TAPER))
		tail=[point_on(path,start+(1-start)*j/16)[0] for j in range(17)]
		root_tips.branch(tail,radius_at(radius,end,start),end,root_rng,10)
		return wood.active_branch
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
			child=smooth_path([p,p+tangent*reach*.28,p+direction*reach*.65+Vector((0,0,-depth*.18)),p+direction*reach+Vector((0,0,-depth*.35))],5)
			root_mesh(child,radius_at(radius,.012,t)*.62,.008,10,root_parent);root_paths.append(child)
	wood.part=1
	leaf_count=0;branch_count=len(paths)

	def shoot_foliage(path,radius):
		nonlocal leaf_count
		count=round(leaf_rng.randint(9,14)*leaf_density)
		for i in range(count):
			t=.1+.88*(i+leaf_rng.random()*.45)/count
			p,tangent=point_on(path,t)
			a=i*2.399+leaf_rng.uniform(-.5,.5)
			side=Vector((math.cos(a),math.sin(a),leaf_rng.uniform(-.24,.56)))
			direction=(tangent*.32+side).normalized()
			length=leaf_rng.uniform(.16,.25)
			leaves.leaf(p,direction,length,leaf_rng.uniform(-1.1,1.1),leaf_rng.randrange(4),leaf_rng)
			leaf_count+=1
		for i in range(3):
			p,tangent=point_on(path,.93+i*.02)
			direction=(tangent+Vector((leaf_rng.uniform(-.6,.6),leaf_rng.uniform(-.6,.6),leaf_rng.uniform(-.1,.5)))).normalized()
			leaves.leaf(p,direction,leaf_rng.uniform(.16,.23),leaf_rng.uniform(-1.5,1.5),leaf_rng.randrange(4),leaf_rng);leaf_count+=1

	def child_path(parent,t,length,azimuth,lift):
		p,tangent=point_on(parent,t)
		ref=Vector((0,0,1)) if abs(tangent.z)<.85 else Vector((1,0,0))
		across=tangent.cross(ref).normalized();other=tangent.cross(across).normalized()
		angle=math.radians(branch_angle+rng.uniform(-14,14))
		direction=(tangent*math.cos(angle)+(across*math.cos(azimuth)+other*math.sin(azimuth))*math.sin(angle)+Vector((0,0,lift*(upward+.2)-droop*.13))).normalized()
		end=p+direction*length
		bend=across*rng.uniform(-.13,.13)*length
		controls=[p,p+tangent*length*.12+direction*length*.16,p+direction*length*.58+bend,end+Vector((0,0,length*.06))]
		return smooth_path(controls,4)

	def branchlets(parent,radius,end,level,is_trunk=False,profile=None,parent_id=0):
		nonlocal branch_count
		if level==0:
			count=max(3,round(rng.randint(4,7)*branch_density));start=.22+rng.random()*.16;length_range=(2.2,3.8)
			if is_trunk:count=max(3,count//2);start=.67
		elif level==1:
			count=rng.randint(4,6);start=.18;length_range=(1.2,2.1)
		elif level==2:
			count=rng.randint(3,5);start=.16;length_range=(.55,1.05)
		else:
			count=rng.randint(2,4);start=.18;length_range=(.24,.51)
		phase=rng.random()*6.28
		for i in range(count):
			t=start+(1-start-.05)*(i+rng.uniform(.2,.75))/count
			length=rng.uniform(*length_range)*(1-.30*t)*(height/16)*(.72 if form=="Woodland" else 1)
			angle=phase+i*2.399+rng.uniform(-.6,.6)
			path=child_path(parent,t,length,angle,rng.uniform(.16,.57) if level<2 else rng.uniform(-.05,.33))
			parent_r=radius_at(radius,end,t,profile)
			r=max(.0026,parent_r*(.70 if level<2 else .49))
			tip=max(.001,r*.11)
			(wood if r>.045 else fine).branch(path,r,tip,rng,10 if r>.045 else 6,parent=parent_id)
			child_id=wood.active_branch if r>.045 else parent_id
			if r>.045:
				start_tip=max(0,1-(max(.001,.055-tip)/max(.001,r-tip))**(1/TAPER))
				tail=[point_on(path,start_tip+(1-start_tip)*j/8)[0] for j in range(9)]
				fine.branch(tail,radius_at(r,tip,start_tip),tip,rng,6)
			branch_count+=1
			if level<3: branchlets(path,r,tip,level+1,parent_id=child_id)
			else: shoot_foliage(path,r)
		if level>=2: shoot_foliage(parent,radius)
		if level==0:
			terminal=[point_on(parent,.90+j*.1/8)[0] for j in range(9)]
			branchlets(terminal,max(.004,radius*.04),.001,2,parent_id=parent_id)
	for i,(path,radius,end) in enumerate(paths):branchlets(path,radius,end,0,i==0,trunk_profile if i==0 else None,parent_id=i)
	wood_obj=wood.object(name+"_Wood",collection,[bark,bark,bark])
	fine_obj=fine.object(name+"_Twigs",collection,[bark])
	leaf_obj=leaves.object(name+"_Leaves",collection,leafmats)
	root_tip_obj=root_tips.object(name+"_RootTips",collection,[bark]);root_tip_obj["render_part"]="root_tips"
	for obj in collection.objects: obj.location=offset
	wood_obj["form"]=form;wood_obj["seed"]=seed
	wood_obj["branch_count"]=branch_count;leaf_obj["leaf_count"]=leaf_count
	wood_obj["root_count"]=len(root_paths)
	collection["root_paths"]=json.dumps([[tuple(p) for p in path] for path in root_paths])
	collection["seed"]=seed;collection["form"]=form
	collection["generator_sha256"]=hashlib.sha256((OUT/"build_oak_studies.py").read_bytes()).hexdigest()
	collection["settings"]=json.dumps(dict(height=height,spread=spread,girth=girth,lean=lean,upward=upward,droop=droop,branch_density=branch_density,leaf_density=leaf_density,branch_angle=branch_angle,character=character,fork_height=fork_height,growth_direction=growth_direction,crown_bias=crown_bias,root_spread=root_spread,root_depth=root_depth))
	build_proxies(name,paths,scene,offset,trunk_profile)
	for mesh in list(bpy.data.meshes):
		if mesh.name.startswith(PREFIX) and mesh.users==0:bpy.data.meshes.remove(mesh)
	print(json.dumps({"form":form,"branches":branch_count,"roots":len(root_paths),"leaves":leaf_count,"wood_vertices":len(wood.verts),"fine_vertices":len(fine.verts),"leaf_vertices":len(leaves.verts)}))
	return collection


def fuse_wood(form="Open_Grown"):
	name=PREFIX+form
	obj=bpy.data.objects[name+"_Wood"]
	collection=bpy.data.collections[name]
	source=obj.copy();source.data=obj.data.copy();source.name=name+"_UV_Source"
	collection.objects.link(source);source.hide_render=True;source.hide_set(True)
	for other in bpy.context.selected_objects: other.select_set(False)
	obj.select_set(True);bpy.context.view_layer.objects.active=obj
	modifier=obj.modifiers.new("Continuous branch forks","REMESH")
	modifier.mode="VOXEL";modifier.voxel_size=.035;modifier.use_smooth_shade=True
	bpy.ops.object.modifier_apply(modifier=modifier.name)
	smooth=obj.modifiers.new("Relax welded junctions","SMOOTH");smooth.factor=.5;smooth.iterations=8
	bpy.ops.object.modifier_apply(modifier=smooth.name)
	# A broader local relaxation rounds fork collars without thinning whole limbs.
	records=json.loads(source["sweeps"])
	junctions=[(Vector(record["frames"][0]["p"]),record["radius"]*2.8) for record in records[1:] if record["radius"]>.10]
	group=obj.vertex_groups.new(name="Fork collars")
	for vertex in obj.data.vertices:
		weight=max((max(0,1-(vertex.co-center).length/reach) for center,reach in junctions),default=0)
		if weight>.01:group.add([vertex.index],weight,"REPLACE")
	smooth=obj.modifiers.new("Round fork collars","SMOOTH");smooth.factor=.7;smooth.iterations=32;smooth.vertex_group=group.name
	bpy.ops.object.modifier_apply(modifier=smooth.name)
	bind_fused_uv(obj,source)
	# Preserve the established grain away from collars, relaxing only welded junctions.
	import bmesh
	bpy.ops.object.mode_set(mode="EDIT")
	try:
		bpy.ops.mesh.select_all(action="SELECT")
		for layer_name in ("UVMap","BarkParent"):
			obj.data.uv_layers.active=obj.data.uv_layers[layer_name]
			bm=bmesh.from_edit_mesh(obj.data);deform=bm.verts.layers.deform.verify();uv_layer=bm.loops.layers.uv[layer_name]
			for face in bm.faces:
				for loop in face.loops:
					loop[uv_layer].pin_uv=loop.vert[deform].get(group.index,0)<.04
			bmesh.update_edit_mesh(obj.data,loop_triangles=False,destructive=False)
			bpy.ops.uv.select_all(action="SELECT")
			bpy.ops.uv.minimize_stretch(iterations=30,fill_holes=False,blend=0)
	finally:bpy.ops.object.mode_set(mode="OBJECT")
	obj.data.uv_layers.active_index=0
	subdivision=obj.modifiers.new("Bark relief sampling","SUBSURF");subdivision.subdivision_type="SIMPLE";subdivision.levels=2
	bpy.ops.object.modifier_apply(modifier=subdivision.name)
	displace_bark(obj)
	for polygon in obj.data.polygons: polygon.use_smooth=True
	# Retain editable branch source in a clearly labelled hidden object.
	source.hide_viewport=True
	split_wood_parts(name,obj,source,collection)
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
	cache={};depths=[0.0]*len(obj.data.vertices)
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
				depths[vertex]=max(depths[vertex],min(.045,radius*.15))
			u,v,pu,pv,weight=cache[key]
			if first_u is None:first_u=u
			else:u+=round((first_u-u)/repeats)*repeats
			uv.data[loop_index].uv=(u,v)
			if first_parent_u is None:first_parent_u=pu
			else:pu+=round((first_parent_u-pu)/parent_repeats)*parent_repeats
			parent_uv.data[loop_index].uv=(pu,pv);weights.data[loop_index].value=weight
	obj.data.uv_layers.active=uv
	obj.data.attributes.new("bark_depth","FLOAT","POINT").data.foreach_set("value",depths)


def displace_bark(obj):
	"""Real scanned height relief, evaluated before splitting shared wood boundaries."""
	import numpy as np
	mesh=obj.data;count=len(mesh.vertices);loops=len(mesh.loops)
	img=bpy.data.images.load(str(OUT/"Textures/japanese_camphor_bark_disp_2k.png"),check_existing=True);img.colorspace_settings.name="Non-Color"
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
	coords=coords.reshape(-1,3)+normals.reshape(-1,3)*((height-.455)*depth)[:,None]
	mesh.vertices.foreach_set("co",coords.ravel());mesh.update()
	obj["bark_relief_max_range_m"]=.045


def split_wood_parts(name,obj,source,collection):
	groups=[[],[],[]]
	for polygon in obj.data.polygons:
		part=obj.data.attributes["wood_part"].data[polygon.index].value
		groups[part].append(polygon)
	for part,polygons in enumerate(groups):
		geo=Geometry();lookup={};normals=[];parent_uv=[];weights=[]
		for polygon in polygons:
			indices=[];uv=[]
			for loop_index in polygon.loop_indices:
				index=obj.data.loops[loop_index].vertex_index
				if index not in lookup:
					lookup[index]=len(geo.verts);geo.verts.append(tuple(obj.data.vertices[index].co))
				indices.append(lookup[index]);uv.append(tuple(obj.data.uv_layers.active.data[loop_index].uv))
				normals.append(tuple(obj.data.corner_normals[loop_index].vector))
				parent_uv.extend(obj.data.uv_layers["BarkParent"].data[loop_index].uv)
				weights.append(obj.data.attributes["bark_parent_weight"].data[loop_index].value)
			geo.face(indices,uv)
		part_name=("Trunk","Branches","Roots")[part]
		result=geo.object(name+"_"+part_name,collection,[obj.data.materials[0]])
		result.location=obj.location;result["render_part"]=part_name.lower()
		result.data.normals_split_custom_set(normals)
		result.data.uv_layers.new(name="BarkParent").data.foreach_set("uv",parent_uv)
		result.data.attributes.new("bark_parent_weight","FLOAT","CORNER").data.foreach_set("value",weights)
		result.data.uv_layers.active_index=0


def build_proxies(name,paths,scene,offset,trunk_profile):
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
		for point,radius in [(start-direction*.055,r0),(end+direction*.055,r1)]:
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
		segment(p0,p1,max(.02,r0),max(.02,r1),name+f"_COL_SOLID_Trunk_{i:02}","solid_trunk")
	for i,(path,radius,end) in enumerate(paths[1:]):
		for j in range(3):
			t0=j/3;t1=(j+1)/3
			segment(point_on(path,t0)[0],point_on(path,t1)[0],radius*(1-t0)+.38,radius*(1-t1)+.45,name+f"_COL_SOFT_Branch_{i:02}_{j}","branch_interaction")


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
	mesh=bpy.data.meshes.new(PREFIX+"GroundMesh");mesh.from_pydata([(-100,-100,-.065),(100,-100,-.065),(100,100,-.065),(-100,100,-.065)],[],[(0,1,2,3)])
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
	camera.location=target+Vector((math.cos(a)*32*math.cos(e),math.sin(a)*32*math.cos(e),32*math.sin(e)))
	camera.rotation_euler=(target-camera.location).to_track_quat("-Z","Y").to_euler();camera.data.ortho_scale=scale


def show_forms(forms,foliage=True):
	for collection in bpy.data.collections:
		if collection.name.startswith(PREFIX) and collection.name not in (PREFIX+"Stage",):
			visible=collection.name.removeprefix(PREFIX) in forms
			for obj in collection.objects:
				if obj.get("collision_role") or obj.type=="CURVE":obj.hide_render=True;obj.hide_set(True);continue
				if obj.name.endswith("_UV_Source") or obj.get("construction_source"):continue
				obj.hide_render=not visible or (not foliage and obj.name.endswith("_Leaves"))
				obj.hide_set(obj.hide_render)


def save():
	OUT.mkdir(exist_ok=True,parents=True);EVIDENCE.mkdir(exist_ok=True,parents=True)
	recipes=[]
	for collection in bpy.data.collections:
		if collection.name.startswith(PREFIX) and "generator_sha256" in collection:
			guides=[]
			for obj in sorted(bpy.data.collections[collection.name+"_Guides"].objects,key=lambda o:o["order"]):
				guides.append({"order":obj["order"],"root_radius":obj["root_radius"],"tip_radius":obj["tip_radius"],"trunk_attachment":obj.get("trunk_attachment"),"points":[{"co":tuple(p.co),"left":tuple(p.handle_left),"right":tuple(p.handle_right),"left_type":p.handle_left_type,"right_type":p.handle_right_type} for p in obj.data.splines[0].bezier_points]})
			recipes.append({"name":collection.name,"seed":collection["seed"],"form":collection["form"],"generator_sha256":collection["generator_sha256"],"settings":json.loads(collection["settings"]),"guides":guides})
	(OUT/"recipes.json").write_text(json.dumps({"schema":1,"trees":recipes},indent=2)+"\n",encoding="utf-8")
	for image in bpy.data.images:
		if image.source=="FILE" and image.filepath: image.pack()
	bpy.ops.wm.save_as_mainfile(filepath=str(OUT/"oak_studies.blend"),compress=True)
	print("Saved oak_studies.blend")


def render(name,azimuth=-55,foliage=True,forms=("Open_Grown",),center=(0,0,7.4),scale=19.5,elevation=8):
	show_forms(forms,foliage);set_view(azimuth,center,scale,elevation)
	scene=bpy.data.scenes[PREFIX+"Studio"]
	scene.render.filepath=str(EVIDENCE/(name+".png"))
	bpy.ops.render.render(write_still=True)
	print("Rendered",scene.render.filepath)
