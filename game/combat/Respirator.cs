using Godot;

namespace RND.Combat;

/// <summary>
/// The gas mask's filter. Host-owned and replicated, like Health: add it as a child named
/// "Respirator" with a MultiplayerSynchronizer. It drains faster the harder you breathe. Running low
/// hurts your mind, not your body: withdrawal (mental damage, see Health) starts at LowGasFraction and
/// gets worse until you screw on a spare.
/// </summary>
public partial class Respirator : Node
{
	// Seconds of calm breathing a fresh filter lasts.
	[Export] public float Capacity { get; set; } = 30f; // 180 normally; 30 while testing withdrawal
	// Filter-seconds used per second: standing still vs. sprinting flat out.
	[Export] public float CalmDrain { get; set; } = 0.6f;
	[Export] public float ExertedDrain { get; set; } = 2.5f;
	// Withdrawal: starts below this much gas, and deals this much mental damage per second, from
	// MinWithdrawal just under the threshold up to MaxWithdrawal once the filter's spent. In ticks.
	[Export] public float LowGasFraction { get; set; } = 0.15f;
	[Export] public float MinWithdrawal { get; set; } = 0.5f;
	[Export] public float MaxWithdrawal { get; set; } = 1.5f;
	[Export] public float WithdrawalInterval { get; set; } = 1f;

	/// <summary>Filter-seconds left. Replicated from the host; change it with Breathe / Refill (host only).</summary>
	[Export]
	public float Remaining
	{
		get => _remaining < 0f ? Capacity : _remaining;
		set => _remaining = value;
	}

	public float Fraction => Capacity > 0f ? Mathf.Clamp(Remaining / Capacity, 0f, 1f) : 0f;
	public bool IsSpent => Remaining <= 0f;

	// -1 = not set yet (spawn replication can set it before _Ready), same trick as Health.
	private float _remaining = -1f;
	private float _withdrawalTimer;

	/// <summary>Host only. Exertion: 0 = standing still, 1 = sprinting.</summary>
	public void Breathe(float delta, float exertion, Health health)
	{
		if (!Multiplayer.IsServer() || health.IsDead)
			return;

		Remaining = Mathf.Max(0f, Remaining - delta * Mathf.Lerp(CalmDrain, ExertedDrain, exertion));
		if (Fraction >= LowGasFraction)
		{
			_withdrawalTimer = WithdrawalInterval; // the first tick comes a beat after it drops below
			return;
		}

		_withdrawalTimer -= delta;
		if (_withdrawalTimer > 0f)
			return;

		_withdrawalTimer = WithdrawalInterval;
		float perSecond = Mathf.Lerp(MaxWithdrawal, MinWithdrawal, Fraction / LowGasFraction);
		health.TakeMentalDamage(perSecond * WithdrawalInterval);
	}

	/// <summary>Host only.</summary>
	public void Refill()
	{
		if (Multiplayer.IsServer())
			Remaining = Capacity;
	}
}
