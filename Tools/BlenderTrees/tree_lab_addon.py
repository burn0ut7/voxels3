bl_info={"name":"s&box Tree Growth","author":"Voxels3","version":(1,0,0),"blender":(5,2,0),"category":"Add Mesh","description":"Shared seasonal tree growth, species profiles, recipes and s&box source geometry"}
import bpy
import json
import time
from mathutils import Vector
from pathlib import Path
from bpy.props import IntProperty,FloatProperty,BoolProperty,EnumProperty,StringProperty,PointerProperty

SOURCE=Path(__file__).resolve().with_name('build_oak_studies.py')
PREFIX="OAK_STUDY_"
_TREE_ITEMS=[]


def generator():
	namespace={'__file__':str(SOURCE)};exec(compile(SOURCE.read_text(encoding="utf-8"),str(SOURCE),"exec"),namespace)
	return namespace


def preset(self,context):
	if self.loading:return
	self.loading=True
	try:
		for key,value in generator()["preset_settings"](self.species,self.stage,self.form).items():setattr(self,key,value)
	finally:self.loading=False


def tree_items(self,context):
	global _TREE_ITEMS
	_TREE_ITEMS=[]
	for c in sorted(bpy.data.collections,key=lambda c:c.name):
		if not c.name.startswith(PREFIX) or 'generator_sha256' not in c:continue
		age=f"{json.loads(c['settings'])['age']} seasons" if c.get('growth_graph') else c.get('stage','Mature')
		kind='preview' if c.get('growth_preview') else 'source'
		label=f"{c.get('species','Oak')} / {age} / {c['seed']} / {c['form'].replace('_',' ')} / {kind}"
		if c.get('protected_reference'):label+=' / preserved'
		_TREE_ITEMS.append((c.name.removeprefix(PREFIX),label,c.name))
	return _TREE_ITEMS or [('NONE','No generated trees','Generate your first tree')]


def select_tree(self,context):
	if self.loading or self.selected_tree=='NONE':return
	collection=bpy.data.collections.get(PREFIX+self.selected_tree)
	if collection is None:return
	self.loading=True
	try:
		self.species=collection.get('species','Oak');self.stage=collection.get('stage','Mature');self.form=collection['form'];self.seed=collection['seed']
		for key,value in json.loads(collection['settings']).items():
			if hasattr(self,key):setattr(self,key,value)
		self.active_tree=self.selected_tree;self.show_guides=False;self.show_collision=False
		self.progress=(f"Source geometry ready ({collection.get('mesh_seconds',0):.1f}s build)" if collection.get('source_ready') else 'Growth preview ready' if collection.get('growth_preview') else 'Stored source specimen')
	finally:self.loading=False
	ns=generator();ns['show_forms']((self.active_tree,),True);focus_growth(context,collection)
	context.scene['active_tree']=self.active_tree


def focus_growth(context,collection):
	if not collection.get('growth_graph'):return
	graph=json.loads(collection['growth_graph']);points=[Vector(node['p']) for node in graph['nodes']]
	low=Vector(tuple(min(p[i] for p in points) for i in range(3)));high=Vector(tuple(max(p[i] for p in points) for i in range(3)))
	for area in context.screen.areas:
		if area.type=='VIEW_3D':
			view=area.spaces.active.region_3d;view.view_perspective='PERSP';view.view_location=(low+high)*.5;view.view_distance=max(2,(high-low).length*1.2)


def overlay(self,context):
	name=PREFIX+self.active_tree
	for suffix,visible in [("_Guides",self.show_guides),("_Collision",self.show_collision)]:
		for collection in bpy.data.collections:
			if collection.name.startswith(PREFIX) and collection.name.endswith(suffix):
				for obj in collection.objects:obj.hide_set(not (visible and collection.name==name+suffix))


