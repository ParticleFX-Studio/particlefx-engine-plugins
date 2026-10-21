@tool
extends RefCounted
## Unpacks a ParticleFX Studio bundle (zip) into a res:// folder.
##
## The zip holds <base>.png, optionally <base>_n.png, particlefx.json and,
## for Godot bundles, <base>_frames.tres and <base>.tscn. Resource paths in
## the .tres/.tscn are rewritten to the chosen folder, so a zip can be
## imported anywhere (the offline "Import zip..." path). When the zip has no
## Godot files (an older or engine-neutral bundle), they are generated from
## the manifest.
##
## No editor APIs here: the dock rescans and opens the scene afterwards, and
## the tests call this directly.

const MANIFEST_NAME := "particlefx.json"
const ALLOWED_EXTENSIONS := ["png", "json", "tres", "tscn", "txt", "md"]
const MAX_ENTRY_BYTES := 64 * 1024 * 1024

const BLEND_MODE_ADD := 1 # CanvasItemMaterial.BLEND_MODE_ADD
const TEXTURE_FILTER_NEAREST := 1 # CanvasItem.TEXTURE_FILTER_NEAREST


## Imports the zip at zip_path into target_dir (a res:// folder).
## Returns { ok, error, manifest, target_dir, files, image_path,
## normal_map_path, frames_path, scene_path, generated }.
static func import_zip(zip_path: String, target_dir: String) -> Dictionary:
	var result := {
		"ok": false, "error": "", "manifest": {}, "target_dir": "", "files": PackedStringArray(),
		"image_path": "", "normal_map_path": "", "frames_path": "", "scene_path": "", "generated": false,
	}
	var dir_check := normalize_target_dir(target_dir)
	if dir_check.error != "":
		result.error = dir_check.error
		return result
	var dir: String = dir_check.path
	result.target_dir = dir

	var reader := ZIPReader.new()
	if reader.open(zip_path) != OK:
		result.error = "Could not open the zip file: %s" % zip_path
		return result

	# Collect the entries we accept, keyed by their file name.
	var entries := {}
	var manifest_entry := ""
	for entry in reader.get_files():
		var name := safe_entry_name(entry)
		if name == "":
			continue
		entries[name] = entry
		if name == MANIFEST_NAME:
			manifest_entry = entry
	if manifest_entry == "":
		reader.close()
		result.error = "This zip has no particlefx.json, so it is not a ParticleFX Studio bundle."
		return result

	var parsed := parse_manifest(reader.read_file(manifest_entry).get_string_from_utf8())
	if parsed.error != "":
		reader.close()
		result.error = parsed.error
		return result
	var manifest: Dictionary = parsed.manifest
	result.manifest = manifest
	if not entries.has(manifest.image):
		reader.close()
		result.error = "The zip is missing the sprite sheet %s." % manifest.image
		return result

	var abs_dir := ProjectSettings.globalize_path(dir)
	if DirAccess.make_dir_recursive_absolute(abs_dir) != OK:
		reader.close()
		result.error = "Could not create the folder %s." % dir
		return result

	var names_in_zip := PackedStringArray(entries.keys())
	var files := PackedStringArray()
	for name in entries:
		var bytes := reader.read_file(entries[name])
		if bytes.size() > MAX_ENTRY_BYTES:
			continue
		var ext := String(name).get_extension().to_lower()
		if ext == "tres" or ext == "tscn":
			bytes = fix_resource_paths(bytes.get_string_from_utf8(), dir, names_in_zip).to_utf8_buffer()
		var res_path := dir.path_join(name)
		var err := write_bytes(res_path, bytes)
		if err != "":
			reader.close()
			result.error = err
			return result
		files.append(res_path)
		var lower := String(name).to_lower()
		if ext == "tscn" and result.scene_path == "":
			result.scene_path = res_path
		elif ext == "tres" and (lower.ends_with("_frames.tres") or result.frames_path == ""):
			result.frames_path = res_path
	reader.close()

	result.image_path = dir.path_join(manifest.image)
	if manifest.normal_map != "" and entries.has(manifest.normal_map):
		result.normal_map_path = dir.path_join(manifest.normal_map)

	# Engine-neutral bundle: build the SpriteFrames and the scene ourselves.
	if result.frames_path == "":
		result.frames_path = dir.path_join("%s_frames.tres" % manifest.base)
		var err := write_bytes(result.frames_path, build_sprite_frames(manifest, result.image_path, result.normal_map_path).to_utf8_buffer())
		if err != "":
			result.error = err
			return result
		files.append(result.frames_path)
		result.generated = true
	if result.scene_path == "":
		result.scene_path = dir.path_join("%s.tscn" % manifest.base)
		var err := write_bytes(result.scene_path, build_scene(manifest, result.frames_path).to_utf8_buffer())
		if err != "":
			result.error = err
			return result
		files.append(result.scene_path)
		result.generated = true

	result.files = files
	result.ok = true
	return result


