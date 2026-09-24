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
	level.queue_free()


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

	_network.Leave()
	await _frames(5)
	_check(_level() == null, "client: level not cleared after leaving")


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
