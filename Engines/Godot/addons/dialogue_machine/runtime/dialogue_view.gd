@tool
class_name DialogueView
extends Control
## Ready-made dialogue box. It reads input actions itself (ui_accept, ui_up and ui_down by default),
## or a game drives it with submit(), move_selection() and select_choice(). Restyle it with a Theme
## (type variations DialoguePanel, DialoguePortrait, DialogueSpeaker, DialogueText, DialogueChoice,
## DialogueChoiceSelected and DialogueContinue) or replace the layout with layout_scene.

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

@export_group("Typewriter")
@export var typewriter_enabled: bool = true
@export var characters_per_second: float = 40.0

@export_group("Input")
## Finishes the typing, then continues or picks the highlighted choice. Clear an action name to
## drive the view from your own code instead.
@export var submit_action: StringName = &"ui_accept"
@export var up_action: StringName = &"ui_up"
@export var down_action: StringName = &"ui_down"

@export_group("Look")
## Replaces the built-in layout. Give its nodes unique names: %Portrait (TextureRect), %Speaker (Label),
## %Text (RichTextLabel), %Choices (a Container) and %Continue (Button). Any of them can be left out.
@export var layout_scene: PackedScene = null
## Portrait key (String) → Texture2D. The key is the line's portrait (the scene's, or a page's
## @Speaker [portrait]:), or its speaker's name when it has none. A key with no entry hides the image.
@export var portraits: Dictionary = {}
@export var continue_text: String = "Continue"
@export var skip_text: String = "Skip"

## True while the line is still typing.
var is_typing: bool = false
## True while the box is showing a line.
var is_open: bool:
	get:
		return _panel != null and _panel.visible

var _panel: Control = null
var _portrait_rect: TextureRect = null
var _speaker_label: Label = null
var _dialogue_label: RichTextLabel = null
var _choices_box: Container = null
var _continue_button: Button = null
var _choice_buttons: Array[Button] = []
var _choices: Array[Dictionary] = []
var _refresh_queued: bool = false
var _revealed: float = 0.0
var _selected: int = -1
var _line_frame: int = -1
var _action_frame: int = -1

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

	if layout_scene:
		_panel = layout_scene.instantiate()
		add_child(_panel)
		_portrait_rect = _panel.get_node_or_null("%Portrait") as TextureRect
		_speaker_label = _panel.get_node_or_null("%Speaker") as Label
		_dialogue_label = _panel.get_node_or_null("%Text") as RichTextLabel
		_choices_box = _panel.get_node_or_null("%Choices") as Container
		_continue_button = _panel.get_node_or_null("%Continue") as Button
	else:
		_build_default_layout()

	if _dialogue_label:
		_dialogue_label.bbcode_enabled = true
	if _continue_button:
		_continue_button.pressed.connect(func(): _act(_continue_or_skip))
	_panel.gui_input.connect(_on_panel_input)
	_panel.visible = false

func _build_default_layout():
	var panel = PanelContainer.new()
	panel.theme_type_variation = &"DialoguePanel"
	panel.anchor_left = 0.1
	panel.anchor_right = 0.9
	panel.anchor_top = 0.65
	panel.anchor_bottom = 0.95
	panel.mouse_filter = MOUSE_FILTER_STOP
	add_child(panel)
	_panel = panel

	var margin = MarginContainer.new()
	margin.add_theme_constant_override("margin_left", 16)
	margin.add_theme_constant_override("margin_top", 12)
	margin.add_theme_constant_override("margin_right", 16)
	margin.add_theme_constant_override("margin_bottom", 12)
	panel.add_child(margin)

	var row = HBoxContainer.new()
	row.add_theme_constant_override("separation", 16)
	margin.add_child(row)

	_portrait_rect = TextureRect.new()
	_portrait_rect.name = "Portrait"
	_portrait_rect.theme_type_variation = &"DialoguePortrait"
	_portrait_rect.custom_minimum_size = Vector2(128, 128)
	_portrait_rect.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	_portrait_rect.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	_portrait_rect.size_flags_vertical = Control.SIZE_SHRINK_BEGIN
	_portrait_rect.visible = false
	row.add_child(_portrait_rect)

	var main_vbox = VBoxContainer.new()
	main_vbox.add_theme_constant_override("separation", 8)
	main_vbox.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_child(main_vbox)

	_speaker_label = Label.new()
	_speaker_label.theme_type_variation = &"DialogueSpeaker"
	main_vbox.add_child(_speaker_label)

	_dialogue_label = RichTextLabel.new()
	_dialogue_label.theme_type_variation = &"DialogueText"
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
	_continue_button.theme_type_variation = &"DialogueContinue"
	_continue_button.size_flags_horizontal = Control.SIZE_SHRINK_END
	bottom_hbox.add_child(_continue_button)

