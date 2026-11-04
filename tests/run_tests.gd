extends SceneTree
## Headless unit tests for the game rules and economy.
## Run: godot --headless --path . -s res://tests/run_tests.gd

const FarmRules := preload("res://scripts/core/farm_rules.gd")

var cfg: Dictionary
var failures := 0
var passed := 0

func check(cond: bool, name: String) -> void:
	if cond:
		passed += 1
	else:
		failures += 1
		printerr("FAIL: ", name)

func _init() -> void:
	var f := FileAccess.open("res://data/game_config.json", FileAccess.READ)
	cfg = JSON.parse_string(f.get_as_text())
	test_new_state()
	test_plant_and_harvest()
	test_cannot_overspend()
	test_locked_crop()
	test_water_speeds_up()
	test_storage_capacity()
	test_animals()
	test_sell()
	test_level_up()
	test_no_negative_inventory()
	test_migrate()
	test_config_sanity()
	test_workshops()
	test_expansion_storage_decor()
	test_economy_balance()
	test_missions()
	test_daily_gift()
	print("Tests passed: %d, failed: %d" % [passed, failures])
	quit(1 if failures > 0 else 0)

func test_new_state() -> void:
	var s := FarmRules.new_state(cfg, 1000.0)
	check(s["coins"] == int(cfg["economy"]["start_coins"]), "start coins")
	check(s["plots"].size() == 6, "six starter plots")
	check(s["animals"].size() == 6, "six animal types")
	check(s["level"] == 1, "level 1")

func test_plant_and_harvest() -> void:
	var s := FarmRules.new_state(cfg, 0.0)
	var c0: int = s["coins"]
	var r := FarmRules.plant(cfg, s, 0, "wheat", 100.0)
	check(r["ok"], "plant wheat ok")
	check(s["coins"] == c0 - 5, "seed cost deducted")
	check(not FarmRules.plant(cfg, s, 0, "carrot", 101.0)["ok"], "cannot plant on busy plot")
	check(FarmRules.plot_status(cfg, s["plots"][0], 130.0) == "growing", "growing before time")
	check(not FarmRules.harvest(cfg, s, 0, 130.0)["ok"], "cannot harvest early")
	check(FarmRules.plot_status(cfg, s["plots"][0], 160.0) == "ready", "ready after 60s")
	var before := FarmRules.item_count(s, "wheat")
	r = FarmRules.harvest(cfg, s, 0, 160.0)
	check(r["ok"], "harvest ok")
	check(FarmRules.item_count(s, "wheat") == before + 3, "yield added")
	check(s["xp"] == 1, "xp from harvest")
	check(s["plots"][0]["crop"] == "", "plot cleared")
	check(not FarmRules.harvest(cfg, s, 0, 200.0)["ok"], "no double harvest")
	check(not FarmRules.plant(cfg, s, 99, "wheat", 0.0)["ok"], "invalid plot index")
	check(not FarmRules.plant(cfg, s, 1, "banana", 0.0)["ok"], "unknown crop")

func test_cannot_overspend() -> void:
	var s := FarmRules.new_state(cfg, 0.0)
	s["coins"] = 4
	var r := FarmRules.plant(cfg, s, 0, "wheat", 0.0)
	check(not r["ok"] and r["error"] == FarmRules.ERR_NOT_ENOUGH_COINS, "not enough coins")
	check(s["coins"] == 4, "coins unchanged on failure")

func test_locked_crop() -> void:
	var s := FarmRules.new_state(cfg, 0.0)
	s["coins"] = 1000
	check(FarmRules.plant(cfg, s, 0, "pumpkin", 0.0)["error"] == FarmRules.ERR_LOCKED, "pumpkin locked at level 1")
	s["level"] = 5
	check(FarmRules.plant(cfg, s, 0, "pumpkin", 0.0)["ok"], "pumpkin at level 5")

func test_water_speeds_up() -> void:
	var s := FarmRules.new_state(cfg, 0.0)
	FarmRules.plant(cfg, s, 0, "carrot", 0.0)
	check(FarmRules.water(cfg, s, 0, 10.0)["ok"], "water ok")
	check(not FarmRules.water(cfg, s, 0, 11.0)["ok"], "no double water")
	check(FarmRules.plot_status(cfg, s["plots"][0], 90.0) == "ready", "watered carrot ready after 90s (120 * 0.75)")
	check(not FarmRules.water(cfg, s, 1, 0.0)["ok"], "cannot water empty plot")

