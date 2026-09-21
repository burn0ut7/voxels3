"""Continuous branch junctions derived from a growth graph inside Blender."""
import math
import bmesh
from mathutils import Vector


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
		reach=min(min(lengths)*.42,node['radius']*3.2)
		ports=[]
		for slot,e in enumerate(links):
			direction=directions[slot];u,v,sides=basis[e];radius=edge_radii[e][0 if edges[e][0]==index else 1]
			cosines=[max(-1,min(1,direction.dot(other))) for j,other in enumerate(directions) if j!=slot]
			clearance=min((math.sqrt(max(1e-10,(1-c)/(1+c+1e-10))) for c in cosines),default=1e6)
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
