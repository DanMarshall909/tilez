extends Node2D

const TILE_SIZE := 128
const REGION_SIZE := 3
const WORLD_SEED := 1337
const HIGH_DETAIL_ZOOM := 2.5
const DEFAULT_OPENROUTER_MODEL := "black-forest-labs/flux.2-klein-4b"
const DEFAULT_LOCAL_MODEL := "flux-2-klein-4b-fp8.safetensors"

var camera: Camera2D
var label: Label
var dotnet_dll: String
var output_root: String
var image_directory: String
var image_backend: String
var image_model: String
var job_limit := 9
var jobs_started := 0
var ready_to_render := false
var zoom_level := 2.5
var sprites: Dictionary = {}
var ready_tiles: Dictionary = {}
var displayed_detail: Dictionary = {}
var queued: Dictionary = {}
var failed: Dictionary = {}
var pending: Array[Vector3i] = []
var worker_busy := false
var worker_task_id := -1
var stopping := false
var scan_elapsed := 0.0

func _exit_tree() -> void:
	stopping = true
	if worker_task_id >= 0:
		WorkerThreadPool.wait_for_task_completion(worker_task_id)

func _ready() -> void:
	camera = $Camera2D
	label = $HUD/Label
	camera.position = Vector2.ONE * TILE_SIZE * 1.5
	camera.zoom = Vector2.ONE * zoom_level
	image_backend = OS.get_environment("TILEZ_IMAGE_BACKEND").to_lower()
	if image_backend.is_empty():
		image_backend = "openrouter"
	if image_backend == "ai":
		image_backend = "openai"
	if image_backend not in ["openrouter", "comfyui", "openai", "procedural"]:
		label.text = "Unsupported image backend: " + image_backend
		return
	image_model = OS.get_environment("TILEZ_AI_MODEL")
	if image_model.is_empty():
		if image_backend == "openrouter":
			image_model = DEFAULT_OPENROUTER_MODEL
		elif image_backend == "comfyui":
			image_model = DEFAULT_LOCAL_MODEL
		elif image_backend == "openai":
			image_model = "flux"
	var configured_limit := OS.get_environment("TILEZ_MAX_RENDER_JOBS").to_int()
	if configured_limit > 0:
		job_limit = configured_limit
	dotnet_dll = ProjectSettings.globalize_path("res://../../Tilez/bin/Debug/net10.0/Tilez.dll").simplify_path()
	var cache_name := image_backend
	if image_backend != "procedural":
		if uses_regions():
			cache_name += "-regions-v4" if image_backend == "comfyui" else "-regions-v3"
		var model_identity := image_model
		if image_backend == "comfyui":
			model_identity += ":" + OS.get_environment("TILEZ_COMFYUI_TEXT_ENCODER") + ":" + OS.get_environment("TILEZ_COMFYUI_VAE") + ":" + OS.get_environment("TILEZ_LOCAL_CELL_SIZE")
		cache_name += "-" + model_identity.sha256_text().substr(0, 12)
	output_root = ProjectSettings.globalize_path("res://../../generated").simplify_path().path_join(cache_name)
	image_directory = output_root.path_join("images")
	if not FileAccess.file_exists(dotnet_dll):
		label.text = "Tilez generator is missing. Run ./run.sh from the repository root."
		push_error("Tilez generator is missing: " + dotnet_dll)
		return
	if image_backend == "openrouter" and OS.get_environment("OPENROUTER_API_KEY").is_empty():
		label.text = "Set OPENROUTER_API_KEY and restart. For offline tiles: TILEZ_IMAGE_BACKEND=procedural ./run.sh"
		return
	ready_to_render = true
	label.text = "Tilez Viewer — arrows: pan, +/-: zoom, R: retry"
	update_visible_tiles()
	start_next_job()

func _process(delta: float) -> void:
	if not ready_to_render:
		return
	var direction := Vector2.ZERO
	if Input.is_action_pressed("ui_right"):
		direction.x += 1
	if Input.is_action_pressed("ui_left"):
		direction.x -= 1
	if Input.is_action_pressed("ui_down"):
		direction.y += 1
	if Input.is_action_pressed("ui_up"):
		direction.y -= 1
	if direction != Vector2.ZERO:
		camera.position += direction.normalized() * 200.0 * delta / zoom_level
		scan_elapsed = 1.0
	scan_elapsed += delta
	if scan_elapsed >= 0.25:
		scan_elapsed = 0.0
		update_visible_tiles()
	start_next_job()