class TREE_LAB_Settings(bpy.types.PropertyGroup):
	loading:BoolProperty(default=False,options={'HIDDEN'})
	selected_tree:EnumProperty(name="Saved Specimen",items=tree_items,update=select_tree)
	species:EnumProperty(name="Species",items=[('Oak','English oak','Lobed leaves and spreading structural limbs'),('Ash','Common ash','Opposite compound leaves and upward branching'),('Spruce','Norway spruce (evergreen)','Single leader, layered branches and individual needles'),('Birch','Silver birch','Light drooping crown, toothed leaves and pale bark')],update=preset)
	stage:EnumProperty(name="Age Preset",items=[('Juvenile','Juvenile','Six growth seasons; young bark'),('Mature','Mature','Twenty-four growth seasons'),('Large','Older','Thirty growth seasons')],default='Mature',update=preset)
	seed:IntProperty(name="Seed",default=1701,min=0,max=2147483647)
	form:EnumProperty(name="Growth Habit",items=[("Open_Grown","Open-grown","Open space and broad crown"),("Woodland","Woodland","Taller stem and narrower crown"),("Weathered","Weathered","Leaning and uneven crown")],update=preset)
	height:FloatProperty(name="Potential Height",default=16,min=1.5,max=45,subtype="DISTANCE")
	spread:FloatProperty(name="Crown Spread",default=1,min=.35,max=1.8)
	girth:FloatProperty(name="Trunk Girth",default=1,min=.3,max=1.7)
	lean:FloatProperty(name="Lean (degrees)",default=0,min=-25,max=25)
	upward:FloatProperty(name="Upward Growth",default=.55,min=0,max=1)
	droop:FloatProperty(name="Branch Droop",default=.35,min=0,max=1)
	branch_angle:FloatProperty(name="Branch Angle",default=57,min=20,max=85)
	character:FloatProperty(name="Crooked Growth",default=.45,min=0,max=1)
	fork_height:FloatProperty(name="Branch Onset",description="Delay before main-stem lateral buds can activate",default=.24,min=.06,max=.65)
	growth_direction:FloatProperty(name="Light Azimuth (degrees)",default=0,min=-180,max=180)
	crown_bias:FloatProperty(name="Directional Light Bias",default=.25,min=0,max=1)
	branch_density:FloatProperty(name="Branch Density",default=1.35,min=.55,max=1.8)
	leaf_density:FloatProperty(name="Leaf Density",default=1.9,min=.2,max=2.4)
	root_spread:FloatProperty(name="Root Spread",default=1,min=.5,max=2)
	root_depth:FloatProperty(name="Root Depth",default=1.2,min=.2,max=3,subtype="DISTANCE")
	age:IntProperty(name="Growth Seasons",description="Simulated seasons, not calibrated chronological years",default=24,min=1,max=80)
	competition:FloatProperty(name="Canopy Competition",description="Light extinction from the tree's own foliage",default=.65,min=0,max=2)
	light_response:FloatProperty(name="Seek Light",default=.4,min=0,max=2)
	resource:FloatProperty(name="Resource Supply",description="Growth-resource multiplier; not a soil chemistry model",default=1,min=.1,max=2)
	growing:BoolProperty(default=False,options={'HIDDEN'})
	cancel_requested:BoolProperty(default=False,options={'HIDDEN'})
	progress:StringProperty(default='',options={'HIDDEN'})
	active_tree:StringProperty(name="Current Tree",default="Open_Grown_1701")
	show_guides:BoolProperty(name="Show Direction Guides",default=False,update=overlay)
	show_collision:BoolProperty(name="Show Collision Proxies",default=False,update=overlay)