func _process(delta: float) -> void:
	if not is_typing or Engine.is_editor_hint():
		return
	_revealed += characters_per_second * delta
	if int(_revealed) >= _dialogue_label.get_total_character_count():
		_finish_typing()
	else:
		_dialogue_label.visible_characters = int(_revealed)

func _unhandled_input(event: InputEvent) -> void:
	if not is_open or Engine.is_editor_hint():
		return
	if _is_action(event, submit_action, false):
		submit()
	elif is_showing_choices() and _is_action(event, up_action, true):
		move_selection(-1)
	elif is_showing_choices() and _is_action(event, down_action, true):
		move_selection(1)
	else:
		return
	get_viewport().set_input_as_handled()

func _is_action(event: InputEvent, action: StringName, allow_echo: bool) -> bool:
	return action != &"" and InputMap.has_action(action) and event.is_action_pressed(action, allow_echo)

# Clicking the box finishes the typing.
func _on_panel_input(event: InputEvent) -> void:
	if is_typing and event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		_act(skip_typewriter)

## The one button a game needs: finishes the typing, then picks the highlighted choice or continues.
## A press in the frame that showed the line is ignored, so the press that started or advanced the
## dialogue doesn't also skip the new line.
func submit() -> void:
	_act(_submit_now)

func _submit_now() -> void:
	if is_typing:
		skip_typewriter()
	elif is_showing_choices():
		_choose_index(_selected)
	elif runner and runner.can_continue:
		runner.continue_dialogue()

## Moves the choice highlight by delta (negative is up), skipping disabled choices and wrapping.
func move_selection(delta: int) -> void:
	if not is_showing_choices() or delta == 0:
		return
	var step = 1 if delta > 0 else -1
	for i in absi(delta):
		_selected = _first_enabled(posmod(_selected + step, _choices.size()), step)
	_apply_selection(true)

## Picks the choice at index (as shown, from 0) if it is enabled. Use it for number keys.
func select_choice(index: int) -> void:
	_act(func(): _choose_index(index))

## Shows the whole line at once.
func skip_typewriter() -> void:
	if is_typing:
		_finish_typing()

## True once the line has finished typing and the runner offers choices.
func is_showing_choices() -> bool:
	return is_open and not is_typing and not _choices.is_empty()

## The highlighted choice, or -1 when no choice is shown or enabled.
func get_selected_index() -> int:
	return _selected if is_showing_choices() else -1

# One action per frame, and none in the frame a line appeared, so the game's input and the
# buttons' own focus handling can't both act on one press.
func _act(action: Callable) -> bool:
	var frame = Engine.get_process_frames()
	if not is_open or frame == _line_frame or frame == _action_frame:
		return false
	_action_frame = frame
	action.call()
	return true

func _continue_or_skip() -> void:
	if is_typing:
		skip_typewriter()
	elif runner and runner.can_continue:
		runner.continue_dialogue()

func _choose_index(index: int) -> void:
	if not is_showing_choices() or index < 0 or index >= _choices.size() or not bool(_choices[index].get("enabled", false)):
		return
	runner.choose(str(_choices[index].get("id", "")))