## Checks a target folder. Returns { path, error } with the trailing slash removed.
static func normalize_target_dir(target_dir: String) -> Dictionary:
	var path := target_dir.strip_edges().replace("\\", "/")
	if not path.begins_with("res://"):
		return {"path": "", "error": "The target folder must be inside the project (start with res://)."}
	while path.ends_with("/") and path.length() > "res://".length():
		path = path.substr(0, path.length() - 1)
	if path == "res://" or path == "res:/":
		return {"path": "", "error": "Choose a folder inside the project, not the project root."}
	for part in path.substr("res://".length()).split("/"):
		if part == ".." or part == ".":
			return {"path": "", "error": "The target folder cannot contain . or .. segments."}
		if part == ".godot":
			return {"path": "", "error": "The target folder cannot be inside .godot."}
	return {"path": path, "error": ""}


## Returns the plain file name for a zip entry, or "" when the entry must be
## skipped (folders, hidden or macOS metadata files, unknown extensions).
## Folder parts are dropped and entries with .. or an absolute path are
## skipped, so nothing can be written outside the target folder.
static func safe_entry_name(entry: String) -> String:
	var e := entry.replace("\\", "/")
	if e.ends_with("/") or e.begins_with("__MACOSX/") or e.contains("/__MACOSX/"):
		return ""
	if e.begins_with("/") or e.split("/").has(".."):
		return ""
	var name := e.get_file()
	if name == "" or name.begins_with("."):
		return ""
	if not ALLOWED_EXTENSIONS.has(name.get_extension().to_lower()):
		return ""
	if not name.is_valid_filename():
		return ""
	return name


## Parses and validates particlefx.json. Returns { manifest, error } where
## manifest has every field the importer uses, with sane types.
static func parse_manifest(text: String) -> Dictionary:
	var json := JSON.new()
	if json.parse(text) != OK or typeof(json.data) != TYPE_DICTIONARY:
		return {"manifest": {}, "error": "particlefx.json is not valid JSON."}
	var d: Dictionary = json.data
	var fmt := int(_num(d.get("format"), 0))
	if fmt < 1:
		return {"manifest": {}, "error": "particlefx.json has no format version."}
	if fmt > 1:
		return {"manifest": {}, "error": "This bundle uses format %d. Please update the ParticleFX Studio add-on." % fmt}

	var m := {}
	m.format = fmt
	m.image = _str(d.get("image"), "").get_file()
	if m.image == "" or m.image.get_extension().to_lower() != "png":
		return {"manifest": {}, "error": "particlefx.json does not name a PNG sprite sheet."}
	m.base = _str(d.get("base"), m.image.get_basename())
	m.slug = _str(d.get("slug"), m.base)
	m.id = _str(d.get("id"), m.slug)
	m.title = _str(d.get("title"), m.slug)
	m.normal_map = _str(d.get("normal_map"), "").get_file()
	m.frame_width = int(_num(d.get("frame_width"), 0))
	m.frame_height = int(_num(d.get("frame_height"), 0))
	m.columns = int(_num(d.get("columns"), 0))
	m.rows = int(_num(d.get("rows"), 0))
	if m.frame_width <= 0 or m.frame_height <= 0:
		return {"manifest": {}, "error": "particlefx.json has no frame size."}
	m.fps = float(_num(d.get("fps"), 0.0))
	if m.fps <= 0.0:
		m.fps = 30.0
	m.loop = _bool(d.get("loop"), true)
	m.duration = float(_num(d.get("duration"), 0.0))
	var pivot = d.get("pivot")
	m.pivot_x = 0.5
	m.pivot_y = 0.5
	if typeof(pivot) == TYPE_DICTIONARY:
		m.pivot_x = clampf(float(_num(pivot.get("x"), 0.5)), 0.0, 1.0)
		m.pivot_y = clampf(float(_num(pivot.get("y"), 0.5)), 0.0, 1.0)
	m.blend = "add" if _str(d.get("blend"), "normal") == "add" else "normal"
	m.pixel_art = _bool(d.get("pixel_art"), false)
	m.pixels_per_unit = float(_num(d.get("pixels_per_unit"), 100.0))
	m.source_url = _str(d.get("source_url"), "")
	m.license = _str(d.get("license"), "")

	var frames: Array = []
	var raw_frames = d.get("frames")
	if typeof(raw_frames) == TYPE_ARRAY:
		for f in raw_frames:
			if typeof(f) != TYPE_DICTIONARY:
				return {"manifest": {}, "error": "particlefx.json has a malformed frame."}
			var fr := {
				"x": int(_num(f.get("x"), 0)), "y": int(_num(f.get("y"), 0)),
				"w": int(_num(f.get("w"), m.frame_width)), "h": int(_num(f.get("h"), m.frame_height)),
				"offset_x": int(_num(f.get("offset_x"), 0)), "offset_y": int(_num(f.get("offset_y"), 0)),
			}
			if fr.w <= 0 or fr.h <= 0:
				return {"manifest": {}, "error": "particlefx.json has a frame with no size."}
			frames.append(fr)
	elif m.columns > 0 and m.rows > 0:
		# No frame list: assume a full grid in reading order.
		for r in m.rows:
			for c in m.columns:
				frames.append({"x": c * m.frame_width, "y": r * m.frame_height, "w": m.frame_width, "h": m.frame_height, "offset_x": 0, "offset_y": 0})
	if frames.is_empty():
		return {"manifest": {}, "error": "particlefx.json lists no frames."}
	m.frames = frames
	return {"manifest": m, "error": ""}


