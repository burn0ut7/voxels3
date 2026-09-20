bl_info={"name":"Voxels Tree Lab","author":"Voxels3","version":(0,6,1),"blender":(5,2,0),"category":"Add Mesh","description":"Seeded multi-species tree library with growth stages, guides, roots and collision roles"}
import bpy
import json
from pathlib import Path
from bpy.props import IntProperty,FloatProperty,BoolProperty,EnumProperty,StringProperty,PointerProperty

SOURCE=Path(r"C:\Users\Gray\Documents\s&box projects\voxels3\Tools\BlenderTrees\build_oak_studies.py")
PREFIX="OAK_STUDY_"
_TREE_ITEMS=[]


def generator():
	namespace={};exec(compile(SOURCE.read_text(encoding="utf-8"),str(SOURCE),"exec"),namespace)
	return namespace


def preset(self,context):
	if self.loading:return
	self.loading=True
	try:
		for key,value in generator()["preset_settings"](self.species,self.stage,self.form).items():setattr(self,key,value)
	finally:self.loading=False


def tree_items(self,context):
	global _TREE_ITEMS
	_TREE_ITEMS=[(c.name.removeprefix(PREFIX),f"{c.get('species','Oak')} / {c.get('stage','Mature')} / {c['seed']}"+(" â€” preserved" if c.get('protected_reference') else "")+f" / {c['form'].replace('_',' ')}",c.name) for c in sorted(bpy.data.collections,key=lambda c:c.name) if c.name.startswith(PREFIX) and 'generator_sha256' in c]
	return _TREE_ITEMS or [('NONE','No generated trees','Generate your first tree')]


def select_tree(self,context):
	if self.loading or self.selected_tree=='NONE':return
	collection=bpy.data.collections.get(PREFIX+self.selected_tree)
	if collection is None:return
	self.loading=True
	try:
		self.species=collection.get('species','Oak');self.stage=collection.get('stage','Mature');self.form=collection['form'];self.seed=collection['seed']
		for key,value in json.loads(collection['settings']).items():setattr(self,key,value)
		self.active_tree=self.selected_tree;self.show_guides=False;self.show_collision=False
	finally:self.loading=False
	ns=generator();ns['show_forms']((self.active_tree,),True);ns['set_view'](-55,(0,0,self.height*.46),self.height*1.45)
	context.scene['active_tree']=self.active_tree


def overlay(self,context):
	name=PREFIX+self.active_tree
	for suffix,visible in [("_Guides",self.show_guides),("_Collision",self.show_collision)]:
		for collection in bpy.data.collections:
			if collection.name.startswith(PREFIX) and collection.name.endswith(suffix):
				for obj in collection.objects:obj.hide_set(not (visible and collection.name==name+suffix))


class TREE_LAB_Settings(bpy.types.PropertyGroup):
	loading:BoolProperty(default=False,options={'HIDDEN'})
	selected_tree:EnumProperty(name="Saved Specimen",items=tree_items,update=select_tree)
	species:EnumProperty(name="Species",items=[('Oak','English oak','Lobed leaves and spreading structural limbs'),('Ash','Common ash','Opposite compound leaves and upward branching'),('Spruce','Norway spruce â€” evergreen','Single leader, layered branches and individual needles'),('Birch','Silver birch','Light drooping crown, toothed leaves and pale bark')],update=preset)
	stage:EnumProperty(name="Growth Stage",items=[('Juvenile','Juvenile','Fewer branching generations, slender stem and young bark'),('Mature','Mature','Established species crown and branching'),('Large','Large / older','Greater height, girth and structural development')],default='Mature',update=preset)
	seed:IntProperty(name="Seed",default=1701,min=0,max=2147483647)
	form:EnumProperty(name="Growth Habit",items=[("Open_Grown","Open-grown","Open space and broad crown"),("Woodland","Woodland","Taller stem and narrower crown"),("Weathered","Weathered","Leaning and uneven crown")],update=preset)
	height:FloatProperty(name="Height",default=16,min=1.5,max=45,subtype="DISTANCE")
	spread:FloatProperty(name="Crown Spread",default=1,min=.35,max=1.8)
	girth:FloatProperty(name="Trunk Girth",default=1,min=.3,max=1.7)
	lean:FloatProperty(name="Lean (degrees)",default=0,min=-25,max=25)
	upward:FloatProperty(name="Upward Growth",default=.55,min=0,max=1)
	droop:FloatProperty(name="Branch Droop",default=.35,min=0,max=1)
	branch_angle:FloatProperty(name="Branch Angle",default=57,min=20,max=85)
	character:FloatProperty(name="Crooked Growth",default=.45,min=0,max=1)
	fork_height:FloatProperty(name="First Fork Height",description="Fraction of main stem height",default=.24,min=.06,max=.65)
	growth_direction:FloatProperty(name="Growth Direction (degrees)",default=0,min=-180,max=180)
	crown_bias:FloatProperty(name="One-sided Crown",default=.25,min=0,max=1)
	branch_density:FloatProperty(name="Branch Density",default=1.35,min=.55,max=1.8)
	leaf_density:FloatProperty(name="Leaf Density",default=1.9,min=.2,max=2.4)
	root_spread:FloatProperty(name="Root Spread",default=1,min=.5,max=2)
	root_depth:FloatProperty(name="Root Depth",default=1.2,min=.2,max=3,subtype="DISTANCE")
	active_tree:StringProperty(name="Current Tree",default="Open_Grown_1701")
	show_guides:BoolProperty(name="Show Direction Guides",default=False,update=overlay)
	show_collision:BoolProperty(name="Show Collision Proxies",default=False,update=overlay)


