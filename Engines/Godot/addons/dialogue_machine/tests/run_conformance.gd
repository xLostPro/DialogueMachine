@tool
extends SceneTree

var DialogueRunnerClass = preload("res://addons/dialogue_machine/runtime/dialogue_runner.gd")
var DialogueStoryClass = preload("res://addons/dialogue_machine/runtime/dialogue_story.gd")
var Rules = preload("res://addons/dialogue_machine/runtime/dialogue_rules.gd")
var ViewClass = preload("res://addons/dialogue_machine/runtime/dialogue_view.gd")

# A game-owned store with only the four required methods.
class GameStore extends RefCounted:
	var numbers: Dictionary = {}
	var texts: Dictionary = {}
	func get_number(key): return numbers.get(key, 0)
	func set_number(key, value): numbers[key] = value
	func get_text(key): return texts.get(key, null)
	func set_text(key, value): texts[key] = value
	func has_number(key): return numbers.has(key)

func _init():
	print("--- Running Dialogue Machine Godot Conformance Tests ---")
	var fixture_dir = _find_fixture_directory()
	if fixture_dir.is_empty():
		printerr("ERROR: Could not locate conformance fixtures directory!")
		quit(1)
		return

	print("Loading fixtures from: ", fixture_dir)
	var dir = DirAccess.open(fixture_dir)
	if not dir:
		printerr("ERROR: Failed to open directory: ", fixture_dir)
		quit(1)
		return

	var files: Array[String] = []
	dir.list_dir_begin()
	var file_name = dir.get_next()
	while not file_name.is_empty():
		if not dir.current_is_dir() and file_name.ends_with(".json"):
			files.append(file_name)
		file_name = dir.get_next()
	dir.list_dir_end()
	files.sort()

	if files.is_empty():
		printerr("ERROR: No JSON fixture files found in: ", fixture_dir)
		quit(1)
		return

	print("Found %d fixture files." % files.size())
	var passed_count = 0
	var failed_count = 0

	var unit_failures = _run_unit_tests()
	for failure in unit_failures:
		printerr("FAIL: unit -> ", failure)
	failed_count += unit_failures.size()
	if unit_failures.is_empty():
		print("PASS: unit tests")
		passed_count += 1

	for fname in files:
		var fpath = fixture_dir + "/" + fname
		var file = FileAccess.open(fpath, FileAccess.READ)
		if not file:
			printerr("FAIL: Cannot open fixture file: ", fpath)
			failed_count += 1
			continue

		var json_str = file.get_as_text()
		var fixture = JSON.parse_string(json_str)
		if not (fixture is Dictionary):
			printerr("FAIL: Invalid JSON in: ", fname)
			failed_count += 1
			continue

		var test_name = str(fixture.get("name", fname))
		var err_msg = _run_fixture(test_name, fixture)
		if not (err_msg is String):
			printerr("FAIL: %s -> the fixture did not finish (script error)" % test_name)
			failed_count += 1
		elif err_msg.is_empty():
			print("PASS: ", test_name)
			passed_count += 1
		else:
			printerr("FAIL: %s -> %s" % [test_name, err_msg])
			failed_count += 1

	print("--- Summary: %d passed, %d failed ---" % [passed_count, failed_count])
	if failed_count > 0:
		quit(1)
	else:
		quit(0)

func _find_fixture_directory() -> String:
	var candidates = [
		"res://addons/dialogue_machine/tests/conformance",
		"res://tests/conformance",
		"res://../../Conformance",
		"res://../Conformance"
	]
	for c in candidates:
		if DirAccess.dir_exists_absolute(c):
			return c
	return ""

