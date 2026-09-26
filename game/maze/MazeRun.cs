using System.Collections.Generic;
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
/// Every player's own profile counts the run when it starts, and gets the run's pay at the end: the
/// host works the pay out and sends it; each player's game applies it and saves.
/// </summary>
public partial class MazeRun : Node
{
	public static MazeRun Current { get; private set; }

	[Export] public Godot.Collections.Array<PackedScene> Chambers { get; set; } = [];
	[Export] public int Length { get; set; } = 3;
	[Export] public float PauseSeconds { get; set; } = 4f;   // "test complete" before the next chamber loads
	[Export] public float ResultSeconds { get; set; } = 8f;  // the result screen before the next run

	[ExportGroup("Pay")]
	// Per chamber passed, whether or not the run is; the bonus only for passing the whole run.
	[Export] public int PayPerChamber { get; set; } = 100;
	[Export] public int XpPerChamber { get; set; } = 50;
	[Export] public int PassBonusPay { get; set; } = 200;
	[Export] public int PassBonusXp { get; set; } = 100;

	[ExportGroup("Difficulty")]
	// A party at this level (or above) starts its runs this far up the chamber list; level 1 starts
	// at the easiest. Runs always end at the hardest.
	[Export] public float FullStartLevel { get; set; } = 20f;
	[Export] public float MaxStartFraction { get; set; } = 0.5f;

	public bool Active { get; private set; }
	public int Chamber { get; private set; }  // 0-based
	public RunResult Result { get; private set; }
	/// <summary>Every peer: the pay from the last run, for the result screen.</summary>
	public (int Money, int Xp, int Levels) LastPay { get; private set; }

	/// <summary>Host: the party's level for difficulty, 0.7 × average + 0.3 × highest (DESIGN).</summary>
	public float PartyLevel
	{
		get
		{
			var levels = _levels.Values.Append(ProfileStore.Instance?.Level ?? 1).ToList();
			return 0.7f * (float)levels.Average() + 0.3f * levels.Max();
		}
	}

	private double _nextAt = -1; // host: when to move on (next chamber or next run)
	private int _runId;          // host: numbers the runs of this session
	private int _countedRun;     // every peer: the run its profile last counted
	private int _paidRun;        // every peer: the run it was last paid for
	private readonly Dictionary<long, int> _levels = new(); // host: each client's (clamped) level

	private static double Now => Time.GetTicksMsec() / 1000.0;

	public override void _EnterTree() => Current = this;

	public override void _ExitTree()
	{
		if (Current == this)
			Current = null;
	}

	public override void _Ready()
	{
		Multiplayer.PeerConnected += OnPeerConnected;
		Multiplayer.PeerDisconnected += id => _levels.Remove(id);
		// Tell the host our level (its difficulty formula reads everyone's). It clamps what it gets.
		Multiplayer.ConnectedToServer += () => RpcId(1, MethodName.ReportLevel, ProfileStore.Instance?.Level ?? 1);
	}

	public bool Has(PackedScene level) => Chambers.Any(chamber => chamber.ResourcePath == level.ResourcePath);

	/// <summary>Host only. Starts a fresh run from the first chamber.</summary>
	public void Start()
	{
		Active = true;
		Result = RunResult.None;
		Chamber = -1;
		_runId++;
		NextChamber();
	}

	/// <summary>Every peer, when the session ends.</summary>
	public void Stop()
	{
		Active = false;
		_nextAt = -1;
		_runId = _countedRun = _paidRun = 0; // run numbers belong to one session
		_levels.Clear();
	}

	public override void _PhysicsProcess(double delta)
	{
		// While the next chamber is on its way, the old one (already passed) is still loaded.
		if (!Active || !Multiplayer.IsServer() || GetParent<Main>().ChangingLevel)
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
		GetParent<Main>().ChangeLevel(PickChamber());
		Broadcast();
	}

	// Chambers are listed easiest first, and a run climbs the list to its hardest. A stronger party
	// starts further up. Between two chambers of the list, it picks one at random, leaning to the
	// nearer, so a longer list gives varied runs.
	private PackedScene PickChamber()
	{
		int last = Chambers.Count - 1;
		float start = Mathf.Clamp((PartyLevel - 1f) / (FullStartLevel - 1f), 0f, 1f) * MaxStartFraction * last;
		float step = Length <= 1 ? start : start + Chamber * (last - start) / (Length - 1);
		int index = Mathf.FloorToInt(step) + (GD.Randf() < step - Mathf.Floor(step) ? 1 : 0);
		return Chambers[Mathf.Clamp(index, 0, last)];
	}

	private void Finish(RunResult result)
	{
		Result = result;
		_nextAt = Now + ResultSeconds;
		Broadcast();

		int passed = Chamber + (result == RunResult.Passed ? 1 : 0);
		bool bonus = result == RunResult.Passed;
		Rpc(MethodName.Pay, _runId, passed * PayPerChamber + (bonus ? PassBonusPay : 0), passed * XpPerChamber + (bonus ? PassBonusXp : 0));
	}

	private void Broadcast() => Rpc(MethodName.SyncState, Active, Chamber, Length, (int)Result, _runId);

	private void OnPeerConnected(long peerId)
	{
		if (Multiplayer.IsServer() && Active)
			RpcId(peerId, MethodName.SyncState, Active, Chamber, Length, (int)Result, _runId);
	}

	// A new run counts on each player's profile as soon as they're in it, joining late included, so
	// quitting mid-run can't dodge the count (DESIGN: decay counts runs).
	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void SyncState(bool active, int chamber, int length, int result, int runId)
	{
		Active = active;
		Chamber = chamber;
		Length = length;
		Result = (RunResult)result;
		if (active && runId != _countedRun)
		{
			_countedRun = runId;
			ProfileStore.Instance?.StartRun();
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void Pay(int runId, int money, int xp)
	{
		if (runId == _paidRun)
			return;
		_paidRun = runId;
		int levels = ProfileStore.Instance?.Grant(money, xp) ?? 0;
		LastPay = (money, xp, levels);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ReportLevel(int level)
	{
		if (Multiplayer.IsServer())
			_levels[Multiplayer.GetRemoteSenderId()] = Mathf.Clamp(level, 1, Profile.MaxLevel);
	}
}
