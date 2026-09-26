using System.Collections.Generic;
using System.Linq;
using Godot;
using RND.Combat;
using RND.Core;
using RND.Items;
using RND.Levels;
using RND.Maze;
using RND.Props;
using RND.Vfx;

namespace RND.Players;

/// <summary>
/// First-person researcher. Each client simulates its own player (so movement feels instant) and
/// replicates the result; everyone else smooths toward the replicated state.
/// The node is named after its owner's peer id, which is how authority gets assigned.
/// Health and the gas mask filter (Respirator) are the exceptions: the host owns them, so clients
/// can't ignore damage or breathe for free.
/// </summary>
public partial class Player : CharacterBody3D
{
	public const int HandsSlot = 0;
	public const int MaxSlots = 5; // hotbar_1 .. hotbar_5

	private const float RemoteSmoothing = 18f;
	private const float RemoteSnapDistance = 3f;
	private const float KillPlaneY = -30f;
	private const float MaxThrowOriginError = 2.5f; // how far a throw may start from where the host thinks our eyes are
	private const float CooldownTolerance = 1f;     // seconds of slack for network jitter in the host's cooldown check
	private const float ReviveRange = 2f;
	private const float ReviveSeconds = 3f;
	private const float ReviveHealth = 50f;
	private const int ReportEveryFrames = 2; // owner → host state reports: 30 a second at 60 physics ticks
	private static readonly Color HurtColor = new(1f, 0.2f, 0.15f);

	private static readonly Dictionary<int, Player> ByPeer = new();

	/// <summary>The player this machine controls, if one is spawned.</summary>
	public static Player Local { get; private set; }

	public static IEnumerable<Player> All => ByPeer.Values;

	public static bool TryGet(int peerId, out Player player) => ByPeer.TryGetValue(peerId, out player);

	[ExportGroup("Movement")]
	[Export] public float WalkSpeed { get; set; } = 4f;
	[Export] public float SprintSpeed { get; set; } = 6.5f;
	[Export] public float JumpVelocity { get; set; } = 4.5f;
	[Export] public float GroundAcceleration { get; set; } = 12f;
	[Export] public float AirAcceleration { get; set; } = 2f;
	[Export] public float MouseSensitivity { get; set; } = 0.0025f;

	// How far away enemies can hear your footsteps (walls halve it).
	[Export] public float WalkNoiseRadius { get; set; } = 3f;
	[Export] public float SprintNoiseRadius { get; set; } = 10f;

	[ExportGroup("Carrying")]
	[Export] public float GrabRange { get; set; } = 2.5f;
	[Export] public float MinHoldDistance { get; set; } = 1f;
	[Export] public float MaxHoldDistance { get; set; } = 3f;

	[ExportGroup("Kit")]
	// Hotbar items after the Hands slot: this character type's kit (Scientist: acid flask).
	[Export] public Godot.Collections.Array<HotbarItem> Loadout { get; set; } = new();
	[Export] public float RespawnDelay { get; set; } = 8f;

	// Written by the owning client, replicated to everyone else by the MultiplayerSynchronizer.
	[ExportGroup("Network")]
	[Export] public Vector3 SyncPosition { get; set; }
	[Export] public float SyncYaw { get; set; }
	[Export] public float SyncPitch { get; set; }
	[Export] public float SyncHoldDistance { get; set; } = 1.8f;
	[Export] public int SelectedSlot { get; set; }
	[Export] public float SelectedItemCharge { get; set; } = 1f; // 0 = just used, 1 = ready

	public int PeerId { get; private set; }
	/// <summary>Which of the level's spawn points is ours, handed out by the host (Level.AddPlayer).</summary>
	public int SpawnSlot { get; set; } = -1;
	public Health Health { get; private set; }
	public Respirator Respirator { get; private set; }
	public Breathing Breathing { get; private set; }
	public bool IsDead => Health?.IsDead ?? false;
	public int SlotCount => Mathf.Min(1 + Loadout.Count, MaxSlots);
	/// <summary>
	/// For someone else's player, where their latest report puts their eyes, not the smoothed body we
	/// draw (which trails it). The host checks reach against this, so lag doesn't make it refuse a
	/// throw, grab or revive the player really was close enough for.
	/// </summary>
	public Vector3 EyePosition => IsMultiplayerAuthority() ? _head.GlobalPosition : SyncPosition + Vector3.Up * _head.Position.Y;
	public Vector3 AimDirection => -_head.GlobalBasis.Z;

