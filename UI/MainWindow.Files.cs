using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using DialogueMachine.Core;
using System.IO;
using System.Text.Json;

namespace DialogueMachine.UI;

public sealed partial class MainWindow
{
    private async Task NativeSmoke(string[] args)
    {
        var index = Array.IndexOf(args, "--smoke-output");
        var output = index >= 0 && index + 1 < args.Length ? args[index + 1] : AppContext.BaseDirectory;
        try
        {
            var projectIndex = Array.IndexOf(args, "--smoke-project");
            if (projectIndex >= 0 && projectIndex + 1 < args.Length && !await OpenProjectAsync(args[projectIndex + 1], showErrorDialog: false)) throw new InvalidOperationException("The smoke-test project could not be opened.");
            await Task.Delay(500); Graph.Fit(); UpdateLayout();
            using var bitmap = new RenderTargetBitmap(new PixelSize((int)ClientSize.Width, (int)ClientSize.Height));
            bitmap.Render(this); Directory.CreateDirectory(output); bitmap.Save(Path.Combine(output, "native-workspace.png"), PngBitmapEncoderOptions.Default);
            if (hasProject)
            {
                var snapshot = ProjectStore.Serialize(project); var viewport = Graph.ViewportWorld;
                GraphImageExport.Save(project, Path.Combine(output, "native-story-graph.png"));
                ProjectStore.AtomicWrite(Path.Combine(output, "native-story-script.md"), ScriptExport.Markdown(project, new(IncludeNotes: true)));
                if (ProjectStore.Serialize(project) != snapshot || Graph.ViewportWorld != viewport) throw new InvalidOperationException("Native exports changed the story or viewport.");
            }
            var heading = Welcome.FindControl<TextBlock>("WelcomeHeading")!;
            ProjectStore.AtomicWrite(Path.Combine(output, "native-smoke.json"), JsonSerializer.Serialize(new { Platform = Environment.OSVersion.ToString(), HasProject = hasProject, WelcomeVisible = Welcome.IsVisible, Scenes = project.ListOfCharacterStates.Count, Errors = hasProject ? DialogueEngine.Validate(project).Count(d => d.Severity == "Error") : 0, FileDialogsAvailable = StorageProvider.CanOpen && StorageProvider.CanSave, CanvasWidth = Graph.Bounds.Width, EditorWidth = Editor.Bounds.Width, WelcomeHeading = new { heading.Text, Foreground = heading.Foreground?.ToString(), Bounds = heading.Bounds.ToString(), heading.IsVisible }, Header = new { C<TextBlock>("ProjectTitle").Text, Foreground = C<TextBlock>("ProjectTitle").Foreground?.ToString(), Bounds = C<TextBlock>("ProjectTitle").Bounds.ToString() } }, ProjectStore.Options));
        }
        catch (Exception ex) { Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "native-smoke-error.txt"), ex.ToString()); Environment.ExitCode = 1; }
        finally { Close(); }
    }
    private async Task Open()
    {
        try
        {
            var path = await projectFilePicker.OpenAsync(this);
            if (path is not null) await OpenProjectAsync(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { await Ui.Ask(this, "Could not browse files", ex.Message + "\nTry opening a recent project or passing a project path when launching the app.", "Close"); }
    }
    private Task<bool> OpenPath(string path) => OpenProjectAsync(path);
    public async Task<bool> OpenProjectAsync(string path, bool showErrorDialog = true)
    {
        try
        {
            path = Path.GetFullPath(path);
            var disk = ProjectFiles.Read(path);
            var next = disk.Parse(); // Validate before replacing the current document.
            if (!await ConfirmReplace()) return false;
            ProjectFiles.RequireUnchanged(path, disk.Version);
            if (next.ProjectName == "Untitled story") next.ProjectName = Path.GetFileNameWithoutExtension(path).Replace(".dialogue", "");
            LoadProject(next, path, false, disk.Version); Remember(path); Status("Opened " + path); return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
        {
            var message = ex.Message + (hasProject ? "\nYour current story is still open." : "");
            if (!hasProject) { RefreshWelcome(); Welcome.ShowNotice("Could not open this project. " + ex.Message); }
            if (showErrorDialog) await Ui.Ask(this, "Could not open project", message, "Close");
            return false;
        }
    }
    private void LoadProject(DialogueProject next, string? path, bool unsaved, ProjectFileVersion? version = null)
    {
        ClearScenarioResults();
        editTimer.Stop(); hasProject = true; isExample = false; project = next; filePath = path; diskVersion = version; recoveryId = Guid.NewGuid().ToString("N");
        selected = project.ListOfCharacterStates.FirstOrDefault(s => s.State == project.Entry) ?? project.ListOfCharacterStates.FirstOrDefault();
        history.Reset(project); pendingEdits = false; savedSnapshot = unsaved ? "" : history.CurrentSnapshot;
        Welcome.IsVisible = false; C<Grid>("WorkspacePanel").IsVisible = true;
        C<Border>("DiagnosticsBorder").IsVisible = false;
        Refresh(); ClearOutlineFilters(); BuildMenu(); UpdateLayout(); Graph.Fit();
    }
    private async Task<bool> Save(bool saveAs = false)
        => await SaveProjectAsync(saveAs);
    public async Task<bool> SaveProjectAsync(bool saveAs = false)
    {
        if (!hasProject || saveInProgress) return false;
        saveInProgress = true;
        try
        {
            Commit(); var path = filePath;
            if (path is null || saveAs)
            {
                var safeName = string.Concat(project.ProjectName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
                path = await projectFilePicker.SaveAsync(this, safeName + ".dialogue.json"); if (path is null) return false;
            }
            path = Path.GetFullPath(path);
            // The public Project model remains mutable. Reconcile untracked programmatic
            // edits at the file boundary, including edits while an async picker was open.
            Commit(true, force: true);
            var sameFile = filePath is not null && ProjectFiles.SamePath(path, filePath);
            var expected = sameFile ? diskVersion : ProjectFiles.Read(path).Version;
            try
            {
                var current = ProjectFiles.RequireUnchanged(path, expected);
                CompleteSave(path, history.CurrentSnapshot, current.Version);
                return true;
            }
            catch (ProjectFileConflictException conflict) { return await ResolveSaveConflict(path, conflict.Current); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { await Ui.Ask(this, "Could not save project", ex.Message + "\nYour changes remain in the editor. Try Save as to another location.", "Close"); return false; }
        finally { saveInProgress = false; }
    }
    private void CompleteSave(string path, string snapshot, ProjectFileVersion expected, bool copy = false)
    {
        var version = copy ? ProjectFiles.SaveCopy(path, snapshot) : ProjectFiles.Save(path, snapshot, expected);
        filePath = path; diskVersion = version; isExample = false; savedSnapshot = snapshot;
        ClearCurrentRecovery(); Remember(path); UpdateStatus(); Status("Saved " + path);
    }
    private async Task<bool> ConfirmReplace()
    {
        Commit(true, force: true);
        if (!Dirty) return true;
        var answer = await Ui.Ask(this, "Save your changes?", "‘" + project.ProjectName + "’ has unsaved changes.", "Save changes", "Discard changes", "Cancel");
        if (answer == "Save changes")
        {
            var local = ProjectStore.Serialize(project);
            // Reload resolves a conflict but does not save these local edits. Keep the
            // document open so Undo can recover them instead of resetting its history.
            return await Save() && savedSnapshot == local;
        }
        if (answer == "Discard changes") { ClearCurrentRecovery(); return true; } return false;
    }
    private async Task ProjectDetails()
    {
        // Edit a local copy so closing the dialog cancels cleanly.
        var name = new TextBox { Text = project.ProjectName }; var character = new TextBox { Text = project.CharacterId };
        Window? w = null;
        w = Ui.Dialog(this, "Project details", Ui.Stack(Ui.Text("Project details", 24), Ui.Field("Story title", name), Ui.Field("Character / export ID", character), Ui.Text("A descriptive title helps you find your story. The character ID is kept for the game export.", 12, true), Ui.Row(Ui.Button("Cancel", () => w!.Close(false)), Ui.Button("Save details", () => w!.Close(true), "primary"))), 520, 350);
        ((StackPanel)w.Content!).Margin = new Thickness(24);
        if (await w.ShowDialog<bool>(this)) Mutate(() => { project.ProjectName = string.IsNullOrWhiteSpace(name.Text) ? "Untitled story" : name.Text.Trim(); project.CharacterId = character.Text?.Trim() ?? ""; });
    }
    private Task<string?> PickExport(string title, string name, string extension)
        => projectFilePicker.ExportAsync(this, title, name, extension);
    private async Task ExportWeb()
    {
        Commit();
        var errors = DialogueEngine.Validate(project).Where(d => d.Severity == "Error").ToList();
        if (errors.Count > 0) { RefreshDiagnostics(); C<Border>("DiagnosticsBorder").IsVisible = true; await Ui.Ask(this, "Fix export errors", "Resolve the story errors shown in diagnostics before exporting for the website or game.", "Close"); return; }
        var path = await PickExport("Export website / game JSON", project.CharacterId + ".json", "json");
        if (path is not null) await WriteExport(path, ProjectStore.WebExport(project));
    }
    private async Task WriteExport(string path, string content)
    {
        try { ProjectStore.AtomicWrite(path, content); Status("Exported " + path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { await Ui.Ask(this, "Could not export", ex.Message, "Close"); }
    }
    private RecoveryStore RecoveriesStore => new(Path.Combine(dataDir, "Recovery"));
    private void WriteRecovery()
    {
        if (!Dirty || IsTestMode || !preferences.RecoveryEnabled) return;
        try { RecoveriesStore.Save(recoveryId, new RecoveryRecord(filePath, DateTimeOffset.Now, project, diskVersion)); Status("Recovery saved locally · " + DateTime.Now.ToString("HH:mm") + " · project has unsaved changes"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status("Recovery save failed: " + ex.Message + " · save your project manually."); }
    }
    private void ClearCurrentRecovery()
    {
        try { RecoveriesStore.Remove(recoveryId); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status("Project saved; old recovery could not be removed: " + ex.Message); }
    }
    private List<(string Path, RecoveryRecord Record)> Recoveries()
    {
        try { return RecoveriesStore.FindUnsaved(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status("Recovery files could not be read: " + ex.Message); return []; }
    }
    private void Restore(RecoveryRecord r, string sourcePath)
    {
        try { LoadProject(ProjectStore.Parse(ProjectStore.Serialize(r.Project)), r.FilePath, true, r.SavedFileVersion); recoveryId = Path.GetFileNameWithoutExtension(sourcePath); Status("Recovered unsaved changes. Save the project when you're ready."); }
        catch (Exception ex) when (ex is JsonException or InvalidDataException) { Status("Could not restore this recovery: " + ex.Message); }
    }
    private async Task ShowRecoveries()
    {
        var panel = Ui.Stack(Ui.Text("Recovery saves", 24), Ui.Text("Automatic recovery protects unsaved edits. Restoring does not overwrite your project file.", 13, true));
        var records = Recoveries(); Window? w = null;
        if (records.Count == 0) panel.Children.Add(Ui.Text("No unsaved recovery files were found.", 14, true));
        foreach (var (path, r) in records)
        {
            var record = r; var source = path; panel.Children.Add(Ui.AsyncButton(r.Project.ProjectName + " · " + r.SavedAt.ToLocalTime().ToString("g"), async () => { w!.Close(); if (await ConfirmReplace()) Restore(record, source); }));
        }
        panel.Margin = new Thickness(24); w = Ui.Dialog(this, "Recovery saves", new ScrollViewer { Content = panel }, 600, 430); await w.ShowDialog(this);
    }
    private void ReadPreferences()
    {
        if (persistWorkspace)
            try { preferences = new WorkspaceStore(dataDir).Load(); }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or ArgumentException) { preferences = new(); }
        ApplyAppearance();
    }
    private void ApplyAppearance()
    {
        ((App)Application.Current!).SetPalette(preferences.Theme);
        Graph.ClearTextLayouts();
        if (Content is LayoutTransformControl transform) transform.LayoutTransform = new ScaleTransform(preferences.Scale, preferences.Scale);
        else if (Content is Control root)
        {
            Content = null;
            Content = new LayoutTransformControl { Child = root, LayoutTransform = new ScaleTransform(preferences.Scale, preferences.Scale) };
        }
        MinWidth = 1000 * preferences.Scale; MinHeight = 680 * preferences.Scale;
    }
    public void ChangeAppearance(string theme, double scale = 1)
    {
        preferences.Theme = theme; preferences.Scale = Math.Clamp(scale, 1, 1.3);
        ApplyAppearance(); WritePreferences(); Refresh(); RefreshWelcome();
    }
    private void WritePreferences()
    {
        if (!persistWorkspace) return;
        try { new WorkspaceStore(dataDir).Save(preferences); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status("Preferences could not be saved: " + ex.Message); }
    }
    private void Remember(string path)
    {
        WorkspaceStore.Remember(preferences, path, project.ProjectName); WritePreferences(); BuildMenu();
    }
    private async Task ShowSettings()
    {
        var theme = preferences.Theme; var scale = preferences.Scale;
        var themePicker = Ui.Select(["Dark", "Light", "High contrast"], theme, v => theme = v);
        var scalePicker = Ui.Select(["100%", "115%", "130%"], $"{scale:P0}", v => scale = v switch { "115%" => 1.15, "130%" => 1.3, _ => 1 });
        var recovery = new CheckBox { Content = "Save local recovery copies every 10 seconds", IsChecked = preferences.RecoveryEnabled };
        var reopen = new CheckBox { Content = "Reopen my last saved project on startup", IsChecked = preferences.ReopenLastProjectOnStartup };
        Window? w = null;
        var content = Ui.Stack(Ui.Text("Make yourself at home", 24), Ui.Field("Appearance", themePicker), Ui.Field("Interface size", scalePicker), recovery, reopen, Ui.Text("Tab moves between controls. Use the scene outline and destination pickers to edit every connection with the keyboard. Ctrl/Cmd+Tab focuses graph scenes; Space toggles selection. Arrows move the selected set; Shift moves it farther.", 13, true), Ui.Row(Ui.Button("Cancel", () => w!.Close(false)), Ui.Button("Apply", () => w!.Close(true), "primary")));
        content.Margin = new Thickness(24); w = Ui.Dialog(this, "Appearance & accessibility", content, 570, 510);
        if (await w.ShowDialog<bool>(this)) { preferences.RecoveryEnabled = recovery.IsChecked == true; preferences.ReopenLastProjectOnStartup = reopen.IsChecked == true; ChangeAppearance(theme, scale); }
    }
    private async Task ShowHelp()
    {
        var text = "1. Write your story\nAdd scenes, name the speaker, and write one dialogue page per line. Give player choices a destination, or use the scene's continue destination. Choose End conversation for an ending.\n\n2. Connect the story\nCtrl/Cmd-click or Shift-click toggles scenes. Shift-drag adds a set. Drag any selected scene or use arrows to move the set; Shift moves farther. Each move is one undo step. Escape cancels a gesture or clears selection. Ctrl/Cmd+A selects all; Ctrl/Cmd+Tab (Shift to reverse) focuses nodes, Space toggles, Enter selects one. The outline supports Ctrl/Cmd toggles and Shift ranges. The editor edits the active scene; duplicate/delete/rename/play-selected use that scene. Outgoing/internal links are purple (yellow in High contrast), incoming green; continue links stay dashed. Drag output dots onto another scene, or use Choose… to search destination titles and IDs. Down focuses results; Enter chooses; Cancel/Escape preserves the target. Jump opens connected scenes, and Notes lists incoming/outgoing links. Outline filters combine group, speaker, tag and diagnostics with text search. Clear search & filters restores the list. Fit story shows the whole graph; Arrange lays it out by connections. Script gives a readable overview.\n\nGroups & chapters\nStory tools or Ctrl/Cmd+Shift+G opens the group manager. Create an empty group, rename/delete it, assign/remove the selected scene set, select its members, or filter the outline. A scene can belong to several groups. Deleting a group preserves scenes and connections. Each edit is one Undo/Redo step after closing the manager. Copies, duplicates and template insertions start ungrouped within and across projects. Groups persist in desktop files and recovery and are omitted from website exports.\n\nMinimap\nView or Ctrl/Cmd+Shift+M shows/hides the story map, selected scenes and green viewport. Click/drag to navigate. Focus it and use arrows (Shift pans farther), Enter/Home to fit and Escape to return to graph focus. Alt+arrows pans the graph viewport with the map hidden too. Visibility is a workspace preference; navigation leaves the story unchanged.\n\n3. Add logic\nConditions compare affinity or inventory against an integer. All conditions must pass. Actions add or subtract values; inventory stays at zero or above. Event, Script and Shop integrations are preserved in exports and logged during playtests.\n\n4. Play and check\nF5 starts at the story's start. Shift+F5 starts at the selected scene. Edit test variables, use Back to revisit decisions, and export the transcript. Diagnostics find missing connections, unreachable scenes and loops without endings.\n\nFind & replace\nCtrl/Cmd+H or Ctrl/Cmd+Shift+F searches dialogue, responses, titles and notes. Choose fields and the entire project or selected scene. Preview replacements, jump to exact matches, or replace selected/all. Each replacement applies immediately and can be undone after closing the dialog. Use Variables & characters to rename variable references.\n\nReuse content\nNew scene… on a choice creates and connects a blank scene in one undo step. Edit → Copy selected scenes copies the graph/outline set. Copy scenes… preselects that set; Paste scenes selects all copies, assigns fresh IDs and remaps internal links. The scene clipboard lasts until this window closes, including project changes; other targets are retained. Save scene templates in Notes and choice templates in Content. Edit → Scene & choice templates… searches, previews, inserts and deletes project-owned definitions. Down focuses results, Enter inserts, Escape closes previews. Scene templates keep self-links and endings and clear other targets; choice templates use continue fallback. Variable renames include template references. Set variable starting values and substitutions when copying into another project.\n\nInline text\n[b]bold[/b], [i]italic[/i], [u]underline[/u], [colour=#79DFC8]colour[/colour]\n{player_name}, {npc_name}, {inv:coin}\n[if aff:trust >= 1]Trusted[else]Stranger[/if]\nNested conditions and negative integer thresholds are supported.\n\nKeyboard shortcuts\nCtrl/Cmd+N: new · Ctrl/Cmd+O: open · Ctrl/Cmd+S: save\nCtrl/Cmd+Shift+S: save as · Ctrl/Cmd+F: outline search\nCtrl/Cmd+H / Shift+F: find & replace\nCtrl/Cmd+Z: undo · Ctrl/Cmd+Shift+Z: redo\nCtrl/Cmd+D: duplicate · Ctrl/Cmd+0: fit graph\nCtrl/Cmd+Shift+G: groups · Ctrl/Cmd+Shift+M: minimap\nCtrl/Cmd+C / V: copy / paste scenes when graph or outline is focused\nF5: play · Shift+F5: play selected · F1: this guide\n\nFiles and recovery\nSave keeps the complete desktop project. Export website / game JSON keeps the original website schema and puts the start scene first. Recovery files and preferences live in:\n" + dataDir;
        var exportGuide = "\n\nExport your story\nFile → Export graph image exports every scene and connection, using full story bounds and the current theme. Choose 1× or 2× resolution; Fit within limits can reduce oversized images to 0.25×. Limits are 16,384 pixels per side and 32 megapixels. Larger stories need closer scene spacing or a script export. Selection, focus, minimap and editor overlays are excluded. Export leaves your view, filters, selection, pan/zoom, positions and Undo/Redo intact.\n\nFile → Export script creates readable Markdown with options for conditions/visibility, response actions, scene exit actions and writer’s notes (off by default). Entry comes first, then project order; pages/candidates and choices retain their order. Inline conditions always retain both branches, substitutions stay literal, and author-written Markdown is escaped. Destinations label inherited continue, endings, unset and missing targets. Cancel/Escape or a failed write preserves your work and any existing export.";
        text = text.Replace("\n\nFiles and recovery", exportGuide + "\n\nFiles and recovery");
        var fileSafetyGuide = "\n\nExternal file changes\nBefore ordinary Save, disk contents are checked against the opened or last saved version. Rewrites, replacements and deletion open a conflict dialog. Compare shows your local edits beside the disk JSON. Reload validates and rechecks the external file; Undo restores your local work. If the file changes again, review the latest version before choosing. Save a copy writes to a new filename and switches the editor to it; existing files are preserved. Cancel, Escape, cancelled browsing and write failures retain local edits and recovery. Save as to the current path has the same protection. Recovery copies remain available when external disk content differs, even if it is newer or malformed.";
        text = text.Replace("\n\nFiles and recovery", fileSafetyGuide + "\n\nFiles and recovery");
        var scenarioGuide = "\n\nRepeatable story tests\nStory → Test scenarios saves named starting scenes, numeric/text overrides and random seeds. Record path opens the player; Use recorded path transfers the current route to the draft. Back restores random state, values, text, events and trace; Restart resets the seed and overrides. Save scenario applies one undoable definition change; save the project to persist it. Duplicate/Delete are undoable after closing. Add assertions for final numeric/text values, ending scenes and emitted Event/Script/Shop hooks (which stay logged only). Run draft or Run all saved keeps results separate from the story; Cancel run or closing cancels. View/Export results explains failures and configured dead ends. Visited scene marks and response dots show observed coverage without changing selection or exports. Authored edits invalidate results. References use scene IDs and choice identities, detecting changes instead of guessing by label/index; review failures and re-record changed paths. Configuration analysis starts at supplied values, has a 1,000-configuration budget and remains distinct from structural reachability. Neither replay nor analysis proves every cyclic path. Core CLI commands and exit codes are documented in STORY_TESTING.md.";
        text = text.Replace("\n\nFiles and recovery", scenarioGuide + "\n\nFiles and recovery");
        var panel = Ui.Stack(Ui.Text("Your guide to Dialogue Machine", 26), Ui.Text(text, 14)); panel.Margin = new Thickness(28);
        await Ui.Dialog(this, "Guide & shortcuts", new ScrollViewer { Content = panel }, 690, 740).ShowDialog(this);
    }
}
