extends Node

## Headless smoke test. Run from the game/ folder:
##   godot --headless res://tests/smoke_test.tscn -- --role=scenes
##   godot --headless res://tests/smoke_test.tscn -- --role=network --clients=3 --ping=150 --jitter=20 --loss=2
## The network role hosts and starts its own clients (the last joins mid-run), optionally behind a
## fake bad connection, and plays the sandbox and a whole maze run (see net_suite.gd). Client logs
## go to --out. By hand, in two terminals: --role=host (first), then --role=client.
## Prints "SMOKE PASS" or "SMOKE FAIL: ..." lines and exits with 0 / 1.
## Visual check (needs a real window, so no --headless): saves visor screenshots to --out.
##   godot --resolution 1280x720 res://tests/smoke_test.tscn -- --role=capture --out=C:/some/folder
## Listening check: renders every procedural sound to .wav in --out (and checks levels).
##   godot --headless res://tests/smoke_test.tscn -- --role=audio --out=C:/some/folder

const PORT := 17777
const TIMEOUT := 150.0 # the offline suite takes about 70 s on the dev machine
const SCENES := [
	"res://core/main.tscn",
	"res://levels/test_level.tscn",
	"res://maze/test_chamber.tscn",
	"res://maze/chamber_crates.tscn",
	"res://maze/chamber_ledge.tscn",
	"res://maze/pressure_plate.tscn",
	"res://maze/chamber_door.tscn",
	"res://maze/chamber_button.tscn",
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
	"res://ui/settings_menu.tscn",
]

var _role := ""
var _failures := PackedStringArray()
var _finished := false
var _main: Node
var _network: Node
var _errors := ErrorCatcher.new()
var _proxy # LagProxy, when a client plays over a fake internet connection


# A script error or C# exception only aborts the function it happens in, so the run carries on and
# could still pass. This collects every error Godot logs, and _finish fails the run on any of them.
class ErrorCatcher extends Logger:
	var messages := PackedStringArray()
	var _mutex := Mutex.new()

	func _log_error(function: String, file: String, line: int, code: String, rationale: String, _editor_notify: bool, error_type: int, _backtraces: Array[ScriptBacktrace]) -> void:
		if error_type == ERROR_TYPE_WARNING:
			return
		_mutex.lock()
		messages.append("%s (%s:%d in %s)" % [rationale if rationale else code, file, line, function])
		_mutex.unlock()

	func _log_message(_message: String, _error: bool) -> void:
		pass


func _ready() -> void:
	OS.add_logger(_errors)
	# Headless Godot runs uncapped, so every test process spins a core flat out. With a host and
	# several clients on one machine they starved each other: a client could go a second without a
	# frame, so the host didn't hear where it was. Players have v-sync; tests get a cap.
	Engine.max_fps = 120
	_role = _arg("role", "scenes")
	_network = get_node("/root/Network")
	# A profile of the test's own (never the player's real one), fresh every run.
	var profile_path := "user://smoke_profile_%s_%s.json" % [_role, _arg("index", "0")]
	DirAccess.remove_absolute(ProjectSettings.globalize_path(profile_path))
	get_node("/root/ProfileStore").Path = profile_path
	var timeout := TIMEOUT if _role in ["scenes", "capture", "audio"] else 600.0
	get_tree().create_timer(float(_arg("timeout", str(timeout)))).timeout.connect(func(): _fail("timed out"); _finish())

	match _role:
		"scenes": await _run_scenes()
		"host", "network", "client": await _run_network()
		"generator": await _run_offline_generated(range(1, int(_arg("seeds", "10")) + 1) if _arg("seed", "") == "" else [int(_arg("seed", "1"))])
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
	_check_settings()

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
		await _run_offline_pack(level, player)
		await _run_offline_filter(level, player)
		await _run_offline_liquid(level)
	level.queue_free()
	await _frames(2)
	await _run_offline_chamber()
	await _run_offline_chambers()
	await _run_offline_ambush()
	await _run_offline_generated([1])
	await _run_offline_run()


# Acid flask vs. evil guy, evil guy vs. player, death and respawn, all offline as the host.
func _run_offline_combat(level: Node, player: Node3D) -> void:
	var flask = load("res://items/acid_flask.tres")
	_check(flask != null and flask.Cooldown == 5.0, "acid_flask.tres didn't load as a 5 s throwable")
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
	_check(enemy_health.Current == after_hit, "offline: flask ignored its recharge")

	# The flask in hand empties when thrown, then its acid refills over the recharge.
	var hand_liquid := player.get_node("Head/Camera3D/HandItem").find_child("Liquid", true, false)
	player.SelectedSlot = 1
	await _frames(2)
	var full_fill: float = hand_liquid.Fill
	player.UseSelectedItem() # the host refuses it (still recharging), but our own flask empties
	await _frames(2)
	_check(full_fill > 0.3 and hand_liquid.Fill < 0.02, "offline: thrown flask didn't empty in hand (%.2f -> %.2f)" % [full_fill, hand_liquid.Fill])
	await _seconds(1.5)
	var expected: float = full_fill * 1.5 / flask.Cooldown
	_check(absf(hand_liquid.Fill - expected) < 0.06, "offline: flask in hand isn't refilling with the recharge (%.2f, expected %.2f)" % [hand_liquid.Fill, expected])
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
	_check(level.TimesLostNear(last_seen) == 1, "habits: losing us didn't get remembered (%d)" % level.TimesLostNear(last_seen))

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


