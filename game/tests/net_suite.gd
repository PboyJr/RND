extends Node

## The network half of the smoke test: a host and its clients play the sandbox, then a whole maze
## run, optionally over a fake bad connection (lag_proxy.gd). The clients do the playing, the way
## players would: they walk (colliding with doors), carry crates onto plates, press buttons, throw
## jars, revive each other, and one of them joins mid-run and another leaves while carrying. They
## meet at barriers on the host, ask it for host-only things (start the run, kill a player), and
## send it their results, so the host's exit code covers everyone.
##
## Client 0 leads. With two or more clients the host starts them itself, and the last one joins late.

const CORNERS := [Vector3(-0.4, 0, -0.4), Vector3(0.4, 0, -0.4), Vector3(-0.4, 0, 0.4), Vector3(0.4, 0, 0.4)]
const MAX_CLIENTS := 4 # two lanes through the 3 m corridors, two rows deep (see _lane)
const EYE_HEIGHT := 1.55
const TURN_SPEED := 3.0 # rad/s: a half turn in about a second

var t # the smoke test: _check, _wait_until, _seconds, _frames, _log, _level, _players, _main, _network
var index := -1       # this client's number (the host is -1)
var total := 1        # clients in the whole test
var has_late := false  # the last client joins mid-run (only when the host starts the clients)
var ping := 0.0

var _roster := {}     # client index -> peer id (from the host)
var _arrived := {}    # host: barrier -> [peer ids]
var _needed := {}     # host: barrier -> how many clients it waits for
var _released := {}   # barriers the host has let everyone through
var _replies := {}    # host replies to our requests, by action
var _reports := {}    # host: client index -> failures
var _stats := []      # this peer's measurements, sent with the report
var _spawn := false   # host: whether it starts the client processes
var _late_started := false


# ── Host ──────────────────────────────────────────────────────────────────────

func run_host(spawn: bool) -> void:
	_spawn = spawn
	t._start_main()
	if not t._check(t._network.Host(t.PORT) == OK, "host: couldn't open port %d" % t.PORT):
		return
	if not t._check(await t._wait_until(func(): return t._players() != null and t._players().has_node("1"), 5.0), "host: level never loaded"):
		return
	# Take the host's own (idle) player out of the sandbox, so the evil guy only goes for clients.
	_bench(t._players().get_node("1"))
	var early := _early()
	if _spawn:
		for i in early:
			_start_client(i)
	t._log("hosting, waiting for %d client(s)%s" % [total, " (the last joins late)" if has_late else ""])

	# The clients drive everything from here (see _host_do); wait for all their reports.
	var reported: bool = await t._wait_until(func(): return _reports.size() >= total, float(t._arg("timeout", "300")) - 5.0)
	t._check(reported, "host: only %d of %d clients reported back (see their logs in %s)" % [_reports.size(), total, _log_dir()])
	for i in _reports:
		for failure in _reports[i]:
			t._fail("client %d: %s" % [i, failure])
	await t._wait_until(func(): return multiplayer.get_peers().is_empty(), 10.0)
	t._network.Leave()


func _early() -> int:
	return total - 1 if has_late else total


func _start_client(i: int) -> void:
	var args := PackedStringArray(["--headless", "--path", ProjectSettings.globalize_path("res://"),
		"--log-file", _log_dir().path_join("client_%d.log" % i), "res://tests/smoke_test.tscn", "--",
		"--role=client", "--index=%d" % i, "--clients=%d" % total, "--late=%d" % int(has_late),
		"--timeout=%s" % t._arg("timeout", "300")])
	for key in ["ping", "jitter", "loss", "steam"]:
		if t._arg(key, "") != "":
			args.append("--%s=%s" % [key, t._arg(key, "")])
	OS.create_process(OS.get_executable_path(), args)


func _log_dir() -> String:
	return t._arg("out", OS.get_user_data_dir())


func _bench(player: Node) -> void:
	player.RespawnDelay = 999.0
	player.get_node("Health").TakeDamage(99999.0, 0)


# Things only the host can do, asked for by a client. It replies when done.
@rpc("any_peer", "call_remote", "reliable")
func _host_do(action: String, arg: Variant = null) -> void:
	var sender := multiplayer.get_remote_sender_id()
	var reply: Variant = true
	match action:
		"start_run":
			var run: Node = t._main.get_node("Run")
			run.Length = run.Chambers.size()
			run.GeneratedShare = 0.0 # the suite knows the hand-built chambers' layouts
			run.PauseSeconds = 1.5
			run.ResultSeconds = 3.0
			# Keep the evil guy out of the maze: this is about the puzzles over the network.
			t._main.get_node("Level").child_entered_tree.connect(_on_level_added)
			run.Start()
		"spawn_late":
			reply = _spawn and has_late and not _late_started
			if reply:
				_late_started = true
				_start_client(total - 1)
		"kill":
			var victim: Node = t._players().get_node_or_null(str(arg))
			if victim:
				victim.get_node("Health").TakeDamage(99999.0, 0)
		"bench":
			_bench(t._players().get_node(str(sender)))
		"kill_all":
			for player in t._players().get_children():
				player.get_node("Health").TakeDamage(99999.0, 0)
		"generate_next": # the next run's chambers are all generated
			t._main.get_node("Run").GeneratedShare = 1.0
		"snapshot":
			reply = _snapshot()
	rpc_id(sender, "_reply", action, reply)


