@tool
class_name DialogueRunner
extends Node

const DialogueRules = preload("dialogue_rules.gd")
const DialogueStory = preload("dialogue_story.gd")

signal line_shown(line: String)
signal choices_shown(choices: Array)
signal action_raised(type: String, target_id: String, parameter: Variant, amount: int)
signal shop_opened(shop_id: String)
signal variable_changed(key: String, before: Variant, after: Variant)
signal ended(scene_id: Variant, choice_id: Variant)
signal warning(message: String)

@export var story: Resource = null
@export var auto_start: bool = false
@export var start_scene_id: String = ""
@export var override_seed: bool = false
@export var custom_seed: int = 0
@export var live_link: bool = true
@export var variables_provider: Node = null

var is_ended: bool = true
var page_index: int = 0
var current_metadata: Dictionary = {}

var current_scene_id: Variant:
	get:
		return _scene.get("State", null) if not _scene.is_empty() else null

var speaker: String:
	get:
		return current_metadata.get("speaker", "Character") if not _scene.is_empty() else ""

var title: String:
	get:
		return current_metadata.get("title", str(current_scene_id)) if not _scene.is_empty() else ""

var current_page: Variant:
	get:
		if _scene.is_empty() or _pages.is_empty():
			return null
		var idx = clampi(page_index, 0, _pages.size() - 1)
		return _pages[idx]

var current_page_plain: Variant:
	get:
		var cp = current_page
		return DialogueRules.plain_text(cp) if cp != null else null

var page_count: int:
	get:
		return _pages.size() if not _scene.is_empty() else 0

var can_continue: bool:
	get:
		if _scene.is_empty():
			return false
		if page_index < _pages.size() - 1:
			return true
		var def_next_raw = _scene.get("DefaultNextId", null)
		var def_next = str(def_next_raw).strip_edges() if def_next_raw != null else ""
		if def_next.is_empty():
			return false
		for c in get_choices():
			if c.get("enabled", false):
				return false
		return true

class DialogueVariables extends RefCounted:
	var _numeric: Dictionary = {}
	var _text: Dictionary = {}
	var _provider: Object = null
	var runner_ref = null

	func _init(provider: Object = null, runner = null):
		_provider = provider
		runner_ref = runner

	func set_provider(provider: Object) -> void:
		_provider = provider

	func get_number(num_key: String) -> int:
		if _provider and _provider.has_method("get_number"):
			return int(_provider.get_number(num_key))
		return int(_numeric.get(num_key, 0))

	func set_number(num_key: String, val: int) -> void:
		var clamped = DialogueRules.clamp_number(num_key, val)
		var before = get_number(num_key)
		var had = has_number(num_key)
		if _provider and _provider.has_method("set_number"):
			_provider.set_number(num_key, clamped)
		else:
			_numeric[num_key] = clamped
		var after = get_number(num_key)
		if runner_ref and (before != after or not had):
			runner_ref.variable_changed.emit(num_key, before, after)
			runner_ref.refresh_page()

	func has_number(num_key: String) -> bool:
		if _provider and _provider.has_method("has_number"):
			return bool(_provider.has_number(num_key))
		return _numeric.has(num_key)

	func get_text(text_key: String) -> Variant:
		if _provider and _provider.has_method("get_text"):
			return _provider.get_text(text_key)
		return _text.get(text_key, null)

	func set_text(text_key: String, val: String) -> void:
		var before = get_text(text_key)
		if _provider and _provider.has_method("set_text"):
			_provider.set_text(text_key, val)
		else:
			_text[text_key] = val
		var after = get_text(text_key)
		if runner_ref and before != after:
			runner_ref.variable_changed.emit(text_key, before, after)
			runner_ref.refresh_page()

	func has_text(text_key: String) -> bool:
		if _provider and _provider.has_method("has_text"):
			return bool(_provider.has_text(text_key))
		return _text.has(text_key)

	func get_keys() -> Array[String]:
		var keys_dict: Dictionary = {}
		for k in _numeric.keys():
			keys_dict[str(k)] = true
		for k in _text.keys():
			keys_dict[str(k)] = true
		if _provider and _provider.has_method("get_keys"):
			var p_keys = _provider.get_keys()
			if p_keys is Array:
				for k in p_keys:
					keys_dict[str(k)] = true
		var res: Array[String] = []
		for k in keys_dict.keys():
			res.append(str(k))
		res.sort()
		return res

	func all_numeric() -> Dictionary:
		var res: Dictionary = _numeric.duplicate()
		if _provider and _provider.has_method("get_all_numeric"):
			var p_num = _provider.get_all_numeric()
			if p_num is Dictionary:
				for k in p_num.keys():
					res[str(k)] = int(p_num[k])
		return res

	func all_text() -> Dictionary:
		var res: Dictionary = _text.duplicate()
		if _provider and _provider.has_method("get_all_text"):
			var p_txt = _provider.get_all_text()
			if p_txt is Dictionary:
				for k in p_txt.keys():
					res[str(k)] = str(p_txt[k])
		return res

	func clear() -> void:
		_numeric.clear()
		_text.clear()

