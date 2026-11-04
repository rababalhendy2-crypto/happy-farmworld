extends RefCounted
## Parental gate: a simple multiplication question that young children cannot easily answer.
## Guards account settings, data deletion and (Phase 6) purchases and external links.

static func open(hud: CanvasLayer, on_pass: Callable) -> void:
	var a := randi_range(6, 9)
	var b := randi_range(6, 9)
	var answer := a * b
	var vb: VBoxContainer = hud._open_modal(hud.tr("PARENT_GATE_TITLE"))
	vb.add_child(hud._label(hud.tr("PARENT_GATE_TEXT") % [a, b], 26))
	var grid := GridContainer.new()
	grid.columns = 4
	var options := [answer, answer + a, answer - b, answer + 10]
	options.shuffle()
	for o in options:
		var val: int = o
		grid.add_child(hud._button(str(val), func():
			if val == answer:
				on_pass.call()
			else:
				hud.close_modal()
				hud.toast(hud.tr("PARENT_GATE_WRONG")), 140, 28))
	vb.add_child(grid)