# The host's picture of the chamber, for a late joiner to compare with its own.
func _snapshot() -> Dictionary:
	var level: Node = t._level()
	var props := {}
	for prop in level.get_node("Props").get_children():
		if prop.HeldBy == 0 and prop.linear_velocity.length() < 0.05:
			props[str(prop.name)] = prop.global_position
	var doors := {}
	for node in level.get_node("Navigation/Geometry").get_children():
		if "IsOpen" in node:
			doors[str(node.name)] = node.IsOpen
	var players := {}
	for player in level.get_node("Players").get_children():
		players[str(player.name)] = "%s reported %s%s" % [player.global_position.snapped(Vector3.ONE * 0.01), player.SyncPosition.snapped(Vector3.ONE * 0.01), " (down)" if player.IsDead else ""]
	var plan: Dictionary = level.get_meta("plan") if level.has_meta("plan") else {}
	var exit: Node = level.get_node_or_null("Exit")
	return {"elapsed": exit.Elapsed if exit else 0.0, "props": props, "doors": doors, "chamber": t._main.get_node("Run").Chamber, "players": players, "plan": plan}


func _on_level_added(level: Node) -> void:
	if "EnemyReleaseDelay" in level:
		level.EnemyReleaseDelay = 999.0
		_bench_host.call_deferred(level)


# The host's own player lies down in each chamber (in a corner, out of everyone's way), so the test
# passes once the clients are in the exit.
func _bench_host(level: Node) -> void:
	if not await t._wait_until(func(): return is_instance_valid(level) and level.has_node("Players/1"), 5.0):
		return
	await t._seconds(0.2) # after it has placed itself at a spawn point
	var host: Node3D = level.get_node("Players/1")
	host.global_position = Vector3(5.2, 0.05, 5.2)
	_bench(host)


@rpc("any_peer", "call_remote", "reliable")
func _register(i: int) -> void:
	_roster[i] = multiplayer.get_remote_sender_id()
	rpc("_set_roster", _roster)


@rpc("any_peer", "call_local", "reliable")
func _arrive(barrier: String, count: int) -> void:
	var arrived: Array = _arrived.get_or_add(barrier, [])
	arrived.append(multiplayer.get_remote_sender_id())
	_needed[barrier] = count
	if arrived.size() >= count:
		rpc("_release", barrier)


@rpc("any_peer", "call_remote", "reliable")
func _report(i: int, failures: PackedStringArray, stats: Array) -> void:
	_reports[i] = failures
	rpc_id(multiplayer.get_remote_sender_id(), "_reply", "report", true)
	for line in stats:
		t._log("client %d: %s" % [i, line])


@rpc("any_peer", "call_remote", "reliable")
func _remote_log(i: int, message: String) -> void:
	print("[client %d] %s" % [i, message])


# ── Messages to clients ───────────────────────────────────────────────────────

@rpc("authority", "call_local", "reliable")
func _release(barrier: String) -> void:
	_released[barrier] = true


@rpc("authority", "call_remote", "reliable")
func _set_roster(roster: Dictionary) -> void:
	_roster = roster


@rpc("authority", "call_remote", "reliable")
func _reply(action: String, value: Variant) -> void:
	_replies[action] = value


# ── Client ────────────────────────────────────────────────────────────────────

func run_client() -> void:
	t._start_main()
	if not t._check(t._network.Join("127.0.0.1", t._connect_port()) == OK, "client: couldn't start connecting"):
		return
	var joined: bool = await t._wait_until(func(): return t._players() != null and t._players().has_node(str(multiplayer.get_unique_id())), 20.0)
	if not t._check(joined, "client %d: level / own player never replicated" % index):
		return
	rpc_id(1, "_register", index)
	await t._wait_until(func(): return _roster.has(index), 5.0)
	_log("joined as %d" % multiplayer.get_unique_id())

	if has_late and index == total - 1:
		await _late_arrival()
	else:
		await _sandbox()
		await _crates_chamber()
		await _closet_chamber()
	await _ledge_chamber()
	await _run_over()
	await _send_report()
	await t._seconds(0.5)
	t._network.Leave()
	await t._wait_until(func(): return not t._network.IsClosing, 3.0) # the goodbye has to get through the proxy
	t._check(t._level() == null, "client %d: level not cleared after leaving" % index)


func _lead() -> bool:
	return index == 0


# The second client does the other half of co-op jobs; alone, the lead does both.
func _helper() -> int:
	return 1 if _early() >= 2 else 0


func _me() -> CharacterBody3D:
	return t._players().get_node_or_null(str(multiplayer.get_unique_id()))


func _log(message: String) -> void:
	t._log(message)
	if multiplayer.has_multiplayer_peer() and multiplayer.get_unique_id() != 1 and not multiplayer.get_peers().is_empty():
		rpc_id(1, "_remote_log", index, message)


# Waits until `count` clients (default: everyone playing so far) have reached this point.
func _sync(barrier: String, count := -1, timeout := 60.0) -> bool:
	rpc_id(1, "_arrive", barrier, count if count > 0 else _playing())
	return t._check(await t._wait_until(func(): return _released.has(barrier), timeout), "client %d: stuck at barrier '%s'" % [index, barrier])


var _late_in := false # whether the late client has arrived (so barriers count it)

func _playing() -> int:
	return total if _late_in else _early()


func _ask(action: String, arg: Variant = null) -> Variant:
	_replies.erase(action)
	rpc_id(1, "_host_do", action, arg)
	await t._wait_until(func(): return _replies.has(action), 10.0)
	return _replies.get(action)


var _reported := false

func _send_report() -> void:
	if _reported:
		return
	_reported = true
	var failures: PackedStringArray = t._failures.duplicate()
	for error in t._errors.messages:
		failures.append("error logged: " + error)
	if t._proxy:
		var bytes: Vector2i = t._proxy.counts()
		_stats.append("traffic %.1f KB up, %.1f KB down over the session" % [bytes.x / 1024.0, bytes.y / 1024.0])
	_replies.erase("report")
	rpc_id(1, "_report", index, failures, _stats)
	# Wait for the host to have it: a lost packet takes a resend, and hanging up first would lose it.
	t._check(await t._wait_until(func(): return _replies.has("report"), 15.0), "client %d: the host never confirmed our report" % index)


