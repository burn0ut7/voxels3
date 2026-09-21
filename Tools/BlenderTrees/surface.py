"""Continuous branch junctions derived from a growth graph inside Blender."""
import math
import bmesh
from mathutils import Vector


def round_surface(mesh):
	"""One finite Catmull-Clark step on our closed, uncreased control mesh.

	Positions and topology use linear-size arrays. No limit-surface patch tables,
	UV interpolation, creases or open-boundary rules are needed at this stage.
	Bark projection and part attributes are created on the resulting surface.
	"""
	import bpy
	import numpy as np
	vertices,edges,faces,corners=len(mesh.vertices),len(mesh.edges),len(mesh.polygons),len(mesh.loops)
	positions=np.empty((vertices,3),np.float64);mesh.vertices.foreach_get('co',positions.ravel())
	ends=np.empty((edges,2),np.int32);mesh.edges.foreach_get('vertices',ends.ravel())
	indices=np.empty(corners,np.int32);mesh.loops.foreach_get('vertex_index',indices)
	edge_ids=np.empty(corners,np.int32);mesh.loops.foreach_get('edge_index',edge_ids)
	starts=np.empty(faces,np.int32);mesh.polygons.foreach_get('loop_start',starts)
	counts=np.empty(faces,np.int32);mesh.polygons.foreach_get('loop_total',counts)
	if not corners or np.any(np.bincount(edge_ids,minlength=edges)!=2):
		raise ValueError('Rounding requires a closed branch control surface')
	face_ids=np.repeat(np.arange(faces,dtype=np.int32),counts)
	centers=np.add.reduceat(positions[indices],starts)/counts[:,None]
	edge_points=positions[ends].sum(axis=1)
	np.add.at(edge_points,edge_ids,centers[face_ids]);edge_points*=.25
	# Interior vertex rule: (F + 2R + (n-3)P) / n, expressed with
	# incident face centers and neighboring original vertices.
	valence=np.bincount(ends.ravel(),minlength=vertices)[:,None]
	if np.any(valence<2):raise ValueError('Rounding found an isolated branch vertex')
	sums=np.zeros_like(positions)
	np.add.at(sums,indices,centers[face_ids])
	np.add.at(sums,ends[:,0],positions[ends[:,1]])
	np.add.at(sums,ends[:,1],positions[ends[:,0]])
	positions=positions*(valence-2)/valence+sums/(valence*valence)
	previous=np.arange(corners,dtype=np.int32)-1;previous[starts]=starts+counts-1
	quads=np.column_stack((indices,vertices+edge_ids,vertices+edges+face_ids,vertices+edge_ids[previous])).astype(np.int32)
	points=np.concatenate((positions,edge_points,centers)).astype(np.float32)
	result=bpy.data.meshes.new(mesh.name+'_Rounded')
	try:
		result.vertices.add(len(points));result.vertices.foreach_set('co',points.ravel())
		result.loops.add(corners*4);result.loops.foreach_set('vertex_index',quads.ravel())
		result.polygons.add(corners);result.polygons.foreach_set('loop_start',np.arange(corners,dtype=np.int32)*4)
		result.polygons.foreach_set('loop_total',np.full(corners,4,np.int32))
		result.update(calc_edges=True)
		for material in mesh.materials:result.materials.append(material)
	except BaseException:
		bpy.data.meshes.remove(result);raise
	return result


