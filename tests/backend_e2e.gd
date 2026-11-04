extends Node
## End-to-end test: Godot client <-> (mock) Supabase API <-> real SQL migrations.
## Needs backend/tests/mock_supabase.py running on 127.0.0.1:54321.

var passed := 0
var failed := 0

func check(c: bool, name: String) -> void:
	if c: passed += 1
	else:
		failed += 1
		printerr("FAIL: ", name)

func _ready() -> void:
	SaveManager.delete_save()
	for p in ["user://session.json", "user://pending_actions.json"]:
		if FileAccess.file_exists(p): DirAccess.remove_absolute(ProjectSettings.globalize_path(p))
	Backend.session = {}
	Backend.queue = []
	# 1. Offline: wrong port, play anyway, actions are queued
	Backend.url = "http://127.0.0.1:59999"
	Backend.anon_key = "test-key"
	Backend.connect_to_server()
	await _until(func(): return Backend.status == "error", 10.0)
	check(Backend.status == "error", "offline detected")
	check(GameState.plant(0, "wheat")["ok"], "plant offline")
	check(GameState.sell("wheat", 2)["ok"], "sell offline")
	check(Backend.queue.size() == 2, "2 actions queued offline")
	# 2. Come online: account is created, queue flushed, server state applied
	Backend.url = "http://127.0.0.1:54321"
	Backend.connect_to_server()
	await _until(func(): return Backend.status == "online" and Backend.queue.is_empty(), 15.0)
	check(Backend.status == "online", "online")
	check(Backend.user_id() != "", "anonymous account created")
	check(Backend.queue.is_empty(), "queue flushed")
	await get_tree().create_timer(0.5).timeout
	# Fresh server account: 120 start, then plant (-5) and sell 2 wheat (+6) = 121
	check(int(GameState.state["coins"]) == 121, "server state applied (121 coins), got %s" % GameState.state["coins"])
	check(GameState.state["plots"][0]["crop"] == "wheat", "plot synced")
	check(absf(TimeService.server_offset) < 5.0, "server time offset set")
	# 3. Cheat attempt: edit local coins, then do any action -> server state wins
	GameState.state["coins"] = 999999
	GameState.state["inventory"]["pumpkin"] = 50
	GameState.sell("pumpkin", 50)   # passes locally because of the fake inventory
	await _until(func(): return Backend.queue.is_empty(), 10.0)
	await get_tree().create_timer(0.5).timeout
	check(int(GameState.state["coins"]) == 121, "cheated coins reverted by server, got %s" % GameState.state["coins"])
	check(not GameState.state["inventory"].has("pumpkin"), "fake inventory reverted")
	# 4. Restart with saved session = same account (cloud save restore)
	var uid := Backend.user_id()
	Backend.session.erase("access_token")
	GameState.reset()
	Backend.status = "offline"
	Backend.connect_to_server()
	await _until(func(): return Backend.status == "online", 10.0)
	await get_tree().create_timer(0.3).timeout
	check(Backend.user_id() == uid, "same account after refresh")
	check(int(GameState.state["coins"]) == 121 and GameState.state["plots"][0]["crop"] == "wheat", "farm restored from cloud")
	# 5. Parity: the same actions give the same result on client rules and server rules
	var snap := {}
	Backend.server_state_received.connect(func(srv, loc): snap.merge({"srv": srv, "loc": loc}, true))
	GameState.state["inventory"]["wheat"] = GameState.state["inventory"].get("wheat", 0)
	check(GameState.claim_daily()["ok"], "daily gift claimed")
	check(not GameState.claim_daily()["ok"], "daily gift only once per day")
	for i in GameState.state["animals"].size():
		GameState.feed(i)
	GameState.sell("carrot", 1)
	await _until(func(): return Backend.queue.is_empty() and not snap.is_empty(), 10.0)
	if snap.is_empty():
		check(false, "parity snapshot received")
	else:
		var srv: Dictionary = snap["srv"]
		var loc: Dictionary = snap["loc"]
		for k in ["coins", "premium", "xp", "level", "inventory", "daily_reward", "achievements"]:
			check(JSON.stringify(srv[k]) == JSON.stringify(loc[k]), "parity %s: server %s vs client %s" % [k, srv[k], loc[k]])
		check(JSON.stringify(srv["missions"]["daily"]["progress"]) == JSON.stringify(loc["missions"]["daily"]["progress"]), "parity daily mission progress")
		var ha: Array = []
		for a in srv["animals"]: ha.append([a["type"], a["status"], a["happiness"]])
		var hl: Array = []
		for a in loc["animals"]: hl.append([a["type"], a["status"], a["happiness"]])
		check(JSON.stringify(ha) == JSON.stringify(hl), "parity animals: %s vs %s" % [ha, hl])
	print("E2E passed: %d, failed: %d" % [passed, failed])
	get_tree().quit(1 if failed else 0)

func _until(cond: Callable, timeout: float) -> void:
	var t := 0.0
	while not cond.call() and t < timeout:
		await get_tree().process_frame
		t += get_process_delta_time()