# AI v3, the pack and its habits. One evil guy spots us and growls: a second one that heard the growl
# (but can't see us) comes for *us*, not for the one that growled. And a spot where players keep
# getting away is the first place it patrols.
func _run_offline_pack(level: Node, player: Node3D) -> void:
	var agent_target := func(e: Node): return (e.get_node("NavigationAgent3D") as NavigationAgent3D).target_position
	var a := await _fresh_enemy(level)
	a.WanderSpeed = 0.0
	a.rotation = Vector3(0, PI, 0) # facing +z, into the room
	var b := (load("res://enemies/enemy.tscn") as PackedScene).instantiate() as Node3D
	b.position = Vector3(8, 0.05, -8)
	b.rotation = Vector3(0, -PI / 2.0, 0) # facing the east wall, away from us
	b.WanderSpeed = 0.0
	level.get_node("Enemies").add_child(b, true) # a readable name, so the spawner can replicate it
	await _frames(3)
	player.global_position = a.global_position + Vector3(0, 0, 5) # in front of the first one
	_check(await _wait_until(func(): return a.Mood == Mood.HUNTING, 3.0), "pack: the first evil guy didn't spot us")
	var heard := await _wait_until(func(): return b.Mood == Mood.SUSPICIOUS, 1.0)
	var goal: Vector3 = agent_target.call(b)
	_check(heard and goal.distance_to(player.global_position) < 1.5, "pack: the second evil guy should head for us %s after the growl, went for %s (the growler is at %s)" % [player.global_position, goal, a.global_position])
	b.queue_free()

	# Habits: players got away twice near the table; once it's calm again, it patrols there first.
	var spot := Vector3(6, 0, -3.5)
	level.RememberLostAt(spot)
	level.RememberLostAt(spot + Vector3(0.5, 0, 0))
	var e := await _fresh_enemy(level)
	player.global_position = Vector3(-10, 0, 10) # far away, out of sight
	var patrolled := await _wait_until(func(): return (agent_target.call(e) as Vector3).distance_to(spot) < 1.5, 6.0)
	_check(patrolled, "habits: didn't go and check the spot where players keep getting away (went for %s)" % agent_target.call(e))
	_log("AI v3: the pack converges on a growl's lead, and it patrols where players got away")


# A shut door between it and something it heard: it waits by the door (listening) instead of
# wandering off, and goes through the moment the door opens.
func _run_offline_ambush() -> void:
	var level := (load("res://maze/test_chamber.tscn") as PackedScene).instantiate()
	level.EnemyReleaseDelay = 0.3
	add_child(level)
	var enemies := level.get_node("Enemies")
	if not _check(await _wait_until(func(): return enemies.get_child_count() == 1 and level.get_node("Navigation").navigation_mesh.get_polygon_count() > 0, 4.0), "ambush: no evil guy or navmesh"):
		level.queue_free()
		return
	await _seconds(0.3)
	var e := enemies.get_child(0) as Node3D
	var player := level.get_node("Players/1") as Node3D
	var door := level.get_node("Navigation/Geometry/ClosetDoor") as Node3D
	player.global_position = Vector3(-7.9, 0.05, 1.0) # in the closet, door shut
	e.global_position = Vector3(-3, 0.05, 1.5)
	e.WanderSpeed = 0.0
	await _frames(3)
	level.EmitNoise(player.global_position, 14.0) # something in there made a noise
	await _seconds(4.0)
	var waiting: bool = e.Mood == Mood.SUSPICIOUS and e.global_position.distance_to(door.global_position - Vector3(0, 1.5, 0)) < 2.5
	_check(waiting, "ambush: should be waiting by the closet door, is at %s (mood %d)" % [e.global_position, e.Mood])
	await _seconds(3.0)
	_check(e.global_position.distance_to(door.global_position - Vector3(0, 1.5, 0)) < 2.5, "ambush: gave up waiting by the door too soon (at %s)" % e.global_position)

	level.get_node("Navigation/Geometry/ButtonInside").RequestPress() # we open the door from inside
	var came_in := await _wait_until(func(): return e.global_position.x < -6.3 or e.Mood == Mood.HUNTING, 6.0)
	_check(came_in, "ambush: didn't come through when the door opened (at %s, mood %d)" % [e.global_position, e.Mood])
	_log("AI v3: waits at a shut door, comes through when it opens")
	level.queue_free()
	await _frames(2)


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
		_check(level.y > 0.001, "audio: %s is near silent (rms %.3f)" % [sound, level.y])
	if verbose:
		_log("wrote %d .wav files to %s" % [levels.size(), out])


# Settings: a save / load round trip keeps the values, and applying them sets the bus volumes.
func _check_settings() -> void:
	var settings := get_node("/root/Settings")
	var path := OS.get_user_data_dir().path_join("smoke_settings.cfg")
	settings.WorldVolume = 0.5
	settings.MouseSensitivity = 1.7
	settings.Save(path)
	settings.WorldVolume = 1.0
	settings.MouseSensitivity = 1.0
	settings.Load(path)
	_check(is_equal_approx(settings.WorldVolume, 0.5) and is_equal_approx(settings.MouseSensitivity, 1.7), "settings: saving and loading lost values")
	settings.Apply()
	var world_db := AudioServer.get_bus_volume_db(AudioServer.get_bus_index("World"))
	_check(absf(world_db - linear_to_db(0.5)) < 0.01, "settings: the world volume didn't reach the World bus (%.1f dB)" % world_db)
	settings.WorldVolume = 1.0
	settings.MouseSensitivity = 1.0
	settings.Apply()
	DirAccess.remove_absolute(path)


