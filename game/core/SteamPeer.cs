using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Godot;
using Steamworks;
using Steamworks.Data;

namespace RND.Core;

/// <summary>
/// Godot's multiplayer over Steam's networking sockets. Friends reach the host through Valve's relay
/// by Steam id, so nobody needs an IP address or port forwarding. Only Network creates these.
/// It behaves like our ENet setup, so gameplay code can't tell the difference:
/// - the host is peer 1 and hands each client a random peer id when it connects;
/// - on a channel other than 0, an unreliable-ordered message is dropped if a reliable message sent
///   after it has already arrived. ENet does this, and the level-change handshake relies on it (see
///   Network.StateChannel). Steam alone doesn't order unreliable and reliable messages.
/// </summary>
public partial class SteamPeer : MultiplayerPeerExtension
{
	private const int MaxMessageSize = 512 * 1024; // Steam's limit
	private const byte Welcome = 255; // host → new client: "your peer id is ..."
	private const int OrderedHeader = 10; // mode, channel, reliable count, sequence number

	private sealed class Link
	{
		public Connection Connection;
		public int Id;
		// Per channel: reliable messages sent / received, and the last unreliable-ordered sequence number.
		public readonly uint[] ReliableSent = new uint[256];
		public readonly uint[] ReliableReceived = new uint[256];
		public readonly uint[] OrderedSent = new uint[256];
		public readonly uint[] OrderedReceived = new uint[256];
	}

	private readonly record struct Packet(int From, int Channel, TransferModeEnum Mode, byte[] Data);

	private HostSocket _socket;
	private ClientConnection _client;
	private Func<SteamId, bool> _admits;
	private int _maxClients;
	private int _uniqueId;
	private ConnectionStatus _status = ConnectionStatus.Disconnected;
	private bool _refusing;
	private int _targetPeer;
	private int _transferChannel;
	private TransferModeEnum _transferMode = TransferModeEnum.Reliable;
	private readonly Dictionary<int, Link> _links = new();
	private readonly Queue<Packet> _incoming = new();

	/// <summary>
	/// Hosts through Steam's relay, or on a UDP port when `port` is given (the network smoke test, all
	/// on one computer). `admits` decides who may connect. Null if Steam refused.
	/// </summary>
	public static SteamPeer CreateHost(int maxClients, Func<SteamId, bool> admits, ushort? port = null)
	{
		HostSocket socket = port is ushort p
			? SteamNetworkingSockets.CreateNormalSocket<HostSocket>(NetAddress.AnyIp(p))
			: SteamNetworkingSockets.CreateRelaySocket<HostSocket>();
		if (socket == null)
			return null;

		var peer = new SteamPeer { _socket = socket, _admits = admits, _maxClients = maxClients, _uniqueId = 1, _status = ConnectionStatus.Connected };
		socket.Peer = peer;
		return peer;
	}

	/// <summary>Connects to a host by Steam id (through the relay).</summary>
	public static SteamPeer CreateClient(SteamId host) => Client(SteamNetworkingSockets.ConnectRelay<ClientConnection>(host));

	/// <summary>Connects to a host by address (the network smoke test).</summary>
	public static SteamPeer CreateClient(string address, ushort port) => Client(SteamNetworkingSockets.ConnectNormal<ClientConnection>(NetAddress.From(address, port)));

	private static SteamPeer Client(ClientConnection connection)
	{
		if (connection == null)
			return null;

		var peer = new SteamPeer { _client = connection, _status = ConnectionStatus.Connecting };
		connection.Peer = peer;
		peer._links[1] = new Link { Connection = connection.Connection, Id = 1 };
		return peer;
	}

	public override void _Poll()
	{
		_socket?.Receive();
		_client?.Receive();
	}

	public override Error _PutPacketScript(byte[] buffer)
	{
		if (_status != ConnectionStatus.Connected)
			return Error.Unconfigured;
		if (_client != null)
			return Send(_links[1], buffer); // clients only ever talk to the host
		if (_targetPeer > 0)
			return _links.TryGetValue(_targetPeer, out Link link) ? Send(link, buffer) : Error.InvalidParameter;

		foreach (Link link in _links.Values)
			if (link.Id != -_targetPeer)
				Send(link, buffer);
		return Error.Ok;
	}

	private Error Send(Link link, byte[] payload)
	{
		int channel = _transferChannel & 0xFF;
		bool ordered = _transferMode == TransferModeEnum.UnreliableOrdered;
		var message = new byte[(ordered ? OrderedHeader : 2) + payload.Length];
		message[0] = (byte)_transferMode;
		message[1] = (byte)channel;
		if (_transferMode == TransferModeEnum.Reliable)
			link.ReliableSent[channel]++;
		if (ordered)
		{
			BitConverter.TryWriteBytes(message.AsSpan(2), link.ReliableSent[channel]);
			BitConverter.TryWriteBytes(message.AsSpan(6), ++link.OrderedSent[channel]);
		}
		payload.CopyTo(message, message.Length - payload.Length);

		SendType type = _transferMode == TransferModeEnum.Reliable ? SendType.Reliable : SendType.Unreliable;
		return link.Connection.SendMessage(message, type) == Result.OK ? Error.Ok : Error.Failed;
	}

