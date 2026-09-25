# RND: Design

This doc changes as we go. Ideas are cheap, so each one is tagged with a status to show what's
actually decided.

**Status tags:** `Decided` · `Leaning` (probably, not locked) · `Idea` (on the table) · `Open` (needs an answer)

Last updated: 2026-09-24

---

## Guiding principle (`Decided`)

**Make everything as intelligent and dynamic as possible.** Entities, systems and the world should
react to what's actually happening instead of following fixed scripts. Enemies find their way to
you, adapt when you move, and don't take shortcuts that only look smart. When choosing between a
simple scripted behaviour and a reactive one, lean reactive, and fix the rough edges rather than
falling back to the script.

## Pitch

Co-op horror in the vein of R.E.P.O. You're a team of researchers sent into places where unknown
entities live. Collect evidence, data and specimens, get out alive, and sell what you found:
to the government, or to the black market for more money and a chance of getting caught.

Working title: **RND** (repo name). The first notes were titled "1" (in Comic Sans, muahahaha).

## Inspiration

- **R.E.P.O.**: physics-based carrying of fragile loot, co-op chaos, proximity voice
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
2. Collect research (evidence, data, specimens) while dealing with entities
3. Extract
4. Sell: government or black market
5. Between rounds: upgrades, research tree, crate opening

---

## Mechanics

### Collecting research (`Idea`)

- Physical props carried REPO-style (the prototype has crates, a heavy case and specimen jars).
- `Open`: do props have value and fragility, losing money when they hit something too hard like in REPO?
- `Open`: is there also non-physical evidence (photos, recordings, readings, data)?

### Selling: government vs black market (`Idea`)

- The black market pays more, but you can get caught. The penalty is a **$ fine**.
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
  - **Where saves live.** Local save files can be edited. If characters can join other people's
    lobbies, fairness needs server-side storage or at least validation by the host.
  - **What "time" means.** Decay on real-world time can be cheated by changing the system clock,
    and it punishes people who take a break. *Suggestion:* count decay in **runs played on other
    characters**. That can't be cheated and creates a roster / bench dynamic: you juggle
    characters, and benched ones get rusty. It pairs well with rebirth.
  - `Open`: which abilities, what the decay curve looks like, whether there's a floor, and
    whether decay can be trained back.

### Player archetypes (`Leaning`)

Players are scientists. Each archetype brings a different kit on the hotbar.

#### The Scientist (first archetype, in the prototype)

- The "main scientist guy". Kit: **acid flask**.
- **Acid flask** (`Decided` for the prototype, numbers still being tuned): an Erlenmeyer flask
  of glowing acid.
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

- Players: **100 HP**. Death is a placeholder: you fall over, then **respawn at a spawn point
  after 8 s** at full health, with a fresh mask and filter. `Open`: REPO-style revive-by-teammate
  instead? Does death cost money or research?
- Hits flash your body for everyone else. For you, they crack your visor (see below).

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
  the mask clammier.
- **The mask sways**: the rim lags a little behind where you look and jolts when you're hit.
- **The HUD is projected onto the glass** in phosphor green (hotbar, hints, crosshair, filter
  gauge), so it curves, glows, flickers and gets split by cracks along with the visor.
- Outside the mask, crisp and **deliberately unstyled**: a debug health bar (Godot's default
  progress bar, showing %) and a filter readout. Remove these once the cracks read well in
  playtests.

### Mask audio (`Decided` for the prototype, sounds still tuning by ear)

Half the gas mask illusion is sound. Everything is generated in code, so there are no audio files.

- **Atmosphere first.** The room should feel present before anything happens:
  - **Room tone:** a slow low rumble, a faint ventilation hum and duct air, looping seamlessly.
  - **Distant events:** a creak, drip, far-off boom or vent rattle every 4–12 s, somewhere 7–18 m
    around you. Atmosphere only, since enemies don't react to them.
  - **Reverb:** a concrete-room echo on every world sound, heard *through* the mask, so the
    echoes come through dull and distant too.
  - **Balance:** calm breathing sits just under the room tone. Your body only takes over when
    things go wrong (choking, heartbeat).
- **You hear yourself breathe, and it matches the fog.** A warm, low "hoo" out through the exhale
  valve, a breathier draw in through the filter, a faint rubber valve flap at each turn of breath,
  and the boxy ring of air trapped in a mask. It shares one breathing rhythm with the visor, so
  every fog puff lands on an exhale you hear.
  - Faster and louder with effort, and stays heavy after a sprint.
  - **Wheezes and whistles as the filter wears out**, so you can *hear* it running down.
  - Ragged gasping when you're choking.
- **Heartbeat** fades in below 50% health: a lub-dub that speeds up as you get closer to death.
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

The mask's filter runs out, and that's a core pressure mechanic.

- A fresh filter lasts **180 s** of calm breathing. It drains at **0.6×** standing still and up
  to **2.5×** sprinting flat out, so **sprinting burns air**.
- **Spent filter = choking:** 4 damage every 0.5 s (so the cracks spread), vision closes in and
  pulses red, and the fog goes thick.
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
- `Idea` (AI v3): learn habits (the spot you keep kiting to, the hiding place you reuse); react to
  light (flashlights?); crouching for quieter movement; multiple evil guys that share what they
  notice; distraction play (throw a crate to lure it away).
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
- [ ] Black market: how you get caught, who pays, round-level consequences?
- [ ] Character abilities: what they are, and how decay works (real time vs runs played)?
- [ ] Where do characters live: local, host-validated, or our own server?
- [ ] Monster players: what can they do, how many per match, phone or PC?
- [ ] Building: in or out?
- [ ] Real title?
- [ ] Acid friendly fire? Lingering acid puddles?
- [ ] Death: timed respawn (prototype) vs. teammate revive vs. out for the round?
- [ ] Other archetypes and their kits?
- [ ] The evil guy's lore, and whether it can be researched
- [ ] Filter tuning: is 180 s right? Should swapping take time? Where do spares come from in real levels?
- [ ] Do the cracks read clearly enough to drop the debug health bar?
