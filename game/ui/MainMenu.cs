using System.Collections.Generic;
using Godot;
using RND.Core;
using RND.Players;

namespace RND.UI;

public partial class MainMenu : Control
{
	private LineEdit _address;
	private Button _host;
	private Button _join;
	private Label _status;
	private OptionButton _level;
	private Label _profile;

	public override void _Ready()
	{
		_address = GetNode<LineEdit>("%Address");
		_host = GetNode<Button>("%Host");
		_join = GetNode<Button>("%Join");
		_status = GetNode<Label>("%Status");
		_level = GetNode<OptionButton>("%Level");
		_profile = GetNode<Label>("%Profile");
		ShowProfile();

		_host.Pressed += OnHostPressed;
		_join.Pressed += OnJoinPressed;
		GetNode<Button>("%Settings").Pressed += () => SettingsMenu.Instance?.Open();
		_address.TextSubmitted += _ => OnJoinPressed();
	}

	/// <summary>The index into Main.Levels the host will load.</summary>
	public int SelectedLevel => Mathf.Max(_level.Selected, 0);

	public void SetLevels(IEnumerable<string> names)
	{
		_level.Clear();
		foreach (string name in names)
			_level.AddItem(name);
	}

	public void Open(string message)
	{
		Show();
		_status.Text = message;
		ShowProfile();
		SetBusy(false);
	}

	private void OnHostPressed()
	{
		Error error = Network.Instance.Host();
		if (error != Error.Ok)
			_status.Text = $"Couldn't host on port {Network.DefaultPort} ({error}). Is another copy already hosting?";
	}

	private void OnJoinPressed()
	{
		(string address, int port) = ParseAddress(_address.Text);
		Error error = Network.Instance.Join(address, port);
		if (error != Error.Ok)
		{
			_status.Text = $"Couldn't connect to {address}:{port} ({error}).";
			return;
		}

		_status.Text = $"Connecting to {address}:{port}...";
		SetBusy(true);
	}

	private void ShowProfile()
	{
		ProfileStore p = ProfileStore.Instance;
		if (p != null)
			_profile.Text = $"Level {p.Level} ({p.Xp} / {p.XpToNextLevel} XP)  ·  {p.Money} money  ·  {p.RunCount} runs";
	}

	private void SetBusy(bool busy)
	{
		_host.Disabled = busy;
		_join.Disabled = busy;
	}

	// Accepts "1.2.3.4" or "1.2.3.4:7777".
	private static (string Address, int Port) ParseAddress(string text)
	{
		text = text.Trim();
		if (text == "")
			return ("127.0.0.1", Network.DefaultPort);

		int colon = text.LastIndexOf(':');
		if (colon > 0 && text.IndexOf(':') == colon && int.TryParse(text[(colon + 1)..], out int port))
			return (text[..colon], port);

		return (text, Network.DefaultPort);
	}
}