# Gas mask filter: drains, low gas hurts your mind (not your body), a spare refills it (once), respawning gives a fresh one.
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
	_check(await _wait_until(func(): return health.Current < before, 2.0), "filter: a spent filter didn't hurt the player")
	_check(health.Mental > 0.0 and is_equal_approx(health.Physical, health.MaxHealth), "filter: withdrawal should be mental damage, not physical")
	var mental: float = health.Mental
	health.TakeDamage(10.0, 0)
	_check(is_equal_approx(health.Physical, health.MaxHealth - 10.0) and is_equal_approx(health.Mental, mental), "filter: a physical hit got counted as mental")
	health.TakeMentalDamage(9999.0)
	_check(is_equal_approx(health.Current, health.MentalFloor) and is_equal_approx(health.MentalFraction, 1.0), "filter: mental damage should stop at the floor, not kill (health %.1f)" % health.Current)

	var canister := level.get_node("Props/FilterA") as RigidBody3D
	player.global_position = canister.global_position + Vector3(0, 0, 1.2)
	await _frames(2)
	canister.rpc_id(1, "RequestUse")
	await _frames(2)
	_check(respirator.Remaining > respirator.Capacity - 1.0, "filter: screwing on a spare didn't refill it")
	# A fresh filter stops the withdrawal but doesn't heal it (healing comes later).
	var hurt: float = health.Current
	var hurt_mind: float = health.Mental
	await _seconds(1.5)
	_check(is_equal_approx(health.Current, hurt) and is_equal_approx(health.Mental, hurt_mind), "filter: a fresh filter should stop withdrawal without healing (health %.1f -> %.1f)" % [hurt, health.Current])
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


# The maze chamber: you spawn at the start, and walking into the exit passes the test.
func _run_offline_chamber() -> void:
	var level := (load("res://maze/test_chamber.tscn") as PackedScene).instantiate()
	add_child(level)
	await _seconds(1.0)
	var player := level.get_node_or_null("Players/1") as Node3D
	var exit := level.get_node("Exit")
	if _check(player != null, "chamber: didn't spawn the local player"):
		_check(player.is_on_floor(), "chamber: player isn't standing on the floor")
		_check(player.global_position.distance_to(exit.global_position) > 10.0, "chamber: player spawned near the exit")
		_check(not exit.IsComplete, "chamber: test passed before anyone reached the exit")
		await _run_offline_plate(level, player)
		await _run_offline_button(level, player)
		player.global_position = exit.global_position - Vector3(0, 1.4, 0)
		_check(await _wait_until(func(): return exit.IsComplete, 2.0), "chamber: standing in the exit didn't pass the test")
	level.queue_free()
	await _frames(2)

	# The evil guy is the chamber's "variable": not there at the start, released later.
	level = (load("res://maze/test_chamber.tscn") as PackedScene).instantiate()
	level.EnemyReleaseDelay = 1.0
	add_child(level)
	await _seconds(0.5)
	_check(level.get_node("Enemies").get_child_count() == 0, "chamber: the evil guy was there from the start")
	_check(await _wait_until(func(): return level.get_node("Enemies").get_child_count() == 1, 2.0), "chamber: the evil guy was never released")
	# Left alone he wanders, and only ever to spots he can walk to (not the roof over the chamber).
	var agent := level.get_node("Enemies").get_child(0).get_node("NavigationAgent3D") as NavigationAgent3D
	var heading_nowhere := await _wait_until(func(): return not _can_path(level, agent.get_parent().global_position, agent.target_position), 6.0)
	_check(not heading_nowhere, "chamber: the evil guy picked a spot he can't reach (%s)" % agent.target_position)
	level.queue_free()
	await _frames(2)


# Every chamber in the maze run: you spawn in its start corridor, the evil guy could reach you there,
# and its exit is shut until the puzzle is solved. The newer chambers get solved here too.
func _run_offline_chambers() -> void:
	var main := (load("res://core/main.tscn") as PackedScene).instantiate()
	var chambers: Array = main.get_node("Run").Chambers
	main.free()
	_check(chambers.size() >= 3, "chambers: a maze run has only %d chambers to pick from" % chambers.size())
	for packed: PackedScene in chambers:
		var chamber := packed.resource_path.get_file().get_basename()
		var level := packed.instantiate()
		level.EnemyReleaseDelay = 999.0 # keep him out of the puzzle checks
		add_child(level)
		await _seconds(1.0)
		var player := level.get_node_or_null("Players/1") as Node3D
		var exit := level.get_node("Exit") as Node3D
		var exit_floor := exit.global_position - Vector3(0, 1.5, 0)
		if _check(player != null, "%s: didn't spawn the local player" % chamber):
			_check(player.is_on_floor(), "%s: player isn't standing on the floor" % chamber)
			_check(player.global_position.distance_to(exit.global_position) > 10.0, "%s: player spawned near the exit" % chamber)
			_check(not exit.IsComplete, "%s: passed before anyone reached the exit" % chamber)
			_check(not _can_path(level, player.global_position, exit_floor), "%s: the exit is open from the start" % chamber)
			var lair := (level.get_node("EnemySpawnPoints").get_child(0) as Node3D).global_position
			_check(_can_path(level, lair, player.global_position), "%s: the evil guy couldn't reach the start corridor" % chamber)
			for prop in level.get_node("Props").get_children():
				_check(prop.global_position.y > -0.5, "%s: %s fell through the floor" % [chamber, prop.name])
			match chamber:
				"chamber_crates": await _solve_crates(level, exit_floor)
				"chamber_ledge": await _solve_ledge(level, player, exit_floor)
		level.queue_free()
		await _frames(2)


