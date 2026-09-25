using System.Collections.Generic;
using System.Linq;
using Godot;
using RND.Levels;
using RND.Players;

namespace RND.Vfx;

/// <summary>
/// Drives the full-screen visor shader (vfx/visor.gdshader) from the local player: cracks from hits
/// (physical health), a corrupted HUD from mental damage, breath fog from the player's Breathing (the
/// same rhythm you hear) and filter wear, choking from a spent filter, sway from looking around, flashes
/// from hits. Also keeps the projected-HUD viewport the same size as the screen.
/// </summary>
public partial class Visor : ColorRect
{
	private const int MaxImpacts = 12; // the size of `impacts` in visor.gdshader
	// Hits smaller than this (choking, grazes) spread the latest crack instead of starting a new one.
	private const float MinNewImpact = 0.06f;
	private const float AttackerReach = 4f;

	// One fracture per hit: where it struck the glass (visor space), its pattern, how big the hit was
	// (fraction of max health), and how far it has spread so far.
	private sealed class Impact
	{
		public Vector2 At;
		public float Seed;
		public float Size;
		public float Spread;
	}

	[Export] public SubViewport HudViewport { get; set; }

	// Look switches: the Hud's debug hotkeys flip these (F4–F6), and these checkboxes are the defaults.
	[ExportGroup("Effects")]
	[Export] public bool Grain { get; set; } = true;
	[Export] public bool ColourCrush { get; set; } = true;
	[Export] public bool Lens { get; set; } = true; // glass curve, edge blur, colour fringe

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
	private readonly List<Impact> _impacts = new();
	private readonly float[] _impactData = new float[MaxImpacts * 4];

	public int ImpactCount => _impacts.Count;

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

		_material.SetShaderParameter("grain_on", Grain);
		_material.SetShaderParameter("crush_on", ColourCrush);
		_material.SetShaderParameter("lens_on", Lens);
		_material.SetShaderParameter("visor_enabled", player != null ? 1f : 0f);
		if (player == null)
			return;

		// The glass is the (physical) health bar: every hit fractures it, and the weaker the mask, the
		// further all the cracks run. A fresh mask (respawn) is instantly clean. Mental damage doesn't
		// crack anything: it corrupts the projected HUD instead, and the worse it gets the more the cracks look
		// like blood (mind).
		float health = player.Health.Physical;
		float targetDamage = 1f - health / player.Health.MaxHealth;
		_damage = targetDamage < _damage ? targetDamage : Mathf.MoveToward(_damage, targetDamage, dt * 3f);
		if (health < _lastHealth)
		{
			_hitFlash = 1f;
			_sway += Vector2.FromAngle((float)GD.RandRange(0.0, Mathf.Tau)) * 0.04f; // the mask jolts
			Crack(player, (_lastHealth - health) / player.Health.MaxHealth);
		}
		else if (health >= player.Health.MaxHealth)
		{
			_impacts.Clear(); // ponytail: only a full mask clears; partial healing would need to mend cracks
		}
		_lastHealth = health;
		UploadImpacts(dt);
		_hitFlash = Mathf.MoveToward(_hitFlash, 0f, dt * 3f);

		// Breath fog puffs on each exhale, harder when exerted; a tired filter leaves the mask clammier.
		Breathing breathing = player.Breathing;
		float exhale = breathing.Exhale;
		float clammy = (1f - player.Respirator.Fraction) * 0.25f;
		float fog = 0.02f + clammy + exhale * exhale * Mathf.Lerp(0.075f, 0.275f, breathing.Exertion) + breathing.Choke * 0.3f;

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
		float mind = player.Health.Mental / player.Health.MaxHealth;
		_material.SetShaderParameter("mind", mind); // the shader eases it (same curve as VisorHud)
		_material.SetShaderParameter("hit_flash", _hitFlash);
		_material.SetShaderParameter("sway", _sway);
	}

	private void Bind(Player player)
	{
		_player = player;
		if (player == null)
			return;

		_damage = 1f - player.Health.Physical / player.Health.MaxHealth;
		_lastHealth = player.Health.Physical;
		_lastYaw = player.Rotation.Y;
		_lastPitch = player.SyncPitch;
		_sway = Vector2.Zero;
	}

	private void Crack(Player player, float size)
	{
		if (_impacts.Count > 0 && (size < MinNewImpact || _impacts.Count == MaxImpacts))
			_impacts[^1].Size += size;
		else
			_impacts.Add(new Impact { At = ImpactPoint(player), Seed = GD.Randf() * 100f, Size = size });
	}

	// Where a hit lands on the glass: toward whatever hit us (the nearest evil guy within reach, as seen
	// through the visor; from behind or the side, the rim on that side), with some scatter. Nothing
	// nearby (choking, a fall): anywhere on the glass.
	private Vector2 ImpactPoint(Player player)
	{
		var glass = new Vector2(1.35f, 0.75f); // roughly the visor's inner half-size
		Camera3D camera = GetViewport().GetCamera3D();
		Node3D attacker = Level.Current?.Enemies
			.Where(enemy => enemy.GlobalPosition.DistanceTo(player.GlobalPosition) < AttackerReach)
			.MinBy(enemy => enemy.GlobalPosition.DistanceTo(player.GlobalPosition));
		if (attacker == null || camera == null)
			return new Vector2(GD.Randf() * 2f - 1f, GD.Randf() * 2f - 1f) * glass * 0.8f;

		// Camera space (-Z forward) to visor space: the same perspective the screen uses.
		Vector3 local = camera.GlobalTransform.AffineInverse() * (attacker.GlobalPosition + Vector3.Up * 1.2f);
		float spread = Mathf.Tan(Mathf.DegToRad(camera.Fov) * 0.5f) * Mathf.Max(-local.Z, 0.3f);
		Vector2 onGlass = new Vector2(local.X, local.Y) / spread;
		Vector2 scatter = new Vector2(GD.Randf() - 0.5f, GD.Randf() - 0.5f) * 0.4f;
		return (onGlass / glass).LimitLength(1f) * glass + scatter;
	}

	// Cracks race out to their full size in a moment, then the shader draws them.
	private void UploadImpacts(float dt)
	{
		for (int i = 0; i < _impacts.Count; i++) // the shader stops at impact_count
		{
			Impact impact = _impacts[i];
			impact.Spread = Mathf.MoveToward(impact.Spread, Mathf.Min(impact.Size, 1f), dt * 2f);
			new[] { impact.At.X, impact.At.Y, impact.Seed, impact.Spread }.CopyTo(_impactData, i * 4);
		}
		_material.SetShaderParameter("impacts", _impactData);
		_material.SetShaderParameter("impact_count", _impacts.Count);
	}

	private void FitHudToScreen() => HudViewport.Size = (Vector2I)GetViewport().GetVisibleRect().Size;
}