	/// <summary>
	/// World-space point a held prop gets pulled toward. For someone else's player it's worked out
	/// from their latest report rather than the smoothed body we draw, which trails it.
	/// </summary>
	public Vector3 HoldPoint => IsMultiplayerAuthority()
		? EyePosition + AimDirection * SyncHoldDistance
		: EyePosition + new Basis(Vector3.Up, SyncYaw) * new Basis(Vector3.Right, SyncPitch) * Vector3.Forward * SyncHoldDistance;

	/// <summary>Contextual prompt for the HUD.</summary>
	public string Hint
	{
		get
		{
			if (IsDead)
				return "";

			if (_reviveTarget != null)
				return $"Reviving… {Mathf.FloorToInt(_reviveProgress * 100f)}%";
			if (DownedTeammateInView != null)
				return "[hold E] revive";

			if (GetItem(SelectedSlot) is { } item)
			{
				float remaining = GetCooldownRemaining(SelectedSlot);
				return remaining > 0f
					? $"{item.DisplayName} recharging… {Mathf.CeilToInt(remaining)}s"
					: $"[LMB] throw {item.DisplayName.ToLower()}";
			}

			if (_grabRay.GetCollider() is ChamberButton)
				return "[E] press";
			if (IsHolding)
				return _heldProp is FilterCanister
					? "[E] screw it on    [RMB] throw    release [LMB] to drop"
					: "[RMB] throw    [scroll] push / pull    release [LMB] to drop";
			if (AimedProp is FilterCanister { HeldBy: 0 } canister)
				return $"[E] screw on {canister.DisplayName}  ·  [LMB] grab";
			if (AimedProp is { HeldBy: 0 } prop)
				return $"[LMB] grab {prop.DisplayName}  ·  {prop.Mass:0.#} kg";
			return "";
		}
	}

	private static bool HasControl => Input.MouseMode == Input.MouseModeEnum.Captured;
	private static double Now => Time.GetTicksMsec() / 1000.0;
	private bool CanAct => HasControl && !IsDead;
	private PhysicsProp AimedProp => _grabRay.GetCollider() as PhysicsProp;
	private bool IsHolding => _heldProp != null && IsInstanceValid(_heldProp) && _heldProp.HeldBy == PeerId;

	// A downed teammate close by that you're roughly looking at.
	private Player DownedTeammateInView => All.FirstOrDefault(p => p != this && p.IsDead
		&& p.GlobalPosition.DistanceTo(GlobalPosition) < ReviveRange
		&& AimDirection.Dot((p.GlobalPosition + Vector3.Up * 0.3f - EyePosition).Normalized()) > 0.8f);

	private Node3D _head;
	private Camera3D _camera;
	private RayCast3D _grabRay;
	private MeshInstance3D _bodyMesh;
	private CollisionShape3D _bodyShape;
	private MeshInstance3D _visor;
	private Node3D _handItem;
	private Liquid _handLiquid;
	private float _handFullFill;
	private Label3D _nameLabel;
	private StandardMaterial3D _bodyMaterial;
	private Tween _flashTween;
	private PhysicsProp _heldProp;
	private float _gravity;
	private Vector3 _lastHostPosition;
	private float _footstepTimer;
	private Player _reviveTarget;
	private float _reviveProgress;
	private MultiplayerSynchronizer _sync;
	private bool _reporting = true;
	private float _appliedFov;

	// Per-slot "ready again at" times. The owner uses theirs for the HUD; the host keeps its own to validate throws.
	private readonly double[] _readyAt = new double[MaxSlots];
	private readonly double[] _hostReadyAt = new double[MaxSlots];

	public HotbarItem GetItem(int slot) => slot > HandsSlot && slot < SlotCount ? Loadout[slot - 1] : null;

	public float GetCooldownRemaining(int slot) =>
		slot >= 0 && slot < MaxSlots ? (float)Mathf.Max(0.0, _readyAt[slot] - Now) : 0f;

	/// <summary>How recharged the item in this slot is: 0 = just used, 1 = ready.</summary>
	public float GetCharge(int slot) =>
		GetItem(slot) is { Cooldown: > 0f } item ? 1f - GetCooldownRemaining(slot) / item.Cooldown : 1f;

