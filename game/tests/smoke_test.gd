extends Node

## Headless smoke test. Run from the game/ folder:
##   godot --headless res://tests/smoke_test.tscn -- --role=scenes
##   godot --headless res://tests/smoke_test.tscn -- --role=host     (start first)
##   godot --headless res://tests/smoke_test.tscn -- --role=client
## Prints "SMOKE PASS" or "SMOKE FAIL: ..." lines and exits with 0 / 1.
## Visual check (needs a real window, so no --headless): saves visor screenshots to --out.
##   godot --resolution 1280x720 res://tests/smoke_test.tscn -- --role=capture --out=C:/some/folder
## Listening check: renders every procedural sound to .wav in --out (and checks levels).
##   godot --headless res://tests/smoke_test.tscn -- --role=audio --out=C:/some/folder

const PORT := 17777
const TIMEOUT := 90.0
const SCENES := [
	"res://core/main.tscn",
	"res://levels/test_level.tscn",
	"res://players/player.tscn",
	"res://props/crate.tscn",
	"res://props/crate_large.tscn",
	"res://props/specimen_jar.tscn",
	"res://enemies/enemy.tscn",
	"res://items/acid_flask_projectile.tscn",
	"res://props/filter_canister.tscn",
	"res://ui/main_menu.tscn",
	"res://ui/hud.tscn",
	"res://ui/visor_hud.tscn",
]

var _role := ""
var _failures := PackedStringArray()
var _finished := false
var _main: Node
var _network: Node


func _ready() -> void:
	_role = _arg("role", "scenes")
	_network = get_node("/root/Network")
	get_tree().create_timer(TIMEOUT).timeout.connect(func(): _fail("timed out"); _finish())

	match _role:
		"scenes": await _run_scenes()
		"host": await _run_host()
		"client": await _run_client()
		"capture": await _run_capture()
		"audio": _check_audio(_arg("out", OS.get_user_data_dir()), true)
		_: _fail("unknown role '%s'" % _role)
	_finish()


# Every scene loads, instantiates and survives _Ready; the level plays offline.
func _run_scenes() -> void:
	for path in SCENES:
		var packed := load(path) as PackedScene
		if not _check(packed != null, "couldn't load " + path):
			continue
		var node := packed.instantiate()
		if not _check(node != null, "couldn't instantiate " + path):
			continue
		if path.ends_with("player.tscn"):
			node.name = "1" # players are named after their peer id
		add_child(node)
		await _frames(3)
		node.queue_free()
		await _frames(1)

	_check_audio(OS.get_user_data_dir(), false)

	# Offline, we're the host (peer 1), so the level should spawn us and simulate props.
	var level := (load("res://levels/test_level.tscn") as PackedScene).instantiate()
	add_child(level)
	await _seconds(1.5)
	var player := level.get_node_or_null("Players/1")
	if _check(player != null, "offline: level didn't spawn the local player"):
		_check(player.is_on_floor(), "offline: player isn't standing on the floor")
	for prop in level.get_node("Props").get_children():
		_check(not prop.freeze, "offline: %s should simulate on the host" % prop.name)
		_check(prop.global_position.y > -0.5, "offline: %s fell through the floor" % prop.name)
	if player != null:
		await _run_offline_combat(level, player)
		await _run_offline_ai(level, player)
		await _run_offline_filter(level, player)
		await _run_offline_liquid(level)
	level.queue_free()


