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


internal static class ShellChecks
{
    public static void UI(Action<bool, string> Check, Action<Window, string> Capture, MainWindow main)
    {
        T Find<T>(string name) where T : Control => main.FindControl<T>(name)!;
        void Click(string name) { Find<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
        Check(!main.HasProject && Find<WelcomeView>("Welcome").IsVisible, "startup shows welcome without automatically loading a sample"); Capture(main,"welcome-dark");
        main.ChangeAppearance("Light"); Capture(main,"welcome-light"); main.ChangeAppearance("High contrast"); Capture(main,"welcome-high-contrast"); main.ChangeAppearance("Dark");
        main.Width = 1000; main.Height = 680; Capture(main,"welcome-compact"); main.Width = 1540; main.Height = 940;
        main.LoadExampleAsync().GetAwaiter().GetResult(); Dispatcher.UIThread.RunJobs();
        Check(main.HasProject && !main.HasUnsavedChanges && main.CurrentFilePath is null, "optional example opens as a clean copy without a file location");
        Check(main.ClientSize.Width > 1000 && main.Project.ListOfCharacterStates.Count == 5, "desktop window lays out the example story"); Capture(main,"workspace-dark");
        Find<TextBox>("SearchBox").Text = "keeper"; Dispatcher.UIThread.RunJobs();
        Check(Find<ListBox>("SceneList").Items.Count == 1, "outline searches both title and dialogue"); Find<TextBox>("SearchBox").Text = "";
        Click("NotesTab"); Check(Find<StackPanel>("EditorPanel").GetVisualDescendants().OfType<TextBox>().Any(), "notes editor is reachable through native buttons");
        Click("ContentTab");
        Check(!Find<TextBlock>("ProjectTitle").Text!.EndsWith(" •"), "opening editor tabs does not create phantom edits");
        var editor = Find<StackPanel>("EditorPanel");
        var addChoice = editor.GetVisualDescendants().OfType<Button>().First(b => b.Content is string s && s.Contains("Add a player choice"));
        addChoice.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(main.Project.ListOfCharacterStates[0].Choices.Count == 4, "choice creation changes the document"); Click("UndoButton"); Check(main.Project.ListOfCharacterStates[0].Choices.Count == 3, "undo restores choice creation"); Click("RedoButton"); Check(main.Project.ListOfCharacterStates[0].Choices.Count == 4, "redo restores choice creation"); Click("UndoButton");
        var titleBox = Find<StackPanel>("EditorPanel").GetVisualDescendants().OfType<TextBox>().First(); titleBox.Focus(); titleBox.Text = "Edited opening";
        main.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z"); main.KeyRelease(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z");
        Check(main.Project.ListOfCharacterStates[0].Title == "A light in the fog", "global undo commits current typing and restores it");
        Check(!Find<TextBlock>("ProjectTitle").Text!.EndsWith(" •"), "undo to saved state clears dirty indicator");
        var graph = Find<GraphCanvas>("Graph"); var scene = main.Project.ListOfCharacterStates[0]; var initialX = scene.VisualX;
        var from = graph.TranslatePoint(graph.WorldToViewport(new Point(scene.VisualX + 30, scene.VisualY + 40)), main)!.Value;
        main.MouseDown(from, MouseButton.Left); main.MouseMove(new Point(from.X + 60, from.Y + 40)); main.MouseUp(new Point(from.X + 60, from.Y + 40), MouseButton.Left);
        Check(main.Project.ListOfCharacterStates[0].VisualX > initialX, "mouse dragging moves graph scenes"); Click("UndoButton"); Check(main.Project.ListOfCharacterStates[0].VisualX == initialX, "node movement is undoable");
        scene = main.Project.ListOfCharacterStates[0]; var target = main.Project.ListOfCharacterStates[4];
        from = graph.TranslatePoint(graph.WorldToViewport(new Point(scene.VisualX + 270, scene.VisualY + 135)), main)!.Value;
        var to = graph.TranslatePoint(graph.WorldToViewport(new Point(target.VisualX + 30, target.VisualY + 40)), main)!.Value;
        main.MouseDown(from, MouseButton.Left); main.MouseMove(to); main.MouseUp(to, MouseButton.Left);
        Check(main.Project.ListOfCharacterStates[0].Choices[0].NextStateId == target.State, "dragging an output port creates a choice connection"); Click("UndoButton"); Check(main.Project.ListOfCharacterStates[0].Choices[0].NextStateId == "keeper", "graph connection changes are undoable");
        Click("LogicTab"); Capture(main,"workspace-logic"); Click("ContentTab"); Click("ScriptButton");
        var scriptList = main.FindControl<ListBox>("ScriptList")!;
        Check(scriptList.ItemsSource is not null && scriptList.Items.Count == main.Project.ListOfCharacterStates.Count, "script view populates virtualized list with project scenes");
        Check(scriptList.ItemTemplate!.Build(null) is null, "script template returns null safely on null data during container recycling");
        var secondScene = main.Project.ListOfCharacterStates[1];
        var secondButton = scriptList.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Content, secondScene.Title));
        secondButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
        Check(main.FindControl<GraphCanvas>("Graph")!.Selected == secondScene, "clicking a scene card button in the script view selects that scene in the editor");
        Capture(main,"workspace-script"); Click("GraphButton");
        Check(scriptList.ItemsSource is null, "switching back to graph view releases script list items");
        main.ChangeAppearance("Light"); Capture(main,"workspace-light"); main.ChangeAppearance("High contrast"); Capture(main,"workspace-high-contrast"); main.ChangeAppearance("Dark");
        main.Width = 1000; main.Height = 680; Capture(main,"workspace-compact");
        Check(Find<StackPanel>("EditorPanel").Bounds.Width > 260 && Find<GraphCanvas>("Graph").Bounds.Width > 250, "minimum window size keeps editor and canvas usable");
        main.Width = 1540; main.Height = 940; main.ChangeAppearance("Dark", 1.15); Capture(main,"workspace-large-text"); main.ChangeAppearance("Dark");
        main.Width = 1540; main.Height = 940; main.FindControl<GraphCanvas>("Graph")!.Fit();
        var iconWindow = new Window { Width = 256, Height = 256, Content = new Border { Background = Avalonia.Media.Brushes.Transparent, Child = new Border { Margin = new Thickness(8), CornerRadius = new CornerRadius(64), Background = UiBrush("Accent"), Child = new TextBlock { Text = "D", FontFamily = new Avalonia.Media.FontFamily("Inter"), FontSize = 170, FontWeight = Avalonia.Media.FontWeight.Bold, Foreground = Avalonia.Media.Brushes.White, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center } } } };
        Avalonia.Media.IBrush UiBrush(string key) => (Avalonia.Media.IBrush)Application.Current!.Resources[key]!;
        iconWindow.Show(); Capture(iconWindow,"app-icon"); iconWindow.Close();
        var controls = main.GetVisualDescendants().Where(c => c.TemplatedParent is null && c is TextBox or ComboBox or NumericUpDown).ToList();
        Check(controls.All(c => !string.IsNullOrEmpty(AutomationProperties.GetName(c))), "editor inputs have screen-reader labels");
    }
}
