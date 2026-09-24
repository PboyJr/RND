using System.Collections.Generic;
using Godot;
using RND.Props;

namespace RND.Players;

/// <summary>
/// First-person researcher. Each client simulates its own player (so movement feels instant) and
/// replicates the result; everyone else smooths toward the replicated state.
/// The node is named after its owner's peer id, which is how authority gets assigned.
/// </summary>
public partial class Player : CharacterBody3D
{
	private const float RemoteSmoothing = 18f;
	private const float RemoteSnapDistance = 3f;
	private const float KillPlaneY = -30f;

	private static readonly Dictionary<int, Player> ByPeer = new();

	/// <summary>The player this machine controls, if one is spawned.</summary>
	public static Player Local { get; private set; }

	public static bool TryGet(int peerId, out Player player) => ByPeer.TryGetValue(peerId, out player);

	[ExportGroup("Movement")]
	[Export] public float WalkSpeed { get; set; } = 4f;
	[Export] public float SprintSpeed { get; set; } = 6.5f;
	[Export] public float JumpVelocity { get; set; } = 4.5f;
	[Export] public float GroundAcceleration { get; set; } = 12f;
	[Export] public float AirAcceleration { get; set; } = 2f;
	[Export] public float MouseSensitivity { get; set; } = 0.0025f;

	[ExportGroup("Carrying")]
	[Export] public float GrabRange { get; set; } = 2.5f;
	[Export] public float MinHoldDistance { get; set; } = 1f;
	[Export] public float MaxHoldDistance { get; set; } = 3f;

	// Written by the owning client, replicated to everyone else by the MultiplayerSynchronizer.
	[ExportGroup("Network")]
	[Export] public Vector3 SyncPosition { get; set; }
	[Export] public float SyncYaw { get; set; }
	[Export] public float SyncPitch { get; set; }
	[Export] public float SyncHoldDistance { get; set; } = 1.8f;

	public int PeerId { get; private set; }
	public Vector3 EyePosition => _head.GlobalPosition;
	public Vector3 AimDirection => -_head.GlobalBasis.Z;

	/// <summary>World-space point a held prop gets pulled toward.</summary>
	public Vector3 HoldPoint => EyePosition + AimDirection * SyncHoldDistance;

	/// <summary>Contextual prompt for the HUD.</summary>
	public string Hint
	{
		get
		{
			if (IsHolding)
				return "[RMB] throw    [scroll] push / pull    release [LMB] to drop";
			if (AimedProp is { HeldBy: 0 } prop)
				return $"[LMB] grab {prop.DisplayName}  ·  {prop.Mass:0.#} kg";
			return "";
		}
	}

	private static bool HasControl => Input.MouseMode == Input.MouseModeEnum.Captured;
	private PhysicsProp AimedProp => _grabRay.GetCollider() as PhysicsProp;
	private bool IsHolding => _heldProp != null && IsInstanceValid(_heldProp) && _heldProp.HeldBy == PeerId;

	private Node3D _head;
	private Camera3D _camera;
	private RayCast3D _grabRay;
	private MeshInstance3D _bodyMesh;
	private MeshInstance3D _visor;
	private Label3D _nameLabel;
	private PhysicsProp _heldProp;
	private float _gravity;

