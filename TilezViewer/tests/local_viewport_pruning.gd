extends SceneTree

func _initialize() -> void:
	call_deferred("_run")

func _run() -> void:
	OS.set_environment("TILEZ_IMAGE_BACKEND", "comfyui")
	OS.set_environment("TILEZ_AI_MODEL", "tilez-pruning-test-" + str(Time.get_ticks_usec()))
	root.size = Vector2i(2400, 1400)
	var scene = load("res://scenes/main.tscn").instantiate()
	scene.worker_busy = true
	root.add_child(scene)
	await process_frame
	if scene.pending.size() < 2:
		push_error("The first view did not queue enough regions for the pruning test")
		quit(1)
		return
	var active: Vector3i = scene.pending.pop_front()
	var active_key := "region:%d:%d" % [active.x, active.y]
	var old_pending: Dictionary = {}
	for job in scene.pending:
		old_pending["region:%d:%d" % [job.x, job.y]] = true

	scene.camera.position = Vector2(128000, 128000)
	scene.update_visible_tiles()
	if not scene.queued.has(active_key):
		push_error("Pruning removed the active render")
		quit(1)
		return
	for job in scene.pending:
		if old_pending.has("region:%d:%d" % [job.x, job.y]):
			push_error("A queued region outside the new viewport was retained")
			quit(1)
			return
	for key in old_pending:
		if scene.queued.has(key):
			push_error("A pruned region stayed marked queued")
			quit(1)
			return

	scene.camera.position = Vector2(192, 192)
	scene.update_visible_tiles()
	var revisited := false
	for job in scene.pending:
		if old_pending.has("region:%d:%d" % [job.x, job.y]):
			revisited = true
	if not revisited:
		push_error("Revisiting did not requeue the previously pruned regions")
		quit(1)
		return
	print("Local viewport pruning passed")
	quit(0)