var variables: DialogueVariables = null

var _handoff: Dictionary = {}
var _scenes_by_id: Dictionary = {}
var _scene: Dictionary = {}
var _pages: Array[String] = []
var _random_state: int = 0
var _random_line: int = -1
var _story_sha: String = ""
var _project_name: String = ""

# Live Link state
var _ws: WebSocketPeer = null
var _ws_token: String = ""
var _ws_connected: bool = false
var _ws_retry_time: float = 0.0
var _last_sent_state: Dictionary = {}

func _init():
	variables = DialogueVariables.new(variables_provider, self)

func _ready():
	if variables_provider:
		variables.set_provider(variables_provider)
	if story:
		load_story(story)
		if auto_start and not Engine.is_editor_hint():
			start(start_scene_id, custom_seed)

func _process(delta: float):
	if Engine.is_editor_hint() or not live_link:
		return
	if not OS.is_debug_build():
		return
	_poll_live_link(delta)

func load_story(source: Variant) -> void:
	var handoff_dict: Dictionary = {}
	if source is DialogueStory:
		story = source
		handoff_dict = source.get_handoff()
		_story_sha = source.sha
		_project_name = source.project_name
	elif source is String:
		var parsed = JSON.parse_string(source)
		if parsed is Dictionary:
			handoff_dict = parsed
	elif source is Dictionary:
		handoff_dict = source

	if handoff_dict.is_empty():
		push_error("DialogueRunner.load_story: empty or invalid story payload")
		return

	var schema = int(handoff_dict.get("SchemaVersion", 1))
	if schema > 1:
		push_error("DialogueRunner: unsupported schema version %d" % schema)
		return

	_handoff = handoff_dict
	_story_sha = str(handoff_dict.get("StorySha256", _story_sha))
	_project_name = str(handoff_dict.get("ProjectName", _project_name))

	var story_raw = handoff_dict.get("StoryJson", "")
	var story_dict: Dictionary = {}
	if story_raw is String and not story_raw.is_empty():
		var parsed = JSON.parse_string(story_raw)
		if parsed is Dictionary:
			story_dict = parsed
	elif story_raw is Dictionary:
		story_dict = story_raw

	_scenes_by_id.clear()
	var list = story_dict.get("ListOfCharacterStates", [])
	if list is Array:
		for s in list:
			if s is Dictionary:
				var state_id = str(s.get("State", ""))
				if not state_id.is_empty():
					_scenes_by_id[state_id] = s

