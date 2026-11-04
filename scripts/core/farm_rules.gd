extends RefCounted
## Pure game rules. No nodes, no rendering, no clock: everything comes in as arguments.
## Every function validates first and mutates `state` only if the action is allowed.
## Returns {"ok": bool, "error": String, ...extra}.
## This exact logic is mirrored server-side in Phase 4 (Supabase SQL functions).

const ERR_INVALID := "invalid"
const ERR_NOT_ENOUGH_COINS := "not_enough_coins"
const ERR_LOCKED := "locked"
const ERR_BUSY := "busy"
const ERR_NOT_READY := "not_ready"
const ERR_STORAGE_FULL := "storage_full"
const ERR_MISSING_ITEMS := "missing_items"
const ERR_ALREADY := "already_done"
const ERR_NOT_ENOUGH_PREMIUM := "not_enough_premium"

# ---------------------------------------------------------------- state

static func new_state(cfg: Dictionary, now: float) -> Dictionary:
	var eco: Dictionary = cfg.get("economy", {})
	var care: Dictionary = cfg.get("animal_care", {})
	var plots: Array = []
	for i in int(eco.get("starting_plots", 6)):
		plots.append(_empty_plot())
	var animals: Array = []
	for type in cfg.get("animals", {}).keys():
		if cfg["animals"][type].get("special", false):
			continue   # special animals are bought, not given
		animals.append({"type": type, "status": "hungry", "started_at": 0.0,
			"hungry_since": now, "happiness": int(care.get("start_happiness", 70))})
	var inv: Dictionary = {}
	var start_inv: Dictionary = eco.get("start_inventory", {})
	for k in start_inv:
		inv[k] = int(start_inv[k])
	return {
		"version": 1,
		"display_name": "Farmer",
		"coins": int(eco.get("start_coins", 100)),
		"premium": int(eco.get("start_premium", 0)),
		"xp": 0,
		"level": 1,
		"inventory": inv,
		"plots": plots,
		"animals": animals,
		"stats": {"harvests": 0, "collected": 0, "sold": 0, "produced": 0},
		"storage_level": 0,
		"workshops": {},
		"expansions": [],
		"decorations": [],
		"missions": {},
		"daily_reward": {"last_day": -1, "step": 0},
		"achievements": [],
		"created_at": now,
		"last_seen": now,
	}

static func _empty_plot() -> Dictionary:
	return {"crop": "", "planted_at": 0.0, "watered": false}

static func _ok(extra: Dictionary = {}) -> Dictionary:
	var r := {"ok": true, "error": ""}
	r.merge(extra)
	return r

static func _err(code: String) -> Dictionary:
	return {"ok": false, "error": code}

# ---------------------------------------------------------------- inventory

static func item_count(state: Dictionary, item: String) -> int:
	return int(state.get("inventory", {}).get(item, 0))

static func inventory_total(state: Dictionary) -> int:
	var total := 0
	for k in state.get("inventory", {}):
		total += int(state["inventory"][k])
	return total

static func storage_capacity(cfg: Dictionary, state: Dictionary) -> int:
	var levels: Array = cfg.get("economy", {}).get("storage_levels", [{"capacity": 50}])
	var i := clampi(int(state.get("storage_level", 0)), 0, levels.size() - 1)
	return int(levels[i]["capacity"])

static func can_store(cfg: Dictionary, state: Dictionary, qty: int) -> bool:
	return inventory_total(state) + qty <= storage_capacity(cfg, state)

static func add_item(state: Dictionary, item: String, qty: int) -> void:
	if qty <= 0:
		return
	var inv: Dictionary = state["inventory"]
	inv[item] = int(inv.get(item, 0)) + qty

static func remove_item(state: Dictionary, item: String, qty: int) -> bool:
	if qty <= 0 or item_count(state, item) < qty:
		return false
	var inv: Dictionary = state["inventory"]
	inv[item] = int(inv[item]) - qty
	if inv[item] == 0:
		inv.erase(item)
	return true

# ---------------------------------------------------------------- xp & levels

static func xp_for_next_level(cfg: Dictionary, level: int) -> int:
	var table: Array = cfg.get("economy", {}).get("level_xp", [0])
	if level < table.size():
		return int(table[level])
	# Past the table: grow linearly from the last step so the game never "ends".
	var last := int(table[table.size() - 1])
	var step := last - int(table[table.size() - 2]) if table.size() > 1 else 100
	return last + step * (level - table.size() + 1)

