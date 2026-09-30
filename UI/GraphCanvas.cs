using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using DialogueMachine.Core;

namespace DialogueMachine.UI;

public sealed class GraphCanvas : Control
{
    public DialogueProject Project { get; set; } = Templates.Blank();
    public DialogueState? Selected { get; set; }
    public event Action<DialogueState>? SceneSelected;
    public event Action? Changed;
    public event Action<double>? ZoomChanged;
    public double Zoom { get; private set; } = .85;
    private Vector offset = new(30, 30);
    private Point last;
    private bool panning, moved;
    private DialogueState? dragging, linkSource;
    private Choice? linkChoice;
    private Point linkEnd;
    public GraphCanvas() { Focusable = true; ClipToBounds = true; }
    private const double NodeWidth = 270;
    private static double NodeHeight(DialogueState s) => 158 + s.Choices.Count * 34;
    private static Rect BoundsOf(DialogueState s) => new(s.VisualX, s.VisualY, NodeWidth, NodeHeight(s));
    private Point World(Point p) => new((p.X - offset.X) / Zoom, (p.Y - offset.Y) / Zoom);
    public Point WorldToViewport(Point point) => new(point.X * Zoom + offset.X, point.Y * Zoom + offset.Y);
    public override void Render(DrawingContext dc)
    {
        base.Render(dc);
        dc.FillRectangle(Ui.Brush("Base"), new Rect(Bounds.Size));
        var grid = 28 * Zoom;
        for (var x = offset.X % grid; x < Bounds.Width; x += grid)
            for (var y = offset.Y % grid; y < Bounds.Height; y += grid) dc.DrawEllipse(Ui.Brush("GraphGrid"), null, new Point(x, y), .9, .9);
        using (dc.PushTransform(Matrix.CreateScale(Zoom, Zoom) * Matrix.CreateTranslation(offset)))
        {
            foreach (var s in Project.ListOfCharacterStates)
            {
                for (var i = 0; i < s.Choices.Count; i++) DrawLink(dc, s, s.Choices[i].NextStateId ?? s.DefaultNextId, new(s.VisualX + NodeWidth, s.VisualY + 135 + i * 34), false);
                if (!string.IsNullOrWhiteSpace(s.DefaultNextId)) DrawLink(dc, s, s.DefaultNextId, new(s.VisualX + NodeWidth, s.VisualY + NodeHeight(s) - 17), true);
            }
            foreach (var s in Project.ListOfCharacterStates) DrawNode(dc, s);
            if (linkSource is not null)
            {
                var index = linkChoice is null ? -1 : linkSource.Choices.IndexOf(linkChoice);
                var start = new Point(linkSource.VisualX + NodeWidth, linkSource.VisualY + (index < 0 ? NodeHeight(linkSource) - 17 : 135 + index * 34));
                Curve(dc, start, linkEnd, Ui.Brush("MintInk"), false);
            }
        }
        if (Project.ListOfCharacterStates.Count == 0) DrawText(dc, "Your story starts with a scene.\nChoose Add scene to begin.", new(60, 100), 20, "Muted", 420, 100);
    }
    private void DrawLink(DrawingContext dc, DialogueState source, string? targetId, Point start, bool fallback)
    {
        var target = Project.ListOfCharacterStates.FirstOrDefault(s => s.State == targetId);
        if (target is null) return;
        Curve(dc, start, new(target.VisualX, target.VisualY + 35), Ui.Brush(source == Selected ? "Accent" : "Line"), fallback);
    }
    private static void Curve(DrawingContext dc, Point a, Point b, IBrush brush, bool dashed)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open()) { g.BeginFigure(a, false); var length = Math.Max(65, Math.Abs(b.X - a.X) * .45); g.CubicBezierTo(new(a.X + length, a.Y), new(b.X - length, b.Y), b); g.EndFigure(false); }
        dc.DrawGeometry(null, new Pen(brush, 2, dashed ? new DashStyle([4, 4], 0) : null), geometry);
        dc.DrawLine(new Pen(brush, 2), new(b.X - 7, b.Y - 4), b); dc.DrawLine(new Pen(brush, 2), new(b.X - 7, b.Y + 4), b);
    }
    private void DrawNode(DrawingContext dc, DialogueState s)
    {
        var rect = BoundsOf(s); var active = s == Selected; var isEntry = s.State == Project.Entry;
        dc.DrawRectangle(Ui.Brush("Panel"), new Pen(Ui.Brush(active ? "Accent" : "Line"), active ? 2 : 1), rect, 11, 11);
        dc.DrawRectangle(Ui.Brush(active ? "Selection" : "Raised"), null, new Rect(rect.X + 1, rect.Y + 1, rect.Width - 2, 64), 10, 10);
        DrawText(dc, (isEntry ? "◆  START  /  " : "●  ") + s.Speaker.ToUpperInvariant(), new(rect.X + 16, rect.Y + 12), 10, isEntry ? "MintInk" : "Muted", 236, 16);
        DrawText(dc, s.Title, new(rect.X + 16, rect.Y + 31), 15, "Ink", 236, 24, true);
        var preview = DialogueEngine.PlainText(s.Text).Replace('\n', ' ');
        DrawText(dc, preview, new(rect.X + 16, rect.Y + 78), 12, "Muted", 238, 34);
        dc.DrawEllipse(Ui.Brush("Panel"), new Pen(Ui.Brush("Line"), 2), new(rect.X, rect.Y + 35), 5, 5);
        for (var i = 0; i < s.Choices.Count; i++)
        {
            var y = rect.Y + 120 + i * 34;
            dc.DrawLine(new Pen(Ui.Brush("Line"), 1), new(rect.X + 12, y), new(rect.Right - 12, y));
            var c = s.Choices[i]; var prefix = c.Requirements.Count > 0 ? "◇  " : "↳  ";
            DrawText(dc, prefix + DialogueEngine.PlainText(c.ButtonText), new(rect.X + 16, y + 8), 11, "Ink", 231, 19);
            dc.DrawEllipse(Ui.Brush("MintInk"), new Pen(Ui.Brush("Panel"), 2), new(rect.Right, y + 15), 5, 5);
        }
        var bottom = rect.Bottom - 30;
        dc.DrawLine(new Pen(Ui.Brush("Line"), 1), new(rect.X + 12, bottom), new(rect.Right - 12, bottom));
        DrawText(dc, s.DefaultNextId == "[EXIT]" ? "○  End conversation" : string.IsNullOrWhiteSpace(s.DefaultNextId) ? "Continue destination not set" : "→  " + s.DefaultNextId, new(rect.X + 16, bottom + 8), 10, "Muted", 230, 18);
        dc.DrawEllipse(Ui.Brush("Muted"), new Pen(Ui.Brush("Panel"), 2), new(rect.Right, rect.Bottom - 17), 5, 5);
    }
    private static void DrawText(DrawingContext dc, string text, Point position, double size, string brush, double width, double height, bool bold = false)
    {
        using var layout = new TextLayout(text, new Typeface("Inter", weight: bold ? FontWeight.SemiBold : FontWeight.Normal), size, Ui.Brush(brush), textWrapping: TextWrapping.Wrap, textTrimming: TextTrimming.CharacterEllipsis, maxWidth: width, maxHeight: height);
        layout.Draw(dc, position);
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e); Focus(); var point = e.GetPosition(this); var world = World(point); var props = e.GetCurrentPoint(this).Properties;
        var node = Project.ListOfCharacterStates.LastOrDefault(s => BoundsOf(s).Inflate(10).Contains(world));
        if (props.IsLeftButtonPressed && node is not null)
        {
            Selected = node; SceneSelected?.Invoke(node);
            if (Math.Abs(world.X - (node.VisualX + NodeWidth)) < 12)
            {
                var index = (int)Math.Round((world.Y - node.VisualY - 135) / 34);
                linkSource = node; linkChoice = index >= 0 && index < node.Choices.Count && Math.Abs(world.Y - node.VisualY - 135 - index * 34) < 13 ? node.Choices[index] : null;
                linkEnd = world;
            }
            else dragging = node;
        }
        else if (props.IsLeftButtonPressed || props.IsMiddleButtonPressed) panning = true;
        last = point; moved = false; e.Pointer.Capture(this); InvalidateVisual(); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e); var p = e.GetPosition(this); var delta = p - last;
        if (dragging is not null) { dragging.VisualX += delta.X / Zoom; dragging.VisualY += delta.Y / Zoom; moved |= delta.X != 0 || delta.Y != 0; }
        if (panning) offset += delta;
        if (linkSource is not null) linkEnd = World(p);
        last = p;
        if (dragging is not null || panning || linkSource is not null) InvalidateVisual();
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (linkSource is not null)
        {
            var target = Project.ListOfCharacterStates.LastOrDefault(s => BoundsOf(s).Contains(World(e.GetPosition(this))));
            if (target is not null) { if (linkChoice is null) linkSource.DefaultNextId = target.State; else linkChoice.NextStateId = target.State; moved = true; }
        }
        dragging = null; linkSource = null; linkChoice = null; panning = false; e.Pointer.Capture(null);
        if (moved) Changed?.Invoke(); InvalidateVisual();
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { dragging = null; linkSource = null; linkChoice = null; panning = false; base.OnPointerCaptureLost(e); }
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e) { SetZoom(Zoom * (e.Delta.Y > 0 ? 1.12 : .89), e.GetPosition(this)); e.Handled = true; }
    public void SetZoom(double value, Point? anchor = null)
    {
        var a = anchor ?? new Point(Bounds.Width / 2, Bounds.Height / 2); var w = World(a);
        Zoom = Math.Clamp(value, .2, 2); offset = new(a.X - w.X * Zoom, a.Y - w.Y * Zoom); ZoomChanged?.Invoke(Zoom); InvalidateVisual();
    }
    public void Fit()
    {
        if (Project.ListOfCharacterStates.Count == 0 || Bounds.Width < 10) return;
        var all = Project.ListOfCharacterStates.Select(BoundsOf).Aggregate((a, b) => a.Union(b));
        Zoom = Math.Clamp(Math.Min((Bounds.Width - 90) / all.Width, (Bounds.Height - 130) / all.Height), .2, 1);
        offset = new((Bounds.Width - all.Width * Zoom) / 2 - all.X * Zoom, (Bounds.Height - all.Height * Zoom) / 2 - all.Y * Zoom);
        ZoomChanged?.Invoke(Zoom); InvalidateVisual();
    }
    public void Centre(DialogueState s) { offset = new(Bounds.Width / 2 - (s.VisualX + NodeWidth / 2) * Zoom, Bounds.Height / 2 - (s.VisualY + NodeHeight(s) / 2) * Zoom); InvalidateVisual(); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Selected is not null && e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            var step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 40 : 10;
            if (e.Key == Key.Left) Selected.VisualX -= step; if (e.Key == Key.Right) Selected.VisualX += step;
            if (e.Key == Key.Up) Selected.VisualY -= step; if (e.Key == Key.Down) Selected.VisualY += step;
            Changed?.Invoke(); InvalidateVisual(); e.Handled = true;
        }
        base.OnKeyDown(e);
    }
}