# Acid flask vs. evil guy, evil guy vs. player, death and respawn, all offline as the host.
func _run_offline_combat(level: Node, player: Node3D) -> void:
	var flask = load("res://items/acid_flask.tres")
	_check(flask != null and flask.Cooldown == 15.0, "acid_flask.tres didn't load as a 15 s throwable")
	if not _check(player.Loadout.size() == 1, "player loadout should hold the acid flask (size %d)" % player.Loadout.size()):
		return

	var nav := level.get_node("Navigation") as NavigationRegion3D
	_check(await _wait_until(func(): return nav.navigation_mesh.get_polygon_count() > 0, 5.0), "offline: navmesh didn't bake")

	var enemies := level.get_node("Enemies")
	if not _check(enemies.get_child_count() == 1, "offline: expected 1 evil guy, found %d" % enemies.get_child_count()):
		return
	var enemy := enemies.get_child(0) as Node3D
	var enemy_health := enemy.get_node("Health")
	var player_health := player.get_node("Health")

	# Throw: freeze the AI, then throw from the hardest spot, bodies touching (0.8 m between centres).
	# The flask used to launch 0.4 m ahead of the eyes, i.e. inside the enemy, and fly straight through.
	enemy.set_physics_process(false)
	player.global_position = enemy.global_position + Vector3(0, 0, 0.8)
	await _frames(2)
	var eye: Vector3 = player.get_node("Head").global_position
	var aim: Vector3 = (enemy.global_position + Vector3.UP * 1.2 - eye).normalized()
	player.rpc_id(1, "RequestThrowItem", 1, eye, aim)
	_check(await _wait_until(func(): return enemy_health.Current < enemy_health.MaxHealth, 3.0), "offline: acid flask didn't hurt the evil guy")
	var speakers := level.get_node("Effects").get_children().filter(func(n): return n is AudioStreamPlayer3D)
	_check(not speakers.is_empty() and speakers[0].bus == &"World", "audio: the flask shattering made no (muffled) world sound")
	var after_hit: float = enemy_health.Current
	_log("flask hit: evil guy at %.0f / %.0f" % [after_hit, enemy_health.MaxHealth])

	player.rpc_id(1, "RequestThrowItem", 1, eye, aim) # still recharging, so the host must refuse
	await _seconds(1.0)
	_check(enemy_health.Current == after_hit, "offline: flask ignored its 15 s recharge")

	# The flask in hand empties when thrown, then its acid refills over the recharge.
	var hand_liquid := player.get_node("Head/Camera3D/HandItem").find_child("Liquid", true, false)
	player.SelectedSlot = 1
	await _frames(2)
	var full_fill: float = hand_liquid.Fill
	player.UseSelectedItem() # the host refuses it (still recharging), but our own flask empties
	await _frames(2)
	_check(full_fill > 0.5 and hand_liquid.Fill < 0.02, "offline: thrown flask didn't empty in hand (%.2f -> %.2f)" % [full_fill, hand_liquid.Fill])
	await _seconds(1.5)
	_check(hand_liquid.Fill > 0.03 and hand_liquid.Fill < 0.15, "offline: flask in hand isn't refilling with the recharge (%.2f)" % hand_liquid.Fill)
	player.SelectedSlot = 0

	# Let it loose next to us: it should swing and hurt us.
	enemy.set_physics_process(true)
	player.global_position = enemy.global_position + Vector3(0, 0, 1.2)
	_check(await _wait_until(func(): return player_health.Current < player_health.MaxHealth, 4.0), "offline: evil guy never hurt the player")
	_log("evil guy hit: player at %.0f / %.0f" % [player_health.Current, player_health.MaxHealth])

	# Pathfinding: from the floor beside the platform, it must walk round and up the ramp to reach
	# a player on the platform's far edge, not camp underneath swinging upward.
	var ledge_spot := Vector3(-7.5, 1.0, -6.0)
	player.global_position = ledge_spot
	enemy.global_position = Vector3(-9.5, 0.0, -6.0)
	enemy_health.TakeDamage(0.01, 1) # lock on to the player
	var climbed := await _wait_until(func():
		player.global_position = ledge_spot
		var flat := Vector2(enemy.global_position.x - ledge_spot.x, enemy.global_position.z - ledge_spot.z).length()
		return enemy.global_position.y > 0.9 and flat < 1.7, 10.0)
	_check(climbed, "offline: evil guy couldn't find its way up to a player on the platform (ended at %s)" % enemy.global_position)

	# Player death and respawn.
	enemy.set_physics_process(false)
	player.RespawnDelay = 0.5
	player_health.TakeDamage(9999.0, 0)
	_check(player_health.Current <= 0.0, "offline: player didn't die")
	_check(await _wait_until(func(): return player_health.Current == player_health.MaxHealth, 3.0), "offline: player didn't respawn")

	# Evil guy death and respawn.
	level.EnemyRespawnDelay = 0.5
	var old_id := enemy.get_instance_id()
	enemy_health.TakeDamage(9999.0, 1)
	_check(await _wait_until(func(): return enemies.get_child_count() == 0, 1.0), "offline: dead evil guy wasn't removed")
	var respawned := await _wait_until(func(): return enemies.get_child_count() == 1 and enemies.get_child(0).get_instance_id() != old_id, 3.0)
	_check(respawned, "offline: evil guy didn't respawn")


