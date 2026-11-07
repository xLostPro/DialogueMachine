extends SceneTree
## Drives DialogueView the way a game does: submit(), move_selection(), input actions and a custom layout.
## godot --headless --path Engines/Godot --script res://addons/dialogue_machine/tests/run_view_tests.gd

const RunnerScript = preload("res://addons/dialogue_machine/runtime/dialogue_runner.gd")
const ViewScript = preload("res://addons/dialogue_machine/runtime/dialogue_view.gd")

var _failures: Array[String] = []

func _initialize():
	_run()

func _expect(ok: bool, what: String) -> void:
	if not ok:
		_failures.append(what)

func _choice(id: String, text: String, requirements: Array = []) -> Dictionary:
	return {"InternalId": id, "ButtonText": text, "NextStateId": "[EXIT]", "Visibility": 1, "Requirements": requirements, "AfterChoiceActions": []}

# One scene: a line, then a disabled choice and two enabled ones.
func _story() -> Dictionary:
	var scene = {
		"State": "a", "Type": "Dialogue", "Text": "Hello [b]there[/b]", "DefaultNextId": "[EXIT]", "SayItAll": true,
		"Metadata": {"speaker": "Mara"},
		"Choices": [
			_choice("rich", "Pay", [{"Type": "Inventory", "TargetId": "coin", "Operator": ">=", "Value": "5"}]),
			_choice("stay", "Stay"),
			_choice("leave", "Leave"),
		]
	}
	return {
		"SchemaVersion": 1, "Contract": "dialogue-machine.story.v1", "StorySha256": "x", "StartingScene": "a",
		"Seed": 0, "NumericValues": {}, "TextValues": {}, "StoryJson": JSON.stringify({"ListOfCharacterStates": [scene]})
	}

func _create(layout: PackedScene = null) -> Array:
	var runner = RunnerScript.new()
	runner.live_link = false
	root.add_child(runner)
	var view = ViewScript.new()
	view.runner = runner
	view.characters_per_second = 1.0
	view.layout_scene = layout
	root.add_child(view)
	runner.load_story(_story())
	return [runner, view]

func _press(action: StringName) -> void:
	var ev = InputEventAction.new()
	ev.action = action
	ev.pressed = true
	root.push_input(ev)

func _custom_layout() -> PackedScene:
	var box = VBoxContainer.new()
	for n in ["Speaker", "Text", "Choices"]:
		var node: Control = Label.new() if n == "Speaker" else (RichTextLabel.new() if n == "Text" else VBoxContainer.new())
		node.name = n
		box.add_child(node)
		node.owner = box
		node.unique_name_in_owner = true
	var packed = PackedScene.new()
	packed.pack(box)
	box.free()
	return packed

func _run() -> void:
	await process_frame

	# submit() skips the typing, then picks the highlighted choice; move_selection() skips disabled choices.
	var made = _create()
	var runner = made[0]
	var view = made[1]
	var chosen = [null]
	runner.ended.connect(func(_s, c): chosen[0] = c)
	runner.start()
	_expect(view.is_open and view.is_typing, "starting shows the first line, typing")
	view.submit()
	_expect(view.is_typing, "the press that started the dialogue doesn't skip its first line")
	await process_frame
	view.submit()
	_expect(not view.is_typing and view.is_showing_choices(), "submit finishes the typing and shows the choices")
	_expect(view.get_selected_index() == 1, "the highlight starts on the first enabled choice")
	view.submit()
	_expect(not runner.is_ended, "one action per frame")
	view.move_selection(1)
	_expect(view.get_selected_index() == 2, "move_selection moves down")
	view.move_selection(1)
	_expect(view.get_selected_index() == 1, "move_selection wraps and skips the disabled choice")
	await process_frame
	view.submit()
	_expect(chosen[0] == "stay", "submit picks the highlighted choice, got %s" % chosen[0])
	_expect(not view.is_open, "the box hides when the dialogue ends")

	# The default input actions drive it too, and select_choice() ignores disabled choices.
	view.typewriter_enabled = false
	runner.start()
	await process_frame
	_press(&"ui_down")
	_expect(view.get_selected_index() == 2, "ui_down moves the highlight")
	view.select_choice(0)
	_expect(not runner.is_ended, "select_choice ignores a disabled choice")
	await process_frame
	_press(&"ui_accept")
	_expect(chosen[0] == "leave", "ui_accept picks the highlighted choice, got %s" % chosen[0])
	runner.queue_free()
	view.queue_free()

	# A custom layout is found by unique names; the missing Continue button is skipped.
	made = _create(_custom_layout())
	runner = made[0]
	view = made[1]
	view.typewriter_enabled = false
	runner.start()
	var speakers = view.find_children("Speaker", "Label", true, false)
	_expect(speakers.size() == 1 and speakers[0].text == "Mara", "the custom layout's %Speaker shows the speaker")
	_expect(view.find_children("*", "Button", true, false).size() == 3, "the custom layout's %Choices gets the choice buttons")

	for f in _failures:
		printerr("FAIL: view -> ", f)
	print("--- View tests: %d failed ---" % _failures.size())
	quit(1 if not _failures.is_empty() else 0)
