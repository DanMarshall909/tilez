extends SceneTree

func _initialize() -> void:
	call_deferred("_run")

func _run() -> void:
	var custom_root := ProjectSettings.globalize_path("res://../../generated/zoom-custom-cache-test")
	var legacy_frame := custom_root.path_join("center-zoom-v3/seed_1337/frames/frame_0000.png")
	DirAccess.make_dir_recursive_absolute(legacy_frame.get_base_dir())
	var legacy_image := Image.create(512, 512, false, Image.FORMAT_RGBA8)
	legacy_image.fill(Color.GREEN)
	legacy_image.save_png(legacy_frame)
	OS.set_environment("TILEZ_ZOOM_OUTPUT_ROOT", custom_root)
	var scene = load("res://scenes/zoom.tscn").instantiate()
	scene.auto_generate = false
	root.add_child(scene)
	await process_frame
	OS.set_environment("TILEZ_ZOOM_OUTPUT_ROOT", "")
	if scene.output_root != custom_root.path_join("center-zoom-v5") or scene._load_cached_frame(0):
		push_error("New zoom settings reused the old accepted-frame cache")
		quit(1)
		return
	DirAccess.remove_absolute(legacy_frame)
	scene.attempts[0] = 3
	scene._update_status()
	if not scene.status_label.text.contains("failed at depth 0"):
		push_error("Failure before the first frame was hidden")
		quit(1)
		return
	scene.attempts.clear()
	var image := Image.create(512, 512, false, Image.FORMAT_RGBA8)
	image.fill(Color.RED)
	scene.accept_frame(0, ImageTexture.create_from_image(image))
	if scene.current_depth != 0 or scene.display_size != 256 or scene.render_size != 512:
		push_error("Wrong initial frame or render/display size")
		quit(1)
		return
	if scene.source_crop_size != 256:
		push_error("Each step must zoom into the redrawn inner half")
		quit(1)
		return
	if scene.max_depth != -1:
		push_error("The zoom stopped after a fixed number of frames")
		quit(1)
		return
	scene.update_generation_duration(6.0)
	if scene.seconds_per_zoom < 6.0:
		push_error("Zoom ran ahead of generation time")
		quit(1)
		return
	scene.update_generation_duration(30.0)
	if scene.seconds_per_zoom < 30.0:
		push_error("Slow generation outlasted the animation pace")
		quit(1)
		return
	scene.seconds_per_zoom = 4.0
	if scene.zoom_progress != 0.0 or scene.display_sprite.texture_filter != CanvasItem.TEXTURE_FILTER_NEAREST:
		push_error("The first frame is not shown with nearest-neighbor sampling")
		quit(1)
		return
	scene.step_zoom(1.0)
	if scene.zoom_progress <= 0.0 or scene.incoming_sprite.visible:
		push_error("The front buffer did not zoom while the next frame rendered")
		quit(1)
		return
	scene.accept_frame(1, ImageTexture.create_from_image(image))
	scene.step_zoom(0.25)
	if scene.zoom_progress <= 0.0 or scene.current_depth != 0:
		push_error("Zoom did not advance toward the prepared frame")
		quit(1)
		return
	if not scene.incoming_sprite.visible or scene.incoming_sprite.modulate.a <= 0.0:
		push_error("The inner detail did not begin appearing during zoom")
		quit(1)
		return
	var incoming_atlas := scene.incoming_sprite.texture as AtlasTexture
	if incoming_atlas == null or incoming_atlas.region != Rect2(128, 128, 256, 256):
		push_error("The reveal included pixels outside the redrawn inner half")
		quit(1)
		return
	var aligned_scale: float = scene.display_sprite.scale.x * 0.5
	if absf(scene.incoming_sprite.scale.x - aligned_scale) > 0.001:
		push_error("The inner detail did not grow from the same source pixels")
		quit(1)
		return
	var early_alpha: float = scene.incoming_sprite.modulate.a
	var early_scale: float = scene.incoming_sprite.scale.x
	if scene.planned_prefetch_depth() != 1:
		push_error("Generation must use the current front buffer")
		quit(1)
		return
	var atlas := scene.display_sprite.texture as AtlasTexture
	if atlas == null or atlas.region.size.x >= 512.0:
		push_error("Zoom did not narrow the central image region")
		quit(1)
		return
	scene.step_zoom(0.5)
	if not scene.incoming_sprite.visible or scene.incoming_sprite.modulate.a <= early_alpha or scene.incoming_sprite.scale.x <= early_scale:
		push_error("The inner detail did not grow and emerge through the zoom")
		quit(1)
		return
	scene.step_zoom(10.0)
	if scene.current_depth != 1 or scene.zoom_progress != 0.0 or scene.frames.has(0):
		push_error("The prepared frame was not swapped in at the transition")
		quit(1)
		return
	if scene.planned_prefetch_depth() != 2 or scene.frames.size() != 1:
		push_error("The buffers were not swapped before scheduling another frame")
		quit(1)
		return
	scene.step_zoom(10.0)
	if scene.current_depth != 1 or scene.zoom_progress != 1.0:
		push_error("The front buffer did not hold at the handoff while rendering ran late")
		quit(1)
		return
	scene.accept_frame(2, ImageTexture.create_from_image(image))
	scene.step_zoom(0.25)
	if scene.current_depth != 1 or not scene.incoming_sprite.visible or scene.reveal_progress <= 0.0:
		push_error("The late frame appeared abruptly instead of revealing its center")
		quit(1)
		return
	scene.step_zoom(1.0)
	if scene.current_depth != 2 or scene.frames.has(1):
		push_error("The late back buffer did not become the next front buffer")
		quit(1)
		return
	print("Predictive center zoom passed")
	quit(0)
