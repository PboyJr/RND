using Godot;
using RND.Core;
using RND.Players;

namespace RND.UI;

/// <summary>In-game overlay: crosshair, context hint, session status and the pause menu.</summary>
public partial class Hud : Control
{
	private Label _hint;
	private Label _status;
	private Control _pauseMenu;

	public override void _Ready()
	{
		_hint = GetNode<Label>("Hint");
		_status = GetNode<Label>("Status");
		_pauseMenu = GetNode<Control>("PauseMenu");

		GetNode<Button>("%Resume").Pressed += () => SetPaused(false);
		GetNode<Button>("%Leave").Pressed += () => Network.Instance.Leave();
	}

	public void Open()
	{
		Show();
		SetPaused(false);
	}

	public void Close()
	{
		Hide();
		_pauseMenu.Hide();
		Input.MouseMode = Input.MouseModeEnum.Visible;
	}

	public override void _Process(double delta)
	{
		if (!Visible)
			return;

		_hint.Text = Player.Local?.Hint ?? "";
		int playerCount = Multiplayer.GetPeers().Length + 1;
		_status.Text = $"{(Multiplayer.IsServer() ? "Hosting" : "Connected")}  ·  {playerCount} in session";
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (Visible && @event.IsActionPressed("pause"))
		{
			SetPaused(!_pauseMenu.Visible);
			GetViewport().SetInputAsHandled();
		}
	}

	// Multiplayer never actually pauses the game; this just frees the mouse.
	private void SetPaused(bool paused)
	{
		_pauseMenu.Visible = paused;
		Input.MouseMode = paused ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
	}
}
