using Godot;

namespace RND.Combat;

/// <summary>
/// Hit points for anything that can be hurt. Add it as a child named "Health".
/// The host owns the value and replicates it (MultiplayerSynchronizer child), so every peer
/// gets Changed / Damaged / Died / Revived and can react visually.
/// </summary>
public partial class Health : Node
{
	[Signal] public delegate void ChangedEventHandler(float current, float max);
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

	public bool IsDead => Current <= 0f;

	// -1 = not set yet. Spawn replication can set Current before _Ready, so _Ready mustn't reset it.
	private float _current = -1f;

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

	/// <summary>Host only; ignored elsewhere.</summary>
	public void Revive()
	{
		if (Multiplayer.IsServer())
			SetCurrent(MaxHealth, 0);
	}

	private void SetCurrent(float value, int sourcePeerId)
	{
		float old = Current;
		_current = value;
		if (Mathf.IsEqualApprox(old, value))
			return;

		EmitSignal(SignalName.Changed, value, MaxHealth);
		if (value < old)
			EmitSignal(SignalName.Damaged, old - value, sourcePeerId);

		if (old > 0f && value <= 0f)
			EmitSignal(SignalName.Died, sourcePeerId);
		else if (old <= 0f && value > 0f)
			EmitSignal(SignalName.Revived);
	}
}