class TREE_LAB_OT_Grow(bpy.types.Operator):
	bl_idname='tree_lab.grow';bl_label='Simulate Growth';bl_options={'REGISTER','UNDO'}
	new_seed:BoolProperty(default=False)
	_timer=None

	def begin(self,context):
		p=context.scene.tree_lab
		if p.growing:raise ValueError('A growth job is already running; press Esc to cancel it')
		self.ns=generator()
		if self.new_seed:p.seed=(p.seed+1)%2147483647
		self.settings={key:getattr(p,key) for key in (*self.ns['TREE_SETTINGS'],'species','stage','form','seed')}
		module,recipe=self.ns['growth_recipe'](self.settings)
		self.job=module.Simulation(recipe);self.scene=context.scene;self.started=time.perf_counter()
		self.label=f"Growth_{p.species}_{p.age}_{p.form}_{p.seed}"
		p.cancel_requested=False;p.growing=True;p.progress='Starting growth'

	def complete(self,context):
		graph=self.job.finish()
		collection=self.ns['publish_growth'](graph,self.label,self.settings)
		collection['growth_seconds']=time.perf_counter()-self.started
		p=self.scene.tree_lab;p.growing=False;p.progress=f"{len(graph['nodes'])} nodes / {len(graph['axes'])} branches"
		p.selected_tree=self.label
		return {'FINISHED'}

	def execute(self,context):
		# Scripted authoring uses the same job as the interactive modal operator.
		try:
			self.begin(context)
			while self.job.step():pass
			return self.complete(context)
		except Exception as error:
			if hasattr(self,'scene'):self.scene.tree_lab.growing=False
			context.scene.tree_lab.progress=f'Growth failed: {error}'
			self.report({'ERROR'},str(error));return {'CANCELLED'}

	def invoke(self,context,event):
		try:self.begin(context)
		except Exception as error:
			self.report({'ERROR'},str(error));return {'CANCELLED'}
		self._timer=context.window_manager.event_timer_add(.02,window=context.window)
		context.window_manager.modal_handler_add(self)
		return {'RUNNING_MODAL'}

	def modal(self,context,event):
		if event.type=='ESC' or context.scene!=self.scene or self.scene.tree_lab.cancel_requested:
			self.cancel(context);return {'CANCELLED'}
		if event.type!='TIMER':return {'PASS_THROUGH'}
		try:
			if self.job.step():
				self.scene.tree_lab.progress=f"Season {self.job.season} / {self.job.recipe.age}"
				for area in context.screen.areas:area.tag_redraw()
				return {'RUNNING_MODAL'}
			context.window_manager.event_timer_remove(self._timer);self._timer=None
			return self.complete(context)
		except Exception as error:
			self.cancel(context);self.scene.tree_lab.progress=f'Growth failed: {error}'
			self.report({'ERROR'},str(error));return {'CANCELLED'}

	def cancel(self,context):
		if self._timer:context.window_manager.event_timer_remove(self._timer);self._timer=None
		self.scene.tree_lab.growing=False;self.scene.tree_lab.progress='Growth cancelled; previous specimen retained'


class TREE_LAB_OT_CancelGrowth(bpy.types.Operator):
	bl_idname='tree_lab.cancel_growth';bl_label='Cancel Build'
	def execute(self,context):
		context.scene.tree_lab.cancel_requested=True
		return {'FINISHED'}


