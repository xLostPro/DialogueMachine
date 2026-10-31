@tool
class_name DialogueStory
extends Resource

@export_multiline var handoff_json: String = ""
@export var sha: String = ""
@export var project_name: String = ""

var _cached_handoff: Dictionary = {}

func get_handoff() -> Dictionary:
	if not _cached_handoff.is_empty():
		return _cached_handoff
	if handoff_json.is_empty():
		return {}
	var parsed = JSON.parse_string(handoff_json)
	if parsed is Dictionary:
		_cached_handoff = parsed
	return _cached_handoff
