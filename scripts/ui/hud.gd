extends CanvasLayer
## Main farm HUD: top status bar, bottom buttons, context panel (plot / animal),
## inventory, market, settings, toasts and level-up popup.
## Every visible button performs its action; nothing is a placeholder.

const UT := preload("res://scripts/ui/ui_theme.gd")
const FarmRules := preload("res://scripts/core/farm_rules.gd")
const ITEM_COLORS := {"egg": "#fff4dc", "milk": "#e8f4ff", "wool": "#f4f1ea", "fluff": "#ffd6e7",
	"truffle": "#6b4a32", "goat_milk": "#fffbea", "flour": "#f6efe0", "bread": "#d9954a", "pumpkin_pie": "#e98a3a",
	"cheese": "#ffd85a", "goat_cheese": "#fff1b8", "carrot_juice": "#ff9a3c", "tomato_juice": "#e5452e",
	"strawberry_jam": "#c2185b"}

var world: Node3D
var root: Control
var _coins: Label
var _premium: Label
var _level: Label
var _xp: ProgressBar
var _storage: Label
var _toast: Label
var _toast_tw: Tween
var _context: PanelContainer
var _context_body: VBoxContainer
var _ctx_kind := ""
var _ctx_idx := -1
var _ctx_status := ""
var _ctx_timer: Label
var _ctx_bar: ProgressBar
var _modal: Control
var _blockers: Array[Control] = []
var _refresh_t := 0.0
var _missions_btn: Button
const PFL := preload("res://scripts/ui/parental_gate.gd")

func _connect_store_signals() -> void:
	Billing.purchase_finished.connect(func(_pid, ok, msg):
		if ok: toast(tr("PURCHASE_DONE"), true)
		elif msg != "": toast(tr("PURCHASE_FAILED")))
	Ads.ad_finished.connect(func(_k, rewarded):
		if rewarded: toast(tr("AD_THANKS"), true))

func _ready() -> void:
	root = Control.new()
	root.set_anchors_preset(Control.PRESET_FULL_RECT)
	root.mouse_filter = Control.MOUSE_FILTER_IGNORE
	root.theme = UT.build()
	add_child(root)
	_build_top_bar()
	_build_bottom_bar()
	_build_context()
	_build_toast()
	GameState.state_changed.connect(func(_r): _refresh_bar())
	GameState.action_result.connect(_on_action_result)
	GameState.level_up.connect(_on_level_up)
	Localization.language_changed.connect(func(_l): _rebuild_all())
	_connect_store_signals()
	_apply_direction()
	_refresh_bar()

# ------------------------------------------------------------------ helpers

func blocks_point(p: Vector2) -> bool:
	if _modal and is_instance_valid(_modal):
		return true
	for c in _blockers:
		if is_instance_valid(c) and c.is_visible_in_tree() and c.get_global_rect().has_point(p):
			return true
	return false

func _apply_direction() -> void:
	root.layout_direction = Control.LAYOUT_DIRECTION_RTL if Localization.is_rtl() else Control.LAYOUT_DIRECTION_LTR

func _rebuild_all() -> void:
	for c in root.get_children():
		c.queue_free()
	_blockers.clear()
	_modal = null
	_context = null
	await get_tree().process_frame
	_build_top_bar()
	_build_bottom_bar()
	_build_context()
	_build_toast()
	_apply_direction()
	_refresh_bar()

func _button(text: String, cb: Callable, min_w := 0, font_size := 24) -> Button:
	var b := Button.new()
	b.text = text
	b.custom_minimum_size = Vector2(min_w, 64)
	b.add_theme_font_size_override("font_size", font_size)
	b.pressed.connect(func():
		Audio.play("click")
		cb.call())
	return b

func _label(text: String, size := 24, color := UT.BROWN) -> Label:
	var l := Label.new()
	l.text = text
	l.add_theme_font_size_override("font_size", size)
	l.add_theme_color_override("font_color", color)
	return l

func item_name(id: String) -> String:
	return tr("ITEM_" + id.to_upper())

func item_color(id: String) -> Color:
	if Config.crops().has(id):
		return Color(Config.crops()[id]["color"])
	return Color(ITEM_COLORS.get(id, "#cccccc"))

func toast(msg: String, good := false) -> void:
	_toast.text = msg
	_toast.add_theme_color_override("font_color", Color("#2f6b1f") if good else Color("#8a2b1f"))
	_toast.get_parent().visible = true
	_toast.get_parent().modulate.a = 1.0
	if _toast_tw:
		_toast_tw.kill()
	_toast_tw = create_tween()
	_toast_tw.tween_interval(1.8)
	_toast_tw.tween_property(_toast.get_parent(), "modulate:a", 0.0, 0.4)
	_toast_tw.tween_callback(func(): _toast.get_parent().visible = false)

# ------------------------------------------------------------------ top / bottom bars

func _pill(icon: Control, label: Label) -> PanelContainer:
	var pc := PanelContainer.new()
	pc.add_theme_stylebox_override("panel", UT.pill_style())
	var hb := HBoxContainer.new()
	hb.add_theme_constant_override("separation", 8)
	pc.add_child(hb)
	var holder := CenterContainer.new()
	holder.custom_minimum_size = Vector2(32, 32)
	holder.add_child(icon)
	hb.add_child(holder)
	hb.add_child(label)
	return pc

