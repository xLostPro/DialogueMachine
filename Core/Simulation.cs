using DialogueMachine.Runtime;

namespace DialogueMachine.Core;

public sealed record TraceEntry(int Sequence, string Kind, string? SceneId, Guid? ChoiceId = null,
    string? Text = null, int? Page = null, string? Key = null, int? Before = null, int? After = null,
    string? EventType = null, string? EventId = null, string? Parameter = null, string? Destination = null, ulong? RandomState = null);
public sealed record StoryEnding(string SceneId, Guid? ChoiceId);

internal sealed class ReplayRandom(ulong state)
{
    public ulong State = state;
    public int Next(int count) => DialogueRules.SplitMix64(ref State, count);
}

public sealed class Simulation
{
    public DialogueProject Project { get; }
    public Dictionary<string, int> Variables { get; private set; }
    public Dictionary<string, string> TextVariables { get; private set; }
    public List<string> Events { get; private set; } = [];
    public List<TraceEntry> Trace { get; } = [];
    public List<ReplayStep> ReplaySteps { get; } = [];
    public DialogueState? Current { get; private set; }
    public StoryEnding? Ending { get; private set; }
    public int PageIndex { get; private set; }
    public string[] Pages { get; private set; } = [];
    private string[] rawPages = [];
    private int currentVisitIndex;
    public string CurrentSpeaker
    {
        get
        {
            if (Current is null) return "";
            if (rawPages.Length == 0) return Current.Speaker;
            DialogueRules.ParsePageHeader(rawPages[PageIndex], Current.Speaker, Current.Metadata.GetValueOrDefault("portrait", ""), out var sp, out _, out _);
            return sp;
        }
    }
    public string CurrentPortrait
    {
        get
        {
            if (Current is null) return "";
            var defPortrait = Current.Metadata.GetValueOrDefault("portrait", "");
            if (rawPages.Length == 0) return defPortrait;
            DialogueRules.ParsePageHeader(rawPages[PageIndex], Current.Speaker, defPortrait, out _, out var pt, out _);
            return pt;
        }
    }
    public List<string> Transcript { get; private set; } = [];
    public int Seed { get; }
    public ulong RandomState => random.State;
    private readonly ReplayRandom random;
    private readonly Dictionary<string, DialogueState> scenes;
    private readonly Dictionary<string, int> startingVariables;
    private readonly Dictionary<string, string> startingText;
    private readonly string start;
    private int randomLine = -1;
    private readonly bool record;
    private readonly bool keepHistory;
    private readonly Stack<Snapshot> history = new();
    public bool CanBack => history.Count > 0;
    public bool MorePages => PageIndex < Pages.Length - 1;
    public IReadOnlyList<Choice> AvailableChoices => Current?.Choices.Where(c => c.Visibility == VisibilityMode.Always || DialogueEngine.RequirementsMet(c, Variables, out _)).ToList() ?? [];
    // The player shows continue only without usable responses (or while delivering pages).
    public bool CanContinue => Current is not null && (MorePages || AvailableChoices.Count == 0 && !string.IsNullOrWhiteSpace(Current.DefaultNextId));

