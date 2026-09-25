# Builds graduated_cylinder.blend and ../graduated_cylinder.glb (see docs/ART.md). Run from anywhere:
#   blender --background --factory-startup --python game/models/src/graduated_cylinder.py
# If someone has edited the .blend by hand, that's the source now: rerunning this overwrites it.
# A 100 ml cylinder. Meshes: "Glass" (tube with a pour spout), "Base" (hexagonal plastic foot),
# "Marks" (the scale: a line every 2 ml, a long numbered one every 10 ml, true to the tube's inside)
# and "Liquid" (closed, filling the inside below the spout; the liquid shader cuts it).
import math
import os
import sys

import bmesh
import bpy

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from model_tools import lathe, material, mesh_object, rounded, save  # noqa: E402

HEIGHT = 0.25  # metres; matches the collision shapes in props/graduated_cylinder.tscn
RADIUS = 0.014  # tube, outside
WALL = 0.0015
FOOT_RADIUS = 0.036  # to the hexagon's corners
FOOT_TOP = 0.011
FLOOR = 0.012  # inside bottom of the tube: the scale's zero
INSET = 0.0015  # liquid gap from the inner wall
SPOUT = 0.006  # how far the spout sticks out
SPOUT_DROP = 0.014  # how far down the tube the spout starts bending
FRONT = -math.pi / 2  # the scale faces -Y in Blender, which is +Z (towards the camera) in Godot

INNER = RADIUS - WALL
ML = 1e-6 / (math.pi * INNER ** 2)  # height of one millilitre in the tube


def spout(bm):
	"""Bends the top of the tube out on the +X side into a pouring lip."""
	for v in bm.verts:
		t = (v.co.z - (HEIGHT - SPOUT_DROP)) / SPOUT_DROP
		if t <= 0:
			continue
		a = math.atan2(v.co.y, v.co.x)
		push = SPOUT * t * t * max(0.0, math.cos(a)) ** 4
		v.co.x += math.cos(a) * push
		v.co.y += math.sin(a) * push


def band(bm, z, a0, a1, height, r):
	"""A thin strip hugging the tube from angle a0 to a1, facing out."""
	steps = max(2, round((a1 - a0) / math.radians(8)))
	rows = []
	for i in range(steps + 1):
		a = a0 + (a1 - a0) * i / steps
		rows.append([bm.verts.new((r * math.cos(a), r * math.sin(a), z + dz)) for dz in (-height / 2, height / 2)])
	for (b0, t0), (b1, t1) in zip(rows, rows[1:]):
		bm.faces.new((b0, b1, t1, t0))


def label(bm, text, z, a0, size, r):
	"""Flat text wrapped round the tube, starting at angle a0, centred on height z."""
	curve = bpy.data.curves.new("label", "FONT")
	curve.body = text
	curve.size = size
	curve.align_y = "CENTER"
	curve.resolution_u = 2
	obj = bpy.data.objects.new("label", curve)
	bpy.context.scene.collection.objects.link(obj)
	mesh = bpy.data.meshes.new_from_object(obj.evaluated_get(bpy.context.evaluated_depsgraph_get()))
	part = bmesh.new()
	part.from_mesh(mesh)
	bmesh.ops.triangulate(part, faces=part.faces)
	lookup = {}
	for v in part.verts:  # text's x runs round the tube, its y runs up it
		a = a0 + v.co.x / r
		lookup[v] = bm.verts.new((r * math.cos(a), r * math.sin(a), z + v.co.y))
	for f in part.faces:
		bm.faces.new([lookup[v] for v in f.verts])
	part.free()
	bpy.data.objects.remove(obj)
	bpy.data.curves.remove(curve)
	bpy.data.meshes.remove(mesh)


bpy.ops.wm.read_factory_settings(use_empty=True)

glass = material("Glass", (0.8, 0.95, 0.92), 0.28)
lip = HEIGHT - 0.003
outer = rounded([(0.0, FLOOR - 0.003, 0), (RADIUS, FLOOR - 0.003, 0.002), (RADIUS, lip, 0)])
rim = [(RADIUS + 0.0012, lip + 0.0008), (RADIUS + 0.0008, HEIGHT), (INNER, HEIGHT - 0.0008)]
inner = rounded([(0.0, FLOOR, 0), (INNER, FLOOR, 0.002), (INNER, HEIGHT - 0.0008, 0)])
lathe("Glass", outer + rim + inner[::-1][1:], glass, shape=spout)

foot = rounded([
	(0.0, 0.0, 0), (FOOT_RADIUS, 0.0, 0.0015), (FOOT_RADIUS, 0.006, 0.002),
	(RADIUS + 0.004, 0.009, 0.002), (RADIUS, FOOT_TOP, 0), (0.0, FOOT_TOP, 0),  # top hides in the glass
], samples=2)
lathe("Base", foot, material("Base", (0.12, 0.3, 0.75), roughness=0.5), segments=6)

marks = bmesh.new()
r = RADIUS + 0.0002  # just off the glass so it doesn't flicker
start = FRONT - math.radians(28)
for ml in range(2, 101, 2):
	z = FLOOR + ml * ML
	if ml % 10:
		band(marks, z, start, start + math.radians(22), 0.0005, r)
	else:
		band(marks, z, start, start + math.radians(44), 0.0008, r)
		label(marks, str(ml), z, start + math.radians(50), 0.0045, r)
label(marks, "ml", FLOOR + 106 * ML, start, 0.0045, r)
mesh_object("Marks", marks, material("Marks", (0.95, 0.95, 0.9), roughness=0.6))

liquid_top = HEIGHT - SPOUT_DROP - 0.002  # below the spout, so it can't poke through the lip
liquid = rounded([
	(0.0, FLOOR + INSET, 0), (INNER - INSET, FLOOR + INSET, 0.0015),
	(INNER - INSET, liquid_top, 0), (0.0, liquid_top, 0),
])
lathe("Liquid", liquid, material("Liquid", (0.85, 1.0, 0.05)), segments=24)

save("graduated_cylinder")
