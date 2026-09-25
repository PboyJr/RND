using System;
using System.Collections.Generic;
using Godot;

namespace RND.Audio;

public enum SoundKind
{
	// Gameplay sounds (Level.EmitSound: players and enemies hear them).
	Shatter, Impact, Hiss, Growl, Swipe, Splat,
	// Ambience (Ambience.cs: atmosphere only, enemies don't react).
	Creak, Drip, Rumble, Rattle,
}

/// <summary>
/// Every sound effect, synthesised in code on first use, so there are no audio files. (Repeats
/// still differ: players vary the pitch.) The recipes below are meant to be tuned by ear: render
/// them with the smoke test's "audio" role and listen. Mostly resonating noise rather than pure
/// tones, so things sound like objects, not synths.
/// </summary>
public static class SoundBank
{
	public const int MixRate = 22050;
	public const int Seed = 1; // same seed as the previews, so what you audition is what plays
	private const float RoomToneSeconds = 20f;

	private static readonly Dictionary<SoundKind, AudioStreamWav> Cache = new();
	private static AudioStreamWav _roomTone;

	public static AudioStreamWav Get(SoundKind kind)
	{
		if (!Cache.TryGetValue(kind, out AudioStreamWav wav))
			Cache[kind] = wav = ToWav(Synthesize(kind, new Random(Seed)));
		return wav;
	}

	/// <summary>The level's background bed, as a seamless loop.</summary>
	public static AudioStreamWav RoomTone()
	{
		if (_roomTone == null)
		{
			float[] samples = SynthesizeRoomTone(new Random(Seed));
			_roomTone = ToWav(samples);
			_roomTone.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
			_roomTone.LoopEnd = samples.Length;
		}
		return _roomTone;
	}

	public static float[] Synthesize(SoundKind kind, Random random) => kind switch
	{
		SoundKind.Shatter => Shatter(random),
		SoundKind.Impact => Impact(random),
		SoundKind.Hiss => Hiss(random),
		SoundKind.Growl => Growl(random),
		SoundKind.Swipe => Swipe(random),
		SoundKind.Splat => Splat(random),
		SoundKind.Creak => Creak(random),
		SoundKind.Drip => Drip(random),
		SoundKind.Rumble => Rumble(random),
		SoundKind.Rattle => Rattle(random),
		_ => Array.Empty<float>(),
	};

