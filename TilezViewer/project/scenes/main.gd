extends Node2D

# Tile display settings
var tile_size: int = 128  # Display size of each tile
var tiles: Dictionary = {}  # Dictionary mapping (x, y) -> texture
var loaded_coords: Dictionary = {}  # Track loaded tiles
var viewport_margin: int = 2  # Generate tiles beyond viewport

# Camera state
var camera: Camera2D
var current_zoom: float = 1.0
var min_zoom: float = 0.5
var max_zoom: float = 4.0
var pan_speed: float = 200.0

# Tile generator settings
var dotnet_project_path: String = "../../Tilez/Tilez.csproj"
var generated_images_path: String = "C:\\code\\tilez\\Tilez\\generated\\images"  # Absolute path
var base_url: String = "http://localhost:1234"
var ai_model: String = "flux.1-schnell"  # Using schnell (faster) variant
var use_ai: bool = true

# UI
var label: Label
var last_update_time: float = 0.0
var update_cooldown: float = 0.5  # Prevent too many updates per second
var generating: bool = false

# Logging
var log_file: String = "user://"  # Godot user directory
var debug_enabled: bool = true

func debug_log(msg: String) -> void:
	var timestamp = Time.get_ticks_msec()
	var formatted = "[%d] %s" % [timestamp, msg]
	print(formatted)
	if debug_enabled:
		print_debug(formatted)

func _ready() -> void:
	debug_log("=== TILEZ VIEWER STARTING ===")
	debug_log("Godot version: %s" % Engine.get_version_info()["string"])
	debug_log("Working directory: %s" % ProjectSettings.globalize_path("res://"))
	debug_log("Generated images path: %s" % generated_images_path)
	
	camera = $Camera2D
	label = $Label
	
	if camera == null:
		debug_log("ERROR: Camera2D not found!")
		return
	if label == null:
		debug_log("ERROR: Label not found!")
		return
	
	debug_log("Camera and Label initialized successfully")
	
	# Set up initial view
	camera.zoom = Vector2(current_zoom, current_zoom)
	camera.position = Vector2(0, 0)
	
	debug_log("Camera positioned at (0,0) with zoom %.2f" % current_zoom)
	
	update_label("Initializing Tilez Viewer...\nLooking for pre-generated tiles...")
	
	# Generate initial 3x3 grid
	debug_log("Generating initial 3x3 tile grid around (0, 0)")
	generate_surrounding_tiles(0, 0)
	update_label("Ready. Use arrow keys to pan, +/- to zoom.\nGenerating tiles in background...")

func _process(delta: float) -> void:
	# Handle camera movement
	var move_dir = Vector2.ZERO
	if Input.is_action_pressed("ui_right"):
		move_dir.x += 1
	if Input.is_action_pressed("ui_left"):
		move_dir.x -= 1
	if Input.is_action_pressed("ui_down"):
		move_dir.y += 1
	if Input.is_action_pressed("ui_up"):
		move_dir.y -= 1
	
	if move_dir != Vector2.ZERO:
		camera.position += move_dir.normalized() * pan_speed * delta
	
	# Handle zoom
	if Input.is_action_just_pressed("ui_plus") or Input.is_action_just_pressed("ui_text_zoom_in"):
		current_zoom = min(current_zoom + 0.2, max_zoom)
		camera.zoom = Vector2(current_zoom, current_zoom)
	
	if Input.is_action_just_pressed("ui_minus") or Input.is_action_just_pressed("ui_text_zoom_out"):
		current_zoom = max(current_zoom - 0.2, min_zoom)
		camera.zoom = Vector2(current_zoom, current_zoom)
	
	# Update tiles based on camera position
	var now = Time.get_ticks_msec() / 1000.0
	if now - last_update_time > update_cooldown:
		update_visible_tiles()
		last_update_time = now

func update_visible_tiles() -> void:
	# Get viewport bounds in world coordinates
	var viewport_rect = get_viewport_rect()
	var camera_pos = camera.position
	var zoom = camera.zoom.x
	
	# Calculate visible tile range with margin
	var screen_width = viewport_rect.size.x / zoom
	var screen_height = viewport_rect.size.y / zoom
	
	var min_x = int(floor((camera_pos.x - screen_width / 2) / tile_size)) - viewport_margin
	var max_x = int(floor((camera_pos.x + screen_width / 2) / tile_size)) + viewport_margin
	var min_y = int(floor((camera_pos.y - screen_height / 2) / tile_size)) - viewport_margin
	var max_y = int(floor((camera_pos.y + screen_height / 2) / tile_size)) + viewport_margin
	
	# Generate tiles for visible range
	for x in range(min_x, max_x + 1):
		for y in range(min_y, max_y + 1):
			var coord = Vector2i(x, y)
			if not loaded_coords.has(str(coord)):
				generate_tile(x, y)

