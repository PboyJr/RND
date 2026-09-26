# RND: Design

This doc changes as we go. Ideas are cheap, so each one is tagged with a status to show what's
actually decided.

**Status tags:** `Decided` · `Leaning` (probably, not locked) · `Idea` (on the table) · `Open` (needs an answer)

Last updated: 2026-09-25

---

## Guiding principle (`Decided`)

**Make everything as intelligent and dynamic as possible.** Entities, systems and the world should
react to what's actually happening instead of following fixed scripts. Enemies find their way to
you, adapt when you move, and don't take shortcuts that only look smart. When choosing between a
simple scripted behaviour and a reactive one, lean reactive, and fix the rough edges rather than
falling back to the script.

## Pitch

Co-op horror in the vein of R.E.P.O. You're a team of scientists sent into places where unknown
entities live to **research and test** them: collect data and specimens, run tests, and get out
alive.

(Changed 2026-09-25: this replaces "sell what you found to the government or the black market".
`Open`: whether selling survives in some form, and what research earns you. See
[Selling](#selling-open).)

Working title: **RND** (repo name). The first notes were titled "1" (in Comic Sans, muahahaha).

## Lore (`Idea`, 2026-09-25)

- **The scientists are actually rats.** Each one is a rat with a human brain uploaded into it.
- **Why the modes differ:** the real scientists are testing whether humans inside rat bodies are
  smarter than actual rats, so they put them in different environments (see
  [Game modes](#game-modes-idea-2026-09-25)).
- **Psychosis** (working name): the longer you stay in the maze, the more likely you are to get a
  status effect that makes you and your friends **look like rats**. It feels like a
  hallucination, but it's actually the truth.
- **Clues, not exposition** (`Decided`, 2026-09-25): the lore is never spoonfed. No text, HUD or
  announcer states the twist. Players piece it together from clues they notice (the huge
  observation window, psychosis, odd details in chambers) and have to think about what they mean.
- **The mask is the lie** (`Idea`, 2026-09-25, brainstorm): the "filter" doesn't clean the air.
  It works more like a scuba regulator, feeding you a gas that keeps you sane: it keeps up the
  illusion that you and your friends are humans. The player is never told; the clues:
  - **The hiss goes the wrong way.** A fresh canister already hisses when it seats: it's
    pressurised, pushing gas *in*. Canisters could have no intake holes, just a valve.
  - **Running low lets the truth in.** Low filter = psychosis creeping in: a teammate flickers
    into a rat for a frame, door frames look too tall, voices squeak at the edges. It reads as a
    scary glitch, not an explanation. This merges the filter and psychosis into one system.
  - **Swapping is a moment of truth.** If swapping takes time (already an idea), the few seconds
    with the canister off are when you see the most.
  - **The maze rations the gas.** Fewer spares in the maze (or something in the vents that
    cancels the dose) is why psychosis builds up there.
  - **The truth is useful** (risk / reward): running low could show what only a rat would notice,
    like scent trails, rat-sized gaps and vents, so players might *choose* to let it run down.
  - Keeps the name "filter" on the HUD and canisters. The lie is in the labelling.
  - **How it runs out** (`Leaning`, 2026-09-25):
    - **Below 15% gas you start taking very low mental damage** (withdrawal; in the prototype):
      0.5 HP/s just under 15%, rising to 1.5 HP/s once the filter's spent, in 1 s ticks.
    - **Running out fully doesn't flip you straight to the truth.** Psychosis takes a while to set
      in (*suggestion:* 20–30 s, getting stronger), so there's a window to find a spare.
    - **It's per player and desyncs** (`Leaning`): you might see your friend as a rat while they
      still see you as a human. What you see depends on your own gas, not theirs.
  - **Mental vs physical damage** (`Leaning`, in the prototype 2026-09-25): one health pool, and
    each hit is physical or mental. Physical cracks the glass and flashes your body. Mental leaves
    the glass alone and corrupts the projected HUD instead (eased in: barely there at low mental
    damage, strong near the end): text scrambles, bands of it tear
    sideways, it browns out (sags dim for a second or so and recovers), and the phosphor green goes
    sickly. **Past 50% mental, the HUD fades away** (gone by ~90%) and turns into a heavy vignette:
    a failing mind loses information. **The cracks turn to blood** as it gets worse (from ~20%, full
    by ~90%): the same impacts, in the same places and sizes, become splatters with droplets and
    drips, the hairlines running dark red. A clue, not an explanation: whose blood, and was the glass
    ever cracked? Open: losing the crosshair with the HUD makes grabbing hard; keep it? Your vision also
    narrows a little and a thin tinnitus ring comes in. So "how hurt am I" is cracks plus HUD
    corruption. Withdrawal is the only mental damage so far. **Mental damage can't kill you**: it stops
    at 25 health, like poison in Minecraft, so you can live with a lost mind (and see it). How far gone
    you are (the purple debug bar, and what drives the effects) is measured against what's left to
    lose down to that floor, so everything is at full strength when you reach it. Physical hits can
    still finish you. **A fresh filter stops withdrawal but
    heals nothing:** health and sanity only come back on respawn for now (Open: healing, later). A purple debug bar shows how much of
    the lost health is mental.
  - `Open`: is everything you see while low real, or is some of it hallucination to muddy the
    clues? What is the evil guy when you see the truth? (Not deciding yet.)
- **Why you keep upgrades when you die** (`Leaning`, 2026-09-25): you're the real scientist,
  sitting outside the test, with your brain uploaded into a rat. When the rat dies, *you* don't,
  so you keep (some of) your upgrades. That's the difference from REPO, where dying and the round
  ending means starting over. `Idea`: what's lost is what the rat had (gear and loot it was
  carrying, maybe per-round buffs); what's kept is what's in your head (the research tree,
  upgrades, character level). This also fits persistent characters.
- `Open`: who are the "entities" to a rat? (The pitch is now scientists doing research and
  testing, which fits the lore better than selling did.)
- `Open`: how psychosis builds up (time in the maze only, or other triggers too?), what it does
  besides the rat visuals, and how you get rid of it.

## Inspiration

- **R.E.P.O.**: physics-based carrying of fragile loot, co-op chaos, proximity voice, levels
  stitched together from hand-built rooms
- **Portal / Portal 2**: test chambers, physics puzzles, co-op puzzles (Portal 2), a watching
  experimenter on the PA (the maze mode)
- **Phasmophobia**: rebirth / prestige
- **Factorio**: research tree for upgrades
- **Crate / chest opening** after rounds

## Visual style (`Leaning`)

- 3D, mid-poly, static / grainy "cheap camera" look.
- **Cel shading (`Leaning`, prototype in; F2 toggles it to compare):** light falls into three
  hard bands (deep shadow, mid, lit) from an editable colour ramp, with ink outlines on every
  edge (thin, 1 px by default; the width is adjustable). It reads like a dark comic: hard-edged
  warm light pools on the walls, crisp props, and the evil guy as a silhouette with glowing eyes.
  It's done in code over ordinary materials, so it applies to everything including grey-box (see
  [ART.md](ART.md)). It keeps the room exactly as dark as without it, and only turns smooth
  light into hard-edged bands (the screenshot test checks this). Shadows stay scary.
- **Liquids** (flasks, specimen jars): glowing liquid that stays level in the world, sloshes when
  you move or throw the container, and settles. Fill level is controllable: the acid flask in
  your hand refills as it recharges.
- The prototype already has a grain pass: film grain, a slightly crushed palette, colour fringing
  and a vignette (part of `game/vfx/visor.gdshader`). UI draws above it and stays crisp.
  `Open`: grain and colour crush look good on the smooth look but choppy over cel shading (the
  user's take). F4 / F5 switch them off to compare. If cel shading stays, maybe it drops them.

## Core loop (`Leaning`)

1. Deploy into a site
2. Research and test (collect data and specimens, run tests) while dealing with entities
3. Extract
4. Hand in the research (`Open`: what it earns, and whether selling is part of it)
5. Between rounds: upgrades, research tree, crate opening

This is the loop for REPO-style retrieval. That may be just one of several modes (below).

## Game modes (`Idea`, 2026-09-25)

The game could have multiple modes, with REPO-style retrieval as one of them:

**The maze is built first** (`Decided`, 2026-09-25): it's the first mode that becomes a whole
game loop, a **run** of chambers that you pass or fail (see ROADMAP M8). Retrieval comes after.

1. **REPO-style retrieval**: the core loop above.
2. **Maze solving**
3. **Payload delivery**
4. **Research**: like "that Factory Roblox game".

In the lore, the modes are different test environments, built to check whether humans in rat
bodies beat real rats (see [Lore](#lore-idea-2026-09-25)).

- `Open`: what payload delivery and research play like, round to round (maze: see below).

### The maze: Portal-style test chambers (`Idea`, 2026-09-25)

Very Portal 1/2. A **maze** is a run of **test chambers** joined by short corridors or lifts.
Each chamber is a physics puzzle: get the team from the entry door to the exit door.

- **Look:** clean, clinical test chambers (white and grey panels, bright even light, numbered
  signs) in contrast to the dark retrieval sites. An **observation window** high on the wall,
  where the real scientists watch. Seen from a rat's size, the window and the figures behind it
  are huge, a quiet hint at the truth (see [Lore](#lore-idea-2026-09-25)).
- **Puzzle pieces, built from what we already have:**
  - **Pressure plates** held down by props (the heavy case is our weighted cube) or by standing
    on them. Doors stay open only while the plate is held. **In the prototype** (2026-09-25): a
    plate is pressed by **weight**, at least 30 (a player counts as 60, props by mass, carried
    props not at all). So a player or the heavy case holds it, a jar doesn't, and a pile of four
    crates does. A door can need several plates at once. The first chamber's exit door has one plate,
    and since everyone has to reach the exit, someone has to bring the case.
  - **Buttons** that open a door for a few seconds, so someone has to run or throw. **In the
    prototype** (2026-09-25): a button on a pedestal, pressed with [E] or by **hitting it with a
    thrown prop**. It stays down for 5 s, and its glow fades from blue to orange as time runs
    out. Pressing it again restarts the clock. A door can open on *any* of its switches (a button
    on each side) or need *all* of them (plates).
  - **Doors never close on anyone:** a player or prop in the doorway stops the door, so you can't
    get crushed, and **a crate can jam a door open** (on purpose: improvising is the point).
    In the first chamber, the heavy case sits in a closet behind a button door, with a second
    button inside so you can't be shut in.
  - **Co-op locks** (Portal 2 co-op): two plates at once, a player holding a door while another
    passes, throwing a filter or crate across a gap to a teammate.
  - **The acid flask** melts certain panels, grates or seals: a Scientist-kit puzzle piece.
  - **Sound locks:** a microphone that opens a door on a loud enough sound, or an alarm that
    releases something if you're too loud. It reuses `EmitSound`, so noise is both tool and risk.
- **Pressure:** the filter keeps draining, so a slow chamber costs air; spare filters are placed
  in chambers as rewards. **Psychosis** builds the longer you're in the maze.
- **The experimenters talk** (GLaDOS-ish): short, clinical PA announcements, maybe shown on the
  visor, that get stranger as psychosis sets in. They never say what you are.
- **No "beat the rat" readout** (`Decided`, 2026-09-25): the visor only shows the test clock and
  "test complete". Comparing you to rats would give the twist away (see Lore → clues, not
  exposition). Any rat benchmark stays hidden, or shows up only as a clue (a chart on a wall
  with an unlabelled "control" line, say).
- **Intelligent and dynamic:** the experimenters **adapt the test**. Solve too fast and the next
  chamber gets harder (fewer props, a monster released); struggle and they "help" in
  condescending ways. Later: chambers assembled from modules instead of hand-built.
- **No portals for now** (`Leaning`). A portal gun is the most Portal thing, but it's a big tech
  and design job and would crowd out the carry-physics puzzles. Maybe later.
- **Monsters** (`Leaning`): yes, but only the one evil guy for now, released as a "variable" the
  experimenters add. **In the prototype** (2026-09-25): he's released **20 s into every chamber**
  with a growl the whole room hears, so dawdling on the puzzle costs you. A shut door is a wall
  to him; he goes through open ones, so shutting a door on him is a tactic. `Idea`: tie the
  delay to how the team is doing (fast team, earlier release), per "the experimenters adapt the
  test".
- **Layout: REPO-style stitched rooms** (`Leaning`). Chambers are hand-built rooms with doorway
  connectors, and each round stitches a set of them together into a maze. Every room is tagged
  with a difficulty, and the generator picks from the pool.
  - **In the prototype** (2026-09-25): chambers stay separate rooms with a "lift" between them,
    and **half of a run's chambers are generated** instead of hand-built. The generator makes a
    fresh room each time from the pieces above, spending a difficulty budget: an easy one is a
    plate and some crates; a hard one might hide the case in a closet, want a jar lobbed onto a
    ledge button, need two plates held at once, and release two evil guys early. Every generated
    chamber is checked solvable in the tests. `Open`: keep a mix, or go all-generated once there are
    more kinds of piece? More pieces to add: sound locks, acid-melted panels, co-op holds.
- **Difficulty ramps through the round** (`Leaning`): the further in you get, the more rooms and
  the harder the rooms. **In the prototype** (2026-09-25): three chambers, and a 3-chamber run
  plays them easiest to hardest:
  1. **Crates:** the exit plate, and no heavy case. Four crates side by side hold it; a jar or three
     crates don't. Teaches weight.
  2. **The first chamber:** the heavy case is in a closet behind a button door.
  3. **Ledge:** the exit needs its plate held (the case) **and** a button on a 3.5 m ledge, out of
     reach from the floor. You lob a jar at it (only jars fly far enough; crates are too heavy
     to throw that high), which opens the exit for 5 s: get everyone through, or jam the door
     with a crate. Standing on the case and jumping might also reach it, but then the case isn't
     on the plate.
  - `Open`: is the ledge button easy enough to spot from the floor and hit? Playtest it.
- **Starting difficulty from the party's level** (`Leaning`): you don't start from level 1 each
  round. Before generating, look at the levels of everyone joining and set the difficulty to
  **`0.7 × average + 0.3 × highest`**. A plain average would let a strong player get carried, or
  a weak one get crushed, and could be gamed by inviting low-level alts; weighting toward the
  highest level limits that.
  - `Idea`: scale rewards so a low-level player in a strong party earns more (they're risking more).
  - Lore fit: the experimenters calibrate the test to the subjects they were given.
  - `Open`: what "level" is (character level, rebirths, upgrades owned?), and whether it's
    recomputed when someone joins mid-round.
  - **In the prototype** (2026-09-25): "level" is the character's level. Level 1 starts a run at
    the easiest chamber; a party at level 20 or more starts halfway up the list. Runs always end at
    the hardest. It's worked out when a run starts, so someone joining mid-run doesn't change it.
- **What a run pays** (`Leaning`, numbers to tune, 2026-09-25): **100 money and 50 XP for every
  chamber passed**, even if the run then fails, **plus 200 money and 100 XP for passing the whole
  run**. Everyone in the run gets the same (late joiners too). XP levels your character up (each
  level takes 100 × its number in XP: 100 to reach level 2, 200 more for level 3). Money goes to
  the scientist (the profile), not the rat, so every character shares it. The result screen shows
  what you earned; nothing spends it yet.
  - `Idea` (from above): pay a low-level player in a strong party more.
  - `Open`: should faster chambers pay more (the clock is already there)?
- `Open`: how many rooms per maze, and does failing (the whole team dying, running out of
  filters) end it? The upload lore says dying costs you the rat, not your progress.
- `Open`: which Roblox factory game is the reference for Research mode? And how does that mode
  relate to the Factorio-style research tree between rounds?
- `Open`: are the modes picked per round, per run, or voted on? Do they share upgrades and
  characters?

---

## Mechanics

### Collecting research (`Idea`)

- Physical props carried REPO-style (the prototype has crates, a heavy case and specimen jars).
- `Open`: do props have value and fragility, losing money when they hit something too hard like in REPO?
- `Open`: is there also non-physical evidence (photos, recordings, readings, data)?

### Selling (`Open`)

The pitch dropped selling on 2026-09-25 (it's now research and testing). Undecided whether
selling stays at all. The earlier idea, if it does:

- Government vs black market: the black market pays more, but you can get caught. The penalty is a **$ fine**.
- The goal is a risk/reward "one more sale" loop.
- `Open`: how is getting caught decided? Flat chance, a heat meter that builds with repeated
  sales, or something tied to what you're selling?
- `Open`: who pays the fine: the team pool or the seller?
- `Open`: can the consequences reach into rounds (raids, agents showing up)?

### Upgrades outside rounds (`Idea`)

- Upgrades happen **outside** rounds, never mid-round.
- Research knowledge tree, "think Factorio".

### Crate / chest opening after rounds (`Idea`)

### Building (`Idea`, low confidence: "eh?")

### Rebirth (`Idea`)

- Phasmophobia-style prestige.

### Persistent characters with decay (`Leaning`, a core difference from REPO)

- Characters are saved across sessions and instances (REPO doesn't do this).
- **The catch:** a character's abilities (TBD) deteriorate the longer the character goes unused.
- Things to settle:
  - **Where saves live.** Decided: a local file, clamped by the host (see below).
  - **What "time" means.** Decided: **runs played on other characters**, not real time (real
    time can be cheated with the system clock, and it punishes people who take a break). It
    creates a roster / bench dynamic: you juggle characters, and benched ones get rusty. It pairs
    well with rebirth.
  - `Open`: which abilities, what the decay curve looks like, whether there's a floor, and
    whether decay can be trained back.

#### How player data is stored (`Decided` 2026-09-25, numbers still open)

- **What's saved follows the upload lore:** only what's "in your head" is saved (character level,
  rebirths, research tree, upgrades, ability levels). What the rat had (gear, loot, per-round
  buffs) lives in the round and is never written to disk. That makes "what do you lose on death"
  a data question: if it's not in the save, you lose it.
- **One profile per player, holding a roster of characters.** Money and the research tree are
  **per profile**: the scientist keeps them, whichever rat they're in. Roughly:
  `Profile { Version, RunCount, Money, Research, Characters[] }` and
  `Character { Id, Name, Archetype, Level, Rebirths, Upgrades, Abilities, LastRun }`.
  - `Open`: are upgrades per character (as above) or per profile like the research tree?
- **A run counts when a round starts.** `RunCount` goes up and is saved at round start, so
  quitting a round can't dodge decay on your other characters.
- **Decay is computed, never stored.** Each character has `LastRun`. Rust = `RunCount − LastRun`
  (roughly, runs played on other characters), and each ability's effective value = its trained
  value run through the decay curve. Nothing ticks and nothing drifts; the floor and the curve
  shape are just parameters of that function, so they can be tuned without migrating saves.
  - Consequence: a one-character player never decays. That fits "benched characters get rusty".
- **Retraining is gradual.** Each run on a character moves its `LastRun` forward by `K` runs
  (capped at `RunCount`), so a rusty character sheds rust over a few runs instead of snapping
  back. `K` is a tuning number (`Open`, 3 to start).
- **Where it lives: a local JSON file** (`user://profile.json`) for now. On join, the client
  sends its active character's summary to the host. The host **clamps** it (level and upgrades
  within what's possible) but otherwise trusts it. Same stance as client-authoritative movement:
  cheating in co-op mostly cheats yourself. The one place it touches others is the maze
  difficulty formula (`0.7 × average + 0.3 × highest`), where a faked level makes the room
  harder for everyone, hence the clamp. **Later**, our backend can hold the same record keyed by
  Steam id; the format doesn't change, only who writes it. (Steam Cloud alone doesn't help: it
  syncs the file, but the file is still editable.)
- **Who writes results:** the host decides a round's outcome (it's host-authoritative anyway)
  and sends each player their results (XP, money, research) by RPC. The player's own game
  applies them and saves. With a backend, the host reports results to the server instead.

### Player archetypes (`Leaning`)

Players are scientists. Each archetype brings a different kit on the hotbar.

#### The Scientist (first archetype, in the prototype)

- The "main scientist guy". Kit: **acid flask**.
- **Acid flask** (`Decided` for the prototype, numbers still being tuned): an Erlenmeyer flask
  of glowing purple acid (colour is `Color` on `Liquid` in `models/erlenmeyer_flask.tscn`).
  - Equip it on the hotbar and press LMB to lob it on an arc. It shatters on the first surface,
    prop or entity it touches and splashes acid in a **2 m radius**.
  - **50 damage** on a direct hit, dropping to 50% at the splash edge.
  - Recharges for **5 s** after each throw (was 15 s). There's no ammo; it just refills. **The
    recharge shows on the flask itself:** after a throw, the flask in your hand is empty, and its
    acid rises back up over the 5 s. Everyone looking at you sees it too.
  - No friendly fire for now (the splash skips players). `Open`: should it hurt teammates?
  - `Idea`: leave an acid puddle that damages over time, for area denial and kiting.
- `Open`: other archetypes and their kits.

### Hotbar (`Decided` for the prototype)

- Slot 1 is always **Hands** (grab / carry / throw props). Slots 2–5 hold the archetype's kit.
- Keys **1–5** select a slot. **Scroll** cycles slots, except while carrying a prop, when it
  pushes / pulls it.
- **LMB uses whatever is selected** (grab with Hands, throw the flask, and so on).
- Each slot shows its recharge time. Everyone can see what you're holding (the flask is in your
  hand, its acid refilling while it recharges).

### Health, damage, death (`Leaning`)

- Players: **100 HP**.
- **In the maze: teammate revive** (`Decided`, 2026-09-25). When you die you're down, not
  respawning. A teammate holds [E] on you for 3 s and you're back at **50 health**, where you
  fell, with the filter you had. **If everyone is down at once, the run fails.** Downed players
  who get left behind come back when the next chamber loads (every chamber starts everyone
  fresh, for now). `Open`: should health and filter carry over between chambers (attrition)?
- **In the test level** (the sandbox): still the placeholder, **respawn after 8 s** at full
  health, with a fresh mask and filter.
- `Open`: does death cost money or research? (The upload lore suggests death loses the rat's
  stuff but not your upgrades, see Lore.)
- Hits flash your body for everyone else. For you, they crack your visor (see below).
- **A downed player lies on the floor and still weighs something** (2026-09-25): lying on a
  pressure plate, they hold it down. `Idea`: lean into it (dragging a downed teammate onto a plate
  to hold a door is grim, funny, and very "they're just test subjects").

### Players and each other (`Leaning`, 2026-09-25)

- **You walk through your teammates.** Over the internet everyone sees the others a moment late,
  so bumping into each other means bumping into where they *were*, which feels broken (and in
  testing pushed a player through the floor). Teammates still count for plates, exits, doors and
  enemies.
- `Idea`: body-blocking or standing on a teammate's head (boosting up to a ledge) as a deliberate
  mechanic later, if a puzzle wants it.
- **What you carry feels instant**, whatever your ping: your own game moves it, and when you let
  go it lands where you saw it land.

### The gas mask HUD (`Decided`)

Everything you see is through a **panoramic full-face gas mask**, like a modern respirator, not
twin lenses. Twin-lens tunnel vision would fight the evil guy's vision cone, since you need to see
it coming from the sides.

- **The cracks are your health bar.** There's no health number on screen. **Every hit fractures
  the glass where it came from** (the evil guy swiping from your left cracks the left side; from
  behind, the rim), sized by how hard it hit, with a new random pattern each time: a crushed,
  frosted spot, spider-web shards around it, and long cracks racing outward that bend, kink and
  catch the light. Each shard shows the world slightly askew. The weaker the mask, the further
  every crack runs, so at low health you're looking through shattered glass. Small knocks
  (choking) spread the latest crack instead of starting a new one. Respawning gives you a clean
  mask.
- **Breath fog** pools low around your mouth and pulses with each exhale. Breathing speeds up
  and fogs more when you sprint, and stays heavy for a few seconds after. A tired filter leaves
  the mask clammier. (Halved 2026-09-25: it was too much.)
- **The mask sways**: the rim lags a little behind where you look and jolts when you're hit.
- **The HUD is projected onto the glass** in phosphor green (hotbar, hints, crosshair, filter
  gauge), so it curves, glows, flickers and gets split by cracks along with the visor.
- **Nothing on the visor flashes on and off** (2026-09-25). Warnings ease instead: the filter gauge
  fades as it runs low (the spent warning is back at full strength), mental damage browns the HUD
  out, and running out of gas closes in a steady vignette.
- **HUD in the black rim** (`Idea`, 2026-09-25): move HUD elements off the glass and onto the
  rubber rim, like readouts built into the mask frame. Would stay code-driven (the rim is
  procedural and sways), not PNG overlays. Open: how much rim there is varies with aspect ratio
  (almost none at the sides on 4:3), so the corners and around the nose cup are the reliable spots;
  and should rim readouts keep the projected-glass effects or look like physical parts (dim LEDs)?
- Outside the mask, crisp and **deliberately unstyled**: a debug health bar (Godot's default
  progress bar, showing %), a purple debug bar for mental damage and a filter readout. Remove these once the cracks read well in
  playtests.

### Mask audio (`Decided` for the prototype, sounds still tuning by ear)

Half the gas mask illusion is sound. Everything is generated in code, so there are no audio files.

- **Atmosphere first.** The room should feel present before anything happens:
  - **Room tone:** a slow low rumble, a faint ventilation hum and duct air, looping seamlessly.
  - **Distant events:** a creak, drip, far-off boom or vent rattle every 4–12 s, somewhere 7–18 m
    around you. Atmosphere only, since enemies don't react to them.
  - **Reverb:** a concrete-room echo on every world sound, heard *through* the mask, so the
    echoes come through dull and distant too.
  - **Balance:** calm breathing is barely there, about 15 dB under the room tone (2026-09-25).
    You don't notice it until the room goes quiet and you actually listen, and then it's there. Your body only takes over when
    things go wrong (choking, heartbeat).
- **You hear yourself breathe, and it matches the fog.** A warm, low "hoo" out through the exhale
  valve, a breathier draw in through the filter, a faint rubber valve flap at each turn of breath,
  and the boxy ring of air trapped in a mask. It shares one breathing rhythm with the visor, so
  every fog puff lands on an exhale you hear.
  - Faster and louder with effort, and stays heavy after a sprint.
  - **Wheezes and whistles as the filter wears out**, so you can *hear* it running down.
  - **Out of gas, you breathe less, not more** (2026-09-25): like an empty scuba tank. Slow,
    uneven breaths (about one every 3 s, never evenly spaced), each a strained pull against a valve
    that gives nothing, with a short weak exhale and the air coming in catches.
- **Heartbeat** kicks in, clearly audible, the moment you drop below 50% health: a lub-dub that speeds up as you get closer to death.
- **Fresh filter:** a click as it seats, then a rush of air (inside your mask), plus a hiss nearby
  enemies can hear.
- **The world is muffled by your mask**, and **cracks let sound in**: an intact mask muffles
  heavily, a shattered one barely at all. Your health changes how the world sounds.
- **World sounds:** flask shatters, crates crashing (louder the harder and heavier), filter
  hiss, and the evil guy's growl when it spots you, a snarl on the windup (**you can hear a swing
  coming**), the whoosh of the swing, and a wet splat when it dies.
- **What you hear, it hears.** Every audible world sound is also a noise event at the same radius.
  Other evil guys come to investigate a buddy's growl, which gives primitive pack behaviour.
- `Idea`: hearing teammates breathe through their masks (proximity breathing); footstep sounds;
  a flatline drone on death; muffling that changes with filter state; room reverb.

### The filter (`Decided` for the prototype, numbers still tuning)

The mask's filter runs out, and that's a core pressure mechanic. (`Idea`: it isn't really a
filter, see Lore → "The mask is the lie".)

- A fresh filter lasts **180 s** of calm breathing. It drains at **0.6×** standing still and up
  to **2.5×** sprinting flat out, so **sprinting burns air**.
- **Low filter = withdrawal:** below 15% you take mental damage (0.5 → 1.5 HP/s as it runs out),
  which corrupts the HUD instead of cracking the glass (see Lore → "The mask is the lie").
  **Spent filter:** on top of that your breathing goes slow and ragged, a steady vignette closes in
  (no pulsing) with a red tint, and the fog goes
  thick. (Was 4 physical damage every 0.5 s until 2026-09-25.)
- **Spare filter canisters** are physical props. Carry them, throw one to a teammate, press
  **[E]** on one (or while holding it) to screw it on. A fresh filter **hisses**, which the evil
  guy can hear within 4 m.
- The test level has 3 spares: near spawn, on the table, up on the platform.
- `Idea`: swapping takes time (you're blind and vulnerable while the filter's off); contaminated
  zones that drain filters faster; better and worse filter types; filters as sellable loot or
  something bought between rounds.

### Entities / monsters (`Idea`)

- Entities can be **NPCs or player-controlled**.

#### The evil guy (first NPC entity, in the prototype)

- Placeholder look: a tall dark capsule with arms and glowing red eyes. **120 HP**.
- Wanders near where it spawned.
- Chase speed **4.3 m/s**: faster than walking (4), slower than sprinting (6.5), so you can
  kite it.

**AI v2: it only knows what it perceives** (`Decided` for the prototype, numbers still tuning):

- **Sight:** a **110° vision cone, 14 m** long, blocked by walls, plus an all-round sense within
  **2.5 m**. Spotting you isn't instant: it takes about 0.3 s up close and about 2 s at 14 m. A
  glimpse makes it **suspicious** (it comes over to look), and full certainty makes it **hunt**.
  You can slip past behind it or far away, but not under its nose.
- **Hearing:** sounds carry a radius, and walls halve it.

  | Sound | Radius |
  | --- | --- |
  | Walking | 3 m |
  | **Sprinting** | **10 m** |
  | Flask shattering | 14 m |
  | Props crashing | 3–18 m (harder and heavier = louder) |

  A noise makes it come and investigate, and a close noise puts it on edge. So **sneaking means
  walking**, and throwing a flask is loud (useful as a distraction?).
- **Memory, not omniscience:** lose line of sight for 0.75 s and it stops tracking you. It heads
  to where it **last saw you, plus 1 s of where you were going**, looks around, checks a few spots
  nearby for up to 12 s, then gives up and calms down.
- **Mood shows in its eyes:** dim red when calm, **orange** when suspicious or searching,
  **bright red** when hunting, and a flare right before a swing.
- **Movement smarts:** it drops off ledges when that's the quicker way to you (links are
  generated from the map automatically). It **shoves props** out of its way (light crates go
  flying and make noise; the heavy case barely moves). If it gets wedged, it sidesteps and
  re-plans.
- **Attack:** its eyes flare as a warning (0.45 s), then it swings for **20 damage** if you're
  still within reach, and pauses for 1 s afterwards.
- Getting hit makes it go after whoever hit it.
- **Finds its way to you.** It always follows a real path: around obstacles, up ramps, onto
  platforms. It only attacks players on roughly its own level (within 0.75 m of height), so
  standing on a platform doesn't just get you hit from below. It walks round and comes up.
  If you're somewhere it genuinely can't reach, it waits below and watches you.
- Respawns **12 s** after dying (prototype convenience).
- **AI v3** (`Decided` for the prototype, 2026-09-25):
  - **It calls the pack.** Spotting you, it growls; any other evil guy that hears that growl comes
    for *you*, not for the one that growled. Losing you, it growls again, and the others head for
    where you were going. You hear every call too, so you know the pack is coming.
  - **It waits at doors.** If you (or a noise) are somewhere it can't get to, like behind a shut
    door, it waits by the door for 20 s, listening, and comes through the moment it opens. If there's
    another way round, it takes it.
  - **It learns your hiding spots.** Wherever it loses someone twice or more, it starts checking on
    patrol, and first when searching nearby. Keep using the same trick and it stops working.
  - **It can be lured**: a crash is just a noise at the crash, so throwing a crate draws it away.
- `Idea` (later): react to light (flashlights?); crouching for quieter movement; more than one evil
  guy per chamber so the pack actually forms (harder runs?).
- `Open`: what is it, lore-wise? Can it be researched (evidence it leaves behind)?

#### Player monsters on phones (`Idea`)

- To nerf monster players, you can only play a monster **from your phone**.
- Design notes:
  - Risk: if "worse controls" is the whole nerf, the monster player just has a worse time.
    Better: make the phone a **different game** with touch-first actions: tap to jump between
    vents, hold to manifest, swipe to slam doors, flicker lights, watch through security cameras.
    Limited senses give natural balance and fit "unknown entity".
  - Jackbox-style appeal: friends without a gaming PC can join.
- Tech notes (details in [DECISIONS.md](DECISIONS.md)):
  - Steam doesn't exist on phones, so phone players need a non-Steam connection (our own relay /
    backend).
  - Godot C# can't export to the web. A "scan a QR code, play in the browser" client would be a
    separate small project (GDScript or plain web). Native C# mobile export exists, but Godot
    still marks it experimental, and it adds app store friction.
  - Players control their own movement right now (see DECISIONS). That's fine for co-op, but it
    matters more once players fight players.
- **Plan:** prototype monster play **on PC first** with deliberately limited, touch-like controls.
  Build the phone client only if it's fun.

---

## Open questions

- [ ] Value / fragility on props?
- [ ] Non-physical evidence types?
- [ ] Selling: does it stay now that the pitch is research and testing? What does research earn you?
  If the black market stays: how you get caught, who pays, round-level consequences?
- [ ] Character abilities: what they are, the decay curve and floor, and the retraining rate `K`?
  (Storage and "time" are decided: see Persistent characters → How player data is stored.)
- [ ] Upgrades: per character or per profile?
- [ ] Monster players: what can they do, how many per match, phone or PC?
- [ ] Building: in or out?
- [ ] Real title?
- [ ] Acid friendly fire? Lingering acid puddles?
- [x] Death: teammate revive in the maze; everyone down fails the run (decided 2026-09-25)
- [ ] Other archetypes and their kits?
- [ ] The evil guy's lore, and whether it can be researched
- [ ] Filter tuning: is 180 s right? Should swapping take time? Where do spares come from in real levels?
- [ ] Do the cracks read clearly enough to drop the debug health bar?
- [ ] Game modes: which ones, and what does each play like? (see Game modes)
- [ ] Dying: which upgrades do you keep, and what goes down with the rat?
- [ ] Mental damage: does the HUD corruption read clearly enough in play? (see Lore → "The mask is the lie")