func _build_top_bar() -> void:
	var m := MarginContainer.new()
	m.set_anchors_preset(Control.PRESET_TOP_WIDE)
	for side in ["left", "right", "top"]:
		m.add_theme_constant_override("margin_" + side, 16)
	m.mouse_filter = Control.MOUSE_FILTER_IGNORE
	root.add_child(m)
	var hb := HBoxContainer.new()
	hb.mouse_filter = Control.MOUSE_FILTER_IGNORE
	m.add_child(hb)
	# Level + XP
	var lvl_box := PanelContainer.new()
	lvl_box.add_theme_stylebox_override("panel", UT.pill_style())
	var lvl_h := HBoxContainer.new()
	lvl_box.add_child(lvl_h)
	_level = _label("1", 26)
	var star := UT.coin_icon(Color("#7cc6ff"), 34)
	var star_c := CenterContainer.new()
	star_c.add_child(star)
	star.add_child(_level)
	_level.set_anchors_preset(Control.PRESET_FULL_RECT)
	_level.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_level.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	_level.add_theme_font_size_override("font_size", 18)
	star.custom_minimum_size = Vector2(40, 40)
	lvl_h.add_child(star_c)
	_xp = ProgressBar.new()
	_xp.custom_minimum_size = Vector2(150, 26)
	_xp.show_percentage = false
	_xp.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	lvl_h.add_child(_xp)
	hb.add_child(lvl_box)
	_coins = _label("0", 26)
	hb.add_child(_pill(UT.coin_icon(Color("#ffd23f")), _coins))
	_premium = _label("0", 26)
	hb.add_child(_pill(UT.coin_icon(Color("#b07cff"), 28, true), _premium))
	_storage = _label("0/0", 22)
	hb.add_child(_pill(UT.coin_icon(Color("#c08a52"), 26, true), _storage))
	var spacer := Control.new()
	spacer.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	spacer.mouse_filter = Control.MOUSE_FILTER_IGNORE
	hb.add_child(spacer)
	var settings := _button(tr("BTN_SETTINGS"), open_settings, 0, 22)
	hb.add_child(settings)
	_blockers.append(lvl_box)
	lvl_box.gui_input.connect(func(ev):
		# Touch arrives here as an emulated mouse click (emulate_mouse_from_touch), so one branch only.
		if ev is InputEventMouseButton and ev.pressed and ev.button_index == MOUSE_BUTTON_LEFT:
			Audio.play("click")
			open_profile())
	_blockers.append(settings)

func _build_bottom_bar() -> void:
	var m := MarginContainer.new()
	m.set_anchors_preset(Control.PRESET_BOTTOM_WIDE)
	m.grow_vertical = Control.GROW_DIRECTION_BEGIN
	m.add_theme_constant_override("margin_bottom", 16)
	m.mouse_filter = Control.MOUSE_FILTER_IGNORE
	root.add_child(m)
	var hb := HBoxContainer.new()
	hb.alignment = BoxContainer.ALIGNMENT_CENTER
	hb.add_theme_constant_override("separation", 18)
	hb.mouse_filter = Control.MOUSE_FILTER_IGNORE
	m.add_child(hb)
	for spec in [["BTN_SHOP", open_shop], ["BTN_MISSIONS", open_missions], ["BTN_INVENTORY", open_inventory], ["BTN_MARKET", open_market]]:
		var b := _button(tr(spec[0]), spec[1], 180, 26)
		if spec[0] == "BTN_MISSIONS":
			_missions_btn = b
		b.custom_minimum_size.y = 72
		hb.add_child(b)
		_blockers.append(b)

func _refresh_bar() -> void:
	if _coins == null or not is_instance_valid(_coins):
		return
	var s := GameState.state
	_coins.text = str(s["coins"])
	_premium.text = str(s["premium"])
	_level.text = str(s["level"])
	var lo := GameState.xp_prev()
	var hi := GameState.xp_next()
	_xp.min_value = lo
	_xp.max_value = maxi(hi, lo + 1)
	_xp.value = int(s["xp"])
	_storage.text = "%d/%d" % [GameState.inventory_total(), GameState.capacity()]
	if _missions_btn and is_instance_valid(_missions_btn):
		var n := _claimable_count()
		_missions_btn.text = tr("BTN_MISSIONS") + ("  (%d)" % n if n > 0 else "")

func _build_toast() -> void:
	var pc := PanelContainer.new()
	pc.add_theme_stylebox_override("panel", UT.pill_style())
	pc.set_anchors_preset(Control.PRESET_CENTER_TOP)
	pc.grow_horizontal = Control.GROW_DIRECTION_BOTH
	pc.position.y = 96
	pc.mouse_filter = Control.MOUSE_FILTER_IGNORE
	pc.visible = false
	_toast = _label("", 24)
	pc.add_child(_toast)
	root.add_child(pc)

# ------------------------------------------------------------------ context panel

func _build_context() -> void:
	_context = PanelContainer.new()
	_context.custom_minimum_size = Vector2(560, 0)
	_context.visible = false
	root.add_child(_context)
	_context_body = VBoxContainer.new()
	_context.add_child(_context_body)
	_blockers.append(_context)

func close_context() -> void:
	if _context:
		_context.visible = false
	_ctx_kind = ""
	_ctx_idx = -1

func _ctx_begin(title: String) -> void:
	for c in _context_body.get_children():
		c.queue_free()
	_ctx_timer = null
	_ctx_bar = null
	var head := HBoxContainer.new()
	var t := _label(title, 30)
	t.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	head.add_child(t)
	head.add_child(_button("X", close_context, 64))
	_context_body.add_child(head)
	_context.visible = true
	_layout_context.call_deferred()

func _layout_context() -> void:
	## Keeps the context panel centred above the bottom buttons whatever its content size.
	if not _context or not _context.visible:
		return
	_context.reset_size()
	var vp := root.get_viewport_rect().size
	_context.position = Vector2((vp.x - _context.size.x) / 2.0, vp.y - _context.size.y - 104.0)

