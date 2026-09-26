using System.Collections.Generic;
using System.Linq;
using Godot;
using RND.Levels;

namespace RND.Maze;

/// <summary>
/// Builds a test chamber from a seed and a difficulty (0 easiest .. 1 hardest), so runs vary. The host
/// picks the seed; the level spawner hands it to every peer, which builds the identical room (same
/// names, same places), so props and switches replicate like in a hand-built chamber.
///
/// A chamber is a room with a start corridor and an exit corridor, and a puzzle made of the pieces we
/// already have (DESIGN → The maze): a weight plate that holds the exit door open, and crates or the
/// heavy case to weigh it down; the case shut in a closet behind a button door; a second plate; a
/// button on a ledge that only a thrown jar can press; the evil guy, released sooner (and at the top,
/// twice) the harder it is. Pieces cost difficulty points and the chamber buys what its budget allows.
/// Every piece is solvable by construction, and the plan it leaves on the level (meta "plan") says how;
/// the smoke test solves chambers from it.
/// </summary>
public static class ChamberGenerator
{
	private const float PlateMargin = 1.2f;

	private static readonly PackedScene Plate = GD.Load<PackedScene>("res://maze/pressure_plate.tscn");
	private static readonly PackedScene Door = GD.Load<PackedScene>("res://maze/chamber_door.tscn");
	private static readonly PackedScene Button = GD.Load<PackedScene>("res://maze/chamber_button.tscn");
	private static readonly PackedScene Crate = GD.Load<PackedScene>("res://props/crate.tscn");
	private static readonly PackedScene Case = GD.Load<PackedScene>("res://props/crate_large.tscn");
	private static readonly PackedScene Jar = GD.Load<PackedScene>("res://props/specimen_jar.tscn");

	public static Level Build(PackedScene skeleton, int seed, float difficulty) =>
		new Builder(skeleton.Instantiate<Level>(), seed, Mathf.Clamp(difficulty, 0f, 1f)).Build();

	private sealed class Builder(Level level, int seed, float difficulty)
	{
		private readonly RandomNumberGenerator _rng = new() { Seed = (ulong)seed };
		private readonly Godot.Collections.Dictionary _plan = new();
		private readonly Godot.Collections.Array _plates = new();
		private readonly List<Node> _exitSwitches = [];
		private readonly Dictionary<string, int> _names = [];
		private readonly StandardMaterial3D _panel = new() { AlbedoColor = new Color(0.78f, 0.8f, 0.82f), Roughness = 0.7f };
		private Node3D _geometry, _props;
		private bool[,] _taken;
		private int _cols, _rows;
		private float _w, _d, _h;