## Points every res:// path in a .tres/.tscn whose file is in the zip at the
## target folder, and drops uid="uid://..." attributes (those were made for
## another project; Godot assigns fresh ones).
static func fix_resource_paths(text: String, target_dir: String, names_in_zip: PackedStringArray) -> String:
	var re := RegEx.new()
	re.compile("path=\"(res://[^\"]*)\"")
	var out := ""
	var last := 0
	for m in re.search_all(text):
		var file: String = m.get_string(1).get_file()
		out += text.substr(last, m.get_start() - last)
		if names_in_zip.has(file):
			out += "path=\"%s\"" % target_dir.path_join(file)
		else:
			out += m.get_string()
		last = m.get_end()
	out += text.substr(last)
	var uid := RegEx.new()
	uid.compile(" uid=\"uid://[^\"]*\"")
	return uid.sub(out, "", true)


## SpriteFrames resource text for the manifest (one AtlasTexture per unique
## rect). With a normal map the atlas is a CanvasTexture (diffuse + normal) so
## 2D lights use it.
static func build_sprite_frames(m: Dictionary, image_path: String, normal_path := "") -> String:
	var anim := animation_name(m)
	var atlas_ref := "SubResource(\"CanvasTexture_lit\")" if normal_path != "" else "ExtResource(\"1_sheet\")"
	var keys := {}
	var subs := PackedStringArray()
	var order := PackedStringArray()
	for f in m.frames:
		var key := "%d,%d,%d,%d,%d,%d" % [f.x, f.y, f.w, f.h, f.offset_x, f.offset_y]
		if not keys.has(key):
			var id := "AtlasTexture_%d" % keys.size()
			keys[key] = id
			var s := "[sub_resource type=\"AtlasTexture\" id=\"%s\"]\natlas = %s\nregion = Rect2(%d, %d, %d, %d)\nfilter_clip = true\n" % [id, atlas_ref, f.x, f.y, f.w, f.h]
			var trimmed: bool = f.w != m.frame_width or f.h != m.frame_height or f.offset_x != 0 or f.offset_y != 0
			if trimmed:
				# The margin puts a trimmed rect back at its place in the full frame.
				s += "margin = Rect2(%d, %d, %d, %d)\n" % [f.offset_x, f.offset_y, m.frame_width - f.w, m.frame_height - f.h]
			subs.append(s)
		order.append("{\n\"duration\": 1.0,\n\"texture\": SubResource(\"%s\")\n}" % keys[key])
	var steps := keys.size() + (4 if normal_path != "" else 2)
	var t := "[gd_resource type=\"SpriteFrames\" load_steps=%d format=3]\n\n" % steps
	t += "[ext_resource type=\"Texture2D\" path=\"%s\" id=\"1_sheet\"]\n" % image_path
	if normal_path != "":
		t += "[ext_resource type=\"Texture2D\" path=\"%s\" id=\"2_normal\"]\n\n" % normal_path
		t += "[sub_resource type=\"CanvasTexture\" id=\"CanvasTexture_lit\"]\ndiffuse_texture = ExtResource(\"1_sheet\")\nnormal_texture = ExtResource(\"2_normal\")\n"
	t += "\n"
	t += "\n".join(subs) + "\n"
	t += "[resource]\nanimations = [{\n\"frames\": [%s],\n\"loop\": %s,\n\"name\": &\"%s\",\n\"speed\": %s\n}]\n" % [
		", ".join(order), "true" if m.loop else "false", anim.c_escape(), _float_text(m.fps)]
	return t


