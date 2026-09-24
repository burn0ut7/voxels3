"""Continuous wood from closed branch volumes inside Blender."""
import json
import math
import time
import bpy
import bmesh
import numpy as np

RELATIVE_SURFACE_TOLERANCE = .03
# Offline source capacity, separate from the game export triangle guard.
MAX_SURFACE_FACES = 4000000


def fill_enclosed_voids(mesh):
	"""Retain the outside of solid wood; reject detached branch surfaces.

	Overlapping curved sweeps can enclose tiny sealed air pockets. Their inward
	shells are disconnected from the exterior but are not detached branches.
	Only negatively oriented shells wholly inside the exterior may be filled.
	"""
	unseen=set(mesh.verts);components=[]
	while unseen:
		first=unseen.pop();pending=[first];vertices=[]
		while pending:
			vertex=pending.pop();vertices.append(vertex)
			for edge in vertex.link_edges:
				other=edge.other_vert(vertex)
				if other in unseen:unseen.remove(other);pending.append(other)
		components.append(vertices)
	if len(components)<=1:return 0,0
	components.sort(key=len,reverse=True)
	exterior=components[0];faces={face for vertex in exterior for face in vertex.link_faces}
	identity=mesh.faces.layers.int['branch_id'];branches={face[identity] for face in faces}
	from mathutils.bvhtree import BVHTree
	indices={vertex:index for index,vertex in enumerate(exterior)}
	bvh=BVHTree.FromPolygons([vertex.co for vertex in exterior],[[indices[vertex] for vertex in face.verts] for face in faces])
	if mesh.calc_volume(signed=True)<=0:raise ValueError('Solid wood union has inverted exterior orientation')
	removed=[];removed_faces=0
	for vertices in components[1:]:
		faces={face for vertex in vertices for face in vertex.link_faces}
		origin=tuple(vertices[0].co);terms=[]
		for face in faces:
			points=[tuple(float(vertex.co[i])-origin[i] for i in range(3)) for vertex in face.verts]
			a=points[0]
			for index in range(1,len(points)-1):
				b,c=points[index:index+2]
				terms.append(a[0]*(b[1]*c[2]-b[2]*c[1])+a[1]*(b[2]*c[0]-b[0]*c[2])+a[2]*(b[0]*c[1]-b[1]*c[0]))
		if math.fsum(terms)>=0 or not {face[identity] for face in faces}<=branches:
			raise ValueError('Solid wood union contains a detached branch surface')
		# Check the entire shell's vertices and face centers against the oriented
		# closed exterior. Any touching/ambiguous/outside shell remains a failure.
		for point in [*(vertex.co for vertex in vertices),*(face.calc_center_median() for face in faces)]:
			near,normal,_,distance=bvh.find_nearest(point)
			if near is None or distance<=1e-7 or (point-near).dot(normal)>=-1e-7:
				raise ValueError('Disconnected wood shell is not enclosed by the exterior')
		removed.extend(vertices);removed_faces+=len(faces)
	bmesh.ops.delete(mesh,geom=removed,context='VERTS')
	return len(components)-1,removed_faces


