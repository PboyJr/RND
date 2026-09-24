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
- The prototype already has a grain pass: film grain, a slightly crushed palette, colour fringing
  and a vignette (`game/vfx/grain.gdshader`). UI draws above it and stays crisp.

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

- The "main scientist guy". Kit: **acid beaker**.
- **Acid beaker** (`Decided` for the prototype, numbers still being tuned):
  - Equip it on the hotbar and press LMB to lob it on an arc. It shatters on the first surface,
    prop or entity it touches and splashes acid in a **2 m radius**.
  - **50 damage** on a direct hit, dropping to 50% at the splash edge.
  - Recharges for **15 s** after each throw. There's no ammo; it just refills.
  - No friendly fire for now (the splash skips players). `Open`: should it hurt teammates?
  - `Idea`: leave an acid puddle that damages over time, for area denial and kiting.
- `Open`: other archetypes and their kits.

### Hotbar (`Decided` for the prototype)

- Slot 1 is always **Hands** (grab / carry / throw props). Slots 2–5 hold the archetype's kit.
- Keys **1–5** select a slot. **Scroll** cycles slots, except while carrying a prop, when it
  pushes / pulls it.
- **LMB uses whatever is selected** (grab with Hands, throw the beaker, and so on).
- Each slot shows its recharge time. Everyone can see what you're holding (the beaker appears in
  your hand while it's charged).

### Health, damage, death (`Leaning`)

- Players: **100 HP**. Death is a placeholder: you fall over, then **respawn at a spawn point
  after 8 s** at full health. `Open`: REPO-style revive-by-teammate instead? Does death cost
  money or research?
- Hits flash the body; the local player gets a red screen flash, a health bar and a respawn
  countdown.

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
  | Beaker shattering | 14 m |
  | Props crashing | 3–18 m (harder and heavier = louder) |

  A noise makes it come and investigate, and a close noise puts it on edge. So **sneaking means
  walking**, and throwing a beaker is loud (useful as a distraction?).
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
