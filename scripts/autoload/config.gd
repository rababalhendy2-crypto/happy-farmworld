extends Node
## Loads the data-driven game configuration (prices, timers, rewards, unlocks).
## Nothing in the game hard-codes economy numbers: change data/game_config.json instead.

const CONFIG_PATH := "res://data/game_config.json"

var data: Dictionary = {}

func _ready() -> void:
	reload()

func reload() -> void:
	data = load_json(CONFIG_PATH)
	if data.is_empty():
		push_error("Config: could not load %s" % CONFIG_PATH)

static func load_json(path: String) -> Dictionary:
	var f := FileAccess.open(path, FileAccess.READ)
	if f == null:
		return {}
	var parsed: Variant = JSON.parse_string(f.get_as_text())
	return parsed if parsed is Dictionary else {}

func crops() -> Dictionary: return data.get("crops", {})
func animals() -> Dictionary: return data.get("animals", {})
func items() -> Dictionary: return data.get("items", {})
func economy() -> Dictionary: return data.get("economy", {})
