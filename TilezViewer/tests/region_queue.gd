extends SceneTree

func _initialize() -> void:
	call_deferred("_run")

func _run() -> void:
	OS.set_environment("TILEZ_IMAGE_BACKEND", "openrouter")
	OS.set_environment("OPENROUTER_API_KEY", "")
	OS.set_environment("TILEZ_AI_MODEL", "tilez-region-test-" + str(Time.get_ticks_usec()))
	var scene = load("res://scenes/main.tscn").instantiate()
	root.add_child(scene)
	await process_frame
	scene.worker_busy = true
	scene.update_visible_tiles()
	if scene.pending.is_empty() or scene.pending[0] != Vector3i(0, 0, -1):
		push_error("The initial view did not prioritize the first region")
		quit(1)
		return
	scene.jobs_started = scene.job_limit
	if not scene.render_limit_reached():
		push_error("The paid backend lost its request cap")
		quit(1)
		return
	scene.pending.clear()
	scene.queued.clear()
	scene.camera.position = Vector2(-128, -128)
	scene.update_visible_tiles()
	var regions: Dictionary = {}
	for job in scene.pending:
		if job.z != -1:
			push_error("OpenRouter queued an individual tile")
			quit(1)
			return
		var id := "%d:%d" % [job.x, job.y]
		if regions.has(id):
			push_error("OpenRouter queued the same region twice")
			quit(1)
			return
		regions[id] = true
	if not regions.has("-1:-1"):
		push_error("Negative coordinates did not map to their region")
		quit(1)
		return
	if scene.artifact_path(Vector2i(-1, -1), 1) != scene.artifact_path(Vector2i(-1, -1), 0):
		push_error("Zoom used a different image before a region was completed")
		quit(1)
		return
	print("Region queue and zoom source passed")
	quit(0)
