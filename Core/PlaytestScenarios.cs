using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DialogueMachine.Core;

// Definitions only. Traces, history and coverage never enter the authored document.
public sealed class PlaytestScenario
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New scenario";
    public string StartStateId { get; set; } = "";
    public int Seed { get; set; }
    public Dictionary<string, int> NumericOverrides { get; set; } = [];
    public Dictionary<string, string> TextOverrides { get; set; } = [];
    public List<ReplayStep> Steps { get; set; } = [];
    public List<ScenarioAssertion> Assertions { get; set; } = [];
    public bool RequireEnding { get; set; } = true;
    public int MaxSteps { get; set; } = 1000;
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class ReplayStep
{
    public string Kind { get; set; } = "Choice";
    public string SceneId { get; set; } = "";
    public Guid? ChoiceId { get; set; }
    public string? Fingerprint { get; set; }
    public string? Label { get; set; }
    public string? Key { get; set; }
    public int? Number { get; set; }
    public string? Text { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class ScenarioAssertion
{
    public string Kind { get; set; } = "Numeric";
    public string Key { get; set; } = "";
    public int? Number { get; set; }
    public string? Text { get; set; }
    public string? SceneId { get; set; }
    public Guid? ChoiceId { get; set; }
    public string? EventType { get; set; }
    public string? Parameter { get; set; }
    public int? Count { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public static class ScenarioStore
{
    public const int MaximumSteps = 10000;
    public static PlaytestScenario Clone(PlaytestScenario scenario)
        => JsonSerializer.Deserialize<PlaytestScenario>(JsonSerializer.Serialize(scenario, ProjectStore.Options), ProjectStore.Options)!;
    internal static void Normalize(DialogueProject project)
    {
        project.PlaytestScenarios ??= [];
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scenario in project.PlaytestScenarios)
        {
            if (scenario is null || string.IsNullOrWhiteSpace(scenario.Id) || !ids.Add(scenario.Id) || string.IsNullOrWhiteSpace(scenario.Name))
                throw new InvalidDataException("Each playtest scenario needs a unique ID and a non-empty name.");
            scenario.NumericOverrides ??= []; scenario.TextOverrides ??= []; scenario.Steps ??= []; scenario.Assertions ??= [];
            if (scenario.Steps.Count > MaximumSteps || scenario.Steps.Any(s => s is null) || scenario.Assertions.Any(a => a is null) || scenario.TextOverrides.Values.Any(v => v is null))
                throw new InvalidDataException("Scenario steps/assertions cannot be null; replay sequences are limited to 10,000 steps and text overrides need string values.");
            // Stale/unsupported references remain available for repair, never rebound by label/index.
        }
    }
    public static void Save(DialogueProject project, PlaytestScenario draft)
    {
        if (string.IsNullOrWhiteSpace(draft.Name)) throw new ArgumentException("Give the scenario a name.");
        if (project.PlaytestScenarios.Any(s => s.Id != draft.Id && s.Name.Equals(draft.Name.Trim(), StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("Choose a unique scenario name.");
        var errors = ScenarioReplay.CheckDefinition(draft);
        if (errors.Count > 0) throw new ArgumentException(errors[0].Message);
        var copy = Clone(draft); copy.Name = copy.Name.Trim();
        var index = project.PlaytestScenarios.FindIndex(s => s.Id == draft.Id);
        if (index < 0) project.PlaytestScenarios.Add(copy); else project.PlaytestScenarios[index] = copy;
    }
    public static PlaytestScenario Duplicate(DialogueProject project, PlaytestScenario source)
    {
        var copy = Clone(source); copy.Id = Guid.NewGuid().ToString("N");
        var name = source.Name + " (copy)"; var suffix = 2;
        while (project.PlaytestScenarios.Any(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) name = source.Name + $" (copy {suffix++})";
        copy.Name = name; project.PlaytestScenarios.Add(copy); return copy;
    }
    public static string Fingerprint(DialogueState scene, Choice? choice)
    {
        // V1 semantic signature: layout, title, notes and extension data are not runtime contracts.
        var json = JsonSerializer.Serialize(new
        {
            scene.State, scene.Type, scene.Text, scene.SayItAll, scene.Speaker, scene.ShopData.ShopId,
            Destination = choice is null ? scene.DefaultNextId : string.IsNullOrWhiteSpace(choice.NextStateId) ? scene.DefaultNextId : choice.NextStateId,
            Response = choice?.ButtonText, Visibility = choice?.Visibility,
            Requirements = choice?.Requirements.Select(r => new { r.Type, r.TargetId, r.Operator, r.Value }),
            ResponseActions = choice?.AfterChoiceActions.Select(a => new { a.Type, a.TargetId, a.Amount, a.Parameter }),
            ExitActions = scene.DefaultAfterActions.Select(a => new { a.Type, a.TargetId, a.Amount, a.Parameter })
        });
        if (choice?.RequirementMatch == RequirementMatch.Any) json += "|any";
        return "v1:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }
    public static ReplayStep Reference(DialogueState scene, string kind, Choice? choice = null)
        => new() { Kind = kind, SceneId = scene.State, ChoiceId = choice?.InternalId, Fingerprint = Fingerprint(scene, choice), Label = choice?.ButtonText };
    // Safe identity/variable renames preserve previously valid recordings. Already-stale steps
    // retain their old signature and fail explicitly; ordinary content edits never call this.
    internal static Action CaptureValidReferences(DialogueProject project)
    {
        var valid = new List<(ReplayStep Step, DialogueState Scene, Choice? Choice)>();
        var scenes = project.ListOfCharacterStates.GroupBy(s => s.State).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single(), StringComparer.Ordinal);
        foreach (var step in project.PlaytestScenarios.SelectMany(s => s.Steps))
        {
            if (string.IsNullOrWhiteSpace(step.SceneId)) continue;
            if (!scenes.TryGetValue(step.SceneId, out var scene)) continue;
            var choices = step.Kind == "Choice" ? scene.Choices.Where(c => c.InternalId == step.ChoiceId).ToArray() : [];
            if (step.Kind == "Choice" && choices.Length != 1) continue;
            var choice = choices.FirstOrDefault();
            if (step.Fingerprint == Fingerprint(scene, choice)) valid.Add((step, scene, choice));
        }
        return () => { foreach (var (step, scene, choice) in valid) { step.SceneId = scene.State; step.Fingerprint = Fingerprint(scene, choice); step.Label = choice?.ButtonText; } };
    }
}