	public override void _EnterTree()
	{
		PeerId = int.Parse(Name.ToString());
		SetMultiplayerAuthority(PeerId);
		GetNode("Health").SetMultiplayerAuthority(1); // health and filter belong to the host
		GetNode("Respirator").SetMultiplayerAuthority(1);
		// The owner reports its state to the host, and the host replicates it to everyone else (see
		// ReportState). Only the host ever sends synchronizer data, so a client never gets state for a
		// player it hasn't spawned yet, or that the host has just removed.
		_sync = GetNode<MultiplayerSynchronizer>("MultiplayerSynchronizer");
		_sync.SetMultiplayerAuthority(1);
		ByPeer[PeerId] = this;
	}

	public override void _ExitTree()
	{
		if (ByPeer.TryGetValue(PeerId, out Player registered) && registered == this)
			ByPeer.Remove(PeerId);
		if (Local == this)
			Local = null;
	}

	public override void _Ready()
	{
		_head = GetNode<Node3D>("Head");
		_camera = GetNode<Camera3D>("Head/Camera3D");
		_grabRay = GetNode<RayCast3D>("Head/Camera3D/GrabRay");
		_handItem = GetNode<Node3D>("Head/Camera3D/HandItem");
		_handLiquid = (Liquid)_handItem.FindChild("Liquid", owned: false); // wherever the model nests it
		_handFullFill = _handLiquid.Fill;
		_bodyMesh = GetNode<MeshInstance3D>("Body");
		_bodyShape = GetNode<CollisionShape3D>("CollisionShape3D");
		_visor = GetNode<MeshInstance3D>("Head/Visor");
		_nameLabel = GetNode<Label3D>("NameLabel");
		Health = GetNode<Health>("Health");
		Respirator = GetNode<Respirator>("Respirator");
		Breathing = GetNode<Breathing>("Breathing");
		_gravity = (float)ProjectSettings.GetSetting("physics/3d/default_gravity");
		_grabRay.TargetPosition = new Vector3(0, 0, -GrabRange);

		Health.Damaged += (_, _) => _flashTween = HitFlash.Play(this, _bodyMaterial, HurtColor, _flashTween);
		Health.Died += OnDied;
		Health.Revived += OnRevived;

		bool isLocal = IsMultiplayerAuthority();
		SetUpVisuals(isLocal);
		SetDeadVisuals(IsDead);
		// Not back to the owner: what it controls (slot, hold distance) mustn't be overwritten by the
		// host's older copy. (The synchronizer is rooted on itself, so this doesn't hide the player.)
		if (Multiplayer.IsServer())
			_sync.AddVisibilityFilter(Callable.From((int peer) => peer != PeerId));

		if (isLocal)
		{
			Local = this;
			_camera.MakeCurrent();
			MoveToSpawnPoint();
			WriteSyncState();
			Input.MouseMode = Input.MouseModeEnum.Captured;
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!IsMultiplayerAuthority())
			return;

		if (@event.IsActionPressed("pause"))
		{
			// Only reached when there's no HUD to handle it (e.g. running the level scene directly).
			Input.MouseMode = HasControl ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
			return;
		}

		if (!HasControl)
			return;

		if (@event is InputEventMouseMotion motion)
		{
			float sensitivity = MouseSensitivity * (Settings.Instance?.MouseSensitivity ?? 1f);
			RotateY(-motion.Relative.X * sensitivity);
			float pitch = _head.Rotation.X - motion.Relative.Y * sensitivity;
			_head.Rotation = new Vector3(Mathf.Clamp(pitch, -1.5f, 1.5f), 0, 0);
			return;
		}

		if (IsDead)
			return;

		for (int slot = 0; slot < MaxSlots; slot++)
		{
			if (@event.IsActionPressed($"hotbar_{slot + 1}"))
			{
				SelectSlot(slot);
				return;
			}
		}

		// Scroll pushes / pulls a carried prop; otherwise it cycles the hotbar.
		int scroll = @event.IsActionPressed("scroll_up") ? 1 : @event.IsActionPressed("scroll_down") ? -1 : 0;
		if (scroll == 0)
			return;

		if (IsHolding)
			SyncHoldDistance = Mathf.Clamp(SyncHoldDistance + scroll * 0.25f, MinHoldDistance, MaxHoldDistance);
		else
			SelectSlot(Mathf.PosMod(SelectedSlot - scroll, SlotCount));
	}

