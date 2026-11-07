@tool
class_name DialogueRules
extends RefCounted

const EXIT: String = "[EXIT]"
const NO_DIALOGUE: String = "(No dialogue)"

const SPLITMIX_STEP: int = -7046029254386353131 # 0x9E3779B97F4A7C15
const SPLITMIX_MUL1: int = -4658895280553007687 # 0xBF58476D1CE4E5B9
const SPLITMIX_MUL2: int = -7723592293110705685 # 0x94D049BB133111EB
const SIGN_MASK: int = 1 << 63                   # 0x8000000000000000

const INT32_MIN: int = -2147483648
const INT32_MAX: int = 2147483647

static func compare(actual: int, op: String, target: int) -> bool:
	match op:
		"==": return actual == target
		"!=": return actual != target
		">": return actual > target
		"<": return actual < target
		">=": return actual >= target
		"<=": return actual <= target
		_: return false

static func key(type: String, id: String) -> String:
	return ("inv:" if type == "Inventory" else "aff:") + id

static func parse_int_exact(raw: String) -> Array:
	var s = raw.strip_edges()
	if s.is_empty():
		return [false, 0]
	var start = 0
	var first_char = s[0]
	if first_char == "-" or first_char == "+":
		start = 1
		if s.length() == 1:
			return [false, 0]
	for i in range(start, s.length()):
		var c = s.unicode_at(i)
		if c < 48 or c > 57:
			return [false, 0]
	var digits = s.substr(start).lstrip("0")
	if digits.length() > 10:
		return [false, 0]
	var val = s.to_int()
	if val < INT32_MIN or val > INT32_MAX:
		return [false, 0]
	return [true, val]

static func requirement_met(type: String, target_id: String, op: String, value: String, vars: Dictionary) -> Array:
	var parsed = parse_int_exact(value)
	if (type != "Affinity" and type != "Inventory") or target_id.strip_edges().is_empty() or not parsed[0]:
		return [false, "This condition is incomplete or invalid."]
	var target: int = parsed[1]
	var req_key: String = key(type, target_id)
	var actual: int = int(vars.get(req_key, 0))
	if not compare(actual, op, target):
		return [false, "Requires %s %s %d · current %d" % [req_key, op, target, actual]]
	return [true, ""]

static func requirements_met(requirements: Array, vars: Dictionary, any: bool = false) -> Array:
	var first_reason = null
	for req in requirements:
		if req is Dictionary:
			var res = requirement_met(
				str(req.get("Type", "")),
				str(req.get("TargetId", "")),
				str(req.get("Operator", "")),
				str(req.get("Value", "")),
				vars
			)
			if res[0] and any:
				return res
			if not res[0]:
				if not any:
					return res
				if first_reason == null:
					first_reason = res
	return first_reason if first_reason != null else [true, ""]

static func _is_key_char(c: int) -> bool:
	if c < 128:
		return (c >= 48 and c <= 57) or (c >= 65 and c <= 90) or (c >= 97 and c <= 122) or c == 95 or c == 58 or c == 46 or c == 45 # 0-9 A-Z a-z _ : . -
	return _is_unicode_word(c)

# Approximates .NET \w (letters, marks, digits) beyond ASCII: everything except spaces, punctuation, symbols and emoji.
static func _is_unicode_word(c: int) -> bool:
	if c < 0xC0:
		return c == 0xAA or c == 0xB5 or c == 0xBA
	if c == 0xD7 or c == 0xF7:
		return false
	if (c >= 0x2000 and c <= 0x2BFF) or (c >= 0x2E00 and c <= 0x2E7F) or (c >= 0x3000 and c <= 0x303F):
		return false
	if (c >= 0xE000 and c <= 0xF8FF) or (c >= 0xFFF0 and c <= 0xFFFF) or c >= 0x10000:
		return false
	return true

# ASCII whitespace plus the Unicode spaces .NET's \s matches.
static func _is_whitespace(c: int) -> bool:
	return c == 32 or (c >= 9 and c <= 13) or c == 0x85 or c == 0xA0 or c == 0x1680 or (c >= 0x2000 and c <= 0x200A) or c == 0x2028 or c == 0x2029 or c == 0x202F or c == 0x205F or c == 0x3000

