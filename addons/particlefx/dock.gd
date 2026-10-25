@tool
extends VBoxContainer
## The ParticleFX Studio dock: API key, account line, Library / My effects
## tabs with poster grids, import options, and "Import zip..." for bundles
## downloaded from the website.

const ApiClient := preload("res://addons/particlefx/api_client.gd")
const Importer := preload("res://addons/particlefx/importer.gd")

const PAGE_SIZE := 60
const DEFAULT_TARGET := "res://particlefx/"
const META_SECTION := "particlefx"
const META_TARGET := "target_dir"
const POSTER_SIZE := Vector2(104, 104)
const MAX_POSTER_DOWNLOADS := 4
const SCENE_WAIT_SECONDS := 60.0

## Emitted when an import has finished (ok or not). result is the importer
## result plus "opened" (whether the scene was opened in the editor).
signal import_finished(result: Dictionary)
## Emitted after a library or my-effects page has been shown.
signal list_loaded(tab: String)

var api: Node
var me := {}
var busy := false

var _key_edit: LineEdit
var _account_label: Label
var _status_label: Label
var _plans_button: Button
var _tabs: TabContainer
var _search_edit: LineEdit
var _category_option: OptionButton
var _search_timer: Timer
var _target_edit: LineEdit
var _scale_spin: SpinBox
var _normal_check: CheckBox
var _layout_option: OptionButton
var _plan_hint: Label
var _zip_dialog: EditorFileDialog
var _dir_dialog: EditorFileDialog

var _lists := {} # tab -> { grid, more_button, count_label, items, total, serial, loaded }
var _categories := PackedStringArray()
var _poster_cache := {} # url -> Texture2D (or null when it failed)
var _poster_queue: Array = [] # [url, TextureRect]
var _poster_waiting := {} # url -> Array[TextureRect]
var _poster_active := 0
var _upgrade_url := ApiClient.PRICING_URL
## Editor display scale (2 on most Retina and 4K setups): fixed sizes are multiplied by it, as the editor's own fonts are.
var _ui_scale := 1.0


func _ready() -> void:
	if Engine.is_editor_hint() and Engine.has_singleton("EditorInterface"):
		_ui_scale = maxf(1.0, Engine.get_singleton("EditorInterface").get_editor_scale())
	name = "ParticleFX"
	api = ApiClient.new()
	api.name = "ApiClient"
	add_child(api)
	_register_settings()
	_build_ui()
	_apply_settings()
	_update_plan_controls()
	# Fetch after the editor has finished starting up.
	_refresh_all.call_deferred()


# ---------------------------------------------------------------- settings

func _register_settings() -> void:
	var es := EditorInterface.get_editor_settings()
	if not es.has_setting(ApiClient.SETTING_API_KEY):
		es.set_setting(ApiClient.SETTING_API_KEY, "")
	es.set_initial_value(ApiClient.SETTING_API_KEY, "", false)
	es.add_property_info({"name": ApiClient.SETTING_API_KEY, "type": TYPE_STRING, "hint": PROPERTY_HINT_PASSWORD})
	if not es.has_setting(ApiClient.SETTING_API_BASE):
		es.set_setting(ApiClient.SETTING_API_BASE, ApiClient.DEFAULT_API_BASE)
	es.set_initial_value(ApiClient.SETTING_API_BASE, ApiClient.DEFAULT_API_BASE, false)


func _apply_settings() -> void:
	var es := EditorInterface.get_editor_settings()
	var key := String(es.get_setting(ApiClient.SETTING_API_KEY)).strip_edges()
	var base := String(es.get_setting(ApiClient.SETTING_API_BASE)).strip_edges()
	# Environment overrides for development and automated tests (never saved).
	if OS.get_environment("PFX_API_BASE") != "":
		base = OS.get_environment("PFX_API_BASE")
	if OS.get_environment("PFX_API_KEY") != "":
		key = OS.get_environment("PFX_API_KEY")
	api.api_base = (base if base != "" else ApiClient.DEFAULT_API_BASE).trim_suffix("/")
	api.api_key = key
	_key_edit.text = key


