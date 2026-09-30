using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Input;

namespace DialogueMachine.UI;

internal static class Ui
{
    public static IBrush Brush(string key) => (IBrush)Application.Current!.Resources[key]!;
    public static TextBlock Text(string text, double size = 14, bool muted = false) => new() { Text = text, FontSize = size, Foreground = Brush(muted ? "Muted" : "Ink"), TextWrapping = TextWrapping.Wrap };
    public static Button Button(string label, Action action, string? style = null)
    {
        var b = new Button { Content = label }; if (style is not null) b.Classes.Add(style);
        AutomationProperties.SetName(b, label); b.Click += (_, _) => action(); return b;
    }
    public static Button AsyncButton(string label, Func<Task> action, string? style = null)
    {
        var b = new Button { Content = label }; if (style is not null) b.Classes.Add(style);
        AutomationProperties.SetName(b, label); b.Click += async (_, _) => await action(); return b;
    }
    public static StackPanel Stack(params Control[] children)
    {
        var s = new StackPanel { Spacing = 8 }; foreach (var c in children) s.Children.Add(c); return s;
    }
    public static StackPanel Row(params Control[] children)
    {
        var s = Stack(children); s.Orientation = Orientation.Horizontal; return s;
    }
    public static Border Card(Control child, double padding = 14) => new() { Child = child, Padding = new Thickness(padding), CornerRadius = new CornerRadius(9), Background = Brush("Raised"), BorderBrush = Brush("Line"), BorderThickness = new Thickness(1) };
    public static Control Field(string label, Control input)
    {
        AutomationProperties.SetName(input, label); return Stack(Text(label, 12, true), input);
    }
    public static ComboBox Select(IEnumerable<string> items, string selected, Action<string> changed)
    {
        var combo = new ComboBox { ItemsSource = items.ToList(), SelectedItem = selected };
        var previous = selected;
        combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is string s && s != previous) { previous = s; changed(s); } }; return combo;
    }
    public static TextBox Input(string value, Action<string> changed, bool multiline = false)
    {
        var input = new TextBox { Text = value, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, MinHeight = multiline ? 150 : 38 };
        var previous = value;
        input.TextChanged += (_, _) => { var current = input.Text ?? ""; if (current != previous) { previous = current; changed(current); } }; return input;
    }
    public static Window Dialog(Window owner, string title, Control content, double width = 540, double height = 500)
    {
        var window = new Window { Title = title + " · Dialogue Machine", Content = content, Width = width, Height = height, MinWidth = 380, MinHeight = 250, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        window.KeyDown += (_, e) => { if (e.Key == Key.Escape) { window.Close(); e.Handled = true; } };
        return window;
    }
    public static async Task<string?> Prompt(Window owner, string title, string label, string initial)
    {
        var input = new TextBox { Text = initial }; AutomationProperties.SetName(input, label);
        Window? window = null;
        window = Dialog(owner, title, Stack(Text(title, 22), Text(label, 13, true), input, Row(Button("Cancel", () => window!.Close(null)), Button("Continue", () => window!.Close(input.Text), "primary"))), 480, 240);
        ((StackPanel)window.Content!).Margin = new Thickness(24);
        window.Opened += (_, _) => { input.Focus(); input.SelectAll(); };
        return await window.ShowDialog<string?>(owner);
    }
    public static async Task<string?> Ask(Window owner, string title, string message, params string[] answers)
    {
        Window? w = null; var row = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var answer in answers) { var a = answer; var b = Button(a, () => w!.Close(a)); b.Margin = new Thickness(0,0,8,8); row.Children.Add(b); }
        w = Dialog(owner, title, Stack(Text(title, 21), Text(message, 14, true), row), 530, 260);
        ((StackPanel)w.Content!).Margin = new Thickness(24); return await w.ShowDialog<string?>(owner);
    }
}