# ── Moving like a player ─────────────────────────────────────────────────────

# Walks through the points at walking speed, colliding like a real player (a shut door stops it).
# Faces where it's going unless `yaw` is given. Samples how far a carried prop trails the hold
# point, if there is one. False if it gets stuck.
var _last_walked := Vector3.INF # where the last step left us, to spot something else moving us mid-walk
var _last_bump := "nothing" # what the last blocked step ran into

func _walk(points: Array, speed := 4.0, yaw: Variant = null, carrying: Node3D = null) -> bool:
	var me := _me()
	var dt := get_physics_process_delta_time()
	_last_walked = Vector3.INF
	for point in points:
		var target: Vector3 = point
		var best := INF
		var progress_at := Time.get_ticks_msec()
		while true:
			await get_tree().physics_frame
			if not is_instance_valid(me):
				return false
			var to := (target - me.global_position) * Vector3(1, 0, 1)
			var distance := to.length()
			if distance < 0.1:
				break
			if distance < best - 0.05:
				best = distance
				progress_at = Time.get_ticks_msec()
			elif Time.get_ticks_msec() - progress_at > 1500:
				if distance < 0.3:
					break # near enough
				_log("stuck at %s on the way to %s (against %s)" % [me.global_position, target, _last_bump])
				return false
			var direction := to / distance
			if yaw == null:
				# Turn toward where we're going like a player would (not instantly), then walk.
				var heading := atan2(-direction.x, -direction.z)
				me.rotation.y = rotate_toward(me.rotation.y, heading, TURN_SPEED * dt)
				if absf(angle_difference(me.rotation.y, heading)) > 0.6:
					progress_at = Time.get_ticks_msec()
					continue
			else:
				me.rotation.y = yaw
			var before := me.global_position
			if _last_walked != Vector3.INF and _last_walked.distance_to(before) > 1.0:
				_log("was moved from %s to %s between steps, walking to %s" % [_last_walked, before, target])
			var step := direction * minf(speed * dt, distance)
			var collision := me.move_and_collide(step, true) # look first
			if collision == null or collision.get_normal().y > 0.7:
				me.global_position += step # clear, or only the floor we're standing on
			else: # a wall or a prop: go as far as it lets us, then slide along it (sideways only)
				_last_bump = str(collision.get_collider().name) if collision.get_collider() else "?"
				collision = me.move_and_collide(step)
				if collision:
					me.move_and_collide(collision.get_remainder().slide(collision.get_normal()) * Vector3(1, 0, 1))
			if before.y - me.global_position.y > 0.3 or before.distance_to(me.global_position) > 2.0:
				_log("jumped from %s to %s walking to %s" % [before, me.global_position, target])
			_last_walked = me.global_position
			if carrying:
				_trail.append(carrying.global_position.distance_to(me.HoldPoint))
	return true


var _trail: Array[float] = []


# Waits until a prop (as we see it) has stopped moving.
func _settled(prop: Node3D, timeout := 3.0) -> bool:
	var last := [Vector3.INF]
	return await t._wait_until(func():
		var moved: float = prop.global_position.distance_to(last[0])
		last[0] = prop.global_position
		return moved < 0.002, timeout)


# Our lane: x across the corridor, and which row (0 front, 1 behind). Two players per lane.
func _lane() -> Vector2:
	return Vector2(-0.5 if index % 2 == 0 else 0.5, index / 2)


# A spot this far from a prop, on our side of it (so not in a wall it has ended up against).
func _approach(prop: Node3D, distance: float) -> Vector3:
	var flat := (prop.global_position - _me().global_position) * Vector3(1, 0, 1)
	return prop.global_position * Vector3(1, 0, 1) - flat.normalized() * distance


# Out of the start corridor into the room, down our own lane. One after another: four players
# crossing to their lanes at once bump into each other (and, at high ping, into where the others were).
func _enter_room(z: float) -> bool:
	var lane := _lane()
	await t._seconds(0.6 * index)
	return t._check(await _walk([Vector3(lane.x, 0, z + lane.y)]), "client %d: couldn't walk out of the start corridor" % index)


func _face(point: Vector3) -> void:
	var me := _me()
	var eye: Vector3 = me.EyePosition
	var flat := (point - eye) * Vector3(1, 0, 1)
	me.rotation.y = atan2(-flat.x, -flat.z)
	me.get_node("Head").rotation.x = atan2(point.y - eye.y, flat.length())


# Where to stand so that, looking along `forward` with this pitch, the hold point is over `spot` at `height`.
func _stand_for(spot: Vector3, forward: Vector3, height: float) -> Array:
	var reach: float = _me().SyncHoldDistance
	var pitch := asin(clampf((height - EYE_HEIGHT) / reach, -1.0, 1.0))
	return [spot - forward.normalized() * reach * cos(pitch), pitch]


