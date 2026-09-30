using Avalonia;
using Avalonia.Controls;
using DialogueMachine.Core;
using System.Text.RegularExpressions;

namespace DialogueMachine.UI;

public sealed partial class MainWindow
{
    private async Task ShowVariables()
    {
        Commit();
        var numbers = DialogueEngine.DiscoverVariables(project).ToDictionary(k => k, k => project.InitialVariables.GetValueOrDefault(k));
        var texts = new Dictionary<string, string>(project.TextVariables);
        var rows = new StackPanel { Spacing = 12 }; Window? window = null;
        void Populate()
        {
            rows.Children.Clear(); rows.Children.Add(Ui.Text("Starting values", 19)); rows.Children.Add(Ui.Text("Conditions, actions and inline tags share these variables. Playtests start with these values.", 13, true));
            foreach (var key in numbers.Keys.Order().ToArray())
            {
                var k = key; var input = new NumericUpDown { Value = numbers[k], Minimum = k.StartsWith("inv:") ? 0 : int.MinValue, Maximum = int.MaxValue, Increment = 1, FormatString = "0" };
                input.ValueChanged += (_, _) => { if (input.Value is { } v) numbers[k] = (int)v; };
                rows.Children.Add(Ui.Field(k, input));
            }
            rows.Children.Add(Ui.AsyncButton("＋ Add numeric variable", async () =>
            {
                var id = await Ui.Prompt(window!, "Add a variable", "Use aff: for affinity or inv: for inventory, e.g. aff:trust or inv:coin.", "aff:");
                if (id is null) return;
                if (!Regex.IsMatch(id, @"^(aff|inv):[\w.\-]+$") || numbers.ContainsKey(id)) { await Ui.Ask(window!, "Choose another variable ID", "Use a unique ID with an aff: or inv: prefix. Letters, digits, dots, underscores and hyphens are supported.", "Close"); return; }
                numbers[id] = 0; Populate();
            }));
            rows.Children.Add(Ui.Text("Character & text substitutions", 19)); rows.Children.Add(Ui.Text("Insert {player_name} or another text ID in your dialogue.", 13, true));
            foreach (var key in texts.Keys.ToArray())
            {
                var k = key; rows.Children.Add(Ui.Field("{" + k + "}", Ui.Input(texts[k], v => texts[k] = v)));
            }
            rows.Children.Add(Ui.AsyncButton("＋ Add text substitution", async () =>
            {
                var id = await Ui.Prompt(window!, "Add a text substitution", "Use a unique text ID without braces, e.g. town_name.", ""); if (id is null) return;
                if (!Regex.IsMatch(id, @"^[\w.\-]+$") || texts.ContainsKey(id)) { await Ui.Ask(window!, "Choose another text ID", "Use a unique ID made of letters, digits, dots, underscores or hyphens.", "Close"); return; }
                texts[id] = ""; Populate();
            }));
        }
        Populate();
        var content = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(24) };
        content.Children.Add(Ui.Text("Variables & characters", 25));
        var scroll = new ScrollViewer { Content = rows, Margin = new Thickness(0,18) }; Grid.SetRow(scroll, 1); content.Children.Add(scroll);
        var buttons = Ui.Row(Ui.Button("Cancel", () => window!.Close(false)), Ui.Button("Apply starting values", () => window!.Close(true), "primary")); Grid.SetRow(buttons, 2); content.Children.Add(buttons);
        window = Ui.Dialog(this, "Variables & characters", content, 570, 680);
        if (await window.ShowDialog<bool>(this)) Mutate(() => { project.InitialVariables = numbers; project.TextVariables = texts; });
    }
}
