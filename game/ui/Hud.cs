using Godot;
using RND.Core;
using RND.Players;

namespace RND.UI;

/// <summary>
/// The crisp overlay outside the mask: pause menu, death message, session status, and a deliberately
/// unstyled debug readout of health and filter. (The real health bar is the visor's cracks, and the
/// in-mask HUD is VisorHud.)
/// </summary>
public partial class Hud : Control
{
	private Label _status;
	private Control _pauseMenu;
	private ProgressBar _debugHealth;
	private Label _debugFilter;
	private Label _deathLabel;
	private double _respawnAt;
	private bool _wasDead;

	private static double Now => Time.GetTicksMsec() / 1000.0;

	public override void _Ready()
	{
		_status = GetNode<Label>("Status");
		_pauseMenu = GetNode<Control>("PauseMenu");
		_debugHealth = GetNode<ProgressBar>("DebugHealth");
		_debugFilter = GetNode<Label>("DebugFilter");
		_deathLabel = GetNode<Label>("DeathLabel");

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

		int playerCount = Multiplayer.GetPeers().Length + 1;
		_status.Text = $"{(Multiplayer.IsServer() ? "Hosting" : "Connected")}  ·  {playerCount} in session";

		Player local = Player.Local;
		_debugHealth.Visible = _debugFilter.Visible = local != null;
		if (local == null)
			return;

		_debugHealth.MaxValue = local.Health.MaxHealth;
		_debugHealth.Value = local.Health.Current;
		_debugFilter.Text = $"filter {Mathf.CeilToInt(local.Respirator.Fraction * 100f)}% ({Mathf.CeilToInt(local.Respirator.Remaining)} s)";

		if (local.IsDead && !_wasDead)
			_respawnAt = Now + local.RespawnDelay;
		_wasDead = local.IsDead;
		_deathLabel.Visible = local.IsDead;
		if (local.IsDead)
			_deathLabel.Text = $"You died.\nRespawning in {Mathf.CeilToInt(Mathf.Max(0.0, _respawnAt - Now))}…";
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