		public Level Build()
		{
			level.Name = "Chamber";
			_geometry = level.GetNode<Node3D>("Navigation/Geometry");
			_props = level.GetNode<Node3D>("Props");

			// What this chamber has: a plate always, then pieces bought with the difficulty budget.
			float budget = 3f * difficulty; // the plate is free
			bool useCase = _rng.Randf() < 0.4f + 0.4f * difficulty;
			var bought = new HashSet<string>();
			var pieces = new List<(string Name, float Cost)> { ("ledge", 1.5f), ("second_plate", 1f), ("heavy", 0.5f) };
			if (useCase)
				pieces.Add(("closet", 1.5f));
			if (difficulty > 0.75f)
				pieces.Add(("two_evil_guys", 1f));
			foreach (var piece in pieces.OrderBy(_ => _rng.Randi()))
			{
				if (piece.Cost <= budget + 0.001f)
				{
					bought.Add(piece.Name);
					budget -= piece.Cost;
				}
			}

			_w = 12 + 2 * _rng.RandiRange(0, 2);
			_d = 12 + 2 * _rng.RandiRange(0, 2);
			_h = bought.Contains("ledge") ? 6f : 4f;
			_cols = (int)_w;
			_rows = (int)_d;
			_taken = new bool[_cols, _rows];
			ReserveLanes();

			float closetSide = _rng.Randf() < 0.5f ? -1f : 1f; // the ledge, if any, goes on the other side
			BuildShell(bought.Contains("closet") ? closetSide : 0f, bought.Contains("ledge") ? -closetSide : 0f, out float closetZ, out float ledgeZ);

			// The first plate, weighed down by the case or by crates; the case may be in the closet.
			Node3D plate1 = AddPlate(bought.Contains("heavy") ? 45f : 30f);
			var weights1 = new Godot.Collections.Array<string>();
			if (useCase)
			{
				weights1.Add(bought.Contains("closet") ? BuildCloset(closetSide, closetZ) : AddProp(Case, "Case", 0.5f));
				if (bought.Contains("heavy"))
					weights1.Add(AddProp(Crate, "Crate", 0.3f));
			}
			else
			{
				int crates = bought.Contains("heavy") ? 6 : 4; // 4 × 8 = 32 ≥ 30; 6 × 8 = 48 ≥ 45
				for (int i = 0; i < crates; i++)
					weights1.Add(AddProp(Crate, "Crate", 0.3f));
			}
			_plates.Add(PlatePlan(plate1, weights1));

			// A second plate (the exit needs both), weighed down by whatever the first one didn't use.
			if (bought.Contains("second_plate"))
			{
				Node3D plate2 = AddPlate(30f);
				var weights2 = new Godot.Collections.Array<string>();
				if (useCase)
					for (int i = 0; i < 4; i++)
						weights2.Add(AddProp(Crate, "Crate", 0.3f));
				else
					weights2.Add(AddProp(Case, "Case", 0.5f));
				_plates.Add(PlatePlan(plate2, weights2));
			}

			// A spare crate, and jars: throwables for distractions (and, on a ledge, the button).
			AddProp(Crate, "Crate", 0.3f);
			int jars = bought.Contains("ledge") ? 3 : 2;
			for (int i = 0; i < jars; i++)
				AddProp(Jar, "Jar", 0.2f);

			if (bought.Contains("ledge"))
				BuildLedgeButton(-closetSide, ledgeZ, bought.Contains("heavy") && difficulty > 0.6f ? 4f : 6f);

			AddExit();
			AddSpawns(bought.Contains("two_evil_guys") ? 2 : 1);
			AddLights(bought.Contains("ledge") ? -closetSide : 0f, ledgeZ);
			level.EnemyReleaseDelay = Mathf.Lerp(28f, 12f, difficulty);

			_plan["seed"] = seed;
			_plan["difficulty"] = difficulty;
			_plan["size"] = new Vector3(_w, _h, _d);
			_plan["pieces"] = new Godot.Collections.Array<string>(bought.OrderBy(p => p));
			_plan["plates"] = _plates;
			level.SetMeta("plan", _plan);
			return level;
		}

		// ── Room ────────────────────────────────────────────────────────────────────

