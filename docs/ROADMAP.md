# RND: Roadmap

What's done, what's next, and what's parked. Design detail lives in [DESIGN.md](DESIGN.md), and the
reasons behind tech choices live in [DECISIONS.md](DECISIONS.md).

Last updated: 2026-09-24 (after M2)

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

---

## Next: pick one

| Option | What it proves | Notes |
| --- | --- | --- |
| **Valuables + round loop** ⭐ recommended | It's a *game*, not a tech demo | Props with value and fragility (damage costs money), sell / extraction point, round timer, end-of-round payout. Works entirely on the current LAN setup. Now there's a threat to survive while doing it. |
| **Combat polish** | Fighting feels good | Acid puddles, beaker prediction for the thrower, enemy reacts to thrown props, better death (revive?), sounds. |
| **Steam lobbies + relay** | Friends can play over the internet | Choose Facepunch.Steamworks or GodotSteam. Swap goes in `core/Network.cs`. Needed before any remote playtest. |
| **Proximity voice** | The REPO social magic | Easiest after Steam (Steam Voice). |

---

## Later / backlog

Rough order, and likely to change:

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
- **Art pass:** real mid-poly models, lighting, sound
- **Our own backend:** character persistence, phone relay

## Tech debt

See *Known limitations* in [DECISIONS.md](DECISIONS.md#known-limitations--tech-debt).
