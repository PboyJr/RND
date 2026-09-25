using System.Linq;
using Godot;
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

	public override void _Ready()
	{
		_levelRoot = GetNode("Level");
		_menu = GetNode<MainMenu>("UI/MainMenu");
		_hud = GetNode<Hud>("UI/Hud");
		_menu.SetLevels(Levels.Select(level => level.ResourcePath.GetFile().GetBaseName().Capitalize()));

		Network.Instance.SessionStarted += OnSessionStarted;
		Network.Instance.SessionEnded += OnSessionEnded;
	}

	private void OnSessionStarted()
	{
		_menu.Hide();
		_hud.Open();

		if (Multiplayer.IsServer())
			Callable.From(() => ChangeLevel(Levels[_menu.SelectedLevel])).CallDeferred();
	}

	private void OnSessionEnded(string reason)
	{
		ClearLevel();
		_hud.Close();
		_menu.Open(reason);
	}

	private void ChangeLevel(PackedScene level)
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
