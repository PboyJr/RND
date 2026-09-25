using System;
using System.Collections.Generic;
using Godot;

namespace RND.Vfx;

/// <summary>
/// The cel-shading switch (F2 toggles it, to compare). While it's on:
/// - every ordinary opaque material in the game is swapped for the toon shader (vfx/toon.gdshader),
///   and each toon copy is kept in sync with its original every frame (albedo, emission), so hit
///   flashes and the evil guy's mood-coloured eyes keep working;
/// - a screen-space outline quad (vfx/outline.gdshader) rides in front of the active camera.
/// Artists keep authoring ordinary materials, and the style is applied on top. Transparent and
/// unshaded materials (glass, effects) are left alone.
/// </summary>
public partial class ToonStyle : Node
{
	[Export] public Shader ToonShader { get; set; }
	// Line width, colour and sensitivity: this material's shader parameters (Main/ToonStyle in the Inspector).
	[Export] public ShaderMaterial OutlineMaterial { get; set; }
	[Export] public Texture2D Ramp { get; set; }

	[Export]
	public bool Enabled
	{
		get => _enabled;
		set
		{
			if (_enabled == value)
				return;
			_enabled = value;
			if (IsInsideTree())
				Refresh();
		}
	}

	private bool _enabled = true;
	// Original material -> its toon copy. (Prototype: copies of freed materials linger until the
	// game closes. It's a handful per enemy respawn.)
	private readonly Dictionary<StandardMaterial3D, ShaderMaterial> _toonFor = new();
	private readonly List<Action> _restore = new();
	private MeshInstance3D _outline;

	public override void _Ready()
	{
		GetTree().NodeAdded += OnNodeAdded;
		Refresh();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F2 })
			Enabled = !Enabled;
	}

	public override void _Process(double delta)
	{
		if (!_enabled)
			return;

		foreach ((StandardMaterial3D source, ShaderMaterial toon) in _toonFor)
			Sync(source, toon);
		KeepOutlineOnCamera();
	}

	private void Refresh()
	{
		foreach (Action restore in _restore)
			restore();
		_restore.Clear();

		if (_enabled)
		{
			Apply(GetTree().Root, recursive: true);
		}
		else if (_outline != null && IsInstanceValid(_outline))
		{
			_outline.QueueFree();
			_outline = null;
		}
	}

	// Deferred, so it runs after the new node's _Ready has set up its materials.
	private void OnNodeAdded(Node node)
	{
		if (_enabled)
			Callable.From(() =>
			{
				if (_enabled && IsInstanceValid(node))
					Apply(node, recursive: false);
			}).CallDeferred();
	}

	private void Apply(Node node, bool recursive)
	{
		switch (node)
		{
			case CsgPrimitive3D csg: // each CSG shape type declares its own "material" property
				var material = csg.Get("material").As<Material>();
				Swap(csg, material, m => csg.Set("material", m), material);
				break;

			case MeshInstance3D mesh when mesh.Mesh != null:
				if (mesh.MaterialOverride != null)
				{
					Swap(mesh, mesh.MaterialOverride, m => mesh.MaterialOverride = m, mesh.MaterialOverride);
					break;
				}
				for (int i = 0; i < mesh.GetSurfaceOverrideMaterialCount(); i++)
				{
					int surface = i;
					Material own = mesh.GetSurfaceOverrideMaterial(surface);
					Swap(mesh, own ?? mesh.Mesh.SurfaceGetMaterial(surface), m => mesh.SetSurfaceOverrideMaterial(surface, m), own);
				}
				break;
		}

		if (recursive)
			foreach (Node child in node.GetChildren())
				Apply(child, recursive: true);
	}

	private void Swap(GodotObject owner, Material source, Action<Material> set, Material restoreTo)
	{
		if (source is not StandardMaterial3D standard
			|| standard.Transparency != BaseMaterial3D.TransparencyEnum.Disabled
			|| standard.ShadingMode == BaseMaterial3D.ShadingModeEnum.Unshaded)
			return;

		if (!_toonFor.TryGetValue(standard, out ShaderMaterial toon))
		{
			toon = new ShaderMaterial { Shader = ToonShader };
			toon.SetShaderParameter("ramp", Ramp);
			toon.SetShaderParameter("albedo_texture", standard.AlbedoTexture);
			Sync(standard, toon);
			_toonFor[standard] = toon;
		}

		set(toon);
		_restore.Add(() =>
		{
			if (IsInstanceValid(owner))
				set(restoreTo);
		});
	}

	private static void Sync(StandardMaterial3D source, ShaderMaterial toon)
	{
		toon.SetShaderParameter("albedo", source.AlbedoColor);
		toon.SetShaderParameter("emission", source.EmissionEnabled ? source.Emission * source.EmissionEnergyMultiplier : Colors.Black);
	}

	private void KeepOutlineOnCamera()
	{
		Camera3D camera = GetViewport().GetCamera3D();
		if (camera == null)
			return;

		if (_outline == null || !IsInstanceValid(_outline))
		{
			_outline = new MeshInstance3D
			{
				Mesh = new QuadMesh { Size = new Vector2(2f, 2f) },
				MaterialOverride = OutlineMaterial,
				ExtraCullMargin = 16384f, // it's drawn in screen space; never cull it
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			};
		}

		if (_outline.GetParent() != camera)
		{
			_outline.GetParent()?.RemoveChild(_outline);
			camera.AddChild(_outline);
		}
	}
}
