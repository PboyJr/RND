using System.Linq;
using Godot;
using RND.Core;
using RND.Players;

namespace RND.Maze;

/// <summary>
/// The end of a test chamber. The clock starts when the chamber loads; the test is passed once every
/// living player is standing in this area at the same time. The host keeps the clock and decides;
/// its synchronizer (a child) replicates both, including to late joiners.
/// </summary>
public partial class ChamberExit : Area3D
{
	public static ChamberExit Current { get; private set; }

	// Written by the host, replicated: the clock once a second (clients run it on between updates),
	// and the result the moment it's decided.
	[Export]
	public float SyncElapsed
	{
		get => _syncElapsed;
		set
		{
			_syncElapsed = value;
			_syncedAt = Now;
		}
	}
	[Export] public bool IsComplete { get; set; }
	[Export] public float FinalTime { get; set; }

	public float Elapsed => IsComplete ? FinalTime : _syncElapsed + (float)(Now - _syncedAt);

	private float _syncElapsed;
	private double _syncedAt;

	private static double Now => Time.GetTicksMsec() / 1000.0;

	public override void _EnterTree() => Current = this;

	public override void _ExitTree()
	{
		if (Current == this)
			Current = null;
	}

	public override void _Ready()
	{
		CollisionLayer = 0;
		CollisionMask = Layers.Players;
		Monitorable = false;
		_syncedAt = Now;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (IsComplete || !Multiplayer.IsServer())
			return;

		SyncElapsed = Elapsed;
		var living = Player.All.Where(p => !p.IsDead).ToList();
		var inside = GetOverlappingBodies();
		if (living.Count > 0 && living.All(inside.Contains))
		{
			FinalTime = Elapsed;
			IsComplete = true;
		}
	}
}
