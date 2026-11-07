using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace DialogueMachine.Runtime
{
    // The one copy of the story rules: desktop playtest, the harness and Unity all run this code.
    public static class DialogueRules
    {
        public const string Exit = "[EXIT]";
        public const string NoDialogue = "(No dialogue)";

        public static bool Compare(int actual, string op, int target)
        {
            switch (op)
            {
                case "==": return actual == target;
                case "!=": return actual != target;
                case ">": return actual > target;
                case "<": return actual < target;
                case ">=": return actual >= target;
                case "<=": return actual <= target;
                default: return false;
            }
        }

        public static string Key(string type, string id) => (type == "Inventory" ? "inv:" : "aff:") + id;

        public static bool RequirementMet(string type, string targetId, string op, string value, IReadOnlyDictionary<string, int> vars, IReadOnlyDictionary<string, string> textVars, out string reason)
        {
            if (type == "Text")
            {
                // Text conditions compare exactly; a missing text variable counts as "".
                if (string.IsNullOrWhiteSpace(targetId) || (op != "==" && op != "!="))
                {
                    reason = "This condition is incomplete or invalid.";
                    return false;
                }
                var actualText = textVars.GetValueOrDefault(targetId) ?? "";
                var targetText = value ?? "";
                if (TextMatches(actualText, op, targetText))
                {
                    reason = "";
                    return true;
                }
                reason = $"Requires {targetId} {op} \"{targetText}\" · current \"{actualText}\"";
                return false;
            }
            if ((type != "Affinity" && type != "Inventory") || string.IsNullOrWhiteSpace(targetId) || !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var target))
            {
                reason = "This condition is incomplete or invalid.";
                return false;
            }
            var key = Key(type, targetId);
            var actual = vars.GetValueOrDefault(key);
            if (!Compare(actual, op, target))
            {
                reason = $"Requires {key} {op} {target} · current {actual}";
                return false;
            }
            reason = "";
            return true;
        }

        public static bool TextMatches(string actual, string op, string target) => (actual == target) == (op == "==");

        public static bool RequirementsMet(StoryChoice choice, IReadOnlyDictionary<string, int> vars, IReadOnlyDictionary<string, string> textVars, out string reason)
        {
            var any = choice.AnyRequirement && choice.Requirements.Count > 0;
            string first = null;
            foreach (var r in choice.Requirements)
            {
                if (RequirementMet(r.Type, r.TargetId, r.Operator, r.Value, vars, textVars, out var why)) { if (any) { reason = ""; return true; } }
                else if (!any) { reason = why; return false; }
                else if (first == null) first = why;
            }
            reason = first ?? "";
            return !any;
        }

        public static string ResolveText(string text, IReadOnlyDictionary<string, int> vars, IReadOnlyDictionary<string, string> textVars)
        {
            // Match the innermost block first, allowing nested conditions and signed thresholds.
            // A quoted value ([if calling == "Plumber"]) compares a text variable; only == and != apply.
            var pattern = @"\[if\s+([\w:.\-]+)\s*(?:(==|!=|>=|<=|>|<)\s*(-?\d+)|(==|!=)\s*""([^""\[\]]*)"")\]((?:(?!\[if\s|\[/if\])[\s\S])*)\[/if\]";
            for (var pass = 0; pass < 64; pass++)
            {
                var changed = false;
                text = Regex.Replace(text, pattern, m =>
                {
                    changed = true;
                    var key = m.Groups[1].Value;
                    var parts = Regex.Split(m.Groups[6].Value, @"\[else\]", RegexOptions.IgnoreCase);
                    bool met;
                    if (m.Groups[4].Success) met = TextMatches(textVars.GetValueOrDefault(key) ?? "", m.Groups[4].Value, m.Groups[5].Value);
                    else
                    {
                        var value = vars.GetValueOrDefault(key, vars.GetValueOrDefault("inv:" + key, vars.GetValueOrDefault("aff:" + key)));
                        met = int.TryParse(m.Groups[3].Value, out var target) && Compare(value, m.Groups[2].Value, target);
                    }
                    return met ? parts[0] : parts.ElementAtOrDefault(1) ?? "";
                }, RegexOptions.IgnoreCase);
                if (!changed) break;
            }
            return Regex.Replace(text, @"\{([\w:.\-]+)\}", m =>
            {
                var key = m.Groups[1].Value;
                if (textVars.TryGetValue(key, out var value)) return value;
                if (vars.TryGetValue(key, out var number)) return number.ToString(CultureInfo.InvariantCulture);
                // {a:key} writes "a" or "an" before the text variable's value.
                if (key.StartsWith("a:", StringComparison.Ordinal) && textVars.TryGetValue(key.Substring(2), out var noun) && noun.Length > 0)
                    return ("aeiouAEIOU".IndexOf(noun[0]) >= 0 ? "an " : "a ") + noun;
                return m.Value;
            });
        }

        public static string PlainText(string text) => Regex.Replace(text, @"\[/?(?:b|i|u|colour(?:=[^\]]*)?)\]", "", RegexOptions.IgnoreCase);

        public static int ApplyAmount(string type, int before, int amount) => (int)Math.Clamp((long)before + amount, type == "Inventory" ? 0 : int.MinValue, int.MaxValue);

        // Set and Toggle write flags (0/1) in the affinity namespace; Amount is the value Set writes.
        public static bool IsVariableAction(string type) => type == "Affinity" || type == "Inventory" || type == "Set" || type == "Toggle";

        public static int ApplyAction(string type, int before, int amount)
        {
            if (type == "Set") return amount;
            if (type == "Toggle") return before == 0 ? 1 : 0;
            return ApplyAmount(type, before, amount);
        }

        public static int ClampNumber(string key, int value) => key.StartsWith("inv:", StringComparison.Ordinal) ? Math.Max(0, value) : value;

        public static string[] SplitPages(string text) => text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        private static readonly Regex PageHeaderPattern = new Regex(@"^\s*@([^:\[\]\r\n]+?)(?:\s*\[([^\[\]\r\n]+)\])?\s*:\s*(.*)$", RegexOptions.Singleline);

        public static void ParsePageHeader(string rawPage, string defaultSpeaker, string defaultPortrait, out string speaker, out string portrait, out string text)
        {
            speaker = string.IsNullOrEmpty(defaultSpeaker) ? "Character" : defaultSpeaker;
            portrait = defaultPortrait ?? "";
            text = rawPage ?? "";
            if (string.IsNullOrEmpty(rawPage)) return;
            var m = PageHeaderPattern.Match(rawPage);
            if (!m.Success) return;
            var parsedSpeaker = m.Groups[1].Value.Trim();
            if (string.IsNullOrEmpty(parsedSpeaker)) return;
            speaker = parsedSpeaker;
            if (m.Groups[2].Success) portrait = m.Groups[2].Value.Trim();
            text = m.Groups[3].Value;
        }

        public static bool IsEscalate(string delivery) => string.Equals(delivery, "escalate", StringComparison.OrdinalIgnoreCase);

        // Numeric key counting entries into an escalate scene; the next visit shows that line.
        public static string VisitKey(string sceneId) => "visit:" + sceneId;

        // A random line is picked once per scene visit, so refreshing text never advances the stream.
        public static string[] BuildPages(string text, bool sayItAll, ref ulong randomState, ref int randomLine, string delivery = null, int visitIndex = 0)
        {
            var pages = SplitPages(text);
            if (pages.Length == 0) return new[] { NoDialogue };
            if (IsEscalate(delivery))
                return new[] { pages[Math.Clamp(visitIndex, 0, pages.Length - 1)] };
            if (sayItAll) return pages;
            if (randomLine < 0) randomLine = SplitMix64(ref randomState, pages.Length);
            return new[] { pages[Math.Min(randomLine, pages.Length - 1)] };
        }

        public static ulong SeedState(int seed) => unchecked((ulong)(long)seed);

        // SplitMix64 with explicit state: portable integer arithmetic, independent of System.Random versions.
        public static int SplitMix64(ref ulong state, int count)
        {
            if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
            ulong value;
            var threshold = unchecked(0UL - (ulong)count) % (ulong)count;
            do
            {
                state = unchecked(state + 0x9E3779B97F4A7C15UL);
                value = state;
                value = unchecked((value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL);
                value = unchecked((value ^ (value >> 27)) * 0x94D049BB133111EBUL);
                value ^= value >> 31;
            } while (value < threshold);
            return (int)(value % (ulong)count);
        }
    }
}