static func add_xp(cfg: Dictionary, state: Dictionary, amount: int) -> int:
	## Adds XP, applies level-ups and their coin reward. Returns number of levels gained.
	if amount <= 0:
		return 0
	state["xp"] = int(state["xp"]) + amount
	var gained := 0
	while int(state["xp"]) >= xp_for_next_level(cfg, int(state["level"])):
		state["level"] = int(state["level"]) + 1
		state["coins"] = int(state["coins"]) + int(cfg.get("economy", {}).get("level_reward_coins", 0))
		state["premium"] = int(state["premium"]) + int(cfg.get("economy", {}).get("level_reward_premium", 0))
		gained += 1
	return gained

# ---------------------------------------------------------------- crops

static func crop_grow_time(cfg: Dictionary, plot: Dictionary) -> float:
	var crop: Dictionary = cfg.get("crops", {}).get(plot.get("crop", ""), {})
	var t := float(crop.get("grow_seconds", 60))
	if plot.get("watered", false):
		t *= 1.0 - float(cfg.get("economy", {}).get("water_speedup", 0.0))
	return t

static func plot_status(cfg: Dictionary, plot: Dictionary, now: float) -> String:
	if plot.get("crop", "") == "":
		return "empty"
	if now - float(plot["planted_at"]) >= crop_grow_time(cfg, plot):
		return "ready"
	return "growing"

static func plot_progress(cfg: Dictionary, plot: Dictionary, now: float) -> float:
	if plot.get("crop", "") == "":
		return 0.0
	return clampf((now - float(plot["planted_at"])) / crop_grow_time(cfg, plot), 0.0, 1.0)

static func plot_remaining(cfg: Dictionary, plot: Dictionary, now: float) -> float:
	return maxf(0.0, float(plot["planted_at"]) + crop_grow_time(cfg, plot) - now)

static func _valid_plot(state: Dictionary, idx: int) -> bool:
	return idx >= 0 and idx < state.get("plots", []).size()

static func plant(cfg: Dictionary, state: Dictionary, idx: int, crop_id: String, now: float) -> Dictionary:
	if not _valid_plot(state, idx):
		return _err(ERR_INVALID)
	var crop: Dictionary = cfg.get("crops", {}).get(crop_id, {})
	if crop.is_empty():
		return _err(ERR_INVALID)
	var plot: Dictionary = state["plots"][idx]
	if plot["crop"] != "":
		return _err(ERR_BUSY)
	if int(state["level"]) < int(crop.get("unlock_level", 1)):
		return _err(ERR_LOCKED)
	var price := int(crop["seed_price"])
	if int(state["coins"]) < price:
		return _err(ERR_NOT_ENOUGH_COINS)
	state["coins"] = int(state["coins"]) - price
	plot["crop"] = crop_id
	plot["planted_at"] = now
	plot["watered"] = false
	track(cfg, state, "plant", crop_id, 1, now)
	return _ok({"cost": price})

static func water(cfg: Dictionary, state: Dictionary, idx: int, now: float) -> Dictionary:
	if not _valid_plot(state, idx):
		return _err(ERR_INVALID)
	var plot: Dictionary = state["plots"][idx]
	if plot_status(cfg, plot, now) != "growing":
		return _err(ERR_NOT_READY)
	if plot["watered"]:
		return _err(ERR_ALREADY)
	plot["watered"] = true
	return _ok()

static func harvest(cfg: Dictionary, state: Dictionary, idx: int, now: float) -> Dictionary:
	if not _valid_plot(state, idx):
		return _err(ERR_INVALID)
	var plot: Dictionary = state["plots"][idx]
	if plot_status(cfg, plot, now) != "ready":
		return _err(ERR_NOT_READY)
	var crop_id: String = plot["crop"]
	var crop: Dictionary = cfg["crops"][crop_id]
	var qty := int(crop.get("yield", 1))
	if not can_store(cfg, state, qty):
		return _err(ERR_STORAGE_FULL)
	add_item(state, crop_id, qty)
	state["plots"][idx] = _empty_plot()
	state["stats"]["harvests"] = int(state["stats"].get("harvests", 0)) + 1
	track(cfg, state, "harvest", crop_id, qty, now)
	var levels := add_xp(cfg, state, int(crop.get("xp", 1)))
	return _ok({"item": crop_id, "qty": qty, "xp": int(crop.get("xp", 1)), "levels": levels})

