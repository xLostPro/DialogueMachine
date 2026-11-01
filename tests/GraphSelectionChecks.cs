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


internal static class GraphSelectionChecks
{
    public static void UI(Action<bool, string> Check, Action<Window, string> Capture, string workflowRoot)
    {
        var otherAuthoringPath = Path.Combine(workflowRoot, "other-authoring.dialogue.json");
        var graphStory = Templates.Harbour();
        for (var i = 0; i < graphStory.ListOfCharacterStates.Count; i++) { graphStory.ListOfCharacterStates[i].VisualX = i % 3 * 370; graphStory.ListOfCharacterStates[i].VisualY = i / 3 * 340; }
        graphStory.Extra = new() { ["FutureRoot"] = JsonSerializer.SerializeToElement(new { Keep = true }) };
        graphStory.ListOfCharacterStates[0].Extra = new() { ["FutureScene"] = JsonSerializer.SerializeToElement("keep") };
        graphStory.ListOfCharacterStates[0].Choices[0].Extra = new() { ["FutureChoice"] = JsonSerializer.SerializeToElement(17) };
        var graphPath = Path.Combine(workflowRoot, "graph.dialogue.json"); ProjectStore.AtomicWrite(graphPath, ProjectStore.Serialize(graphStory));
        var graphWorkspace = new MainWindow(null, true); graphWorkspace.Show(); Pump(graphWorkspace.OpenProjectAsync(graphPath));
        var multiGraph = graphWorkspace.FindControl<GraphCanvas>("Graph")!;
        var graphBaseline = ProjectStore.Serialize(graphWorkspace.Project);
        List<DialogueState> GraphScenes() => graphWorkspace.Project.ListOfCharacterStates;
        Point GraphPoint(int index, double x = 35, double y = 95) => multiGraph.TranslatePoint(multiGraph.WorldToViewport(new Point(GraphScenes()[index].VisualX + x, GraphScenes()[index].VisualY + y)), graphWorkspace)!.Value;
        void GraphClick(int index, RawInputModifiers modifiers = RawInputModifiers.None)
        {
            var point = GraphPoint(index); graphWorkspace.MouseDown(point, MouseButton.Left, modifiers); graphWorkspace.MouseUp(point, MouseButton.Left, modifiers); Dispatcher.UIThread.RunJobs();
        }
        void GraphKey(Key key, RawInputModifiers modifiers = RawInputModifiers.None)
        {
            graphWorkspace.KeyPress(key, modifiers, PhysicalKey.None, ""); graphWorkspace.KeyRelease(key, modifiers, PhysicalKey.None, ""); Dispatcher.UIThread.RunJobs();
        }
        void GraphUndo() => Press(graphWorkspace.FindControl<Button>("UndoButton")!);
        void GraphRedo() => Press(graphWorkspace.FindControl<Button>("RedoButton")!);
        void GraphEdit(string label)
        {
            graphWorkspace.FindControl<Menu>("AppMenu")!.Items.Cast<MenuItem>().Single(m => Equals(m.Header, "_Edit")).Items.OfType<MenuItem>().Single(m => Equals(m.Header, label)).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Dispatcher.UIThread.RunJobs();
        }
        bool GraphSelected(params int[] indices) => multiGraph.SelectedScenes.SequenceEqual(indices.Select(i => GraphScenes()[i]));
        GraphClick(0); GraphClick(1, RawInputModifiers.Control);
        Check(GraphSelected(0, 1) && graphWorkspace.FindControl<ListBox>("SceneList")!.SelectedItems!.Count == 2, "Ctrl-click selects a scene set and synchronizes the outline");
        Check(multiGraph.Selected == GraphScenes()[1] && graphWorkspace.FindControl<TextBlock>("EditorSubtitle")!.Text!.Contains("Editing 1 of 2"), "a selected set retains one explicit active editor scene");
        GraphClick(1, RawInputModifiers.Shift); Check(GraphSelected(0), "Shift-click toggles an included graph scene off");
        GraphClick(1, RawInputModifiers.Meta); Check(GraphSelected(0, 1), "Cmd-click supports additive graph selection");
        Check(ProjectStore.Serialize(graphWorkspace.Project) == graphBaseline && !graphWorkspace.HasUnsavedChanges && !graphWorkspace.FindControl<Button>("UndoButton")!.IsEnabled, "selection alone changes neither JSON, dirty state nor history");
        var groupFrom = GraphPoint(0); var dx = GraphScenes()[1].VisualX - GraphScenes()[0].VisualX; var dy = GraphScenes()[1].VisualY - GraphScenes()[0].VisualY;
        var groupZoom = multiGraph.Zoom;
        graphWorkspace.MouseDown(groupFrom, MouseButton.Left); graphWorkspace.MouseMove(groupFrom + new Vector(30, 15)); graphWorkspace.MouseMove(groupFrom + new Vector(75, 45)); graphWorkspace.MouseUp(groupFrom + new Vector(75, 45), MouseButton.Left); Dispatcher.UIThread.RunJobs();
        Check(Math.Abs(GraphScenes()[0].VisualX - graphStory.ListOfCharacterStates[0].VisualX - 75 / groupZoom) < .0001 && Math.Abs(GraphScenes()[1].VisualX - GraphScenes()[0].VisualX - dx) < .0001 && Math.Abs(GraphScenes()[1].VisualY - GraphScenes()[0].VisualY - dy) < .0001, "a multi-event group drag applies the zoom-correct delta and preserves relative positions");
        Check(GraphScenes()[2].VisualX == graphStory.ListOfCharacterStates[2].VisualX && GraphSelected(0, 1), "group dragging leaves unselected scenes and the selected set intact");
        var groupMoved = ProjectStore.Serialize(graphWorkspace.Project);
        var expectedMoved = ProjectStore.Clone(graphStory); expectedMoved.ListOfCharacterStates[0].VisualX = GraphScenes()[0].VisualX; expectedMoved.ListOfCharacterStates[0].VisualY = GraphScenes()[0].VisualY; expectedMoved.ListOfCharacterStates[1].VisualX = GraphScenes()[1].VisualX; expectedMoved.ListOfCharacterStates[1].VisualY = GraphScenes()[1].VisualY;
        Check(groupMoved == ProjectStore.Serialize(expectedMoved), "group movement changes only positions, preserving root/nested unknown fields, links and logic");
        GraphUndo(); Check(ProjectStore.Serialize(graphWorkspace.Project) == graphBaseline && GraphSelected(0, 1) && !graphWorkspace.HasUnsavedChanges && !graphWorkspace.FindControl<Button>("UndoButton")!.IsEnabled, "one Undo restores the entire group drag and selection without an extra history step");
        GraphRedo(); Check(ProjectStore.Serialize(graphWorkspace.Project) == groupMoved && GraphSelected(0, 1), "one Redo restores all moved scenes and their selection"); GraphUndo();
        multiGraph.Focus(); graphWorkspace.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, ""); graphWorkspace.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, ""); graphWorkspace.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, ""); graphWorkspace.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, ""); Dispatcher.UIThread.RunJobs();
        Check(GraphScenes()[0].VisualX == graphStory.ListOfCharacterStates[0].VisualX + 30 && GraphScenes()[1].VisualX == graphStory.ListOfCharacterStates[1].VisualX + 30, "held arrow-key repeats move the selected set together");
        GraphUndo(); Check(ProjectStore.Serialize(graphWorkspace.Project) == graphBaseline && !graphWorkspace.FindControl<Button>("UndoButton")!.IsEnabled, "a held group nudge is one Undo step");
        multiGraph.Focus(); GraphKey(Key.Down, RawInputModifiers.Shift); Check(GraphScenes()[0].VisualY == graphStory.ListOfCharacterStates[0].VisualY + 40 && GraphScenes()[1].VisualY == graphStory.ListOfCharacterStates[1].VisualY + 40, "Shift-arrow moves the group by the larger step"); GraphUndo();
        multiGraph.Focus(); graphWorkspace.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, ""); GraphKey(Key.Escape); graphWorkspace.KeyRelease(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, "");
        Check(ProjectStore.Serialize(graphWorkspace.Project) == graphBaseline && GraphSelected(0, 1) && !graphWorkspace.FindControl<Button>("UndoButton")!.IsEnabled, "Escape rolls back an in-progress keyboard move without clearing selection or creating history");
        multiGraph.Fit(); groupFrom = GraphPoint(0); graphWorkspace.MouseDown(groupFrom, MouseButton.Left); graphWorkspace.MouseMove(groupFrom + new Vector(25, 15)); GraphKey(Key.Escape); graphWorkspace.MouseUp(groupFrom + new Vector(25, 15), MouseButton.Left);
        Check(ProjectStore.Serialize(graphWorkspace.Project) == graphBaseline && GraphSelected(0, 1), "Escape cancels a group pointer drag and restores every position");
        IPointer? groupPointer = null;
        multiGraph.AddHandler(InputElement.PointerPressedEvent, (_, e) => groupPointer = e.Pointer, RoutingStrategies.Bubble, handledEventsToo: true);
        groupFrom = GraphPoint(0); graphWorkspace.MouseDown(groupFrom, MouseButton.Left); graphWorkspace.MouseMove(groupFrom + new Vector(30, 15)); groupPointer!.Capture(null);
        Check(graphWorkspace.HasUnsavedChanges && graphWorkspace.FindControl<Button>("UndoButton")!.IsEnabled, "actual capture loss records the moved group before another command");
        graphWorkspace.MouseUp(groupFrom + new Vector(30, 15), MouseButton.Left); GraphUndo();
        Check(ProjectStore.Serialize(graphWorkspace.Project) == graphBaseline && !graphWorkspace.FindControl<Button>("UndoButton")!.IsEnabled, "losing pointer capture commits a completed group movement exactly once");
        multiGraph.Focus(); graphWorkspace.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, ""); graphWorkspace.FindControl<TextBox>("SearchBox")!.Focus();
        Check(graphWorkspace.HasUnsavedChanges && graphWorkspace.FindControl<Button>("UndoButton")!.IsEnabled, "leaving graph focus finishes an active keyboard group move"); GraphUndo();
        Check(ProjectStore.Serialize(graphWorkspace.Project) == graphBaseline && GraphSelected(0, 1), "one Undo restores a group move finished by focus loss");
        multiGraph.Focus(); GraphKey(Key.A, RawInputModifiers.Control); Check(GraphSelected(0, 1, 2, 3, 4), "Ctrl+A selects every graph scene from the keyboard"); GraphKey(Key.Escape); Check(GraphSelected() && multiGraph.Selected is null, "Escape clears the graph and editor selection");
        GraphKey(Key.Tab, RawInputModifiers.Control); GraphKey(Key.Enter); var keyboardFirst = multiGraph.Selected!;
        GraphKey(Key.Tab, RawInputModifiers.Control); GraphKey(Key.Space);
        Check(multiGraph.SelectedScenes.Count == 2 && multiGraph.SelectedScenes.Contains(keyboardFirst), "Ctrl+Tab and Space build a graph selection without the mouse");
        GraphKey(Key.Space); Check(multiGraph.SelectedScenes.Count == 1 && multiGraph.Selected == keyboardFirst, "Space toggles the keyboard-focused scene off while retaining the active editor");
        GraphKey(Key.Tab, RawInputModifiers.Control | RawInputModifiers.Shift); GraphKey(Key.Enter); Check(multiGraph.Selected == keyboardFirst, "Ctrl+Shift+Tab navigates backwards and Enter selects only that scene");
        GraphKey(Key.Escape); multiGraph.Fit();
        var marqueeA = multiGraph.TranslatePoint(multiGraph.WorldToViewport(new Point(-20, -20)), graphWorkspace)!.Value;
        var marqueeB = multiGraph.TranslatePoint(multiGraph.WorldToViewport(new Point(660, 265)), graphWorkspace)!.Value;
        graphWorkspace.MouseDown(marqueeA, MouseButton.Left, RawInputModifiers.Shift); graphWorkspace.MouseMove(marqueeB, RawInputModifiers.Shift); Capture(graphWorkspace, "graph-marquee"); graphWorkspace.MouseUp(marqueeB, MouseButton.Left, RawInputModifiers.Shift); Dispatcher.UIThread.RunJobs();
        Check(GraphSelected(0, 1), "Shift-drag selects intersecting scenes using world coordinates");
        var panFrom = multiGraph.TranslatePoint(new Point(multiGraph.Bounds.Width - 20, multiGraph.Bounds.Height / 2), graphWorkspace)!.Value;
        graphWorkspace.MouseDown(panFrom, MouseButton.Left); graphWorkspace.MouseMove(panFrom + new Vector(-15, 10)); graphWorkspace.MouseUp(panFrom + new Vector(-15, 10), MouseButton.Left);
        Check(ProjectStore.Serialize(graphWorkspace.Project) == graphBaseline && GraphSelected(0, 1) && !graphWorkspace.HasUnsavedChanges, "background panning preserves selected scenes and creates no document edit");
        multiGraph.Fit(); GraphClick(2); Check(GraphSelected(2), "an unmodified click on another scene replaces the graph set");
        var graphOutline = graphWorkspace.FindControl<ListBox>("SceneList")!; graphOutline.SelectedItems!.Clear(); graphOutline.SelectedItems!.Add(graphOutline.Items[0]); graphOutline.SelectedItems!.Add(graphOutline.Items[1]); Dispatcher.UIThread.RunJobs();
        Check(GraphSelected(0, 1), "multiple outline selections synchronize the graph set");
        var outlineItem = graphOutline.ContainerFromIndex(0)!; var outlineClick = outlineItem.TranslatePoint(new Point(35, 20), graphWorkspace)!.Value;
        graphWorkspace.MouseDown(outlineClick, MouseButton.Left); graphWorkspace.MouseUp(outlineClick, MouseButton.Left); Dispatcher.UIThread.RunJobs();
        Check(GraphSelected(0), "a plain outline click replaces a multiple selection");
        GraphKey(Key.Down, RawInputModifiers.Shift); GraphKey(Key.Down, RawInputModifiers.Shift);
        Check(GraphSelected(0, 1, 2) && multiGraph.Selected == GraphScenes()[2], "native outline Shift-arrow selection retains its anchor across successive keys");
        graphOutline.Focus(); GraphKey(Key.A, RawInputModifiers.Control); Check(multiGraph.SelectedScenes.Count == 5, "outline Ctrl+A selects every visible scene with native keyboard input");
        multiGraph.Focus(); GraphKey(Key.Escape); multiGraph.Fit(); GraphClick(0); GraphClick(1, RawInputModifiers.Control);
        graphWorkspace.FindControl<TextBox>("SearchBox")!.Text = "fog"; Dispatcher.UIThread.RunJobs();
        Check(GraphSelected(0, 1), "outline filtering preserves the graph selection, including hidden scenes");
        Press(graphWorkspace.FindControl<Button>("ClearFiltersButton")!); multiGraph.Focus(); GraphKey(Key.C, RawInputModifiers.Control);
        Check(ProjectStore.Serialize(graphWorkspace.Project) == graphBaseline && !graphWorkspace.HasUnsavedChanges, "Ctrl+C copies the current graph set without changing history");
        GraphEdit("Copy scenes…"); var graphCopyDialog = Modal(graphWorkspace);
        Check(NamedControl<ListBox>(graphCopyDialog, "CopySceneList").SelectedItems!.Count == 2, "the existing copy dialog preselects the complete graph selection"); PressAnswer(graphCopyDialog, "Cancel");
        multiGraph.Focus(); GraphKey(Key.V, RawInputModifiers.Control); var graphPasted = GraphScenes().TakeLast(2).ToList();
        Check(GraphScenes().Count == 7 && multiGraph.SelectedScenes.SequenceEqual(graphPasted) && graphPasted[0].Choices[0].NextStateId == graphPasted[1].State, "Ctrl+V pastes and selects the complete graph bundle with internal links remapped");
        Check(graphPasted[1].VisualX - graphPasted[0].VisualX == dx && graphPasted[1].VisualY - graphPasted[0].VisualY == dy && graphPasted[0].Extra!.ContainsKey("FutureScene") && graphPasted[0].Choices[0].Extra!.ContainsKey("FutureChoice"), "graph-set paste preserves relative positions and nested unknown data");
        GraphUndo(); Check(ProjectStore.Serialize(graphWorkspace.Project) == graphBaseline && !graphWorkspace.HasUnsavedChanges, "graph-set paste remains one undo step");
        multiGraph.Fit(); GraphClick(0); GraphEdit("Copy selected scenes"); GraphEdit("Paste scenes"); Check(GraphScenes().Count == 6 && multiGraph.SelectedScenes.Count == 1, "Edit menu copies the selected set directly"); GraphUndo();
        multiGraph.Fit(); GraphClick(0);
        var originalPortTarget = GraphScenes()[0].Choices[0].NextStateId;
        GraphClick(1, RawInputModifiers.Control);
        var groupPort = GraphPoint(0, GraphGeometry.NodeWidth, 135); var groupDestination = GraphPoint(4);
        graphWorkspace.MouseDown(groupPort, MouseButton.Left); graphWorkspace.MouseMove(groupDestination); graphWorkspace.MouseUp(groupDestination, MouseButton.Left); Dispatcher.UIThread.RunJobs();
        Check(GraphScenes()[0].Choices[0].NextStateId == GraphScenes()[4].State && GraphSelected(0, 1) && GraphScenes()[0].VisualX == graphStory.ListOfCharacterStates[0].VisualX && GraphScenes()[1].VisualX == graphStory.ListOfCharacterStates[1].VisualX, "dragging one selected output edits only that connection without moving the group");
        GraphUndo(); Check(GraphScenes()[0].Choices[0].NextStateId == originalPortTarget && ProjectStore.Serialize(graphWorkspace.Project) == graphBaseline && GraphSelected(0, 1), "one Undo restores a connection made from a selected set");
        multiGraph.Focus(); GraphKey(Key.Escape); multiGraph.Fit(); GraphClick(0);
        Check(multiGraph.ConnectionHighlight(GraphScenes()[0], GraphScenes()[1]) == "GraphSelection" && multiGraph.ConnectionHighlight(GraphScenes()[1], GraphScenes()[0]) == "GraphIncoming" && multiGraph.ConnectionHighlight(GraphScenes()[1], GraphScenes()[2]) is null, "connection emphasis distinguishes outgoing, incoming and unrelated links");
        GraphClick(1, RawInputModifiers.Control); Check(multiGraph.ConnectionHighlight(GraphScenes()[1], GraphScenes()[0]) == "GraphSelection", "connections internal to a selected set use the outgoing highlight");
        using (var graphWeb = JsonDocument.Parse(ProjectStore.WebExport(graphWorkspace.Project)))
            Check(graphWeb.RootElement.EnumerateObject().Count() == 1 && graphWeb.RootElement.GetProperty("ListOfCharacterStates")[0].GetProperty("Choices")[1].GetProperty("Visibility").ValueKind == JsonValueKind.Number && graphWeb.RootElement.GetProperty("ListOfCharacterStates")[0].GetProperty("FutureScene").GetString() == "keep", "graph selection introduces no JSON fields and preserves numeric website visibility and extensions");
        foreach (var theme in new[] { "Dark", "Light", "High contrast" })
            foreach (var scale in new[] { 1.0, 1.15, 1.3 })
            {
                graphWorkspace.ChangeAppearance(theme, scale); graphWorkspace.Width = 1540 * scale; graphWorkspace.Height = 940 * scale; Dispatcher.UIThread.RunJobs(); multiGraph.Fit();
                var renderName = "graph-selection-" + theme.ToLowerInvariant().Replace(' ', '-') + "-" + (int)Math.Round(scale * 100);
                Capture(graphWorkspace, renderName);
                Check(GraphSelected(0, 1) && graphWorkspace.FindControl<TextBlock>("GraphSelectionSummary")!.Text == "2 scenes selected" && AutomationProperties.GetLiveSetting(graphWorkspace.FindControl<TextBlock>("GraphSelectionSummary")!) == AutomationLiveSetting.Polite, "appearance retains selection and its accessible count at " + theme + " " + scale);
                graphWorkspace.Width = 1000 * scale; graphWorkspace.Height = 680 * scale; Dispatcher.UIThread.RunJobs(); multiGraph.Fit(); Capture(graphWorkspace, renderName + "-compact");
                var hint = graphWorkspace.FindControl<Border>("GraphHint")!;
                Check(hint.Bounds.Right <= multiGraph.Bounds.Width && multiGraph.Bounds.Width > 250 && graphWorkspace.FindControl<ListBox>("SceneList")!.Bounds.Height > 30, "compact graph keeps selection guidance and outline usable at " + theme + " " + scale);
            }
        graphWorkspace.ChangeAppearance("Dark"); graphWorkspace.Width = 1540; graphWorkspace.Height = 940; Dispatcher.UIThread.RunJobs(); multiGraph.Fit();
        var graphText = graphWorkspace.FindControl<StackPanel>("EditorPanel")!.GetVisualDescendants().OfType<TextBox>().First(); graphText.Text = "Typing before a group move";
        multiGraph.Focus(); GraphKey(Key.Right); GraphUndo(); Check(GraphScenes()[1].Title == "Typing before a group move" && GraphSelected(0, 1), "undoing a group move preserves typing committed before the gesture");
        GraphUndo(); Check(ProjectStore.Serialize(graphWorkspace.Project) == graphBaseline, "typing before a group gesture retains its own Undo step");
        multiGraph.Focus(); GraphKey(Key.Escape); GraphKey(Key.Right); Check(ProjectStore.Serialize(graphWorkspace.Project) == graphBaseline && !graphWorkspace.FindControl<Button>("UndoButton")!.IsEnabled, "empty selection ignores group movement without adding history");
        GraphKey(Key.A, RawInputModifiers.Meta); Check(GraphSelected(0, 1, 2, 3, 4), "Cmd+A supports select-all with the same graph behavior");
        graphWorkspace.FindControl<TextBox>("SearchBox")!.Focus(); graphWorkspace.FindControl<TextBox>("SearchBox")!.Text = "keeper"; GraphKey(Key.A, RawInputModifiers.Control);
        Check(GraphSelected(0, 1, 2, 3, 4) && graphWorkspace.FindControl<TextBox>("SearchBox")!.SelectionEnd == 6, "text-input select-all retains its native behavior without changing graph selection");
        graphWorkspace.FindControl<TextBox>("SearchBox")!.Text = "";
        Pump(graphWorkspace.OpenProjectAsync(otherAuthoringPath)); Check(multiGraph.SelectedScenes.Count == 1 && multiGraph.Selected == graphWorkspace.Project.ListOfCharacterStates[0], "opening another project replaces the previous graph set with its entry scene");
        var duplicateGraph = ProjectStore.Clone(graphStory); duplicateGraph.ListOfCharacterStates[4].State = duplicateGraph.ListOfCharacterStates[1].State;
        var duplicateGraphPath = Path.Combine(workflowRoot, "duplicate-graph.dialogue.json"); ProjectStore.AtomicWrite(duplicateGraphPath, ProjectStore.Serialize(duplicateGraph)); Pump(graphWorkspace.OpenProjectAsync(duplicateGraphPath));
        multiGraph.Fit(); GraphClick(1); GraphClick(4, RawInputModifiers.Control); GraphKey(Key.Right);
        Check(GraphSelected(1, 4) && GraphScenes()[1].VisualX == duplicateGraph.ListOfCharacterStates[1].VisualX + 10 && GraphScenes()[4].VisualX == duplicateGraph.ListOfCharacterStates[4].VisualX + 10, "repairable duplicate-ID scenes can be selected and moved as distinct graph nodes");
        GraphUndo(); Check(ProjectStore.Serialize(graphWorkspace.Project) == ProjectStore.Serialize(duplicateGraph) && GraphSelected(1, 4) && multiGraph.Selected == GraphScenes()[4], "Undo rebinds a duplicate-ID selection by occurrence and preserves its active scene");
        graphWorkspace.Close();
    }
}