static func _try_parse_if_header(text: String, start_pos: int) -> Array:
	# Returns [bool success, int header_end, String key, String op, int target, bool target_valid]
	var len_text = text.length()
	if start_pos + 4 > len_text:
		return [false, -1, "", "", 0, false]
	if text[start_pos] != "[":
		return [false, -1, "", "", 0, false]
	var c1 = text[start_pos + 1].to_lower()
	var c2 = text[start_pos + 2].to_lower()
	if c1 != "i" or c2 != "f":
		return [false, -1, "", "", 0, false]
	if not _is_whitespace(text.unicode_at(start_pos + 3)):
		return [false, -1, "", "", 0, false]

	var pos = start_pos + 4
	while pos < len_text and _is_whitespace(text.unicode_at(pos)):
		pos += 1

	var key_start = pos
	while pos < len_text and _is_key_char(text.unicode_at(pos)):
		pos += 1
	if pos == key_start:
		return [false, -1, "", "", 0, false]
	var parsed_key = text.substr(key_start, pos - key_start)

	while pos < len_text and _is_whitespace(text.unicode_at(pos)):
		pos += 1

	var parsed_op = ""
	if pos + 1 < len_text:
		var two_char = text.substr(pos, 2)
		if two_char in ["==", "!=", ">=", "<="]:
			parsed_op = two_char
			pos += 2
	if parsed_op.is_empty() and pos < len_text:
		var one_char = text[pos]
		if one_char == ">" or one_char == "<":
			parsed_op = one_char
			pos += 1
	if parsed_op.is_empty():
		return [false, -1, "", "", 0, false]

	while pos < len_text and _is_whitespace(text.unicode_at(pos)):
		pos += 1

	var num_start = pos
	if pos < len_text and text[pos] == "-":
		pos += 1
	var digit_start = pos
	while pos < len_text:
		var dc = text.unicode_at(pos)
		if dc >= 48 and dc <= 57:
			pos += 1
		else:
			break
	if pos == digit_start:
		return [false, -1, "", "", 0, false]

	if pos >= len_text or text[pos] != "]":
		return [false, -1, "", "", 0, false]

	var num_str = text.substr(num_start, pos - num_start)
	var parsed_target = parse_int_exact(num_str)
	var target_valid = parsed_target[0]
	var target_val = parsed_target[1]

	return [true, pos, parsed_key, parsed_op, target_val, target_valid]

static func _find_next_if_with_space(text: String, start_pos: int) -> int:
	var pos = start_pos
	var len_text = text.length()
	while pos + 3 < len_text:
		if text[pos] == "[" and text[pos + 1].to_lower() == "i" and text[pos + 2].to_lower() == "f" and _is_whitespace(text.unicode_at(pos + 3)):
			return pos
		pos += 1
	return -1

static func _find_next_close_if(text: String, start_pos: int) -> int:
	var pos = start_pos
	var len_text = text.length()
	while pos + 4 < len_text:
		if text[pos] == "[" and text[pos + 1] == "/" and text[pos + 2].to_lower() == "i" and text[pos + 3].to_lower() == "f" and text[pos + 4] == "]":
			return pos
		pos += 1
	return -1

static func _find_else_tag(text: String) -> int:
	var pos = 0
	var len_text = text.length()
	while pos + 5 < len_text:
		if text[pos] == "[" and text[pos + 1].to_lower() == "e" and text[pos + 2].to_lower() == "l" and text[pos + 3].to_lower() == "s" and text[pos + 4].to_lower() == "e" and text[pos + 5] == "]":
			return pos
		pos += 1
	return -1

