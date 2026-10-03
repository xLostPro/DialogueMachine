using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using DialogueMachine.Core;

namespace DialogueMachine.UI;

public sealed partial class MainWindow
{
    private void RefreshEditor()
    {
        Editor.Children.Clear();
        C<TextBlock>("EditorTitle").Text = selected?.Title ?? "Select a scene";
        C<TextBlock>("EditorSubtitle").Text = selected is null ? "Your scene's content and connections live here." : selected.State + (selected.State == project.Entry ? "  ·  Story start" : "");
        foreach (var tab in new[] { "Content", "Logic", "Notes" }) C<Button>(tab + "Tab").Background = Ui.Brush(tab == editorTab ? "Selection" : "Panel");
        if (selected is null) { Editor.Children.Add(Ui.Text("Choose a scene in the outline, or add one to start writing.", 14, true)); return; }
        switch (editorTab) { case "Logic": BuildLogicEditor(selected); break; case "Notes": BuildNotesEditor(selected); break; default: BuildContentEditor(selected); break; }
    }
    private TextBox EditText(string text, Action<string> apply, bool multiline = false)
    {
        var t = Ui.Input(text, v => { apply(v); Edited(); }, multiline);
        t.LostFocus += (_, _) => { Commit(); RefreshOutline(); C<TextBlock>("EditorTitle").Text = selected?.Title; }; return t;
    }
    private AutoCompleteBox VariableInput(string type, string id, Action<string> apply)
    {
        var prefix = type == "Inventory" ? "inv:" : "aff:";
        var input = new AutoCompleteBox { Text = id, ItemsSource = DialogueEngine.DiscoverVariables(project).Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).Select(k => k[4..]).ToList(), MinimumPrefixLength = 0, FilterMode = AutoCompleteFilterMode.Contains, IsTextCompletionEnabled = false, PlaceholderText = "Choose or type a variable ID", PlaceholderForeground = Ui.Brush("Muted") };
        var previous = id;
        input.TextChanged += (_, _) => { var current = input.Text ?? ""; if (current != previous) { previous = current; apply(current); Edited(); } };
        input.LostFocus += (_, _) => Commit(); return input;
    }
    private void BuildContentEditor(DialogueState s)
    {
        Editor.Children.Add(Ui.Field("Scene title", EditText(s.Title, v => s.Metadata["title"] = v)));
        Editor.Children.Add(Ui.Field("Speaker", EditText(s.Speaker, v => s.Metadata["speaker"] = v)));
        var narrative = EditText(s.Text, v => s.Text = v, true); narrative.MinHeight = 175;
        Avalonia.Automation.AutomationProperties.SetName(narrative, "Dialogue text");
        AutomationProperties.SetHelpText(narrative, "One dialogue page per line. Supports bold, italic, underline, colour and inline conditions.");
        var tools = new WrapPanel();
        void InsertButton(string label, string before, string after)
        {
            var button = Ui.Button(label, () =>
            {
                var start = Math.Min(narrative.SelectionStart, narrative.SelectionEnd); var end = Math.Max(narrative.SelectionStart, narrative.SelectionEnd);
                var text = narrative.Text ?? ""; narrative.Text = text[..start] + before + text[start..end] + after + text[end..];
                narrative.Focus(); narrative.SelectionStart = start + before.Length; narrative.SelectionEnd = end + before.Length; Commit();
            }, "subtle"); button.Margin = new Thickness(0,0,4,0); ToolTip.SetTip(button, "Insert " + label + " at the cursor or around selected text"); tools.Children.Add(button);
        }
        InsertButton("Bold", "[b]", "[/b]"); InsertButton("Italic", "[i]", "[/i]"); InsertButton("Condition", "[if aff:trust >= 1]", "[else]Alternative text[/if]"); InsertButton("{player}", "{player_name}", "");
        tools.Children.Add(Ui.AsyncButton("Variable…", async () =>
        {
            var key = await PickVariable(); if (key is null) return;
            var start = Math.Min(narrative.SelectionStart, narrative.SelectionEnd); var end = Math.Max(narrative.SelectionStart, narrative.SelectionEnd);
            var text = narrative.Text ?? ""; var inserted = "{" + key + "}";
            narrative.Text = text[..start] + inserted + text[end..]; narrative.Focus(); narrative.CaretIndex = start + inserted.Length; Commit();
        }, "subtle"));
        Editor.Children.Add(Ui.Field("Dialogue", Ui.Stack(tools, narrative, Ui.Text("One page per line. Playtest to preview formatting and conditions.", 11, true))));
        var delivery = Ui.Select(["All pages, in order", "One random line"], s.SayItAll ? "All pages, in order" : "One random line", v => { s.SayItAll = v == "All pages, in order"; Edited(); });
        Editor.Children.Add(Ui.Field("Delivery", delivery));
        Editor.Children.Add(Ui.Field("Continue destination", Destination(s, s.DefaultNextId, v => s.DefaultNextId = v)));
        Editor.Children.Add(Ui.Text("Choices inherit this destination when their own target is unset.", 11, true));
        var heading = Ui.Text("PLAYER CHOICES", 11, true); heading.LetterSpacing = 1.2; heading.Margin = new Thickness(0,12,0,0); Editor.Children.Add(heading);
        for (var i = 0; i < s.Choices.Count; i++)
        {
            var choice = s.Choices[i]; var index = i;
            var label = EditText(choice.ButtonText, v => choice.ButtonText = v, true); label.MinHeight = 65;
            var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto") };
            top.Children.Add(Ui.Text($"Choice {i + 1}" + (choice.Requirements.Count > 0 ? " · conditional" : ""), 12, true));
            var up = Ui.Button("↑", () => Mutate(() => { if (index > 0) (s.Choices[index - 1], s.Choices[index]) = (s.Choices[index], s.Choices[index - 1]); }), "subtle"); up.IsEnabled = i > 0; AutomationProperties.SetName(up, $"Move choice {i + 1} up"); Grid.SetColumn(up, 1); top.Children.Add(up);
            var down = Ui.Button("↓", () => Mutate(() => { if (index < s.Choices.Count - 1) (s.Choices[index + 1], s.Choices[index]) = (s.Choices[index], s.Choices[index + 1]); }), "subtle"); down.IsEnabled = i < s.Choices.Count - 1; AutomationProperties.SetName(down, $"Move choice {i + 1} down"); Grid.SetColumn(down, 2); top.Children.Add(down);
            var remove = Ui.Button("×", () => Mutate(() => s.Choices.Remove(choice)), "subtle"); AutomationProperties.SetName(remove, $"Remove choice {i + 1}"); Grid.SetColumn(remove, 3); top.Children.Add(remove);
            var create = Ui.AsyncButton("New scene…", () => CreateSceneFromChoice(s, choice), "subtle"); AutomationProperties.SetName(create, $"Create scene from choice {i + 1}");
            var saveTemplate = Ui.AsyncButton("Save template…", () => SaveChoiceTemplate(choice), "subtle"); AutomationProperties.SetName(saveTemplate, $"Save choice {i + 1} template");
            Editor.Children.Add(Ui.Card(Ui.Stack(top, Ui.Field("Player response", label), Ui.Field("Go to", Destination(s, choice.NextStateId, v => choice.NextStateId = v, index)), Ui.Row(create, saveTemplate)), 12));
        }
        Editor.Children.Add(Ui.Button("＋  Add a player choice", () => Mutate(() => s.Choices.Add(new Choice())), "primary"));
        Editor.Children.Add(Ui.AsyncButton("Add choice from template…", () => ShowTemplates(true), "subtle"));
        Editor.Children.Add(Ui.Text("Tip: connect scenes by dragging an output dot onto another scene in the graph.", 11, true));
    }
    private void BuildLogicEditor(DialogueState s)
    {
        Editor.Children.Add(Ui.Field("Scene type", Ui.Select(["Dialogue", "Shop"], s.Type, v => { s.Type = v; Commit(); RefreshEditor(); })));
        if (s.Type == "Shop")
        {
            Editor.Children.Add(Ui.Field("Shop ID", EditText(s.ShopData.ShopId, v => s.ShopData.ShopId = v)));
            Editor.Children.Add(Ui.Text("Playtest logs shop visits. The exported shop ID is handled by your game.", 12, true));
        }
        Editor.Children.Add(Ui.Text("When leaving this scene", 17));
        Editor.Children.Add(Ui.Text("These actions run once per transition, after the chosen response's actions.", 12, true));
        AddActions(Editor, s.DefaultAfterActions);
        for (var i = 0; i < s.Choices.Count; i++)
        {
            var choice = s.Choices[i];
            var panel = new StackPanel { Spacing = 10 };
            panel.Children.Add(Ui.Text($"Choice {i + 1}: " + DialogueEngine.PlainText(choice.ButtonText), 15));
            panel.Children.Add(Ui.Field("When conditions fail", Ui.Select(["Disable", "Hide", "Always"], choice.Visibility.ToString(), v => { choice.Visibility = Enum.Parse<VisibilityMode>(v); Edited(); })));
            panel.Children.Add(Ui.Text("Disable = visible but locked · Hide = invisible · Always = ignore conditions", 11, true));
            panel.Children.Add(Ui.Text("Conditions · all must pass", 13));
            foreach (var requirement in choice.Requirements.ToArray())
            {
                var r = requirement;
                var type = Ui.Select(["Affinity", "Inventory"], r.Type, v => { r.Type = v; Commit(); RefreshEditor(); });
                var op = Ui.Select(["==", "!=", ">", "<", ">=", "<="], r.Operator, v => { r.Operator = v; Edited(); });
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,80") }; row.Children.Add(type); Grid.SetColumn(op, 2); row.Children.Add(op);
                panel.Children.Add(Ui.Card(Ui.Stack(row, Ui.Field("Variable ID", VariableInput(r.Type, r.TargetId, v => r.TargetId = v)), Ui.Field("Compare with (integer)", EditText(r.Value, v => r.Value = v)), Ui.Button("Remove condition", () => Mutate(() => choice.Requirements.Remove(r)), "subtle")), 10));
            }
            panel.Children.Add(Ui.Button("＋ Add condition", () => Mutate(() => choice.Requirements.Add(new Requirement { TargetId = "trust" })), "subtle"));
            panel.Children.Add(Ui.Text("After this response", 13));
            AddActions(panel, choice.AfterChoiceActions); Editor.Children.Add(Ui.Card(panel, 12));
        }
        if (s.Choices.Count == 0) Editor.Children.Add(Ui.Text("Add choices in Content to give a response conditions and actions.", 12, true));
        Editor.Children.Add(Ui.Text("Known variables: " + string.Join(", ", DialogueEngine.DiscoverVariables(project)), 11, true));
    }
    private void AddActions(StackPanel panel, List<DialogueAction> actions)
    {
        foreach (var action in actions.ToArray())
        {
            var a = action;
            var type = Ui.Select(["Affinity", "Inventory", "Event", "Script"], a.Type, v => { a.Type = v; Commit(); RefreshEditor(); });
            var group = Ui.Stack(Ui.Field("Action type", type), Ui.Field(a.Type is "Event" or "Script" ? "Event / method ID" : "Variable ID", a.Type is "Event" or "Script" ? EditText(a.TargetId, v => a.TargetId = v) : VariableInput(a.Type, a.TargetId, v => a.TargetId = v)));
            if (a.Type is "Event" or "Script") group.Children.Add(Ui.Field("Parameter", EditText(a.Parameter ?? "", v => a.Parameter = v)));
            else
            {
                var amount = new NumericUpDown { Value = a.Amount, Minimum = int.MinValue, Maximum = int.MaxValue, Increment = 1, FormatString = "0" };
                amount.ValueChanged += (_, _) => { if (amount.Value is { } value) { a.Amount = (int)value; Edited(); } }; group.Children.Add(Ui.Field("Amount to add (negative to subtract)", amount));
            }
            group.Children.Add(Ui.Button("Remove action", () => Mutate(() => actions.Remove(a)), "subtle")); panel.Children.Add(Ui.Card(group, 10));
        }
        panel.Children.Add(Ui.Button("＋ Add action", () => Mutate(() => actions.Add(new DialogueAction { TargetId = "trust", Amount = 1 })), "subtle"));
    }
    private void BuildNotesEditor(DialogueState s)
    {
        Editor.Children.Add(Ui.Text("Scene organisation", 17));
        Editor.Children.Add(Ui.Field("Scene ID", Ui.Row(Ui.Text(s.State, 13, true), Ui.AsyncButton("Rename…", RenameScene, "subtle"))));
        Editor.Children.Add(Ui.Button(s.State == project.Entry ? "◆  This is the story start" : "Set as story start", SetStart));
        Editor.Children.Add(Ui.Field("Writer's notes", EditText(s.Metadata.GetValueOrDefault("notes", ""), v => s.Metadata["notes"] = v, true)));
        Editor.Children.Add(Ui.Field("Tags (comma separated)", EditText(s.Metadata.GetValueOrDefault("tags", ""), v => s.Metadata["tags"] = v)));
        Editor.Children.Add(Ui.Text("Notes and tags are saved in scene metadata. They won't appear in dialogue.", 12, true));
        Editor.Children.Add(Ui.Row(Ui.Button("Duplicate scene", DuplicateScene), Ui.AsyncButton("Delete scene…", DeleteScene)));
        Editor.Children.Add(Ui.AsyncButton("Save scene template…", () => SaveSceneTemplate(s), "subtle"));
        Editor.Children.Add(Ui.AsyncButton("Scene & choice templates…", () => ShowTemplates(), "subtle"));
        Editor.Children.Add(Ui.AsyncButton("▶  Play from this scene", () => Play(true), "play"));
        AddSceneConnections(Editor, s);
    }
    private void RefreshScript()
    {
        var panel = C<StackPanel>("ScriptPanel"); panel.Children.Clear();
        panel.Children.Add(Ui.Text(project.ProjectName, 28)); panel.Children.Add(Ui.Text("A readable view of your story. Select any scene to edit it.", 13, true));
        foreach (var s in project.ListOfCharacterStates)
        {
            var stack = Ui.Stack(Ui.Button((s.State == project.Entry ? "◆  " : "") + s.Title, () => SelectScene(s, true), "subtle"), Ui.Text(s.Speaker + "  ·  " + s.State, 12, true), Ui.Text(DialogueEngine.PlainText(s.Text), 15));
            foreach (var c in s.Choices) stack.Children.Add(Ui.Text("↳  " + DialogueEngine.PlainText(c.ButtonText) + "  →  " + (c.NextStateId ?? s.DefaultNextId ?? "(unset)"), 12, true));
            stack.Children.Add(Ui.Text("Continue → " + (s.DefaultNextId ?? "(unset)"), 11, true)); panel.Children.Add(Ui.Card(stack, 20));
        }
    }
}