func open_plot(idx: int) -> void:
	_ctx_kind = "plot"
	_ctx_idx = idx
	_ctx_status = GameState.plot_status(idx)
	var p := GameState.plot(idx)
	match _ctx_status:
		"empty":
			_ctx_begin(tr("PLOT_EMPTY"))
			_context_body.add_child(_label(tr("PICK_SEED"), 20))
			var grid := GridContainer.new()
			grid.columns = 3
			_context_body.add_child(grid)
			var lvl := int(GameState.state["level"])
			for crop_id in Config.crops():
				var c: Dictionary = Config.crops()[crop_id]
				var locked := lvl < int(c["unlock_level"])
				var txt := "%s\n%d  ·  %s" % [item_name(crop_id), int(c["seed_price"]), TimeService.format_duration(float(c["grow_seconds"]))]
				if locked:
					txt = "%s\n%s" % [item_name(crop_id), tr("UNLOCK_AT") % int(c["unlock_level"])]
				var id: String = crop_id
				var b := _button(txt, func(): GameState.plant(idx, id), 170, 20)
				b.custom_minimum_size.y = 84
				b.disabled = locked
				if not locked:
					var sb := UT.box(Color(c["color"]).lightened(0.45), Color(c["color"]).darkened(0.25))
					b.add_theme_stylebox_override("normal", sb)
					b.add_theme_stylebox_override("hover", sb)
				grid.add_child(b)
		"growing":
			_ctx_begin(item_name(p["crop"]))
			_ctx_bar = ProgressBar.new()
			_ctx_bar.custom_minimum_size = Vector2(0, 30)
			_ctx_bar.show_percentage = false
			_ctx_bar.max_value = 1.0
			_ctx_bar.step = 0.001
			_context_body.add_child(_ctx_bar)
			_ctx_timer = _label("", 22)
			_context_body.add_child(_ctx_timer)
			var wb := _button(tr("BTN_WATERED") if p["watered"] else tr("BTN_WATER"), func(): GameState.water(idx), 0, 24)
			wb.disabled = p["watered"]
			_context_body.add_child(wb)
			if Ads.available() and Ads.remaining() > 0:
				_context_body.add_child(_button(tr("BTN_AD_SPEEDUP"), func():
					close_context()
					Ads.watch("speedup", {"idx": idx}), 0, 18))
			_update_ctx_live()
		_:
			close_context()

func open_animal(idx: int) -> void:
	_ctx_kind = "animal"
	_ctx_idx = idx
	_ctx_status = GameState.animal_status(idx)
	var a := GameState.animal(idx)
	var def: Dictionary = Config.animals()[a["type"]]
	_ctx_begin(tr("ANIMAL_" + String(a["type"]).to_upper()))
	var hb := HBoxContainer.new()
	hb.add_child(_label(tr("HAPPINESS"), 20))
	var hap := ProgressBar.new()
	hap.max_value = 100
	hap.value = int(a["happiness"])
	hap.show_percentage = false
	hap.custom_minimum_size = Vector2(220, 24)
	hap.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	hap.add_theme_stylebox_override("fill", UT.box(Color("#ff7aa2"), Color("#d94f7a"), 10, 2, 0))
	hb.add_child(hap)
	_context_body.add_child(hb)
	_context_body.add_child(_label(tr("PRODUCES") % item_name(def["product"]), 20))
	match _ctx_status:
		"hungry":
			var need := int(def["feed_qty"])
			var have := int(GameState.state["inventory"].get(def["feed_item"], 0))
			_context_body.add_child(_label(tr("NEEDS_FOOD") % [need, item_name(def["feed_item"]), have], 20))
			var fb := _button(tr("BTN_FEED"), func(): GameState.feed(idx), 0, 24)
			_context_body.add_child(fb)
		"producing":
			_ctx_bar = ProgressBar.new()
			_ctx_bar.custom_minimum_size = Vector2(0, 30)
			_ctx_bar.show_percentage = false
			_ctx_bar.max_value = 1.0
			_ctx_bar.step = 0.001
			_context_body.add_child(_ctx_bar)
			_ctx_timer = _label("", 22)
			_context_body.add_child(_ctx_timer)
			_update_ctx_live()
		_:
			close_context()

func _update_ctx_live() -> void:
	if _ctx_kind == "plot":
		var st := GameState.plot_status(_ctx_idx)
		if st != _ctx_status:
			if st == "ready":
				close_context()
			else:
				open_plot(_ctx_idx)
			return
		if _ctx_bar:
			_ctx_bar.value = GameState.plot_progress(_ctx_idx)
			_ctx_timer.text = tr("READY_IN") % TimeService.format_duration(GameState.plot_remaining(_ctx_idx))
	elif _ctx_kind == "animal":
		var st := GameState.animal_status(_ctx_idx)
		if st != _ctx_status:
			if st == "ready":
				close_context()
			else:
				open_animal(_ctx_idx)
			return
		if _ctx_bar:
			var def: Dictionary = Config.animals()[GameState.animal(_ctx_idx)["type"]]
			var rem := GameState.animal_remaining(_ctx_idx)
			_ctx_bar.value = 1.0 - rem / float(def["produce_seconds"])
			_ctx_timer.text = tr("READY_IN") % TimeService.format_duration(rem)

func _process(delta: float) -> void:
	_refresh_t += delta
	if _refresh_t > 0.2:
		_refresh_t = 0.0
		if _context and _context.visible:
			_update_ctx_live()
			_layout_context()
		_live_t += 0.2
		if _live_workshop != "" and _live_t >= 1.0:
			_live_t = 0.0
			open_workshop(_live_workshop, false)

# ------------------------------------------------------------------ modal screens

func _open_modal(title: String, animate := true) -> VBoxContainer:
	close_modal()
	close_context()
	_live_workshop = ""
	var dim := ColorRect.new()
	dim.color = Color(0, 0, 0, 0.35)
	dim.set_anchors_preset(Control.PRESET_FULL_RECT)
	dim.mouse_filter = Control.MOUSE_FILTER_STOP
	root.add_child(dim)
	_modal = dim
	var pc := PanelContainer.new()
	pc.set_anchors_preset(Control.PRESET_CENTER)
	pc.grow_horizontal = Control.GROW_DIRECTION_BOTH
	pc.grow_vertical = Control.GROW_DIRECTION_BOTH
	pc.custom_minimum_size = Vector2(760, 520)
	dim.add_child(pc)
	var vb := VBoxContainer.new()
	pc.add_child(vb)
	var head := HBoxContainer.new()
	var t := _label(title, 34)
	t.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	head.add_child(t)
	head.add_child(_button("X", close_modal, 64))
	vb.add_child(head)
	if not animate:
		return vb
	pc.scale = Vector2(0.9, 0.9)
	pc.pivot_offset = Vector2(380, 260)
	create_tween().tween_property(pc, "scale", Vector2.ONE, 0.15).set_trans(Tween.TRANS_BACK).set_ease(Tween.EASE_OUT)
	return vb

