using System.Linq;
using Godot;
using RND.Core;
using RND.Players;
using RND.Vfx;

namespace RND.UI;

/// <summary>
/// The crisp overlay outside the mask: pause menu, death message, session status, a deliberately
/// unstyled debug readout of health (total), how far gone your mind is (purple bar) and filter, and
/// debug hotkeys that switch parts of the look on and off to compare. (The real health bar is the visor's cracks, and the in-mask HUD is VisorHud.)
/// </summary>
public partial class Hud : Control
{
	[Export] public ToonStyle Toon { get; set; }
	[Export] public Visor Visor { get; set; }

	// Key, label, and the bool property it flips. Defaults are those properties in the Inspector.
	private (Key Key, string Label, GodotObject Target, string Property)[] _viewToggles;
	private Label _viewTogglesList;
	private double _viewTogglesShownUntil;

	private Label _status;
	private Control _pauseMenu;
	private ProgressBar _debugHealth;
	private ProgressBar _debugMental;
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
		_debugMental = GetNode<ProgressBar>("DebugMental");
		_debugFilter = GetNode<Label>("DebugFilter");
		_deathLabel = GetNode<Label>("DeathLabel");
		_viewTogglesList = GetNode<Label>("ViewToggles");
		_viewToggles = new (Key, string, GodotObject, string)[]
		{
			(Key.F2, "cel shading", Toon, nameof(ToonStyle.Enabled)),
			(Key.F3, "outlines", Toon, nameof(ToonStyle.Outlines)),
			(Key.F4, "film grain", Visor, nameof(Visor.Grain)),
			(Key.F5, "colour crush", Visor, nameof(Visor.ColourCrush)),
			(Key.F6, "lens (curve, edge blur, fringe)", Visor, nameof(Visor.Lens)),
		};

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

		_viewTogglesList.Visible = Now < _viewTogglesShownUntil;
		if (_viewTogglesList.Visible)
			_viewTogglesList.Text = string.Join("\n", _viewToggles.Select(t =>
				$"{t.Key}  {t.Label}: {(t.Target.Get(t.Property).AsBool() ? "on" : "off")}"));

		int playerCount = Multiplayer.GetPeers().Length + 1;
		_status.Text = $"{(Multiplayer.IsServer() ? "Hosting" : "Connected")}  ·  {playerCount} in session";

		Player local = Player.Local;
		_debugHealth.Visible = _debugMental.Visible = _debugFilter.Visible = local != null;
		if (local == null)
			return;

		_debugHealth.MaxValue = local.Health.MaxHealth;
		_debugHealth.Value = local.Health.Current;
		_debugMental.MaxValue = 1.0; // how far gone your mind is (full at the mental floor, 25 HP)
		_debugMental.Value = local.Health.MentalFraction;
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
			return;
		}

		if (@event is not InputEventKey { Pressed: true, Echo: false } key)
			return;
		var toggle = _viewToggles.FirstOrDefault(t => t.Key == key.Keycode);
		if (toggle.Target == null)
			return;
		toggle.Target.Set(toggle.Property, !toggle.Target.Get(toggle.Property).AsBool());
		_viewTogglesShownUntil = Now + 3.0; // flash the list so you can see what's on
		GetViewport().SetInputAsHandled();
	}

	// Multiplayer never actually pauses the game; this just frees the mouse.
	private void SetPaused(bool paused)
	{
		_pauseMenu.Visible = paused;
		Input.MouseMode = paused ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
	}
}
