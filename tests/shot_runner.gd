extends Node
## Dev tool: renders the farm and saves screenshots of key screens to user://shots/.
## Run (needs a display, e.g. xvfb-run): godot --path . res://tests/shot.tscn

var main: Node
const PFL := preload("res://scripts/ui/parental_gate.gd")

func _ready() -> void:
	TimeService.last_seen = 0.0  # ignore future timestamps left by earlier test runs
	SaveManager.delete_save()
	main = load("res://scenes/main.tscn").instantiate()
	add_child(main)
	DirAccess.make_dir_recursive_absolute("user://shots")
	await _wait(30)
	while main.hud._modal == null:
		await _wait(5)
	await _wait(10)
	await _shot("00_daily_gift")
	main.hud.close_modal()
	await _wait(10)
	await _shot("01_farm_start")
	# Plant everything that is unlocked, feed animals, then let time pass
	GameState.plant(0, "wheat"); GameState.plant(1, "carrot"); GameState.plant(2, "wheat")
	GameState.plant(3, "carrot"); GameState.plant(4, "wheat")
	GameState.state["inventory"]["wheat"] = 20
	GameState.state["inventory"]["carrot"] = 10
	for i in GameState.state["animals"].size():
		GameState.feed(i)
	TimeService.server_offset += 40
	await _wait(20)
	await _shot("02_growing")
	main.hud.open_plot(1)
	await _wait(10)
	await _shot("03_plot_panel")
	main.hud.open_plot(5)
	await _wait(10)
	await _shot("04_seed_picker")
	main.hud.close_context()
	TimeService.server_offset += 400
	await _wait(20)
	main.rig.dist = 12.0
	main.rig.focus(Vector3(9, 0, 1))
	await _wait(40)
	await _shot("05_animals_ready")
	main.rig.focus(Vector3(-9, 0, 1))
	await _wait(40)
	await _shot("05b_small_animals")
	main.rig.dist = 22.0
	main.rig.focus(Vector3(0, 0, 1.5))
	for i in 6: GameState.harvest(i)
	for i in GameState.state["animals"].size(): GameState.collect(i)
	await _wait(10)
	await _shot("06_after_harvest")
	main.hud.open_market()
	await _wait(10)
	await _shot("07_market")
	Localization.set_language("ar")
	await _wait(5)
	main.hud.open_inventory()
	await _wait(10)
	await _shot("08_inventory_ar")
	main.hud.close_modal()
	main.hud.open_plot(0)
	await _wait(10)
	await _shot("09_seed_picker_ar")
	Localization.set_language("de")
	await _wait(5)
	main.hud.open_settings()
	await _wait(10)
	await _shot("10_settings_de")
	# ---- Phase 3: workshops, land, decorations
	Localization.set_language("en")
	main.hud.close_modal()
	await _wait(5)
	GameState.state["level"] = 6
	GameState.state["coins"] = 5000
	GameState.state["premium"] = 20
	main.hud.open_shop("buildings")
	await _wait(10)
	await _shot("11_shop_buildings")
	for ws in ["bakery", "dairy", "juice_press", "jam_kitchen"]:
		GameState.buy_workshop(ws)
	GameState.buy_expansion("field_north")
	GameState.buy_expansion("field_south")
	GameState.upgrade_storage()
	for d in ["flower_bed", "bench", "scarecrow", "birdhouse", "windmill", "fountain"]:
		GameState.buy_decoration(d)
	GameState.state["inventory"] = {"wheat": 12, "egg": 3, "milk": 4}
	GameState.start_job("bakery", "flour")
	GameState.start_job("dairy", "cheese")
	main.hud.close_modal()
	await _wait(40)
	main.rig.dist = 30.0
	main.rig.focus(Vector3(0, 0, 2))
	await _wait(60)
	await _shot("12_farm_expanded")
	main.rig.dist = 16.0
	main.rig.focus(Vector3(-9, 0, 8))
	await _wait(60)
	await _shot("13_workshops")
	main.hud.open_workshop("bakery")
	await _wait(10)
	await _shot("14_bakery_panel")
	main.hud.open_shop("land")
	await _wait(10)
	await _shot("15_shop_land")
	# ---- Phase 5: missions, profile, parental gate
	main.hud.close_modal()
	for i in 4: GameState.harvest(i)
	main.hud.open_missions("daily")
	await _wait(10)
	await _shot("16_missions")
	main.hud.open_missions("achievements")
	await _wait(10)
	await _shot("17_achievements")
	Localization.set_language("ar")
	await _wait(5)
	main.hud.open_profile()
	await _wait(10)
	await _shot("18_profile_ar")
	PFL.open(main.hud, func(): pass)
	await _wait(10)
	await _shot("19_parent_gate_ar")
	# ---- Phase 6: store
	Localization.set_language("en")
	await _wait(5)
	main.hud.open_shop("store")
	await _wait(10)
	await _shot("20_store")
	main.hud.open_shop("animals")
	await _wait(10)
	await _shot("21_shop_animals")
	main.hud._confirm_purchase("golden_hen", "1.49 €")
	await _wait(10)
	await _shot("22_purchase_confirm")
	SaveManager.delete_save()
	get_tree().quit()

func _wait(frames: int) -> void:
	for i in frames:
		await get_tree().process_frame

func _shot(name: String) -> void:
	await RenderingServer.frame_post_draw
	var img := get_viewport().get_texture().get_image()
	img.save_png("user://shots/%s.png" % name)
	print("shot ", name)