func close_modal() -> void:
	_live_workshop = ""
	if _modal and is_instance_valid(_modal):
		_modal.queue_free()
	_modal = null

func _scroll(vb: VBoxContainer) -> VBoxContainer:
	var sc := ScrollContainer.new()
	sc.size_flags_vertical = Control.SIZE_EXPAND_FILL
	sc.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	vb.add_child(sc)
	var inner := VBoxContainer.new()
	inner.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	sc.add_child(inner)
	return inner

func _item_row(id: String, qty: int) -> HBoxContainer:
	var row := HBoxContainer.new()
	var ic := CenterContainer.new()
	ic.custom_minimum_size = Vector2(44, 44)
	ic.add_child(UT.coin_icon(item_color(id), 36))
	row.add_child(ic)
	var n := _label(item_name(id), 24)
	n.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_child(n)
	row.add_child(_label("x%d" % qty, 24))
	return row

func open_inventory() -> void:
	var vb := _open_modal(tr("TITLE_INVENTORY"))
	vb.add_child(_label(tr("STORAGE_USED") % [GameState.inventory_total(), GameState.capacity()], 22))
	var list := _scroll(vb)
	var inv: Dictionary = GameState.state["inventory"]
	if inv.is_empty():
		list.add_child(_label(tr("INVENTORY_EMPTY"), 22))
	for cat in ["crops", "animal_products", "processed"]:
		var ids := []
		for id in inv:
			if Config.items().get(id, {}).get("category", "") == cat:
				ids.append(id)
		if ids.is_empty():
			continue
		list.add_child(_label(tr("CAT_" + cat.to_upper()), 26, Color("#a0612a")))
		for id in ids:
			list.add_child(_item_row(id, int(inv[id])))

func open_market() -> void:
	var vb := _open_modal(tr("TITLE_MARKET"))
	var list := _scroll(vb)
	var inv: Dictionary = GameState.state["inventory"]
	if inv.is_empty():
		list.add_child(_label(tr("MARKET_EMPTY"), 22))
	for id in inv.keys():
		var qty := int(inv[id])
		var price := int(Config.items().get(id, {}).get("sell_price", 0))
		var row := _item_row(id, qty)
		row.add_child(_label(tr("EACH") % price, 20))
		var item_id: String = id
		row.add_child(_button(tr("BTN_SELL_ONE"), func():
			GameState.sell(item_id, 1)
			open_market(), 120, 20))
		row.add_child(_button(tr("BTN_SELL_ALL") % (price * qty), func():
			GameState.sell(item_id, int(GameState.state["inventory"].get(item_id, 0)))
			open_market(), 170, 20))
		list.add_child(row)

func _cost_text(coins: int, premium: int) -> String:
	if premium > 0:
		return tr("COST_PREMIUM") % premium
	return tr("COST_COINS") % coins

func _shop_row(list: VBoxContainer, title: String, desc: String, cost: int, premium: int, unlock: int, owned: bool, buy: Callable) -> void:
	var row := HBoxContainer.new()
	var info := VBoxContainer.new()
	info.add_theme_constant_override("separation", 0)
	info.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	info.add_child(_label(title, 24))
	if desc != "":
		info.add_child(_label(desc, 17, Color("#8a6a4a")))
	row.add_child(info)
	var b: Button
	if owned:
		b = _button(tr("OWNED"), func(): pass, 200, 20)
		b.disabled = true
	elif int(GameState.state["level"]) < unlock:
		b = _button(tr("UNLOCK_AT") % unlock, func(): pass, 200, 20)
		b.disabled = true
	else:
		b = _button(_cost_text(cost, premium), buy, 200, 20)
	row.add_child(b)
	list.add_child(row)

func open_shop(tab: String = "buildings") -> void:
	var vb := _open_modal(tr("TITLE_SHOP"))
	var tabs := HBoxContainer.new()
	for spec in [["buildings", "TAB_BUILDINGS"], ["land", "TAB_LAND"], ["decor", "TAB_DECOR"], ["animals", "TAB_ANIMALS"], ["store", "TAB_STORE"]]:
		var t: String = spec[0]
		var b := _button(tr(spec[1]), func(): open_shop(t), 0, 20)
		b.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		b.disabled = t == tab
		tabs.add_child(b)
	vb.add_child(tabs)
	var list := _scroll(vb)
	var st := GameState.state
	match tab:
		"buildings":
			for id in Config.data.get("workshops", {}):
				var d: Dictionary = Config.data["workshops"][id]
				var outs := []
				for r in d["recipes"]:
					outs.append(item_name(r))
				var ws: String = id
				_shop_row(list, tr("WS_" + id.to_upper()), tr("MAKES") % ", ".join(outs), int(d["cost"]), 0,
					int(d["unlock_level"]), st["workshops"].has(id), func():
						if GameState.buy_workshop(ws)["ok"]: open_shop("buildings"))
		"land":
			for e in Config.data.get("expansions", []):
				var eid: String = e["id"]
				_shop_row(list, tr("EXP_" + eid.to_upper()), tr("EXP_DESC") % int(e["plots"]), int(e["cost"]), 0,
					int(e["unlock_level"]), eid in st["expansions"], func():
						if GameState.buy_expansion(eid)["ok"]: open_shop("land"))
			var levels: Array = Config.economy().get("storage_levels", [])
			var nxt := int(st.get("storage_level", 0)) + 1
			if nxt < levels.size():
				_shop_row(list, tr("STORAGE_UPGRADE"), tr("STORAGE_UPGRADE_DESC") % [GameState.capacity(), int(levels[nxt]["capacity"])],
					int(levels[nxt]["cost"]), 0, 1, false, func():
						if GameState.upgrade_storage()["ok"]: open_shop("land"))
			else:
				list.add_child(_label(tr("STORAGE_MAX"), 20))
		"decor":
			for id in Config.data.get("decorations", {}):
				var d: Dictionary = Config.data["decorations"][id]
				var did: String = id
				_shop_row(list, tr("DECO_" + id.to_upper()), "", int(d["cost"]), int(d.get("premium", 0)),
					int(d["unlock_level"]), id in st["decorations"], func():
						if GameState.buy_decoration(did)["ok"]: open_shop("decor"))
			list.add_child(_label(tr("PREMIUM_NOTE"), 17, Color("#8a6a4a")))
		"animals":
			for id in Config.data.get("special_animals", {}):
				var d: Dictionary = Config.data["special_animals"][id]
				var aid: String = id
				_shop_row(list, tr("ANIMAL_" + id.to_upper()), tr("SPECIAL_ANIMAL_DESC") % item_name(Config.animals()[id]["product"]),
					0, int(d["premium"]), int(d.get("unlock_level", 1)), FarmRules.has_animal(st, id), func():
						if GameState.buy_special_animal(aid)["ok"]: open_shop("animals"))
			list.add_child(_label(tr("PREMIUM_NOTE"), 17, Color("#8a6a4a")))
		"store":
			_build_store(list)