func start(scene_id: String = "", seed_override: Variant = null, numeric_overrides: Dictionary = {}, text_overrides: Dictionary = {}) -> void:
	if _scenes_by_id.is_empty():
		if story:
			load_story(story)
		if _scenes_by_id.is_empty():
			push_error("DialogueRunner.start: no story loaded")
			return

	variables.clear()
	var num_vals = _handoff.get("NumericValues", {})
	if num_vals is Dictionary:
		for k in num_vals.keys():
			var val = num_vals[k]
			if val is int or val is float:
				variables.set_number(str(k), int(val))

	var txt_vals = _handoff.get("TextValues", {})
	if txt_vals is Dictionary:
		for k in txt_vals.keys():
			variables.set_text(str(k), str(txt_vals[k]))

	for k in numeric_overrides.keys():
		variables.set_number(str(k), int(numeric_overrides[k]))

	for k in text_overrides.keys():
		variables.set_text(str(k), str(text_overrides[k]))

	var chosen_seed: int = 0
	if seed_override != null:
		chosen_seed = int(seed_override)
	elif override_seed:
		chosen_seed = custom_seed
	else:
		chosen_seed = int(_handoff.get("Seed", 0))

	_random_state = DialogueRules.seed_state(chosen_seed)
	_random_line = -1

	var target_scene = scene_id
	if target_scene.is_empty():
		target_scene = str(_handoff.get("StartingScene", ""))
	if target_scene.is_empty() and not _scenes_by_id.is_empty():
		target_scene = _scenes_by_id.keys()[0]

	if not _scenes_by_id.has(target_scene):
		push_error("DialogueRunner.start: missing scene '%s'" % target_scene)
		return

	_enter(target_scene, "started")

func get_choices() -> Array[Dictionary]:
	var result: Array[Dictionary] = []
	if _scene.is_empty():
		return result
	var numbers = variables.all_numeric()
	var texts = variables.all_text()
	for c in _scene.get("Choices", []):
		if not (c is Dictionary):
			continue
		var btn_text = str(c.get("ButtonText", ""))
		var resolved_btn = DialogueRules.resolve_text(btn_text, numbers, texts)
		var visibility = int(c.get("Visibility", 0))
		var req_res = DialogueRules.requirements_met(c.get("Requirements", []), numbers)
		var met = req_res[0] or visibility == 2
		if not met and visibility == 0:
			continue
		result.append({
			"id": str(c.get("InternalId", "")),
			"text": resolved_btn,
			"enabled": met,
			"disabled_reason": null if met else req_res[1]
		})
	return result

func continue_dialogue() -> void:
	if page_index < _pages.size() - 1:
		page_index += 1
		_deliver()
		_send_live_link_state("step", null)
	elif not _scene.is_empty():
		var def_next_raw = _scene.get("DefaultNextId", null)
		var def_next = str(def_next_raw).strip_edges() if def_next_raw != null else ""
		_move(def_next, null)

func choose(choice_id: String) -> void:
	if _scene.is_empty():
		push_warning("DialogueRunner.choose: conversation has ended")
		return
	if page_index < _pages.size() - 1:
		push_error("DialogueRunner.choose: choosing refused before reaching last page")
		return

	var found_choice = null
	for c in _scene.get("Choices", []):
		if str(c.get("InternalId", "")).nocasecmp_to(choice_id) == 0:
			found_choice = c
			break

	if not found_choice:
		push_error("DialogueRunner.choose: unknown choice ID: %s" % choice_id)
		return

	var visibility = int(found_choice.get("Visibility", 0))
	var req_res = DialogueRules.requirements_met(found_choice.get("Requirements", []), variables.all_numeric())
	if visibility != 2 and not req_res[0]:
		push_error("DialogueRunner.choose: cannot pick disabled choice: %s (%s)" % [choice_id, req_res[1]])
		return

	var next_id_raw = found_choice.get("NextStateId", null)
	var next_id = str(next_id_raw).strip_edges() if next_id_raw != null else ""
	if next_id.is_empty():
		var def_next_raw = _scene.get("DefaultNextId", null)
		next_id = str(def_next_raw).strip_edges() if def_next_raw != null else ""

	_move(next_id, found_choice)

