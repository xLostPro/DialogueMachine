@tool
class_name DialogueLiveLinkDock
extends Control

var _status_label: Label = null
var _info_label: Label = null
var _diagnostics_label: Label = null
var _connect_btn: Button = null
var _update_btn: Button = null

var _ws: WebSocketPeer = null
var _ws_token: String = ""
var _ws_port: int = 47615
var _is_connected: bool = false
var _auto_reconnect: bool = true
var _retry_timer: float = 0.0
var _update_url: String = ""

func _ready():
	_build_ui()

func _build_ui():
	if _status_label:
		return

	var margin = MarginContainer.new()
	margin.anchor_left = 0.0
	margin.anchor_top = 0.0
	margin.anchor_right = 1.0
	margin.anchor_bottom = 1.0
	margin.add_theme_constant_override("margin_left", 8)
	margin.add_theme_constant_override("margin_top", 8)
	margin.add_theme_constant_override("margin_right", 8)
	margin.add_theme_constant_override("margin_bottom", 8)
	add_child(margin)

	var vbox = VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 6)
	margin.add_child(vbox)

	var header = Label.new()
	header.text = "Dialogue Machine Live Link"
	header.add_theme_font_size_override("font_size", 16)
	vbox.add_child(header)

	_status_label = Label.new()
	_status_label.text = "Status: Disconnected"
	vbox.add_child(_status_label)

	_info_label = Label.new()
	_info_label.text = "No connection."
	_info_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	vbox.add_child(_info_label)

	_diagnostics_label = Label.new()
	_diagnostics_label.text = ""
	_diagnostics_label.modulate = Color(1.0, 0.4, 0.4)
	_diagnostics_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	vbox.add_child(_diagnostics_label)

	var hbox = HBoxContainer.new()
	vbox.add_child(hbox)

	_connect_btn = Button.new()
	_connect_btn.text = "Connect"
	_connect_btn.pressed.connect(_on_connect_toggle)
	hbox.add_child(_connect_btn)

	_update_btn = Button.new()
	_update_btn.text = "Update Plugin"
	_update_btn.visible = false
	_update_btn.pressed.connect(_on_update_pressed)
	hbox.add_child(_update_btn)

func _process(delta: float):
	if not _auto_reconnect and not _ws:
		return

	if not _ws:
		_retry_timer -= delta
		if _retry_timer <= 0.0:
			_retry_timer = 5.0
			_start_connect()
		return

	_ws.poll()
	var state = _ws.get_ready_state()
	if state == WebSocketPeer.STATE_OPEN:
		if not _is_connected:
			_is_connected = true
			_status_label.text = "Status: Connected (port %d)" % _ws_port
			_connect_btn.text = "Disconnect"
			_send_hello()
		while _ws.get_available_packet_count() > 0:
			var packet = _ws.get_packet()
			_handle_packet(packet.get_string_from_utf8())
	elif state == WebSocketPeer.STATE_CLOSED or state == WebSocketPeer.STATE_CLOSING:
		_disconnect_socket()
		_status_label.text = "Status: Disconnected"
		_connect_btn.text = "Connect"
		_retry_timer = 5.0

func _start_connect():
	var disc_path = DialogueRunner.get_discovery_file_path()
	if not FileAccess.file_exists(disc_path):
		_status_label.text = "Status: Idle (Dialogue Machine not running)"
		return

	var file = FileAccess.open(disc_path, FileAccess.READ)
	if not file:
		return
	var parsed = JSON.parse_string(file.get_as_text())
	if not (parsed is Dictionary):
		return

	_ws_port = int(parsed.get("port", 47615))
	_ws_token = str(parsed.get("token", ""))
	if _ws_token.is_empty():
		return

	_status_label.text = "Status: Connecting to 127.0.0.1:%d..." % _ws_port
	_ws = WebSocketPeer.new()
	var err = _ws.connect_to_url("ws://127.0.0.1:%d/dialogue-machine/" % _ws_port)
	if err != OK:
		_disconnect_socket()

