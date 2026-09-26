using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Godot;
using Steamworks;
using Steamworks.Data;

namespace RND.Core;

/// <summary>
/// Autoload that owns the multiplayer connection. This is the only place that knows which
/// transport we use: ENet by IP address (LAN / localhost testing), or Steam (SteamPeer: friends
/// join from their friends list or an invite). Everything else talks to Godot's high-level
/// multiplayer API, so gameplay code doesn't know which one it's on.
/// </summary>
public partial class Network : Node
{
	public const int DefaultPort = 7777;
	public const int MaxClients = 5; // + the host = 6 players
	// A transfer channel of its own: reliable and unreliable messages on one channel stay in order
	// with each other (on channel 0 they go by separate routes). Players' state reports use it.
	public const int StateChannel = 1;
	// Valve's free test app ("Spacewar"). Our own app id comes with the Steam Direct fee.
	public const uint SteamAppId = 480;

	// Someone silent this long (a crash, a pulled cable) is dropped. ENet's default is 30 s, which
	// leaves a frozen player holding whatever they carried for half a minute.
	private const int TimeoutMinMs = 2000;
	private const int TimeoutMaxMs = 6000;
	private const double LeaveSeconds = 1.0; // how long a leaver waits to say goodbye properly

	[Signal] public delegate void SessionStartedEventHandler();
	[Signal] public delegate void SessionEndedEventHandler(string reason);
	/// <summary>Joining a friend's game from Steam (an invite, or "Join game" in the friends list).</summary>
	[Signal] public delegate void JoiningEventHandler(string message);

	public static Network Instance { get; private set; }

	/// <summary>True while a connection we left is still saying goodbye (see EndSession).</summary>
	public bool IsClosing => _closing != null;

	/// <summary>True once Steam is running and has let us in (see StartSteam).</summary>
	public bool SteamReady { get; private set; }

	/// <summary>True while we're in a Steam lobby, so friends can be invited.</summary>
	public bool CanInvite => _lobby != null;

	/// <summary>
	/// The network smoke test's --steam: Host and Join by address go through Steam's sockets
	/// instead of ENet, so the Steam transport can be tested on one computer.
	/// </summary>
	public bool DirectOverSteam { get; set; }

	private bool _active;
	private ENetMultiplayerPeer _closing;
	private double _closeBy;
	private Lobby? _lobby;

	// Steam's native library is copied next to our assemblies, but Godot loads those from memory, so
	// .NET doesn't know to look there.
	static Network() => NativeLibrary.SetDllImportResolver(typeof(SteamClient).Assembly, (name, _, _) =>
		NativeLibrary.TryLoad(Path.Combine(AppContext.BaseDirectory, Path.ChangeExtension(name, ".dll")), out IntPtr library) ? library : IntPtr.Zero);

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

		// Headless runs are tests and servers: no Steam unless a test asks for it.
		if (DisplayServer.GetName() == "headless" || !StartSteam())
			return;