func replace_story(new_story: Variant) -> void:
	var prev_scene_id = current_scene_id
	var prev_page = page_index
	load_story(new_story)

	if is_ended or prev_scene_id == null:
		return

	if not _scenes_by_id.has(str(prev_scene_id)):
		warning.emit("Hot reload: current scene '%s' no longer exists in story" % str(prev_scene_id))
		_end(prev_scene_id, null)
		return

	_scene = _scenes_by_id[str(prev_scene_id)]
	_rebuild_pages()
	page_index = clampi(prev_page, 0, _pages.size() - 1)
	_deliver()

func save_state() -> String:
	var data: Dictionary = {
		"sceneId": current_scene_id,
		"pageIndex": page_index,
		"randomState": DialogueRules.u64_to_string(_random_state),
		"randomLine": _random_line,
		"numeric": variables.all_numeric(),
		"text": variables.all_text(),
		"storySha256": _story_sha
	}
	return JSON.stringify(data)

func restore_state(json_str: String) -> void:
	if _handoff.is_empty():
		push_error("Load a story before restoring a save.")
		return
	var parsed = JSON.parse_string(json_str)
	if not (parsed is Dictionary):
		push_error("A save state must be a JSON object.")
		return

	var target_scene_val = parsed.get("sceneId", null)
	var target_scene = str(target_scene_val).strip_edges() if target_scene_val != null else null
	if target_scene != null and not target_scene.is_empty() and not _is_exit(target_scene):
		if not _scenes_by_id.has(target_scene):
			push_error("Scene '%s' does not exist." % target_scene)
			return

	variables.clear()
	var num_vals = parsed.get("numeric", {})
	if num_vals is Dictionary:
		for k in num_vals.keys():
			variables.set_number(str(k), int(num_vals[k]))
	var txt_vals = parsed.get("text", {})
	if txt_vals is Dictionary:
		for k in txt_vals.keys():
			variables.set_text(str(k), str(txt_vals[k]))

	_random_state = DialogueRules.string_to_u64(str(parsed.get("randomState", "0")))
	_random_line = int(parsed.get("randomLine", -1))
	var saved_page = int(parsed.get("pageIndex", 0))

	if target_scene == null or target_scene.is_empty() or _is_exit(target_scene):
		_scene = {}
		_pages = []
		page_index = 0
		is_ended = true
	else:
		_scene = _scenes_by_id[target_scene]
		is_ended = false
		page_index = saved_page
		current_metadata.clear()
		var meta = _scene.get("Metadata", {})
		if meta is Dictionary:
			for k in meta.keys():
				current_metadata[str(k)] = str(meta[k])
		_rebuild_pages()
		_deliver()

func refresh_page() -> void:
	if _scene.is_empty():
		return
	var prev = current_page
	_rebuild_pages()
	if prev != current_page and current_page != null:
		line_shown.emit(current_page)

func _rebuild_pages() -> void:
	var raw_text = str(_scene.get("Text", ""))
	var resolved_text = DialogueRules.resolve_text(raw_text, variables.all_numeric(), variables.all_text())
	var say_it_all = bool(_scene.get("SayItAll", true))

	var rstate_ref = [_random_state]
	var rline_ref = [_random_line]
	_pages = DialogueRules.build_pages(resolved_text, say_it_all, rstate_ref, rline_ref)
	_random_state = rstate_ref[0]
	_random_line = rline_ref[0]
	page_index = clampi(page_index, 0, maxi(0, _pages.size() - 1))

func _deliver() -> void:
	if current_page != null:
		line_shown.emit(current_page)
	if page_index < _pages.size() - 1:
		return
	var choices = get_choices()
	if choices.size() > 0:
		choices_shown.emit(choices)

func _is_exit(id: Variant) -> bool:
	if id == null:
		return true
	var s = str(id).strip_edges()
	return s.is_empty() or s == DialogueRules.EXIT