# ---------------------------------------------------------------- animals

static func animal_status(cfg: Dictionary, animal: Dictionary, now: float) -> String:
	if animal.get("status", "hungry") == "hungry":
		return "hungry"
	var def: Dictionary = cfg.get("animals", {}).get(animal["type"], {})
	if now - float(animal["started_at"]) >= float(def.get("produce_seconds", 60)):
		return "ready"
	return "producing"

static func animal_remaining(cfg: Dictionary, animal: Dictionary, now: float) -> float:
	var def: Dictionary = cfg.get("animals", {}).get(animal["type"], {})
	return maxf(0.0, float(animal["started_at"]) + float(def.get("produce_seconds", 60)) - now)

static func _valid_animal(state: Dictionary, idx: int) -> bool:
	return idx >= 0 and idx < state.get("animals", []).size()

static func feed(cfg: Dictionary, state: Dictionary, idx: int, now: float) -> Dictionary:
	if not _valid_animal(state, idx):
		return _err(ERR_INVALID)
	var animal: Dictionary = state["animals"][idx]
	if animal_status(cfg, animal, now) != "hungry":
		return _err(ERR_BUSY)
	var def: Dictionary = cfg["animals"][animal["type"]]
	if int(state["level"]) < int(def.get("unlock_level", 1)):
		return _err(ERR_LOCKED)
	var food: String = def["feed_item"]
	var qty := int(def.get("feed_qty", 1))
	if not remove_item(state, food, qty):
		return _err(ERR_MISSING_ITEMS)
	# Gentle care model: happiness drops slowly while hungry but never below a floor,
	# so a child who misses a day is not punished.
	var care: Dictionary = cfg.get("animal_care", {})
	var hours := maxf(0.0, now - float(animal.get("hungry_since", now))) / 3600.0
	var h := float(animal.get("happiness", 70)) - hours * float(care.get("decay_per_hour_hungry", 0))
	h = maxf(h, float(care.get("min_happiness", 0))) + float(care.get("feed_bonus", 10))
	animal["happiness"] = int(clampf(h, 0.0, 100.0))
	animal["status"] = "producing"
	animal["started_at"] = now
	track(cfg, state, "feed", animal["type"], 1, now)
	return _ok({"item": food, "qty": qty})

static func collect(cfg: Dictionary, state: Dictionary, idx: int, now: float) -> Dictionary:
	if not _valid_animal(state, idx):
		return _err(ERR_INVALID)
	var animal: Dictionary = state["animals"][idx]
	if animal_status(cfg, animal, now) != "ready":
		return _err(ERR_NOT_READY)
	var def: Dictionary = cfg["animals"][animal["type"]]
	var care: Dictionary = cfg.get("animal_care", {})
	var qty := int(def.get("product_qty", 1))
	if int(animal.get("happiness", 0)) >= int(care.get("bonus_threshold", 101)):
		qty += int(care.get("bonus_qty", 0))
	if not can_store(cfg, state, qty):
		return _err(ERR_STORAGE_FULL)
	add_item(state, def["product"], qty)
	animal["status"] = "hungry"
	animal["hungry_since"] = now
	state["stats"]["collected"] = int(state["stats"].get("collected", 0)) + 1
	track(cfg, state, "collect", def["product"], qty, now)
	var levels := add_xp(cfg, state, int(def.get("xp", 1)))
	return _ok({"item": def["product"], "qty": qty, "xp": int(def.get("xp", 1)), "levels": levels})

# ---------------------------------------------------------------- market

static func sell(cfg: Dictionary, state: Dictionary, item: String, qty: int, now: float = 0.0) -> Dictionary:
	var def: Dictionary = cfg.get("items", {}).get(item, {})
	if def.is_empty() or qty <= 0:
		return _err(ERR_INVALID)
	if not remove_item(state, item, qty):
		return _err(ERR_MISSING_ITEMS)
	var earned := int(def.get("sell_price", 0)) * qty
	state["coins"] = int(state["coins"]) + earned
	state["stats"]["sold"] = int(state["stats"].get("sold", 0)) + qty
	track(cfg, state, "sell", item, qty, now)
	var per := int(cfg.get("economy", {}).get("sell_xp_per_coins", 0))
	var xp := earned / per if per > 0 else 0
	var levels := add_xp(cfg, state, xp)
	return _ok({"coins": earned, "xp": xp, "levels": levels})