func _on_line_shown(line: String) -> void:
	_build_ui_if_needed()
	_panel.visible = true
	_line_frame = Engine.get_process_frames()
	is_typing = false
	if _dialogue_label:
		_dialogue_label.text = to_bbcode(line)
		_revealed = 0.0
		is_typing = typewriter_enabled and characters_per_second > 0.0 and not Engine.is_editor_hint() \
			and _dialogue_label.get_total_character_count() > 0
		_dialogue_label.visible_characters = 0 if is_typing else -1
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

func _finish_typing() -> void:
	is_typing = false
	if _dialogue_label:
		_dialogue_label.visible_characters = -1
	_sync_controls()

func _sync_controls() -> void:
	if not runner or runner.is_ended:
		return
	if _speaker_label:
		_speaker_label.text = runner.speaker
		_speaker_label.visible = not runner.speaker.is_empty()
	if _portrait_rect:
		var key = runner.portrait if not runner.portrait.is_empty() else runner.speaker
		var texture = portraits.get(key) as Texture2D
		_portrait_rect.texture = texture
		_portrait_rect.visible = texture != null
	_clear_choices()

	# Choices are only shown on the last page of a scene, once the line has finished typing.
	if not is_typing and runner.page_index >= runner.page_count - 1:
		_choices = runner.get_choices()
	if _choices_box:
		for i in _choices.size():
			var btn = _create_choice_button(_choices[i], i)
			_choice_buttons.append(btn)
			_choices_box.add_child(btn)
	_selected = _first_enabled(0, 1)

	if _continue_button:
		_continue_button.visible = is_typing or runner.can_continue
		_continue_button.text = skip_text if is_typing else continue_text

	if Engine.is_editor_hint() or not is_inside_tree():
		return
	if is_showing_choices():
		_apply_selection(true)
	elif _continue_button and _continue_button.visible and not is_typing:
		_continue_button.grab_focus()

func _create_choice_button(c: Dictionary, index: int) -> Button:
	var btn = Button.new()
	btn.theme_type_variation = &"DialogueChoice"
	btn.alignment = HORIZONTAL_ALIGNMENT_LEFT
	btn.text = DialogueRules.plain_text(str(c.get("text", "")))
	var is_enabled = bool(c.get("enabled", false))
	btn.disabled = not is_enabled
	btn.focus_mode = Control.FOCUS_ALL if is_enabled else Control.FOCUS_NONE
	if not is_enabled and c.get("disabled_reason", null) != null:
		btn.tooltip_text = str(c["disabled_reason"])
		btn.text += " (%s)" % c["disabled_reason"]
	btn.pressed.connect(func(): select_choice(index))
	# Mouse hover and GUI focus move the same highlight as the input actions.
	btn.mouse_entered.connect(func(): _highlight(index))
	btn.focus_entered.connect(func(): _highlight(index))
	return btn

func _highlight(index: int) -> void:
	if index == _selected or not is_showing_choices() or not bool(_choices[index].get("enabled", false)):
		return
	_selected = index
	_apply_selection(false)

func _apply_selection(focus: bool) -> void:
	for i in _choice_buttons.size():
		_choice_buttons[i].theme_type_variation = &"DialogueChoiceSelected" if i == _selected else &"DialogueChoice"
	if focus and _selected >= 0 and _selected < _choice_buttons.size() and _choice_buttons[_selected].is_inside_tree():
		_choice_buttons[_selected].grab_focus()

func _clear_choices() -> void:
	_choices = []
	_choice_buttons.clear()
	if not _choices_box:
		return
	for child in _choices_box.get_children():
		_choices_box.remove_child(child)
		child.queue_free()

# The first enabled choice from start, walking in direction step (wraps); -1 if none is enabled.
func _first_enabled(start: int, step: int) -> int:
	var n = _choices.size()
	for i in n:
		var k = posmod(start + i * step, n)
		if bool(_choices[k].get("enabled", false)):
			return k
	return -1

func _on_ended(_scene_id: Variant, _choice_id: Variant) -> void:
	is_typing = false
	if _panel:
		_panel.visible = false
	_clear_choices()
