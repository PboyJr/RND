using System.Linq;
using Godot;
using RND.Audio;
using RND.Core;
using RND.Levels;

namespace RND.Maze;

/// <summary>Anything that can open a door: pressure plates, buttons. Pressed is replicated.</summary>
public interface ISwitch
{
	bool Pressed { get; }
}

/// <summary>
/// A sliding door that's open while its switches are pressed (all of them, or any one). Every peer
/// works out open / closed from the switches' replicated state and slides its own copy, so the
/// door itself needs no syncing. It never closes on anyone: while a player or prop is in the way
/// it waits, so a crate can jam it open.
/// </summary>
public partial class ChamberDoor : AnimatableBody3D
{
	private const float SlideSpeed = 3f;
	private const float SoundRadius = 8f;

	[Export] public Godot.Collections.Array<Node> Switches { get; set; } = [];
	// Open when any one switch is pressed (e.g. a button on each side) instead of all of them.
	[Export] public bool OpensOnAny { get; set; }
	// Where it slides to when open, relative to where it's placed (closed). Hide it inside a wall.
	[Export] public Vector3 OpenOffset { get; set; } = new(3.1f, 0, 0);

	public bool IsOpen => Switches.Count > 0 && (OpensOnAny ? Switches.Any(IsPressed) : Switches.All(IsPressed));

	private const float LinkReach = 1.2f; // how far each side of the door the enemy link starts, past the navmesh's edge

	private Vector3 _closed;
	private bool _wasOpen;
	private NavigationLink3D _link;

	private static bool IsPressed(Node node) => ((ISwitch)node).Pressed;

	public override void _Ready()
	{
		_closed = Position;
		CollisionMask = Layers.Players | Layers.Props | Layers.Entities; // only for the "something's in the way" test

		// Doors sit in the level geometry, so the navmesh is baked with them shut: to enemies a door is
		// a wall. This link through the doorway (on the floor, left where the door was placed) is only on
		// while the door is fully open.
		float floor = -((BoxShape3D)GetNode<CollisionShape3D>("Shape").Shape).Size.Y / 2f;
		_link = new NavigationLink3D
		{
			TopLevel = true,
			Enabled = false,
			StartPosition = new Vector3(0, floor, -LinkReach),
			EndPosition = new Vector3(0, floor, LinkReach),
		};
		AddChild(_link);
		_link.GlobalTransform = GlobalTransform;
	}

	public override void _PhysicsProcess(double delta)
	{
		bool open = IsOpen;
		if (open != _wasOpen)
		{
			_wasOpen = open;
			Level.Current?.EmitSound(GlobalPosition, SoundRadius, SoundKind.Impact); // host only; a no-op elsewhere
		}

		Vector3 motion = Position.MoveToward(open ? _closed + OpenOffset : _closed, SlideSpeed * (float)delta) - Position;
		var parentBasis = GetParentOrNull<Node3D>()?.GlobalBasis ?? Basis.Identity;
		if (!open && TestMove(GlobalTransform, parentBasis * motion))
			return;
		Position += motion;
		_link.Enabled = Position.DistanceTo(_closed + OpenOffset) < 0.05f;
	}
}