func _save_key() -> void:
	var key := _key_edit.text.strip_edges()
	EditorInterface.get_editor_settings().set_setting(ApiClient.SETTING_API_KEY, key)
	api.api_key = key
	_set_status("API key saved." if key != "" else "API key removed.")
	_refresh_all()


func _target_root() -> String:
	var t := _target_edit.text.strip_edges()
	return t if t != "" else DEFAULT_TARGET


func _on_target_changed(text: String) -> void:
	EditorInterface.get_editor_settings().set_project_metadata(META_SECTION, META_TARGET, text.strip_edges())


# ---------------------------------------------------------------- UI

func _build_ui() -> void:
	add_theme_constant_override("separation", 6)

	# API key row
	var key_row := HBoxContainer.new()
	add_child(key_row)
	var key_label := Label.new()
	key_label.text = "API key"
	key_row.add_child(key_label)
	_key_edit = LineEdit.new()
	_key_edit.secret = true
	_key_edit.placeholder_text = "pfx_live_..."
	_key_edit.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_key_edit.tooltip_text = "Stored in your Editor Settings, never in the project."
	_key_edit.text_submitted.connect(func(_t): _save_key())
	key_row.add_child(_key_edit)
	var save_btn := Button.new()
	save_btn.text = "Save"
	save_btn.pressed.connect(_save_key)
	key_row.add_child(save_btn)

	var acct_row := HBoxContainer.new()
	add_child(acct_row)
	_account_label = Label.new()
	_account_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_account_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	acct_row.add_child(_account_label)
	var get_key := Button.new()
	get_key.text = "Get an API key"
	get_key.tooltip_text = ApiClient.BILLING_URL
	get_key.pressed.connect(func(): OS.shell_open(ApiClient.BILLING_URL))
	acct_row.add_child(get_key)

	var status_row := HBoxContainer.new()
	add_child(status_row)
	_status_label = Label.new()
	_status_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_status_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	status_row.add_child(_status_label)
	_plans_button = Button.new()
	_plans_button.text = "See plans"
	_plans_button.visible = false
	_plans_button.pressed.connect(func(): OS.shell_open(_upgrade_url))
	status_row.add_child(_plans_button)

	# Tabs
	_tabs = TabContainer.new()
	_tabs.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_tabs.custom_minimum_size = Vector2(0, 260) * _ui_scale
	add_child(_tabs)
	_tabs.tab_changed.connect(_on_tab_changed)

	var lib := VBoxContainer.new()
	lib.name = "Library"
	_tabs.add_child(lib)
	var filter_row := HBoxContainer.new()
	lib.add_child(filter_row)
	_search_edit = LineEdit.new()
	_search_edit.placeholder_text = "Search effects"
	_search_edit.clear_button_enabled = true
	_search_edit.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_search_edit.text_changed.connect(func(_t): _search_timer.start())
	_search_edit.text_submitted.connect(func(_t):
		_search_timer.stop()
		_load_list("library", false))
	filter_row.add_child(_search_edit)
	_category_option = OptionButton.new()
	_category_option.add_item("All categories")
	_category_option.item_selected.connect(func(_i): _load_list("library", false))
	_category_option.fit_to_longest_item = false
	_category_option.custom_minimum_size = Vector2(130, 0) * _ui_scale
	filter_row.add_child(_category_option)
	_search_timer = Timer.new()
	_search_timer.one_shot = true
	_search_timer.wait_time = 0.4
	_search_timer.timeout.connect(func(): _load_list("library", false))
	add_child(_search_timer)
	_lists["library"] = _build_list(lib)

	var mine := VBoxContainer.new()
	mine.name = "My effects"
	_tabs.add_child(mine)
	_lists["mine"] = _build_list(mine)

	# Options
	add_child(HSeparator.new())
	var target_row := HBoxContainer.new()
	add_child(target_row)
	var tl := Label.new()
	tl.text = "Target folder"
	target_row.add_child(tl)
	_target_edit = LineEdit.new()
	_target_edit.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_target_edit.text = String(EditorInterface.get_editor_settings().get_project_metadata(META_SECTION, META_TARGET, DEFAULT_TARGET))
	_target_edit.tooltip_text = "Each effect goes into its own subfolder, e.g. res://particlefx/fireball."
	_target_edit.text_changed.connect(_on_target_changed)
	target_row.add_child(_target_edit)
	var browse := Button.new()
	browse.text = "Browse..."
	browse.pressed.connect(func(): _dir_dialog.popup_file_dialog())
	target_row.add_child(browse)

	var opt_row := HFlowContainer.new()
	add_child(opt_row)
	var sl := Label.new()
	sl.text = "Scale"
	opt_row.add_child(sl)
	_scale_spin = SpinBox.new()
	_scale_spin.min_value = 0.5
	_scale_spin.max_value = 4.0
	_scale_spin.step = 0.5
	_scale_spin.value = 1.0
	_scale_spin.suffix = "x"
	opt_row.add_child(_scale_spin)
	_normal_check = CheckBox.new()
	_normal_check.text = "Normal map"
	opt_row.add_child(_normal_check)
	var ll := Label.new()
	ll.text = "Layout"
	opt_row.add_child(ll)
	_layout_option = OptionButton.new()
	_layout_option.add_item("Grid")
	_layout_option.add_item("Packed")
	opt_row.add_child(_layout_option)
	_plan_hint = Label.new()
	_plan_hint.text = "Scale, normal maps and packed layout need a paid plan."
	_plan_hint.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_plan_hint.add_theme_color_override("font_color", get_theme_color("font_disabled_color", "Editor"))
	add_child(_plan_hint)

	var bottom := HBoxContainer.new()
	add_child(bottom)
	var zip_btn := Button.new()
	zip_btn.text = "Import zip..."
	zip_btn.tooltip_text = "Import a bundle you downloaded from particlefx.studio. No account needed."
	zip_btn.pressed.connect(func(): _zip_dialog.popup_file_dialog())
	bottom.add_child(zip_btn)
	var spacer := Control.new()
	spacer.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	bottom.add_child(spacer)
	var refresh := Button.new()
	refresh.text = "Refresh"
	refresh.pressed.connect(_refresh_all)
	bottom.add_child(refresh)

	_zip_dialog = EditorFileDialog.new()
	_zip_dialog.file_mode = EditorFileDialog.FILE_MODE_OPEN_FILE
	_zip_dialog.access = EditorFileDialog.ACCESS_FILESYSTEM
	_zip_dialog.title = "Import a ParticleFX Studio zip"
	_zip_dialog.add_filter("*.zip", "ParticleFX Studio bundle")
	_zip_dialog.file_selected.connect(func(path): import_zip_file(path))
	add_child(_zip_dialog)

	_dir_dialog = EditorFileDialog.new()
	_dir_dialog.file_mode = EditorFileDialog.FILE_MODE_OPEN_DIR
	_dir_dialog.access = EditorFileDialog.ACCESS_RESOURCES
	_dir_dialog.title = "Choose the target folder"
	_dir_dialog.dir_selected.connect(func(path):
		_target_edit.text = path
		_on_target_changed(path))
	add_child(_dir_dialog)