func generate_tile(x: int, y: int) -> void:
	var tile_coord = Vector2i(x, y)
	var coord_str = str(tile_coord)
	
	debug_log("generate_tile(%d, %d) called" % [x, y])
	
	# Check if already loaded
	if tiles.has(coord_str):
		debug_log("  -> Already loaded in tiles dict")
		return
	
	# Try to load pre-generated tile
	debug_log("  -> Checking for pre-generated files...")
	var files = get_matching_files(generated_images_path, "tile_%d_%d" % [x, y])
	debug_log("  -> Found %d matching files" % files.size())
	
	if files.size() > 0:
		# Found pre-generated tile, load it
		debug_log("  -> Loading pre-generated tile: %s" % files[0])
		var texture = load_image_as_texture(files[0])
		if texture:
			debug_log("  -> Texture loaded successfully")
			tiles[coord_str] = texture
			display_tile(x, y, texture)
			loaded_coords[coord_str] = true
			return
		else:
			debug_log("  -> ERROR: Failed to load texture")
	
	# No pre-generated tile found, generate one
	debug_log("  -> No pre-generated tile found, will generate with AI")
	loaded_coords[coord_str] = true
	if use_ai:
		generate_tile_with_flux_async(x, y)
	else:
		generate_tile_procedural(x, y)

func generate_tile_with_flux_async(x: int, y: int) -> void:
	# Wrapper to handle async generation
	debug_log("generate_tile_with_flux_async(%d, %d) started" % [x, y])
	await generate_tile_with_flux(x, y)
	debug_log("  -> Task completed")

func generate_tile_with_flux(x: int, y: int) -> void:
	var seed = hash_coord(x, y)
	
	debug_log("[FLUX GEN] Starting tile generation (%d, %d) seed=%d" % [x, y, seed])
	update_label("Generating tile (%d, %d)...\nSeed: %d" % [x, y, seed])
	
	# Build full command line
	var working_dir = "C:\\code\\tilez"
	var project_path = "Tilez\\Tilez.csproj"
	var cmd_line = "dotnet run --project %s -- render --image-backend openai --ai-base-url %s --ai-model %s --x %d --y %d --zoom 3.0 --seed %d" % [
		project_path,
		base_url,
		ai_model,
		x, y,
		seed
	]
	
	debug_log("[FLUX GEN] Working dir: %s" % working_dir)
	debug_log("[FLUX GEN] Command: %s" % cmd_line)
	
	# Check if working directory exists
	var dir = DirAccess.open(working_dir)
	if dir == null:
		debug_log("[FLUX GEN] ERROR: Working directory not found: %s" % working_dir)
		update_label("ERROR: Working directory not found")
		return
	debug_log("[FLUX GEN] Working directory verified")
	
	# Run the command
	var output = []
	debug_log("[FLUX GEN] Executing command...")
	var start_time = Time.get_ticks_msec()
	
	var result = OS.execute("cmd.exe", ["/C", cmd_line], output, true)
	
	var elapsed = Time.get_ticks_msec() - start_time
	debug_log("[FLUX GEN] Command finished in %d ms, exit code: %d" % [elapsed, result])
	
	# Log output
	if output.size() > 0:
		var output_str = output[0] if output.size() > 0 else ""
		debug_log("[FLUX GEN] Output (first 500 chars): %s" % output_str.substr(0, 500))
	else:
		debug_log("[FLUX GEN] No output captured")
	
	# Wait for file system
	debug_log("[FLUX GEN] Waiting 1 second for file system sync...")
	await get_tree().create_timer(1.0).timeout
	
	# Check for generated files
	debug_log("[FLUX GEN] Looking for generated files...")
	var search_pattern = "tile_%d_%d" % [x, y]
	debug_log("[FLUX GEN] Search pattern: %s" % search_pattern)
	
	var files = get_matching_files(generated_images_path, search_pattern)
	debug_log("[FLUX GEN] Found %d matching files" % files.size())
	
	for f in files:
		debug_log("[FLUX GEN] File: %s" % f)
	
	if files.size() > 0:
		debug_log("[FLUX GEN] Loading first file: %s" % files[0])
		var texture = load_image_as_texture(files[0])
		if texture:
			debug_log("[FLUX GEN] Texture loaded successfully")
			var coord_str = str(Vector2i(x, y))
			tiles[coord_str] = texture
			display_tile(x, y, texture)
			update_label("Tile (%d, %d) loaded!\nPan: arrows, Zoom: +/-" % [x, y])
			debug_log("[FLUX GEN] Tile displayed successfully")
		else:
			debug_log("[FLUX GEN] ERROR: Failed to load texture from %s" % files[0])
			update_label("ERROR: Failed to load texture")
	else:
		debug_log("[FLUX GEN] ERROR: No generated files found")
		debug_log("[FLUX GEN] Checked directory: %s" % generated_images_path)
		var dir_check = DirAccess.open(generated_images_path)
		if dir_check:
			debug_log("[FLUX GEN] Directory exists and is readable")
			# List all PNG files in directory
			dir_check.list_dir_begin()
			var file_name = dir_check.get_next()
			var png_count = 0
			while file_name != "":
				if file_name.ends_with(".png"):
					png_count += 1
				file_name = dir_check.get_next()
			debug_log("[FLUX GEN] Total PNG files in directory: %d" % png_count)
		else:
			debug_log("[FLUX GEN] ERROR: Cannot open directory %s" % generated_images_path)
		update_label("ERROR: Tile generation failed")

