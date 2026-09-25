using System;
using Godot;

namespace RND.Audio;

/// <summary>
/// Renders every procedural sound to .wav files so they can be auditioned without playing the game,
/// and measures them (peak, RMS) so tests can catch clipping or silence. Used by the smoke test.
/// </summary>
public partial class AudioPreview : RefCounted
{
	private const int Chunk = 512;

	/// <summary>Returns { name: Vector2(peak, rms) } for everything it wrote to `directory`.</summary>
	public Godot.Collections.Dictionary Render(string directory)
	{
		var levels = new Godot.Collections.Dictionary();

		foreach (SoundKind kind in Enum.GetValues<SoundKind>())
			levels[kind.ToString().ToLower()] = Save(directory, kind.ToString().ToLower(), SoundBank.Synthesize(kind, new Random(SoundBank.Seed)));
		levels["room_tone"] = Save(directory, "room_tone", SoundBank.SynthesizeRoomTone(new Random(SoundBank.Seed)));

		var calm = new MaskState { Alive = true, BreathRate = 0.25f };
		levels["mask_calm"] = SaveMask(directory, "mask_calm", calm, 8f, fresh: false);
		levels["mask_sprinting"] = SaveMask(directory, "mask_sprinting", calm with { BreathRate = 0.9f, Exertion = 1f }, 6f, fresh: false);
		levels["mask_worn_filter"] = SaveMask(directory, "mask_worn_filter", calm with { BreathRate = 0.4f, FilterWear = 0.9f }, 8f, fresh: false);
		levels["mask_choking"] = SaveMask(directory, "mask_choking", calm with { BreathRate = 0.3f, Choke = 1f, FilterWear = 1f, Danger = 0.6f }, 6f, fresh: false);
		levels["mask_out_of_gas"] = SaveMask(directory, "mask_out_of_gas", calm with { BreathRate = 0.3f, Choke = 1f, FilterWear = 1f }, 10f, fresh: false);
		levels["mask_half_health"] = SaveMask(directory, "mask_half_health", calm with { BreathRate = 0.3f, Danger = 0.35f }, 8f, fresh: false);
		levels["mask_low_health"] = SaveMask(directory, "mask_low_health", calm with { BreathRate = 0.4f, Exertion = 0.3f, Danger = 0.8f }, 8f, fresh: false);
		levels["mask_mental"] = SaveMask(directory, "mask_mental", calm with { BreathRate = 0.4f, FilterWear = 0.95f, Mind = 0.6f }, 8f, fresh: false);
		levels["mask_fresh_filter"] = SaveMask(directory, "mask_fresh_filter", calm, 3f, fresh: true);
		return levels;
	}

	private static Vector2 SaveMask(string directory, string name, MaskState state, float seconds, bool fresh)
	{
		var synth = new MaskSynth();
		if (fresh)
			synth.PlayFreshFilter();

		var samples = new float[(int)(seconds * SoundBank.MixRate)];
		var chunk = new Vector2[Chunk];
		for (int start = 0; start < samples.Length; start += Chunk)
		{
			synth.Render(chunk, state);
			state.BreathPhase = (state.BreathPhase + state.BreathRate * Chunk / SoundBank.MixRate) % 1f;
			for (int i = 0; i < Chunk && start + i < samples.Length; i++)
				samples[start + i] = chunk[i].X;
		}
		return Save(directory, name, samples);
	}

	private static Vector2 Save(string directory, string name, float[] samples)
	{
		SoundBank.ToWav(samples).SaveToWav(directory.PathJoin(name + ".wav"));

		float peak = 0f;
		double energy = 0;
		foreach (float v in samples)
		{
			peak = Mathf.Max(peak, Mathf.Abs(v));
			energy += v * v;
		}
		return new Vector2(peak, (float)Math.Sqrt(energy / Math.Max(1, samples.Length)));
	}
}