# AI v2: senses, memory, search, drop-down links, shoving props. Each scenario gets a fresh evil guy.
enum Mood { CALM, SUSPICIOUS, HUNTING }

func _run_offline_ai(level: Node, player: Node3D) -> void:
	var player_health := player.get_node("Health")
	player_health.MaxHealth = 100000.0 # these scenarios are about behaviour, not surviving it
	player_health.Revive()
	var agent_target := func(e: Node): return (e.get_node("NavigationAgent3D") as NavigationAgent3D).target_position

	# Vision cone: blind to a player standing behind it, notices them once it turns round.
	var e := await _fresh_enemy(level)
	e.WanderSpeed = 0.0 # stand still so the facing we set sticks
	e.rotation = Vector3.ZERO # looking toward -Z
	player.global_position = e.global_position + Vector3(0, 0, 6)
	await _seconds(1.0)
	_check(e.Mood == Mood.CALM, "vision: noticed a player standing behind it (mood %d)" % e.Mood)
	e.rotation = Vector3(0, PI, 0)
	_check(await _wait_until(func(): return e.Mood == Mood.HUNTING, 2.5), "vision: didn't notice a player right in front of it")

	# Memory: duck behind the pillar and it goes to where it last saw us, not to where we are.
	var last_seen := player.global_position
	e.SearchSeconds = 2.0
	e.SearchRadius = 2.0
	player.global_position = Vector3(0, 0, 0.5) # the pillar at z=-2 blocks its view
	var searching := await _wait_until(func(): return e.Mood == Mood.SUSPICIOUS, 3.0)
	var goal: Vector3 = agent_target.call(e)
	_check(searching and goal.distance_to(last_seen) < 1.5, "memory: after losing us it should search our last seen spot %s, went for %s" % [last_seen, goal])

	# ...then gives up if it finds nothing.
	player.global_position = Vector3(10, 0, 10) # far out of sight
	_check(await _wait_until(func(): return e.Mood == Mood.CALM, 6.0), "search: never gave up and calmed down")

	# Hearing: ignores distant noise, investigates loud noise, can't hear walking but hears sprinting.
	e = await _fresh_enemy(level)
	e.WanderSpeed = 0.0
	e.rotation = Vector3.ZERO
	level.EmitNoise(e.global_position + Vector3(8, 0, 0), 4.0)
	await _frames(2)
	_check(e.Mood == Mood.CALM, "hearing: reacted to a noise out of earshot")
	var noise_at: Vector3 = e.global_position + Vector3(6, 0, 2)
	level.EmitNoise(noise_at, 12.0)
	await _frames(2)
	var noise_goal: Vector3 = agent_target.call(e)
	_check(e.Mood == Mood.SUSPICIOUS and noise_goal.distance_to(noise_at) < 1.5, "hearing: didn't come to investigate a loud noise (mood %d, heading %s)" % [e.Mood, noise_goal])

	e = await _fresh_enemy(level)
	e.WanderSpeed = 0.0
	e.rotation = Vector3.ZERO
	# Behind it, outside its view cone, in a lane clear of the crates at z=-3 (a player blocked by
	# a crate isn't moving, so rightly makes no footstep noise).
	player.global_position = e.global_position + Vector3(-3, 0, 6)
	await _frames(2)
	var heard_walking := await _wait_until(func():
		player.global_position += Vector3(3.0 / 60.0, 0, 0)
		return e.Mood != Mood.CALM, 1.5)
	_check(not heard_walking, "hearing: heard a player merely walking 6 m behind it")
	var heard_sprinting := await _wait_until(func():
		player.global_position += Vector3(6.0 / 60.0, 0, 0)
		return e.Mood != Mood.CALM, 1.5)
	_check(heard_sprinting, "hearing: didn't hear a player sprinting 6 m behind it")

	# Drop-down links: from the platform it hops straight down to us instead of using the ramp.
	var links := level.get_node("Navigation").get_children().filter(func(n): return n is NavigationLink3D)
	_check(links.size() > 0, "drop links: none were generated")
	e = await _fresh_enemy(level)
	e.global_position = Vector3(-6, 1.05, -6)
	var below := Vector3(-9.5, 0, -6)
	player.global_position = below
	e.get_node("Health").TakeDamage(0.01, 1) # lock on
	var dropped := await _wait_until(func():
		player.global_position = below
		return e.global_position.y < 0.5 and Vector2(e.global_position.x - below.x, e.global_position.z - below.z).length() < 1.7, 3.0)
	_check(dropped, "drop links: took the long way round instead of dropping off the platform (at %s)" % e.global_position)

	# Shoving: a crate in its way gets pushed aside.
	e = await _fresh_enemy(level)
	var crate := level.get_node("Props/CrateD") as RigidBody3D
	e.global_position = Vector3(8, 0.05, -4)
	crate.global_position = Vector3(8, 0.3, -1)
	crate.linear_velocity = Vector3.ZERO
	var crate_start := crate.global_position
	player.global_position = Vector3(8, 0, 3)
	e.get_node("Health").TakeDamage(0.01, 1)
	var shoved := await _wait_until(func():
		player.global_position = Vector3(8, 0, 3)
		return crate.global_position.distance_to(crate_start) > 0.5, 4.0)
	_check(shoved, "shove: didn't push a crate out of its way")
	_log("AI v2: vision, memory, search, hearing, drop links (%d), shoving all behave" % links.size())


