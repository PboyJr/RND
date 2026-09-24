using Godot;
using RND.Combat;
using RND.Core;
using RND.Levels;
using RND.Players;
using RND.Vfx;

namespace RND.Enemies;

/// <summary>
/// The evil guy. Host-authoritative: the host runs the AI and replicates position, facing and the
/// attack telegraph; clients smooth toward it, the same way they handle remote players.
///
/// Behaviour: idle / wander near home → chase the closest player it can see → wind up (eyes flare)
/// → swing → brief recovery. Getting hit makes it go after whoever hit it.
/// </summary>
public partial class Enemy : CharacterBody3D
{
	[Signal] public delegate void DefeatedEventHandler();

	private enum State { Idle, Wander, Chase, Windup, Recover }

	private const float RemoteSmoothing = 14f;
	private const float RemoteSnapDistance = 3f;
	private const float RepathInterval = 0.25f;
	private static readonly Color HitColor = new(0.6f, 1f, 0.4f);

	[ExportGroup("Movement")]
	[Export] public float WanderSpeed { get; set; } = 1.8f;
	[Export] public float ChaseSpeed { get; set; } = 4.3f; // players walk at 4, sprint at 6.5
	[Export] public float WanderRadius { get; set; } = 8f;
	[Export] public float TurnRate { get; set; } = 8f;

	[ExportGroup("Senses")]
	[Export] public float SightRange { get; set; } = 12f;
	[Export] public float LoseInterestRange { get; set; } = 20f;
	// Keeps chasing this long after losing sight of its target.
	[Export] public float MemorySeconds { get; set; } = 4f;
	[Export] public float EyeHeight { get; set; } = 2f;

	[ExportGroup("Attack")]
	[Export] public float AttackDamage { get; set; } = 20f;
	// Starts a swing when a player is this close...
	[Export] public float AttackRange { get; set; } = 1.6f;
	// ...and the swing lands if they're still this close when the windup ends.
	[Export] public float AttackReach { get; set; } = 2.2f;
	[Export] public float WindupSeconds { get; set; } = 0.45f;
	[Export] public float RecoverSeconds { get; set; } = 1f;

	// Written by the host, replicated to clients.
	[ExportGroup("Network")]
	[Export] public Vector3 SyncPosition { get; set; }
	[Export] public float SyncYaw { get; set; }
	[Export] public bool Telegraphing { get; set; }

	public Health Health { get; private set; }

	private readonly RandomNumberGenerator _rng = new();
	private NavigationAgent3D _agent;
	private Label3D _healthLabel;
	private StandardMaterial3D _bodyMaterial;
	private StandardMaterial3D _eyeMaterial;
	private Tween _flashTween;
	private float _gravity;

	private State _state = State.Idle;
	private float _stateTimer;
	private Player _target;
	private float _timeSinceSeen;
	private float _repathTimer;
	private float _moveSpeed;
	private Vector3 _home;

