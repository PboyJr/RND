using Godot;
using RND.Audio;
using RND.Combat;
using RND.Enemies;
using RND.Items;
using RND.Players;
using RND.Vfx;

namespace RND.Levels;

/// <summary>
/// Base script for playable levels. On the host it spawns players, enemies and projectiles; the
/// MultiplayerSpawners in the scene replicate them to everyone else.
/// </summary>
public partial class Level : Node3D
{
	public static Level Current { get; private set; }

	[Export] public PackedScene PlayerScene { get; set; }
	[Export] public PackedScene EnemyScene { get; set; }
	[Export] public float EnemyRespawnDelay { get; set; } = 12f;

	public Effects Effects { get; private set; }

	private Node3D _players;
	private Node3D _enemies;
	private Node3D _projectiles;
	private bool _spawningPlayers;

	public override void _EnterTree() => Current = this;

	public override void _Ready()
	{
		_players = GetNode<Node3D>("Players");
		_enemies = GetNode<Node3D>("Enemies");
		_projectiles = GetNode<Node3D>("Projectiles");
		Effects = GetNode<Effects>("Effects");

		if (!Multiplayer.IsServer())
			return;

		_spawningPlayers = true;
		Multiplayer.PeerConnected += OnPeerConnected;
		Multiplayer.PeerDisconnected += OnPeerDisconnected;

		AddPlayer(Multiplayer.GetUniqueId()); // the host plays too
		foreach (int peerId in Multiplayer.GetPeers())
			AddPlayer(peerId);

		// Only the host runs AI, so only the host needs a navmesh. Deferred because CSG geometry only
		// builds its meshes at the end of the frame; baking now would find nothing to walk on.
		var navigation = GetNode<NavigationRegion3D>("Navigation");
		navigation.BakeFinished += () => DropLinks.Generate(navigation);
		Callable.From(() => navigation.BakeNavigationMesh()).CallDeferred();

		foreach (Node point in GetTree().GetNodesInGroup("enemy_spawn"))
			SpawnEnemy((Node3D)point);
	}

	public override void _ExitTree()
	{
		if (Current == this)
			Current = null;

		if (!_spawningPlayers)
			return;

		_spawningPlayers = false;
		Multiplayer.PeerConnected -= OnPeerConnected;
		Multiplayer.PeerDisconnected -= OnPeerDisconnected;
	}

	/// <summary>
	/// Host only. Something made a sound: every enemy within earshot decides for itself whether it
	/// heard it (walls muffle) and whether to come and look.
	/// </summary>
	public void EmitNoise(Vector3 position, float radius)
	{
		if (!Multiplayer.IsServer())
			return;

		foreach (Node child in _enemies.GetChildren())
			(child as Enemy)?.Hear(position, radius);
	}

	/// <summary>
	/// Host only. A sound everyone hears: players through their speakers (muffled by their mask),
	/// enemies through Hear(), at the same radius, so the AI never hears something you couldn't.
	/// Silent AI-only noise (footsteps, for now) uses EmitNoise.
	/// </summary>
	public void EmitSound(Vector3 position, float radius, SoundKind sound)
	{
		if (!Multiplayer.IsServer())
			return;

		EmitNoise(position, radius);
		Effects.Rpc(nameof(Effects.PlaySound), (int)sound, position, radius);
	}

	/// <summary>Host only. The ProjectileSpawner must list the projectile's scene.</summary>
	public void SpawnProjectile(PackedScene scene, Vector3 origin, Vector3 velocity, int throwerId)
	{
		var projectile = scene.Instantiate<SplashProjectile>();
		projectile.Position = origin;
		projectile.Velocity = velocity;
		projectile.ThrowerId = throwerId;
		_projectiles.AddChild(projectile, forceReadableName: true);
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

	private void SpawnEnemy(Node3D point)
	{
		var enemy = EnemyScene.Instantiate<Enemy>();
		enemy.Position = point.GlobalPosition;
		enemy.Rotation = new Vector3(0, point.GlobalRotation.Y, 0);
		enemy.SyncPosition = enemy.Position;
		enemy.SyncYaw = enemy.Rotation.Y;
		enemy.GetNode<Health>("Health").Died += _ => RespawnEnemyLater(point);
		_enemies.AddChild(enemy, forceReadableName: true);
	}

	private async void RespawnEnemyLater(Node3D point)
	{
		await ToSignal(GetTree().CreateTimer(EnemyRespawnDelay), SceneTreeTimer.SignalName.Timeout);
		if (IsInstanceValid(this) && IsInsideTree() && IsInstanceValid(point))
			SpawnEnemy(point);
	}
}
