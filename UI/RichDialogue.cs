using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using System.Text.RegularExpressions;

namespace DialogueMachine.UI;

internal static class RichDialogue
{
    public static TextBlock Create(string source, double fontSize = 21)
    {
        var block = Ui.Text("", fontSize); block.LineHeight = fontSize * 1.65; block.Inlines ??= new InlineCollection();
        var stack = new List<(string Tag, string Value)>();
        foreach (var part in Regex.Split(source, @"(\[/?(?:b|i|u|colour(?:=[^\]]*)?)\])", RegexOptions.IgnoreCase))
        {
            if (Regex.IsMatch(part, @"^\[(b|i|u|colour=[^\]]+)\]$", RegexOptions.IgnoreCase))
            {
                var tag = part[1..^1]; var pieces = tag.Split('=', 2); stack.Add((pieces[0].ToLowerInvariant(), pieces.ElementAtOrDefault(1) ?? "")); continue;
            }
            if (Regex.IsMatch(part, @"^\[/(b|i|u|colour)\]$", RegexOptions.IgnoreCase))
            {
                var tag = part[2..^1].ToLowerInvariant(); var index = stack.FindLastIndex(t => t.Tag == tag); if (index >= 0) stack.RemoveAt(index); continue;
            }
            var run = new Run(part);
            if (stack.Any(t => t.Tag == "b")) run.FontWeight = FontWeight.Bold;
            if (stack.Any(t => t.Tag == "i")) run.FontStyle = FontStyle.Italic;
            if (stack.Any(t => t.Tag == "u")) run.TextDecorations = TextDecorations.Underline;
            var colour = stack.LastOrDefault(t => t.Tag == "colour").Value;
            if (colour is not null && Color.TryParse(colour, out var parsed)) run.Foreground = new SolidColorBrush(parsed);
            block.Inlines.Add(run);
        }
        return block;
    }
}
