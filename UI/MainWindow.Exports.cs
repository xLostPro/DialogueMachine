using Avalonia;
using Avalonia.Controls;
using DialogueMachine.Core;
using System.IO;

namespace DialogueMachine.UI;

public sealed partial class MainWindow
{
    private ExportView? exports;
    private ExportView Exports => exports ??= new ExportView(() => project, projectFilePicker, Status);

    private Task ExportScript() => ExportScriptAsync();
    private Task ExportGraph() => ExportGraphAsync();
    private Task ExportRuntimeCompanion() => ExportRuntimeCompanionAsync();

    public async Task<bool> ExportScriptAsync() => hasProject && await Exports.Script(this, preferences.Scale);
    public async Task<bool> ExportGraphAsync() => hasProject && await Exports.Graph(this, preferences.Scale);
    public async Task<bool> ExportRuntimeCompanionAsync()
    {
        if (!hasProject) return false;
        Commit();
        return await Exports.RuntimeCompanion(this, preferences.Scale);
    }

    public async Task<bool> ExportLocalizationAsync(string? explicitPath = null)
    {
        if (!hasProject) return false;
        try
        {
            var path = explicitPath ?? await PickExport("Export translation CSV", "story-strings.csv", "csv");
            if (path is null) return false;
            ProjectStore.AtomicWrite(path, LocalizationStore.ExportCsv(project));
            Status("Exported translation CSV to " + path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            await Ui.Ask(this, "Could not export translation CSV", ex.Message, "Close");
            return false;
        }
    }

    public async Task<bool> ImportLocalizationAsync(string? explicitPath = null)
    {
        if (!hasProject) return false;
        var path = explicitPath ?? await projectFilePicker.OpenAsync(this);
        if (path is null) return false;
        try
        {
            var csvText = await File.ReadAllTextAsync(path);
            var draft = ProjectStore.Clone(project);
            LocalizationStore.ImportCsv(draft, csvText);
            Mutate(() => LocalizationStore.ImportCsv(project, csvText));
            Status("Imported translations from " + Path.GetFileName(path));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            await Ui.Ask(this, "Could not import translations", ex.Message, "Close");
            return false;
        }
    }

    public async Task<bool> ConfigureEngineExportFolderAsync()
    {
        if (!hasProject) return false;
        if (filePath is null)
        {
            await Ui.Ask(this, "Save story first", "Please save your story to a file before configuring its engine export folder.", "Close");
            return false;
        }

        var (nextFolder, saved) = await Exports.EngineFolder(this, preferences.Scale, WorkspaceStore.GetEngineExportFolder(preferences, filePath));
        if (!saved) return false;
        WorkspaceStore.SetEngineExportFolder(preferences, filePath, nextFolder);
        WritePreferences();
        if (nextFolder is not null)
        {
            Status("Engine export folder set to " + nextFolder);
            TryAutoExportEngineRuntime();
        }
        else
        {
            Status("Engine export folder cleared");
        }
        return true;
    }

    public void TryAutoExportEngineRuntime()
    {
        if (filePath is null) return;
        var folder = WorkspaceStore.GetEngineExportFolder(preferences, filePath);
        if (string.IsNullOrWhiteSpace(folder)) return;

        var errors = DialogueEngine.Validate(project).Where(d => d.Severity == "Error").ToList();
        if (errors.Count > 0)
        {
            RefreshDiagnostics();
            C<Border>("DiagnosticsBorder").IsVisible = true;
            return;
        }

        RuntimeHandoff handoff;
        try
        {
            handoff = RuntimeExport.Create(project);
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
        {
            RefreshDiagnostics();
            C<Border>("DiagnosticsBorder").IsVisible = true;
            return;
        }

        var fileName = RuntimeExport.Slug(project.ProjectName) + ".runtime.json";
        var outPath = Path.Combine(folder, fileName);

        if (File.Exists(outPath))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(outPath));
                if (doc.RootElement.TryGetProperty("StorySha256", out var shaProp) &&
                    string.Equals(shaProp.GetString(), handoff.StorySha256, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
            catch { }
        }

        try
        {
            ProjectStore.AtomicWrite(outPath, RuntimeExport.Serialize(handoff));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status("Engine export failed: " + ex.Message);
        }
    }
}
