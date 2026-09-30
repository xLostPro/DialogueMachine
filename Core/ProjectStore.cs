using System.IO;
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
        if (project.ListOfCharacterStates is null || project.ListOfCharacterStates.Count > 10000)
            throw new InvalidDataException("The scene list is missing or exceeds 10,000 scenes.");
        project.InitialVariables ??= []; project.TextVariables ??= [];
        foreach (var s in project.ListOfCharacterStates)
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
        return project;
    }
    public static DialogueProject Load(string path)
    {
        if (new FileInfo(path).Length > 20 * 1024 * 1024) throw new InvalidDataException("Projects must be smaller than 20 MB.");
        return Parse(File.ReadAllText(path));
    }
    public static void AtomicWrite(string path, string content)
    {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var temp = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream)) { writer.Write(content); writer.Flush(); stream.Flush(true); }
            File.Move(temp, full, true);
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
    private readonly List<string> undo = [];
    private readonly List<string> redo = [];
    private string current = "";
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public void Reset(DialogueProject p) { current = ProjectStore.Serialize(p); undo.Clear(); redo.Clear(); }
    public bool Commit(DialogueProject p)
    {
        var next = ProjectStore.Serialize(p);
        if (next == current) return false;
        undo.Add(current); if (undo.Count > 100) undo.RemoveAt(0);
        current = next; redo.Clear(); return true;
    }
    public DialogueProject? Undo()
    {
        if (!CanUndo) return null;
        redo.Add(current); current = undo[^1]; undo.RemoveAt(undo.Count - 1); return ProjectStore.Parse(current);
    }
    public DialogueProject? Redo()
    {
        if (!CanRedo) return null;
        undo.Add(current); current = redo[^1]; redo.RemoveAt(redo.Count - 1); return ProjectStore.Parse(current);
    }
}
