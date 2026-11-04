extends RefCounted
## Builds the game's original low-poly cartoon models from primitive meshes.
## Materials are cached per colour so the whole farm uses a handful of materials (cheap on mobile).

static var _mats: Dictionary = {}
static var _meshes: Dictionary = {}

static func mat(color: Color, rough: float = 0.85) -> StandardMaterial3D:
	var key := "%s_%.2f" % [color.to_html(), rough]
	if not _mats.has(key):
		var m := StandardMaterial3D.new()
		m.albedo_color = color
		m.roughness = rough
		if color.a < 1.0:
			m.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
		_mats[key] = m
	return _mats[key]

static func _mi(mesh: Mesh, color: Color, pos: Vector3, parent: Node3D, rot := Vector3.ZERO, scl := Vector3.ONE) -> MeshInstance3D:
	var mi := MeshInstance3D.new()
	mi.mesh = mesh
	mi.material_override = mat(color)
	mi.position = pos
	mi.rotation_degrees = rot
	mi.scale = scl
	parent.add_child(mi)
	return mi

static func _cached(key: String, maker: Callable) -> Mesh:
	if not _meshes.has(key):
		_meshes[key] = maker.call()
	return _meshes[key]

# ------------------------------------------------------------ CC0 model packs (Quaternius)

const MODELS := "res://assets/models/"
static var _res: Dictionary = {}

## Loads (once) an imported model, or returns null when the file is not in the project.
static func res(path: String) -> Resource:
	if not _res.has(path):
		_res[path] = load(MODELS + path) if ResourceLoader.exists(MODELS + path) else null
	return _res[path]

## Places an imported static mesh (OBJ). Height `h` > 0 scales it to that height.
static func model(parent: Node3D, path: String, pos: Vector3, h: float = 0.0, rot_y: float = 0.0, ref: String = "", tint := Color(0, 0, 0, 0)) -> MeshInstance3D:
	var m: Mesh = res(path)
	if m == null:
		return null
	var mi := MeshInstance3D.new()
	mi.mesh = m
	var k := 1.0
	if h > 0.0:
		var r: Mesh = res(ref) if ref != "" else m
		k = h / maxf(r.get_aabb().size.y, 0.001)
	mi.scale = Vector3.ONE * k
	_recolor(mi, path, tint)
	mi.position = pos
	mi.rotation_degrees.y = rot_y
	parent.add_child(mi)
	return mi

## The crop pack's .mtl colours are linear (Blender), the importer reads them as sRGB -> convert.
## The nature pack has plain grey materials -> trunk/stem gets brown, the rest gets `tint`.
static func _recolor(mi: MeshInstance3D, path: String, tint: Color) -> void:
	var m := mi.mesh
	if path.begins_with("crops/"):
		for i in m.get_surface_count():
			var sm: Material = m.surface_get_material(i)
			if sm is StandardMaterial3D:
				mi.set_surface_override_material(i, mat(sm.albedo_color.linear_to_srgb().lightened(0.05)))
		return
	if path.begins_with("nature/"):
		var low := 0
		var low_y := INF
		for i in m.get_surface_count():
			var vs: PackedVector3Array = m.surface_get_arrays(i)[Mesh.ARRAY_VERTEX]
			var y := 0.0
			for v in vs: y += v.y
			y /= maxf(vs.size(), 1)
			if y < low_y:
				low_y = y; low = i
		var leaf := tint if tint.a > 0.0 else Color("#4caf50")
		for i in m.get_surface_count():
			var c := leaf
			if path.contains("Rock"):
				c = Color("#a7a29a")
			elif i == low and m.get_surface_count() > 1:
				c = Color("#8a5a36")
			mi.set_surface_override_material(i, mat(c))

static func box(parent: Node3D, size: Vector3, color: Color, pos: Vector3, rot := Vector3.ZERO) -> MeshInstance3D:
	var mesh := _cached("box%s" % size, func(): return rounded_box(size))
	return _mi(mesh, color, pos, parent, rot)

## Box with softly rounded edges: a sphere whose vertices are pushed out to the box corners.
## Sphere normals are exactly the rounded-box normals, so lighting is smooth on the bevels.
static func rounded_box(size: Vector3) -> Mesh:
	var r := clampf(minf(size.x, minf(size.y, size.z)) * 0.22, 0.004, 0.09)
	var half := size * 0.5
	var sm := SphereMesh.new()
	sm.radius = 1.0; sm.height = 2.0; sm.radial_segments = 12; sm.rings = 6
	var a := sm.get_mesh_arrays()
	var v: PackedVector3Array = a[Mesh.ARRAY_VERTEX]
	for i in v.size():
		var p := v[i]
		v[i] = Vector3(_push(p.x, r, half.x), _push(p.y, r, half.y), _push(p.z, r, half.z))
	a[Mesh.ARRAY_VERTEX] = v
	var m := ArrayMesh.new()
	m.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, a)
	return m

static func _push(c: float, r: float, h: float) -> float:
	if absf(c) < 0.0001:
		return 0.0
	return c * r + signf(c) * maxf(h - r, 0.0)

static func sphere(parent: Node3D, r: float, color: Color, pos: Vector3, scl := Vector3.ONE, segs := 14) -> MeshInstance3D:
	var mesh := _cached("sph%.3f_%d" % [r, segs], func():
		var s := SphereMesh.new(); s.radius = r; s.height = r * 2.0
		s.radial_segments = segs; s.rings = segs / 2; return s)
	return _mi(mesh, color, pos, parent, Vector3.ZERO, scl)

