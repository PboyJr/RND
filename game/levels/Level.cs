using System.Collections.Generic;
using System.Linq;
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
	private const float ReleaseSoundRadius = 40f; // the whole chamber hears the variable arrive
	private const float LostSpotMerge = 2.5f;

	public static Level Current { get; private set; }

	[Export] public PackedScene PlayerScene { get; set; }
	[Export] public PackedScene EnemyScene { get; set; }
	[Export] public float EnemyRespawnDelay { get; set; } = 12f;
	// Seconds before the level's enemies first appear (a maze chamber's "variable"). They arrive with a
	// growl everyone hears. 0: there from the start.
	[Export] public float EnemyReleaseDelay { get; set; }
	// Dead players come back on a timer. Off (maze chambers): they stay down until a teammate revives them.
	[Export] public bool TimedRespawn { get; set; } = true;

	public Effects Effects { get; private set; }

	private Node3D _players;
	private MultiplayerSpawner _playerSpawner;
	private Node3D _enemies;
	private Node3D _projectiles;
	private bool _spawningPlayers;
	private readonly List<(Vector3 Position, int Count)> _lostSpots = new();

	public IEnumerable<Enemy> Enemies => _enemies.GetChildren().OfType<Enemy>();

	public override void _EnterTree() => Current = this;

	public override void _Ready()
	{
		_players = GetNode<Node3D>("Players");
		_playerSpawner = GetNode<MultiplayerSpawner>("PlayerSpawner");
		_playerSpawner.SpawnFunction = Callable.From<Variant, Node>(CreatePlayer); // every peer builds its copy this way
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
		{
			if (EnemyReleaseDelay > 0f)
				SpawnEnemyLater((Node3D)point, EnemyReleaseDelay, announce: true);
			else
				SpawnEnemy((Node3D)point);
		}
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
	/// Host only. An enemy calling out to the others (a growl everyone hears): any enemy that hears it
	/// heads for `lead`, the spot the caller is telling them about, rather than for the caller.
	/// </summary>
	public void EmitCall(Vector3 position, float radius, SoundKind sound, Vector3 lead, Enemy caller)
	{
		if (!Multiplayer.IsServer())
			return;

		Effects.Rpc(nameof(Effects.PlaySound), (int)sound, position, radius);
		foreach (Node child in _enemies.GetChildren())
			if (child is Enemy enemy && enemy != caller)
				enemy.HearCall(position, radius, lead);
	}

	/// <summary>
	/// Host only. Where players keep getting away: the enemies (who all tell this level) check those
	/// spots more and more. Spots within LostSpotMerge of each other count as one.
	/// </summary>
	public void RememberLostAt(Vector3 position)
	{
		for (int i = 0; i < _lostSpots.Count; i++)
		{
			if (_lostSpots[i].Position.DistanceTo(position) < LostSpotMerge)
			{
				_lostSpots[i] = (_lostSpots[i].Position, _lostSpots[i].Count + 1);
				return;
			}
		}
		_lostSpots.Add((position, 1));
	}

	/// <summary>Host only. The spots where players got away, and how many times.</summary>
	public IReadOnlyList<(Vector3 Position, int Count)> LostSpots => _lostSpots;

	/// <summary>How many times players have got away near this point (for the tests).</summary>
	public int TimesLostNear(Vector3 position) => _lostSpots.Where(s => s.Position.DistanceTo(position) < LostSpotMerge).Sum(s => s.Count);

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

	// The host hands out spawn points (the first one nobody has), so no two players start on top of
	// each other. Late joiners get one that's free.
	private void AddPlayer(int peerId)
	{
		if (_players.HasNode(peerId.ToString()))
			return;

		var taken = _players.GetChildren().OfType<Player>().Select(p => p.SpawnSlot).ToHashSet();
		int slot = Enumerable.Range(0, taken.Count + 1).First(s => !taken.Contains(s));
		_playerSpawner.Spawn(new Godot.Collections.Array { peerId, slot });
	}

	private Node CreatePlayer(Variant data)
	{
		var args = data.AsGodotArray();
		var player = PlayerScene.Instantiate<Player>();
		player.Name = args[0].ToString();
		player.SpawnSlot = args[1].AsInt32();
		return player;
	}

	private void SpawnEnemy(Node3D point)
	{
		var enemy = EnemyScene.Instantiate<Enemy>();
		enemy.Position = point.GlobalPosition;
		enemy.Rotation = new Vector3(0, point.GlobalRotation.Y, 0);
		enemy.SyncPosition = enemy.Position;
		enemy.SyncYaw = enemy.Rotation.Y;
		enemy.GetNode<Health>("Health").Died += _ => SpawnEnemyLater(point, EnemyRespawnDelay, announce: false);
		_enemies.AddChild(enemy, forceReadableName: true);
	}

	private async void SpawnEnemyLater(Node3D point, float delay, bool announce)
	{
		await ToSignal(GetTree().CreateTimer(delay), SceneTreeTimer.SignalName.Timeout);
		if (!IsInstanceValid(this) || !IsInsideTree() || !IsInstanceValid(point))
			return;

		SpawnEnemy(point);
		if (announce)
			EmitSound(point.GlobalPosition, ReleaseSoundRadius, SoundKind.Growl);
	}
}
