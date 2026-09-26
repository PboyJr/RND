# RND: notes for Claude

Co-op research horror game (REPO-like) in **Godot 4.7 .NET / C#**. The Godot project is `game/`.

## Guiding principle

**Make everything as intelligent and dynamic as possible** (see DESIGN.md). Prefer reactive
behaviour over scripted shortcuts, e.g. enemies always pathfind and never beeline.

## Read first

- `docs/DESIGN.md`: game ideas and their status (Decided / Leaning / Idea / Open)
- `docs/DECISIONS.md`: tech choices and why, plus known limitations
- `docs/ROADMAP.md`: what's done and what's next

## Keep the docs current

The team wants everything written down **in the repo's markdown files** (not in Claude's memory).
Whenever a conversation adds or changes a design idea, a technical decision, or milestone status,
update the matching doc in the same turn:

- New or changed game idea / open question → `docs/DESIGN.md`
- New or changed tech choice → append a dated entry to `docs/DECISIONS.md`
- Milestone progress → `docs/ROADMAP.md` (and bump "Last updated")

The markdown docs are Claude's working notes. **`docs/overview.html` is the team's plain-English
handbook** (no class names, file paths or code terms), published as an Artifact the team shares.
When a change would alter something it says (a status tag, what's playable, controls, open
questions, next steps), update it too, bump its date, and republish it. Its **Working on now**
section says what's in progress: update it whenever the current task starts, changes or finishes.

## Working in the code

- Build: `dotnet build game/RND.sln`
- Smoke tests (headless, from `game/`): see README. Run `--role=scenes`, and for networking
  changes `--role=network` (it starts its own clients; try `--clients=3 --ping=150 --jitter=20
  --loss=2`, and `--clients=4 --ping=250 --jitter=40 --loss=5` for a stress run; client logs go
  to `--out`). Use Godot's `_console.exe` on Windows to see output.
  Always wrap runs in a timeout: if the test script fails to parse, Godot never quits.
- Conventions: feature folders, one namespace per folder (`RND.Core`, `RND.Players`, ...), tabs,
  file-scoped namespaces, scripts next to their scenes.
- Networking rules: transport only in `core/Network.cs`; player movement is client-authoritative;
  props, health, enemies and projectiles are host-authoritative (clients send requests via RPC).
  See DECISIONS before changing.
- Anything audible calls `Level.EmitSound(position, radius, SoundKind)` on the host: players hear
  it (World bus, muffled by the mask) and enemies hear it (`Hear`) at the same radius. Silent
  AI-only noise uses `Level.EmitNoise`. New sounds are recipes in `audio/SoundBank.cs`.
- Claude can't listen: after changing any sound, run the smoke test's `audio` role (clipping and
  silence checks) and hand the rendered .wav files to the user to judge by ear. For the in-game
  balance, run the `capture` role and read its bus meter log. Don't trust `AudioEffectRecord` on
  the dev machine: it's 7.1 surround, and the recorder misses non-positional sound.
- Atmosphere-only sounds (ambience) go through `audio/Ambience.cs`, not `EmitSound`.
- Player settings: a property on `core/Settings.cs` (load / save / `Apply`) plus a line in the
  table in `ui/SettingsMenu.cs`. CI (`.github/workflows/build.yml`) runs both smoke tests and
  exports the Windows build on every push; keep `export_presets.cfg`'s preset named "Windows".
- Generated chambers (`maze/ChamberGenerator.cs`): after changing the generator (or a piece it uses),
  run the smoke test's `generator` role (`--seeds=30`): every chamber must still solve from its plan.
- Materials: author ordinary `StandardMaterial3D`s. `vfx/ToonStyle.cs` turns opaque ones into the
  cel-shaded look at runtime (F2 toggles it; the F2–F7 debug switches (look, plus F7 sound) are one table in `ui/Hud.cs`),
  and animating the original's albedo or emission still works. Liquids: a closed `Liquid` mesh + `vfx/Liquid.cs` + `vfx/liquid.gdshader`. A model used
  in several places (prop, in hand, thrown) is one scene in `models/`, instanced by each.
- Enemies only act on what they perceive (sight cone, hearing, memory). Don't give AI direct
  access to player positions it couldn't know.
- Anything damageable gets a `Health` child (`combat/Health.cs`).
- HUD: things that belong "in the mask" go in `ui/visor_hud.tscn` (projected through the visor
  shader, phosphor green, and they must avoid the nose cup at the bottom centre). Menus and
  debug go in `ui/hud.tscn` (crisp). Health is shown by visor cracks, not a number.
- Shader and visual changes can't be verified headless: run the smoke test's `capture` role
  (windowed, opens a window for a few seconds) and look at the screenshots. It also checks that
  cel shading doesn't change the room's brightness. New hotbar items are `.tres`
  files (`items/`), added to a player's `Loadout`. New spawnable scenes must be listed in the
  level's matching MultiplayerSpawner.
