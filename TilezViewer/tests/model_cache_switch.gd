extends SceneTree

func _initialize() -> void:
	call_deferred("_run")

func _run() -> void:
	OS.set_environment("TILEZ_AI_BASE_URL", "http://127.0.0.1:1")
	var first_model := "tilez-cache-a-" + str(Time.get_ticks_usec())
	var second_model := "tilez-cache-b-" + str(Time.get_ticks_usec())
	var directory := ProjectSettings.globalize_path("res://../../generated").simplify_path().path_join(
		"openai-" + first_model.sha256_text().substr(0, 12)).path_join("images")
	DirAccess.make_dir_recursive_absolute(directory)
	var image := Image.create(128, 128, false, Image.FORMAT_RGB8)
	image.fill(Color.RED)
	image.save_png(directory.path_join("tile_0_0_1337_base_64_upscaled_128.png"))

	OS.set_environment("TILEZ_IMAGE_BACKEND", "openai")
	OS.set_environment("TILEZ_AI_MODEL", first_model)
	var first_scene = load("res://scenes/main.tscn").instantiate()
	first_scene.zoom_level = 2.0
	root.add_child(first_scene)
	if not first_scene.ready_tiles.has(first_scene.tile_key(Vector2i.ZERO, 0)):
		push_error("First model's cached tile was not loaded")
		quit(1)
		return
	first_scene.queue_free()
	await process_frame

	OS.set_environment("TILEZ_AI_MODEL", second_model)
	var second_scene = load("res://scenes/main.tscn").instantiate()
	second_scene.zoom_level = 2.0
	root.add_child(second_scene)
	if second_scene.ready_tiles.has(second_scene.tile_key(Vector2i.ZERO, 0)):
		push_error("Second model reused the first model's tile")
		quit(1)
		return
	print("Local model cache switch passed")
	quit(0)