func _build_store(list: VBoxContainer) -> void:
	# Free coins: optional rewarded ad. Clearly labelled, never automatic.
	var ad_row := HBoxContainer.new()
	var ad_info := VBoxContainer.new()
	ad_info.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	ad_info.add_child(_label(tr("AD_COINS_TITLE"), 22))
	var ad_ok := Ads.available() and Ads.remaining() > 0
	var note := tr("AD_REMAINING") % [Ads.remaining(), int(Ads.status["limit"])] if Ads.available() else tr("ADS_UNAVAILABLE")
	ad_info.add_child(_label(note, 16, Color("#8a6a4a")))
	ad_row.add_child(ad_info)
	var ad_btn := _button(tr("BTN_WATCH_AD") % int(Ads.status.get("next_coins", 0)) if ad_ok else tr("BTN_WATCH_AD_OFF"), func():
		await Ads.watch("coins")
		open_shop("store"), 220, 18)
	ad_btn.disabled = not ad_ok
	ad_row.add_child(ad_btn)
	list.add_child(ad_row)
	list.add_child(_label(tr("STORE_TITLE"), 24, Color("#a0612a")))
	if not Billing.available():
		list.add_child(_label(tr("BILLING_UNAVAILABLE"), 17, UT.RED))
	var now := TimeService.now()
	var events := []
	for e in FarmRules.active_events(Config.data, now):
		events.append(e["id"])
	for pr in Config.data.get("store", {}).get("products", []):
		if pr.has("event") and not pr["event"] in events:
			continue
		var pid: String = pr["id"]
		var row := HBoxContainer.new()
		var info := VBoxContainer.new()
		info.add_theme_constant_override("separation", 0)
		info.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		info.add_child(_label(tr("PRODUCT_" + pid.to_upper()), 22))
		info.add_child(_label(_grants_text(pr["grants"]), 16, Color("#8a6a4a")))
		row.add_child(info)
		var price: String = Billing.prices.get(pid, pr["price_label"])
		var b := _button(price, func(): PFL.open(self, func(): _confirm_purchase(pid, price)), 160, 20)
		b.disabled = not Billing.available()
		row.add_child(b)
		list.add_child(row)
	var restore := _button(tr("BTN_RESTORE_PURCHASES"), func():
		Billing.restore()
		toast(tr("RESTORE_STARTED"), true), 0, 18)
	restore.disabled = not Billing.available()
	list.add_child(restore)
	list.add_child(_label(tr("STORE_NOTE"), 16, Color("#8a6a4a")))

func _grants_text(g: Dictionary) -> String:
	var parts := []
	if int(g.get("premium", 0)) > 0: parts.append(tr("COST_PREMIUM") % int(g["premium"]))
	if int(g.get("coins", 0)) > 0: parts.append(tr("COST_COINS") % int(g["coins"]))
	for d in g.get("decorations", []): parts.append(tr("DECO_" + String(d).to_upper()))
	for a in g.get("animals", []): parts.append(tr("ANIMAL_" + String(a).to_upper()))
	return " + ".join(parts)

func _confirm_purchase(pid: String, price: String) -> void:
	## Clear confirmation: what you get and the exact price, two equal buttons, no tricks.
	var vb := _open_modal(tr("CONFIRM_PURCHASE"))
	vb.add_child(_label(tr("PRODUCT_" + pid.to_upper()), 28))
	for pr in Config.data["store"]["products"]:
		if pr["id"] == pid:
			vb.add_child(_label(_grants_text(pr["grants"]), 20))
	vb.add_child(_label(tr("PRICE_IS") % price, 24, UT.BROWN))
	var hb := HBoxContainer.new()
	hb.add_child(_button(tr("BTN_CANCEL"), close_modal, 220))
	hb.add_child(_button(tr("BTN_BUY_FOR") % price, func():
		close_modal()
		Billing.buy(pid), 260))
	vb.add_child(hb)

