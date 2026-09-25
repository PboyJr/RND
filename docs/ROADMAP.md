# RND: Roadmap

What's done, what's next, and what's parked. Design detail lives in [DESIGN.md](DESIGN.md), and the
reasons behind tech choices live in [DECISIONS.md](DECISIONS.md).

Last updated: 2026-09-25 (real flask and graduated cylinder models; backlog: game modes, psychosis / rat lore, maze as Portal-style test chambers)

---

## Done

### M1: Core prototype ✅ (2026-09-24)

Goal: prove the riskiest tech (networked physics carrying) works in Godot C#.

- [x] Godot 4.7 .NET project, feature-folder layout, Jolt physics
- [x] Host / Join menu over ENet, pause menu, leave session
- [x] Level copied to clients by the host (late joiners supported)
- [x] First-person player: walk / sprint / jump, controlled by its own client, smoothed for others
- [x] REPO-style carrying run by the host: hold to carry, release to fling, RMB throw, scroll
      push / pull; heavy props feel sluggish
- [x] Test level: greybox room, crates, heavy case, specimen jars, ramp and platform
- [x] Grain / cheap-camera post-process
- [x] Headless smoke tests (scenes, host, client), all passing
- [x] Visual Studio launch profile (F5 plays with the debugger attached)
- [x] Docs: DESIGN / DECISIONS / ROADMAP

### M2: Combat basics ✅ (2026-09-24)

Goal: the first archetype (the Scientist) can fight the first entity (the evil guy).

- [x] Hotbar: Hands + archetype kit, keys 1–5 / scroll, cooldown overlay, item visible in hand
- [x] Acid beaker: thrown arc, 2 m splash, 50 damage, 15 s recharge, checked by the host
- [x] Evil guy: grey-box enemy with wander / chase / telegraphed swing, navmesh pathing, respawns
- [x] Health for players and the evil guy, controlled by the host: hit flashes, HUD health bar,
      damage flash, death and respawn
- [x] Smoke tests cover combat offline and over a real host / client connection
- [x] Fix: evil guy now finds its way up ramps and onto platforms instead of getting stuck at edges
- [x] Fix: point-blank beakers no longer pass through an enemy standing against you

### M3: Enemy AI v2 ✅ (2026-09-24)

Goal: the evil guy acts on what it perceives, not what the code knows.

- [x] Vision cone + close-range sense, gradual noticing (suspicion meter)
- [x] Hearing via `Level.EmitNoise`: footsteps (sprint loud, walk quiet), beaker shatters, prop crashes
- [x] Last-known-position + heading, then area search, then give up
- [x] Mood shown in its eyes (calm / suspicious / hunting / about to swing)
- [x] Auto-generated drop-down links off ledges
- [x] Shoves props out of its way; sidesteps and re-plans when wedged
- [x] Smoke checks for every behaviour above

### M4: Gas mask HUD + filter ✅ (2026-09-24)

Goal: you're wearing a gas mask, and it matters.

- [x] Panoramic visor shader: rim, curved glass, edge blur and fringe, breath fog, sway, hit flash
- [x] Cracks are the health bar (procedural shattered glass, clean on respawn)
- [x] HUD projected onto the glass (hotbar, hints, crosshair, filter gauge)
- [x] Unstyled debug health / filter readout outside the mask
- [x] Filter mechanic: drains with effort, choke damage when spent, spare canisters ([E]) that
      can be carried and thrown, and whose hiss enemies hear
- [x] Smoke checks (offline + network) plus a windowed `capture` role for visual checks

### M5: Mask audio ✅ (2026-09-24)

Goal: sound sells the mask as much as the visor does.

- [x] Procedural breathing (valve exhale, filter inhale), synced to the visor fog via one breathing
      clock; louder with effort, wheezing as the filter wears, gasping when choking