class TREE_LAB_OT_Generate(bpy.types.Operator):
	bl_idname='tree_lab.generate';bl_label='Build Source Geometry';bl_options={'REGISTER'}
	_timer=None

	def retire(self,label):
		prefix=PREFIX+label
		collection=bpy.data.collections.get(prefix)
		if collection and collection.get('protected_reference'):raise ValueError('Cannot replace a preserved source')
		for obj in list(bpy.data.objects):
			if obj.name.startswith(prefix+'_'):bpy.data.objects.remove(obj,do_unlink=True)
		for suffix in ('','_Guides','_Collision'):
			collection=bpy.data.collections.get(prefix+suffix)
			if collection:bpy.data.collections.remove(collection)
		for database in (bpy.data.meshes,bpy.data.curves,bpy.data.node_groups,bpy.data.materials):
			for item in list(database):
				if item.name.startswith(prefix+'_') and item.users==0:database.remove(item)

	def begin(self,context):
		import uuid
		p=context.scene.tree_lab
		if p.growing:raise ValueError('An authoring job is already running')
		self.ns=generator();collection=bpy.data.collections.get(PREFIX+p.active_tree)
		if collection is None or not collection.get('growth_graph'):raise ValueError('Simulate growth before building source geometry')
		if collection.get('protected_reference'):raise ValueError('This is a preserved source specimen')
		self.settings={key:getattr(p,key) for key in (*self.ns['TREE_SETTINGS'],'species','stage','form','seed')}
		self.graph=json.loads(collection['growth_graph']);module,recipe=self.ns['growth_recipe'](self.settings);module.validate(self.graph)
		if self.graph['recipe']!=module.asdict(recipe):raise ValueError('Growth controls changed; simulate again before meshing')
		self.label=p.active_tree.removesuffix('_Source')+'_Source'
		old=bpy.data.collections.get(PREFIX+self.label)
		if old and (old.get('protected_reference') or not old.get('growth_graph')):raise ValueError('Cannot replace this stored specimen')
		self.growth_revision=collection.get('growth_generator_sha256',collection['generator_sha256'])
		self.pending='Build_'+uuid.uuid4().hex[:12];self.scene=context.scene;self.started=time.perf_counter()
		self.ns['end_gallery']();self.job=self.work(context)
		p.cancel_requested=False;p.growing=True;p.progress='Preparing source geometry'

	def work(self,context):
		if context.mode!='OBJECT':bpy.ops.object.mode_set(mode='OBJECT')
		yield from self.ns['build_tree'](**self.settings,label=self.pending,graph=self.graph)
		yield from self.ns['fuse_wood'](self.pending)
		yield 'Publishing completed source geometry'
		self.retire(self.label)
		prefix=PREFIX+self.pending;destination=PREFIX+self.label
		for database in (bpy.data.objects,bpy.data.collections,bpy.data.meshes,bpy.data.curves,bpy.data.materials,bpy.data.node_groups):
			for item in list(database):
				if item.name==prefix or item.name.startswith(prefix+'_'):item.name=destination+item.name[len(prefix):]
		collection=bpy.data.collections[destination]
		collection['source_ready']=True;collection['growth_generator_sha256']=self.growth_revision
		collection['mesh_seconds']=time.perf_counter()-self.started
		p=self.scene.tree_lab;p.growing=False;p.progress=f"Source mesh complete in {collection['mesh_seconds']:.1f}s"
		p.selected_tree=self.label

	def execute(self,context):
		try:
			self.begin(context)
			for progress in self.job:self.scene.tree_lab.progress=progress
			return {'FINISHED'}
		except Exception as error:
			if hasattr(self,'scene'):self.cancel(context)
			context.scene.tree_lab.progress=f'Mesh build failed: {error}'
			self.report({'ERROR'},str(error));return {'CANCELLED'}

	def invoke(self,context,event):
		try:self.begin(context)
		except Exception as error:
			self.report({'ERROR'},str(error));return {'CANCELLED'}
		self._timer=context.window_manager.event_timer_add(.02,window=context.window)
		context.window_manager.modal_handler_add(self);return {'RUNNING_MODAL'}

	def modal(self,context,event):
		if event.type=='ESC' or context.scene!=self.scene or self.scene.tree_lab.cancel_requested:
			self.cancel(context);return {'CANCELLED'}
		if event.type!='TIMER':return {'PASS_THROUGH'}
		try:
			self.scene.tree_lab.progress=next(self.job)
			for area in context.screen.areas:area.tag_redraw()
		except StopIteration:
			context.window_manager.event_timer_remove(self._timer);self._timer=None;return {'FINISHED'}
		except Exception as error:
			self.cancel(context);self.scene.tree_lab.progress=f'Mesh build failed: {error}'
			self.report({'ERROR'},str(error));return {'CANCELLED'}
		return {'RUNNING_MODAL'}

	def cancel(self,context):
		if self._timer:context.window_manager.event_timer_remove(self._timer);self._timer=None
		# Close while mesh/context references still exist, allowing edit-mode
		# finally blocks to restore object mode before retiring temporary IDs.
		self.job.close();self.retire(self.pending)
		self.scene.tree_lab.growing=False;self.scene.tree_lab.progress='Mesh build cancelled; previous specimen retained'
		if context.scene==self.scene:self.ns['show_forms']((self.scene.tree_lab.active_tree,),True)