static func cyl(parent: Node3D, top: float, bottom: float, h: float, color: Color, pos: Vector3, rot := Vector3.ZERO, segs := 14) -> MeshInstance3D:
	var mesh := _cached("cyl%.3f_%.3f_%.3f_%d" % [top, bottom, h, segs], func():
		var c := CylinderMesh.new(); c.top_radius = top; c.bottom_radius = bottom
		c.height = h; c.radial_segments = segs; c.rings = 1; return c)
	return _mi(mesh, color, pos, parent, rot)

static func prism(parent: Node3D, size: Vector3, color: Color, pos: Vector3, rot := Vector3.ZERO) -> MeshInstance3D:
	var mesh := _cached("pri%s" % size, func():
		var p := PrismMesh.new(); p.size = size; return p)
	return _mi(mesh, color, pos, parent, rot)

static func collider(parent: Node3D, size: Vector3, pos: Vector3) -> StaticBody3D:
	var body := StaticBody3D.new()
	var cs := CollisionShape3D.new()
	var shape := BoxShape3D.new()
	shape.size = size
	cs.shape = shape
	body.position = pos
	body.add_child(cs)
	parent.add_child(body)
	return body

# ------------------------------------------------------------ buildings

static func house(parent: Node3D, wall: Color, roof: Color, w: float, d: float, h: float) -> void:
	box(parent, Vector3(w, h, d), wall, Vector3(0, h / 2, 0))
	prism(parent, Vector3(d + 0.4, h * 0.6, w + 0.5), roof, Vector3(0, h + h * 0.3, 0), Vector3(0, 90, 0))
	box(parent, Vector3(0.7, 1.2, 0.08), Color("#7a4a2a"), Vector3(0, 0.6, d / 2 + 0.03))
	for x in [-w / 3.2, w / 3.2]:
		box(parent, Vector3(0.6, 0.6, 0.06), Color("#bfe6ff"), Vector3(x, h * 0.62, d / 2 + 0.03))
		box(parent, Vector3(0.75, 0.1, 0.1), Color.WHITE, Vector3(x, h * 0.62 - 0.35, d / 2 + 0.05))

static func farmhouse(parent: Node3D) -> void:
	house(parent, Color("#fff3d6"), Color("#e2574c"), 3.6, 3.0, 2.2)
	box(parent, Vector3(0.5, 1.2, 0.5), Color("#b9b2a8"), Vector3(1.0, 3.3, -0.4))  # chimney
	box(parent, Vector3(1.6, 0.12, 0.9), Color("#c89b6d"), Vector3(0, 0.06, 1.95))  # porch

static func barn(parent: Node3D) -> void:
	house(parent, Color("#c94b3b"), Color("#6e4a3a"), 4.0, 3.2, 2.8)
	for x in [-0.45, 0.45]:
		box(parent, Vector3(0.85, 1.9, 0.08), Color("#fff3d6"), Vector3(x, 0.95, 1.63))
	box(parent, Vector3(0.12, 1.8, 0.1), Color.WHITE, Vector3(0, 0.95, 1.68), Vector3(0, 0, 25))

static func coop(parent: Node3D) -> void:
	box(parent, Vector3(1.8, 1.1, 1.4), Color("#f2c46d"), Vector3(0, 0.85, 0))
	for x in [-0.8, 0.8]:
		for z in [-0.6, 0.6]:
			cyl(parent, 0.06, 0.06, 0.4, Color("#7a4a2a"), Vector3(x, 0.2, z))
	prism(parent, Vector3(2.1, 0.7, 1.7), Color("#4f8fd6"), Vector3(0, 1.75, 0))
	box(parent, Vector3(0.4, 0.4, 0.06), Color("#3a2a1a"), Vector3(0, 0.75, 0.72))

static func warehouse(parent: Node3D) -> void:
	house(parent, Color("#b98a5a"), Color("#5c7d4b"), 3.0, 2.4, 2.0)
	for i in 3:
		box(parent, Vector3(0.6, 0.6, 0.6), Color("#d9a85c"), Vector3(1.9, 0.3 + i * 0.0, -0.6 + i * 0.65))

static func seed_shop(parent: Node3D) -> void:
	box(parent, Vector3(2.4, 0.9, 1.0), Color("#8bc34a"), Vector3(0, 0.45, 0))
	for x in [-1.1, 1.1]:
		cyl(parent, 0.07, 0.07, 2.2, Color("#7a4a2a"), Vector3(x, 1.1, -0.4))
		cyl(parent, 0.07, 0.07, 2.2, Color("#7a4a2a"), Vector3(x, 1.1, 0.4))
	for i in 5:
		box(parent, Vector3(0.52, 0.12, 1.4), Color("#ffffff") if i % 2 == 0 else Color("#4caf50"), Vector3(-1.04 + i * 0.52, 2.25, 0), Vector3(-12, 0, 0))
	var cols := [Color("#e8c547"), Color("#f08a24"), Color("#e8384f")]
	for i in 3:
		box(parent, Vector3(0.5, 0.3, 0.5), cols[i], Vector3(-0.7 + i * 0.7, 1.05, 0))

