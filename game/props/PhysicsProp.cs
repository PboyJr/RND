using Godot;
using RND.Core;
using RND.Players;

namespace RND.Props;

/// <summary>
/// Anything players can grab, carry and throw: crates, specimens, loot.
/// The host runs the real physics. Clients get a frozen copy that follows the host's replicated
/// transform, so everyone sees the same thing and nobody can desync a prop.
/// </summary>
public partial class PhysicsProp : RigidBody3D
{
	private const float SmoothRate = 20f;
	private const float SnapDistance = 3f;
	private const float MaxGrabDistance = 4f;

	[Export] public string DisplayName { get; set; } = "Object";

	[ExportGroup("Carrying")]
	// How hard the prop is pulled toward the hold point, and how fast it may move to get there.
	[Export] public float FollowStrength { get; set; } = 12f;
	[Export] public float MaxFollowSpeed { get; set; } = 10f;
	// Divided by mass, so heavy props feel sluggish and sag a little.
	[Export] public float CarryResponse { get; set; } = 4f;
	// Throw impulse, capped so light props don't rocket off.
	[Export] public float ThrowImpulse { get; set; } = 40f;
	[Export] public float MaxThrowSpeed { get; set; } = 12f;
	// Dropped if it lags this far behind the hold point (e.g. snagged on a doorframe).
	[Export] public float BreakDistance { get; set; } = 3f;

	// Written by the host, replicated to clients by the MultiplayerSynchronizer.
	[ExportGroup("Network")]
	[Export] public Vector3 SyncPosition { get; set; }
	[Export] public Quaternion SyncRotation { get; set; } = Quaternion.Identity;

	/// <summary>Peer id of the player carrying this, or 0.</summary>
	[Export]
	public int HeldBy
	{
		get => _heldBy;
		set => SetHeldBy(value);
	}

	private int _heldBy;

	public override void _Ready()
	{
		SyncPosition = GlobalPosition;
		SyncRotation = GlobalBasis.GetRotationQuaternion();

		if (!Multiplayer.IsServer())
		{
			FreezeMode = FreezeModeEnum.Kinematic;
			Freeze = true;
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!Multiplayer.IsServer())
		{
			FollowSyncedTransform((float)delta);
			return;
		}

		if (HeldBy != 0 && !(Player.TryGet(HeldBy, out Player holder) && holder.HoldPoint.DistanceTo(GlobalPosition) < BreakDistance))
			HeldBy = 0;

		SyncPosition = GlobalPosition;
		SyncRotation = GlobalBasis.GetRotationQuaternion();
	}

	public override void _IntegrateForces(PhysicsDirectBodyState3D state)
	{
		if (HeldBy == 0 || !Multiplayer.IsServer() || !Player.TryGet(HeldBy, out Player holder))
			return;

		Vector3 toTarget = holder.HoldPoint - state.Transform.Origin;
		Vector3 desired = (toTarget * FollowStrength).LimitLength(MaxFollowSpeed);
		float response = Mathf.Clamp(CarryResponse / Mass, 0.05f, 1f);
		state.LinearVelocity = state.LinearVelocity.Lerp(desired, response);
		state.AngularVelocity = state.AngularVelocity.Lerp(Vector3.Zero, 0.2f);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void RequestGrab()
	{
		if (!Multiplayer.IsServer() || HeldBy != 0)
			return;

		int sender = Multiplayer.SenderId();
		if (Player.TryGet(sender, out Player player) && player.EyePosition.DistanceTo(GlobalPosition) < MaxGrabDistance)
			HeldBy = sender;
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void RequestRelease()
	{
		if (Multiplayer.IsServer() && HeldBy == Multiplayer.SenderId())
			HeldBy = 0;
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void RequestThrow()
	{
		if (!Multiplayer.IsServer() || HeldBy != Multiplayer.SenderId() || !Player.TryGet(HeldBy, out Player player))
			return;

		HeldBy = 0;
		ApplyCentralImpulse(player.AimDirection * Mathf.Min(ThrowImpulse, MaxThrowSpeed * Mass));
	}

	private void SetHeldBy(int peerId)
	{
		if (peerId == _heldBy)
			return;

		IgnoreCollisionsWith(_heldBy, false);
		_heldBy = peerId;
		IgnoreCollisionsWith(_heldBy, true);

		if (_heldBy != 0)
			Sleeping = false;
	}

	// The holder shouldn't be able to stand on (or get shoved by) what they're carrying.
	private void IgnoreCollisionsWith(int peerId, bool ignore)
	{
		if (peerId == 0 || !Player.TryGet(peerId, out Player player))
			return;

		if (ignore)
		{
			AddCollisionExceptionWith(player);
			player.AddCollisionExceptionWith(this);
		}
		else
		{
			RemoveCollisionExceptionWith(player);
			player.RemoveCollisionExceptionWith(this);
		}
	}

	private void FollowSyncedTransform(float delta)
	{
		float weight = 1f - Mathf.Exp(-SmoothRate * delta);
		Vector3 position = GlobalPosition.DistanceTo(SyncPosition) > SnapDistance
			? SyncPosition
			: GlobalPosition.Lerp(SyncPosition, weight);
		Quaternion rotation = GlobalBasis.GetRotationQuaternion().Normalized()
			.Slerp(SyncRotation.Normalized(), weight);
		GlobalTransform = new Transform3D(new Basis(rotation), position);
	}
}
