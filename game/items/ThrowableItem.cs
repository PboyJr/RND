using Godot;

namespace RND.Items;

/// <summary>An item you throw: the host spawns Projectile on an arc from the thrower's eyes.</summary>
[GlobalClass]
public partial class ThrowableItem : HotbarItem
{
	[Export] public PackedScene Projectile { get; set; }
	[Export(PropertyHint.Range, "1,40,0.5,suffix:m/s")] public float ThrowSpeed { get; set; } = 14f;
	// Extra upward speed so throws arc a little instead of flying laser-straight.
	[Export(PropertyHint.Range, "0,10,0.1,suffix:m/s")] public float ThrowLift { get; set; } = 2.5f;
}
