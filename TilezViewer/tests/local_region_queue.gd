extends SceneTree

func _initialize() -> void:
	call_deferred("_run")

func _run() -> void:
	OS.set_environment("TILEZ_IMAGE_BACKEND", "comfyui")
	OS.set_environment("OPENROUTER_API_KEY", "")
	var scene = load("res://scenes/main.tscn").instantiate()
	scene.worker_busy = true
	root.add_child(scene)
	await process_frame
	if not scene.ready_to_render:
		push_error("The local backend required a remote API key")
		quit(1)
		return
	scene.update_visible_tiles()
	if scene.pending.is_empty() or scene.pending[0] != Vector3i(0, 0, -1):
		push_error("Local generation did not queue the first shared region")
		quit(1)
		return
	if not scene.output_root.contains("comfyui-regions-v4-"):
		push_error("The local cache was not isolated from remote images")
		quit(1)
		return
	print("Local region queue passed")
	quit(0)
