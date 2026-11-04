extends Node
## English / German / Arabic. Strings live in data/i18n.json (no editor import step needed).
## Arabic switches the UI to right-to-left layout.

signal language_changed(lang: String)

const I18N_PATH := "res://data/i18n.json"
const LANGUAGES := ["en", "de", "ar"]

var current := "en"

func _ready() -> void:
	var d := Config.load_json(I18N_PATH)
	for lang in d:
		var t := Translation.new()
		t.locale = lang
		var table: Dictionary = d[lang]
		for key in table:
			t.add_message(key, table[key])
		TranslationServer.add_translation(t)
	set_language("")

func set_language(lang: String) -> void:
	if lang == "" or not lang in LANGUAGES:
		lang = OS.get_locale_language()
		if not lang in LANGUAGES:
			lang = "en"
	current = lang
	TranslationServer.set_locale(lang)
	language_changed.emit(lang)

func is_rtl() -> bool:
	return current == "ar"
