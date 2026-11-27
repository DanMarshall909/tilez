extends SceneTree

func _initialize() -> void:
	call_deferred("_run")

func _run() -> void:
	OS.set_environment("TILEZ_AI_BASE_URL", "http://127.0.0.1:1")
	var scene = load("res://scenes/main.tscn").instantiate()
	root.add_child(scene)
	await process_frame
	var backend := OS.get_environment("TILEZ_IMAGE_BACKEND")
	var model := OS.get_environment("TILEZ_AI_MODEL")
	if backend == "openai":
		var expected_model := model if not model.is_empty() else "flux"
		if scene.image_model != expected_model or not scene.output_root.ends_with("openai-" + expected_model.sha256_text().substr(0, 12)):
			push_error("Local model default or cache identity is wrong")
			quit(1)
			return
	elif backend == "openrouter":
		var expected_model: String = model if not model.is_empty() else "black-forest-labs/flux.2-klein-4b"
		if not scene.output_root.ends_with("openrouter-regions-v2-" + expected_model.sha256_text().substr(0, 12)):
			push_error("OpenRouter region cache reused the old independent tile cache")
			quit(1)
			return
	elif backend.contains("/"):
		if scene.ready_to_render:
			push_error("Invalid backend was accepted")
			quit(1)
			return
	else:
		push_error("Unexpected test backend")
		quit(1)
		return
	print("Backend configuration passed: ", backend, " ", scene.image_model)
	quit(0)
