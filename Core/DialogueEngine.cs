using System.Globalization;
using System.Text.RegularExpressions;

namespace DialogueMachine.Core;

public static class DialogueEngine
{
    private static readonly string[] Operators = ["==", "!=", ">", "<", ">=", "<="];
    public static bool Compare(int actual, string op, int target) => op switch
    { "==" => actual == target, "!=" => actual != target, ">" => actual > target, "<" => actual < target, ">=" => actual >= target, "<=" => actual <= target, _ => false };
    public static string Key(string type, string id) => (type == "Inventory" ? "inv:" : "aff:") + id;
    public static bool RequirementsMet(Choice c, IReadOnlyDictionary<string, int> vars, out string reason)
    {
        foreach (var r in c.Requirements)
        {
            if (r.Type is not ("Affinity" or "Inventory") || string.IsNullOrWhiteSpace(r.TargetId) || !int.TryParse(r.Value, out var target))
            { reason = "This condition is incomplete or invalid."; return false; }
            var key = Key(r.Type, r.TargetId);
            var actual = vars.GetValueOrDefault(key);
            if (!Compare(actual, r.Operator, target)) { reason = $"Requires {key} {r.Operator} {target} · current {actual}"; return false; }
        }
        reason = ""; return true;
    }
    public static IEnumerable<string> DiscoverVariables(DialogueProject p) => VariableStore.DiscoverNumeric(p);
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
                if (string.IsNullOrWhiteSpace(a.TargetId) || a.Type is not ("Affinity" or "Inventory" or "Event" or "Script")) Error(s.State, "An action needs a supported type and target ID.");
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
        var old = state.State; SceneGroups.PreserveMemberships(p, () => state.State = id);
        if (p.EntryStateId == old) p.EntryStateId = id;
        // Repairing an imported reserved ID must keep intentional ending targets intact.
        if (old == "[EXIT]") return;
        foreach (var s in p.ListOfCharacterStates)
        {
            if (s.DefaultNextId == old) s.DefaultNextId = id;
            foreach (var c in s.Choices) if (c.NextStateId == old) c.NextStateId = id;
        }
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

public sealed class Simulation
{
    public DialogueProject Project { get; }
    public Dictionary<string, int> Variables { get; private set; }
    public List<string> Events { get; private set; } = [];
    public DialogueState? Current { get; private set; }
    public int PageIndex { get; private set; }
    public string[] Pages { get; private set; } = [];
    public List<string> Transcript { get; private set; } = [];
    private int randomLine = -1;
    private readonly Stack<Snapshot> history = new();
    public bool CanBack => history.Count > 0;
    public bool MorePages => PageIndex < Pages.Length - 1;
    public Simulation(DialogueProject p, string? start = null)
    {
        if (p.ListOfCharacterStates.Any(s => s.State == "[EXIT]")) throw new InvalidOperationException("[EXIT] is reserved for endings. Rename the scene before playtesting.");
        Project = ProjectStore.Clone(p);
        Variables = DialogueEngine.DiscoverVariables(p).ToDictionary(k => k, k => k.StartsWith("inv:") ? Math.Max(0, p.InitialVariables.GetValueOrDefault(k)) : p.InitialVariables.GetValueOrDefault(k));
        Enter(start ?? p.Entry);
    }
    public void RefreshText()
    {
        if (Current is null) return;
        var previous = Pages.ElementAtOrDefault(PageIndex);
        RebuildText();
        if (previous is not null && previous != Pages[PageIndex])
        {
            Transcript.Add("— Preview refreshed —");
            Transcript.Add(Current.Speaker + ": " + DialogueEngine.PlainText(Pages[PageIndex]));
        }
    }
    public void SetVariable(string key, int value)
    {
        if (!Variables.TryGetValue(key, out var previous)) throw new ArgumentException("Choose an existing test variable.", nameof(key));
        if (key.StartsWith("inv:", StringComparison.Ordinal)) value = Math.Max(0, value);
        if (value == previous) return;
        SaveSnapshot(); Variables[key] = value;
        Transcript.Add($"— Test variable {key}: {previous} → {value} —");
        RefreshText();
    }
    public void RerollLine()
    {
        if (Current is null || Current.SayItAll) return;
        SaveSnapshot(); randomLine = -1;
        Transcript.Add("— Random line rerolled —"); RefreshText();
    }
    private void RebuildText()
    {
        if (Current is null) return;
        var text = DialogueEngine.ResolveText(Current.Text, Variables, Project.TextVariables);
        var pages = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (pages.Length == 0) Pages = ["(No dialogue)"];
        else if (!Current.SayItAll)
        {
            if (randomLine < 0) randomLine = Random.Shared.Next(pages.Length);
            Pages = [pages[Math.Min(randomLine, pages.Length - 1)]];
        }
        else Pages = pages;
        PageIndex = Math.Min(PageIndex, Pages.Length - 1);
    }
    private void Enter(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || id == "[EXIT]") { Current = null; Pages = []; PageIndex = 0; Transcript.Add("— Conversation ended —"); return; }
        Current = Project.ListOfCharacterStates.FirstOrDefault(s => s.State == id) ?? throw new InvalidOperationException($"Scene ‘{id}’ does not exist.");
        PageIndex = 0; randomLine = -1; RebuildText(); Transcript.Add(Current.Speaker + ": " + DialogueEngine.PlainText(Pages[0]));
        if (Current.Type == "Shop") Events.Add("Shop preview: " + Current.ShopData.ShopId + " (external game integration)");
    }
    public void NextPage()
    {
        if (!MorePages) return;
        SaveSnapshot(); PageIndex++; Transcript.Add(Current!.Speaker + ": " + DialogueEngine.PlainText(Pages[PageIndex]));
    }
    public void Choose(Choice choice)
    {
        if (Current is null || !Current.Choices.Contains(choice)) throw new InvalidOperationException("Select a choice from the current scene.");
        if (MorePages) throw new InvalidOperationException("Finish the dialogue pages before choosing.");
        if (choice.Visibility != VisibilityMode.Always && !DialogueEngine.RequirementsMet(choice, Variables, out var reason)) throw new InvalidOperationException(reason);
        Move(string.IsNullOrWhiteSpace(choice.NextStateId) ? Current.DefaultNextId : choice.NextStateId, choice);
    }
    public void Continue() { if (MorePages) NextPage(); else Move(Current?.DefaultNextId, null); }
    private void Move(string? id, Choice? choice)
    {
        if (!string.IsNullOrWhiteSpace(id) && id != "[EXIT]" && !Project.ListOfCharacterStates.Any(s => s.State == id)) throw new InvalidOperationException($"Scene ‘{id}’ does not exist. No actions were applied.");
        SaveSnapshot();
        if (choice is not null) { Transcript.Add("You: " + DialogueEngine.PlainText(DialogueEngine.ResolveText(choice.ButtonText, Variables, Project.TextVariables))); Apply(choice.AfterChoiceActions); }
        if (Current is not null) Apply(Current.DefaultAfterActions);
        Enter(id);
    }
    private void Apply(IEnumerable<DialogueAction> actions)
    {
        foreach (var a in actions)
        {
            if (a.Type is "Event" or "Script") { Events.Add($"{a.Type}: {a.TargetId}({a.Parameter}) — logged only"); continue; }
            var key = DialogueEngine.Key(a.Type, a.TargetId);
            var amount = Math.Clamp((long)Variables.GetValueOrDefault(key) + a.Amount, a.Type == "Inventory" ? 0 : int.MinValue, int.MaxValue);
            Variables[key] = (int)amount;
        }
    }
    private void SaveSnapshot() => history.Push(new(Current?.State, PageIndex, randomLine, Pages.ToArray(), new(Variables), Events.ToList(), Transcript.ToList()));
    public void Back()
    {
        if (!history.TryPop(out var s)) return;
        Current = Project.ListOfCharacterStates.FirstOrDefault(n => n.State == s.Id); PageIndex = s.Page; randomLine = s.RandomLine; Pages = s.Pages; Variables = s.Variables; Events = s.Events; Transcript = s.Transcript;
    }
    private sealed record Snapshot(string? Id, int Page, int RandomLine, string[] Pages, Dictionary<string, int> Variables, List<string> Events, List<string> Transcript);
}
