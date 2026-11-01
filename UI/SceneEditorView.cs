using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using DialogueMachine.Core;

namespace DialogueMachine.UI;

/// <summary>What the scene editor needs from its host window. Operations stay in Core and MainWindow; the view only builds controls.</summary>
internal sealed record SceneEditorActions(
    Action Edited, Action Commit, Action TextCommitted, Action<Action> Mutate, Action<string> SwitchTab,
    Func<DialogueState, string?, Action<string?>, int?, Control> Destination, Func<DialogueState, Choice, string> DestinationLabel, Action<StackPanel, DialogueState> Connections,
    Func<Task<string?>> PickVariable, Func<DialogueState, Choice, Task> NewSceneFromChoice, Func<Choice, Task> SaveChoiceTemplate,
    Func<DialogueState, Task> SaveSceneTemplate, Func<bool, Task> ShowTemplates, Func<Task> Rename, Action SetStart,
    Action Duplicate, Func<Task> Delete, Func<bool, Task> Play);

/// <summary>The Content, Logic and Notes tabs of the selected scene, built into the window's editor panel.</summary>
internal sealed class SceneEditorView(StackPanel panel, ScrollViewer scroll, SceneEditorActions actions)
{
    private DialogueProject project = new();
    private const int CollapseAbove = 4;
    private DialogueState? shownScene; private string? shownTab; private Choice? focusChoice;
    private readonly Dictionary<Choice, bool> open = [];
    public string Tab { get; set; } = "Content";