func _run_fixture(name: String, fixture: Dictionary) -> String:
	var handoff = fixture.get("handoff", {})
	var inputs = fixture.get("inputs", [])
	var expected = fixture.get("expected", [])

	if not (handoff is Dictionary) or not (inputs is Array) or not (expected is Array):
		return "Malformed fixture structure"

	var runner = DialogueRunnerClass.new()
	var events: Array[Dictionary] = []
	var saved_state: String = ""

	runner.action_raised.connect(func(type, target, param, _amount):
		events.append({ "type": type, "targetId": target, "parameter": param })
	)
	runner.shop_opened.connect(func(shop_id):
		events.append({ "type": "Shop", "targetId": shop_id, "parameter": null })
	)

	runner.load_story(handoff)

	for i in range(inputs.size()):
		var input_obj = inputs[i]
		if not (input_obj is Dictionary):
			return "Input %d is not a Dictionary" % i
		var op = str(input_obj.get("op", ""))
		events.clear()

		match op:
			"start":
				runner.start()
			"continue":
				runner.continue_dialogue()
			"choose":
				runner.choose(str(input_obj.get("choiceId", "")))
			"setNumber":
				runner.variables.set_number(str(input_obj.get("key", "")), int(input_obj.get("value", 0)))
			"setText":
				runner.variables.set_text(str(input_obj.get("key", "")), str(input_obj.get("value", "")))
			"save":
				saved_state = runner.save_state()
			"restore":
				runner.restore_state(saved_state)
			_:
				return "Unknown fixture op: '%s' at input %d" % [op, i]

		if i >= expected.size():
			return "No expected snapshot for input index %d" % i

		var exp_snap = expected[i]
		if not (exp_snap is Dictionary):
			return "Expected snapshot %d is not a Dictionary" % i

		var act_snap = _take_snapshot(runner, events)
		var diff = _compare_snapshots(act_snap, exp_snap)
		if not diff.is_empty():
			runner.free()
			return "Input %d (%s) mismatch: %s" % [i, op, diff]

	runner.free()
	return ""

func _take_snapshot(runner, events: Array) -> Dictionary:
	var choices_arr: Array = []
	for c in runner.get_choices():
		choices_arr.append({
			"id": c.get("id"),
			"text": c.get("text"),
			"enabled": c.get("enabled")
		})

	var numeric_dict: Dictionary = {}
	var text_dict: Dictionary = {}
	var all_num = runner.variables.all_numeric()
	var all_txt = runner.variables.all_text()

	var num_keys: Array = all_num.keys()
	num_keys.sort()
	for k in num_keys:
		numeric_dict[k] = int(all_num[k])

	var txt_keys: Array = all_txt.keys()
	txt_keys.sort()
	for k in txt_keys:
		text_dict[k] = str(all_txt[k])

	var events_copy: Array = []
	for ev in events:
		events_copy.append(ev.duplicate(true))

	return {
		"sceneId": null if runner.is_ended else runner.current_scene_id,
		"ended": runner.is_ended,
		"page": null if runner.is_ended else runner.current_page,
		"plain": null if runner.is_ended else runner.current_page_plain,
		"pageIndex": runner.page_index,
		"pageCount": runner.page_count,
		"choices": choices_arr,
		"canContinue": runner.can_continue,
		"numeric": numeric_dict,
		"text": text_dict,
		"events": events_copy,
		"speaker": null if runner.is_ended else runner.speaker,
		"portrait": null if runner.is_ended else runner.portrait
	}

func _compare_snapshots(actual: Dictionary, expected: Dictionary) -> String:
	for key in expected.keys():
		var exp_val = expected[key]
		var act_val = actual.get(key, null)
		if not _deep_equals(act_val, exp_val):
			return "field '%s': expected %s, got %s" % [key, JSON.stringify(exp_val), JSON.stringify(act_val)]
	return ""

func _deep_equals(a: Variant, b: Variant) -> bool:
	if typeof(a) != typeof(b):
		# Handle int / float comparison
		if (typeof(a) == TYPE_INT or typeof(a) == TYPE_FLOAT) and (typeof(b) == TYPE_INT or typeof(b) == TYPE_FLOAT):
			return a == b
		return false
	if typeof(a) == TYPE_DICTIONARY:
		if a.size() != b.size():
			return false
		for k in a.keys():
			if not b.has(k) or not _deep_equals(a[k], b[k]):
				return false
		return true
	if typeof(a) == TYPE_ARRAY:
		if a.size() != b.size():
			return false
		for idx in range(a.size()):
			if not _deep_equals(a[idx], b[idx]):
				return false
		return true
	return a == b

# ---------------------------------------------------------------------------
# Unit tests for what the shared fixtures can't reach
# ---------------------------------------------------------------------------

