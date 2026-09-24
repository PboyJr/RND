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
  snap if more than 3 m off.
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

## 2026-09-24: Look: grain post-process layer

**Decision:** a full-screen `ColorRect` with a screen-reading canvas shader on its own CanvasLayer
(`Main/PostFX`, layer 1), drawn between the 3D view and the UI (`Main/UI`, layer 2).

**Why:** cheap, works in every renderer, and keeps UI text crisp.

## 2026-09-24: Project layout and code style

- **Feature folders** under `game/`: `core/`, `levels/`, `players/`, `props/`, `ui/`, `vfx/`,
  `tests/`. Each script sits next to its scene.
- **One namespace per folder:** `RND.Core`, `RND.Levels`, `RND.Players`, `RND.Props`, `RND.UI`.
- Tabs for indentation (Godot's default), file-scoped namespaces.

## 2026-09-24: Testing: headless smoke tests

**Decision:** `game/tests/smoke_test.tscn` runs headless in three roles: `scenes` (every scene
loads and the level plays offline), `host` and `client` (real ENet session: join, grab, throw,
leave). Commands are in the README.

**Why:** we can't click through the game on every change, and networking bugs hide until a second
player shows up.

## 2026-09-24: Dev tooling: Visual Studio launch profile

**Decision:** `game/Properties/launchSettings.json` has **Play** and **Godot Editor** profiles
that launch Godot with the project, with the debugger attached. It's **gitignored** because it
contains local paths; each dev makes their own (template in the README).

## 2026-09-24: Documentation lives in the repo

**Decision:** design, decisions and roadmap live in `docs/*.md`, not in anyone's head or chat
history. Update them in the same change that makes them outdated.

---

## Known limitations / tech debt

Things the prototype does on purpose that we'll need to revisit:

- Props sync position every tick even when asleep. Fine for a few dozen props; optimise later
  (sleep-aware sync, lower rate).
- The "one held prop per player" rule is only enforced on the client.
- Clients pick their own spawn point (peer id mod spawn count), so two players can pick the same one.
- Name labels show peer ids. Real names come with Steam.
- Walking into props doesn't push them (clients see frozen copies). Grabbing is the only way to
  move them.
- No crouch, no stamina, no health yet.
