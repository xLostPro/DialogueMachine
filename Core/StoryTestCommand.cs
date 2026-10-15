using System.Text.Json;

namespace DialogueMachine.Core;

public static class StoryTestCommand
{
    public const string Help = "Dialogue Machine story tests\nvalidate <project.json>\nreplay <project.json> [--all | --scenario <id-or-name>] [--max-steps <1..10000>] [--analyze] [--max-configurations <1..10000>] [--trace <output.json>]\nExit: 0 passed, 1 invalid story, 2 input/usage/write error, 3 replay/assertion/dead-end failure, 4 bounded limit, 5 cancelled.\n";
    public static int Run(string[] args, TextWriter output, CancellationToken cancellation = default)
    {
        if (args.Length == 1 && args[0] is "--help" or "-h") { output.Write(Help); return 0; }
        object report; var exit = 2; string? tracePath = null;
        try
        {
            if (args.Length < 2 || args[0] is not ("validate" or "replay")) throw new ArgumentException("Use validate <project.json> or replay <project.json>. See --help.");
            var command = args[0]; string? selector = null; var all = false; var analyze = false; int? maxSteps = null; var maxConfigurations = 1000;
            var supplied = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 2; i < args.Length; i++)
            {
                var option = args[i];
                if (command == "validate" || !supplied.Add(option)) throw new ArgumentException($"Unsupported or duplicate option ‘{option}’.");
                string Value() => ++i < args.Length ? args[i] : throw new ArgumentException($"{option} needs a value.");
                int Limit() { var value = Value(); if (!int.TryParse(value, out var n) || n < 1 || n > 10000) throw new ArgumentException($"{option} must be an integer between 1 and 10,000."); return n; }
                switch (option)
                {
                    case "--all": all = true; break;
                    case "--scenario": selector = Value(); break;
                    case "--max-steps": maxSteps = Limit(); break;
                    case "--analyze": analyze = true; break;
                    case "--max-configurations": maxConfigurations = Limit(); break;
                    case "--trace": tracePath = Value(); break;
                    default: throw new ArgumentException($"Unknown option ‘{option}’. See --help.");
                }
            }
            if (all && selector is not null) throw new ArgumentException("Choose --all or --scenario, not both.");
            if (supplied.Contains("--max-configurations") && !analyze) throw new ArgumentException("--max-configurations requires --analyze.");
            cancellation.ThrowIfCancellationRequested(); var project = ProjectStore.Load(args[1]);
            if (tracePath is not null && Path.GetFullPath(tracePath).Equals(Path.GetFullPath(args[1]), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new ArgumentException("Trace output must use a different path from the authored project.");
            if (command == "validate")
            {
                var diagnostics = DialogueEngine.Validate(project); exit = diagnostics.Any(d => d.Severity == "Error") ? 1 : 0;
                report = new { SchemaVersion = 1, Command = command, ExitCode = exit, Diagnostics = diagnostics };
            }
            else
            {
                var scenarios = project.PlaytestScenarios.Where(s => selector is null || s.Id == selector || s.Name == selector).ToList();
                if (scenarios.Count == 0) throw new ArgumentException(selector is null ? "No saved scenarios. Create one using Story → Test scenarios in the editor." : $"No scenario matches ‘{selector}’. Use its saved ID or exact name.");
                if (selector is not null && scenarios.Count != 1) throw new ArgumentException($"Scenario selector ‘{selector}’ is ambiguous. Use a unique saved ID.");
                var results = new List<ScenarioResult>(); var analyses = new List<DeadEndReport>();
                foreach (var saved in scenarios)
                {
                    var scenario = ScenarioStore.Clone(saved); if (maxSteps is { } cap) scenario.MaxSteps = Math.Min(scenario.MaxSteps, cap);
                    results.Add(ScenarioReplay.Run(project, scenario, cancellation));
                    if (analyze) analyses.Add(ScenarioAnalysis.Analyze(project, scenario, maxConfigurations, cancellation));
                    if (cancellation.IsCancellationRequested) break;
                }
                exit = cancellation.IsCancellationRequested ? 5 : results.Any(r => r.Diagnostics.Any(d => d.Code == "StepLimit")) || analyses.Any(a => a.Truncated) ? 4 : results.Any(r => !r.Passed) || analyses.Any(a => a.DeadEnds.Count > 0 || a.Diagnostics.Count > 0) ? 3 : 0;
                report = new { SchemaVersion = 1, Command = command, ExitCode = exit, Results = results, Coverage = ScenarioReplay.Coverage(project, results), Analysis = analyses };
            }
            var json = JsonSerializer.Serialize(report, ProjectStore.Options);
            if (tracePath is not null) ProjectStore.AtomicWrite(tracePath, json);
            output.WriteLine(json); return exit;
        }
        catch (OperationCanceledException) { exit = 5; report = new { SchemaVersion = 1, ExitCode = exit, Diagnostics = new[] { new ReplayDiagnostic("Cancelled", "Story testing cancelled.") } }; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
        { exit = 2; report = new { SchemaVersion = 1, ExitCode = exit, Diagnostics = new[] { new ReplayDiagnostic("InputError", ex.Message) } }; }
        output.WriteLine(JsonSerializer.Serialize(report, ProjectStore.Options)); return exit;
    }
}
