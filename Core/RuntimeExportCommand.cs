namespace DialogueMachine.Core;

public static class RuntimeExportCommand
{
    public static int Run(string[] args, TextWriter output, CancellationToken cancellation = default)
    {
        try
        {
            if (args.Length < 2) throw new ArgumentException("Use runtime-export <project.json> [--seed <int32>] [--max-operations <1..10000>] [--output <handoff.json>].");
            var seed = 0; var limit = 1000; string? path = null; var seen = new HashSet<string>();
            for (var i = 2; i < args.Length; i++)
            {
                var option = args[i];
                if (!seen.Add(option) || ++i >= args.Length) throw new ArgumentException($"Duplicate option or missing value for ‘{option}’.");
                if (option == "--output") path = args[i];
                else if (option == "--seed" && int.TryParse(args[i], out var value)) seed = value;
                else if (option == "--max-operations" && int.TryParse(args[i], out value) && value is >= 1 and <= 10000) limit = value;
                else throw new ArgumentException($"Invalid option/value ‘{option} {args[i]}’. Seed is int32; operation limit is 1–10,000.");
            }
            cancellation.ThrowIfCancellationRequested();
            if (path is not null && Path.GetFullPath(path).Equals(Path.GetFullPath(args[1]), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new ArgumentException("Handoff output must differ from the authored project path.");
            var json = RuntimeExport.Serialize(RuntimeExport.Create(ProjectStore.Load(args[1]), seed, limit));
            cancellation.ThrowIfCancellationRequested();
            if (path is not null) ProjectStore.AtomicWrite(path, json);
            output.WriteLine(json); return 0;
        }
        catch (OperationCanceledException) { output.WriteLine("Runtime export cancelled."); return 5; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException or InvalidOperationException)
        { output.WriteLine(ex.Message); return 2; }
    }
}
