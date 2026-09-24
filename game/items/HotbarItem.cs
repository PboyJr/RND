using Godot;

namespace RND.Items;

/// <summary>
/// Something that goes in a hotbar slot. Pure data (a .tres file): per-player state such as
/// cooldowns lives on the player, since one item resource is shared by everyone carrying it.
/// </summary>
[GlobalClass]
public partial class HotbarItem : Resource
{
	[Export] public string DisplayName { get; set; } = "Item";
	[Export] public string ShortName { get; set; } = "ITEM"; // hotbar label
	[Export] public Color Tint { get; set; } = Colors.White;
	[Export(PropertyHint.Range, "0,120,0.5,suffix:s")] public float Cooldown { get; set; }
}