	private void Receive(Connection connection, IntPtr data, int size)
	{
		Link link = _client != null ? _links.GetValueOrDefault(1) : _links.GetValueOrDefault((int)connection.UserData);
		if (link == null || size < 2)
			return;

		byte mode = Marshal.ReadByte(data, 0);
		int channel = Marshal.ReadByte(data, 1);
		if (mode == Welcome)
		{
			if (_client != null && _status == ConnectionStatus.Connecting && size >= 6)
			{
				_uniqueId = Marshal.ReadInt32(data, 2);
				_status = ConnectionStatus.Connected;
				EmitSignal(MultiplayerPeer.SignalName.PeerConnected, 1);
			}
			return;
		}
		if (_status != ConnectionStatus.Connected)
			return;

		int header = 2;
		switch ((TransferModeEnum)mode)
		{
			case TransferModeEnum.Reliable:
				link.ReliableReceived[channel]++;
				break;
			case TransferModeEnum.UnreliableOrdered:
				header = OrderedHeader;
				if (size < header)
					return;
				uint reliableCount = (uint)Marshal.ReadInt32(data, 2);
				uint sequence = (uint)Marshal.ReadInt32(data, 6);
				if ((channel > 0 && reliableCount != link.ReliableReceived[channel]) || sequence <= link.OrderedReceived[channel])
					return; // older than something already delivered (or ahead of a reliable still on its way)
				link.OrderedReceived[channel] = sequence;
				break;
			case TransferModeEnum.Unreliable:
				break;
			default:
				return;
		}

		var payload = new byte[size - header];
		Marshal.Copy(data + header, payload, 0, payload.Length);
		_incoming.Enqueue(new Packet(link.Id, channel, (TransferModeEnum)mode, payload));
	}

	// Host: a client got through. Give it an id; the welcome is reliable, so it arrives before anything
	// else we send it.
	private void Admit(Connection connection)
	{
		int id;
		do
			id = (int)GenerateUniqueId();
		while (_links.ContainsKey(id));

		connection.UserData = id;
		_links[id] = new Link { Connection = connection, Id = id };
		var welcome = new byte[6];
		welcome[0] = Welcome;
		BitConverter.TryWriteBytes(welcome.AsSpan(2), id);
		connection.SendMessage(welcome, SendType.Reliable);
		EmitSignal(MultiplayerPeer.SignalName.PeerConnected, id);
	}

	private bool Accepts(ConnectionInfo info) => !_refusing && _links.Count < _maxClients && (_admits == null || _admits(info.Identity.SteamId));

	private void Dropped(Connection connection)
	{
		if (_client != null)
		{
			bool wasConnected = _status == ConnectionStatus.Connected;
			_status = ConnectionStatus.Disconnected;
			_links.Clear();
			if (wasConnected)
				EmitSignal(MultiplayerPeer.SignalName.PeerDisconnected, 1);
			return;
		}

		int id = (int)connection.UserData;
		if (_links.Remove(id))
			EmitSignal(MultiplayerPeer.SignalName.PeerDisconnected, id);
	}

	public override void _DisconnectPeer(int peer, bool force)
	{
		if (!_links.Remove(peer, out Link link))
			return;
		link.Connection.Close(linger: !force);
		EmitSignal(MultiplayerPeer.SignalName.PeerDisconnected, peer);
	}

	// A client lingers so its last messages (say, dropping what it carried) still reach the host.
	public override void _Close()
	{
		foreach (Link link in _links.Values)
			link.Connection.Close(linger: _client != null);
		_links.Clear();
		_incoming.Clear();
		_socket?.Close();
		_socket = null;
		_client = null;
		_status = ConnectionStatus.Disconnected;
	}

	public override int _GetAvailablePacketCount() => _incoming.Count;
	public override byte[] _GetPacketScript() => _incoming.TryDequeue(out Packet packet) ? packet.Data : Array.Empty<byte>();
	public override int _GetPacketPeer() => _incoming.TryPeek(out Packet packet) ? packet.From : 0;
	public override int _GetPacketChannel() => _incoming.TryPeek(out Packet packet) ? packet.Channel : 0;
	public override TransferModeEnum _GetPacketMode() => _incoming.TryPeek(out Packet packet) ? packet.Mode : TransferModeEnum.Reliable;
	public override int _GetMaxPacketSize() => MaxMessageSize - OrderedHeader;
	public override void _SetTransferChannel(int channel) => _transferChannel = channel;
	public override int _GetTransferChannel() => _transferChannel;
	public override void _SetTransferMode(TransferModeEnum mode) => _transferMode = mode;
	public override TransferModeEnum _GetTransferMode() => _transferMode;
	public override void _SetTargetPeer(int peer) => _targetPeer = peer;
	public override bool _IsServer() => _uniqueId == 1;
	public override int _GetUniqueId() => _uniqueId;
	public override bool _IsServerRelaySupported() => false;
	public override void _SetRefuseNewConnections(bool enable) => _refusing = enable;
	public override bool _IsRefusingNewConnections() => _refusing;
	public override ConnectionStatus _GetConnectionStatus() => _status;

	private sealed class HostSocket : SocketManager
	{
		public SteamPeer Peer;

		public override void OnConnecting(Connection connection, ConnectionInfo info)
		{
			if (Peer.Accepts(info))
				base.OnConnecting(connection, info); // accepts
			else
				connection.Close();
		}

		public override void OnConnected(Connection connection, ConnectionInfo info)
		{
			base.OnConnected(connection, info);
			Peer.Admit(connection);
		}

		public override void OnDisconnected(Connection connection, ConnectionInfo info)
		{
			Peer.Dropped(connection);
			base.OnDisconnected(connection, info);
		}

		public override void OnMessage(Connection connection, NetIdentity identity, IntPtr data, int size, long messageNum, long recvTime, int channel)
			=> Peer.Receive(connection, data, size);
	}

	private sealed class ClientConnection : ConnectionManager
	{
		public SteamPeer Peer;

		public override void OnDisconnected(ConnectionInfo info)
		{
			base.OnDisconnected(info);
			Peer.Dropped(Connection);
		}

		public override void OnMessage(IntPtr data, int size, long messageNum, long recvTime, int channel)
			=> Peer.Receive(Connection, data, size);
	}
}