func generate_tile_procedural(x: int, y: int) -> void:
	# Fallback: create a simple procedural tile
	var seed = hash_coord(x, y)
	var image = Image.create(128, 128, false, Image.FORMAT_RGB8)
	
	var rng = RandomNumberGenerator.new()
	rng.seed = seed
	
	for px in range(128):
		for py in range(128):
			var val = int(rng.randf() * 255)
			image.set_pixel(px, py, Color8(val, val, val))
	
	var texture = ImageTexture.create_from_image(image)
	var coord_str = str(Vector2i(x, y))
	tiles[coord_str] = texture
	display_tile(x, y, texture)

func generate_surrounding_tiles(center_x: int, center_y: int) -> void:
	for ox in range(-1, 2):
		for oy in range(-1, 2):
			var x = center_x + ox
			var y = center_y + oy
			var coord_str = str(Vector2i(x, y))
			if not loaded_coords.has(coord_str):
				generate_tile(x, y)
				loaded_coords[coord_str] = true

func display_tile(x: int, y: int, texture: Texture2D) -> void:
	debug_log("[DISPLAY] Displaying tile (%d, %d)" % [x, y])
	var sprite = Sprite2D.new()
	sprite.texture = texture
	sprite.position = Vector2(x * tile_size + tile_size / 2.0, y * tile_size + tile_size / 2.0)
	sprite.centered = true
	$TileContainer.add_child(sprite)
	debug_log("[DISPLAY] Sprite added to container at position %.1f, %.1f" % [sprite.position.x, sprite.position.y])

func load_image_as_texture(path: String) -> Texture2D:
	debug_log("[TEXTURE] Loading image: %s" % path)
	
	# Check file exists
	if not ResourceLoader.exists(path):
		debug_log("[TEXTURE] Path does not exist as resource: %s" % path)
	
	# Try loading PNG directly
	var image = Image.new()
	var error = image.load(path)
	
	if error == OK:
		debug_log("[TEXTURE] Image loaded successfully, size: %dx%d" % [image.get_width(), image.get_height()])
		var texture = ImageTexture.create_from_image(image)
		debug_log("[TEXTURE] Texture created successfully")
		return texture
	else:
		debug_log("[TEXTURE] ERROR loading image, error code: %d" % error)
		return null

func get_matching_files(directory: String, pattern: String) -> PackedStringArray:
	var result = PackedStringArray()
	
	# Use absolute path
	var abs_dir = "C:\\code\\tilez\\generated\\images"
	
	debug_log("[FILE SEARCH] Looking in: %s" % abs_dir)
	debug_log("[FILE SEARCH] Pattern: %s" % pattern)
	
	var dir = DirAccess.open(abs_dir)
	
	if dir == null:
		debug_log("[FILE SEARCH] ERROR: Could not open directory: %s" % abs_dir)
		return result
	
	debug_log("[FILE SEARCH] Directory opened successfully")
	
	dir.list_dir_begin()
	var file_name = dir.get_next()
	var total_files = 0
	var matching_files = 0
	
	while file_name != "":
		total_files += 1
		debug_log("[FILE SEARCH] Checking: %s" % file_name)
		
		if file_name != "." and file_name != ".." and file_name.ends_with(".png"):
			debug_log("[FILE SEARCH]   - Is PNG")
			if pattern in file_name:
				var full_path = abs_dir.path_join(file_name)
				result.append(full_path)
				matching_files += 1
				debug_log("[FILE SEARCH]   - MATCH! Pattern '%s' found in '%s'" % [pattern, file_name])
			else:
				debug_log("[FILE SEARCH]   - No match for pattern '%s'" % pattern)
		
		file_name = dir.get_next()
	
	debug_log("[FILE SEARCH] Scanned %d files, found %d matches" % [total_files, matching_files])
	
	return result

func hash_coord(x: int, y: int) -> int:
	# Simple hash function for deterministic seed
	var h = x ^ (y << 16)
	h = ((h >> 16) ^ h) * 0x45d9f3b
	h = ((h >> 16) ^ h) * 0x45d9f3b
	return ((h >> 16) ^ h) & 0x7FFFFFFF

func update_label(text: String) -> void:
	label.text = text

func _input(event: InputEvent) -> void:
	if event is InputEventKey and event.pressed:
		if event.keycode == KEY_R:
			# Reload tiles
			tiles.clear()
			loaded_coords.clear()
			for child in $TileContainer.get_children():
				child.queue_free()
			generate_surrounding_tiles(0, 0)
			update_label("Tiles reloaded")
