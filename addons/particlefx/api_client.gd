@tool
extends Node
## HTTP client for the ParticleFX Studio plugin API (/v1/plugin/*).
##
## Every call returns a Dictionary built by parse_response():
##   ok, status, kind, error, message, upgrade_url, data, body, headers
## kind is one of: ok, network, invalid_token, payment, not_found,
## rate_limited, unavailable, bad_request, error.
## The parse_* functions are static so the tests can use them without a server.

const ADDON_VERSION := "0.1.0"
const DEFAULT_API_BASE := "https://mcp.particlefx.studio"
const SITE_URL := "https://particlefx.studio"
const BILLING_URL := "https://particlefx.studio/billing"
const PRICING_URL := "https://particlefx.studio/pricing"

const SETTING_API_KEY := "particlefx/api_key"
const SETTING_API_BASE := "particlefx/api_base"

const JSON_TIMEOUT := 30.0
const POSTER_TIMEOUT := 30.0
const IMPORT_TIMEOUT := 300.0
const MAX_ZIP_BYTES := 200 * 1024 * 1024

## The key sent with requests. The dock keeps it in sync with EditorSettings.
var api_key := ""
## Base URL without a trailing slash.
var api_base := DEFAULT_API_BASE


func get_me() -> Dictionary:
	var r := await _request_json(HTTPClient.METHOD_GET, "/v1/plugin/me", {}, null, true)
	if r.ok:
		r.data = parse_me(r.data)
	return r


func get_library(query: String, category: String, offset: int, limit: int) -> Dictionary:
	var params := {"q": query, "category": category, "offset": offset, "limit": limit}
	var r := await _request_json(HTTPClient.METHOD_GET, "/v1/plugin/library", params, null, false)
	if r.ok:
		r.data = parse_library(r.data)
	return r


func get_my_effects(offset: int, limit: int) -> Dictionary:
	var r := await _request_json(HTTPClient.METHOD_GET, "/v1/plugin/my-effects", {"offset": offset, "limit": limit}, null, true)
	if r.ok:
		r.data = parse_my_effects(r.data)
	return r


## POST /v1/plugin/import. On success r.body is the zip, r.data has
## { filename, exports_left } (exports_left: -1 when unlimited, -2 when unknown).
func import_effect(effect_id: String, target_dir: String, scale: float, normal_map: bool, layout: String) -> Dictionary:
	var body := build_import_body(effect_id, target_dir, scale, normal_map, layout)
	var r := await _send(HTTPClient.METHOD_POST, api_base + "/v1/plugin/import", _headers(true, true), JSON.stringify(body), IMPORT_TIMEOUT, MAX_ZIP_BYTES)
	if r.ok:
		if not looks_like_zip(r.body):
			return error_result(r.status, "error", "bad_response", "The server did not send a zip file. Please try again later.", "")
		r.data = {
			"filename": filename_from_disposition(header_value(r.headers, "content-disposition")),
			"exports_left": parse_exports_left(header_value(r.headers, "x-pfx-exports-left")),
		}
	return r


## Downloads a poster and decodes it (WebP, with PNG/JPEG fallbacks). Returns a Texture2D or null.
func fetch_poster(url: String) -> Texture2D:
	if url == "":
		return null
	var r := await _send(HTTPClient.METHOD_GET, url, PackedStringArray([client_header()]), "", POSTER_TIMEOUT, 8 * 1024 * 1024)
	if not r.ok:
		return null
	var img := decode_image(r.body)
	if img == null:
		return null
	return ImageTexture.create_from_image(img)


func _request_json(method: int, path: String, params: Dictionary, body, needs_key: bool) -> Dictionary:
	if needs_key and api_key.strip_edges() == "":
		return error_result(401, "invalid_token", "invalid_token", "Add your API key first. You can get one from particlefx.studio/billing.", "")
	var url := api_base + path + build_query(params)
	var payload := "" if body == null else JSON.stringify(body)
	var r := await _send(method, url, _headers(api_key.strip_edges() != "", body != null), payload, JSON_TIMEOUT, 16 * 1024 * 1024)
	if r.ok:
		var parsed = parse_json(r.body)
		if typeof(parsed) != TYPE_DICTIONARY:
			return error_result(r.status, "error", "bad_response", "The server sent an unexpected reply. Please try again later.", "")
		r.data = parsed
	return r