func _build_list(parent: Control) -> Dictionary:
	var count := Label.new()
	count.add_theme_color_override("font_color", get_theme_color("font_disabled_color", "Editor"))
	parent.add_child(count)
	var scroll := ScrollContainer.new()
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	parent.add_child(scroll)
	var inner := VBoxContainer.new()
	inner.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	scroll.add_child(inner)
	var grid := HFlowContainer.new()
	grid.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	inner.add_child(grid)
	var more := Button.new()
	more.text = "Load more"
	more.visible = false
	inner.add_child(more)
	var tab := "library" if parent.name == "Library" else "mine"
	more.pressed.connect(func(): _load_list(tab, true))
	return {"grid": grid, "more": more, "count": count, "items": [], "total": 0, "serial": 0, "loaded": false}


func _make_card(item: Dictionary) -> Control:
	var card := PanelContainer.new()
	var poster_size := POSTER_SIZE * _ui_scale
	card.custom_minimum_size = Vector2(poster_size.x + 12 * _ui_scale, 0)
	card.tooltip_text = _card_tooltip(item)
	var box := VBoxContainer.new()
	card.add_child(box)
	var poster := TextureRect.new()
	poster.custom_minimum_size = poster_size
	poster.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	poster.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	poster.mouse_filter = Control.MOUSE_FILTER_PASS
	box.add_child(poster)
	var title := Label.new()
	title.text = item.title
	title.clip_text = true
	title.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
	title.custom_minimum_size = Vector2(poster_size.x, 0)
	box.add_child(title)
	var sub := Label.new()
	sub.text = _card_subtitle(item)
	sub.clip_text = true
	sub.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
	sub.custom_minimum_size = Vector2(poster_size.x, 0)
	sub.add_theme_color_override("font_color", get_theme_color("font_disabled_color", "Editor"))
	sub.add_theme_font_size_override("font_size", maxi(10, get_theme_font_size("font_size", "Label") - 2))
	box.add_child(sub)
	var btn := Button.new()
	btn.text = "Import"
	btn.add_to_group("particlefx_import_buttons")
	btn.disabled = busy
	btn.pressed.connect(func(): import_item(item))
	box.add_child(btn)
	_request_poster(String(item.poster), poster)
	return card


