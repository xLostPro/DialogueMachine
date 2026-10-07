using System.Text.Json;

namespace DialogueMachine.Core;

public sealed record ReplayDiagnostic(string Code, string Message, string? ScenarioId = null, int? Step = null, string? SceneId = null, Guid? ChoiceId = null);
public sealed record ChoiceCoverage(string SceneId, Guid ChoiceId);
public sealed record CoverageReport(int VisitedScenes, int TotalScenes, int VisitedChoices, int TotalChoices,
    IReadOnlyList<string> Scenes, IReadOnlyList<ChoiceCoverage> Choices);
public sealed record ScenarioResult(string ScenarioId, string Name, int Seed, string StartingSceneId,
    IReadOnlyDictionary<string, int> NumericOverrides, IReadOnlyDictionary<string, string> TextOverrides, int MaxSteps, string Status, int ExecutedSteps,
    string? CurrentSceneId, StoryEnding? Ending, IReadOnlyDictionary<string, int> Variables,
    IReadOnlyDictionary<string, string> TextVariables, ulong RandomState, IReadOnlyList<TraceEntry> Trace,
    IReadOnlyList<ReplayDiagnostic> Diagnostics, CoverageReport Coverage)
{
    public bool Passed => Status == "Passed";
}

public static class ScenarioReplay
{
    public static List<ReplayDiagnostic> CheckDefinition(PlaytestScenario scenario)
    {
        var issues = new List<ReplayDiagnostic>();
        void Error(string message, int? step = null) => issues.Add(new("InvalidScenario", message, scenario.Id, step));
        if (string.IsNullOrWhiteSpace(scenario.Id) || string.IsNullOrWhiteSpace(scenario.Name)) Error("Scenario needs an ID and a name.");
        if (string.IsNullOrWhiteSpace(scenario.StartStateId) || scenario.StartStateId == "[EXIT]") Error("Choose an existing starting scene.");
        if (scenario.MaxSteps < 1 || scenario.MaxSteps > ScenarioStore.MaximumSteps) Error("Step limit must be between 1 and 10,000.");
        foreach (var (key, value) in scenario.NumericOverrides)
            if (!VariableStore.ValidNumericId(key) || key.StartsWith("inv:", StringComparison.Ordinal) && value < 0) Error($"Starting override ‘{key}’ needs a typed numeric ID and nonnegative inventory value.");
        foreach (var (key, value) in scenario.TextOverrides)
            if (string.IsNullOrWhiteSpace(key) || value is null) Error("Text overrides need an ID and a string value.");
        for (var i = 0; i < scenario.Steps.Count; i++)
        {
            var step = scenario.Steps[i];
            if (string.IsNullOrWhiteSpace(step.SceneId)) Error("Every step needs its expected scene ID. Record the path again.", i + 1);
            if (step.Kind is not ("Choice" or "Continue" or "Page" or "SetNumeric" or "SetText" or "Reroll" or "Refresh")) Error($"Unsupported replay operation ‘{step.Kind}’.", i + 1);
            if (string.IsNullOrWhiteSpace(step.Fingerprint) || !step.Fingerprint.StartsWith("v1:", StringComparison.Ordinal)) Error("Step needs a v1 semantic fingerprint. Record the path again in the editor.", i + 1);
            if (step.Kind == "Choice" && (step.ChoiceId is null || step.ChoiceId == Guid.Empty)) Error("A choice step needs its persisted choice GUID. Record the path again.", i + 1);
            if (step.Kind == "SetNumeric" && (string.IsNullOrWhiteSpace(step.Key) || step.Number is null)) Error("Numeric edit needs a key and integer value.", i + 1);
            if (step.Kind == "SetText" && (string.IsNullOrWhiteSpace(step.Key) || step.Text is null)) Error("Text edit needs a key and string value.", i + 1);
        }
        foreach (var a in scenario.Assertions)
        {
            if (a.Kind is not ("Numeric" or "Text" or "Ending" or "Event")) Error($"Unsupported assertion ‘{a.Kind}’.");
            if (a.Kind is "Numeric" or "Text" && string.IsNullOrWhiteSpace(a.Key)) Error("Variable assertions need a key.");
            if (a.Kind == "Numeric" && a.Number is null || a.Kind == "Text" && a.Text is null) Error("Variable assertions need an expected value.");
            if (a.Kind == "Ending" && string.IsNullOrWhiteSpace(a.SceneId)) Error("Ending assertions need the scene that exits.");
            if (a.Kind == "Event" && (a.EventType is not ("Event" or "Script" or "Shop") || string.IsNullOrWhiteSpace(a.Key) || a.Count < 0)) Error("Event assertions need Event, Script or Shop, a target ID and an optional nonnegative exact count.");
        }
        return issues;
    }
    public static Simulation Start(DialogueProject project, PlaytestScenario scenario, bool retainHistory = true)
        => new(project, scenario.StartStateId, scenario.Seed, scenario.NumericOverrides, scenario.TextOverrides, retainHistory: retainHistory);