	public static AudioStreamWav ToWav(float[] samples)
	{
		var data = new byte[samples.Length * 2];
		for (int i = 0; i < samples.Length; i++)
		{
			short value = (short)(Mathf.Clamp(samples[i], -1f, 1f) * short.MaxValue);
			data[i * 2] = (byte)value;
			data[i * 2 + 1] = (byte)(value >> 8);
		}
		return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = MixRate, Data = data };
	}

	// ── Gameplay recipes ────────────────────────────────────────────────────────

	// Glass: a sharp crack, a burst of glassy rings, then shards tinkling down.
	private static float[] Shatter(Random r)
	{
		float[] crack = Samples(0.9f), rings = Samples(0.9f), shards = Samples(0.9f);
		var ring = new Resonator[7];
		var ringDecay = new float[ring.Length];
		for (int k = 0; k < ring.Length; k++)
		{
			ring[k] = new Resonator(Range(r, 2500f, 7500f), Range(r, 15f, 60f));
			ringDecay[k] = Range(r, 8f, 22f);
		}
		var shardTimes = new float[16];
		var shard = new Resonator[shardTimes.Length];
		for (int k = 0; k < shard.Length; k++)
		{
			shardTimes[k] = Range(r, 0.03f, 0.75f);
			shard[k] = new Resonator(Range(r, 3000f, 9000f), 8f);
		}

		for (int i = 0; i < crack.Length; i++)
		{
			float t = i / (float)MixRate;
			float n = Noise(r);
			crack[i] = n * Mathf.Exp(-t * 60f);
			for (int k = 0; k < ring.Length; k++)
				rings[i] += ring[k].Process(n * Mathf.Exp(-t * ringDecay[k]));
			for (int k = 0; k < shard.Length; k++)
			{
				float since = t - shardTimes[k];
				if (since > 0f)
					shards[i] += shard[k].Process(since < 0.004f ? n : 0f); // a tap, then it rings on its own
			}
		}
		return Layered(0.8f, (crack, 0.5f), (rings, 1f), (shards, 0.6f));
	}

	// A crate or case hitting the floor: a low knock that drops in pitch, plus a dull thud.
	private static float[] Impact(Random r)
	{
		float[] knock = Samples(0.35f), thud = Samples(0.35f);
		float pitch = Range(r, 85f, 130f);
		float phase = 0f;
		float low = 0f;
		for (int i = 0; i < knock.Length; i++)
		{
			float t = i / (float)MixRate;
			phase += Mathf.Tau * pitch * (1f + Mathf.Exp(-t * 30f)) / MixRate;
			low += 0.12f * (Noise(r) - low);
			knock[i] = Mathf.Sin(phase) * Mathf.Exp(-t * 16f);
			thud[i] = low * Mathf.Exp(-t * 30f);
		}
		return Layered(0.8f, (knock, 1f), (thud, 0.6f));
	}

	// Pressurised air: bright noise with a quick attack and a long tail, and a faint whistle.
	private static float[] Hiss(Random r)
	{
		float[] s = Samples(1f);
		float whistle = Range(r, 2800f, 3600f);
		float low = 0f;
		for (int i = 0; i < s.Length; i++)
		{
			float t = i / (float)MixRate;
			float n = Noise(r);
			low += 0.25f * (n - low);
			float env = Mathf.Min(1f, t * 60f) * (1f - t) * (1f - t);
			s[i] = (n - low + Mathf.Sin(Mathf.Tau * whistle * t) * 0.08f) * env;
		}
		return Normalize(s, 0.6f);
	}

	// The evil guy: a wet, throaty snarl. A rough buzzing voice pushed through throat-like
	// resonances, breath mixed in, with a pitch that never quite holds still.
	private static float[] Growl(Random r)
	{
		float[] s = Samples(0.9f);
		float f0 = Range(r, 52f, 70f);
		float phase = 0f;
		float drift = 0f;
		BandPass throat = new(), mouth = new(), chest = new();
		for (int i = 0; i < s.Length; i++)
		{
			float t = i / (float)MixRate;
			drift += 0.0015f * (Noise(r) - drift);
			phase += Mathf.Tau * f0 * (1f + 0.08f * Mathf.Sin(Mathf.Tau * 4.5f * t) + drift * 3f) / MixRate;
			float buzz = 0f;
			for (int k = 1; k <= 12; k++)
				buzz += Mathf.Sin(phase * k) / k;
			float source = buzz * 0.7f + Noise(r) * 0.5f;
			float voiced = chest.Process(source, 260f, 2f) * 0.8f + throat.Process(source, 650f, 3f) + mouth.Process(source, 1100f, 4f) * 0.7f;
			float env = Mathf.Pow(Mathf.Sin(Mathf.Pi * t / 0.9f), 0.8f) * (0.75f + 0.25f * Mathf.Sin(Mathf.Tau * 11f * t));
			s[i] = MathF.Tanh(voiced * 3f) * env;
		}
		return Normalize(s, 0.8f);
	}

	// A heavy arm cutting the air: noise swept up through a band-pass.
	private static float[] Swipe(Random r)
	{
		float[] s = Samples(0.3f);
		var band = new BandPass();
		for (int i = 0; i < s.Length; i++)
		{
			float t = i / (float)MixRate;
			s[i] = band.Process(Noise(r), Mathf.Lerp(400f, 2200f, t / 0.3f), 2f) * Mathf.Sin(Mathf.Pi * t / 0.3f);
		}
		return Normalize(s, 0.7f);
	}

	// Something big collapsing into a wet heap: a low slump and a few bubbling glugs.
	private static float[] Splat(Random r)
	{
		float[] slump = Samples(0.7f), glugs = Samples(0.7f);
		var bubbles = new (float Time, float Freq)[5];
		for (int i = 0; i < bubbles.Length; i++)
			bubbles[i] = (Range(r, 0.05f, 0.5f), Range(r, 180f, 420f));

		float low = 0f;
		for (int i = 0; i < slump.Length; i++)
		{
			float t = i / (float)MixRate;
			low += 0.05f * (Noise(r) - low);
			slump[i] = low * Mathf.Exp(-t * 6f);
			foreach (var bubble in bubbles)
			{
				float since = t - bubble.Time;
				if (since > 0f && since < 0.12f)
					glugs[i] += Mathf.Sin(Mathf.Tau * bubble.Freq * (1f + since * 6f) * since) * Mathf.Sin(Mathf.Pi * since / 0.12f);
			}
		}
		return Layered(0.8f, (slump, 1f), (glugs, 0.5f));
	}

	// ── Ambience recipes ────────────────────────────────────────────────────────

	// Metal under strain somewhere in the building: a slow, sliding groan that sticks and slips.
	private static float[] Creak(Random r)
	{
		float[] s = Samples(1.6f);
		float from = Range(r, 150f, 220f);
		float to = from * Range(r, 1.2f, 1.6f);
		BandPass body = new(), overtone = new();
		for (int i = 0; i < s.Length; i++)
		{
			float t = i / (float)MixRate;
			float pitch = Mathf.Lerp(from, to, t / 1.6f) * (1f + 0.03f * Mathf.Sin(Mathf.Tau * 3.3f * t));
			float n = Noise(r);
			float stickSlip = 0.6f + 0.4f * Mathf.Sin(Mathf.Tau * (9f + 4f * Mathf.Sin(Mathf.Tau * 0.7f * t)) * t);
			float env = Mathf.Pow(Mathf.Sin(Mathf.Pi * t / 1.6f), 0.7f);
			s[i] = (body.Process(n, pitch, 12f) + overtone.Process(n, pitch * 2.3f, 10f) * 0.5f) * env * stickSlip;
		}
		return Normalize(s, 0.7f);
	}

	// A single drop landing in a puddle: a quick "plink" whose pitch rises, like a real water drop.
	private static float[] Drip(Random r)
	{
		float[] s = Samples(0.4f);
		float pitch = Range(r, 900f, 1600f);
		float phase = 0f;
		for (int i = 0; i < s.Length; i++)
		{
			float t = i / (float)MixRate;
			phase += Mathf.Tau * pitch * (1f + 0.6f * (1f - Mathf.Exp(-t * 30f))) / MixRate;
			float tick = t < 0.002f ? Noise(r) * 0.3f : 0f;
			s[i] = Mathf.Sin(phase) * Mathf.Exp(-t * 14f) + tick;
		}
		return Normalize(s, 0.6f);
	}

	// Something heavy, far away through the walls: a slow, deep boom with no edges.
	private static float[] Rumble(Random r)
	{
		float[] boom = Samples(1.5f), body = Samples(1.5f);
		float deep = 0f;
		for (int i = 0; i < boom.Length; i++)
		{
			float t = i / (float)MixRate;
			deep += 0.01f * (Noise(r) - deep);
			float env = Mathf.Min(1f, t / 0.05f) * Mathf.Exp(-t * 2.5f);
			boom[i] = deep * env;
			body[i] = Mathf.Sin(Mathf.Tau * 42f * t) * env;
		}
		return Layered(0.8f, (boom, 1f), (body, 0.5f));
	}

	// A loose vent panel buzzing in a draught: a cluster of tiny metallic taps.
	private static float[] Rattle(Random r)
	{
		float[] s = Samples(0.8f);
		var taps = new (float Time, Resonator Ring)[Mathf.RoundToInt(Range(r, 12f, 25f))];
		for (int k = 0; k < taps.Length; k++)
			taps[k] = (Range(r, 0f, 0.6f) * Range(r, 0.4f, 1f), new Resonator(Range(r, 1800f, 3200f), 120f));

		for (int i = 0; i < s.Length; i++)
		{
			float t = i / (float)MixRate;
			float n = Noise(r);
			for (int k = 0; k < taps.Length; k++)
			{
				float since = t - taps[k].Time;
				if (since > 0f && since < 0.05f)
					s[i] += taps[k].Ring.Process(since < 0.002f ? n : 0f);
			}
		}
		return Normalize(s, 0.6f);
	}

	// The level's bed: a slow low rumble, a faint ventilation hum and duct air. Everything cycles a
	// whole number of times per loop, and the tail is crossfaded over the head, so the loop is seamless.
	public static float[] SynthesizeRoomTone(Random r)
	{
		int length = (int)(RoomToneSeconds * MixRate);
		int fade = MixRate;
		float[] rumble = new float[length + fade], hum = new float[length + fade], air = new float[length + fade];
		float brown = 0f, low = 0f;
		var duct = new BandPass();
		for (int i = 0; i < rumble.Length; i++)
		{
			float t = i / (float)MixRate;
			float n = Noise(r);
			brown = brown * 0.998f + n * 0.02f;
			low += 0.01f * (brown - low);
			rumble[i] = low * (0.8f + 0.2f * Mathf.Sin(Mathf.Tau * 0.05f * t));
			hum[i] = (Mathf.Sin(Mathf.Tau * 55f * t) + 0.5f * Mathf.Sin(Mathf.Tau * 110f * t) + 0.25f * Mathf.Sin(Mathf.Tau * 165f * t))
				* (0.8f + 0.2f * Mathf.Sin(Mathf.Tau * 0.1f * t));
			air[i] = duct.Process(n, 900f, 0.6f) * (0.6f + 0.4f * Mathf.Sin(Mathf.Tau * 0.05f * t));
		}

		float[] mix = Layered(0.5f, (rumble, 1f), (hum, 0.15f), (air, 0.35f));
		var loop = new float[length];
		Array.Copy(mix, loop, length);
		for (int i = 0; i < fade; i++)
		{
			float a = i / (float)fade;
			loop[i] = mix[i] * a + mix[length + i] * (1f - a);
		}
		return loop;
	}

	// ── Helpers ─────────────────────────────────────────────────────────────────

	private static float[] Samples(float seconds) => new float[(int)(seconds * MixRate)];

	private static float Noise(Random r) => (float)r.NextDouble() * 2f - 1f;

	private static float Range(Random r, float from, float to) => from + (float)r.NextDouble() * (to - from);

	private static float[] Normalize(float[] samples, float peak)
	{
		float max = 0f;
		foreach (float v in samples)
			max = Mathf.Max(max, Mathf.Abs(v));
		if (max > 0f)
			for (int i = 0; i < samples.Length; i++)
				samples[i] *= peak / max;
		return samples;
	}

	// Mixes layers after normalising each to peak 1, so recipes can weight their parts by ear
	// without guessing filter gains.
	private static float[] Layered(float peak, params (float[] Samples, float Weight)[] layers)
	{
		var mix = new float[layers[0].Samples.Length];
		foreach (var (samples, weight) in layers)
		{
			Normalize(samples, 1f);
			for (int i = 0; i < mix.Length; i++)
				mix[i] += samples[i] * weight;
		}
		return Normalize(mix, peak);
	}
}

