using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DialogueMachine.Core;
using DialogueMachine.UI;
using System.Text;
using System.Text.Json;

internal static class ConflictChecks
{
    private static DialogueProject Story()
    {
        var story = ProjectStore.Parse("""
            {"ProjectName":"Conflict story","FutureRoot":{"keep":42},"ListOfCharacterStates":[
            {"State":"start","Text":"Baseline","DefaultNextId":"[EXIT]","FutureScene":true,
            "Choices":[{"ButtonText":"Bye","NextStateId":"[EXIT]","Visibility":0,"FutureChoice":"keep"}]}]}
            """);
        SceneGroups.Assign(story, SceneGroups.Create(story, "Act I"), story.ListOfCharacterStates);
        story.SceneGroups[0].Extra = new() { ["FutureGroup"] = JsonSerializer.SerializeToElement(7) };
        return story;
    }

    public static void Core(Action<bool, string> check, Action<Action, string> throws, string output)
    {
        var root = Path.Combine(Path.GetFullPath(output), "conflict-core-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var path = Path.Combine(root, "story.json"); var story = Story(); var content = ProjectStore.Serialize(story);
        File.WriteAllText(path, content, new UTF8Encoding(true)); var original = ProjectFiles.Read(path);
        check(original.Parse().ListOfCharacterStates[0].Text == "Baseline" && original.Version.ContentHash is not null, "disk capture parses BOM files and fingerprints original bytes rather than normalized JSON");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-3));
        check(ProjectFiles.RequireUnchanged(path, original.Version).Version == original.Version, "timestamp-only changes do not cause false content conflicts");
        var timestamp = File.GetLastWriteTimeUtc(path); var length = new FileInfo(path).Length;
        File.WriteAllText(path, content.Replace("Baseline", "External"), new UTF8Encoding(true)); File.SetLastWriteTimeUtc(path, timestamp);
        check(new FileInfo(path).Length == length && File.GetLastWriteTimeUtc(path) == timestamp && ProjectFiles.Read(path).Version != original.Version, "equal-length rewrites with restored timestamps are detected by content");
        throws(() => ProjectFiles.Save(path, content, original.Version), "ordinary Core save refuses an externally rewritten project");
        check(File.ReadAllText(path).Contains("External"), "conflicted save preserves external bytes");
        var rewrite = ProjectFiles.Read(path);
        File.WriteAllText(path, File.ReadAllText(path) + " ");
        throws(() => ProjectFiles.RequireUnchanged(path, rewrite.Version), "JSON-equivalent whitespace rewrites are still external changes");
        var beforeReplace = ProjectFiles.Read(path); var replacement = Path.Combine(root, "replacement.json"); File.Copy(path, replacement); File.Move(replacement, path, true);
        check(ProjectFiles.Read(path).Version.ContentHash == beforeReplace.Version.ContentHash && (!OperatingSystem.IsWindows() || ProjectFiles.Read(path).Version != beforeReplace.Version), "Windows file identity detects atomic same-byte replacements");
        var beforeDelete = ProjectFiles.Read(path); File.Delete(path);
        throws(() => ProjectFiles.Save(path, content, beforeDelete.Version), "deleted files are conflicts rather than silently recreated");
        check(!ProjectFiles.Read(path).Version.Exists && !File.Exists(path), "deletion detection preserves the missing original path");
        File.WriteAllText(path, "{"); var malformed = ProjectFiles.Read(path);
        throws(() => malformed.Parse(), "external JSON syntax is validated before reload");
        throws(() => ProjectFiles.RequireUnchanged(path, null), "unknown recovery baselines require explicit conflict resolution");
        File.WriteAllText(path, """{"FormatVersion":999,"ListOfCharacterStates":[]}""");
        throws(() => ProjectFiles.Read(path).Parse(), "incompatible external formats cannot replace an editor document");
        File.WriteAllText(path, """{"ListOfCharacterStates":[{"State":""}]}""");
        throws(() => ProjectFiles.Read(path).Parse(), "semantically malformed external scenes fail import sanitation");
        File.WriteAllText(path, content); var baseline = ProjectFiles.Read(path);
        throws(() => ProjectStore.AtomicWrite(path, content, _ =>
        {
            File.WriteAllText(path, "external change during staged write");
            ProjectFiles.RequireUnchanged(path, baseline.Version);
        }), "staged atomic writes can reject changes at the replacement boundary");
        check(File.ReadAllText(path) == "external change during staged write" && !Directory.GetFiles(root, "*.tmp").Any(), "late conflict preserves the latest file and cleans staged output");
        File.WriteAllText(path, content); baseline = ProjectFiles.Read(path);
        var edited = ProjectStore.Clone(story); edited.ListOfCharacterStates[0].Text = "Local work";
        var written = ProjectFiles.Save(path, ProjectStore.Serialize(edited), baseline.Version);
        check(written == ProjectFiles.Read(path).Version && ProjectStore.Serialize(ProjectFiles.Read(path).Parse()) == ProjectStore.Serialize(edited), "successful guarded save returns its written baseline and preserves groups and unknown fields");
        check((int)ProjectFiles.Read(path).Parse().ListOfCharacterStates[0].Choices[0].Visibility == 0 && ProjectStore.StoryExport(edited).Contains("\"Visibility\": 0"), "conflict-safe persistence retains numeric visibility ordering");
        if (OperatingSystem.IsWindows())
        {
            using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            throws(() => ProjectFiles.Save(path, content, written), "replacement lock failures leave an existing project intact");
            check(File.ReadAllText(path) == ProjectStore.Serialize(edited) && !Directory.GetFiles(root, "*.tmp").Any(), "failed replacement retains saved bytes and removes only its own temporary file");
        }
        var copy = Path.Combine(root, "copy.json"); var copyVersion = ProjectFiles.SaveCopy(copy, content);
        check(copyVersion == ProjectFiles.Read(copy).Version, "new copies return a trustworthy disk baseline");
        throws(() => ProjectFiles.SaveCopy(copy, "changed"), "save-a-copy refuses any existing destination");
        check(File.ReadAllText(copy) == content, "rejected copy overwrite preserves the destination");
        throws(() => ProjectFiles.SaveCopy(root, content), "directory write failures are surfaced without document state changes");
        var recovery = new RecoveryStore(Path.Combine(root, "Recovery")); var id = Guid.NewGuid().ToString("N");
        recovery.Save(id, new(path, DateTimeOffset.UtcNow.AddDays(-1), story, written));
        check(recovery.FindUnsaved().Single().Record.SavedFileVersion == written, "recovery round trips the original saved-file baseline even after a newer external write");
        File.WriteAllText(path, "{");
        check(recovery.FindUnsaved().Count == 1, "malformed external files cannot hide valid local recovery work");
        using (var stream = new FileStream(path, FileMode.Create)) stream.SetLength(20 * 1024 * 1024 + 1);
        var oversized = ProjectFiles.Read(path);
        check(oversized.Version.Exists && oversized.Version.ContentHash is not null && oversized.Text is null, "oversized external content remains detectable without allocating a text preview");
        throws(() => oversized.Parse(), "oversized external content cannot be reloaded");
    }

    public static void UI(Action<bool, string> check, string output, Action<Window, string> capture)
    {
        var root = Path.Combine(Path.GetFullPath(output), "conflict-ui-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var path = Path.Combine(root, "story.json"); var picker = new Picker(); var settings = Path.Combine(root, "settings");
        var window = new MainWindow(null, true, settings, picker); window.Show();
        var original = ProjectStore.Serialize(Story()); File.WriteAllText(path, original); check(Pump(window.OpenProjectAsync(path)), "conflict workflow opens an isolated baseline project");
        T Control<T>(string name) where T : Control => window.FindControl<T>(name)!;
        Window Dialog() { Dispatcher.UIThread.RunJobs(); return window.OwnedWindows.Single(w => w.IsVisible); }
        void Answer(string name) => Press(Dialog().GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, name)));
        void Edit(string text)
        {
            Control<StackPanel>("EditorPanel").GetVisualDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "Dialogue text").Text = text;
            Dispatcher.UIThread.RunJobs();
        }
        Edit("Local draft");
        var external = ProjectStore.Parse(original); external.ListOfCharacterStates[0].Text = "External draft";
        var externalJson = ProjectStore.Serialize(external); File.WriteAllText(path, externalJson);
        var recoveries = new RecoveryStore(Path.Combine(settings, "Recovery"));
        var id = Guid.NewGuid().ToString("N"); recoveries.Save(id, new(path, DateTimeOffset.UtcNow, window.Project));
        // Restore to bind an active recovery, then verify that failed/cancelled operations keep it.
        var recoveryWindow = new MainWindow(null, true, settings, picker); recoveryWindow.Show(); Pump(recoveryWindow.InitialiseWorkspaceAsync());
        Press(recoveryWindow.FindControl<WelcomeView>("Welcome")!.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Restore story")));
        window.Close(); window = recoveryWindow;
        var local = ProjectStore.Serialize(window.Project); var settingsBytes = File.ReadAllBytes(Path.Combine(settings, "preferences.json"));
        bool Unchanged() => ProjectStore.Serialize(window.Project) == local && window.CurrentFilePath == path && window.HasUnsavedChanges
            && File.ReadAllText(path) == externalJson && File.Exists(Path.Combine(settings, "Recovery", id + ".json")) && File.ReadAllBytes(Path.Combine(settings, "preferences.json")).SequenceEqual(settingsBytes);
        var task = window.SaveProjectAsync(); Answer("Cancel"); check(!Pump(task) && Unchanged(), "conflict Cancel preserves local story, identity, dirty state, recents, recovery and external bytes");
        task = window.SaveProjectAsync(); Key(Dialog(), Avalonia.Input.Key.Escape); check(!Pump(task) && Unchanged(), "conflict Escape preserves the entire file workflow state");
        task = window.SaveProjectAsync(); Dialog().Close(); check(!Pump(task) && Unchanged(), "closing a conflict dialog cancels without a write");
        task = window.SaveProjectAsync(); Answer("Compare");
        check(Named<TextBox>(Dialog(), "ConflictLocalJson").Text == local && Named<TextBox>(Dialog(), "ConflictDiskJson").Text == externalJson && Unchanged(), "Compare shows complete detached versions without changing the story or existing files");
        external.ListOfCharacterStates[0].Text = "Second external draft"; externalJson = ProjectStore.Serialize(external); File.WriteAllText(path, externalJson);
        Answer("Back to choices"); check(Dialog().GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("changed again") == true), "external changes during Compare refresh the conflict choices");
        external.ListOfCharacterStates[0].Text = "Third external draft"; externalJson = ProjectStore.Serialize(external); File.WriteAllText(path, externalJson);
        Answer("Reload"); check(!task.IsCompleted && Unchanged() && Dialog().GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("changed again") == true), "a stale Reload decision reopens the latest conflict and retains local work");
        Answer("Reload"); check(Pump(task) && !window.HasUnsavedChanges && ProjectStore.Serialize(window.Project) == externalJson && !File.Exists(Path.Combine(settings, "Recovery", id + ".json")), "successful Reload updates baseline and clears active recovery only after validation and recheck");
        Press(Control<Button>("UndoButton")); check(ProjectStore.Serialize(window.Project) == local && window.HasUnsavedChanges, "Reload Undo restores the complete local edits with groups and unknown fields");
        Press(Control<Button>("RedoButton")); check(ProjectStore.Serialize(window.Project) == externalJson && !window.HasUnsavedChanges, "Reload Redo restores the disk version and its clean saved state");
        Edit("Saved after reload"); check(Pump(window.SaveProjectAsync()) && !window.HasUnsavedChanges && File.ReadAllText(path).Contains("Saved after reload"), "saving after Reload uses the refreshed disk baseline");
        Edit("Pending local work"); var pending = ProjectStore.Serialize(window.Project);
        foreach (var bad in new[] { "{", "{}", """{"FormatVersion":999,"ListOfCharacterStates":[]}""", """{"ListOfCharacterStates":[null]}""" })
        {
            File.WriteAllText(path, bad); task = window.SaveProjectAsync(); Answer("Reload");
            check(!task.IsCompleted && ProjectStore.Serialize(window.Project) == pending && window.HasUnsavedChanges && File.ReadAllText(path) == bad, "malformed/incompatible external Reload preserves local edits: " + bad);
            Answer("Compare"); check(Named<TextBox>(Dialog(), "ConflictDiskJson").Text == bad, "malformed Compare retains the raw disk evidence: " + bad);
            Answer("Back to choices"); Answer("Cancel"); check(!Pump(task), "malformed external resolution remains cancel-safe: " + bad);
        }
        File.Delete(path); task = window.SaveProjectAsync(); Answer("Reload");
        check(!task.IsCompleted && !File.Exists(path) && ProjectStore.Serialize(window.Project) == pending, "Reload of a deleted project preserves local edits and does not recreate the file");
        picker.Paths.Enqueue(null); Answer("Save a copy"); check(!Pump(task) && window.CurrentFilePath == path && window.HasUnsavedChanges && !File.Exists(path), "canceled copy picker preserves local identity and deletion");
        var copy = Path.Combine(root, "copy.json"); task = window.SaveProjectAsync(); picker.Paths.Enqueue(copy); Answer("Save a copy");
        check(Pump(task) && window.CurrentFilePath == copy && !window.HasUnsavedChanges && !File.Exists(path) && File.ReadAllText(copy) == pending, "save-a-copy after deletion establishes a new identity without recreating the original");
        var copyExternal = pending + " "; File.WriteAllText(copy, copyExternal); task = window.SaveProjectAsync();
        picker.Paths.Enqueue(copy); Answer("Save a copy"); check(!task.IsCompleted && File.ReadAllText(copy) == copyExternal, "copy selection cannot overwrite the conflicted original even when local edits are clean");
        picker.Paths.Enqueue(path); File.WriteAllText(path, "existing copy destination"); Answer("Save a copy");
        check(!task.IsCompleted && File.ReadAllText(path) == "existing copy destination", "copy selection preserves unrelated existing destinations");
        picker.Paths.Enqueue(Path.Combine(path, "failed-copy.json")); Answer("Save a copy");
        check(!task.IsCompleted && ProjectStore.Serialize(window.Project) == pending && window.CurrentFilePath == copy, "copy write failure retains the local story and file identity"); Answer("Cancel"); check(!Pump(task), "copy failures leave conflict cancellation available");
        File.WriteAllText(copy, pending); // Content alone is unchanged; Windows identity stayed unchanged too.
        Edit("Unsaved after copy"); var failBefore = ProjectStore.Serialize(window.Project); var prefsBefore = File.ReadAllBytes(Path.Combine(settings, "preferences.json"));
        if (OperatingSystem.IsWindows())
        {
            using var locked = new FileStream(copy, FileMode.Open, FileAccess.Read, FileShare.Read);
            task = window.SaveProjectAsync(); Answer("Close");
            check(!Pump(task) && window.HasUnsavedChanges && window.CurrentFilePath == copy && ProjectStore.Serialize(window.Project) == failBefore
                && File.ReadAllText(copy) == pending && File.ReadAllBytes(Path.Combine(settings, "preferences.json")).SequenceEqual(prefsBefore) && !Directory.GetFiles(root, "*.tmp").Any(), "ordinary replacement failure preserves dirty state, baseline, recents, local story and existing bytes");
        }
        check(Pump(window.SaveProjectAsync()) && !window.HasUnsavedChanges, "retry after a write failure succeeds against the unchanged saved baseline");
        check(Pump(window.SaveProjectAsync()) && !window.HasUnsavedChanges, "the next ordinary Save recognizes its own atomic replacement baseline");
        Edit("New pending work"); File.WriteAllText(copy, pending); picker.Paths.Enqueue(copy); task = window.SaveProjectAsync(true);
        Answer("Cancel"); check(!Pump(task) && window.HasUnsavedChanges, "Save as to the current path also respects ordinary conflict protection");
        foreach (var theme in new[] { "Dark", "Light", "High contrast" })
            foreach (var scale in new[] { 1d, 1.15, 1.3 })
            {
                window.ChangeAppearance(theme, scale); var name = "conflicts-" + theme.Replace(' ', '-') + "-" + (int)Math.Round(scale * 100);
                window.Width = 1540 * scale; window.Height = 940 * scale; task = window.SaveProjectAsync(); var dialog = Dialog(); capture(dialog, name + "-choices");
                dialog.Width = 400 * scale; dialog.Height = 300 * scale; Dispatcher.UIThread.RunJobs(); capture(dialog, name + "-choices-compact");
                check(dialog.GetVisualDescendants().OfType<Button>().Where(b => b.Name?.StartsWith("Conflict") == true).All(b => InWindow(b, dialog))
                    && dialog.GetVisualDescendants().OfType<ScrollViewer>().Single().Bounds.Height > 35, "compact conflict choices keep every action visible and explanation scrollable at " + theme + " " + scale);
                Answer("Compare"); dialog = Dialog(); capture(dialog, name + "-compare"); dialog.Width = 400 * scale; dialog.Height = 320 * scale; Dispatcher.UIThread.RunJobs(); capture(dialog, name + "-compare-compact");
                check(InWindow(Named<Button>(dialog, "ConflictBack"), dialog) && Named<TextBox>(dialog, "ConflictLocalJson").Bounds.Height > 70
                    && Named<TextBox>(dialog, "ConflictDiskJson").Bounds.Width > 90 && dialog.GetVisualDescendants().OfType<TextBox>().All(t => t.IsReadOnly && !string.IsNullOrEmpty(AutomationProperties.GetName(t))), "compact comparison keeps both labelled read-only versions and Back reachable at " + theme + " " + scale);
                Key(dialog, Avalonia.Input.Key.Escape); Answer("Cancel"); check(!Pump(task) && window.HasUnsavedChanges && File.ReadAllText(copy) == pending, "comparison Escape and conflict cancellation preserve edits at " + theme + " " + scale);
            }
        window.Close();
        var shared = Path.Combine(root, "shared.json"); File.WriteAllText(shared, original);
        var first = new MainWindow(null, true); first.Show(); Pump(first.OpenProjectAsync(shared));
        var second = new MainWindow(null, true); second.Show(); Pump(second.OpenProjectAsync(shared));
        first.Project.ProjectName = "Saved by first instance"; check(Pump(first.SaveProjectAsync()), "the first editor instance saves its own loaded version");
        var secondSave = second.SaveProjectAsync(); var secondDialog = second.OwnedWindows.Single(w => w.IsVisible);
        Press(secondDialog.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Cancel")));
        check(!Pump(secondSave) && !second.HasUnsavedChanges && second.Project.ProjectName == "Conflict story" && ProjectStore.Load(shared).ProjectName == "Saved by first instance", "a second clean editor instance detects the first instance's save and cannot overwrite it");
        second.Project.ProjectName = "Draft to undo"; secondSave = second.SaveProjectAsync();
        Press(second.OwnedWindows.Single(w => w.IsVisible).GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Cancel"))); Pump(secondSave);
        Press(second.FindControl<Button>("UndoButton")!); secondSave = second.SaveProjectAsync();
        Press(second.OwnedWindows.Single(w => w.IsVisible).GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Cancel")));
        check(!Pump(secondSave) && second.FindControl<Button>("RedoButton")!.IsEnabled && !second.HasUnsavedChanges, "canceling a clean conflicted Save preserves the existing Redo stack");
        Press(second.FindControl<Button>("RedoButton")!); check(second.Project.ProjectName == "Draft to undo" && second.HasUnsavedChanges, "Redo restores local edits after conflict cancellation");
        second.Project.ProjectName = "Local work before leaving"; var home = second.ReturnHomeAsync();
        Press(second.OwnedWindows.Single(w => w.IsVisible).GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Save changes")));
        Press(second.OwnedWindows.Single(w => w.IsVisible).GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Reload")));
        check(!Pump(home) && second.HasProject && second.Project.ProjectName == "Saved by first instance", "Reload during a save-before-leaving prompt keeps the document open with local Undo history");
        Press(second.FindControl<Button>("UndoButton")!); check(second.Project.ProjectName == "Local work before leaving" && second.HasUnsavedChanges, "save-before-leaving Reload retains local edits in Undo");
        first.Close(); second.Close();
        var activeSettings = Path.Combine(root, "active-recovery-settings"); var activeStore = new RecoveryStore(Path.Combine(activeSettings, "Recovery"));
        var activeId = Guid.NewGuid().ToString("N"); var activeStory = ProjectStore.Load(shared); activeStory.ProjectName = "Recovered local work";
        var activeBaseline = ProjectFiles.Read(shared).Version; activeStore.Save(activeId, new(shared, DateTimeOffset.UtcNow, activeStory, activeBaseline));
        var active = new MainWindow(null, true, activeSettings); active.Show(); Pump(active.InitialiseWorkspaceAsync());
        Press(active.FindControl<WelcomeView>("Welcome")!.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Restore story")));
        if (OperatingSystem.IsWindows())
        {
            using var locked = new FileStream(shared, FileMode.Open, FileAccess.Read, FileShare.Read);
            var failed = active.SaveProjectAsync();
            Press(active.OwnedWindows.Single(w => w.IsVisible).GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Close")));
            check(!Pump(failed) && activeStore.FindUnsaved().Single().Record.SavedFileVersion == activeBaseline && active.HasUnsavedChanges
                && active.Project.ProjectName == "Recovered local work" && ProjectStore.Load(shared).ProjectName == "Saved by first instance", "failed Save of a restored document preserves its active recovery and original disk baseline");
        }
        check(Pump(active.SaveProjectAsync()) && !active.HasUnsavedChanges && activeStore.FindUnsaved().Count == 0 && ProjectStore.Load(shared).ProjectName == "Recovered local work", "a restored document with an unchanged stored baseline saves safely and clears recovery only on success");
        active.Close();
    }
    private static bool InWindow(Control control, Window window)
    {
        var point = control.TranslatePoint(new Point(0, 0), window)!.Value;
        var scale = window.Content is LayoutTransformControl transform ? ((Avalonia.Media.ScaleTransform)transform.LayoutTransform!).ScaleX : 1;
        return control.IsEffectivelyVisible && point.X >= 0 && point.Y >= 0 && point.X + control.Bounds.Width * scale <= window.ClientSize.Width + 1 && point.Y + control.Bounds.Height * scale <= window.ClientSize.Height + 1;
    }
    private sealed class Picker : IProjectFilePicker
    {
        public Queue<string?> Paths { get; } = new();
        public Task<string?> OpenAsync(Window owner) => Task.FromResult<string?>(null);
        public Task<string?> SaveAsync(Window owner, string suggestedName) => Task.FromResult(Paths.Count > 0 ? Paths.Dequeue() : null);
        public Task<string?> ExportAsync(Window owner, string title, string suggestedName, string extension) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(Window owner, string title) => Task.FromResult<string?>(null);
    }
    private static T Named<T>(Window window, string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Press(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
    private static void Key(Window window, Key key) { window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, ""); if (window.IsVisible) window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, ""); Dispatcher.UIThread.RunJobs(); }
    private static T Pump<T>(Task<T> task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!task.IsCompleted && DateTime.UtcNow < deadline) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
        if (!task.IsCompleted) throw new Exception("Conflict operation did not complete.");
        var result = task.GetAwaiter().GetResult(); Dispatcher.UIThread.RunJobs(); return result;
    }
    private static void Pump(Task task)
    {
        async Task<bool> Complete() { await task; return true; }
        Pump(Complete());
    }
}
