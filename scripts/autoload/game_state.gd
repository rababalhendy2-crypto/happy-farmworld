extends Node
## Holds the current player's farm state and is the ONLY place that changes it.
## Every change goes through FarmRules. In Phase 4 the same action calls become
## server RPCs and the server's answer replaces `state`.

const FarmRules := preload("res://scripts/core/farm_rules.gd")

signal state_changed(reason: String)
signal action_result(action: String, result: Dictionary)
signal level_up(new_level: int)

var state: Dictionary = {}

func _ready() -> void:
	reset()

func reset() -> void:
	state = FarmRules.new_state(Config.data, TimeService.now())
	state_changed.emit("reset")

func load_state(loaded: Dictionary) -> void:
	state = FarmRules.migrate(Config.data, loaded, TimeService.now())
	TimeService.set_last_seen(float(state.get("last_seen", 0.0)))
	state_changed.emit("load")

func apply_server_state(server: Dictionary) -> void:
	## The server's answer is authoritative and replaces the local state.
	var keep_name: String = server.get("display_name", "")
	state = FarmRules.migrate(Config.data, server.duplicate(true), TimeService.now())
	if keep_name != "":
		state["display_name"] = keep_name
	state_changed.emit("sync")

func _apply(action: String, result: Dictionary, args: Dictionary = {}, idx: int = -1) -> Dictionary:
	result["idx"] = idx
	action_result.emit(action, result)
	if result.get("ok", false):
		Backend.enqueue(action, args)
		if int(result.get("levels", 0)) > 0:
			level_up.emit(int(state["level"]))
		state_changed.emit(action)
	return result

func _now() -> float:
	return TimeService.now()

func plant(idx: int, crop_id: String) -> Dictionary:
	return _apply("plant", FarmRules.plant(Config.data, state, idx, crop_id, _now()), {"idx": idx, "crop": crop_id}, idx)

func water(idx: int) -> Dictionary:
	return _apply("water", FarmRules.water(Config.data, state, idx, _now()), {"idx": idx}, idx)

func harvest(idx: int) -> Dictionary:
	return _apply("harvest", FarmRules.harvest(Config.data, state, idx, _now()), {"idx": idx}, idx)

func feed(idx: int) -> Dictionary:
	return _apply("feed", FarmRules.feed(Config.data, state, idx, _now()), {"idx": idx}, idx)

func collect(idx: int) -> Dictionary:
	return _apply("collect", FarmRules.collect(Config.data, state, idx, _now()), {"idx": idx}, idx)

func sell(item: String, qty: int) -> Dictionary:
	return _apply("sell", FarmRules.sell(Config.data, state, item, qty, _now()), {"item": item, "qty": qty})

func buy_workshop(id: String) -> Dictionary:
	return _apply("buy_workshop", FarmRules.buy_workshop(Config.data, state, id), {"id": id})

func start_job(ws: String, recipe: String) -> Dictionary:
	return _apply("start_job", FarmRules.start_job(Config.data, state, ws, recipe, _now()), {"ws": ws, "recipe": recipe})

func collect_job(ws: String, job_idx: int) -> Dictionary:
	var r := FarmRules.collect_job(Config.data, state, ws, job_idx, _now())
	r["ws"] = ws
	return _apply("collect_job", r, {"ws": ws, "job": job_idx})

func buy_expansion(id: String) -> Dictionary:
	return _apply("buy_expansion", FarmRules.buy_expansion(Config.data, state, id), {"id": id})

func upgrade_storage() -> Dictionary:
	return _apply("upgrade_storage", FarmRules.upgrade_storage(Config.data, state), {})

func buy_decoration(id: String) -> Dictionary:
	return _apply("buy_decoration", FarmRules.buy_decoration(Config.data, state, id), {"id": id})

func buy_special_animal(type: String) -> Dictionary:
	return _apply("buy_special_animal", FarmRules.buy_special_animal(Config.data, state, type, _now()), {"id": type})

func claim_mission(kind: String, id: String) -> Dictionary:
	return _apply("claim_mission", FarmRules.claim_mission(Config.data, state, kind, id, _now()), {"kind": kind, "id": id})

func claim_achievement(id: String) -> Dictionary:
	return _apply("claim_achievement", FarmRules.claim_achievement(Config.data, state, id), {"id": id})

func claim_daily() -> Dictionary:
	return _apply("claim_daily", FarmRules.claim_daily(Config.data, state, _now()), {})

func set_display_name(n: String) -> void:
	state["display_name"] = n
	state_changed.emit("name")
	if Backend.enabled() and Backend.status == "online":
		Backend.rpc_call("hf_set_display_name", {"p_name": n})

func daily_available() -> bool:
	return FarmRules.daily_available(state, _now())

func job_remaining(job: Dictionary) -> float:
	return FarmRules.job_remaining(Config.data, job, TimeService.now())

# Read helpers for UI / world
func plot(idx: int) -> Dictionary: return state["plots"][idx]
func plot_status(idx: int) -> String: return FarmRules.plot_status(Config.data, plot(idx), TimeService.now())
func plot_progress(idx: int) -> float: return FarmRules.plot_progress(Config.data, plot(idx), TimeService.now())
func plot_remaining(idx: int) -> float: return FarmRules.plot_remaining(Config.data, plot(idx), TimeService.now())
func animal(idx: int) -> Dictionary: return state["animals"][idx]
func animal_status(idx: int) -> String: return FarmRules.animal_status(Config.data, animal(idx), TimeService.now())
func animal_remaining(idx: int) -> float: return FarmRules.animal_remaining(Config.data, animal(idx), TimeService.now())
func xp_next() -> int: return FarmRules.xp_for_next_level(Config.data, int(state["level"]))
func xp_prev() -> int: return FarmRules.xp_for_next_level(Config.data, int(state["level"]) - 1)
func inventory_total() -> int: return FarmRules.inventory_total(state)
func capacity() -> int: return FarmRules.storage_capacity(Config.data, state)