    public Simulation(DialogueProject p, string? start = null, int seed = 0,
        IReadOnlyDictionary<string, int>? numericOverrides = null, IReadOnlyDictionary<string, string>? textOverrides = null, bool recordHistory = true, bool retainHistory = true)
    {
        if (p.ListOfCharacterStates.Any(s => s.State == "[EXIT]")) throw new InvalidOperationException("[EXIT] is reserved for endings. Rename the scene before playtesting.");
        if (p.ListOfCharacterStates.GroupBy(s => s.State).Any(g => g.Count() > 1)) throw new InvalidOperationException("Duplicate scene IDs are ambiguous. Rename them before playtesting.");
        Project = ProjectStore.Clone(p); scenes = Project.ListOfCharacterStates.ToDictionary(s => s.State, StringComparer.Ordinal);
        Seed = seed; random = new(DialogueRules.SeedState(seed)); this.start = start ?? p.Entry; record = recordHistory; keepHistory = recordHistory && retainHistory;
        startingVariables = DialogueEngine.DiscoverVariables(p).ToDictionary(k => k, k => k.StartsWith("inv:", StringComparison.Ordinal) ? Math.Max(0, p.InitialVariables.GetValueOrDefault(k)) : p.InitialVariables.GetValueOrDefault(k), StringComparer.Ordinal);
        startingText = VariableStore.DiscoverText(p).ToDictionary(k => k, k => p.TextVariables.GetValueOrDefault(k, ""), StringComparer.Ordinal);
        foreach (var (key, value) in numericOverrides ?? new Dictionary<string, int>())
        {
            if (!startingVariables.ContainsKey(key)) throw new ArgumentException($"Starting numeric variable ‘{key}’ no longer exists. Edit the scenario overrides.");
            startingVariables[key] = key.StartsWith("inv:", StringComparison.Ordinal) ? Math.Max(0, value) : value;
        }
        foreach (var (key, value) in textOverrides ?? new Dictionary<string, string>())
        {
            if (!startingText.ContainsKey(key)) throw new ArgumentException($"Starting text variable ‘{key}’ no longer exists. Edit the scenario overrides.");
            startingText[key] = value;
        }
        Variables = new(startingVariables); TextVariables = new(startingText); Enter(this.start);
    }
    private Simulation(Simulation source)
    {
        Project = source.Project; scenes = source.scenes; start = source.start; Seed = source.Seed; record = false;
        startingVariables = source.startingVariables; startingText = source.startingText;
        random = new(source.random.State); Current = source.Current; Ending = source.Ending; PageIndex = source.PageIndex;
        randomLine = source.randomLine; rawPages = source.rawPages.ToArray(); Pages = source.Pages.ToArray();
        currentVisitIndex = source.currentVisitIndex;
        Variables = new(source.Variables); TextVariables = new(source.TextVariables);
    }
    // Analysis shares the isolated read-only story and copies only runtime state; no trace/history tree.
    internal Simulation Fork() => new(this);
    public void Restart()
    {
        Variables = new(startingVariables); TextVariables = new(startingText); random.State = DialogueRules.SeedState(Seed);
        currentVisitIndex = 0; history.Clear(); Trace.Clear(); ReplaySteps.Clear(); Transcript.Clear(); Events.Clear(); Ending = null; Enter(start);
    }
    private void Add(string kind, Guid? choiceId = null, string? text = null, int? page = null, string? key = null,
        int? before = null, int? after = null, string? eventType = null, string? eventId = null, string? parameter = null, string? destination = null)
    {
        if (record) Trace.Add(new(Trace.Count + 1, kind, Current?.State, choiceId, text, page, key, before, after, eventType, eventId, parameter, destination, RandomState));
    }
    private void Step(string kind, Choice? choice = null, string? key = null, int? number = null, string? text = null)
    {
        if (!record) return;
        if (Current is null)
        {
            if (Ending is not null && kind is "SetNumeric" or "SetText") ReplaySteps.Add(new() { Kind = kind, SceneId = "[EXIT]", Fingerprint = "v1:ended", Key = key, Number = number, Text = text });
            return;
        }
        var step = ScenarioStore.Reference(Current, kind, choice); step.Key = key; step.Number = number; step.Text = text; ReplaySteps.Add(step);
    }
    public void RefreshText()
    {
        if (Current is null) return;
        SaveSnapshot(); Step("Refresh"); RefreshPreview();
    }
    private void RefreshPreview()
    {
        var previous = Pages.ElementAtOrDefault(PageIndex); RebuildText();
        if (Current is not null && previous is not null && previous != Pages[PageIndex])
        {
            Add("PreviewRefresh", text: Pages[PageIndex], page: PageIndex);
            if (record) { Transcript.Add("— Preview refreshed —"); Transcript.Add(CurrentSpeaker + ": " + DialogueEngine.PlainText(Pages[PageIndex])); }
        }
    }
    public void SetVariable(string key, int value)
    {
        if (!Variables.TryGetValue(key, out var previous)) throw new ArgumentException("Choose an existing test variable.", nameof(key));
        if (key.StartsWith("inv:", StringComparison.Ordinal)) value = Math.Max(0, value);
        if (value == previous) return;
        SaveSnapshot(); Step("SetNumeric", key: key, number: value); Variables[key] = value;
        Add("VariableChange", key: key, before: previous, after: value, text: "Live override");
        if (record) Transcript.Add($"— Test variable {key}: {previous} → {value} —"); RefreshPreview();
    }
    public void SetTextVariable(string key, string value)
    {
        if (!TextVariables.TryGetValue(key, out var previous)) throw new ArgumentException("Choose an existing text variable.", nameof(key));
        if (value == previous) return;
        SaveSnapshot(); Step("SetText", key: key, text: value); TextVariables[key] = value;
        Add("TextChange", key: key, text: value, parameter: previous);
        if (record) Transcript.Add($"— Test text {key}: {previous} → {value} —"); RefreshPreview();
    }
    public void RerollLine()
    {
        if (Current is null || Current.SayItAll || DialogueRules.IsEscalate(Current.Metadata.GetValueOrDefault("delivery"))) return;
        SaveSnapshot(); Step("Reroll"); randomLine = -1;
        if (record) Transcript.Add("— Random line rerolled —"); RefreshPreview(); Add("Reroll");
    }
    private void RebuildText()
    {
        if (Current is null) return;
        var text = DialogueEngine.ResolveText(Current.Text, Variables, TextVariables);
        var delivery = Current.Metadata.GetValueOrDefault("delivery");
        rawPages = DialogueRules.BuildPages(text, Current.SayItAll, ref random.State, ref randomLine, delivery, currentVisitIndex);
        Pages = new string[rawPages.Length];
        var defPortrait = Current.Metadata.GetValueOrDefault("portrait", "");
        for (var i = 0; i < rawPages.Length; i++)
        {
            DialogueRules.ParsePageHeader(rawPages[i], Current.Speaker, defPortrait, out _, out _, out var pageText);
            Pages[i] = pageText;
        }
        PageIndex = Math.Min(PageIndex, Pages.Length - 1);
    }
    private void Deliver()
    {
        Add("Dialogue", text: Pages[PageIndex], page: PageIndex);
        if (record) Transcript.Add(CurrentSpeaker + ": " + DialogueEngine.PlainText(Pages[PageIndex]));
    }
    private void Enter(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || id == "[EXIT]")
        {
            Ending = new(Current?.State ?? start, Trace.LastOrDefault(t => t.Kind == "Choice" && t.SceneId == Current?.State)?.ChoiceId);
            Add("Ending", Ending.ChoiceId, destination: "[EXIT]"); Current = null; rawPages = []; Pages = []; PageIndex = 0;
            if (record) Transcript.Add("— Conversation ended —"); return;
        }
        Current = scenes.GetValueOrDefault(id) ?? throw new InvalidOperationException($"Scene ‘{id}’ does not exist.");
        currentVisitIndex = 0;
        if (DialogueRules.IsEscalate(Current.Metadata.GetValueOrDefault("delivery")))
        {
            var key = DialogueRules.VisitKey(Current.State);
            currentVisitIndex = Variables.GetValueOrDefault(key); Variables[key] = currentVisitIndex + 1;
        }
        PageIndex = 0; randomLine = -1; RebuildText(); Add("SceneVisit"); Deliver();
        if (Current.Type == "Shop")
        {
            Add("Integration", eventType: "Shop", eventId: Current.ShopData.ShopId);
            if (record) Events.Add("Shop preview: " + Current.ShopData.ShopId + " (external game integration)");
        }
    }
    public void NextPage()
    {
        if (!MorePages) return;
        SaveSnapshot(); Step("Page"); PageIndex++; Deliver();
    }
    public void Choose(Choice choice)
    {
        if (Current is null || !Current.Choices.Contains(choice)) throw new InvalidOperationException("Select a choice from the current scene.");
        if (MorePages) throw new InvalidOperationException("Finish the dialogue pages before choosing.");
        if (choice.Visibility != VisibilityMode.Always && !DialogueEngine.RequirementsMet(choice, Variables, out var reason)) throw new InvalidOperationException(reason);
        Move(string.IsNullOrWhiteSpace(choice.NextStateId) ? Current.DefaultNextId : choice.NextStateId, choice);
    }
    public void Continue() { if (MorePages) NextPage(); else if (Current is not null) Move(Current.DefaultNextId, null); }
    private void Move(string? id, Choice? choice)
    {
        if (!string.IsNullOrWhiteSpace(id) && id != "[EXIT]" && !scenes.ContainsKey(id)) throw new InvalidOperationException($"Scene ‘{id}’ does not exist. No actions were applied.");
        SaveSnapshot(); Step(choice is null ? "Continue" : "Choice", choice);
        if (choice is not null)
        {
            var label = DialogueEngine.ResolveText(choice.ButtonText, Variables, TextVariables);
            Add("Choice", choice.InternalId, text: label, destination: id);
            if (record) Transcript.Add("You: " + DialogueEngine.PlainText(label)); Apply(choice.AfterChoiceActions);
        }
        else Add("Continue", destination: id);
        if (Current is not null) Apply(Current.DefaultAfterActions);
        // The exit carries the chosen response for this transition, even on a revisited scene.
        if (string.IsNullOrWhiteSpace(id) || id == "[EXIT]")
        {
            Ending = new(Current!.State, choice?.InternalId); Add("Ending", choice?.InternalId, destination: "[EXIT]");
            Current = null; rawPages = []; Pages = []; PageIndex = 0; if (record) Transcript.Add("— Conversation ended —");
        }
        else Enter(id);
    }
    private void Apply(IEnumerable<DialogueAction> actions)
    {
        foreach (var a in actions)
        {
            if (a.Type is "Event" or "Script")
            {
                Add("Integration", eventType: a.Type, eventId: a.TargetId, parameter: a.Parameter);
                if (record) Events.Add($"{a.Type}: {a.TargetId}({a.Parameter}) — logged only"); continue;
            }
            if (a.Type == "SetText")
            {
                var beforeText = TextVariables.GetValueOrDefault(a.TargetId, "");
                var afterText = a.Parameter ?? "";
                TextVariables[a.TargetId] = afterText;
                Add("TextChange", key: a.TargetId, text: afterText, parameter: beforeText);
                continue;
            }
            var key = DialogueEngine.Key(a.Type, a.TargetId); var before = Variables.GetValueOrDefault(key);
            if (!DialogueRules.IsVariableAction(a.Type)) continue;
            var amount = DialogueRules.ApplyAction(a.Type, before, a.Amount);
            Variables[key] = amount; Add("VariableChange", key: key, before: before, after: amount, text: "Action");
        }
    }
    private void SaveSnapshot()
    {
        if (!keepHistory) return;
        history.Push(new(Current?.State, Ending, PageIndex, randomLine, RandomState, rawPages.ToArray(), Pages.ToArray(), currentVisitIndex, new(Variables), new(TextVariables), Events.Count, Transcript.Count, Trace.Count, ReplaySteps.Count));
    }
    public void Back()
    {
        if (!history.TryPop(out var s)) return;
        Current = s.Id is null ? null : scenes.GetValueOrDefault(s.Id); Ending = s.Ending; PageIndex = s.Page; randomLine = s.RandomLine;
        random.State = s.RandomState; rawPages = s.RawPages; Pages = s.Pages; currentVisitIndex = s.CurrentVisitIndex;
        Variables = s.Variables; TextVariables = s.TextVariables;
        Events.RemoveRange(s.Events, Events.Count - s.Events); Transcript.RemoveRange(s.Transcript, Transcript.Count - s.Transcript);
        Trace.RemoveRange(s.Trace, Trace.Count - s.Trace); ReplaySteps.RemoveRange(s.Steps, ReplaySteps.Count - s.Steps);
    }
    private sealed record Snapshot(string? Id, StoryEnding? Ending, int Page, int RandomLine, ulong RandomState, string[] RawPages, string[] Pages,
        int CurrentVisitIndex, Dictionary<string, int> Variables, Dictionary<string, string> TextVariables, int Events, int Transcript, int Trace, int Steps);
}