# Audio: the buses exist (the World bus muffles through the mask), and every procedural sound renders
# at a sane level: audible, not clipping. The .wavs land in `out` for listening.
func _check_audio(out: String, verbose: bool) -> void:
	var world := AudioServer.get_bus_index("World")
	_check(world >= 0 and AudioServer.get_bus_index("Mask") >= 0, "audio: World / Mask buses missing (default_bus_layout.tres)")
	_check(world >= 0 and AudioServer.get_bus_effect_count(world) > 0 and AudioServer.get_bus_effect(world, 0) is AudioEffectLowPassFilter, "audio: the World bus has no mask muffle")

	var levels: Dictionary = load("res://audio/AudioPreview.cs").new().Render(out)
	for sound in levels:
		var level: Vector2 = levels[sound]
		if verbose:
			_log("%-18s peak %.2f  rms %.3f" % [sound, level.x, level.y])
		_check(level.x < 0.99, "audio: %s clips (peak %.2f)" % [sound, level.x])
		_check(level.y > 0.01, "audio: %s is near silent (rms %.3f)" % [sound, level.y])
	if verbose:
		_log("wrote %d .wav files to %s" % [levels.size(), out])


# Gas mask filter: drains, chokes you when spent, a spare refills it (once), respawning gives a fresh one.
func _run_offline_filter(level: Node, player: Node3D) -> void:
	level.EnemyRespawnDelay = 999.0 # keep the evil guy out of this one
	for enemy in level.get_node("Enemies").get_children():
		enemy.get_node("Health").TakeDamage(99999.0, 0)
	var health := player.get_node("Health")
	var respirator := player.get_node("Respirator")
	health.MaxHealth = 100.0
	health.Revive()
	respirator.Refill()

	var start: float = respirator.Remaining
	await _seconds(0.5)
	_check(respirator.Remaining < start, "filter: didn't drain while breathing")

	respirator.Remaining = 0.0
	var before: float = health.Current
	_check(await _wait_until(func(): return health.Current < before, 2.0), "filter: a spent filter didn't make the player choke")

	var canister := level.get_node("Props/FilterA") as RigidBody3D
	player.global_position = canister.global_position + Vector3(0, 0, 1.2)
	await _frames(2)
	canister.rpc_id(1, "RequestUse")
	await _frames(2)
	_check(respirator.Remaining > respirator.Capacity - 1.0, "filter: screwing on a spare didn't refill it")
	_check(canister.Consumed and not canister.visible and canister.collision_layer == 0, "filter: the used canister is still there")
	respirator.Remaining = 10.0
	canister.rpc_id(1, "RequestUse")
	await _frames(2)
	_check(respirator.Remaining < 11.0, "filter: a used-up canister refilled the filter again")

	player.RespawnDelay = 0.3
	health.TakeDamage(9999.0, 0)
	var fresh := await _wait_until(func(): return health.Current > 0.0 and respirator.Remaining > respirator.Capacity - 1.0, 3.0)
	_check(fresh, "filter: respawning didn't come with a fresh filter")


