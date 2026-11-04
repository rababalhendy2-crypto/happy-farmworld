extends Node3D
## A production building, or its empty build site before it is bought.

const MF := preload("res://scripts/world/model_factory.gd")

var id := ""
var _owned := -1
var _model: Node3D
var _smoke_anchor: Node3D
var _marker: Node3D
var _smoke: CPUParticles3D
var _t := 0.0

func setup(ws_id: String) -> void:
	id = ws_id
	set_meta("building", id)
	var body := MF.collider(self, Vector3(3.2, 3.0, 2.6), Vector3(0, 1.5, 0))
	body.set_meta("target", self)
	_marker = Node3D.new()
	add_child(_marker)
	MF.sphere(_marker, 0.25, Color("#ffd23f"), Vector3.ZERO, Vector3.ONE, 10)
	MF.sphere(_marker, 0.12, Color.WHITE, Vector3(0, 0.42, 0), Vector3.ONE, 6)
	_marker.visible = false
	refresh()

func owned() -> bool:
	return GameState.state["workshops"].has(id)

func refresh() -> void:
	var o := 1 if owned() else 0
	if o != _owned:
		_owned = o
		if _model:
			_model.queue_free()
		_model = Node3D.new()
		add_child(_model)
		if o == 1:
			_smoke_anchor = MF.workshop(_model, id)
			_smoke = _make_smoke()
			_smoke_anchor.add_child(_smoke)
			if _owned == 1 and is_inside_tree():
				_model.scale = Vector3(0.2, 0.2, 0.2)
				create_tween().tween_property(_model, "scale", Vector3.ONE, 0.5).set_trans(Tween.TRANS_BACK).set_ease(Tween.EASE_OUT)
		else:
			MF.build_site(_model)
	if o == 1:
		var jobs: Array = GameState.state["workshops"][id]["jobs"]
		var any_ready := false
		for j in jobs:
			if GameState.job_remaining(j) <= 0.0:
				any_ready = true
		_marker.visible = any_ready
		_smoke.emitting = jobs.size() > 0 and not any_ready
	else:
		_marker.visible = false

func _make_smoke() -> CPUParticles3D:
	var p := CPUParticles3D.new()
	p.amount = 6
	p.lifetime = 2.0
	p.direction = Vector3.UP
	p.spread = 15.0
	p.initial_velocity_min = 0.5
	p.initial_velocity_max = 0.8
	p.gravity = Vector3(0.2, 0.2, 0)
	p.scale_amount_min = 0.8
	p.scale_amount_max = 1.6
	var m := SphereMesh.new()
	m.radius = 0.15
	m.height = 0.3
	m.radial_segments = 6
	m.rings = 3
	var mat := StandardMaterial3D.new()
	mat.albedo_color = Color(0.95, 0.95, 0.95, 0.7)
	mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	m.material = mat
	p.mesh = m
	p.emitting = false
	return p

func _process(delta: float) -> void:
	_t += delta
	if _marker.visible:
		_marker.position = Vector3(0, 4.2 + sin(_t * 3.0) * 0.12, 0)
	if fmod(_t, 0.5) < delta:
		refresh()
