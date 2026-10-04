using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Selection;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.VisualTree;
using DialogueMachine.Core;

namespace DialogueMachine.UI;

public sealed partial class MainWindow
{
    private SceneBundle? copiedScenes;
    private sealed record CopyOption(DialogueState Scene) { public override string ToString() => Scene.Title + " [" + Scene.State + "]"; }
    private sealed record TemplateOption(SceneTemplate? Scene, ChoiceTemplate? Choice)
    {
        public override string ToString() => Scene is not null ? "Scene · " + Scene.Name : "Choice · " + Choice!.Name;
    }

    private async Task AuthoringAction(Action action)
    {
        try { action(); }
        catch (ArgumentException ex) { await Ui.Ask(this, "Cannot complete operation", ex.Message, "Close"); }
    }

    private async Task CreateSceneFromChoice(DialogueState source, Choice choice)
    {
        Commit();
        var title = await Ui.Prompt(this, "Create a connected scene", "Name the new scene. This choice will point to it; Undo restores its previous destination.", "New scene", preferences.Scale);
        if (title is null) return;
        await AuthoringAction(() =>
        {
            Mutate(() => selected = AuthoringOperations.CreateConnectedScene(project, source, choice, title));
            SelectScene(selected!, true); Status("Created a scene and connected the choice. Undo restores both.");
        });
    }

