extends Node

## Headless smoke test. Run from the game/ folder:
##   godot --headless res://tests/smoke_test.tscn -- --role=scenes
##   godot --headless res://tests/smoke_test.tscn -- --role=host     (start first)
##   godot --headless res://tests/smoke_test.tscn -- --role=client
## Prints "SMOKE PASS" or "SMOKE FAIL: ..." lines and exits with 0 / 1.

const PORT := 17777
const TIMEOUT := 60.0
const SCENES := [
	"res://core/main.tscn",
	"res://levels/test_level.tscn",
	"res://players/player.tscn",
	"res://props/crate.tscn",
	"res://props/crate_large.tscn",
	"res://props/specimen_jar.tscn",
	"res://enemies/enemy.tscn",
	"res://items/acid_beaker_projectile.tscn",
	"res://ui/main_menu.tscn",
	"res://ui/hud.tscn",
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
	level.queue_free()


# Acid beaker vs. evil guy, evil guy vs. player, death and respawn, all offline as the host.
func _run_offline_combat(level: Node, player: Node3D) -> void:
	var beaker = load("res://items/acid_beaker.tres")
	_check(beaker != null and beaker.Cooldown == 15.0, "acid_beaker.tres didn't load as a 15 s throwable")
	if not _check(player.Loadout.size() == 1, "player loadout should hold the acid beaker (size %d)" % player.Loadout.size()):
		return

	var nav := level.get_node("Navigation") as NavigationRegion3D
	_check(await _wait_until(func(): return nav.navigation_mesh.get_polygon_count() > 0, 5.0), "offline: navmesh didn't bake")

	var enemies := level.get_node("Enemies")
	if not _check(enemies.get_child_count() == 1, "offline: expected 1 evil guy, found %d" % enemies.get_child_count()):
		return
	var enemy := enemies.get_child(0) as Node3D
	var enemy_health := enemy.get_node("Health")
	var player_health := player.get_node("Health")

	# Throw: freeze the AI so it stands still, then lob a beaker at it from 5 m.
	enemy.set_physics_process(false)
	player.global_position = enemy.global_position + Vector3(0, 0, 5)
	await _frames(2)
	var eye: Vector3 = player.get_node("Head").global_position
	var aim: Vector3 = (enemy.global_position + Vector3.UP * 1.2 - eye).normalized()
	player.rpc_id(1, "RequestThrowItem", 1, eye, aim)
	_check(await _wait_until(func(): return enemy_health.Current < enemy_health.MaxHealth, 3.0), "offline: acid beaker didn't hurt the evil guy")
	var after_hit: float = enemy_health.Current
	_log("beaker hit: evil guy at %.0f / %.0f" % [after_hit, enemy_health.MaxHealth])

	player.rpc_id(1, "RequestThrowItem", 1, eye, aim) # still recharging, so the host must refuse
	await _seconds(1.0)
	_check(enemy_health.Current == after_hit, "offline: beaker ignored its 15 s recharge")

	# Let it loose next to us: it should swing and hurt us.
	enemy.set_physics_process(true)
	player.global_position = enemy.global_position + Vector3(0, 0, 1.2)
	_check(await _wait_until(func(): return player_health.Current < player_health.MaxHealth, 4.0), "offline: evil guy never hurt the player")
	_log("evil guy hit: player at %.0f / %.0f" % [player_health.Current, player_health.MaxHealth])

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


func _run_host() -> void:
	_start_main()
	if not _check(_network.Host(PORT) == OK, "host: couldn't open port %d" % PORT):
		return
	if not _check(await _wait_until(func(): return _level() != null, 5.0), "host: level never loaded"):
		return
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

	await _run_network_combat(me, head)

	_network.Leave()
	await _frames(5)
	_check(_level() == null, "client: level not cleared after leaving")


# The host runs the evil guy and all damage; the client throws a beaker and gets hit back.
func _run_network_combat(me: Node3D, head: Node3D) -> void:
	var enemies := _level().get_node("Enemies")
	if not _check(await _wait_until(func(): return enemies.get_child_count() > 0, 5.0), "client: evil guy never replicated"):
		return
	var enemy := enemies.get_child(0) as Node3D
	var enemy_health := enemy.get_node("Health")
	var my_health := me.get_node("Health")
	var start_health: float = enemy_health.Current

	# Stand 4 m from it, on the room-centre side so we never end up inside a wall.
	var away := -enemy.global_position * Vector3(1, 0, 1)
	away = away.normalized() if away.length() > 1.0 else Vector3.BACK
	me.global_position = enemy.global_position + away * 4.0
	await _seconds(0.3) # the host checks the throw starts near where it thinks we are

	var eye := head.global_position
	var aim := (enemy.global_position + Vector3.UP * 1.2 - eye).normalized()
	me.rpc_id(1, "RequestThrowItem", 1, eye, aim)
	_check(await _wait_until(func(): return enemy_health.Current < start_health, 3.0), "client: acid beaker didn't hurt the evil guy")
	_log("beaker hit: evil guy at %.0f / %.0f" % [enemy_health.Current, enemy_health.MaxHealth])

	# It should now come for us; stay close and wait for the host to replicate the damage.
	me.global_position = enemy.global_position + away * 1.2
	_check(await _wait_until(func(): return my_health.Current < my_health.MaxHealth, 6.0), "client: evil guy never hurt us (host damage didn't replicate)")
	_log("evil guy hit us: %.0f / %.0f" % [my_health.Current, my_health.MaxHealth])


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
