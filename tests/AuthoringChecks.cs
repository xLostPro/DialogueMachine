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


internal static class AuthoringChecks
{
    public static void Core(Action<bool, string> Check, Action<Action, string> Throws)
    {
        var authoringStory = Templates.Harbour();
        var authoringSource = authoringStory.ListOfCharacterStates[0]; var authoringTarget = authoringStory.ListOfCharacterStates[1];
        authoringSource.DefaultNextId = authoringSource.State; authoringTarget.DefaultNextId = authoringSource.State;
        authoringSource.Choices.Add(new Choice { ButtonText = "Unspecified", NextStateId = "" });
        authoringSource.Choices.Add(new Choice { ButtonText = "Missing", NextStateId = "missing_future" });
        authoringSource.Extra = new() { ["FutureScene"] = JsonSerializer.SerializeToElement(new { keep = 42 }) };
        authoringSource.Choices[1].Extra = new() { ["FutureChoice"] = JsonSerializer.SerializeToElement("kept") };
        authoringSource.Choices[1].Requirements[0].Extra = new() { ["FutureRequirement"] = JsonSerializer.SerializeToElement(42) };
        authoringSource.Choices[1].AfterChoiceActions[0].Extra = new() { ["FutureAction"] = JsonSerializer.SerializeToElement(42) };
        authoringSource.ShopData.Extra = new() { ["FutureShop"] = JsonSerializer.SerializeToElement(42) };
        var authoringBefore = ProjectStore.Serialize(authoringStory);
        var bundle = AuthoringOperations.CopyScenes([authoringSource, authoringTarget]);
        Check(bundle.Count == 2 && ProjectStore.Serialize(authoringStory) == authoringBefore, "copying a scene set captures content without changing the project");
        authoringSource.Text = "Edited after copying";
        var pasted = AuthoringOperations.PasteScenes(authoringStory, bundle, 1000, 1000);
        Check(pasted.Count == 2 && pasted.All(s => s.State != authoringSource.State && s.State != authoringTarget.State) && authoringStory.ListOfCharacterStates.Select(s => s.State).Distinct().Count() == 7, "pasted scenes receive unique IDs without changing existing scene identity");
        Check(pasted[0].DefaultNextId == pasted[0].State && pasted[0].Choices[0].NextStateId == pasted[1].State && pasted[1].DefaultNextId == pasted[0].State, "copying cycles remaps self, choice and continue links within the pasted set");
        Check(pasted[0].Choices[1].NextStateId == "gift" && pasted[0].Choices[^2].NextStateId == "" && pasted[0].Choices[^1].NextStateId == "missing_future" && pasted[1].Choices[^1].NextStateId == "departure", "paste preserves external, missing and blank fallback destinations");
        Check(pasted[0].Text != "Edited after copying" && pasted[0].Metadata["notes"] == authoringSource.Metadata["notes"] && pasted[0].Speaker == authoringSource.Speaker && pasted[0].Choices[1].Visibility == authoringSource.Choices[1].Visibility, "scene copies are snapshots and preserve metadata, speakers, delivery and visibility");
        Check(pasted[0].Extra!["FutureScene"].GetProperty("keep").GetInt32() == 42 && pasted[0].Choices[1].Extra!.ContainsKey("FutureChoice") && pasted[0].Choices[1].Requirements[0].Extra!.ContainsKey("FutureRequirement") && pasted[0].Choices[1].AfterChoiceActions[0].Extra!.ContainsKey("FutureAction") && pasted[0].ShopData.Extra!.ContainsKey("FutureShop"), "bulk paste preserves unknown fields on scenes, choices, conditions, actions and shops");
        Check(pasted[0].Choices[0].InternalId != authoringSource.Choices[0].InternalId && pasted[1].VisualX - pasted[0].VisualX == authoringTarget.VisualX - authoringSource.VisualX && pasted[1].VisualY - pasted[0].VisualY == authoringTarget.VisualY - authoringSource.VisualY, "paste renews choice identity and preserves relative graph positions");
        pasted[0].Choices[1].Requirements[0].Value = "9"; pasted[0].Metadata["notes"] = "Changed copy";
        var pastedAgain = AuthoringOperations.PasteScenes(authoringStory, bundle, 1500, 1500);
        Check(pastedAgain[0].Choices[1].Requirements[0].Value == "1" && pastedAgain[0].Metadata["notes"] != "Changed copy" && authoringSource.Choices[1].Requirements[0].Value == "1", "pastes never share mutable logic or metadata with source or previous copies");
        var otherAuthoringStory = Templates.Blank(); var otherPasted = AuthoringOperations.PasteScenes(otherAuthoringStory, bundle, 100, 100);
        Check(otherPasted[0].Choices[0].NextStateId == otherPasted[1].State && otherPasted[0].Choices[1].NextStateId == "gift" && otherAuthoringStory.Entry == "start", "cross-project paste remaps internal links and retains visible external targets without replacing the entry");
        Throws(() => AuthoringOperations.CopyScenes([]), "copy rejects an empty selection");
        Throws(() => AuthoringOperations.CopyScenes([authoringSource, authoringSource]), "copy rejects duplicate source IDs before ambiguous remapping");
        Throws(() => AuthoringOperations.CopyScenes([new DialogueState { State = "[EXIT]" }]), "copy requires reserved IDs to be repaired");
        var fullAuthoringStory = new DialogueProject { ListOfCharacterStates = Enumerable.Range(0, 10000).Select(i => new DialogueState { State = "s" + i }).ToList() };
        var fullBefore = ProjectStore.Serialize(fullAuthoringStory);
        Throws(() => AuthoringOperations.PasteScenes(fullAuthoringStory, bundle, 0, 0), "paste enforces the import scene limit before mutation");
        Check(ProjectStore.Serialize(fullAuthoringStory) == fullBefore, "capacity rejection leaves the whole project unchanged");
        Throws(() => AuthoringOperations.CreateConnectedScene(otherAuthoringStory, otherAuthoringStory.ListOfCharacterStates[0], otherPasted[0].Choices[0], "Wrong owner"), "connected-scene creation rejects a choice from another scene");
        var connected = AuthoringOperations.CreateConnectedScene(otherAuthoringStory, otherPasted[0], otherPasted[0].Choices[0], "  New branch  ");
        Check(connected.Title == "New branch" && connected.Speaker == otherPasted[0].Speaker && connected.DefaultNextId == "[EXIT]" && otherPasted[0].Choices[0].NextStateId == connected.State, "connected-scene creation adds a blank ending and changes only the chosen destination");
        AuthoringOperations.SaveSceneTemplate(authoringStory, pastedAgain[0], "Conversation");
        var savedSceneTemplate = authoringStory.SceneTemplates.Single();
        Check(savedSceneTemplate.Scene.DefaultNextId == savedSceneTemplate.Scene.State && savedSceneTemplate.Scene.Choices[0].NextStateId is null && savedSceneTemplate.Scene.Choices[^1].NextStateId is null, "scene templates retain self-links and detach external and missing destinations");
        AuthoringOperations.SaveChoiceTemplate(authoringStory, authoringSource.Choices[1], "Offer item");
        var savedChoiceTemplate = authoringStory.ChoiceTemplates.Single();
        Check(savedChoiceTemplate.Choice.NextStateId is null && savedChoiceTemplate.Choice.Requirements.Count == 1 && savedChoiceTemplate.Choice.AfterChoiceActions.Count == 2, "choice templates detach destinations while preserving conditions and actions");
        var templateBefore = ProjectStore.Serialize(authoringStory);
        Throws(() => AuthoringOperations.SaveSceneTemplate(authoringStory, authoringSource, " conversation "), "template names reject case-insensitive duplicates");
        Throws(() => AuthoringOperations.SaveChoiceTemplate(authoringStory, authoringSource.Choices[0], " "), "template names cannot be blank");
        Check(ProjectStore.Serialize(authoringStory) == templateBefore, "failed template saves do not mutate project or templates");
        var templateScene = AuthoringOperations.InsertSceneTemplate(authoringStory, savedSceneTemplate, 2000, 2000);
        Check(templateScene.DefaultNextId == templateScene.State && templateScene.State != savedSceneTemplate.Scene.State, "using a scene template renews identity and remaps its self-connection");
        var templateChoice = AuthoringOperations.InsertChoiceTemplate(templateScene, savedChoiceTemplate);
        templateChoice.Requirements[0].Value = "12";
        Check(templateChoice.NextStateId is null && templateChoice.InternalId != savedChoiceTemplate.Choice.InternalId && savedChoiceTemplate.Choice.Requirements[0].Value == "1", "using a choice template creates independent logic and fresh identity with a fallback destination");
        savedSceneTemplate.Extra = new() { ["FutureTemplate"] = JsonSerializer.SerializeToElement(42) };
        var templateRoundtrip = ProjectStore.Parse(ProjectStore.Serialize(authoringStory));
        Check(templateRoundtrip.SceneTemplates[0].Extra!.ContainsKey("FutureTemplate") && templateRoundtrip.SceneTemplates[0].Scene.Extra!.ContainsKey("FutureScene") && templateRoundtrip.ChoiceTemplates[0].Choice.Extra!.ContainsKey("FutureChoice"), "desktop saves retain template definitions and unknown fields throughout them");
        using (var webTemplates = JsonDocument.Parse(ProjectStore.StoryExport(authoringStory)))
            Check(!webTemplates.RootElement.TryGetProperty("SceneTemplates", out _) && !webTemplates.RootElement.TryGetProperty("ChoiceTemplates", out _) && webTemplates.RootElement.GetProperty("ListOfCharacterStates")[0].GetProperty("Choices")[1].GetProperty("Visibility").ValueKind == JsonValueKind.Number, "story exports omit desktop templates and retain numeric visibility");
        Check(ProjectStore.Parse("""{"ListOfCharacterStates":[],"SceneTemplates":null,"ChoiceTemplates":null}""").SceneTemplates.Count == 0, "null template collections normalize for compatible imports");
        Throws(() => ProjectStore.Parse("""{"ListOfCharacterStates":[],"SceneTemplates":[{"Name":"Broken","Scene":null}]}"""), "damaged scene templates are rejected before document replacement");
        Throws(() => ProjectStore.Parse("""{"ListOfCharacterStates":[],"ChoiceTemplates":[{"Name":"Broken","Choice":{"Requirements":[null]}}]}"""), "damaged template logic is rejected before document replacement");
        var templateVariables = Templates.Blank(); templateVariables.InitialVariables["aff:trust"] = 1; templateVariables.ListOfCharacterStates[0].Text = "[if trust >= 1]Friend[else]Stranger[/if]";
        var variableTemplateScene = Templates.Scene("template", "Template", "Character", "{player_name} [if trust >= 1]Friend[/if]", 0, 0);
        variableTemplateScene.DefaultAfterActions.Add(new DialogueAction { Type = "Inventory", TargetId = "trust", Amount = 1 });
        AuthoringOperations.SaveSceneTemplate(templateVariables, variableTemplateScene, "Inventory greeting");
        AuthoringOperations.SaveChoiceTemplate(templateVariables, new Choice { ButtonText = "{player_name}", Requirements = [new Requirement { TargetId = "trust" }] }, "Trust response");
        Check(new Simulation(templateVariables).Pages[0] == "Friend" && !DialogueEngine.DiscoverVariables(templateVariables).Contains("inv:trust"), "inactive template variables do not change live bare-condition bindings or runtime discovery");
        Check(VariableStore.References(templateVariables, "aff:trust").Count(r => r.IsTemplate) == 1 && VariableStore.References(templateVariables, "inv:trust").Count(r => r.IsTemplate) == 2, "template reference details distinguish active and template-local alias bindings");
        Throws(() => VariableStore.Delete(templateVariables, "player_name", true), "text variables referenced only by templates remain protected from deletion");
        VariableStore.Rename(templateVariables, "aff:trust", "aff:confidence"); VariableStore.Rename(templateVariables, "player_name", "hero", true);
        Check(templateVariables.SceneTemplates[0].Scene.Text.StartsWith("{hero} [if trust") && templateVariables.ChoiceTemplates[0].Choice.ButtonText == "{hero}" && templateVariables.ChoiceTemplates[0].Choice.Requirements[0].TargetId == "confidence" && new Simulation(templateVariables).Pages[0] == "Friend", "variable renames update saved templates while preserving their distinct aliases and live behavior");
        VariableStore.Rename(templateVariables, "inv:trust", "inv:tokens");
        Check(templateVariables.SceneTemplates[0].Scene.Text.Contains("[if inv:tokens") && templateVariables.SceneTemplates[0].Scene.DefaultAfterActions[0].TargetId == "tokens", "renaming a template-only numeric ID rewrites its typed action and inline alias");
        Throws(() => VariableStore.Delete(templateVariables, "inv:tokens"), "numeric variables used by saved templates cannot be deleted");
    }

