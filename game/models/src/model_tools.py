# Shared helpers for the Blender build scripts in this folder (see docs/ART.md). Blender units are
# metres, Z up; the glTF export turns that into Godot's Y up.
import math
import os

import bmesh
import bpy

HERE = os.path.dirname(os.path.abspath(__file__))  # models/src: sources; exports go up to models/


def rounded(corners, samples=5):
	"""Polyline through (r, z) corners, each (r, z, fillet radius), with circular fillets."""
	out = [corners[0][:2]]
	for (ax, az, _), (bx, bz, rad), (cx, cz, _) in zip(corners, corners[1:], corners[2:]):
		if rad <= 0:
			out.append((bx, bz))
			continue
		u = _unit(ax - bx, az - bz)
		v = _unit(cx - bx, cz - bz)
		half = math.acos(max(-1.0, min(1.0, u[0] * v[0] + u[1] * v[1]))) / 2
		d = rad / math.tan(half)  # corner to tangent point
		bis = _unit(u[0] + v[0], u[1] + v[1])
		centre = (bx + bis[0] * rad / math.sin(half), bz + bis[1] * rad / math.sin(half))
		p0, p1 = (bx + u[0] * d, bz + u[1] * d), (bx + v[0] * d, bz + v[1] * d)
		a0 = math.atan2(p0[1] - centre[1], p0[0] - centre[0])
		a1 = math.atan2(p1[1] - centre[1], p1[0] - centre[0])
		sweep = (a1 - a0 + math.pi) % (2 * math.pi) - math.pi
		for i in range(samples + 1):
			a = a0 + sweep * i / samples
			out.append((centre[0] + rad * math.cos(a), centre[1] + rad * math.sin(a)))
	out.append(corners[-1][:2])
	return out


def _unit(x, z):
	n = math.hypot(x, z)
	return (x / n, z / n)


def lathe(name, profile, material, segments=32, shape=None):
	"""Spins an (r, z) profile that starts and ends on the axis into a closed mesh. shape(bm) can
	bend the result before normals are worked out."""
	bm = bmesh.new()
	verts = [bm.verts.new((r, 0.0, z)) for r, z in profile]
	edges = [bm.edges.new(pair) for pair in zip(verts, verts[1:])]
	bmesh.ops.spin(bm, geom=verts + edges, axis=(0, 0, 1), cent=(0, 0, 0), angle=2 * math.pi,
		steps=segments, use_merge=True)
	bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)  # closes the poles on the axis
	bmesh.ops.dissolve_degenerate(bm, edges=bm.edges, dist=1e-6)
	if shape:
		shape(bm)
	bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
	return mesh_object(name, bm, material)


def mesh_object(name, bm, material):
	"""Turns a bmesh into a named object: smooth, but sharp where faces meet at more than
	35 degrees (clean normals matter for the cel shading)."""
	for f in bm.faces:
		f.smooth = True
	for e in bm.edges:
		if len(e.link_faces) == 2 and e.calc_face_angle() > math.radians(35):
			e.smooth = False
	mesh = bpy.data.meshes.new(name)
	bm.to_mesh(mesh)
	bm.free()
	mesh.materials.append(material)
	obj = bpy.data.objects.new(name, mesh)
	bpy.context.scene.collection.objects.link(obj)
	return obj


def material(name, colour, alpha=1.0, roughness=0.05):
	mat = bpy.data.materials.new(name)
	bsdf = mat.node_tree.nodes["Principled BSDF"]
	bsdf.inputs["Base Color"].default_value = (*colour, 1)
	bsdf.inputs["Roughness"].default_value = roughness
	bsdf.inputs["Alpha"].default_value = alpha
	return mat


def save(name):
	"""Prints each mesh's size, then writes src/<name>.blend and <name>.glb."""
	for o in bpy.data.objects:
		tris = sum(len(p.vertices) - 2 for p in o.data.polygons)
		print(o.name, "tris:", tris, "dims:", tuple(round(d, 4) for d in o.dimensions))
	bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, name + ".blend"))
	bpy.ops.export_scene.gltf(filepath=os.path.join(HERE, "..", name + ".glb"), export_format="GLB",
		export_apply=True, export_yup=True)
