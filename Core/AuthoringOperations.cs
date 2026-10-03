namespace DialogueMachine.Core;

// Snapshots are detached from both the source document and every later paste.
public sealed class SceneBundle
{
    private readonly string snapshot;
    public int Count { get; }
    internal SceneBundle(List<DialogueState> scenes)
    {
        Count = scenes.Count;
        snapshot = ProjectStore.Serialize(new DialogueProject { ListOfCharacterStates = scenes });
    }
    internal List<DialogueState> Read() => ProjectStore.Parse(snapshot).ListOfCharacterStates;
}

public static class AuthoringOperations
{
    public static SceneBundle CopyScenes(IEnumerable<DialogueState> scenes)
    {
        var list = scenes.ToList();
        if (list.Count == 0) throw new ArgumentException("Select at least one scene to copy.");
        if (list.Any(s => string.IsNullOrWhiteSpace(s.State) || s.State == "[EXIT]") || list.Select(s => s.State).Distinct(StringComparer.Ordinal).Count() != list.Count)
            throw new ArgumentException("Repair duplicate or reserved scene IDs before copying these scenes.");
        var bundle = new SceneBundle(list);
        _ = bundle.Read(); // Validate the complete snapshot before storing it.
        return bundle;
    }

    public static IReadOnlyList<DialogueState> PasteScenes(DialogueProject project, SceneBundle bundle, double x, double y)
    {
        CheckCapacity(project, bundle.Count);
        if (!double.IsFinite(x) || !double.IsFinite(y)) throw new ArgumentException("Scene positions must be finite.");
        var copies = bundle.Read();
        var used = project.ListOfCharacterStates.Select(s => s.State).ToHashSet(StringComparer.Ordinal);
        var remap = copies.ToDictionary(s => s.State, _ => NewId(used), StringComparer.Ordinal);
        var left = copies.Min(s => s.VisualX); var top = copies.Min(s => s.VisualY);
        foreach (var copy in copies)
        {
            copy.State = remap[copy.State];
            copy.DefaultNextId = Remap(copy.DefaultNextId);
            copy.VisualX = x + copy.VisualX - left; copy.VisualY = y + copy.VisualY - top;
            if (!double.IsFinite(copy.VisualX) || !double.IsFinite(copy.VisualY)) throw new ArgumentException("The copied scene positions are too far apart.");
            foreach (var choice in copy.Choices) { choice.InternalId = Guid.NewGuid(); choice.NextStateId = Remap(choice.NextStateId); }
        }
        project.ListOfCharacterStates.AddRange(copies);
        return copies;
        string? Remap(string? id) => id is not null && remap.TryGetValue(id, out var next) ? next : id;
    }

    public static DialogueState CreateConnectedScene(DialogueProject project, DialogueState source, Choice choice, string title)
    {
        if (!project.ListOfCharacterStates.Contains(source) || !source.Choices.Contains(choice)) throw new ArgumentException("The source choice is no longer in this project.");
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Give the new scene a title.");
        CheckCapacity(project, 1);
        var used = project.ListOfCharacterStates.Select(s => s.State).ToHashSet(StringComparer.Ordinal);
        var scene = Templates.Scene(NewId(used), title.Trim(), source.Speaker, "", source.VisualX + 350, source.VisualY);
        if (!double.IsFinite(scene.VisualX) || !double.IsFinite(scene.VisualY)) throw new ArgumentException("The source scene position is too large.");
        project.ListOfCharacterStates.Add(scene); choice.NextStateId = scene.State;
        return scene;
    }

    public static void SaveSceneTemplate(DialogueProject project, DialogueState source, string name)
    {
        name = TemplateName(name, project.SceneTemplates.Select(t => t.Name));
        var scene = CopyScenes([source]).Read()[0];
        // Retain self-connections; other destinations must be chosen for each use.
        if (scene.DefaultNextId != scene.State && scene.DefaultNextId != "[EXIT]") scene.DefaultNextId = null;
        foreach (var c in scene.Choices) if (c.NextStateId != scene.State && c.NextStateId != "[EXIT]") c.NextStateId = null;
        project.SceneTemplates.Add(new SceneTemplate { Name = name, Scene = scene });
    }

    public static DialogueState InsertSceneTemplate(DialogueProject project, SceneTemplate template, double x, double y) => PasteScenes(project, CopyScenes([template.Scene]), x, y)[0];

    public static void SaveChoiceTemplate(DialogueProject project, Choice source, string name)
    {
        name = TemplateName(name, project.ChoiceTemplates.Select(t => t.Name));
        var choice = CloneChoice(source); choice.NextStateId = null;
        project.ChoiceTemplates.Add(new ChoiceTemplate { Name = name, Choice = choice });
    }

    public static Choice InsertChoiceTemplate(DialogueState scene, ChoiceTemplate template)
    {
        var choice = CloneChoice(template.Choice); choice.InternalId = Guid.NewGuid(); choice.NextStateId = null;
        scene.Choices.Add(choice); return choice;
    }

    private static Choice CloneChoice(Choice choice) => ProjectStore.Clone(new DialogueProject { ListOfCharacterStates = [new DialogueState { Choices = [choice] }] }).ListOfCharacterStates[0].Choices[0];
    private static string TemplateName(string name, IEnumerable<string> existing)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Give the template a name.");
        name = name.Trim();
        if (existing.Contains(name, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("A template with that name already exists. Choose another name.");
        return name;
    }
    private static void CheckCapacity(DialogueProject project, int count)
    {
        if (project.ListOfCharacterStates.Count + count > 10000) throw new ArgumentException("This operation would exceed the 10,000-scene project limit.");
    }
    private static string NewId(HashSet<string> used)
    {
        string id; do { id = "scene_" + Guid.NewGuid().ToString("N")[..8]; } while (!used.Add(id)); return id;
    }
}
