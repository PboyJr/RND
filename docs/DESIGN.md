# RND: Design

This doc changes as we go. Ideas are cheap, so each one is tagged with a status to show what's
actually decided.

**Status tags:** `Decided` · `Leaning` (probably, not locked) · `Idea` (on the table) · `Open` (needs an answer)

Last updated: 2026-09-24

---

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

### Entities / monsters (`Idea`)

- Entities can be **NPCs or player-controlled**.

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