	public override void _Process(double delta)
	{
		// Everyone sees the equipped flask in hand, and its acid refilling as it recharges.
		_handItem.Visible = GetItem(SelectedSlot) != null && !IsDead;
		_handLiquid.Fill = _handFullFill * SelectedItemCharge;

		// Field of view from the settings, only when it changes (so something else can zoom the camera).
		float fov = Settings.Instance?.FieldOfView ?? _camera.Fov;
		if (IsMultiplayerAuthority() && fov != _appliedFov)
			_camera.Fov = _appliedFov = fov;
	}

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;

		if (IsMultiplayerAuthority())
		{
			Move(dt);

			if (SelectedSlot == HandsSlot)
				UpdateCarrying();
			else if (CanAct && Input.IsActionJustPressed("primary"))
				UseSelectedItem();
			SelectedItemCharge = GetCharge(SelectedSlot);
			UpdateInteract();
			UpdateRevive(dt);

			if (GlobalPosition.Y < KillPlaneY)
				MoveToSpawnPoint();

			WriteSyncState();
			if (!Multiplayer.IsServer() && _reporting && Engine.GetPhysicsFrames() % ReportEveryFrames == 0)
				RpcId(1, MethodName.ReportState, SyncPosition, SyncYaw, SyncPitch, SyncHoldDistance, SelectedSlot, SelectedItemCharge);
		}
		else
		{
			FollowNetworkState(dt);
		}