func open_workshop(ws: String, animate := true) -> void:
	var vb := _open_modal(tr("WS_" + ws.to_upper()), animate)
	var def: Dictionary = Config.data["workshops"][ws]
	var jobs: Array = GameState.state["workshops"][ws]["jobs"]
	vb.add_child(_label(tr("SLOTS") % [jobs.size(), int(def["slots"])], 20))
	var list := _scroll(vb)
	for i in jobs.size():
		var job: Dictionary = jobs[i]
		var rem := GameState.job_remaining(job)
		var row := HBoxContainer.new()
		var ic := CenterContainer.new()
		ic.custom_minimum_size = Vector2(44, 44)
		ic.add_child(UT.coin_icon(item_color(job["recipe"]), 36))
		row.add_child(ic)
		var mid := VBoxContainer.new()
		mid.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		mid.add_child(_label(item_name(job["recipe"]), 22))
		var bar := ProgressBar.new()
		bar.show_percentage = false
		bar.max_value = 1.0
		bar.step = 0.001
		bar.custom_minimum_size = Vector2(0, 20)
		var total := float(Config.data["recipes"][job["recipe"]]["seconds"])
		bar.value = 1.0 - rem / total
		mid.add_child(bar)
		row.add_child(mid)
		var ji := i
		if rem <= 0.0:
			row.add_child(_button(tr("BTN_COLLECT"), func():
				GameState.collect_job(ws, ji)
				open_workshop(ws, false), 170, 22))
		else:
			row.add_child(_label(TimeService.format_duration(rem), 22))
		list.add_child(row)
	list.add_child(_label(tr("RECIPES"), 24, Color("#a0612a")))
	var full := jobs.size() >= int(def["slots"])
	for r in def["recipes"]:
		var rec: Dictionary = Config.data["recipes"][r]
		var parts := []
		var can := true
		for item in rec["inputs"]:
			var need := int(rec["inputs"][item])
			var have := int(GameState.state["inventory"].get(item, 0))
			parts.append("%d %s (%d)" % [need, item_name(item), have])
			if have < need:
				can = false
		var row := HBoxContainer.new()
		var ic := CenterContainer.new()
		ic.custom_minimum_size = Vector2(44, 44)
		ic.add_child(UT.coin_icon(item_color(r), 36))
		row.add_child(ic)
		var info := VBoxContainer.new()
		info.add_theme_constant_override("separation", 0)
		info.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		info.add_child(_label("%s  ·  %s" % [item_name(r), TimeService.format_duration(float(rec["seconds"]))], 22))
		info.add_child(_label(tr("NEEDS") % ", ".join(parts), 17, Color("#8a6a4a") if can else UT.RED))
		row.add_child(info)
		var rid: String = r
		var b := _button(tr("BTN_MAKE"), func():
			GameState.start_job(ws, rid)
			open_workshop(ws, false), 150, 22)
		b.disabled = full or not can
		row.add_child(b)
		list.add_child(row)
	_live_workshop = ws

var _live_workshop := ""
var _live_t := 0.0

# ------------------------------------------------------------------ missions / daily gift / profile

func _reward_text(r: Dictionary) -> String:
	var parts := []
	if int(r.get("coins", 0)) > 0: parts.append(tr("COST_COINS") % int(r["coins"]))
	if int(r.get("premium", 0)) > 0: parts.append(tr("COST_PREMIUM") % int(r["premium"]))
	if int(r.get("xp", 0)) > 0: parts.append("%d XP" % int(r["xp"]))
	var items: Dictionary = r.get("items", {})
	for it in items: parts.append("%d %s" % [int(items[it]), item_name(it)])
	return " + ".join(parts)

func _claimable_count() -> int:
	var n := 0
	var now := TimeService.now()
	if GameState.daily_available(): n += 1
	for kind in FarmRules.mission_kinds(Config.data, now):
		var b: Dictionary = GameState.state["missions"].get(kind, {})
		if int(b.get("period", -1)) != FarmRules._period(kind, now): continue
		for m in FarmRules.active_missions(Config.data, kind, now):
			if int(b["progress"].get(m["id"], 0)) >= int(m["count"]) and not m["id"] in b["claimed"]: n += 1
	for a in Config.data["missions"]["achievements"]:
		if not a["id"] in GameState.state["achievements"] and int(GameState.state["stats"].get(a["stat"], 0)) >= int(a["count"]): n += 1
	return n

func _mission_text(m: Dictionary) -> String:
	var what := tr("ANY_ITEM") if m.get("item", "") == "" else item_name(m["item"])
	if m.has("stat"):
		return tr("ACH_" + String(m["stat"]).to_upper()) % int(m["count"])
	return tr("MIS_" + String(m["event"]).to_upper()) % [int(m["count"]), what]

func _mission_row(list: VBoxContainer, m: Dictionary, progress: int, claimed: bool, claim: Callable) -> void:
	var row := HBoxContainer.new()
	var info := VBoxContainer.new()
	info.add_theme_constant_override("separation", 2)
	info.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	info.add_child(_label(_mission_text(m), 22))
	var bar := ProgressBar.new()
	bar.max_value = int(m["count"])
	bar.value = mini(progress, int(m["count"]))
	bar.show_percentage = false
	bar.custom_minimum_size = Vector2(0, 18)
	info.add_child(bar)
	info.add_child(_label("%d / %d   ·   %s" % [mini(progress, int(m["count"])), int(m["count"]), _reward_text(m["reward"])], 17, Color("#8a6a4a")))
	row.add_child(info)
	var b: Button
	if claimed:
		b = _button(tr("CLAIMED"), func(): pass, 160, 20)
		b.disabled = true
	else:
		b = _button(tr("BTN_CLAIM"), claim, 160, 20)
		b.disabled = progress < int(m["count"])
	row.add_child(b)
	list.add_child(row)

func open_missions(tab: String = "daily") -> void:
	var vb := _open_modal(tr("TITLE_MISSIONS"))
	var now := TimeService.now()
	var tabs := HBoxContainer.new()
	var kinds: Array = FarmRules.mission_kinds(Config.data, now) + ["achievements"]
	for k in kinds:
		var key: String = k
		var label := tr("TAB_" + key.to_upper()) if not key.begins_with("event:") else tr("TAB_EVENT")
		var b := _button(label, func(): open_missions(key), 0, 20)
		b.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		b.disabled = key == tab
		tabs.add_child(b)
	vb.add_child(tabs)
	var list := _scroll(vb)
	if tab == "daily":
		var gift := HBoxContainer.new()
		var gl := _label(tr("DAILY_GIFT"), 22, Color("#a0612a"))
		gl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		gift.add_child(gl)
		var gb := _button(tr("BTN_CLAIM") if GameState.daily_available() else tr("GIFT_TOMORROW"), func():
			if GameState.claim_daily()["ok"]: open_missions("daily"), 220, 20)
		gb.disabled = not GameState.daily_available()
		gift.add_child(gb)
		list.add_child(gift)
	if tab == "achievements":
		for a in Config.data["missions"]["achievements"]:
			var aid: String = a["id"]
			_mission_row(list, a, int(GameState.state["stats"].get(a["stat"], 0)), a["id"] in GameState.state["achievements"], func():
				if GameState.claim_achievement(aid)["ok"]: open_missions("achievements"))
		return
	var b: Dictionary = GameState.state["missions"].get(tab, {})
	var current := int(b.get("period", -1)) == FarmRules._period(tab, now)
	for m in FarmRules.active_missions(Config.data, tab, now):
		var mid: String = m["id"]
		var prog := int(b["progress"].get(mid, 0)) if current else 0
		var done: bool = current and mid in b["claimed"]
		_mission_row(list, m, prog, done, func():
			if GameState.claim_mission(tab, mid)["ok"]: open_missions(tab))
	list.add_child(_label(tr("MISSIONS_NOTE"), 17, Color("#8a6a4a")))

