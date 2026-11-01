using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DialogueMachine;
using DialogueMachine.Core;
using DialogueMachine.UI;
using System.Text.Json;
using static UiHelpers;


internal static class WorkspaceChecks
{
    static string settingsRoot = "", sourceFile = "", savedFile = "", missingFile = "", sourceJson = "";
    static WorkspaceStore store = null!;
    static WorkspacePreferences prefs = null!;

    public static void Preferences(Action<bool, string> Check, string workflowRoot)
    {
        settingsRoot = Path.Combine(workflowRoot, "settings"); store = new WorkspaceStore(settingsRoot);
        sourceFile = Path.Combine(workflowRoot, "website-story.json"); savedFile = Path.Combine(workflowRoot, "my-story.dialogue.json");
        sourceJson = ProjectStore.WebExport(Templates.Harbour()); File.WriteAllText(sourceFile, sourceJson);
        missingFile = Path.Combine(workflowRoot, "moved-story.json");
        ProjectStore.AtomicWrite(Path.Combine(settingsRoot, "preferences.json"), JsonSerializer.Serialize(new { Theme = "Light", Scale = 1.15, RecoveryEnabled = false, RecentFiles = new[] { sourceFile, sourceFile, missingFile } }, ProjectStore.Options));
        prefs = store.Load();
        Check(prefs.Theme == "Light" && prefs.Scale == 1.15 && !prefs.RecoveryEnabled && prefs.RecentProjects.Count == 2, "old preferences migrate without resetting appearance or duplicating recent files");
        Check(!prefs.ReopenLastProjectOnStartup && WorkspaceStartup.Plan(prefs, null, false).ProjectPath is null, "reopening the last project is opt-in");
        WorkspaceStore.Remember(prefs, sourceFile, "Website story"); WorkspaceStore.Remember(prefs, savedFile, "My story"); WorkspaceStore.Remember(prefs, sourceFile, "Updated story");
        Check(prefs.RecentProjects[0].Name == "Updated story" && prefs.RecentProjects.Count == 3 && prefs.LastProjectPath == sourceFile, "recents retain titles, deduplicate paths and move opened files to the top");
        WorkspaceStore.Forget(prefs, sourceFile); Check(File.ReadAllText(sourceFile) == sourceJson && prefs.LastProjectPath == savedFile, "removing a recent never deletes the story file");
        for (var i = 0; i < 10; i++) WorkspaceStore.Remember(prefs, Path.Combine(workflowRoot, $"story-{i}.json"), $"Story {i}");
        Check(prefs.RecentProjects.Count == 8, "recent projects are bounded to eight entries");
        prefs.LastProjectPath = sourceFile; prefs.ReopenLastProjectOnStartup = true;
        Check(WorkspaceStartup.Plan(prefs, null, false).ProjectPath == sourceFile, "opt-in startup selects the existing last project");
        Check(WorkspaceStartup.Plan(prefs, null, true).ProjectPath is null && WorkspaceStartup.Plan(prefs, null, true).Notice is not null, "recovery takes priority over automatic reopen");
        Check(WorkspaceStartup.Plan(prefs, savedFile, true).ProjectPath == savedFile, "an explicit launch file takes priority over startup preferences");
        prefs.LastProjectPath = missingFile; Check(WorkspaceStartup.Plan(prefs, null, false).Notice is not null, "a missing last project falls back to welcome with an explanation");
        store.Save(new WorkspacePreferences());
    }

