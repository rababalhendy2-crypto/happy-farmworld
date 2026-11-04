extends Node
## Supabase client: anonymous account, cloud save, server-authoritative actions.
##
## How it works:
##  * The game applies an action locally first (instant feedback), then queues it here with a
##    random idempotency key. The queue is saved to disk, so actions made offline are sent later.
##  * The server re-validates every action with its own clock and config. Its answer contains the
##    full authoritative state, which replaces the local state once the queue is empty.
##    So a modified client or save file cannot create coins: the server state always wins.
##  * Only the publishable (anon) key is in the app. The service-role key never leaves the server.
##  * No email, name or birthday is collected: accounts are anonymous.
##
## Configure in data/backend.json: {"url": "https://<ref>.supabase.co", "anon_key": "<publishable key>"}.
## If url is empty the game runs in local-only mode.

signal status_changed(status: String)   # "offline" | "connecting" | "online" | "error"
signal server_state_received(server_state: Dictionary, local_state: Dictionary)

const CONFIG_PATH := "res://data/backend.json"
const SESSION_PATH := "user://session.json"
const QUEUE_PATH := "user://pending_actions.json"

var url := ""
var anon_key := ""
var status := "offline"
var session: Dictionary = {}
var queue: Array = []
var _busy := false
var _retry_s := 2.0
var _http: HTTPRequest

func enabled() -> bool:
	return url != "" and anon_key != ""

func _ready() -> void:
	var cfg := Config.load_json(CONFIG_PATH)
	url = String(cfg.get("url", "")).trim_suffix("/")
	anon_key = String(cfg.get("anon_key", ""))
	_http = HTTPRequest.new()
	_http.timeout = 15.0
	add_child(_http)
	var q: Variant = _parse(_read(QUEUE_PATH))
	queue = q if q is Array else []
	var s: Variant = _parse(_read(SESSION_PATH))
	session = s if s is Dictionary else {}
	if enabled():
		connect_to_server.call_deferred()

func user_id() -> String:
	return String(session.get("user", {}).get("id", ""))

# ------------------------------------------------------------------ public API

func connect_to_server() -> void:
	if not enabled() or _busy:
		return
	_set_status("connecting")
	_busy = true
	var ok := await _ensure_session()
	if ok:
		var r := await rpc_call("hf_init_player", {})
		if r["ok"]:
			_apply_server_state(r["data"])
			_set_status("online")
			_busy = false
			_retry_s = 2.0
			_flush()
			return
	_busy = false
	_set_status("error")
	_schedule_retry()

func enqueue(action: String, args: Dictionary) -> void:
	if not enabled():
		return
	queue.append({"action": action, "args": args, "key": _uuid4()})
	_write(QUEUE_PATH, JSON.stringify(queue))
	_flush()

func delete_my_data() -> bool:
	if status != "online":
		return false
	var r := await rpc_call("hf_delete_my_data", {})
	if r["ok"]:
		session = {}
		queue = []
		_write(SESSION_PATH, "{}")
		_write(QUEUE_PATH, "[]")
		_set_status("offline")
	return r["ok"]

## Parents: attach an email to this anonymous account (Supabase sends a confirmation email).
func link_email(email: String) -> Dictionary:
	if status != "online":
		return {"ok": false, "error": "offline"}
	if not _valid_email(email):
		return {"ok": false, "error": "invalid"}
	var r := await _request("/auth/v1/user", {"email": email}, true, HTTPClient.METHOD_PUT)
	return {"ok": r["ok"], "error": "" if r["ok"] else "invalid"}

## Parents: restore a linked farm on a new phone. Step 1: send a one-time code by email.
func send_login_code(email: String) -> Dictionary:
	if not enabled():
		return {"ok": false, "error": "offline"}
	if not _valid_email(email):
		return {"ok": false, "error": "invalid"}
	var r := await _request("/auth/v1/otp", {"email": email, "create_user": false}, false)
	return {"ok": r["ok"], "error": "" if r["ok"] else "invalid"}

## Step 2: verify the code, replace the local session, load the farm from the cloud.
func verify_login_code(email: String, code: String) -> Dictionary:
	var r := await _request("/auth/v1/verify", {"type": "email", "email": email, "token": code}, false)
	if not r["ok"]:
		return {"ok": false, "error": "invalid"}
	var d: Dictionary = r["data"]
	session = {"access_token": d.get("access_token", ""), "refresh_token": d.get("refresh_token", ""),
		"expires_at": Time.get_unix_time_from_system() + float(d.get("expires_in", 3600)),
		"user": {"id": d.get("user", {}).get("id", "")}}
	_write(SESSION_PATH, JSON.stringify(session))
	queue = []
	_write(QUEUE_PATH, "[]")
	_set_status("offline")
	await connect_to_server()
	return {"ok": status == "online", "error": "" if status == "online" else "offline"}

static func _valid_email(e: String) -> bool:
	var re := RegEx.create_from_string("^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$")
	return e.length() <= 120 and re.search(e) != null

