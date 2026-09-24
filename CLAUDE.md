# RND: notes for Claude

Co-op research horror game (REPO-like) in **Godot 4.7 .NET / C#**. The Godot project is `game/`.

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

## Working in the code

- Build: `dotnet build game/RND.sln`
- Smoke tests (headless, from `game/`): see README. Run `--role=scenes`, and for networking
  changes run `host` then `client`. Use Godot's `_console.exe` on Windows to see output.
  Always wrap runs in a timeout: if the test script fails to parse, Godot never quits.
- Conventions: feature folders, one namespace per folder (`RND.Core`, `RND.Players`, ...), tabs,
  file-scoped namespaces, scripts next to their scenes.
- Networking rules: transport only in `core/Network.cs`; player movement is client-authoritative;
  props, health, enemies and projectiles are host-authoritative (clients send requests via RPC).
  See DECISIONS before changing.
- Anything damageable gets a `Health` child (`combat/Health.cs`). New hotbar items are `.tres`
  files (`items/`), added to a player's `Loadout`. New spawnable scenes must be listed in the
  level's matching MultiplayerSpawner.