# Liquid: a shoved flask sloshes, then settles back to level.
func _run_offline_liquid(level: Node) -> void:
	var flask := level.get_node("Props/FlaskB") as RigidBody3D
	var liquid := flask.find_child("Liquid", true, false)
	await _frames(5)
	flask.apply_central_impulse(Vector3(1.2, 0.8, 0))
	_check(await _wait_until(func(): return liquid.Slosh() > 0.05, 1.0), "liquid: didn't slosh when its flask was shoved")
	_check(await _wait_until(func(): return liquid.Slosh() < 0.01, 6.0), "liquid: never settled back to level")


# Kills whatever's there and waits for the level to spawn a fresh, calm evil guy.
func _fresh_enemy(level: Node) -> Node3D:
	var enemies := level.get_node("Enemies")
	level.EnemyRespawnDelay = 0.2
	for old in enemies.get_children():
		old.get_node("Health").TakeDamage(99999.0, 0)
	await _wait_until(func(): return enemies.get_child_count() == 0, 1.0)
	await _wait_until(func(): return enemies.get_child_count() == 1, 2.0)
	await _frames(2)
	return enemies.get_child(0) as Node3D


func _run_host() -> void:
	_start_main()
	if not _check(_network.Host(PORT) == OK, "host: couldn't open port %d" % PORT):
		return
	if not _check(await _wait_until(func(): return _players() != null and _players().has_node("1"), 5.0), "host: level never loaded"):
		return
	# Take the host's own (idle) player out of play, so the evil guy can only lock on to the client
	# the network checks are about. Otherwise it sometimes spots the host first and ignores the client.
	var own := _players().get_node("1")
	own.RespawnDelay = 999.0
	own.get_node("Health").TakeDamage(99999.0, 0)
	_log("hosting, waiting for a client")

	if not _check(await _wait_until(func(): return not multiplayer.get_peers().is_empty(), 30.0), "host: no client connected"):
		return
	var client_id := multiplayer.get_peers()[0]
	_check(await _wait_until(func(): return _players().has_node(str(client_id)), 5.0), "host: client's player not spawned")
	_log("client %d joined" % client_id)

	_check(await _wait_until(func(): return multiplayer.get_peers().is_empty(), 30.0), "host: client never left")
	await _frames(10)
	_check(not _players().has_node(str(client_id)), "host: client's player not removed after it left")
	_network.Leave()


