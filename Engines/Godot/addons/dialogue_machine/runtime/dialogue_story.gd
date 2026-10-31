@tool
class_name DialogueStory
extends Resource

@export_multiline var handoff_json: String = ""
@export var sha: String = ""
@export var project_name: String = ""

var _cached_handoff: Dictionary = {}
var _cached_story: Dictionary = {}
var _cached_scenes_by_id: Dictionary = {}

func get_handoff() -> Dictionary:
	if not _cached_handoff.is_empty():
		return _cached_handoff
	if handoff_json.is_empty():
		return {}
	var parsed = JSON.parse_string(handoff_json)
	if parsed is Dictionary:
		_cached_handoff = parsed
	return _cached_handoff

func get_story() -> Dictionary:
	if not _cached_story.is_empty():
		return _cached_story
	var h = get_handoff()
	if h.is_empty():
		return {}
	var story_raw = h.get("StoryJson", "")
	if story_raw is String and not story_raw.is_empty():
		var parsed = JSON.parse_string(story_raw)
		if parsed is Dictionary:
			_cached_story = parsed
	elif story_raw is Dictionary:
		_cached_story = story_raw
	return _cached_story

func get_scenes_by_id() -> Dictionary:
	if not _cached_scenes_by_id.is_empty():
		return _cached_scenes_by_id
	var story = get_story()
	var list = story.get("ListOfCharacterStates", [])
	if list is Array:
		for scene in list:
			if scene is Dictionary:
				var state_id = str(scene.get("State", ""))
				if not state_id.is_empty():
					_cached_scenes_by_id[state_id] = scene
	return _cached_scenes_by_id

func find_scene(scene_id: String) -> Variant:
	var scenes = get_scenes_by_id()
	return scenes.get(scene_id, null)
