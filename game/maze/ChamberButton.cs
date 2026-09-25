using Godot;
using RND.Audio;
using RND.Core;
using RND.Levels;
using RND.Players;
using RND.Props;

namespace RND.Maze;

/// <summary>
/// A button on a pedestal that stays pressed for a few seconds: [E] on it, or hit it with a thrown
/// prop, so someone has to run or throw. Pressing it again restarts the clock. The host keeps the
/// time and replicates Pressed; the cap's glow fades from blue to orange as the time runs out.
/// </summary>
public partial class ChamberButton : StaticBody3D, ISwitch
{
	private const float MaxUseDistance = 3f;
	private const float MinHitSpeed = 2f;
	private const float PressDepth = 0.04f;
	private const float ClickRadius = 5f;
	private static readonly Color OffColour = new(1f, 0.35f, 0.1f);
	private static readonly Color OnColour = new(0.2f, 0.6f, 1f);

	[Export] public float OpenSeconds { get; set; } = 5f;

	// Written by the host, replicated to clients. Presses counts presses, so a re-press restarts the
	// glow everywhere.
	[Export] public bool Pressed { get; set; }
	[Export] public int Presses { get; set; }

	private MeshInstance3D _cap;
	private StandardMaterial3D _light;
	private float _restY;
	private double _releaseAt; // host
	private double _pressedAt; // every peer, for the glow
	private int _seenPresses;

	private static double Now => Time.GetTicksMsec() / 1000.0;

	public override void _Ready()
	{
		CollisionLayer = Layers.World;
		_cap = GetNode<MeshInstance3D>("Cap");
		_restY = _cap.Position.Y;
		_light = (StandardMaterial3D)_cap.GetActiveMaterial(0); // local to scene, so each button has its own
		GetNode<Area3D>("HitZone").BodyEntered += OnHit;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (Multiplayer.IsServer())
			Pressed = Now < _releaseAt;
	}

	public override void _Process(double delta)
	{
		if (Presses != _seenPresses)
		{
			_seenPresses = Presses;
			_pressedAt = Now;
		}

		float left = Pressed ? Mathf.Clamp(1f - (float)(Now - _pressedAt) / OpenSeconds, 0f, 1f) : 0f;
		_cap.Position = _cap.Position with { Y = Pressed ? _restY - PressDepth : _restY };
		_light.Emission = OffColour.Lerp(OnColour, left);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void RequestPress()
	{
		if (!Multiplayer.IsServer())
			return;

		if (Player.TryGet(Multiplayer.SenderId(), out Player player) && !player.IsDead && player.EyePosition.DistanceTo(GlobalPosition) <= MaxUseDistance)
			Press();
	}

	private void OnHit(Node3D body)
	{
		if (Multiplayer.IsServer() && body is PhysicsProp prop && prop.LinearVelocity.Length() >= MinHitSpeed)
			Press();
	}

	private void Press()
	{
		_releaseAt = Now + OpenSeconds;
		Presses++;
		Level.Current?.EmitSound(GlobalPosition, ClickRadius, SoundKind.Impact);
	}
}