class TREE_LAB_OT_GrowthFile(bpy.types.Operator):
	bl_idname='tree_lab.growth_file';bl_label='Growth Recipe'
	filepath:StringProperty(subtype='FILE_PATH')
	load:BoolProperty(default=False)
	filter_glob:StringProperty(default='*.tree.json',options={'HIDDEN'})
	def execute(self,context):
		try:
			if not self.filepath:raise ValueError('Choose a recipe file')
			ns=generator();module=ns['growth_module']();path=Path(self.filepath)
			if self.load:
				payload=json.loads(path.read_text(encoding='utf-8'))
				if payload.get('format')!='sbox-tree-growth' or payload.get('version')!=1:raise ValueError('Unsupported growth recipe format')
				if not payload.get('graph',{}).get('sha256'):raise ValueError('Growth recipe is missing its graph checksum')
				module.validate(payload['graph'])
				_,recipe=ns['growth_recipe'](payload['settings'])
				if module.asdict(recipe)!=payload['graph']['recipe']:raise ValueError('Recipe and stored growth graph disagree')
				settings=payload['settings'];ns['preset_settings'](settings['species'],settings['stage'],settings['form'])
				for key,low,high in (('leaf_density',.2,2.4),('root_spread',.5,2),('root_depth',.2,3)):
					if not low-1e-6<=settings[key]<=high+1e-6:raise ValueError(f'Invalid source geometry control: {key}')
				label=f"Growth_{settings['species']}_{settings['age']}_{settings['form']}_{settings['seed']}"
				ns['publish_growth'](payload['graph'],label,settings,payload['generator_sha256']);context.scene.tree_lab.selected_tree=label
			else:
				collection=bpy.data.collections.get(PREFIX+context.scene.tree_lab.active_tree)
				if collection is None or not collection.get('growth_graph'):raise ValueError('Select a simulated specimen first')
				graph=json.loads(collection['growth_graph']);module.validate(graph)
				payload={'format':'sbox-tree-growth','version':1,'generator_sha256':collection.get('growth_generator_sha256',collection['generator_sha256']),'settings':json.loads(collection['settings']),'graph':graph}
				if not str(path).endswith('.tree.json'):path=Path(str(path)+'.tree.json')
				path.parent.mkdir(parents=True,exist_ok=True)
				temporary=path.with_suffix(path.suffix+'.tmp');temporary.write_text(json.dumps(payload,indent=2,allow_nan=False)+'\n',encoding='utf-8');temporary.replace(path)
		except Exception as error:
			self.report({'ERROR'},str(error));return {'CANCELLED'}
		return {'FINISHED'}

	def invoke(self,context,event):
		context.window_manager.fileselect_add(self);return {'RUNNING_MODAL'}


class TREE_LAB_OT_View(bpy.types.Operator):
	bl_idname='tree_lab.view';bl_label='View Library'
	gallery:BoolProperty(default=False)
	def execute(self,context):
		ns=generator();p=context.scene.tree_lab
		if self.gallery:ns['show_gallery']()
		else:ns['show_forms']((p.active_tree,),True);focus_growth(context,bpy.data.collections[PREFIX+p.active_tree])
		return {'FINISHED'}


