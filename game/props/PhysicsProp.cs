using System.Collections.Generic;
using Godot;
using RND.Audio;
using RND.Core;
using RND.Levels;
using RND.Players;

namespace RND.Props;

/// <summary>
/// Anything players can grab, carry and throw: crates, specimens, loot.
/// The host runs the real physics. Clients get a frozen copy that follows the host's replicated
/// transform, so everyone sees the same thing and nobody can desync a prop.
/// The exception is the one you're carrying: your game simulates its own copy, so it hangs where
/// you hold it with no network lag, and when you let go (or throw) the host carries on from where
/// your copy was. Once it has come to rest where the host has it too, it's a frozen copy again.
/// </summary>
public partial class PhysicsProp : RigidBody3D
{
	private const float SmoothRate = 20f;
	private const float SnapDistance = 3f;
	private const float MaxGrabDistance = 4f;
	// The host takes the holder's word for where the prop was on release only if it's roughly where
	// the host has it too (they differ by the network lag); anything further off is ignored.
	private const float AdoptDistance = 2f;
	private const float RestSpeed = 0.05f;
	private const float AgreeDistance = 0.25f;
	private const double MaxPredictSeconds = 4.0; // after letting go, hand back to the host by then at the latest

	// Host: who holds what, so nobody carries two things at once.
	private static readonly Dictionary<int, PhysicsProp> HeldByPeer = new();

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

	[ExportGroup("Noise")]
	// Losing at least this much speed in one step counts as a crash that enemies can hear.
	// Louder the harder and heavier it hits.
	[Export] public float ImpactNoiseSpeed { get; set; } = 2.5f;

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

	/// <summary>True while this client is simulating its own copy (carrying it, or just let go).</summary>
	public bool Predicting { get; private set; }

	private int _heldBy;
	private float _lastSpeed;
	private float _noiseCooldown;
	private double _letGoAt;
	private bool _letGoLocally; // client: we've let go, the host hasn't confirmed yet

	private static double Now => Time.GetTicksMsec() / 1000.0;
	private bool HeldByMe => _heldBy != 0 && _heldBy == Multiplayer.GetUniqueId();

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

	public override void _ExitTree()
	{
		if (_heldBy != 0 && HeldByPeer.TryGetValue(_heldBy, out PhysicsProp held) && held == this)
			HeldByPeer.Remove(_heldBy);
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!Multiplayer.IsServer())
		{
			UpdatePrediction();
			if (!Predicting)
				FollowSyncedTransform((float)delta);
			return;
		}

		if (HeldBy != 0 && !(Player.TryGet(HeldBy, out Player holder) && holder.HoldPoint.DistanceTo(GlobalPosition) < BreakDistance))
			HeldBy = 0;