# Generated chambers: for each seed at easy, medium and hard, the room builds; you spawn in the start
# corridor; the exit is shut; the evil guy can reach you; everything its plan needs is reachable; and
# following the plan opens the exit. The same seed builds the same room twice.
func _run_offline_generated(seeds: Array) -> void:
	_start_main()
	var run := _main.get_node("Run")
	var first: Node = run.BuildGenerated(7, 0.8)
	var again: Node = run.BuildGenerated(7, 0.8)
	var layout := func(level: Node): return level.get_node("Props").get_children().map(func(p): return [str(p.name), p.position])
	_check(layout.call(first) == layout.call(again) and first.get_meta("plan") == again.get_meta("plan"), "generated: seed 7 built two different rooms")
	first.free()
	again.free()
	var solved := 0
	for difficulty in ([0.0, 0.5, 1.0] if _arg("difficulty", "") == "" else [float(_arg("difficulty", "0"))]):
		for seed in seeds:
			var level: Node3D = run.BuildGenerated(seed, difficulty)
			level.EnemyReleaseDelay = 999.0
			add_child(level)
			if await _solve_generated(level):
				solved += 1
			level.queue_free()
			await _frames(2)
	_log("generated: solved %d of %d chambers" % [solved, seeds.size() * 3])
	_main.queue_free()
	_main = null
	await _frames(2)


func _solve_generated(level: Node3D) -> bool:
	var plan: Dictionary = level.get_meta("plan")
	var name := "generated %d at %.1f %s" % [plan["seed"], plan["difficulty"], plan["pieces"]]
	var failures := _failures.size()
	var nav := level.get_node("Navigation") as NavigationRegion3D
	if not _check(await _wait_until(func(): return nav.navigation_mesh.get_polygon_count() > 0 and level.has_node("Players/1"), 5.0), "%s: no navmesh or player" % name):
		return false
	await _seconds(0.8)
	var player := level.get_node("Players/1") as Node3D
	var start := player.global_position
	var exit_floor := (level.get_node("Exit") as Node3D).global_position - Vector3(0, 1.5, 0)
	var door := level.get_node("Navigation/Geometry/ExitDoor") as Node3D
	_check(player.is_on_floor() and start.distance_to(exit_floor) > 8.0, "%s: didn't spawn on the floor of the start corridor (%s)" % [name, start])
	_check(not _can_path(level, start, exit_floor), "%s: the exit is open from the start" % name)
	for lair in level.get_node("EnemySpawnPoints").get_children():
		_check(_can_path(level, lair.global_position, start), "%s: the evil guy at %s can't reach the start" % [name, lair.global_position])
	for prop in level.get_node("Props").get_children():
		_check(prop.global_position.y > -0.5, "%s: %s fell through the floor" % [name, prop.name])

	if plan.has("closet"):
		var closet: Dictionary = plan["closet"]
		var button := level.get_node(closet["button"]) as Node3D
		player.global_position = button.global_position + Vector3(0, 0.05, 1.0)
		await _frames(2)
		button.RequestPress()
		var case := level.get_node(closet["holds"]) as Node3D
		var opened := await _wait_until(func(): return _can_path(level, start, case.global_position * Vector3(1, 0, 1)), 4.0)
		_check(opened, "%s: pressing the closet button didn't let anyone reach the case" % name)

	for entry in plan["plates"]:
		var plate := level.get_node(entry["plate"]) as Node3D
		var props: Array = entry["props"]
		var spots := _plate_spots(level, props)
		for i in props.size():
			var prop := level.get_node(props[i]) as RigidBody3D
			if not plan.has("closet") or props[i] != plan["closet"]["holds"]:
				_check(_can_path(level, start, prop.global_position * Vector3(1, 0, 1)), "%s: can't walk to %s at %s" % [name, props[i], prop.global_position])
			prop.global_position = plate.global_position + spots[i]
			prop.linear_velocity = Vector3.ZERO
			prop.angular_velocity = Vector3.ZERO
			prop.rotation = Vector3.ZERO
		_check(await _wait_until(func(): return plate.Pressed, 3.0), "%s: its props (%s) didn't hold %s down" % [name, props, entry["plate"]])

	if plan.has("ledge"):
		var ledge: Dictionary = plan["ledge"]
		var button := level.get_node(ledge["button"]) as Node3D
		var from: Vector3 = ledge["throw_from"] + Vector3(0, 1.7, 0)
		_check(_can_path(level, start, ledge["throw_from"]), "%s: can't walk to the spot to throw from" % name)
		var jar := level.get_node("Props/JarA") as RigidBody3D
		var target := (button.get_node("HitZone") as Node3D).global_position
		var hit := false
		for attempt in 5:
			var aim := target + (target - from).slide(Vector3.UP).normalized() * 0.25 * attempt
			jar.global_position = from
			jar.angular_velocity = Vector3.ZERO
			jar.linear_velocity = _lob(from, aim, 12.0)
			hit = await _wait_until(func(): return button.Pressed, 2.0)
			if hit:
				break
		_check(hit, "%s: a jar thrown from %s never hit the ledge button" % [name, ledge["throw_from"]])

	var open := await _wait_until(func(): return door.IsOpen and _can_path(level, start, exit_floor), 4.0)
	_check(open, "%s: following the plan didn't open the way out (door open %s)" % [name, door.IsOpen])
	if _failures.size() > failures:
		var verts: PackedVector3Array = nav.navigation_mesh.get_vertices()
		var box := AABB(verts[0], Vector3.ZERO) if not verts.is_empty() else AABB()
		for v in verts:
			box = box.expand(v)
		_log("%s: navmesh %d polygons over %s" % [name, nav.navigation_mesh.get_polygon_count(), box])
		var map := level.get_world_3d().navigation_map
		_log("  map iteration %d, regions %s, ours %s" % [NavigationServer3D.map_get_iteration_id(map), NavigationServer3D.map_get_regions(map), nav.get_rid()])
		_log("  start %s (navmesh %s); path to the room's middle: %s" % [start, NavigationServer3D.map_get_closest_point(map, start), NavigationServer3D.map_get_path(map, start, Vector3(0, 0, 0), true)])
		_log("%s: plan %s" % [name, plan])
		_log("  props: %s" % [level.get_node("Props").get_children().map(func(p): return "%s %s" % [p.name, p.global_position.snapped(Vector3.ONE * 0.1)])])
		_log("  plates: %s" % [level.get_children().filter(func(n): return n is Area3D and str(n.name).begins_with("Plate")).map(func(p): return "%s %s pressed %s" % [p.name, p.global_position, p.Pressed])])
	return _failures.size() == failures


