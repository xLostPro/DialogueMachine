using Avalonia;
using Avalonia.Media.Imaging;
using DialogueMachine.Core;
using System.IO;

namespace DialogueMachine.UI;

public sealed record GraphImagePlan(Rect WorldBounds, PixelSize Pixels, double Scale, bool Reduced);

// UI-owned raster sizing/rendering. The live canvas is never measured, panned or attached here.
public static class GraphImageExport
{
    public const int MaxDimension = 16384;
    public const long MaxPixels = 32_000_000;
    public const double MinScale = .25;
    public const double Padding = 24;

    public static GraphImagePlan Plan(DialogueProject project, double scale = 1, bool fitWithinLimits = false)
    {
        if (!double.IsFinite(scale) || scale < MinScale || scale > 2) throw new ArgumentException("Choose a resolution from 0.25× to 2×.");
        if (project.ListOfCharacterStates.Count == 0) throw new InvalidOperationException("Add a scene before exporting a graph image.");
        var targets = new Dictionary<string, DialogueState>(StringComparer.Ordinal);
        Rect? bounds = null;
        foreach (var scene in project.ListOfCharacterStates)
        {
            if (!double.IsFinite(scene.VisualX) || !double.IsFinite(scene.VisualY)) throw new InvalidOperationException("A scene has an invalid position. Arrange the story before exporting.");
            var node = new Rect(scene.VisualX, scene.VisualY, GraphGeometry.NodeWidth, GraphGeometry.NodeHeight(scene));
            bounds = bounds?.Union(node) ?? node;
            if (scene.State != "[EXIT]") targets.TryAdd(scene.State, scene);
        }
        // Refuse extreme spans before constructing native path geometry.
        if (bounds!.Value.Width * MinScale > MaxDimension || bounds.Value.Height * MinScale > MaxDimension
            || bounds.Value.Width * MinScale * (bounds.Value.Height * MinScale) > MaxPixels)
            throw new InvalidOperationException("This graph is too large even at 0.25×. Move scenes closer together with Arrange, or export a readable script.");
        void Include(string? id, Point start)
        {
            if (id is null || !targets.TryGetValue(id, out var target)) return;
            var end = new Point(target.VisualX, target.VisualY + 35);
            bounds = bounds!.Value.Union(GraphCanvas.ConnectionCurve(start, end).Bounds).Union(new Rect(end.X - 9, end.Y - 5, 9, 10));
        }
        foreach (var scene in project.ListOfCharacterStates)
        {
            for (var i = 0; i < scene.Choices.Count; i++)
                Include(string.IsNullOrWhiteSpace(scene.Choices[i].NextStateId) ? scene.DefaultNextId : scene.Choices[i].NextStateId, new(scene.VisualX + GraphGeometry.NodeWidth, scene.VisualY + 135 + i * GraphGeometry.ChoiceSpacing));
            Include(scene.DefaultNextId, new(scene.VisualX + GraphGeometry.NodeWidth, scene.VisualY + GraphGeometry.NodeHeight(scene) - 17));
        }
        var world = bounds!.Value.Inflate(Padding);
        if (!double.IsFinite(world.Width) || !double.IsFinite(world.Height) || world.Width <= 0 || world.Height <= 0)
            throw new InvalidOperationException("The graph bounds are invalid. Arrange the story before exporting.");
        var requested = scale;
        bool Fits(double candidate) => Math.Ceiling(world.Width * candidate) <= MaxDimension && Math.Ceiling(world.Height * candidate) <= MaxDimension
            && Math.Ceiling(world.Width * candidate) * Math.Ceiling(world.Height * candidate) <= MaxPixels;
        if (!Fits(scale))
        {
            if (!fitWithinLimits) throw new InvalidOperationException("This graph exceeds the 16,384-pixel side or 32-megapixel limit. Choose Fit within limits to reduce resolution.");
            if (!Fits(MinScale)) throw new InvalidOperationException("This graph is too large even at 0.25×. Move scenes closer together with Arrange, or export a readable script.");
            var low = MinScale; var high = scale;
            for (var i = 0; i < 48; i++) { var mid = (low + high) / 2; if (Fits(mid)) low = mid; else high = mid; }
            scale = low;
        }
        return new(world, new PixelSize((int)Math.Ceiling(world.Width * scale), (int)Math.Ceiling(world.Height * scale)), scale, scale < requested);
    }

    public static void Save(DialogueProject project, string path, double scale = 1, bool fitWithinLimits = false)
    {
        // Validate before allocation or opening an output file; never truncate an existing export.
        var plan = Plan(project, scale, fitWithinLimits);
        var canvas = new GraphCanvas { Project = project, ExportPlan = plan };
        canvas.Measure(new Size(plan.Pixels.Width, plan.Pixels.Height));
        canvas.Arrange(new Rect(0, 0, plan.Pixels.Width, plan.Pixels.Height));
        using var bitmap = new RenderTargetBitmap(plan.Pixels, new Vector(96, 96));
        bitmap.Render(canvas);
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { bitmap.Save(temporary, PngBitmapEncoderOptions.Default); File.Move(temporary, full, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