func _unhandled_input(event: InputEvent) -> void:
	if not ready_to_render:
		return
	if not event is InputEventKey or not event.pressed or event.echo:
		return
	if event.keycode in [KEY_PLUS, KEY_EQUAL, KEY_KP_ADD]:
		zoom_level = minf(zoom_level + 0.25, 4.0)
	elif event.keycode in [KEY_MINUS, KEY_KP_SUBTRACT]:
		zoom_level = maxf(zoom_level - 0.25, 0.5)
	elif event.keycode == KEY_R:
		failed.clear()
		jobs_started = 0
		update_visible_tiles()
		start_next_job()
		return
	else:
		return
	camera.zoom = Vector2.ONE * zoom_level
	update_visible_tiles()

func update_visible_tiles() -> void:
	var viewport_size := get_viewport_rect().size / zoom_level
	var center := camera.position / TILE_SIZE
	var min_x := floori((camera.position.x - viewport_size.x / 2.0) / TILE_SIZE) - 1
	var max_x := floori((camera.position.x + viewport_size.x / 2.0) / TILE_SIZE) + 1
	var min_y := floori((camera.position.y - viewport_size.y / 2.0) / TILE_SIZE) - 1
	var max_y := floori((camera.position.y + viewport_size.y / 2.0) / TILE_SIZE) + 1
	var detail := 1 if zoom_level >= HIGH_DETAIL_ZOOM else 0
	for x in range(min_x, max_x + 1):
		for y in range(min_y, max_y + 1):
			var coord := Vector2i(x, y)
			ensure_placeholder(coord)
			var key := tile_key(coord, detail)
			if ready_tiles.has(key):
				if displayed_detail.get(coord, -1) != detail:
					load_generated_tile(coord, detail)
				continue
			if queued.has(key) or failed.has(key):
				continue
			if load_generated_tile(coord, detail):
				continue
			if uses_regions():
				var region := Vector2i(floori(float(x) / REGION_SIZE), floori(float(y) / REGION_SIZE))
				var region_key := "region:%d:%d" % [region.x, region.y]
				if queued.has(region_key) or failed.has(region_key):
					continue
				queued[region_key] = true
				pending.append(Vector3i(region.x, region.y, -1))
				continue
			queued[key] = true
			pending.append(Vector3i(x, y, detail))
	pending.sort_custom(func(a: Vector3i, b: Vector3i) -> bool:
		if uses_regions():
			var a_center := Vector2(a.x * REGION_SIZE + 1.5, a.y * REGION_SIZE + 1.5)
			var b_center := Vector2(b.x * REGION_SIZE + 1.5, b.y * REGION_SIZE + 1.5)
			return a_center.distance_squared_to(center) < b_center.distance_squared_to(center)
		return Vector2(a.x - center.x, a.y - center.y).length_squared() < Vector2(b.x - center.x, b.y - center.y).length_squared()
	)

func ensure_placeholder(coord: Vector2i) -> void:
	if sprites.has(coord):
		return
	var sprite := Sprite2D.new()
	var image := Image.create(TILE_SIZE, TILE_SIZE, false, Image.FORMAT_RGB8)
	var shade := 0.13 if (coord.x + coord.y) % 2 == 0 else 0.18
	image.fill(Color(shade, shade + 0.03, shade + 0.05))
	sprite.texture = ImageTexture.create_from_image(image)
	sprite.position = Vector2(coord.x * TILE_SIZE + TILE_SIZE / 2.0, coord.y * TILE_SIZE + TILE_SIZE / 2.0)
	$TileContainer.add_child(sprite)
	sprites[coord] = sprite

func tile_key(coord: Vector2i, detail: int) -> String:
	return "%d:%d:%d" % [coord.x, coord.y, detail]

func uses_regions() -> bool:
	return image_backend in ["openrouter", "comfyui"]

func region_marker_path(coord: Vector2i) -> String:
	var region := Vector2i(floori(float(coord.x) / REGION_SIZE), floori(float(coord.y) / REGION_SIZE))
	return output_root.path_join("regions/region_%d_%d_%d.complete" % [region.x, region.y, WORLD_SEED])

func artifact_path(coord: Vector2i, detail: int) -> String:
	if detail == 1 and (not uses_regions() or FileAccess.file_exists(region_marker_path(coord))):
		return image_directory.path_join("tile_%d_%d_%d_high_256.png" % [coord.x, coord.y, WORLD_SEED])
	return image_directory.path_join("tile_%d_%d_%d_base_64_upscaled_128.png" % [coord.x, coord.y, WORLD_SEED])