func _run_client() -> void:
	_start_main()
	if not _check(_network.Join("127.0.0.1", PORT) == OK, "client: couldn't start connecting"):
		return
	var joined := await _wait_until(func(): return _players() != null and _players().get_child_count() >= 2, 15.0)
	if not _check(joined, "client: level / players never replicated"):
		return

	var my_id := multiplayer.get_unique_id()
	var me := _players().get_node_or_null(str(my_id)) as Node3D
	if not _check(me != null, "client: own player missing"):
		return
	_check(me.is_multiplayer_authority(), "client: doesn't own its player")
	_check(_players().has_node("1"), "client: host's player missing")
	_log("joined as %d, players: %s" % [my_id, _players().get_children().map(func(p): return str(p.name))])

	var jar := _level().get_node("Props/JarA") as RigidBody3D
	_check(jar.freeze, "client: props should be frozen (the host simulates them)")

	# Walk up to the jar (facing it) and grab it.
	me.global_position = jar.global_position + Vector3(0, -0.175, 1.5)
	await _seconds(0.5) # let the host see where we are
	jar.rpc_id(1, "RequestGrab")
	if not _check(await _wait_until(func(): return jar.HeldBy == my_id, 3.0), "client: host didn't confirm the grab (HeldBy=%s)" % jar.HeldBy):
		return

	await _seconds(1.0)
	var head := me.get_node("Head") as Node3D
	var hold_point: Vector3 = head.global_position - head.global_basis.z * me.SyncHoldDistance
	var hold_error := jar.global_position.distance_to(hold_point)
	_check(hold_error < 0.5, "client: held jar isn't following the hold point (off by %.2f m)" % hold_error)
	_log("carrying jar at %s (%.2f m from hold point)" % [jar.global_position, hold_error])

	var before := jar.global_position
	jar.rpc_id(1, "RequestThrow")
	_check(await _wait_until(func(): return jar.HeldBy == 0, 3.0), "client: throw didn't release the jar")
	await _seconds(0.4)
	_check(jar.global_position.z < before.z - 1.0, "client: jar didn't fly forward (z %.2f -> %.2f)" % [before.z, jar.global_position.z])
	_log("threw jar: %s -> %s" % [before, jar.global_position])

	await _run_network_filter(me)
	await _run_network_combat(me, head)

	_network.Leave()
	await _frames(5)
	_check(_level() == null, "client: level not cleared after leaving")


# The host breathes for us (drains our filter) and replicates it; a spare canister refills it.
func _run_network_filter(me: Node3D) -> void:
	var respirator := me.get_node("Respirator")
	var capacity: float = respirator.Capacity
	_check(await _wait_until(func(): return respirator.Remaining < capacity, 3.0), "client: filter never drained (host breathing not replicating?)")

	var canister := _level().get_node("Props/FilterB") as Node3D # on the table
	me.global_position = Vector3(canister.global_position.x, 0.05, -4.5) # beside the table
	await _seconds(0.3) # let the host see us next to it
	canister.rpc_id(1, "RequestUse")
	var refilled := await _wait_until(func(): return canister.Consumed and respirator.Remaining > capacity - 1.0, 2.0)
	_check(refilled, "client: a spare filter didn't refill us and vanish (remaining %.1f, consumed %s)" % [respirator.Remaining, canister.Consumed])
	_log("filter: drained to <%d s, spare canister refilled it" % capacity)


