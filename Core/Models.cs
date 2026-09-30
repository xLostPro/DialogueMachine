using System.Text.Json;
using System.Text.Json.Serialization;

namespace DialogueMachine.Core;

// The website's PascalCase schema is intentionally retained for round-trip compatibility.
public sealed class DialogueProject
{
    public int FormatVersion { get; set; } = 1;
    public string ProjectName { get; set; } = "Untitled story";
    public string CharacterId { get; set; } = "new_character";
    public string? EntryStateId { get; set; }
    public List<DialogueState> ListOfCharacterStates { get; set; } = [];
    public Dictionary<string, int> InitialVariables { get; set; } = [];
    public Dictionary<string, string> TextVariables { get; set; } = new() { ["player_name"] = "Traveller", ["npc_name"] = "Mara" };
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
    [JsonIgnore] public string Entry => EntryStateId ?? ListOfCharacterStates.FirstOrDefault()?.State ?? "";
}

public sealed class DialogueState
{
    public string State { get; set; } = "scene_" + Guid.NewGuid().ToString("N")[..8];
    public string Type { get; set; } = "Dialogue";
    public string Text { get; set; } = "";
    public string? DefaultNextId { get; set; } = "[EXIT]";
    public bool SayItAll { get; set; } = true;
    public ShopData ShopData { get; set; } = new();
    public List<Choice> Choices { get; set; } = [];
    public List<DialogueAction> DefaultAfterActions { get; set; } = [];
    public Dictionary<string, string> Metadata { get; set; } = [];
    public double VisualX { get; set; } = 80;
    public double VisualY { get; set; } = 80;
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
    [JsonIgnore] public string Title => Metadata.GetValueOrDefault("title", State);
    [JsonIgnore] public string Speaker => Metadata.GetValueOrDefault("speaker", "Character");
}

public sealed class Choice
{
    public Guid InternalId { get; set; } = Guid.NewGuid();
    public string ButtonText { get; set; } = "New choice";
    public string? NextStateId { get; set; }
    public VisibilityMode Visibility { get; set; } = VisibilityMode.Disable;
    public List<Requirement> Requirements { get; set; } = [];
    public List<DialogueAction> AfterChoiceActions { get; set; } = [];
    public double VisualX { get; set; } = 150;
    public double VisualY { get; set; } = 150;
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}
public sealed class ShopData
{
    public string ShopId { get; set; } = "";
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}
public sealed class DialogueAction
{
    public string Type { get; set; } = "Affinity";
    public string TargetId { get; set; } = "";
    public int Amount { get; set; }
    public string? Parameter { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}
public sealed class Requirement
{
    public string Type { get; set; } = "Affinity";
    public string TargetId { get; set; } = "";
    public string Operator { get; set; } = ">=";
    public string Value { get; set; } = "0";
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}
public enum VisibilityMode { Hide, Disable, Always }
public sealed record Diagnostic(string StateId, string Severity, string Message);
public sealed record RecoveryRecord(string? FilePath, DateTimeOffset SavedAt, DialogueProject Project);
