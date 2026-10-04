using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using DialogueMachine.Core;

namespace DialogueMachine.UI;

public sealed class GraphMinimap : Control
{
    private GraphCanvas? graph;
    private bool navigating;
    public GraphCanvas? Graph
    {
        get => graph;
        set
        {
            if (graph is not null) graph.OverviewChanged -= InvalidateVisual;
            graph = value;
            if (graph is not null) graph.OverviewChanged += InvalidateVisual;
            InvalidateVisual();
        }
    }
    public GraphMinimap() { Focusable = true; ClipToBounds = true; GotFocus += (_, _) => InvalidateVisual(); LostFocus += (_, _) => InvalidateVisual(); }
    private (Rect World, double Scale, Vector Offset) Transform()
    {
        // Include the viewport so off-story panning is visible; keep the transform fixed during a drag.
        var world = navigationWorld ?? (graph is null ? new Rect(0, 0, 1, 1) : graph.StoryBounds.Union(graph.ViewportWorld)).Inflate(30);
        var scale = Math.Max(.000001, Math.Min(Math.Max(1, Bounds.Width - 12) / Math.Max(1, world.Width), Math.Max(1, Bounds.Height - 12) / Math.Max(1, world.Height)));
        return (world, scale, new Vector((Bounds.Width - world.Width * scale) / 2 - world.X * scale, (Bounds.Height - world.Height * scale) / 2 - world.Y * scale));
    }
    private Rect? navigationWorld;
    public Point WorldToMap(Point point) => MapPoint(point, Transform());
    private static Point MapPoint(Point point, (Rect World, double Scale, Vector Offset) transform)
        => new(point.X * transform.Scale + transform.Offset.X, point.Y * transform.Scale + transform.Offset.Y);
    private static Rect MapRect(Rect rect, (Rect World, double Scale, Vector Offset) transform)
        => new(MapPoint(rect.TopLeft, transform), new Size(rect.Width * transform.Scale, rect.Height * transform.Scale));
    public override void Render(DrawingContext context)
    {
        base.Render(context); context.FillRectangle(Ui.Brush("Base"), new Rect(Bounds.Size));
        if (graph is null) return;
        var transform = Transform(); // Calculate story bounds once per frame, not once per node/link.
        var destinations = new Dictionary<string, DialogueState>(StringComparer.Ordinal);
        foreach (var scene in graph.Project.ListOfCharacterStates) destinations.TryAdd(scene.State, scene);
        foreach (var scene in graph.Project.ListOfCharacterStates)
            foreach (var targetId in scene.Choices.Select(c => string.IsNullOrWhiteSpace(c.NextStateId) ? scene.DefaultNextId : c.NextStateId).Append(scene.DefaultNextId).Distinct())
                if (targetId is not null && destinations.TryGetValue(targetId, out var target))
                    context.DrawLine(new Pen(Ui.Brush("GraphLink"), 1), MapPoint(new Point(scene.VisualX + GraphGeometry.NodeWidth, scene.VisualY + 35), transform), MapPoint(new Point(target.VisualX, target.VisualY + 35), transform));
        var selected = graph.SelectedScenes.ToHashSet();
        foreach (var scene in graph.Project.ListOfCharacterStates)
        {
            var rect = MapRect(new Rect(scene.VisualX, scene.VisualY, GraphGeometry.NodeWidth, GraphGeometry.NodeHeight(scene)), transform);
            context.DrawRectangle(Ui.Brush(selected.Contains(scene) ? "GraphSelection" : "Raised"), new Pen(Ui.Brush(selected.Contains(scene) ? "GraphSelection" : "Muted"), scene == graph.Selected ? 2 : 1), rect, 1, 1);
        }
        context.DrawRectangle(null, new Pen(Ui.Brush("GraphIncoming"), 2), MapRect(graph.ViewportWorld, transform));
        if (IsFocused) context.DrawRectangle(null, new Pen(Ui.Brush("Ink"), 1, new DashStyle([3, 2], 0)), new Rect(Bounds.Size).Deflate(1));
    }
    private void Navigate(Point point)
    {
        if (graph is null) return;
        var t = Transform(); point = new Point(Math.Clamp(point.X, 0, Bounds.Width), Math.Clamp(point.Y, 0, Bounds.Height));
        graph.CentreWorld(new Point((point.X - t.Offset.X) / t.Scale, (point.Y - t.Offset.Y) / t.Scale));
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || graph is null) return;
        graph.FinishInteraction(); Focus(); navigationWorld = Transform().World; navigating = true; Navigate(e.GetPosition(this)); e.Pointer.Capture(this); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e) { base.OnPointerMoved(e); if (navigating) { Navigate(e.GetPosition(this)); e.Handled = true; } }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e); if (!navigating) return;
        Navigate(e.GetPosition(this)); navigating = false; navigationWorld = null; e.Pointer.Capture(null); InvalidateVisual(); e.Handled = true;
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { navigating = false; navigationWorld = null; InvalidateVisual(); base.OnPointerCaptureLost(e); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (graph is not null)
        {
            if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down) { graph.PanViewport(e.Key, e.KeyModifiers.HasFlag(KeyModifiers.Shift)); e.Handled = true; }
            else if (e.Key is Key.Enter or Key.Home) { graph.Fit(); e.Handled = true; }
            else if (e.Key == Key.Escape) { graph.Focus(); e.Handled = true; }
        }
        base.OnKeyDown(e);
    }
}