# Visual + level check (needs a real window and sound): host a session, save screenshots of the visor
# healthy, hurt, and choking on a spent filter, and log the live bus meters through each phase: the
# in-mask sound (Mask bus) vs. the world (World bus: muffled, with reverb and ambience).
# (Not a recording: Godot's recorder only captures one channel pair on surround setups.)
func _run_capture() -> void:
	var out := _arg("out", OS.get_user_data_dir())
	_start_main()
	if not _check(_network.Host(PORT + 1) == OK, "capture: couldn't host"):
		return
	if not _check(await _wait_until(func(): return _players() != null and _players().has_node("1"), 5.0), "capture: level never loaded"):
		return
	await _frames(5)
	Input.mouse_mode = Input.MOUSE_MODE_VISIBLE # don't hold on to the mouse while capturing

	var player := _players().get_node("1") as Node3D
	var enemy := _level().get_node("Enemies").get_child(0) as Node3D
	enemy.set_physics_process(false) # pose it in view
	enemy.global_position = player.global_position + Vector3(1.2, 0, -5)
	enemy.rotation = Vector3(0, PI, 0)
	var health := player.get_node("Health")
	var ambience := _level().get_node("Ambience")
	const SHATTER := 0 # SoundKind order in audio/SoundBank.cs
	const GROWL := 3

	# Cel shading before / after, same moment.
	var toon := _main.get_node("ToonStyle")
	toon.Enabled = false
	await _seconds(0.3)
	await _screenshot(out.path_join("toon_off.png"))
	toon.Enabled = true
	await _seconds(0.3)
	await _screenshot(out.path_join("toon_on.png"))

	# Liquid close-up: stand at the table's end facing the flask, zoom in, then shove it sideways and
	# catch it mid-slosh.
	var start := player.global_transform
	var head := player.get_node("Head") as Node3D
	var camera := head.get_node("Camera3D") as Camera3D
	var flask := _level().get_node("Props/FlaskA") as RigidBody3D
	player.global_position = Vector3(4.35, 0.05, flask.global_position.z)
	player.rotation = Vector3(0, -PI / 2.0, 0) # facing +x, along the table
	head.rotation = Vector3(-0.5, 0, 0)
	camera.fov = 40.0
	await _seconds(0.6)
	await _screenshot(out.path_join("liquid_still.png"))
	flask.apply_central_impulse(Vector3(0, 0, 0.3))
	await _seconds(0.18)
	await _screenshot(out.path_join("liquid_slosh.png"))
	camera.fov = 80.0
	player.global_transform = start
	head.rotation = Vector3.ZERO

	# The acid flask in hand: full, then refilling a third of the way through its recharge.
	player.SelectedSlot = 1
	await _seconds(0.5)
	await _screenshot(out.path_join("hand_full.png"))
	player.UseSelectedItem()
	await _seconds(5.0)
	await _screenshot(out.path_join("hand_refilling.png"))
	player.SelectedSlot = 0

	await _meter("calm: breathing + room tone", 1.5)
	await _screenshot(out.path_join("visor_1_healthy.png"))
	ambience.PlayDistantEvent()
	await _meter("a distant ambience event", 1.5)
	_level().EmitSound(player.global_position + Vector3(2, 1, -3), 14.0, SHATTER)
	_level().EmitSound(enemy.global_position + Vector3.UP * 2.0, 12.0, GROWL)
	await _meter("shatter + growl nearby", 1.5)
	health.TakeDamage(45.0, 0) # cracks: the mask muffles less from here
	await _meter("hurt (cracked mask)", 0.6)
	await _screenshot(out.path_join("visor_2_hurt.png"))
	health.TakeDamage(35.0, 0)
	player.get_node("Respirator").Remaining = 0.0
	await _meter("choking + heartbeat", 1.2)
	await _screenshot(out.path_join("visor_3_choking.png"))
	_network.Leave()


# Loudest peak (dB, across all channels) on each bus over a stretch of real playback.
func _meter(label: String, seconds: float) -> void:
	var loudest := {"Master": -200.0, "World": -200.0, "Mask": -200.0}
	var until := Time.get_ticks_msec() + int(seconds * 1000)
	while Time.get_ticks_msec() < until:
		await get_tree().process_frame
		for bus_name in loudest:
			var bus := AudioServer.get_bus_index(bus_name)
			for ch in AudioServer.get_bus_channels(bus):
				loudest[bus_name] = max(loudest[bus_name], AudioServer.get_bus_peak_volume_left_db(bus, ch), AudioServer.get_bus_peak_volume_right_db(bus, ch))
	_log("%-30s Mask %6.1f dB   World %6.1f dB   Master %6.1f dB" % [label, loudest["Mask"], loudest["World"], loudest["Master"]])