def round_junction(mesh,group,width,serial):
	"""Round a complete junction fan, then refresh normals in its full context."""
	faces={face for edge in group for vertex in edge.verts for face in vertex.link_faces}
	vertices={vertex for face in faces for vertex in face.verts};edges={edge for face in faces for edge in face.edges}
	identity=mesh.faces.layers.int['branch_id'];radius=mesh.verts.layers.float['bark_radius']
	patch=bmesh.new()
	try:
		patch_identity=patch.faces.layers.int.new('branch_id');patch_radius=patch.verts.layers.float.new('bark_radius')
		copied={};original={}
		for vertex in sorted(vertices,key=lambda v:v.index):
			copy=patch.verts.new(vertex.co);copy[patch_radius]=vertex[radius];copy.normal=vertex.normal
			copied[vertex]=copy;original[copy]=vertex
		copied_edges={}
		for edge in sorted(edges,key=lambda e:e.index):copied_edges[edge]=patch.edges.new(tuple(copied[v] for v in edge.verts))
		for face in sorted(faces,key=lambda f:f.index):
			copy=patch.faces.new(tuple(copied[v] for v in face.verts));copy[patch_identity]=face[identity];copy.normal=face.normal
		bmesh.ops.bevel(patch,geom=[copied_edges[edge] for edge in group],offset=width,segments=3,profile=.5,affect='EDGES',clamp_overlap=True,loop_slide=True)
		for face in faces:mesh.faces.remove(face)
		stitched={}
		for vertex in patch.verts:
			full=original.get(vertex)
			if full is None:
				full=mesh.verts.new(vertex.co);full.index=serial[0];serial[0]+=1
			else:full.co=vertex.co
			full[radius]=vertex[patch_radius];stitched[vertex]=full
		for edge in patch.edges:
			ends=tuple(stitched[v] for v in edge.verts)
			if mesh.edges.get(ends) is None:
				full=mesh.edges.new(ends);full.index=serial[1];serial[1]+=1
		for face in patch.faces:
			full=mesh.faces.new(tuple(stitched[v] for v in face.verts));full[identity]=face[patch_identity];full.normal=face.normal
			full.index=serial[2];serial[2]+=1
		for edge in edges:
			if edge.is_valid and not edge.link_faces:mesh.edges.remove(edge)
		for vertex in vertices:
			if vertex.is_valid and not vertex.link_edges:mesh.verts.remove(vertex)
		# A copied open patch does not contain every boundary vertex's fan.
		# Copying its partial normal back contaminated subsequent junctions.
		for vertex in stitched.values():vertex.normal_update()
	finally:patch.free()


