extends RefCounted
## Shared look for all screens: rounded, warm, large touch targets, Latin + Arabic font.

const BROWN := Color("#5a3a1a")
const CREAM := Color("#fff6e0")
const ORANGE := Color("#ffb547")
const GREEN := Color("#6cc24a")
const RED := Color("#ef6b5b")

static var _font: Font

static func font() -> Font:
	if _font == null:
		var latin: FontFile = load("res://assets/fonts/NotoSans-Bold.ttf")
		var arabic: FontFile = load("res://assets/fonts/NotoSansArabic-Bold.ttf")
		latin.fallbacks = [arabic]
		_font = latin
	return _font

static func box(bg: Color, border: Color, radius := 18, border_w := 4, pad := 14) -> StyleBoxFlat:
	var s := StyleBoxFlat.new()
	s.bg_color = bg
	s.border_color = border
	s.set_border_width_all(border_w)
	s.set_corner_radius_all(radius)
	s.content_margin_left = pad
	s.content_margin_right = pad
	s.content_margin_top = pad * 0.6
	s.content_margin_bottom = pad * 0.6
	s.shadow_color = Color(0, 0, 0, 0.18)
	s.shadow_size = 4
	s.shadow_offset = Vector2(0, 3)
	return s

static func build() -> Theme:
	var t := Theme.new()
	t.default_font = font()
	t.default_font_size = 24
	t.set_color("font_color", "Label", BROWN)
	t.set_color("font_color", "Button", BROWN)
	t.set_color("font_hover_color", "Button", BROWN)
	t.set_color("font_pressed_color", "Button", BROWN)
	t.set_color("font_focus_color", "Button", BROWN)
	t.set_color("font_disabled_color", "Button", Color(BROWN, 0.45))
	t.set_stylebox("normal", "Button", box(ORANGE, Color("#c97a1c")))
	t.set_stylebox("hover", "Button", box(ORANGE.lightened(0.1), Color("#c97a1c")))
	t.set_stylebox("pressed", "Button", box(ORANGE.darkened(0.12), Color("#a8620f")))
	t.set_stylebox("focus", "Button", StyleBoxEmpty.new())
	t.set_stylebox("disabled", "Button", box(Color("#e9dcc3"), Color("#c9b48f")))
	t.set_stylebox("panel", "Panel", box(CREAM, Color("#e0b46a"), 26, 5, 20))
	t.set_stylebox("panel", "PanelContainer", box(CREAM, Color("#e0b46a"), 26, 5, 20))
	var bg := box(Color("#e9dcc3"), Color("#c9b48f"), 10, 2, 0)
	var fill := box(GREEN, Color("#4e9a33"), 10, 2, 0)
	t.set_stylebox("background", "ProgressBar", bg)
	t.set_stylebox("fill", "ProgressBar", fill)
	t.set_font_size("font_size", "ProgressBar", 16)
	t.set_color("font_color", "ProgressBar", BROWN)
	t.set_stylebox("slider", "HSlider", box(Color("#e9dcc3"), Color("#c9b48f"), 8, 2, 4))
	t.set_stylebox("grabber_area", "HSlider", box(ORANGE, Color("#c97a1c"), 8, 2, 4))
	t.set_stylebox("grabber_area_highlight", "HSlider", box(ORANGE, Color("#c97a1c"), 8, 2, 4))
	t.set_constant("separation", "HBoxContainer", 12)
	t.set_constant("separation", "VBoxContainer", 12)
	t.set_constant("h_separation", "GridContainer", 12)
	t.set_constant("v_separation", "GridContainer", 12)
	return t

static func pill_style() -> StyleBoxFlat:
	return box(Color(1, 1, 1, 0.92), Color("#e0b46a"), 30, 3, 12)

static func coin_icon(color: Color, size := 30, diamond := false) -> Control:
	var p := Panel.new()
	p.custom_minimum_size = Vector2(size, size)
	var s := box(color, color.darkened(0.3), size / 2 if not diamond else 6, 3, 0)
	s.shadow_size = 0
	p.add_theme_stylebox_override("panel", s)
	if diamond:
		p.pivot_offset = Vector2(size, size) / 2
		p.rotation_degrees = 45
		p.custom_minimum_size = Vector2(size, size) * 0.8
	p.mouse_filter = Control.MOUSE_FILTER_IGNORE
	return p
