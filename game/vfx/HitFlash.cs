using Godot;

namespace RND.Vfx;

public static class HitFlash
{
	/// <summary>Flashes a material's emission and fades it out. Returns the tween so the next flash can cancel it.</summary>
	public static Tween Play(Node owner, StandardMaterial3D material, Color color, Tween previous)
	{
		previous?.Kill();
		material.EmissionEnabled = true;
		material.Emission = color;
		material.EmissionEnergyMultiplier = 2.5f;

		Tween tween = owner.CreateTween();
		tween.TweenProperty(material, "emission_energy_multiplier", 0f, 0.3f);
		return tween;
	}
}
