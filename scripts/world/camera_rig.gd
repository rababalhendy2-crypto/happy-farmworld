extends Node3D
## Farm camera: elevated 3/4 view.
## Touch: 1 finger drag = pan, 2 fingers = pinch zoom + twist rotate, short tap = select.
## Desktop: left drag = pan, right drag = rotate, wheel = zoom, click = select.
## The camera target is clamped inside the farm bounds.

signal tapped(screen_pos: Vector2)

const TAP_SLOP := 14.0

var bounds := Rect2(-15, -13, 30, 26)  # x/z rectangle the target may move in
var min_dist := 9.0
var max_dist := 34.0
var pitch_deg := -52.0

var target := Vector3(0, 0, 1.5)
var yaw := 0.0
var dist := 22.0
var _cur_target := target
var _cur_yaw := yaw
var _cur_dist := dist

var ui_blocker: Callable = func(_p): return false
var camera: Camera3D
var _touches: Dictionary = {}
var _press_pos := Vector2.ZERO
var _moved := false
var _mouse_pan := false
var _mouse_rot := false

func _ready() -> void:
	camera = Camera3D.new()
	camera.fov = 40.0
	camera.far = 200.0
	add_child(camera)
	camera.make_current()
	_apply()

func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventScreenTouch:
		_on_touch(event)
	elif event is InputEventScreenDrag:
		_on_drag(event)
	elif event is InputEventMouseButton and event.device != InputEvent.DEVICE_ID_EMULATION:
		_on_mouse_button(event)
	elif event is InputEventMouseMotion and event.device != InputEvent.DEVICE_ID_EMULATION:
		_on_mouse_motion(event)

func _on_touch(e: InputEventScreenTouch) -> void:
	if e.pressed:
		if ui_blocker.call(e.position):
			return
		_touches[e.index] = e.position
		if _touches.size() == 1:
			_press_pos = e.position
			_moved = false
		else:
			_moved = true
	else:
		if not _touches.has(e.index):
			return
		_touches.erase(e.index)
		if _touches.is_empty() and not _moved:
			tapped.emit(e.position)

func _on_drag(e: InputEventScreenDrag) -> void:
	if not _touches.has(e.index):
		return
	if _touches.size() == 1:
		if e.position.distance_to(_press_pos) > TAP_SLOP:
			_moved = true
		if _moved:
			_pan(e.relative)
	elif _touches.size() >= 2:
		var keys := _touches.keys()
		var other: Vector2 = _touches[keys[1] if keys[0] == e.index else keys[0]]
		var old_vec: Vector2 = _touches[e.index] - other
		var new_vec: Vector2 = e.position - other
		if old_vec.length() > 1.0 and new_vec.length() > 1.0:
			_zoom(old_vec.length() / new_vec.length())
			yaw -= new_vec.angle() - old_vec.angle()
	_touches[e.index] = e.position

func _on_mouse_button(e: InputEventMouseButton) -> void:
	match e.button_index:
		MOUSE_BUTTON_WHEEL_UP:
			if e.pressed: _zoom(0.9)
		MOUSE_BUTTON_WHEEL_DOWN:
			if e.pressed: _zoom(1.1)
		MOUSE_BUTTON_LEFT:
			if e.pressed:
				if ui_blocker.call(e.position):
					return
				_mouse_pan = true
				_press_pos = e.position
				_moved = false
			elif _mouse_pan:
				_mouse_pan = false
				if not _moved:
					tapped.emit(e.position)
		MOUSE_BUTTON_RIGHT:
			_mouse_rot = e.pressed

func _on_mouse_motion(e: InputEventMouseMotion) -> void:
	if _mouse_pan:
		if e.position.distance_to(_press_pos) > TAP_SLOP:
			_moved = true
		if _moved:
			_pan(e.relative)
	elif _mouse_rot:
		yaw -= e.relative.x * 0.008

func _pan(rel: Vector2) -> void:
	var vp_h := maxf(get_viewport().get_visible_rect().size.y, 1.0)
	var k := dist * 1.15 / vp_h
	var right := Vector3(cos(yaw), 0, -sin(yaw))
	var fwd := Vector3(-sin(yaw), 0, -cos(yaw))
	target += (-right * rel.x + fwd * rel.y * 1.4) * k
	_clamp()

func _zoom(factor: float) -> void:
	dist = clampf(dist * factor, min_dist, max_dist)

func _clamp() -> void:
	target.x = clampf(target.x, bounds.position.x, bounds.end.x)
	target.z = clampf(target.z, bounds.position.y, bounds.end.y)

func focus(p: Vector3) -> void:
	target = Vector3(p.x, 0, p.z)
	_clamp()

func _process(delta: float) -> void:
	var w := 1.0 - exp(-delta * 10.0)
	_cur_target = _cur_target.lerp(target, w)
	_cur_yaw = lerp_angle(_cur_yaw, yaw, w)
	_cur_dist = lerpf(_cur_dist, dist, w)
	_apply()

func _apply() -> void:
	position = _cur_target
	rotation = Vector3(0, _cur_yaw, 0)
	var p := deg_to_rad(pitch_deg)
	camera.position = Vector3(0, -sin(p) * _cur_dist, cos(p) * _cur_dist)
	camera.rotation = Vector3(p, 0, 0)
