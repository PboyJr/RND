using System.Linq;
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
	// A transfer channel of its own: reliable and unreliable messages on one channel stay in order
	// with each other (on channel 0 they go by separate routes). Players' state reports use it.
	public const int StateChannel = 1;

	// Someone silent this long (a crash, a pulled cable) is dropped. ENet's default is 30 s, which
	// leaves a frozen player holding whatever they carried for half a minute.
	private const int TimeoutMinMs = 2000;
	private const int TimeoutMaxMs = 6000;
	private const double LeaveSeconds = 1.0; // how long a leaver waits to say goodbye properly

	[Signal] public delegate void SessionStartedEventHandler();
	[Signal] public delegate void SessionEndedEventHandler(string reason);

	public static Network Instance { get; private set; }

	/// <summary>True while a connection we left is still saying goodbye (see EndSession).</summary>
	public bool IsClosing => _closing != null;

	private bool _active;
	private ENetMultiplayerPeer _closing;
	private double _closeBy;

	public override void _EnterTree() => Instance = this;

	public override void _Ready()
	{
		Multiplayer.ConnectedToServer += () =>
		{
			SetTimeout(1);
			EmitSignal(SignalName.SessionStarted);
		};
		Multiplayer.PeerConnected += id =>
		{
			if (Multiplayer.IsServer())
				SetTimeout(id);
		};
		Multiplayer.ConnectionFailed += () => EndSession("Couldn't reach the host.");
		Multiplayer.ServerDisconnected += () => EndSession("Lost connection to the host.");
	}

	public override void _Process(double delta)
	{
		if (_closing == null)
			return;

		bool saidGoodbye = _closing.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Disconnected
			|| _closing.Host == null
			|| _closing.Host.GetPeers().All(peer => peer.GetState() == ENetPacketPeer.PeerState.Disconnected);
		if (saidGoodbye || Time.GetTicksMsec() / 1000.0 > _closeBy)
		{
			_closing.Close();
			_closing = null;
			return;
		}
		_closing.Poll();
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
		// Star network: clients only ever talk to the host, so they don't need the relay that lets
		// clients message each other (or hear about each other joining and leaving). It can only be
		// changed while there's no active peer.
		Multiplayer.MultiplayerPeer = null;
		((SceneMultiplayer)Multiplayer).ServerRelay = false;
		Multiplayer.MultiplayerPeer = peer;
	}

	private void SetTimeout(long peerId)
	{
		if (Multiplayer.MultiplayerPeer is ENetMultiplayerPeer enet)
			enet.GetPeer((int)peerId)?.SetTimeout(0, TimeoutMinMs, TimeoutMaxMs);
	}

	// Leaving says goodbye properly: a disconnect the other side acknowledges, sent after anything
	// still queued, so the host lets go of our player at once. (Closing outright sends one unacked
	// packet; if that's lost, the host only notices when we time out.)
	private void EndSession(string reason)
	{
		if (!_active)
			return;

		_active = false;
		var peer = Multiplayer.MultiplayerPeer;
		Multiplayer.MultiplayerPeer = new OfflineMultiplayerPeer();
		if (peer is ENetMultiplayerPeer enet && enet.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected)
		{
			foreach (var remote in enet.Host.GetPeers())
				if (remote.GetState() == ENetPacketPeer.PeerState.Connected)
					remote.PeerDisconnectLater();
			_closing = enet;
			_closeBy = Time.GetTicksMsec() / 1000.0 + LeaveSeconds;
		}
		else
		{
			peer.Close();
		}
		EmitSignal(SignalName.SessionEnded, reason);
	}
}
