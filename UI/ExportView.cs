using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using DialogueMachine.Core;
using System.IO;

namespace DialogueMachine.UI;

/// <summary>The export option dialogs (script, graph image, runtime companion, engine folder) and their file writes.</summary>
internal sealed class ExportView(Func<DialogueProject> getProject, IProjectFilePicker picker, Action<string> status)
{
    private Task<string?> Pick(string title, string name, string extension, Window owner) => picker.ExportAsync(owner, title, name, extension);

    private static Window Dialog(Window owner, double scale, string title, Control options, Button cancel, Button export)
    {
        var content = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(24), RowSpacing = 16 };
        content.Children.Add(Ui.Text(title, 24));
        var scroll = new ScrollViewer { Content = options, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); content.Children.Add(scroll);
        var buttons = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var button in new[] { cancel, export }) { button.Margin = new Thickness(0, 0, 8, 4); buttons.Children.Add(button); }
        Grid.SetRow(buttons, 2); content.Children.Add(buttons);
        var window = Ui.Dialog(owner, title, content, 560, 510, scale);
        window.MinWidth = 440 * scale; window.MinHeight = 340 * scale;
        return window;
    }

    public async Task<bool> Script(Window owner, double scale)
    {
        CheckBox Option(string name, string label, bool value)
        {
            var box = new CheckBox { Name = name, Content = Ui.Text(label), IsChecked = value };
            AutomationProperties.SetName(box, label); return box;
        }
        var conditions = Option("ExportConditions", "Conditions and visibility", true);
        var responseActions = Option("ExportResponseActions", "Response actions", true);
        var sceneActions = Option("ExportSceneActions", "Scene exit actions", true);
        var notes = Option("ExportNotes", "Writer’s notes", false);
        Window? dialog = null;
        var cancel = Ui.Button("Cancel", () => dialog!.Close(false)); cancel.Name = "CancelExport";
        var export = Ui.Button("Choose file…", () => dialog!.Close(true), "primary"); export.Name = "ConfirmExport";
        dialog = Dialog(owner, scale, "Export readable script", Ui.Stack(Ui.Text("All scenes, entry first, then project order. Dialogue pages and choices retain their authored order.", 13, true), conditions, responseActions, sceneActions, notes,
            Ui.Text("Inline conditions always retain both branches and their markers. Substitutions stay literal; author-written Markdown is escaped.", 13, true)), cancel, export);
        dialog.Opened += (_, _) => conditions.Focus();
        if (!await dialog.ShowDialog<bool>(owner)) return false;
        try
        {
            var path = await Pick("Export readable script", "story-script.md", "md", owner); if (path is null) return false;
            ProjectStore.AtomicWrite(path, ScriptExport.Markdown(getProject(), new(conditions.IsChecked == true, responseActions.IsChecked == true, sceneActions.IsChecked == true, notes.IsChecked == true)));
            status("Exported readable script to " + path); return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { await Ui.Ask(owner, "Could not export script", ex.Message, "Close"); return false; }
    }

    public async Task<bool> Graph(Window owner, double scale)
    {
        var resolution = new ComboBox { Name = "ExportResolution", ItemsSource = new[] { "Standard (1×)", "Detailed (2×)" }, SelectedIndex = 0 };
        var fit = new CheckBox { Name = "ExportFitLimits", Content = Ui.Text("Fit within limits (reduce resolution if needed)"), IsChecked = false };
        AutomationProperties.SetName(fit, "Fit within image size limits");
        var summary = Ui.Text("", 13, true); summary.Name = "ExportImageSummary"; AutomationProperties.SetLiveSetting(summary, AutomationLiveSetting.Polite);
        Window? dialog = null;
        var cancel = Ui.Button("Cancel", () => dialog!.Close(false)); cancel.Name = "CancelExport";
        var export = Ui.Button("Choose file…", () => dialog!.Close(true), "primary"); export.Name = "ConfirmExport";
        void Update()
        {
            try
            {
                var plan = GraphImageExport.Plan(getProject(), resolution.SelectedIndex == 1 ? 2 : 1, fit.IsChecked == true);
                summary.Text = $"{getProject().ListOfCharacterStates.Count} scenes · {plan.Pixels.Width:N0} × {plan.Pixels.Height:N0} pixels · {plan.Scale:0.##}×" + (plan.Reduced ? "\nResolution reduced to fit. Text will appear smaller." : "");
                export.IsEnabled = true;
            }
            catch (InvalidOperationException ex) { summary.Text = ex.Message; export.IsEnabled = false; }
        }
        resolution.SelectionChanged += (_, _) => Update(); fit.IsCheckedChanged += (_, _) => Update(); Update();
        dialog = Dialog(owner, scale, "Export full-story graph", Ui.Stack(Ui.Text("Every scene and connection, including scenes hidden by outline filters. The image uses the current theme.", 13, true), Ui.Field("Image resolution", resolution), fit, summary,
            Ui.Text("Maximum 16,384 pixels per side and 32 megapixels. Fit can reduce to 0.25×; larger stories need closer spacing or a script export.", 13, true)), cancel, export);
        dialog.Opened += (_, _) => resolution.Focus();
        if (!await dialog.ShowDialog<bool>(owner)) return false;
        try
        {
            var path = await Pick("Export full-story graph PNG", "story-graph.png", "png", owner); if (path is null) return false;
            GraphImageExport.Save(getProject(), path, resolution.SelectedIndex == 1 ? 2 : 1, fit.IsChecked == true);
            status("Exported full-story graph to " + path); return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or OutOfMemoryException)
        { await Ui.Ask(owner, "Could not export graph", ex is OutOfMemoryException ? "There is not enough memory for this image. Try Standard resolution or export a readable script." : ex.Message, "Close"); return false; }
    }

    public async Task<bool> RuntimeCompanion(Window owner, double scale)
    {
        var seedInput = new NumericUpDown
        {
            Name = "ExportRuntimeSeed",
            Minimum = int.MinValue,
            Maximum = int.MaxValue,
            Value = 0,
            Increment = 1,
            FormatString = "0"
        };
        AutomationProperties.SetName(seedInput, "Random seed");

        var maxOpsInput = new NumericUpDown
        {
            Name = "ExportRuntimeMaxOps",
            Minimum = 1,
            Maximum = 10000,
            Value = 1000,
            Increment = 100,
            FormatString = "0"
        };
        AutomationProperties.SetName(maxOpsInput, "Maximum operations");

        var summary = Ui.Text("", 13, true);
        summary.Name = "ExportRuntimeSummary";
        AutomationProperties.SetLiveSetting(summary, AutomationLiveSetting.Polite);

        Window? dialog = null;
        var cancel = Ui.Button("Cancel", () => dialog!.Close(false));
        cancel.Name = "CancelExport";
        var export = Ui.Button("Choose file…", () => dialog!.Close(true), "primary");
        export.Name = "ConfirmExport";

        void Update()
        {
            try
            {
                var seed = (int)(seedInput.Value ?? 0);
                var maxOps = (int)(maxOpsInput.Value ?? 1000);
                var companion = RuntimeExport.Create(getProject(), seed, maxOps);
                summary.Text = $"Starting scene: {getProject().Entry} · {companion.NumericValues.Count} active numeric variables · {companion.TextValues.Count} text substitutions\nRuntime contract: {companion.Contract} (schema v{companion.SchemaVersion})";
                export.IsEnabled = true;
            }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentException or InvalidOperationException)
            {
                summary.Text = ex.Message;
                export.IsEnabled = false;
            }
        }

        seedInput.ValueChanged += (_, _) => Update();
        maxOpsInput.ValueChanged += (_, _) => Update();
        Update();

        dialog = Dialog(owner, scale, "Export runtime companion", Ui.Stack(
            Ui.Text("Creates a self-contained companion payload (dialogue-machine.website.v1) containing the game story, initial seed, starting numeric variables, and text substitutions.", 13, true),
            Ui.Row(Ui.Field("Random seed", seedInput), Ui.Field("Maximum operations", maxOpsInput)),
            summary,
            Ui.Text("The companion contract decouples website and headless runners from editor format changes.", 13, true)),
            cancel, export);
        dialog.Opened += (_, _) => seedInput.Focus();
        if (!await dialog.ShowDialog<bool>(owner)) return false;

        try
        {
            var seed = (int)(seedInput.Value ?? 0);
            var maxOps = (int)(maxOpsInput.Value ?? 1000);
            var companion = RuntimeExport.Create(getProject(), seed, maxOps);
            var path = await Pick("Export runtime companion", "story.runtime.json", "json", owner);
            if (path is null) return false;
            ProjectStore.AtomicWrite(path, RuntimeExport.Serialize(companion));
            status("Exported runtime companion to " + path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or InvalidDataException)
        {
            await Ui.Ask(owner, "Could not export runtime companion", ex.Message, "Close");
            return false;
        }
    }

    /// <summary>Shows the engine export folder dialog. Returns null when cancelled, otherwise the chosen folder (null inside the result = cleared).</summary>
    public async Task<(string? Folder, bool Saved)> EngineFolder(Window owner, double scale, string? current)
    {
        var folderInput = new TextBox
        {
            Name = "EngineExportFolderPath",
            Text = current ?? "",
            PlaceholderText = "Path to engine Assets or project folder…",
            MinHeight = 36
        };
        Avalonia.Automation.AutomationProperties.SetName(folderInput, "Engine export folder path");

        var browseButton = Ui.AsyncButton("Choose folder…", async () =>
        {
            var picked = await picker.PickFolderAsync(owner, "Choose engine export folder");
            if (!string.IsNullOrWhiteSpace(picked)) folderInput.Text = picked;
        });
        browseButton.Name = "BrowseEngineExportFolder";

        var clearButton = Ui.Button("Clear", () => folderInput.Text = "");
        clearButton.Name = "ClearEngineExportFolder";

        var controlsRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            Children = { folderInput, browseButton, clearButton }
        };
        browseButton.Margin = new Thickness(8, 0, 0, 0);
        clearButton.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(folderInput, 0);
        Grid.SetColumn(browseButton, 1);
        Grid.SetColumn(clearButton, 2);

        Window? dialog = null;
        var cancel = Ui.Button("Cancel", () => dialog!.Close(false));
        cancel.Name = "CancelExport";
        var save = Ui.Button("Save", () => dialog!.Close(true), "primary");
        save.Name = "SaveEngineExportFolder";

        var content = Ui.Stack(
            Ui.Text("Engine export folder", 24),
            Ui.Text("When configured, saving your story automatically exports a companion runtime file (*.runtime.json) directly into your game engine project (Unity, Unreal, Godot).", 13, true),
            Ui.Field("Folder location", controlsRow),
            Ui.Text("The export file is named <ProjectName-slug>.runtime.json and is only rewritten when the story changes. Stories with validation errors will not overwrite existing exports.", 12, true),
            Ui.Row(cancel, save)
        );
        content.Margin = new Thickness(24);

        dialog = Ui.Dialog(owner, "Engine export folder", content, 560, 320, scale);
        dialog.Opened += (_, _) => folderInput.Focus();

        if (!await dialog.ShowDialog<bool>(owner)) return (null, false);
        var next = string.IsNullOrWhiteSpace(folderInput.Text) ? null : folderInput.Text.Trim();
        return (next, true);
    }
}