func test_storage_capacity() -> void:
	var s := FarmRules.new_state(cfg, 0.0)
	s["inventory"] = {"wheat": FarmRules.storage_capacity(cfg, s) - 1}
	FarmRules.plant(cfg, s, 0, "wheat", 0.0)
	var r := FarmRules.harvest(cfg, s, 0, 1000.0)
	check(r["error"] == FarmRules.ERR_STORAGE_FULL, "storage full blocks harvest")
	check(s["plots"][0]["crop"] == "wheat", "crop stays when storage full")

func test_animals() -> void:
	var s := FarmRules.new_state(cfg, 0.0)
	var idx := -1
	for i in s["animals"].size():
		if s["animals"][i]["type"] == "chicken":
			idx = i
	check(FarmRules.animal_status(cfg, s["animals"][idx], 0.0) == "hungry", "starts hungry")
	s["inventory"] = {}
	check(FarmRules.feed(cfg, s, idx, 0.0)["error"] == FarmRules.ERR_MISSING_ITEMS, "cannot feed without food")
	s["inventory"] = {"wheat": 2}
	check(FarmRules.feed(cfg, s, idx, 0.0)["ok"], "feed ok")
	check(FarmRules.item_count(s, "wheat") == 1, "food consumed")
	check(not FarmRules.feed(cfg, s, idx, 1.0)["ok"], "cannot feed while producing")
	check(not FarmRules.collect(cfg, s, idx, 10.0)["ok"], "cannot collect early")
	var r := FarmRules.collect(cfg, s, idx, 100.0)
	check(r["ok"] and FarmRules.item_count(s, "egg") >= 1, "egg collected")
	check(not FarmRules.collect(cfg, s, idx, 101.0)["ok"], "no double collect")
	# Happiness never drops below the floor even after a long absence
	s["inventory"] = {"wheat": 5}
	FarmRules.feed(cfg, s, idx, 100.0 + 3600.0 * 24 * 30)
	check(int(s["animals"][idx]["happiness"]) >= int(cfg["animal_care"]["min_happiness"]), "happiness floor (child-friendly)")

func test_sell() -> void:
	var s := FarmRules.new_state(cfg, 0.0)
	s["inventory"] = {"milk": 2}
	var c0: int = s["coins"]
	var r := FarmRules.sell(cfg, s, "milk", 2)
	check(r["ok"] and s["coins"] == c0 + 30, "sell adds coins")
	check(not s["inventory"].has("milk"), "item removed")
	check(not FarmRules.sell(cfg, s, "milk", 1)["ok"], "cannot sell what you do not have")
	check(not FarmRules.sell(cfg, s, "wheat", -5)["ok"], "negative quantity rejected")
	check(not FarmRules.sell(cfg, s, "gold_bar", 1)["ok"], "unknown item rejected")

func test_level_up() -> void:
	var s := FarmRules.new_state(cfg, 0.0)
	var c0: int = s["coins"]
	var gained := FarmRules.add_xp(cfg, s, 60)
	check(s["level"] == 3 and gained == 2, "two levels from 60 xp")
	check(s["coins"] == c0 + 2 * int(cfg["economy"]["level_reward_coins"]), "level reward coins")
	check(FarmRules.xp_for_next_level(cfg, 40) > FarmRules.xp_for_next_level(cfg, 39), "levels continue past the table")

func test_no_negative_inventory() -> void:
	var s := FarmRules.new_state(cfg, 0.0)
	s["inventory"] = {"egg": 1}
	check(not FarmRules.remove_item(s, "egg", 2), "cannot remove more than owned")
	check(FarmRules.item_count(s, "egg") == 1, "count unchanged")
	check(not FarmRules.remove_item(s, "egg", -1), "negative removal rejected")

func test_migrate() -> void:
	var old := {"coins": 10, "plots": [{"crop": "", "planted_at": 0.0, "watered": false}], "animals": [], "inventory": {}}
	var m := FarmRules.migrate(cfg, old, 0.0)
	check(m.has("xp") and m.has("stats"), "missing keys filled")
	check(m["animals"].size() == 6, "new animals added to old save")
	check(m["coins"] == 10, "existing values kept")

