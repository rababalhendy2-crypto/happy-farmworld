extends Node
## Simulates a finger tap (screen touch, as on Android) on HUD buttons and checks they react.
func _tap(pos: Vector2) -> void:
	for pressed in [true, false]:
		var e := InputEventScreenTouch.new()
		e.index = 0; e.position = pos; e.pressed = pressed
		Input.parse_input_event(e)
		for i in 3: await get_tree().process_frame

func _ready() -> void:
	TimeService.last_seen = 0.0
	SaveManager.delete_save()
	var main: Node = load("res://scenes/main.tscn").instantiate()
	add_child(main)
	for i in 60: await get_tree().process_frame
	main.hud.close_modal()
	for i in 5: await get_tree().process_frame
	var results := []
	for b in main.hud.root.find_children("*", "Button", true, false):
		if b.is_visible_in_tree() and b.text in [tr("BTN_SHOP"), tr("BTN_SETTINGS"), tr("BTN_INVENTORY"), tr("BTN_MARKET")]:
			main.hud.close_modal()
			for i in 3: await get_tree().process_frame
			await _tap(b.get_global_rect().get_center())
			results.append("%s=%s" % [b.text, main.hud._modal != null and is_instance_valid(main.hud._modal)])
	print("TOUCH ", results)
	SaveManager.delete_save()
	get_tree().quit()
