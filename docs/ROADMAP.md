# RND: Roadmap

What's done, what's next, and what's parked. Design detail lives in [DESIGN.md](DESIGN.md), and the
reasons behind tech choices live in [DECISIONS.md](DECISIONS.md).

Last updated: 2026-09-26 (the big push is done; Steam needs a two-account playtest)

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
      Inspector; F7 mutes all sound
- [x] Procedural cracks: every hit fractures the glass on the side it came from (crushed spot,
      spider-web shards, long bending cracks), sized by the hit; smoke-checked in the capture
- [x] Cel shading keeps the room as dark as without it (it was 89% brighter). The capture test
      measures this

### M7: Maze mode (in progress)

- [x] Level picker in the main menu (the host chooses which level to load)
- [x] First grey-box test chamber (`maze/test_chamber.tscn`): a start corridor where you spawn,
      a room, and an exit corridor. The test passes when every living player stands in the exit
- [x] Visor readout: test clock, then "test complete" (no rat comparison: lore stays clues only)
- [x] Mental damage: one health pool, physical cracks the glass, mental corrupts the HUD (text
      scramble, tearing, dropouts, sickly green), narrows vision and rings in your ears. Low gas
      (below 15%) deals it as withdrawal, replacing physical choke damage. Purple debug bar
- [x] Pressure plate (pressed by weight) + sliding door; the test chamber's exit needs the heavy
      case on the plate
- [x] Timed button ([E] or a thrown prop), doors that wait for whatever's in the way (a crate
      jams them), and the heavy case in a closet behind a button door
- [ ] Chain chambers into a maze (REPO-style stitched rooms), difficulty ramp
- [x] The evil guy as a maze "variable" (see M8)

### M8: A maze run: from proof of concept to a game (in progress)

The first complete loop is the **maze run** (chosen 2026-09-25 over REPO retrieval): start → get
through the chambers under pressure → pass or fail → reward → go again.

- [x] **The run:** "Maze run" in the menu starts a run of 3 chambers. Passing one loads the next
      ("TEST 2 OF 3" on the visor), and passing the last passes the run. A result screen follows,
      then a new run
- [x] **Teammate revive:** in chambers you don't respawn. You're down until a teammate holds [E]
      on you for 3 s (back at 50 health, where you fell). **Everyone down at once fails the run**
- [x] **The evil guy in chambers:** released 20 s into each chamber with a growl the whole room
      hears. Shut doors are walls to his pathfinding; open ones he walks through
- [x] **Rewards:** the run pays out (money / XP) into the local profile (`user://profile.json`,
      see DESIGN → How player data is stored), and `RunCount` goes up at run start