		MakeImpactNoise((float)delta);
		SyncPosition = GlobalPosition;
		SyncRotation = GlobalBasis.GetRotationQuaternion();
	}

	// Client: simulate our own copy while we hold it, and after letting go until it has come to rest
	// where the host has it too. Then go back to following the host.
	private void UpdatePrediction()
	{
		if (HeldByMe && !_letGoLocally)
		{
			if (!Predicting)
			{
				Predicting = true;
				Freeze = false;
				Sleeping = false;
			}
			return;
		}

		if (Predicting && ((HeldBy != 0 && !HeldByMe) || Now - _letGoAt > MaxPredictSeconds
			|| (LinearVelocity.Length() < RestSpeed && GlobalPosition.DistanceTo(SyncPosition) < AgreeDistance)))
		{
			Predicting = false;
			Freeze = true;
		}
	}

	// A sudden loss of speed means it hit something. Speeding up (being thrown) is silent, and so is
	// being carried.
	private void MakeImpactNoise(float delta)
	{
		float speed = LinearVelocity.Length();
		float lost = _lastSpeed - speed;
		_lastSpeed = speed;
		_noiseCooldown -= delta;

		if (HeldBy != 0 || lost < ImpactNoiseSpeed || _noiseCooldown > 0f)
			return;

		_noiseCooldown = 0.3f;
		Level.Current?.EmitSound(GlobalPosition, Mathf.Clamp(lost * 2f * Mathf.Sqrt(Mass / 4f), 3f, 18f), SoundKind.Impact);
	}

	// Pulls a held prop toward its holder's hold point: on the host, and on the holder's own game.
	public override void _IntegrateForces(PhysicsDirectBodyState3D state)
	{
		if (HeldBy == 0 || !(Multiplayer.IsServer() || (HeldByMe && !_letGoLocally)) || !Player.TryGet(HeldBy, out Player holder))
			return;

		Vector3 toTarget = holder.HoldPoint - state.Transform.Origin;
		Vector3 desired = (toTarget * FollowStrength).LimitLength(MaxFollowSpeed);
		float response = Mathf.Clamp(CarryResponse / Mass, 0.05f, 1f);
		state.LinearVelocity = state.LinearVelocity.Lerp(desired, response);
		state.AngularVelocity = state.AngularVelocity.Lerp(Vector3.Zero, 0.2f);
	}

	/// <summary>The holder lets go: it drops with whatever momentum it has.</summary>
	public void Release() => LetGo(Vector3.Zero);

	/// <summary>The holder throws it along `direction`.</summary>
	public void Throw(Vector3 direction) => LetGo(direction.Normalized());

	// Our copy stops being pulled at once (the host only confirms a round trip later) and, for a
	// throw, flies off at once; the host is sent its state to carry on from.
	private void LetGo(Vector3 throwDirection)
	{
		if (Predicting)
		{
			_letGoLocally = true;
			LinearVelocity += throwDirection * ThrowStrength / Mass;
		}
		_letGoAt = Now;
		RpcId(1, MethodName.RequestLetGo, Predicting, GlobalPosition, GlobalBasis.GetRotationQuaternion(), LinearVelocity, AngularVelocity, throwDirection);
	}

	private float ThrowStrength => Mathf.Min(ThrowImpulse, MaxThrowSpeed * Mass);

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void RequestGrab()
	{
		if (!Multiplayer.IsServer() || HeldBy != 0)
			return;

		int sender = Multiplayer.SenderId();
		if (HeldByPeer.ContainsKey(sender))
			return; // one thing at a time
		if (Player.TryGet(sender, out Player player) && !player.IsDead && player.EyePosition.DistanceTo(GlobalPosition) < MaxGrabDistance)
			HeldBy = sender;
	}

	/// <summary>
	/// Holder → host: let go (a throw if `throwDirection` isn't zero). A client that was simulating
	/// its own copy sends that copy's state (already thrown, for a throw); the host takes it over if
	/// it's close to where the host has the prop, so the prop goes where the holder saw it go.
	/// </summary>
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void RequestLetGo(bool predicted, Vector3 position, Quaternion rotation, Vector3 velocity, Vector3 angularVelocity, Vector3 throwDirection)
	{
		int sender = Multiplayer.SenderId();
		if (!Multiplayer.IsServer() || HeldBy != sender || !Player.TryGet(sender, out Player _))
			return;

		HeldBy = 0;
		bool thrown = throwDirection.LengthSquared() > 0.5f;
		if (predicted && position.DistanceTo(GlobalPosition) < AdoptDistance && rotation.LengthSquared() > 0.5f)
		{
			GlobalTransform = new Transform3D(new Basis(rotation.Normalized()), position);
			// Its velocity already includes the throw; cap it like a throw would be.
			LinearVelocity = velocity.LimitLength(MaxFollowSpeed + MaxThrowSpeed);
			AngularVelocity = angularVelocity;
			_lastSpeed = LinearVelocity.Length(); // not a crash
		}
		else if (thrown)
		{
			ApplyCentralImpulse(throwDirection.Normalized() * ThrowStrength);
		}
	}

	private void SetHeldBy(int peerId)
	{
		if (peerId == _heldBy)
			return;

		if (_heldBy != 0 && HeldByPeer.TryGetValue(_heldBy, out PhysicsProp held) && held == this)
			HeldByPeer.Remove(_heldBy);
		if (HeldByMe)
			_letGoAt = Now; // the host dropped it for us (snagged), or we let go

		IgnoreCollisionsWith(_heldBy, false);
		_heldBy = peerId;
		_letGoLocally = false;
		IgnoreCollisionsWith(_heldBy, true);

		// A held prop resting at the hold point mustn't fall asleep: a sleeping body stops being pulled.
		CanSleep = _heldBy == 0;
		if (_heldBy != 0)
		{
			HeldByPeer[_heldBy] = this;
			Sleeping = false;
		}
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
