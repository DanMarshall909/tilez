extends SceneTree

func _initialize() -> void:
	call_deferred("_run")

func _run() -> void:
	OS.set_environment("TILEZ_ZOOM_BACKEND", "openai-zoom")
	OS.set_environment("TILEZ_OPENAI_IMAGE_MODEL", "gpt-image-2.5-sunburst")
	OS.set_environment("TILEZ_OPENAI_IMAGE_QUALITY", "medium")
	var custom_root := ProjectSettings.globalize_path("res://../../generated/zoom-openai-cache-test")
	OS.set_environment("TILEZ_ZOOM_OUTPUT_ROOT", custom_root)
	var scene = load("res://scenes/zoom.tscn").instantiate()
	scene.auto_generate = false
	root.add_child(scene)
	await process_frame
	var identity := "gpt-image-2.5-sunburst|medium".sha256_text().substr(0, 12)
	var expected_root := custom_root.path_join("center-zoom-openai-v2-" + identity)
	if scene.output_root != expected_root:
		push_error("OpenAI zoom reused the local model cache")
		quit(1)
		return
	if scene.render_size != 1024 or scene.source_crop_size != 512 or scene.display_size != 512:
		push_error("OpenAI zoom discarded the model's extra resolution")
		quit(1)
		return
	if scene.max_attempts() != 1:
		push_error("A failed paid generation could be retried automatically")
		quit(1)
		return
	scene.attempts[0] = 1
	scene._update_status()
	scene._ensure_prefetch()
	if scene.active_pid > 0 or not scene.status_label.text.contains("failed at depth 0"):
		push_error("The paid route relaunched an ambiguous failed request")
		quit(1)
		return
	scene.attempts.clear()
	scene._update_status()
	if not scene.status_label.text.contains("1024×1024"):
		push_error("Paid frame status reported the wrong resolution")
		quit(1)
		return
	var arguments: PackedStringArray = scene.build_generation_arguments(0)
	if arguments.find("openai-zoom") < 0 or arguments.find(expected_root) < 0:
		push_error("The viewer did not request the OpenAI zoom backend and its own cache")
		quit(1)
		return
	var image := Image.create(1024, 1024, false, Image.FORMAT_RGBA8)
	image.fill(Color.BLUE)
	var texture := ImageTexture.create_from_image(image)
	scene.accept_frame(0, texture)
	scene.accept_frame(1, texture)
	scene.step_zoom(0.25)
	var inner := scene.incoming_sprite.texture as AtlasTexture
	if inner == null or inner.region != Rect2(256, 256, 512, 512):
		push_error("The high-resolution inner scene was reduced before display")
		quit(1)
		return
	scene.queue_free()
	OS.set_environment("TILEZ_ZOOM_BACKEND", "openrouter-zoom")
	OS.set_environment("TILEZ_OPENAI_IMAGE_MODEL", "openai/gpt-image-2.6-sunburst")
	var routed = load("res://scenes/zoom.tscn").instantiate()
	routed.auto_generate = false
	root.add_child(routed)
	await process_frame
	var routed_identity := "openai/gpt-image-2.6-sunburst|medium".sha256_text().substr(0, 12)
	var routed_root := custom_root.path_join("center-zoom-openrouter-v2-" + routed_identity)
	if routed.output_root != routed_root or routed.build_generation_arguments(0).find("openrouter-zoom") < 0:
		push_error("OpenRouter-routed OpenAI frames mixed with direct OpenAI frames")
		quit(1)
		return
	print("OpenAI center zoom configuration passed")
	quit(0)