static func resolve_text(text: String, vars: Dictionary, text_vars: Dictionary) -> String:
	var current_text = text

	# Up to 64 passes of innermost conditional resolution
	for _pass in range(64):
		var changed = false
		var new_text = ""
		var scan_pos = 0

		while scan_pos < current_text.length():
			var if_start = _find_next_if_with_space(current_text, scan_pos)
			if if_start == -1:
				new_text += current_text.substr(scan_pos)
				break

			new_text += current_text.substr(scan_pos, if_start - scan_pos)

			var header = _try_parse_if_header(current_text, if_start)
			if header[0]:
				var header_end: int = header[1]
				var req_key: String = header[2]
				var req_op: String = header[3]
				var req_target: int = header[4]
				var target_valid: bool = header[5]

				var close_pos = _find_next_close_if(current_text, header_end + 1)
				var next_if = _find_next_if_with_space(current_text, header_end + 1)

				if close_pos != -1 and (next_if == -1 or next_if > close_pos):
					# Innermost block found
					var body = current_text.substr(header_end + 1, close_pos - (header_end + 1))
					var else_pos = _find_else_tag(body)
					var true_branch = ""
					var false_branch = ""
					if else_pos != -1:
						true_branch = body.substr(0, else_pos)
						false_branch = body.substr(else_pos + 6)
						var second_else = _find_else_tag(false_branch)
						if second_else != -1:
							false_branch = false_branch.substr(0, second_else)
					else:
						true_branch = body

					var val: int = 0
					if vars.has(req_key):
						val = int(vars[req_key])
					elif vars.has("inv:" + req_key):
						val = int(vars["inv:" + req_key])
					elif vars.has("aff:" + req_key):
						val = int(vars["aff:" + req_key])

					if target_valid and compare(val, req_op, req_target):
						new_text += true_branch
					else:
						new_text += false_branch

					scan_pos = close_pos + 5
					changed = true
					continue

			# Not innermost or invalid header: advance 1 char
			new_text += current_text[if_start]
			scan_pos = if_start + 1

		current_text = new_text
		if not changed:
			break

	# Placeholder substitutions: {key}
	var substituted = ""
	var pos = 0
	var len_sub = current_text.length()
	while pos < len_sub:
		if current_text[pos] == "{":
			var close_brace = current_text.find("}", pos + 1)
			if close_brace != -1:
				var sub_key = current_text.substr(pos + 1, close_brace - (pos + 1))
				var valid_key = not sub_key.is_empty()
				for i in range(sub_key.length()):
					if not _is_key_char(sub_key.unicode_at(i)):
						valid_key = false
						break
				if valid_key:
					if text_vars.has(sub_key):
						substituted += str(text_vars[sub_key])
						pos = close_brace + 1
						continue
					elif vars.has(sub_key):
						substituted += str(int(vars[sub_key]))
						pos = close_brace + 1
						continue
					elif sub_key.begins_with("a:") and text_vars.has(sub_key.substr(2)) and str(text_vars[sub_key.substr(2)]).length() > 0:
						# {a:key} writes "a" or "an" before the text variable's value.
						var noun = str(text_vars[sub_key.substr(2)])
						substituted += ("an " if "aeiouAEIOU".contains(noun[0]) else "a ") + noun
						pos = close_brace + 1
						continue

		substituted += current_text[pos]
		pos += 1

	return substituted

static func plain_text(text: String) -> String:
	# Strip [b] [i] [u] [colour] [colour=...] and their closing forms, like the C# pattern.
	# Note: [color] is NOT stripped and stays in the text.
	var result = ""
	var pos = 0
	var len_text = text.length()
	while pos < len_text:
		if text[pos] == "[":
			var close_bracket = text.find("]", pos + 1)
			if close_bracket != -1:
				var tag = text.substr(pos + 1, close_bracket - (pos + 1)).to_lower()
				var name = tag.substr(1) if tag.begins_with("/") else tag
				if name in ["b", "i", "u", "colour"] or name.begins_with("colour="):
					pos = close_bracket + 1
					continue
		result += text[pos]
		pos += 1
	return result

static func apply_amount(type: String, before: int, amount: int) -> int:
	var sum_val = before + amount
	var min_val = 0 if type == "Inventory" else INT32_MIN
	var max_val = INT32_MAX
	return clampi(sum_val, min_val, max_val)

static func is_variable_action(type: String) -> bool:
	return type == "Affinity" or type == "Inventory" or type == "Set" or type == "Toggle"

static func apply_action(type: String, before: int, amount: int) -> int:
	if type == "Set":
		return amount
	if type == "Toggle":
		return 1 if before == 0 else 0
	return apply_amount(type, before, amount)

static func clamp_number(num_key: String, value: int) -> int:
	return maxi(0, value) if num_key.begins_with("inv:") else value

static func split_pages(text: String) -> Array[String]:
	var pages: Array[String] = []
	var len_text = text.length()
	var pos = 0
	while pos < len_text:
		var line_start = pos
		while pos < len_text and text[pos] != "\r" and text[pos] != "\n":
			pos += 1
		if pos > line_start:
			pages.append(text.substr(line_start, pos - line_start))
		if pos < len_text:
			if text[pos] == "\r" and pos + 1 < len_text and text[pos + 1] == "\n":
				pos += 2
			else:
				pos += 1
	return pages

