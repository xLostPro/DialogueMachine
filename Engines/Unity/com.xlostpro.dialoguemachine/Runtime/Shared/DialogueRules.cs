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

        public static bool RequirementMet(string type, string targetId, string op, string value, IReadOnlyDictionary<string, int> vars, out string reason)
        {
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

        public static bool RequirementsMet(StoryChoice choice, IReadOnlyDictionary<string, int> vars, out string reason)
        {
            var any = choice.AnyRequirement && choice.Requirements.Count > 0;
            string first = null;
            foreach (var r in choice.Requirements)
            {
                if (RequirementMet(r.Type, r.TargetId, r.Operator, r.Value, vars, out var why)) { if (any) { reason = ""; return true; } }
                else if (!any) { reason = why; return false; }
                else if (first == null) first = why;
            }
            reason = first ?? "";
            return !any;
        }

        public static string ResolveText(string text, IReadOnlyDictionary<string, int> vars, IReadOnlyDictionary<string, string> textVars)
        {
            // Match the innermost block first, allowing nested conditions and signed thresholds.
            var pattern = @"\[if\s+([\w:.\-]+)\s*(==|!=|>=|<=|>|<)\s*(-?\d+)\]((?:(?!\[if\s|\[/if\])[\s\S])*)\[/if\]";
            for (var pass = 0; pass < 64; pass++)
            {
                var changed = false;
                text = Regex.Replace(text, pattern, m =>
                {
                    changed = true;
                    var key = m.Groups[1].Value;
                    var value = vars.GetValueOrDefault(key, vars.GetValueOrDefault("inv:" + key, vars.GetValueOrDefault("aff:" + key)));
                    var parts = Regex.Split(m.Groups[4].Value, @"\[else\]", RegexOptions.IgnoreCase);
                    return int.TryParse(m.Groups[3].Value, out var target) && Compare(value, m.Groups[2].Value, target) ? parts[0] : parts.ElementAtOrDefault(1) ?? "";
                }, RegexOptions.IgnoreCase);
                if (!changed) break;
            }
            return Regex.Replace(text, @"\{([\w:.\-]+)\}", m => textVars.TryGetValue(m.Groups[1].Value, out var value) ? value : vars.TryGetValue(m.Groups[1].Value, out var number) ? number.ToString(CultureInfo.InvariantCulture) : m.Value);
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

        // A random line is picked once per scene visit, so refreshing text never advances the stream.
        public static string[] BuildPages(string text, bool sayItAll, ref ulong randomState, ref int randomLine, string delivery = null, int visitIndex = 0)
        {
            var pages = SplitPages(text);
            if (pages.Length == 0) return new[] { NoDialogue };
            if (string.Equals(delivery, "escalate", StringComparison.OrdinalIgnoreCase))
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
