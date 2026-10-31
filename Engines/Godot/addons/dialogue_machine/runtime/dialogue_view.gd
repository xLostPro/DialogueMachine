@tool
class_name DialogueView
extends Control

const DialogueRules = preload("dialogue_rules.gd")
const DialogueRunner = preload("dialogue_runner.gd")

@export var runner: Node = null:
	set(value):
		if runner != value:
			_unbind_runner()
			runner = value
			if is_inside_tree() and runner:
				_bind_runner()

@export var auto_bind_parent: bool = true

var _panel: PanelContainer = null
var _speaker_label: Label = null
var _dialogue_label: RichTextLabel = null
var _choices_box: VBoxContainer = null
var _continue_button: Button = null
var _refresh_queued: bool = false

# Only [b] [i] [u] and [colour=X] are markup; any other bracket is shown as text, like Unity's rich text conversion.
static func to_bbcode(text: String) -> String:
	var result = ""
	var pos = 0
	var len_text = text.length()
	while pos < len_text:
		if text[pos] == "[":
			var close_bracket = text.find("]", pos + 1)
			if close_bracket != -1:
				var tag = text.substr(pos + 1, close_bracket - (pos + 1))
				var tag_lower = tag.to_lower()
				if tag_lower in ["b", "/b", "i", "/i", "u", "/u"]:
					result += "[" + tag_lower + "]"
					pos = close_bracket + 1
					continue
				elif tag_lower.begins_with("colour=") and not tag.contains("["):
					result += "[color=" + tag.substr(7) + "]"
					pos = close_bracket + 1
					continue
				elif tag_lower == "/colour":
					result += "[/color]"
					pos = close_bracket + 1
					continue
			result += "[lb]"
			pos += 1
			continue
		result += text[pos]
		pos += 1
	return result

func _ready():
	_build_ui_if_needed()
	if not runner and auto_bind_parent:
		var p = get_parent()
		if p is DialogueRunner:
			runner = p
		else:
			for child in get_tree().get_nodes_in_group("dialogue_runner"):
				if child is DialogueRunner:
					runner = child
					break
	if runner:
		_bind_runner()

func _bind_runner():
	if not runner.line_shown.is_connected(_on_line_shown):
		runner.line_shown.connect(_on_line_shown)
		runner.ended.connect(_on_ended)
		runner.variable_changed.connect(_on_variable_changed)
	if not runner.is_ended and runner.current_page != null:
		_on_line_shown(runner.current_page)

func _unbind_runner():
	if runner and runner.line_shown.is_connected(_on_line_shown):
		runner.line_shown.disconnect(_on_line_shown)
		runner.ended.disconnect(_on_ended)
		runner.variable_changed.disconnect(_on_variable_changed)

func _build_ui_if_needed():
	if _panel:
		return

	anchor_left = 0.0
	anchor_top = 0.0
	anchor_right = 1.0
	anchor_bottom = 1.0
	mouse_filter = MOUSE_FILTER_IGNORE

	_panel = PanelContainer.new()
	_panel.anchor_left = 0.1
	_panel.anchor_right = 0.9
	_panel.anchor_top = 0.65
	_panel.anchor_bottom = 0.95
	_panel.mouse_filter = MOUSE_FILTER_STOP
	add_child(_panel)

	var margin = MarginContainer.new()
	margin.add_theme_constant_override("margin_left", 16)
	margin.add_theme_constant_override("margin_top", 12)
	margin.add_theme_constant_override("margin_right", 16)
	margin.add_theme_constant_override("margin_bottom", 12)
	_panel.add_child(margin)

	var main_vbox = VBoxContainer.new()
	main_vbox.add_theme_constant_override("separation", 8)
	margin.add_child(main_vbox)

	_speaker_label = Label.new()
	_speaker_label.text = "Speaker"
	_speaker_label.add_theme_font_size_override("font_size", 18)
	main_vbox.add_child(_speaker_label)

	_dialogue_label = RichTextLabel.new()
	_dialogue_label.bbcode_enabled = true
	_dialogue_label.fit_content = false
	_dialogue_label.scroll_active = true
	_dialogue_label.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_dialogue_label.mouse_filter = MOUSE_FILTER_PASS
	main_vbox.add_child(_dialogue_label)

	var bottom_hbox = HBoxContainer.new()
	bottom_hbox.size_flags_vertical = Control.SIZE_SHRINK_END
	main_vbox.add_child(bottom_hbox)

	_choices_box = VBoxContainer.new()
	_choices_box.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	bottom_hbox.add_child(_choices_box)

	_continue_button = Button.new()
	_continue_button.text = "Continue"
	_continue_button.size_flags_horizontal = Control.SIZE_SHRINK_END
	_continue_button.pressed.connect(_on_continue_pressed)
	bottom_hbox.add_child(_continue_button)

func _on_line_shown(line: String) -> void:
	if not _panel:
		_build_ui_if_needed()
	_panel.visible = true
	_dialogue_label.text = to_bbcode(line)
	_sync_controls()

# A variable can change which choices are enabled without changing the line, so the controls refresh too.
func _on_variable_changed(_key: String, _before: Variant, _after: Variant) -> void:
	if not _refresh_queued:
		_refresh_queued = true
		_refresh_controls.call_deferred()

func _refresh_controls() -> void:
	_refresh_queued = false
	if runner and not runner.is_ended and _panel:
		_sync_controls()

func _sync_controls() -> void:
	if not runner or runner.is_ended:
		return
	_speaker_label.text = runner.speaker
	_continue_button.visible = runner.can_continue
	_clear_choices()
	var first_enabled_btn: Button = null

	# Choices are only shown on the last page of a scene.
	if runner.page_index >= runner.page_count - 1:
		for c in runner.get_choices():
			var btn = Button.new()
			btn.text = DialogueRules.plain_text(str(c.get("text", "")))
			var is_enabled = bool(c.get("enabled", false))
			btn.disabled = not is_enabled
			if not is_enabled and c.get("disabled_reason", null) != null:
				btn.tooltip_text = str(c["disabled_reason"])

			var choice_id = str(c.get("id", ""))
			btn.pressed.connect(func(): _on_choice_pressed(choice_id))
			_choices_box.add_child(btn)

			if is_enabled and not first_enabled_btn:
				first_enabled_btn = btn

	if Engine.is_editor_hint() or not is_inside_tree():
		return
	if first_enabled_btn:
		first_enabled_btn.grab_focus()
	elif _continue_button.visible:
		_continue_button.grab_focus()

func _clear_choices() -> void:
	if not _choices_box:
		return
	for child in _choices_box.get_children():
		_choices_box.remove_child(child)
		child.queue_free()

func _on_choice_pressed(choice_id: String) -> void:
	if runner:
		runner.choose(choice_id)

func _on_continue_pressed() -> void:
	if runner:
		runner.continue_dialogue()

func _on_ended(_scene_id: Variant, _choice_id: Variant) -> void:
	if _panel:
		_panel.visible = false
	_clear_choices()
