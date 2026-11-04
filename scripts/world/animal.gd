extends Node3D
## One animal: wanders inside its pen, idles, plays an eating animation when fed,
## and shows a status bubble (hungry / product ready).

const MF := preload("res://scripts/world/model_factory.gd")

var index := 0
var type := ""
var pen := Rect2()
var _parts: Dictionary = {}
var _body: Node3D
var _goal := Vector3.ZERO
var _mode := "idle"   # idle | walk | eat
var _mode_time := 0.0
var _t := 0.0
var _speed := 1.0
var _bubble: Node3D
var _bubble_item: MeshInstance3D
var _status := ""

const PRODUCT_COLORS := {"egg": "#fff4dc", "milk": "#f5fbff", "wool": "#f4f1ea", "fluff": "#ffe4ef", "truffle": "#6b4a32", "goat_milk": "#fffbea", "golden_egg": "#ffd23f"}

func setup(idx: int, animal_type: String, pen_rect: Rect2) -> void:
	index = idx
	type = animal_type
	pen = pen_rect
	_parts = MF.animal(self, type)
	_body = _parts["body"]
	var s: float = _parts["size"]
	MF.shadow_blob(self, s * 1.2)
	var body := MF.collider(self, Vector3(s * 2.6, s * 3.0, s * 3.2), Vector3(0, s * 1.4, 0))
	body.set_meta("target", self)
	_speed = {"golden_hen": 1.2, "chicken": 1.2, "rabbit": 1.6, "cow": 0.6, "pig": 0.8}.get(type, 0.8)
	_bubble = Node3D.new()
	add_child(_bubble)
	var disc := MF.sphere(_bubble, 0.32, Color("#ffe08a"), Vector3.ZERO, Vector3(1, 1, 0.35), 12)
	disc.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	_bubble_item = MF.sphere(_bubble, 0.17, Color.WHITE, Vector3(0, 0, 0.1), Vector3.ONE, 8)
	_bubble_item.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	_bubble.position = Vector3(0, s * 3.2 + 0.5, 0)
	_pick_goal()
	_t = randf() * 10.0
	refresh()

func kind() -> String:
	return "animal"

func refresh() -> void:
	var st := GameState.animal_status(index)
	if st == _status:
		return
	_status = st
	var def: Dictionary = Config.animals()[type]
	match st:
		"hungry":
			_bubble.visible = true
			_bubble_item.material_override = MF.mat(Color(Config.crops()[def["feed_item"]]["color"]))
		"ready":
			_bubble.visible = true
			_bubble_item.material_override = MF.mat(Color(PRODUCT_COLORS.get(def["product"], "#ffffff")))
		_:
			_bubble.visible = false

func _anim(name: String) -> void:
	var ap: AnimationPlayer = _parts.get("anim")
	if ap == null:
		return
	for n in ["Armature|" + name, name, "Armature|Idle"]:
		if ap.has_animation(n):
			if ap.current_animation != n:
				var a := ap.get_animation(n)
				a.loop_mode = Animation.LOOP_LINEAR
				ap.play(n, 0.25)
			return

func play_eat() -> void:
	_mode = "eat"
	_mode_time = 2.0

func play_happy() -> void:
	var tw := create_tween()
	tw.tween_property(_body, "position:y", 0.5, 0.15).set_ease(Tween.EASE_OUT)
	tw.tween_property(_body, "position:y", 0.0, 0.25).set_trans(Tween.TRANS_BOUNCE).set_ease(Tween.EASE_OUT)

func _pick_goal() -> void:
	_goal = Vector3(randf_range(pen.position.x, pen.end.x), 0, randf_range(pen.position.y, pen.end.y))

func _process(delta: float) -> void:
	_t += delta
	_mode_time -= delta
	var head: Node3D = _parts["head"]
	match _mode:
		"walk":
			var to := _goal - position
			to.y = 0
			if to.length() < 0.2:
				_mode = "idle"
				_mode_time = randf_range(1.5, 4.0)
			else:
				_anim("WalkSlow" if type == "cow" else "Walk")
				var dir := to.normalized()
				position += dir * _speed * delta
				rotation.y = lerp_angle(rotation.y, atan2(dir.x, dir.z), 1.0 - exp(-delta * 6.0))
				var swing := sin(_t * _speed * 9.0) * 0.5
				var legs: Array = _parts["legs"]
				for i in legs.size():
					legs[i].rotation.x = swing * (1 if i % 2 == 0 else -1)
				_body.position.y = absf(sin(_t * _speed * 9.0)) * (0.12 if type == "rabbit" else 0.04)
				head.rotation.x = 0
		"eat":
			head.rotation.x = 0.5 + sin(_t * 10.0) * 0.12
			if _mode_time <= 0:
				_mode = "idle"
				_mode_time = 1.0
		_:
			_anim("Idle")
			head.rotation.x = sin(_t * 1.3) * 0.08
			head.rotation.y = sin(_t * 0.7) * 0.25
			_body.scale = Vector3(1, 1.0 + sin(_t * 2.0) * 0.015, 1)  # breathing
			for leg in _parts["legs"]:
				leg.rotation.x = lerpf(leg.rotation.x, 0.0, delta * 8.0)
			if _mode_time <= 0:
				_mode = "walk"
				_pick_goal()
	if _bubble.visible:
		_bubble.position.y = _parts["size"] * 3.2 + 0.5 + sin(_t * 2.5) * 0.08
		_bubble.global_rotation = Vector3(0, get_viewport().get_camera_3d().global_rotation.y, 0) if get_viewport().get_camera_3d() else Vector3.ZERO
	if fmod(_t, 0.5) < delta:
		refresh()