# ---------------------------------------------------------------- workshops (production)

static func buy_workshop(cfg: Dictionary, state: Dictionary, id: String) -> Dictionary:
	var def: Dictionary = cfg.get("workshops", {}).get(id, {})
	if def.is_empty():
		return _err(ERR_INVALID)
	if state["workshops"].has(id):
		return _err(ERR_ALREADY)
	if int(state["level"]) < int(def.get("unlock_level", 1)):
		return _err(ERR_LOCKED)
	var cost := int(def.get("cost", 0))
	if int(state["coins"]) < cost:
		return _err(ERR_NOT_ENOUGH_COINS)
	state["coins"] = int(state["coins"]) - cost
	state["workshops"][id] = {"jobs": []}
	return _ok({"cost": cost})

static func job_remaining(cfg: Dictionary, job: Dictionary, now: float) -> float:
	var r: Dictionary = cfg.get("recipes", {}).get(job.get("recipe", ""), {})
	return maxf(0.0, float(job["started_at"]) + float(r.get("seconds", 60)) - now)

static func start_job(cfg: Dictionary, state: Dictionary, ws: String, recipe_id: String, now: float) -> Dictionary:
	var def: Dictionary = cfg.get("workshops", {}).get(ws, {})
	if def.is_empty() or not state["workshops"].has(ws):
		return _err(ERR_INVALID)
	if not recipe_id in def.get("recipes", []):
		return _err(ERR_INVALID)
	var jobs: Array = state["workshops"][ws]["jobs"]
	if jobs.size() >= int(def.get("slots", 1)):
		return _err(ERR_BUSY)
	var recipe: Dictionary = cfg["recipes"][recipe_id]
	var inputs: Dictionary = recipe.get("inputs", {})
	# Check every ingredient first so a failed start never removes anything.
	for item in inputs:
		if item_count(state, item) < int(inputs[item]):
			return _err(ERR_MISSING_ITEMS)
	for item in inputs:
		remove_item(state, item, int(inputs[item]))
	jobs.append({"recipe": recipe_id, "started_at": now})
	return _ok({"recipe": recipe_id})

static func collect_job(cfg: Dictionary, state: Dictionary, ws: String, job_idx: int, now: float) -> Dictionary:
	if not state["workshops"].has(ws):
		return _err(ERR_INVALID)
	var jobs: Array = state["workshops"][ws]["jobs"]
	if job_idx < 0 or job_idx >= jobs.size():
		return _err(ERR_INVALID)
	var job: Dictionary = jobs[job_idx]
	if job_remaining(cfg, job, now) > 0.0:
		return _err(ERR_NOT_READY)
	var recipe: Dictionary = cfg["recipes"][job["recipe"]]
	var qty := int(recipe.get("qty", 1))
	if not can_store(cfg, state, qty):
		return _err(ERR_STORAGE_FULL)
	add_item(state, job["recipe"], qty)
	jobs.remove_at(job_idx)
	state["stats"]["produced"] = int(state["stats"].get("produced", 0)) + qty
	track(cfg, state, "produce", job["recipe"], qty, now)
	var levels := add_xp(cfg, state, int(recipe.get("xp", 1)))
	return _ok({"item": job["recipe"], "qty": qty, "xp": int(recipe.get("xp", 1)), "levels": levels})

# ---------------------------------------------------------------- farm growth: land, storage, decorations

static func buy_expansion(cfg: Dictionary, state: Dictionary, id: String) -> Dictionary:
	var def: Dictionary = {}
	for e in cfg.get("expansions", []):
		if e["id"] == id:
			def = e
	if def.is_empty():
		return _err(ERR_INVALID)
	if id in state["expansions"]:
		return _err(ERR_ALREADY)
	if int(state["level"]) < int(def.get("unlock_level", 1)):
		return _err(ERR_LOCKED)
	var cost := int(def.get("cost", 0))
	if int(state["coins"]) < cost:
		return _err(ERR_NOT_ENOUGH_COINS)
	state["coins"] = int(state["coins"]) - cost
	state["expansions"].append(id)
	for i in int(def.get("plots", 0)):
		state["plots"].append(_empty_plot())
	return _ok({"cost": cost})