func load_generated_tile(coord: Vector2i, detail: int, display: bool = true) -> bool:
	var path := artifact_path(coord, detail)
	if not FileAccess.file_exists(path):
		return false
	var image := Image.new()
	if image.load(path) != OK or image.is_empty():
		return false
	if display:
		var sprite: Sprite2D = sprites[coord]
		sprite.texture = ImageTexture.create_from_image(image)
		sprite.scale = Vector2(float(TILE_SIZE) / image.get_width(), float(TILE_SIZE) / image.get_height())
		displayed_detail[coord] = detail
	ready_tiles[tile_key(coord, detail)] = true
	return true

func start_next_job() -> void:
	if worker_busy or pending.is_empty():
		return
	if uses_regions() and jobs_started >= job_limit:
		label.text = "%d render jobs started. Press R for another batch, or raise TILEZ_MAX_RENDER_JOBS." % job_limit
		return
	var job: Vector3i = pending.pop_front()
	var coord := Vector2i(job.x, job.y)
	var key := "region:%d:%d" % [job.x, job.y] if job.z == -1 else tile_key(coord, job.z)
	if job.z != -1 and ready_tiles.has(key):
		queued.erase(key)
		start_next_job()
		return
	worker_busy = true
	if uses_regions():
		jobs_started += 1
	label.text = "Expanding region (%d, %d)…" % [coord.x, coord.y] if job.z == -1 else "Generating tile (%d, %d)…  Arrows: pan, +/-: zoom, R: retry" % [coord.x, coord.y]
	worker_task_id = WorkerThreadPool.add_task(render_tile.bind(job))

func render_tile(job: Vector3i) -> void:
	var output: Array = []
	var args: PackedStringArray
	if job.z == -1:
		args = PackedStringArray([
			dotnet_dll, "render-region", "--image-backend", image_backend, "--ai-model", image_model,
			"--region-x", str(job.x), "--region-y", str(job.y),
			"--seed", str(WORLD_SEED), "--output-root", output_root
		])
	else:
		var zoom_arg := "3.0" if job.z == 1 else "1.0"
		args = PackedStringArray([
			dotnet_dll, "render", "--image-backend", image_backend, "--ai-model", image_model,
			"--x", str(job.x), "--y", str(job.y),
			"--seed", str(WORLD_SEED), "--zoom", zoom_arg, "--output-root", output_root
		])
	var result := OS.execute("dotnet", args, output, true)
	if not stopping:
		call_deferred("finish_job", job, result, "\n".join(output))

func finish_job(job: Vector3i, result: int, output: String) -> void:
	WorkerThreadPool.wait_for_task_completion(worker_task_id)
	worker_task_id = -1
	var coord := Vector2i(job.x, job.y)
	var key := "region:%d:%d" % [job.x, job.y] if job.z == -1 else tile_key(coord, job.z)
	queued.erase(key)
	worker_busy = false
	if job.z == -1:
		if result == 0:
			for x in range(job.x * REGION_SIZE, (job.x + 1) * REGION_SIZE):
				for y in range(job.y * REGION_SIZE, (job.y + 1) * REGION_SIZE):
					var tile := Vector2i(x, y)
					ready_tiles.erase(tile_key(tile, 0))
					ready_tiles.erase(tile_key(tile, 1))
					displayed_detail.erase(tile)
			update_visible_tiles()
			label.text = "Region (%d, %d) ready.  Arrows: pan, +/-: zoom, R: retry" % [job.x, job.y]
		else:
			failed[key] = true
			label.text = "Region (%d, %d) failed. Press R to retry." % [job.x, job.y]
			push_error("Region generation failed (%s): %s" % [key, output])
		start_next_job()
		return
	var display := job.z == (1 if zoom_level >= HIGH_DETAIL_ZOOM else 0)
	if result == 0 and load_generated_tile(coord, job.z, display):
		label.text = "Tile (%d, %d) ready.  Arrows: pan, +/-: zoom, R: retry" % [coord.x, coord.y]
	else:
		failed[key] = true
		label.text = "Tile (%d, %d) failed. Press R to retry." % [coord.x, coord.y]
		push_error("Tile generation failed (%s): %s" % [key, output])
	start_next_job()
