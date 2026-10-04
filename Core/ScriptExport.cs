using System.Globalization;
using System.Text;

namespace DialogueMachine.Core;

public sealed record ScriptExportOptions(
    bool IncludeConditionsAndVisibility = true,
    bool IncludeResponseActions = true,
    bool IncludeSceneActions = true,
    bool IncludeNotes = false);

// A read-only authoring export: never evaluate conditions or traverse story paths.
public static class ScriptExport
{
    public static string Markdown(DialogueProject project, ScriptExportOptions? options = null)
    {
        options ??= new();
        var scenes = project.ListOfCharacterStates;
        var destinations = new Dictionary<string, DialogueState>(StringComparer.Ordinal);
        foreach (var scene in scenes) destinations.TryAdd(scene.State, scene);
        string Destination(string? id) => string.IsNullOrWhiteSpace(id) ? "Unset destination"
            : id == "[EXIT]" ? "End conversation"
            : destinations.TryGetValue(id, out var target) ? Inline(target.Title) + " [" + Inline(id) + "]"
            : "Missing target [" + Inline(id) + "]";
        var sb = new StringBuilder();
        sb.Append("# ").Append(Inline(project.ProjectName)).Append("\n\nStart → ").Append(Destination(project.Entry)).Append("\n\n");
        sb.Append("Inline [if …], [else] and [/if] markers retain all authored branches; substitutions remain literal.\n\n");
        // Entry first, then original project order, including unreachable and duplicate-ID scenes.
        var entry = scenes.FindIndex(s => s.State == project.Entry);
        foreach (var index in Enumerable.Range(0, scenes.Count).OrderBy(i => i == entry ? 0 : 1))
        {
            var scene = scenes[index];
            sb.Append("## ").Append(Inline(scene.Title)).Append(" [").Append(Inline(scene.State)).Append("]\n\n");
            sb.Append("**Speaker:** ").Append(Inline(scene.Speaker)).Append("\n\n");
            sb.Append("**Delivery:** ").Append(scene.SayItAll ? "Ordered dialogue pages" : "One random candidate per visit").Append("\n\n");
            if (scene.Type != "Dialogue") sb.Append("**Scene type:** ").Append(Inline(scene.Type)).Append("\n\n");
            if (scene.Type == "Shop") sb.Append("**Shop:** ").Append(Inline(scene.ShopData.ShopId)).Append("\n\n");
            var pages = Normalize(scene.Text).Split('\n');
            for (var page = 0; page < pages.Length; page++)
                sb.Append("**").Append(scene.SayItAll ? "Page " : "Candidate ").Append(page + 1).Append(":** ")
                    .Append(pages[page].Length == 0 ? "(Empty page)" : Escape(DialogueEngine.PlainText(pages[page]))).Append("\n\n");
            if (scene.Choices.Count > 0) sb.Append("**Choices (authored order):**\n\n");
            for (var i = 0; i < scene.Choices.Count; i++)
            {
                var choice = scene.Choices[i];
                var inherited = string.IsNullOrWhiteSpace(choice.NextStateId);
                // CommonMark needs one more indent column for choices numbered 10 and above.
                var indent = new string(' ', (i + 1).ToString(CultureInfo.InvariantCulture).Length + 2);
                sb.Append(i + 1).Append(". ").Append(Escape(DialogueEngine.PlainText(choice.ButtonText)).Replace("\n", "\n" + indent))
                    .Append(" → ").Append(inherited ? "Inherited continue → " : "").Append(Destination(inherited ? scene.DefaultNextId : choice.NextStateId)).Append("\n");
                if (options.IncludeConditionsAndVisibility)
                {
                    var visibility = choice.Visibility switch { VisibilityMode.Hide => "Hide (0)", VisibilityMode.Disable => "Disable (1)", VisibilityMode.Always => "Always (2)", _ => "Unknown (" + ((int)choice.Visibility).ToString(CultureInfo.InvariantCulture) + ")" };
                    sb.Append('\n').Append(indent).Append("Visibility: ").Append(visibility).Append("\n");
                    sb.Append('\n').Append(indent).Append("Conditions (all must pass): ").Append(choice.Requirements.Count == 0 ? "None" : string.Join("; ", choice.Requirements.Select(r => Inline(r.Type + " " + r.TargetId + " " + r.Operator + " " + r.Value)))).Append("\n");
                }
                if (options.IncludeResponseActions) Actions(sb, choice.AfterChoiceActions, "Response actions (in order)", indent);
                sb.Append('\n');
            }
            sb.Append("**Continue →** ").Append(Destination(scene.DefaultNextId)).Append("\n\n");
            if (options.IncludeSceneActions) Actions(sb, scene.DefaultAfterActions, "Scene exit actions (after response actions, in order)", "");
            if (options.IncludeNotes && scene.Metadata.TryGetValue("notes", out var notes) && !string.IsNullOrEmpty(notes))
                sb.Append("**Writer’s notes:**\n\n").Append(Escape(notes)).Append("\n\n");
        }
        return sb.ToString();
    }

    private static void Actions(StringBuilder sb, List<DialogueAction> actions, string label, string indent)
    {
        sb.Append('\n').Append(indent).Append("**").Append(label).Append(":**").Append(actions.Count == 0 ? " None\n\n" : "\n\n");
        for (var i = 0; i < actions.Count; i++)
        {
            var action = actions[i];
            var detail = action.Type is "Event" or "Script" ? "parameter: " + (action.Parameter ?? "(none)")
                : "add " + action.Amount.ToString("+0;-0;0", CultureInfo.InvariantCulture);
            var continuation = indent + new string(' ', (i + 1).ToString(CultureInfo.InvariantCulture).Length + 2);
            sb.Append(indent).Append(i + 1).Append(". ").Append(Escape(action.Type + " " + action.TargetId + " — " + detail).Replace("\n", "\n" + continuation)).Append('\n');
        }
        if (actions.Count > 0) sb.Append('\n');
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');
    private static string Inline(string text) => Escape(Normalize(text).Replace('\n', ' '));
    private static string Escape(string text)
    {
        var sb = new StringBuilder();
        foreach (var c in Normalize(text))
        {
            if (c == '&') sb.Append("&amp;");
            else if (c == '<') sb.Append("&lt;");
            else if (c == '>') sb.Append("&gt;");
            else if (c == '\n') sb.Append("  \n");
            else { if (c is '\\' or '`' or '*' or '_' or '{' or '}' or '[' or ']' or '(' or ')' or '#' or '+' or '-' or '.' or '!' or '|' or '~' or '=') sb.Append('\\'); sb.Append(c); }
        }
        return sb.ToString();
    }
}
