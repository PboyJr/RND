extends RefCounted

## A UDP relay that makes localhost behave like the internet: every packet is held back by a delay
## (plus random jitter) and some are dropped. A client connects to the relay's port instead of the
## host's; the relay forwards both ways on its own thread. It also counts bytes, so tests can report
## bandwidth. ENet runs on UDP, so this is transparent to Godot's multiplayer.

var delay_ms := 0.0     # one way, so the round trip is twice this
var jitter_ms := 0.0    # extra random delay per packet, 0..jitter (order is kept, like most real routes)
var loss := 0.0         # 0..1, the chance each packet is dropped
var bytes_up := 0       # client → host
var bytes_down := 0     # host → client

var _thread := Thread.new()
var _running := false
var _mutex := Mutex.new()


func start(listen_port: int, host_port: int) -> void:
	_running = true
	_thread.start(_relay.bind(listen_port, host_port))


func stop() -> void:
	if not _running:
		return
	_running = false
	_thread.wait_to_finish()


func counts() -> Vector2i:
	_mutex.lock()
	var result := Vector2i(bytes_up, bytes_down)
	_mutex.unlock()
	return result


func _relay(listen_port: int, host_port: int) -> void:
	var client_side := PacketPeerUDP.new()
	client_side.bind(listen_port, "127.0.0.1")
	var host_side := PacketPeerUDP.new()
	host_side.bind(0, "127.0.0.1")
	host_side.set_dest_address("127.0.0.1", host_port)
	var rng := RandomNumberGenerator.new()
	var up: Array = []    # [release at (usec), packet], oldest first
	var down: Array = []
	var client_ip := ""
	var client_port := 0

	while _running:
		while client_side.get_available_packet_count() > 0:
			var packet := client_side.get_packet()
			client_ip = client_side.get_packet_ip()
			client_port = client_side.get_packet_port()
			_queue(up, packet, rng)
		while host_side.get_available_packet_count() > 0:
			_queue(down, host_side.get_packet(), rng)

		var now := Time.get_ticks_usec()
		while not up.is_empty() and up[0][0] <= now:
			var packet: PackedByteArray = up.pop_front()[1]
			host_side.put_packet(packet)
			_count(packet.size(), 0)
		while not down.is_empty() and down[0][0] <= now and client_port != 0:
			var packet: PackedByteArray = down.pop_front()[1]
			client_side.set_dest_address(client_ip, client_port)
			client_side.put_packet(packet)
			_count(0, packet.size())
		OS.delay_usec(250)

	client_side.close()
	host_side.close()


func _queue(queue: Array, packet: PackedByteArray, rng: RandomNumberGenerator) -> void:
	if rng.randf() < loss:
		return
	var release := Time.get_ticks_usec() + int((delay_ms + rng.randf() * jitter_ms) * 1000.0)
	if not queue.is_empty():
		release = maxi(release, queue[-1][0]) # no overtaking
	queue.append([release, packet])


func _count(up: int, down: int) -> void:
	_mutex.lock()
	bytes_up += up
	bytes_down += down
	_mutex.unlock()
