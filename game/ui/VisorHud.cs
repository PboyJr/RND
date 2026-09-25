using Godot;
using RND.Combat;
using RND.Players;

namespace RND.UI;

/// <summary>
/// The HUD projected onto the gas mask's glass. It renders into a SubViewport (Main/HudViewport) that
/// the visor shader draws through the curved, fogged, cracked glass. There's deliberately no health
/// readout: the cracks are the health bar.
/// </summary>
public partial class VisorHud : Control
{
	private Label _hint;
	private Label _filter;
	private HotbarView _hotbar;

	public override void _Ready()
	{
		_hint = GetNode<Label>("Hint");
		_filter = GetNode<Label>("Filter");
		_hotbar = GetNode<HotbarView>("Hotbar");
	}

	public override void _Process(double delta)
	{
		Player local = Player.Local;
		Visible = local is { IsDead: false };
		_hotbar.Refresh(Visible ? local : null);
		if (!Visible)
			return;

		_hint.Text = local.Hint;
		_filter.Text = FilterReadout(local.Respirator);

		// Blink when it's nearly gone.
		bool blinkOff = local.Respirator.Fraction < 0.2f && Time.GetTicksMsec() / 400 % 2 == 0;
		_filter.Modulate = blinkOff ? new Color(1f, 1f, 1f, 0.25f) : Colors.White;
	}

	private static string FilterReadout(Respirator respirator)
	{
		if (respirator.IsSpent)
			return "FILTER SPENT - FIND A SPARE";

		int bars = Mathf.CeilToInt(respirator.Fraction * 10f);
		return $"FILTER [{new string('#', bars)}{new string('-', 10 - bars)}] {Mathf.CeilToInt(respirator.Fraction * 100f)}%";
	}
}
