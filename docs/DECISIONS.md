# RND: Technical Decisions

A log of what we chose and **why**, so we don't re-argue it later. Add new entries at the bottom
with a date. If a decision changes, mark the old entry *Superseded* and link the new one.

---

## 2026-09-24: Engine: Godot 4 (.NET / C#)

**Decision:** Godot 4, .NET edition, with C# for gameplay code.

**Why:**
- The team knows C#, not Unity.
- Free and open source, no royalties, small and fast to iterate in.
- A mid-poly / grainy art style doesn't need a high-end renderer.

**Trade-offs we accepted:**
- R.E.P.O., Lethal Company and Content Warning were all made in Unity, mostly because Unity has
  ready-made networking and voice plugins. In Godot, **Steam integration, relay / NAT traversal and
  proximity voice are more do-it-yourself.**
- Godot C# can't export to the web, and its mobile export is still marked experimental (matters for
  the phone-monster idea).

## 2026-09-24: C# everywhere (GDScript only for test tooling)

**Decision:** gameplay code is C#. The only GDScript is the headless smoke test harness.

**Why:** calling between GDScript and C# is clunky. Tutorials are mostly GDScript, but the API
translates directly (`get_node` → `GetNode`, snake_case → PascalCase).

## 2026-09-24: Godot version 4.7

**Decision:** Godot 4.7 stable (.NET).

**Notes:**
- The `Godot.NET.Sdk/x.y.z` version in `game/RND.csproj` must match the editor. Whichever editor
  opens the project rewrites it and leaves a `RND.csproj.old` backup, which is gitignored.
- Everyone on the team should use the **same Godot version**.

## 2026-09-24: Physics: Jolt

**Decision:** `physics/3d/physics_engine = "Jolt Physics"`.

**Why:** more stable stacking and carrying than Godot Physics, which matters for a REPO-style game
about carrying physics objects.

## 2026-09-24: Networking: listen server, transport isolated

**Decision:**
- **Listen server:** the host plays and is the server. No dedicated servers for now.
- **ENet** for now (localhost / LAN testing).
- **All transport code lives in `game/core/Network.cs`.** Everything else uses Godot's high-level
  multiplayer API (RPCs, MultiplayerSpawner, MultiplayerSynchronizer), so switching to Steam or our
  own relay shouldn't touch gameplay code.

## 2026-09-24: Authority model

**Decision:**
- **Players are client-authoritative.** Each client moves its own player, so movement feels
  instant, and the synchronizer replicates it. Remote players smooth toward the replicated state and
  snap if more than 3 m off. (Updated 2026-09-25: the client now reports its state to the host,
  which replicates it; see "Player state goes through the host".)
  - The player node is **named after its owner's peer id**. Authority is set from the name in
    `_EnterTree`.
- **Props are host-authoritative.** Only the host simulates physics. Clients hold **frozen
  (kinematic) copies** that follow the host's replicated transform (about 30 Hz, smoothed).
  - Grab / release / throw are **RPC requests to the host** (`RequestGrab`, `RequestRelease`,
    `RequestThrow`). The host validates them and sets `HeldBy`, which replicates back.
  - The holder and the held prop ignore collisions with each other, so you can't stand on what
    you're carrying.

**Why:** responsive movement for everyone. Props never desync because only the host decides where
they are.

**Trade-offs:**
- Client authority is cheatable. That's fine for co-op, but **revisit it if player-controlled
  monsters make the game PvP.**
- A prop held by a client lags by one round trip, since the host simulates it. That feels OK for
  REPO-style floaty carrying.

## 2026-09-24: Level replication through a MultiplayerSpawner

**Decision:** only the host loads levels. `Main/LevelSpawner` copies them to clients.

**Why:** clients get levels automatically, including late joiners. Also, synchronizers on nodes
placed in the scene (not spawned) only start once the client confirms the node path. If a client
loads the level on its own, it can miss that confirmation, and syncing silently never starts. Having
the level spawned by the host avoids that race.

## 2026-09-24: Steam, voice and backend are planned, not decided

- **Steam lobbies + relay:** `Open`. Options are **Facepunch.Steamworks** (C#-native NuGet package,
  needs a small adapter to Godot's multiplayer) or **GodotSteam** (engine extension + community C#
  bindings).
- **Proximity voice:** `Open`. Likely Steam Voice (via whichever Steam option we pick) or an Opus
  addon over our own connection.
- **Our own backend:** probably needed eventually, for (a) persistent characters that can't be
  edited locally (the decay system) and (b) relaying phone players, who can't use Steam.
- (2026-09-25: Steam now has a plan, see "Steam plan" below.)

## 2026-09-24: Look: grain post-process layer (*Superseded* by "Gas mask visor" below)

**Decision:** a full-screen `ColorRect` with a screen-reading canvas shader on its own CanvasLayer
(`Main/PostFX`, layer 1), drawn between the 3D view and the UI (`Main/UI`, layer 2).

**Why:** cheap, works in every renderer, and keeps UI text crisp.

## 2026-09-24: Project layout and code style

- **Feature folders** under `game/`: `core/`, `combat/`, `enemies/`, `items/`, `levels/`,
  `players/`, `props/`, `ui/`, `vfx/`, `tests/`. Each script sits next to its scene.
- **One namespace per folder:** `RND.Core`, `RND.Combat`, `RND.Enemies`, `RND.Items`,
  `RND.Levels`, `RND.Players`, `RND.Props`, `RND.UI`, `RND.Vfx`.
