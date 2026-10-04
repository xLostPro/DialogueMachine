using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace DialogueMachine.UI;

// Per-canvas LRU: bounded by layouts and source text, including full-story exports.
// Content and appearance are part of the key; evicted native layouts are disposed immediately.
internal sealed class GraphTextCache
{
    internal const int Capacity = 1024;
    internal const int CharacterBudget = 256 * 1024;
    private readonly record struct Key(string Text, double Size, IBrush Brush, double Width, double Height, bool Bold);
    private sealed record Entry(Key Key, TextLayout Layout);
    private readonly Dictionary<Key, LinkedListNode<Entry>> entries = [];
    private readonly LinkedList<Entry> recent = [];
    private int characters;
    public int Count => entries.Count;
    public int Characters => characters;
    public void Draw(DrawingContext context, string text, Point position, double size, IBrush brush, double width, double height, bool bold)
    {
        var key = new Key(text, size, brush, width, height, bold);
        if (entries.TryGetValue(key, out var node)) { recent.Remove(node); recent.AddLast(node); node.Value.Layout.Draw(context, position); return; }
        var layout = new TextLayout(text, new Typeface("Inter", weight: bold ? FontWeight.SemiBold : FontWeight.Normal), size, brush,
            textWrapping: TextWrapping.Wrap, textTrimming: TextTrimming.CharacterEllipsis, maxWidth: width, maxHeight: height);
        if (text.Length > CharacterBudget) { using (layout) layout.Draw(context, position); return; }
        while (entries.Count >= Capacity || characters + text.Length > CharacterBudget) RemoveFirst();
        entries.Add(key, recent.AddLast(new Entry(key, layout))); characters += text.Length;
        layout.Draw(context, position);
    }
    private void RemoveFirst()
    {
        var entry = recent.First!.Value; recent.RemoveFirst(); entries.Remove(entry.Key); characters -= entry.Key.Text.Length; entry.Layout.Dispose();
    }
    public void Clear() { while (recent.Count > 0) RemoveFirst(); }
}