static func market_stand(parent: Node3D) -> void:
	box(parent, Vector3(2.4, 0.9, 1.0), Color("#d08c4c"), Vector3(0, 0.45, 0))
	for x in [-1.1, 1.1]:
		cyl(parent, 0.07, 0.07, 2.2, Color("#7a4a2a"), Vector3(x, 1.1, -0.4))
		cyl(parent, 0.07, 0.07, 2.2, Color("#7a4a2a"), Vector3(x, 1.1, 0.4))
	for i in 5:
		box(parent, Vector3(0.52, 0.12, 1.4), Color("#ffffff") if i % 2 == 0 else Color("#f0544f"), Vector3(-1.04 + i * 0.52, 2.25, 0), Vector3(-12, 0, 0))
	sphere(parent, 0.35, Color("#ffd23f"), Vector3(0, 1.25, 0), Vector3(1, 0.4, 1))  # coin pile

static func well(parent: Node3D) -> void:
	cyl(parent, 0.8, 0.85, 0.8, Color("#a8a29a"), Vector3(0, 0.4, 0), Vector3.ZERO, 14)
	cyl(parent, 0.68, 0.68, 0.05, Color("#4fb3e8", 0.9), Vector3(0, 0.75, 0), Vector3.ZERO, 14)
	for x in [-0.75, 0.75]:
		cyl(parent, 0.06, 0.06, 1.4, Color("#7a4a2a"), Vector3(x, 1.4, 0))
	prism(parent, Vector3(1.9, 0.5, 1.0), Color("#e2574c"), Vector3(0, 2.3, 0))

static func tree(parent: Node3D, scale: float, tint: Color) -> void:
	var v := int(absf(parent.position.x * 7.0 + parent.position.z * 3.0)) % 3 + 1
	if model(parent, "nature/Tree%d.obj" % v, Vector3.ZERO, 3.2 * scale, parent.position.x * 37.0, "", tint):
		return
	cyl(parent, 0.14 * scale, 0.2 * scale, 1.4 * scale, Color("#8a5a36"), Vector3(0, 0.7 * scale, 0))
	sphere(parent, 0.9 * scale, tint, Vector3(0, 1.9 * scale, 0))
	sphere(parent, 0.6 * scale, tint.lightened(0.12), Vector3(0.45 * scale, 2.4 * scale, 0.2 * scale))
	sphere(parent, 0.55 * scale, tint.darkened(0.08), Vector3(-0.5 * scale, 2.2 * scale, -0.2 * scale))

static func fence(parent: Node3D, a: Vector3, b: Vector3) -> void:
	var length := a.distance_to(b)
	var posts := maxi(2, int(length / 1.0) + 1)
	var dir := (b - a).normalized()
	var angle := rad_to_deg(atan2(dir.x, dir.z))
	for i in posts:
		var p := a.lerp(b, float(i) / (posts - 1))
		box(parent, Vector3(0.12, 0.7, 0.12), Color("#fff8ec"), p + Vector3(0, 0.35, 0))
	for y in [0.25, 0.5]:
		var rail := box(parent, Vector3(0.06, 0.08, length), Color("#fff8ec"), (a + b) / 2 + Vector3(0, y, 0))
		rail.rotation_degrees.y = angle

static func flower(parent: Node3D, color: Color) -> void:
	var v := int(absf(color.h * 10.0 + parent.position.x * 3.0)) % 3 + 2
	if model(parent, "crops/Flower_%d.obj" % v, Vector3.ZERO, 0.45, parent.position.z * 53.0):
		return
	cyl(parent, 0.02, 0.02, 0.3, Color("#4c9a3b"), Vector3(0, 0.15, 0), Vector3.ZERO, 4)
	sphere(parent, 0.09, color, Vector3(0, 0.32, 0), Vector3.ONE, 6)

static func rock(parent: Node3D, s: float) -> void:
	var v := int(absf(parent.position.x)) % 3 + 1
	if model(parent, "nature/Rock%d.obj" % v, Vector3(0, -0.05, 0), 0.55 * s, parent.position.z * 41.0):
		return
	sphere(parent, 0.35 * s, Color("#a9aab0"), Vector3(0, 0.12 * s, 0), Vector3(1.2, 0.6, 1.0), 7)

# ------------------------------------------------------------ crops (4 visual stages)

