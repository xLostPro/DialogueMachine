using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DialogueMachine.Core;

public static class ProjectStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true, PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }, MaxDepth = 64
    };
    public static string Serialize(DialogueProject project) => JsonSerializer.Serialize(project, Options);
    public static DialogueProject Parse(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
        if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.EnumerateObject().Any(p => p.Name.Equals("ListOfCharacterStates", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("This file is not a Dialogue Machine project. It must contain ListOfCharacterStates.");
        var project = JsonSerializer.Deserialize<DialogueProject>(json, Options) ?? throw new InvalidDataException("The project is empty.");
        if (project.FormatVersion != DialogueProject.CurrentFormatVersion)
            throw new InvalidDataException($"Project format {project.FormatVersion} is not supported. This app supports format {DialogueProject.CurrentFormatVersion}. Use a compatible version of Dialogue Machine to open this file.");
        if (project.ListOfCharacterStates is null || project.ListOfCharacterStates.Count > 10000)
            throw new InvalidDataException("The scene list is missing or exceeds 10,000 scenes.");
        project.InitialVariables ??= []; project.TextVariables ??= []; project.SceneTemplates ??= []; project.ChoiceTemplates ??= [];
        foreach (var template in project.SceneTemplates)
            if (template is null || string.IsNullOrWhiteSpace(template.Name) || template.Scene is null) throw new InvalidDataException("Each scene template needs a name and scene.");
        foreach (var template in project.ChoiceTemplates)
            if (template is null || string.IsNullOrWhiteSpace(template.Name) || template.Choice is null) throw new InvalidDataException("Each choice template needs a name and choice.");
        foreach (var s in project.ListOfCharacterStates.Concat(project.SceneTemplates.Select(t => t.Scene)).Concat(project.ChoiceTemplates.Select(t => new DialogueState { Choices = [t.Choice] })))
        {
            if (s is null || string.IsNullOrWhiteSpace(s.State)) throw new InvalidDataException("Each scene needs a non-empty ID.");
            s.Text ??= ""; s.Metadata ??= []; s.Choices ??= []; s.DefaultAfterActions ??= []; s.ShopData ??= new();
            if (!double.IsFinite(s.VisualX) || !double.IsFinite(s.VisualY)) { s.VisualX = 80; s.VisualY = 80; }
            foreach (var c in s.Choices)
            {
                if (c is null) throw new InvalidDataException("A choice cannot be null.");
                c.ButtonText ??= ""; c.Requirements ??= []; c.AfterChoiceActions ??= [];
                if (c.Requirements.Any(r => r is null) || c.AfterChoiceActions.Any(a => a is null)) throw new InvalidDataException("Logic rows cannot be null.");
            }
            if (s.DefaultAfterActions.Any(a => a is null)) throw new InvalidDataException("Actions cannot be null.");
        }
        CharacterRoster.Normalize(project);
        project.SceneGroups ??= [];
        var groupIds = new HashSet<string>(StringComparer.Ordinal);
        var sceneKeys = SceneGroups.Keys(project).Values.ToHashSet();
        foreach (var group in project.SceneGroups)
        {
            if (group is null || string.IsNullOrWhiteSpace(group.Id) || !groupIds.Add(group.Id) || string.IsNullOrWhiteSpace(group.Name))
                throw new InvalidDataException("Each scene group needs a unique ID and a non-empty name.");
            group.Scenes ??= [];
            if (group.Scenes.Any(r => r is null || string.IsNullOrWhiteSpace(r.StateId) || r.Occurrence < 0))
                throw new InvalidDataException("Each group member needs a scene ID and a non-negative occurrence.");
            // Missing/duplicate memberships cannot attach themselves to a later inserted scene.
            var seen = new HashSet<(string, int)>();
            group.Scenes.RemoveAll(r => !sceneKeys.Contains((r.StateId, r.Occurrence)) || !seen.Add((r.StateId, r.Occurrence)));
        }
        ScenarioStore.Normalize(project);
        return project;
    }
    public static DialogueProject Load(string path)
    {
        if (new FileInfo(path).Length > 20 * 1024 * 1024) throw new InvalidDataException("Projects must be smaller than 20 MB.");
        return Parse(File.ReadAllText(path));
    }
    public static void AtomicWrite(string path, string content, Action<string>? beforeReplace = null, bool overwrite = true)
    {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var temp = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream)) { writer.Write(content); writer.Flush(); stream.Flush(true); }
            beforeReplace?.Invoke(temp);
            File.Move(temp, full, overwrite);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static string WebExport(DialogueProject project)
    {
        // The web player starts at the first scene; honour the desktop entry selection.
        var states = project.ListOfCharacterStates.OrderBy(s => s.State == project.Entry ? 0 : 1).ToList();
        return JsonSerializer.Serialize(new { ListOfCharacterStates = states }, new JsonSerializerOptions { WriteIndented = true });
    }
    public static DialogueProject Clone(DialogueProject p) => Parse(Serialize(p));
}

public sealed class ProjectHistory
{
    private readonly List<byte[]> undo = [];
    private readonly List<byte[]> redo = [];
    private string current = "";
    public string CurrentSnapshot => current;
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public void Reset(DialogueProject p) { current = ProjectStore.Serialize(p); undo.Clear(); redo.Clear(); }
    public bool Commit(DialogueProject p)
    {
        var next = ProjectStore.Serialize(p);
        if (next == current) return false;
        undo.Add(Compress(current)); if (undo.Count > 100) undo.RemoveAt(0);
        current = next; redo.Clear(); return true;
    }
    public DialogueProject? Undo()
    {
        if (!CanUndo) return null;
        var snapshot = Expand(undo[^1]); var project = ProjectStore.Parse(snapshot);
        redo.Add(Compress(current)); current = snapshot; undo.RemoveAt(undo.Count - 1); return project;
    }
    public DialogueProject? Redo()
    {
        if (!CanRedo) return null;
        var snapshot = Expand(redo[^1]); var project = ProjectStore.Parse(snapshot);
        undo.Add(Compress(current)); current = snapshot; redo.RemoveAt(redo.Count - 1); return project;
    }
    // Keep complete, lossless JSON semantics. Only archived entries are compressed;
    // the current string is reused for dirty comparisons and guarded saves.
    private static byte[] Compress(string snapshot)
    {
        using var output = new MemoryStream();
        using (var compressed = new BrotliStream(output, CompressionLevel.Fastest, leaveOpen: true))
        using (var writer = new StreamWriter(compressed, new UTF8Encoding(false), 4096)) writer.Write(snapshot);
        return output.ToArray();
    }
    private static string Expand(byte[] snapshot)
    {
        using var input = new MemoryStream(snapshot, writable: false);
        using var compressed = new BrotliStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(compressed, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