func _send(method: int, url: String, headers: PackedStringArray, payload: String, timeout: float, max_bytes: int) -> Dictionary:
	var http := HTTPRequest.new()
	http.use_threads = true
	http.timeout = timeout
	http.body_size_limit = max_bytes
	add_child(http)
	var err := http.request(url, headers, method, payload)
	if err != OK:
		http.queue_free()
		return error_result(0, "network", "network", "Could not start the request (%s)." % error_string(err), "")
	var res: Array = await http.request_completed
	http.queue_free()
	return parse_response(res[0], res[1], res[2], res[3])


func _headers(with_key: bool, json_body: bool) -> PackedStringArray:
	var h := PackedStringArray([client_header(), "Accept: application/json"])
	if with_key:
		h.append("Authorization: Bearer " + api_key.strip_edges())
	if json_body:
		h.append("Content-Type: application/json")
	return h


# ---------------------------------------------------------------- parsing

## X-PFX-Client header, e.g. "X-PFX-Client: godot-addon/0.1.0 (Godot 4.3.0)".
static func client_header() -> String:
	return "X-PFX-Client: godot-addon/%s (Godot %s)" % [ADDON_VERSION, engine_version()]


static func engine_version() -> String:
	var v := Engine.get_version_info()
	return "%d.%d.%d" % [v.major, v.minor, v.patch]


static func build_query(params: Dictionary) -> String:
	var parts := PackedStringArray()
	for k in params:
		var v = params[k]
		if v == null or (typeof(v) == TYPE_STRING and v == ""):
			continue
		parts.append("%s=%s" % [String(k).uri_encode(), str(v).uri_encode()])
	return "" if parts.is_empty() else "?" + "&".join(parts)


static func build_import_body(effect_id: String, target_dir: String, scale: float, normal_map: bool, layout: String) -> Dictionary:
	var body := {"effect_id": effect_id, "engine": "godot", "scale": scale, "normal_map": normal_map, "layout": layout, "target_dir": target_dir}
	# JSON.stringify writes 1.0 as 1; keep whole scales as ints for clarity.
	if is_equal_approx(scale, roundf(scale)):
		body.scale = int(roundf(scale))
	return body


## Turns an HTTPRequest result into the common result Dictionary.
static func parse_response(result: int, status: int, headers: PackedStringArray, body: PackedByteArray) -> Dictionary:
	if result != HTTPRequest.RESULT_SUCCESS:
		return error_result(0, "network", "network", network_message(result), "")
	if status >= 200 and status < 300:
		return {"ok": true, "status": status, "kind": "ok", "error": "", "message": "", "upgrade_url": "", "data": {}, "body": body, "headers": headers}
	var info = parse_json(body)
	var code := ""
	var desc := ""
	var upgrade := ""
	if typeof(info) == TYPE_DICTIONARY:
		code = _s(info.get("error"))
		desc = _s(info.get("error_description"))
		upgrade = _s(info.get("upgrade_url"))
	var kind := "error"
	var fallback := "The server returned an error (HTTP %d). Please try again later." % status
	match status:
		401:
			kind = "invalid_token"
			fallback = "Your API key was not accepted. Paste a new key from particlefx.studio/billing."
		402:
			kind = "payment"
			fallback = "This needs a paid plan."
			if upgrade == "":
				upgrade = PRICING_URL
		403:
			kind = "payment" if upgrade != "" else "error"
			fallback = "Your account cannot use this effect."
		404:
			kind = "not_found"
			fallback = "That effect was not found. It may have been removed."
		429:
			kind = "rate_limited"
			fallback = "Too many requests right now. Please try again in a minute."
		503:
			kind = "unavailable"
			fallback = "ParticleFX Studio is busy right now. Please try again later."
		400:
			kind = "bad_request"
			fallback = "The request was not accepted."
	if status >= 500 and status != 503:
		kind = "unavailable"
	var message := desc if desc != "" else fallback
	if kind == "rate_limited":
		var retry := header_value(headers, "retry-after")
		if retry.is_valid_int() and int(retry) > 0:
			message += " (retry in %d s)" % int(retry)
	var r := error_result(status, kind, code if code != "" else kind, message, upgrade)
	r.headers = headers
	return r