    public void Refresh(DialogueState? scene, DialogueProject current)
    {
        project = current;
        var keep = shownScene == scene && shownTab == Tab ? scroll.Offset : default;
        shownScene = scene; shownTab = Tab; var reveal = focusChoice is not null;
        panel.Children.Clear();
        if (scene is null) { panel.Children.Add(Ui.Text("Choose a scene in the outline, or add one to start writing.", 14, true)); return; }
        switch (Tab) { case "Logic": BuildLogic(scene); break; case "Notes": BuildNotes(scene); break; default: BuildContent(scene); break; }
        if (!reveal) Avalonia.Threading.Dispatcher.UIThread.Post(() => { scroll.UpdateLayout(); scroll.Offset = keep; }, Avalonia.Threading.DispatcherPriority.Loaded);
    }
    private bool IsOpen(DialogueState s, Choice c) => open.TryGetValue(c, out var v) ? v : s.Choices.Count <= CollapseAbove;
    public void Expand(DialogueState scene, int index) { if (index < scene.Choices.Count) focusChoice = scene.Choices[index]; }
    private void ShowConditions(Choice c) { focusChoice = c; actions.SwitchTab("Logic"); }
    private void Refresh() => Refresh(shownScene, project);
    private TextBox EditText(string text, Action<string> apply, bool multiline = false)
    {
        var t = Ui.Input(text, v => { apply(v); actions.Edited(); }, multiline);
        t.LostFocus += (_, _) => actions.TextCommitted(); return t;
    }
    private AutoCompleteBox VariableInput(string type, string id, Action<string> apply, bool flags = false)
    {
        var prefix = type == "Inventory" ? "inv:" : "aff:";
        var input = new AutoCompleteBox { Text = id, ItemsSource = flags ? VariableStore.DiscoverFlags(project) : DialogueEngine.DiscoverVariables(project).Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).Select(k => k[4..]).ToList(), MinimumPrefixLength = 0, FilterMode = AutoCompleteFilterMode.Contains, IsTextCompletionEnabled = false, PlaceholderText = flags ? "Choose or type a flag ID" : "Choose or type a variable ID", PlaceholderForeground = Ui.Brush("Muted") };
        var previous = id;
        input.TextChanged += (_, _) => { var current = input.Text ?? ""; if (current != previous) { previous = current; apply(current); actions.Edited(); } };
        input.LostFocus += (_, _) => actions.Commit(); return input;
    }
    private AutoCompleteBox SpeakerInput(string current, Action<string> apply)
    {
        var input = new AutoCompleteBox
        {
            Text = current,
            ItemsSource = VariableStore.DiscoverSpeakers(project),
            MinimumPrefixLength = 0,
            FilterMode = AutoCompleteFilterMode.Contains,
            IsTextCompletionEnabled = false,
            PlaceholderText = "Character name",
            PlaceholderForeground = Ui.Brush("Muted")
        };
        AutomationProperties.SetName(input, "Speaker");
        var previous = current;
        input.TextChanged += (_, _) => { var val = input.Text ?? ""; if (val != previous) { previous = val; apply(val); actions.Edited(); } };
        input.LostFocus += (_, _) => actions.Commit();
        return input;
    }
    private void BuildContent(DialogueState s)
    {
        panel.Children.Add(Ui.Field("Scene title", EditText(s.Title, v => s.Metadata["title"] = v)));
        panel.Children.Add(Ui.Field("Speaker", SpeakerInput(s.Speaker, v => s.Metadata["speaker"] = v)));
        var narrative = EditText(s.Text, v => s.Text = v, true); narrative.MinHeight = 175;
        AutomationProperties.SetName(narrative, "Dialogue text");
        AutomationProperties.SetHelpText(narrative, "One dialogue page per line. Supports bold, italic, underline, colour and inline conditions.");
        var tools = new WrapPanel();
        void InsertButton(string label, string before, string after)
        {
            var button = Ui.Button(label, () =>
            {
                var start = Math.Min(narrative.SelectionStart, narrative.SelectionEnd); var end = Math.Max(narrative.SelectionStart, narrative.SelectionEnd);
                var text = narrative.Text ?? ""; narrative.Text = text[..start] + before + text[start..end] + after + text[end..];
                narrative.Focus(); narrative.SelectionStart = start + before.Length; narrative.SelectionEnd = end + before.Length; actions.Commit();
            }, "subtle"); button.Margin = new Thickness(0,0,4,0); ToolTip.SetTip(button, "Insert " + label + " at the cursor or around selected text"); tools.Children.Add(button);
        }
        InsertButton("Bold", "[b]", "[/b]"); InsertButton("Italic", "[i]", "[/i]"); InsertButton("Condition", "[if aff:trust >= 1]", "[else]Alternative text[/if]"); InsertButton("{player}", "{player_name}", "");
        tools.Children.Add(Ui.AsyncButton("Variable…", async () =>
        {
            var key = await actions.PickVariable(); if (key is null) return;
            var start = Math.Min(narrative.SelectionStart, narrative.SelectionEnd); var end = Math.Max(narrative.SelectionStart, narrative.SelectionEnd);
            var text = narrative.Text ?? ""; var inserted = "{" + key + "}";
            narrative.Text = text[..start] + inserted + text[end..]; narrative.Focus(); narrative.CaretIndex = start + inserted.Length; actions.Commit();
        }, "subtle"));
        panel.Children.Add(Ui.Field("Dialogue", Ui.Stack(tools, narrative, Ui.Text("One page per line. Playtest to preview formatting and conditions.", 11, true))));
        var delivery = Ui.Select(["All pages, in order", "One random line"], s.SayItAll ? "All pages, in order" : "One random line", v => { s.SayItAll = v == "All pages, in order"; actions.Edited(); });
        panel.Children.Add(Ui.Field("Delivery", delivery));
        panel.Children.Add(Ui.Field("Continue destination", actions.Destination(s, s.DefaultNextId, v => s.DefaultNextId = v, null)));
        panel.Children.Add(Ui.Text("Choices inherit this destination when their own target is unset.", 11, true));
        var heading = Ui.Text("PLAYER CHOICES", 11, true); heading.LetterSpacing = 1.2; heading.Margin = new Thickness(0,12,0,0); panel.Children.Add(heading);
        if (s.Choices.Count > CollapseAbove)
        {
            var all = s.Choices.Any(c => !IsOpen(s, c));
            panel.Children.Add(Ui.Button(all ? "Expand all choices" : "Collapse all choices", () => { foreach (var c in s.Choices) open[c] = all; Refresh(); }, "subtle"));
        }
        for (var i = 0; i < s.Choices.Count; i++)
        {
            var choice = s.Choices[i]; var index = i;
            var isOpen = IsOpen(s, choice) || choice == focusChoice;
            var label = EditText(choice.ButtonText, v => choice.ButtonText = v, true); label.MinHeight = 65;
            AutomationProperties.SetName(label, $"Player response {i + 1}");
            label.AddHandler(InputElement.KeyDownEvent, (_, e) => { if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { AddChoice(s); e.Handled = true; } }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            if (choice == focusChoice)
            {
                focusChoice = null; open[choice] = true;
                Avalonia.Threading.Dispatcher.UIThread.Post(() => { label.Focus(); label.BringIntoView(); }, Avalonia.Threading.DispatcherPriority.Loaded);
            }
            var top = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto") };
            var toggle = Ui.Button((isOpen ? "▾  " : "▸  ") + $"Choice {i + 1}", () => { open[choice] = !isOpen; Refresh(); }, "subtle");
            AutomationProperties.SetName(toggle, (isOpen ? "Collapse" : "Expand") + $" choice {i + 1}"); top.Children.Add(toggle);
            if (choice.Requirements.Count > 0 || isOpen)
            {
                var conditions = Ui.Button(choice.Requirements.Count > 0 ? "◆ If  ›" : "If  ›", () => ShowConditions(choice), "subtle");
                AutomationProperties.SetName(conditions, $"Edit choice {i + 1} conditions"); ToolTip.SetTip(conditions, "Open this choice's conditions in the Logic tab"); Grid.SetColumn(conditions, 1); top.Children.Add(conditions);
            }
            var up = Ui.Button("↑", () => actions.Mutate(() => { if (index > 0) (s.Choices[index - 1], s.Choices[index]) = (s.Choices[index], s.Choices[index - 1]); }), "subtle"); up.IsEnabled = i > 0; AutomationProperties.SetName(up, $"Move choice {i + 1} up"); Grid.SetColumn(up, 2); top.Children.Add(up);
            var down = Ui.Button("↓", () => actions.Mutate(() => { if (index < s.Choices.Count - 1) (s.Choices[index + 1], s.Choices[index]) = (s.Choices[index], s.Choices[index + 1]); }), "subtle"); down.IsEnabled = i < s.Choices.Count - 1; AutomationProperties.SetName(down, $"Move choice {i + 1} down"); Grid.SetColumn(down, 3); top.Children.Add(down);
            var remove = Ui.Button("×", () => actions.Mutate(() => s.Choices.Remove(choice)), "subtle"); AutomationProperties.SetName(remove, $"Remove choice {i + 1}"); Grid.SetColumn(remove, 4); top.Children.Add(remove);
            if (!isOpen)
            {
                var text = DialogueEngine.PlainText(choice.ButtonText).ReplaceLineEndings(" ").Trim();
                var summary = Ui.Text((text.Length > 0 ? text : "(no response text)") + "  →  " + actions.DestinationLabel(s, choice), 13, true);
                summary.TextWrapping = Avalonia.Media.TextWrapping.NoWrap; summary.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis;
                panel.Children.Add(Ui.Card(Ui.Stack(top, summary), 12)); continue;
            }
            var create = Ui.AsyncButton("New scene…", () => actions.NewSceneFromChoice(s, choice), "subtle"); AutomationProperties.SetName(create, $"Create scene from choice {i + 1}");
            var saveTemplate = Ui.AsyncButton("Save template…", () => actions.SaveChoiceTemplate(choice), "subtle"); AutomationProperties.SetName(saveTemplate, $"Save choice {i + 1} template");
            ToolTip.SetTip(label, "Ctrl+Enter adds the next choice");
            panel.Children.Add(Ui.Card(Ui.Stack(top, Ui.Field("Player response", label), Ui.Field("Go to", actions.Destination(s, choice.NextStateId, v => choice.NextStateId = v, index)), Ui.Row(create, saveTemplate)), 12));
        }
        panel.Children.Add(Ui.Button("＋  Add a player choice", () => AddChoice(s), "primary"));
        panel.Children.Add(Ui.AsyncButton("Add choice from template…", () => actions.ShowTemplates(true), "subtle"));
        panel.Children.Add(Ui.Text("Tip: connect scenes by dragging an output dot onto another scene in the graph.", 11, true));
    }
    private void AddChoice(DialogueState s)
    {
        var choice = new Choice();
        actions.Mutate(() => s.Choices.Add(choice));
        focusChoice = choice; Refresh();
    }
    private void BuildLogic(DialogueState s)
    {
        panel.Children.Add(Ui.Field("Scene type", Ui.Select(["Dialogue", "Shop"], s.Type, v => { s.Type = v; actions.Edited(); actions.Commit(); Refresh(); })));
        if (s.Type == "Shop")
        {
            panel.Children.Add(Ui.Field("Shop ID", EditText(s.ShopData.ShopId, v => s.ShopData.ShopId = v)));
            panel.Children.Add(Ui.Text("Playtest logs shop visits. The exported shop ID is handled by your game.", 12, true));
        }
        panel.Children.Add(Ui.Text("When leaving this scene", 17));
        panel.Children.Add(Ui.Text("These actions run once per transition, after the chosen response's actions.", 12, true));
        AddActions(panel, s.DefaultAfterActions);
        for (var i = 0; i < s.Choices.Count; i++)
        {
            var choice = s.Choices[i];
            var card = new StackPanel { Spacing = 10 };
            card.Children.Add(Ui.Text($"Choice {i + 1}: " + DialogueEngine.PlainText(choice.ButtonText), 15));
            var visibility = Ui.Select(["Disable", "Hide", "Always"], choice.Visibility.ToString(), v => { choice.Visibility = Enum.Parse<VisibilityMode>(v); actions.Edited(); });
            card.Children.Add(Ui.Field("When conditions fail", visibility));
            card.Children.Add(Ui.Text("Disable = visible but locked · Hide = invisible · Always = ignore conditions", 11, true));
            card.Children.Add(Ui.Text("Conditions · all must pass", 13));
            var flagIds = VariableStore.DiscoverFlags(project);
            foreach (var requirement in choice.Requirements.ToArray())
            {
                var r = requirement;
                if (VariableStore.IsFlagCondition(r, flagIds))
                {
                    var state = Ui.Select(["is set", "is not set"], (r.Operator == "==") == (r.Value == "1") ? "is set" : "is not set", v => { r.Operator = "=="; r.Value = v == "is set" ? "1" : "0"; actions.Edited(); });
                    card.Children.Add(Ui.Card(Ui.Stack(Ui.Field("Flag", VariableInput("Affinity", r.TargetId, v => r.TargetId = v, true)), Ui.Field("Flag state", state), Ui.Button("Remove condition", () => actions.Mutate(() => choice.Requirements.Remove(r)), "subtle")), 10));
                    continue;
                }
                var type = Ui.Select(["Affinity", "Inventory"], r.Type, v => { r.Type = v; actions.Edited(); actions.Commit(); Refresh(); });
                var op = Ui.Select(["==", "!=", ">", "<", ">=", "<="], r.Operator, v => { r.Operator = v; actions.Edited(); });
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,80") }; row.Children.Add(type); Grid.SetColumn(op, 2); row.Children.Add(op);
                card.Children.Add(Ui.Card(Ui.Stack(row, Ui.Field("Variable ID", VariableInput(r.Type, r.TargetId, v => r.TargetId = v)), Ui.Field("Compare with (integer)", EditText(r.Value, v => r.Value = v)), Ui.Button("Remove condition", () => actions.Mutate(() => choice.Requirements.Remove(r)), "subtle")), 10));
            }
            card.Children.Add(Ui.Row(Ui.Button("＋ Add condition", () => actions.Mutate(() => choice.Requirements.Add(new Requirement { TargetId = "trust" })), "subtle"),
                Ui.Button("＋ Add flag condition", () => actions.Mutate(() => choice.Requirements.Add(new Requirement { TargetId = flagIds.FirstOrDefault() ?? "flag", Operator = "==", Value = "1" })), "subtle")));
            card.Children.Add(Ui.Text("After this response", 13));
            AddActions(card, choice.AfterChoiceActions);
            var border = Ui.Card(card, 12); panel.Children.Add(border);
            if (choice == focusChoice)
            {
                focusChoice = null;
                Avalonia.Threading.Dispatcher.UIThread.Post(() => { border.BringIntoView(); visibility.Focus(); }, Avalonia.Threading.DispatcherPriority.Loaded);
            }
        }
        if (s.Choices.Count == 0) panel.Children.Add(Ui.Text("Add choices in Content to give a response conditions and actions.", 12, true));
        panel.Children.Add(Ui.Text("Known variables: " + string.Join(", ", DialogueEngine.DiscoverVariables(project)), 11, true));
    }
    private void AddActions(StackPanel target, List<DialogueAction> list)
    {
        foreach (var action in list.ToArray())
        {
            var a = action;
            var flag = a.Type is "Set" or "Toggle";
            var type = Ui.Select(["Affinity", "Inventory", "Set flag", "Toggle flag", "Event", "Script"], a.Type switch { "Set" => "Set flag", "Toggle" => "Toggle flag", _ => a.Type }, v => { a.Type = v switch { "Set flag" => "Set", "Toggle flag" => "Toggle", _ => v }; if (a.Type == "Set") a.Amount = a.Amount == 0 ? 0 : 1; actions.Edited(); actions.Commit(); Refresh(); });
            var group = Ui.Stack(Ui.Field("Action type", type), Ui.Field(a.Type is "Event" or "Script" ? "Event / method ID" : flag ? "Flag ID" : "Variable ID", a.Type is "Event" or "Script" ? EditText(a.TargetId, v => a.TargetId = v) : VariableInput(a.Type, a.TargetId, v => a.TargetId = v, flag)));
            if (a.Type is "Event" or "Script") group.Children.Add(Ui.Field("Parameter", EditText(a.Parameter ?? "", v => a.Parameter = v)));
            else if (a.Type == "Set") group.Children.Add(Ui.Field("Set flag to", Ui.Select(["On (1)", "Off (0)"], a.Amount == 0 ? "Off (0)" : "On (1)", v => { a.Amount = v == "On (1)" ? 1 : 0; actions.Edited(); })));
            else if (a.Type == "Toggle") group.Children.Add(Ui.Text("Flips the flag between off (0) and on (1).", 12, true));
            else
            {
                var amount = new NumericUpDown { Value = a.Amount, Minimum = int.MinValue, Maximum = int.MaxValue, Increment = 1, FormatString = "0" };
                amount.ValueChanged += (_, _) => { if (amount.Value is { } value) { a.Amount = (int)value; actions.Edited(); } }; group.Children.Add(Ui.Field("Amount to add (negative to subtract)", amount));
            }
            group.Children.Add(Ui.Button("Remove action", () => actions.Mutate(() => list.Remove(a)), "subtle")); target.Children.Add(Ui.Card(group, 10));
        }
        target.Children.Add(Ui.Row(Ui.Button("＋ Add action", () => actions.Mutate(() => list.Add(new DialogueAction { TargetId = "trust", Amount = 1 })), "subtle"),
            Ui.Button("＋ Add flag action", () => actions.Mutate(() => list.Add(new DialogueAction { Type = "Set", TargetId = VariableStore.DiscoverFlags(project).FirstOrDefault() ?? "flag", Amount = 1 })), "subtle")));
    }
    private void BuildNotes(DialogueState s)
    {
        panel.Children.Add(Ui.Text("Scene organisation", 17));
        panel.Children.Add(Ui.Field("Scene ID", Ui.Row(Ui.Text(s.State, 13, true), Ui.AsyncButton("Rename…", actions.Rename, "subtle"))));
        panel.Children.Add(Ui.Button(s.State == project.Entry ? "◆  This is the story start" : "Set as story start", actions.SetStart));
        panel.Children.Add(Ui.Field("Writer's notes", EditText(s.Metadata.GetValueOrDefault("notes", ""), v => s.Metadata["notes"] = v, true)));
        panel.Children.Add(Ui.Field("Tags (comma separated)", EditText(s.Metadata.GetValueOrDefault("tags", ""), v => s.Metadata["tags"] = v)));
        panel.Children.Add(Ui.Text("Notes and tags are saved in scene metadata. They won't appear in dialogue.", 12, true));
        panel.Children.Add(Ui.Row(Ui.Button("Duplicate scene", actions.Duplicate), Ui.AsyncButton("Delete scene…", actions.Delete)));
        panel.Children.Add(Ui.AsyncButton("Save scene template…", () => actions.SaveSceneTemplate(s), "subtle"));
        panel.Children.Add(Ui.AsyncButton("Scene & choice templates…", () => actions.ShowTemplates(false), "subtle"));
        panel.Children.Add(Ui.AsyncButton("▶  Play from this scene", () => actions.Play(true), "play"));
        actions.Connections(panel, s);
    }
}
