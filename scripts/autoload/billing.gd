extends Node
## Google Play Billing adapter.
## Uses the official GodotGooglePlayBilling Android plugin when it is present in the build
## (requires a Gradle Android build, see docs/MONETIZATION.md). Without it, `available()` is false
## and the store shows that purchases are not available: there are NO fake purchases.
##
## Security: purchases are bound to the player (obfuscated account id = Supabase user id) and are
## only granted after the server (Edge Function verify-purchase) confirmed the token with Google.

signal products_loaded
signal purchase_finished(product_id: String, ok: bool, message: String)

var _p: Object = null
var connected := false
var prices: Dictionary = {}   # product_id -> localized price string from Google Play

func _ready() -> void:
	if Engine.has_singleton("GodotGooglePlayBilling"):
		_p = Engine.get_singleton("GodotGooglePlayBilling")
		_p.connected.connect(_on_connected)
		_p.disconnected.connect(func(): connected = false)
		_p.sku_details_query_completed.connect(_on_details)
		_p.purchases_updated.connect(_on_purchases_updated)
		_p.purchase_error.connect(func(code, msg): purchase_finished.emit("", false, str(msg)))
		_p.query_purchases_response.connect(_on_query_purchases)
		_p.startConnection()

func available() -> bool:
	return _p != null and connected and Backend.status == "online"

func product_ids() -> PackedStringArray:
	var ids := PackedStringArray()
	for pr in Config.data.get("store", {}).get("products", []):
		ids.append(pr["id"])
	return ids

func _on_connected() -> void:
	connected = true
	_p.querySkuDetails(product_ids(), "inapp")
	restore()

func _on_details(details: Array) -> void:
	for d in details:
		prices[d["sku"]] = d["price"]
	products_loaded.emit()

func buy(product_id: String) -> void:
	if not available():
		purchase_finished.emit(product_id, false, "unavailable")
		return
	_p.setObfuscatedAccountId(Backend.user_id())
	_p.purchase(product_id)

func restore() -> void:
	if _p and connected:
		_p.queryPurchases("inapp")

func _on_query_purchases(result: Dictionary) -> void:
	if result.get("status", 1) == OK:
		_on_purchases_updated(result.get("purchases", []))

func _on_purchases_updated(purchases: Array) -> void:
	for p in purchases:
		if int(p.get("purchase_state", 0)) != 1:   # 1 = PURCHASED (pending purchases are not granted)
			continue
		var sku: String = p["skus"][0] if p.has("skus") else p.get("sku", "")
		_verify(sku, p["purchase_token"])

func _verify(product_id: String, token: String) -> void:
	var r: Dictionary = await Backend.call_function("verify-purchase", {"product_id": product_id, "purchase_token": token})
	var ok: bool = r.get("ok", false) and r.get("data", {}).get("ok", false)
	if ok:
		if _is_consumable(product_id):
			_p.consumePurchase(token)
		await Backend.refresh_state()
	purchase_finished.emit(product_id, ok, "" if ok else String(r.get("data", {}).get("error", "error")))

func _is_consumable(id: String) -> bool:
	for pr in Config.data.get("store", {}).get("products", []):
		if pr["id"] == id:
			return pr["type"] == "consumable"
	return false
