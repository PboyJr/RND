using Godot;
using RND.Audio;
using RND.Combat;
using RND.Core;
using RND.Levels;
using RND.Players;
using RND.Props;
using RND.Vfx;

namespace RND.Enemies;

/// <summary>
/// The evil guy. Host-authoritative: the host runs the AI and replicates position, facing, mood and
/// the attack telegraph; clients smooth toward it, the same way they handle remote players.
///
/// It only knows what it perceives:
/// - Sight: a vision cone (plus a small all-round sense up close), blocked by walls. Being sure it saw
///   someone takes longer the further away they are, so you can slip past at a distance.
/// - Hearing: footsteps (sprinting is loud), shattering flasks and props crashing, muffled by walls.
/// Lose it and it heads for where you were going, then searches the area before giving up. It always
/// follows the navmesh (including drop-down links off ledges), shoves props out of its way and
/// sidesteps when wedged.
/// </summary>
public partial class Enemy : CharacterBody3D
{
	public enum MoodKind { Calm, Suspicious, Hunting }

	private enum State { Idle, Wander, Search, Chase, Windup, Recover }

	private const float RemoteSmoothing = 14f;
	private const float RemoteSnapDistance = 3f;
	// Not just a perf limit: re-planning every frame while stepping off a ledge re-roots the path at
	// the nearest navmesh point, which is behind it, so it would wobble at the lip and never drop.
	private const float RepathInterval = 0.25f;
	private const float GlimpseThreshold = 0.35f; // suspicion at which it comes over to look
	private const float SuspicionDecay = 0.3f;     // per second while nobody's in view
	private const float MuffledHearing = 0.5f;     // fraction of a noise's range that carries through walls
	private const float ScanTurnRate = 1.6f;       // rad/s while looking around
	private const float StuckSeconds = 0.75f;
	private const float UnstickSeconds = 0.5f;
	private static readonly Color HitColor = new(0.6f, 1f, 0.4f);

	[ExportGroup("Movement")]
	[Export] public float WanderSpeed { get; set; } = 1.8f;
	[Export] public float SearchSpeed { get; set; } = 2.8f;
	[Export] public float ChaseSpeed { get; set; } = 4.3f; // players walk at 4, sprint at 6.5
	[Export] public float WanderRadius { get; set; } = 8f;
	[Export] public float TurnRate { get; set; } = 8f;
	// Shoves props out of its way: light crates go flying, the heavy case barely budges.
	[Export] public float ShoveForce { get; set; } = 450f;

	[ExportGroup("Senses")]
	[Export] public float SightRange { get; set; } = 14f;
	[Export(PropertyHint.Range, "10,360,1,suffix:°")] public float FieldOfView { get; set; } = 110f;
	// Within this range it senses you in any direction.
	[Export] public float CloseSenseRange { get; set; } = 2.5f;
	// How long it takes to be sure it's seen someone: quick up close, slow at the edge of sight.
	[Export] public float NoticeTimeNear { get; set; } = 0.3f;
	[Export] public float NoticeTimeFar { get; set; } = 2f;
	[Export] public float EyeHeight { get; set; } = 2f;
	// Out of sight this long and it stops tracking you, heading for where you were going instead.
	[Export] public float LoseSightGrace { get; set; } = 0.75f;
	[Export] public float PredictSeconds { get; set; } = 1f;

	[ExportGroup("Search")]
	[Export] public float SearchSeconds { get; set; } = 12f;
	[Export] public float SearchRadius { get; set; } = 6f;

	[ExportGroup("Attack")]
	[Export] public float AttackDamage { get; set; } = 20f;
	// Starts a swing when a player is this close...
	[Export] public float AttackRange { get; set; } = 1.6f;
	// ...and the swing lands if they're still this close when the windup ends.
	[Export] public float AttackReach { get; set; } = 2.2f;
	// Only swings at players roughly on its own level. Anyone higher or lower has to be walked to,
	// otherwise it camps under ledges swinging upward instead of finding the way up.
	[Export] public float AttackHeightTolerance { get; set; } = 0.75f;
	[Export] public float WindupSeconds { get; set; } = 0.45f;
	[Export] public float RecoverSeconds { get; set; } = 1f;