static func upgrade_storage(cfg: Dictionary, state: Dictionary) -> Dictionary:
	var levels: Array = cfg.get("economy", {}).get("storage_levels", [])
	var next := int(state.get("storage_level", 0)) + 1
	if next >= levels.size():
		return _err(ERR_ALREADY)
	var cost := int(levels[next]["cost"])
	if int(state["coins"]) < cost:
		return _err(ERR_NOT_ENOUGH_COINS)
	state["coins"] = int(state["coins"]) - cost
	state["storage_level"] = next
	return _ok({"cost": cost, "capacity": int(levels[next]["capacity"])})

static func buy_decoration(cfg: Dictionary, state: Dictionary, id: String) -> Dictionary:
	var def: Dictionary = cfg.get("decorations", {}).get(id, {})
	if def.is_empty():
		return _err(ERR_INVALID)
	if id in state["decorations"]:
		return _err(ERR_ALREADY)
	if int(state["level"]) < int(def.get("unlock_level", 1)):
		return _err(ERR_LOCKED)
	var cost := int(def.get("cost", 0))
	var prem := int(def.get("premium", 0))
	if int(state["coins"]) < cost:
		return _err(ERR_NOT_ENOUGH_COINS)
	if int(state["premium"]) < prem:
		return _err(ERR_NOT_ENOUGH_PREMIUM)
	state["coins"] = int(state["coins"]) - cost
	state["premium"] = int(state["premium"]) - prem
	state["decorations"].append(id)
	return _ok({"cost": cost, "premium": prem})

static func new_animal(cfg: Dictionary, type: String, now: float) -> Dictionary:
	return {"type": type, "status": "hungry", "started_at": 0.0, "hungry_since": now,
		"happiness": int(cfg.get("animal_care", {}).get("start_happiness", 70))}

static func has_animal(state: Dictionary, type: String) -> bool:
	for a in state["animals"]:
		if a["type"] == type:
			return true
	return false

## Special animals can always be earned with free gems (also sold in the store for real money).
static func buy_special_animal(cfg: Dictionary, state: Dictionary, type: String, now: float) -> Dictionary:
	var def: Dictionary = cfg.get("special_animals", {}).get(type, {})
	if def.is_empty():
		return _err(ERR_INVALID)
	if has_animal(state, type):
		return _err(ERR_ALREADY)
	if int(state["level"]) < int(def.get("unlock_level", 1)):
		return _err(ERR_LOCKED)
	var prem := int(def.get("premium", 0))
	if int(state["premium"]) < prem:
		return _err(ERR_NOT_ENOUGH_PREMIUM)
	state["premium"] = int(state["premium"]) - prem
	state["animals"].append(new_animal(cfg, type, now))
	return _ok({"premium": prem})

# ---------------------------------------------------------------- missions, achievements, daily gift
## Periods are UTC days/weeks so client and server pick the same missions.
## Missing a day never resets anything (child-friendly: no streak punishment).

static func day_index(now: float) -> int:
	return floori(now / 86400.0)

static func week_index(now: float) -> int:
	return floori((day_index(now) + 3) / 7.0)  # weeks start on Monday (1970-01-01 was a Thursday)

static func _date(now: float) -> String:
	return Time.get_date_string_from_unix_time(int(now))

static func active_events(cfg: Dictionary, now: float) -> Array:
	var out: Array = []
	var today := _date(now)
	for e in cfg.get("missions", {}).get("events", []):
		if String(e["from"]) <= today and today <= String(e["to"]):
			out.append(e)
	return out

## kind: "daily", "weekly" or "event:<id>"
static func active_missions(cfg: Dictionary, kind: String, now: float) -> Array:
	var m: Dictionary = cfg.get("missions", {})
	if kind.begins_with("event:"):
		for e in active_events(cfg, now):
			if "event:" + String(e["id"]) == kind:
				return e.get("missions", [])
		return []
	var pool: Array = m.get(kind, [])
	if pool.is_empty():
		return []
	var count := mini(int(m.get(kind + "_count", 1)), pool.size())
	var period := day_index(now) if kind == "daily" else week_index(now)
	var out: Array = []
	for k in count:
		out.append(pool[(period * count + k) % pool.size()])
	return out

static func mission_kinds(cfg: Dictionary, now: float) -> Array:
	var kinds: Array = ["daily", "weekly"]
	for e in active_events(cfg, now):
		kinds.append("event:" + String(e["id"]))
	return kinds