def branch_surface(source):
	"""Unite swept branches without narrowing a parent to fit a small twig.

	The stored sweep mesh is immutable. Temporary operands are local to the
	current transactional build and are removed on completion or cancellation.
	"""
	mesh=source.data;prefix=source.name+'_Union'
	coordinates=np.empty((len(mesh.vertices),3),np.float32);mesh.vertices.foreach_get('co',coordinates.ravel())
	radii=np.empty(len(mesh.vertices),np.float32);mesh.attributes['bark_radius'].data.foreach_get('value',radii)
	ids=np.empty(len(mesh.polygons),np.int32);mesh.attributes['branch_id'].data.foreach_get('value',ids)
	starts=np.empty(len(mesh.polygons),np.int32);mesh.polygons.foreach_get('loop_start',starts)
	counts=np.empty(len(mesh.polygons),np.int32);mesh.polygons.foreach_get('loop_total',counts)
	vertices=np.empty(len(mesh.loops),np.int32);mesh.loops.foreach_get('vertex_index',vertices)
	order=np.argsort(ids,kind='stable');keys,offsets=np.unique(ids[order],return_index=True)
	if len(keys)==0 or keys[0]<0:raise ValueError('Wood contains an unidentified branch volume')
	sweeps=json.loads(source['sweeps'])
	branch_radii=[max(frame['r'] for frame in sweep['frames']) for sweep in sweeps]
	plans=[];operand_faces=0;input_rings=0;output_rings=0;maximum_error=0.0
	for group,key in enumerate(keys):
		selected=order[offsets[group]:offsets[group+1] if group+1<len(keys) else len(order)]
		sizes=counts[selected];local_starts=np.cumsum(sizes,dtype=np.int32)-sizes;total=int(sizes.sum())
		loops=np.repeat(starts[selected]-local_starts,sizes)+np.arange(total,dtype=np.int32)
		used=np.unique(vertices[loops]);frames=sweeps[int(key)]['frames'];rings=len(frames)
		sides=len(used)//rings
		if sides<3 or rings*sides!=len(used) or len(selected)!=(rings-1)*sides+2:
			raise ValueError('Branch volume does not match its stored sweep rings')
		points=coordinates[used].reshape(rings,sides,3)
		distances=np.array([frame['d'] for frame in frames]);sizes_at_ring=np.array([frame['r'] for frame in frames])
		# Bound the deviation of every original ring corner, including taper
		# and frame rotation. Keep the immutable reference for bark/motion.
		keep={0,rings-1};pending=[(0,rings-1)]
		while pending:
			first,last=pending.pop()
			if last-first<2:continue
			t=(distances[first+1:last]-distances[first])/max(distances[last]-distances[first],1e-12)
			linear=points[first][None]+t[:,None,None]*(points[last]-points[first])[None]
			error=np.linalg.norm(points[first+1:last]-linear,axis=2).max(axis=1)/np.maximum(sizes_at_ring[first+1:last],1e-12)
			worst=int(error.argmax());amount=float(error[worst])
			if amount>RELATIVE_SURFACE_TOLERANCE:
				middle=first+1+worst;keep.add(middle);pending.extend(((first,middle),(middle,last)))
			else:maximum_error=max(maximum_error,amount)
		keep=np.array(sorted(keep),np.int32);input_rings+=rings;output_rings+=len(keep)
		used=used.reshape(rings,sides)[keep].ravel()
		plans.append((key,used,sides,len(keep)))
		operand_faces+=(len(keep)-1)*sides+2
		if group%64==63:yield f'Checking branch surface capacity {group+1} / {len(keys)}'
	if operand_faces>MAX_SURFACE_FACES:
		raise ValueError(f'Branch operands require {operand_faces:,} faces; offline surface limit is {MAX_SURFACE_FACES:,}')
	operands=bpy.data.collections.new(prefix);bpy.context.scene.collection.children.link(operands)
	objects=[];meshes=[];base=None
	try:
		for group,(key,used,sides,rings) in enumerate(plans):
			lower=np.arange((rings-1)*sides,dtype=np.int32);following=lower//sides*sides+(lower+1)%sides
			quads=np.stack((lower,following,following+sides,lower+sides),axis=1)
			indices=np.concatenate((quads.ravel(),np.arange(sides-1,-1,-1,dtype=np.int32),np.arange(len(used)-sides,len(used),dtype=np.int32)))
			sizes=np.full(len(quads)+2,4,np.int32);sizes[-2:]=sides
			local_starts=np.cumsum(sizes,dtype=np.int32)-sizes;total=len(indices)
			data=bpy.data.meshes.new(prefix+f'_{int(key)}');meshes.append(data)
			data.vertices.add(len(used));data.vertices.foreach_set('co',coordinates[used].ravel())
			data.loops.add(total);data.loops.foreach_set('vertex_index',indices.astype(np.int32))
			data.polygons.add(len(sizes));data.polygons.foreach_set('loop_start',local_starts)
			data.polygons.foreach_set('loop_total',sizes);data.update(calc_edges=True)
			data.attributes.new('branch_id','INT','FACE').data.foreach_set('value',np.full(len(sizes),key,np.int32))
			data.attributes.new('bark_radius','FLOAT','POINT').data.foreach_set('value',radii[used])
			item=bpy.data.objects.new(data.name,data);objects.append(item)
			if base is None:
				base=item;source.users_collection[0].objects.link(item)
			else:operands.objects.link(item)
			item.hide_render=True;item.hide_set(True)
			if group%32==31:yield f'Preparing branch solids {group+1} / {len(keys)}'
		yield 'Joining overlapping branch volumes'
		started=time.perf_counter()
		if len(objects)>1:
			modifier=base.modifiers.new('Continuous wood','BOOLEAN');modifier.operation='UNION'
			modifier.solver='MANIFOLD';modifier.operand_type='COLLECTION';modifier.collection=operands
			with bpy.context.temp_override(object=base,active_object=base,selected_objects=[base],selected_editable_objects=[base]):
				bpy.ops.object.modifier_apply(modifier=modifier.name)
			if base.data not in meshes:meshes.append(base.data)
		union_seconds=time.perf_counter()-started
		if len(base.data.polygons)>MAX_SURFACE_FACES:raise ValueError(f'Solid union exceeds the {MAX_SURFACE_FACES:,}-face offline surface limit')
		yield 'Rounding branch intersections'
		bm=bmesh.new();bm.from_mesh(base.data);bm.normal_update()
		try:
			if any(not edge.is_manifold for edge in bm.edges):raise ValueError('Branch union is not closed before rounding')
			identity=bm.faces.layers.int.get('branch_id')
			if identity is None:raise ValueError('Solid union lost branch identity')
			enclosed_voids,enclosed_void_faces=fill_enclosed_voids(bm)
			# Boolean cuts can leave microscopic spokes beside an intersection.
			# The bevel overlap clamp then shrinks the entire collar to that spoke.
			# Clean only this local cut debris, at the same relative resolution as
			# the sweep rings, before collecting the surviving junction edges.
			from mathutils.kdtree import KDTree
			from mathutils.geometry import tessellate_polygon
			short_edges={};radius_trees={};cleanup_started=time.perf_counter();cleanup_operations=0;cleanup_edges=0
			for edge in bm.edges:
				if len(edge.link_faces)!=2 or edge.is_convex:continue
				pair={face[identity] for face in edge.link_faces}
				if len(pair)!=2:continue
				tolerance=min(branch_radii[branch] for branch in pair)*RELATIVE_SURFACE_TOLERANCE
				for vertex in edge.verts:
					for neighbor in vertex.link_edges:
						if (neighbor.is_manifold and {face[identity] for v in neighbor.verts for face in v.link_faces}<=pair
							and neighbor.calc_length()<tolerance):
							candidate=(tolerance,tuple(sorted(pair)))
							short_edges[neighbor]=min(candidate,short_edges.get(neighbor,candidate))
			def cut_tolerance(edge,pair,tolerance):
				if not edge.is_valid or not edge.is_manifold:return 0.0
				if any(not vertex.is_manifold for vertex in edge.verts):return 0.0
				if not {face[identity] for vertex in edge.verts for face in vertex.link_faces}<=set(pair):return 0.0
				# Collapsing a manifold edge can still pinch a handle or merge
				# duplicate faces. Require compatible endpoint links as well.
				a,b=edge.verts;shared_faces=set(a.link_faces)&set(b.link_faces)
				if shared_faces!=set(edge.link_faces):return 0.0
				common={e.other_vert(a) for e in a.link_edges}&{e.other_vert(b) for e in b.link_edges}
				opposite={v for face in edge.link_faces if len(face.verts)==3 for v in face.verts if v not in (a,b)}
				if common!=opposite:return 0.0
				remaining=set()
				midpoint=(a.co+b.co)*.5
				for face in set(a.link_faces)|set(b.link_faces):
					vertices=frozenset(a if v==b else v for v in face.verts)
					if len(vertices)<3:continue
					if vertices in remaining:return 0.0
					remaining.add(vertices)
					# Disjoint single-edge collapses move both ends to their
					# midpoint. Check the original face tessellation there before
					# allowing a surviving triangle to flatten or turn inside out.
					coordinates=[v.co.copy() for v in face.verts]
					if len({tuple(point) for point in coordinates})!=len(coordinates):return 0.0
					for indices in tessellate_polygon([coordinates]):
						corners=[face.verts[index] for index in indices]
						if len({a if v==b else v for v in corners})<3:continue
						triangle=[coordinates[index] for index in indices]
						before=(triangle[1]-triangle[0]).cross(triangle[2]-triangle[0])
						after=[midpoint if v in (a,b) else v.co for v in corners]
						normal=(after[1]-after[0]).cross(after[2]-after[0])
						if before.length_squared<=0 or normal.length_squared<=before.length_squared*1e-12:return 0.0
						if normal.dot(before)<=0:return 0.0
				# Manifold creates intersection vertices with zero point attributes.
				# Use the immutable sweep's nearby radii, not those unset values.
				for branch in pair:
					frames=sweeps[branch]['frames']
					if branch not in radius_trees:
						tree=KDTree(len(frames))
						for i,frame in enumerate(frames):tree.insert(frame['p'],i)
						tree.balance();radius_trees[branch]=tree
					for vertex in edge.verts:
						nearby=radius_trees[branch].find_n(vertex.co,min(3,len(frames)))
						tolerance=min(tolerance,min(frames[i]['r'] for _,i,_ in nearby)*RELATIVE_SURFACE_TOLERANCE)
				return tolerance
			# The native operator scans the mesh on every call. Round tolerances
			# DOWN, and batch only cuts whose complete neighboring face fans have
			# disjoint vertices. No cut in a batch can invalidate another's guard.
			buckets={}
			for edge,(tolerance,pair) in sorted(short_edges.items(),key=lambda item:item[0].index):
				tolerance=cut_tolerance(edge,pair,tolerance)
				if tolerance<=0:continue
				distance=2.0**math.floor(math.log2(tolerance))
				if edge.calc_length()<distance:buckets.setdefault(distance,[]).append((edge,pair))
			for distance,pending in sorted(buckets.items()):
				while pending:
					batch=[];deferred=[];reserved=set()
					for edge,pair in pending:
						if cut_tolerance(edge,pair,distance)<distance or edge.calc_length()>=distance:continue
						region={v for endpoint in edge.verts for face in endpoint.link_faces for v in face.verts}
						if region&reserved:deferred.append((edge,pair));continue
						batch.append(edge);reserved.update(region)
					if batch:
						bmesh.ops.collapse(bm,edges=batch,uvs=False)
						cleanup_operations+=1;cleanup_edges+=len(batch)
					pending=deferred
					yield f'Cleaning junction cuts ({cleanup_operations} batches)'
			bm.verts.index_update();bm.edges.index_update();bm.faces.index_update();bm.normal_update()
			cleanup_seconds=time.perf_counter()-cleanup_started
			if any(not edge.is_manifold for edge in bm.edges):raise ValueError('Junction cleanup opened the surface')
			# Each junction has its own overlap clamp. A single microscopic edge
			# must not reduce the rounding radius of every other fork in the tree.
			widths={}
			for edge in bm.edges:
				if len(edge.link_faces)!=2:continue
				a,b=(face[identity] for face in edge.link_faces)
				if a!=b and not edge.is_convex:widths[edge]=min(branch_radii[a],branch_radii[b])*.4
			blended=len(widths);remaining=set(widths);groups=[]
			# Visit the same minimum-index seeds without rescanning every remaining
			# edge for each of thousands of disconnected junction fans.
			for first in sorted(widths,key=lambda edge:edge.index):
				if first not in remaining:continue
				remaining.remove(first)
				group=[first];pending=[first]
				while pending:
					for vertex in pending.pop().verts:
						for edge in vertex.link_edges:
							if edge in remaining:remaining.remove(edge);pending.append(edge);group.append(edge)
				groups.append((group,min(widths[edge] for edge in group)))
				if len(groups)%256==0:yield f'Finding wood junctions {len(groups)}'
			serial=[len(bm.verts),len(bm.edges),len(bm.faces)]
			for index,(group,width) in enumerate(groups):
				if not all(edge.is_valid for edge in group):raise ValueError('Junction rounding invalidated another junction')
				round_junction(bm,group,width,serial)
				if len(bm.faces)>MAX_SURFACE_FACES:raise ValueError(f'Connected wood exceeds the {MAX_SURFACE_FACES:,}-face offline surface limit')
				if index%16==15:yield f'Rounding wood junctions {index+1} / {len(groups)}'
			if any(not edge.is_manifold for edge in bm.edges):raise ValueError('Junction rounding opened the surface')
			yield 'Checking continuous wood topology'
			if not bm.verts:raise ValueError('Solid wood union is empty')
			unseen=set(bm.verts);stack=[unseen.pop()]
			while stack:
				for edge in stack.pop().link_edges:
					for vertex in edge.verts:
						if vertex in unseen:unseen.remove(vertex);stack.append(vertex)
			if unseen:raise ValueError('Solid wood union is disconnected')
			bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
		except BaseException:
			bm.free();raise
		return bm,{'enclosed_voids':enclosed_voids,'enclosed_void_faces':enclosed_void_faces,'union_sweeps':len(keys),'operand_faces':operand_faces,'surface_face_limit':MAX_SURFACE_FACES,'union_seconds':union_seconds,'junction_cleanup_candidates':len(short_edges),'junction_cleanup_operations':cleanup_operations,'junction_cleanup_edges':cleanup_edges,'junction_cleanup_seconds':cleanup_seconds,'blended_edges':blended,'blend_groups':len(groups),'control_faces':len(bm.faces),'input_rings':input_rings,'surface_rings':output_rings,'ring_error_radius_fraction':maximum_error}
	finally:
		# Only the Boolean base can acquire a replacement mesh. All operand
		# meshes were tracked before their objects were created.
		if base is not None and base.data not in meshes:meshes.append(base.data)
		# Remove only this transaction's temporary IDs. One unlink pass avoids
		# rescanning the whole Blender database for each of thousands of branches.
		bpy.data.batch_remove(ids=[*objects,operands])
		orphans=[data for data in meshes if data.users==0]
		if orphans:bpy.data.batch_remove(ids=orphans)
