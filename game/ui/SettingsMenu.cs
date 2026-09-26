using System;
using Godot;
using RND.Core;

namespace RND.UI;

/// <summary>
/// The settings panel, opened from the main menu and the pause menu. Every change applies at once
/// and is saved when the panel closes. The rows are one table; adding a setting is a line here plus
/// a property on Settings.
/// </summary>
public partial class SettingsMenu : PanelContainer
{
	public static SettingsMenu Instance { get; private set; }

	[Signal] public delegate void ClosedEventHandler();

	private GridContainer _rows;

	public override void _EnterTree() => Instance = this;

	public override void _Ready()
	{
		_rows = GetNode<GridContainer>("%Rows");
		GetNode<Button>("%Done").Pressed += Close;

		Settings s = Settings.Instance;
		AddSlider("Mouse sensitivity", 0.1, 3, 0.05, () => s.MouseSensitivity, v => s.MouseSensitivity = v, v => $"{v:0.00}×");
		AddSlider("Field of view", 60, 110, 1, () => s.FieldOfView, v => s.FieldOfView = v, v => $"{v:0}°");
		AddSlider("Master volume", 0, 1, 0.05, () => s.MasterVolume, v => s.MasterVolume = v, Percent);
		AddSlider("World sounds", 0, 1, 0.05, () => s.WorldVolume, v => s.WorldVolume = v, Percent);
		AddSlider("Inside the mask", 0, 1, 0.05, () => s.MaskVolume, v => s.MaskVolume = v, Percent);
		AddSlider("3D resolution", 0.5, 1, 0.05, () => s.RenderScale, v => s.RenderScale = v, Percent);
		AddCheck("Fullscreen", () => s.Fullscreen, v => s.Fullscreen = v);
		AddCheck("V-sync", () => s.VSync, v => s.VSync = v);
	}

	public void Open() => Show();

	public void Close()
	{
		Hide();
		Settings.Instance.Save();
		EmitSignal(SignalName.Closed);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (Visible && @event.IsActionPressed("pause"))
		{
			Close();
			GetViewport().SetInputAsHandled();
		}
	}

	private static string Percent(float value) => $"{Mathf.RoundToInt(value * 100f)}%";

	private void AddSlider(string label, double min, double max, double step, Func<float> get, Action<float> set, Func<float, string> format)
	{
		var slider = new HSlider { MinValue = min, MaxValue = max, Step = step, Value = get(), CustomMinimumSize = new Vector2(220, 0) };
		var value = new Label { Text = format(get()), CustomMinimumSize = new Vector2(56, 0) };
		slider.ValueChanged += v =>
		{
			set((float)v);
			value.Text = format((float)v);
			Settings.Instance.Apply();
		};
		var row = new HBoxContainer();
		row.AddChild(slider);
		row.AddChild(value);
		_rows.AddChild(new Label { Text = label });
		_rows.AddChild(row);
	}

	private void AddCheck(string label, Func<bool> get, Action<bool> set)
	{
		// An On / Off button: the default theme's unticked checkbox (or switch) barely shows on the dark panel.
		var check = new Button { ToggleMode = true, ButtonPressed = get(), Text = get() ? "On" : "Off", CustomMinimumSize = new Vector2(64, 0) };
		check.Toggled += on =>
		{
			set(on);
			check.Text = on ? "On" : "Off";
			Settings.Instance.Apply();
		};
		_rows.AddChild(new Label { Text = label });
		_rows.AddChild(check);
	}
}