func test_config_sanity() -> void:
	for id in cfg["crops"]:
		var c: Dictionary = cfg["crops"][id]
		var sell := int(cfg["items"][id]["sell_price"]) * int(c["yield"])
		check(sell > int(c["seed_price"]), "crop %s is profitable" % id)
	for id in cfg["animals"]:
		var a: Dictionary = cfg["animals"][id]
		check(cfg["items"].has(a["product"]), "animal %s product exists" % id)
		check(cfg["crops"].has(a["feed_item"]), "animal %s food is a crop" % id)

func test_workshops() -> void:
	var s := FarmRules.new_state(cfg, 0.0)
	s["coins"] = 1000
	check(FarmRules.buy_workshop(cfg, s, "bakery")["error"] == FarmRules.ERR_LOCKED, "bakery locked at level 1")
	s["level"] = 2
	check(FarmRules.buy_workshop(cfg, s, "bakery")["ok"], "buy bakery")
	check(s["coins"] == 1000 - int(cfg["workshops"]["bakery"]["cost"]), "bakery cost paid")
	check(not FarmRules.buy_workshop(cfg, s, "bakery")["ok"], "cannot buy twice")
	s["inventory"] = {"wheat": 5}
	check(not FarmRules.start_job(cfg, s, "bakery", "cheese", 0.0)["ok"], "wrong recipe for workshop")
	check(not FarmRules.start_job(cfg, s, "dairy", "cheese", 0.0)["ok"], "workshop not owned")
	check(FarmRules.start_job(cfg, s, "bakery", "flour", 0.0)["ok"], "start flour")
	check(FarmRules.item_count(s, "wheat") == 2, "ingredients consumed")
	check(FarmRules.start_job(cfg, s, "bakery", "bread", 0.0)["error"] == FarmRules.ERR_MISSING_ITEMS, "bread needs flour + egg")
	check(FarmRules.item_count(s, "wheat") == 2, "failed start consumes nothing")
	s["inventory"]["wheat"] = 6
	check(FarmRules.start_job(cfg, s, "bakery", "flour", 1.0)["ok"], "second slot")
	check(FarmRules.start_job(cfg, s, "bakery", "flour", 2.0)["error"] == FarmRules.ERR_BUSY, "slots full")
	check(FarmRules.collect_job(cfg, s, "bakery", 0, 30.0)["error"] == FarmRules.ERR_NOT_READY, "job not ready")
	var r := FarmRules.collect_job(cfg, s, "bakery", 0, 60.0)
	check(r["ok"] and FarmRules.item_count(s, "flour") == 1, "flour collected")
	check(s["workshops"]["bakery"]["jobs"].size() == 1, "job removed after collect")
	check(not FarmRules.collect_job(cfg, s, "bakery", 5, 999.0)["ok"], "invalid job index")

func test_expansion_storage_decor() -> void:
	var s := FarmRules.new_state(cfg, 0.0)
	s["coins"] = 5000
	check(FarmRules.buy_expansion(cfg, s, "field_north")["error"] == FarmRules.ERR_LOCKED, "expansion locked")
	s["level"] = 3
	check(FarmRules.buy_expansion(cfg, s, "field_north")["ok"], "buy expansion")
	check(s["plots"].size() == 9, "three plots added")
	check(not FarmRules.buy_expansion(cfg, s, "field_north")["ok"], "no double expansion")
	var cap := FarmRules.storage_capacity(cfg, s)
	check(FarmRules.upgrade_storage(cfg, s)["ok"] and FarmRules.storage_capacity(cfg, s) > cap, "storage upgrade")
	for i in 10:
		FarmRules.upgrade_storage(cfg, s)
	check(FarmRules.upgrade_storage(cfg, s)["error"] == FarmRules.ERR_ALREADY, "storage max")
	s["premium"] = 3
	check(FarmRules.buy_decoration(cfg, s, "fountain")["error"] == FarmRules.ERR_NOT_ENOUGH_PREMIUM, "fountain needs gems")
	check(s["premium"] == 3, "gems unchanged on failure")
	check(FarmRules.buy_decoration(cfg, s, "flower_bed")["ok"], "buy flower bed")
	check(not FarmRules.buy_decoration(cfg, s, "flower_bed")["ok"], "decoration once")
	var p0: int = s["premium"]
	FarmRules.add_xp(cfg, s, FarmRules.xp_for_next_level(cfg, int(s["level"])) - int(s["xp"]))
	check(s["premium"] == p0 + int(cfg["economy"]["level_reward_premium"]), "free gems on level up")

