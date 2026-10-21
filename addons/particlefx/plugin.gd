@tool
extends EditorPlugin
## ParticleFX Studio add-on: adds the ParticleFX dock to the editor.

const Dock := preload("res://addons/particlefx/dock.gd")
const ICON_PATH := "res://addons/particlefx/icon.svg"

var dock: Control


func _enter_tree() -> void:
	dock = Dock.new()
	dock.name = "ParticleFX"
	add_control_to_dock(DOCK_SLOT_RIGHT_UL, dock)
	# Tab icons arrived in Godot 4.3.
	if has_method("set_dock_tab_icon") and ResourceLoader.exists(ICON_PATH):
		call("set_dock_tab_icon", dock, load(ICON_PATH))


func _exit_tree() -> void:
	if dock:
		remove_control_from_docks(dock)
		dock.queue_free()
		dock = null