		// Steam starts the game with "+connect_lobby <id>" when you accept an invite while it isn't running.
		string[] args = OS.GetCmdlineArgs();
		int at = Array.IndexOf(args, "+connect_lobby");
		if (at >= 0 && at + 1 < args.Length && ulong.TryParse(args[at + 1], out ulong lobby))
			JoinLobby(lobby);
	}

	public override void _ExitTree()
	{
		if (!SteamReady)
			return;
		_lobby?.Leave();
		SteamClient.Shutdown();
	}

	public override void _Process(double delta)
	{
		if (SteamReady)
			SteamClient.RunCallbacks();

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

	/// <summary>Connects to Steam if it's running. False if it isn't (Steam hosting is then off).</summary>
	public bool StartSteam()
	{
		if (SteamReady)
			return true;
		try
		{
			SteamClient.Init(SteamAppId, asyncCallbacks: false); // callbacks run in _Process, on the main thread
		}
		catch (Exception e)
		{
			GD.Print($"Steam isn't available, so Steam hosting is off: {e.Message}");
			return false;
		}

		SteamReady = true;
		SteamNetworkingUtils.Timeout = TimeoutMaxMs;
		SteamNetworkingUtils.InitRelayNetworkAccess(); // find the relays now, so hosting and joining start quicker
		SteamFriends.OnGameLobbyJoinRequested += (lobby, _) => JoinLobby(lobby.Id);
		return true;
	}

	public Error Host(int port = DefaultPort)
	{
		if (_active)
			return Error.AlreadyInUse;
		if (DirectOverSteam)
			return HostWith(StartSteam() ? SteamPeer.CreateHost(MaxClients, null, (ushort)port) : null);

		var peer = new ENetMultiplayerPeer();
		Error error = peer.CreateServer(port, MaxClients);
		return error != Error.Ok ? error : HostWith(peer);
	}

	/// <summary>
	/// Hosts on Steam: a friends-only lobby that friends join from their friends list or an invite.
	/// Only friends and people in the lobby get in.
	/// </summary>
	public Error HostSteam()
	{
		if (_active)
			return Error.AlreadyInUse;
		if (!SteamReady)
			return Error.Unavailable;

		SteamPeer peer = SteamPeer.CreateHost(MaxClients, IsWelcome);
		Error error = HostWith(peer);
		if (error == Error.Ok)
			OpenLobby(peer);
		return error;
	}

	private Error HostWith(MultiplayerPeer peer)
	{
		if (peer == null)
			return Error.CantCreate;
		StartSession(peer);
		EmitSignal(SignalName.SessionStarted);
		return Error.Ok;
	}

	private async void OpenLobby(SteamPeer peer)
	{
		Lobby? lobby = await SteamMatchmaking.CreateLobbyAsync(MaxClients + 1);
		if (lobby is not Lobby opened)
			return;
		if (Multiplayer.MultiplayerPeer != peer)
		{
			opened.Leave(); // we stopped hosting while it was being made
			return;
		}
		opened.SetFriendsOnly();
		opened.SetJoinable(true);
		_lobby = opened;
	}

	private bool IsWelcome(SteamId id) => new Friend(id).IsFriend || (_lobby is Lobby lobby && lobby.Members.Any(member => member.Id == id));

	/// <summary>Opens Steam's invite dialog for our lobby.</summary>
	public void InviteFriends()
	{
		if (_lobby is Lobby lobby)
			SteamFriends.OpenGameInviteOverlay(lobby.Id);
	}

	/// <summary>Starts connecting. SessionStarted fires once we're in, SessionEnded if it fails.</summary>
	public Error Join(string address, int port = DefaultPort)
	{
		if (_active)
			return Error.AlreadyInUse;
		if (DirectOverSteam)
			return JoinWith(StartSteam() ? SteamPeer.CreateClient(address, (ushort)port) : null);

		var peer = new ENetMultiplayerPeer();
		Error error = peer.CreateClient(address, port);
		return error != Error.Ok ? error : JoinWith(peer);
	}

	private Error JoinWith(MultiplayerPeer peer)
	{
		if (peer == null)
			return Error.CantCreate;
		StartSession(peer);
		return Error.Ok;
	}

	// A friend's lobby: join it, then connect to whoever owns it (the host).
	private async void JoinLobby(SteamId id)
	{
		if (_active)
			return;
		EmitSignal(SignalName.Joining, "Joining your friend's game...");

		Lobby? lobby = await SteamMatchmaking.JoinLobbyAsync(id);
		if (lobby is not Lobby joined)
		{
			EmitSignal(SignalName.SessionEnded, "Couldn't join your friend's game (the lobby may have closed).");
			return;
		}
		if (_active || JoinWith(SteamPeer.CreateClient(joined.Owner.Id)) != Error.Ok)
		{
			joined.Leave();
			return;
		}
		_lobby = joined;
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
	// packet; if that's lost, the host only notices when we time out.) Steam does the same itself
	// when a SteamPeer closes.
	private void EndSession(string reason)
	{
		if (!_active)
			return;

		_active = false;
		_lobby?.Leave();
		_lobby = null;
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
