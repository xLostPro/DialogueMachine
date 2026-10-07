using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using DialogueMachine.Core;

namespace DialogueMachine.UI;

public sealed class GraphCanvas : Control
{
    private DialogueProject project = Templates.Blank();
    public DialogueProject Project
    {
        get => project;
        set { if (!ReferenceEquals(project, value)) ClearTextLayouts(); project = value; }
    }
    public DialogueState? Selected { get; private set; }
    private readonly HashSet<string> visitedScenes = new(StringComparer.Ordinal);
    private readonly HashSet<ChoiceCoverage> visitedChoices = [];
    public CoverageReport? Coverage { get; private set; }
    public void SetCoverage(CoverageReport? coverage)
    {
        Coverage = coverage; visitedScenes.Clear(); visitedChoices.Clear();
        if (coverage is not null) { visitedScenes.UnionWith(coverage.Scenes); visitedChoices.UnionWith(coverage.Choices); }
        RefreshVisual();
    }
    private readonly HashSet<DialogueState> selection = [];
    public IReadOnlyList<DialogueState> SelectedScenes => Project.ListOfCharacterStates.Where(selection.Contains).ToList();
    private DialogueState? keyboardScene;
    public event Action? SelectionChanged;
    public event Action? InteractionStarted;
    public event Action? Changed;
    public event Action<double>? ZoomChanged;
    public event Action? OverviewChanged;
    public double Zoom { get; private set; } = .85;
    private Vector offset = new(30, 30);
    private Point last;
    private bool panning, dragging;
    private Dictionary<DialogueState, Point>? moveOrigins;
    private readonly HashSet<Key> moveKeys = [];
    private Point moveStart;
    private Point? marqueeStart, marqueeEnd;
    private HashSet<DialogueState> marqueeSelection = [];
    private DialogueState? linkSource;
    private Choice? linkChoice;
    private Point linkEnd;
    internal GraphImagePlan? ExportPlan { get; init; }
    private readonly GraphTextCache textCache = new();
    private IBrush? paletteInk;
    internal int CachedTextLayouts => textCache.Count;
    internal int CachedTextCharacters => textCache.Characters;
    internal int RenderedNodes { get; private set; }
    internal int RenderedLinks { get; private set; }
    public void ClearTextLayouts() => textCache.Clear();
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { ClearTextLayouts(); base.OnDetachedFromVisualTree(e); }
    public GraphCanvas()
    {
        Focusable = true; ClipToBounds = true;
        LostFocus += (_, _) => { FinishInteraction(); RefreshVisual(); };
        GotFocus += (_, _) => { keyboardScene ??= Selected ?? Project.ListOfCharacterStates.FirstOrDefault(); RefreshVisual(); };
    }
    private const double NodeWidth = GraphGeometry.NodeWidth;
    private static double NodeHeight(DialogueState s) => GraphGeometry.NodeHeight(s);
    private static Rect BoundsOf(DialogueState s) => new(s.VisualX, s.VisualY, NodeWidth, NodeHeight(s));
    private Point World(Point p) => new((p.X - offset.X) / Zoom, (p.Y - offset.Y) / Zoom);
    public Point WorldToViewport(Point point) => new(point.X * Zoom + offset.X, point.Y * Zoom + offset.Y);
    public Rect ViewportWorld => new(World(new Point(0, 0)), new Size(Bounds.Width / Zoom, Bounds.Height / Zoom));
    public Rect StoryBounds => Project.ListOfCharacterStates.Count == 0 ? new Rect(0, 0, 1, 1) : Project.ListOfCharacterStates.Select(BoundsOf).Aggregate((a, b) => a.Union(b));
    private void RefreshVisual() { InvalidateVisual(); OverviewChanged?.Invoke(); }
    public void CentreWorld(Point point)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y)) return;
        offset = new(Bounds.Width / 2 - point.X * Zoom, Bounds.Height / 2 - point.Y * Zoom); RefreshVisual();
    }
    public void PanViewport(Key direction, bool farther = false)
    {
        var step = (farther ? 160 : 60) / Zoom;
        var delta = direction switch { Key.Left => new Vector(-step, 0), Key.Right => new Vector(step, 0), Key.Up => new Vector(0, -step), Key.Down => new Vector(0, step), _ => default };
        CentreWorld(ViewportWorld.Center + delta);
    }
    public void SetSelection(IEnumerable<DialogueState> scenes, DialogueState? active)
    {
        var available = Project.ListOfCharacterStates.ToHashSet();
        selection.Clear(); selection.UnionWith(scenes.Where(available.Contains));
        Selected = active is not null && selection.Contains(active) ? active : SelectedScenes.FirstOrDefault();
        if (keyboardScene is null || !Project.ListOfCharacterStates.Contains(keyboardScene)) keyboardScene = Selected;
        RefreshVisual();
    }
    private void Select(IEnumerable<DialogueState> scenes, DialogueState? active)
    {
        // Materialize before replacing the set; callers may enumerate the current selection.
        SetSelection(scenes.ToList(), active); SelectionChanged?.Invoke();
    }
    public string? ConnectionHighlight(DialogueState source, DialogueState target)
        => selection.Contains(source) ? "GraphSelection" : selection.Contains(target) ? "GraphIncoming" : null;
    public override void Render(DrawingContext dc)
    {
        base.Render(dc);
        if (!ReferenceEquals(paletteInk, Ui.Brush("Ink"))) { ClearTextLayouts(); paletteInk = Ui.Brush("Ink"); }
        RenderedNodes = RenderedLinks = 0;
        dc.FillRectangle(Ui.Brush("Base"), new Rect(Bounds.Size));
        if (ExportPlan is { } plan)
        {
            using (dc.PushTransform(Matrix.CreateTranslation(-plan.WorldBounds.X, -plan.WorldBounds.Y) * Matrix.CreateScale(plan.Scale, plan.Scale)))
                DrawStory(dc, null);
            return;
        }
        var grid = 28 * Zoom;
        for (var x = offset.X % grid; x < Bounds.Width; x += grid)
            for (var y = offset.Y % grid; y < Bounds.Height; y += grid) dc.DrawEllipse(Ui.Brush("GraphGrid"), null, new Point(x, y), .9, .9);
        using (dc.PushTransform(Matrix.CreateScale(Zoom, Zoom) * Matrix.CreateTranslation(offset)))
        {
            // Inflate for strokes, ports, arrowheads and keyboard-focus decoration at every zoom.
            DrawStory(dc, ViewportWorld.Inflate(8 + 2 / Zoom));
            if (linkSource is not null)
            {
                var index = linkChoice is null ? -1 : linkSource.Choices.IndexOf(linkChoice);
                var start = new Point(linkSource.VisualX + NodeWidth, linkSource.VisualY + (index < 0 ? NodeHeight(linkSource) - 17 : 135 + index * 34));
                Curve(dc, start, linkEnd, Ui.Brush("MintInk"), false);
            }
            if (marqueeStart is { } a && marqueeEnd is { } b)
                dc.DrawRectangle(null, new Pen(Ui.Brush("GraphSelection"), 2 / Zoom, new DashStyle([4, 3], 0)), new Rect(a, b));
        }
        if (Project.ListOfCharacterStates.Count == 0) DrawText(dc, "Your story starts with a scene.\nChoose Add scene to begin.", new(60, 100), 20, "Muted", 420, 100);
    }
    private void DrawStory(DrawingContext dc, Rect? viewport)
    {
        var destinations = new Dictionary<string, DialogueState>(StringComparer.Ordinal);
        foreach (var scene in Project.ListOfCharacterStates) if (scene.State != "[EXIT]") destinations.TryAdd(scene.State, scene);
        // Draw highlighted connections last so they stay visible at crossings.
        foreach (var highlighted in new[] { false, true })
            foreach (var s in Project.ListOfCharacterStates)
            {
                for (var i = 0; i < s.Choices.Count; i++) DrawLink(dc, s, string.IsNullOrWhiteSpace(s.Choices[i].NextStateId) ? s.DefaultNextId : s.Choices[i].NextStateId, new(s.VisualX + NodeWidth, s.VisualY + 135 + i * 34), false, destinations, highlighted, viewport);
                if (!string.IsNullOrWhiteSpace(s.DefaultNextId)) DrawLink(dc, s, s.DefaultNextId, new(s.VisualX + NodeWidth, s.VisualY + NodeHeight(s) - 17), true, destinations, highlighted, viewport);
            }
        foreach (var s in Project.ListOfCharacterStates)
            if (viewport is null || viewport.Value.Intersects(BoundsOf(s))) { DrawNode(dc, s); RenderedNodes++; }
    }
    private void DrawLink(DrawingContext dc, DialogueState source, string? targetId, Point start, bool fallback, Dictionary<string, DialogueState> destinations, bool highlighted, Rect? viewport)
    {
        if (targetId is null || !destinations.TryGetValue(targetId, out var target)) return;
        var highlight = ConnectionHighlight(source, target);
        if ((highlight is not null) != highlighted) return;
        var end = new Point(target.VisualX, target.VisualY + 35);
        if (viewport is not null && !ConnectionHull(start, end).Intersects(viewport.Value)) return;
        Curve(dc, start, end, Ui.Brush(highlight ?? "GraphLink"), fallback, highlighted); RenderedLinks++;
    }
    // A cubic is contained in the hull of its control points. Conservative bounds retain
    // curves crossing the viewport with both endpoints offscreen, including backward loops.
    internal static Rect ConnectionHull(Point a, Point b)
    {
        var length = Math.Max(65, Math.Abs(b.X - a.X) * .45);
        var left = Math.Min(Math.Min(a.X, b.X - length), b.X - 9);
        var right = Math.Max(a.X + length, b.X);
        return new Rect(left, Math.Min(a.Y, b.Y - 5), right - left, Math.Max(a.Y, b.Y + 5) - Math.Min(a.Y, b.Y - 5)).Inflate(4);
    }
    internal static StreamGeometry ConnectionCurve(Point a, Point b)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open()) { g.BeginFigure(a, false); var length = Math.Max(65, Math.Abs(b.X - a.X) * .45); g.CubicBezierTo(new(a.X + length, a.Y), new(b.X - length, b.Y), b); g.EndFigure(false); }
        return geometry;
    }
    private static void Curve(DrawingContext dc, Point a, Point b, IBrush brush, bool dashed, bool highlighted = false)
    {
        var geometry = ConnectionCurve(a, b);
        if (highlighted) dc.DrawGeometry(null, new Pen(Ui.Brush("Base"), 7), geometry);
        var pen = new Pen(brush, highlighted ? 3.5 : 1.5, dashed ? new DashStyle([4, 4], 0) : null);
        dc.DrawGeometry(null, pen, geometry);
        var arrow = new Pen(brush, highlighted ? 3.5 : 1.5);
        dc.DrawLine(arrow, new(b.X - 9, b.Y - 5), b); dc.DrawLine(arrow, new(b.X - 9, b.Y + 5), b);
    }
    private void DrawNode(DrawingContext dc, DialogueState s)
    {
        var rect = BoundsOf(s); var active = s == Selected; var included = selection.Contains(s); var isEntry = s.State == Project.Entry;
        dc.DrawRectangle(Ui.Brush("Panel"), new Pen(Ui.Brush(included ? "GraphSelection" : "Line"), active ? 3 : included ? 2 : 1), rect, 11, 11);
        dc.DrawRectangle(Ui.Brush(included ? "Selection" : "Raised"), null, new Rect(rect.X + 1, rect.Y + 1, rect.Width - 2, 64), 10, 10);
        if (ExportPlan is null && visitedScenes.Contains(s.State))
        {
            dc.DrawRectangle(Ui.Brush("MintInk"), null, new Rect(rect.X + 6, rect.Y + 70, 3, rect.Height - 80));
            DrawText(dc, "✓", new(rect.Right - 24, rect.Y + 12), 12, "MintInk", 18, 18);
        }
        if (IsFocused && s == keyboardScene) dc.DrawRectangle(null, new Pen(Ui.Brush("GraphIncoming"), 1.5 / Zoom, new DashStyle([3, 3], 0)), rect.Inflate(5), 13, 13);
        DrawText(dc, (isEntry ? "◆  START  /  " : "●  ") + s.Speaker.ToUpperInvariant(), new(rect.X + 16, rect.Y + 12), 10, isEntry ? "MintInk" : "Muted", 236, 16);
        DrawText(dc, s.Title, new(rect.X + 16, rect.Y + 31), 15, "Ink", 236, 24, true);
        var preview = DialogueEngine.PlainText(s.Text).Replace('\n', ' ');
        DrawText(dc, preview, new(rect.X + 16, rect.Y + 78), 12, "Muted", 238, 34);
        dc.DrawEllipse(Ui.Brush("Panel"), new Pen(Ui.Brush("Line"), 2), new(rect.X, rect.Y + 35), 5, 5);
        for (var i = 0; i < s.Choices.Count; i++)
        {
            if (ExportPlan is null && visitedChoices.Contains(new(s.State, s.Choices[i].InternalId)))
                dc.DrawEllipse(Ui.Brush("MintInk"), null, new(rect.X + 8, rect.Y + 135 + i * 34), 3, 3);
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
    private void DrawText(DrawingContext dc, string text, Point position, double size, string brush, double width, double height, bool bold = false)
    {
        textCache.Draw(dc, text, position, size, Ui.Brush(brush), width, height, bold);
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e); FinishInteraction(); InteractionStarted?.Invoke(); Focus();
        var point = e.GetPosition(this); var world = World(point); var props = e.GetCurrentPoint(this).Properties;
        var extend = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta | KeyModifiers.Shift)) != 0;
        var node = Project.ListOfCharacterStates.LastOrDefault(s => BoundsOf(s).Inflate(10).Contains(world));
        if (props.IsLeftButtonPressed && node is not null)
        {
            keyboardScene = node;
            if (extend)
            {
                var next = new HashSet<DialogueState>(selection);
                if (!next.Remove(node)) next.Add(node);
                Select(next, next.Contains(node) ? node : Selected);
            }
            else
            {
                Select(selection.Contains(node) ? SelectedScenes : [node], node);
                var index = (int)Math.Round((world.Y - node.VisualY - 135) / 34);
                var choicePort = index >= 0 && index < node.Choices.Count && Math.Abs(world.Y - node.VisualY - 135 - index * 34) < 13;
                var continuePort = Math.Abs(world.Y - node.VisualY - NodeHeight(node) + 17) < 13;
                if (Math.Abs(world.X - (node.VisualX + NodeWidth)) < 12 && (choicePort || continuePort))
                {
                    linkSource = node; linkChoice = choicePort ? node.Choices[index] : null; linkEnd = world;
                }
                else { dragging = true; StartMove(); moveStart = point; }
            }
        }
        else if (props.IsLeftButtonPressed && extend)
        {
            marqueeStart = marqueeEnd = world; marqueeSelection = new(selection);
        }
        else if (props.IsLeftButtonPressed || props.IsMiddleButtonPressed) panning = true;
        else return;
        last = point; e.Pointer.Capture(this); RefreshVisual(); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e); var p = e.GetPosition(this); var delta = p - last;
        if (dragging && moveOrigins is not null)
        {
            var total = (p - moveStart) / Zoom;
            foreach (var (scene, origin) in moveOrigins) { scene.VisualX = origin.X + total.X; scene.VisualY = origin.Y + total.Y; }
        }
        if (panning) offset += delta;
        if (linkSource is not null) linkEnd = World(p);
        if (marqueeStart is not null) marqueeEnd = World(p);
        last = p;
        if (dragging || panning || linkSource is not null || marqueeStart is not null) RefreshVisual();
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var connectionChanged = false;
        if (linkSource is not null)
        {
            var target = Project.ListOfCharacterStates.LastOrDefault(s => BoundsOf(s).Contains(World(e.GetPosition(this))));
            if (target is not null)
            {
                connectionChanged = (linkChoice is null ? linkSource.DefaultNextId : linkChoice.NextStateId) != target.State;
                if (linkChoice is null) linkSource.DefaultNextId = target.State; else linkChoice.NextStateId = target.State;
            }
        }
        if (marqueeStart is { } a)
        {
            var rectangle = new Rect(a, World(e.GetPosition(this)));
            var next = Project.ListOfCharacterStates.Where(s => marqueeSelection.Contains(s) || rectangle.Intersects(BoundsOf(s))).ToList();
            marqueeStart = marqueeEnd = null;
            Select(next, Selected);
        }
        FinishInteraction(); e.Pointer.Capture(null);
        if (connectionChanged) Changed?.Invoke(); RefreshVisual();
    }
    private void StartMove() => moveOrigins ??= SelectedScenes.ToDictionary(s => s, s => new Point(s.VisualX, s.VisualY));
    public void FinishInteraction(bool cancel = false)
    {
        var changed = moveOrigins?.Any(p => p.Key.VisualX != p.Value.X || p.Key.VisualY != p.Value.Y) == true;
        if (cancel && moveOrigins is not null)
            foreach (var (scene, origin) in moveOrigins) { scene.VisualX = origin.X; scene.VisualY = origin.Y; }
        moveOrigins = null; moveKeys.Clear(); dragging = false; linkSource = null; linkChoice = null; panning = false; marqueeStart = marqueeEnd = null;
        if (changed && !cancel) Changed?.Invoke(); RefreshVisual();
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { FinishInteraction(); base.OnPointerCaptureLost(e); }
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e) { SetZoom(Zoom * (e.Delta.Y > 0 ? 1.12 : .89), e.GetPosition(this)); e.Handled = true; }
    public void SetZoom(double value, Point? anchor = null)
    {
        var a = anchor ?? new Point(Bounds.Width / 2, Bounds.Height / 2); var w = World(a);
        Zoom = Math.Clamp(value, .2, 2); offset = new(a.X - w.X * Zoom, a.Y - w.Y * Zoom); ZoomChanged?.Invoke(Zoom); RefreshVisual();
    }
    public void Fit()
    {
        if (Project.ListOfCharacterStates.Count == 0 || Bounds.Width < 10) return;
        var all = Project.ListOfCharacterStates.Select(BoundsOf).Aggregate((a, b) => a.Union(b));
        Zoom = Math.Clamp(Math.Min((Bounds.Width - 90) / all.Width, (Bounds.Height - 130) / all.Height), .2, 1);
        offset = new((Bounds.Width - all.Width * Zoom) / 2 - all.X * Zoom, (Bounds.Height - all.Height * Zoom) / 2 - all.Y * Zoom);
        ZoomChanged?.Invoke(Zoom); RefreshVisual();
    }
    public void Centre(DialogueState s) => CentreWorld(new Point(s.VisualX + NodeWidth / 2, s.VisualY + NodeHeight(s) / 2));
    protected override void OnKeyDown(KeyEventArgs e)
    {
        var command = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt) && e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            FinishInteraction(); PanViewport(e.Key, e.KeyModifiers.HasFlag(KeyModifiers.Shift)); e.Handled = true;
        }
        else if (command && e.Key == Key.A)
        {
            FinishInteraction(); InteractionStarted?.Invoke(); Select(Project.ListOfCharacterStates, Selected); e.Handled = true;
        }
        else if (command && e.Key == Key.Tab && Project.ListOfCharacterStates.Count > 0)
        {
            FinishInteraction(); var scenes = Project.ListOfCharacterStates;
            var index = scenes.IndexOf(keyboardScene ?? Selected!); var step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1;
            keyboardScene = scenes[(index + step + scenes.Count) % scenes.Count]; Centre(keyboardScene); e.Handled = true;
        }
        else if (e.Key is Key.Space or Key.Enter && keyboardScene is not null)
        {
            FinishInteraction(); InteractionStarted?.Invoke();
            var next = new HashSet<DialogueState>(selection);
            if (e.Key == Key.Enter) { next.Clear(); next.Add(keyboardScene); }
            else if (!next.Remove(keyboardScene)) next.Add(keyboardScene);
            Select(next, next.Contains(keyboardScene) ? keyboardScene : Selected); e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (moveOrigins is not null || linkSource is not null || marqueeStart is not null) FinishInteraction(cancel: true);
            else { InteractionStarted?.Invoke(); Select([], null); }
            e.Handled = true;
        }
        else if (!command && selection.Count > 0 && e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            if (moveOrigins is null) { InteractionStarted?.Invoke(); StartMove(); }
            moveKeys.Add(e.Key);
            var step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 40 : 10;
            foreach (var scene in selection)
            {
                if (e.Key == Key.Left) scene.VisualX -= step; if (e.Key == Key.Right) scene.VisualX += step;
                if (e.Key == Key.Up) scene.VisualY -= step; if (e.Key == Key.Down) scene.VisualY += step;
            }
            RefreshVisual(); e.Handled = true;
        }
        base.OnKeyDown(e);
    }
    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (moveKeys.Remove(e.Key)) { if (moveKeys.Count == 0) FinishInteraction(); e.Handled = true; }
        base.OnKeyUp(e);
    }
}