static func error_result(status: int, kind: String, code: String, message: String, upgrade_url: String) -> Dictionary:
	return {"ok": false, "status": status, "kind": kind, "error": code, "message": message, "upgrade_url": upgrade_url, "data": {}, "body": PackedByteArray(), "headers": PackedStringArray()}


static func network_message(result: int) -> String:
	match result:
		HTTPRequest.RESULT_CANT_RESOLVE:
			return "Could not find the ParticleFX Studio server. Check your internet connection."
		HTTPRequest.RESULT_CANT_CONNECT, HTTPRequest.RESULT_CONNECTION_ERROR:
			return "Could not connect to ParticleFX Studio. Check your internet connection."
		HTTPRequest.RESULT_TLS_HANDSHAKE_ERROR:
			return "A secure connection to ParticleFX Studio could not be made."
		HTTPRequest.RESULT_TIMEOUT:
			return "The request timed out. Please try again."
		HTTPRequest.RESULT_BODY_SIZE_LIMIT_EXCEEDED:
			return "The download was larger than expected and was stopped."
	return "The request failed (network error %d). Please try again." % result


static func parse_json(body: PackedByteArray):
	var json := JSON.new()
	if json.parse(body.get_string_from_utf8()) != OK:
		return null
	return json.data


## GET /v1/plugin/me -> { plan, has_plan, exports_left_today, exports_per_day,
## pricing_url, site_url }. The two counts are -1 when uncapped (null).
static func parse_me(d: Dictionary) -> Dictionary:
	return {
		"plan": _s(d.get("plan"), "free"),
		"has_plan": _b(d.get("has_plan")),
		"exports_left_today": _count(d.get("exports_left_today")),
		"exports_per_day": _count(d.get("exports_per_day")),
		"pricing_url": _s(d.get("pricing_url"), PRICING_URL),
		"site_url": _s(d.get("site_url"), SITE_URL),
	}


static func parse_library(d: Dictionary) -> Dictionary:
	var items: Array = []
	var raw = d.get("items")
	if typeof(raw) == TYPE_ARRAY:
		for it in raw:
			if typeof(it) != TYPE_DICTIONARY or _s(it.get("id")) == "":
				continue
			var id := _s(it.get("id"))
			var slug := _s(it.get("slug"), id.get_file())
			items.append({
				"id": id,
				"slug": slug,
				"title": _s(it.get("title"), slug),
				"category": _s(it.get("category")),
				"tags": _strings(it.get("tags")),
				"description": _s(it.get("description")),
				"duration": float(it.get("duration")) if typeof(it.get("duration")) in [TYPE_INT, TYPE_FLOAT] else 0.0,
				"loop": _b(it.get("loop")),
				"pixel_art": _b(it.get("pixel_art")),
				"premium": _b(it.get("premium")),
				"poster": _s(it.get("poster")),
				"page": _s(it.get("page")),
				"own": false,
			})
	var total = d.get("total")
	return {
		"total": int(total) if typeof(total) in [TYPE_INT, TYPE_FLOAT] else items.size(),
		"categories": _strings(d.get("categories")),
		"items": items,
	}


