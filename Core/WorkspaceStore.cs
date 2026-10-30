using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DialogueMachine.Core;

public sealed record RecentProject(string Path, string Name, DateTimeOffset LastOpenedAt);

public sealed class WorkspacePreferences
{
    public string Theme { get; set; } = "Dark";
    public double Scale { get; set; } = 1;
    public bool RecoveryEnabled { get; set; } = true;
    public bool ReopenLastProjectOnStartup { get; set; }
    public bool ShowMinimap { get; set; }
    public string? LastProjectPath { get; set; }
    public List<RecentProject> RecentProjects { get; set; } = [];
    // Read the original preferences without requiring users to reset their settings.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? RecentFiles { get; set; }
    public Dictionary<string, string> EngineExportFolders { get; set; } = [];
}

public sealed class WorkspaceStore(string directory)
{
    public static StringComparer PathComparer { get; } = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    public WorkspacePreferences Load()
    {
        var path = System.IO.Path.Combine(directory, "preferences.json");
        var p = File.Exists(path) ? JsonSerializer.Deserialize<WorkspacePreferences>(File.ReadAllText(path), ProjectStore.Options) ?? new() : new();
        Normalise(p); return p;
    }
    public void Save(WorkspacePreferences p)
    {
        Normalise(p);
        ProjectStore.AtomicWrite(System.IO.Path.Combine(directory, "preferences.json"), JsonSerializer.Serialize(p, ProjectStore.Options));
    }
    public static void Normalise(WorkspacePreferences p)
    {
        p.RecentProjects ??= [];
        foreach (var path in p.RecentFiles ?? [])
            if (!string.IsNullOrWhiteSpace(path) && !p.RecentProjects.Any(r => r is not null && PathComparer.Equals(r.Path, path)))
                p.RecentProjects.Add(new(path, System.IO.Path.GetFileNameWithoutExtension(path).Replace(".dialogue", ""), DateTimeOffset.MinValue));
        p.RecentFiles = null;
        p.RecentProjects = p.RecentProjects.Where(r => r is not null && !string.IsNullOrWhiteSpace(r.Path)).DistinctBy(r => r.Path, PathComparer).Take(8)
            .Select(r => r with { Name = string.IsNullOrWhiteSpace(r.Name) ? System.IO.Path.GetFileName(r.Path) : r.Name }).ToList();
        if (p.Theme is not ("Dark" or "Light" or "High contrast")) p.Theme = "Dark";
        p.Scale = double.IsFinite(p.Scale) ? Math.Clamp(p.Scale, 1, 1.3) : 1;
        p.LastProjectPath ??= p.RecentProjects.FirstOrDefault()?.Path;
        p.EngineExportFolders = p.EngineExportFolders is null
            ? new(PathComparer)
            : new(p.EngineExportFolders.Where(kv => !string.IsNullOrWhiteSpace(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value)), PathComparer);
    }
    public static string? GetEngineExportFolder(WorkspacePreferences p, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        path = System.IO.Path.GetFullPath(path);
        return p.EngineExportFolders.TryGetValue(path, out var folder) ? folder : null;
    }
    public static void SetEngineExportFolder(WorkspacePreferences p, string path, string? folder)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        path = System.IO.Path.GetFullPath(path);
        if (string.IsNullOrWhiteSpace(folder)) p.EngineExportFolders.Remove(path);
        else p.EngineExportFolders[path] = System.IO.Path.GetFullPath(folder);
    }
    public static void Remember(WorkspacePreferences p, string path, string name)
    {
        path = System.IO.Path.GetFullPath(path);
        p.RecentProjects.RemoveAll(r => PathComparer.Equals(r.Path, path));
        p.RecentProjects.Insert(0, new(path, name, DateTimeOffset.Now));
        p.RecentProjects = p.RecentProjects.Take(8).ToList(); p.LastProjectPath = path;
    }
    public static void Forget(WorkspacePreferences p, string path)
    {
        path = System.IO.Path.GetFullPath(path);
        p.RecentProjects.RemoveAll(r => PathComparer.Equals(r.Path, path));
        p.EngineExportFolders.Remove(path);
        if (PathComparer.Equals(p.LastProjectPath, path)) p.LastProjectPath = p.RecentProjects.FirstOrDefault()?.Path;
    }
}

public sealed record StartupPlan(string? ProjectPath, string? Notice);

public static class WorkspaceStartup
{
    public static StartupPlan Plan(WorkspacePreferences preferences, string? explicitPath, bool hasRecovery)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath)) return new(explicitPath, null);
        if (hasRecovery) return new(null, "Unsaved work is available below. Restore it to pick up where you left off.");
        if (preferences.ReopenLastProjectOnStartup && preferences.LastProjectPath is { } last)
            return File.Exists(last) ? new(last, null) : new(null, "Your last project has moved or is unavailable. Open it from its new location.");
        return new(null, null);
    }
}