		if (Multiplayer.IsServer())
			BreatheAndMakeFootstepNoise(dt);
	}

	// Host only. Effort is judged from how far the player actually moved, so it works the same for
	// remote players (which the host only sees as replicated positions). Harder breathing drains the
	// filter faster; walking is quiet, sprinting carries.
	private void BreatheAndMakeFootstepNoise(float dt)
	{
		Vector3 moved = GlobalPosition - _lastHostPosition;
		_lastHostPosition = GlobalPosition;
		float speed = new Vector2(moved.X, moved.Z).Length() / dt;
		if (speed > 20f)
			speed = 0f; // a teleport or respawn, not effort

		Respirator.Breathe(dt, Mathf.Clamp((speed - 1f) / (SprintSpeed - 1f), 0f, 1f), Health);

		_footstepTimer -= dt;
		if (IsDead || speed < 1f || _footstepTimer > 0f)
			return;

		bool sprinting = speed > (WalkSpeed + SprintSpeed) / 2f;
		_footstepTimer = sprinting ? 0.3f : 0.5f;
		Level.Current?.EmitNoise(GlobalPosition, sprinting ? SprintNoiseRadius : WalkNoiseRadius);
	}

	private void Move(float dt)
	{
		Vector3 velocity = Velocity;
		if (!IsOnFloor())
			velocity.Y -= _gravity * dt;
		else if (CanAct && Input.IsActionJustPressed("jump"))
			velocity.Y = JumpVelocity;

		Vector2 input = CanAct
			? Input.GetVector("move_left", "move_right", "move_forward", "move_back")
			: Vector2.Zero;
		Vector3 wishDirection = Transform.Basis * new Vector3(input.X, 0, input.Y);
		float speed = CanAct && Input.IsActionPressed("sprint") ? SprintSpeed : WalkSpeed;
		float acceleration = IsOnFloor() ? GroundAcceleration : AirAcceleration;

		Vector3 horizontal = new Vector3(velocity.X, 0, velocity.Z)
			.Lerp(wishDirection * speed, 1f - Mathf.Exp(-acceleration * dt));
		Velocity = new Vector3(horizontal.X, velocity.Y, horizontal.Z);
		MoveAndSlide();
	}

	// ── Hotbar ──────────────────────────────────────────────────────────────────

	private void SelectSlot(int slot)
	{
		if (slot < 0 || slot >= SlotCount || slot == SelectedSlot)
			return;

		if (slot != HandsSlot)
			ReleaseHeldProp();
		SelectedSlot = slot;
	}

	/// <summary>Owner side: throw the selected item if it's charged. (The host re-checks.)</summary>
	public void UseSelectedItem()
	{
		if (GetItem(SelectedSlot) is not ThrowableItem item || GetCooldownRemaining(SelectedSlot) > 0f)
			return;

		_readyAt[SelectedSlot] = Now + item.Cooldown;
		RpcId(1, nameof(RequestThrowItem), SelectedSlot, _camera.GlobalPosition, AimDirection);
	}

	/// <summary>Owner → host: "I threw the item in this slot". The host re-checks everything.</summary>
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void RequestThrowItem(int slot, Vector3 origin, Vector3 direction)
	{
		if (!Multiplayer.IsServer() || Multiplayer.SenderId() != PeerId || IsDead)
			return;
		if (GetItem(slot) is not ThrowableItem { Projectile: not null } item || direction.LengthSquared() < 0.0001f)
			return;
		if (origin.DistanceTo(EyePosition) > MaxThrowOriginError || Now < _hostReadyAt[slot] - CooldownTolerance)
			return;

		_hostReadyAt[slot] = Now + item.Cooldown;
		direction = direction.Normalized();
		Vector3 velocity = direction * item.ThrowSpeed + Vector3.Up * item.ThrowLift;
		// Launch from the eyes, not in front of them: an offset start point can end up inside an enemy
		// that's right in your face (or past a wall you're hugging), and the throw would pass through.
		Level.Current?.SpawnProjectile(item.Projectile, origin, velocity, PeerId);
	}

	// ── Carrying ────────────────────────────────────────────────────────────────

	// Hold LMB to carry, let go to drop (props keep their momentum, so you can fling them), RMB to throw.
	// The host decides who actually gets the prop; we just ask.
	private void UpdateCarrying()
	{
		if (_heldProp != null && (!IsInstanceValid(_heldProp) || (_heldProp.HeldBy != 0 && _heldProp.HeldBy != PeerId)))
			_heldProp = null;

		if (_heldProp == null)
		{
			if (CanAct && Input.IsActionJustPressed("primary") && AimedProp is { HeldBy: 0 } prop)
			{
				_heldProp = prop;
				prop.RpcId(1, nameof(PhysicsProp.RequestGrab));
			}
		}
		else if (CanAct && Input.IsActionJustPressed("secondary"))
		{
			_heldProp.Throw(AimDirection);
			_heldProp = null;
		}
		else if (!CanAct || !Input.IsActionPressed("primary"))
		{
			ReleaseHeldProp();
		}
	}

	// [E] presses the button you're looking at, or screws on a spare filter (the one in your hands
	// first, else the one you're looking at).
	private void UpdateInteract()
	{
		if (!CanAct || !Input.IsActionJustPressed("interact"))
			return;

		if (_grabRay.GetCollider() is ChamberButton button)
		{
			button.RpcId(1, nameof(ChamberButton.RequestPress));
			return;
		}

		var canister = (IsHolding ? _heldProp : AimedProp) as FilterCanister;
		if (canister is { Consumed: false })
			canister.RpcId(1, nameof(FilterCanister.RequestUse));
	}

	private void ReleaseHeldProp()
	{
		if (_heldProp != null && IsInstanceValid(_heldProp))
			_heldProp.Release();
		_heldProp = null;
	}

	// ── Death ───────────────────────────────────────────────────────────────────

	private void OnDied(int sourcePeerId)
	{
		SetDeadVisuals(true);

		if (IsMultiplayerAuthority())
			ReleaseHeldProp();

		// Levels without timed respawn (maze chambers) leave you down until a teammate revives you.
		if (Multiplayer.IsServer() && (Level.Current?.TimedRespawn ?? true))
		{
			GetTree().CreateTimer(RespawnDelay).Timeout += () =>
			{
				if (IsInstanceValid(this) && IsInsideTree())
					Health.Revive();
			};
		}
	}

	// A full-health revive is a respawn: new mask, new filter, back at a spawn point. A teammate's
	// revive (less than full) leaves you where you fell, with the filter you had.
	private void OnRevived()
	{
		SetDeadVisuals(false);
		if (Health.Current < Health.MaxHealth)
			return;

		Respirator.Refill(); // host only; ignored elsewhere
		if (IsMultiplayerAuthority())
		{
			MoveToSpawnPoint();
			WriteSyncState();
		}
	}

	// Hold [E] on a downed teammate for a few seconds to bring them back.
	private void UpdateRevive(float dt)
	{
		Player target = CanAct && Input.IsActionPressed("interact") ? DownedTeammateInView : null;
		if (target != _reviveTarget)
		{
			_reviveTarget = target;
			_reviveProgress = 0f;
		}
		if (target == null)
			return;

		_reviveProgress += dt / ReviveSeconds;
		if (_reviveProgress < 1f)
			return;

		target.RpcId(1, MethodName.RequestRevive);
		_reviveProgress = 0f; // if the host says no, holding on tries again
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RequestRevive()
	{
		if (!Multiplayer.IsServer() || !IsDead)
			return;

		if (TryGet(Multiplayer.SenderId(), out Player reviver) && reviver != this && !reviver.IsDead
			&& reviver.SyncPosition.DistanceTo(SyncPosition) < ReviveRange + 1f) // latest reports, plus slack for lag
			Health.ReviveTo(ReviveHealth);
	}

	// ── Networking ──────────────────────────────────────────────────────────────

	private void WriteSyncState()
	{
		SyncPosition = GlobalPosition;
		SyncYaw = Rotation.Y;
		SyncPitch = _head.Rotation.X;
	}

	/// <summary>
	/// Owner → host, 30 times a second. The host copies it into the Sync properties, which its
	/// synchronizer sends on. Same ordered channel as Main's level-change handshake, so nothing sent
	/// before the handshake can arrive after it.
	/// </summary>
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = Network.StateChannel)]
	private void ReportState(Vector3 position, float yaw, float pitch, float holdDistance, int slot, float charge)
	{
		if (!Multiplayer.IsServer() || Multiplayer.GetRemoteSenderId() != PeerId)
			return;

		SyncPosition = position;
		SyncYaw = yaw;
		SyncPitch = pitch;
		SyncHoldDistance = Mathf.Clamp(holdDistance, MinHoldDistance, MaxHoldDistance);
		SelectedSlot = Mathf.Clamp(slot, 0, SlotCount - 1);
		SelectedItemCharge = Mathf.Clamp(charge, 0f, 1f);
	}

	/// <summary>Owner: the level is about to be swapped (see Main.ChangeLevel), so stop reporting.</summary>
	public void StopReporting() => _reporting = false;

	private void FollowNetworkState(float dt)
	{
		float weight = 1f - Mathf.Exp(-RemoteSmoothing * dt);
		GlobalPosition = GlobalPosition.DistanceTo(SyncPosition) > RemoteSnapDistance
			? SyncPosition
			: GlobalPosition.Lerp(SyncPosition, weight);
		Rotation = new Vector3(0, Mathf.LerpAngle(Rotation.Y, SyncYaw, weight), 0);
		_head.Rotation = new Vector3(Mathf.LerpAngle(_head.Rotation.X, SyncPitch, weight), 0, 0);
	}

	private void MoveToSpawnPoint()
	{
		var spawns = GetTree().GetNodesInGroup("player_spawn");
		if (spawns.Count == 0)
			return;

		var spawn = (Node3D)spawns[(SpawnSlot >= 0 ? SpawnSlot : PeerId) % spawns.Count];
		GlobalPosition = spawn.GlobalPosition;
		Rotation = new Vector3(0, spawn.GlobalRotation.Y, 0);
		Velocity = Vector3.Zero;
	}

	// ── Visuals ─────────────────────────────────────────────────────────────────

	private void SetUpVisuals(bool isLocal)
	{
		_bodyMaterial = new StandardMaterial3D
		{
			AlbedoColor = Color.FromHsv(PeerId % 360 / 360f, 0.45f, 0.8f),
			Roughness = 0.8f,
		};
		_bodyMesh.MaterialOverride = _bodyMaterial;
		_nameLabel.Text = PeerId == 1 ? "Host" : $"Researcher {PeerId % 1000}";

		if (isLocal)
		{
			// Keep our own shadow, but don't render our body inside our own camera.
			_bodyMesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.ShadowsOnly;
			_visor.CastShadow = GeometryInstance3D.ShadowCastingSetting.ShadowsOnly;
			_nameLabel.Visible = false;
		}
	}

	// Placeholder "down" pose: lie flat, camera near the floor.
	// The body collides as it's drawn, lying down, not as an invisible standing pillar. (A downed
	// player still weighs down a pressure plate.)
	private void SetDeadVisuals(bool dead)
	{
		_bodyMesh.Rotation = dead ? new Vector3(Mathf.Pi / 2f, 0, 0) : Vector3.Zero;
		_bodyMesh.Position = new Vector3(0, dead ? 0.35f : 0.9f, 0);
		_bodyShape.Transform = _bodyMesh.Transform;
		_head.Position = new Vector3(0, dead ? 0.4f : 1.55f, 0);
		_visor.Visible = !dead;
	}
}
