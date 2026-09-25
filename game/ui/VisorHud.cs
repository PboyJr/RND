using Godot;
using RND.Combat;
using RND.Maze;
using RND.Players;

namespace RND.UI;

/// <summary>
/// The HUD projected onto the gas mask's glass. It renders into a SubViewport (Main/HudViewport) that
/// the visor shader draws through the curved, fogged, cracked glass. There's deliberately no health
/// readout: the cracks are the health bar. Mental damage scrambles the text (and the shader tears it).
/// </summary>
public partial class VisorHud : Control
{
	private Label _hint;
	private Label _filter;
	private HotbarView _hotbar;
	private Label _test;

	public override void _Ready()
	{
		_hint = GetNode<Label>("Hint");
		_filter = GetNode<Label>("Filter");
		_hotbar = GetNode<HotbarView>("Hotbar");
		_test = GetNode<Label>("Test");
	}

	public override void _Process(double delta)
	{
		Player local = Player.Local;
		Visible = local is { IsDead: false };
		_hotbar.Refresh(Visible ? local : null);
		if (!Visible)
			return;

		float mind = local.Health.Mental / local.Health.MaxHealth;
		mind *= mind; // eased in: a little mental damage barely shows (same curve as the visor shader)
		_hint.Text = Scramble(local.Hint, mind);
		_filter.Text = Scramble(FilterReadout(local.Respirator), mind);

		// The gauge fades as it runs out (below 20%) instead of blinking: nothing on the visor flashes.
		// The spent warning is back at full strength, since it's telling you what to do.
		float fade = local.Respirator.IsSpent ? 1f : Mathf.Lerp(0.35f, 1f, Mathf.Clamp(local.Respirator.Fraction / 0.2f, 0f, 1f));
		_filter.Modulate = new Color(1f, 1f, 1f, fade);

		_test.Text = Scramble(TestReadout(ChamberExit.Current), mind);
	}

	private const string Garbage = "#%&@$?!/\\<>=+*";

	// Swaps some characters for garbage, more the worse your mind is. Re-rolls ten times a second, so
	// it jitters instead of shimmering every frame.
	private static string Scramble(string text, float mind)
	{
		if (mind <= 0f || text.Length == 0)
			return text;

		var rng = new System.Random((int)(Time.GetTicksMsec() / 100) + text.Length);
		char[] chars = text.ToCharArray();
		for (int i = 0; i < chars.Length; i++)
			if (!char.IsWhiteSpace(chars[i]) && rng.NextDouble() < mind * 0.4)
				chars[i] = Garbage[rng.Next(Garbage.Length)];
		return new string(chars);
	}

	private static string TestReadout(ChamberExit exit)
	{
		if (exit == null)
			return "";

		return $"{(exit.IsComplete ? "TEST COMPLETE" : "TEST IN PROGRESS")}\n{Clock(exit.Elapsed)}";
	}

	private static string Clock(float seconds) => $"{(int)seconds / 60:00}:{seconds % 60f:00.0}";

	private static string FilterReadout(Respirator respirator)
	{
		if (respirator.IsSpent)
			return "FILTER SPENT - FIND A SPARE";

		int bars = Mathf.CeilToInt(respirator.Fraction * 10f);
		return $"FILTER [{new string('#', bars)}{new string('-', 10 - bars)}] {Mathf.CeilToInt(respirator.Fraction * 100f)}%";
	}
}
