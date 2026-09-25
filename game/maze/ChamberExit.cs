using System.Linq;
using Godot;
using RND.Core;
using RND.Players;

namespace RND.Maze;

/// <summary>
/// The end of a test chamber. The clock starts when the chamber loads; the test is passed once every
/// living player is standing in this area at the same time. The host decides and tells everyone.
/// </summary>
public partial class ChamberExit : Area3D
{
	public static ChamberExit Current { get; private set; }

	public bool IsComplete { get; private set; }
	public float Elapsed => IsComplete ? _finalTime : (float)(Now - _startedAt);

	private double _startedAt;
	private float _finalTime;

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
		// ponytail: each peer starts its own clock on load, so a late joiner's running clock is off
		// (the final time comes from the host). Sync a start time when late joining matters.
		_startedAt = Now;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (IsComplete || !Multiplayer.IsServer())
			return;

		var living = Player.All.Where(p => !p.IsDead).ToList();
		var inside = GetOverlappingBodies();
		if (living.Count > 0 && living.All(inside.Contains))
			Rpc(MethodName.Complete, Elapsed);
	}

	// ponytail: a late joiner misses this and never sees the chamber as done. Sync the state
	// (MultiplayerSynchronizer) once chambers chain into a maze.
	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void Complete(float seconds)
	{
		IsComplete = true;
		_finalTime = seconds;
	}
}
