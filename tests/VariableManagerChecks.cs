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


internal static class VariableManagerChecks
{
    public static void UI(Action<bool, string> Check, Action<Window, string> Capture)
    {
        var variableWorkspace = new MainWindow(null, true); variableWorkspace.Show(); Pump(variableWorkspace.LoadExampleAsync());
        variableWorkspace.Project.InitialVariables["aff:unused"] = 0;
        variableWorkspace.Project.ListOfCharacterStates[0].Text = "Hello {player_name}. [if aff:trust >= 1]Friend[else]Stranger[/if] {aff:trust}";
        var beforeManager = ProjectStore.Serialize(variableWorkspace.Project);
        Button NamedButton(Window owner, string name) => owner.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == name);
        T NamedControl<T>(Window owner, string name) where T : Control => owner.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
        void OpenManager() => Press(variableWorkspace.FindControl<Button>("VariablesButton")!);
        void RenameInManager(Window manager, string oldId, string newId)
        {
            Press(NamedButton(manager, "Rename " + oldId)); var prompt = manager.OwnedWindows.Single(w => w.IsVisible);
            prompt.GetVisualDescendants().OfType<TextBox>().Single().Text = newId;
            Press(prompt.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Continue")));
        }
        void AddInManager(Window manager, string label, string id)
        {
            Press(NamedButton(manager, label)); var prompt = manager.OwnedWindows.Single(w => w.IsVisible);
            prompt.GetVisualDescendants().OfType<TextBox>().Single().Text = id;
            Press(prompt.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Continue")));
        }
        void PressAnswer(Window owner, string label) => Press(owner.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, label)));
        void UndoWorkspace() { variableWorkspace.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z"); variableWorkspace.KeyRelease(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z"); Dispatcher.UIThread.RunJobs(); }
        OpenManager(); var manager = Modal(variableWorkspace);
        Check(!NamedButton(manager, "Delete aff:trust").IsEnabled && NamedButton(manager, "Delete aff:unused").IsEnabled, "manager allows unused deletion and protects referenced variables");
        RenameInManager(manager, "aff:trust", "aff:rapport");
        manager.GetVisualDescendants().OfType<NumericUpDown>().Single(n => AutomationProperties.GetName(n) == "aff:rapport").Value = 7;
        Check(ProjectStore.Serialize(variableWorkspace.Project) == beforeManager, "renaming and editing manager values does not mutate the live project before Apply");
        PressAnswer(manager, "Cancel");
        Check(ProjectStore.Serialize(variableWorkspace.Project) == beforeManager, "Cancel discards variable renames and value changes together");
        OpenManager(); manager = Modal(variableWorkspace); RenameInManager(manager, "aff:trust", "aff:rapport"); RenameInManager(manager, "player_name", "hero_name");
        AddInManager(manager, "＋ Add numeric variable", "inv:tokens");
        manager.GetVisualDescendants().OfType<NumericUpDown>().Single(n => AutomationProperties.GetName(n) == "inv:tokens").Value = 2;
        AddInManager(manager, "＋ Add text substitution", "town_name");
        manager.GetVisualDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "{town_name}").Text = "Harbour";
        Press(NamedButton(manager, "Delete aff:unused"));
        Press(NamedButton(manager, "References for Mara")); var speakerDialog = manager.OwnedWindows.Single(w => w.IsVisible);
        Check(speakerDialog.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("arrival") == true), "speaker references dialog identifies matching scenes");
        PressAnswer(speakerDialog, "Close");
        RenameInManager(manager, "Mara", "Captain Mara");
        Press(NamedButton(manager, "References for aff:rapport")); var referenceDialog = manager.OwnedWindows.Single(w => w.IsVisible);
        Check(referenceDialog.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("arrival") == true), "reference details identify actual scene locations");
        PressAnswer(referenceDialog, "Close"); NamedControl<TextBox>(manager, "VariableSearch").Text = "rapport"; Dispatcher.UIThread.RunJobs();
        Check(manager.GetVisualDescendants().OfType<NumericUpDown>().Count() == 1 && !manager.GetVisualDescendants().OfType<TextBox>().Any(t => AutomationProperties.GetName(t) == "{hero_name}"), "variable search filters numeric and text cards");
        Capture(manager, "variables-filtered"); NamedControl<TextBox>(manager, "VariableSearch").Text = ""; Dispatcher.UIThread.RunJobs(); Capture(manager, "variables-dark");
        Check(manager.GetVisualDescendants().Where(c => c.TemplatedParent is null && c is TextBox or NumericUpDown).All(c => !string.IsNullOrEmpty(AutomationProperties.GetName(c))), "manager search and value fields have accessibility labels");
        PressAnswer(manager, "Apply changes");
        Check(variableWorkspace.Project.InitialVariables.ContainsKey("aff:rapport") && !variableWorkspace.Project.InitialVariables.ContainsKey("aff:unused") && VariableStore.References(variableWorkspace.Project, "aff:trust").Count == 0 && variableWorkspace.Project.ListOfCharacterStates[0].Text.Contains("{hero_name}"), "Apply updates numeric/text references and deletions together");
        Check(variableWorkspace.Project.InitialVariables["inv:tokens"] == 2 && variableWorkspace.Project.TextVariables["town_name"] == "Harbour", "new numeric and text variables retain their edited values on Apply");
        Check(variableWorkspace.Project.ListOfCharacterStates.All(s => s.Speaker == "Captain Mara"), "Apply renames scene speakers project-wide");
        UndoWorkspace(); Check(ProjectStore.Serialize(variableWorkspace.Project) == beforeManager, "one Undo restores every applied manager operation and its references");
        foreach (var theme in new[] { "Light", "High contrast" }) { variableWorkspace.ChangeAppearance(theme); OpenManager(); manager = Modal(variableWorkspace); Capture(manager, "variables-" + theme.ToLowerInvariant().Replace(' ', '-')); PressAnswer(manager, "Cancel"); }
        variableWorkspace.ChangeAppearance("Dark", 1.3); OpenManager(); manager = Modal(variableWorkspace);
        Check(manager.Content is LayoutTransformControl && Math.Abs(manager.Width - 845) < 1, "variable manager honors the 130 percent interface size"); Capture(manager, "variables-large-text");
        RenameInManager(manager, "aff:trust", "aff:rapport"); manager.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, ""); Dispatcher.UIThread.RunJobs();
        Check(!manager.IsVisible && ProjectStore.Serialize(variableWorkspace.Project) == beforeManager, "Escape cancels the complete manager draft");
        variableWorkspace.ChangeAppearance("Dark");
        var insertNarrative = variableWorkspace.FindControl<StackPanel>("EditorPanel")!.GetVisualDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "Dialogue text"); insertNarrative.SelectionStart = 0; insertNarrative.SelectionEnd = 0;
        Press(NamedButton(variableWorkspace, "Variable…")); var insertion = Modal(variableWorkspace); NamedControl<TextBox>(insertion, "InsertVariableSearch").Text = "coin"; Dispatcher.UIThread.RunJobs();
        Check(NamedControl<ListBox>(insertion, "InsertVariableList").Items.Count == 1, "inline variable picker searches available IDs"); Capture(insertion, "variable-insertion"); PressAnswer(insertion, "Insert");
        Check(variableWorkspace.Project.ListOfCharacterStates[0].Text.StartsWith("{inv:coin}Hello"), "inline picker inserts the selected substitution at the cursor"); UndoWorkspace();
        var speakerInput = variableWorkspace.FindControl<StackPanel>("EditorPanel")!.GetVisualDescendants().OfType<AutoCompleteBox>().Single(b => AutomationProperties.GetName(b) == "Speaker");
        Check(speakerInput.ItemsSource!.Cast<string>().Contains("Mara"), "speaker autocomplete includes project speakers");
        speakerInput.Text = "Navigator"; Dispatcher.UIThread.RunJobs();
        Check(variableWorkspace.Project.ListOfCharacterStates[0].Speaker == "Navigator", "speaker input updates selected scene speaker");
        UndoWorkspace();
        Check(variableWorkspace.Project.ListOfCharacterStates[0].Speaker == "Mara", "speaker edits are undoable");
        OpenManager(); manager = Modal(variableWorkspace);
        AddInManager(manager, "＋ Add character", "Ghost");
        var maraColour = manager.GetVisualDescendants().OfType<TextBox>().Single(b => AutomationProperties.GetName(b) == "Colour for Mara"); maraColour.Text = "not a colour"; maraColour.Text = "#E07A5F";
        PressAnswer(manager, "Apply changes");
        Check(CharacterRoster.ColorFor(variableWorkspace.Project, "Mara") == "#E07A5F" && CharacterRoster.Find(variableWorkspace.Project, "Ghost") is not null, "manager adds characters and sets colours on Apply");
        Capture(variableWorkspace, "character-colour-graph");
        speakerInput = variableWorkspace.FindControl<StackPanel>("EditorPanel")!.GetVisualDescendants().OfType<AutoCompleteBox>().Single(b => AutomationProperties.GetName(b) == "Speaker");
        Check(speakerInput.ItemsSource!.Cast<string>().Contains("Ghost"), "speaker autocomplete suggests roster characters not yet used");
        UndoWorkspace(); Check(variableWorkspace.Project.Characters.Count == 0, "one Undo removes the roster changes");
        Press(variableWorkspace.FindControl<Button>("LogicTab")!); var beforePickerEdit = ProjectStore.Serialize(variableWorkspace.Project);
        var variablePicker = variableWorkspace.FindControl<StackPanel>("EditorPanel")!.GetVisualDescendants().OfType<AutoCompleteBox>().First();
        Check(variablePicker.ItemsSource!.Cast<string>().Contains("coin") && !variablePicker.ItemsSource!.Cast<string>().Contains("trust") && AutomationProperties.GetName(variablePicker) == "Variable ID", "logic variable picker offers IDs for the selected numeric type and has a label");
        variablePicker.Text = "tokens"; Dispatcher.UIThread.RunJobs();
        Check(DialogueEngine.DiscoverVariables(variableWorkspace.Project).Contains("inv:tokens"), "typing a new ID in the logic picker updates the actual condition");
        UndoWorkspace(); Check(ProjectStore.Serialize(variableWorkspace.Project) == beforePickerEdit, "logic picker edits are undoable"); variableWorkspace.Close();
    }
}
