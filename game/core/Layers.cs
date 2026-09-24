namespace RND.Core;

/// <summary>Physics layer bits. Names match Project Settings → Layer Names → 3D Physics.</summary>
public static class Layers
{
	public const uint World = 1 << 0;
	public const uint Players = 1 << 1;
	public const uint Props = 1 << 2;
	public const uint Entities = 1 << 3;
}
