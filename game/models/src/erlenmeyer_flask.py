# Builds erlenmeyer_flask.blend and ../erlenmeyer_flask.glb (see docs/ART.md). Run from anywhere:
#   blender --background --factory-startup --python game/models/src/erlenmeyer_flask.py
# If someone has edited the .blend by hand, that's the source now: rerunning this overwrites it.
# Two meshes: "Glass" (a solid, closed glass shell) and "Liquid" (closed, filling the whole inside,
# inset from the inner wall; the liquid shader cuts it at the fill level).
import math
import os
import sys

import bpy

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from model_tools import lathe, material, rounded, save  # noqa: E402

HEIGHT = 0.21  # metres; matches the collision cylinder in props/erlenmeyer_flask.tscn
RADIUS = 0.08
NECK = 0.026  # outer radius of the neck
SHOULDER = 0.148  # where the cone meets the neck (before rounding)
WALL = 0.0025
BASE = 0.004  # the bottom is thicker than the walls
INSET = 0.0015  # liquid gap from the inner wall


def body(offset, top, bottom):
	"""Outer profile shrunk inwards by offset, from the axis at z = bottom up to z = top."""
	slope = math.atan2(RADIUS - NECK, SHOULDER)  # cone wall's lean from vertical
	cone_shift = offset / math.cos(slope)  # horizontal shift that moves the cone wall in by offset
	heel_r = RADIUS - cone_shift - bottom * math.tan(slope)
	shoulder_z = SHOULDER - (cone_shift - offset) / math.tan(slope)
	return rounded([
		(0.0, bottom, 0),
		(heel_r, bottom, 0.008 - offset),  # rounded heel (fillet centre is inside the glass)...
		(NECK - offset, shoulder_z, 0.022 + offset),  # ...shoulder into the neck (centre outside)
		(NECK - offset, top, 0),
	])


bpy.ops.wm.read_factory_settings(use_empty=True)

lip = HEIGHT - 0.006
outer = body(0.0, lip, 0.0)
inner = body(WALL, lip, BASE)
rim = [  # rolled lip: bulges out, over the top, and back down inside
	(NECK + 0.003, lip + 0.0015), (NECK + 0.0035, lip + 0.0035), (NECK + 0.0025, HEIGHT - 0.0008),
	(NECK - WALL * 0.5, HEIGHT), (NECK - WALL, HEIGHT - 0.0015),
]
lathe("Glass", outer + rim + inner[::-1][1:], material("Glass", (0.8, 0.95, 0.92), 0.28))

liquid = body(WALL + INSET, HEIGHT - 0.003, BASE + INSET)
liquid += [(0.0, HEIGHT - 0.003)]
lathe("Liquid", liquid, material("Liquid", (0.3, 1.0, 0.25)))

save("erlenmeyer_flask")
