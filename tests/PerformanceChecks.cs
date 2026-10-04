using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DialogueMachine;
using DialogueMachine.Core;
using DialogueMachine.UI;

// Opt-in instrumentation, deliberately separate from correctness checks. No timing assertions.
internal static class PerformanceChecks
{
    public static DialogueProject Fixture(int count)
    {
        var project = new DialogueProject { ProjectName = $"Performance {count}", CharacterId = "performance", EntryStateId = "scene_00000" };
        project.InitialVariables["aff:trust"] = 1;
        var extra = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>("{\"FutureData\":{\"version\":2,\"label\":\"preserved\"}}")!;
        project.Extra = extra;
        for (var i = 0; i < count; i++)
        {
            string Id(int index) => $"scene_{index:D5}";
            var scene = Templates.Scene(Id(i), $"Chapter {i / 50:D3} · Scene {i:D5}", i % 2 == 0 ? "Ada" : "Mara",
                string.Join('\n', Enumerable.Repeat("[b]The harbour[/b] waits for {player_name}. [if aff:trust >= 1]Welcome back.[else]Who are you?[/if] A perf-marker on the old map leads across the water.", i % 20 == 0 ? 12 : 1)),
                i % 20 * 360 - 360, i / 20 * 520 - 520);
            scene.Metadata["notes"] = "Follow the perf-marker; preserve the cycle and the ending.";
            scene.Metadata["tags"] = i % 5 == 0 ? "checkpoint, harbour" : "harbour";
            scene.Extra = i % 10 == 0 ? extra : null;
            scene.DefaultNextId = i == count - 1 ? "[EXIT]" : Id(i + 1);
            var choices = new[] { 0, 1, 2, 3, 5 }[i % 5];
            for (var c = 0; c < choices; c++)
            {
                var bytes = new byte[16]; BitConverter.GetBytes(i).CopyTo(bytes, 0); BitConverter.GetBytes(c).CopyTo(bytes, 4);
                scene.Choices.Add(new Choice { InternalId = new Guid(bytes), ButtonText = $"Route {c}: follow the perf-marker at scene {i:D5}",
                    NextStateId = c == 0 ? null : c == 1 ? Id(Math.Max(0, i - 19)) : Id((i + c * 7) % count), Visibility = (VisibilityMode)(c % 3),
                    Requirements = c % 2 == 0 ? [] : [new() { TargetId = "trust", Value = "1" }], Extra = c == 0 ? extra : null });
            }
            project.ListOfCharacterStates.Add(scene);
        }
        for (var start = 0; start < count; start += 50)
            project.SceneGroups.Add(new SceneGroup { Id = $"group_{start:D5}", Name = $"Chapter {start / 50:D3}",
                Scenes = project.ListOfCharacterStates.Skip(start).Take(60).Select(s => new SceneReference { StateId = s.State }).ToList(), Extra = extra });
        project.SceneTemplates.Add(new() { Name = "Opening", Scene = ProjectStore.Clone(new DialogueProject { ListOfCharacterStates = [project.ListOfCharacterStates[0]] }).ListOfCharacterStates[0] });
        project.ChoiceTemplates.Add(new() { Name = "Continue", Choice = new() { InternalId = Guid.Empty, ButtonText = "Continue with {player_name}", Visibility = VisibilityMode.Always } });
        return project;
    }