func _move(dest_id: Variant, choice_dict: Variant) -> void:
	if not _is_exit(dest_id) and not _scenes_by_id.has(str(dest_id)):
		push_error("Scene '%s' does not exist. No actions were applied." % str(dest_id))
		return

	if choice_dict != null:
		_apply_actions(choice_dict.get("AfterChoiceActions", []))
	_apply_actions(_scene.get("DefaultAfterActions", []))

	var choice_id = str(choice_dict.get("InternalId", "")) if choice_dict != null else null
	var prev_scene_id = _scene.get("State", null)
	if _is_exit(dest_id):
		_end(prev_scene_id, choice_id)
	else:
		_enter(str(dest_id), "step", choice_id)

func _enter(id: String, status: String, choice_taken: Variant = null) -> void:
	if _is_exit(id):
		_end(id, null)
		return
	if not _scenes_by_id.has(id):
		warning.emit("Scene not found: " + id)
		_end(id, choice_taken)
		return

	_scene = _scenes_by_id[id]
	is_ended = false
	page_index = 0
	_random_line = -1

	current_metadata.clear()
	var meta = _scene.get("Metadata", {})
	if meta is Dictionary:
		for k in meta.keys():
			current_metadata[str(k)] = str(meta[k])

	_rebuild_pages()
	_deliver()

	if str(_scene.get("Type", "")) == "Shop":
		var shop_data = _scene.get("ShopData", {})
		var shop_id = ""
		if shop_data is Dictionary:
			shop_id = str(shop_data.get("ShopId", ""))
		shop_opened.emit(shop_id)
		_send_live_link_event("Shop", shop_id, null)

	_send_live_link_state(status, choice_taken)

func _end(scene_id: Variant, choice_id: Variant) -> void:
	_scene = {}
	_pages = []
	page_index = 0
	is_ended = true
	ended.emit(scene_id, choice_id)
	_send_live_link_state("ended", choice_id, scene_id)

func _apply_actions(actions: Array) -> void:
	for a in actions:
		if a is Dictionary:
			var type = str(a.get("Type", ""))
			var target_id = str(a.get("TargetId", ""))
			var amount = int(a.get("Amount", 0))
			var parameter = a.get("Parameter", null)

			if type == "Affinity" or type == "Inventory":
				var act_key = DialogueRules.key(type, target_id)
				var before_val = variables.get_number(act_key)
				var after_val = DialogueRules.apply_amount(type, before_val, amount)
				variables.set_number(act_key, after_val)
			elif type == "Event" or type == "Script":
				action_raised.emit(type, target_id, parameter, amount)
				_send_live_link_event(type, target_id, parameter)

# -----------------------------------------------------------------------------
# Live Link Client (Dev-only, lazy connect)
# -----------------------------------------------------------------------------

static func get_discovery_file_path() -> String:
	var os_name = OS.get_name()
	if os_name == "Windows":
		var local_app_data = OS.get_environment("LOCALAPPDATA")
		if local_app_data.is_empty():
			local_app_data = OS.get_environment("USERPROFILE") + "/AppData/Local"
		return local_app_data.replace("\\", "/") + "/DialogueMachine/livelink.json"
	elif os_name == "macOS":
		var home = OS.get_environment("HOME")
		return home + "/Library/Application Support/DialogueMachine/livelink.json"
	else:
		var xdg = OS.get_environment("XDG_DATA_HOME")
		if xdg.is_empty():
			var home = OS.get_environment("HOME")
			xdg = home + "/.local/share"
		return xdg + "/DialogueMachine/livelink.json"

func _poll_live_link(delta: float) -> void:
	if not _ws:
		_ws_retry_time -= delta
		if _ws_retry_time <= 0.0:
			_ws_retry_time = 10.0
			_try_connect_live_link()
		return

	_ws.poll()
	var state = _ws.get_ready_state()
	if state == WebSocketPeer.STATE_OPEN:
		if not _ws_connected:
			_ws_connected = true
			_send_live_link_hello()
			if not _last_sent_state.is_empty():
				_send_frame("runtime.state", _last_sent_state)
		while _ws.get_available_packet_count() > 0:
			var packet = _ws.get_packet()
			_handle_live_link_packet(packet.get_string_from_utf8())
	elif state == WebSocketPeer.STATE_CLOSED or state == WebSocketPeer.STATE_CLOSING:
		_ws = null
		_ws_connected = false
		_ws_retry_time = 10.0

