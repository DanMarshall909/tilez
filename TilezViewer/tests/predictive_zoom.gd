extends SceneTree

func _initialize() -> void:
	call_deferred("_run")

func _run() -> void:
	var scene = load("res://scenes/zoom.tscn").instantiate()
	scene.auto_generate = false
	root.add_child(scene)
	await process_frame
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
	if scene.source_crop_size != 410:
		push_error("Each step must keep most of the previous composition")
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
	if scene.zoom_progress != 0.0:
		push_error("Zoom began before a new frame was ready")
		quit(1)
		return
	scene.accept_frame(1, ImageTexture.create_from_image(image))
	scene.step_zoom(1.0)
	if scene.zoom_progress <= 0.0 or scene.current_depth != 0:
		push_error("Zoom did not advance toward the prepared frame")
		quit(1)
		return
	if scene.planned_prefetch_depth() != 2:
		push_error("The frame after next was not scheduled during the current transition")
		quit(1)
		return
	var atlas := scene.display_sprite.texture as AtlasTexture
	if atlas == null or atlas.region.size.x >= 512.0:
		push_error("Zoom did not narrow the central image region")
		quit(1)
		return
	scene.step_zoom(2.5)
	if not scene.incoming_sprite.visible or scene.incoming_sprite.modulate.a <= 0.0:
		push_error("Prepared frame did not blend into the old image")
		quit(1)
		return
	scene.step_zoom(10.0)
	if scene.current_depth != 1 or scene.zoom_progress != 0.0 or scene.frames.has(0):
		push_error("The prepared frame was not swapped in at the transition")
		quit(1)
		return
	print("Predictive center zoom passed")
	quit(0)
