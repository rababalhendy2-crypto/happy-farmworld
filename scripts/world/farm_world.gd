extends Node3D
## Builds the starter farm: terrain, lighting, buildings, plots, animal pens, decoration.

const MF := preload("res://scripts/world/model_factory.gd")
const PlotScript := preload("res://scripts/world/plot.gd")
const AnimalScript := preload("res://scripts/world/animal.gd")
const WorkshopScript := preload("res://scripts/world/workshop.gd")

const PLOT_POSITIONS := [Vector3(-2.7, 0, 0.2), Vector3(0, 0, 0.2), Vector3(2.7, 0, 0.2),
	Vector3(-2.7, 0, 2.9), Vector3(0, 0, 2.9), Vector3(2.7, 0, 2.9),
	Vector3(-2.7, 0, -2.5), Vector3(0, 0, -2.5), Vector3(2.7, 0, -2.5),   # expansion 1
	Vector3(-2.7, 0, 5.6), Vector3(0, 0, 5.6), Vector3(2.7, 0, 5.6)]      # expansion 2
const WORKSHOP_POS := {"bakery": Vector3(-8.8, 0, 9.6), "jam_kitchen": Vector3(-13.4, 0, 8.8),
	"juice_press": Vector3(8.8, 0, 9.6), "dairy": Vector3(13.4, 0, 8.8)}
const DECOR_POS := {"flower_bed": [Vector3(-7.6, 0, -6.6), 0.0], "bench": [Vector3(-1.5, 0, -7.2), 90.0],
	"scarecrow": [Vector3(5.0, 0, -3.6), -20.0], "birdhouse": [Vector3(-6.2, 0, 5.6), 0.0],
	"windmill": [Vector3(3.2, 0, -12.0), -15.0], "fountain": [Vector3(0, 0, 12.2), 0.0],
	"rainbow_arch": [Vector3(0, 0, -11.0), 0.0], "balloon": [Vector3(-14.5, 0, -1.5), 0.0]}
const EAST_PEN := Rect2(6.5, -2.5, 6.5, 7.5)   # large animals
const WEST_PEN := Rect2(-13.0, -1.0, 6.5, 6.0) # small animals
const PEN_OF := {"cow": "east", "sheep": "east", "goat": "east", "pig": "east", "chicken": "west", "rabbit": "west", "golden_hen": "west"}

var plots: Array = []
var animals: Array = []
var workshops: Dictionary = {}
var _decor: Dictionary = {}
var _decor_anim: Dictionary = {}
var _water: MeshInstance3D
var _t := 0.0

func _ready() -> void:
	_build_environment()
	_build_ground()
	_build_buildings()
	_build_decoration()
	_batch_static()
	_build_plots()
	_build_animals()
	_build_workshops()
	_sync_decorations()
	GameState.state_changed.connect(_on_state_changed)

func _on_state_changed(reason: String) -> void:
	if reason == "load" or reason == "reset":
		for c in plots + animals:
			c.queue_free()
		plots.clear()
		animals.clear()
		_build_plots()
		_build_animals()
		_sync_decorations()
		for w in workshops.values():
			w.refresh()
		return
	if plots.size() > GameState.state["plots"].size():
		for c in plots:
			c.queue_free()
		plots.clear()
	if plots.size() < GameState.state["plots"].size():
		_build_plots()
	if animals.size() != GameState.state["animals"].size():
		for c in animals:
			c.queue_free()
		animals.clear()
		_build_animals()
	_sync_decorations()
	for w in workshops.values():
		w.refresh()
	for p in plots:
		p.refresh()
	for a in animals:
		a.refresh()