    public static void Session(Action<bool, string> Check, Action<Window, string> Capture, string workflowRoot)
    {
        var semanticDamage = RecoveryChecks.SemanticDamage;
        var picker = new TestFilePicker(); var workspace = new MainWindow(null, true, settingsRoot, picker); workspace.Show(); Dispatcher.UIThread.RunJobs();
        var welcome = workspace.FindControl<WelcomeView>("Welcome")!;
        Pump(workspace.InitialiseWorkspaceAsync());
        Check(!workspace.HasProject, "a fresh persisted workspace starts at welcome");
        Press(welcome.FindControl<Button>("ExampleButton")!);
        Check(workspace.HasProject && store.Load().RecentProjects.Count == 0, "exploring the bundled example does not add it to recent projects");
        Pump(workspace.ReturnHomeAsync());
        workspace.KeyPress(Key.N, RawInputModifiers.Control, PhysicalKey.N, "n"); workspace.KeyRelease(Key.N, RawInputModifiers.Control, PhysicalKey.N, "n"); Dispatcher.UIThread.RunJobs();
        var newDialog = Modal(workspace); Check(newDialog.GetVisualDescendants().OfType<TextBox>().SingleOrDefault(t => t.Name == "NewProjectTitle") is not null, "Ctrl+N opens the named project form from welcome");
        newDialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, ""); Dispatcher.UIThread.RunJobs();
        Check(!workspace.HasProject && !workspace.OwnedWindows.Any(w => w.IsVisible), "Escape cancels project creation without changing the workspace");
        Press(welcome.FindControl<Button>("NewProjectButton")!); newDialog = Modal(workspace);
        newDialog.GetVisualDescendants().OfType<TextBox>().SingleOrDefault(t => t.Name == "NewProjectTitle")!.Text = ""; Answer(workspace, "Create project");
        Check(!workspace.HasProject && newDialog.IsVisible, "a blank project title keeps the form open for correction");
        newDialog.GetVisualDescendants().OfType<TextBox>().SingleOrDefault(t => t.Name == "NewProjectTitle")!.Text = "My first story"; newDialog.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "NewProjectCharacter")!.Text = "captain"; Answer(workspace, "Create project");
        Check(workspace.HasProject && workspace.HasUnsavedChanges && workspace.Project.ProjectName == "My first story" && workspace.Project.CharacterId == "captain" && workspace.Project.ListOfCharacterStates.Single().Text == "", "new projects have the requested identity and a genuinely blank opening scene");
        picker.SavePaths.Enqueue(null); var home = workspace.ReturnHomeAsync(); Answer(workspace, "Save changes"); Pump(home);
        Check(!home.Result && workspace.HasProject && workspace.HasUnsavedChanges, "canceling Save as also cancels leaving an unsaved project");
        home = workspace.ReturnHomeAsync(); Answer(workspace, "Cancel"); Pump(home);
        Check(!home.Result && workspace.Project.ProjectName == "My first story", "canceling the unsaved-changes dialog keeps the current story");
        home = workspace.ReturnHomeAsync(); Answer(workspace, "Discard changes"); Pump(home);
        Check(home.Result && !workspace.HasProject && welcome.IsVisible, "discard returns to welcome without creating a project file");
        picker.OpenPaths.Enqueue(sourceFile); Press(welcome.FindControl<Button>("OpenProjectButton")!);
        Check(workspace.HasProject && !workspace.HasUnsavedChanges && workspace.CurrentFilePath == sourceFile && workspace.Project.ListOfCharacterStates.Count == 5, "Browse files opens website-format JSON through the storage boundary");
        var contentInputs = workspace.FindControl<StackPanel>("EditorPanel")!.GetVisualDescendants().OfType<TextBox>().ToList();
        contentInputs.First().Text = "My opening";
        contentInputs.Single(t => AutomationProperties.GetName(t) == "Dialogue text").Text = "The door is open.\nWill you enter?";
        picker.SavePaths.Enqueue(null); var save = workspace.SaveProjectAsync(true); Pump(save);
        Check(!save.Result && workspace.CurrentFilePath == sourceFile && workspace.HasUnsavedChanges && File.ReadAllText(sourceFile) == sourceJson, "canceled Save as keeps the original location, file and pending edits");
        picker.SavePaths.Enqueue(savedFile); save = workspace.SaveProjectAsync(true); Pump(save);
        var savedProject = ProjectStore.Load(savedFile);
        Check(save.Result && !workspace.HasUnsavedChanges && workspace.CurrentFilePath == savedFile && savedProject.ListOfCharacterStates[0].Title == "My opening" && savedProject.ListOfCharacterStates[0].Text.StartsWith("The door"), "Save as writes edited content and marks the new file as saved");
        Check(File.ReadAllText(sourceFile) == sourceJson && savedProject.ListOfCharacterStates[0].Choices[1].Requirements.Count == 1, "Save as preserves the original website file and branching conditions");
        var badFile = Path.Combine(workflowRoot, "invalid.json"); File.WriteAllText(badFile, "{}"); var beforeBadOpen = ProjectStore.Serialize(workspace.Project);
        var openBad = workspace.OpenProjectAsync(badFile); Answer(workspace, "Close"); Pump(openBad);
        Check(!openBad.Result && workspace.CurrentFilePath == savedFile && ProjectStore.Serialize(workspace.Project) == beforeBadOpen, "invalid imports show an error and preserve the open story");
        File.WriteAllText(badFile, """{"FormatVersion":999,"ListOfCharacterStates":[]}""");
        openBad = workspace.OpenProjectAsync(badFile); Answer(workspace, "Close"); Pump(openBad);
        Check(!openBad.Result && workspace.CurrentFilePath == savedFile && ProjectStore.Serialize(workspace.Project) == beforeBadOpen, "unsupported project versions preserve the currently open story");
        Pump(workspace.ReturnHomeAsync());
        Check(welcome.FindControl<StackPanel>("RecentCards")!.GetVisualDescendants().OfType<Button>().Count(b => b.Tag is string) == 2, "welcome lists the saved and imported projects with their titles");
        Capture(workspace, "welcome-recent");
        Press(welcome.FindControl<StackPanel>("RecentCards")!.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Tag, savedFile)));
        Check(workspace.CurrentFilePath == savedFile && workspace.Project.ListOfCharacterStates[0].Title == "My opening", "opening a recent project reloads the saved content");
        workspace.Project.ProjectName = "Saved on Home"; picker.SavePaths.Enqueue(savedFile); home = workspace.ReturnHomeAsync(); Answer(workspace, "Save changes"); Pump(home);
        Check(home.Result && !workspace.HasProject && ProjectStore.Load(savedFile).ProjectName == "Saved on Home", "Save changes completes before returning to welcome");
        welcome.FindControl<CheckBox>("ReopenLastCheckBox")!.IsChecked = true; Dispatcher.UIThread.RunJobs();
        Check(store.Load().ReopenLastProjectOnStartup, "the welcome reopen preference persists immediately"); workspace.Close();
        var reopened = new MainWindow(null, true, settingsRoot, new TestFilePicker()); reopened.Show(); Pump(reopened.InitialiseWorkspaceAsync());
        Check(reopened.HasProject && reopened.CurrentFilePath == savedFile && reopened.Project.ProjectName == "Saved on Home", "a new session reopens the last saved project when enabled"); reopened.Close();
        prefs = store.Load(); WorkspaceStore.Remember(prefs, missingFile, "Moved story"); store.Save(prefs);
        var missing = new MainWindow(null, true, settingsRoot, new TestFilePicker()); missing.Show(); Pump(missing.InitialiseWorkspaceAsync());
        var missingWelcome = missing.FindControl<WelcomeView>("Welcome")!;
        Check(!missing.HasProject && missingWelcome.FindControl<Border>("NoticeBorder")!.IsVisible && !missingWelcome.FindControl<StackPanel>("RecentCards")!.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Tag, missingFile)).IsEnabled, "missing files are explained and unavailable recent entries are disabled");
        missing.Close();
        var restoreId = Guid.NewGuid().ToString("N"); var restoreStore = new RecoveryStore(Path.Combine(settingsRoot, "Recovery"));
        var recovered = Templates.Harbour(); recovered.ProjectName = "Recovered work"; restoreStore.Save(restoreId, new RecoveryRecord(null, DateTimeOffset.Now, recovered));
        var damagedStartupId = Guid.NewGuid().ToString("N"); restoreStore.Save(damagedStartupId, new RecoveryRecord(null, DateTimeOffset.Now, semanticDamage));
        prefs = store.Load(); prefs.LastProjectPath = savedFile; store.Save(prefs);
        picker = new TestFilePicker(); var recoverWindow = new MainWindow(null, true, settingsRoot, picker); recoverWindow.Show(); Pump(recoverWindow.InitialiseWorkspaceAsync());
        var recoveryWelcome = recoverWindow.FindControl<WelcomeView>("Welcome")!;
        Check(!recoverWindow.HasProject && recoveryWelcome.FindControl<StackPanel>("RecoverySection")!.IsVisible, "unsaved recovery is presented before automatically reopening a saved project"); Capture(recoverWindow,"welcome-recovery");
        Check(File.Exists(Path.Combine(settingsRoot, "Recovery", damagedStartupId + ".json")) && restoreStore.FindUnsaved().Count == 1, "Welcome starts with damaged recovery and still offers valid unsaved work");
        Press(recoveryWelcome.FindControl<StackPanel>("RecoveryCards")!.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Content, "Restore story")));
        Check(recoverWindow.Project.ProjectName == "Recovered work" && recoverWindow.HasUnsavedChanges && restoreStore.FindUnsaved().Count == 1 && ProjectStore.Load(savedFile).ProjectName == "Saved on Home", "restoring opens unsaved work without overwriting or removing its recovery");
        picker.SavePaths.Enqueue(Path.Combine(workflowRoot, "recovered.dialogue.json")); Pump(recoverWindow.SaveProjectAsync());
        Check(restoreStore.FindUnsaved().Count == 0 && !recoverWindow.HasUnsavedChanges, "successful saving clears the restored source recovery"); recoverWindow.Close();
    }
}
