using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using DialogueMachine.Core;
using System.IO;
using System.Text.Json;

namespace DialogueMachine.UI;

public sealed partial class MainWindow
{
    private async Task<bool> ResolveSaveConflict(string path, ProjectFileSnapshot external)
    {
        var notice = "";
        while (true)
        {
            var answer = await ConflictDialog(external, notice);
            if (answer is null or "Cancel") return false;
            try
            {
                if (answer == "Save a copy")
                {
                    var safeName = string.Concat(project.ProjectName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
                    var copyPath = await projectFilePicker.SaveAsync(this, safeName + " copy.dialogue.json");
                    if (copyPath is null) return false;
                    copyPath = Path.GetFullPath(copyPath);
                    if (ProjectFiles.SamePath(copyPath, path) || ProjectFiles.Read(copyPath).Version.Exists)
                    { notice = "Choose a new file for the copy. Existing files are preserved."; external = ProjectFiles.Read(path); continue; }
                    CompleteSave(copyPath, ProjectStore.Serialize(project), new(false, null, null), copy: true);
                    return true;
                }
                // Decisions refer to the version shown, including any comparison just viewed.
                external = ProjectFiles.RequireUnchanged(path, external.Version);
                if (answer == "Compare")
                {
                    await CompareConflict(external);
                    var latest = ProjectFiles.Read(path);
                    notice = latest.Version == external.Version ? "" : "The file changed again while comparing. Review the latest version before choosing.";
                    external = latest;
                    continue;
                }
                var next = external.Parse();
                // Structural import validation is required; repairable graph diagnostics remain openable.
                _ = DialogueEngine.Validate(next);
                ProjectFiles.RequireUnchanged(path, external.Version);
                var snapshot = ProjectStore.Serialize(next);
                history.Commit(next); // Keep local edits reachable through Undo after a successful reload.
                diskVersion = external.Version; savedSnapshot = snapshot; isExample = false;
                ReplaceHistoryProject(next); ClearCurrentRecovery(); Remember(path);
                Status("Reloaded " + path + " · Undo restores your previous local edits.");
                return true;
            }
            catch (ProjectFileConflictException conflict)
            { external = conflict.Current; notice = "The file changed again. Review this version before choosing."; }
            catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            { notice = ex.Message + " Your local edits and existing files are preserved."; }
        }
    }

    private Task<string?> ConflictDialog(ProjectFileSnapshot external, string notice)
    {
        Window? dialog = null;
        var explanation = !external.Version.Exists ? "The project file was deleted or moved."
            : diskVersion is null ? "This recovered story has no verified saved-file baseline."
            : "Another app or editor instance changed or replaced the project file.";
        var body = Ui.Stack(Ui.Text("Your file changed on disk", 24), Ui.Text(explanation, 14), Ui.Text(external.Path, 12, true),
            Ui.Text("Your local edits are still in the editor. Compare the versions, reload the disk version (Undo keeps your local work), or save your edits to a new file.", 14, true));
        if (notice.Length > 0) body.Children.Add(Ui.Text(notice, 14));
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var label in new[] { "Compare", "Reload", "Save a copy", "Cancel" })
        {
            var button = Ui.Button(label, () => dialog!.Close(label)); button.Name = "Conflict" + label.Replace(" ", "");
            button.Margin = new Thickness(0, 0, 8, 8); actions.Children.Add(button);
        }
        var grid = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Margin = new Thickness(24) };
        grid.Children.Add(new ScrollViewer { Content = body }); Grid.SetRow(actions, 1); grid.Children.Add(actions);
        dialog = Ui.Dialog(this, "File conflict", grid, 670, 410, preferences.Scale);
        return dialog.ShowDialog<string?>(this);
    }

    private async Task CompareConflict(ProjectFileSnapshot external)
    {
        string diskText; string summary;
        try
        {
            var parsed = external.Parse(); var diagnostics = DialogueEngine.Validate(parsed);
            diskText = ProjectStore.Serialize(parsed);
            summary = "Disk version: " + diagnostics.Count(d => d.Severity == "Error") + " errors, " + diagnostics.Count(d => d.Severity == "Warning") + " warnings. Compare the complete desktop JSON, including groups and unknown fields.";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException)
        { diskText = external.Text ?? "(No readable project content)"; summary = "The disk version cannot be reloaded: " + ex.Message; }
        var localText = ProjectStore.Serialize(project);
        var panes = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 12 };
        Control Pane(string label, string value, string name)
        {
            var text = new TextBox { Name = name, Text = value, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("monospace") };
            AutomationProperties.SetName(text, label);
            ScrollViewer.SetHorizontalScrollBarVisibility(text, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
            var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 8 };
            grid.Children.Add(Ui.Text(label, 15)); Grid.SetRow(text, 1); grid.Children.Add(text); return grid;
        }
        panes.Children.Add(Pane("Local edits", localText, "ConflictLocalJson"));
        var disk = Pane("Current disk version", diskText, "ConflictDiskJson"); Grid.SetColumn(disk, 1); panes.Children.Add(disk);
        Window? dialog = null;
        var close = Ui.Button("Back to choices", () => dialog!.Close()); close.Name = "ConflictBack";
        var header = new ScrollViewer { MaxHeight = 100, Content = Ui.Text(summary, 13, true) };
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 12, Margin = new Thickness(24) };
        root.Children.Add(header); Grid.SetRow(panes, 1); root.Children.Add(panes); Grid.SetRow(close, 2); root.Children.Add(close);
        dialog = Ui.Dialog(this, "Compare project versions", root, 1000, 660, preferences.Scale);
        await dialog.ShowDialog(this);
    }
}