func _build_environment() -> void:
	# Warm, saturated "sunny afternoon" look.
	var env := Environment.new()
	env.background_mode = Environment.BG_SKY
	var sky := Sky.new()
	var sm := ProceduralSkyMaterial.new()
	sm.sky_top_color = Color("#3f9bff")
	sm.sky_horizon_color = Color("#bfe6ff")
	sm.ground_horizon_color = Color("#bfe6ff")
	sm.ground_bottom_color = Color("#5fbf3c")
	sm.sun_angle_max = 20.0
	sky.sky_material = sm
	env.sky = sky
	env.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	env.ambient_light_color = Color("#ffe8c8")
	env.ambient_light_energy = 0.36
	env.tonemap_mode = Environment.TONE_MAPPER_LINEAR
	env.tonemap_exposure = 1.0
	env.adjustment_enabled = true
	env.adjustment_saturation = 1.15
	env.adjustment_contrast = 1.06
	env.adjustment_brightness = 1.0
	var we := WorldEnvironment.new()
	we.environment = env
	add_child(we)
	var sun := DirectionalLight3D.new()
	sun.rotation_degrees = Vector3(-50, -40, 0)
	sun.light_energy = 0.82
	sun.light_color = Color("#ffd9a0")
	sun.shadow_enabled = true
	sun.shadow_opacity = 0.7
	sun.shadow_blur = 1.5
	sun.directional_shadow_max_distance = 55.0
	sun.directional_shadow_mode = DirectionalLight3D.SHADOW_ORTHOGONAL
	add_child(sun)

func _build_ground() -> void:
	var g := MeshInstance3D.new()
	var pm := PlaneMesh.new()
	pm.size = Vector2(80, 80)
	g.mesh = pm
	g.material_override = MF.mat(Color("#3a9140"), 1.0)
	add_child(g)
	# Slightly lighter farm yard
	var yard := MF.box(self, Vector3(34, 0.02, 30), Color("#43a046"), Vector3(0, 0.01, 0))
	yard.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	# Paths
	var path_col := Color("#f0c27a")
	for seg in [[Vector3(0, 0.03, -5), Vector3(1.4, 0.03, 10)], [Vector3(0, 0.03, 7.6), Vector3(12, 0.03, 1.4)],
			[Vector3(-5, 0.03, -6.5), Vector3(8, 0.03, 1.4)], [Vector3(4.6, 0.03, 1.5), Vector3(2.4, 0.03, 1.2)],
			[Vector3(-4.8, 0.03, 1.5), Vector3(2.4, 0.03, 1.2)]]:
		var p := MF.box(self, seg[1], path_col, seg[0])
		p.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	# Pond (water source)
	var pond_root := Node3D.new()
	pond_root.position = Vector3(11.5, 0, -10.5)
	add_child(pond_root)
	MF.cyl(pond_root, 3.2, 3.4, 0.08, Color("#c9b98f"), Vector3(0, 0.04, 0), Vector3.ZERO, 20)
	_water = MF.cyl(pond_root, 2.9, 2.9, 0.06, Color("#58b7ea"), Vector3(0, 0.08, 0), Vector3.ZERO, 20)
	var wm := StandardMaterial3D.new()
	wm.albedo_color = Color("#58b7ea")
	wm.roughness = 0.1
	wm.metallic_specular = 0.9
	_water.material_override = wm
	for k in 3:
		MF.sphere(pond_root, 0.35, Color("#4fa83a"), Vector3(cos(k * 2.1) * 1.6, 0.13, sin(k * 2.1) * 1.4), Vector3(1, 0.1, 1), 8)
	# Grass tufts: one MultiMesh draw call for hundreds of tufts
	var tuft := PrismMesh.new()
	tuft.size = Vector3(0.12, 0.3, 0.05)
	var mm := MultiMesh.new()
	mm.transform_format = MultiMesh.TRANSFORM_3D
	mm.mesh = tuft
	mm.instance_count = 700
	var rng := RandomNumberGenerator.new()
	rng.seed = 7
	for i in mm.instance_count:
		var pos := Vector3(rng.randf_range(-24, 24), 0.15, rng.randf_range(-20, 20))
		if absf(pos.x) < 5 and pos.z > -1.5 and pos.z < 4.5:
			pos.x += 10.0 * signf(pos.x + 0.01)
		var b := Basis(Vector3.UP, rng.randf() * TAU).scaled(Vector3.ONE * rng.randf_range(0.7, 1.4))
		mm.set_instance_transform(i, Transform3D(b, pos))
	var mmi := MultiMeshInstance3D.new()
	mmi.multimesh = mm
	mmi.material_override = MF.mat(Color("#2f8a2c"))
	mmi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	add_child(mmi)