    public static ScenarioResult Run(DialogueProject project, PlaytestScenario scenario, CancellationToken cancellation = default)
    {
        var diagnostics = new List<ReplayDiagnostic>(); Simulation? session = null; var executed = 0; var status = "Failed";
        void Error(string code, string message, int? step = null, string? sceneId = null, Guid? choiceId = null)
            => diagnostics.Add(new(code, message, scenario.Id, step, sceneId, choiceId));
        try
        {
            cancellation.ThrowIfCancellationRequested(); diagnostics.AddRange(CheckDefinition(scenario));
            foreach (var group in project.ListOfCharacterStates.GroupBy(s => s.State).Where(g => g.Count() > 1))
                Error("AmbiguousScene", $"Scene ID ‘{group.Key}’ occurs more than once. Rename duplicate scenes before replaying.", sceneId: group.Key);
            if (diagnostics.Count == 0)
            {
                // Story diagnostics remain structural; scenario/reference failures are a separate channel.
                foreach (var issue in DialogueEngine.Validate(project).Where(d => d.Severity == "Error")) Error("InvalidStory", issue.Message, sceneId: issue.StateId);
            }
            if (diagnostics.Count == 0)
            {
                cancellation.ThrowIfCancellationRequested(); session = Start(project, scenario, retainHistory: false);
                for (var i = 0; i < scenario.Steps.Count; i++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var step = scenario.Steps[i];
                    if (executed >= scenario.MaxSteps) { Error("StepLimit", $"Replay stopped at {scenario.MaxSteps} operations. Shorten the path or raise its bounded limit; cycles are not exhaustively verified.", i + 1, step.SceneId); break; }
                    if ((session.Current?.State ?? "[EXIT]") != step.SceneId)
                    {
                        var count = project.ListOfCharacterStates.Count(s => s.State == step.SceneId);
                        Error(count == 0 ? "MissingScene" : count > 1 ? "AmbiguousScene" : "UnexpectedScene", $"Step {i + 1} expects scene ‘{step.SceneId}’, but the run is at ‘{session.Current?.State ?? "[EXIT]"}’. Check preceding destinations or re-record the path.", i + 1, step.SceneId); break;
                    }
                    Choice? choice = null;
                    if (session.Current is null && (step.Kind is not ("SetNumeric" or "SetText") || session.Ending is null))
                    { Error("UnavailableStep", $"Step {i + 1} requires an active scene, but the conversation has ended. Re-record the path.", i + 1, step.SceneId); break; }
                    if (step.Kind == "Choice")
                    {
                        var matches = session.Current!.Choices.Where(c => c.InternalId == step.ChoiceId).ToList();
                        if (matches.Count != 1)
                        {
                            Error(matches.Count == 0 ? "MissingChoice" : "AmbiguousChoice", $"Step {i + 1}: response ‘{step.Label}’ ({step.ChoiceId}) in ‘{step.SceneId}’ {(matches.Count == 0 ? "was removed or has a new identity" : "has duplicate identities")}. Select the intended response and re-record the path.", i + 1, step.SceneId, step.ChoiceId); break;
                        }
                        choice = matches[0];
                    }
                    if (step.Fingerprint != (session.Current is null ? "v1:ended" : ScenarioStore.Fingerprint(session.Current, choice)))
                    {
                        Error("ChangedStep", $"Step {i + 1} in ‘{step.SceneId}’ has changed dialogue, delivery, destination or logic. Review the change and re-record this path.", i + 1, step.SceneId, step.ChoiceId); break;
                    }
                    try
                    {
                        switch (step.Kind)
                        {
                            case "Choice": session.Choose(choice!); break;
                            case "Page": if (!session.MorePages) throw new InvalidOperationException("The recorded page no longer exists."); session.NextPage(); break;
                            case "Continue": if (session.MorePages || !session.CanContinue) throw new InvalidOperationException("Continue is unavailable; finish pages or select an available response."); session.Continue(); break;
                            case "SetNumeric": session.SetVariable(step.Key!, step.Number!.Value); break;
                            case "SetText": session.SetTextVariable(step.Key!, step.Text!); break;
                            case "Reroll": if (session.Current!.SayItAll) throw new InvalidOperationException("This scene no longer uses random delivery."); session.RerollLine(); break;
                            case "Refresh": session.RefreshText(); break;
                        }
                        executed++;
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
                    { Error("UnavailableStep", $"Step {i + 1}: {ex.Message} Check starting values and conditions or re-record the path.", i + 1, step.SceneId, step.ChoiceId); break; }
                }
                if (diagnostics.Count == 0)
                {
                    if (scenario.RequireEnding && session.Ending is null) Error(session.Current is not null && !session.MorePages && session.AvailableChoices.Count == 0 && !session.CanContinue ? "DeadEnd" : "IncompleteReplay", "The saved sequence did not reach an ending. Record the remaining path or turn off Require ending for a checkpoint test.", sceneId: session.Current?.State);
                    foreach (var assertion in scenario.Assertions) CheckAssertion(session, assertion, Error);
                }
                status = diagnostics.Count == 0 ? "Passed" : "Failed";
            }
        }
        catch (OperationCanceledException) { status = "Cancelled"; Error("Cancelled", "Replay was cancelled; its partial trace is retained."); }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or InvalidDataException)
        { Error("InvalidConfiguration", ex.Message); }
        var trace = session?.Trace.ToArray() ?? [];
        return new(scenario.Id, scenario.Name, scenario.Seed, scenario.StartStateId, new Dictionary<string, int>(scenario.NumericOverrides), new Dictionary<string, string>(scenario.TextOverrides), scenario.MaxSteps, status, executed, session?.Current?.State, session?.Ending,
            session is null ? new Dictionary<string, int>() : new Dictionary<string, int>(session.Variables),
            session is null ? new Dictionary<string, string>() : new Dictionary<string, string>(session.TextVariables),
            session?.RandomState ?? unchecked((ulong)(long)scenario.Seed), trace, diagnostics, Coverage(project, trace));
    }
    private static void CheckAssertion(Simulation session, ScenarioAssertion a, Action<string, string, int?, string?, Guid?> error)
    {
        var met = a.Kind switch
        {
            "Numeric" => session.Variables.TryGetValue(a.Key, out var n) && n == a.Number,
            "Text" => session.TextVariables.TryGetValue(a.Key, out var t) && t == a.Text,
            "Ending" => session.Ending is { } ending && ending.SceneId == a.SceneId && (a.ChoiceId is null || ending.ChoiceId == a.ChoiceId),
            "Event" => EventMatches(session, a),
            _ => false
        };
        if (met) return;
        var expected = a.Kind switch { "Numeric" => $"{a.Key} = {a.Number}", "Text" => $"{a.Key} = ‘{a.Text}’", "Ending" => $"ending from ‘{a.SceneId}’ ({a.ChoiceId?.ToString() ?? "any response"})", _ => $"{a.EventType} ‘{a.Key}’ ({a.Parameter ?? "any parameter"}), count {a.Count?.ToString() ?? "at least one"}" };
        var actual = a.Kind switch { "Numeric" => session.Variables.TryGetValue(a.Key, out var v) ? v.ToString() : "missing variable", "Text" => session.TextVariables.GetValueOrDefault(a.Key, "missing variable"), "Ending" => session.Ending?.SceneId ?? "conversation still running", _ => EventCount(session, a).ToString() };
        error("AssertionFailed", $"Expected {expected}; actual: {actual}. Review the story actions or update the assertion.", null, session.Current?.State ?? session.Ending?.SceneId, null);
    }
    private static int EventCount(Simulation session, ScenarioAssertion a) => session.Trace.Count(t => t.Kind == "Integration" && t.EventType == a.EventType && t.EventId == a.Key && (a.Parameter is null || a.Parameter == t.Parameter));
    private static bool EventMatches(Simulation session, ScenarioAssertion a) { var count = EventCount(session, a); return a.Count is { } exact ? count == exact : count > 0; }
    public static CoverageReport Coverage(DialogueProject project, IEnumerable<TraceEntry> trace)
    {
        var entries = trace.ToList(); var known = project.ListOfCharacterStates.Select(s => s.State).ToHashSet(StringComparer.Ordinal);
        var allChoices = project.ListOfCharacterStates.SelectMany(s => s.Choices.Select(c => new ChoiceCoverage(s.State, c.InternalId))).ToHashSet();
        var scenes = entries.Where(t => t.Kind == "SceneVisit" && t.SceneId is not null && known.Contains(t.SceneId)).Select(t => t.SceneId!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var choices = entries.Where(t => t.Kind == "Choice" && t.ChoiceId is not null).Select(t => new ChoiceCoverage(t.SceneId!, t.ChoiceId!.Value)).Where(allChoices.Contains).Distinct().OrderBy(c => c.SceneId, StringComparer.Ordinal).ThenBy(c => c.ChoiceId).ToArray();
        return new(scenes.Length, project.ListOfCharacterStates.Count, choices.Length, project.ListOfCharacterStates.Sum(s => s.Choices.Count), scenes, choices);
    }
    public static CoverageReport Coverage(DialogueProject project, IEnumerable<ScenarioResult> results) => Coverage(project, results.SelectMany(r => r.Trace));
}

public sealed record ConfiguredDeadEnd(string SceneId, IReadOnlyDictionary<string, int> Variables, IReadOnlyList<string> Reasons);
public sealed record DeadEndReport(string ScenarioId, int ExploredConfigurations, bool Truncated, bool Cancelled,
    IReadOnlyList<ConfiguredDeadEnd> DeadEnds, IReadOnlyList<ReplayDiagnostic> Diagnostics);

public static class ScenarioAnalysis
{
    // Bounded branching over reachable runtime configurations. Never a proof over all cyclic paths.
    public static DeadEndReport Analyze(DialogueProject project, PlaytestScenario scenario, int maxConfigurations = 1000, CancellationToken cancellation = default)
    {
        if (maxConfigurations < 1 || maxConfigurations > 10000) throw new ArgumentOutOfRangeException(nameof(maxConfigurations));
        var deadEnds = new List<ConfiguredDeadEnd>(); var diagnostics = new List<ReplayDiagnostic>(); var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<Simulation>(); var explored = 0; var transitions = 0; var transitionLimit = maxConfigurations * 4; var truncated = false; var cancelled = false;
        void Enqueue(Simulation state)
        {
            if (state.Current is null) return;
            var key = JsonSerializer.Serialize(new { Scene = state.Current.State, state.RandomState, Variables = state.Variables.OrderBy(v => v.Key, StringComparer.Ordinal), Text = state.TextVariables.OrderBy(v => v.Key, StringComparer.Ordinal) });
            if (seen.Contains(key)) return;
            if (seen.Count >= maxConfigurations) { truncated = true; return; }
            seen.Add(key); pending.Enqueue(state);
        }
        try
        {
            cancellation.ThrowIfCancellationRequested();
            diagnostics.AddRange(ScenarioReplay.CheckDefinition(scenario));
            foreach (var d in DialogueEngine.Validate(project).Where(d => d.Severity == "Error")) diagnostics.Add(new("InvalidStory", d.Message, scenario.Id, SceneId: d.StateId));
            if (diagnostics.Count == 0) Enqueue(new Simulation(project, scenario.StartStateId, scenario.Seed, scenario.NumericOverrides, scenario.TextOverrides, false));
            while (pending.TryDequeue(out var state))
            {
                cancellation.ThrowIfCancellationRequested(); if (state.Current is null) continue;
                explored++;
                while (state.MorePages) { cancellation.ThrowIfCancellationRequested(); state.NextPage(); }
                var choices = state.AvailableChoices;
                if (choices.Count == 0)
                {
                    if (state.CanContinue) { if (transitions++ >= transitionLimit) { truncated = true; break; } state.Continue(); Enqueue(state); }
                    else deadEnds.Add(new(state.Current.State, new Dictionary<string, int>(state.Variables), state.Current.Choices.Select(c => { DialogueEngine.RequirementsMet(c, state.Variables, out var reason); return reason; }).ToArray()));
                }
                else foreach (var choice in choices)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (transitions++ >= transitionLimit) { truncated = true; break; }
                    var branch = state.Fork(); branch.Choose(choice); Enqueue(branch);
                }
            }
        }
        catch (OperationCanceledException) { cancelled = true; diagnostics.Add(new("Cancelled", "Configuration analysis cancelled.", scenario.Id)); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or InvalidDataException) { diagnostics.Add(new("InvalidConfiguration", ex.Message, scenario.Id)); }
        if (truncated) diagnostics.Add(new("AnalysisLimit", $"Reached the {maxConfigurations} configuration or {transitionLimit} transition budget. Unexplored branches/cycles remain; this is not exhaustive verification.", scenario.Id));
        return new(scenario.Id, explored, truncated, cancelled, deadEnds, diagnostics);
    }
}
