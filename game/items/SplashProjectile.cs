using System.Collections.Generic;
using Godot;
using RND.Audio;
using RND.Combat;
using RND.Core;
using RND.Levels;
using RND.Players;

namespace RND.Items;

/// <summary>
/// A thrown flask that shatters on the first thing it hits and splashes damage around it.
/// The host spawns it; ProjectileSpawner copies it to clients along with its starting velocity,
/// so every peer flies the same arc locally. Only the host's copy deals damage.
/// </summary>
public partial class SplashProjectile : Node3D
{
	[Export] public float Damage { get; set; } = 50f;
	[Export] public float SplashRadius { get; set; } = 2f;
	// Damage at the edge of the splash, as a fraction of Damage. A direct hit always deals full damage.
	[Export(PropertyHint.Range, "0,1,0.05")] public float EdgeDamageFraction { get; set; } = 0.5f;
	[Export] public float Gravity { get; set; } = 12f;
	[Export] public float MaxLifetime { get; set; } = 5f;
	[Export] public bool DamagesPlayers { get; set; }
	[Export] public Color SplashColor { get; set; } = new(0.45f, 1f, 0.3f, 0.8f);
	// Shattering glass is loud: enemies this far away come to look (walls halve it).
	[Export] public float ShatterNoiseRadius { get; set; } = 14f;

	// Set by the host before spawning; replicated once, on spawn.
	[ExportGroup("Network")]
	[Export] public Vector3 Velocity { get; set; }
	[Export] public int ThrowerId { get; set; }

	// Shatters on world geometry, props and entities. Passes through players so teammates don't block throws.
	private const uint HitMask = Layers.World | Layers.Props | Layers.Entities;

	private Node3D _model;
	private float _age;
	private bool _shattered;

	// The model starts hidden (see the scene): it launches from the thrower's eyes and is shown once
	// it's clear of their camera.
	public override void _Ready() => _model = GetNode<Node3D>("Model");

	public override void _PhysicsProcess(double delta)
	{
		if (_shattered)
			return;

		float dt = (float)delta;
		_age += dt;
		Velocity += Vector3.Down * Gravity * dt;

		Vector3 from = GlobalPosition;
		Vector3 to = from + Velocity * dt;
		var hit = GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(from, to, HitMask));
		if (hit.Count > 0)
		{
			Shatter((Vector3)hit["position"], (Vector3)hit["normal"], hit["collider"].As<Node>());
			return;
		}

		GlobalPosition = to;
		_model.RotateObjectLocal(Vector3.Right, 12f * dt); // tumble
		_model.Visible = _age > 0.03f;

		if (_age > MaxLifetime)
			Shatter(GlobalPosition, Vector3.Up, null);
	}

	private void Shatter(Vector3 point, Vector3 normal, Node directHit)
	{
		_shattered = true;
		Visible = false;

		// Clients just hide their copy; the host's copy deals damage, plays the splash and despawns everyone's.
		if (!Multiplayer.IsServer())
			return;

		DealSplashDamage(point, directHit);
		Level.Current?.Effects.Rpc(nameof(Vfx.Effects.Splash), point, normal, SplashColor, SplashRadius);
		Level.Current?.EmitSound(point, ShatterNoiseRadius, SoundKind.Shatter);
		QueueFree();
	}

	private void DealSplashDamage(Vector3 point, Node directHit)
	{
		var query = new PhysicsShapeQueryParameters3D
		{
			Shape = new SphereShape3D { Radius = SplashRadius },
			Transform = new Transform3D(Basis.Identity, point),
			CollisionMask = DamagesPlayers ? Layers.Entities | Layers.Players : Layers.Entities,
		};

		var alreadyHit = new HashSet<Health>();
		foreach (var result in GetWorld3D().DirectSpaceState.IntersectShape(query, 32))
		{
			var body = result["collider"].As<Node3D>();
			Health health = Health.Of(body);
			if (health == null || !alreadyHit.Add(health))
				continue;
			if (body is Player player && player.PeerId == ThrowerId)
				continue;

			float distance = Mathf.Clamp(point.DistanceTo(body.GlobalPosition) / SplashRadius, 0f, 1f);
			float scale = body == directHit ? 1f : Mathf.Lerp(1f, EdgeDamageFraction, distance);
			health.TakeDamage(Damage * scale, ThrowerId);
		}
	}
}
