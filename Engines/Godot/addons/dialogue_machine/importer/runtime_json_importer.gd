@tool
extends EditorImportPlugin

const DialogueStory = preload("../runtime/dialogue_story.gd")

func _get_importer_name() -> String:
	return "dialogue_machine.runtime_json"

func _get_visible_name() -> String:
	return "Dialogue Machine Runtime Story"

func _get_recognized_extensions() -> PackedStringArray:
	return PackedStringArray(["runtime.json"])

func _get_save_extension() -> String:
	return "tres"

func _get_resource_type() -> String:
	return "DialogueStory"

func _get_preset_count() -> int:
	return 1

func _get_preset_name(_preset_index: int) -> String:
	return "Default"

func _get_import_options(_path: String, _preset_index: int) -> Array[Dictionary]:
	return []

func _get_option_visibility(_path: String, _option_name: StringName, _options: Dictionary) -> bool:
	return true

func _get_priority() -> float:
	return 2.0

func _get_import_order() -> int:
	return 0

func _import(source_file: String, save_path: String, _options: Dictionary, _platform_variants: Array[String], _gen_files: Array[String]) -> Error:
	if not source_file.ends_with(".runtime.json"):
		return ERR_FILE_UNRECOGNIZED

	var file = FileAccess.open(source_file, FileAccess.READ)
	if not file:
		printerr("DialogueMachine: Could not open source file: ", source_file)
		return FileAccess.get_open_error()

	var json_text = file.get_as_text()
	var parsed = JSON.parse_string(json_text)
	if not (parsed is Dictionary):
		printerr("DialogueMachine: Invalid JSON format in: ", source_file)
		return ERR_FILE_CORRUPT

	var contract = str(parsed.get("Contract", ""))
	if contract != "dialogue-machine.website.v1":
		return ERR_FILE_UNRECOGNIZED

	var schema_version = int(parsed.get("SchemaVersion", 1))
	if schema_version > 1:
		printerr("DialogueMachine: Unsupported SchemaVersion %d in %s. Please update the plugin." % [schema_version, source_file])
		return ERR_INVALID_DATA

	var story_res = DialogueStory.new()
	story_res.handoff_json = json_text
	story_res.sha = str(parsed.get("StorySha256", ""))
	var base_name = source_file.get_file()
	if base_name.ends_with(".runtime.json"):
		base_name = base_name.substr(0, base_name.length() - 13)
	story_res.project_name = str(parsed.get("ProjectName", base_name))

	var out_path = save_path + "." + _get_save_extension()
	var err = ResourceSaver.save(story_res, out_path)
	if err != OK:
		printerr("DialogueMachine: Failed to save DialogueStory resource to: ", out_path)
	return err
