using Godot;
using RND.Audio;

namespace RND.Vfx;

/// <summary>One-shot effects the host triggers for everyone (splashes, sounds). Lives in each level.</summary>
public partial class Effects : Node3D
{
	/// <summary>
	/// A positional world sound on the "World" bus (so it's muffled by the listener's mask). Loudness
	/// is the same radius the AI hears it at, so what you hear and what it hears line up. Use
	/// Level.EmitSound rather than calling this directly.
	/// </summary>
	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void PlaySound(int sound, Vector3 position, float loudness)
	{
		var speaker = new AudioStreamPlayer3D
		{
			Stream = SoundBank.Get((SoundKind)sound),
			Bus = "World",
			UnitSize = loudness * 0.4f,
			MaxDistance = loudness * 2.5f,
			PitchScale = (float)GD.RandRange(0.9, 1.1),
		};
		AddChild(speaker);
		speaker.GlobalPosition = position;
		speaker.Finished += speaker.QueueFree;
		speaker.Play();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void Splash(Vector3 position, Vector3 normal, Color color, float radius)
	{
		// A quick expanding burst...
		var burstMaterial = new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			AlbedoColor = color,
		};
		var burst = new MeshInstance3D
		{
			Mesh = new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 12, Rings = 6 },
			MaterialOverride = burstMaterial,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		AddChild(burst);
		burst.GlobalPosition = position;
		burst.Scale = Vector3.One * 0.2f;

		Tween burstTween = burst.CreateTween().SetParallel();
		burstTween.TweenProperty(burst, "scale", Vector3.One * radius * 2f, 0.35f)
			.SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
		burstTween.TweenProperty(burstMaterial, "albedo_color:a", 0f, 0.35f);
		burstTween.Chain().TweenCallback(Callable.From(burst.QueueFree));

		// ...and a puddle stuck to whatever it hit, which fades out.
		var puddleMaterial = new StandardMaterial3D
		{
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			AlbedoColor = color,
			EmissionEnabled = true,
			Emission = color,
			EmissionEnergyMultiplier = 0.5f,
			Roughness = 0.1f,
		};
		var puddle = new MeshInstance3D
		{
			Mesh = new CylinderMesh { TopRadius = radius * 0.6f, BottomRadius = radius * 0.6f, Height = 0.02f, RadialSegments = 20, Rings = 1 },
			MaterialOverride = puddleMaterial,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		AddChild(puddle);
		puddle.GlobalTransform = new Transform3D(BasisWithUp(normal), position + normal.Normalized() * 0.015f);

		Tween puddleTween = puddle.CreateTween();
		puddleTween.TweenInterval(2f);
		puddleTween.TweenProperty(puddleMaterial, "albedo_color:a", 0f, 2f);
		puddleTween.TweenCallback(Callable.From(puddle.QueueFree));
	}

	private static Basis BasisWithUp(Vector3 up)
	{
		up = up.LengthSquared() > 0.0001f ? up.Normalized() : Vector3.Up;
		Vector3 side = up.Cross(Mathf.Abs(up.Y) < 0.99f ? Vector3.Up : Vector3.Right).Normalized();
		return new Basis(side, up, side.Cross(up));
	}
}
