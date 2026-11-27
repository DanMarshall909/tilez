extends Node2D

@export var auto_generate := true
@export var seconds_per_zoom := 8.0
@export var max_depth := -1

var render_size := 512
var display_size := 256
var source_crop_size := 256
const world_seed := 1337

@onready var display_sprite: Sprite2D = $Image
@onready var incoming_sprite: Sprite2D = $Incoming
@onready var status_label: Label = $Status

var output_root := ""
var image_backend := "comfyui"
var current_depth := -1
var zoom_progress := 0.0
var reveal_progress := 0.0
var frames: Dictionary = {}
var active_pid := -1
var active_depth := -1
var attempts: Dictionary = {}
var retry_at := 0.0
var generation_started_at := 0.0

func _exit_tree() -> void:
	if active_pid > 0 and OS.is_process_running(active_pid):
		OS.kill(active_pid)

func _ready() -> void:
	image_backend = OS.get_environment("TILEZ_ZOOM_BACKEND").to_lower()
	if image_backend.is_empty():
		image_backend = "comfyui"
	if image_backend == "openai-zoom" or image_backend == "openrouter-zoom":
		render_size = 1024
		display_size = 512
		source_crop_size = 512
	if output_root.is_empty():
		var custom_root := OS.get_environment("TILEZ_ZOOM_OUTPUT_ROOT")
		var cache_name := "center-zoom-v5"
		if image_backend == "openai-zoom" or image_backend == "openrouter-zoom":
			var model := OS.get_environment("TILEZ_OPENAI_IMAGE_MODEL")
			if model.is_empty():
				model = "gpt-image-2.6-sunburst" if image_backend == "openai-zoom" else "openai/gpt-image-2.6-sunburst"
			var quality := OS.get_environment("TILEZ_OPENAI_IMAGE_QUALITY")
			if quality.is_empty():
				quality = "medium"
			var identity := (model + "|" + quality).sha256_text().substr(0, 12)
			cache_name = ("center-zoom-openai-v3-" if image_backend == "openai-zoom" else "center-zoom-openrouter-v3-") + identity
		output_root = custom_root.path_join(cache_name) if not custom_root.is_empty() else ProjectSettings.globalize_path("res://../../generated/" + cache_name)
	if OS.has_environment("TILEZ_ZOOM_MAX_DEPTH"):
		max_depth = int(OS.get_environment("TILEZ_ZOOM_MAX_DEPTH"))
	get_viewport().size_changed.connect(_update_display)
	_update_display()
	if auto_generate:
		_load_cached_frame(0)
		_ensure_prefetch()
	_update_status()

func _process(delta: float) -> void:
	if auto_generate:
		_poll_generation()
		_ensure_prefetch()
	step_zoom(delta)

func step_zoom(delta: float) -> void:
	if current_depth < 0 or (max_depth >= 0 and current_depth >= max_depth):
		return
	zoom_progress = minf(1.0, zoom_progress + delta / maxf(seconds_per_zoom, 0.01))
	if frames.has(current_depth + 1):
		reveal_progress = minf(zoom_progress, reveal_progress + delta)
	_update_display()
	if zoom_progress >= 1.0 and reveal_progress >= 1.0:
		current_depth += 1
		frames.erase(current_depth - 1)
		zoom_progress = 0.0
		reveal_progress = 0.0
		_update_display()
		_update_status()

func accept_frame(depth: int, image_texture: Texture2D) -> void:
	if image_texture.get_width() != render_size or image_texture.get_height() != render_size:
		push_error("Zoom frames must match the selected render size")
		return
	frames[depth] = image_texture
	if current_depth < 0 and depth == 0:
		current_depth = 0
		_update_display()
	_update_status()