/// <summary>State-variable band-pass filter (Chamberlin), normalised to unity gain at the centre.</summary>
public struct BandPass
{
	private float _low;
	private float _band;

	public float Process(float input, float centreHz, float q)
	{
		float f = 2f * Mathf.Sin(Mathf.Pi * Mathf.Min(centreHz, SoundBank.MixRate / 6f) / SoundBank.MixRate);
		float damping = 1f / q;
		_low += f * _band;
		float high = input - _low - damping * _band;
		_band += f * high;
		return _band * damping;
	}
}

/// <summary>Two-pole resonator: rings at one frequency, and stays stable right up to Nyquist (unlike BandPass).</summary>
public struct Resonator
{
	private readonly float _a1;
	private readonly float _a2;
	private readonly float _gain;
	private float _y1;
	private float _y2;

	public Resonator(float hz, float bandwidthHz)
	{
		float radius = Mathf.Exp(-Mathf.Pi * bandwidthHz / SoundBank.MixRate);
		_a1 = 2f * radius * Mathf.Cos(Mathf.Tau * hz / SoundBank.MixRate);
		_a2 = -radius * radius;
		_gain = 1f - radius;
		_y1 = 0f;
		_y2 = 0f;
	}

	public float Process(float input)
	{
		float y = _gain * input + _a1 * _y1 + _a2 * _y2;
		_y2 = _y1;
		_y1 = y;
		return y;
	}
}