## Reloads the authoritative state (after purchases / ad rewards credited by the server).
func refresh_state() -> void:
	if status != "online" or not queue.is_empty():
		return
	var r := await rpc_call("hf_get_state", {})
	if r["ok"]:
		_apply_server_state(r["data"])

## Calls a Supabase Edge Function with the player's session.
func call_function(fn: String, body: Dictionary) -> Dictionary:
	if status != "online":
		return {"ok": false}
	return await _request("/functions/v1/" + fn, body, true)

# ------------------------------------------------------------------ queue

func _flush() -> void:
	if _busy or status != "online" or queue.is_empty():
		return
	_busy = true
	while not queue.is_empty():
		var a: Dictionary = queue[0]
		var r := await rpc_call("hf_action", {"p_action": a["action"], "p_args": a["args"], "p_key": a["key"]})
		if not r["ok"]:
			if int(r.get("code", 0)) == 401:
				session.erase("access_token")
				if await _ensure_session():
					continue
			# Network or server problem: keep the action (same key = no double apply) and retry later.
			_busy = false
			_set_status("error")
			_schedule_retry()
			return
		queue.pop_front()
		_write(QUEUE_PATH, JSON.stringify(queue))
		var res: Dictionary = r["data"]
		if queue.is_empty() and res.has("state"):
			_apply_server_state(res["state"])
	_busy = false

func _schedule_retry() -> void:
	get_tree().create_timer(_retry_s).timeout.connect(func():
		if status != "online":
			connect_to_server()
		else:
			_flush())
	_retry_s = minf(_retry_s * 2.0, 60.0)

func _apply_server_state(s: Dictionary) -> void:
	server_state_received.emit(s, GameState.state.duplicate(true))
	if s.has("server_time"):
		TimeService.server_offset = float(s["server_time"]) - Time.get_unix_time_from_system()
		TimeService.last_seen = 0.0
	GameState.apply_server_state(s)

# ------------------------------------------------------------------ auth

func _ensure_session() -> bool:
	var now := Time.get_unix_time_from_system()
	if session.has("access_token") and float(session.get("expires_at", 0)) > now + 60:
		return true
	var r: Dictionary
	if session.has("refresh_token"):
		r = await _request("/auth/v1/token?grant_type=refresh_token", {"refresh_token": session["refresh_token"]}, false)
		if not r["ok"] and int(r.get("code", 0)) in [400, 401]:
			# Refresh token no longer valid. We do NOT silently create a new account here,
			# because that would look like lost progress. Report an error instead.
			return false
	else:
		# Anonymous sign-in: no personal data at all.
		r = await _request("/auth/v1/signup", {"data": {}}, false)
	if not r["ok"]:
		return false
	var d: Dictionary = r["data"]
	session = {
		"access_token": d.get("access_token", ""),
		"refresh_token": d.get("refresh_token", ""),
		"expires_at": now + float(d.get("expires_in", 3600)),
		"user": {"id": d.get("user", {}).get("id", "")},
	}
	_write(SESSION_PATH, JSON.stringify(session))
	return session["access_token"] != ""

# ------------------------------------------------------------------ http

func rpc_call(fn: String, body: Dictionary) -> Dictionary:
	return await _request("/rest/v1/rpc/" + fn, body, true)

func _request(path: String, body: Dictionary, authed: bool, method := HTTPClient.METHOD_POST) -> Dictionary:
	while _http.get_http_client_status() != HTTPClient.STATUS_DISCONNECTED:
		await get_tree().process_frame
	var headers := PackedStringArray(["Content-Type: application/json", "apikey: " + anon_key])
	if authed:
		headers.append("Authorization: Bearer " + String(session.get("access_token", "")))
	var err := _http.request(url + path, headers, method, JSON.stringify(body))
	if err != OK:
		return {"ok": false, "code": 0}
	var res: Array = await _http.request_completed
	var code: int = res[1]
	var text: String = (res[3] as PackedByteArray).get_string_from_utf8()
	var data: Variant = _parse(text)
	if res[0] != HTTPRequest.RESULT_SUCCESS or code < 200 or code >= 300:
		return {"ok": false, "code": code, "data": data}
	return {"ok": true, "code": code, "data": data if data is Dictionary else {}}

# ------------------------------------------------------------------ util

func _set_status(s: String) -> void:
	if s != status:
		status = s
		status_changed.emit(s)

static func _uuid4() -> String:
	var b := Crypto.new().generate_random_bytes(16)
	b[6] = (b[6] & 0x0f) | 0x40
	b[8] = (b[8] & 0x3f) | 0x80
	var h := b.hex_encode()
	return "%s-%s-%s-%s-%s" % [h.substr(0, 8), h.substr(8, 4), h.substr(12, 4), h.substr(16, 4), h.substr(20, 12)]

static func _parse(text: String) -> Variant:
	if text.strip_edges() == "":
		return null
	var j := JSON.new()
	return j.data if j.parse(text) == OK else null

static func _read(path: String) -> String:
	var f := FileAccess.open(path, FileAccess.READ)
	return f.get_as_text() if f else ""

static func _write(path: String, text: String) -> void:
	var f := FileAccess.open(path, FileAccess.WRITE)
	if f:
		f.store_string(text)
