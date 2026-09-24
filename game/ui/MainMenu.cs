using Godot;
using RND.Core;

namespace RND.UI;

public partial class MainMenu : Control
{
	private LineEdit _address;
	private Button _host;
	private Button _join;
	private Label _status;

	public override void _Ready()
	{
		_address = GetNode<LineEdit>("%Address");
		_host = GetNode<Button>("%Host");
		_join = GetNode<Button>("%Join");
		_status = GetNode<Label>("%Status");

		_host.Pressed += OnHostPressed;
		_join.Pressed += OnJoinPressed;
		_address.TextSubmitted += _ => OnJoinPressed();
	}

	public void Open(string message)
	{
		Show();
		_status.Text = message;
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