# Where each prop goes on its plate: the case and a crate side by side, or crates in a grid.
func _plate_spots(level: Node, props: Array) -> Array:
	var cases := props.filter(func(p): return (level.get_node(p) as RigidBody3D).mass > 20.0)
	if not cases.is_empty():
		return props.map(func(p): return Vector3(-0.28, 0.55, 0) if p == cases[0] else Vector3(0.52, 0.35, 0))
	var grid := [Vector3(-0.32, 0.35, -0.32), Vector3(0.32, 0.35, -0.32), Vector3(-0.32, 0.35, 0.32), Vector3(0.32, 0.35, 0.32)]
	if props.size() > 4:
		grid = [Vector3(-0.62, 0.35, -0.32), Vector3(0, 0.35, -0.32), Vector3(0.62, 0.35, -0.32), Vector3(-0.62, 0.35, 0.32), Vector3(0, 0.35, 0.32), Vector3(0.62, 0.35, 0.32)]
	return grid.slice(0, props.size())


# The easy chamber: a jar or three crates don't hold the plate, four crates do.
func _solve_crates(level: Node, exit_floor: Vector3) -> void:
	var plate := level.get_node("Plate") as Node3D
	var door := level.get_node("Navigation/Geometry/ExitDoor") as Node3D
	var jar := level.get_node("Props/JarA") as RigidBody3D
	jar.global_position = plate.global_position + Vector3(0, 0.3, 0)
	await _seconds(0.5)
	_check(not plate.Pressed, "crates: a jar held the plate down")
	jar.global_position = plate.global_position + Vector3(2.5, 0.3, 0)
	var corners := [Vector3(-0.35, 0.35, -0.35), Vector3(0.35, 0.35, -0.35), Vector3(-0.35, 0.35, 0.35), Vector3(0.35, 0.35, 0.35)]
	for i in 4:
		var crate := level.get_node("Props").get_child(i) as RigidBody3D
		crate.global_position = plate.global_position + corners[i]
		crate.linear_velocity = Vector3.ZERO
		if i == 2:
			await _seconds(0.5)
			_check(not plate.Pressed, "crates: three crates held the plate down")
	_check(await _wait_until(func(): return plate.Pressed and door.IsOpen, 2.0), "crates: four crates on the plate didn't open the exit")
	_check(await _wait_until(func(): return _can_path(level, Vector3.ZERO, exit_floor), 3.0), "crates: no path through the open exit")


# The hard chamber: the exit needs the plate held *and* the button up on the ledge, which is out of
# reach from the floor. A jar lobbed from by the exit door (a real throw's speed) presses it.
func _solve_ledge(level: Node, player: Node3D, exit_floor: Vector3) -> void:
	var plate := level.get_node("Plate") as Node3D
	var door := level.get_node("Navigation/Geometry/ExitDoor") as Node3D
	var button := level.get_node("Navigation/Geometry/LedgeButton") as Node3D
	var case := level.get_node("Props/CaseA") as RigidBody3D
	var jar := level.get_node("Props/JarA") as RigidBody3D

	var start := player.global_position
	player.global_position = Vector3(4.1, 0.05, 0) # at the foot of the ledge, under the button
	await _frames(2)
	button.RequestPress()
	await _frames(2)
	_check(not button.Pressed, "ledge: pressed the button from the floor")
	player.global_position = start

	case.global_position = plate.global_position + Vector3(0, 0.7, 0)
	_check(await _wait_until(func(): return plate.Pressed, 2.0), "ledge: the case didn't press the plate")
	await _seconds(0.5)
	_check(not door.IsOpen, "ledge: the plate alone opened the exit")

	var from := Vector3(0, 1.7, -5.5)
	var target := (button.get_node("HitZone") as Node3D).global_position
	var hit := false
	for attempt in 4: # aim a little long each time, like a player would (air drag)
		var aim := target + (target - from).slide(Vector3.UP).normalized() * 0.3 * attempt
		jar.global_position = from
		jar.angular_velocity = Vector3.ZERO
		jar.linear_velocity = _lob(from, aim, 12.0)
		hit = await _wait_until(func(): return button.Pressed, 2.0)
		if hit:
			_log("ledge: a lobbed jar hit the button on throw %d" % (attempt + 1))
			break
	if not _check(hit, "ledge: a jar lobbed from the exit door never hit the button"):
		return
	_check(await _wait_until(func(): return door.IsOpen, 0.5), "ledge: plate + button didn't open the exit")
	_check(await _wait_until(func(): return _can_path(level, Vector3(0, 0, -5), exit_floor), 3.0), "ledge: no path through the open exit")
	_check(await _wait_until(func(): return not door.IsOpen, 6.0), "ledge: the exit stayed open after the button popped up")


