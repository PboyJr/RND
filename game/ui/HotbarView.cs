using System.Collections.Generic;
using Godot;
using RND.Items;
using RND.Players;

namespace RND.UI;

/// <summary>Draws the local player's hotbar: one box per slot, a highlight on the selected one, and a recharge overlay.</summary>
public partial class HotbarView : HBoxContainer
{
	private const float SlotSize = 76f;
	private static readonly Color IdleBorder = new(1f, 1f, 1f, 0.2f);
	private static readonly Color SelectedBorder = new(1f, 1f, 1f, 0.9f);

	private sealed class Slot
	{
		public StyleBoxFlat Style;
		public Control CooldownFill;
		public Label Timer;
	}

	private readonly List<Slot> _slots = new();
	private Player _player;

	public void Refresh(Player player)
	{
		if (player != _player)
			Rebuild(player);
		if (_player == null)
			return;

		for (int i = 0; i < _slots.Count; i++)
		{
			Slot slot = _slots[i];
			slot.Style.BorderColor = i == _player.SelectedSlot ? SelectedBorder : IdleBorder;

			HotbarItem item = _player.GetItem(i);
			float remaining = _player.GetCooldownRemaining(i);
			float fraction = item != null && item.Cooldown > 0f ? remaining / item.Cooldown : 0f;
			slot.CooldownFill.SetAnchor(Side.Top, 1f - fraction);
			slot.Timer.Text = remaining > 0f ? Mathf.CeilToInt(remaining).ToString() : "";
		}
	}

	private void Rebuild(Player player)
	{
		foreach (Node child in GetChildren())
			child.QueueFree();
		_slots.Clear();

		_player = player;
		if (player == null)
			return;

		for (int i = 0; i < player.SlotCount; i++)
			_slots.Add(CreateSlot(i, player.GetItem(i)));
	}

	private Slot CreateSlot(int index, HotbarItem item)
	{
		var style = new StyleBoxFlat { BgColor = new Color(0.05f, 0.06f, 0.07f, 0.75f), BorderColor = IdleBorder };
		style.SetBorderWidthAll(2);
		style.SetCornerRadiusAll(6);

		var frame = new Panel { CustomMinimumSize = new Vector2(SlotSize, SlotSize), MouseFilter = MouseFilterEnum.Ignore };
		frame.AddThemeStyleboxOverride("panel", style);
		AddChild(frame);

		// Placeholder icon: a coloured block in the item's tint.
		if (item != null)
		{
			var icon = new ColorRect { Color = item.Tint, MouseFilter = MouseFilterEnum.Ignore };
			icon.SetAnchorsPreset(LayoutPreset.Center);
			icon.OffsetLeft = -9;
			icon.OffsetRight = 9;
			icon.OffsetTop = -22;
			icon.OffsetBottom = 6;
			frame.AddChild(icon);
		}

		// Darkens the slot from the bottom up while recharging.
		var cooldownFill = new ColorRect { Color = new Color(0f, 0f, 0f, 0.65f), MouseFilter = MouseFilterEnum.Ignore };
		cooldownFill.SetAnchorsPreset(LayoutPreset.FullRect);
		frame.AddChild(cooldownFill);

		var key = new Label { Text = (index + 1).ToString(), Position = new Vector2(6, 2), Modulate = new Color(1f, 1f, 1f, 0.6f) };
		key.AddThemeFontSizeOverride("font_size", 12);
		frame.AddChild(key);

		var name = new Label
		{
			Text = item?.ShortName ?? "HANDS",
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Bottom,
			Modulate = item?.Tint ?? Colors.White,
		};
		name.SetAnchorsPreset(LayoutPreset.FullRect);
		name.OffsetBottom = -4;
		name.AddThemeFontSizeOverride("font_size", 13);
		frame.AddChild(name);

		var timer = new Label { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
		timer.SetAnchorsPreset(LayoutPreset.FullRect);
		timer.AddThemeFontSizeOverride("font_size", 26);
		frame.AddChild(timer);

		return new Slot { Style = style, CooldownFill = cooldownFill, Timer = timer };
	}
}