	// Written by the host, replicated to clients.
	[ExportGroup("Network")]
	[Export] public Vector3 SyncPosition { get; set; }
	[Export] public float SyncYaw { get; set; }
	[Export] public bool Telegraphing { get; set; }
	[Export] public MoodKind Mood { get; set; }

	public Health Health { get; private set; }

	private readonly RandomNumberGenerator _rng = new();
	private NavigationAgent3D _agent;
	private Label3D _healthLabel;
	private StandardMaterial3D _bodyMaterial;
	private StandardMaterial3D _eyeMaterial;
	private Tween _flashTween;
	private float _gravity;
	private Vector3 _home;

	private State _state = State.Idle;
	private float _stateTimer;
	private float _moveSpeed;
	private float _repathTimer;

	// What it knows.
	private Player _target;
	private float _timeSinceSeen;
	private Vector3 _lastSeenPosition;
	private Vector3 _lastSeenVelocity;
	private float _suspicion;

	// Searching.
	private Vector3 _searchOrigin;
	private float _lookTimer;
	private float _scanDirection = 1f;

	// Getting unstuck.
	private Vector3 _lastPosition;
	private float _stuckTime;
	private float _unstickTimer;
	private Vector3 _unstickDirection;

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
			_lastPosition = GlobalPosition;
			SyncPosition = GlobalPosition;
			SyncYaw = Rotation.Y;
			EnterState(State.Idle, _rng.RandfRange(1f, 3f));
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
			UpdatePerception(dt);
			Think(dt);
			Move(dt);
			Mood = CurrentMood();
			SyncPosition = GlobalPosition;
			SyncYaw = Rotation.Y;
		}

		UpdateEyes();
	}

	/// <summary>Host only, via Level.EmitNoise. Busy with a target, it ignores noise.</summary>
	public void Hear(Vector3 position, float radius)
	{
		if (Health.IsDead || _target != null)
			return;

		Vector3 ear = GlobalPosition + Vector3.Up * EyeHeight;
		var wall = PhysicsRayQueryParameters3D.Create(ear, position + Vector3.Up * 0.3f, Layers.World);
		float reach = radius * (GetWorld3D().DirectSpaceState.IntersectRay(wall).Count > 0 ? MuffledHearing : 1f);
		float distance = ear.DistanceTo(position);
		if (distance > reach)
			return;

		// Close noises are alarming, not just interesting: it's already half sure when it gets there.
		_suspicion = Mathf.Max(_suspicion, Mathf.Lerp(0.8f, GlimpseThreshold, distance / reach));
		Investigate(position);
	}

	// ── Perception ──────────────────────────────────────────────────────────────

	private void UpdatePerception(float dt)
	{
		if (_target != null)
		{
			if (!IsInstanceValid(_target) || _target.IsDead)
			{
				_target = null; // a chase with no target falls back to Idle in UpdateChase
				_suspicion = 0f;
				return;
			}

			if (CanSee(_target, focused: true))
			{
				_timeSinceSeen = 0f;
				_lastSeenVelocity = _lastSeenVelocity.Lerp((_target.GlobalPosition - _lastSeenPosition) / dt, 0.2f);
				_lastSeenPosition = _target.GlobalPosition;
			}
			else
			{
				_timeSinceSeen += dt;
			}
			return;
		}

		// Nobody locked on: notice whoever is most visible, gradually.
		Player seen = null;
		float noticeRate = 0f;
		foreach (Player player in Player.All)
		{
			if (player.IsDead || !CanSee(player, focused: false))
				continue;

			float distance = GlobalPosition.DistanceTo(player.GlobalPosition);
			float rate = 1f / Mathf.Lerp(NoticeTimeNear, NoticeTimeFar, Mathf.Clamp(distance / SightRange, 0f, 1f));
			if (rate > noticeRate)
			{
				noticeRate = rate;
				seen = player;
			}
		}

		if (seen == null)
		{
			_suspicion = Mathf.Max(0f, _suspicion - SuspicionDecay * dt);
			return;
		}

		_suspicion += noticeRate * dt;
		if (_suspicion >= 1f)
			StartChase(seen);
		else if (_suspicion >= GlimpseThreshold && _state is State.Idle or State.Wander)
			Investigate(seen.GlobalPosition);
	}

	// Focused (already hunting them) = no vision cone; it's watching them.
	private bool CanSee(Player player, bool focused)
	{
		Vector3 eye = GlobalPosition + Vector3.Up * EyeHeight;
		Vector3 toPlayer = player.EyePosition - eye;
		float distance = toPlayer.Length();
		if (distance > (focused ? SightRange * 1.5f : SightRange))
			return false;

		if (!focused && distance > CloseSenseRange)
		{
			Vector3 flat = toPlayer with { Y = 0f };
			if (flat.LengthSquared() > 0.0001f && (-GlobalBasis.Z).AngleTo(flat) > Mathf.DegToRad(FieldOfView) / 2f)
				return false;
		}

		var ray = PhysicsRayQueryParameters3D.Create(eye, player.EyePosition, Layers.World);
		return GetWorld3D().DirectSpaceState.IntersectRay(ray).Count == 0;
	}

	private void StartChase(Player player)
	{
		bool spotted = _target == null;
		_target = player; // set first, so it doesn't turn round to investigate its own growl
		if (spotted)
			Voice(SoundKind.Growl, 12f); // "it's seen me" (and it tells any others nearby)
		_suspicion = 1f;
		_timeSinceSeen = 0f;
		_lastSeenPosition = player.GlobalPosition;
		_lastSeenVelocity = Vector3.Zero;
		if (_state is not (State.Windup or State.Recover))
			EnterState(State.Chase);
	}

	private void Investigate(Vector3 point)
	{
		_searchOrigin = SnapToNavmesh(point);
		_agent.TargetPosition = _searchOrigin;
		_lookTimer = _rng.RandfRange(1.5f, 2.5f);
		EnterState(State.Search, SearchSeconds);
	}

	private MoodKind CurrentMood() => _state switch
	{
		State.Chase or State.Windup or State.Recover when _target != null => MoodKind.Hunting,
		State.Search => MoodKind.Suspicious,
		_ => _suspicion >= GlimpseThreshold ? MoodKind.Suspicious : MoodKind.Calm,
	};

	// ── Behaviour ───────────────────────────────────────────────────────────────

	private void Think(float dt)
	{
		_stateTimer -= dt;

		switch (_state)
		{
			case State.Idle:
				_moveSpeed = 0f;
				if (_stateTimer <= 0f)
				{
					_agent.TargetPosition = RandomReachablePoint(_home, WanderRadius);
					EnterState(State.Wander, 12f);
				}
				break;

			case State.Wander:
				_moveSpeed = WanderSpeed;
				if (_agent.IsNavigationFinished() || _stateTimer <= 0f)
					EnterState(State.Idle, _rng.RandfRange(2f, 4f));
				break;

			case State.Search:
				UpdateSearch(dt);
				break;

			case State.Chase:
				UpdateChase(dt);
				break;

			case State.Windup:
				_moveSpeed = 0f;
				if (_target != null)
					FaceTowards(_target.GlobalPosition, dt);

				if (_stateTimer <= 0f)
				{
					Telegraphing = false;
					Voice(SoundKind.Swipe, 5f);
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

	private void UpdateChase(float dt)
	{
		if (_target == null)
		{
			EnterState(State.Idle, 1f);
			return;
		}

		bool visible = _timeSinceSeen == 0f;
		if (_timeSinceSeen > LoseSightGrace)
		{
			// Trail's gone cold: it doesn't know where you are, only where you were heading.
			Vector3 heading = _lastSeenVelocity with { Y = 0f };
			_target = null;
			_suspicion = GlimpseThreshold;
			Investigate(_lastSeenPosition + heading * PredictSeconds);
			return;
		}

		_moveSpeed = ChaseSpeed;

		_repathTimer -= dt;
		if (_repathTimer <= 0f)
		{
			_agent.TargetPosition = SnapToNavmesh(visible ? _target.GlobalPosition : _lastSeenPosition);
			_repathTimer = RepathInterval;
		}

		// Path ran out but we're not in reach: they're somewhere we can't get to. Wait and watch.
		if (_agent.IsNavigationFinished())
			FaceTowards(_lastSeenPosition, dt);

		if (visible && IsWithin(_target, AttackRange))
		{
			Telegraphing = true;
			Voice(SoundKind.Growl, 8f); // audible telegraph: you can hear the swing coming
			EnterState(State.Windup, WindupSeconds);
		}
	}

	// Host only. Heard by players and by other enemies alike.
	private void Voice(SoundKind sound, float radius) =>
		Level.Current?.EmitSound(GlobalPosition + Vector3.Up * EyeHeight, radius, sound);

	// Go to the spot, look around, then check other spots nearby until time runs out.
	private void UpdateSearch(float dt)
	{
		if (_stateTimer <= 0f)
		{
			_suspicion = 0f;
			EnterState(State.Idle, _rng.RandfRange(1f, 2f));
			return;
		}

		if (!_agent.IsNavigationFinished())
		{
			_moveSpeed = SearchSpeed;
			return;
		}

		_moveSpeed = 0f;
		if (_lookTimer > 0f)
		{
			_lookTimer -= dt;
			RotateY(ScanTurnRate * _scanDirection * dt);
			return;
		}

		_agent.TargetPosition = RandomReachablePoint(_searchOrigin, SearchRadius);
		_lookTimer = _rng.RandfRange(1f, 2f);
		_scanDirection = _rng.Randf() < 0.5f ? -1f : 1f;
	}

	private void EnterState(State state, float duration = 0f)
	{
		_state = state;
		_stateTimer = duration;
		_repathTimer = 0f;
	}

	private bool IsWithin(Player player, float range)
	{
		Vector3 offset = player.GlobalPosition - GlobalPosition;
		return Mathf.Abs(offset.Y) < AttackHeightTolerance && new Vector2(offset.X, offset.Z).Length() < range;
	}

	// The navmesh also covers places it can't walk to (the roof over a chamber, a room behind a shut
	// door), and a random spot near a wall often snaps there. Only take spots its path actually reaches.
	private Vector3 RandomReachablePoint(Vector3 centre, float radius)
	{
		Rid map = GetWorld3D().NavigationMap;
		for (int attempt = 0; attempt < 8; attempt++)
		{
			Vector3 point = SnapToNavmesh(centre + new Vector3(_rng.RandfRange(-1f, 1f), 0f, _rng.RandfRange(-1f, 1f)) * radius);
			Vector3[] path = NavigationServer3D.MapGetPath(map, GlobalPosition, point, true);
			if (path.Length > 0 && path[^1].DistanceTo(point) < 0.5f)
				return point;
		}
		return GlobalPosition; // nowhere to go: stays put, and picks again next time it's idle
	}

	// The navmesh point on the surface under a position. A plain closest-point query can pick the floor
	// beside a platform instead of its top near the edge; probing a vertical segment hits the surface
	// underneath first.
	private Vector3 SnapToNavmesh(Vector3 position)
	{
		Rid map = GetWorld3D().NavigationMap;
		return NavigationServer3D.MapGetIterationId(map) > 0
			? NavigationServer3D.MapGetClosestPointToSegment(map, position + Vector3.Up, position + Vector3.Down)
			: position;
	}

	private void OnDamaged(float amount, int sourcePeerId)
	{
		_flashTween = HitFlash.Play(this, _bodyMaterial, HitColor, _flashTween);

		// Whoever hurt us becomes the target, even if we couldn't see them.
		if (Multiplayer.IsServer() && Player.TryGet(sourcePeerId, out Player attacker) && !attacker.IsDead)
			StartChase(attacker);
	}

	private void OnDied(int sourcePeerId)
	{
		Telegraphing = false;
		if (!Multiplayer.IsServer())
			return;

		Level.Current?.Effects.Rpc(nameof(Effects.Splash), GlobalPosition + Vector3.Up * 0.05f, Vector3.Up, new Color(0.12f, 0.02f, 0.03f, 0.9f), 1.6f);
		Voice(SoundKind.Splat, 10f);
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
			Vector3 direction = _unstickTimer > 0f ? _unstickDirection : SteerDirection();
			desired = direction * _moveSpeed;
			if (direction != Vector3.Zero)
				FaceTowards(GlobalPosition + direction, dt);
		}
		_unstickTimer -= dt;

		Vector3 horizontal = new Vector3(velocity.X, 0f, velocity.Z).Lerp(desired, 1f - Mathf.Exp(-10f * dt));
		Velocity = new Vector3(horizontal.X, velocity.Y, horizontal.Z);
		MoveAndSlide();

		ShoveProps();
		CheckIfStuck(desired, dt);
	}

	// Always follow the navmesh path (drop-down links included). Never beeline at the target: that's
	// what walks enemies into walls and ledges. No path means stand still.
	private Vector3 SteerDirection()
	{
		if (_agent.IsNavigationFinished())
			return Vector3.Zero;

		Vector3 toNext = _agent.GetNextPathPosition() - GlobalPosition;
		toNext.Y = 0f;
		return toNext.LengthSquared() > 0.01f ? toNext.Normalized() : Vector3.Zero;
	}

	// Props aren't in the navmesh, so it walks into them; shove them out of the way.
	private void ShoveProps()
	{
		for (int i = 0; i < GetSlideCollisionCount(); i++)
		{
			KinematicCollision3D hit = GetSlideCollision(i);
			if (hit.GetCollider() is not PhysicsProp prop)
				continue;

			Vector3 push = -hit.GetNormal() with { Y = 0f };
			if (push.LengthSquared() < 0.01f)
				continue;

			prop.Sleeping = false;
			prop.ApplyForce(push.Normalized() * ShoveForce, hit.GetPosition() - prop.GlobalPosition);
		}
	}

	// Pushing but going nowhere (wedged on a prop pile, a corner, another body): sidestep, then re-plan.
	private void CheckIfStuck(Vector3 desired, float dt)
	{
		Vector3 moved = GlobalPosition - _lastPosition;
		_lastPosition = GlobalPosition;

		bool pushing = desired.LengthSquared() > 0.25f && _unstickTimer <= 0f;
		if (!pushing || new Vector2(moved.X, moved.Z).Length() > desired.Length() * dt * 0.25f)
		{
			_stuckTime = 0f;
			return;
		}

		_stuckTime += dt;
		if (_stuckTime < StuckSeconds)
			return;

		_stuckTime = 0f;
		Vector3 forward = desired.Normalized();
		_unstickDirection = new Vector3(-forward.Z, 0f, forward.X) * (_rng.Randf() < 0.5f ? -1f : 1f);
		_unstickTimer = UnstickSeconds;
		_agent.TargetPosition = _agent.TargetPosition; // forces a fresh path
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

	// The eyes tell players what it's thinking: dim when calm, orange when suspicious, red when
	// hunting, flaring right before a swing.
	private void UpdateEyes()
	{
		(Color color, float energy) = Telegraphing
			? (new Color(1f, 0.35f, 0.25f), 10f)
			: Mood switch
			{
				MoodKind.Hunting => (new Color(1f, 0.08f, 0.03f), 5f),
				MoodKind.Suspicious => (new Color(1f, 0.55f, 0.05f), 3f),
				_ => (new Color(0.7f, 0.1f, 0.05f), 1.2f),
			};
		_eyeMaterial.AlbedoColor = color;
		_eyeMaterial.Emission = color;
		_eyeMaterial.EmissionEnergyMultiplier = energy;
	}

	private void UpdateHealthLabel()
	{
		_healthLabel.Visible = !Health.IsDead && Health.Current < Health.MaxHealth;
		_healthLabel.Text = $"{Mathf.CeilToInt(Health.Current)} / {Mathf.CeilToInt(Health.MaxHealth)}";
	}
}