# Launch velocity at this speed that lands on the target (the flatter of the two arcs, no drag).
func _lob(from: Vector3, to: Vector3, speed: float) -> Vector3:
	var flat := (to - from).slide(Vector3.UP)
	var x := flat.length()
	var y := to.y - from.y
	var g: float = ProjectSettings.get_setting("physics/3d/default_gravity")
	var v2 := speed * speed
	var disc := v2 * v2 - g * (g * x * x + 2.0 * y * v2)
	if disc < 0.0:
		return Vector3.ZERO
	var angle := atan((v2 - sqrt(disc)) / (g * x))
	return flat.normalized() * speed * cos(angle) + Vector3.UP * speed * sin(angle)


# Whether the host's navmesh has a path between two floor points (the end lands on the target).
func _can_path(level: Node3D, from: Vector3, to: Vector3) -> bool:
	var path := NavigationServer3D.map_get_path(level.get_world_3d().navigation_map, from, to, true)
	return not path.is_empty() and path[path.size() - 1].distance_to(to) < 0.5


# The exit door stays open only while the plate is weighed down: a player can, and so can the heavy case.
func _run_offline_plate(level: Node, player: Node3D) -> void:
	var exit_floor := Vector3(0, 0, -8.5)
	var plate := level.get_node("Plate") as Node3D
	var door := level.get_node("Navigation/Geometry/ExitDoor") as Node3D
	var case := level.get_node("Props/CaseA") as RigidBody3D
	var closed := door.position
	_check(not plate.Pressed and not door.IsOpen, "plate: pressed / door open with nothing on it")
	_check(not _can_path(level, Vector3.ZERO, exit_floor), "plate: the evil guy can path through the shut exit door")
	var start := player.global_position
	player.global_position = plate.global_position + Vector3(0, 0.1, 0)
	_check(await _wait_until(func(): return plate.Pressed and door.IsOpen, 1.0), "plate: standing on it didn't open the door")
	player.global_position = start
	_check(await _wait_until(func(): return not plate.Pressed and not door.IsOpen, 1.0), "plate: stepping off didn't close the door")
	case.global_position = plate.global_position + Vector3(0, 0.7, 0)
	_check(await _wait_until(func(): return plate.Pressed, 2.0), "plate: the heavy case didn't press it")
	_check(await _wait_until(func(): return door.position.distance_to(closed) > 3.0, 2.0), "plate: door didn't slide open (moved %.2f m)" % door.position.distance_to(closed))
	_check(await _wait_until(func(): return _can_path(level, Vector3.ZERO, exit_floor), 1.0), "plate: the evil guy can't path through the open exit door")


# The closet button: [E] within reach opens the door for a while, a thrown crate presses it too, and a
# crate in the doorway stops the door closing.
func _run_offline_button(level: Node, player: Node3D) -> void:
	var button := level.get_node("Navigation/Geometry/ButtonOutside") as Node3D
	var door := level.get_node("Navigation/Geometry/ClosetDoor") as Node3D
	var crate := level.get_node("Props/CrateA") as RigidBody3D
	var closed := door.position
	button.OpenSeconds = 1.5
	var start := player.global_position

	player.global_position = button.global_position + Vector3(0, 0.05, 5.0)
	await _frames(2)
	button.RequestPress()
	await _frames(2)
	_check(not button.Pressed, "button: pressed from 5 m away")

	player.global_position = button.global_position + Vector3(0, 0.05, 1.0)
	await _frames(2)
	button.RequestPress()
	_check(await _wait_until(func(): return button.Pressed and door.IsOpen, 0.5), "button: [E] within reach didn't press it")
	_check(await _wait_until(func(): return door.position.distance_to(closed) > 3.0, 2.0), "button: closet door didn't slide open")
	_check(await _wait_until(func(): return not button.Pressed, 1.5), "button: never popped back up")
	_check(await _wait_until(func(): return door.position.distance_to(closed) < 0.01, 2.0), "button: closet door didn't close again")
	player.global_position = start

	crate.global_position = button.global_position + Vector3(1.5, 1.05, 0)
	crate.linear_velocity = Vector3(-6, 0, 0)
	_check(await _wait_until(func(): return button.Pressed, 1.0), "button: a thrown crate didn't press it")
	_check(await _wait_until(func(): return door.position.distance_to(closed) > 3.0, 2.0), "button: door didn't open for the thrown crate")

	crate.global_position = closed + Vector3(0, -1.2, 0) # in the doorway, on the floor (the level sits at the origin)
	crate.linear_velocity = Vector3.ZERO
	await _wait_until(func(): return not button.Pressed, 2.0)
	await _seconds(1.5)
	_check(door.position.distance_to(closed) > 0.5, "button: door closed on a crate in the doorway (%.2f m from shut)" % door.position.distance_to(closed))
	crate.global_position = Vector3(2.5, 0.3, 3)
	_check(await _wait_until(func(): return door.position.distance_to(closed) < 0.01, 2.0), "button: door didn't close once the crate was moved")