static func crop(parent: Node3D, crop_id: String, stage: int, color: Color) -> void:
	## stage 0 = seeded soil, 1 = sprout, 2 = growing, 3 = ready.
	if stage > 0 and _model_crop(parent, crop_id, stage):
		return
	var green := Color("#5cb85c")
	var spots := [Vector3(-0.45, 0, -0.45), Vector3(0.45, 0, -0.45), Vector3(-0.45, 0, 0.45), Vector3(0.45, 0, 0.45), Vector3.ZERO]
	for p in spots:
		if stage == 0:
			sphere(parent, 0.06, Color("#5a3a22"), p + Vector3(0, 0.02, 0), Vector3.ONE, 5)
			continue
		if stage == 1:
			cyl(parent, 0.015, 0.03, 0.25, green, p + Vector3(0, 0.12, 0), Vector3(0, 0, 10), 4)
			sphere(parent, 0.07, green.lightened(0.2), p + Vector3(0, 0.26, 0), Vector3(1.4, 0.5, 1), 5)
			continue
		var grow := 0.65 if stage == 2 else 1.0
		match crop_id:
			"wheat":
				for k in 3:
					var off := Vector3((k - 1) * 0.1, 0, (k % 2) * 0.08)
					cyl(parent, 0.015, 0.02, 0.7 * grow, green if stage == 2 else color.darkened(0.15), p + off + Vector3(0, 0.35 * grow, 0), Vector3(0, 0, (k - 1) * 8), 4)
					if stage == 3:
						sphere(parent, 0.05, color, p + off + Vector3(0, 0.75, 0), Vector3(0.8, 2.2, 0.8), 5)
			"carrot":
				for k in 3:
					cyl(parent, 0.01, 0.025, 0.4 * grow, green, p + Vector3(0, 0.2 * grow, 0), Vector3((k - 1) * 22, k * 60, 0), 4)
				if stage == 3:
					cyl(parent, 0.09, 0.03, 0.2, color, p + Vector3(0, 0.05, 0), Vector3.ZERO, 6)
			"strawberry", "tomato":
				sphere(parent, 0.22 * grow, green.darkened(0.1), p + Vector3(0, 0.2 * grow, 0), Vector3(1, 0.8, 1), 7)
				if crop_id == "tomato":
					cyl(parent, 0.015, 0.015, 0.8 * grow, Color("#8a5a36"), p + Vector3(0.1, 0.4 * grow, 0), Vector3.ZERO, 4)
				if stage == 3:
					for k in 3:
						var a := k * TAU / 3.0
						sphere(parent, 0.07 if crop_id == "strawberry" else 0.09, color, p + Vector3(cos(a) * 0.17, 0.2, sin(a) * 0.17), Vector3(1, 1.15, 1), 6)
			"corn":
				cyl(parent, 0.03, 0.04, 1.1 * grow, green, p + Vector3(0, 0.55 * grow, 0), Vector3.ZERO, 5)
				for k in 2:
					cyl(parent, 0.005, 0.03, 0.45 * grow, green.lightened(0.1), p + Vector3(0, 0.5 * grow, 0), Vector3(0, k * 180, 45), 4)
				if stage == 3:
					sphere(parent, 0.07, color, p + Vector3(0.07, 0.75, 0), Vector3(1, 2.3, 1), 6)
			"pumpkin":
				cyl(parent, 0.015, 0.015, 0.3 * grow, green, p + Vector3(0, 0.12, 0), Vector3(0, 0, 70), 4)
				sphere(parent, 0.12 * grow, green.lightened(0.15), p + Vector3(0.12, 0.08, 0.1), Vector3(1.4, 0.4, 1.4), 6)
				if stage == 3 and p == Vector3.ZERO:
					sphere(parent, 0.34, color, Vector3(0, 0.26, 0), Vector3(1.25, 0.85, 1.25), 10)
					cyl(parent, 0.03, 0.04, 0.12, Color("#5a7d2b"), Vector3(0, 0.6, 0), Vector3.ZERO, 5)
			_:
				sphere(parent, 0.15 * grow, color, p + Vector3(0, 0.15, 0))

## crop id -> [model name, full-grown height, single plant in the middle?]
const CROP_MODELS := {
	"wheat": ["Wheat", 0.85, false], "carrot": ["Carrot", 0.45, false], "strawberry": ["BushBerries", 0.45, false],
	"tomato": ["Tomato", 0.9, false], "corn": ["Corn", 1.25, false], "pumpkin": ["Pumpkin", 0.5, true],
}
const CROP_STAGE_FILE := {1: "1", 2: "3", 3: "4"}

static func _model_crop(parent: Node3D, crop_id: String, stage: int) -> bool:
	if not CROP_MODELS.has(crop_id):
		return false
	var cm: Array = CROP_MODELS[crop_id]
	var full := "crops/%s_4.obj" % cm[0]
	if res(full) == null:
		return false
	var file := "crops/%s_%s.obj" % [cm[0], CROP_STAGE_FILE[stage]]
	var spots := [Vector3.ZERO] if cm[2] else [Vector3(-0.45, 0, -0.45), Vector3(0.45, 0, -0.45), Vector3(-0.45, 0, 0.45), Vector3(0.45, 0, 0.45), Vector3.ZERO]
	for i in spots.size():
		model(parent, file, spots[i] + Vector3(0, 0.02, 0), cm[1], i * 71.0, full)
	return true

# ------------------------------------------------------------ animals

## type -> [file, length in metres, yaw offset in degrees]
const ANIMAL_MODELS := {"cow": ["animals/Cow.glb", 1.7, 0.0], "pig": ["animals/Pig.fbx", 1.15, 0.0], "sheep": ["animals/Sheep.fbx", 1.35, 0.0]}

static func _model_animal(parent: Node3D, type: String) -> Dictionary:
	if not ANIMAL_MODELS.has(type):
		return {}
	var def: Array = ANIMAL_MODELS[type]
	var ps: PackedScene = res(def[0])
	if ps == null:
		return {}
	var body_root := Node3D.new()
	parent.add_child(body_root)
	var inst: Node3D = ps.instantiate()
	body_root.add_child(inst)
	inst.rotation_degrees.y = def[2]
	# Normalise size: measure the mesh in world space and scale to the wanted length.
	var box := AABB()
	var first := true
	for mi in inst.find_children("*", "MeshInstance3D", true, false):
		var b: AABB = (body_root.global_transform.affine_inverse() * mi.global_transform) * mi.get_aabb()
		box = b if first else box.merge(b)
		first = false
	var length := maxf(box.size.x, box.size.z)
	var k: float = def[1] / maxf(length, 0.0001)
	inst.scale *= k
	inst.position.y = -box.position.y * k
	var ap: AnimationPlayer = inst.find_child("AnimationPlayer", true, false)
	var head := Node3D.new()
	body_root.add_child(head)
	return {"body": body_root, "head": head, "legs": [], "size": minf(def[1], 1.7) * 0.32, "anim": ap}