- [ ] **More chambers** (content), so a run isn't the same room three times, then the difficulty
      ramp and the party-level starting difficulty (**in progress**, started 2026-09-25)
  - [x] Two new chambers, three in total, easiest to hardest: **Crates** (four crates hold the
        exit plate; a jar or three crates don't), the first chamber (case in the button closet),
        and **Ledge** (the exit needs its plate held *and* a button on a 3.5 m ledge, pressed by
        lobbing a jar at it; then 5 s to get through, or jam the door)
  - [x] Difficulty ramp: a run climbs the chamber list from easiest to hardest
  - [x] Smoke test: every chamber spawns you, the evil guy can reach the start, the exit is shut;
        Crates and Ledge are solved in the test (Ledge with a real-speed jar throw)
  - [ ] Playtest the new chambers (is the ledge button easy enough to spot and hit?)
  - [x] More chambers per difficulty, so runs vary (generated chambers, half of each run)
  - [x] Starting difficulty from the party's level (0.7 × average + 0.3 × highest, clamped by the host)
- [ ] Between runs: somewhere to spend the reward (upgrades / research tree), instead of an
      automatic restart

### The big push (started 2026-09-25)

The team asked for "all of it": the hurdles we hadn't tackled yet, worked through in this order.

1. [x] **Network hardening** (details below)
2. [x] **A build anyone can run:** Windows export preset, CI (build, both smoke tests, Windows
       build to download on every push), settings menu (sensitivity, field of view, three
       volumes, fullscreen, v-sync, 3D resolution) saved to disk. CI is green and its Windows build
       downloads from the run page; a local export works too. (The very first CI run crashed once in
       the offline test and the rerun passed; failures now show the log's end on the run page.)
3. [x] **Rewards and saving:** a run pays money and XP into each player's own profile (saved safely, levels up); the party's level sets the starting difficulty. *Still to do:* something to spend money on
4. [x] **Enemy AI v3:** the pack converges on a spotter's growl, waits at shut doors (and comes
       through when they open), learns where players keep getting away. The hardest generated
       chambers release two evil guys, so the pack forms
5. [x] **Generated chambers:** a seeded generator builds chambers from the puzzle pieces (plates,
       closet, ledge button, second plate, heavier plate, a second evil guy) to a difficulty budget;
       every peer builds the same room from the seed. Half of a run's chambers are generated. 90 of 90
       swept chambers solve from their plan. (Found and fixed: async navigation map updates dropping a
       new level's navmesh.)
6. [x] **Steam lobbies and relay:** "Host on Steam" opens a friends-only lobby; friends join from
       their friends list or an invite. The whole network test passes over the Steam transport
       (`--steam=1`). *Still to do:* a two-computer, two-account playtest of joining through the relay

### M9: Network hardening ✅ (2026-09-25)

Goal: the game holds up over a real internet connection, with a full party, people joining late
and leaving.

- [x] One-command network test (`--role=network`): the host starts up to 4 clients (the last
      joins mid-run) and they play the sandbox and a whole maze run like players would, optionally
      through a fake bad connection (`--ping`, `--jitter`, `--loss`)
- [x] Passes with 4 clients at 250 ms / 5% loss, 3 at 150 ms / 2% loss, and 1–2 with no lag
- [x] Carried props are simulated by the carrier: they trail 0.5 m instead of 2–3 m at 150 ms, and
      land where the carrier dropped them
- [x] Player state goes through the host; clients never talk to each other (no engine errors on
      level changes, late joins, or several people leaving at once)
- [x] Level changes wait for clients (fixed a skipped chamber)
- [x] Leavers are let go at once; dropped connections after 6 s instead of 30
- [x] Late joiners see the right chamber clock, props and doors
- [x] Spawn points handed out by the host (no two players on one spot)
- [x] Reach checks use where the player really is, so lag doesn't refuse throws, grabs or revives
- [x] Players pass through each other; downed players lie down (and still weigh down a plate)

---

## Next: pick one (after the big push)

| Option | What it proves | Notes |
| --- | --- | --- |
| **Playtest what's there** ⭐ | Whether it's fun | Nothing in the big push has been played by a person: the chambers (can you spot and hit the ledge button?), generated rooms, the pack of evil guys, carrying online. Now possible over the internet with Steam, which also tests joining through the relay. Cheap, and it should steer everything below. |
| **Somewhere to spend the pay** | The loop closes: runs → money → upgrades → better runs | The between-run hub / research tree (DESIGN: Factorio-style). Needs the team to decide what the upgrades are. |
| **Valuables + retrieval round** | The second mode, the one most like REPO | Research props with value and fragility, a hand-in point, a round timer. |
| **Proximity voice** | The REPO social magic | Easiest after Steam (Steam Voice). |
| **Combat polish** | Fighting feels good | Acid puddles, flask prediction for the thrower, the evil guy reacting to thrown props, sounds. |

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
- **Selling** (`Open` whether it stays, since the pitch is now research and testing): government
  vs black market, getting-caught fines
- **Between-round hub:** research / upgrade tree (Factorio-ish), crate opening
- **Persistent characters + decay:** storage is designed (local profile JSON, host-clamped, decay
  in runs; see DESIGN). Still needs the abilities and the decay curve before it's worth building.
- **Rebirth / prestige**
- **Player-controlled monsters:** prototype on PC first, then decide on the phone client
- **More game modes** (idea): maze solving, payload delivery, and a Research mode, alongside
  REPO-style retrieval (see DESIGN → Game modes). A maze would be the first thing to prototype,
  since psychosis builds up in it. Maze = Portal-style test chambers. A first prototype could be
  one grey-box chamber: a pressure plate + door, a timed button, and an exit trigger.
- **Psychosis + rat lore** (idea): the longer you stay in the maze, the more likely you and your
  friends start to look like rats, which is the truth. Needs rat models (see ART.md) and a
  per-player status effect (see DESIGN → Lore).
- **Art pass:** real mid-poly models, lighting, sound
- **Our own backend:** character persistence, phone relay

## Tech debt

See *Known limitations* in [DECISIONS.md](DECISIONS.md#known-limitations--tech-debt).