# A maze run, offline: passing a chamber loads the next, passing the last passes the run, a new run
# starts after the result, and everyone going down fails it (no timed respawn in chambers).
func _run_offline_run() -> void:
	_start_main()
	var run := _main.get_node("Run")
	run.Length = 2
	run.GeneratedShare = 0.0 # hand-built chambers, so the ramp below is predictable
	run.PauseSeconds = 0.2
	run.ResultSeconds = 0.5
	run.Start()
	for test in 2:
		var loaded := await _wait_until(func(): return run.Chamber == test and _players() != null and _players().has_node("1"), 3.0)
		if not _check(loaded, "run: chamber %d never loaded" % (test + 1)):
			break
		# Difficulty ramps: a 2-chamber run is the easiest chamber, then the hardest.
		var expected: PackedScene = run.Chambers[0 if test == 0 else run.Chambers.size() - 1]
		_check(_level().scene_file_path == expected.resource_path, "run: chamber %d was %s, not %s" % [test + 1, _level().scene_file_path, expected.resource_path])
		await _seconds(0.3)
		_players().get_node("1").global_position = _level().get_node("Exit").global_position - Vector3(0, 1.4, 0)
	_check(await _wait_until(func(): return run.Result == 1, 3.0), "run: passing the last chamber didn't pass the run")
	_check(await _wait_until(func(): return run.Result == 0 and run.Chamber == 0, 3.0), "run: no new run after the result")
	await _wait_until(func(): return _players() != null and _players().has_node("1"), 3.0)
	await _seconds(0.3)
	var health := _players().get_node("1").get_node("Health")
	health.TakeDamage(99999.0, 0)
	_check(await _wait_until(func(): return run.Result == 2, 2.0), "run: everyone down didn't fail the run")
	_check(health.IsDead, "run: a downed player came back on a timer in a chamber")
	var money: int = 2 * run.PayPerChamber + run.PassBonusPay
	var xp: int = 2 * run.XpPerChamber + run.PassBonusXp
	_main.queue_free()
	_main = null
	await _frames(2)
	_check_profile(money, xp)


# The profile: both runs above counted when they started; the passed run paid 2 chambers + the
# bonus, the failed one nothing. It's on disk, and an unreadable file is set aside, not fatal.
func _check_profile(money: int, xp: int) -> void:
	var store := get_node("/root/ProfileStore")
	_check(store.RunCount == 2 and store.LastRun == 2, "profile: 2 runs started, but it counted %d (last run %d)" % [store.RunCount, store.LastRun])
	_check(store.Money == money, "profile: the runs paid %d money, not %d" % [store.Money, money])
	_check(store.Level == 2 and store.Xp == xp - 100, "profile: %d XP should make level 2 with %d over (level %d, %d XP)" % [xp, xp - 100, store.Level, store.Xp])
	store.Reload()
	_check(store.Money == money, "profile: the money wasn't saved (%d after reloading)" % store.Money)

	var path: String = store.Path
	var bad := "user://smoke_profile_bad.json"
	var file := FileAccess.open(bad, FileAccess.WRITE)
	file.store_string("{ this isn't json")
	file.close()
	store.Path = bad
	store.Reload()
	_check(store.Money == 0 and store.Level == 1, "profile: an unreadable file didn't give a fresh profile")
	_check(FileAccess.file_exists(bad + ".bad"), "profile: the unreadable file wasn't kept aside")
	DirAccess.remove_absolute(ProjectSettings.globalize_path(bad + ".bad"))
	store.Path = path
	store.Reload()