## Returns a dictionary of the moving parts so the Animal script can animate them.
static func animal(parent: Node3D, type: String) -> Dictionary:
	var modeled := _model_animal(parent, type)
	if not modeled.is_empty():
		return modeled
	var body_root := Node3D.new()
	parent.add_child(body_root)
	var parts := {"body": body_root}
	var c: Array = {
		"cow": [Color("#ffffff"), Color("#3b3b3b"), 0.55], "chicken": [Color("#ffffff"), Color("#f2b134"), 0.28],
		"sheep": [Color("#f4f1ea"), Color("#4a3d3a"), 0.5], "rabbit": [Color("#e9d7c4"), Color("#ffb6c1"), 0.26],
		"pig": [Color("#ffb3c1"), Color("#ff8fa6"), 0.45], "goat": [Color("#d9c3a5"), Color("#6b5443"), 0.45],
		"golden_hen": [Color("#ffd23f"), Color("#f08a24"), 0.3],
	}.get(type, [Color.WHITE, Color.GRAY, 0.4])
	var main: Color = c[0]; var accent: Color = c[1]; var s: float = c[2]
	var small := type in ["chicken", "rabbit", "golden_hen"]
	var leg_h := s * 0.6 if not small else s * 0.4
	var head := Node3D.new()
	body_root.add_child(head)
	parts["head"] = head
	var legs: Array = []
	var shape := "chicken" if type == "golden_hen" else type
	match shape:
		"chicken":
			sphere(body_root, s, main, Vector3(0, leg_h + s, 0), Vector3(1, 0.95, 1.2))
			sphere(body_root, s * 0.45, main, Vector3(0, leg_h + s * 1.25, -s * 0.9), Vector3(0.6, 1, 1))  # tail
			head.position = Vector3(0, leg_h + s * 1.8, s * 0.7)
			sphere(head, s * 0.55, main, Vector3.ZERO)
			sphere(head, s * 0.22, Color("#e8384f"), Vector3(0, s * 0.5, 0), Vector3(0.6, 1, 1.4))  # comb
			prism(head, Vector3(s * 0.25, s * 0.3, s * 0.25), accent, Vector3(0, -s * 0.05, s * 0.55), Vector3(90, 0, 0))  # beak
		"rabbit":
			sphere(body_root, s, main, Vector3(0, leg_h + s * 0.9, 0), Vector3(1, 0.9, 1.25))
			sphere(body_root, s * 0.3, Color.WHITE, Vector3(0, leg_h + s * 1.0, -s * 1.15))  # tail
			head.position = Vector3(0, leg_h + s * 1.75, s * 0.75)
			sphere(head, s * 0.6, main, Vector3.ZERO)
			for x in [-1, 1]:
				var ear := sphere(head, s * 0.18, main, Vector3(x * s * 0.25, s * 0.85, -s * 0.1), Vector3(1, 3.2, 0.7))
				ear.rotation_degrees.z = x * -10
				sphere(head, s * 0.12, accent, Vector3(x * s * 0.25, s * 0.85, -s * 0.02), Vector3(0.7, 2.6, 0.4))
			sphere(head, s * 0.1, accent, Vector3(0, -s * 0.05, s * 0.58))
		_:
			var body_len := 1.5 if type == "cow" else 1.3
			sphere(body_root, s, main, Vector3(0, leg_h + s * 0.85, 0), Vector3(1.0, 0.9, body_len))
			if type == "sheep":
				for k in 6:
					var a := k * TAU / 6.0
					sphere(body_root, s * 0.45, main.darkened(0.03), Vector3(cos(a) * s * 0.7, leg_h + s * 1.35, sin(a) * s * 0.9), Vector3.ONE, 8)
			if type == "cow":
				sphere(body_root, s * 0.4, accent, Vector3(s * 0.75, leg_h + s * 1.0, s * 0.2), Vector3(0.5, 1, 1))
				sphere(body_root, s * 0.35, accent, Vector3(-s * 0.7, leg_h + s * 1.1, -s * 0.5), Vector3(0.5, 1, 1))
				sphere(body_root, s * 0.22, Color("#ffb6c1"), Vector3(0, leg_h + s * 0.2, -s * 0.3))  # udder
			if type == "pig":
				var tail := cyl(body_root, 0.02, 0.03, s * 0.4, accent, Vector3(0, leg_h + s * 1.1, -s * 1.3), Vector3(-50, 0, 0), 4)
				tail.name = "Tail"
			head.position = Vector3(0, leg_h + s * 1.3, s * body_len * 0.95)
			var head_col := Color("#3b3b3b") if type == "sheep" else main
			sphere(head, s * 0.58, head_col, Vector3.ZERO, Vector3(1, 1, 1.05))
			match type:
				"cow":
					sphere(head, s * 0.38, Color("#ffc9b5"), Vector3(0, -s * 0.2, s * 0.45), Vector3(1.2, 0.8, 0.8))
					for x in [-1, 1]:
						cyl(head, 0.02, 0.04, s * 0.35, Color("#f3e3c0"), Vector3(x * s * 0.4, s * 0.5, -s * 0.05), Vector3(0, 0, x * -35), 5)
						sphere(head, s * 0.16, accent, Vector3(x * s * 0.6, s * 0.2, -s * 0.05), Vector3(1.6, 0.6, 1))
				"pig":
					cyl(head, s * 0.22, s * 0.22, s * 0.15, accent, Vector3(0, -s * 0.05, s * 0.58), Vector3(90, 0, 0), 10)
					for x in [-1, 1]:
						prism(head, Vector3(s * 0.3, s * 0.3, s * 0.08), accent, Vector3(x * s * 0.35, s * 0.55, 0), Vector3(0, 0, x * -20))
				"goat":
					for x in [-1, 1]:
						cyl(head, 0.015, 0.04, s * 0.45, accent, Vector3(x * s * 0.22, s * 0.6, -s * 0.15), Vector3(-30, 0, 0), 5)
						sphere(head, s * 0.14, main, Vector3(x * s * 0.6, s * 0.15, 0), Vector3(1.8, 0.6, 1))
					sphere(head, s * 0.12, Color("#f4f1ea"), Vector3(0, -s * 0.6, s * 0.3), Vector3(0.8, 1.6, 0.8))  # beard
				"sheep":
					sphere(head, s * 0.4, main, Vector3(0, s * 0.45, -s * 0.05), Vector3(1.2, 0.7, 1.1))
					for x in [-1, 1]:
						sphere(head, s * 0.14, head_col, Vector3(x * s * 0.6, s * 0.1, 0), Vector3(1.8, 0.6, 1))
	# Eyes: big and friendly
	for x in [-1, 1]:
		var eye_r := s * (0.14 if small else 0.11)
		var fwd := s * (0.5 if small else 0.52)
		sphere(head, eye_r, Color.WHITE, Vector3(x * s * 0.25, s * 0.12, fwd), Vector3.ONE, 8)
		sphere(head, eye_r * 0.6, Color("#1d1d1d"), Vector3(x * s * 0.25, s * 0.12, fwd + eye_r * 0.6), Vector3.ONE, 6)
	# Legs
	var bird := shape == "chicken"
	var leg_col := Color("#f2b134") if bird else main.darkened(0.15)
	var xs := [-s * 0.25, s * 0.25] if bird else [-s * 0.5, s * 0.5]
	var zs := [0.0] if bird else [-s * 0.7, s * 0.7]
	for x in xs:
		for z in zs:
			var leg := Node3D.new()
			leg.position = Vector3(x, leg_h, z)
			body_root.add_child(leg)
			cyl(leg, s * 0.09 if not bird else 0.025, s * 0.11 if not bird else 0.025, leg_h, leg_col, Vector3(0, -leg_h / 2, 0), Vector3.ZERO, 6)
			legs.append(leg)
	parts["legs"] = legs
	parts["size"] = s
	return parts

