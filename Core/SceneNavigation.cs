namespace DialogueMachine.Core;

public enum SceneIssueFilter { All, WithIssues, Errors, Warnings, WithoutIssues }
public sealed record OutlineFilter(string Query = "", string? Speaker = null, string? Tag = null, SceneIssueFilter Issues = SceneIssueFilter.All, string? GroupId = null);
public sealed record NavigationScene(int Index, string Id, string Title, string Speaker, IReadOnlyList<string> Tags, string SearchText, bool HasErrors, bool HasWarnings)
{
    public IReadOnlyList<string> GroupIds { get; init; } = [];
}
public sealed record DestinationOption(string Label, string? Id, int? SceneIndex = null) { public override string ToString() => Label; }
public sealed record DestinationResults(IReadOnlyList<DestinationOption> Matches, int TotalCount);
public sealed record SceneConnection(string Label, string? TargetId, int? TargetSceneIndex);

// A read-only snapshot rebuilt after edits, so typing filters never revalidates the story.
public sealed class SceneNavigation
{
    public const int DestinationLimit = 200;
    public IReadOnlyList<NavigationScene> Scenes { get; }
    public IReadOnlyList<string> Speakers { get; }
    public IReadOnlyList<string> Tags { get; }
    private readonly Dictionary<string, NavigationScene> byId = new(StringComparer.Ordinal);
    private readonly Dictionary<int, List<SceneConnection>> outgoing = [];
    private readonly Dictionary<int, List<int>> incoming = [];
    public SceneNavigation(DialogueProject project, IReadOnlyList<Diagnostic> diagnostics)
    {
        var errors = diagnostics.Where(d => d.Severity == "Error").Select(d => d.StateId).ToHashSet(StringComparer.Ordinal);
        var warnings = diagnostics.Where(d => d.Severity == "Warning").Select(d => d.StateId).ToHashSet(StringComparer.Ordinal);
        var keys = SceneGroups.Keys(project);
        var memberships = new Dictionary<(string, int), List<string>>();
        foreach (var group in project.SceneGroups)
            foreach (var member in group.Scenes)
            {
                var key = (member.StateId, member.Occurrence);
                if (!memberships.TryGetValue(key, out var groups)) memberships[key] = groups = [];
                groups.Add(group.Id);
            }
        Scenes = project.ListOfCharacterStates.Select((scene, i) =>
        {
            var title = scene.Title ?? scene.State; var speaker = scene.Speaker ?? "Character";
            var tags = (scene.Metadata.GetValueOrDefault("tags", "") ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var search = string.Join(" ", title, scene.State, speaker, scene.Text, string.Join(" ", scene.Choices.Select(c => c.ButtonText)), string.Join(" ", tags), scene.Metadata.GetValueOrDefault("notes", ""));
            return new NavigationScene(i, scene.State, title, speaker, tags, search, errors.Contains(scene.State), warnings.Contains(scene.State)) { GroupIds = memberships.GetValueOrDefault(keys[scene]) ?? [] };
        }).ToArray();
        Speakers = Scenes.Select(s => s.Speaker).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        Tags = Scenes.SelectMany(s => s.Tags).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var scene in Scenes) if (scene.Id != "[EXIT]") byId.TryAdd(scene.Id, scene);
        foreach (var scene in Scenes)
        {
            var source = project.ListOfCharacterStates[scene.Index];
            var links = new List<SceneConnection> { Connection("Continue", source.DefaultNextId) };
            links.AddRange(source.Choices.Select((choice, i) => Connection($"Choice {i + 1}" + (string.IsNullOrWhiteSpace(choice.NextStateId) ? " (continue fallback)" : ""), string.IsNullOrWhiteSpace(choice.NextStateId) ? source.DefaultNextId : choice.NextStateId)));
            outgoing[scene.Index] = links;
            foreach (var target in links.Where(l => l.TargetSceneIndex is not null).Select(l => l.TargetSceneIndex!.Value).Distinct())
            {
                if (!incoming.TryGetValue(target, out var sources)) incoming[target] = sources = [];
                sources.Add(scene.Index);
            }
        }
    }
    public IReadOnlyList<NavigationScene> Filter(OutlineFilter filter) => Scenes.Where(s => Matches(s, filter)).ToArray();
    public static bool Matches(NavigationScene scene, OutlineFilter filter) =>
        scene.SearchText.Contains(filter.Query, StringComparison.OrdinalIgnoreCase)
        && (filter.Speaker is null || scene.Speaker.Equals(filter.Speaker, StringComparison.OrdinalIgnoreCase))
        && (filter.Tag is null || scene.Tags.Contains(filter.Tag, StringComparer.OrdinalIgnoreCase))
        && (filter.GroupId is null || (filter.GroupId == "" ? scene.GroupIds.Count == 0 : scene.GroupIds.Contains(filter.GroupId, StringComparer.Ordinal)))
        && (filter.Issues switch { SceneIssueFilter.WithIssues => scene.HasErrors || scene.HasWarnings, SceneIssueFilter.Errors => scene.HasErrors, SceneIssueFilter.Warnings => scene.HasWarnings, SceneIssueFilter.WithoutIssues => !scene.HasErrors && !scene.HasWarnings, _ => true });
    public NavigationScene? Resolve(string? id) => id is not null && byId.TryGetValue(id, out var scene) ? scene : null;
    public IReadOnlyList<NavigationScene> Incoming(int index) => incoming.TryGetValue(index, out var sources) ? sources.Select(i => Scenes[i]).ToArray() : [];
    public IReadOnlyList<SceneConnection> Outgoing(int index) => outgoing.GetValueOrDefault(index) ?? [];
    private SceneConnection Connection(string label, string? id) => new(label, id, Resolve(id)?.Index);
    public IReadOnlyList<DestinationOption> Destinations(bool allowFallback, string? current)
    {
        var options = new List<DestinationOption> { new(allowFallback ? "Use scene's continue destination" : "No continue destination", null), new("End conversation", "[EXIT]") };
        options.AddRange(byId.Values.Select(s => new DestinationOption(s.Title + "  [" + s.Id + "]", s.Id, s.Index)));
        if (!string.IsNullOrWhiteSpace(current) && current != "[EXIT]" && Resolve(current) is null) options.Add(new("Missing: " + current, current));
        return options;
    }
    public static DestinationResults FindDestinations(IReadOnlyList<DestinationOption> options, string query)
    {
        var matches = new List<DestinationOption>(); var count = 0;
        foreach (var option in options)
            if (option.Label.Contains(query, StringComparison.OrdinalIgnoreCase))
            { count++; if (matches.Count < DestinationLimit) matches.Add(option); }
        return new(matches, count);
    }
}
