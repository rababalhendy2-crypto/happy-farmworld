extends Node
## Dev tool: one screenshot of the expanded farm, to tune colours/lighting.
func _ready() -> void:
	TimeService.last_seen = 0.0
	SaveManager.delete_save()
	GameState.state["level"] = 6
	GameState.state["coins"] = 9000
	GameState.state["premium"] = 40
	for ws in ["bakery", "dairy", "juice_press", "jam_kitchen"]: GameState.buy_workshop(ws)
	GameState.buy_expansion("field_north"); GameState.buy_expansion("field_south")
	for d in ["flower_bed", "bench", "scarecrow", "birdhouse", "windmill", "fountain"]: GameState.buy_decoration(d)
	for i in 12: GameState.plant(i, ["wheat", "carrot", "strawberry", "tomato", "corn", "pumpkin"][i % 6])
	TimeService.server_offset += 2000
	var main: Node = load("res://scenes/main.tscn").instantiate()
	add_child(main)
	for i in 120: await get_tree().process_frame
	main.hud.close_modal()
	var d := float(OS.get_environment("LOOK_DIST")) if OS.get_environment("LOOK_DIST") != "" else 26.0
	var fx := OS.get_environment("LOOK_FOCUS").split(",") if OS.get_environment("LOOK_FOCUS") != "" else PackedStringArray(["0", "0", "1.5"])
	main.rig.dist = d
	main.rig.focus(Vector3(float(fx[0]), float(fx[1]), float(fx[2])))
	for i in 60: await get_tree().process_frame
	await RenderingServer.frame_post_draw
	get_viewport().get_texture().get_image().save_png("user://look.png")
	print("PERF draw_calls=", Performance.get_monitor(Performance.RENDER_TOTAL_DRAW_CALLS_IN_FRAME), " objects=", Performance.get_monitor(Performance.RENDER_TOTAL_OBJECTS_IN_FRAME), " prims=", Performance.get_monitor(Performance.RENDER_TOTAL_PRIMITIVES_IN_FRAME), " nodes=", Performance.get_monitor(Performance.OBJECT_NODE_COUNT), " mem_mb=", Performance.get_monitor(Performance.MEMORY_STATIC) / 1048576.0)
	SaveManager.delete_save()
	get_tree().quit()