static func shadow_blob(parent: Node3D, r: float) -> void:
	var m := cyl(parent, r, r, 0.01, Color(0, 0, 0, 0.18), Vector3(0, 0.02, 0), Vector3.ZERO, 12)
	m.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF

# ------------------------------------------------------------ workshops

static func workshop(parent: Node3D, id: String) -> Node3D:
	## Returns a node that may be animated (chimney smoke anchor / moving part) or null.
	match id:
		"bakery":
			house(parent, Color("#ffe7c2"), Color("#b5652b"), 3.0, 2.4, 2.0)
			box(parent, Vector3(0.45, 1.0, 0.45), Color("#b9b2a8"), Vector3(-0.8, 2.9, -0.3))
			sphere(parent, 0.35, Color("#e09a4a"), Vector3(0, 2.2, 1.3), Vector3(1.3, 0.8, 0.5))  # bread sign
		"dairy":
			house(parent, Color("#f4fbff"), Color("#4f8fd6"), 3.0, 2.4, 2.0)
			box(parent, Vector3(0.45, 1.0, 0.45), Color("#b9b2a8"), Vector3(-0.8, 2.9, -0.3))
			for x in [1.75, 2.05]:
				cyl(parent, 0.16, 0.2, 0.6, Color("#d9e2ea"), Vector3(x, 0.3, 0.6), Vector3.ZERO, 8)
		"juice_press":
			house(parent, Color("#e6f7c8"), Color("#f08a24"), 3.0, 2.4, 2.0)
			box(parent, Vector3(0.45, 1.0, 0.45), Color("#b9b2a8"), Vector3(-0.8, 2.9, -0.3))
			for z in [-0.3, 0.4]:
				cyl(parent, 0.32, 0.32, 0.8, Color("#a0663a"), Vector3(1.9, 0.4, z), Vector3.ZERO, 10)
		"jam_kitchen":
			house(parent, Color("#ffe1ec"), Color("#9b5de5"), 3.0, 2.4, 2.0)
			box(parent, Vector3(0.45, 1.0, 0.45), Color("#b9b2a8"), Vector3(-0.8, 2.9, -0.3))
			for i in 3:
				cyl(parent, 0.12, 0.12, 0.28, Color("#e8384f"), Vector3(-0.5 + i * 0.5, 1.25, 1.25), Vector3.ZERO, 8)
	var smoke := Node3D.new()
	smoke.position = Vector3(-0.8, 3.5, -0.3)
	parent.add_child(smoke)
	return smoke