    private async Task CopyScenes()
    {
        if (!hasProject) return;
        Commit();
        var options = project.ListOfCharacterStates.Select(s => new CopyOption(s)).ToList();
        var list = new ListBox { Name = "CopySceneList", ItemsSource = options, Focusable = true, SelectionMode = SelectionMode.Multiple | SelectionMode.Toggle };
        list.ItemTemplate = new FuncDataTemplate<CopyOption>((option, _) => Ui.Text(option?.ToString() ?? "", 13));
        AutomationProperties.SetName(list, "Scenes to copy");
        foreach (var option in options.Where(o => selectedScenes.Contains(o.Scene))) list.SelectedItems!.Add(option);
        var keyboardIndex = Math.Max(0, list.SelectedIndex);
        list.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Space || options.Count == 0) return;
            var focused = list.GetVisualDescendants().OfType<ListBoxItem>().FirstOrDefault(item => item.IsKeyboardFocusWithin)?.DataContext as CopyOption;
            var option = focused ?? options[keyboardIndex];
            if (list.SelectedItems!.Contains(option)) list.SelectedItems.Remove(option); else list.SelectedItems.Add(option);
            e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Window? window = null;
        var copy = Ui.Button("Copy selected scenes", () => window!.Close(list.SelectedItems!.Cast<CopyOption>().Select(o => o.Scene).ToList()), "primary"); copy.Name = "CopyScenesConfirm"; copy.IsDefault = true;
        var summary = Ui.Text("", 12, true); summary.Name = "CopyScenesSummary"; AutomationProperties.SetLiveSetting(summary, AutomationLiveSetting.Polite);
        void Update() { var count = list.SelectedItems?.Count ?? 0; copy.IsEnabled = count > 0; summary.Text = $"{count} {(count == 1 ? "scene" : "scenes")} selected"; }
        list.SelectionChanged += (_, _) => { if (list.SelectedIndex >= 0) keyboardIndex = list.SelectedIndex; Update(); }; Update();
        var all = Ui.Button("Select all", () => list.SelectAll(), "subtle");
        var content = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), Margin = new Thickness(24) };
        content.Children.Add(Ui.Stack(Ui.Text("Copy scenes", 23), Ui.Text("Click or press Space to toggle a scene. Shift selects a range. Copies stay available in this app until it closes, including after opening another project. Pasted scenes start ungrouped.", 13, true)));
        var selection = Ui.Row(all, summary); selection.Margin = new Thickness(0,12,0,10); Grid.SetRow(selection, 1); content.Children.Add(selection);
        Grid.SetRow(list, 2); content.Children.Add(list);
        var footer = Ui.Row(Ui.Button("Cancel", () => window!.Close()), copy); footer.Margin = new Thickness(0,12,0,0); Grid.SetRow(footer, 3); content.Children.Add(footer);
        window = Ui.Dialog(this, "Copy scenes", content, 650, 560, preferences.Scale); window.Opened += (_, _) => list.Focus();
        var scenes = await window.ShowDialog<List<DialogueState>?>(this);
        if (scenes is null) return;
        await AuthoringAction(() => { copiedScenes = AuthoringOperations.CopyScenes(scenes); BuildMenu(); Status($"Copied {copiedScenes.Count} scenes. Use Edit → Paste scenes."); });
    }

    private void CopySelectedScenes()
    {
        if (!hasProject || selectedScenes.Count == 0) return;
        Commit();
        _ = AuthoringAction(() => { copiedScenes = AuthoringOperations.CopyScenes(project.ListOfCharacterStates.Where(selectedScenes.Contains)); BuildMenu(); Status($"Copied {copiedScenes.Count} scenes. Use Edit → Paste scenes."); });
    }

    private async Task PasteScenes()
    {
        if (!hasProject || copiedScenes is null) return;
        await AuthoringAction(() =>
        {
            Mutate(() =>
            {
                var pasted = AuthoringOperations.PasteScenes(project, copiedScenes, (selected?.VisualX ?? 80) + 45, (selected?.VisualY ?? 80) + 65);
                selectedScenes.Clear(); selectedScenes.UnionWith(pasted); selected = pasted[0];
            });
            Graph.Centre(selected!); BuildMenu(); Status($"Pasted {copiedScenes.Count} ungrouped scenes with new IDs. Internal links follow the copies; other destinations are retained.");
        });
    }

    private async Task SaveSceneTemplate(DialogueState scene)
    {
        Commit();
        var name = await Ui.Prompt(this, "Save scene template", "Name this reusable scene. Self-links and endings are kept; other destinations become unset.", scene.Title, preferences.Scale);
        if (name is null) return;
        await AuthoringAction(() => { Mutate(() => AuthoringOperations.SaveSceneTemplate(project, scene, name)); Status("Scene template saved in this project. Open Edit → Templates to use it."); });
    }

    private async Task SaveChoiceTemplate(Choice choice)
    {
        Commit();
        var name = await Ui.Prompt(this, "Save choice template", "Name this reusable response and its logic. Each inserted choice uses its scene's continue destination.", DialogueEngine.PlainText(choice.ButtonText), preferences.Scale);
        if (name is null) return;
        await AuthoringAction(() => { Mutate(() => AuthoringOperations.SaveChoiceTemplate(project, choice, name)); Status("Choice template saved in this project."); });
    }

    private async Task ShowTemplates(bool choicesOnly = false)
    {
        if (!hasProject) return;
        Commit();
        var search = new TextBox { Name = "TemplateSearch", PlaceholderText = "Search template names…", PlaceholderForeground = Ui.Brush("Muted") }; AutomationProperties.SetName(search, "Search templates");
        var list = new ListBox { Name = "TemplateList", Focusable = true }; AutomationProperties.SetName(list, "Reusable templates");
        list.ItemTemplate = new FuncDataTemplate<TemplateOption>((option, _) => Ui.Text(option?.ToString() ?? "", 13));
        var summary = Ui.Text("", 13, true); summary.Name = "TemplateSummary";
        var preview = Ui.Text("", 13); preview.Name = "TemplatePreview";
        Window? window = null;
        var insert = Ui.Button("Insert template", () => window!.Close(list.SelectedItem as TemplateOption), "primary"); insert.Name = "InsertTemplate"; insert.IsDefault = true;
        Button delete = null!;
        delete = Ui.Button("Delete template", () =>
        {
            if (list.SelectedItem is not TemplateOption option) return;
            Mutate(() => { if (option.Scene is not null) project.SceneTemplates.Remove(option.Scene); else project.ChoiceTemplates.Remove(option.Choice!); });
            Filter();
        }, "subtle"); delete.Name = "DeleteTemplate";
        void Preview()
        {
            var option = list.SelectedItem as TemplateOption;
            insert.IsEnabled = option is not null && (option.Scene is not null || selected is not null); delete.IsEnabled = option is not null;
            preview.Text = option?.Scene is { } scene ? scene.Scene.Speaker + "\n" + scene.Scene.Text + $"\n{scene.Scene.Choices.Count} choices · {scene.Scene.DefaultAfterActions.Count} exit actions"
                : option?.Choice is { } choice ? choice.Choice.ButtonText + $"\n{choice.Choice.Requirements.Count} conditions · {choice.Choice.AfterChoiceActions.Count} actions\nUses the selected scene's continue destination." : "";
        }
        void Filter()
        {
            var options = (choicesOnly ? Enumerable.Empty<TemplateOption>() : project.SceneTemplates.Select(t => new TemplateOption(t, null)))
                .Concat(project.ChoiceTemplates.Select(t => new TemplateOption(null, t))).Where(t => t.ToString().Contains(search.Text ?? "", StringComparison.OrdinalIgnoreCase)).ToList();
            var previous = list.SelectedItem as TemplateOption; list.ItemsSource = options; list.SelectedItem = options.FirstOrDefault(o => o == previous) ?? options.FirstOrDefault();
            summary.Text = options.Count == 0 ? "No matching templates. Save one from Content (choice) or Notes (scene)." : $"{options.Count} {(options.Count == 1 ? "template" : "templates")} · saved with this project. Insertions and deletions can be undone after closing.";
            Preview();
        }
        list.SelectionChanged += (_, _) => Preview(); search.TextChanged += (_, _) => Filter(); Filter();
        search.AddHandler(KeyDownEvent, (_, e) => { if (e.Key == Key.Down && list.SelectedItem is not null) { list.Focus(); e.Handled = true; } }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        var content = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto"), Margin = new Thickness(24) };
        content.Children.Add(Ui.Text(choicesOnly ? "Choice templates" : "Scene & choice templates", 23));
        search.Margin = new Thickness(0,12,0,10); Grid.SetRow(search, 1); content.Children.Add(search);
        summary.Margin = new Thickness(0,0,0,10); Grid.SetRow(summary, 2); content.Children.Add(summary);
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,16,*") }; body.Children.Add(list); var scroll = new ScrollViewer { Content = preview }; Grid.SetColumn(scroll, 2); body.Children.Add(scroll); Grid.SetRow(body, 3); content.Children.Add(body);
        var footer = new WrapPanel(); foreach (var button in new[] { Ui.Button("Close", () => window!.Close()), delete, insert }) { button.Margin = new Thickness(0,12,8,0); footer.Children.Add(button); } Grid.SetRow(footer, 4); content.Children.Add(footer);
        window = Ui.Dialog(this, "Templates", content, 760, 550, preferences.Scale); window.Opened += (_, _) => search.Focus();
        if (await window.ShowDialog<TemplateOption?>(this) is not { } chosen) return;
        await AuthoringAction(() =>
        {
            Mutate(() =>
            {
                if (chosen.Scene is not null) selected = AuthoringOperations.InsertSceneTemplate(project, chosen.Scene, (selected?.VisualX ?? 80) + 45, (selected?.VisualY ?? 80) + 65);
                else if (selected is not null) AuthoringOperations.InsertChoiceTemplate(selected, chosen.Choice!);
            });
            if (selected is not null) SelectScene(selected, true); Status("Inserted a template. Undo restores the story in one step.");
        });
    }
}
