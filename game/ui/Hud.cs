using Godot;
using RND.Core;
using RND.Players;

namespace RND.UI;

/// <summary>In-game overlay: crosshair, hotbar, health, context hint, session status and the pause menu.</summary>
public partial class Hud : Control
{
	private Label _hint;
	private Label _status;
	private Control _pauseMenu;
	private HotbarView _hotbar;
	private ProgressBar _healthBar;
	private Label _healthText;
	private ColorRect _damageFlash;
	private Label _deathLabel;
	private Tween _damageTween;

	private Player _boundPlayer;
	private double _respawnAt;

	private static double Now => Time.GetTicksMsec() / 1000.0;

	public override void _Ready()
	{
		_hint = GetNode<Label>("Hint");
		_status = GetNode<Label>("Status");
		_pauseMenu = GetNode<Control>("PauseMenu");
		_hotbar = GetNode<HotbarView>("Hotbar");
		_healthBar = GetNode<ProgressBar>("Health/Bar");
		_healthText = GetNode<Label>("Health/Text");
		_damageFlash = GetNode<ColorRect>("DamageFlash");
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

		Player local = Player.Local;
		if (local != _boundPlayer)
			Bind(local);

		_hotbar.Refresh(local);
		_hint.Text = local?.Hint ?? "";

		if (local != null)
		{
			_healthBar.MaxValue = local.Health.MaxHealth;
			_healthBar.Value = local.Health.Current;
			_healthText.Text = $"{Mathf.CeilToInt(local.Health.Current)} / {Mathf.CeilToInt(local.Health.MaxHealth)}";

			_deathLabel.Visible = local.IsDead;
			if (local.IsDead)
				_deathLabel.Text = $"You died.\nRespawning in {Mathf.CeilToInt(Mathf.Max(0.0, _respawnAt - Now))}…";
		}

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

	private void Bind(Player player)
	{
		if (_boundPlayer != null && IsInstanceValid(_boundPlayer))
		{
			_boundPlayer.Health.Damaged -= OnLocalDamaged;
			_boundPlayer.Health.Died -= OnLocalDied;
		}

		_boundPlayer = player;
		if (player == null)
			return;

		player.Health.Damaged += OnLocalDamaged;
		player.Health.Died += OnLocalDied;
	}

	private void OnLocalDamaged(float amount, int sourcePeerId)
	{
		_damageTween?.Kill();
		_damageFlash.Color = new Color(0.7f, 0f, 0f, Mathf.Clamp(amount / 40f, 0.2f, 0.5f));
		_damageTween = CreateTween();
		_damageTween.TweenProperty(_damageFlash, "color:a", 0f, 0.5f);
	}

	private void OnLocalDied(int sourcePeerId) => _respawnAt = Now + _boundPlayer.RespawnDelay;

	// Multiplayer never actually pauses the game; this just frees the mouse.
	private void SetPaused(bool paused)
	{
		_pauseMenu.Visible = paused;
		Input.MouseMode = paused ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
	}
}