static func build_site(parent: Node3D) -> void:
	## "For sale" plot: wooden sign and corner posts.
	cyl(parent, 0.07, 0.07, 1.4, Color("#8a5a36"), Vector3(0, 0.7, 0.9))
	box(parent, Vector3(1.2, 0.7, 0.08), Color("#ffe08a"), Vector3(0, 1.25, 0.95))
	box(parent, Vector3(0.5, 0.12, 0.1), Color("#e2574c"), Vector3(0, 1.3, 1.0))
	box(parent, Vector3(0.12, 0.5, 0.1), Color("#e2574c"), Vector3(0, 1.3, 1.0))
	for x in [-1.4, 1.4]:
		for z in [-1.1, 1.1]:
			box(parent, Vector3(0.12, 0.4, 0.12), Color("#ffb547"), Vector3(x, 0.2, z))
	var g := box(parent, Vector3(2.9, 0.03, 2.3), Color("#c9b98f"), Vector3(0, 0.02, 0))
	g.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF

# ------------------------------------------------------------ decorations

static func decoration(parent: Node3D, id: String) -> Node3D:
	## Returns a node to animate (windmill blades, fountain water) or null.
	match id:
		"flower_bed":
			box(parent, Vector3(2.0, 0.25, 1.0), Color("#a0663a"), Vector3(0, 0.12, 0))
			box(parent, Vector3(1.8, 0.05, 0.8), Color("#5a3720"), Vector3(0, 0.26, 0))
			var cols := [Color("#ff6f91"), Color("#ffd23f"), Color("#b388ff"), Color("#ff9f43")]
			for i in 10:
				var f := Node3D.new()
				f.position = Vector3(-0.75 + (i % 5) * 0.37, 0.25, -0.2 + (i / 5) * 0.4)
				parent.add_child(f)
				flower(f, cols[i % cols.size()])
		"bench":
			for z in [-0.18, 0.0, 0.18]:
				box(parent, Vector3(1.6, 0.06, 0.15), Color("#c08a52"), Vector3(0, 0.45, z))
			box(parent, Vector3(1.6, 0.35, 0.06), Color("#c08a52"), Vector3(0, 0.75, -0.28))
			for x in [-0.7, 0.7]:
				box(parent, Vector3(0.08, 0.45, 0.5), Color("#4a4a4a"), Vector3(x, 0.22, 0))
		"scarecrow":
			cyl(parent, 0.05, 0.05, 1.8, Color("#8a5a36"), Vector3(0, 0.9, 0))
			cyl(parent, 0.04, 0.04, 1.4, Color("#8a5a36"), Vector3(0, 1.3, 0), Vector3(0, 0, 90))
			box(parent, Vector3(0.6, 0.6, 0.25), Color("#4f8fd6"), Vector3(0, 1.15, 0))
			sphere(parent, 0.22, Color("#f2d6a2"), Vector3(0, 1.75, 0))
			cyl(parent, 0.18, 0.35, 0.18, Color("#e8c547"), Vector3(0, 1.98, 0), Vector3.ZERO, 10)
		"birdhouse":
			cyl(parent, 0.05, 0.05, 1.6, Color("#8a5a36"), Vector3(0, 0.8, 0))
			box(parent, Vector3(0.45, 0.45, 0.45), Color("#7cc6ff"), Vector3(0, 1.8, 0))
			prism(parent, Vector3(0.6, 0.3, 0.55), Color("#e2574c"), Vector3(0, 2.17, 0))
			sphere(parent, 0.07, Color("#3a2a1a"), Vector3(0, 1.85, 0.23), Vector3(1, 1, 0.3))
		"windmill":
			cyl(parent, 0.7, 1.1, 4.0, Color("#fff3d6"), Vector3(0, 2.0, 0), Vector3.ZERO, 8)
			cyl(parent, 0.05, 0.9, 0.9, Color("#e2574c"), Vector3(0, 4.45, 0), Vector3.ZERO, 8)
			var hub := Node3D.new()
			hub.position = Vector3(0, 3.6, 0.95)
			parent.add_child(hub)
			sphere(hub, 0.15, Color("#8a5a36"), Vector3.ZERO)
			for k in 4:
				var blade := box(hub, Vector3(0.35, 1.7, 0.04), Color("#ffffff"), Vector3.ZERO)
				blade.rotation.z = k * PI / 2.0
				blade.position = Vector3(sin(k * PI / 2.0) * -0.9, cos(k * PI / 2.0) * 0.9, 0)
			return hub
		"fountain":
			cyl(parent, 1.3, 1.4, 0.45, Color("#cfd4dc"), Vector3(0, 0.22, 0), Vector3.ZERO, 16)
			cyl(parent, 1.15, 1.15, 0.05, Color("#58b7ea"), Vector3(0, 0.44, 0), Vector3.ZERO, 16)
			cyl(parent, 0.15, 0.2, 1.1, Color("#cfd4dc"), Vector3(0, 0.9, 0), Vector3.ZERO, 8)
			cyl(parent, 0.55, 0.35, 0.18, Color("#cfd4dc"), Vector3(0, 1.45, 0), Vector3.ZERO, 12)
			var w := Node3D.new()
			w.position = Vector3(0, 1.6, 0)
			parent.add_child(w)
			sphere(w, 0.25, Color("#9fdcff"), Vector3.ZERO, Vector3(1, 0.6, 1), 10)
			return w
		"rainbow_arch":
			var cols := [Color("#ff5a5f"), Color("#ffb547"), Color("#ffe066"), Color("#6cc24a"), Color("#4fb3e8"), Color("#9b5de5")]
			for band in cols.size():
				var r := 2.0 - band * 0.18
				for k in 13:
					var a := PI * k / 12.0
					var seg := box(parent, Vector3(0.2, 0.5, 0.18), cols[band], Vector3(cos(a) * r, sin(a) * r, 0))
					seg.rotation.z = a
			for x in [-2.1, 2.1]:
				sphere(parent, 0.45, Color.WHITE, Vector3(x, 0.2, 0), Vector3(1.4, 0.8, 1))
		"balloon":
			var b := Node3D.new()
			b.position = Vector3(0, 3.6, 0)
			parent.add_child(b)
			sphere(b, 1.1, Color("#ff6f91"), Vector3.ZERO, Vector3(1, 1.15, 1), 14)
			sphere(b, 1.12, Color("#ffd23f"), Vector3.ZERO, Vector3(0.45, 1.16, 1.02), 14)
			for k in 4:
				var a := k * TAU / 4.0 + PI / 4.0
				cyl(b, 0.015, 0.015, 1.2, Color("#8a5a36"), Vector3(cos(a) * 0.35, -1.6, sin(a) * 0.35), Vector3.ZERO, 4)
			box(b, Vector3(0.7, 0.5, 0.7), Color("#c08a52"), Vector3(0, -2.3, 0))
			cyl(parent, 0.03, 0.03, 1.2, Color("#8a5a36"), Vector3(0, 0.6, 0), Vector3.ZERO, 4)
			return b
	return null

