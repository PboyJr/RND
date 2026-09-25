# RND: Art pipeline

How models get from Blender (or anywhere) into the game, and who does what. The visual direction
lives in [DESIGN.md](DESIGN.md#visual-style-leaning).

Last updated: 2026-09-25

---

## Who does what

- **Modelling (art):** shapes, UVs, clean normals, simple textures or flat colour areas, and
  separate named meshes for anything that behaves differently (glass, liquid, moving parts).
- **Code:** lighting style (e.g. cel shading), outlines, liquids (fill level, sloshing, glow),
  glass, damage, anything that moves or reacts to gameplay, and collision shapes.

If it needs to *change during play*, it's code. Model the thing it happens to.

## Handing a model over

- **Format:** glTF 2.0 binary (`.glb`). Godot imports it directly; drop it in `game/models/`.
- **Scale:** 1 unit = 1 metre, real-world size, transforms applied. (An Erlenmeyer flask is
  roughly 15–20 cm tall.)
- **Origin:** bottom centre for things that sit on the floor or on tables.
- **Names:** name every mesh and material clearly (`Glass`, `Liquid`, `Cap`, `Label`...). Code
  finds parts by name.
- **Normals:** decide which edges are sharp and which are smooth, deliberately. This matters a
  lot if we go cel-shaded, because shading breaks show up harshly.
- **Textures:** flat colour areas or simple textures, and **no painted-in lighting, shadows or
  ambient occlusion**. The engine does the shading.
- **Budget (mid-poly):** small props around 500–3,000 triangles; characters around 5–15k.
- **Collision:** we build simple collision shapes in Godot. You don't need to model them.

## Liquids (flasks, specimen jars)

- Model the **vessel**, plus a separate, closed **`Liquid` mesh that fills the whole inside**,
  inset about 1–2 mm from the inner wall so it doesn't flicker against the glass.
- **Don't** model it half-full or model the liquid's top surface. A liquid shader cuts the mesh at
  whatever fill level the game wants, draws the surface, and makes it slosh when the flask is
  carried or thrown.
- Keep the glass a separate mesh (`Glass`) with its own material, so it can be transparent while
  the liquid stays readable.

## Cel shading and your materials

The cel-shaded look (prototype, **F2 toggles it in-game**) is applied by code on top of ordinary
materials, so **author normal materials** (Godot's StandardMaterial3D, or Blender materials that
import as it). The game swaps each opaque one for the toon shader at runtime and keeps its colour
and glow in sync. Transparent materials (glass) and unshaded ones are left as they are. What
matters from the model: clean normals, and flat colour areas without painted-in shading.

The outlines are thin (1 px) by default. To change their width, colour or how many edges get
lines: select `Main/ToonStyle` in `core/main.tscn` → **Outline Material** → Shader Parameters
(`Thickness`, `Line Color`, `Depth Threshold`, `Normal Threshold`, `Fade Distance`).

## The models folder

`game/models/` holds what the game uses, one set of files per model with the same name. The
sources live in `game/models/src/`, which Godot ignores (it has a `.gdignore`), so nobody needs
Blender set up in Godot.

| File | What it is |
|---|---|
| `name.tscn` | The game-ready scene: the `.glb` plus materials and scripts. Game scenes instance this |
| `name.glb` | The export Godot imports. Never edit it, re-export it |
| `glass.tres` | The shared glass material (transparent, rim light) for every vessel |
| `src/name.blend` | The Blender file. Open it to tweak by hand |
| `src/name.py` | Blender Python that builds the model (if it was built by script) |

Shared helpers for the build scripts (rounded profiles, lathing, export) are in `src/model_tools.py`.
Rebuild a scripted model (writes `src/name.blend` and `name.glb`):
`blender --background --factory-startup --python game/models/src/name.py`. Once someone edits the
`.blend` by hand, the `.blend` becomes the source: export from it to `game/models/name.glb`, and
don't rerun the script.

## The Erlenmeyer flask

The flask is the Scientist's **acid flask**: it sits on tables as a prop, it's in your hand (its
acid refills as it recharges), and it's what you throw. All three use one model scene,
`game/models/erlenmeyer_flask.tscn`. The real model is in (21 cm tall, 15 cm wide, glass with a
rolled lip, plus a `Liquid` mesh that fills the inside up into the neck), built by
`src/erlenmeyer_flask.py`. To replace it with a different model, follow these steps and all three
update:

1. Export the flask as `.glb` (see above) with two meshes, `Glass` and `Liquid`, origin at the
   bottom centre, and put it in `game/models/`.
2. In Godot, double-click the `.glb` → **New Inherited Scene**.
3. Select the **`Liquid`** mesh:
   - attach the script `vfx/Liquid.cs`
   - set its material to a new **ShaderMaterial** using `vfx/liquid.gdshader`
   
   Fill, colour, glow and slosh feel are in the Inspector. (Fill is how full the flask is when
   the acid is charged; in your hand it refills from empty up to that.)
4. Give **`Glass`** the shared glass material, `models/glass.tres`.
5. **Save** it over `models/erlenmeyer_flask.tscn`.
6. If it's a different size from the placeholder (21 cm tall, 16 cm wide): resize the collision
   cylinder in `props/erlenmeyer_flask.tscn`, and move `Model` down by half the flask's height
   there and in `items/acid_flask_projectile.tscn` (the `Flask` node), so they spin around its
   middle. The in-hand position is `HandItem` in `players/player.tscn`.

Everything else (carrying, throwing, networking, the liquid staying level, sloshing and
refilling) already works.

## Coming up if the rat lore sticks (idea)

In the lore idea (see [DESIGN.md](DESIGN.md#lore-idea-2026-09-25)), the researchers are really
rats with uploaded human brains. The **psychosis** effect makes players see themselves and each
other as rats. That would need:

- **A rat version of the researcher** that can swap in for the human model on the same
  character: same rough size and origin, so it can reuse the collision, carrying and hands.
- **Rat hands / paws for first person** (the hotbar item and carried props are held in front of
  the camera), if psychosis also changes what you see of yourself.
- Whether the rat still wears the gas mask is an open question: it's the whole HUD.

## Open questions

- [ ] Cel shading: keep it? (Prototype is in; compare with F2.)
- [ ] Rats: does the rat keep the gas mask and lab gear, and how "cartoon" vs "gross" should it be?
