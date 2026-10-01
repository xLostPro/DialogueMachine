using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using DialogueMachine.Core;

namespace DialogueMachine.UI;

public sealed partial class MainWindow
{
    private async Task<string?> PickVariable()
    {
        var keys = DialogueEngine.DiscoverVariables(project).Concat(project.TextVariables.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var search = new TextBox { Name = "InsertVariableSearch", PlaceholderText = "Search variable IDs…", PlaceholderForeground = Ui.Brush("Muted") }; AutomationProperties.SetName(search, "Search variables to insert");
        var list = new ListBox { Name = "InsertVariableList" }; AutomationProperties.SetName(list, "Variables to insert");
        Window? window = null; var insert = Ui.Button("Insert", () => window!.Close(list.SelectedItem as string), "primary"); insert.IsDefault = true;
        void Filter() { var matches = keys.Where(k => k.Contains(search.Text ?? "", StringComparison.OrdinalIgnoreCase)).ToList(); list.ItemsSource = matches; list.SelectedItem = matches.FirstOrDefault(); insert.IsEnabled = matches.Count > 0; }
        search.TextChanged += (_, _) => Filter(); Filter();
        var content = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), Margin = new Thickness(24) };
        content.Children.Add(Ui.Text("Insert a variable", 24)); Grid.SetRow(search, 1); search.Margin = new Thickness(0,12); content.Children.Add(search);
        Grid.SetRow(list, 2); content.Children.Add(list); var buttons = Ui.Row(Ui.Button("Cancel", () => window!.Close(null)), insert); Grid.SetRow(buttons, 3); content.Children.Add(buttons);
        window = Ui.Dialog(this, "Insert a variable", content, 500, 480, preferences.Scale); window.Opened += (_, _) => search.Focus();
        return await window.ShowDialog<string?>(this);
    }
    private async Task ShowVariables()
    {
        Commit();
        var draft = ProjectStore.Clone(project);
        foreach (var key in DialogueEngine.DiscoverVariables(draft)) draft.InitialVariables.TryAdd(key, 0);
        var rows = new StackPanel { Spacing = 12 }; Window? window = null;
        var search = new TextBox { Name = "VariableSearch", PlaceholderText = "Search variable IDs…", PlaceholderForeground = Ui.Brush("Muted") };
        AutomationProperties.SetName(search, "Search variables");
        async Task Rename(string key, bool textVariable)
        {
            var next = await Ui.Prompt(window!, "Rename variable", $"Rename {key}. All its references will update when you apply changes.", key, preferences.Scale);
            if (next is null) return;
            try { VariableStore.Rename(draft, key, next.Trim(), textVariable); Populate(); }
            catch (ArgumentException ex) { await Ui.Ask(window!, "Cannot rename variable", ex.Message, "Close"); }
        }
        async Task ShowReferences(string key, bool textVariable)
        {
            var references = VariableStore.References(draft, key, textVariable);
            var list = Ui.Stack(Ui.Text(key, 22), Ui.Text($"{references.Count} {(references.Count == 1 ? "reference" : "references")}. Changes apply together when you choose Apply changes.", 12, true));
            foreach (var reference in references)
            {
                var scene = draft.ListOfCharacterStates.First(s => s.State == reference.StateId);
                list.Children.Add(Ui.Text(scene.Title + " [" + reference.StateId + "] · " + reference.Field, 13));
            }
            Window? details = null; list.Children.Add(Ui.Button("Close", () => details!.Close())); list.Margin = new Thickness(24);
            details = Ui.Dialog(window!, "Variable references", new ScrollViewer { Content = list }, 580, 450, preferences.Scale);
            await details.ShowDialog(window!);
        }
        void Card(string key, bool textVariable)
        {
            var references = VariableStore.References(draft, key, textVariable);
            Control input;
            if (textVariable) input = Ui.Input(draft.TextVariables[key], v => draft.TextVariables[key] = v);
            else
            {
                var number = new NumericUpDown { Value = draft.InitialVariables[key], Minimum = key.StartsWith("inv:") ? 0 : int.MinValue, Maximum = int.MaxValue, Increment = 1, FormatString = "0" };
                number.ValueChanged += (_, _) => { if (number.Value is { } value) draft.InitialVariables[key] = (int)value; };
                input = number;
            }
            var rename = Ui.AsyncButton("Rename…", () => Rename(key, textVariable), "subtle"); AutomationProperties.SetName(rename, "Rename " + key);
            var delete = Ui.Button("Delete", () => { VariableStore.Delete(draft, key, textVariable); Populate(); }, "subtle");
            AutomationProperties.SetName(delete, "Delete " + key); delete.IsEnabled = references.Count == 0;
            ToolTip.SetTip(delete, "Remove references before deleting a variable, or rename it to update them together.");
            var show = Ui.AsyncButton($"{references.Count} {(references.Count == 1 ? "reference" : "references")}", () => ShowReferences(key, textVariable), "subtle");
            AutomationProperties.SetName(show, "References for " + key); show.IsEnabled = references.Count > 0;
            var actions = new WrapPanel(); foreach (var button in new[] { show, rename, delete }) actions.Children.Add(button);
            rows.Children.Add(Ui.Card(Ui.Stack(Ui.Field(textVariable ? "{" + key + "}" : key, input), actions), 12));
        }
        void Populate()
        {
            rows.Children.Clear(); var filter = search.Text ?? "";
            rows.Children.Add(Ui.Text("Starting values", 19));
            rows.Children.Add(Ui.Text("Rename to update conditions, actions and inline text together. Referenced variables cannot be deleted. Changes apply together; Cancel keeps your story as it was.", 12, true));
            foreach (var key in draft.InitialVariables.Keys.Order(StringComparer.Ordinal).Where(k => k.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray()) Card(key, false);
            rows.Children.Add(Ui.AsyncButton("＋ Add numeric variable", async () =>
            {
                var id = await Ui.Prompt(window!, "Add a variable", "Use aff: for affinity or inv: for inventory, e.g. aff:trust or inv:coin.", "aff:", preferences.Scale);
                if (id is null) return; id = id.Trim();
                if (!VariableStore.ValidNumericId(id) || draft.InitialVariables.ContainsKey(id) || draft.TextVariables.ContainsKey(id)) { await Ui.Ask(window!, "Choose another variable ID", "Use a unique aff: or inv: ID with letters, digits, dots, underscores or hyphens.", "Close"); return; }
                draft.InitialVariables[id] = 0; search.Text = ""; Populate();
            }));
            rows.Children.Add(Ui.Text("Character & text substitutions", 19)); rows.Children.Add(Ui.Text("Insert {player_name} or another text ID in your dialogue or responses.", 12, true));
            foreach (var key in draft.TextVariables.Keys.Order(StringComparer.Ordinal).Where(k => k.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray()) Card(key, true);
            rows.Children.Add(Ui.AsyncButton("＋ Add text substitution", async () =>
            {
                var id = await Ui.Prompt(window!, "Add a text substitution", "Use a unique text ID without braces, e.g. town_name.", "", preferences.Scale);
                if (id is null) return; id = id.Trim();
                if (!VariableStore.ValidTextId(id) || draft.TextVariables.ContainsKey(id) || draft.InitialVariables.ContainsKey(id)) { await Ui.Ask(window!, "Choose another text ID", "Use a unique text ID made of letters, digits, dots, underscores or hyphens.", "Close"); return; }
                draft.TextVariables[id] = ""; search.Text = ""; Populate();
            }));
        }
        search.TextChanged += (_, _) => Populate(); Populate();
        var content = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), Margin = new Thickness(24) };
        content.Children.Add(Ui.Text("Variables & characters", 25));
        Grid.SetRow(search, 1); search.Margin = new Thickness(0,14,0,0); content.Children.Add(search);
        var scroll = new ScrollViewer { Content = rows, Margin = new Thickness(0,18) }; Grid.SetRow(scroll, 2); content.Children.Add(scroll);
        var buttons = Ui.Row(Ui.Button("Cancel", () => window!.Close(false)), Ui.Button("Apply changes", () => window!.Close(true), "primary")); Grid.SetRow(buttons, 3); content.Children.Add(buttons);
        window = Ui.Dialog(this, "Variables & characters", content, 650, 760, preferences.Scale);
        if (await window.ShowDialog<bool>(this))
        {
            // Every change to the draft becomes one history entry, including reference rewrites.
            Mutate(() => { var id = selected?.State; project = draft; selected = project.ListOfCharacterStates.FirstOrDefault(s => s.State == id); });
        }
    }
}
