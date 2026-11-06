using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;

namespace DialogueMachine.Core;

// JSON Schema for *.dialogue.json, generated from the model so it can't drift. Lets scripts and agents write projects;
// `validate` still checks what a schema can't (links, reachability, conditions).
public static class ProjectSchema
{
    public const string FileName = "dialogue-project.schema.json";

    private static readonly Dictionary<string, string> Descriptions = new(StringComparer.Ordinal)
    {
        ["DialogueProject.FormatVersion"] = "Project format version. Must be 1.",
        ["DialogueProject.ProjectName"] = "Story name. The runtime export file is named after it.",
        ["DialogueProject.EntryStateId"] = "Scene ID the story starts at. Defaults to the first scene.",
        ["DialogueProject.ListOfCharacterStates"] = "The scenes. Each scene is one node in the graph.",
        ["DialogueProject.InitialVariables"] = "Starting numeric variables, keyed 'aff:<id>' (affinity) or 'inv:<id>' (inventory, never below 0).",
        ["DialogueProject.TextVariables"] = "Starting text variables, used as {key} placeholders in text.",
        ["DialogueProject.SceneGroups"] = "Editor-only groups/chapters of scenes.",
        ["DialogueProject.Characters"] = "Editor-only speaker roster. Scenes still name their speaker in Metadata.speaker.",
        ["DialogueProject.PlaytestScenarios"] = "Saved story tests. Easiest to record in the editor.",
        ["DialogueProject.EngineExportFolder"] = "Folder that saving auto-exports <project-slug>.runtime.json into, relative to this file with '/' separators.",
        ["DialogueState.State"] = "Unique scene ID, e.g. 'arrival'. '[EXIT]' is reserved for ending the conversation.",
        ["DialogueState.Type"] = "'Dialogue', or 'Shop' to also open ShopData.ShopId in the game.",
        ["DialogueState.Text"] = "One page or line per text line. Supports [if aff:x >= 1]...[else]...[/if], {placeholders}, [b] [i] [u] [colour=#hex] tags, and '@Speaker [portrait]: text' per-line speaker overrides.",
        ["DialogueState.DefaultNextId"] = "Scene to continue to when the scene has no usable choice. '[EXIT]' ends the conversation. A scene needs choices or this.",
        ["DialogueState.SayItAll"] = "true: show every line in order as pages. false: show one random line per visit (or one line per visit with Metadata.delivery = 'escalate').",
        ["DialogueState.Choices"] = "Player responses shown after the last page.",
        ["DialogueState.DefaultAfterActions"] = "Actions run when the scene continues through DefaultNextId.",
        ["DialogueState.Metadata"] = "String keys: speaker (default 'Character'), title, portrait, notes, tags (comma-separated), entry ('true' = also started by the game), ending ('true' = intended ending), delivery ('escalate' = next line on each visit, stops on the last). Other keys pass through to the game.",
        ["DialogueState.VisualX"] = "Graph X position in the editor.",
        ["DialogueState.VisualY"] = "Graph Y position in the editor.",
        ["Choice.InternalId"] = "Stable unique choice ID (GUID).",
        ["Choice.ButtonText"] = "Response text. Supports the same conditionals and placeholders as scene text.",
        ["Choice.NextStateId"] = "Destination scene ID. Empty or null uses the scene's DefaultNextId; '[EXIT]' ends the conversation.",
        ["Choice.Visibility"] = "What happens when Requirements fail: Hide, Disable (shown greyed out) or Always (shown and usable).",
        ["Choice.Requirements"] = "Conditions for this choice, combined by RequirementMatch.",
        ["Choice.RequirementMatch"] = "All (default): every requirement must pass. Any: one is enough.",
        ["Choice.AfterChoiceActions"] = "Actions run when the player picks this choice.",
        ["ShopData.ShopId"] = "Shop ID passed to the game when Type is 'Shop'.",
        ["DialogueAction.Type"] = "Affinity/Inventory add Amount to aff:/inv: TargetId. Set writes Amount to aff:TargetId; Toggle flips it between 0 and 1. SetText writes Parameter to text variable TargetId. Event/Script are raised to the game with TargetId and Parameter.",
        ["DialogueAction.TargetId"] = "Variable name without the aff:/inv: prefix, or the event/script ID.",
        ["DialogueAction.Amount"] = "Amount to add, or the value for Set.",
        ["DialogueAction.Parameter"] = "Text value for SetText, or an optional parameter for Event/Script.",
        ["Requirement.Type"] = "Affinity checks aff:TargetId; Inventory checks inv:TargetId.",
        ["Requirement.TargetId"] = "Variable name without the aff:/inv: prefix.",
        ["Requirement.Value"] = "Integer to compare against, written as a string.",
    };

    private static readonly Dictionary<string, string[]> Allowed = new(StringComparer.Ordinal)
    {
        ["DialogueState.Type"] = ["Dialogue", "Shop"],
        ["DialogueAction.Type"] = ["Affinity", "Inventory", "Set", "Toggle", "SetText", "Event", "Script"],
        ["Requirement.Type"] = ["Affinity", "Inventory"],
        ["Requirement.Operator"] = ["==", "!=", ">", "<", ">=", "<="],
    };

    public static string Generate()
    {
        var options = new JsonSerializerOptions(ProjectStore.Options) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
        var schema = JsonSchemaExporter.GetJsonSchemaAsNode(options, typeof(DialogueProject), new JsonSchemaExporterOptions
        {
            TreatNullObliviousAsNonNullable = true,
            TransformSchemaNode = (context, node) =>
            {
                if (context.PropertyInfo is not { } property || node is not JsonObject obj) return node;
                var key = property.DeclaringType.Name + "." + property.Name;
                if (Descriptions.TryGetValue(key, out var description)) obj.Insert(0, "description", description);
                if (Allowed.TryGetValue(key, out var values)) obj["enum"] = new JsonArray(values.Select(v => (JsonNode)v).ToArray());
                if (key == "Requirement.Value") obj["pattern"] = "^-?[0-9]+$";
                return obj;
            }
        }).AsObject();
        schema.Insert(0, "$schema", "https://json-schema.org/draft/2020-12/schema");
        schema.Insert(1, "title", "Dialogue Machine project (*.dialogue.json)");
        schema.Insert(2, "description", "A Dialogue Machine story. Only ListOfCharacterStates is required; omitted fields take their defaults. Unknown fields are kept. Run `DialogueMachine.Cli validate <file>` after writing one.");
        schema["required"] = new JsonArray("ListOfCharacterStates");
        return schema.ToJsonString(new JsonSerializerOptions { WriteIndented = true, NewLine = "\n", Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n";
    }
}