func _card_subtitle(item: Dictionary) -> String:
	if item.own:
		return String(item.get("updated_at", "")).substr(0, 10)
	var parts := PackedStringArray()
	if item.category != "":
		parts.append(item.category)
	if item.premium:
		parts.append("FX pack")
	return ", ".join(parts)


func _card_tooltip(item: Dictionary) -> String:
	var t: String = item.title
	if String(item.description) != "":
		t += "\n" + String(item.description)
	if not item.own and item.get("duration", 0.0) > 0.0:
		t += "\n%.1f s%s" % [item.duration, ", loops" if item.get("loop", false) else ""]
	return t


func _set_status(text: String, is_error := false, upgrade_url := "") -> void:
	_status_label.text = text
	if is_error:
		_status_label.add_theme_color_override("font_color", get_theme_color("error_color", "Editor"))
	else:
		_status_label.remove_theme_color_override("font_color")
	_plans_button.visible = upgrade_url != ""
	if upgrade_url != "":
		_upgrade_url = upgrade_url


func _show_error(r: Dictionary) -> void:
	var upgrade := String(r.get("upgrade_url", "")) if r.kind == "payment" else ""
	_set_status(String(r.message), true, upgrade)
	if r.kind == "invalid_token" and api.api_key != "":
		_account_label.text = "Your API key was not accepted. Paste a new one and press Save."
		_key_edit.grab_focus.call_deferred()
		_key_edit.select_all()


func _update_plan_controls() -> void:
	var has_plan: bool = me.get("has_plan", false)
	for c in [_scale_spin, _normal_check, _layout_option]:
		if c is SpinBox:
			c.editable = has_plan
		else:
			c.disabled = not has_plan
		c.tooltip_text = "" if has_plan else "Needs a paid plan."
	if not has_plan:
		_scale_spin.value = 1.0
		_normal_check.button_pressed = false
		_layout_option.select(0)
	_plan_hint.visible = not has_plan


func _set_busy(value: bool) -> void:
	busy = value
	for b in get_tree().get_nodes_in_group("particlefx_import_buttons"):
		if is_ancestor_of(b):
			b.disabled = value


# ---------------------------------------------------------------- data

func _refresh_all() -> void:
	_refresh_account()
	_load_list("library", false)
	_lists["mine"].loaded = false
	if _tabs.current_tab == 1:
		_load_list("mine", false)


func _on_tab_changed(index: int) -> void:
	if index == 1 and not _lists["mine"].loaded:
		_load_list("mine", false)


func _refresh_account() -> void:
	if api.api_key == "":
		me = {}
		_account_label.text = "Not signed in. Browsing is free; importing needs an API key."
		_update_plan_controls()
		return
	_account_label.text = "Checking your account..."
	var r: Dictionary = await api.get_me()
	if not r.ok:
		me = {}
		_account_label.text = "Could not load your account."
		_show_error(r)
	else:
		me = r.data
		_account_label.text = ApiClient.account_line(me)
	_update_plan_controls()


