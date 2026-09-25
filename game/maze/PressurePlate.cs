using System.Linq;
using Godot;
using RND.Core;
using RND.Players;
using RND.Props;

namespace RND.Maze;

/// <summary>
/// A floor plate that's pressed while enough weight rests on it: a player, the heavy case, or a
/// pile of crates. Carried props don't count. The host weighs it and replicates Pressed; doors
/// read that on every peer.
/// </summary>
public partial class PressurePlate : Area3D, ISwitch
{
	private const float PlayerWeight = 60f;
	private const float PressDepth = 0.04f;
	private static readonly Color OffColour = new(1f, 0.35f, 0.1f);
	private static readonly Color OnColour = new(0.2f, 0.6f, 1f);

	// Enough for a player or the heavy case (40 kg), not a jar or a single crate.
	[Export] public float MinWeight { get; set; } = 30f;

	// Written by the host, replicated to clients.
	[Export] public bool Pressed { get; set; }

	private MeshInstance3D _top;
	private StandardMaterial3D _light;
	private float _restY;

	public override void _Ready()
	{
		CollisionLayer = 0;
		CollisionMask = Layers.Players | Layers.Props;
		Monitorable = false;
		_top = GetNode<MeshInstance3D>("Top");
		_restY = _top.Position.Y;
		_light = (StandardMaterial3D)_top.GetActiveMaterial(0); // local to scene, so each plate has its own
	}

	public override void _PhysicsProcess(double delta)
	{
		if (Multiplayer.IsServer())
			Pressed = Weight() >= MinWeight;
	}

	public override void _Process(double delta)
	{
		_top.Position = _top.Position with { Y = Pressed ? _restY - PressDepth : _restY };
		_light.Emission = Pressed ? OnColour : OffColour;
	}

	private float Weight() => GetOverlappingBodies().Sum(body => body switch
	{
		Player => PlayerWeight,
		PhysicsProp prop when prop.HeldBy == 0 => prop.Mass,
		_ => 0f,
	});
}