func show_daily_gift_if_available() -> void:
	## A friendly welcome gift. Never forced: closing it is fine, it stays in Missions all day.
	if not GameState.daily_available() or _modal:
		return
	var step := int(GameState.state["daily_reward"].get("step", 0))
	var table: Array = Config.data.get("daily_rewards", [])
	var vb := _open_modal(tr("DAILY_GIFT"))
	vb.add_child(_label(tr("DAILY_GIFT_TEXT"), 24))
	var row := HBoxContainer.new()
	for i in table.size():
		var cell := PanelContainer.new()
		var cur := i == step % table.size()
		cell.add_theme_stylebox_override("panel", UT.box(Color("#ffe08a") if cur else Color("#fff6e0"), Color("#e0b46a"), 14, 3 if cur else 2, 8))
		cell.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		var l := _label("%d\n%s" % [i + 1, _reward_text(table[i])], 15)
		l.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		l.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		cell.add_child(l)
		row.add_child(cell)
	vb.add_child(row)
	vb.add_child(_button(tr("BTN_CLAIM"), func():
		GameState.claim_daily()
		close_modal(), 0, 26))

const NAME_A := ["Happy", "Sunny", "Lucky", "Busy", "Merry", "Clever", "Brave", "Cozy"]
const NAME_B := ["Farmer", "Bunny", "Duck", "Goat", "Lamb", "Pumpkin", "Carrot", "Bee"]

func open_profile() -> void:
	var vb := _open_modal(tr("TITLE_PROFILE"))
	var st := GameState.state
	var name_row := HBoxContainer.new()
	var nl := _label(String(st.get("display_name", "Farmer")), 30)
	nl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	name_row.add_child(nl)
	# Child-safe names: picked from word lists, never typed.
	name_row.add_child(_button(tr("BTN_NEW_NAME"), func():
		var n: String = NAME_A.pick_random() + " " + NAME_B.pick_random()
		GameState.set_display_name(n)
		open_profile(), 220, 20))
	vb.add_child(name_row)
	vb.add_child(_label(tr("PROFILE_LEVEL") % [int(st["level"]), int(st["xp"])], 22))
	var stats: Dictionary = st["stats"]
	for k in ["harvests", "collected", "produced", "sold"]:
		vb.add_child(_label(tr("STAT_" + k.to_upper()) % int(stats.get(k, 0)), 20))
	vb.add_child(_label(tr("PROFILE_ACH") % [st["achievements"].size(), Config.data["missions"]["achievements"].size()], 20))
	if Backend.enabled() and Backend.user_id() != "":
		vb.add_child(_label(tr("PLAYER_ID") % Backend.user_id().substr(0, 8), 16, Color("#8a6a4a")))

func open_settings() -> void:
	var vb := _open_modal(tr("TITLE_SETTINGS"))
	for spec in [["MUSIC", "music"], ["SOUND", "sfx"]]:
		var row := HBoxContainer.new()
		var l := _label(tr(spec[0]), 24)
		l.custom_minimum_size.x = 200
		row.add_child(l)
		var s := HSlider.new()
		s.min_value = 0
		s.max_value = 1
		s.step = 0.05
		s.value = float(SaveManager.settings[spec[1]])
		s.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		s.custom_minimum_size.y = 48
		var key: String = spec[1]
		s.value_changed.connect(func(v):
			SaveManager.settings[key] = v
			Audio.set_volumes(float(SaveManager.settings["music"]), float(SaveManager.settings["sfx"]))
			SaveManager.save_settings())
		row.add_child(s)
		vb.add_child(row)
	vb.add_child(_label(tr("LANGUAGE"), 24))
	var langs := HBoxContainer.new()
	for spec in [["en", "English"], ["de", "Deutsch"], ["ar", "العربية"]]:
		var code: String = spec[0]
		var b := _button(spec[1], func():
			SaveManager.settings["language"] = code
			SaveManager.save_settings()
			Localization.set_language(code), 180)
		b.disabled = Localization.current == code
		langs.add_child(b)
	vb.add_child(langs)
	var sp := Control.new()
	sp.size_flags_vertical = Control.SIZE_EXPAND_FILL
	vb.add_child(sp)
	var cloud := "SAVE_LOCAL_NOTE"
	if Backend.enabled():
		cloud = {"online": "CLOUD_ONLINE", "connecting": "CLOUD_CONNECTING"}.get(Backend.status, "CLOUD_ERROR")
	vb.add_child(_label(tr(cloud), 18, Color("#8a6a4a")))
	if Backend.enabled() and Backend.status == "online":
		var hb2 := HBoxContainer.new()
		var parents := _button(tr("BTN_PARENTS"), func(): PFL.open(self, open_parents), 0, 18)
		parents.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		hb2.add_child(parents)
		var del := _button(tr("BTN_DELETE_DATA"), func(): PFL.open(self, _confirm_delete_data), 0, 18)
		del.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		hb2.add_child(del)
		vb.add_child(hb2)
	var reset := _button(tr("BTN_RESET_FARM"), _confirm_reset, 0, 20)
	reset.add_theme_stylebox_override("normal", UT.box(Color("#ffd6cf"), UT.RED))
	vb.add_child(reset)

