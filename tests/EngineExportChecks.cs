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


internal static class EngineExportChecks
{
    public static void UI(Action<bool, string> Check, Action<Window, string> Capture, string workflowRoot)
    {
        var settingsRoot = Path.Combine(workflowRoot, "settings");
        var runtimePicker = new TestFilePicker();
        var runtimeWindow = new MainWindow(null, true, settingsRoot, runtimePicker);
        runtimeWindow.Show();
        Pump(runtimeWindow.LoadExampleAsync());
        var fileMenu = runtimeWindow.FindControl<Menu>("AppMenu")!.Items.OfType<MenuItem>().First(m => (string?)m.Header == "_File");
        Check(fileMenu.ItemsSource!.OfType<MenuItem>().Any(m => (string?)m.Header == "Export runtime companion…"), "File menu includes Export runtime companion action");
        Check(fileMenu.ItemsSource!.OfType<MenuItem>().Any(m => (string?)m.Header == "Engine export folder…"), "File menu includes Engine export folder action");

        var exportTask = runtimeWindow.ExportRuntimeCompanionAsync();
        var exportDialog = Modal(runtimeWindow);
        var seedInput = NamedControl<NumericUpDown>(exportDialog, "ExportRuntimeSeed");
        var maxOpsInput = NamedControl<NumericUpDown>(exportDialog, "ExportRuntimeMaxOps");
        var exportSummary = NamedControl<TextBlock>(exportDialog, "ExportRuntimeSummary");
        var confirmBtn = NamedControl<Button>(exportDialog, "ConfirmExport");
        var cancelBtn = NamedControl<Button>(exportDialog, "CancelExport");

        Check(seedInput is not null && maxOpsInput is not null && exportSummary is not null && confirmBtn is not null && cancelBtn is not null, "runtime companion dialog controls exist");
        Check(AutomationProperties.GetName(seedInput!) == "Random seed" && AutomationProperties.GetName(maxOpsInput!) == "Maximum operations", "runtime companion controls have accessibility names");
        Check(confirmBtn!.IsEnabled && exportSummary!.Text!.Contains(runtimeWindow.Project.Entry), "runtime companion dialog shows starting scene and enables export");
        Capture(exportDialog, "export-runtime-companion");

        seedInput!.Value = 42;
        var runtimeOut = Path.Combine(workflowRoot, "harbour.runtime.json");
        runtimePicker.SavePaths.Enqueue(runtimeOut);
        Press(confirmBtn!);
        Pump(exportTask);

        Check(File.Exists(runtimeOut), "runtime companion export creates file on disk");
        var parsedCompanion = JsonSerializer.Deserialize<RuntimeHandoff>(File.ReadAllText(runtimeOut), ProjectStore.Options)!;
        Check(parsedCompanion.Seed == 42 && parsedCompanion.Contract == RuntimeExport.Contract && parsedCompanion.StartingScene == runtimeWindow.Project.Entry, "exported runtime companion contains seed 42 and valid contract");

        var beforeState = ProjectStore.Serialize(runtimeWindow.Project);
        runtimeWindow.Project.InitialVariables["bad_var"] = 5;
        var invalidTask = runtimeWindow.ExportRuntimeCompanionAsync();
        var invalidDialog = Modal(runtimeWindow);
        var invalidSummary = NamedControl<TextBlock>(invalidDialog, "ExportRuntimeSummary");
        var invalidConfirm = NamedControl<Button>(invalidDialog, "ConfirmExport");

        Check(invalidConfirm is not null && !invalidConfirm.IsEnabled && invalidSummary.Text?.Contains("bad_var") == true, "invalid runtime variable ID disables ConfirmExport and shows repair guidance");
        invalidDialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");
        Dispatcher.UIThread.RunJobs();
        Pump(invalidTask);
        runtimeWindow.Project.InitialVariables.Remove("bad_var");
        Check(ProjectStore.Serialize(runtimeWindow.Project) == beforeState && !runtimeWindow.HasUnsavedChanges, "malformed runtime variable check does not mutate history or crash");

        var unsavedFolderTask = runtimeWindow.ConfigureEngineExportFolderAsync();
        var unsavedPrompt = Modal(runtimeWindow);
        Press(unsavedPrompt.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Close")));
        Pump(unsavedFolderTask);
        Check(!unsavedFolderTask.Result, "configuring engine export folder on unsaved story prompts to save first");

        var savedStoryPath = Path.Combine(workflowRoot, "saved-story.dialogue.json");
        runtimePicker.SavePaths.Enqueue(savedStoryPath);
        Pump(runtimeWindow.SaveProjectAsync(true));

        var configTask = runtimeWindow.ConfigureEngineExportFolderAsync();
        var configDialog = Modal(runtimeWindow);
        var folderInput = NamedControl<TextBox>(configDialog, "EngineExportFolderPath");
        var browseBtn = NamedControl<Button>(configDialog, "BrowseEngineExportFolder");
        var clearBtn = NamedControl<Button>(configDialog, "ClearEngineExportFolder");
        var saveFolderBtn = NamedControl<Button>(configDialog, "SaveEngineExportFolder");
        var cancelFolderBtn = NamedControl<Button>(configDialog, "CancelExport");

        Check(folderInput is not null && browseBtn is not null && clearBtn is not null && saveFolderBtn is not null && cancelFolderBtn is not null, "engine export folder dialog controls exist");
        Check(AutomationProperties.GetName(folderInput!) == "Engine export folder path", "engine export folder controls have accessibility names");

        var targetEngineFolder = Path.Combine(workflowRoot, "EngineAssets");
        runtimePicker.FolderPaths.Enqueue(targetEngineFolder);
        Press(browseBtn!);
        Check(folderInput!.Text == targetEngineFolder, "browse button updates engine export folder input");

        Press(clearBtn!);
        Check(string.IsNullOrEmpty(folderInput!.Text), "clear button empties engine export folder input");

        folderInput!.Text = targetEngineFolder;
        Capture(configDialog, "engine-export-folder-dialog");
        Press(saveFolderBtn!);
        Pump(configTask);

        Check(runtimeWindow.Project.EngineExportFolder == "EngineAssets" && runtimeWindow.HasUnsavedChanges, "configured engine export folder is stored in the project, relative to the project file");
        Check(WorkspaceStore.GetEngineExportFolder(new WorkspaceStore(settingsRoot).Load(), savedStoryPath) is null, "configured engine export folder is not kept in machine preferences");

        var expectedAutoExport = Path.Combine(targetEngineFolder, RuntimeExport.Slug(runtimeWindow.Project.ProjectName) + ".runtime.json");
        Check(File.Exists(expectedAutoExport), "auto-export wrote file to engine folder on folder config / save");
        Check(File.ReadAllText(expectedAutoExport) == RuntimeExport.Serialize(RuntimeExport.Create(runtimeWindow.Project)), "auto-exported runtime file matches RuntimeExport.Serialize");

        var writeTime1 = File.GetLastWriteTimeUtc(expectedAutoExport);
        Thread.Sleep(50);
        Pump(runtimeWindow.SaveProjectAsync());
        Check(File.GetLastWriteTimeUtc(expectedAutoExport) == writeTime1, "unchanged SHA skips the export write on save");

        // Introduce validation error and save
        runtimeWindow.Project.ListOfCharacterStates[0].DefaultNextId = "broken_scene_target";
        var lastGoodContent = File.ReadAllText(expectedAutoExport);
        var writeTimeBeforeBadSave = File.GetLastWriteTimeUtc(expectedAutoExport);
        Pump(runtimeWindow.SaveProjectAsync());
        Check(File.ReadAllText(expectedAutoExport) == lastGoodContent, "invalid story on save leaves the last good export file intact");
        Check(File.GetLastWriteTimeUtc(expectedAutoExport) == writeTimeBeforeBadSave, "invalid story on save does not overwrite export file");
        Check(runtimeWindow.FindControl<Border>("DiagnosticsBorder")!.IsVisible, "invalid story on save shows diagnostics");

        // Restore valid target
        runtimeWindow.Project.ListOfCharacterStates[0].DefaultNextId = null;
        Pump(runtimeWindow.SaveProjectAsync());
        Check(ProjectStore.Load(savedStoryPath).EngineExportFolder == "EngineAssets", "saving writes the engine export folder into the project file");

        var movedStoryPath = Path.Combine(workflowRoot, "Stories", "moved-story.dialogue.json");
        runtimePicker.SavePaths.Enqueue(movedStoryPath);
        Pump(runtimeWindow.SaveProjectAsync(true));
        Check(runtimeWindow.Project.EngineExportFolder == "../EngineAssets" && ProjectStore.Load(movedStoryPath).EngineExportFolder == "../EngineAssets", "Save as into another folder keeps the export folder pointing at the same place");

        Directory.Delete(targetEngineFolder, true);
        runtimeWindow.Project.ListOfCharacterStates[0].Text += " Edited.";
        Pump(runtimeWindow.SaveProjectAsync());
        Check(!Directory.Exists(targetEngineFolder) && runtimeWindow.FindControl<TextBlock>("StatusText")!.Text!.Contains("folder not found"), "a missing engine export folder is reported instead of silently recreated");
        Directory.CreateDirectory(targetEngineFolder);

        // Clear folder
        configTask = runtimeWindow.ConfigureEngineExportFolderAsync();
        configDialog = Modal(runtimeWindow);
        clearBtn = NamedControl<Button>(configDialog, "ClearEngineExportFolder");
        saveFolderBtn = NamedControl<Button>(configDialog, "SaveEngineExportFolder");
        Press(clearBtn!);
        Press(saveFolderBtn!);
        Pump(configTask);
        Check(runtimeWindow.Project.EngineExportFolder is null, "clearing the engine export folder removes it from the project");

        // A folder configured by an older version lives only in this machine's preferences; the next save moves it into the project.
        var legacyRoot = Path.Combine(workflowRoot, "legacy-settings");
        var legacyStore = new WorkspaceStore(legacyRoot); var legacyPrefs = legacyStore.Load();
        WorkspaceStore.SetEngineExportFolder(legacyPrefs, movedStoryPath, targetEngineFolder); legacyStore.Save(legacyPrefs);
        var legacyWindow = new MainWindow(null, true, legacyRoot, new TestFilePicker()); legacyWindow.Show();
        Pump(legacyWindow.OpenProjectAsync(movedStoryPath));
        Pump(legacyWindow.SaveProjectAsync());
        Check(ProjectStore.Load(movedStoryPath).EngineExportFolder == "../EngineAssets", "saving a project configured by an older version moves its export folder into the project");
        legacyWindow.Close();

        runtimeWindow.Close();
    }
}
