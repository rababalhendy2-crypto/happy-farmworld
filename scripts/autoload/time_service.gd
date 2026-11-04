extends Node
## Single source of time for all game timers.
## Phase 1: device clock + anti-rollback guard (time never goes backwards relative to the last save).
## Phase 4: server_offset is set from the Supabase server clock, so changing the phone clock
## cannot speed up crops or animals.

var server_offset: float = 0.0
var last_seen: float = 0.0

func now() -> float:
	var t := Time.get_unix_time_from_system() + server_offset
	if t < last_seen:
		return last_seen
	last_seen = t
	return t

func set_last_seen(t: float) -> void:
	last_seen = maxf(last_seen, t)

static func format_duration(seconds: float) -> String:
	var s := int(ceil(maxf(seconds, 0.0)))
	if s >= 3600:
		return "%dh %02dm" % [s / 3600, (s % 3600) / 60]
	if s >= 60:
		return "%dm %02ds" % [s / 60, s % 60]
	return "%ds" % s
