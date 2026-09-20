bl_info={"name":"Voxels Tree Lab","author":"Voxels3","version":(0,4,0),"blender":(5,2,0),"category":"Add Mesh","description":"Seeded tree growth, editable limb guides, roots and distinct collision roles"}
import bpy
from pathlib import Path
from bpy.props import IntProperty,FloatProperty,BoolProperty,EnumProperty,StringProperty,PointerProperty

SOURCE=Path(r"C:\Users\Gray\Documents\s&box projects\voxels3\Tools\BlenderTrees\build_oak_studies.py")

def generator():
	namespace={};exec(compile(SOURCE.read_text(encoding="utf-8"),str(SOURCE),"exec"),namespace)
	return namespace

def preset(self,context):
	settings={"Open_Grown":(16,1,1,0,.55,.35,57,.45,.24,.25),"Woodland":(21,.8,.8,2,.8,.12,44,.35,.47,.2),"Weathered":(14,1.1,1.15,7,.4,.45,59,.65,.17,.55)}[self.form]
	self.height,self.spread,self.girth,self.lean,self.upward,self.droop,self.branch_angle,self.character,self.fork_height,self.crown_bias=settings
	self.branch_density=1.35;self.leaf_density=1.9
	self.root_spread=1;self.root_depth=1.2

def overlay(self,context):
	name="OAK_STUDY_"+self.active_tree
	for suffix,visible in [("_Guides",self.show_guides),("_Collision",self.show_collision)]:
		for collection in bpy.data.collections:
			if collection.name.startswith("OAK_STUDY_") and collection.name.endswith(suffix):
				for obj in collection.objects:obj.hide_set(not (visible and collection.name==name+suffix))

class TREE_LAB_Settings(bpy.types.PropertyGroup):
	seed:IntProperty(name="Seed",default=1701,min=0,max=2147483647)
	form:EnumProperty(name="Growth Habit",items=[("Open_Grown","Open-grown oak","Broad crown and lower scaffold limbs"),("Woodland","Woodland oak","Tall clear trunk and narrower upper crown"),("Weathered","Weathered oak","Leaning, heavier trunk and spreading crown")],update=preset)
	height:FloatProperty(name="Height",default=16,min=4,max=35,subtype="DISTANCE")
	spread:FloatProperty(name="Crown Spread",default=1,min=.35,max=1.8)
	girth:FloatProperty(name="Trunk Girth",default=1,min=.4,max=1.7)
	lean:FloatProperty(name="Lean (degrees)",default=0,min=-25,max=25)
	upward:FloatProperty(name="Upward Growth",default=.55,min=0,max=1)
	droop:FloatProperty(name="Branch Droop",default=.35,min=0,max=1)
	branch_angle:FloatProperty(name="Branch Angle",default=57,min=20,max=85)
	character:FloatProperty(name="Crooked Growth",default=.45,min=0,max=1)
	fork_height:FloatProperty(name="First Fork Height",description="Fraction of the main stem height",default=.24,min=.1,max=.65)
	growth_direction:FloatProperty(name="Growth Direction (degrees)",default=0,min=-180,max=180)
	crown_bias:FloatProperty(name="One-sided Crown",default=.25,min=0,max=1)
	branch_density:FloatProperty(name="Branch Density",default=1.35,min=.55,max=1.8)
	leaf_density:FloatProperty(name="Leaf Density",default=1.9,min=.2,max=2.4)
	root_spread:FloatProperty(name="Root Spread",description="Multiplier of root reach relative to trunk size",default=1,min=.5,max=2)
	root_depth:FloatProperty(name="Root Depth",description="Typical depth below local soil level",default=1.2,min=.4,max=3,subtype="DISTANCE")
	active_tree:StringProperty(name="Current Tree",default="Open_Grown")
	show_guides:BoolProperty(name="Show Direction Guides",default=False,update=overlay)
	show_collision:BoolProperty(name="Show Collision Proxies",default=False,update=overlay)

class TREE_LAB_OT_Generate(bpy.types.Operator):
	bl_idname="tree_lab.generate";bl_label="Generate from Seed";bl_options={"REGISTER","UNDO"}
	use_guides:BoolProperty(default=False)
	new_seed:BoolProperty(default=False)
	def execute(self,context):
		p=context.scene.tree_lab
		if self.new_seed:p.seed=(p.seed+1)%2147483647
		label=p.active_tree if self.use_guides else f"{p.form}_{p.seed}"
		args={key:getattr(p,key) for key in ("form","seed","height","spread","girth","lean","upward","droop","branch_density","leaf_density","branch_angle","character","fork_height","growth_direction","crown_bias","root_spread","root_depth")}
		if self.use_guides:
			collection=bpy.data.collections.get("OAK_STUDY_"+label)
			if collection:
				args["seed"]=collection["seed"];args["form"]=collection["form"]
		ns=generator()
		try:
			if context.mode!="OBJECT":bpy.ops.object.mode_set(mode="OBJECT")
			ns["build_tree"](**args,use_guides=self.use_guides,label=label)
			ns["fuse_wood"](label)
			if context.scene.camera is None:ns["setup_studio"]()
			ns["show_forms"]((label,),True)
			ns["set_view"](-55,(0,0,p.height*.46),p.height*1.25)
			p=context.scene.tree_lab
			for key,value in args.items():setattr(p,key,value)
			p.active_tree=label;p.show_guides=False;p.show_collision=False
			context.scene["active_tree"]=label
		except Exception as error:
			self.report({"ERROR"},str(error));return {"CANCELLED"}
		self.report({"INFO"},f"Generated {label}: editable meshes, limb guides and collision proxies")
		return {"FINISHED"}

class TREE_LAB_PT_Panel(bpy.types.Panel):
	bl_label="Procedural Tree Lab";bl_idname="TREE_LAB_PT_Panel";bl_space_type="VIEW_3D";bl_region_type="UI";bl_category="Tree Lab"
	def draw(self,context):
		p=context.scene.tree_lab;l=self.layout
		l.prop(p,"form");l.prop(p,"seed")
		for name in ("height","spread","girth","lean","upward","droop","branch_angle","character","fork_height","growth_direction","crown_bias","branch_density","leaf_density"):l.prop(p,name)
		box=l.box();box.label(text="Roots into the ground")
		box.prop(p,"root_spread");box.prop(p,"root_depth")
		l.operator("tree_lab.generate",text="Generate from Seed",icon="OUTLINER_OB_CURVES")
		l.operator("tree_lab.generate",text="Next Seed",icon="FILE_REFRESH").new_seed=True
		box=l.box();box.label(text="Direct individual branches")
		box.prop(p,"show_guides")
		box.label(text="Edit a guide curve in Edit Mode.")
		box.operator("tree_lab.generate",text="Rebuild from Edited Guides").use_guides=True
		box=l.box();box.label(text="Gameplay Parts")
		box.prop(p,"show_collision")
		box.label(text="Trunk: solid convex pieces")
		box.label(text="Branches: nonblocking trigger proxies")
		box.label(text="Leaves: visual only")
		l.label(text="Current: "+p.active_tree)

CLASSES=(TREE_LAB_Settings,TREE_LAB_OT_Generate,TREE_LAB_PT_Panel)
def register():
	for cls in CLASSES:bpy.utils.register_class(cls)
	bpy.types.Scene.tree_lab=PointerProperty(type=TREE_LAB_Settings)
def unregister():
	del bpy.types.Scene.tree_lab
	for cls in reversed(CLASSES):bpy.utils.unregister_class(cls)

if __name__=="__main__":register()
