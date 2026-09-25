# RND

Co-op research horror (working title). Players are scientists researching and testing unknown
entities, and trying to get out alive.

Godot 4.7 (.NET / C#), Jolt physics, ENet networking for now.

## Docs

**Start here:** [Team handbook](docs/overview.html), the plain-English overview for everyone.
The files below are the detailed working notes.

- [Design](docs/DESIGN.md): game ideas, mechanics, open questions
- [Decisions](docs/DECISIONS.md): tech choices and why, known limitations
- [Roadmap](docs/ROADMAP.md): what's done, what's next
- [Art pipeline](docs/ART.md): how to hand models over (format, scale, naming, liquids)

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
| 1–5 / Scroll | Pick a hotbar slot (slot 1 = Hands) |
| LMB | Use the selected slot |
| Hold LMB with Hands | Grab and carry (let go to drop; props keep momentum, so you can fling them) |
| RMB while carrying | Throw the prop |
| Scroll while carrying | Push / pull |
| LMB with the acid flask | Throw it (5 s recharge; the flask in your hand refills) |
| E on (or holding) a spare filter | Screw it onto your mask |
| F2 – F6 | Debug view switches, to compare looks: cel shading, outlines, film grain, colour crush, lens (glass curve, edge blur, fringe). Pressing one lists what's on, top right |
| F7 | Mute / unmute all sound (debug) |
| Esc | Free the mouse, leave session |

You see everything through a gas mask. **The cracks in the glass are your health** (each hit
cracks the side it came from); there's no health number, except a plain debug bar in the corner. Your **filter** runs down (faster when you
sprint), and when it's spent you choke. Find spare filter canisters and screw them on. Play with
sound on: you hear your own breathing (it wheezes as the filter wears), a heartbeat when you're
badly hurt, and the world muffled through the mask, less so the more it's cracked.

The evil guy only knows what it sees and hears. Sprinting, shattering flasks, crashing props and
fresh filters are loud, walking is quiet. Its eyes show its mood: dim = calm,
orange = suspicious, red = hunting you.

## Layout

```
game/
  core/      Main scene, Network autoload (the only code that knows the transport), layers
  combat/    Health + Respirator (gas mask filter) components (host-owned, replicated)
  enemies/   The evil guy
  items/     Hotbar items (.tres data) + thrown projectiles (acid flask)
  models/    Model scenes shared by props, hand items and projectiles (Erlenmeyer flask)
  levels/    Level base script, drop-link generator, test_level greybox (CSG)
  players/   First-person researcher (Scientist kit)
  props/     PhysicsProp + crate / heavy case / specimen jar / spare filter
  ui/        Main menu, crisp overlay (hud), HUD projected on the visor (visor_hud), hotbar
  audio/     Procedural sound: world sound bank, in-mask synth (breathing, heartbeat), previews
  vfx/       Visor shader (mask + cracks + grain), hit flashes, splash effects
  tests/     Smoke test (headless checks + windowed screenshot capture)
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
godot --headless res://tests/smoke_test.tscn -- --role=scenes   # scenes load; level + combat work offline
godot --headless res://tests/smoke_test.tscn -- --role=host     # start this first...
godot --headless res://tests/smoke_test.tscn -- --role=client   # ...then: join, carry, throw, fight, leave
```

Each prints `SMOKE PASS` or `SMOKE FAIL: ...` and exits 0 / 1.

Shaders can't render headless, so the visor has a **visual check** instead. It opens a window for
a few seconds and saves screenshots: cel shading off / on, a flask sloshing, the acid flask in
hand (full, then refilling), and the mask healthy, hurt and choking:

```
godot --resolution 1280x720 res://tests/smoke_test.tscn -- --role=capture --out=C:/some/folder
```

All sounds are generated in code. To **listen** to them without playing (and check nothing clips),
render every sound to `.wav`:

```
godot --headless res://tests/smoke_test.tscn -- --role=audio --out=C:/some/folder
```

## Notes

- Commit the `.uid` files Godot creates next to scripts and shaders.
- Tune the cel-shading outlines (width, colour) on `Main/ToonStyle`'s Outline Material, and its
  light bands in `vfx/toon_ramp.tres`. The `capture` test fails if the bands change the room's
  overall brightness by more than 20%.
- Tune the visor and camera look (glass curve, rim colour, grain, colour crush) on
  `Main/PostFX/Visor`'s material. Breathing and sway feel are exports on the `Visor` node.
- What's next: see the [Roadmap](docs/ROADMAP.md).
