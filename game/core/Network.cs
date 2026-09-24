using Godot;

namespace RND.Core;

/// <summary>
/// Autoload that owns the multiplayer connection. This is the only place that knows which
/// transport we use: ENet for now (LAN / localhost testing), Steam later. Everything else talks
/// to Godot's high-level multiplayer API, so swapping transports shouldn't touch gameplay code.
/// </summary>
public partial class Network : Node
{
	public const int DefaultPort = 7777;
	public const int MaxClients = 5; // + the host = 6 players

	[Signal] public delegate void SessionStartedEventHandler();
	[Signal] public delegate void SessionEndedEventHandler(string reason);

	public static Network Instance { get; private set; }

	private bool _active;

	public override void _EnterTree() => Instance = this;

	public override void _Ready()
	{
		Multiplayer.ConnectedToServer += () => EmitSignal(SignalName.SessionStarted);
		Multiplayer.ConnectionFailed += () => EndSession("Couldn't reach the host.");
		Multiplayer.ServerDisconnected += () => EndSession("Lost connection to the host.");
	}

	public Error Host(int port = DefaultPort)
	{
		if (_active)
			return Error.AlreadyInUse;

		var peer = new ENetMultiplayerPeer();
		Error error = peer.CreateServer(port, MaxClients);
		if (error != Error.Ok)
			return error;

		StartSession(peer);
		EmitSignal(SignalName.SessionStarted);
		return Error.Ok;
	}

	/// <summary>Starts connecting. SessionStarted fires once we're in, SessionEnded if it fails.</summary>
	public Error Join(string address, int port = DefaultPort)
	{
		if (_active)
			return Error.AlreadyInUse;

		var peer = new ENetMultiplayerPeer();
		Error error = peer.CreateClient(address, port);
		if (error != Error.Ok)
			return error;

		StartSession(peer);
		return Error.Ok;
	}

	public void Leave() => EndSession("");

	private void StartSession(MultiplayerPeer peer)
	{
		_active = true;
		Multiplayer.MultiplayerPeer = peer;
	}

	private void EndSession(string reason)
	{
		if (!_active)
			return;

		_active = false;
		Multiplayer.MultiplayerPeer.Close();
		Multiplayer.MultiplayerPeer = new OfflineMultiplayerPeer();
		EmitSignal(SignalName.SessionEnded, reason);
	}
}
