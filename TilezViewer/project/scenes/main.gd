extends Node2D

const TILE_SIZE := 128
const WORLD_SEED := 1337
const HIGH_DETAIL_ZOOM := 2.5

var camera: Camera2D
var label: Label
var dotnet_dll: String
var output_root: String
var image_directory: String
var zoom_level := 1.0
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
	camera.zoom = Vector2.ONE * zoom_level
	dotnet_dll = ProjectSettings.globalize_path("res://../../Tilez/bin/Debug/net10.0/Tilez.dll").simplify_path()
	output_root = ProjectSettings.globalize_path("res://../../generated").simplify_path()
	image_directory = output_root.path_join("images")
	if not FileAccess.file_exists(dotnet_dll):
		label.text = "Tilez generator is missing. Run ./run.sh from the repository root."
		push_error("Tilez generator is missing: " + dotnet_dll)
		return
	label.text = "Tilez Viewer — arrows: pan, +/-: zoom, R: retry"
	update_visible_tiles()
	start_next_job()

func _process(delta: float) -> void:
	if not FileAccess.file_exists(dotnet_dll):
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
	if not event is InputEventKey or not event.pressed or event.echo:
		return
	if event.keycode in [KEY_PLUS, KEY_EQUAL, KEY_KP_ADD]:
		zoom_level = minf(zoom_level + 0.25, 4.0)
	elif event.keycode in [KEY_MINUS, KEY_KP_SUBTRACT]:
		zoom_level = maxf(zoom_level - 0.25, 0.5)
	elif event.keycode == KEY_R:
		failed.clear()
		update_visible_tiles()
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
			queued[key] = true
			pending.append(Vector3i(x, y, detail))
	pending.sort_custom(func(a: Vector3i, b: Vector3i) -> bool:
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

func artifact_path(coord: Vector2i, detail: int) -> String:
	if detail == 1:
		return image_directory.path_join("tile_%d_%d_%d_high_256.png" % [coord.x, coord.y, WORLD_SEED])
	return image_directory.path_join("tile_%d_%d_%d_low_64_upscaled_128.png" % [coord.x, coord.y, WORLD_SEED])

func load_generated_tile(coord: Vector2i, detail: int) -> bool:
	var path := artifact_path(coord, detail)
	if not FileAccess.file_exists(path):
		return false
	var image := Image.new()
	if image.load(path) != OK or image.is_empty():
		return false
	var sprite: Sprite2D = sprites[coord]
	sprite.texture = ImageTexture.create_from_image(image)
	sprite.scale = Vector2(float(TILE_SIZE) / image.get_width(), float(TILE_SIZE) / image.get_height())
	ready_tiles[tile_key(coord, detail)] = true
	displayed_detail[coord] = detail
	return true

func start_next_job() -> void:
	if worker_busy or pending.is_empty():
		return
	var job: Vector3i = pending.pop_front()
	var coord := Vector2i(job.x, job.y)
	var key := tile_key(coord, job.z)
	if ready_tiles.has(key):
		queued.erase(key)
		start_next_job()
		return
	worker_busy = true
	label.text = "Generating tile (%d, %d)…  Arrows: pan, +/-: zoom, R: retry" % [coord.x, coord.y]
	worker_task_id = WorkerThreadPool.add_task(render_tile.bind(job))

func render_tile(job: Vector3i) -> void:
	var output: Array = []
	var zoom_arg := "3.0" if job.z == 1 else "1.0"
	var args := PackedStringArray([
		dotnet_dll, "render", "--x", str(job.x), "--y", str(job.y),
		"--seed", str(WORLD_SEED), "--zoom", zoom_arg, "--output-root", output_root
	])
	var result := OS.execute("dotnet", args, output, true)
	if not stopping:
		call_deferred("finish_job", job, result, "\n".join(output))

func finish_job(job: Vector3i, result: int, output: String) -> void:
	var coord := Vector2i(job.x, job.y)
	var key := tile_key(coord, job.z)
	queued.erase(key)
	worker_busy = false
	var image_exists := FileAccess.file_exists(artifact_path(coord, job.z))
	if result == 0 and image_exists:
		ready_tiles[key] = true
		if job.z == (1 if zoom_level >= HIGH_DETAIL_ZOOM else 0):
			load_generated_tile(coord, job.z)
		label.text = "Tile (%d, %d) ready.  Arrows: pan, +/-: zoom, R: retry" % [coord.x, coord.y]
	else:
		failed[key] = true
		label.text = "Tile (%d, %d) failed. Press R to retry." % [coord.x, coord.y]
		push_error("Tile generation failed (%s): %s" % [key, output])
	start_next_job()