func _disconnect_socket():
	if _ws:
		_ws.close()
		_ws = null
	_is_connected = false

func _on_connect_toggle():
	if _ws or _is_connected:
		_auto_reconnect = false
		_disconnect_socket()
		_status_label.text = "Status: Disconnected"
		_connect_btn.text = "Connect"
	else:
		_auto_reconnect = true
		_start_connect()

func _on_update_pressed():
	if not _update_url.is_empty():
		OS.shell_open(_update_url)

func _send_frame(type: String, payload: Dictionary):
	if not _ws or _ws.get_ready_state() != WebSocketPeer.STATE_OPEN:
		return
	var frame = {
		"protocol": 1,
		"type": type,
		"payload": payload
	}
	_ws.send_text(JSON.stringify(frame))

func _send_hello():
	_send_frame("hello", DialogueRunner.build_hello(_ws_token))

func _handle_packet(text: String):
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
		"welcome":
			var app_ver = str(payload.get("appVersion", ""))
			var proj_name = str(payload.get("projectName", ""))
			_info_label.text = "Dialogue Machine %s · Story: %s" % [app_ver, proj_name]
			_diagnostics_label.text = ""
			_update_url = str(payload.get("updateUrl", ""))
			_update_btn.visible = not _update_url.is_empty()
		"story.invalid":
			var diags = payload.get("diagnostics", [])
			var msgs: Array[String] = []
			if diags is Array:
				for d in diags:
					if d is Dictionary:
						msgs.append("• " + str(d.get("message", "")))
			_diagnostics_label.text = "Story Validation Errors:\n" + "\n".join(msgs)
		"error":
			var code = str(payload.get("code", ""))
			var msg = str(payload.get("message", ""))
			_diagnostics_label.text = "Error (%s): %s" % [code, msg]
		"story.updated":
			_diagnostics_label.text = ""
			_handle_story_updated(payload)

# The export file name Dialogue Machine uses (RuntimeExport.Slug).
static func _slug(name: String) -> String:
	var out = ""
	var hyphen = false
	for ch in name.strip_edges():
		var c = ch.unicode_at(0)
		if (c >= 48 and c <= 57) or (c >= 65 and c <= 90) or (c >= 97 and c <= 122) or c > 127:
			out += ch.to_lower()
			hyphen = false
		elif ch == "-" or ch == "_":
			out += ch
			hyphen = false
		elif not hyphen and not out.is_empty():
			out += "-"
			hyphen = true
	out = out.lstrip("-_").rstrip("-_")
	return "story" if out.is_empty() else out

# JSON.parse_string reads every number as a float; whole numbers go back to ints so the file matches the export.
static func _intify(value: Variant) -> Variant:
	if value is Dictionary:
		var d: Dictionary = {}
		for k in value.keys():
			d[k] = _intify(value[k])
		return d
	if value is Array:
		var a: Array = []
		for v in value:
			a.append(_intify(v))
		return a
	if value is float and value == floor(value) and absf(value) < 9007199254740992.0:
		return int(value)
	return value

func _handle_story_updated(payload: Dictionary):
	var proj_name = str(payload.get("ProjectName", ""))
	var sha = str(payload.get("StorySha256", ""))
	_info_label.text = "Updated: %s (SHA: %s)" % [proj_name, sha.substr(0, 8)]

	# Write the pushed story next to Dialogue Machine's own export so builds match what was played.
	var target_file = "res://" + _slug(proj_name) + ".runtime.json"
	if FileAccess.file_exists(target_file):
		var existing = JSON.parse_string(FileAccess.get_file_as_string(target_file))
		if existing is Dictionary and str(existing.get("StorySha256", "")) == sha and not sha.is_empty():
			return

	var f = FileAccess.open(target_file, FileAccess.WRITE)
	if f:
		f.store_string(JSON.stringify(_intify(payload), "  "))
		f.close()
		if Engine.is_editor_hint():
			EditorInterface.get_resource_filesystem().scan()
