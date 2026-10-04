namespace DialogueMachine.Core;

// Root-only memberships leave website scene data and detached scene bundles unchanged.
public static class SceneGroups
{
    internal static Dictionary<DialogueState, (string Id, int Occurrence)> Keys(DialogueProject project)
    {
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var keys = new Dictionary<DialogueState, (string, int)>();
        foreach (var scene in project.ListOfCharacterStates)
        {
            var occurrence = occurrences.GetValueOrDefault(scene.State);
            keys.Add(scene, (scene.State, occurrence)); occurrences[scene.State] = occurrence + 1;
        }
        return keys;
    }
    public static IReadOnlyList<DialogueState> Members(DialogueProject project, SceneGroup group)
    {
        var members = group.Scenes.Select(r => (r.StateId, r.Occurrence)).ToHashSet();
        return Keys(project).Where(p => members.Contains(p.Value)).Select(p => p.Key).ToArray();
    }
    public static SceneGroup Create(DialogueProject project, string name)
    {
        var group = new SceneGroup { Name = Name(project, name) };
        project.SceneGroups.Add(group); return group;
    }
    public static void Rename(DialogueProject project, SceneGroup group, string name)
    {
        RequireGroup(project, group); group.Name = Name(project, name, group);
    }
    public static void Delete(DialogueProject project, SceneGroup group)
    {
        RequireGroup(project, group); project.SceneGroups.Remove(group);
    }
    public static void Assign(DialogueProject project, SceneGroup group, IEnumerable<DialogueState> scenes)
    {
        RequireGroup(project, group);
        var keys = SelectionKeys(project, scenes);
        var existing = group.Scenes.Select(r => (r.StateId, r.Occurrence)).ToHashSet();
        foreach (var key in keys)
            if (existing.Add(key)) group.Scenes.Add(new SceneReference { StateId = key.Id, Occurrence = key.Occurrence });
    }
    public static void Remove(DialogueProject project, SceneGroup group, IEnumerable<DialogueState> scenes)
    {
        RequireGroup(project, group);
        var keys = SelectionKeys(project, scenes).ToHashSet();
        group.Scenes.RemoveAll(r => keys.Contains((r.StateId, r.Occurrence)));
    }
    // Rebind before identity/deletion changes, including shifted duplicate-ID occurrences.
    internal static void PreserveMemberships(DialogueProject project, Action change)
    {
        var byKey = Keys(project).ToDictionary(p => p.Value, p => p.Key);
        var bindings = project.SceneGroups.Select(g => (Group: g, Members: g.Scenes.Select(r => (Reference: r, Scene: byKey.GetValueOrDefault((r.StateId, r.Occurrence)))).ToList())).ToList();
        change(); var keys = Keys(project);
        foreach (var (group, members) in bindings)
        {
            group.Scenes.Clear();
            foreach (var (reference, scene) in members)
                if (scene is not null && keys.TryGetValue(scene, out var key))
                { reference.StateId = key.Id; reference.Occurrence = key.Occurrence; group.Scenes.Add(reference); }
        }
    }
    public static void RemoveScene(DialogueProject project, DialogueState scene)
        => PreserveMemberships(project, () => project.ListOfCharacterStates.Remove(scene));
    private static List<(string Id, int Occurrence)> SelectionKeys(DialogueProject project, IEnumerable<DialogueState> scenes)
    {
        var keys = Keys(project); var selection = scenes.Distinct().ToList();
        if (selection.Any(s => !keys.ContainsKey(s))) throw new ArgumentException("The selected scenes are no longer in this project.");
        return selection.Select(s => keys[s]).ToList();
    }
    private static void RequireGroup(DialogueProject project, SceneGroup group)
    {
        if (!project.SceneGroups.Contains(group)) throw new ArgumentException("This group is no longer in the project.");
    }
    private static string Name(DialogueProject project, string name, SceneGroup? current = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Give the group a name.");
        name = name.Trim();
        if (project.SceneGroups.Any(g => g != current && g.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("A group with that name already exists. Choose another name.");
        return name;
    }
}
