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

	private Vector3 _closed;
	private bool _wasOpen;

	private static bool IsPressed(Node node) => ((ISwitch)node).Pressed;

	public override void _Ready()
	{
		_closed = Position;
		CollisionMask = Layers.Players | Layers.Props; // only for the "something's in the way" test
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
	}
}