def branch_surface(graph):
	"""Bridge convex junction collars through shared, uncapped tube rings.

	Ports lie on supporting planes of each collar. Angular clearance prevents
	neighboring branch ports from swallowing one another. Graph points and radii
	remain authoritative; clearance only bounds local derived collar geometry.
	"""
	nodes=graph['nodes'];children=[[] for _ in nodes];adjacent=[[] for _ in nodes]
	for i,node in enumerate(nodes):
		if node['parent']>=0:children[node['parent']].append(i)
	positions=[];edges=[];middle=[];basis=[];edge_radii=[];end_ports={};faces=[]
	def ring(center,u,v,radius,sides):
		indices=[]
		for j in range(sides):
			angle=math.tau*j/sides;indices.append(len(positions));positions.append(center+(u*math.cos(angle)+v*math.sin(angle))*radius)
		return indices
	for b,node in enumerate(nodes):
		a=node['parent']
		if a<0:continue
		p=Vector(nodes[a]['p']);q=Vector(node['p']);direction=(q-p).normalized()
		u=direction.cross(Vector((0,0,1)) if abs(direction.z)<.9 else Vector((0,1,0))).normalized();v=direction.cross(u)
		start=nodes[a]['radius'] if node['axis']==nodes[a]['axis'] or len(children[a])==1 else node['radius']
		radius=(start+node['radius'])*.5*1.12;sides=8 if radius>.025 else 6 if radius>.009 else 4
		e=len(edges);edges.append((a,b));basis.append((u,v,sides));edge_radii.append((start*1.12,node['radius']*1.12))
		middle.append(ring((p+q)*.5,u,v,radius,sides));adjacent[a].append(e);adjacent[b].append(e)
	clipped=0;expanded_edges=set()
	for index,node in enumerate(nodes):
		center=Vector(node['p']);links=adjacent[index];directions=[];lengths=[]
		for e in links:
			a,b=edges[e];other=b if a==index else a;delta=Vector(nodes[other]['p'])-center
			directions.append(delta.normalized());lengths.append(delta.length)
		clearances=[];radii=[]
		for slot,e in enumerate(links):
			cosines=[max(-1,min(1,directions[slot].dot(other))) for j,other in enumerate(directions) if j!=slot]
			clearances.append(min((math.sqrt(max(1e-10,(1-c)/(1+c+1e-10))) for c in cosines),default=1e6))
			radii.append(edge_radii[e][0 if edges[e][0]==index else 1])
		# Acute forks need a longer transition than a right-angle junction.
		# Use available branch length before reducing the port diameter.
		required=max((radius/(clearance*.94) for radius,clearance in zip(radii,clearances)),default=0)
		reach=min(min(lengths)*.42,max(node['radius']*3.2,required))
		ports=[]
		for slot,e in enumerate(links):
			direction=directions[slot];u,v,sides=basis[e];radius=radii[slot];clearance=clearances[slot]
			limited=min(radius,reach*clearance*.94)
			if limited<radius*.99:clipped+=1;expanded_edges.add(e)
			port=ring(center+direction*reach,u,v,limited,sides);ports.append(port);end_ports[index,e]=port
		body=[];support=min(node['radius']*.8,reach*.75)
		for axis in range(3):
			for sign in (-1,1):
				p=center.copy();p[axis]+=support*sign;body.append(len(positions));positions.append(p)
		indices=body+[v for port in ports for v in port]
		bm=bmesh.new();identity=bm.verts.layers.int.new('source_index')
		for i in indices:vertex=bm.verts.new(positions[i]);vertex[identity]=i
		bmesh.ops.convex_hull(bm,input=list(bm.verts),use_existing_faces=False)
		port_sets=[set(port) for port in ports];cap_edges=[{} for _ in ports]
		for face in bm.faces:
			indices=tuple(vertex[identity] for vertex in face.verts);face_set=set(indices)
			cap=next((i for i,p in enumerate(port_sets) if face_set<=p),None)
			if cap is None:faces.append(indices)
			else:
				for a,b in zip(indices,indices[1:]+indices[:1]):
					key=tuple(sorted((a,b)));cap_edges[cap][key]=cap_edges[cap].get(key,0)+1
		bm.free()
		for port,counts in zip(ports,cap_edges):
			expected={tuple(sorted((a,b))) for a,b in zip(port,port[1:]+port[:1])}
			if {edge for edge,count in counts.items() if count==1}!=expected:raise ValueError(f'Cannot resolve branch collar at node {index} (radius {node["radius"]:.5f}m, reach {reach:.5f}m, edge lengths {[round(v,5) for v in lengths]})')
		if index%64==63:yield f'Blending branch collars {index+1} / {len(nodes)}'
	for e,(a,b) in enumerate(edges):
		# A straight tapered span needs only its two end rings. Keep the
		# middle support where a crowded collar narrows locally, so that
		# clearance does not thin the entire branch between junctions.
		spans=((end_ports[a,e],middle[e]),(middle[e],end_ports[b,e])) if e in expanded_edges else ((end_ports[a,e],end_ports[b,e]),)
		for left,right in spans:
			for j in range(len(left)):
				k=(j+1)%len(left);faces.append((left[j],left[k],right[k],right[j]))
	yield 'Joining shared branch panels'
	bm=bmesh.new();vertices={}
	for face in faces:
		for i in face:
			if i not in vertices:vertices[i]=bm.verts.new(positions[i])
		bm.faces.new([vertices[i] for i in face])
	try:
		bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
		# Dissolving coplanar panels changes subdivision support, so the
		# rounded junctions require visual review as well as topology checks.
		yield 'Simplifying coplanar branch panels'
		bm.normal_update()
		bmesh.ops.dissolve_limit(bm,angle_limit=.0001,verts=list(bm.verts),edges=list(bm.edges),use_dissolve_boundaries=False)
		yield 'Checking connected branch surface'
		if any(not edge.is_manifold for edge in bm.edges):raise ValueError('Branch collar surface is not closed')
		unseen=set(bm.verts);stack=[unseen.pop()]
		while stack:
			for edge in stack.pop().link_edges:
				for vertex in edge.verts:
					if vertex in unseen:unseen.remove(vertex);stack.append(vertex)
		if unseen:raise ValueError('Branch collar surface is disconnected')
	except BaseException:
		bm.free();raise
	return bm,{'clearance_limited_ports':clipped,'collars':len(nodes),'control_faces':len(bm.faces),'supported_spans':len(expanded_edges)}
