using Godot;

namespace RND.Audio;

/// <summary>
/// A level's background: a looping room tone (low rumble, ventilation hum, duct air) plus sparse
/// distant events (creaks, drips, far-off booms, vent rattles) placed at random around the listener.
/// It's local to each player, since it's atmosphere, not gameplay (enemies don't react to it). It
/// plays on the World bus, so it's muffled by the mask and echoes like everything else.
/// </summary>
public partial class Ambience : Node3D
{
	[Export] public float RoomToneVolumeDb { get; set; } = -9f;
	[Export] public float MinEventSeconds { get; set; } = 4f;
	[Export] public float MaxEventSeconds { get; set; } = 12f;
	[Export] public float MinEventDistance { get; set; } = 7f;
	[Export] public float MaxEventDistance { get; set; } = 18f;

	private static readonly SoundKind[] Events = { SoundKind.Creak, SoundKind.Drip, SoundKind.Rumble, SoundKind.Rattle };

	private readonly RandomNumberGenerator _rng = new();
	private float _untilNextEvent;

	public override void _Ready()
	{
		var roomTone = new AudioStreamPlayer { Stream = SoundBank.RoomTone(), Bus = "World", VolumeDb = RoomToneVolumeDb };
		AddChild(roomTone);
		roomTone.Play();
		_untilNextEvent = _rng.RandfRange(MinEventSeconds, MaxEventSeconds);
	}

	public override void _Process(double delta)
	{
		_untilNextEvent -= (float)delta;
		if (_untilNextEvent > 0f)
			return;

		_untilNextEvent = _rng.RandfRange(MinEventSeconds, MaxEventSeconds);
		PlayDistantEvent();
	}

	/// <summary>A random creak, drip, boom or rattle somewhere off in the distance.</summary>
	public void PlayDistantEvent()
	{
		Camera3D listener = GetViewport().GetCamera3D();
		if (listener == null)
			return;

		Vector3 offset = Vector3.Forward.Rotated(Vector3.Up, _rng.RandfRange(0f, Mathf.Tau)) * _rng.RandfRange(MinEventDistance, MaxEventDistance);
		var speaker = new AudioStreamPlayer3D
		{
			Stream = SoundBank.Get(Events[_rng.RandiRange(0, Events.Length - 1)]),
			Bus = "World",
			UnitSize = 3f,
			MaxDistance = MaxEventDistance * 2f,
			PitchScale = _rng.RandfRange(0.8f, 1.15f),
		};
		AddChild(speaker);
		speaker.GlobalPosition = listener.GlobalPosition + offset + Vector3.Up * _rng.RandfRange(-0.5f, 2.5f);
		speaker.Finished += speaker.QueueFree;
		speaker.Play();
	}
}
