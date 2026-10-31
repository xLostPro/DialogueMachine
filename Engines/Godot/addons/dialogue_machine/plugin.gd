@tool
extends EditorPlugin

var _importer = null
var _dock = null

func _enter_tree():
	var importer_script = load("res://addons/dialogue_machine/importer/runtime_json_importer.gd")
	if importer_script:
		_importer = importer_script.new()
		add_import_plugin(_importer)

	var dock_script = load("res://addons/dialogue_machine/livelink/live_link_dock.gd")
	if dock_script:
		_dock = dock_script.new()
		_dock.name = "Dialogue Machine"
		add_control_to_dock(DOCK_SLOT_RIGHT_UL, _dock)

func _exit_tree():
	if _importer:
		remove_import_plugin(_importer)
		_importer = null

	if _dock:
		remove_control_from_docks(_dock)
		_dock.free()
		_dock = null