# Picks a prop up, carries it so it hangs over `spot` at `height`, and lets go. Logs how far it
# trailed behind the hold point on the way (the lag you'd see while carrying). True if it lands
# within `tolerance` of the spot.
func _carry(prop: RigidBody3D, spot: Vector3, height := 1.1, via: Array = [], tolerance := 0.35, speed := 4.0) -> bool:
	var me := _me()
	var my_id := multiplayer.get_unique_id()
	var flat := (prop.global_position - me.global_position) * Vector3(1, 0, 1)
	var approach: Vector3 = prop.global_position * Vector3(1, 0, 1) - flat.normalized() * 1.3
	if not await _walk([approach]):
		return t._check(false, "client %d: couldn't reach %s" % [index, prop.name])
	_face(prop.global_position)
	await t._frames(2)
	var asked := Time.get_ticks_msec()
	prop.rpc_id(1, "RequestGrab")
	if not t._check(await t._wait_until(func(): return prop.HeldBy == my_id, 3.0), "client %d: host didn't let us grab %s" % [index, prop.name]):
		return false
	var grab_ms := Time.get_ticks_msec() - asked

	var last: Vector3 = via[-1] if not via.is_empty() else me.global_position
	var forward := (spot - last) * Vector3(1, 0, 1)
	var stand: Array = _stand_for(spot, forward, height)
	me.get_node("Head").rotation.x = stand[1]
	await t._seconds(0.3)
	_trail.clear()
	var walked: bool = await _walk(via + [stand[0]], speed, null, prop)
	var trail := _trail.duplicate()
	me.rotation.y = atan2(-forward.x, -forward.z)
	# Wait for the prop to hang still at the hold point, then let go (a swinging prop flies on).
	var settled: bool = await t._wait_until(func(): return prop.global_position.distance_to(me.HoldPoint) < 0.1 and prop.linear_velocity.length() < 0.3, 4.0)
	prop.Release()
	await t._wait_until(func(): return prop.HeldBy == 0 and not prop.Predicting, 6.0) # landed, and handed back to the host
	await _settled(prop)
	var off := ((prop.global_position - spot) * Vector3(1, 0, 1)).length()
	if not trail.is_empty():
		trail.sort()
		_stats.append("carried %s (%.0f kg): grab took %d ms, trailed the hold point by %.2f m on average, %.2f m at worst%s; landed %.2f m off" % [
			prop.name, prop.mass, grab_ms, trail.reduce(func(a, b): return a + b) / trail.size(), trail[-1],
			"" if settled else " (never caught up)", off])
	t._check(walked, "client %d: got stuck carrying %s" % [index, prop.name])
	return t._check(prop.HeldBy == 0 and off < tolerance, "client %d: %s landed %.2f m from where we carried it (%s, held by %d)" % [index, prop.name, off, prop.global_position, prop.HeldBy])


# ── Sandbox (the test level) ─────────────────────────────────────────────────

func _sandbox() -> void:
	await _sync("joined")
	# Everyone steps into a line; everyone must see everyone else there (positions relay via the host).
	var spot := func(i: int) -> Vector3: return Vector3(-4.0 + 1.5 * i, 0.05, 5.0)
	_me().global_position = spot.call(index)
	await _sync("in line")
	for i in _roster:
		if i == index or i >= _early():
			continue
		var other: Node3D = t._players().get_node_or_null(str(_roster[i]))
		var there: bool = other != null and await t._wait_until(func(): return other.global_position.distance_to(spot.call(i)) < 0.2, 3.0)
		t._check(there, "client %d: client %d isn't where it stood (%s)" % [index, i, other.global_position if other else "missing"])
	await _sync("line checked")

	if _lead():
		await _sandbox_lead()
	else:
		await _sandbox_other()
	await _sync("sandbox done")


func _sandbox_other() -> void:
	# Carrying is covered in the maze; here the other clients only need to be out of the lead's way.
	await _ask("bench") # out of the evil guy's way while the lead fights it


# What one client always checked on the test level: carry, throw, filter, combat, revive.
func _sandbox_lead() -> void:
	var me := _me()
	var my_id := multiplayer.get_unique_id()
	_check_start(me)
	var jar := t._level().get_node("Props/JarA") as RigidBody3D
	t._check(jar.freeze, "client: props should be frozen (the host simulates them)")

	# Walk up to the jar (facing it) and grab it.
	me.global_position = jar.global_position + Vector3(0, -0.175, 1.5)
	me.rotation = Vector3.ZERO
	me.get_node("Head").rotation = Vector3.ZERO
	await t._seconds(0.5) # let the host see where we are
	jar.rpc_id(1, "RequestGrab")
	if not t._check(await t._wait_until(func(): return jar.HeldBy == my_id, 3.0), "client: host didn't confirm the grab (HeldBy=%s)" % jar.HeldBy):
		var snap: Dictionary = await _ask("snapshot")
		_log("grab refused: I'm at %s, the jar at %s; the host has players %s, props %s" % [me.global_position, jar.global_position, snap["players"], snap["props"].get("JarA", "held or moving")])
		return
	await t._seconds(1.0)
	var hold_error := jar.global_position.distance_to(me.HoldPoint)
	t._check(hold_error < 0.5, "client: held jar isn't following the hold point (off by %.2f m)" % hold_error)

	# Turn on the spot while holding it: how far behind does it swing?
	_trail.clear()
	for step in 45: # a quarter turn in 0.75 s
		me.rotation.y += PI / 2.0 / 45.0
		await get_tree().physics_frame
		_trail.append(jar.global_position.distance_to(me.HoldPoint))
	_trail.sort()
	_stats.append("turning 90° in 0.75 s with a jar: it swung up to %.2f m behind the hold point" % _trail[-1])
	me.rotation.y = 0.0
	await t._seconds(1.0)

	var before := jar.global_position
	jar.Throw(me.AimDirection)
	t._check(await t._wait_until(func(): return jar.HeldBy == 0, 3.0), "client: throw didn't release the jar")
	await t._seconds(0.4)
	t._check(jar.global_position.distance_to(before) > 1.0, "client: the thrown jar didn't fly (%s -> %s)" % [before, jar.global_position])
	_log("threw jar: %s -> %s" % [before, jar.global_position])

	await _sandbox_filter(me)
	await _sandbox_combat(me)
	await _sandbox_revive(me)


func _check_start(me: Node3D) -> void:
	t._check(me.is_multiplayer_authority(), "client %d: doesn't own its player" % index)
	t._check(t._players().has_node("1"), "client %d: host's player missing" % index)