	public override void _Ready()
	{
		Health = GetNode<Health>("Health");
		_agent = GetNode<NavigationAgent3D>("NavigationAgent3D");
		_healthLabel = GetNode<Label3D>("HealthLabel");
		_gravity = (float)ProjectSettings.GetSetting("physics/3d/default_gravity");
		SetUpMaterials();

		Health.Damaged += OnDamaged;
		Health.Died += OnDied;
		Health.Changed += (_, _) => UpdateHealthLabel();
		UpdateHealthLabel();

		if (Multiplayer.IsServer())
		{
			_home = GlobalPosition;
			SyncPosition = GlobalPosition;
			SyncYaw = Rotation.Y;
			EnterState(State.Idle, _rng.RandfRange(1f, 3f));
		}
		else if (SyncPosition != Vector3.Zero)
		{
			GlobalPosition = SyncPosition;
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;

		if (!Multiplayer.IsServer())
		{
			FollowNetworkState(dt);
		}
		else if (!Health.IsDead)
		{
			Think(dt);
			Move(dt);
			SyncPosition = GlobalPosition;
			SyncYaw = Rotation.Y;
		}

		_eyeMaterial.EmissionEnergyMultiplier = Telegraphing ? 10f : 2.5f;
	}

	// ── AI (host only) ──────────────────────────────────────────────────────────

	private void Think(float dt)
	{
		_stateTimer -= dt;
		UpdateTarget(dt);

		switch (_state)
		{
			case State.Idle:
				_moveSpeed = 0f;
				if (_target != null)
				{
					EnterState(State.Chase);
				}
				else if (_stateTimer <= 0f)
				{
					_agent.TargetPosition = PickWanderPoint();
					EnterState(State.Wander, 12f);
				}
				break;

			case State.Wander:
				_moveSpeed = WanderSpeed;
				if (_target != null)
					EnterState(State.Chase);
				else if (_agent.IsNavigationFinished() || _stateTimer <= 0f)
					EnterState(State.Idle, _rng.RandfRange(2f, 4f));
				break;

			case State.Chase:
				if (_target == null)
				{
					EnterState(State.Idle, 1.5f);
					break;
				}

				_moveSpeed = ChaseSpeed;
				_repathTimer -= dt;
				if (_repathTimer <= 0f)
				{
					_agent.TargetPosition = _target.GlobalPosition;
					_repathTimer = RepathInterval;
				}

				if (_timeSinceSeen == 0f && IsWithin(_target, AttackRange))
				{
					Telegraphing = true;
					EnterState(State.Windup, WindupSeconds);
				}
				break;

			case State.Windup:
				_moveSpeed = 0f;
				if (_target != null)
					FaceTowards(_target.GlobalPosition, dt);

				if (_stateTimer <= 0f)
				{
					Telegraphing = false;
					if (_target != null && IsWithin(_target, AttackReach))
						_target.Health.TakeDamage(AttackDamage, 0);
					EnterState(State.Recover, RecoverSeconds);
				}
				break;

			case State.Recover:
				_moveSpeed = 0f;
				if (_stateTimer <= 0f)
					EnterState(_target != null ? State.Chase : State.Idle, 1f);
				break;
		}
	}

	private void EnterState(State state, float duration = 0f)
	{
		_state = state;
		_stateTimer = duration;
		_repathTimer = 0f;
	}

	private void UpdateTarget(float dt)
	{
		if (_target != null && (!IsInstanceValid(_target) || _target.IsDead || DistanceTo(_target) > LoseInterestRange))
			_target = null;

		if (_target != null)
		{
			_timeSinceSeen = CanSee(_target) ? 0f : _timeSinceSeen + dt;
			if (_timeSinceSeen > MemorySeconds)
				_target = null;
			return;
		}

		float closest = SightRange;
		foreach (Player player in Player.All)
		{
			float distance = DistanceTo(player);
			if (!player.IsDead && distance < closest && CanSee(player))
			{
				closest = distance;
				_target = player;
				_timeSinceSeen = 0f;
			}
		}
	}

	private bool CanSee(Player player)
	{
		var ray = PhysicsRayQueryParameters3D.Create(GlobalPosition + Vector3.Up * EyeHeight, player.EyePosition, Layers.World);
		return GetWorld3D().DirectSpaceState.IntersectRay(ray).Count == 0;
	}

	private float DistanceTo(Player player) => GlobalPosition.DistanceTo(player.GlobalPosition);

	private bool IsWithin(Player player, float range)
	{
		Vector3 offset = player.GlobalPosition - GlobalPosition;
		return Mathf.Abs(offset.Y) < 1.5f && new Vector2(offset.X, offset.Z).Length() < range;
	}

	private Vector3 PickWanderPoint()
	{
		Vector3 candidate = _home + new Vector3(_rng.RandfRange(-1f, 1f), 0f, _rng.RandfRange(-1f, 1f)) * WanderRadius;
		Rid map = GetWorld3D().NavigationMap;
		return NavigationServer3D.MapGetIterationId(map) > 0 ? NavigationServer3D.MapGetClosestPoint(map, candidate) : candidate;
	}

	private void OnDamaged(float amount, int sourcePeerId)
	{
		_flashTween = HitFlash.Play(this, _bodyMaterial, HitColor, _flashTween);

		// Whoever hurt us becomes the target, even if we couldn't see them.
		if (Multiplayer.IsServer() && Player.TryGet(sourcePeerId, out Player attacker) && !attacker.IsDead)
		{
			_target = attacker;
			_timeSinceSeen = 0f;
			if (_state is State.Idle or State.Wander)
				EnterState(State.Chase);
		}
	}

	private void OnDied(int sourcePeerId)
	{
		Telegraphing = false;
		if (!Multiplayer.IsServer())
			return;

		Level.Current?.Effects.Rpc(nameof(Effects.Splash), GlobalPosition + Vector3.Up * 0.05f, Vector3.Up, new Color(0.12f, 0.02f, 0.03f, 0.9f), 1.6f);
		EmitSignal(SignalName.Defeated);
		QueueFree(); // despawns on every client
	}

	// ── Movement ────────────────────────────────────────────────────────────────

	private void Move(float dt)
	{
		Vector3 velocity = Velocity;
		if (!IsOnFloor())
			velocity.Y -= _gravity * dt;

		Vector3 desired = Vector3.Zero;
		if (_moveSpeed > 0f)
		{
			Vector3 direction = SteerDirection();
			desired = direction * _moveSpeed;
			if (direction != Vector3.Zero)
				FaceTowards(GlobalPosition + direction, dt);
		}

		Vector3 horizontal = new Vector3(velocity.X, 0f, velocity.Z).Lerp(desired, 1f - Mathf.Exp(-10f * dt));
		Velocity = new Vector3(horizontal.X, velocity.Y, horizontal.Z);
		MoveAndSlide();
	}

	private Vector3 SteerDirection()
	{
		Vector3 next = _agent.IsNavigationFinished() ? GlobalPosition : _agent.GetNextPathPosition();
		Vector3 toNext = next - GlobalPosition;
		toNext.Y = 0f;

		// No path (navmesh still baking, or we're at the end of it): head straight for the target.
		if (toNext.LengthSquared() < 0.01f && _state == State.Chase && _target != null)
		{
			toNext = _target.GlobalPosition - GlobalPosition;
			toNext.Y = 0f;
		}

		return toNext.LengthSquared() > 0.01f ? toNext.Normalized() : Vector3.Zero;
	}

	private void FaceTowards(Vector3 point, float dt)
	{
		Vector3 direction = point - GlobalPosition;
		if (new Vector2(direction.X, direction.Z).LengthSquared() < 0.0001f)
			return;

		float yaw = Mathf.Atan2(-direction.X, -direction.Z);
		Rotation = new Vector3(0f, Mathf.LerpAngle(Rotation.Y, yaw, 1f - Mathf.Exp(-TurnRate * dt)), 0f);
	}

	private void FollowNetworkState(float dt)
	{
		float weight = 1f - Mathf.Exp(-RemoteSmoothing * dt);
		GlobalPosition = GlobalPosition.DistanceTo(SyncPosition) > RemoteSnapDistance
			? SyncPosition
			: GlobalPosition.Lerp(SyncPosition, weight);
		Rotation = new Vector3(0f, Mathf.LerpAngle(Rotation.Y, SyncYaw, weight), 0f);
	}

	// ── Visuals ─────────────────────────────────────────────────────────────────

	// Per-instance copies, so flashing one evil guy doesn't flash them all.
	private void SetUpMaterials()
	{
		var body = GetNode<MeshInstance3D>("Body");
		_bodyMaterial = (StandardMaterial3D)body.GetActiveMaterial(0).Duplicate();
		foreach (var part in new[] { body, GetNode<MeshInstance3D>("Body/ArmL"), GetNode<MeshInstance3D>("Body/ArmR") })
			part.MaterialOverride = _bodyMaterial;

		var eyeLeft = GetNode<MeshInstance3D>("Head/EyeL");
		_eyeMaterial = (StandardMaterial3D)eyeLeft.GetActiveMaterial(0).Duplicate();
		eyeLeft.MaterialOverride = _eyeMaterial;
		GetNode<MeshInstance3D>("Head/EyeR").MaterialOverride = _eyeMaterial;
	}

	private void UpdateHealthLabel()
	{
		_healthLabel.Visible = !Health.IsDead && Health.Current < Health.MaxHealth;
		_healthLabel.Text = $"{Mathf.CeilToInt(Health.Current)} / {Mathf.CeilToInt(Health.MaxHealth)}";
	}
}