func _scene(id: String, text: String, extra: Dictionary = {}) -> Dictionary:
	var scene = {
		"State": id, "Type": "Dialogue", "Text": text, "DefaultNextId": "[EXIT]", "SayItAll": true,
		"Choices": [], "DefaultAfterActions": [], "Metadata": {"speaker": "Ann", "title": id}
	}
	scene.merge(extra, true)
	return scene

func _handoff(scenes: Array, extra: Dictionary = {}) -> Dictionary:
	var h = {
		"SchemaVersion": 1, "Contract": "dialogue-machine.story.v1",
		"StoryJson": JSON.stringify({"ListOfCharacterStates": scenes}), "StorySha256": "x",
		"StartingScene": scenes[0]["State"], "Seed": 0, "NumericValues": {}, "TextValues": {}
	}
	h.merge(extra, true)
	return h

func _expect(failures: Array[String], ok: bool, what: String) -> void:
	if not ok:
		failures.append(what)

func _run_unit_tests() -> Array[String]:
	var f: Array[String] = []

	# 64-bit helpers, with values computed from the C# definition
	_expect(f, Rules.string_to_u64("11400714819323198485") == -7046029254386353131, "string_to_u64 with the high bit")
	_expect(f, Rules.u64_to_string(-1) == "18446744073709551615", "u64_to_string of -1")
	_expect(f, Rules.u64_to_string(Rules.string_to_u64("17418742259747381416")) == "17418742259747381416", "u64 round trip")
	_expect(f, Rules.is_u64_string("18446744073709551615") and not Rules.is_u64_string("18446744073709551616") and not Rules.is_u64_string("-1") and not Rules.is_u64_string("1a"), "is_u64_string bounds")
	var golden = {0: [2, 1, 2, 4, 2, 2, 1, 2], 1: [2, 0, 1, 0, 5, 2, 0, 3], 42: [5, 5, 0, 2, 6, 4, 2, 6], -1: [0, 1, 0, 6, 1, 5, 3, 4], 2147483647: [1, 4, 0, 1, 2, 5, 4, 6]}
	for seed_val in golden.keys():
		var state = [Rules.seed_state(seed_val)]
		var got: Array = []
		for _i in range(8):
			got.append(Rules.splitmix64(state, 7))
		_expect(f, got == golden[seed_val], "splitmix64 seed %d: %s" % [seed_val, got])

	# text rules
	_expect(f, Rules.plain_text("[colour]a[/colour=x][COLOUR=red]b[/COLOUR][B]c[/B]") == "abc", "plain_text strips colour forms")
	_expect(f, Rules.plain_text("[color=red]a[/color]") == "[color=red]a[/color]", "plain_text keeps [color]")
	_expect(f, Rules.parse_int_exact("0000000000005") == [true, 5], "parse_int_exact leading zeros")
	_expect(f, not Rules.parse_int_exact("2147483648")[0] and Rules.parse_int_exact("-2147483648")[0], "parse_int_exact int32 bounds")

	# the view's markup conversion: known tags map to BBCode, everything else is shown as text
	_expect(f, ViewClass.to_bbcode("[b]x[/B] [colour=red]y[/colour] [Colour=#fff]z") == "[b]x[/b] [color=red]y[/color] [color=#fff]z", "to_bbcode maps the four tags")
	_expect(f, ViewClass.to_bbcode("[color=red]a [wave]b[/wave] [") == "[lb]color=red]a [lb]wave]b[lb]/wave] [lb]", "to_bbcode escapes other brackets")
	_expect(f, ViewClass.to_bbcode("[colour=[img]x") == "[lb]colour=[lb]img]x", "to_bbcode refuses a nested bracket in a colour")

	# actions change variables quietly: the old scene must not re-emit its line
	var story = _handoff([
		_scene("A", "Trust [if aff:trust>=1]high[else]low[/if]", {"Choices": [{"InternalId": "go", "ButtonText": "Go", "NextStateId": "B", "Visibility": 0, "Requirements": [], "AfterChoiceActions": [{"Type": "Affinity", "TargetId": "trust", "Amount": 1}]}]}),
		_scene("B", "Done")
	])
	var r = DialogueRunnerClass.new()
	var lines: Array = []
	var changes: Array = []
	r.line_shown.connect(func(l): lines.append(l))
	r.variable_changed.connect(func(k, b, a): changes.append(k))
	r.load_story(story)
	r.start()
	_expect(f, lines == ["Trust low"], "start lines: %s" % [lines])
	_expect(f, changes.is_empty(), "start emits no variable_changed")
	lines.clear()
	r.choose("go")
	_expect(f, lines == ["Done"], "choose emits only the new line: %s" % [lines])
	_expect(f, changes == ["aff:trust"], "choose emits one variable_changed: %s" % [changes])
	r.free()

	# game-owned variables keep their values and are visible to text
	var store = GameStore.new()
	store.numbers["aff:trust"] = 3
	r = DialogueRunnerClass.new()
	r.variables.set_provider(store)
	r.load_story(_handoff([_scene("A", "Trust [if aff:trust>=1]high[else]low[/if]")], {"NumericValues": {"aff:trust": 0, "inv:coin": 2}}))
	r.start()
	_expect(f, store.numbers["aff:trust"] == 3, "start keeps the game's value")
	_expect(f, r.current_page == "Trust high", "provider values reach text: %s" % [r.current_page])
	_expect(f, store.numbers.get("inv:coin", -1) == 2, "start fills keys the game lacks")
	r.free()

	# variables refresh the page while keeping the random line
	r = DialogueRunnerClass.new()
	r.load_story(_handoff([_scene("A", "[if aff:x>=1]p[else]q[/if]1\n[if aff:x>=1]p[else]q[/if]2\nthree", {"SayItAll": false})], {"Seed": 1}))
	r.start()
	var before = r.current_page
	r.variables.set_number("aff:x", 1)
	_expect(f, r.current_page.begins_with("p") == before.begins_with("q") or r.page_count == 1, "set_number refreshes the page")
	r.free()

	# auto_start honours the story seed (seed 1 picks line 5 of 6; seed 0 would pick line 1)
	var seed_story = _handoff([_scene("A", "a\nb\nc\nd\ne\nf", {"SayItAll": false})], {"Seed": 1})
	var plain = DialogueRunnerClass.new()
	plain.load_story(seed_story)
	plain.start()
	var res = DialogueStoryClass.new()
	res.handoff_json = JSON.stringify(seed_story)
	var auto = DialogueRunnerClass.new()
	auto.story = res
	auto.auto_start = true
	auto._ready() # the node isn't in a tree yet while the SceneTree is still initialising
	_expect(f, plain.current_page == "f", "seed 1 picks f: %s" % [plain.current_page])
	_expect(f, auto.current_page == plain.current_page, "auto_start keeps the story seed: %s" % [auto.current_page])
	auto.free()
	plain.free()

	# hot reload refreshes the speaker and adds new starting variables
	r = DialogueRunnerClass.new()
	r.load_story(_handoff([_scene("A", "Hi")]))
	r.start()
	r.replace_story(_handoff([_scene("A", "Hi", {"Metadata": {"speaker": "Bob"}})], {"NumericValues": {"aff:new": 4}}))
	_expect(f, r.speaker == "Bob", "replace_story refreshes the speaker")
	_expect(f, r.variables.get_number("aff:new") == 4, "replace_story adds new starting values")
	r.replace_story({"SchemaVersion": 1})
	_expect(f, not r.is_ended and r.speaker == "Bob", "a bad payload leaves the story and conversation alone")
	r.free()

	# a bad save changes nothing
	r = DialogueRunnerClass.new()
	r.load_story(_handoff([_scene("A", "Hi")]))
	r.start()
	r.restore_state(JSON.stringify({"sceneId": "A", "randomState": "banana"}))
	_expect(f, not r.is_ended and r.current_page == "Hi", "restore with a bad randomState is refused")
	r.free()
	# start() before the runner's own _ready (a sibling started it) must keep the live link polling
	r = DialogueRunnerClass.new()
	r.load_story(_handoff([_scene("A", "Hi")]))
	r.start()
	r._ready()
	_expect(f, r.is_processing() == OS.is_debug_build(), "live link polling survives start() before _ready")
	r.free()
	return f