# The host breathes for us (drains our filter) and replicates it; a spare canister refills it.
func _sandbox_filter(me: Node3D) -> void:
	var respirator := me.get_node("Respirator")
	var capacity: float = respirator.Capacity
	t._check(await t._wait_until(func(): return respirator.Remaining < capacity, 3.0), "client: filter never drained (host breathing not replicating?)")

	var canister := t._level().get_node("Props/FilterB") as Node3D # on the table
	me.global_position = Vector3(canister.global_position.x, 0.05, -4.5) # beside the table
	await t._seconds(0.3 + ping / 1000.0) # let the host see us next to it
	canister.rpc_id(1, "RequestUse")
	var refilled: bool = await t._wait_until(func(): return canister.Consumed and respirator.Remaining > capacity - 1.0, 2.0)
	t._check(refilled, "client: a spare filter didn't refill us and vanish (remaining %.1f, consumed %s)" % [respirator.Remaining, canister.Consumed])


# The host runs the evil guy and all damage. The client gets hit, then hits back.
func _sandbox_combat(me: Node3D) -> void:
	var enemies: Node = t._level().get_node("Enemies")
	if not t._check(await t._wait_until(func(): return enemies.get_child_count() > 0, 5.0), "client: evil guy never replicated"):
		return
	var enemy := enemies.get_child(0) as Node3D
	var enemy_health := enemy.get_node("Health")
	var my_health := me.get_node("Health")

	# Stay next to it (room-centre side, same level) until it swings: proves the host's AI picks us
	# and its damage replicates back to us.
	var got_hit: bool = await t._wait_until(func():
		if me.global_position.distance_to(enemy.global_position) > 1.6:
			var away := -enemy.global_position * Vector3(1, 0, 1)
			me.global_position = enemy.global_position + (away.normalized() if away.length() > 1.0 else Vector3.BACK) * 1.2
		return my_health.Current < my_health.MaxHealth, 15.0)
	if not t._check(got_hit, "client: evil guy never hurt us (host damage didn't replicate?)"):
		return
	_log("evil guy hit us: %.0f / %.0f" % [my_health.Current, my_health.MaxHealth])

	# It stands still recovering from the swing, so a point-blank throw should land. Over a laggy
	# connection we see it a little late, and it may already be moving again: then step up to it
	# and throw again once the flask has refilled, as a player would.
	var start_health: float = enemy_health.Current
	var hurt := false
	for attempt in 2:
		if attempt > 0:
			await t._seconds(5.2) # the flask's recharge
			var away := -enemy.global_position * Vector3(1, 0, 1)
			me.global_position = enemy.global_position + (away.normalized() if away.length() > 1.0 else Vector3.BACK) * 1.2
		await t._seconds(ping / 1000.0) # the host needs our latest position to accept the throw
		var eye: Vector3 = me.EyePosition
		me.rpc_id(1, "RequestThrowItem", 1, eye, (enemy.global_position + Vector3.UP * 1.2 - eye).normalized())
		hurt = await t._wait_until(func(): return enemy_health.Current < start_health, 2.0)
		if hurt:
			break
	t._check(hurt, "client: two point-blank acid flasks didn't hurt the evil guy")


# The host's player lies dead (see run_host). Next to it we revive it at half health where it fell;
# from across the room, nothing happens.
func _sandbox_revive(me: Node3D) -> void:
	var host_player := t._players().get_node("1") as Node3D
	var health := host_player.get_node("Health")
	if not t._check(health.IsDead, "revive: the host's player should be lying dead"):
		return
	if me.global_position.distance_to(host_player.global_position) > 4.0:
		host_player.rpc_id(1, "RequestRevive")
		await t._seconds(0.5)
		t._check(health.IsDead, "revive: revived from %.1f m away" % me.global_position.distance_to(host_player.global_position))
	var fell_at := host_player.global_position
	me.global_position = fell_at + Vector3(0, 0.05, 1.2)
	await t._seconds(0.3 + ping / 1000.0) # let the host see us next to it
	host_player.rpc_id(1, "RequestRevive")
	t._check(await t._wait_until(func(): return not health.IsDead, 2.0), "revive: standing next to it didn't revive the host's player")
	t._check(absf(health.Current - 50.0) < 0.1, "revive: came back with %.0f health, not 50" % health.Current)
	await t._seconds(0.3)
	t._check(host_player.global_position.distance_to(fell_at) < 1.0, "revive: the revived player was moved to a spawn point")
	_ask("kill", 1) # back down, out of the way


# ── The maze run ──────────────────────────────────────────────────────────────

func _run() -> Node:
	return t._main.get_node("Run")


# Waits for chamber `number` (0-based) of the run to arrive, and checks it arrived whole.
func _arrive_in_chamber(number: int) -> bool:
	var run := _run()
	var path: String = run.Chambers[number].resource_path
	var arrived: bool = await t._wait_until(func(): return run.Active and run.Chamber == number and t._level() != null \
		and t._level().scene_file_path == path and _me() != null, 15.0)
	if not t._check(arrived, "client %d: chamber %d (%s) never arrived (run active %s, chamber %d, level %s)" % [index, number + 1, path.get_file(), run.Active, run.Chamber, t._level().scene_file_path if t._level() else "none"]):
		return false
	await t._seconds(0.6)
	var me := _me()
	var exit: Node3D = t._level().get_node("Exit")
	t._check(me.global_position.distance_to(exit.global_position) > 10.0, "client %d: spawned near the exit of %s" % [index, path.get_file()])
	t._check(not me.get_node("Health").IsDead and me.get_node("Health").Current == me.get_node("Health").MaxHealth, "client %d: didn't start %s at full health" % [index, path.get_file()])
	t._check(run.Length == run.Chambers.size(), "client %d: the run's length didn't replicate (%d)" % [index, run.Length])
	# Nobody spawns on top of anyone else.
	for other in t._players().get_children():
		if other != me and other.name != "1":
			t._check(other.global_position.distance_to(me.global_position) > 0.6, "client %d: spawned on top of player %s in %s" % [index, other.name, path.get_file()])
	return true


