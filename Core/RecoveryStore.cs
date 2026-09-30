using System.IO;
using System.Text.Json;

namespace DialogueMachine.Core;

public sealed class RecoveryStore(string directory)
{
    private string PathFor(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("Recovery IDs must be generated GUIDs.", nameof(id));
        return Path.Combine(directory, id + ".json");
    }
    public void Save(string id, RecoveryRecord record) => ProjectStore.AtomicWrite(PathFor(id), JsonSerializer.Serialize(record, ProjectStore.Options));
    public void Remove(string id) { var path = PathFor(id); if (File.Exists(path)) File.Delete(path); }
    public List<(string Path, RecoveryRecord Record)> FindUnsaved()
    {
        var records = new List<(string, RecoveryRecord)>(); if (!Directory.Exists(directory)) return records;
        foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
        {
            if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out _)) continue;
            try
            {
                if (new FileInfo(path).Length > 21 * 1024 * 1024) continue;
                var r = JsonSerializer.Deserialize<RecoveryRecord>(File.ReadAllText(path), ProjectStore.Options);
                if (r?.Project is null) continue;
                var validated = ProjectStore.Parse(ProjectStore.Serialize(r.Project));
                if (r.FilePath is null || !File.Exists(r.FilePath) || File.GetLastWriteTimeUtc(r.FilePath) < r.SavedAt.UtcDateTime)
                    records.Add((path, r with { Project = validated }));
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or ArgumentException) { /* Keep damaged recovery files for manual investigation. */ }
        }
        return records.OrderByDescending(r => r.Item2.SavedAt).ToList();
    }
}