# ------------------------------------------------------------------ batching (performance)

static var _vc_mat: StandardMaterial3D

## Merges every opaque, shared-material mesh under `root` (except subtrees in `skip`, nodes with a
## script, and MultiMeshes) into one mesh with vertex colours. Hundreds of draw calls become one,
## which matters a lot on low-end phones. Returns the number of meshes merged.
static func merge_static(root: Node3D, skip: Array = []) -> int:
	if _vc_mat == null:
		_vc_mat = StandardMaterial3D.new()
		_vc_mat.vertex_color_use_as_albedo = true
		_vc_mat.vertex_color_is_srgb = true
		_vc_mat.roughness = 0.85
	var found: Array = []
	_collect(root, root, skip, found)
	if found.is_empty():
		return 0
	var groups := {true: [], false: []}   # casts shadow?
	for mi in found:
		groups[mi.cast_shadow != GeometryInstance3D.SHADOW_CASTING_SETTING_OFF].append(mi)
	var inv := root.global_transform.affine_inverse()
	for shadow in groups:
		var list: Array = groups[shadow]
		if list.is_empty():
			continue
		var verts := PackedVector3Array(); var norms := PackedVector3Array()
		var cols := PackedColorArray(); var idx := PackedInt32Array()
		for mi in list:
			var xf: Transform3D = inv * mi.global_transform
			var nb := xf.basis.inverse().transposed()
			for si in mi.mesh.get_surface_count():
				var col: Color = _surface_mat(mi, si).albedo_color
				var a: Array = mi.mesh.surface_get_arrays(si)
				var base := verts.size()
				var v: PackedVector3Array = a[Mesh.ARRAY_VERTEX]
				var n: PackedVector3Array = a[Mesh.ARRAY_NORMAL]
				for i in v.size():
					verts.append(xf * v[i])
					norms.append((nb * n[i]).normalized())
					cols.append(col)
				var ix: PackedInt32Array = a[Mesh.ARRAY_INDEX]
				if ix.is_empty():
					for i in v.size(): idx.append(base + i)
				else:
					for i in ix: idx.append(base + i)
			mi.get_parent().remove_child(mi)
			mi.free()
		var arr := []
		arr.resize(Mesh.ARRAY_MAX)
		arr[Mesh.ARRAY_VERTEX] = verts; arr[Mesh.ARRAY_NORMAL] = norms
		arr[Mesh.ARRAY_COLOR] = cols; arr[Mesh.ARRAY_INDEX] = idx
		var am := ArrayMesh.new()
		am.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arr)
		var out := MeshInstance3D.new()
		out.mesh = am
		out.material_override = _vc_mat
		if not shadow:
			out.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		root.add_child(out)
	return found.size()

static func _mergeable(mi: MeshInstance3D) -> bool:
	if not (mi.mesh is ArrayMesh or mi.mesh is PrimitiveMesh):
		return false
	for i in mi.mesh.get_surface_count():
		var m: Material = _surface_mat(mi, i)
		if not (m is StandardMaterial3D) or m.albedo_texture != null or m.albedo_color.a < 1.0 \
				or m.transparency != BaseMaterial3D.TRANSPARENCY_DISABLED:
			return false
	return true

static func _surface_mat(mi: MeshInstance3D, i: int) -> Material:
	if mi.get_surface_override_material(i):
		return mi.get_surface_override_material(i)
	if mi.material_override:
		return mi.material_override
	return mi.mesh.surface_get_material(i)

static func _collect(n: Node, root: Node, skip: Array, out: Array) -> void:
	for c in n.get_children():
		if c in skip or (c != root and c.get_script() != null):
			continue
		if c is MeshInstance3D and c.mesh != null and _mergeable(c):
			out.append(c)
		if not (c is MeshInstance3D):
			_collect(c, root, skip, out)