# Hosting, or joining as a client (see net_suite.gd). The suite is a child node so its RPCs have the
# same path on every peer.
func _run_network() -> void:
	var net: Node = preload("res://tests/net_suite.gd").new()
	net.name = "Net"
	net.t = self
	net.index = int(_arg("index", "0"))
	net.total = int(_arg("clients", "3" if _role == "network" else "1"))
	net.ping = float(_arg("ping", "0"))
	if not _check(net.total <= net.MAX_CLIENTS, "the network test plays at most %d clients" % net.MAX_CLIENTS):
		return
	add_child(net)
	match _role:
		"network":
			net.has_late = net.total >= 2
			await net.run_host(true)
		"host":
			await net.run_host(false)
		"client":
			net.has_late = _arg("late", "0") == "1"
			await net.run_client()


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
	var plain := _brightness(await _screenshot(out.path_join("toon_off.png")))
	toon.Enabled = true
	await _seconds(0.3)
	var toon_lit := _brightness(await _screenshot(out.path_join("toon_on.png")))
	# Cel shading should band the light, not brighten or darken the room (see vfx/toon_ramp.tres).
	_log("brightness: toon off %.3f, on %.3f (%+.0f%%)" % [plain, toon_lit, (toon_lit / plain - 1.0) * 100.0])
	_check(absf(toon_lit / plain - 1.0) < 0.2, "capture: cel shading changes the room's brightness by more than 20%")

	# The view hotkeys: F4 / F5 turn film grain and colour crush off (and back on).
	var visor := _main.get_node("PostFX/Visor")
	_press(KEY_F4)
	_press(KEY_F5)
	await _seconds(0.3)
	_check(not visor.Grain and not visor.ColourCrush, "capture: F4 / F5 didn't turn grain and colour crush off")
	await _screenshot(out.path_join("toon_on_clean.png"))
	_press(KEY_F4)
	_press(KEY_F5)

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
	await _seconds(player.Loadout[0].Cooldown / 3.0)
	await _screenshot(out.path_join("hand_refilling.png"))
	player.SelectedSlot = 0

	await _meter("calm: breathing + room tone", 1.5)
	await _screenshot(out.path_join("visor_1_healthy.png"))
	ambience.PlayDistantEvent()
	await _meter("a distant ambience event", 1.5)
	_level().EmitSound(player.global_position + Vector3(2, 1, -3), 14.0, SHATTER)
	_level().EmitSound(enemy.global_position + Vector3.UP * 2.0, 12.0, GROWL)
	await _meter("shatter + growl nearby", 1.5)
	# Hits crack the glass toward whatever hit you: first from the front left, then from behind on the right.
	enemy.global_position = player.global_position + Vector3(-1.6, 0, -1.8)
	await _frames(2)
	health.TakeDamage(45.0, 0) # cracks: the mask muffles less from here
	await _meter("hurt (cracked mask)", 0.6)
	await _screenshot(out.path_join("visor_2_hurt.png"))
	enemy.global_position = player.global_position + Vector3(1.5, 0, 1.2)
	await _frames(2)
	health.TakeDamage(35.0, 0)
	player.get_node("Respirator").Remaining = 0.0
	await _meter("choking + heartbeat", 1.2)
	await _screenshot(out.path_join("visor_3_choking.png"))
	var fractures: int = visor.ImpactCount
	_check(fractures == 2, "capture: expected one fracture per hit, and choking to spread them, not add more (%d)" % fractures)

	# Mental damage: a clean mask, but the projected HUD scrambles and tears, and your ears ring.
	health.Revive()
	player.get_node("Respirator").Refill()
	await _frames(2)
	health.TakeMentalDamage(60.0)
	await _meter("mental damage (tinnitus)", 1.2)
	await _screenshot(out.path_join("visor_4_mental.png"))
	_check(visor.ImpactCount == 0, "capture: mental damage cracked the glass (%d fractures)" % visor.ImpactCount)
	# Far gone: the HUD has faded into a heavy vignette, and a physical hit shows as blood, not a crack.
	enemy.global_position = player.global_position + Vector3(-1.4, 0, -1.6)
	await _frames(2)
	health.TakeDamage(12.0, 0)
	health.TakeMentalDamage(22.0)
	await _seconds(0.8)
	await _screenshot(out.path_join("visor_5_blood.png"))

	# The settings panel over the game (hidden again without Close, which would save to the real file).
	var settings_menu := _main.get_node("UI/SettingsMenu")
	settings_menu.Open()
	await _seconds(0.3)
	await _screenshot(out.path_join("settings.png"))
	settings_menu.hide()

	# A generated chamber (seed 1 at the top difficulty: a closet and a ledge), from just inside the room.
	_main.ChangeToGenerated(1, 1.0)
	var built := await _wait_until(func(): return _level() != null and _level().has_meta("plan") and _players() != null and _players().has_node("1"), 5.0)
	if _check(built, "capture: the generated chamber never loaded"):
		await _seconds(1.0)
		var size: Vector3 = _level().get_meta("plan")["size"]
		var me := _players().get_node("1") as Node3D
		me.global_position = Vector3(0, 0.05, size.z / 2.0 - 0.5)
		me.rotation = Vector3.ZERO
		me.get_node("Head").rotation = Vector3(-0.15, 0, 0)
		await _seconds(0.5)
		await _screenshot(out.path_join("generated.png"))
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


func _screenshot(path: String) -> Image:
	await RenderingServer.frame_post_draw
	var image := get_viewport().get_texture().get_image()
	image.save_png(path)
	_log("saved " + path)
	return image


func _press(key: Key) -> void:
	var event := InputEventKey.new()
	event.keycode = key
	event.pressed = true
	Input.parse_input_event(event)


# Average brightness of the middle of the view (above the hotbar and hints), sampled on a grid.
func _brightness(image: Image) -> float:
	var w := image.get_width()
	var h := image.get_height()
	var total := 0.0
	var count := 0
	for y in range(int(h * 0.17), int(h * 0.64), 4):
		for x in range(int(w * 0.16), int(w * 0.84), 4):
			total += image.get_pixel(x, y).get_luminance()
			count += 1
	return total / count


# The port a client joins: the host's, or a lag proxy in front of it when --ping / --loss ask for a
# bad connection (--ping is the round trip in ms, --jitter extra ms per packet, --loss a percentage).
func _connect_port() -> int:
	var ping := float(_arg("ping", "0"))
	var loss := float(_arg("loss", "0")) / 100.0
	if ping <= 0.0 and loss <= 0.0:
		return PORT
	_proxy = preload("res://tests/lag_proxy.gd").new()
	_proxy.delay_ms = ping / 2.0
	_proxy.jitter_ms = float(_arg("jitter", "0"))
	_proxy.loss = loss
	var port := PORT + 100 + int(_arg("index", "0"))
	_proxy.start(port, PORT)
	_log("playing through a lag proxy: %.0f ms round trip, +%.0f ms jitter, %.0f%% loss" % [ping, _proxy.jitter_ms, loss * 100.0])
	return port


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
	if _proxy:
		var bytes: Vector2i = _proxy.counts()
		_log("traffic through the proxy: %.1f KB up, %.1f KB down" % [bytes.x / 1024.0, bytes.y / 1024.0])
		_proxy.stop()
	OS.remove_logger(_errors)
	for error in _errors.messages:
		_fail("error logged: " + error)
	if _failures.is_empty():
		print("SMOKE PASS (%s)" % _role)
		get_tree().quit(0)
	else:
		for failure in _failures:
			printerr("SMOKE FAIL (%s): %s" % [_role, failure])
		get_tree().quit(1)
