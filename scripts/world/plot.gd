extends Node3D
## One farm plot. Rebuilds its crop model only when the growth stage changes.

const MF := preload("res://scripts/world/model_factory.gd")
const SIZE := 2.3

var index := 0
var _crop_root: Node3D
var _soil: MeshInstance3D
var _stage := -2
var _crop_id := ""
var _watered := false
var _ready_marker: Node3D
var _t := 0.0

func setup(idx: int) -> void:
	index = idx
	MF.box(self, Vector3(SIZE + 0.2, 0.18, SIZE + 0.2), Color("#8a5a36"), Vector3(0, 0.09, 0))  # border
	_soil = MF.box(self, Vector3(SIZE, 0.2, SIZE), Color("#6b4226"), Vector3(0, 0.12, 0))
	for z in [-0.6, 0.0, 0.6]:
		MF.box(self, Vector3(SIZE - 0.2, 0.05, 0.12), Color("#5a3720"), Vector3(0, 0.23, z))
	_crop_root = Node3D.new()
	_crop_root.position.y = 0.22
	add_child(_crop_root)
	var body := MF.collider(self, Vector3(SIZE, 0.8, SIZE), Vector3(0, 0.4, 0))
	body.set_meta("target", self)
	_ready_marker = Node3D.new()
	add_child(_ready_marker)
	MF.sphere(_ready_marker, 0.22, Color("#ffd23f"), Vector3.ZERO, Vector3(1, 1, 1), 10)
	MF.sphere(_ready_marker, 0.11, Color("#ffffff"), Vector3(0, 0.38, 0), Vector3(1, 1, 1), 6)
	_ready_marker.visible = false
	refresh()

func kind() -> String:
	return "plot"

func refresh() -> void:
	var p: Dictionary = GameState.plot(index)
	var status := GameState.plot_status(index)
	var stage := -1
	if status != "empty":
		var prog := GameState.plot_progress(index)
		stage = 3 if status == "ready" else (0 if prog < 0.15 else (1 if prog < 0.5 else 2))
	var watered: bool = p.get("watered", false)
	if watered != _watered or stage != _stage:
		_soil.material_override = MF.mat(Color("#4a2c18") if watered and status == "growing" else Color("#6b4226"))
		_watered = watered
	if stage == _stage and p["crop"] == _crop_id:
		return
	var grew := stage > _stage and _stage >= -1
	_stage = stage
	_crop_id = p["crop"]
	for c in _crop_root.get_children():
		c.queue_free()
	_ready_marker.visible = stage == 3
	if stage < 0:
		return
	var color := Color(Config.crops()[_crop_id].get("color", "#ffffff"))
	MF.crop(_crop_root, _crop_id, stage, color)
	if grew:
		# Small "pop" when a crop reaches a new stage
		_crop_root.scale = Vector3(0.6, 0.3, 0.6)
		create_tween().tween_property(_crop_root, "scale", Vector3.ONE, 0.35).set_trans(Tween.TRANS_BACK).set_ease(Tween.EASE_OUT)

func _process(delta: float) -> void:
	_t += delta
	if _ready_marker.visible:
		_ready_marker.position = Vector3(0, 1.6 + sin(_t * 3.0) * 0.12, 0)
		_ready_marker.rotation.y = _t * 1.5
	# Cheap periodic check: stage changes are rare, so check 4x per second.
	if fmod(_t, 0.25) < delta:
		refresh()
