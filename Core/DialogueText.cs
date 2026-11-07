using System.Text.RegularExpressions;

namespace DialogueMachine.Core;

internal static class DialogueText
{
    private static readonly Regex ConditionalTags = new(@"\[(?:/?if\b|else\b)[^\[\]]*(?:\]|(?=\[)|$)", RegexOptions.IgnoreCase);
    private static readonly Regex ConditionHeader = new(@"^\[if\s+([\w:.\-]+)\s*(?:(==|!=|>=|<=|>|<)\s*(-?\d+)|(?:==|!=)\s*""[^""\[\]]*"")\]$", RegexOptions.IgnoreCase);

    // Check every branch without evaluating it against today's starting variables.
    public static List<Diagnostic> Validate(string text, string stateId, string fieldLabel)
    {
        var diagnostics = new List<Diagnostic>();
        if (!ValidConditions(text))
            diagnostics.Add(new(stateId, "Error", $"{fieldLabel}: inline conditional tags are malformed. Use [if aff:trust >= 1]…[else]…[/if], with at most one else per block and up to 64 nested blocks."));
        if (!ValidFormatting(text))
            diagnostics.Add(new(stateId, "Warning", $"{fieldLabel}: formatting tags are unclosed, mismatched or malformed. Check [b], [i], [u] and [colour=…] tags."));
        return diagnostics;
    }

    public static bool ValidConditions(string text)
    {
        var blocks = new Stack<bool>();
        foreach (Match token in ConditionalTags.Matches(text))
        {
            var header = ConditionHeader.Match(token.Value);
            if (header.Success)
            {
                if (blocks.Count >= 64 || header.Groups[3].Success && !int.TryParse(header.Groups[3].Value, out _)) return false;
                blocks.Push(false);
            }
            else if (token.Value.Equals("[else]", StringComparison.OrdinalIgnoreCase))
            {
                if (!blocks.TryPop(out var hadElse) || hadElse) return false;
                blocks.Push(true);
            }
            else if (token.Value.Equals("[/if]", StringComparison.OrdinalIgnoreCase))
            {
                if (!blocks.TryPop(out _)) return false;
            }
            else return false;
        }
        return blocks.Count == 0;
    }

    public static bool ValidFormatting(string text)
    {
        var tags = new Stack<string>();
        foreach (Match token in Regex.Matches(text, @"\[(/?)(b|i|u|colour)\b([^\[\]]*)(?:\]|$)", RegexOptions.IgnoreCase))
        {
            var tag = token.Groups[2].Value.ToLowerInvariant();
            var suffix = token.Groups[3].Value;
            var closing = token.Groups[1].Value == "/";
            if (!token.Value.EndsWith(']')) return false;
            if (tag == "colour" && !closing)
            {
                if (!suffix.StartsWith('=') || suffix.Length < 2) return false;
            }
            else if (suffix.Length != 0) return false;
            if (!closing) tags.Push(tag);
            else if (!tags.TryPop(out var open) || open != tag) return false;
        }
        return tags.Count == 0;
    }
}
