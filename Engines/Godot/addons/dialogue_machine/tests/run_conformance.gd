@tool
extends SceneTree

var DialogueRunnerClass = preload("res://addons/dialogue_machine/runtime/dialogue_runner.gd")

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
		if err_msg.is_empty():
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
		"events": events_copy
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
