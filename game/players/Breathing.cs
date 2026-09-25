using Godot;

namespace RND.Players;

/// <summary>
/// The local player's breathing: a rhythm that speeds up with effort, settles slowly afterwards and
/// turns into gasps when the filter's spent. It's the one clock for both the visor's breath fog and
/// the breathing you hear, so every fog puff lands on an exhale. Only runs for the player this machine
/// controls. (Filter drain on the host is judged separately, from how far the player moves.)
/// </summary>
public partial class Breathing : Node
{
	[Export] public float CalmBreathsPerSecond { get; set; } = 0.25f;
	[Export] public float ExertedBreathsPerSecond { get; set; } = 0.9f;
	[Export] public float GaspsPerSecond { get; set; } = 1.5f;
	// Heavy breathing lingers this long after you stop sprinting.
	[Export] public float RecoverySeconds { get; set; } = 3f;

	/// <summary>0 calm .. 1 flat out; jumps up with effort, settles over RecoverySeconds.</summary>
	public float Exertion { get; private set; }
	/// <summary>0 .. 1: how much you're choking on a spent filter.</summary>
	public float Choke { get; private set; }
	/// <summary>Breaths per second right now.</summary>
	public float Rate { get; private set; }
	/// <summary>0..1 through the current breath: first half exhale, second half inhale.</summary>
	public float Phase { get; private set; }
	public float Exhale => Mathf.Max(0f, Mathf.Sin(Phase * Mathf.Tau));

	private Player _player;

	public override void _Ready() => _player = GetParent<Player>();

	public override void _Process(double delta)
	{
		if (!_player.IsMultiplayerAuthority())
			return;

		float dt = (float)delta;
		float speed = new Vector2(_player.Velocity.X, _player.Velocity.Z).Length();
		float effort = _player.IsDead ? 0f : Mathf.Clamp((speed - 1f) / (_player.SprintSpeed - 1f), 0f, 1f);
		Exertion = effort > Exertion
			? Mathf.Lerp(Exertion, effort, 1f - Mathf.Exp(-4f * dt))
			: Mathf.MoveToward(Exertion, effort, dt / RecoverySeconds);
		Choke = Mathf.MoveToward(Choke, _player.Respirator.IsSpent && !_player.IsDead ? 1f : 0f, dt * 2f);

		Rate = Mathf.Lerp(Mathf.Lerp(CalmBreathsPerSecond, ExertedBreathsPerSecond, Exertion), GaspsPerSecond, Choke);
		Phase = (Phase + Rate * dt) % 1f;
	}
}
