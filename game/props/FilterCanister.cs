using Godot;
using RND.Audio;
using RND.Core;
using RND.Levels;
using RND.Players;

namespace RND.Props;

/// <summary>
/// A spare gas mask filter. It's a normal prop (carry it, throw it to a teammate), and [E] on it,
/// or while holding it, screws it on. Fresh filters hiss, and enemies nearby can hear that.
/// </summary>
public partial class FilterCanister : PhysicsProp
{
	private const float MaxUseDistance = 3f;
	private const float HissNoiseRadius = 4f;

	/// <summary>
	/// Used up. Replicated, and hides it everywhere. It's placed in the level rather than spawned, so it
	/// can't simply be freed on clients.
	/// </summary>
	[Export]
	public bool Consumed
	{
		get => _consumed;
		set
		{
			_consumed = value;
			Visible = !value;
			CollisionLayer = value ? 0u : Layers.Props;
			CollisionMask = value ? 0u : Layers.World | Layers.Players | Layers.Props | Layers.Entities;
			if (value && IsInsideTree())
				Freeze = true; // nothing left to collide with, so don't let the host's copy fall forever
		}
	}

	private bool _consumed;

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void RequestUse()
	{
		if (!Multiplayer.IsServer() || Consumed)
			return;

		int sender = Multiplayer.SenderId();
		if (!Player.TryGet(sender, out Player player) || player.IsDead || player.EyePosition.DistanceTo(GlobalPosition) > MaxUseDistance)
			return;

		HeldBy = 0;
		Consumed = true;
		player.Respirator.Refill();
		Level.Current?.EmitSound(GlobalPosition, HissNoiseRadius, SoundKind.Hiss);
	}
}
