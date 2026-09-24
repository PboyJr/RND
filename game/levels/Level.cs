using Godot;
using RND.Players;

namespace RND.Levels;

/// <summary>
/// Base script for playable levels. On the host it spawns a player for every peer;
/// PlayerSpawner (a MultiplayerSpawner) replicates them to everyone else.
/// </summary>
public partial class Level : Node3D
{
	[Export] public PackedScene PlayerScene { get; set; }

	private Node3D _players;
	private bool _spawningPlayers;

	public override void _Ready()
	{
		_players = GetNode<Node3D>("Players");
		if (!Multiplayer.IsServer())
			return;

		_spawningPlayers = true;
		Multiplayer.PeerConnected += OnPeerConnected;
		Multiplayer.PeerDisconnected += OnPeerDisconnected;

		AddPlayer(Multiplayer.GetUniqueId()); // the host plays too
		foreach (int peerId in Multiplayer.GetPeers())
			AddPlayer(peerId);
	}

	public override void _ExitTree()
	{
		if (!_spawningPlayers)
			return;

		_spawningPlayers = false;
		Multiplayer.PeerConnected -= OnPeerConnected;
		Multiplayer.PeerDisconnected -= OnPeerDisconnected;
	}

	private void OnPeerConnected(long peerId) => AddPlayer((int)peerId);

	private void OnPeerDisconnected(long peerId) => _players.GetNodeOrNull(peerId.ToString())?.QueueFree();

	private void AddPlayer(int peerId)
	{
		if (_players.HasNode(peerId.ToString()))
			return;

		var player = PlayerScene.Instantiate<Player>();
		player.Name = peerId.ToString();
		_players.AddChild(player, forceReadableName: true);
	}
}
