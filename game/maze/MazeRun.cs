using System.Linq;
using Godot;
using RND.Core;
using RND.Players;

namespace RND.Maze;

public enum RunResult { None, Passed, Failed }

/// <summary>
/// A maze run: a string of test chambers, one after another. Passing a chamber loads the next; the
/// run is passed after the last one, and failed if every player is down at once. After the result
/// a new run starts. The host runs it and tells everyone the state (HUD only); the chambers
/// themselves reach clients through the LevelSpawner like any level.
/// </summary>
public partial class MazeRun : Node
{
	public static MazeRun Current { get; private set; }

	[Export] public Godot.Collections.Array<PackedScene> Chambers { get; set; } = [];
	[Export] public int Length { get; set; } = 3;
	[Export] public float PauseSeconds { get; set; } = 4f;   // "test complete" before the next chamber loads
	[Export] public float ResultSeconds { get; set; } = 8f;  // the result screen before the next run

	public bool Active { get; private set; }
	public int Chamber { get; private set; }  // 0-based
	public RunResult Result { get; private set; }

	private double _nextAt = -1; // host: when to move on (next chamber or next run)

	private static double Now => Time.GetTicksMsec() / 1000.0;

	public override void _EnterTree() => Current = this;

	public override void _ExitTree()
	{
		if (Current == this)
			Current = null;
	}

	public override void _Ready() => Multiplayer.PeerConnected += OnPeerConnected;

	public bool Has(PackedScene level) => Chambers.Any(chamber => chamber.ResourcePath == level.ResourcePath);

	/// <summary>Host only. Starts a fresh run from the first chamber.</summary>
	public void Start()
	{
		Active = true;
		Result = RunResult.None;
		Chamber = -1;
		NextChamber();
	}

	/// <summary>Every peer, when the session ends.</summary>
	public void Stop()
	{
		Active = false;
		_nextAt = -1;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!Active || !Multiplayer.IsServer())
			return;

		if (_nextAt >= 0)
		{
			if (Now < _nextAt)
				return;
			_nextAt = -1;
			if (Result == RunResult.None)
				NextChamber();
			else
				Start();
			return;
		}

		if (Player.All.Any() && Player.All.All(player => player.IsDead))
			Finish(RunResult.Failed);
		else if (ChamberExit.Current is { IsComplete: true })
		{
			if (Chamber + 1 >= Length)
				Finish(RunResult.Passed);
			else
				_nextAt = Now + PauseSeconds;
		}
	}

	private void NextChamber()
	{
		Chamber++;
		// ponytail: random pick from the pool, so with one chamber it just repeats. Pick by difficulty
		// (DESIGN: ramp through the run, start from the party's level) once there are more chambers.
		GetParent<Main>().ChangeLevel(Chambers[GD.RandRange(0, Chambers.Count - 1)]);
		Broadcast();
	}

	private void Finish(RunResult result)
	{
		Result = result;
		_nextAt = Now + ResultSeconds;
		Broadcast();
	}

	private void Broadcast() => Rpc(MethodName.SyncState, Active, Chamber, Length, (int)Result);

	private void OnPeerConnected(long peerId)
	{
		if (Multiplayer.IsServer() && Active)
			RpcId(peerId, MethodName.SyncState, Active, Chamber, Length, (int)Result);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void SyncState(bool active, int chamber, int length, int result)
	{
		Active = active;
		Chamber = chamber;
		Length = length;
		Result = (RunResult)result;
	}
}
