using Godot;

namespace RND.Core;

/// <summary>
/// Autoload holding the player's settings. Loaded from user://settings.cfg at startup and applied
/// at once (window, audio, rendering); the settings menu changes them live and saves on close.
/// Things that read them every frame (mouse sensitivity, field of view) read Settings.Instance.
/// </summary>
public partial class Settings : Node
{
	public const string DefaultPath = "user://settings.cfg";
	private const string Section = "settings";

	public static Settings Instance { get; private set; }

	/// <summary>Multiplies the base mouse sensitivity.</summary>
	public float MouseSensitivity { get; set; } = 1f;
	public float FieldOfView { get; set; } = 80f;
	// 0..1 on each audio bus. "Mask" is what you hear inside the mask: breathing, heartbeat.
	public float MasterVolume { get; set; } = 1f;
	public float WorldVolume { get; set; } = 1f;
	public float MaskVolume { get; set; } = 1f;
	public bool Fullscreen { get; set; }
	public bool VSync { get; set; } = true;
	/// <summary>3D resolution scale: below 1 renders the world at a lower resolution (faster on weak GPUs).</summary>
	public float RenderScale { get; set; } = 1f;

	public override void _EnterTree() => Instance = this;

	public override void _Ready()
	{
		Load(DefaultPath);
		Apply();
	}

	public void Load(string path)
	{
		var file = new ConfigFile();
		if (file.Load(path) != Error.Ok)
			return; // first run: keep the defaults

		MouseSensitivity = Mathf.Clamp((float)file.GetValue(Section, "mouse_sensitivity", MouseSensitivity), 0.1f, 5f);
		FieldOfView = Mathf.Clamp((float)file.GetValue(Section, "field_of_view", FieldOfView), 60f, 110f);
		MasterVolume = Mathf.Clamp((float)file.GetValue(Section, "master_volume", MasterVolume), 0f, 1f);
		WorldVolume = Mathf.Clamp((float)file.GetValue(Section, "world_volume", WorldVolume), 0f, 1f);
		MaskVolume = Mathf.Clamp((float)file.GetValue(Section, "mask_volume", MaskVolume), 0f, 1f);
		Fullscreen = (bool)file.GetValue(Section, "fullscreen", Fullscreen);
		VSync = (bool)file.GetValue(Section, "vsync", VSync);
		RenderScale = Mathf.Clamp((float)file.GetValue(Section, "render_scale", RenderScale), 0.5f, 1f);
	}

	public void Save(string path = DefaultPath)
	{
		var file = new ConfigFile();
		file.SetValue(Section, "mouse_sensitivity", MouseSensitivity);
		file.SetValue(Section, "field_of_view", FieldOfView);
		file.SetValue(Section, "master_volume", MasterVolume);
		file.SetValue(Section, "world_volume", WorldVolume);
		file.SetValue(Section, "mask_volume", MaskVolume);
		file.SetValue(Section, "fullscreen", Fullscreen);
		file.SetValue(Section, "vsync", VSync);
		file.SetValue(Section, "render_scale", RenderScale);
		file.Save(path);
	}

	/// <summary>Pushes everything that isn't read live into the engine.</summary>
	public void Apply()
	{
		SetBusVolume("Master", MasterVolume);
		SetBusVolume("World", WorldVolume);
		SetBusVolume("Mask", MaskVolume);
		GetViewport().Scaling3DScale = RenderScale;

		if (DisplayServer.GetName() == "headless")
			return;
		DisplayServer.WindowSetMode(Fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed);
		DisplayServer.WindowSetVsyncMode(VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
	}

	private static void SetBusVolume(string bus, float volume)
	{
		int index = AudioServer.GetBusIndex(bus);
		if (index >= 0)
			AudioServer.SetBusVolumeDb(index, Mathf.LinearToDb(Mathf.Max(volume, 0.0001f)));
	}
}