func _place(id: String, pos: Vector3, rot_y: float, builder: Callable, col_size: Vector3) -> Node3D:
	var n := Node3D.new()
	n.name = id
	n.position = pos
	n.rotation_degrees.y = rot_y
	add_child(n)
	builder.call(n)
	n.set_meta("building", id)
	var body := MF.collider(n, col_size, Vector3(0, col_size.y / 2, 0))
	body.set_meta("target", n)
	return n

func _build_buildings() -> void:
	_place("farmhouse", Vector3(-4.5, 0, -8.5), 0, MF.farmhouse, Vector3(3.6, 3.5, 3.0))
	_place("barn", Vector3(9.5, 0, -6.5), 0, MF.barn, Vector3(4.0, 4.0, 3.2))
	_place("coop", Vector3(-11.2, 0, -3.2), 20, MF.coop, Vector3(2.0, 2.2, 1.6))
	_place("warehouse", Vector3(-11.5, 0, -9.5), 30, MF.warehouse, Vector3(3.4, 3.2, 2.6))
	_place("seed_shop", Vector3(-3.6, 0, 9.4), 0, MF.seed_shop, Vector3(2.5, 2.4, 1.2))
	_place("market", Vector3(3.6, 0, 9.4), 0, MF.market_stand, Vector3(2.5, 2.4, 1.2))
	_place("well", Vector3(1.8, 0, -4.8), 0, MF.well, Vector3(1.8, 2.4, 1.8))

## Performance: buildings keep their own merged mesh (so tap feedback still moves them),
## everything else that never changes becomes one mesh.
func _batch_static() -> void:
	var buildings: Array = []
	for c in get_children():
		if c.has_meta("building"):
			buildings.append(c)
			MF.merge_static(c)
	MF.merge_static(self, buildings + [_water.get_parent()])

func _build_plots() -> void:
	var n: int = GameState.state["plots"].size()
	for i in range(plots.size(), n):
		var p := Node3D.new()
		p.set_script(PlotScript)
		var pos: Vector3 = PLOT_POSITIONS[i] if i < PLOT_POSITIONS.size() else Vector3(-2.7 + (i % 3) * 2.7, 0, 0.2 + (i / 3) * 2.7)
		p.position = pos
		add_child(p)
		p.setup(i)
		plots.append(p)
		if i >= 6 and is_inside_tree():
			p.scale = Vector3(0.1, 0.1, 0.1)
			create_tween().tween_property(p, "scale", Vector3.ONE, 0.4).set_trans(Tween.TRANS_BACK).set_ease(Tween.EASE_OUT)

func _build_animals() -> void:
	var list: Array = GameState.state["animals"]
	for i in list.size():
		var type: String = list[i]["type"]
		var pen: Rect2 = EAST_PEN if PEN_OF.get(type, "east") == "east" else WEST_PEN
		var inner := pen.grow(-0.8)
		var a := Node3D.new()
		a.set_script(AnimalScript)
		a.position = Vector3(randf_range(inner.position.x, inner.end.x), 0, randf_range(inner.position.y, inner.end.y))
		add_child(a)
		a.setup(i, type, inner)
		animals.append(a)

func _pen_fence(r: Rect2, gap_side: String) -> void:
	var a := Vector3(r.position.x, 0, r.position.y)
	var b := Vector3(r.end.x, 0, r.position.y)
	var c := Vector3(r.end.x, 0, r.end.y)
	var d := Vector3(r.position.x, 0, r.end.y)
	MF.fence(self, a, b)
	MF.fence(self, d, c)
	MF.fence(self, b, c if gap_side != "east" else b.lerp(c, 0.35))
	MF.fence(self, a, d if gap_side != "west" else a.lerp(d, 0.35))