class TREE_LAB_OT_Export(bpy.types.Operator):
	bl_idname='tree_lab.export_sbox';bl_label='Prepare for s&box'
	bl_description='Bake materials and three mesh detail levels for the current stored tree; preserve the Blender source'
	@classmethod
	def poll(cls,context):
		return SOURCE.with_name('export_sbox.py').is_file() and not context.scene.tree_lab.growing
	def execute(self,context):
		import runpy
		p=context.scene.tree_lab
		collection=bpy.data.collections.get(PREFIX+p.active_tree)
		if collection is None or collection.get('growth_preview') or (collection.get('growth_graph') and not collection.get('source_ready')):
			self.report({'ERROR'},'Build source geometry from the growth graph before exporting models');return {'CANCELLED'}
		if collection.get('growth_graph') and not 1<collection.get('primary_count',0)<=256:
			self.report({'ERROR'},'Source mesh is complete; the current game wind adapter requires 2 to 256 structural axes');return {'CANCELLED'}
		try:
			export=runpy.run_path(str(SOURCE.with_name('export_sbox.py')))
			if collection.get('surface_method')=='shared_collars' and 'shared_collars' not in export.get('SUPPORTED_SURFACE_METHODS',()):
				raise ValueError('The installed game exporter does not support connected branch surfaces yet; Blender source is preserved')
			export['export_specimen'](p.active_tree,2048 if collection.get('stage')=='Juvenile' else 4096)
		except Exception as error:
			self.report({'ERROR'},str(error));return {'CANCELLED'}
		self.report({'INFO'},'Prepared three detail levels and trunk collision; export is ready to install in s&box')
		return {'FINISHED'}


class TREE_LAB_PT_Panel(bpy.types.Panel):
	bl_label='s&box Tree Growth';bl_idname='TREE_LAB_PT_Panel';bl_space_type='VIEW_3D';bl_region_type='UI';bl_category='Tree Growth'
	def draw(self,context):
		p=context.scene.tree_lab;l=self.layout
		library=l.column();library.enabled=not p.growing
		library.prop(p,'selected_tree');row=library.row(align=True);row.operator('tree_lab.view',text='Current');row.operator('tree_lab.view',text='Gallery').gallery=True
		collection=bpy.data.collections.get(PREFIX+p.active_tree)
		controls=l.column();controls.enabled=not p.growing
		for key in ('species','stage','age','form','seed'):controls.prop(p,key)
		box=controls.box();box.label(text='Growth and environment')
		for key in ('height','resource','competition','light_response','growth_direction','crown_bias'):box.prop(p,key)
		box=controls.box();box.label(text='Branch architecture')
		for key in ('spread','girth','lean','upward','droop','branch_angle','character','fork_height','branch_density'):box.prop(p,key)
		row=controls.row(align=True);row.operator('tree_lab.grow',text='Simulate Growth');row.operator('tree_lab.grow',text='Next Seed').new_seed=True
		if p.progress:l.label(text=p.progress)
		if p.growing:l.operator('tree_lab.cancel_growth',text='Cancel Build (Esc)')
		row=controls.row(align=True);row.operator('tree_lab.growth_file',text='Save Recipe');row.operator('tree_lab.growth_file',text='Load Recipe').load=True
		box=controls.box();box.label(text='Derived source and game export')
		for key in ('leaf_density','root_spread','root_depth'):box.prop(p,key)
		box.operator('tree_lab.generate');box.operator('tree_lab.export_sbox',icon='EXPORT')
		box.label(text='Preview first; geometry and baking are separate')


CLASSES=(TREE_LAB_Settings,TREE_LAB_OT_Grow,TREE_LAB_OT_CancelGrowth,TREE_LAB_OT_Generate,TREE_LAB_OT_GrowthFile,TREE_LAB_OT_View,TREE_LAB_OT_Export,TREE_LAB_PT_Panel)

def register():
	for cls in CLASSES:bpy.utils.register_class(cls)
	bpy.types.Scene.tree_lab=PointerProperty(type=TREE_LAB_Settings)
def unregister():
	del bpy.types.Scene.tree_lab
	for cls in reversed(CLASSES):bpy.utils.unregister_class(cls)

if __name__=='__main__':register()