func _try_connect_live_link() -> void:
	var path = get_discovery_file_path()
	if not FileAccess.file_exists(path):
		return
	var file = FileAccess.open(path, FileAccess.READ)
	if not file:
		return
	var content = file.get_as_text()
	var parsed = JSON.parse_string(content)
	if not (parsed is Dictionary):
		return

	var port = int(parsed.get("port", 47615))
	_ws_token = str(parsed.get("token", ""))
	if _ws_token.is_empty():
		return

	_ws = WebSocketPeer.new()
	var url = "ws://127.0.0.1:%d/dialogue-machine/" % port
	var err = _ws.connect_to_url(url)
	if err != OK:
		_ws = null

func _send_frame(type: String, payload: Dictionary) -> void:
	if not _ws or _ws.get_ready_state() != WebSocketPeer.STATE_OPEN:
		return
	var frame = {
		"protocol": 1,
		"type": type,
		"payload": payload
	}
	_ws.send_text(JSON.stringify(frame))

func _send_live_link_hello() -> void:
	var payload = {
		"token": _ws_token,
		"engine": "godot",
		"engineVersion": Engine.get_version_info().get("string", "4.3"),
		"pluginVersion": "1.3.0",
		"protocol": 1,
		"schemaVersion": 1
	}
	_send_frame("hello", payload)

func _handle_live_link_packet(text: String) -> void:
	var parsed = JSON.parse_string(text)
	if not (parsed is Dictionary):
		return
	var type = str(parsed.get("type", ""))
	var payload = parsed.get("payload", {})
	if not (payload is Dictionary):
		payload = {}

	match type:
		"ping":
			_send_frame("pong", {})
		"story.updated":
			replace_story(payload)
		"play.from":
			var target_scene = str(payload.get("sceneId", ""))
			var seed_val = payload.get("seed", null)
			var num_overrides = payload.get("numeric", {})
			var txt_overrides = payload.get("text", {})
			start(target_scene, seed_val, num_overrides if num_overrides is Dictionary else {}, txt_overrides if txt_overrides is Dictionary else {})
		"runtime.setVariable":
			var key = str(payload.get("key", ""))
			if payload.has("number"):
				variables.set_number(key, int(payload["number"]))
			elif payload.has("text"):
				variables.set_text(key, str(payload["text"]))

func _send_live_link_state(status: String, choice_taken: Variant, scene_override: Variant = null) -> void:
	if not live_link or not _ws or _ws.get_ready_state() != WebSocketPeer.STATE_OPEN:
		return
	var s_id = current_scene_id if scene_override == null else scene_override
	var runner_name = str(get_path()) if is_inside_tree() else name
	var state_payload = {
		"runnerId": str(get_instance_id()),
		"runnerName": runner_name,
		"projectName": _project_name,
		"storySha256": _story_sha,
		"status": status,
		"sceneId": s_id if s_id != null and not str(s_id).is_empty() else null,
		"pageIndex": page_index,
		"choiceId": choice_taken,
		"numeric": variables.all_numeric(),
		"text": variables.all_text()
	}
	_last_sent_state = state_payload
	_send_frame("runtime.state", state_payload)

func _send_live_link_event(type: String, target_id: String, parameter: Variant) -> void:
	if not live_link or not _ws or _ws.get_ready_state() != WebSocketPeer.STATE_OPEN:
		return
	var event_payload = {
		"runnerId": str(get_instance_id()),
		"type": type,
		"targetId": target_id,
		"parameter": parameter
	}
	_send_frame("runtime.event", event_payload)
