# RND

Co-op research horror (working title). Players are researchers collecting evidence on unknown
entities and selling it: to the government, or to the black market for more money and more risk.

Godot 4.7 (.NET / C#), Jolt physics, ENet networking for now.

## Docs

- [Design](docs/DESIGN.md): game ideas, mechanics, open questions
- [Decisions](docs/DECISIONS.md): tech choices and why, known limitations
- [Roadmap](docs/ROADMAP.md): what's done, what's next

Keep these updated when things change. If it isn't written down, we'll forget it.

## Setup

1. Install the **.NET edition** of Godot 4.7 (the standard edition can't run C#):
   `winget install GodotEngine.GodotEngine.Mono`, or download ".NET" from godotengine.org.
2. Have the .NET 8+ SDK installed.
3. In Godot's project manager: **Import** → `game/project.godot`.
4. Press **Play** (F5). The first run builds the C# project.

Everyone should use the **same Godot version**. Opening the project in a different version rewrites
the SDK version in `game/RND.csproj`.

### Visual Studio (optional)

VS can't start a Godot C# project by itself ("A project with an Output Type of Class Library cannot
be started directly"). Create `game/Properties/launchSettings.json` (gitignored, since the paths are
per-machine) with your own Godot path and repo path:

```json
{
  "profiles": {
    "Play": {
      "commandName": "Executable",
      "executablePath": "C:\\path\\to\\Godot_v4.7-stable_mono_win64.exe",
      "commandLineArgs": "--path \"C:\\path\\to\\RND\\game\"",
      "workingDirectory": "C:\\path\\to\\RND\\game"
    },
    "Godot Editor": {
      "commandName": "Executable",
      "executablePath": "C:\\path\\to\\Godot_v4.7-stable_mono_win64.exe",
      "commandLineArgs": "--path \"C:\\path\\to\\RND\\game\" --editor",
      "workingDirectory": "C:\\path\\to\\RND\\game"
    }
  }
}
```

Point it at the normal exe, **not** `_console.exe`: the console build is a wrapper, so the debugger
would attach to the wrong process. Pick **Play** next to the ▶ button and press F5. C# breakpoints
work.

To open scripts from Godot in VS: **Editor → Editor Settings → Dotnet → Editor → External Editor**
→ Visual Studio.

## Testing multiplayer on one PC

**Debug → Customize Run Instances…** → tick **Enable Multiple Instances**, set it to 2, then Play.
Click **Host** in one window and **Join** (127.0.0.1) in the other.

On a LAN, other machines join with the host's local IP. Windows Firewall will ask the first time
you host; allow it on private networks.

Running a level scene directly (F6) also works: you play offline as the host.

## Controls

| Input | Action |
| --- | --- |
| WASD / Space / Shift | Move / jump / sprint |
| Hold LMB | Grab and carry (let go to drop; props keep momentum, so you can fling them) |
| RMB while carrying | Throw |
| Scroll while carrying | Push / pull |
| Esc | Free the mouse, leave session |

## Layout

```
game/
  core/      Main scene + Network autoload (the only code that knows the transport)
  levels/    Level base script + test_level greybox (CSG)
  players/   First-person researcher
  props/     PhysicsProp + crate / heavy case / specimen jar
  ui/        Main menu, HUD
  vfx/       Grain / cheap-camera post-process
  tests/     Headless smoke test
docs/        Design, decisions, roadmap
```

## How the networking works

- **Listen server.** The host is also the server. Only the host loads levels; `LevelSpawner`
  replicates them to clients, including late joiners.
- **Players are client-authoritative.** Each client moves its own player, so movement feels
  instant. The player node is named after its owner's peer id, which is how authority gets
  assigned. Everyone else smooths toward the replicated position and look direction.
- **Props are host-authoritative.** Only the host simulates physics. Clients hold frozen copies
  that follow the host's transform. Grab / release / throw are requests sent to the host, which
  decides who holds what, so props never desync.
- **Transport lives in `core/Network.cs`.** Swapping ENet for Steam (or a relay) happens there,
  without touching gameplay code.

## Smoke test

`game/tests/smoke_test.tscn` checks everything headlessly. Run these from `game/`, using the
`_console.exe` Godot build on Windows so the output shows up:

```
godot --headless res://tests/smoke_test.tscn -- --role=scenes   # every scene loads; level plays offline
godot --headless res://tests/smoke_test.tscn -- --role=host     # start this first...
godot --headless res://tests/smoke_test.tscn -- --role=client   # ...then this: join, grab, throw, leave
```

Each prints `SMOKE PASS` or `SMOKE FAIL: ...` and exits 0 / 1.

## Notes

- Commit the `.uid` files Godot creates next to scripts and shaders.
- Tune the grain / vignette / colour crush on `Main/PostFX/Grain`'s material.
- What's next: see the [Roadmap](docs/ROADMAP.md).
