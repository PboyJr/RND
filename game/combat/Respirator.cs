using Godot;

namespace RND.Combat;

/// <summary>
/// The gas mask's filter. Host-owned and replicated, like Health: add it as a child named
/// "Respirator" with a MultiplayerSynchronizer. It drains faster the harder you breathe; once it's
/// spent you choke (damage in gasps) until you screw on a spare.
/// </summary>
public partial class Respirator : Node
{
	// Seconds of calm breathing a fresh filter lasts.
	[Export] public float Capacity { get; set; } = 180f;
	// Filter-seconds used per second: standing still vs. sprinting flat out.
	[Export] public float CalmDrain { get; set; } = 0.6f;
	[Export] public float ExertedDrain { get; set; } = 2.5f;
	// Choking on a spent filter: damage per gasp, and time between gasps.
	[Export] public float ChokeDamage { get; set; } = 4f;
	[Export] public float ChokeInterval { get; set; } = 0.5f;

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
	private float _chokeTimer;

	/// <summary>Host only. Exertion: 0 = standing still, 1 = sprinting.</summary>
	public void Breathe(float delta, float exertion, Health health)
	{
		if (!Multiplayer.IsServer() || health.IsDead)
			return;

		Remaining = Mathf.Max(0f, Remaining - delta * Mathf.Lerp(CalmDrain, ExertedDrain, exertion));
		if (!IsSpent)
		{
			_chokeTimer = ChokeInterval; // the first gasp comes a beat after it runs out
			return;
		}

		_chokeTimer -= delta;
		if (_chokeTimer > 0f)
			return;

		_chokeTimer = ChokeInterval;
		health.TakeDamage(ChokeDamage, 0);
	}

	/// <summary>Host only.</summary>
	public void Refill()
	{
		if (Multiplayer.IsServer())
			Remaining = Capacity;
	}
}
