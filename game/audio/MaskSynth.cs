using System;
using Godot;

namespace RND.Audio;

/// <summary>What the in-mask sound needs to know, sampled once per audio buffer.</summary>
public struct MaskState
{
	public bool Alive;          // breathing at all
	public float BreathPhase;   // 0..1, first half exhale, second half inhale (shared with the visor fog)
	public float BreathRate;    // breaths per second right now
	public float Exertion;      // 0 calm .. 1 flat out
	public float Choke;         // 0 .. 1, spent filter
	public float FilterWear;    // 0 fresh .. 1 spent
	public float Danger;        // 0 healthy .. 1 nearly dead (drives the heartbeat)
	public float Mind;          // 0 .. 1, mental damage (drives a tinnitus ring)
}

/// <summary>
/// Synthesises what you hear inside the mask, sample by sample: breathing through the valves (in step
/// with the visor's breath fog), a heartbeat when you're badly hurt, and the hiss of a fresh filter
/// going on. Plain C# so it can also be rendered offline (AudioPreview).
/// </summary>
public sealed class MaskSynth
{
	// Your own breathing should sit under the world, not on top of it.
	private const float BreathGain = 0.07f; // barely there: you notice it when the room goes quiet and you listen

	private readonly Random _random = new(1);
	private readonly float[] _cavity = new float[70]; // ~3 ms: the boxy ring of air trapped in a mask
	private int _cavityIndex;
	private BandPass _exhaleLow;
	private BandPass _exhaleMid;
	private BandPass _inhaleMid;
	private BandPass _inhaleWhistle;
	private BandPass _hiss;
	private float _soft;
	private float _lastPhase;
	private float _valveTime = 1f;
	private float _heartPhase;
	private float _hissTime = -1f;
	private float _time;
	private float _catch = 1f;   // starved breathing: the current catch in the throat (0.2 choked off .. 1 flowing)
	private int _catchLeft;

	public void PlayFreshFilter() => _hissTime = 0f;

	public void Render(Vector2[] output, in MaskState state)
	{
		float bpm = Mathf.Lerp(72f, 150f, state.Danger) + state.Exertion * 20f;
		float loudness = Mathf.Lerp(0.5f, 1.3f, state.Exertion) + state.Choke * 2f; // fewer breaths out of gas, but you hear every strained one
		float wheeze = Mathf.Max(state.FilterWear * state.FilterWear, state.Choke);

		for (int i = 0; i < output.Length; i++)
		{
			float noise = (float)_random.NextDouble() * 2f - 1f;
			_soft += 0.35f * (noise - _soft); // noise with the harsh top rolled off: air, not static
			float sample = FreshFilter(noise);

			if (state.Alive)
			{
				float phase = (state.BreathPhase + i * state.BreathRate / SoundBank.MixRate) % 1f;
				if (phase < _lastPhase - 0.5f || (_lastPhase < 0.5f && phase >= 0.5f))
					_valveTime = 0f; // a valve flaps at each turn of breath
				_lastPhase = phase;

				float wave = Mathf.Sin(phase * Mathf.Tau);
				float exhale = wave > 0f ? wave * wave : 0f;
				float inhale = wave < 0f ? wave * wave : 0f;

				// Out through the exhale valve: a warm, low "hoo". In through the filter: a breathier draw,
				// with a whistle that grows as the filter clogs.
				// Out of gas (Choke): like an empty scuba tank. Each pull is strained against a valve that gives
				// nothing (the thin whistle takes over), each exhale is short and weak, and the air comes in
				// catches and stutters instead of a steady flow.
				float breath = (_exhaleLow.Process(_soft, 380f, 0.9f) + _exhaleMid.Process(_soft, 950f, 1.6f) * 0.5f) * exhale * (1f - 0.7f * state.Choke)
					+ (_inhaleMid.Process(_soft, 1150f, 1.2f) * 0.6f * (1f - 0.5f * state.Choke)
						+ _inhaleWhistle.Process(noise, Mathf.Lerp(2000f, 2600f, wheeze), Mathf.Lerp(2f, 9f, wheeze)) * (0.1f + 0.5f * wheeze)) * inhale;
				if (state.Choke > 0f)
				{
					if (--_catchLeft <= 0)
					{
						_catch = 0.2f + 0.8f * (float)_random.NextDouble();
						_catchLeft = 1200 + _random.Next(3600); // 25-100 ms per catch
					}
					breath *= Mathf.Lerp(1f, _catch, state.Choke);
				}
				breath = breath * loudness + Valve();

				// Resonate in the small space between your face and the glass.
				breath += _cavity[_cavityIndex] * 0.35f;
				_cavity[_cavityIndex] = breath;
				_cavityIndex = (_cavityIndex + 1) % _cavity.Length;
				sample += breath * BreathGain;

				if (state.Danger > 0f)
					sample += Heartbeat(bpm) * state.Danger * 0.6f;

				// Mental damage rings in your ears: a thin high tone that slowly swells and fades.
				if (state.Mind > 0f)
					sample += Mathf.Sin(_time * Mathf.Tau * 6800f) * (0.75f + 0.25f * Mathf.Sin(_time * 0.9f)) * state.Mind * 0.03f;
			}

			sample = MathF.Tanh(sample); // soft limiter: overlapping sounds squash instead of clipping
			output[i] = new Vector2(sample, sample);
			_time += 1f / SoundBank.MixRate;
		}
	}

	// A soft rubber flap: barely there, but it's what makes it a respirator.
	private float Valve()
	{
		if (_valveTime > 0.03f)
			return 0f;

		float t = _valveTime;
		_valveTime += 1f / SoundBank.MixRate;
		return Mathf.Sin(Mathf.Tau * 170f * t) * Mathf.Exp(-t * 150f) * 0.3f;
	}

	// Lub-dub: two low thumps per beat.
	private float Heartbeat(float bpm)
	{
		_heartPhase = (_heartPhase + bpm / 60f / SoundBank.MixRate) % 1f;
		float since = _heartPhase * 60f / bpm;
		return Thump(since) + Thump(since - 0.16f) * 0.6f;
	}

	private static float Thump(float t) => t < 0f
		? 0f
		: Mathf.Sin(Mathf.Tau * 55f * t) * Mathf.Exp(-t * 20f) * 0.8f + Mathf.Sin(Mathf.Tau * 110f * t) * Mathf.Exp(-t * 35f) * 0.25f;

	// The thread seating (a click), then air rushing through the new filter.
	private float FreshFilter(float noise)
	{
		if (_hissTime < 0f)
			return 0f;

		float t = _hissTime;
		_hissTime = t > 1.2f ? -1f : t + 1f / SoundBank.MixRate;
		float click = t < 0.02f ? Mathf.Sin(Mathf.Tau * 900f * t) * (1f - t / 0.02f) * 0.25f : 0f;
		return click + _hiss.Process(noise, 3000f, 0.7f) * Mathf.Min(1f, t * 40f) * Mathf.Exp(-t * 2.5f) * 0.35f;
	}
}