- [x] Heartbeat below 50% health; fresh-filter click and hiss
- [x] World muffled through the mask, less so the more it's cracked (bus low-pass)
- [x] World sounds: shatter, impacts, hiss, evil guy growl / snarl / swipe / splat
- [x] `EmitSound`: one event for players and AI (buddies investigate each other's growls)
- [x] `audio` smoke role: renders every sound to .wav and checks for clipping and silence
- [x] Atmosphere pass: room reverb, looping room tone, distant ambience events, more natural
      (resonating-noise) recipes, breathing about 14 dB quieter and more human; mix balance
      checked with live bus meters

### M6: Look dev ✅ (2026-09-24)

- [x] Cel shading prototype: toon ramp lighting + screen-space outlines over all materials, F2 to
      compare (tuned so the dim room stays readable)
- [x] Liquid shader + slosh: level-in-the-world surface, sloshes and settles, fill and glow
- [x] Placeholder Erlenmeyer flask prop, ready for the real model (steps in ART.md)
- [x] Real Erlenmeyer flask model (Blender, built by script in `game/models/src/`), same scene for prop, hand and throw
- [x] Graduated cylinder prop (100 ml, true-to-volume scale marks, neon yellow liquid) on the lab table
- [x] Capture shots: toon off / on, liquid still / mid-slosh; smoke check that liquid sloshes and settles
- [x] Acid beaker → **acid flask**: the Erlenmeyer flask in your hand (and thrown), its acid
      refilling over its recharge (now 5 s), visible to everyone; one shared flask model scene
- [x] Thinner outlines (1 px instead of 2), width / colour adjustable in the Inspector
- [x] Debug view hotkeys F2–F6 (cel shading, outlines, grain, colour crush, lens), defaults in the
      Inspector
- [x] Procedural cracks: every hit fractures the glass on the side it came from (crushed spot,
      spider-web shards, long bending cracks), sized by the hit; smoke-checked in the capture
- [x] Cel shading keeps the room as dark as without it (it was 89% brighter). The capture test
      measures this

---

## Next: pick one

| Option | What it proves | Notes |
| --- | --- | --- |
| **Valuables + round loop** ⭐ recommended | It's a *game*, not a tech demo | Props with value and fragility (damage costs money), sell / extraction point, round timer, end-of-round payout. Works entirely on the current LAN setup. Now there's a threat to survive while doing it. |
| **Combat polish** | Fighting feels good | Acid puddles, flask prediction for the thrower, enemy reacts to thrown props, better death (revive?), sounds. |
| **Steam lobbies + relay** | Friends can play over the internet | Choose Facepunch.Steamworks or GodotSteam. Swap goes in `core/Network.cs`. Needed before any remote playtest. |
| **Proximity voice** | The REPO social magic | Easiest after Steam (Steam Voice). |

---

## Later / backlog

Rough order, and likely to change:

- **Audio v2:** footstep sounds, hearing teammates breathe, room reverb, tuning (or recorded
  replacements) after playtests
- **Filter depth:** timed swaps, contaminated zones, filter types (see DESIGN)
- **Enemy AI v3:** learns your habits, reacts to light, crouch-sneaking, several enemies sharing
  what they notice, lure-with-noise play (see DESIGN)
- **More archetypes + kits** (the hotbar and item system are ready for them)
- **More entity types** (the evil guy is v1)
- **Evidence types:** beyond physical props (photos, recordings, readings?)
- **Selling:** government vs black market, getting-caught fines
- **Between-round hub:** research / upgrade tree (Factorio-ish), crate opening
- **Persistent characters + decay:** needs the "where saves live" decision (see DESIGN)
- **Rebirth / prestige**
- **Player-controlled monsters:** prototype on PC first, then decide on the phone client
- **More game modes** (idea): maze solving, payload delivery, and a Research mode, alongside
  REPO-style retrieval (see DESIGN → Game modes). A maze would be the first thing to prototype,
  since psychosis builds up in it. Maze = Portal-style test chambers. A first prototype could be
  one grey-box chamber: a pressure plate + door, a timed button, an exit trigger and a rat par time.
- **Psychosis + rat lore** (idea): the longer you stay in the maze, the more likely you and your
  friends start to look like rats, which is the truth. Needs rat models (see ART.md) and a
  per-player status effect (see DESIGN → Lore).
- **Art pass:** real mid-poly models, lighting, sound
- **Our own backend:** character persistence, phone relay

## Tech debt

See *Known limitations* in [DECISIONS.md](DECISIONS.md#known-limitations--tech-debt).