# Everyone walks down their lane, through the exit door, into the exit. Passing it moves the run on.
func _walk_out(door_z: float, exit_z: float) -> void:
	var lane := _lane()
	var door: Node3D = t._level().get_node("Navigation/Geometry/ExitDoor")
	t._check(await t._wait_until(func(): return door.IsOpen, 5.0), "client %d: the exit door is shut on our side" % index)
	# The front row goes a metre further in than the row behind it.
	var path := [Vector3(lane.x, 0, door_z + 1.5 + lane.y), Vector3(lane.x, 0, door_z - 1.0), Vector3(lane.x, 0, exit_z - 1.0 + lane.y)]
	t._check(await _walk(path), "client %d: couldn't walk out through the exit" % index)
	var exit: Node = t._level().get_node("Exit")
	if not t._check(await t._wait_until(func(): return exit.IsComplete, 10.0), "client %d: standing in the exit never passed the test" % index):
		var snap: Dictionary = await _ask("snapshot")
		_log("the host sees the players at %s; I'm %d at %s" % [snap.get("players"), multiplayer.get_unique_id(), _me().global_position])


# Chamber 1: four crates hold the exit plate. The clients carry them over between them.
func _crates_chamber() -> void:
	if _lead():
		await _ask("start_run")
	if not await _arrive_in_chamber(0):
		return
	var level: Node = t._level()
	var plate: Node3D = level.get_node("Plate")
	var door: Node3D = level.get_node("Navigation/Geometry/ExitDoor")
	await _sync("c1 arrived")
	await _enter_room(5.2)
	await _walk([Vector3(-5.3, 0, 0.2 + 0.8 * index)]) # wait by the west wall, off every carrier's path
	var crates := ["CrateA", "CrateB", "CrateD", "CrateE"]
	# One at a time: several players converging on the plate at once knock each other's crates.
	for j in crates.size():
		if j % _early() == index:
			await _carry(level.get_node("Props/" + crates[j]), plate.global_position + CORNERS[j], 1.1, [Vector3(0, 0, 0)], 0.6) # a dropped crate can bounce a little
			await _walk([Vector3(-5.3, 0, 0.2 + 0.8 * index)]) # by the west wall, out of the next carrier's way
		await _sync("c1 crate %d" % j)
	await _sync("c1 crates down")
	t._check(await t._wait_until(func(): return plate.Pressed and door.IsOpen, 3.0), "client %d: four carried crates didn't hold the plate (pressed %s)" % [index, plate.Pressed])
	await _walk_out(-6.2, -8.2)


# Chamber 2: the heavy case is in a closet behind a button door. One client holds the door open
# (the button runs out, so it keeps pressing), the other fetches the case. Then one goes down and
# the other revives them, and a late client joins in.
func _closet_chamber() -> void:
	if not await _arrive_in_chamber(1):
		return
	var level: Node = t._level()
	var button: Node3D = level.get_node("Navigation/Geometry/ButtonOutside")
	var closet: Node3D = level.get_node("Navigation/Geometry/ClosetDoor")
	var plate: Node3D = level.get_node("Plate")
	var case := level.get_node("Props/CaseA") as RigidBody3D
	var closed := closet.position
	if _lead():
		_ask("spawn_late")
	await _sync("c2 arrived")
	await _enter_room(5.2)

	if index == _helper():
		await _walk([Vector3(-1.5, 0, 4.6)])
		_face(button.global_position + Vector3.UP * 0.9)
		button.rpc_id(1, "RequestPress")
		t._check(await t._wait_until(func(): return button.Pressed, 4.0), "client %d: [E] on the closet button didn't press it" % index)
	if _lead():
		t._check(await t._wait_until(func(): return closet.IsOpen and closet.position.distance_to(closed) > 2.5, 4.0), "client 0: the closet door didn't open on our side")
		# In through the closet door, then backwards out of it with the case, then over to the plate.
		t._check(await _walk([Vector3(-3.5, 0, 0), Vector3(-5.8, 0, 0)]), "client 0: couldn't walk to the closet")
		t._check(await _carry_case_out(case, plate), "client 0: couldn't get the case out of the closet onto the plate")
		rpc_id(1, "_arrive", "c2 case out", 1)
	else:
		await t._wait_until(func(): return _released.has("c2 case out"), 60.0)
	t._check(await t._wait_until(func(): return plate.Pressed, 3.0), "client %d: the case isn't holding the exit plate" % index)

	if _early() >= 2:
		await _revive_in_chamber()
	# The late client is in by now.
	await _sync("c2 all in", total, 90.0)
	_late_in = true
	await _walk_out(-6.2, -8.2)


func _carry_case_out(case: RigidBody3D, plate: Node3D) -> bool:
	var me := _me()
	var my_id := multiplayer.get_unique_id()
	await _walk([Vector3(-6.6, 0, -0.4)])
	_face(case.global_position)
	case.rpc_id(1, "RequestGrab")
	if not t._check(await t._wait_until(func(): return case.HeldBy == my_id, 3.0), "client 0: couldn't grab the case"):
		return false
	me.get_node("Head").rotation.x = -0.35
	# The outside button is about to run out: press the one inside the closet on the way out.
	var inside: Node3D = t._level().get_node("Navigation/Geometry/ButtonInside")
	inside.rpc_id(1, "RequestPress")
	t._check(await t._wait_until(func(): return inside.Pressed, 2.0), "client 0: [E] on the button inside the closet didn't press it")
	await t._seconds(0.5)
	# Back out of the closet facing the case, so it follows us through the door.
	_trail.clear()
	if not await _walk([Vector3(-4.0, 0, -0.4)], 2.5, me.rotation.y, case):
		return false
	var stand: Array = _stand_for(plate.global_position, plate.global_position - me.global_position, 0.9)
	me.get_node("Head").rotation.x = stand[1]
	return await _carry_on(case, stand[0], plate.global_position)


