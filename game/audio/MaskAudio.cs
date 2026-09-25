using System;
using Godot;
using RND.Players;

namespace RND.Audio;

/// <summary>
/// Plays the local player's in-mask sound (MaskSynth, on the unmuffled "Mask" bus) and muffles the
/// world through the mask by driving the "World" bus low-pass. The more cracked the visor, the more
/// sound leaks in. In menus nothing is muffled.
/// </summary>
public partial class MaskAudio : Node
{
	// World low-pass cutoff through an intact mask vs. a shattered one.
	[Export] public float SealedCutoffHz { get; set; } = 1100f;
	[Export] public float ShatteredCutoffHz { get; set; } = 7000f;

	private readonly MaskSynth _synth = new();
	private AudioStreamGeneratorPlayback _playback;
	private AudioEffectLowPassFilter _muffle;
	private Vector2[] _buffer = Array.Empty<Vector2>();
	private float _lastFilter = -1f;

	public override void _Ready()
	{
		var speaker = new AudioStreamPlayer
		{
			Stream = new AudioStreamGenerator { MixRate = SoundBank.MixRate, BufferLength = 0.1f },
			Bus = "Mask",
		};
		AddChild(speaker);
		speaker.Play();
		_playback = speaker.GetStreamPlayback() as AudioStreamGeneratorPlayback;

		_muffle = (AudioEffectLowPassFilter)AudioServer.GetBusEffect(AudioServer.GetBusIndex("World"), 0);
	}

	public override void _Process(double delta)
	{
		Player player = Player.Local;
		float health = player == null ? 1f : player.Health.Current / player.Health.MaxHealth;

		float damage = player == null ? 0f : 1f - player.Health.Physical / player.Health.MaxHealth; // only cracks let sound in
		_muffle.CutoffHz = player == null ? 20000f : Mathf.Lerp(SealedCutoffHz, ShatteredCutoffHz, damage * damage);

		if (player != null)
		{
			float filter = player.Respirator.Remaining;
			if (_lastFilter >= 0f && filter > _lastFilter + 1f)
				_synth.PlayFreshFilter();
			_lastFilter = filter;
		}

		int frames = _playback?.GetFramesAvailable() ?? 0;
		if (frames <= 0)
			return;

		if (_buffer.Length != frames)
			_buffer = new Vector2[frames];
		_synth.Render(_buffer, player == null ? default : new MaskState
		{
			Alive = !player.IsDead,
			BreathPhase = player.Breathing.Phase,
			BreathRate = player.Breathing.Rate,
			Exertion = player.Breathing.Exertion,
			Choke = player.Breathing.Choke,
			FilterWear = 1f - player.Respirator.Fraction,
			// Kicks in audibly the moment you drop under half health, then builds to nearly dead.
			Danger = health < 0.5f ? Mathf.Lerp(0.35f, 1f, (0.5f - health) / 0.5f) : 0f,
			Mind = player.Health.MentalFraction,
		});
		_playback.PushBuffer(_buffer);
	}
}