		// The room is built from plain boxes (floor, ceilings, walls with gaps for the doorways), not CSG:
		// the navmesh bake reads static boxes at once, whereas CSG made in code only builds its geometry
		// later, and a bake that ran first found an uncut block.
		private void BuildShell(float closetSide, float ledgeSide, out float closetZ, out float ledgeZ)
		{
			const float corridor = 4.1f, gap = 1.5f, door = 3f;
			float w = _w / 2f, d = _d / 2f, far = _w / 2f + 3.6f;
			closetZ = closetSide != 0f ? _rng.RandfRange(-d + 2.5f, d - 5.5f) : 0f; // room for its door to slide into the wall
			ledgeZ = ledgeSide != 0f ? _rng.RandfRange(-d + 4f, d - 4f) : 0f;

			Slab("Floor", new Vector3(-far, -1, -d - corridor - 1), new Vector3(far, 0, d + corridor + 1));
			Slab("Ceiling", new Vector3(-w - 1, _h, -d - 1), new Vector3(w + 1, _h + 1, d + 1));
			foreach (float end in new[] { -1f, 1f }) // the exit wall (−z) and the start wall (+z), each with a corridor
			{
				string which = end < 0 ? "Exit" : "Start";
				float wall = end * d, outer = end * (d + corridor);
				Slab(which + "WallLeft", new Vector3(-w - 1, 0, wall), new Vector3(-gap, _h, wall + end));
				Slab(which + "WallRight", new Vector3(gap, 0, wall), new Vector3(w + 1, _h, wall + end));
				Slab(which + "Lintel", new Vector3(-gap, door, wall), new Vector3(gap, _h, wall + end));
				Slab(which + "CorridorLeft", new Vector3(-gap - 1, 0, wall), new Vector3(-gap, door, outer));
				Slab(which + "CorridorRight", new Vector3(gap, 0, wall), new Vector3(gap + 1, door, outer));
				Slab(which + "CorridorEnd", new Vector3(-gap - 1, 0, outer), new Vector3(gap + 1, door, outer + end));
				Slab(which + "CorridorCeiling", new Vector3(-gap - 1, door, wall), new Vector3(gap + 1, door + 1, outer + end));
			}
			foreach (float side in new[] { -1f, 1f }) // the side walls; the closet's side has a doorway
			{
				string which = side < 0 ? "West" : "East";
				if (side != closetSide)
				{
					Slab(which + "Wall", new Vector3(side * w, 0, -d), new Vector3(side * (w + 1), _h, d));
					continue;
				}
				float z0 = closetZ - gap, z1 = closetZ + gap;
				Slab(which + "WallA", new Vector3(side * w, 0, -d), new Vector3(side * (w + 1), _h, z0));
				Slab(which + "WallB", new Vector3(side * w, 0, z1), new Vector3(side * (w + 1), _h, d));
				Slab(which + "Lintel", new Vector3(side * w, door, z0), new Vector3(side * (w + 1), _h, z1));
				// The closet: 2.6 m deep, 3 m wide, behind the doorway.
				Slab("ClosetBack", new Vector3(side * (w + 2.6f), 0, z0 - 1), new Vector3(side * (w + 3.6f), door, z1 + 1));
				Slab("ClosetSideA", new Vector3(side * w, 0, z0 - 1), new Vector3(side * (w + 2.6f), door, z0));
				Slab("ClosetSideB", new Vector3(side * w, 0, z1), new Vector3(side * (w + 2.6f), door, z1 + 1));
				Slab("ClosetCeiling", new Vector3(side * w, door, z0 - 1), new Vector3(side * (w + 3.6f), door + 1, z1 + 1));
				Reserve(new Vector2(side * w, closetZ), new Vector2(5f, 6f)); // the way in stays clear
			}
			if (ledgeSide != 0f)
			{
				Slab("Ledge", new Vector3(ledgeSide * (w - 2.5f), 0, ledgeZ - 3f), new Vector3(ledgeSide * w, 3.5f, ledgeZ + 3f));
				Reserve(new Vector2(ledgeSide * (w - 1.25f), ledgeZ), new Vector2(4.5f, 8f));
			}

			var glow = new StandardMaterial3D { AlbedoColor = new Color(0.2f, 0.6f, 1f), EmissionEnabled = true, Emission = new Color(0.2f, 0.6f, 1f), EmissionEnergyMultiplier = 0.6f };
			_geometry.AddChild(new MeshInstance3D
			{
				Name = "ExitMarker",
				Position = new Vector3(0, 0.01f, -d - 2.5f),
				Mesh = new BoxMesh { Size = new Vector3(2.6f, 0.02f, 2.6f), Material = glow },
			});
		}

