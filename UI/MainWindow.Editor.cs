using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using DialogueMachine.Core;

namespace DialogueMachine.UI;

public sealed partial class MainWindow
{
    private SceneEditorView sceneEditor = null!;
    private void WireSceneEditor() => sceneEditor = new SceneEditorView(C<StackPanel>("EditorPanel"), C<ScrollViewer>("EditorScroll"), new SceneEditorActions(
        Edited, Commit, () => { Commit(); C<TextBlock>("EditorTitle").Text = selected?.Title; }, Mutate, SwitchTab, Destination, ChoiceDestinationLabel, AddSceneConnections, PickVariable,
        CreateSceneFromChoice, SaveChoiceTemplate, SaveSceneTemplate, ShowTemplates, RenameScene, SetStart, DuplicateScene, DeleteScene, Play));
    private void RefreshEditor()
    {
        C<TextBlock>("EditorTitle").Text = selected?.Title ?? "Select a scene";
        C<TextBlock>("EditorSubtitle").Text = selected is null ? "Your scene's content and connections live here." : selected.State + (selected.State == project.Entry ? "  ·  Story start" : "") + (selectedScenes.Count > 1 ? $"  ·  Editing 1 of {selectedScenes.Count} selected" : "");
        foreach (var tab in new[] { "Content", "Logic", "Notes" }) C<Button>(tab + "Tab").Background = Ui.Brush(tab == sceneEditor.Tab ? "Selection" : "Panel");
        sceneEditor.Refresh(selected, project);
    }
    internal static string ScriptChoiceLine(DialogueState s, Choice c)
    {
        var line = "↳  " + DialogueEngine.PlainText(c.ButtonText) + "  →  " + (c.NextStateId ?? s.DefaultNextId ?? "(unset)");
        if (c.Requirements.Count == 0) return line;
        var when = string.Join(c.RequirementMatch == RequirementMatch.Any ? " or " : " and ", c.Requirements.Select(r => $"{r.TargetId} {r.Operator} {r.Value}"));
        return line + $"   [if {when}; {c.Visibility.ToString().ToLowerInvariant()} otherwise]";
    }
    private bool scriptDirty = true;
    internal sealed record ScriptRow(int Index, string State, string Title, string Speaker, bool IsEntry, string Dialogue, IReadOnlyList<string> Choices, string ContinueTarget);

    private void RefreshScript()
    {
        var scroll = C<Control>("ScriptScroll");
        var list = C<ListBox>("ScriptList");
        if (!scroll.IsVisible)
        {
            if (scriptDirty) list.ItemsSource = null;
            return;
        }
        if (!scriptDirty) return;
        scriptDirty = false;
        C<TextBlock>("ScriptTitle").Text = project.ProjectName;
        list.ItemTemplate ??= new FuncDataTemplate<ScriptRow>((row, _) =>
        {
            if (row is null) return null;
            var stack = Ui.Stack(
                Ui.Button((row.IsEntry ? "◆  " : "") + row.Title, () => SelectScene(project.ListOfCharacterStates[row.Index], true), "subtle"),
                Ui.Text(row.Speaker + "  ·  " + row.State, 12, true),
                Ui.Text(row.Dialogue, 15)
            );
            foreach (var choice in row.Choices)
                stack.Children.Add(Ui.Text(choice, 12, true));
            stack.Children.Add(Ui.Text("Continue → " + row.ContinueTarget, 11, true));
            var card = Ui.Card(stack, 20);
            card.Margin = new Thickness(0, 0, 0, 16);
            return card;
        });
        list.ItemsSource = project.ListOfCharacterStates.Select((s, i) => new ScriptRow(
            i,
            s.State,
            s.Title,
            s.Speaker,
            s.State == project.Entry,
            DialogueEngine.PlainText(s.Text),
            s.Choices.Select(c => ScriptChoiceLine(s, c)).ToList(),
            s.DefaultNextId ?? "(unset)"
        )).ToList();
    }
}
