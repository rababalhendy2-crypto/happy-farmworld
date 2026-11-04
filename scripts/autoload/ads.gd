extends Node
## Rewarded ads adapter (Google Mobile Ads / AdMob).
## The player must tap a clearly labelled button; ads never start on their own, never at app start,
## never interrupt play. If no ad SDK is present or no ad is loaded, the game works normally.
##
## Child safety: the SDK must be initialised with
##   tag_for_child_directed_treatment = true, tag_for_under_age_of_consent = true,
##   max_ad_content_rating = "G"  (non-personalised ads only).
## Rewards are granted ONLY by the server after AdMob's signed server-side verification (SSV)
## callback reaches the admob-ssv Edge Function. This client never adds coins itself.

signal ad_finished(kind: String, rewarded: bool)

var _p: Object = null
var status: Dictionary = {"watched": 0, "limit": 5, "next_coins": 0}

func _ready() -> void:
	# The AdMob plugin is added in the Gradle build; its singleton name depends on the plugin used.
	for name in ["GodotAdMob", "AdMob"]:
		if Engine.has_singleton(name):
			_p = Engine.get_singleton(name)
			break
	if _p and _p.has_method("initialize_child_directed"):
		_p.initialize_child_directed(true, "G")

func available() -> bool:
	return _p != null and Backend.status == "online" and _p.has_method("is_rewarded_loaded") and _p.is_rewarded_loaded()

func remaining() -> int:
	return maxi(0, int(status["limit"]) - int(status["watched"]))

func refresh_status() -> void:
	if Backend.status != "online":
		return
	var r: Dictionary = await Backend.rpc_call("hf_ad_status", {})
	if r["ok"]:
		status = r["data"]

## kind: "coins" or "speedup" (args: {"idx": plot})
func watch(kind: String, args: Dictionary = {}) -> void:
	if not available():
		ad_finished.emit(kind, false)
		return
	var r: Dictionary = await Backend.rpc_call("hf_ad_begin", {"p_kind": kind, "p_args": args})
	if not r["ok"] or not r["data"].get("ok", false):
		ad_finished.emit(kind, false)
		return
	var nonce: String = r["data"]["nonce"]
	# SSV options: AdMob sends these back to our server, signed by Google.
	_p.show_rewarded(Backend.user_id(), nonce)
	var rewarded: bool = await _p.rewarded_closed   # plugin emits true if the reward callback fired
	if rewarded:
		# The server is credited by the SSV callback; give it a moment, then load the new state.
		for wait in [1.5, 3.0, 6.0]:
			await get_tree().create_timer(wait).timeout
			await Backend.refresh_state()
	await refresh_status()
	ad_finished.emit(kind, rewarded)