    public static void UI(Action<bool, string> Check, Action<Window, string> Capture, string workflowRoot)
    {
        var authoringUiPath = Path.Combine(workflowRoot, "authoring.dialogue.json"); ProjectStore.AtomicWrite(authoringUiPath, ProjectStore.Serialize(Templates.Harbour()));
        var authoringWorkspace = new MainWindow(null, true); authoringWorkspace.Show(); Pump(authoringWorkspace.OpenProjectAsync(authoringUiPath));
        void AuthoringEdit(string label)
        {
            var editMenu = authoringWorkspace.FindControl<Menu>("AppMenu")!.Items.Cast<MenuItem>().Single(m => Equals(m.Header, "_Edit"));
            editMenu.Items.OfType<MenuItem>().Single(m => Equals(m.Header, label)).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Dispatcher.UIThread.RunJobs();
        }
        void AuthoringUndo() => Press(authoringWorkspace.FindControl<Button>("UndoButton")!);
        void AuthoringRedo() => Press(authoringWorkspace.FindControl<Button>("RedoButton")!);
        var authoringUiBefore = ProjectStore.Serialize(authoringWorkspace.Project);
        Press(NamedButton(authoringWorkspace, "Create scene from choice 1")); var authoringPrompt = Modal(authoringWorkspace);
        authoringPrompt.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, ""); Dispatcher.UIThread.RunJobs();
        Check(ProjectStore.Serialize(authoringWorkspace.Project) == authoringUiBefore && !authoringWorkspace.HasUnsavedChanges, "canceling connected-scene creation preserves the original connection and project");
        Press(NamedButton(authoringWorkspace, "Create scene from choice 1")); authoringPrompt = Modal(authoringWorkspace);
        authoringPrompt.GetVisualDescendants().OfType<TextBox>().Single().Text = "An unexpected visitor"; PressAnswer(authoringPrompt, "Continue");
        var uiConnected = authoringWorkspace.Project.ListOfCharacterStates[^1];
        Check(authoringWorkspace.Project.ListOfCharacterStates[0].Choices[0].NextStateId == uiConnected.State && uiConnected.Title == "An unexpected visitor" && authoringWorkspace.FindControl<TextBlock>("EditorSubtitle")!.Text!.StartsWith(uiConnected.State), "New scene creates and selects a connected scene through actual controls");
        AuthoringUndo(); Check(ProjectStore.Serialize(authoringWorkspace.Project) == authoringUiBefore && !authoringWorkspace.HasUnsavedChanges, "one Undo restores connected-scene creation and its previous destination");
        AuthoringRedo(); Check(authoringWorkspace.Project.ListOfCharacterStates.Count == 6, "Redo restores the scene and the choice connection together"); AuthoringUndo();
        AuthoringEdit("Copy scenes…"); var copyDialog = Modal(authoringWorkspace); var copyList = NamedControl<ListBox>(copyDialog, "CopySceneList");
        Check(copyList.IsFocused && copyList.SelectedItems!.Count == 1, "copy dialog focuses the scene list and preselects the current scene");
        copyDialog.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " "); copyDialog.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " "); Dispatcher.UIThread.RunJobs();
        Check(copyList.SelectedItems!.Count == 0, "Space toggles scene selection from the keyboard");
        copyList.SelectedItems!.Clear(); Check(!NamedControl<Button>(copyDialog, "CopyScenesConfirm").IsEnabled, "copy dialog disables empty selections");
        copyList.SelectedItems.Add(copyList.Items[0]); copyList.SelectedItems.Add(copyList.Items[1]); Dispatcher.UIThread.RunJobs();
        Capture(copyDialog, "copy-scenes-dark"); PressAnswer(copyDialog, "Copy selected scenes");
        Check(ProjectStore.Serialize(authoringWorkspace.Project) == authoringUiBefore && !authoringWorkspace.HasUnsavedChanges, "copying multiple scenes through the dialog does not dirty the story");
        AuthoringEdit("Paste scenes"); var uiPastes = authoringWorkspace.Project.ListOfCharacterStates.TakeLast(2).ToList();
        Check(authoringWorkspace.Project.ListOfCharacterStates.Count == 7 && uiPastes[0].Choices[0].NextStateId == uiPastes[1].State && authoringWorkspace.HasUnsavedChanges, "Paste scenes adds the selected set with remapped choice links and marks edits unsaved");
        AuthoringUndo(); Check(ProjectStore.Serialize(authoringWorkspace.Project) == authoringUiBefore, "one Undo removes an entire multi-scene paste");
        AuthoringRedo(); Check(authoringWorkspace.Project.ListOfCharacterStates.Count == 7, "Redo restores the entire pasted set"); AuthoringUndo();
        AuthoringEdit("Copy scenes…"); copyDialog = Modal(authoringWorkspace); PressAnswer(copyDialog, "Select all");
        Check(NamedControl<ListBox>(copyDialog, "CopySceneList").SelectedItems!.Count == 5, "Select all marks every source scene for copying"); PressAnswer(copyDialog, "Cancel");
        AuthoringEdit("Paste scenes"); Check(authoringWorkspace.Project.ListOfCharacterStates.Count == 7, "canceling a later copy preserves the previous scene clipboard"); AuthoringUndo();
        Press(authoringWorkspace.FindControl<Button>("NotesTab")!); Press(NamedButton(authoringWorkspace, "Save scene template…")); authoringPrompt = Modal(authoringWorkspace);
        PressAnswer(authoringPrompt, "Cancel"); Check(ProjectStore.Serialize(authoringWorkspace.Project) == authoringUiBefore, "canceling template naming saves no template");
        Press(NamedButton(authoringWorkspace, "Save scene template…")); authoringPrompt = Modal(authoringWorkspace);
        authoringPrompt.GetVisualDescendants().OfType<TextBox>().Single().Text = "Opening conversation"; PressAnswer(authoringPrompt, "Continue");
        Check(authoringWorkspace.Project.SceneTemplates.Single().Name == "Opening conversation" && authoringWorkspace.HasUnsavedChanges, "Notes saves a reusable scene template in the project");
        AuthoringUndo(); Check(authoringWorkspace.Project.SceneTemplates.Count == 0 && !authoringWorkspace.HasUnsavedChanges, "saving a scene template is one undo step"); AuthoringRedo();
        Press(authoringWorkspace.FindControl<Button>("ContentTab")!); Press(NamedButton(authoringWorkspace, "Save choice 2 template")); authoringPrompt = Modal(authoringWorkspace);
        authoringPrompt.GetVisualDescendants().OfType<TextBox>().Single().Text = "Give an item"; PressAnswer(authoringPrompt, "Continue");
        Check(authoringWorkspace.Project.ChoiceTemplates.Single().Choice.Requirements.Count == 1, "Content captures the chosen response and its logic as a template");
        var authoringWithTemplates = ProjectStore.Serialize(authoringWorkspace.Project);
        AuthoringEdit("Scene & choice templates…"); var templateDialog = Modal(authoringWorkspace);
        Check(NamedControl<TextBox>(templateDialog, "TemplateSearch").IsFocused && NamedControl<ListBox>(templateDialog, "TemplateList").Items.Count == 2, "template manager opens with scene and choice templates and keyboard focus");
        NamedControl<TextBox>(templateDialog, "TemplateSearch").Text = "nothing matches"; Dispatcher.UIThread.RunJobs();
        Check(!NamedControl<Button>(templateDialog, "InsertTemplate").IsEnabled && !NamedControl<Button>(templateDialog, "DeleteTemplate").IsEnabled && NamedControl<TextBlock>(templateDialog, "TemplateSummary").Text!.StartsWith("No matching"), "empty template searches explain the state and disable operations");
        NamedControl<TextBox>(templateDialog, "TemplateSearch").Text = "OPENING"; Dispatcher.UIThread.RunJobs(); Capture(templateDialog, "templates-dark");
        PressAnswer(templateDialog, "Close"); Check(ProjectStore.Serialize(authoringWorkspace.Project) == authoringWithTemplates, "searching and closing the template manager leaves story and definitions unchanged");
        AuthoringEdit("Scene & choice templates…"); templateDialog = Modal(authoringWorkspace); PressAnswer(templateDialog, "Insert template");
        Check(authoringWorkspace.Project.ListOfCharacterStates.Count == 6 && authoringWorkspace.Project.ListOfCharacterStates[^1].Title == "A light in the fog" && authoringWorkspace.Project.ListOfCharacterStates[^1].Choices[0].NextStateId is null, "Insert template creates a new scene with detached external links");
        AuthoringUndo(); Check(ProjectStore.Serialize(authoringWorkspace.Project) == authoringWithTemplates, "one Undo removes a template scene while preserving template definitions");
        Press(NamedButton(authoringWorkspace, "Add choice from template…")); templateDialog = Modal(authoringWorkspace);
        Check(NamedControl<ListBox>(templateDialog, "TemplateList").Items.Count == 1, "choice insertion lists only reusable choices"); templateDialog.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r"); Dispatcher.UIThread.RunJobs();
        Check(authoringWorkspace.Project.ListOfCharacterStates[0].Choices.Count == 4 && authoringWorkspace.Project.ListOfCharacterStates[0].Choices[^1].NextStateId is null && authoringWorkspace.Project.ListOfCharacterStates[0].Choices[^1].AfterChoiceActions.Count == 2, "choice template insertion adds the full logic and uses the scene fallback");
        AuthoringUndo(); Check(ProjectStore.Serialize(authoringWorkspace.Project) == authoringWithTemplates, "one Undo removes an inserted choice template");
        AuthoringEdit("Scene & choice templates…"); templateDialog = Modal(authoringWorkspace); PressAnswer(templateDialog, "Delete template");
        Check(authoringWorkspace.Project.SceneTemplates.Count == 0 && NamedControl<ListBox>(templateDialog, "TemplateList").Items.Count == 1, "template deletion refreshes the available definitions"); PressAnswer(templateDialog, "Close");
        AuthoringUndo(); Check(ProjectStore.Serialize(authoringWorkspace.Project) == authoringWithTemplates, "template deletion can be undone after closing the manager");
        Press(authoringWorkspace.FindControl<Button>("VariablesButton")!); var templateVariableManager = Modal(authoringWorkspace);
        Press(NamedButton(templateVariableManager, "References for aff:trust")); var templateReferenceDialog = templateVariableManager.OwnedWindows.Single(w => w.IsVisible);
        Check(templateReferenceDialog.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("Scene template: Opening conversation") == true) && templateReferenceDialog.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("Choice template: Give an item") == true), "variable manager reference details display both scene and choice templates");
        PressAnswer(templateReferenceDialog, "Close"); Press(NamedButton(templateVariableManager, "Rename aff:trust")); var templateRenamePrompt = templateVariableManager.OwnedWindows.Single(w => w.IsVisible);
        templateRenamePrompt.GetVisualDescendants().OfType<TextBox>().Single().Text = "aff:rapport"; PressAnswer(templateRenamePrompt, "Continue"); PressAnswer(templateVariableManager, "Apply changes");
        Check(authoringWorkspace.Project.SceneTemplates[0].Scene.Choices[1].AfterChoiceActions[1].TargetId == "rapport" && authoringWorkspace.Project.ChoiceTemplates[0].Choice.AfterChoiceActions[1].TargetId == "rapport", "applying a variable rename updates reusable template logic in the actual manager");
        AuthoringUndo(); Check(ProjectStore.Serialize(authoringWorkspace.Project) == authoringWithTemplates, "one Undo restores template-aware variable changes and live story together");
        AuthoringEdit("Scene & choice templates…"); templateDialog = Modal(authoringWorkspace);
        templateDialog.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, ""); Dispatcher.UIThread.RunJobs();
        Check(NamedControl<ListBox>(templateDialog, "TemplateList").IsKeyboardFocusWithin, "Down moves from template search to selectable results");
        templateDialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, ""); Dispatcher.UIThread.RunJobs();
        Check(ProjectStore.Serialize(authoringWorkspace.Project) == authoringWithTemplates, "Escape closes template previews without inserting anything");
        foreach (var theme in new[] { "Light", "High contrast" })
        {
            authoringWorkspace.ChangeAppearance(theme); AuthoringEdit("Copy scenes…"); copyDialog = Modal(authoringWorkspace); Capture(copyDialog, "copy-scenes-" + theme.ToLowerInvariant().Replace(' ', '-')); PressAnswer(copyDialog, "Cancel");
            AuthoringEdit("Scene & choice templates…"); templateDialog = Modal(authoringWorkspace); Capture(templateDialog, "templates-" + theme.ToLowerInvariant().Replace(' ', '-')); PressAnswer(templateDialog, "Close");
        }
        foreach (var scale in new[] { 1.15, 1.3 })
        {
            authoringWorkspace.ChangeAppearance("Dark", scale); AuthoringEdit("Copy scenes…"); copyDialog = Modal(authoringWorkspace);
            Check(copyDialog.Content is LayoutTransformControl && Math.Abs(copyDialog.Width - 650 * scale) < 1, "copy dialog respects interface size " + scale); Capture(copyDialog, "copy-scenes-scale-" + (int)(scale * 100)); PressAnswer(copyDialog, "Cancel");
            AuthoringEdit("Scene & choice templates…"); templateDialog = Modal(authoringWorkspace);
            Check(templateDialog.Content is LayoutTransformControl && Math.Abs(templateDialog.Width - 760 * scale) < 1, "template manager respects interface size " + scale); Capture(templateDialog, "templates-scale-" + (int)(scale * 100));
            Check(templateDialog.GetVisualDescendants().Where(c => c.TemplatedParent is null && c is TextBox or ListBox).All(c => !string.IsNullOrEmpty(AutomationProperties.GetName(c))), "template search and selection have accessibility names at " + scale); PressAnswer(templateDialog, "Close");
        }
        authoringWorkspace.ChangeAppearance("Dark", 1.3); authoringWorkspace.Width = 1000; authoringWorkspace.Height = 680; Dispatcher.UIThread.RunJobs(); Capture(authoringWorkspace, "authoring-compact-large-text");
        NamedButton(authoringWorkspace, "Create scene from choice 1").BringIntoView(); Dispatcher.UIThread.RunJobs(); Capture(authoringWorkspace, "authoring-choices-large-text");
        AuthoringEdit("Scene & choice templates…"); templateDialog = Modal(authoringWorkspace); templateDialog.Width = 600; templateDialog.Height = 430; Dispatcher.UIThread.RunJobs(); Capture(templateDialog, "templates-compact-large-text");
        var insertTemplateButton = NamedControl<Button>(templateDialog, "InsertTemplate"); var insertPoint = insertTemplateButton.TranslatePoint(default, templateDialog)!.Value;
        Check(insertPoint.Y + insertTemplateButton.Bounds.Height * 1.3 < templateDialog.ClientSize.Height, "compact large-text template manager keeps Insert reachable"); PressAnswer(templateDialog, "Close");
        var authoringSavePicker = new TestFilePicker(); var authoringSave = new MainWindow(null, true, filePicker: authoringSavePicker); authoringSave.Show();
        var authoringRoundtripPath = Path.Combine(workflowRoot, "authoring-template-roundtrip.dialogue.json");
        ProjectStore.AtomicWrite(authoringRoundtripPath, authoringWithTemplates); Pump(authoringSave.OpenProjectAsync(authoringRoundtripPath));
        Check(authoringSave.Project.SceneTemplates.Count == 1 && authoringSave.Project.ChoiceTemplates.Count == 1, "opening a saved project restores reusable template definitions"); authoringSave.Close();
        Pump(authoringWorkspace.SaveProjectAsync()); var otherAuthoringPath = Path.Combine(workflowRoot, "other-authoring.dialogue.json"); ProjectStore.AtomicWrite(otherAuthoringPath, ProjectStore.Serialize(Templates.Blank()));
        Pump(authoringWorkspace.OpenProjectAsync(otherAuthoringPath)); AuthoringEdit("Paste scenes");
        Check(authoringWorkspace.Project.ListOfCharacterStates.Count == 3 && authoringWorkspace.Project.ListOfCharacterStates[1].Choices[0].NextStateId == authoringWorkspace.Project.ListOfCharacterStates[2].State && authoringWorkspace.Project.Entry == "start", "scene clipboard persists across actual project changes and preserves the destination project's entry");
        AuthoringUndo(); Check(!authoringWorkspace.HasUnsavedChanges && authoringWorkspace.Project.ListOfCharacterStates.Count == 1, "cross-project paste remains a single undo step");
        authoringWorkspace.Close();
    }
}
