using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using RND.Maze;
using RND.Players;
using RND.UI;

namespace RND.Core;

/// <summary>
/// Root scene. Swaps between the menu and the current level.
/// Only the host loads levels; LevelSpawner replicates them to clients, including late joiners.
/// </summary>
public partial class Main : Node
{
	private const double SwapTimeout = 1.0; // don't wait longer than this for clients to confirm a level change

	/// <summary>What the host can pick in the menu. The first is the default. Each must also be in LevelSpawner.</summary>
	[Export] public Godot.Collections.Array<PackedScene> Levels { get; set; } = new();

	private Node _levelRoot;
	private MainMenu _menu;
	private Hud _hud;
	private MazeRun _run;

	// Host, while a level change waits for clients to stop reporting their players (see ChangeLevel):
	// what to load once they have.
	private Action _swap;
	private readonly HashSet<long> _unconfirmed = new();
	private double _swapBy;
	private MultiplayerSpawner _levelSpawner;

	private static double Now => Time.GetTicksMsec() / 1000.0;

	/// <summary>Host: a level change is waiting on clients. The old level is still there until it's done.</summary>
	public bool ChangingLevel => _swap != null;

	public override void _Ready()
	{
		_levelRoot = GetNode("Level");
		_menu = GetNode<MainMenu>("UI/MainMenu");
		_hud = GetNode<Hud>("UI/Hud");
		_run = GetNode<MazeRun>("Run");
		// Generated chambers: the host spawns [seed, difficulty], and every peer builds the room from it.
		_levelSpawner = GetNode<MultiplayerSpawner>("LevelSpawner");
		_levelSpawner.SpawnFunction = Callable.From<Variant, Node>(data =>
		{
			var args = data.AsGodotArray();
			return _run.BuildGenerated(args[0].AsInt32(), args[1].AsSingle());
		});
		// A maze chamber in the list means "start a maze run" rather than that one chamber.
		_menu.SetLevels(Levels.Select(level => _run.Has(level) ? "Maze run" : level.ResourcePath.GetFile().GetBaseName().Capitalize()));

		Network.Instance.SessionStarted += OnSessionStarted;
		Network.Instance.SessionEnded += OnSessionEnded;
		Multiplayer.PeerDisconnected += id => _unconfirmed.Remove(id);
	}

	public override void _Process(double delta)
	{
		if (_swap != null && (_unconfirmed.Count == 0 || Now > _swapBy))
			SwapLevel();
	}

	private void OnSessionStarted()
	{
		_menu.Hide();
		_hud.Open();

		if (!Multiplayer.IsServer())
			return;

		PackedScene picked = Levels[_menu.SelectedLevel];
		Callable.From(() =>
		{
			if (_run.Has(picked))
				_run.Start();
			else
				ChangeLevel(picked);
		}).CallDeferred();
	}

	private void OnSessionEnded(string reason)
	{
		_run.Stop();
		_swap = null;
		ClearLevel();
		_hud.Close();
		_menu.Open(reason);
	}

	/// <summary>
	/// Host only. Clients report their own player's state to the host all the time; a report still on
	/// its way when the host removes the level would arrive for a player that no longer exists. So the
	/// host first asks clients to stop, and swaps once they've all said so (or after a second).
	/// </summary>
	public void ChangeLevel(PackedScene level) => BeginChange(() => _levelRoot.AddChild(level.Instantiate()));

	/// <summary>Host only. Loads a generated chamber (see MazeRun.BuildGenerated) the same way.</summary>
	public void ChangeToGenerated(int seed, float difficulty) => BeginChange(() => _levelSpawner.Spawn(new Godot.Collections.Array { seed, difficulty }));

	private void BeginChange(Action swap)
	{
		_swap = swap;
		_unconfirmed.Clear();
		if (_levelRoot.GetChildCount() > 0)
			foreach (int peer in Multiplayer.GetPeers())
				_unconfirmed.Add(peer);
		_swapBy = Now + SwapTimeout;
		if (_unconfirmed.Count > 0)
			Rpc(MethodName.LevelEnding);
		else
			SwapLevel();
	}

	private void SwapLevel()
	{
		Action swap = _swap;
		_swap = null;
		ClearLevel();
		swap();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = Network.StateChannel)]
	private void LevelEnding()
	{
		Player.Local?.StopReporting();
		RpcId(1, MethodName.LevelEndingConfirmed);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = Network.StateChannel)]
	private void LevelEndingConfirmed() => _unconfirmed.Remove(Multiplayer.GetRemoteSenderId());

	private void ClearLevel()
	{
		foreach (Node child in _levelRoot.GetChildren())
		{
			_levelRoot.RemoveChild(child);
			child.QueueFree();
		}
	}
}