func _update_display() -> void:
	if current_depth < 0 or not frames.has(current_depth) or display_sprite == null:
		return
	var viewport_size := get_viewport_rect().size
	var pixel_scale := maxf(1.0, floorf(minf(viewport_size.x, viewport_size.y) / float(display_size)))
	var extent := float(render_size) - float(render_size - source_crop_size) * zoom_progress
	var inset := (float(render_size) - extent) / 2.0
	var atlas := AtlasTexture.new()
	atlas.atlas = frames[current_depth]
	atlas.region = Rect2(inset, inset, extent, extent)
	display_sprite.texture = atlas
	display_sprite.position = viewport_size / 2.0
	display_sprite.scale = Vector2.ONE * (float(display_size) / extent) * pixel_scale
	display_sprite.texture_filter = CanvasItem.TEXTURE_FILTER_NEAREST
	incoming_sprite.visible = frames.has(current_depth + 1) and reveal_progress > 0.0
	if incoming_sprite.visible:
		var detail_atlas := AtlasTexture.new()
		detail_atlas.atlas = frames[current_depth + 1]
		var inner_size := render_size / 2
		detail_atlas.region = Rect2(render_size / 4, render_size / 4, inner_size, inner_size)
		incoming_sprite.texture = detail_atlas
		incoming_sprite.position = viewport_size / 2.0
		incoming_sprite.scale = display_sprite.scale * 0.5
		incoming_sprite.modulate.a = reveal_progress * reveal_progress * (3.0 - 2.0 * reveal_progress)
		incoming_sprite.texture_filter = CanvasItem.TEXTURE_FILTER_NEAREST

func _frame_path(depth: int) -> String:
	return output_root.path_join("seed_%d" % world_seed).path_join("frames").path_join("frame_%04d.png" % depth)

func _load_cached_frame(depth: int) -> bool:
	if frames.has(depth):
		return true
	var path := _frame_path(depth)
	if not FileAccess.file_exists(path):
		return false
	var image := Image.new()
	if image.load(path) != OK or image.get_width() != render_size or image.get_height() != render_size:
		return false
	accept_frame(depth, ImageTexture.create_from_image(image))
	return true

func _ensure_prefetch() -> void:
	if active_pid > 0 or Time.get_ticks_msec() / 1000.0 < retry_at:
		return
	var wanted := planned_prefetch_depth()
	if (max_depth >= 0 and wanted > max_depth) or _load_cached_frame(wanted):
		return
	if attempts.get(wanted, 0) >= max_attempts():
		return
	active_pid = OS.create_process("dotnet", build_generation_arguments(wanted))
	if active_pid <= 0:
		status_label.text = "Could not launch the zoom renderer"
		retry_at = Time.get_ticks_msec() / 1000.0 + 5.0
		return
	active_depth = wanted
	generation_started_at = Time.get_ticks_msec() / 1000.0
	attempts[wanted] = attempts.get(wanted, 0) + 1
	_update_status()

func planned_prefetch_depth() -> int:
	return 0 if current_depth < 0 else current_depth + 1

func max_attempts() -> int:
	return 1 if image_backend == "openai-zoom" or image_backend == "openrouter-zoom" else 3

func build_generation_arguments(depth: int) -> PackedStringArray:
	var dll := ProjectSettings.globalize_path("res://../../Tilez/bin/Debug/net10.0/Tilez.dll")
	return PackedStringArray([dll, "zoom-frame", "--image-backend", image_backend, "--depth", str(depth), "--seed", str(world_seed), "--output-root", output_root])

func _poll_generation() -> void:
	if active_pid <= 0 or OS.is_process_running(active_pid):
		return
	var finished_depth := active_depth
	active_pid = -1
	active_depth = -1
	if _load_cached_frame(finished_depth):
		if finished_depth > 0:
			update_generation_duration(Time.get_ticks_msec() / 1000.0 - generation_started_at)
	else:
		retry_at = Time.get_ticks_msec() / 1000.0 + 5.0
	_update_status()

func update_generation_duration(duration: float) -> void:
	seconds_per_zoom = maxf(2.0, duration * 1.15)

func _update_status() -> void:
	if status_label == null:
		return
	var needed := 0 if current_depth < 0 else current_depth + 1
	if active_depth < 0 and not frames.has(needed) and attempts.get(needed, 0) >= max_attempts():
		status_label.text = "Generation failed at depth %d · restart to retry" % needed
	elif current_depth < 0:
		status_label.text = "Generating first %d×%d frame…" % [render_size, render_size]
	elif max_depth >= 0 and current_depth >= max_depth:
		status_label.text = "Center zoom complete · depth %d" % current_depth
	elif active_depth >= 0:
		status_label.text = "Center zoom · depth %d · preparing frame %d" % [current_depth, active_depth]
	else:
		status_label.text = "Center zoom · depth %d · next frame ready" % current_depth
