using System.Text.RegularExpressions;
using DialogueMachine.Runtime;

namespace DialogueMachine.Core;

public static class DialogueEngine
{
    private static readonly string[] Operators = ["==", "!=", ">", "<", ">=", "<="];
    public static bool Compare(int actual, string op, int target) => DialogueRules.Compare(actual, op, target);
    public static string Key(string type, string id) => DialogueRules.Key(type, id);
    public static bool RequirementsMet(Choice c, IReadOnlyDictionary<string, int> vars, out string reason)
    {
        var any = c.RequirementMatch == RequirementMatch.Any && c.Requirements.Count > 0;
        string? first = null;
        foreach (var r in c.Requirements)
        {
            if (DialogueRules.RequirementMet(r.Type, r.TargetId, r.Operator, r.Value, vars, out var why)) { if (any) { reason = ""; return true; } }
            else if (!any) { reason = why; return false; }
            else first ??= why;
        }
        reason = first ?? ""; return !any;
    }
    public static IEnumerable<string> DiscoverVariables(DialogueProject p) => VariableStore.DiscoverNumeric(p);
    public static string ResolveText(string text, IReadOnlyDictionary<string, int> vars, IReadOnlyDictionary<string, string> textVars) => DialogueRules.ResolveText(text, vars, textVars);
    public static string PlainText(string text) => DialogueRules.PlainText(text);
    public static List<Diagnostic> Validate(DialogueProject p)
    {
        var issues = new List<Diagnostic>();
        void Error(string id, string msg) => issues.Add(new(id, "Error", msg));
        void Warning(string id, string msg) => issues.Add(new(id, "Warning", msg));
        var variables = DiscoverVariables(p).ToHashSet(StringComparer.Ordinal);
        void CheckText(string id, string text, string field)
        {
            if (!DialogueText.ValidConditions(text)) Error(id, $"{field}: inline conditional tags are malformed. Use [if aff:trust >= 1]…[else]…[/if], with at most one else per block and up to 64 nested blocks.");
            if (!DialogueText.ValidFormatting(text)) Warning(id, $"{field}: formatting tags are unclosed, mismatched or malformed. Check [b], [i], [u] and [colour=…] tags.");
            foreach (Match m in Regex.Matches(text, @"\{([\w:.\-]+)\}"))
                if (!p.TextVariables.ContainsKey(m.Groups[1].Value) && !variables.Contains(m.Groups[1].Value)) Warning(id, $"{field}: unknown substitution {{{m.Groups[1].Value}}}. Add it in Variables & characters or correct its ID.");
        }
        if (p.FormatVersion != DialogueProject.CurrentFormatVersion) Error("", $"Unsupported project format {p.FormatVersion}.");
        var ids = p.ListOfCharacterStates.Select(s => s.State).ToHashSet();
        foreach (var group in p.ListOfCharacterStates.GroupBy(s => s.State).Where(g => g.Count() > 1)) Error(group.Key, "Duplicate scene ID. Rename one of these scenes before playtesting.");
        if (!ids.Contains(p.Entry)) Error("", "Choose an existing scene as the story's start.");
        var edges = new Dictionary<string, HashSet<string>>();
        foreach (var s in p.ListOfCharacterStates)
        {
            if (s.State == "[EXIT]") Error(s.State, "[EXIT] is reserved for endings. Rename this scene before playtesting or exporting.");
            if (s.Type is not ("Dialogue" or "Shop")) Error(s.State, "Scene type must be Dialogue or Shop.");
            CheckText(s.State, s.Text, "Dialogue");
            var targets = new HashSet<string>(); edges[s.State] = targets;
            void Check(string? target, string label)
            {
                if (string.IsNullOrWhiteSpace(target)) { Error(s.State, $"{label} has no destination or fallback."); return; }
                targets.Add(target);
                if (target != "[EXIT]" && !ids.Contains(target)) Error(s.State, $"{label} points to missing scene ‘{target}’.");
            }
            if (!string.IsNullOrWhiteSpace(s.DefaultNextId)) Check(s.DefaultNextId, "Continue");
            if (s.Choices.Count == 0 && string.IsNullOrWhiteSpace(s.DefaultNextId)) Error(s.State, "Scene has no choices or continue destination. Select End conversation for an ending.");
            if (string.IsNullOrWhiteSpace(s.Text)) Warning(s.State, "This scene has no dialogue yet.");
            if (s.Type == "Shop" && string.IsNullOrWhiteSpace(s.ShopData.ShopId)) Warning(s.State, "Shop scene needs a shop ID for the game integration.");
            for (var choiceIndex = 0; choiceIndex < s.Choices.Count; choiceIndex++)
            {
                var c = s.Choices[choiceIndex];
                CheckText(s.State, c.ButtonText, $"Choice {choiceIndex + 1} response");
                if (!Enum.IsDefined(c.Visibility)) Error(s.State, $"Choice {choiceIndex + 1}: visibility must be Hide, Disable or Always.");
                Check(string.IsNullOrWhiteSpace(c.NextStateId) ? s.DefaultNextId : c.NextStateId, $"Choice ‘{PlainText(c.ButtonText)}’");
                if (string.IsNullOrWhiteSpace(c.ButtonText)) Warning(s.State, "A player choice has no label.");
                foreach (var r in c.Requirements)
                    if (r.Type is not ("Affinity" or "Inventory") || string.IsNullOrWhiteSpace(r.TargetId) || !Operators.Contains(r.Operator) || !int.TryParse(r.Value, out _)) Error(s.State, "A choice condition needs a valid variable, comparison and integer value.");
            }
            foreach (var a in s.DefaultAfterActions.Concat(s.Choices.SelectMany(c => c.AfterChoiceActions)))
                if (string.IsNullOrWhiteSpace(a.TargetId) || a.Type is not ("Affinity" or "Inventory" or "Event" or "Script" or "Set" or "Toggle")) Error(s.State, "An action needs a supported type and target ID.");
        }
        var reachable = new HashSet<string>(); var queue = new Queue<string>(); queue.Enqueue(p.Entry);
        while (queue.TryDequeue(out var id)) if (reachable.Add(id) && edges.TryGetValue(id, out var next)) foreach (var n in next) queue.Enqueue(n);
        foreach (var s in p.ListOfCharacterStates.Where(s => !reachable.Contains(s.State))) Warning(s.State, "This scene is unreachable from the story's start.");
        // Cycles are valid dialogue. Warn only when there is no path to an explicit exit.
        var canEnd = new HashSet<string> { "[EXIT]" };
        var incoming = new Dictionary<string, HashSet<string>>();
        foreach (var (id, targets) in edges)
            foreach (var target in targets)
            {
                if (!incoming.TryGetValue(target, out var sources)) incoming[target] = sources = [];
                sources.Add(id);
            }
        queue.Enqueue("[EXIT]");
        while (queue.TryDequeue(out var end))
            if (incoming.TryGetValue(end, out var sources))
                foreach (var source in sources) if (canEnd.Add(source)) queue.Enqueue(source);
        foreach (var s in p.ListOfCharacterStates.Where(s => reachable.Contains(s.State) && !canEnd.Contains(s.State))) Warning(s.State, "No path from this scene reaches an ending. Check for a closed loop.");
        return issues;
    }
    public static void Rename(DialogueProject p, DialogueState state, string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id == "[EXIT]" || p.ListOfCharacterStates.Any(s => s != state && s.State == id)) throw new ArgumentException("Use a unique, non-empty ID. [EXIT] is reserved.");
        var rebindScenarios = ScenarioStore.CaptureValidReferences(p);
        var old = state.State; SceneGroups.PreserveMemberships(p, () => state.State = id);
        if (p.EntryStateId == old) p.EntryStateId = id;
        foreach (var scenario in p.PlaytestScenarios)
        {
            if (scenario.StartStateId == old) scenario.StartStateId = id;
            foreach (var step in scenario.Steps) if (step.SceneId == old) step.SceneId = id;
            foreach (var assertion in scenario.Assertions) if (assertion.Kind == "Ending" && assertion.SceneId == old) assertion.SceneId = id;
        }
        // Repairing an imported reserved ID must keep intentional ending targets intact.
        if (old == "[EXIT]") { rebindScenarios(); return; }
        foreach (var s in p.ListOfCharacterStates)
        {
            if (s.DefaultNextId == old) s.DefaultNextId = id;
            foreach (var c in s.Choices) if (c.NextStateId == old) c.NextStateId = id;
        }
        rebindScenarios();
    }
    public static void AutoLayout(DialogueProject p)
    {
        var scenes = p.ListOfCharacterStates.GroupBy(s => s.State).ToDictionary(g => g.Key, g => g.First());
        var layers = new Dictionary<string, int>(); var queue = new Queue<(string Id, int Layer)>(); queue.Enqueue((p.Entry, 0));
        while (queue.TryDequeue(out var pair))
        {
            if (layers.ContainsKey(pair.Id)) continue;
            if (!scenes.TryGetValue(pair.Id, out var s)) continue;
            layers[pair.Id] = pair.Layer;
            foreach (var id in s.Choices.Select(c => string.IsNullOrWhiteSpace(c.NextStateId) ? s.DefaultNextId : c.NextStateId).Append(s.DefaultNextId).Where(id => !string.IsNullOrWhiteSpace(id) && id != "[EXIT]")) queue.Enqueue((id!, pair.Layer + 1));
        }
        var nextY = new Dictionary<int, double>();
        var disconnectedLayer = layers.Values.DefaultIfEmpty(0).Max() + 1;
        foreach (var s in p.ListOfCharacterStates)
        {
            var layer = layers.GetValueOrDefault(s.State, disconnectedLayer);
            s.VisualX = 80 + layer * 360; s.VisualY = nextY.GetValueOrDefault(layer, 80);
            nextY[layer] = s.VisualY + Math.Max(300, GraphGeometry.NodeHeight(s) + 48);
        }
    }
}