func _build_decoration() -> void:
	_pen_fence(EAST_PEN, "west")
	_pen_fence(WEST_PEN, "east")
	# Water trough and hay in pens
	MF.box(self, Vector3(1.6, 0.4, 0.5), Color("#8a5a36"), Vector3(12.2, 0.2, 4.2))
	MF.box(self, Vector3(1.4, 0.1, 0.35), Color("#58b7ea"), Vector3(12.2, 0.42, 4.2))
	var hay := Node3D.new()
	hay.position = Vector3(7.6, 0, -1.6)
	add_child(hay)
	if not MF.building_model(hay, "Hay.obj", Vector3(1.4, 1.0, 1.4), 30.0):
		MF.box(hay, Vector3(1.0, 0.7, 1.0), Color("#e8c547"), Vector3(0, 0.35, 0))
	# Trees around the edge
	var rng := RandomNumberGenerator.new()
	rng.seed = 42
	var tints := [Color("#4caf50"), Color("#66bb44"), Color("#3f9d4a")]
	for i in 26:
		var ang := float(i) / 26.0 * TAU
		var r := rng.randf_range(17.5, 21.0)
		var pos := Vector3(cos(ang) * r * 1.1, 0, sin(ang) * r * 0.95)
		var t := Node3D.new()
		t.position = pos
		add_child(t)
		MF.tree(t, rng.randf_range(0.9, 1.5), tints[i % 3])
	for pos in [Vector3(-7, 0, -6), Vector3(5.5, 0, -9.5), Vector3(-14.5, 0, 7.5), Vector3(14.5, 0, 7.5)]:
		var t := Node3D.new()
		t.position = pos
		add_child(t)
		MF.tree(t, 1.0, tints[1])
	# Flowers along paths and near the farmhouse
	var fcols := [Color("#ff6f91"), Color("#ffd23f"), Color("#b388ff"), Color("#ffffff"), Color("#ff9f43")]
	for i in 60:
		var f := Node3D.new()
		var side := -1.0 if i % 2 == 0 else 1.0
		f.position = Vector3(side * rng.randf_range(0.9, 1.4), 0, rng.randf_range(-4.0, 9.5)) if i < 24 \
			else Vector3(rng.randf_range(-7.5, -1.5), 0, rng.randf_range(-6.5, -5.2)) if i < 40 \
			else Vector3(rng.randf_range(-6, 6), 0, rng.randf_range(10.8, 13.5))
		add_child(f)
		MF.flower(f, fcols[i % fcols.size()])
	for pos in [Vector3(-5.6, 0, 12.0), Vector3(6.0, 0, 12.4), Vector3(14, 0, -2), Vector3(-15, 0, -6), Vector3(5, 0, -12)]:
		var r := Node3D.new()
		r.position = pos
		add_child(r)
		MF.rock(r, rng.randf_range(0.8, 1.6))

func _build_workshops() -> void:
	for id in Config.data.get("workshops", {}):
		var w := Node3D.new()
		w.set_script(WorkshopScript)
		w.position = WORKSHOP_POS.get(id, Vector3.ZERO)
		add_child(w)
		w.setup(id)
		workshops[id] = w

func _sync_decorations() -> void:
	for id in GameState.state.get("decorations", []):
		if _decor.has(id) or not DECOR_POS.has(id):
			continue
		var n := Node3D.new()
		n.position = DECOR_POS[id][0]
		n.rotation_degrees.y = DECOR_POS[id][1]
		add_child(n)
		var anim: Node3D = MF.decoration(n, id)
		if anim:
			_decor_anim[id] = anim
		_decor[id] = n
		n.scale = Vector3(0.1, 0.1, 0.1)
		create_tween().tween_property(n, "scale", Vector3.ONE, 0.45).set_trans(Tween.TRANS_BACK).set_ease(Tween.EASE_OUT)
	for id in _decor.keys():
		if not id in GameState.state.get("decorations", []):
			_decor[id].queue_free()
			_decor.erase(id)
			_decor_anim.erase(id)

func _process(delta: float) -> void:
	_t += delta
	if _decor_anim.has("windmill"):
		_decor_anim["windmill"].rotation.z += delta * 1.2
	if _decor_anim.has("balloon"):
		_decor_anim["balloon"].position.y = 3.6 + sin(_t * 0.8) * 0.25
	if _decor_anim.has("fountain"):
		_decor_anim["fountain"].scale = Vector3.ONE * (1.0 + sin(_t * 4.0) * 0.12)
	if _water:
		_water.position.y = 0.08 + sin(_t * 1.5) * 0.01

func plot_node(idx: int) -> Node3D:
	return plots[idx] if idx < plots.size() else null

func animal_node(idx: int) -> Node3D:
	return animals[idx] if idx < animals.size() else null