static func _period(kind: String, now: float) -> int:
	if kind == "daily":
		return day_index(now)
	if kind == "weekly":
		return week_index(now)
	return 0

static func mission_bucket(state: Dictionary, kind: String, now: float) -> Dictionary:
	var all: Dictionary = state["missions"]
	var period := _period(kind, now)
	var b: Dictionary = all.get(kind, {})
	if int(b.get("period", -1)) != period:
		b = {"period": period, "progress": {}, "claimed": []}
		all[kind] = b
	return b

static func track(cfg: Dictionary, state: Dictionary, event: String, item: String, qty: int, now: float) -> void:
	for kind in mission_kinds(cfg, now):
		var b := mission_bucket(state, kind, now)
		for mis in active_missions(cfg, kind, now):
			if mis["event"] == event and (mis["item"] == "" or mis["item"] == item):
				var id: String = mis["id"]
				b["progress"][id] = mini(int(mis["count"]), int(b["progress"].get(id, 0)) + qty)

static func grant_reward(cfg: Dictionary, state: Dictionary, reward: Dictionary) -> int:
	state["coins"] = int(state["coins"]) + int(reward.get("coins", 0))
	state["premium"] = int(state["premium"]) + int(reward.get("premium", 0))
	var items: Dictionary = reward.get("items", {})
	for it in items:
		add_item(state, it, int(items[it]))   # gifts may exceed storage: never lose a reward
	return add_xp(cfg, state, int(reward.get("xp", 0)))

static func claim_mission(cfg: Dictionary, state: Dictionary, kind: String, mission_id: String, now: float) -> Dictionary:
	var mis: Dictionary = {}
	for m in active_missions(cfg, kind, now):
		if m["id"] == mission_id:
			mis = m
	if mis.is_empty():
		return _err(ERR_INVALID)
	var b := mission_bucket(state, kind, now)
	if mission_id in b["claimed"]:
		return _err(ERR_ALREADY)
	if int(b["progress"].get(mission_id, 0)) < int(mis["count"]):
		return _err(ERR_NOT_READY)
	b["claimed"].append(mission_id)
	var levels := grant_reward(cfg, state, mis["reward"])
	return _ok({"reward": mis["reward"], "levels": levels})

static func claim_achievement(cfg: Dictionary, state: Dictionary, id: String) -> Dictionary:
	var ach: Dictionary = {}
	for a in cfg.get("missions", {}).get("achievements", []):
		if a["id"] == id:
			ach = a
	if ach.is_empty():
		return _err(ERR_INVALID)
	if id in state["achievements"]:
		return _err(ERR_ALREADY)
	if int(state["stats"].get(ach["stat"], 0)) < int(ach["count"]):
		return _err(ERR_NOT_READY)
	state["achievements"].append(id)
	var levels := grant_reward(cfg, state, ach["reward"])
	return _ok({"reward": ach["reward"], "levels": levels})

static func daily_available(state: Dictionary, now: float) -> bool:
	return int(state["daily_reward"].get("last_day", -1)) < day_index(now)

static func claim_daily(cfg: Dictionary, state: Dictionary, now: float) -> Dictionary:
	var table: Array = cfg.get("daily_rewards", [])
	if table.is_empty():
		return _err(ERR_INVALID)
	if not daily_available(state, now):
		return _err(ERR_ALREADY)
	var dr: Dictionary = state["daily_reward"]
	var step := int(dr.get("step", 0))
	var reward: Dictionary = table[step % table.size()]
	dr["step"] = step + 1
	dr["last_day"] = day_index(now)
	var levels := grant_reward(cfg, state, reward)
	return _ok({"reward": reward, "step": step, "levels": levels})

# ---------------------------------------------------------------- persistence helpers

static func migrate(cfg: Dictionary, loaded: Dictionary, now: float) -> Dictionary:
	## Fills any keys missing from an older save so new features do not crash old saves.
	var fresh := new_state(cfg, now)
	for k in fresh:
		if not loaded.has(k):
			loaded[k] = fresh[k]
	# Add newly configured animals that an older save does not have yet.
	var have := {}
	for a in loaded["animals"]:
		have[a["type"]] = true
	for a in fresh["animals"]:
		if not have.has(a["type"]):
			loaded["animals"].append(a)
	return loaded
