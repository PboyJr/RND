using System.Linq;
using Godot;
using RND.Maze;
using RND.UI;

namespace RND.Core;

/// <summary>
/// Root scene. Swaps between the menu and the current level.
/// Only the host loads levels; LevelSpawner replicates them to clients, including late joiners.
/// </summary>
public partial class Main : Node
{
	/// <summary>What the host can pick in the menu. The first is the default. Each must also be in LevelSpawner.</summary>
	[Export] public Godot.Collections.Array<PackedScene> Levels { get; set; } = new();

	private Node _levelRoot;
	private MainMenu _menu;
	private Hud _hud;
	private MazeRun _run;

	public override void _Ready()
	{
		_levelRoot = GetNode("Level");
		_menu = GetNode<MainMenu>("UI/MainMenu");
		_hud = GetNode<Hud>("UI/Hud");
		_run = GetNode<MazeRun>("Run");
		// A maze chamber in the list means "start a maze run" rather than that one chamber.
		_menu.SetLevels(Levels.Select(level => _run.Has(level) ? "Maze run" : level.ResourcePath.GetFile().GetBaseName().Capitalize()));

		Network.Instance.SessionStarted += OnSessionStarted;
		Network.Instance.SessionEnded += OnSessionEnded;
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
		ClearLevel();
		_hud.Close();
		_menu.Open(reason);
	}

	public void ChangeLevel(PackedScene level)
	{
		ClearLevel();
		_levelRoot.AddChild(level.Instantiate());
	}

	private void ClearLevel()
	{
		foreach (Node child in _levelRoot.GetChildren())
		{
			_levelRoot.RemoveChild(child);
			child.QueueFree();
		}
	}
}
