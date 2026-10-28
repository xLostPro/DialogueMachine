using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using DialogueMachine.Core;
using System.IO;

namespace DialogueMachine.UI;

public sealed partial class MainWindow
{
    private Task ExportScript() => ExportScriptAsync();
    private Task ExportGraph() => ExportGraphAsync();
    private Task ExportRuntimeCompanion() => ExportRuntimeCompanionAsync();

    private Window ExportDialog(string title, Control options, Button cancel, Button export)
    {
        var content = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(24), RowSpacing = 16 };
        content.Children.Add(Ui.Text(title, 24));
        var scroll = new ScrollViewer { Content = options, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); content.Children.Add(scroll);
        var buttons = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var button in new[] { cancel, export }) { button.Margin = new Thickness(0, 0, 8, 4); buttons.Children.Add(button); }
        Grid.SetRow(buttons, 2); content.Children.Add(buttons);
        var window = Ui.Dialog(this, title, content, 560, 510, preferences.Scale);
        window.MinWidth = 440 * preferences.Scale; window.MinHeight = 340 * preferences.Scale;
        return window;
    }

    public async Task<bool> ExportScriptAsync()
    {
        if (!hasProject) return false;
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
        dialog = ExportDialog("Export readable script", Ui.Stack(Ui.Text("All scenes, entry first, then project order. Dialogue pages and choices retain their authored order.", 13, true), conditions, responseActions, sceneActions, notes,
            Ui.Text("Inline conditions always retain both branches and their markers. Substitutions stay literal; author-written Markdown is escaped.", 13, true)), cancel, export);
        dialog.Opened += (_, _) => conditions.Focus();
        if (!await dialog.ShowDialog<bool>(this)) return false;
        try
        {
            var path = await PickExport("Export readable script", "story-script.md", "md"); if (path is null) return false;
            ProjectStore.AtomicWrite(path, ScriptExport.Markdown(project, new(conditions.IsChecked == true, responseActions.IsChecked == true, sceneActions.IsChecked == true, notes.IsChecked == true)));
            Status("Exported readable script to " + path); return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { await Ui.Ask(this, "Could not export script", ex.Message, "Close"); return false; }
    }

    public async Task<bool> ExportGraphAsync()
    {
        if (!hasProject) return false;
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
                var plan = GraphImageExport.Plan(project, resolution.SelectedIndex == 1 ? 2 : 1, fit.IsChecked == true);
                summary.Text = $"{project.ListOfCharacterStates.Count} scenes · {plan.Pixels.Width:N0} × {plan.Pixels.Height:N0} pixels · {plan.Scale:0.##}×" + (plan.Reduced ? "\nResolution reduced to fit. Text will appear smaller." : "");
                export.IsEnabled = true;
            }
            catch (InvalidOperationException ex) { summary.Text = ex.Message; export.IsEnabled = false; }
        }
        resolution.SelectionChanged += (_, _) => Update(); fit.IsCheckedChanged += (_, _) => Update(); Update();
        dialog = ExportDialog("Export full-story graph", Ui.Stack(Ui.Text("Every scene and connection, including scenes hidden by outline filters. The image uses the current theme.", 13, true), Ui.Field("Image resolution", resolution), fit, summary,
            Ui.Text("Maximum 16,384 pixels per side and 32 megapixels. Fit can reduce to 0.25×; larger stories need closer spacing or a script export.", 13, true)), cancel, export);
        dialog.Opened += (_, _) => resolution.Focus();
        if (!await dialog.ShowDialog<bool>(this)) return false;
        try
        {
            var path = await PickExport("Export full-story graph PNG", "story-graph.png", "png"); if (path is null) return false;
            GraphImageExport.Save(project, path, resolution.SelectedIndex == 1 ? 2 : 1, fit.IsChecked == true);
            Status("Exported full-story graph to " + path); return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or OutOfMemoryException)
        { await Ui.Ask(this, "Could not export graph", ex is OutOfMemoryException ? "There is not enough memory for this image. Try Standard resolution or export a readable script." : ex.Message, "Close"); return false; }
    }

    public async Task<bool> ExportRuntimeCompanionAsync()
    {
        if (!hasProject) return false;
        Commit();
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
                var companion = RuntimeExport.Create(project, seed, maxOps);
                summary.Text = $"Starting scene: {project.Entry} · {companion.NumericValues.Count} active numeric variables · {companion.TextValues.Count} text substitutions\nRuntime contract: {companion.Contract} (schema v{companion.SchemaVersion})";
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

        dialog = ExportDialog("Export runtime companion", Ui.Stack(
            Ui.Text("Creates a self-contained companion payload (dialogue-machine.website.v1) containing the game story, initial seed, starting numeric variables, and text substitutions.", 13, true),
            Ui.Row(Ui.Field("Random seed", seedInput), Ui.Field("Maximum operations", maxOpsInput)),
            summary,
            Ui.Text("The companion contract decouples website and headless runners from editor format changes.", 13, true)),
            cancel, export);
        dialog.Opened += (_, _) => seedInput.Focus();
        if (!await dialog.ShowDialog<bool>(this)) return false;

        try
        {
            var seed = (int)(seedInput.Value ?? 0);
            var maxOps = (int)(maxOpsInput.Value ?? 1000);
            var companion = RuntimeExport.Create(project, seed, maxOps);
            var path = await PickExport("Export runtime companion", "story.runtime.json", "json");
            if (path is null) return false;
            ProjectStore.AtomicWrite(path, RuntimeExport.Serialize(companion));
            Status("Exported runtime companion to " + path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or InvalidDataException)
        {
            await Ui.Ask(this, "Could not export runtime companion", ex.Message, "Close");
            return false;
        }
    }
}