# Carries what we're holding to `stand`, then lets it go over `spot`.
func _carry_on(prop: RigidBody3D, stand: Vector3, spot: Vector3) -> bool:
	var me := _me()
	var walked: bool = await _walk([stand], 3.0, null, prop)
	var forward := (spot - me.global_position) * Vector3(1, 0, 1)
	me.rotation.y = atan2(-forward.x, -forward.z)
	await t._wait_until(func(): return prop.global_position.distance_to(me.HoldPoint) < 0.1 and prop.linear_velocity.length() < 0.3, 4.0)
	_trail.sort()
	if not _trail.is_empty():
		_stats.append("carried %s (%.0f kg) out of the closet: trailed by up to %.2f m" % [prop.name, prop.mass, _trail[-1]])
	prop.Release()
	await t._seconds(1.5)
	return walked and prop.HeldBy == 0 and ((prop.global_position - spot) * Vector3(1, 0, 1)).length() < 0.6


# The helper goes down (the host kills it); the lead walks over and revives it.
func _revive_in_chamber() -> void:
	var helper_id: int = _roster[_helper()]
	await _sync("c2 before revive", _early())
	if _lead():
		await _ask("kill", helper_id)
	var downed: Node3D = t._players().get_node(str(helper_id))
	var health: Node = downed.get_node("Health")
	t._check(await t._wait_until(func(): return health.IsDead, 3.0), "client %d: the helper never went down" % index)
	var fell_at := downed.global_position
	if _lead():
		await _walk([fell_at + (_me().global_position - fell_at).normalized() * 1.3])
		await t._seconds(0.2 + ping / 1000.0)
		downed.rpc_id(1, "RequestRevive")
	t._check(await t._wait_until(func(): return not health.IsDead, 5.0), "client %d: the helper wasn't revived" % index)
	await t._seconds(0.3)
	t._check(absf(health.Current - 50.0) < 0.1, "client %d: revived at %.0f health, not 50" % [index, health.Current])
	t._check(downed.global_position.distance_to(fell_at) < 1.0, "client %d: the revived helper moved from where it fell" % index)
	await _sync("c2 revived", _early())


# The late client arrives mid-way through chamber 2: it must see the chamber as it is on the host.
func _late_arrival() -> void:
	var run := _run()
	var in_closet: bool = await t._wait_until(func(): return run.Active and run.Chamber == 1 and t._level() != null, 10.0)
	if not t._check(in_closet, "late client: arrived to run chamber %d, active %s" % [run.Chamber, run.Active]):
		return
	await t._seconds(1.0 + ping / 1000.0)
	var snap: Dictionary = await _ask("snapshot")
	var exit: Node = t._level().get_node("Exit")
	var clock_off: float = absf(exit.Elapsed - (snap["elapsed"] + ping / 2000.0))
	_stats.append("late join: chamber clock %.2f s off the host's" % clock_off)
	t._check(clock_off < 0.5, "late client: chamber clock reads %.1f s, the host's %.1f s" % [exit.Elapsed, snap["elapsed"]])
	for prop_name in snap["props"]:
		var mine: Node3D = t._level().get_node("Props/" + prop_name)
		t._check(mine.global_position.distance_to(snap["props"][prop_name]) < 0.3, "late client: %s is at %s, on the host %s" % [prop_name, mine.global_position, snap["props"][prop_name]])
	for door_name in snap["doors"]:
		t._check(t._level().get_node("Navigation/Geometry/" + door_name).IsOpen == snap["doors"][door_name], "late client: %s open is %s here, %s on the host" % [door_name, not snap["doors"][door_name], snap["doors"][door_name]])
	_late_in = true
	await _sync("c2 all in", total, 90.0)
	await _enter_room(5.2)
	await _walk_out(-6.2, -8.2)


# Chamber 3: the plate (the case) and a button up on a ledge, pressed by lobbing a jar at it. The
# door then stays open for 5 s: everyone has to be through by then.
func _ledge_chamber() -> void:
	if not await _arrive_in_chamber(2):
		return
	var level: Node = t._level()
	var plate: Node3D = level.get_node("Plate")
	var door: Node3D = level.get_node("Navigation/Geometry/ExitDoor")
	var button: Node3D = level.get_node("Navigation/Geometry/LedgeButton")
	var lane := _lane()
	await _sync("c3 arrived")
	await _enter_room(5.5)

	if index == _helper():
		await _carry(level.get_node("Props/CaseA"), plate.global_position, 0.9, [], 0.6)
	await _sync("c3 case down")
	t._check(await t._wait_until(func(): return plate.Pressed, 3.0), "client %d: the case isn't holding the plate" % index)
	t._check(not door.IsOpen, "client %d: the plate alone opened the exit" % index)

	if _lead():
		var hit: bool = false
		for jar_name in ["JarA", "JarB"]:
			hit = await _lob_at_button(level.get_node("Props/" + jar_name), button)
			if hit:
				break
		t._check(hit, "client 0: couldn't hit the ledge button with a thrown jar")
		rpc_id(1, "_arrive", "c3 thrown", 1)
	else:
		await _walk([Vector3(lane.x, 0, 1.0 + lane.y)])
		await t._wait_until(func(): return _released.has("c3 thrown"), 40.0)
	await _walk_out(-7.2, -9.2)


