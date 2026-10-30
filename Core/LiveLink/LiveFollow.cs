using DialogueMachine.Runtime;

namespace DialogueMachine.Core.LiveLink;

public sealed record LiveRunnerState(string ClientId, string Engine, string RunnerId, string RunnerName, string ProjectName, string StorySha256, string Status,
    string? SceneId, int PageIndex, string? ChoiceId, IReadOnlyDictionary<string, int> Numeric, IReadOnlyDictionary<string, string> Text)
{
    public string Key => ClientId + "/" + RunnerId;
    public string Label => $"{Engine[..1].ToUpperInvariant()}{Engine[1..]} · {RunnerName} ({ProjectName})";
}
public sealed record LiveRunnerEvent(string RunnerKey, string Type, string TargetId, string? Parameter);
public sealed record LiveFollowStatus(bool Highlight, string? Notice, bool Outdated);

// What a running game is doing. This is UI state: it never touches the project or history.
public sealed class LiveFollow
{
    private const int MaxRunners = 32, MaxEvents = 200;
    private readonly List<LiveRunnerState> runners = [];
    private readonly List<string> events = [];
    private bool stopped;

    public IReadOnlyList<LiveRunnerState> Runners => runners;
    public LiveRunnerState? Followed { get; private set; }
    public bool Ended { get; private set; }
    public bool Disconnected { get; private set; }
    public string? CurrentSceneId { get; private set; }
    public HashSet<string> TrailScenes { get; } = new(StringComparer.Ordinal);
    public HashSet<(string From, string To)> TrailLinks { get; } = [];
    public HashSet<ChoiceCoverage> TrailChoices { get; } = [];
    public IReadOnlyList<string> Events => events;

    public void Apply(LiveRunnerState state)
    {
        var index = runners.FindIndex(r => r.Key == state.Key);
        if (index >= 0) runners[index] = state; else runners.Add(state);
        if (runners.Count > MaxRunners) runners.RemoveAt(runners.FindIndex(r => r.Key != Followed?.Key));
        var starting = state.Status == "started";
        if (starting && Followed?.Key != state.Key) { Reset(); Followed = state; }
        else if (Followed is null && !stopped) Followed = state; // DM opened mid-conversation.
        if (starting) stopped = false;
        if (Followed?.Key != state.Key) return;
        Followed = state; Ended = state.Status == "ended"; Disconnected = false;
        if (starting) CurrentSceneId = null; // A new conversation doesn't link to the last one.
        if (state.SceneId is not { } scene) return;
        if (CurrentSceneId is { } from)
        {
            if (Guid.TryParse(state.ChoiceId, out var choice)) TrailChoices.Add(new(from, choice));
            if (from != scene) TrailLinks.Add((from, scene));
        }
        CurrentSceneId = scene; TrailScenes.Add(scene);
    }
    public void AddEvent(LiveRunnerEvent entry)
    {
        if (entry.RunnerKey != Followed?.Key) return;
        events.Add(entry.Type + " · " + entry.TargetId + (string.IsNullOrEmpty(entry.Parameter) ? "" : " · " + entry.Parameter));
        if (events.Count > MaxEvents) events.RemoveAt(0);
    }
    public void Follow(string key)
    {
        if (key == Followed?.Key || runners.FirstOrDefault(r => r.Key == key) is not { } runner) return;
        Reset(); stopped = false; Followed = runner; Ended = runner.Status == "ended"; CurrentSceneId = runner.SceneId;
        if (runner.SceneId is { } scene) TrailScenes.Add(scene);
    }
    public void Stop() { Reset(); stopped = true; }
    public void Clear() { Reset(); runners.Clear(); stopped = false; }
    public void RemoveClient(string clientId)
    {
        runners.RemoveAll(r => r.ClientId == clientId);
        if (Followed?.ClientId == clientId) Disconnected = true; // Keep the trail: it is still useful to read.
    }
    private void Reset()
    {
        Followed = null; Ended = Disconnected = false; CurrentSceneId = null;
        TrailScenes.Clear(); TrailLinks.Clear(); TrailChoices.Clear(); events.Clear();
    }

    public LiveFollowStatus Check(DialogueProject? project, string? storySha256)
    {
        if (Followed is not { } runner) return new(false, null, false);
        if (project is null || runner.ProjectName != project.ProjectName) return new(false, "Game is running " + runner.ProjectName, false);
        if (CurrentSceneId is { } scene && project.ListOfCharacterStates.All(s => s.State != scene)) return new(false, $"Scene ‘{scene}’ isn't in this story.", false);
        var outdated = storySha256 is not null && runner.StorySha256 != storySha256;
        return new(true, outdated ? "Game is on an older version." : null, outdated);
    }

    // The bridge sends positions, not text, so the line comes from the open story. A random
    // scene's pick stays inside the game, so every candidate is returned.
    public string? CurrentLine(DialogueProject? project)
    {
        if (Followed is not { } runner || project?.ListOfCharacterStates.FirstOrDefault(s => s.State == CurrentSceneId) is not { } scene) return null;
        var pages = DialogueRules.SplitPages(DialogueEngine.ResolveText(scene.Text, runner.Numeric, runner.Text));
        if (pages.Length == 0) return "(No dialogue)";
        return scene.SayItAll ? pages[Math.Clamp(runner.PageIndex, 0, pages.Length - 1)] : string.Join("\n", pages);
    }
}