- Tabs for indentation (Godot's default), file-scoped namespaces.

## 2026-09-24: Testing: headless smoke tests

**Decision:** `game/tests/smoke_test.tscn` runs headless in three roles: `scenes` (every scene
loads and the level plays offline), `host` and `client` (real ENet session: join, grab, throw,
leave). Commands are in the README. (2026-09-25: `network` now runs the host and its clients in
one command, over a fake bad connection; see "The network test plays a whole maze run".)

**Why:** we can't click through the game on every change, and networking bugs hide until a second
player shows up.

## 2026-09-24: Dev tooling: Visual Studio launch profile

**Decision:** `game/Properties/launchSettings.json` has **Play** and **Godot Editor** profiles
that launch Godot with the project, with the debugger attached. It's **gitignored** because it
contains local paths; each dev makes their own (template in the README).

## 2026-09-24: Documentation lives in the repo

**Decision:** design, decisions and roadmap live in `docs/*.md`, not in anyone's head or chat
history. Update them in the same change that makes them outdated.

## 2026-09-24: Health is host-owned, on a shared component

**Decision:** anything that can be hurt gets a child node named `Health` (`combat/Health.cs`)
with its own MultiplayerSynchronizer. **Only the host changes it** (`TakeDamage`, `Revive`). The
value is replicated, and every peer gets `Damaged` / `Died` / `Revived` signals for visuals.

- On players, the Health node's authority is set back to the host (1) in `_EnterTree`, even
  though the rest of the player belongs to the owning client.

**Why:** if a client owned its own health, it could simply ignore damage. One component means
players, enemies and future breakables all work the same way.

## 2026-09-24: Hotbar items are data (`.tres`), per-player state lives on the player

**Decision:**
- `HotbarItem` / `ThrowableItem` are `[GlobalClass]` Resources (`items/*.tres`): name, tint,
  cooldown, projectile scene, throw speed.
- The player's kit is `Player.Loadout`, a typed array of items. Slot 1 (Hands) is built in.
- Cooldowns are tracked on the **player**, not the resource, because every player shares the same
  resource. The owner keeps a copy for the HUD, and the **host keeps its own to validate** throws
  (1 s leeway for network jitter).

**Why:** new items and archetypes are mostly new `.tres` files, not new code.

## 2026-09-24: Thrown projectiles are spawned by the host, flown by everyone

**Decision:** the owner asks the host (`Player.RequestThrowItem` RPC). The host validates it
(cooldown, alive, throw starts near its view of the player's eyes) and spawns the projectile
through `ProjectileSpawner`. Starting position, velocity and thrower are replicated **once, at
spawn**. Every peer then flies the same arc locally. **Only the host's copy** detects the hit,
deals damage, triggers the splash effect for everyone (`Effects.Splash` RPC) and despawns it.

**Why:** no per-frame position syncing for projectiles, and damage stays host-authoritative.

**Trade-off:** the thrower sees their own flask appear after a round trip (not noticeable on a
LAN). Client-side prediction can come later if it feels laggy over the internet.

## 2026-09-24: Enemies are host-authoritative, spawned, with a runtime navmesh

**Decision:**
- The host runs enemy AI and replicates position, facing and `Telegraphing` (the attack warning).
  Clients smooth toward it, like remote players.
- Enemies are **spawned by `EnemySpawner`**, not placed in the level, so a dead enemy despawns on
  every client.
- **Navigation:** `NavigationRegion3D` (the level geometry is its child) is **baked at runtime on
  the host only**, one frame after load, since CSG geometry doesn't exist before that.

## 2026-09-24: Enemy pathfinding rules (fix for "stuck at the platform edge")

**Decision:**
- **The navmesh must match what bodies can physically do.** Characters are capsules with no
  step-up, and a 0.45 m capsule rolls over roughly 0.13 m at most. So the navmesh uses
  `agent_max_climb = 0.1` with `cell_height = 0.05`. Fine height cells keep ramps connected even
  with a small climb. The matching project setting is `navigation/3d/default_cell_height = 0.05`,
  since the map and navmesh cell heights must agree. The old 0.25 m climb connected ramp *sides*
  to the floor, so paths ran into walls.
- **Target the surface the player is on**, not the nearest navmesh point. We probe a vertical
  segment through their feet (`MapGetClosestPointToSegment`), which picks the platform top over
  the floor beside it.
- **Never beeline.** The AI only follows navmesh paths. No path (still baking, or unreachable)
  means it stands still and faces the target.
- **Attacks need the target on roughly the same level** (`AttackHeightTolerance`, 0.75 m), so it
  doesn't camp under ledges swinging upward instead of walking round.

**How we verified it:** a headless probe put the evil guy on the floor and a player at five spots
on the platform. Before: 4 of 5 stuck. After: 5 of 5 reached, repeatably. The hardest case is now
in the smoke test.

**Rule for future levels:** when characters get new movement abilities (step-up, jumping,
vaulting), update `agent_max_climb` and add navigation links to match, or paths will lie.

## 2026-09-24: Thrown items launch from the eyes

**Decision:** projectiles spawn exactly at the thrower's eye position (the mesh stays hidden for
the first 0.03 s so it doesn't flash through the camera). They used to spawn 0.4 m ahead, which put
them *inside* an enemy standing against you (or past a wall you were hugging). A ray that starts
inside a shape doesn't hit it, so the beaker flew straight through. Players aren't in the
projectile's hit mask, so launching from inside the thrower is safe.

## 2026-09-24: Input actions and layers

- LMB / RMB are `primary` / `secondary`: what they do depends on the selected hotbar slot.
  Scroll is `scroll_up` / `scroll_down`. Keys 1–5 are `hotbar_1..5`.
- Physics layer 4 = **Entities** (enemies). Bits are in `core/Layers.cs`.

## 2026-09-24: Enemy AI v2: perception, memory, noise, drop links

**Decision:**
- **Perception replaces omniscience.** The enemy tracks a player's real position only while it
  can see them (LOS, focused). After `LoseSightGrace` (0.75 s) out of sight it switches to
  **Search** at the last seen position plus velocity × `PredictSeconds`, a snapshot and never
  live. Acquiring a target needs the **vision cone** (or `CloseSenseRange`) and builds a
  **suspicion** meter over time (`NoticeTimeNear`/`Far`); ≥ 0.35 investigates, 1.0 hunts.
- **Noise is one host-side call: `Level.EmitNoise(position, radius)`.** Each enemy
  decides in `Enemy.Hear` whether it heard it (walls halve the radius) and whether to investigate.
  Anything that makes sound should call it:
  - **Footsteps:** worked out on the host from how far each player actually moved (so remote
    players need no extra RPC, and a blocked player is silent). Teleports (> 20 m/s) are ignored.
  - **Flask shatter:** `SplashProjectile.ShatterNoiseRadius`.
  - **Prop crashes:** a sudden *loss* of speed (being thrown or carried is silent).
- **Mood is replicated** (`Enemy.Mood`: Calm / Suspicious / Hunting) purely so every client can
  show it in the eyes. Readable AI is fair AI.
- **Drop-down links are generated, not placed.** `DropLinks.Generate` runs when the navmesh
  finishes baking. It walks the navmesh outline, probes past each edge for a 0.4–3 m drop onto
  walkable ground (no wall in the way), and adds a one-way `NavigationLink3D`. Any level, hand-made
  or procedural, gets them for free. The agent just walks the link and gravity does the rest.
- **Props:** the enemy **shoves** whatever it bumps (force at the contact point, so things tip and
  tumble) rather than planning around them. Cheaper than re-baking the navmesh as props move, and
  it creates noise and chaos.
- **Stuck recovery:** if it's pushing but moving under 25% of the intended speed for 0.75 s, it
  sidesteps for 0.5 s and re-plans.
- **Chasing re-plans every 0.25 s, not every frame, and that's load-bearing.** Stepping off a ledge
  puts it just off the navmesh, so a fresh plan starts from the nearest navmesh point *behind* it
  and says "go back". Re-planning every frame left it wobbling at the lip forever. A simplification
  pass removed the throttle and the drop-link smoke check caught it.

**How we verified it:** offline smoke checks cover each behaviour on a fresh enemy: blind behind,
sees in front; searches the last seen spot, not the true one; gives up; ignores far noise and
investigates loud noise; can't hear walking but can hear sprinting; drops off the platform; shoves
a crate. They pass 5/5 offline and 5/5 over the network.

## 2026-09-24: Gas mask visor: one post pass, HUD projected through it

**Decision:**
- **One full-screen shader does everything** (`vfx/visor.gdshader` on `Main/PostFX/Visor`): the
  visor (rim shape, curved glass, blur and fringing at the edge, fog, cracks, choke, hit flash)
  and then the old grain / colour crush, folded in. One screen read means no back-buffer ordering
  problems. The old `grain.gdshader` is gone.
- **`vfx/Visor.cs` drives it** from `Player.Local`: cracks = 1 − health fraction (spreading
  quickly, snapping clean on respawn), fog from exertion (breath rate and a slow recovery) plus
  filter wear, choke from a spent filter, sway from look speed, flash on health drops. No local
  player (menus) means `visor_enabled = 0`, i.e. just the camera grain.
- **Projected HUD:** `ui/visor_hud.tscn` renders in a `SubViewport` (`Main/HudViewport`,
  transparent, sized to the screen by `Visor`). The shader samples it through the same glass
  distortion, sway and shard offsets, with glow and flicker, so the HUD sits *on* the glass.
- **Crisp overlay stays outside the mask** (`ui/hud.tscn` on `Main/UI`): pause menu, death text,
  status, and the unstyled debug health / filter readout.
- **Cracks are procedural:** 8 fixed impact points with damage thresholds; each is a star of
  straight, kinked spokes (piecewise-linear noise) plus polygonal ring fragments. No textures.

**How we verified it:** `smoke_test` has a `capture` role that runs *windowed* (the headless
renderer can't draw shaders) and saves screenshots of the visor healthy, hurt and choking. The
first pass showed wavy "spider web" cracks, which were fixed to straight fractures from the
screenshots.

## 2026-09-24: Gas mask filter is host-owned, like health

**Decision:**
- `combat/Respirator.cs` is a child of the player (authority = host, like `Health`), replicated
  at 4 Hz (`replication_interval = 0.25`, since it changes constantly).
- The host drains it from the **same measured speed as footsteps**, so remote players need no
  extra RPC. When it's spent it deals damage in gasps (not per frame, which would spam hit
  effects).
- `props/FilterCanister.cs` is a `PhysicsProp` subclass: carry and throw work unchanged, plus a
  `RequestUse` RPC. Canisters are placed in the level (not spawned), so a used one can't be
  freed on clients. Instead a replicated `Consumed` flag hides it and turns off its collision
  everywhere.

## 2026-09-24: Audio is procedural, and "sound" and "noise" share one event

**Decision:**
- **No audio files.** `audio/SoundBank.cs` synthesises world one-shots (one per kind, cached as
  `AudioStreamWav` on first use, with the same seed as the previews, so what you audition is
  exactly what plays; `Effects.PlaySound` varies the pitch so repeats differ). `audio/MaskSynth.cs` synthesises the in-mask
  sound sample by sample into an `AudioStreamGenerator` (`audio/MaskAudio.cs`). Both are plain
  code "recipes" meant to be tuned by ear.
- **Buses** (`default_bus_layout.tres`):
  - **World** has a low-pass, the mask muffle. `MaskAudio` drives its cutoff from visor damage:
    1100 Hz intact up to 7000 Hz shattered, and 20 kHz with no mask (menus).
  - **Mask** is clean: breathing, heartbeat, fresh-filter hiss.
- **`Level.EmitSound(position, radius, kind)`** is the host-side call for anything audible: it
  calls `EmitNoise` (enemies hear it) *and* broadcasts `Effects.PlaySound` (players hear it,
  positionally, on the World bus) at the same radius. `EmitNoise` alone is for silent AI-only
  noise (footsteps, for now).
- **One breathing clock.** `players/Breathing.cs` (local player only) owns exertion, choke,
  rate and phase. The visor's fog and `MaskSynth`'s breathing both read it, so they can't drift
  apart. It replaced the visor's private copy.
- The in-mask mix goes through a **tanh soft limiter**, so overlapping sounds squash instead of
  clipping.

**Atmosphere pass (after "breathing way too loud, sounds superficial"):**
- World bus: low-pass (mask) **then reverb** (concrete room: room size 0.75, 35% wet, 40 ms
  predelay), so echoes are muffled too.
- `audio/Ambience.cs` (in each level, local to each player): a looping room tone plus random
  distant events placed around the listener. Atmosphere only, so it's not an `EmitSound`.
- Recipes moved from pure sine tones to **resonating noise** (`Resonator`, a two-pole resonator
  that's stable up to Nyquist, and `Layered`, which mixes per-layer-normalised parts), so things
  sound like objects rather than synths.
- Breathing about −14 dB from the first version: softer noise, two-band valve "voice", a comb-filter
  mask-cavity resonance, and rubber valve flaps. At calm it now sits just under the room tone.

**How we verified it:** Claude can't listen, so there's an objective check instead. The smoke
test's `audio` role (and the offline suite) renders every sound to `.wav` and fails on clipping
(peak ≥ 0.99) or near-silence (RMS ≤ 0.01). It caught the heartbeat and fresh-filter hiss
clipping on the first run. The windowed `capture` role runs it all on the real audio driver and
logs the **live bus meters** (Mask vs. World vs. Master) through calm, distant event, nearby
shatter + growl, hurt, and choking. That's how the mix balance is checked.
**Tuning is by ear:** render with `--role=audio --out=<folder>` and listen, or just play.

**Gotcha:** don't verify the mix with `AudioEffectRecord` on surround setups. On a 7.1 device
(like the main dev machine) it only records one channel pair (not the front), so non-positional
sounds (room tone, breathing) look silent in the recording even though they play fine. Use the bus
meters instead.

## 2026-09-24: Cel shading is applied at runtime over ordinary materials

**Decision (prototype):**
- `vfx/ToonStyle.cs` (on `Main`, **F2 toggles**) swaps every opaque, lit `StandardMaterial3D` in
  the scene (existing nodes, and new ones as they're added) for a `ShaderMaterial` using
  `vfx/toon.gdshader`. Each toon copy's albedo and emission are **copied from the original every
  frame**, so code that animates materials (hit flashes, the evil guy's mood eyes) keeps working
  untouched. Turning it off restores the originals. Transparent and unshaded materials (glass,
  effects, liquids) are skipped.
- Lighting bands come from `vfx/toon_ramp.tres` (a constant-interpolation gradient: light level in,
  brightness out), editable in the inspector. The thresholds are tuned for our dim, fast-falloff
  lamps (0.05 / 0.25). The first try (0.22 / 0.6) turned most walls black. (Superseded below: the
  ramp now keeps the room's brightness.)
- Outlines are a screen-space pass (`vfx/outline.gdshader`) on a full-screen quad parented to the
  active camera: depth and normal discontinuities, fading with distance. It reads the normal
  buffer, so it's **Forward+ only** (fine for PC; phones would need another approach).

**Why:** artists keep authoring ordinary materials, the style is switchable for comparison, and it
covers grey-box, props, characters and effects with no per-asset work.

## 2026-09-24: Liquids: world-space cut plane plus local slosh

**Decision:** `vfx/liquid.gdshader` discards everything above a world-space surface plane and draws
the back faces below it as the flat surface (plus a meniscus band). `vfx/Liquid.cs` (on the
`Liquid` mesh) puts the plane at `Fill` of the mesh's current world-space height range (so tipping
pours it to the low side) and tilts it with a damped spring driven by changes in the vessel's
velocity. It's purely visual and computed on every peer from how the prop moves, so there's no
networking. The model just needs a closed `Liquid` mesh filling the vessel (see ART.md).

## 2026-09-24: The acid item is the Erlenmeyer flask, and its recharge shows in the liquid

**Decision:**
- One model scene, `models/erlenmeyer_flask.tscn` (glass + `Liquid`), is instanced by the flask
  prop, the flask in your hand (`HandItem` in `players/player.tscn`) and the thrown flask
  (`items/acid_flask_projectile.tscn`). Swapping in the real model there updates all three.
- The player syncs **`SelectedItemCharge`** (0 = just thrown, 1 = ready) instead of the old
  `SelectedItemReady` bool, and the hand flask's `Fill` is its full fill × charge. So everyone,
  not just the thrower, sees it empty and refill. It costs one float per player while recharging.
- The hand item is always the flask, since it's the only item. When an item that isn't a flask
  arrives, give `HotbarItem` a hand-model scene.
- `Liquid.cs`: a shallow liquid's slosh is capped by its depth, so a nearly empty flask doesn't
  slosh a wedge of liquid up the side from nowhere. `Fill` 0 hides it.

**Why:** the user's call: the cooldown shows on the object, not only on the hotbar.

## 2026-09-24: Outlines are one-sided (1 px), with width in the Inspector

**Decision:** `vfx/outline.gdshader` only counts depth / normal jumps towards neighbours that are
farther away, so only the nearer side of each edge draws the line. Lines are `thickness` pixels
wide (default 1). Before this, both sides drew it, so lines were 2 px even at the lowest setting.
The outline material lives on `Main/ToonStyle` (**Outline Material** in the Inspector): width,
colour, sensitivity and fade distance are its shader parameters.

**Why:** the user wanted thinner lines.

## 2026-09-24: The toon ramp keeps the room's brightness

**Decision:** each step of `vfx/toon_ramp.tres` outputs roughly the average light level it
covers (the steps follow the diagonal): below 0.03 → 0.01, 0.03–0.15 → 0.07, above 0.15 → 0.3.
Cel shading then only bands the light into hard edges and doesn't change how bright the room
is. The `capture` role measures the average brightness of the view with cel shading off and on,
logs both, and fails if they differ by more than 20%.

**Why:** the old ramp (0.05 → 0.5, 0.25 → 1.0) made the view 89% brighter than without cel
shading. A wall getting 5% light rendered at 50%, which killed the dark mood. The new ramp
measures −1%.
To get more punch at the same brightness, spread the steps apart (darker darks, brighter lights),
and let the capture check keep the average honest.

## 2026-09-24: Debug view hotkeys are one table in the Hud

**Decision:** F2–F6 switch parts of the look on and off to compare: cel shading, outlines, film
grain, colour crush, and the lens effects (glass curve, edge blur, colour fringe). Each one is a
bool `[Export]` on the node that owns it (`ToonStyle.Enabled` / `Outlines`, `Visor.Grain` /
`ColourCrush` / `Lens`), so **the Inspector checkboxes are the defaults**. `ui/Hud.cs` holds the
key → property table, and after each press it lists what's on (top right, for 3 s). The visor
shader skips grain / crush / lens with `grain_on` / `crush_on` / `lens_on`. Breath fog, cracks and
choking can't be switched off: they're gameplay feedback. (2026-09-25: F7 mutes all sound, via a
`Hud.Sound` property over the Master bus, for testing without the noise. Not saved.)

**Why:** the user wanted to compare effects. Grain and colour crush look good on the smooth
look but choppy over cel shading. Adding a new switch is one line in the table.

## 2026-09-24: Visor cracks are procedural, one fracture per hit

**Decision:** `vfx/Visor.cs` turns each drop in the local player's health into a fracture: where
it struck the glass, a random seed, and its size (the damage as a fraction of max health). It
passes up to 12 of them to `visor.gdshader` (`impacts[12]`, as a float array). The 8 fixed
crack spots are gone.
- **Where:** toward the nearest evil guy within 4 m, projected through the camera the same way
  the screen is. Hits from behind or the side land on the rim on that side. There's some
  scatter. With no enemy nearby (choking), it lands anywhere.
- **Look:** a frosted crushed spot, spider-web shards around it, and 3–6 long cracks. The shards
  are Voronoi cells in log-polar space: wedges, small near the impact and growing outward, with
  most radial edges cracked and fewer ring edges the further out they are. The long cracks bend
  and kink, hairlines glint unevenly, and each shard shifts and shades the view a little.
- **Growth:** each fracture spreads out over about 0.2 s. The overall damage makes every crack
  run further, so the glass still reads as the health bar.
- **Small hits:** a hit under 6% of max health (choking ticks) spreads the latest fracture
  instead of starting a new one, so choking doesn't pepper the glass.

**Why:** the user wanted more realistic, procedural cracks. It's also the guiding principle:
cracks now say where you were hit from.

---

## 2026-09-25: Models are built by Blender scripts, sources in an ignored src folder

**Decision:** each model in `game/models/` is `<name>.glb` (the export Godot imports) and
`<name>.tscn` (the game-ready scene: the `.glb` plus materials and scripts; vessels share
`glass.tres`). Its sources are in `game/models/src/`: `<name>.py` (Blender Python that builds it)
and `<name>.blend` (what it builds, for hand-tweaking), with shared helpers in `model_tools.py`.
`src/` has a `.gdignore`, because Godot 4.7 imports `.blend` files itself whenever Blender is
installed, even with `filesystem/import/blender/enabled=false` (tried; it still made
`.blend.import` files). `*.blend1` backups are gitignored. The first model is the Erlenmeyer flask:
glass shell (1,920 tris) and a closed `Liquid` mesh (832 tris), 21 cm tall, lathed from a profile.

**Why:** a script is diffable and rebuilds exactly, and Claude can model through it (the Blender MCP
tools weren't available in the session, headless Blender was). Keeping the `.blend` means an artist
can still open it and work by hand. After that, the `.blend` is the source and the script is stale,
so note that in the script's header.

**Note:** the flask's `Liquid` now fills the neck too, so the same `Fill` sits higher than on the
placeholder. The model scene sets `Fill = 0.45` to keep about the same level.

## 2026-09-25: The host picks a level in the menu; maze chambers are ordinary levels

**Decision:** `Main.Levels` lists the levels the host can load (the first is the default), and the
main menu's level picker chooses one before hosting. Each must also be in `LevelSpawner`. The first
maze chamber, `maze/test_chamber.tscn`, uses the same `Level` script as the test level. Its end is a
`ChamberExit` area (`maze/ChamberExit.cs`): the host passes the test once every living player is
inside at the same time, and tells everyone the time with a reliable RPC. The visor shows the clock at the top.

**Why:** the smallest way to try a second mode without a lobby or mode-select screen. Chambers
being plain levels means stitching them together later is about placing rooms, not new plumbing.

## 2026-09-25: Mental damage is part of Health, not a separate meter

**Decision:** `Health` keeps one pool, plus `Mental`: how much of the lost health was mental.
`TakeMentalDamage` (host only) takes it off `Current` and adds it to `Mental`; `Physical`
(`Current + Mental`) is health counting only physical hits. `Damaged` only fires for physical
damage, so mental damage doesn't flash your body or make an enemy retarget. Revive clears both.
Mental damage stops at MentalFloor (25). MentalFraction = Mental / (Physical - MentalFloor), so
it's 1 exactly when there's no mental damage left to take; the effects read that, not Mental / Max.
The player's health synchronizer lists `Mental` **before** `Current`, so a client already has the
new `Mental` when `Current` drops and can tell the drop was mental. The visor's cracks and the
mask muffle follow `Physical`; the visor shader's `mind` uniform (Mental / MaxHealth) corrupts the
projected HUD, `VisorHud` scrambles its text, and `MaskSynth` adds a tinnitus tone. Withdrawal
(`Respirator`, below `LowGasFraction`) replaces the old physical choke damage.

**Why:** the user wanted one health that dies either way, with mental damage looking different
(see DESIGN → "The mask is the lie"). A second pool would need its own death rules and a second
readout.

## 2026-09-25: Player data is a local profile, host-clamped; decay counts runs

**Decision:**
- One `user://profile.json` per player: `RunCount`, money and the research tree (per profile),
  and a roster of characters (level, rebirths, upgrades, abilities, `LastRun`). Only what's "in
  the scientist's head" is saved; the rat's gear and loot never are.
- `RunCount` goes up and is saved **when a round starts**, so quitting can't dodge decay.
- **Decay is derived, not stored:** rust = `RunCount − LastRun`, fed through a curve when an
  ability is read. Playing a character moves `LastRun` forward by `K` runs (capped), so retraining
  is gradual.
- On join, the client sends its active character to the host, which clamps impossible values.
  The host sends round results back by RPC; each client applies them and saves its own file.

**Why:** there's no backend or Steam yet, and co-op cheating mostly only affects the cheater
(the same call as client-authoritative movement). The clamp covers the one shared effect: the
maze difficulty formula reads everyone's level. Counting runs instead of real time can't be
cheated with the clock and doesn't punish breaks. A derived rust value means the curve can be
retuned without migrating saves.

**Trade-off:** saves can be edited. When there's a backend, it stores the same record (keyed by
Steam id) and the host reports results to it; the format stays the same. Steam Cloud alone
wouldn't fix this, since a synced file is still editable.

## 2026-09-25: Pressure plates are weighed by the host; doors follow them on every peer

**Decision:**
- `maze/PressurePlate.cs` (an `Area3D`, scene `maze/pressure_plate.tscn`): the host adds up the
  weight on it every physics step (players count as 60, `PhysicsProp`s count their mass unless
  someone is carrying them) and sets `Pressed` when it reaches `MinWeight` (30). Only `Pressed`
  is replicated.
- `maze/ChamberDoor.cs` (an `AnimatableBody3D`, scene `maze/chamber_door.tscn`) is open while
  **all** of its `Plates` are pressed. Every peer works this out from the replicated plates and
  slides its own copy, so the door needs no synchronizer. It pushes players and props out of the
  way as it closes. The host plays the `Impact` sound when it starts moving (no new recipe).
- ~~Doors sit **outside** the `Navigation` node, so the navmesh is baked as if they were open.~~
  Superseded the same day, see "Doors are walls to the navmesh" below.

**Why:** weight, rather than "anything touching it", makes the heavy case the weighted cube for
free, and lets players improvise (a pile of crates works). Deriving the door from the plates
keeps one piece of replicated state per puzzle piece.

**Update, same day: timed buttons, and doors don't crush.**
- Anything that opens a door implements `ISwitch` (`bool Pressed`), next to `ChamberDoor`. The
  door's list is `Switches`, with `OpensOnAny` for "any one of them" (default: all of them).
- `maze/ChamberButton.cs` (a `StaticBody3D` on the World layer, so the grab ray hits it): the
  player's [E] sends `RequestPress` (the host checks the player is alive and within 3 m), and a
  `PhysicsProp` hitting its `HitZone` at 2 m/s or faster presses it too. The host keeps the
  release time and replicates `Pressed` plus a `Presses` counter, so every peer restarts the
  glow countdown on a re-press without replicating a timer.
- `Player.UpdateFilterSwap` became `UpdateInteract`: a button you're looking at comes first, then
  the filter.
- **A closing door waits** while `TestMove` finds a player or prop in its way (its collision
  mask exists only for that test). That means no crushing, and a crate jams it open. Opening
  doors don't check; they only move into walls.

## 2026-09-25: A maze run is a node on Main that swaps chamber levels; revive is per level

**Decision:**
- `maze/MazeRun.cs` sits on `Main` (`Main/Run`), so it survives level changes. On the host it
  loads a chamber through `Main.ChangeLevel` (the `LevelSpawner` copies it to clients like any
  level) and moves on once `ChamberExit.Current` is complete. It **fails the run when every
  player is dead at once** and passes it after `Length` chambers. It tells everyone its state
  (active, chamber, length, result) with one reliable RPC, re-sent to late joiners. The menu shows
  any level in `Chambers` as "Maze run".
- Each chamber is its own level, loaded fresh (a lift between tests, in Portal terms). It's
  simpler than stitching rooms into one level, and it's what "chambers are ordinary levels"
  already gave us. Every player starts each chamber at full health.
- `Level.TimedRespawn` (off in chambers) decides whether the dead come back on the 8 s timer.
  Otherwise the owner holds [E] near a downed teammate. Once the hold finishes, it sends
  `RequestRevive` to the host (alive, within 3 m) → `Health.ReviveTo(50)`. **A full-health revive is
  a respawn** (spawn point, fresh filter); anything less leaves you where you fell. That is how
  `Player.OnRevived` tells them apart.
- `Health.Revive()` stays argument-free, with `ReviveTo(amount)` beside it: GDScript can't use
  a C# default argument, so `Revive(float amount = -1)` broke the smoke test's calls.
- **The smoke test now fails on any logged error** (`OS.add_logger`). Before, a script error or
  C# exception only aborted the function it was in, and the run could still say PASS. That's
  exactly what happened to the `Revive()` calls above.

**Why:** the smallest loop that has an end state and a way to lose, built from what exists.

## 2026-09-25: Doors are walls to the navmesh, with a link that's on while they're open

**Decision:**
- Chamber doors and buttons go **inside `Navigation/Geometry`**, so the navmesh is baked with
  the doors shut (an `AnimatableBody3D` is a `StaticBody3D`, so it's parsed as a static collider).
  To the evil guy, a door is a wall.
- Each `ChamberDoor` makes a `NavigationLink3D` through its doorway (1.2 m each side, on the floor,
  `TopLevel` so it stays put while the door slides). It's **enabled only while the door is fully
  open**, so he never plans into a half-open door, and the moment a door starts closing his route
  is gone. It's the same mechanism as the drop-down links.
- A closing door also waits for enemies (its test mask includes `Entities`).
- `Level.EnemyReleaseDelay`: a level's enemies first appear after this many seconds, with a
  `Growl` via `EmitSound` (40 m, so everyone hears it and knows). The test chamber uses 20 s.

**Why:** re-baking the navmesh every time a door moves is slow and would regenerate the drop
links. A link toggled with the door is one flag on the host. Baking the doors shut means
unreachable really is unreachable: he waits at the door instead of walking into it.

**How we verified it:** the smoke test asks the navmesh for a path from the room to the exit
with the door shut (none) and fully open (found). Forcing the link always on makes the "shut"
check fail, so it's not passing by accident.

## 2026-09-25: The evil guy only wanders to spots he can reach

**Decision:** `Enemy.RandomReachablePoint` (wander and search spots) samples up to 8 spots and
keeps the first one whose navmesh path actually ends there. If none does, it stays put and tries
again next time it's idle.

**Why:** the navmesh also covers unreachable surfaces, such as the roof on top of a chamber's
shell (5 m up) and tabletops. A random spot near a wall often snapped up there. In the test
chamber he spawns in a corner, so most picks were unreachable, and he stood still after his
release. Checking the path is the general fix: it also covers rooms behind shut doors.

**How we verified it:** the smoke test watches the released evil guy for 6 s and fails if he
ever heads for a spot he can't reach. Against the old code it fails with a spot on the roof.

## 2026-09-25: Chambers are listed easiest first, and a run climbs the list

**Decision:** `MazeRun.Chambers` (on `Main`) is in difficulty order, with no separate difficulty
tag. Chamber *k* of a run sits at position `k × (chambers − 1) / (length − 1)` along the list. When
that falls between two chambers it picks one at random, weighted to the nearer. So a 3-chamber run
over 3 chambers is always easiest → hardest, and adding chambers makes runs vary without code
changes. New chambers go in `maze/` as ordinary levels, and in `Main`'s `LevelSpawner` list.

**Why:** a list order is the smallest thing that gives a ramp. A per-chamber difficulty number
only earns its place once chambers are assembled from modules, or picked from a big pool.

**How we verified it:** the smoke test runs every chamber in the list (spawn, the evil guy can
reach the start, the exit is shut). It solves the new two with props, including a jar thrown at a
real throw's speed from the exit door onto the ledge button. It checks that a 2-chamber run is the
easiest chamber, then the hardest.

## 2026-09-25: The network test plays a whole maze run, over a fake bad connection

**Decision:** `--role=network` (in `tests/smoke_test.gd`, the suite is `tests/net_suite.gd`) hosts
and starts its own clients (up to 4; with 2 or more, the last joins mid-run). They play the sandbox
and then a whole maze run the way players would: they walk (colliding with doors), carry crates onto
the plate one at a time, hold a button door open, fetch the case from the closet, go down and get
revived, lob a jar onto the ledge button, and at the end one leaves while carrying a crate and the
rest go down to fail the run. They meet at barriers on the host, ask it for host-only things, and
send it their results, so the host's exit code covers everyone. `--ping` (round trip, ms),
`--jitter` and `--loss` (%) put each client behind `tests/lag_proxy.gd`, a UDP relay on a thread
that holds packets back and drops some. It also measures what a player would feel: how far a
carried prop trails the hold point, how long a grab takes, the late joiner's clock, and traffic.
The old `host` / `client` roles still work by hand (one client, no late join).

**Why:** every network test ran on one machine with no delay, and the maze run had never been
played over the network at all. This found nine real bugs in its first day (the entries below).

**Passing configurations:** 1 client with no lag; 3 clients at 150 ms / 20 ms jitter / 2% loss; 4
clients at 250 ms / 40 ms jitter / 5% loss.

**Gotcha:** test processes cap themselves at 120 fps (`Engine.max_fps` in `smoke_test.gd`).
Headless Godot runs uncapped, so a host and four clients on one machine each spun a core flat out
and starved each other: a client could go a second without a frame, so the host didn't know where
it was and refused its first grab. That looked exactly like a network bug. Players have v-sync.

## 2026-09-25: Player state goes through the host; clients never talk to each other

**Decision:**
- Each client sends its own player's state (position, look, hold distance, slot, flask charge) to
  the host 30 times a second (`Player.ReportState`, unreliable-ordered). The host copies it into
  the player's `Sync*` properties, and the player's synchronizer (now **host-owned**, rooted on
  itself, 30 Hz) sends it on to everyone else. A visibility filter keeps it from going back to the
  owner, so nothing the owner controls (slot, hold distance) is overwritten by an older copy.
- The synchronizer is rooted on its own node, not the player: a spawned node's synchronizer
  visibility also decides whether that node is spawned for a peer, and hiding the player from its
  owner would stop the owner getting its own player.
- **Server relay is off** (`SceneMultiplayer.ServerRelay = false`, set in `Network`): a star
  network. Clients only hear from the host, and don't learn about each other.

**Why:** with client-owned synchronizers, every client sent its state to everyone (relayed by the
host). State in flight for a player that had just been removed (level changes) or not yet created
(late joiners) gave dozens of engine errors per transition at 150 ms. With the relay on, two
clients leaving in the same frame made the host tell one about the other after ENet had already
dropped it (another engine error). Same latency as before, since relayed traffic went through the
host anyway.

## 2026-09-25: Level changes wait for clients to stop reporting

**Decision:** `Main.ChangeLevel` (host) first sends `LevelEnding`; each client stops reporting its
player and confirms; the host swaps once everyone has (or after 1 s). The request, the reports and
the confirmation all use `Network.StateChannel` (transfer channel 1): on one channel, reliable and
unreliable messages stay in order with each other, so no report sent before the confirmation can
arrive after it. (On channel 0 they take separate routes.) `Main.ChangingLevel` is true meanwhile;
`MazeRun` waits for it.

**Why:** a report still on its way when the host freed the level arrived for a player that no
longer existed. And while the swap waits, the old chamber (already passed) is still loaded: the run
counted it as passed again and skipped the next chamber. The network test caught both.

## 2026-09-25: The prop you carry is simulated on your own machine

**Decision:** a client carrying a prop unfreezes its own copy and runs the same carry spring on it,
toward its own (lag-free) hold point, so the prop hangs where you hold it. When you let go or throw,
your copy stops being pulled at once (and, for a throw, flies off at once), and the host is sent
your copy's position and velocity (`PhysicsProp.RequestLetGo`). The host takes them over if they're
within 2 m of where it has the prop, so the prop goes where you saw it go. Your copy keeps
simulating until it has come to rest where the host has it too (or 4 s), then goes back to being
a frozen copy of the host's. Everyone else still sees the host's copy.
Also: the host pulls a client's prop toward that client's latest *reported* hold point, not the
smoothed body it draws (which trails it), and a held prop can't fall asleep (a sleeping body stops
being pulled).

**Why:** the host simulated every prop, so a client's carried prop lagged a full round trip plus
two lots of smoothing: it trailed the hold point by about 1 m on a perfect connection and 2–2.9 m
at 150 ms, and the host drops a prop that falls 3 m behind. Now it trails by the carry spring alone:
0.5 m on average while walking, at any ping, and drops land within a few centimetres of where the
holder let go.

**Trade-offs:** your copy collides with frozen copies of other props, which don't budge, so while
carrying it can snag on something that moves on the host; the host takes over your version on
release. Grabbing still waits a round trip (200 ms at 150 ms ping): the host has to agree you got it.

## 2026-09-25: Leaving says goodbye, and silent peers are dropped after 6 s

**Decision:** `Network.EndSession` disconnects gracefully (`PeerDisconnectLater`: acknowledged,
after anything still queued) and keeps polling the old connection for up to 1 s while the rest of
the game has already moved on (`Network.IsClosing`). Both sides set ENet's timeout to 2–6 s
(default 30 s).

**Why:** closing outright sends one unacknowledged packet; when it was lost, the host only noticed
the player had gone when they timed out, 30 s later, and their frozen player kept holding whatever
it carried.

## 2026-09-25: Smaller network fixes the test found

- **Spawn points are handed out by the host** (`Level.AddPlayer`, the first free slot, through the
  player spawner's spawn function so every peer builds the player with its `SpawnSlot`). Clients
  used to pick `peer id mod 6`: a 72% chance that two of four players start on top of each other.
- **Reach checks use the latest report** (`Player.EyePosition` for someone else's player): throws,
  grabs, buttons, filters and revives. At high ping the host refused actions the player really was
  close enough for.
- **One held prop per player is enforced by the host** (`RequestGrab`), not just the client.
- **The chamber clock and "test complete" are replicated** from the host (a synchronizer on
  `Exit`, 10 Hz), so late joiners see the right time and result. Each peer used to start its own
  clock when it loaded the chamber.

## 2026-09-25: Players pass through each other; downed players lie down

**Decision:** players don't collide with other players (`collision_mask` 13: world, props,
entities). They still count for plates, the exit, doors and enemies. A downed player's collision
shape lies down with the body (it was an invisible 1.8 m pillar), so it still weighs down a plate.

**Why:** at internet ping, everyone bumps into where the others were a moment ago, and in the
network test the physics pushing two overlapping players apart shoved one through the floor. Being
able to block or stand on teammates can come back later as a deliberate mechanic.

## 2026-09-25: Settings are an autoload; the panel is one table

**Decision:** `core/Settings.cs` (autoload `Settings`) holds mouse sensitivity (a multiplier on
`Player.MouseSensitivity`), field of view, master / world / in-mask volume, fullscreen, v-sync and
3D resolution scale. It loads `user://settings.cfg` (a `ConfigFile`) at startup and applies what the
engine needs (bus volumes, window mode, v-sync, `Viewport.Scaling3DScale`); the player reads
sensitivity live and field of view whenever it changes. `ui/settings_menu.tscn` (one instance on
`Main/UI`, `SettingsMenu.Instance`) builds its rows from a table in `SettingsMenu.cs`, applies every
change at once and saves when it closes. It opens from the main menu and the pause menu (taking the
pause menu's place until it closes). The F2–F7 debug switches stay separate and unsaved.

**Why:** mouse sensitivity was a hard-coded export, and there was no volume, window or graphics
option at all: the first things a playtester asks for. The 3D resolution scale is the cheap lever
for weak GPUs (the visor, outlines and cel shading have never been tried on one).

**How we verified it:** the offline smoke test saves, reloads and applies settings (the world
volume must reach the World bus) and loads the panel; the capture role screenshots it over the game.

## 2026-09-25: Windows export preset and CI on every push

**Decision:**
- `game/export_presets.cfg` has one preset, **Windows** (x86_64, `.pck` embedded, tests excluded),
  exporting to `builds/windows/RND.exe` (gitignored). With .NET, the exe sits next to a
  `data_RND_windows_x86_64` folder; ship the whole folder.
- `.github/workflows/build.yml` runs on every push and pull request (Ubuntu): downloads Godot 4.7
  .NET and its export templates (cached), builds the C# project, imports assets, runs the offline
  smoke test and the network test (2 clients at 150 ms / 2% loss), then exports the Windows build
  and uploads it as the run's `RND-windows` artifact. Client logs are kept if the network test fails.

**Why:** nobody outside this machine could run the game, and nothing checked that a push didn't
break it.

**Not yet verified:** the workflow has never run (it runs once the branch is pushed), and no build
has been exported locally, because the export templates (about 1 GB) aren't installed on the dev
machine. The preset itself loads: a local export stops only at the missing templates.

## 2026-09-25: Runs pay into the local profile; the host clamps levels for difficulty

**Decision (the storage design from "Player data is a local profile", now built):**
- `players/Profile.cs` is the data (`Profile`: version, run count, money, active character, the
  roster; `Character`: id, name, archetype, level, XP, rebirths, upgrades, abilities, `LastRun`),
  plus its rules: `StartRun` (count the run; the active character's `LastRun` moves forward by
  `RetrainRuns` = 3, capped), `Grant` (money and XP, levelling up at `100 × level` XP per level) and
  `Sanitise` (clamps anything a hand-edited file got wrong).
- `players/ProfileStore.cs` (autoload) loads `user://profile.json` the first time it's asked, or
  the path given with `--profile=<path>` (a second copy of the game on the same PC, and the tests,
  use their own). Saves go to a `.tmp` file and are then moved into place, so a crash mid-save can't
  corrupt it; a file that won't parse is kept as `.bad` and a fresh profile started.
- `MazeRun` numbers each run of the session (`runId`, sent with its state). Each peer's profile
  counts a run the first time it sees that run active, late joiners included, so quitting can't
  dodge the count. At the end, the host sends the pay (`Pay` RPC, every peer, once per run):
  **100 money and 50 XP per chamber passed** (failed runs too), **plus 200 money and 100 XP for
  passing the run**. Numbers are exports on `Main/Run`, to tune.
- Each client reports its level to the host on joining; the host clamps it (1–50) and uses
  **0.7 × average + 0.3 × highest** (DESIGN) to pick where in the chamber list the run starts: level
  1 at the easiest, level 20+ halfway up. Runs still end at the hardest.
- The result screen shows the pay (and a level-up); the main menu shows level, XP, money and runs.

**Why:** the maze run had an end but paid nothing, so there was nothing to come back for.

**How we verified it:** the offline smoke test (on its own profile) passes a run and fails one,
then checks the run count, `LastRun`, money, level and XP, that it's on disk, and that an unreadable
file is set aside. The network test checks every client (the late joiner too) was paid for the run
and counted it once, and that the failed run afterwards paid nothing.

**Not yet:** nothing to spend money on (the between-run hub / research tree is its own job), no
abilities (so no decay effects yet, though `LastRun` is tracked), one character per profile (the
roster exists in the file, there's no UI to make or pick another).

## 2026-09-25: Enemy AI v3: calls, waiting at doors, habits

**Decision:** still only what it perceives, but it uses more of it.
- **Calls.** When an evil guy spots someone, and again when it loses them, it growls a *call*
  (`Level.EmitCall`): players hear the growl like any sound, and every other evil guy that can hear
  it (walls muffle, as for noise) goes to the call's *lead*, where the player is (or, when lost,
  where they were heading), not to the caller. The pack converges on you, and a lost chase becomes a
  flank. It's fair: you hear the same growl.
- **Waiting at doors.** `Investigate` checks whether its path reaches the spot. If not (behind a
  shut door, since doors are walls to the navmesh), it goes as close as it can and waits there
  (`State.Ambush`, `AmbushSeconds` = 20 s), facing the spot and re-checking every 0.5 s; when the door
  opens it goes in. If there's another way round, that's simply the path it takes.
- **Habits.** Where it loses sight of someone is remembered per level (`Level.RememberLostAt`,
  spots within 2.5 m merge). A spot where players have got away twice or more is where it patrols
  when calm, and the first place it checks when searching nearby (each spot at most every
  `HabitCooldown` = 20 s). The more you hide somewhere, the more it looks there.
- `Enemy.Hear` and `HearCall` share one hearing check (`HearingReach`).

**Why:** the next step from the design's AI v3 list, and three known limitations: it stood at shut
doors doing nothing, it had no way to share what it saw beyond a growl that pointed at itself, and
it never learnt. Noise itself stays sourceless on purpose: a thrown crate should lure it away (the
design's distraction play), which a noise that told it who threw it would spoil.

**How we verified it:** offline smoke checks: a second evil guy that hears the first one's growl
(but can't see you) heads for you, not the growler; losing you is remembered; a spot where players
got away twice is the first place it patrols; in the closet chamber it waits by the shut door for 7 s
after hearing a noise inside, and comes in when the door opens.

## 2026-09-25: Generated chambers, built from a seed on every peer

**Decision:**
- `maze/ChamberGenerator.cs` builds a chamber from a seed and a difficulty (0–1) into
  `maze/generated_chamber.tscn`, an empty chamber (spawners, exit, effects, no room). A room is
  12–16 m square with a start and an exit corridor; the puzzle is bought from a difficulty budget
  (1 + 3 × difficulty points): a plate always (weighed down by 4 crates or the case), then a closet
  holding the case behind a button door (1.5), a ledge button only a thrown jar can press (1.5), a
  second plate (1), a heavier plate needing case + crate (0.5), and above 0.75 a second evil guy
  (1). The evil guy's release comes sooner the harder it is (28 s → 12 s). Pieces go on a 1 m grid
  that keeps the walk from start to exit, both doorways and the closet's mouth clear.
- The room is built from **plain boxes** (static bodies with box meshes), not CSG: the navmesh
  bake reads them at once, whereas CSG made in code only builds its geometry later.
- The generator leaves a **plan** on the level (meta `plan`: seed, difficulty, size, pieces, each
  plate and the props that weigh it down, the closet's button and what it holds, the ledge button
  and a spot to throw from). The smoke test solves chambers from it.
- **Networking:** the host's `MazeRun` picks `GeneratedShare` of chambers (0.5) as generated, with a
  fresh seed and the run's difficulty at that point; `Main.ChangeToGenerated` spawns
  `[seed, difficulty]` through the level spawner, whose spawn function builds the room on every peer
  (late joiners too). Same seed, same names and places, so everything replicates as in a
  hand-built chamber.

**Why:** a run was the same three rooms in the same order. The design's "chambers assembled from
modules" is the scalable answer to content, and "intelligent and dynamic" applies to rooms too.
Chambers stay separate levels (a lift between tests) rather than stitched into one.

**How we verified it:** the offline smoke test builds seed 7 twice (the same room), then builds
and solves one chamber at each of three difficulties. `--role=generator --seeds=N` sweeps more:
each chamber must spawn you in the start corridor with the exit shut, let the evil guy reach you,
keep every prop it needs reachable, and open when its plan is followed (closet button, props onto
plates, a real-speed jar lob at the ledge button). 90 of 90 passed (30 seeds). The network test
makes the run after the failed one generated and checks every client built the host's room.

## 2026-09-25: Navigation maps update synchronously

**Decision:** `navigation/world/map_use_async_iterations = false` and
`navigation/world/region_use_async_iterations = false` in `project.godot`.

**Why:** with Godot's asynchronous map updates, a navmesh change that arrives while the map is
still rebuilding (a new level's bake finishing right after the level itself was added) can be
dropped: the map then has **no polygons** for that level, and nothing can path. The generator sweep
found it (every chamber after the first failed; alone, each passed; forcing a map update fixed it;
the map setting alone still missed one in the longer offline suite, so regions update in step too).
It can happen on any level change, so it's the likely cause of "the evil guy saw you but never left
his spawn" (noted earlier as stale editor state). Our maps are small, so updating them in step
costs nothing noticeable.

## 2026-09-25: Steam plan (built 2026-09-26, see "Steam transport built" below)

**Recommendation:** **Facepunch.Steamworks** (MIT, C#, one NuGet package that includes
`steam_api64.dll`) over GodotSteam (a C++ engine extension; from C# it's reached through untyped
calls or community bindings, and it's a per-platform binary to keep in step with the editor).
The plan, all inside `core/` so gameplay code doesn't change:
- `core/SteamPeer.cs`: a `MultiplayerPeerExtension` over Steam's networking sockets (Valve's relay,
  so no port forwarding or NAT trouble). Peer ids come from Steam ids (a 31-bit hash; the host is
  1). Godot's reliable / unreliable modes map to Steam's send flags.
- `Network.cs`: `HostSteam()` creates a friends-only Steam lobby and opens a listen socket;
  `JoinSteam(lobby)` connects to the lobby owner. Accepting an invite (Steam overlay) or joining
  from the friends list calls it. The main menu gets "Host on Steam" / "Invite friends"; the IP
  box stays for LAN.
- Development uses Steam's test app id **480** (set in code; no `steam_appid.txt` needed, and not
  shipped). A real app id needs the $100 Steam Direct fee.
- One thing to change first: our level-change handshake relies on ENet keeping a channel's
  reliable and unreliable messages in order. Steam doesn't promise that across the two, so the
  player state reports should carry the level they belong to (the host drops stale ones).
- **Testing needs two computers with two Steam accounts** (Steam allows one account per machine),
  so the last step is a playtest by the team. The network smoke test keeps running on ENet.

*(The team said yes on 2026-09-26.)*

## 2026-09-26: Steam transport built (`Decided`, relay still to try with two accounts)

Built as planned (Facepunch.Steamworks 2.3.3, Steam test app id 480), with two changes:
- **Ordering lives in the Steam peer, not in gameplay.** Instead of tagging player reports with the
  level they belong to, `core/SteamPeer.cs` copies ENet's rule: on a channel other than 0, an
  unreliable-ordered message is dropped if a reliable message sent after it has already arrived
  (each message carries the sender's reliable count for its channel). Gameplay code and the
  level-change handshake stay transport-blind.
- **The host hands out peer ids** (a welcome message, reliable, so it arrives first), like ENet,
  instead of hashing Steam ids. That's also what lets several test clients share one Steam account.

How it fits together:
- `Network.StartSteam()` runs at startup (not in headless runs) if Steam is running; callbacks are
  pumped in `Network._Process`, on the main thread. No Steam just means no Steam button.
- **Host on Steam** (main menu) opens a relay listen socket and a friends-only lobby. Friends join
  from their friends list ("Join game") or an invite (pause menu → Invite Steam friends, which
  needs the Steam overlay); both arrive as a lobby join, and the game connects to the lobby's
  owner. A game that isn't running is started with `+connect_lobby <id>`, which we read too.
- The host only lets in its Steam friends and people in its lobby.
- Steam's native library (`steam_api64.dll`) is copied next to our assemblies by the csproj, and a
  resolver in `Network` loads it from there: Godot loads our assemblies from memory, so .NET
  wouldn't look in that folder by itself.
- **Testing on one computer:** `--steam=1` on the network smoke test makes Host / Join by address
  use Steam's sockets over plain UDP instead of ENet (`Network.DirectOverSteam`), so the whole test
  (lag proxy included) runs through `SteamPeer`. It passes with 1 client, 3 at 150 ms / 2% loss and
  4 at 250 ms / 5% loss. Hosting through the relay with a lobby works on one computer; a friend
  actually connecting through the relay needs a second Steam account, so that's the team's
  playtest. CI keeps testing ENet (no Steam on the build machine).

## Known limitations / tech debt

Things the prototype does on purpose that we'll need to revisit:

- Props sync position every tick even when asleep. Fine for a few dozen props; optimise later
  (sleep-aware sync, lower rate).
- Name labels show peer ids. Steam names need the host to pass each player's name on (not done yet).
- Steam: joining through the relay hasn't been tried with two accounts yet (see "Steam transport
  built"). Invites need the Steam overlay, which may only work when the game is started from Steam.
- Walking into props doesn't push them (clients see frozen copies). Grabbing is the only way to
  move them.
- No crouch or stamina yet.
- Settings have no key rebinding and no graphics presets beyond the 3D resolution scale.
- Player movement is still client-authoritative. With combat, a hacked client could teleport or
  speed-hack away from the evil guy. Fine for co-op; revisit before PvP (player monsters).
- Building the navmesh from CSG prints a Godot warning ("had to parse RenderingServer meshes at
  runtime"). It's harmless for grey-box levels and goes away once levels use real meshes with
  collision shapes (the navmesh already only reads colliders on the World layer).
- The death "pose" is a placeholder (the capsule tips over), and respawn is a fixed 8 s timer.
- Only one enemy type, and it ignores light. Levels have one evil guy each; the pack behaviour (calls)
  needs two or more, which no level has yet.
- The enemy can drop down but never jump up. Drop links are one-way, and the only way up is a real
  ramp or stairs.
- Drop links are generated once per bake. Anything that changes the level at runtime (a door, a
  collapsing floor) will need a re-bake plus fresh links.
- It doesn't plan around props, it just shoves them. A pile of heavy cases can still slow it down,
  though stuck recovery stops it wedging forever.
- The navmesh has walkable islands on the tabletop and on top of each chamber's shell, and gets
  drop links off them. They're unreachable, and the AI filters them out of its wander spots, but
  a bake bounds box (`filter_baking_aabb`) would keep them out of the navmesh altogether.
- `ToonStyle` keeps a toon copy of every material it has ever seen (a few per enemy respawn) and
  syncs them all every frame. Fine for a prototype; prune it if cel shading is adopted.
- The liquid slosh only reacts to linear motion (sliding, throwing, stopping), not to spinning.
- Outline width is in screen pixels, so lines look thinner at higher resolutions. Scale
  `thickness` by resolution if that matters on 1440p / 4K screens.
- The sounds are synthesised placeholders tuned by numbers, not by ear. Expect to retune them,
  or swap in recorded sounds later (keep the `SoundKind` / `EmitSound` API).
- Footsteps make AI noise but no sound yet, and you can't hear teammates breathe.
- World sound RPCs are sent reliably, one per event. Fine for now; batch or throttle them if prop
  chaos ever floods the network.
- Visor effects can't be tested headless. Use the `capture` role (windowed) and look at the
  screenshots.
- The projected HUD's layout assumes the visor shape (it avoids the nose cup at the bottom
  centre). If the visor shape changes, re-check `ui/visor_hud.tscn` anchors.
- Noise carries no source, only a position and a radius. Hearing a crash sends it to the crash,
  not to whoever threw the crate. That's on purpose (it makes distraction work); calls between evil
  guys carry a lead instead.
- The thrower's own flask appears after a network round trip (no client-side prediction).
- Crack direction is a local guess (the nearest evil guy within 4 m), because `Health.Damaged`
  doesn't carry where a hit came from on clients. Send the hit position with the damage if
  ranged attackers or several enemies make the guess wrong.
- Cracks only clear when health is back to full (respawn). Partial healing, if it's ever added,
  should mend some cracks.
- Rebuilding the C# code or reimporting assets from the command line while the Godot editor is
  open can leave the editor running stale state. (The "evil guy never left his spawn" seen on
  2026-09-25 was most likely the async navigation map bug; see "Navigation maps update
  synchronously".) Close Godot fully and reopen the project if things act strangely.
- The evil guy walking through an open door is only checked as "a path exists", not by watching
  him walk it (drop links work the same way and he walks those).
- Doors and buttons have no sounds of their own (both reuse `Impact`), no ticking while a button
  runs down, and doors have no visible frame or track.
- Each peer runs its own door, so a jam can differ slightly between peers for a moment: the
  host's crate is in the doorway before the client's copy of it is. It settles once the crate stops.
- The acid flask can't press a button (it's a projectile, not a `PhysicsProp`).
- The hold-[E] revive (aiming, progress) isn't tested; the test sends `RequestRevive` directly.
  The network test walks and carries by moving the player directly (headless has no mouse capture,
  so the real input path isn't driven).
- The run restarts on its own after the result screen (no lobby, no reward yet).
- Traffic, measured by the network test: each client downloads about 16 KB/s (130 kbit/s) and
  uploads about 2 KB/s, so the host uploads about 16 KB/s per client (a 6-player host: ~80 KB/s).
  Most of it is props, synced 30 times a second even when asleep.
- The network test plays at most 4 clients: two lanes, two rows through the 3 m corridors. 6-player
  sessions (5 clients) aren't tested.
- Telling mental from physical drops on clients relies on the health synchronizer sending `Mental`
  and `Current` in the same update, `Mental` first. If a drop ever mixes both in one update, only
  the physical part counts as a hit, which is right; if they ever arrive in separate updates, a
  mental tick could briefly look like a small physical hit (a tiny crack).
- Mental damage never heals except by respawning, like physical.