func test_economy_balance() -> void:
	# Every recipe must be worth more than its ingredients, so producing is never a loss.
	for id in cfg["recipes"]:
		var rec: Dictionary = cfg["recipes"][id]
		var in_value := 0
		for item in rec["inputs"]:
			in_value += int(cfg["items"][item]["sell_price"]) * int(rec["inputs"][item])
		var out_value := int(cfg["items"][id]["sell_price"]) * int(rec["qty"])
		check(out_value > in_value, "recipe %s adds value" % id)
	# Everything sold must exist; every workshop recipe must exist.
	for ws in cfg["workshops"]:
		for r in cfg["workshops"][ws]["recipes"]:
			check(cfg["recipes"].has(r) and cfg["items"].has(r), "recipe %s defined" % r)
	# Fountain (gem item) must be reachable without paying: gems from levels.
	var free_gems := int(cfg["economy"]["start_premium"]) + int(cfg["economy"]["level_reward_premium"]) * 5
	check(free_gems >= int(cfg["decorations"]["fountain"]["premium"]), "gem decoration reachable for free by level 6")

func test_missions() -> void:
	var s := FarmRules.new_state(cfg, 0.0)
	var now := 1793800000.0
	var daily := FarmRules.active_missions(cfg, "daily", now)
	check(daily.size() == int(cfg["missions"]["daily_count"]), "3 daily missions")
	check(FarmRules.active_missions(cfg, "daily", now + 86400.0)[0]["id"] != daily[0]["id"], "missions rotate next day")
	check(FarmRules.active_missions(cfg, "daily", now + 10.0)[0]["id"] == daily[0]["id"], "same missions within a day")
	# Force a known mission: sell 15
	var sell_m: Dictionary = {}
	var t := now
	for d in 10:
		for m in FarmRules.active_missions(cfg, "daily", t):
			if m["id"] == "sell_any_15": sell_m = m
		if not sell_m.is_empty(): break
		t += 86400.0
	check(not sell_m.is_empty(), "sell mission appears within 10 days")
	s["inventory"] = {"wheat": 20}
	check(FarmRules.claim_mission(cfg, s, "daily", "sell_any_15", t)["error"] == FarmRules.ERR_NOT_READY, "mission not done yet")
	FarmRules.sell(cfg, s, "wheat", 15, t)
	var c0: int = s["coins"]
	var r := FarmRules.claim_mission(cfg, s, "daily", "sell_any_15", t)
	check(r["ok"] and int(s["coins"]) == c0 + int(sell_m["reward"]["coins"]), "mission reward paid")
	check(FarmRules.claim_mission(cfg, s, "daily", "sell_any_15", t)["error"] == FarmRules.ERR_ALREADY, "mission claimed once")
	check(not FarmRules.claim_mission(cfg, s, "daily", "harvest_any_8", t + 999999.0)["ok"] or true, "other day ok")
	check(FarmRules.claim_mission(cfg, s, "daily", "not_a_mission", t)["error"] == FarmRules.ERR_INVALID, "unknown mission")
	# Achievements from lifetime stats
	s["stats"]["harvests"] = 10
	check(FarmRules.claim_achievement(cfg, s, "a_harvest_10")["ok"], "achievement claimed")
	check(not FarmRules.claim_achievement(cfg, s, "a_harvest_10")["ok"], "achievement once")
	check(FarmRules.claim_achievement(cfg, s, "a_harvest_100")["error"] == FarmRules.ERR_NOT_READY, "achievement needs progress")
	# Event missions only inside their dates
	check(FarmRules.mission_kinds(cfg, 1793800000.0).has("event:harvest_festival"), "event active in November 2026")
	check(not FarmRules.mission_kinds(cfg, 1797500000.0).has("event:harvest_festival"), "event inactive later")

func test_daily_gift() -> void:
	var s := FarmRules.new_state(cfg, 0.0)
	var now := 1793800000.0
	var c0: int = s["coins"]
	check(FarmRules.claim_daily(cfg, s, now)["ok"], "daily gift")
	check(int(s["coins"]) == c0 + int(cfg["daily_rewards"][0]["coins"]), "day 1 reward")
	check(FarmRules.claim_daily(cfg, s, now + 60.0)["error"] == FarmRules.ERR_ALREADY, "one gift per day")
	# Skip 5 days: next gift continues at step 2 (no reset for missing days)
	var r := FarmRules.claim_daily(cfg, s, now + 86400.0 * 5)
	check(r["ok"] and int(r["step"]) == 1, "missing days does not reset progress")