class TREE_LAB_OT_Generate(bpy.types.Operator):
	bl_idname="tree_lab.generate";bl_label="Generate from Seed";bl_options={"REGISTER","UNDO"}
	use_guides:BoolProperty(default=False)
	new_seed:BoolProperty(default=False)
	def execute(self,context):
		p=context.scene.tree_lab;ns=generator();ns['end_gallery']()
		if self.new_seed:p.seed=(p.seed+1)%2147483647
		label=p.active_tree if self.use_guides else f"{p.species}_{p.stage}_{p.form}_{p.seed}"
		args={key:getattr(p,key) for key in (*ns['TREE_SETTINGS'],'species','stage','form','seed')}
		if self.use_guides:
			collection=bpy.data.collections.get(PREFIX+label)
			if collection:
				args.update(seed=collection['seed'],form=collection['form'],species=collection.get('species','Oak'),stage=collection.get('stage','Mature'))
		try:
			if context.mode!="OBJECT":bpy.ops.object.mode_set(mode="OBJECT")
			ns['build_tree'](**args,use_guides=self.use_guides,label=label)
			ns['fuse_wood'](label)
			if context.scene.camera is None:ns['setup_studio']()
			p=context.scene.tree_lab;p.selected_tree=label
		except Exception as error:
			self.report({'ERROR'},str(error));return {'CANCELLED'}
		self.report({'INFO'},f"Generated {label}; other specimens retained")
		return {'FINISHED'}


class TREE_LAB_OT_View(bpy.types.Operator):
	bl_idname='tree_lab.view';bl_label='View Library'
	gallery:BoolProperty(default=False)
	def execute(self,context):
		ns=generator();p=context.scene.tree_lab
		if self.gallery:ns['show_gallery']()
		else:ns['show_forms']((p.active_tree,),True);ns['set_view'](-55,(0,0,p.height*.46),p.height*1.45)
		return {'FINISHED'}


class TREE_LAB_OT_Save(bpy.types.Operator):
	bl_idname='tree_lab.save';bl_label='Save Tree Library'
	def execute(self,context):
		ns=generator();p=context.scene.tree_lab;ns['end_gallery']();ns['show_forms']((p.active_tree,),True)
		ns['set_view'](-55,(0,0,p.height*.46),p.height*1.45);ns['save']()
		self.report({'INFO'},'Saved tree_library.blend; original oak file retained')
		return {'FINISHED'}


class TREE_LAB_PT_Panel(bpy.types.Panel):
	bl_label="Procedural Tree Lab";bl_idname="TREE_LAB_PT_Panel";bl_space_type="VIEW_3D";bl_region_type="UI";bl_category="Tree Lab"
	def draw(self,context):
		p=context.scene.tree_lab;l=self.layout
		box=l.box();box.prop(p,'selected_tree');row=box.row(align=True)
		row.operator('tree_lab.view',text='Current tree');row.operator('tree_lab.view',text='All trees').gallery=True
		collection=bpy.data.collections.get(PREFIX+p.active_tree)
		if collection and collection.get('protected_reference'):box.label(text='Preserved oak reference',icon='LOCKED')
		if collection and collection.get('species')=='Spruce' and collection.get('stage')!='Juvenile':box.label(text='Fewer preview needles; renders show all')
		l.prop(p,'species');l.prop(p,'stage');l.prop(p,'form');l.prop(p,'seed')
		for key in ('height','spread','girth','lean','upward','droop','branch_angle','character','fork_height','growth_direction','crown_bias','branch_density','leaf_density'):l.prop(p,key)
		box=l.box();box.label(text='Roots into the ground');box.prop(p,'root_spread');box.prop(p,'root_depth')
		row=l.row(align=True);row.operator('tree_lab.generate',text='Generate from Seed');row.operator('tree_lab.generate',text='Next Seed').new_seed=True
		box=l.box();box.label(text='Direct individual branches');box.prop(p,'show_guides')
		row=box.row();row.enabled=not bool(collection and collection.get('protected_reference'));row.operator('tree_lab.generate',text='Rebuild from Edited Guides').use_guides=True
		box=l.box();box.label(text='Gameplay Parts');box.prop(p,'show_collision');box.label(text='Trunk: solid convex pieces');box.label(text='Branches: nonblocking proxies')
		l.operator('tree_lab.save',icon='FILE_TICK')


CLASSES=(TREE_LAB_Settings,TREE_LAB_OT_Generate,TREE_LAB_OT_View,TREE_LAB_OT_Save,TREE_LAB_PT_Panel)
def register():
	for cls in CLASSES:bpy.utils.register_class(cls)
	bpy.types.Scene.tree_lab=PointerProperty(type=TREE_LAB_Settings)
def unregister():
	del bpy.types.Scene.tree_lab
	for cls in reversed(CLASSES):bpy.utils.unregister_class(cls)

if __name__=='__main__':register()
