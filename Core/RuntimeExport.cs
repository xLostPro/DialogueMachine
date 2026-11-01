using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DialogueMachine.Core;

// A separate handoff leaves desktop format 1 and the legacy website export unchanged.
public sealed record RuntimeHandoff(int SchemaVersion, string Contract, string StoryJson, string StorySha256,
    string StartingScene, int Seed, Dictionary<string, int> NumericValues, Dictionary<string, string> TextValues,
    int MaxOperations);

public static class RuntimeExport
{
    public const string Contract = "dialogue-machine.website.v1";
    public static RuntimeHandoff Build(DialogueProject project, int seed = 0, int maxOperations = 1000) => Create(project, seed, maxOperations);
    public static RuntimeHandoff Create(DialogueProject project, int seed = 0, int maxOperations = 1000)
    {
        if (maxOperations is < 1 or > 10000) throw new ArgumentException("Runtime operation limit must be between 1 and 10,000.");
        var errors = DialogueEngine.Validate(project).Where(d => d.Severity == "Error").ToList();
        if (errors.Count > 0) throw new InvalidDataException("Repair story errors before runtime export: " + string.Join("; ", errors.Select(d => $"{d.StateId}: {d.Message}")));
        var numeric = DialogueEngine.DiscoverVariables(project).ToDictionary(k => k,
            k => k.StartsWith("inv:", StringComparison.Ordinal) ? Math.Max(0, project.InitialVariables.GetValueOrDefault(k)) : project.InitialVariables.GetValueOrDefault(k), StringComparer.Ordinal);
        var invalid = numeric.Keys.FirstOrDefault(k => !VariableStore.ValidNumericId(k));
        if (invalid is not null) throw new InvalidDataException($"Runtime numeric ID ‘{invalid}’ must use aff: or inv: followed by letters, digits, underscores, dots or hyphens. Rename it in Variables & characters; the desktop project remains openable.");
        foreach (var scene in project.ListOfCharacterStates)
            if (scene.Choices.Any(c => c.InternalId == Guid.Empty) || scene.Choices.Select(c => c.InternalId).Distinct().Count() != scene.Choices.Count)
                throw new InvalidDataException($"Scene ‘{scene.State}’ has empty or duplicate response identities. Duplicate/recreate the affected response and re-record scenarios before runtime export.");
        var story = ProjectStore.WebExport(project, forEngine: true);
        return new(1, Contract, story, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(story))),
            project.Entry, seed, numeric, new(project.TextVariables, StringComparer.Ordinal), maxOperations);
    }
    public static string Serialize(RuntimeHandoff handoff) => JsonSerializer.Serialize(handoff, ProjectStore.Options);
    public static string Slug(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "story";
        var sb = new StringBuilder();
        var hyphen = false;
        foreach (var ch in name.Trim())
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
                hyphen = false;
            }
            else if (ch is '-' or '_')
            {
                sb.Append(ch);
                hyphen = false;
            }
            else if (!hyphen && sb.Length > 0)
            {
                sb.Append('-');
                hyphen = true;
            }
        }
        var slug = sb.ToString().Trim('-', '_');
        return string.IsNullOrEmpty(slug) ? "story" : slug;
    }
}
