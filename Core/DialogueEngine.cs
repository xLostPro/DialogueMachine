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
    public static IEnumerable<string> DiscoverVariables(DialogueProject p)
    {
        var keys = new HashSet<string>(p.InitialVariables.Keys, StringComparer.Ordinal);
        foreach (var s in p.ListOfCharacterStates)
        {
            foreach (var a in s.DefaultAfterActions.Concat(s.Choices.SelectMany(c => c.AfterChoiceActions)))
                if (a.Type is "Affinity" or "Inventory" && !string.IsNullOrWhiteSpace(a.TargetId)) keys.Add(Key(a.Type, a.TargetId));
            foreach (var r in s.Choices.SelectMany(c => c.Requirements))
                if (r.Type is "Affinity" or "Inventory" && !string.IsNullOrWhiteSpace(r.TargetId)) keys.Add(Key(r.Type, r.TargetId));
            foreach (Match m in Regex.Matches(s.Text + " " + string.Join(" ", s.Choices.Select(c => c.ButtonText)), @"(?:aff|inv):[\w.\-]+", RegexOptions.IgnoreCase)) keys.Add(m.Value);
        }
        return keys.Order(StringComparer.Ordinal);
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
    public static List<Diagnostic> Validate(DialogueProject p)
    {
        var issues = new List<Diagnostic>();
        void Error(string id, string msg) => issues.Add(new(id, "Error", msg));
        void Warning(string id, string msg) => issues.Add(new(id, "Warning", msg));
        var ids = p.ListOfCharacterStates.Select(s => s.State).ToHashSet();
        foreach (var group in p.ListOfCharacterStates.GroupBy(s => s.State).Where(g => g.Count() > 1)) Error(group.Key, "Duplicate scene ID. Rename one of these scenes before playtesting.");
        if (!ids.Contains(p.Entry)) Error("", "Choose an existing scene as the story's start.");
        var edges = new Dictionary<string, HashSet<string>>();
        foreach (var s in p.ListOfCharacterStates)
        {
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
            foreach (var c in s.Choices)
            {
                Check(string.IsNullOrWhiteSpace(c.NextStateId) ? s.DefaultNextId : c.NextStateId, $"Choice ‘{PlainText(c.ButtonText)}’");
                if (string.IsNullOrWhiteSpace(c.ButtonText)) Warning(s.State, "A player choice has no label.");
                foreach (var r in c.Requirements)
                    if (r.Type is not ("Affinity" or "Inventory") || string.IsNullOrWhiteSpace(r.TargetId) || !Operators.Contains(r.Operator) || !int.TryParse(r.Value, out _)) Error(s.State, "A choice condition needs a valid variable, comparison and integer value.");
            }
            foreach (var a in s.DefaultAfterActions.Concat(s.Choices.SelectMany(c => c.AfterChoiceActions)))
                if (string.IsNullOrWhiteSpace(a.TargetId) || a.Type is not ("Affinity" or "Inventory" or "Event" or "Script")) Error(s.State, "An action needs a supported type and target ID.");
            var open = Regex.Matches(s.Text, @"\[if\s", RegexOptions.IgnoreCase).Count;
            var close = Regex.Matches(s.Text, @"\[/if\]", RegexOptions.IgnoreCase).Count;
            if (open != close || Regex.IsMatch(ResolveText(s.Text, p.InitialVariables, p.TextVariables), @"\[if\s|\[/if\]", RegexOptions.IgnoreCase)) Error(s.State, "Inline conditional tags are malformed. Use [if aff:trust >= 1]…[else]…[/if].");
        }
        var reachable = new HashSet<string>(); var queue = new Queue<string>(); queue.Enqueue(p.Entry);
        while (queue.TryDequeue(out var id)) if (reachable.Add(id) && edges.TryGetValue(id, out var next)) foreach (var n in next) queue.Enqueue(n);
        foreach (var s in p.ListOfCharacterStates.Where(s => !reachable.Contains(s.State))) Warning(s.State, "This scene is unreachable from the story's start.");
        // Cycles are valid dialogue. Warn only when there is no path to an explicit exit.
        var canEnd = new HashSet<string> { "[EXIT]" };
        bool grew;
        do { grew = false; foreach (var (id, targets) in edges) if (targets.Any(canEnd.Contains)) grew |= canEnd.Add(id); } while (grew);
        foreach (var s in p.ListOfCharacterStates.Where(s => reachable.Contains(s.State) && !canEnd.Contains(s.State))) Warning(s.State, "No path from this scene reaches an ending. Check for a closed loop.");
        return issues;
    }
    public static void Rename(DialogueProject p, DialogueState state, string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id == "[EXIT]" || p.ListOfCharacterStates.Any(s => s != state && s.State == id)) throw new ArgumentException("Use a unique, non-empty ID. [EXIT] is reserved.");
        var old = state.State; state.State = id;
        if (p.EntryStateId == old) p.EntryStateId = id;
        foreach (var s in p.ListOfCharacterStates)
        {
            if (s.DefaultNextId == old) s.DefaultNextId = id;
            foreach (var c in s.Choices) if (c.NextStateId == old) c.NextStateId = id;
        }
    }
    public static void AutoLayout(DialogueProject p)
    {
        var layers = new Dictionary<string, int>(); var queue = new Queue<(string Id, int Layer)>(); queue.Enqueue((p.Entry, 0));
        while (queue.TryDequeue(out var pair))
        {
            if (layers.ContainsKey(pair.Id)) continue;
            var s = p.ListOfCharacterStates.FirstOrDefault(s => s.State == pair.Id); if (s is null) continue;
            layers[pair.Id] = pair.Layer;
            foreach (var id in s.Choices.Select(c => c.NextStateId ?? s.DefaultNextId).Append(s.DefaultNextId).Where(id => id is not null && id != "[EXIT]")) queue.Enqueue((id!, pair.Layer + 1));
        }
        var rows = new Dictionary<int, int>();
        foreach (var s in p.ListOfCharacterStates)
        {
            var layer = layers.GetValueOrDefault(s.State, layers.Values.DefaultIfEmpty(0).Max() + 1);
            s.VisualX = 80 + layer * 360; s.VisualY = 80 + rows.GetValueOrDefault(layer) * 300; rows[layer] = rows.GetValueOrDefault(layer) + 1;
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
    private readonly Stack<Snapshot> history = new();
    public bool CanBack => history.Count > 0;
    public bool MorePages => PageIndex < Pages.Length - 1;
    public Simulation(DialogueProject p, string? start = null)
    {
        Project = ProjectStore.Clone(p);
        Variables = DialogueEngine.DiscoverVariables(p).ToDictionary(k => k, k => k.StartsWith("inv:") ? Math.Max(0, p.InitialVariables.GetValueOrDefault(k)) : p.InitialVariables.GetValueOrDefault(k));
        Enter(start ?? p.Entry);
    }
    public void RefreshText()
    {
        if (Current is null) return;
        var text = DialogueEngine.ResolveText(Current.Text, Variables, Project.TextVariables);
        var pages = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        Pages = pages.Length == 0 ? ["(No dialogue)"] : !Current.SayItAll ? [pages[Random.Shared.Next(pages.Length)]] : pages;
        PageIndex = Math.Min(PageIndex, Pages.Length - 1);
    }
    private void Enter(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || id == "[EXIT]") { Current = null; Pages = []; PageIndex = 0; Transcript.Add("— Conversation ended —"); return; }
        Current = Project.ListOfCharacterStates.FirstOrDefault(s => s.State == id) ?? throw new InvalidOperationException($"Scene ‘{id}’ does not exist.");
        PageIndex = 0; RefreshText(); Transcript.Add(Current.Speaker + ": " + DialogueEngine.PlainText(Pages[0]));
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
    private void SaveSnapshot() => history.Push(new(Current?.State, PageIndex, Pages.ToArray(), new(Variables), Events.ToList(), Transcript.ToList()));
    public void Back()
    {
        if (!history.TryPop(out var s)) return;
        Current = Project.ListOfCharacterStates.FirstOrDefault(n => n.State == s.Id); PageIndex = s.Page; Pages = s.Pages; Variables = s.Variables; Events = s.Events; Transcript = s.Transcript;
    }
    private sealed record Snapshot(string? Id, int Page, string[] Pages, Dictionary<string, int> Variables, List<string> Events, List<string> Transcript);
}