## Scene text: a root AnimatedSprite2D with the pivot at the node origin.
static func build_scene(m: Dictionary, frames_path: String) -> String:
	var anim := animation_name(m).c_escape()
	var additive: bool = m.blend == "add"
	var t := "[gd_scene load_steps=%d format=3]\n\n" % (3 if additive else 2)
	t += "[ext_resource type=\"SpriteFrames\" path=\"%s\" id=\"1_frames\"]\n\n" % frames_path
	if additive:
		t += "[sub_resource type=\"CanvasItemMaterial\" id=\"CanvasItemMaterial_add\"]\nblend_mode = %d\n\n" % BLEND_MODE_ADD
	t += "[node name=\"%s\" type=\"AnimatedSprite2D\"]\n" % node_name(m.title).c_escape()
	if m.pixel_art:
		t += "texture_filter = %d\n" % TEXTURE_FILTER_NEAREST
	if additive:
		t += "material = SubResource(\"CanvasItemMaterial_add\")\n"
	t += "sprite_frames = ExtResource(\"1_frames\")\n"
	t += "animation = &\"%s\"\n" % anim
	t += "autoplay = \"%s\"\n" % anim
	var off := pivot_offset(m)
	if off != Vector2.ZERO:
		t += "offset = Vector2(%s, %s)\n" % [_float_text(off.x), _float_text(off.y)]
	return t


## Offset for a centered sprite that puts the manifest pivot at the node origin.
static func pivot_offset(m: Dictionary) -> Vector2:
	return Vector2(m.frame_width * (0.5 - m.pivot_x), m.frame_height * (0.5 - m.pivot_y))


static func animation_name(m: Dictionary) -> String:
	var n := String(m.base).strip_edges()
	return n if n != "" else "default"


## A valid node name from an effect title.
static func node_name(title: String) -> String:
	var n := title.strip_edges()
	for c in [".", ":", "@", "/", "\"", "%", "\\", "\n", "\r", "\t"]:
		n = n.replace(c, "")
	n = n.strip_edges()
	return n if n != "" else "ParticleFX"


## A folder-safe slug ("Fire Ball!" -> "fire-ball").
static func slugify(text: String) -> String:
	var out := ""
	var dash := false
	for ch in text.to_lower():
		var ok: bool = (ch >= "a" and ch <= "z") or (ch >= "0" and ch <= "9") or ch == "_"
		if ok:
			out += ch
			dash = false
		elif not dash and out != "":
			out += "-"
			dash = true
	out = out.trim_suffix("-")
	return out if out != "" else "effect"


static func write_bytes(res_path: String, bytes: PackedByteArray) -> String:
	var f := FileAccess.open(res_path, FileAccess.WRITE)
	if f == null:
		return "Could not write %s (%s)." % [res_path, error_string(FileAccess.get_open_error())]
	f.store_buffer(bytes)
	f.close()
	return ""


static func _float_text(v: float) -> String:
	var s := String.num(v, 4)
	return s if s.contains(".") else s + ".0"


static func _num(v, fallback):
	if typeof(v) == TYPE_INT or typeof(v) == TYPE_FLOAT:
		return v
	return fallback


static func _str(v, fallback: String) -> String:
	if typeof(v) == TYPE_STRING and String(v) != "":
		return String(v)
	return fallback


static func _bool(v, fallback: bool) -> bool:
	if typeof(v) == TYPE_BOOL:
		return v
	return fallback
