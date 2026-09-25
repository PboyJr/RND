using Godot;
using RND.Players;

namespace RND.Vfx;

/// <summary>
/// Drives the full-screen visor shader (vfx/visor.gdshader) from the local player: cracks from health,
/// breath fog from the player's Breathing (the same rhythm you hear) and filter wear, choking from a
/// spent filter, sway from looking around, flashes from hits. Also keeps the projected-HUD viewport
/// the same size as the screen.
/// </summary>
public partial class Visor : ColorRect
{
	[Export] public SubViewport HudViewport { get; set; }

	[ExportGroup("Sway")]
	// Visor-space offset per rad/s of turning, and the cap on it.
	[Export] public float SwayPerTurnSpeed { get; set; } = 0.02f;
	[Export] public float MaxSway { get; set; } = 0.08f;
	[Export] public float SwaySpring { get; set; } = 10f;

	private ShaderMaterial _material;
	private Player _player;
	private float _damage;
	private float _hitFlash;
	private float _lastHealth;
	private float _lastYaw;
	private float _lastPitch;
	private Vector2 _sway;

	public override void _Ready()
	{
		_material = (ShaderMaterial)Material;
		_material.SetShaderParameter("hud_texture", HudViewport.GetTexture());
		GetViewport().SizeChanged += FitHudToScreen;
		FitHudToScreen();
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		Player player = Player.Local;
		if (player != _player)
			Bind(player);

		_material.SetShaderParameter("visor_enabled", player != null ? 1f : 0f);
		if (player == null)
			return;

		// The glass is the health bar. New damage spreads the cracks quickly; a fresh mask (respawn) is
		// instantly clean.
		float health = player.Health.Current;
		float targetDamage = 1f - health / player.Health.MaxHealth;
		_damage = targetDamage < _damage ? targetDamage : Mathf.MoveToward(_damage, targetDamage, dt * 3f);
		if (health < _lastHealth)
		{
			_hitFlash = 1f;
			_sway += Vector2.FromAngle((float)GD.RandRange(0.0, Mathf.Tau)) * 0.04f; // the mask jolts
		}
		_lastHealth = health;
		_hitFlash = Mathf.MoveToward(_hitFlash, 0f, dt * 3f);

		// Breath fog puffs on each exhale, harder when exerted; a tired filter leaves the mask clammier.
		Breathing breathing = player.Breathing;
		float exhale = breathing.Exhale;
		float clammy = (1f - player.Respirator.Fraction) * 0.25f;
		float fog = 0.04f + clammy + exhale * exhale * Mathf.Lerp(0.15f, 0.55f, breathing.Exertion) + breathing.Choke * 0.3f;

		// Sway: the rim lags behind where you look.
		float yaw = player.Rotation.Y;
		float pitch = player.SyncPitch;
		Vector2 turnSpeed = new Vector2(Mathf.AngleDifference(_lastYaw, yaw), pitch - _lastPitch) / Mathf.Max(dt, 0.001f);
		_lastYaw = yaw;
		_lastPitch = pitch;
		Vector2 target = new Vector2(turnSpeed.X, -turnSpeed.Y) * SwayPerTurnSpeed;
		_sway = _sway.Lerp(target.LimitLength(MaxSway), 1f - Mathf.Exp(-SwaySpring * dt));

		_material.SetShaderParameter("damage", _damage);
		_material.SetShaderParameter("fog", Mathf.Clamp(fog, 0f, 1f));
		_material.SetShaderParameter("choke", breathing.Choke);
		_material.SetShaderParameter("hit_flash", _hitFlash);
		_material.SetShaderParameter("sway", _sway);
	}

	private void Bind(Player player)
	{
		_player = player;
		if (player == null)
			return;

		_damage = 1f - player.Health.Current / player.Health.MaxHealth;
		_lastHealth = player.Health.Current;
		_lastYaw = player.Rotation.Y;
		_lastPitch = player.SyncPitch;
		_sway = Vector2.Zero;
	}

	private void FitHudToScreen() => HudViewport.Size = (Vector2I)GetViewport().GetVisibleRect().Size;
}