func _load_list(tab: String, append: bool) -> void:
	var L: Dictionary = _lists[tab]
	L.serial += 1
	var serial: int = L.serial
	L.loaded = true
	var offset: int = L.items.size() if append else 0
	if not append:
		for c in L.grid.get_children():
			c.queue_free()
		L.items = []
		L.more.visible = false
	if tab == "mine" and api.api_key == "":
		L.count.text = "Add your API key to see the effects you saved or made with an agent."
		return
	L.count.text = "Loading..."
	var r: Dictionary
	if tab == "library":
		var cat := ""
		if _category_option.selected > 0:
			cat = _category_option.get_item_text(_category_option.selected)
		r = await api.get_library(_search_edit.text.strip_edges(), cat, offset, PAGE_SIZE)
	else:
		r = await api.get_my_effects(offset, PAGE_SIZE)
	if serial != L.serial:
		return # a newer request replaced this one
	if not r.ok:
		L.count.text = String(r.message)
		if r.kind == "payment":
			_set_status(String(r.message), true, String(r.upgrade_url))
		elif r.kind == "invalid_token" and tab == "mine":
			_show_error(r)
		return
	var data: Dictionary = r.data
	if tab == "library" and not data.categories.is_empty() and data.categories != _categories:
		_set_categories(data.categories)
	L.total = data.total
	for item in data.items:
		L.items.append(item)
		L.grid.add_child(_make_card(item))
	if L.items.is_empty():
		L.count.text = "No effects found." if tab == "library" else "You have no saved effects yet. Make one at particlefx.studio."
	else:
		L.count.text = "Showing %d of %d" % [L.items.size(), L.total]
	L.more.visible = L.items.size() < L.total and not data.items.is_empty()
	list_loaded.emit(tab)


func _set_categories(cats: PackedStringArray) -> void:
	var current := ""
	if _category_option.selected > 0:
		current = _category_option.get_item_text(_category_option.selected)
	_categories = cats
	_category_option.clear()
	_category_option.add_item("All categories")
	for c in cats:
		_category_option.add_item(c)
		if c == current:
			_category_option.select(_category_option.item_count - 1)


# ---------------------------------------------------------------- posters

func _request_poster(url: String, rect: TextureRect) -> void:
	if url == "":
		return
	if _poster_cache.has(url):
		rect.texture = _poster_cache[url]
		return
	if _poster_waiting.has(url):
		_poster_waiting[url].append(rect)
		return
	_poster_waiting[url] = [rect]
	_poster_queue.append(url)
	_pump_posters()


func _pump_posters() -> void:
	while _poster_active < MAX_POSTER_DOWNLOADS and not _poster_queue.is_empty():
		var url: String = _poster_queue.pop_front()
		_poster_active += 1
		_download_poster(url)


func _download_poster(url: String) -> void:
	var tex: Texture2D = await api.fetch_poster(url)
	_poster_active -= 1
	_poster_cache[url] = tex
	for rect in _poster_waiting.get(url, []):
		if is_instance_valid(rect):
			rect.texture = tex
	_poster_waiting.erase(url)
	_pump_posters()


# ---------------------------------------------------------------- import

## Imports a library or own effect through the API.
func import_item(item: Dictionary) -> void:
	if busy:
		return
	if api.api_key == "":
		_set_status("Paste your API key and press Save to import. Get one from particlefx.studio/billing.", true)
		_key_edit.grab_focus()
		return
	var slug: String = item.slug if String(item.get("slug", "")) != "" else Importer.slugify(item.title)
	var check := Importer.normalize_target_dir(_target_root().path_join(slug))
	if check.error != "":
		_set_status(check.error, true)
		return
	var target: String = check.path
	var has_plan: bool = me.get("has_plan", false)
	var scale := _scale_spin.value if has_plan else 1.0
	var normal := _normal_check.button_pressed and has_plan
	var layout := "packed" if has_plan and _layout_option.selected == 1 else "grid"
	_set_busy(true)
	_set_status("Rendering %s on ParticleFX Studio..." % item.title)
	var r: Dictionary = await api.import_effect(item.id, target, scale, normal, layout)
	if not r.ok:
		_set_busy(false)
		_show_error(r)
		import_finished.emit({"ok": false, "error": r.message, "kind": r.kind})
		return
	var left: int = r.data.exports_left
	if left != -2 and not me.is_empty():
		me.exports_left_today = left
		_account_label.text = ApiClient.account_line(me)
	var zip_path := _temp_zip_path()
	var f := FileAccess.open(zip_path, FileAccess.WRITE)
	if f == null:
		_set_busy(false)
		_set_status("Could not save the download to %s." % zip_path, true)
		import_finished.emit({"ok": false, "error": "write"})
		return
	f.store_buffer(r.body)
	f.close()
	await _import_and_open(zip_path, target)
	DirAccess.remove_absolute(zip_path)