	public override void _EnterTree()
	{
		PeerId = int.Parse(Name.ToString());
		SetMultiplayerAuthority(PeerId);
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
		_bodyMesh = GetNode<MeshInstance3D>("Body");
		_visor = GetNode<MeshInstance3D>("Head/Visor");
		_nameLabel = GetNode<Label3D>("NameLabel");
		_gravity = (float)ProjectSettings.GetSetting("physics/3d/default_gravity");
		_grabRay.TargetPosition = new Vector3(0, 0, -GrabRange);

		bool isLocal = IsMultiplayerAuthority();
		SetUpVisuals(isLocal);

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
			RotateY(-motion.Relative.X * MouseSensitivity);
			float pitch = _head.Rotation.X - motion.Relative.Y * MouseSensitivity;
			_head.Rotation = new Vector3(Mathf.Clamp(pitch, -1.5f, 1.5f), 0, 0);
		}
		else if (@event.IsActionPressed("hold_farther"))
		{
			SyncHoldDistance = Mathf.Min(SyncHoldDistance + 0.25f, MaxHoldDistance);
		}
		else if (@event.IsActionPressed("hold_closer"))
		{
			SyncHoldDistance = Mathf.Max(SyncHoldDistance - 0.25f, MinHoldDistance);
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;

		if (!IsMultiplayerAuthority())
		{
			FollowNetworkState(dt);
			return;
		}

		Move(dt);
		UpdateCarrying();

		if (GlobalPosition.Y < KillPlaneY)
			MoveToSpawnPoint();

		WriteSyncState();
	}

	private void Move(float dt)
	{
		Vector3 velocity = Velocity;
		if (!IsOnFloor())
			velocity.Y -= _gravity * dt;
		else if (HasControl && Input.IsActionJustPressed("jump"))
			velocity.Y = JumpVelocity;

		Vector2 input = HasControl
			? Input.GetVector("move_left", "move_right", "move_forward", "move_back")
			: Vector2.Zero;
		Vector3 wishDirection = Transform.Basis * new Vector3(input.X, 0, input.Y);
		float speed = HasControl && Input.IsActionPressed("sprint") ? SprintSpeed : WalkSpeed;
		float acceleration = IsOnFloor() ? GroundAcceleration : AirAcceleration;

		Vector3 horizontal = new Vector3(velocity.X, 0, velocity.Z)
			.Lerp(wishDirection * speed, 1f - Mathf.Exp(-acceleration * dt));
		Velocity = new Vector3(horizontal.X, velocity.Y, horizontal.Z);
		MoveAndSlide();
	}

	// Hold LMB to carry, let go to drop (props keep their momentum, so you can fling them), RMB to throw.
	// The host decides who actually gets the prop; we just ask.
	private void UpdateCarrying()
	{
		if (_heldProp != null && (!IsInstanceValid(_heldProp) || (_heldProp.HeldBy != 0 && _heldProp.HeldBy != PeerId)))
			_heldProp = null;

		if (_heldProp == null)
		{
			if (HasControl && Input.IsActionJustPressed("grab") && AimedProp is { HeldBy: 0 } prop)
			{
				_heldProp = prop;
				prop.RpcId(1, nameof(PhysicsProp.RequestGrab));
			}
		}
		else if (HasControl && Input.IsActionJustPressed("throw"))
		{
			_heldProp.RpcId(1, nameof(PhysicsProp.RequestThrow));
			_heldProp = null;
		}
		else if (!Input.IsActionPressed("grab"))
		{
			_heldProp.RpcId(1, nameof(PhysicsProp.RequestRelease));
			_heldProp = null;
		}
	}

	private void WriteSyncState()
	{
		SyncPosition = GlobalPosition;
		SyncYaw = Rotation.Y;
		SyncPitch = _head.Rotation.X;
	}

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

		var spawn = (Node3D)spawns[PeerId % spawns.Count];
		GlobalPosition = spawn.GlobalPosition;
		Rotation = new Vector3(0, spawn.GlobalRotation.Y, 0);
		Velocity = Vector3.Zero;
	}

	private void SetUpVisuals(bool isLocal)
	{
		_bodyMesh.MaterialOverride = new StandardMaterial3D
		{
			AlbedoColor = Color.FromHsv(PeerId % 360 / 360f, 0.45f, 0.8f),
			Roughness = 0.8f,
		};
		_nameLabel.Text = PeerId == 1 ? "Host" : $"Researcher {PeerId % 1000}";

		if (isLocal)
		{
			// Keep our own shadow, but don't render our body inside our own camera.
			_bodyMesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.ShadowsOnly;
			_visor.CastShadow = GeometryInstance3D.ShadowCastingSetting.ShadowsOnly;
			_nameLabel.Visible = false;
		}
	}
}
