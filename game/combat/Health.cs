using Godot;

namespace RND.Combat;

/// <summary>
/// Hit points for anything that can be hurt. Add it as a child named "Health".
/// The host owns the value and replicates it (MultiplayerSynchronizer child), so every peer
/// gets Changed / Damaged / Died / Revived and can react visually.
/// Damage is physical (hits: cracks the visor, flashes the body) or mental (withdrawal from the
/// mask's gas). Both come off the same pool; Mental remembers how much of the loss was mental.
/// </summary>
public partial class Health : Node
{
	[Signal] public delegate void ChangedEventHandler(float current, float max);
	/// <summary>Physical damage only: mental damage changes Current and Mental but doesn't fire this.</summary>
	[Signal] public delegate void DamagedEventHandler(float amount, int sourcePeerId);
	[Signal] public delegate void DiedEventHandler(int sourcePeerId);
	[Signal] public delegate void RevivedEventHandler();

	[Export] public float MaxHealth { get; set; } = 100f;

	/// <summary>Replicated from the host. Change it with TakeDamage / Revive (host only).</summary>
	[Export]
	public float Current
	{
		get => _current < 0f ? MaxHealth : _current;
		set => SetCurrent(value, 0);
	}

	/// <summary>
	/// How much of the lost health is mental. Replicated from the host, listed before Current in the
	/// synchronizer so a client already knows a drop was mental when the new Current arrives.
	/// </summary>
	[Export] public float Mental { get; set; }

	/// <summary>Health counting only physical damage: what the visor's cracks show.</summary>
	public float Physical => Mathf.Min(MaxHealth, Current + Mental);

	public bool IsDead => Current <= 0f;

	// -1 = not set yet. Spawn replication can set Current before _Ready, so _Ready mustn't reset it.
	private float _current = -1f;
	private float _mentalSeen; // Mental as of the last Current change, to tell mental drops from physical

	public static Health Of(Node node) => node?.GetNodeOrNull<Health>("Health");

	public override void _Ready()
	{
		if (_current < 0f)
			_current = MaxHealth;
	}

	/// <summary>Host only; ignored elsewhere.</summary>
	public void TakeDamage(float amount, int sourcePeerId)
	{
		if (!Multiplayer.IsServer() || IsDead || amount <= 0f)
			return;

		SetCurrent(Mathf.Max(Current - amount, 0f), sourcePeerId);
	}

	/// <summary>Host only; ignored elsewhere. Hurts the mind, not the body: no cracks, no hit flash.</summary>
	public void TakeMentalDamage(float amount)
	{
		if (!Multiplayer.IsServer() || IsDead || amount <= 0f)
			return;

		float dealt = Mathf.Min(amount, Current);
		Mental += dealt;
		SetCurrent(Current - dealt, 0);
	}

	/// <summary>Host only; ignored elsewhere.</summary>
	public void Revive()
	{
		if (!Multiplayer.IsServer())
			return;

		Mental = 0f;
		SetCurrent(MaxHealth, 0);
	}

	private void SetCurrent(float value, int sourcePeerId)
	{
		float old = Current;
		_current = value;
		float mentalLoss = Mathf.Max(Mental - _mentalSeen, 0f);
		_mentalSeen = Mental;
		if (Mathf.IsEqualApprox(old, value))
			return;

		EmitSignal(SignalName.Changed, value, MaxHealth);
		float physicalLoss = old - value - mentalLoss;
		if (physicalLoss > 0.001f)
			EmitSignal(SignalName.Damaged, physicalLoss, sourcePeerId);

		if (old > 0f && value <= 0f)
			EmitSignal(SignalName.Died, sourcePeerId);
		else if (old <= 0f && value > 0f)
			EmitSignal(SignalName.Revived);
	}
}