func _confirm_delete_data() -> void:
	var vb := _open_modal(tr("DELETE_DATA_TITLE"))
	vb.add_child(_label(tr("DELETE_DATA_TEXT"), 22))
	var hb := HBoxContainer.new()
	hb.add_child(_button(tr("BTN_CANCEL"), open_settings, 200))
	var yes := _button(tr("BTN_DELETE_DATA"), func():
		close_modal()
		if await Backend.delete_my_data():
			SaveManager.delete_save()
			toast(tr("DATA_DELETED"), true)
		else:
			toast(tr("ERR_OFFLINE")), 260)
	yes.add_theme_stylebox_override("normal", UT.box(Color("#ffd6cf"), UT.RED))
	hb.add_child(yes)
	vb.add_child(hb)

func open_parents() -> void:
	## Parents only (behind the parental gate): link a parent email so the farm can be restored
	## after reinstalling or on a new phone. Optional. The email is used only for sign-in.
	var vb := _open_modal(tr("TITLE_PARENTS"))
	vb.add_child(_label(tr("PARENTS_TEXT"), 18))
	var email := LineEdit.new()
	email.placeholder_text = "parent@example.com"
	email.custom_minimum_size = Vector2(0, 56)
	email.virtual_keyboard_type = LineEdit.KEYBOARD_TYPE_EMAIL_ADDRESS
	vb.add_child(email)
	var row := HBoxContainer.new()
	var link := _button(tr("BTN_LINK_EMAIL"), func():
		var r: Dictionary = await Backend.link_email(email.text.strip_edges())
		toast(tr("LINK_SENT") if r["ok"] else tr("ERR_" + String(r.get("error", "invalid")).to_upper()), r["ok"]), 0, 18)
	link.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_child(link)
	var restore := _button(tr("BTN_RESTORE"), func():
		var r: Dictionary = await Backend.send_login_code(email.text.strip_edges())
		if r["ok"]:
			_open_code_entry(email.text.strip_edges())
		else:
			toast(tr("ERR_" + String(r.get("error", "invalid")).to_upper())), 0, 18)
	restore.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_child(restore)
	vb.add_child(row)
	vb.add_child(_label(tr("RESTORE_NOTE"), 16, Color("#8a6a4a")))

func _open_code_entry(email: String) -> void:
	var vb := _open_modal(tr("TITLE_PARENTS"))
	vb.add_child(_label(tr("ENTER_CODE") % email, 20))
	var code := LineEdit.new()
	code.custom_minimum_size = Vector2(0, 56)
	code.virtual_keyboard_type = LineEdit.KEYBOARD_TYPE_NUMBER
	code.max_length = 10
	vb.add_child(code)
	vb.add_child(_button(tr("BTN_RESTORE"), func():
		var r: Dictionary = await Backend.verify_login_code(email, code.text.strip_edges())
		close_modal()
		toast(tr("RESTORED") if r["ok"] else tr("ERR_" + String(r.get("error", "invalid")).to_upper()), r["ok"]), 0, 22))

func _confirm_reset() -> void:
	var vb := _open_modal(tr("RESET_CONFIRM_TITLE"))
	vb.add_child(_label(tr("RESET_CONFIRM_TEXT"), 22))
	var hb := HBoxContainer.new()
	hb.add_child(_button(tr("BTN_CANCEL"), open_settings, 200))
	var yes := _button(tr("BTN_RESET_FARM"), func():
		SaveManager.delete_save()
		close_modal()
		toast(tr("FARM_RESET"), true), 260)
	yes.add_theme_stylebox_override("normal", UT.box(Color("#ffd6cf"), UT.RED))
	hb.add_child(yes)
	vb.add_child(hb)

# ------------------------------------------------------------------ feedback

func _on_action_result(action: String, r: Dictionary) -> void:
	if not r.get("ok", false):
		Audio.play("error")
		toast(tr("ERR_" + String(r.get("error", "invalid")).to_upper()))
		return
	match action:
		"plant":
			close_context()
		"water":
			open_plot(int(r["idx"]))
		"feed":
			close_context()
		"sell":
			toast(tr("SOLD_FOR") % int(r["coins"]), true)
		"claim_mission", "claim_achievement", "claim_daily":
			Audio.play("coin")
			toast(tr("REWARD_GOT") % _reward_text(r["reward"]), true)
		"buy_workshop", "buy_expansion", "buy_decoration", "upgrade_storage":
			Audio.play("coin")
			toast(tr("PURCHASED"), true)

func _on_level_up(lvl: int) -> void:
	Audio.play("levelup")
	var vb := _open_modal(tr("LEVEL_UP_TITLE"))
	vb.add_child(_label(tr("LEVEL_UP_TEXT") % [lvl, int(Config.economy().get("level_reward_coins", 0))], 28))
	var unlocked := []
	for crop_id in Config.crops():
		if int(Config.crops()[crop_id]["unlock_level"]) == lvl:
			unlocked.append(tr("UNLOCKED_CROP") % item_name(crop_id))
	for id in Config.data.get("workshops", {}):
		if int(Config.data["workshops"][id]["unlock_level"]) == lvl:
			unlocked.append(tr("UNLOCKED_BUILDING") % tr("WS_" + id.to_upper()))
	for e in Config.data.get("expansions", []):
		if int(e["unlock_level"]) == lvl:
			unlocked.append(tr("UNLOCKED_LAND"))
	for id in Config.data.get("decorations", {}):
		if int(Config.data["decorations"][id]["unlock_level"]) == lvl:
			unlocked.append(tr("UNLOCKED_DECOR") % tr("DECO_" + id.to_upper()))
	for u in unlocked:
		vb.add_child(_label(u, 24, Color("#2f6b1f")))
	vb.add_child(_button(tr("BTN_GREAT"), close_modal, 0, 26))