    public static void Run(string[] args)
    {
        string Option(string name, string fallback) { var i = Array.IndexOf(args, name); return i < 0 ? fallback : args[i + 1]; }
        var output = Path.GetFullPath(Option("--output", "artifacts/performance")); Directory.CreateDirectory(output);
        var samples = int.Parse(Option("--samples", "5")); var historyCount = int.Parse(Option("--history", "20"));
        if (samples < 3 || samples > 100 || historyCount < 1 || historyCount > 100) throw new ArgumentException("Use 3–100 samples and 1–100 history entries.");
        var native = args.Contains("--native");
        var builder = AppBuilder.Configure<App>().WithInterFont();
        if (native) builder.UsePlatformDetect();
        else builder.UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
        builder.SetupWithoutStarting();
        void Execute()
        {
            var rows = new List<object>(); var memories = new List<object>(); var fixtures = new List<object>();
            foreach (var size in Option("--sizes", "100,1000,10000").Split(',').Select(int.Parse))
            {
                if (size is not (100 or 1000 or 10000)) throw new ArgumentException("Fixture sizes are 100, 1000 and 10000.");
                var path = Path.Combine(output, $"fixture-{size}.dialogue.json");
                var json = ProjectStore.Serialize(Fixture(size)); ProjectStore.AtomicWrite(path, json);
                fixtures.Add(new { Scenes = size, Bytes = Encoding.UTF8.GetByteCount(json), Sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))) });
                json = "";
                var window = new MainWindow(null, true) { Width = 1540, Height = 940 };
                // Measure the delayed commit explicitly, without a 700 ms timer racing a slow baseline frame.
                ((DispatcherTimer)typeof(MainWindow).GetField("editTimer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!).Interval = TimeSpan.FromHours(1);
                if (native) { window.Position = new PixelPoint(-5000, -5000); window.ShowInTaskbar = false; }
                window.Show(); Drain(window);
                T C<T>(string name) where T : Control => window.FindControl<T>(name)!;
                var graph = C<GraphCanvas>("Graph");
                void Frame()
                {
                    Drain(window);
                    foreach (var surface in new[] { (Window)window }.Concat(window.OwnedWindows.Where(w => w.IsVisible)))
                    {
                        using var bitmap = new RenderTargetBitmap(new PixelSize((int)surface.ClientSize.Width, (int)surface.ClientSize.Height)); bitmap.Render(surface);
                    }
                }
                void Press(string name) { Dispatcher.UIThread.RunJobs(DispatcherPriority.Normal); C<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
                void Open() { if (!window.OpenProjectAsync(path, false).GetAwaiter().GetResult()) throw new Exception("Fixture failed to open"); }
                void Measure(string operation, Action action, bool frame = true)
                {
                    action(); if (frame) Frame(); // Warm operation/JIT once, outside samples.
                    var times = new List<double>(); var allocations = new List<long>();
                    for (var i = 0; i < samples; i++)
                    {
                        var bytes = GC.GetTotalAllocatedBytes(true); var timer = Stopwatch.StartNew();
                        action(); if (frame) Frame(); timer.Stop();
                        times.Add(timer.Elapsed.TotalMilliseconds); allocations.Add(GC.GetTotalAllocatedBytes(true) - bytes);
                    }
                    var sorted = times.Order().ToArray();
                    rows.Add(new { Scenes = size, Operation = operation, MedianMs = sorted[sorted.Length / 2], P95Ms = sorted[(int)Math.Ceiling(sorted.Length * .95) - 1], MedianAllocatedBytes = allocations.Order().ElementAt(allocations.Count / 2), SamplesMs = times, AllocatedBytes = allocations });
                    Console.WriteLine($"{size,5} {operation,-26} {sorted[sorted.Length / 2],9:F2} ms {allocations.Order().ElementAt(allocations.Count / 2),13:N0} bytes");
                    File.WriteAllText(Path.Combine(output, "measurements.partial.json"), JsonSerializer.Serialize(rows, ProjectStore.Options));
                }
                Measure("load-and-frame", Open);
                graph.SetZoom(.85); graph.Centre(window.Project.ListOfCharacterStates[0]); Frame();
                memories.Add(Memory(size, "loaded-local-viewport", 0));
                if (args.Contains("--cache-probe"))
                {
                    var clearLayouts = typeof(GraphCanvas).GetMethod("ClearTextLayouts");
                    Measure("graph-local-cold-layouts", () => { clearLayouts?.Invoke(graph, null); using var bitmap = new RenderTargetBitmap(new PixelSize((int)graph.Bounds.Width, (int)graph.Bounds.Height)); bitmap.Render(graph); }, false);
                }
                Measure("graph-local-render", () => { using var bitmap = new RenderTargetBitmap(new PixelSize((int)graph.Bounds.Width, (int)graph.Bounds.Height)); bitmap.Render(graph); }, false);
                var tick = 0;
                TextBox Dialogue() => C<StackPanel>("EditorPanel").GetVisualDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "Dialogue text");
                Measure("typing-one-character", () => { var t = Dialogue(); t.Text += (++tick % 2 == 0 ? "x" : "y"); });
                Measure("typing-commit", () => { Dialogue().Text += "z"; Press("ContentTab"); });
                Measure("selection", () => { var items = C<ListBox>("SceneList").Items.Cast<object>().ToArray(); C<ListBox>("SceneList").SelectedItem = items[(++tick % 2) + 1]; });
                var groupItems = C<ListBox>("SceneList").Items.Cast<object>().Take(20).ToArray();
                C<ListBox>("SceneList").SelectedItems!.Clear(); foreach (var item in groupItems) C<ListBox>("SceneList").SelectedItems!.Add(item);
                graph.Focus();
                Measure("group-move-20", () => { graph.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Right }); graph.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.Right }); });
                Measure("outline-filter", () => C<TextBox>("SearchBox").Text = ++tick % 2 == 0 ? "checkpoint" : "harbour");
                C<TextBox>("SearchBox").Text = ""; Press("FindReplaceButton"); Drain(window);
                var finder = window.OwnedWindows.Single(w => w.IsVisible);
                Measure("find-dialog-query", () => finder.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "FindQuery").Text = ++tick % 2 == 0 ? "perf-marker" : "harbour");
                finder.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Close")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Measure("undo-redo-pair", () => { Press("UndoButton"); Press("RedoButton"); });
                Measure("pan-and-frame", () => graph.PanViewport(++tick % 2 == 0 ? Key.Left : Key.Right));
                Measure("zoom-and-frame", () => graph.SetZoom(++tick % 2 == 0 ? .85 : .95));
                graph.Fit(); Measure("graph-fit-render", () => { using var bitmap = new RenderTargetBitmap(new PixelSize((int)graph.Bounds.Width, (int)graph.Bounds.Height)); bitmap.Render(graph); }, false);
                graph.SetZoom(.85); graph.Centre(window.Project.ListOfCharacterStates[0]);
                Measure("save-and-frame", () => { if (!window.SaveProjectAsync().GetAwaiter().GetResult()) throw new Exception("Save failed"); });
                // Real editor text/commit path, retained after a full collection; separate from operation timings.
                memories.Add(Memory(size, "before-history", 0));
                for (var i = 0; i < historyCount; i++) { Dialogue().Text += $" history-{i}"; Press("ContentTab"); Frame(); }
                var measuredHistory = (ProjectHistory)typeof(MainWindow).GetField("history", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!;
                var committedText = (string)typeof(ProjectHistory).GetField("current", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(measuredHistory)!;
                if (!committedText.Contains($"history-{historyCount - 1}")) throw new Exception("The final typing cycle was not committed; discard these memory measurements.");
                memories.Add(Memory(size, "after-history", historyCount));
                File.WriteAllText(Path.Combine(output, "memory.partial.json"), JsonSerializer.Serialize(memories, ProjectStore.Options));
                window.Close(); Dispatcher.UIThread.RunJobs();
            }
            File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new {
                Environment = new { DateUtc = DateTimeOffset.UtcNow, OS = RuntimeInformation.OSDescription, Runtime = RuntimeInformation.FrameworkDescription, Architecture = RuntimeInformation.ProcessArchitecture.ToString(), LogicalProcessors = Environment.ProcessorCount, AvailableMemoryBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes, Avalonia = typeof(Window).Assembly.GetName().Version?.ToString(), Backend = native ? "native Windows / Skia RenderTargetBitmap" : "headless / real Skia RenderTargetBitmap", Window = "1540x940", Theme = "Dark", InterfaceScale = 1, samples, historyCount, Command = string.Join(' ', args) }, Fixtures = fixtures, Measurements = rows, Memory = memories }, ProjectStore.Options));
        }
        if (!native) Execute();
        else
        {
            using var cancellation = new CancellationTokenSource(); Exception? failure = null;
            Dispatcher.UIThread.Post(() => { try { Execute(); } catch (Exception ex) { failure = ex; } finally { cancellation.Cancel(); } });
            Dispatcher.UIThread.MainLoop(cancellation.Token);
            if (failure is not null) throw failure;
        }
    }
    private static void Drain(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    private static object Memory(int scenes, string phase, int historyEntries)
    {
        var managed = GC.GetTotalMemory(true); using var process = Process.GetCurrentProcess(); process.Refresh();
        return new { Scenes = scenes, Phase = phase, HistoryEntries = historyEntries, RetainedManagedBytes = managed, PrivateBytes = process.PrivateMemorySize64, WorkingSetBytes = process.WorkingSet64 };
    }
}