## Imports a zip from disk (the offline path). The folder is named after the
## effect's slug inside the target folder.
func import_zip_file(zip_path: String) -> void:
	if busy:
		return
	var slug := _slug_from_zip(zip_path)
	var check := Importer.normalize_target_dir(_target_root().path_join(slug))
	if check.error != "":
		_set_status(check.error, true)
		return
	_set_busy(true)
	await _import_and_open(zip_path, check.path)


func _import_and_open(zip_path: String, target: String) -> void:
	_set_status("Unpacking into %s..." % target)
	var res := Importer.import_zip(zip_path, target)
	if not res.ok:
		_set_busy(false)
		_set_status(res.error, true)
		res.opened = false
		import_finished.emit(res)
		return
	_set_status("Importing %s..." % res.manifest.title)
	var opened := await _rescan_and_open(res)
	res.opened = opened
	_set_busy(false)
	var note := ""
	if String(res.manifest.license) != "":
		note = " " + String(res.manifest.license)
	if opened:
		_set_status("Imported %s into %s.%s" % [res.manifest.title, target, note])
	else:
		_set_status("Imported %s into %s. Open %s once the editor has finished importing.%s" % [res.manifest.title, target, res.scene_path.get_file(), note])
	import_finished.emit(res)


## Rescans, waits until the sheet PNG has been imported, then opens the scene.
func _rescan_and_open(res: Dictionary) -> bool:
	var efs := EditorInterface.get_resource_filesystem()
	# Wait for any scan already running (e.g. at editor start-up).
	var waited := 0.0
	while efs.is_scanning() and waited < SCENE_WAIT_SECONDS:
		await get_tree().create_timer(0.2).timeout
		waited += 0.2
	efs.scan()
	await get_tree().process_frame
	waited = 0.0
	var done := false
	while waited < SCENE_WAIT_SECONDS:
		if not efs.is_scanning() and _is_imported(efs, res.image_path) and efs.get_file_type(res.scene_path) != "":
			done = true
			break
		await get_tree().create_timer(0.2).timeout
		waited += 0.2
	EditorInterface.select_file(res.scene_path)
	if not done:
		return false
	# Already open (a re-import): reload it from disk, then switch to its tab.
	if EditorInterface.get_open_scenes().has(res.scene_path):
		EditorInterface.reload_scene_from_path(res.scene_path)
	EditorInterface.open_scene_from_path(res.scene_path)
	EditorInterface.set_main_screen_editor("2D")
	return true


func _is_imported(efs: EditorFileSystem, path: String) -> bool:
	return efs.get_file_type(path) != "" and FileAccess.file_exists(path + ".import")


func _temp_zip_path() -> String:
	var dir := EditorInterface.get_editor_paths().get_cache_dir()
	return dir.path_join("particlefx_import_%d.zip" % Time.get_ticks_msec())


## Slug from the zip's manifest, else from the file name.
func _slug_from_zip(zip_path: String) -> String:
	var slug := ""
	var reader := ZIPReader.new()
	if reader.open(zip_path) == OK:
		for entry in reader.get_files():
			if entry.get_file() == Importer.MANIFEST_NAME:
				var parsed := Importer.parse_manifest(reader.read_file(entry).get_string_from_utf8())
				if parsed.error == "":
					slug = Importer.slugify(parsed.manifest.slug)
				break
		reader.close()
	if slug != "":
		return slug
	return Importer.slugify(zip_path.get_file().get_basename())