		// A solid box between two corners: collision (on the World layer, so the navmesh sees it) and a panel.
		private void Slab(string name, Vector3 a, Vector3 b)
		{
			Vector3 size = (b - a).Abs();
			var body = new StaticBody3D { Name = name, Position = (a + b) / 2f };
			body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
			body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size, Material = _panel } });
			_geometry.AddChild(body);
		}

		// ── Pieces ──────────────────────────────────────────────────────────────────

		private Node3D AddPlate(float minWeight)
		{
			// On the exit half if it can be (so you carry things toward the door), else anywhere free.
			Vector2 at = FreeSpot(new Vector2(2f + 2 * PlateMargin, 2f + 2 * PlateMargin), preferExitHalf: true);
			var plate = Plate.Instantiate<PressurePlate>();
			plate.Name = NextName("Plate");
			plate.MinWeight = minWeight;
			plate.Position = new Vector3(at.X, 0, at.Y);
			level.AddChild(plate);
			_exitSwitches.Add(plate);
			return plate;
		}

		private Godot.Collections.Dictionary PlatePlan(Node3D plate, Godot.Collections.Array<string> weights) => new()
		{
			["plate"] = plate.Name.ToString(),
			["min_weight"] = ((PressurePlate)plate).MinWeight,
			["props"] = weights,
		};

		private string AddProp(PackedScene scene, string kind, float height)
		{
			Vector2 at = FreeSpot(new Vector2(2.2f, 2.2f), preferExitHalf: false); // a cell of space all round
			var prop = scene.Instantiate<Node3D>();
			prop.Name = NextName(kind);
			prop.Position = new Vector3(at.X, height, at.Y);
			prop.Rotation = new Vector3(0, _rng.RandfRange(-0.4f, 0.4f), 0);
			_props.AddChild(prop);
			return "Props/" + prop.Name;
		}

		// The case in a closet on the side wall, behind a button door: a button outside in the room, and one
		// inside so nobody gets shut in. Returns the case.
		private string BuildCloset(float side, float z)
		{
			var inside = Button.Instantiate<ChamberButton>();
			inside.Name = "ButtonInside";
			inside.Position = new Vector3(side * (_w / 2f + 2.2f), 0, z + 1.1f);
			_geometry.AddChild(inside);

			var outside = Button.Instantiate<ChamberButton>();
			outside.Name = "ButtonOutside";
			float along = z + 3.5f < _d / 2f - 1.5f ? z + 3.5f : z - 3.5f;
			outside.Position = new Vector3(side * (_w / 2f - 4.5f), 0, along);
			_geometry.AddChild(outside);
			Reserve(new Vector2(outside.Position.X, along), new Vector2(2.5f, 2.5f));

			var door = Door.Instantiate<ChamberDoor>();
			door.Name = "ClosetDoor";
			door.Transform = new Transform3D(new Basis(Vector3.Up, Mathf.Pi / 2f), new Vector3(side * (_w / 2f + 0.1f), 1.5f, z));
			door.OpenOffset = new Vector3(0, 0, 3.1f); // into the wall
			door.OpensOnAny = true;
			door.Switches = [outside, inside];
			_geometry.AddChild(door);

			var box = Case.Instantiate<Node3D>();
			box.Name = NextName("Case");
			box.Position = new Vector3(side * (_w / 2f + 1.6f), 0.5f, z - 0.4f);
			_props.AddChild(box);

			_plan["closet"] = new Godot.Collections.Dictionary
			{
				["door"] = "Navigation/Geometry/ClosetDoor",
				["button"] = "Navigation/Geometry/ButtonOutside",
				["inside"] = "Navigation/Geometry/ButtonInside",
				["holds"] = "Props/" + box.Name,
			};
			return "Props/" + box.Name;
		}

		// A button on the ledge, out of reach from the floor: a jar thrown from the room presses it. The exit
		// then opens for a few seconds.
		private void BuildLedgeButton(float side, float z, float openSeconds)
		{
			var button = Button.Instantiate<ChamberButton>();
			button.Name = "LedgeButton";
			button.OpenSeconds = openSeconds;
			button.Position = new Vector3(side * (_w / 2f - 0.4f), 3.5f, z);
			_geometry.AddChild(button);
			_exitSwitches.Add(button);

			// Where a player can throw from: about 7 m out from the button, toward the middle of the room.
			float throwX = side * (_w / 2f - 0.4f) - side * 6.5f;
			_plan["ledge"] = new Godot.Collections.Dictionary
			{
				["button"] = "Navigation/Geometry/LedgeButton",
				["throw_from"] = new Vector3(Mathf.Clamp(throwX, -_w / 2f + 1f, _w / 2f - 1f), 0, z),
				["open_seconds"] = openSeconds,
			};
		}

		private void AddExit()
		{
			var door = Door.Instantiate<ChamberDoor>();
			door.Name = "ExitDoor";
			door.Position = new Vector3(0, 1.5f, -_d / 2f - 0.2f);
			door.Switches = new Godot.Collections.Array<Node>(_exitSwitches);
			_geometry.AddChild(door);
			level.GetNode<Node3D>("Exit").Position = new Vector3(0, 1.5f, -_d / 2f - 2.5f);
		}

		private void AddSpawns(int evilGuys)
		{
			var spawns = level.GetNode<Node3D>("SpawnPoints");
			int n = 0;
			foreach (float z in new[] { 2.5f, 3.5f })
			{
				foreach (float x in new[] { 0f, -1f, 1f })
				{
					var marker = new Marker3D { Name = $"Spawn{++n}", Position = new Vector3(x, 0.05f, _d / 2f + z) };
					spawns.AddChild(marker);
					marker.AddToGroup("player_spawn", persistent: true);
				}
			}

			// The evil guys start in the far half of the room, in free spots.
			var lairs = level.GetNode<Node3D>("EnemySpawnPoints");
			for (int i = 0; i < evilGuys; i++)
			{
				Vector2 at = FreeSpot(new Vector2(1.6f, 1.6f), preferExitHalf: true);
				var marker = new Marker3D { Name = $"EnemySpawn{i + 1}", Position = new Vector3(at.X, 0.05f, at.Y) };
				lairs.AddChild(marker);
				marker.AddToGroup("enemy_spawn", persistent: true);
			}
		}

		private void AddLights(float ledgeSide, float ledgeZ)
		{
			var lights = level.GetNode<Node3D>("Lights");
			lights.AddChild(new OmniLight3D { Name = "RoomLight", Position = new Vector3(0, _h - 0.6f, 0), LightEnergy = 1.8f, ShadowEnabled = true, OmniRange = Mathf.Max(_w, _d) + 2f });
			lights.AddChild(new OmniLight3D { Name = "StartLight", Position = new Vector3(0, 2.7f, _d / 2f + 2.5f), LightEnergy = 0.8f, OmniRange = 5f });
			lights.AddChild(new OmniLight3D { Name = "ExitLight", Position = new Vector3(0, 2.7f, -_d / 2f - 2.5f), LightColor = new Color(0.6f, 0.8f, 1f), LightEnergy = 0.8f, OmniRange = 5f });
			if (ledgeSide != 0f)
				lights.AddChild(new OmniLight3D { Name = "LedgeLight", Position = new Vector3(ledgeSide * (_w / 2f - 1.5f), _h - 0.6f, ledgeZ), LightColor = new Color(1f, 0.85f, 0.7f), LightEnergy = 0.7f, OmniRange = 4f });
		}

		// ── Free space ──────────────────────────────────────────────────────────────
		// A 1 m grid over the room. Lanes stay clear (the middle from start to exit, the areas at both
		// doors, the walls), and each piece reserves its footprint plus a margin.

		private void ReserveLanes()
		{
			Reserve(new Vector2(0, 0), new Vector2(2.4f, _d));                    // the walk from start to exit
			Reserve(new Vector2(0, _d / 2f - 1.5f), new Vector2(5f, 3f));         // around the way in
			Reserve(new Vector2(0, -_d / 2f + 1.5f), new Vector2(5f, 3f));        // around the exit door
			for (int i = 0; i < _cols; i++)
			{
				_taken[i, 0] = _taken[i, _rows - 1] = true;
			}
			for (int j = 0; j < _rows; j++)
			{
				_taken[0, j] = _taken[_cols - 1, j] = true;
			}
		}

		private void Reserve(Vector2 centre, Vector2 size)
		{
			for (int i = 0; i < _cols; i++)
			{
				for (int j = 0; j < _rows; j++)
				{
					Vector2 cell = CellCentre(i, j);
					if (Mathf.Abs(cell.X - centre.X) < size.X / 2f && Mathf.Abs(cell.Y - centre.Y) < size.Y / 2f)
						_taken[i, j] = true;
				}
			}
		}

		private Vector2 CellCentre(int i, int j) => new(-_w / 2f + 0.5f + i, -_d / 2f + 0.5f + j);

		// A random free spot whose whole footprint is free, then reserved. Falls back to any free cell (a
		// cramped room), and to the middle of the room if the grid is full.
		private Vector2 FreeSpot(Vector2 footprint, bool preferExitHalf)
		{
			var candidates = new List<Vector2>();
			for (int i = 0; i < _cols; i++)
			{
				for (int j = 0; j < _rows; j++)
				{
					Vector2 cell = CellCentre(i, j);
					if (preferExitHalf && cell.Y > 0f)
						continue;
					if (Fits(cell, footprint))
						candidates.Add(cell);
				}
			}
			if (candidates.Count == 0 && preferExitHalf)
				return FreeSpot(footprint, preferExitHalf: false);
			if (candidates.Count == 0)
				return FallbackSpot();

			Vector2 spot = candidates[_rng.RandiRange(0, candidates.Count - 1)];
			Reserve(spot, footprint);
			return spot;
		}

		private Vector2 FallbackSpot()
		{
			for (int i = 0; i < _cols; i++)
				for (int j = 0; j < _rows; j++)
					if (!_taken[i, j])
					{
						_taken[i, j] = true;
						return CellCentre(i, j);
					}
			return new Vector2(_w / 4f, 0f);
		}

		private bool Fits(Vector2 centre, Vector2 footprint)
		{
			for (int i = 0; i < _cols; i++)
			{
				for (int j = 0; j < _rows; j++)
				{
					Vector2 cell = CellCentre(i, j);
					if (Mathf.Abs(cell.X - centre.X) < footprint.X / 2f && Mathf.Abs(cell.Y - centre.Y) < footprint.Y / 2f && _taken[i, j])
						return false;
				}
			}
			// The footprint must also lie inside the room.
			return Mathf.Abs(centre.X) + footprint.X / 2f <= _w / 2f && Mathf.Abs(centre.Y) + footprint.Y / 2f <= _d / 2f;
		}

		private string NextName(string kind)
		{
			int n = _names.GetValueOrDefault(kind) + 1;
			_names[kind] = n;
			return kind == "Plate" ? $"Plate{n}" : $"{kind}{(char)('A' + n - 1)}";
		}
	}
}