func _screenshot(path: String) -> void:
	await RenderingServer.frame_post_draw
	get_viewport().get_texture().get_image().save_png(path)
	_log("saved " + path)


# The host runs the evil guy and all damage. The client gets hit, then hits back.
func _run_network_combat(me: Node3D, head: Node3D) -> void:
	var enemies := _level().get_node("Enemies")
	if not _check(await _wait_until(func(): return enemies.get_child_count() > 0, 5.0), "client: evil guy never replicated"):
		return
	var enemy := enemies.get_child(0) as Node3D
	var enemy_health := enemy.get_node("Health")
	var my_health := me.get_node("Health")

	# Stay next to it (room-centre side, same level) until it swings: proves the host's AI picks us
	# and its damage replicates back to us.
	var got_hit := await _wait_until(func():
		if me.global_position.distance_to(enemy.global_position) > 1.6:
			var away := -enemy.global_position * Vector3(1, 0, 1)
			me.global_position = enemy.global_position + (away.normalized() if away.length() > 1.0 else Vector3.BACK) * 1.2
		return my_health.Current < my_health.MaxHealth, 8.0)
	if not _check(got_hit, "client: evil guy never hurt us (host damage didn't replicate?)"):
		return
	_log("evil guy hit us: %.0f / %.0f" % [my_health.Current, my_health.MaxHealth])

	# It stands still recovering from the swing, so a point-blank throw must land. (At range, a
	# path-following target can legitimately dodge, which made this flaky.)
	var start_health: float = enemy_health.Current
	var eye := head.global_position
	me.rpc_id(1, "RequestThrowItem", 1, eye, (enemy.global_position + Vector3.UP * 1.2 - eye).normalized())
	_check(await _wait_until(func(): return enemy_health.Current < start_health, 2.0), "client: point-blank acid flask didn't hurt the evil guy")
	_log("flask hit: evil guy at %.0f / %.0f" % [enemy_health.Current, enemy_health.MaxHealth])


func _start_main() -> void:
	_main = (load("res://core/main.tscn") as PackedScene).instantiate()
	add_child(_main)


func _level() -> Node:
	var root := _main.get_node("Level")
	return root.get_child(0) if root.get_child_count() > 0 else null


func _players() -> Node:
	var level := _level()
	return level.get_node("Players") if level else null


func _wait_until(condition: Callable, timeout: float) -> bool:
	var deadline := Time.get_ticks_msec() + int(timeout * 1000)
	while not condition.call():
		if Time.get_ticks_msec() > deadline:
			return false
		await get_tree().physics_frame
	return true


func _seconds(duration: float) -> void:
	await get_tree().create_timer(duration).timeout


func _frames(count: int) -> void:
	for i in count:
		await get_tree().physics_frame


func _check(condition: bool, message: String) -> bool:
	if not condition:
		_fail(message)
	return condition


func _fail(message: String) -> void:
	_failures.append(message)


func _log(message: String) -> void:
	print("[%s] %s" % [_role, message])


func _arg(key: String, fallback: String) -> String:
	for arg in OS.get_cmdline_user_args():
		if arg.begins_with("--%s=" % key):
			return arg.get_slice("=", 1)
	return fallback


func _finish() -> void:
	if _finished:
		return
	_finished = true
	if _failures.is_empty():
		print("SMOKE PASS (%s)" % _role)
		get_tree().quit(0)
	else:
		for failure in _failures:
			printerr("SMOKE FAIL (%s): %s" % [_role, failure])
		get_tree().quit(1)