static func parse_page_header(raw_page: String, default_speaker: String, default_portrait: String) -> Array:
	var sp = "Character" if default_speaker.is_empty() else default_speaker
	var pt = default_portrait
	if raw_page.is_empty():
		return [sp, pt, ""]
	var trimmed = raw_page.lstrip(" \t")
	if not trimmed.begins_with("@"):
		return [sp, pt, raw_page]
	var colon = trimmed.find(":")
	if colon == -1:
		return [sp, pt, raw_page]
	var prefix = trimmed.substr(1, colon - 1).strip_edges()
	var open_b = prefix.find("[")
	var parsed_sp = prefix
	var parsed_pt = pt
	if open_b != -1:
		if not prefix.ends_with("]"):
			return [sp, pt, raw_page]
		parsed_sp = prefix.substr(0, open_b).strip_edges()
		parsed_pt = prefix.substr(open_b + 1, prefix.length() - open_b - 2).strip_edges()
	if parsed_sp.is_empty() or parsed_sp.find("[") != -1 or parsed_sp.find("]") != -1 or parsed_pt.find("[") != -1 or parsed_pt.find("]") != -1:
		return [sp, pt, raw_page]
	var body = trimmed.substr(colon + 1).lstrip(" \t")
	return [parsed_sp, parsed_pt, body]

static func is_escalate(delivery: String) -> bool:
	return delivery.to_lower() == "escalate"

# Numeric key counting entries into an escalate scene; the next visit shows that line.
static func visit_key(scene_id: String) -> String:
	return "visit:" + scene_id

static func build_pages(text: String, say_it_all: bool, random_state_ref: Array, random_line_ref: Array, delivery: String = "", visit_index: int = 0) -> Array[String]:
	var pages = split_pages(text)
	if pages.is_empty():
		return [NO_DIALOGUE]
	if is_escalate(delivery):
		return [pages[clampi(visit_index, 0, pages.size() - 1)]]
	if say_it_all:
		return pages
	if random_line_ref[0] < 0:
		random_line_ref[0] = splitmix64(random_state_ref, pages.size())
	var idx = mini(random_line_ref[0], pages.size() - 1)
	return [pages[idx]]

static func seed_state(seed_val: int) -> int:
	return seed_val

static func _shr_u64(x: int, n: int) -> int:
	return (x >> n) & ((1 << (64 - n)) - 1)

static func _u64_lt(a: int, b: int) -> bool:
	return (a ^ SIGN_MASK) < (b ^ SIGN_MASK)

static func _u64_mod(val: int, count: int) -> int:
	if val >= 0:
		return val % count
	var half_pow: int = (9223372036854775807 % count + 1) % count
	var pow2_64_mod: int = (half_pow * 2) % count
	var rem: int = val % count
	if rem < 0:
		rem += count
	return (pow2_64_mod + rem) % count

static func splitmix64(state_ref: Array, count: int) -> int:
	if count <= 0:
		return 0
	var half_pow: int = (9223372036854775807 % count + 1) % count
	var threshold: int = (half_pow * 2) % count

	var state: int = state_ref[0]
	var value: int = 0
	while true:
		state += SPLITMIX_STEP
		value = state
		value = (value ^ _shr_u64(value, 30)) * SPLITMIX_MUL1
		value = (value ^ _shr_u64(value, 27)) * SPLITMIX_MUL2
		value ^= _shr_u64(value, 31)
		if not _u64_lt(value, threshold):
			break
	state_ref[0] = state
	return _u64_mod(value, count)

static func u64_to_string(val: int) -> String:
	if val >= 0:
		return str(val)
	var digits: String = ""
	var current: int = val
	while current != 0:
		var half: int = (current >> 1) & 0x7FFFFFFFFFFFFFFF
		var half_div5: int = half / 5
		var half_rem5: int = half % 5
		var rem: int = (half_rem5 * 2) + (current & 1)
		var next_val: int = half_div5
		if rem >= 10:
			rem -= 10
			next_val += 1
		digits = str(rem) + digits
		current = next_val
	return digits

# True for a decimal that fits in a uint64.
static func is_u64_string(s: String) -> bool:
	var t = s.strip_edges()
	if t.is_empty() or t.length() > 20:
		return false
	for i in range(t.length()):
		var c = t.unicode_at(i)
		if c < 48 or c > 57:
			return false
	return t.length() < 20 or t <= "18446744073709551615"

static func string_to_u64(s: String) -> int:
	var trimmed = s.strip_edges()
	var result: int = 0
	for i in range(trimmed.length()):
		var c = trimmed.unicode_at(i)
		if c >= 48 and c <= 57:
			result = result * 10 + (c - 48)
	return result