# Carries the jar to the spot by the exit door, aims so it lobs onto the button, and throws.
func _lob_at_button(jar: RigidBody3D, button: Node3D) -> bool:
	var me := _me()
	var my_id := multiplayer.get_unique_id()
	var target: Vector3 = button.get_node("HitZone").global_position
	var speed := minf(jar.ThrowImpulse, jar.MaxThrowSpeed * jar.mass) / jar.mass
	if not await _walk([_approach(jar, 1.2)]):
		return false
	_face(jar.global_position)
	jar.rpc_id(1, "RequestGrab")
	if not await t._wait_until(func(): return jar.HeldBy == my_id, 3.0):
		return false
	me.get_node("Head").rotation.x = 0.0
	await _walk([Vector3(-0.5, 0, -5.4)], 3.0)
	for attempt in 4:
		# Aim so a throw from the hold point lands on the button (a little long, for drag).
		var aim := target + (target - me.global_position).slide(Vector3.UP).normalized() * 0.3 * (attempt + 1)
		for i in 4:
			var direction: Vector3 = t._lob(me.HoldPoint, aim, speed).normalized()
			me.rotation.y = atan2(-direction.x, -direction.z)
			me.get_node("Head").rotation.x = asin(direction.y)
			await t._frames(1)
		# The host has to see our new aim and pull the jar to it before the throw.
		await t._seconds(0.3 + ping / 1000.0)
		await t._wait_until(func(): return jar.global_position.distance_to(me.HoldPoint) < 0.05 and jar.linear_velocity.length() < 0.15, 3.0)
		await t._seconds(ping / 1000.0)
		jar.Throw(me.AimDirection)
		var launched: Vector3 = jar.linear_velocity
		if await t._wait_until(func(): return button.Pressed, 2.5):
			_log("the jar hit the ledge button (attempt %d)" % (attempt + 1))
			return true
		_log("missed the ledge button: thrown at %s (%.1f m/s), landed at %s, aiming at %s" % [launched, launched.length(), jar.global_position, aim])
		# Missed: pick it up again from wherever it landed, if we can reach it.
		if jar.global_position.y > 1.0:
			return false
		if not await _walk([_approach(jar, 1.1)]):
			return false
		_face(jar.global_position)
		jar.rpc_id(1, "RequestGrab")
		if not await t._wait_until(func(): return jar.HeldBy == my_id, 3.0):
			return false
		me.get_node("Head").rotation.x = 0.0
		await _walk([Vector3(-0.5, 0, -5.4)], 3.0)
	return false


# The run is passed; the result reaches everyone, and a new run starts. Then someone leaves while
# carrying a crate, and everyone else going down fails the run.
func _run_over() -> void:
	var run := _run()
	t._check(await t._wait_until(func(): return run.Result == 1, 10.0), "client %d: the run's pass never reached us (result %d)" % [index, run.Result])
	# Everyone was paid for 3 chambers and the pass, into their own profile (the late joiner too).
	var profile := get_node("/root/ProfileStore")
	var paid: int = 3 * run.PayPerChamber + run.PassBonusPay
	t._check(await t._wait_until(func(): return profile.Money == paid, 5.0), "client %d: paid %d money for the run, not %d" % [index, profile.Money, paid])
	t._check(profile.RunCount == 1, "client %d: the run counted %d times on our profile" % [index, profile.RunCount])
	if not await _arrive_in_chamber(0):
		return
	t._check(run.Result == 0, "client %d: the new run still shows the old result" % index)
	await _sync("new run")

	var leaver := total - 1 if total >= 2 else -1
	if index == leaver:
		var crate := t._level().get_node("Props/CrateA") as RigidBody3D
		await _enter_room(5.2)
		await _walk([crate.global_position * Vector3(1, 0, 1) + Vector3(0, 0, 1.3)])
		_face(crate.global_position)
		crate.rpc_id(1, "RequestGrab")
		t._check(await t._wait_until(func(): return crate.HeldBy == multiplayer.get_unique_id(), 3.0), "client %d: couldn't grab a crate to leave with" % index)
		await _send_report() # now, so leaving right after the barrier is quick
		await _sync("leaving")
		return # run_client leaves
	if leaver >= 0:
		await _sync("leaving")
		var crate := t._level().get_node("Props/CrateA") as RigidBody3D
		var gone_id: int = _roster.get(leaver, -1)
		# A goodbye lost on a bad connection falls back to the 6 s silence timeout.
		t._check(await t._wait_until(func(): return not t._players().has_node(str(gone_id)), 20.0), "client %d: the leaver's player is still here" % index)
		t._check(await t._wait_until(func(): return crate.HeldBy == 0, 3.0), "client %d: the crate is still held by someone who left" % index)
	var remaining := total - (1 if leaver >= 0 else 0)
	await _sync("before the fall", remaining)
	if _lead():
		await _ask("generate_next")
		await _ask("kill_all")
	t._check(await t._wait_until(func(): return run.Result == 2, 5.0), "client %d: everyone down didn't fail the run (result %d)" % [index, run.Result])
	await t._seconds(0.5 + ping / 1000.0) # the (empty) pay for it
	t._check(profile.RunCount == 2 and profile.Money == paid, "client %d: after a failed run in chamber 1: %d runs, %d money (want 2, %d)" % [index, profile.RunCount, profile.Money, paid])
	await _generated_matches()
	await _sync("done", remaining)


# The next run starts in a generated chamber: every client must have built exactly the host's room
# from the seed (same plan, props where the host has them).
func _generated_matches() -> void:
	var built: bool = await t._wait_until(func(): return t._level() != null and t._level().has_meta("plan") and _me() != null, 15.0)
	if not t._check(built, "client %d: the generated chamber never arrived" % index):
		return
	await t._seconds(1.0 + ping / 1000.0)
	var snap: Dictionary = await _ask("snapshot")
	var plan: Dictionary = t._level().get_meta("plan")
	t._check(snap["plan"] == plan, "client %d: built a different chamber from the host's (%s vs %s)" % [index, plan, snap["plan"]])
	for prop_name in snap["props"]:
		var mine: Node3D = t._level().get_node_or_null("Props/" + prop_name)
		t._check(mine != null and mine.global_position.distance_to(snap["props"][prop_name]) < 0.1, "client %d: generated %s is at %s, on the host %s" % [index, prop_name, mine.global_position if mine else "missing", snap["props"][prop_name]])
	_stats.append("generated chamber %s built the same as the host's" % [plan["pieces"]])