static func parse_my_effects(d: Dictionary) -> Dictionary:
	var items: Array = []
	var raw = d.get("items")
	if typeof(raw) == TYPE_ARRAY:
		for it in raw:
			if typeof(it) != TYPE_DICTIONARY or _s(it.get("id")) == "":
				continue
			var title := _s(it.get("title"), "Untitled effect")
			items.append({
				"id": _s(it.get("id")),
				"slug": "",
				"title": title,
				"category": "",
				"tags": PackedStringArray(),
				"description": "",
				"updated_at": _s(it.get("updated_at")),
				"poster": _s(it.get("poster")),
				"page": "",
				"premium": false,
				"own": true,
			})
	var total = d.get("total")
	return {"total": int(total) if typeof(total) in [TYPE_INT, TYPE_FLOAT] else items.size(), "categories": PackedStringArray(), "items": items}


## X-PFX-Exports-Left: "<n>" or "unlimited". Returns n, -1 for unlimited, -2 when missing.
static func parse_exports_left(value: String) -> int:
	var v := value.strip_edges().to_lower()
	if v == "unlimited":
		return -1
	if v.is_valid_int():
		return maxi(0, int(v))
	return -2


## filename from Content-Disposition (plain or RFC 5987 filename*), "" when absent.
static func filename_from_disposition(value: String) -> String:
	var re := RegEx.new()
	re.compile("filename\\*=(?:UTF-8'')?\"?([^\";]+)\"?")
	var m := re.search(value)
	if m:
		return m.get_string(1).uri_decode().get_file()
	re.compile("filename=\"?([^\";]+)\"?")
	m = re.search(value)
	if m:
		return m.get_string(1).strip_edges().get_file()
	return ""


static func header_value(headers: PackedStringArray, name: String) -> String:
	var want := name.to_lower() + ":"
	for h in headers:
		if h.to_lower().begins_with(want):
			return h.substr(want.length()).strip_edges()
	return ""


static func looks_like_zip(body: PackedByteArray) -> bool:
	return body.size() >= 4 and body[0] == 0x50 and body[1] == 0x4B and body[2] == 0x03 and body[3] == 0x04


static func decode_image(bytes: PackedByteArray) -> Image:
	if bytes.size() < 12:
		return null
	var img := Image.new()
	var err := ERR_FILE_UNRECOGNIZED
	# RIFF....WEBP
	if bytes[0] == 0x52 and bytes[1] == 0x49 and bytes[8] == 0x57 and bytes[9] == 0x45:
		err = img.load_webp_from_buffer(bytes)
	elif bytes[0] == 0x89 and bytes[1] == 0x50:
		err = img.load_png_from_buffer(bytes)
	elif bytes[0] == 0xFF and bytes[1] == 0xD8:
		err = img.load_jpg_from_buffer(bytes)
	return img if err == OK and not img.is_empty() else null


## "Plan: Free. 2 of 3 imports left today." and so on.
static func account_line(me: Dictionary) -> String:
	var plan := plan_label(me.get("plan", "free"))
	var left: int = me.get("exports_left_today", -1)
	var per_day: int = me.get("exports_per_day", -1)
	if left < 0:
		return "Plan: %s. Unlimited imports." % plan
	if per_day > 0:
		return "Plan: %s. %d of %d imports left today." % [plan, left, per_day]
	return "Plan: %s. %d imports left today." % [plan, left]


static func plan_label(plan: String) -> String:
	match plan:
		"free":
			return "Free"
		"starter":
			return "Starter"
		"creator":
			return "Creator"
		"studio":
			return "Studio"
		"studio_max":
			return "Studio Max"
	return plan.capitalize() if plan != "" else "Free"


static func _s(v, fallback := "") -> String:
	return String(v) if typeof(v) == TYPE_STRING and String(v) != "" else fallback


static func _b(v) -> bool:
	return typeof(v) == TYPE_BOOL and v


static func _count(v) -> int:
	if typeof(v) == TYPE_INT or typeof(v) == TYPE_FLOAT:
		return maxi(0, int(v))
	return -1


static func _strings(v) -> PackedStringArray:
	var out := PackedStringArray()
	if typeof(v) == TYPE_ARRAY:
		for s in v:
			if typeof(s) == TYPE_STRING and s != "":
				out.append(s)
	return out
