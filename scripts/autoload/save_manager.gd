extends Node
## Local save with autosave, background/quit save and a backup copy for crash recovery.
## This is LOCAL ONLY. Cloud save (Supabase) is added in Phase 4 behind the same interface.

const SAVE_PATH := "user://farm_save.json"
const BACKUP_PATH := "user://farm_save.bak.json"
const SETTINGS_PATH := "user://settings.json"

var settings: Dictionary = {"music": 0.0, "sfx": 0.8, "language": ""}
var _timer: Timer

func _ready() -> void:
	load_settings()
	# v2: the old synthesized music was switched on by default; start everyone with it off.
	if not settings.has("music_v2"):
		settings["music"] = 0.0
		settings["music_v2"] = true
	Localization.set_language(settings.get("language", ""))
	Audio.set_volumes(float(settings["music"]), float(settings["sfx"]))
	load_game()
	_timer = Timer.new()
	_timer.wait_time = float(Config.economy().get("autosave_seconds", 20))
	_timer.autostart = true
	_timer.timeout.connect(save_game)
	add_child(_timer)
	get_tree().auto_accept_quit = false

func _notification(what: int) -> void:
	match what:
		NOTIFICATION_APPLICATION_PAUSED, NOTIFICATION_APPLICATION_FOCUS_OUT:
			save_game()
		NOTIFICATION_WM_CLOSE_REQUEST, NOTIFICATION_WM_GO_BACK_REQUEST:
			save_game()
			get_tree().quit()

func save_game() -> bool:
	if GameState.state.is_empty():
		return false
	GameState.state["last_seen"] = TimeService.now()
	var text := JSON.stringify(GameState.state)
	# Write to a temp file first, then swap, so a crash mid-write never corrupts the save.
	var tmp := SAVE_PATH + ".tmp"
	var f := FileAccess.open(tmp, FileAccess.WRITE)
	if f == null:
		return false
	f.store_string(text)
	f.close()
	var dir := DirAccess.open("user://")
	if FileAccess.file_exists(SAVE_PATH):
		dir.copy(SAVE_PATH, BACKUP_PATH)
	dir.rename(tmp, SAVE_PATH)
	return true

func load_game() -> bool:
	for path in [SAVE_PATH, BACKUP_PATH]:
		var d := Config.load_json(path)
		if not d.is_empty() and d.has("plots"):
			GameState.load_state(d)
			return true
	return false

func delete_save() -> void:
	for p in [SAVE_PATH, BACKUP_PATH]:
		if FileAccess.file_exists(p):
			DirAccess.remove_absolute(ProjectSettings.globalize_path(p))
	GameState.reset()
	save_game()

func load_settings() -> void:
	var d := Config.load_json(SETTINGS_PATH)
	settings.merge(d, true)

func save_settings() -> void:
	var f := FileAccess.open(SETTINGS_PATH, FileAccess.WRITE)
	if f:
		f.store_string(JSON.stringify(settings))
