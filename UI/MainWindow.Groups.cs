using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using DialogueMachine.Core;

namespace DialogueMachine.UI;

public sealed partial class MainWindow
{
    private async Task ShowGroups()
    {
        if (!hasProject) return;
        Commit();
        var list = new ListBox { Name = "SceneGroupList", Focusable = true };
        AutomationProperties.SetName(list, "Scene groups and chapters");
        list.ItemTemplate = new FuncDataTemplate<SceneGroup>((group, _) => Ui.Stack(Ui.Text(group?.Name ?? "", 14), Ui.Text($"{group?.Scenes.Count ?? 0} scenes", 11, true)));
        var name = new TextBox { Name = "SceneGroupName", PlaceholderText = "Group or chapter name…" };
        AutomationProperties.SetName(name, "Group or chapter name");
        var error = Ui.Text("", 12, true); error.Name = "SceneGroupError"; AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Polite);
        var summary = Ui.Text($"{selectedScenes.Count} selected scenes · active scene: {selected?.Title ?? "none"}", 13, true);
        Window? window = null;
        SceneGroup? Current() => list.SelectedItem as SceneGroup;
        Button? rename = null, delete = null, assign = null, remove = null, select = null, filter = null;
        void Update()
        {
            var group = Current();
            rename!.IsEnabled = delete!.IsEnabled = select!.IsEnabled = filter!.IsEnabled = group is not null;
            assign!.IsEnabled = remove!.IsEnabled = group is not null && selectedScenes.Count > 0;
        }
        void RefreshGroups(string? id)
        {
            list.ItemsSource = project.SceneGroups.ToList();
            list.SelectedItem = project.SceneGroups.FirstOrDefault(g => g.Id == id) ?? project.SceneGroups.FirstOrDefault();
            if (Current() is { } group) name.Text = group.Name;
            Update();
        }
        void Apply(Action action, Func<string?>? nextId = null)
        {
            var id = Current()?.Id;
            try { Mutate(action); error.Text = ""; RefreshGroups(nextId?.Invoke() ?? id); }
            catch (ArgumentException ex) { error.Text = ex.Message; name.Focus(); }
        }
        SceneGroup? created = null;
        var create = Ui.Button("Create group", () => Apply(() => created = SceneGroups.Create(project, name.Text ?? ""), () => created?.Id), "primary"); create.Name = "CreateSceneGroup";
        rename = Ui.Button("Rename group", () => { if (Current() is { } group) Apply(() => SceneGroups.Rename(project, group, name.Text ?? "")); }); rename.Name = "RenameSceneGroup";
        delete = Ui.Button("Delete group", () => { if (Current() is { } group) Apply(() => SceneGroups.Delete(project, group)); }); delete.Name = "DeleteSceneGroup";
        ToolTip.SetTip(delete, "Scenes and connections are preserved. Close the manager and Undo to restore the group.");
        assign = Ui.Button("Assign selected scenes", () => { if (Current() is { } group) Apply(() => SceneGroups.Assign(project, group, selectedScenes)); }); assign.Name = "AssignSceneGroup";
        remove = Ui.Button("Remove selected scenes", () => { if (Current() is { } group) Apply(() => SceneGroups.Remove(project, group, selectedScenes)); }); remove.Name = "RemoveSceneGroup";
        select = Ui.Button("Select group's scenes", () => { if (Current() is { } group) window!.Close((group.Id, false)); }); select.Name = "SelectSceneGroup";
        filter = Ui.Button("Filter outline by group", () => { if (Current() is { } group) window!.Close((group.Id, true)); }); filter.Name = "FilterSceneGroup";
        list.SelectionChanged += (_, _) => { if (Current() is { } group) name.Text = group.Name; Update(); };
        RefreshGroups((C<ComboBox>("GroupFilter").SelectedItem as FilterOption)?.Value);
        var actions = Ui.Stack(Ui.Field("Group / chapter name", name), error, create, rename, assign, remove, select, filter, delete,
            Ui.Text("Scenes can belong to several groups. Deleting a group keeps every scene and connection. Changes apply immediately; close this window to Undo/Redo each operation. Copies and pasted scenes start ungrouped in every project.", 12, true));
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,1.4*"), ColumnSpacing = 16 };
        body.Children.Add(list); var scroll = new ScrollViewer { Content = actions, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }; Grid.SetColumn(scroll, 1); body.Children.Add(scroll);
        var content = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(24), RowSpacing = 16 };
        content.Children.Add(Ui.Stack(Ui.Text("Groups & chapters", 23), summary, Ui.Text("Edits apply immediately. Close to Undo/Redo.", 12, true))); Grid.SetRow(body, 1); content.Children.Add(body);
        var close = Ui.Button("Close", () => window!.Close()); Grid.SetRow(close, 2); content.Children.Add(close);
        window = Ui.Dialog(this, "Groups & chapters", content, 740, 570, preferences.Scale); window.Opened += (_, _) => name.Focus();
        var result = await window.ShowDialog<(string Id, bool Filter)?>(this);
        if (result is not { } command || project.SceneGroups.FirstOrDefault(g => g.Id == command.Id) is not { } chosen) return;
        if (command.Filter)
        {
            C<ComboBox>("GroupFilter").SelectedItem = C<ComboBox>("GroupFilter").Items.OfType<FilterOption>().First(o => o.Value == chosen.Id);
            C<Expander>("OutlineFilters").IsExpanded = true; Status("Outline filtered by ‘" + chosen.Name + "’. Other filters still apply.");
        }
        else
        {
            var members = SceneGroups.Members(project, chosen);
            SelectScenes(members, selected is not null && members.Contains(selected) ? selected : members.FirstOrDefault(), true);
            Status("Selected scenes in ‘" + chosen.Name + "’.");
        }
    }
    private void ToggleMinimap()
    {
        if (preferences.ShowMinimap && C<GraphMinimap>("Minimap").IsKeyboardFocusWithin && Graph.IsVisible) Graph.Focus();
        preferences.ShowMinimap = !preferences.ShowMinimap; WritePreferences(); UpdateGraphOverlays(); BuildMenu();
    }
    private void UpdateGraphOverlays()
    {
        var show = Graph.IsVisible && preferences.ShowMinimap;
        var minimap = C<GraphMinimap>("Minimap");
        minimap.Width = Math.Clamp(Graph.Bounds.Width * .27, 140, 190); minimap.Height = minimap.Width * .63;
        C<Border>("MinimapPanel").IsVisible = show;
        C<Border>("GraphLegend").IsVisible = Graph.IsVisible && Graph.Bounds.Width >= 650;
        C<Border>("GraphHint").MaxWidth = Math.Clamp(Graph.Bounds.Width - (show ? minimap.Width + 70 : 40), 70, 430);
        C<StackPanel>("GraphGestures").IsVisible = !show || Graph.Bounds.Width >= 650;
        minimap.InvalidateVisual();
    }
}
