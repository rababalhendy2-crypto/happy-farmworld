extends Node3D
## Entry point: builds the world, camera and HUD; routes taps to plots/animals/buildings;
## plays feedback (sounds, floating text, particles, animal animations).

const FarmWorld := preload("res://scripts/world/farm_world.gd")
const CameraRig := preload("res://scripts/world/camera_rig.gd")
const Hud := preload("res://scripts/ui/hud.gd")
const UT := preload("res://scripts/ui/ui_theme.gd")

var world: Node3D
var rig: Node3D
var hud: CanvasLayer

func _ready() -> void:
	world = Node3D.new()
	world.set_script(FarmWorld)
	add_child(world)
	rig = Node3D.new()
	rig.set_script(CameraRig)
	add_child(rig)
	hud = CanvasLayer.new()
	hud.set_script(Hud)
	hud.world = world
	add_child(hud)
	rig.ui_blocker = hud.blocks_point
	rig.tapped.connect(_on_tap)
	GameState.action_result.connect(_on_action_result)
	_welcome.call_deferred()

func _welcome() -> void:
	# Wait for the cloud state first (if any) so the gift popup reflects the server.
	if Backend.enabled() and Backend.status != "online":
		var t := get_tree().create_timer(6.0)
		while Backend.status != "online" and t.time_left > 0.0:
			await get_tree().process_frame
	await get_tree().create_timer(1.0).timeout
	hud.show_daily_gift_if_available()

func _on_tap(screen_pos: Vector2) -> void:
	var cam: Camera3D = rig.camera
	var from := cam.project_ray_origin(screen_pos)
	var to := from + cam.project_ray_normal(screen_pos) * 200.0
	var q := PhysicsRayQueryParameters3D.create(from, to)
	var hit := get_world_3d().direct_space_state.intersect_ray(q)
	if hit.is_empty() or not hit["collider"].has_meta("target"):
		hud.close_context()
		return
	select(hit["collider"].get_meta("target"))

func select(target: Node3D) -> void:
	if target.has_method("kind") and target.kind() == "plot":
		if GameState.plot_status(target.index) == "ready":
			GameState.harvest(target.index)
		else:
			hud.open_plot(target.index)
	elif target.has_method("kind") and target.kind() == "animal":
		Audio.play("chicken" if target.type == "golden_hen" else target.type)
		if GameState.animal_status(target.index) == "ready":
			GameState.collect(target.index)
		else:
			hud.open_animal(target.index)
	elif target.has_meta("building"):
		match String(target.get_meta("building")):
			"warehouse", "barn":
				hud.open_inventory()
			"market":
				hud.open_market()
			"seed_shop":
				var empty := _first_empty_plot()
				if empty >= 0:
					hud.open_plot(empty)
				else:
					hud.toast(tr("NO_EMPTY_PLOT"))
			"bakery", "dairy", "juice_press", "jam_kitchen":
				var ws := String(target.get_meta("building"))
				if GameState.state["workshops"].has(ws):
					hud.open_workshop(ws)
				else:
					hud.open_shop("buildings")
			"well":
				var n := _water_all()
				hud.toast(tr("WATERED_N") % n if n > 0 else tr("NOTHING_TO_WATER"), n > 0)
			_:
				hud.toast(tr("BUILDING_" + String(target.get_meta("building")).to_upper()), true)

func _first_empty_plot() -> int:
	for i in GameState.state["plots"].size():
		if GameState.plot_status(i) == "empty":
			return i
	return -1

func _water_all() -> int:
	var n := 0
	for i in GameState.state["plots"].size():
		var p := GameState.plot(i)
		if GameState.plot_status(i) == "growing" and not p["watered"]:
			if GameState.water(i)["ok"]:
				n += 1
	return n

func _on_action_result(action: String, r: Dictionary) -> void:
	if not r.get("ok", false):
		return
	var idx := int(r.get("idx", -1))
	match action:
		"plant":
			Audio.play("plant")
			_dust(world.plot_node(idx).global_position, Color("#8a5a36"))
			_float_text(world.plot_node(idx).global_position, "-%d" % int(r["cost"]), Color("#ffd23f"))
		"water":
			Audio.play("water")
			_dust(world.plot_node(idx).global_position, Color("#58b7ea"))
		"harvest":
			Audio.play("harvest")
			var pos: Vector3 = world.plot_node(idx).global_position
			_dust(pos, Color(Config.crops()[r["item"]]["color"]))
			_float_text(pos, "+%d %s   +%d XP" % [int(r["qty"]), hud.item_name(r["item"]), int(r["xp"])], Color.WHITE)
		"feed":
			var a: Node3D = world.animal_node(idx)
			a.play_eat()
			_float_text(a.global_position, "-%d %s" % [int(r["qty"]), hud.item_name(r["item"])], Color.WHITE)
		"collect":
			Audio.play("harvest", 1.2)
			var a: Node3D = world.animal_node(idx)
			a.play_happy()
			_dust(a.global_position, Color("#ff9fc0"))
			_float_text(a.global_position, "+%d %s   +%d XP" % [int(r["qty"]), hud.item_name(r["item"]), int(r["xp"])], Color.WHITE)
		"sell":
			Audio.play("coin")
		"start_job":
			Audio.play("plant", 1.3)
		"collect_job":
			Audio.play("harvest", 1.1)
			var w: Node3D = world.workshops[r["ws"]]
			_dust(w.global_position + Vector3(0, 1.0, 0), Color("#ffd23f"))
			_float_text(w.global_position + Vector3(0, 1.5, 0), "+%d %s   +%d XP" % [int(r["qty"]), hud.item_name(r["item"]), int(r["xp"])], Color.WHITE)

func _float_text(pos: Vector3, text: String, color: Color) -> void:
	var l := Label3D.new()
	l.text = text
	l.font = UT.font()
	l.font_size = 56
	l.outline_size = 14
	l.modulate = color
	l.outline_modulate = Color("#5a3a1a")
	l.billboard = BaseMaterial3D.BILLBOARD_ENABLED
	l.no_depth_test = true
	l.pixel_size = 0.01
	add_child(l)
	l.global_position = pos + Vector3(0, 2.0, 0)
	var tw := create_tween().set_parallel()
	tw.tween_property(l, "global_position:y", pos.y + 3.6, 1.2).set_ease(Tween.EASE_OUT)
	tw.tween_property(l, "modulate:a", 0.0, 1.2).set_delay(0.5)
	tw.chain().tween_callback(l.queue_free)

func _dust(pos: Vector3, color: Color) -> void:
	var p := CPUParticles3D.new()
	p.one_shot = true
	p.amount = 18
	p.lifetime = 0.8
	p.explosiveness = 0.95
	p.direction = Vector3.UP
	p.spread = 60.0
	p.initial_velocity_min = 2.0
	p.initial_velocity_max = 4.0
	p.gravity = Vector3(0, -9, 0)
	p.scale_amount_min = 0.6
	p.scale_amount_max = 1.2
	var m := SphereMesh.new()
	m.radius = 0.08
	m.height = 0.16
	m.radial_segments = 6
	m.rings = 3
	var mat := StandardMaterial3D.new()
	mat.albedo_color = color
	m.material = mat
	p.mesh = m
	add_child(p)
	p.global_position = pos + Vector3(0, 0.4, 0)
	p.emitting = true
	get_tree().create_timer(1.5).timeout.connect(p.queue_free)
