using Godot;

namespace RND.Vfx;

/// <summary>
/// Liquid inside a vessel. Put it on the vessel's "Liquid" mesh (a closed mesh filling the whole
/// inside; see docs/ART.md), with vfx/liquid.gdshader as its material. The surface stays level in the
/// world, sloshes when the vessel speeds up, stops, is thrown or shaken, then settles. Fill is how far
/// up the vessel it reaches. It's purely visual and worked out locally from how the vessel moves, so
/// there's no networking: clients see props move, so their liquid sloshes too.
/// </summary>
public partial class Liquid : MeshInstance3D
{
	[Export(PropertyHint.Range, "0,1,0.01")] public float Fill { get; set; } = 0.6f;
	[Export] public Color Color { get; set; } = new(0.3f, 1f, 0.25f);
	[Export] public float Glow { get; set; } = 0.8f;

	[ExportGroup("Slosh")]
	// How hard the surface springs back to level (higher = quicker, tighter sloshing)...
	[Export] public float Stiffness { get; set; } = 60f;
	// ...how quickly the sloshing dies down...
	[Export] public float Damping { get; set; } = 4f;
	// ...and how much a change in speed throws it around.
	[Export] public float Push { get; set; } = 1f;
	[Export] public float MaxTilt { get; set; } = 0.7f;

	private ShaderMaterial _material;
	private Aabb _bounds;
	private Vector3 _lastPosition;
	private Vector3 _lastVelocity;
	private Vector2 _tilt;
	private Vector2 _tiltSpeed;

	/// <summary>How far off level the surface is right now (0 = still).</summary>
	public float Slosh() => _tilt.Length();

	public override void _Ready()
	{
		_material = (ShaderMaterial)GetActiveMaterial(0).Duplicate(); // each vessel sloshes on its own
		MaterialOverride = _material;
		_bounds = Mesh.GetAabb();
		_lastPosition = GlobalPosition;
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		Visible = Fill > 0f; // empty: no sliver of liquid sloshing around the bottom
		if (dt <= 0f)
			return;

		Vector3 velocity = (GlobalPosition - _lastPosition) / dt;
		Vector3 kick = velocity - _lastVelocity;
		_lastPosition = GlobalPosition;
		_lastVelocity = velocity;

		// A change in speed shoves the surface the other way (liquid piles up at the back); then it
		// springs back past level, and settles.
		if (kick.Length() < 20f) // bigger jumps are teleports, not motion
			_tiltSpeed -= new Vector2(kick.X, kick.Z) * Push;
		_tiltSpeed += (-_tilt * Stiffness - _tiltSpeed * Damping) * dt;
		_tilt = (_tilt + _tiltSpeed * dt).LimitLength(MaxTilt);

		// Surface height: Fill of the way up the vessel as it sits in the world right now, so tipping
		// it over sends the liquid to its low side.
		float low = float.MaxValue, high = float.MinValue;
		for (int i = 0; i < 8; i++)
		{
			float y = ToGlobal(_bounds.GetEndpoint(i)).Y;
			low = Mathf.Min(low, y);
			high = Mathf.Max(high, y);
		}
		Vector3 centre = ToGlobal(_bounds.GetCenter());
		float surface = Mathf.Lerp(low, high, Fill);
		// A shallow puddle can't slosh higher up the side than it is deep (liquid from nowhere).
		float radius = Mathf.Max(_bounds.Size.X, _bounds.Size.Z) * 0.5f;

		_material.SetShaderParameter("surface_center", new Vector3(centre.X, surface, centre.Z));
		_material.SetShaderParameter("tilt", _tilt.LimitLength((surface - low) / radius));
		_material.SetShaderParameter("liquid_color", Color);
		_material.SetShaderParameter("glow", Glow);
	}
}
