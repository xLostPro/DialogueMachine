using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using DialogueMachine.Core;
using System.Text.Json;

namespace DialogueMachine.UI;

public sealed partial class MainWindow
{
    private readonly Dictionary<string, ScenarioResult> scenarioResults = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DeadEndReport> scenarioAnalyses = new(StringComparer.Ordinal);
    public CoverageReport? TestCoverage => Graph.Coverage;
    private void ClearScenarioResults()
    {
        if (scenarioResults.Count == 0 && scenarioAnalyses.Count == 0 && Graph.Coverage is null) return;
        scenarioResults.Clear(); scenarioAnalyses.Clear(); SetCoverage(null);
    }
    private void SetCoverage(CoverageReport? coverage)
    {
        Graph.SetCoverage(coverage);
        C<TextBlock>("CoverageSummary").Text = coverage is null ? "" : $"✓ Tested {coverage.VisitedScenes}/{coverage.TotalScenes} scenes · {coverage.VisitedChoices}/{coverage.TotalChoices} choices";
    }
    private static string StepLabel(ReplayStep step, int index) => $"{index + 1}. {step.SceneId} · {step.Kind}" + (step.Kind == "Choice" ? " · " + DialogueEngine.PlainText(step.Label ?? step.ChoiceId?.ToString() ?? "") : step.Key is not null ? " · " + step.Key : "");
    private static string AssertionLabel(ScenarioAssertion a) => a.Kind switch
    {
        "Numeric" => $"{a.Key} = {a.Number}", "Text" => $"{a.Key} = ‘{a.Text}’", "Ending" => $"Ending from {a.SceneId}",
        _ => $"{a.EventType}: {a.Key} ({a.Parameter ?? "any"}) · {a.Count?.ToString() ?? "at least one"}"
    };

    public async Task ShowScenariosAsync()
    {
        if (!hasProject) return;
        Commit();
        var list = new ListBox { Name = "ScenarioList", MinWidth = 170 };
        AutomationProperties.SetName(list, "Saved test scenarios");
        list.ItemTemplate = new FuncDataTemplate<PlaytestScenario>((s, _) => s is null ? null : Ui.Stack(Ui.Text(s.Name), Ui.Text($"Seed {s.Seed} · {s.Steps.Count} steps", 11, true)));
        var editor = new StackPanel { Spacing = 12, Name = "ScenarioDraft" };
        var summary = Ui.Text("Choose or create a scenario. Save definitions with the project; results are transient.", 12, true); summary.Name = "ScenarioRunSummary";
        AutomationProperties.SetLiveSetting(summary, AutomationLiveSetting.Polite);
        var error = Ui.Text("", 12, true); error.Name = "ScenarioError";
        AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Polite);
        PlaytestScenario? draft = null; Window? window = null; CancellationTokenSource? running = null;
        var steps = new ListBox { Name = "ScenarioSteps", Height = 140 }; AutomationProperties.SetName(steps, "Saved replay steps");
        var assertions = new ListBox { Name = "ScenarioAssertions", Height = 120 }; AutomationProperties.SetName(assertions, "Outcome assertions");
        void RefreshSteps() => steps.ItemsSource = draft!.Steps.Select(StepLabel).ToArray();
        void RefreshAssertions() => assertions.ItemsSource = draft!.Assertions.Select(AssertionLabel).ToArray();
        void LoadDraft(PlaytestScenario source)
        {
            var editing = ScenarioStore.Clone(source); draft = editing; error.Text = ""; editor.Children.Clear();
            steps = new ListBox { Name = "ScenarioSteps", Height = 140 }; AutomationProperties.SetName(steps, "Saved replay steps");
            assertions = new ListBox { Name = "ScenarioAssertions", Height = 120 }; AutomationProperties.SetName(assertions, "Outcome assertions");
            var name = Ui.Input(editing.Name, value => editing.Name = value); name.Name = "ScenarioName";
            var startOptions = project.ListOfCharacterStates.Select(s => s.State).Distinct(StringComparer.Ordinal).ToList();
            if (!startOptions.Contains(editing.StartStateId)) startOptions.Insert(0, editing.StartStateId);
            var startPicker = Ui.Select(startOptions, editing.StartStateId, value => editing.StartStateId = value); startPicker.Name = "ScenarioStart";
            var seed = new NumericUpDown { Value = editing.Seed, Minimum = int.MinValue, Maximum = int.MaxValue, FormatString = "0" }; seed.Name = "ScenarioSeed";
            seed.ValueChanged += (_, _) => { if (seed.Value is { } value) editing.Seed = (int)value; };
            var limit = new NumericUpDown { Value = editing.MaxSteps, Minimum = 1, Maximum = ScenarioStore.MaximumSteps, FormatString = "0" }; limit.Name = "ScenarioMaxSteps";
            limit.ValueChanged += (_, _) => { if (limit.Value is { } value) editing.MaxSteps = (int)value; };
            var ending = new CheckBox { Content = "Require an ending", IsChecked = editing.RequireEnding, Name = "ScenarioRequireEnding" };
            ending.IsCheckedChanged += (_, _) => editing.RequireEnding = ending.IsChecked == true;
            editor.Children.Add(Ui.Stack(Ui.Text("Scenario configuration", 18), Ui.Field("Scenario name", name), Ui.Field("Starting scene ID", startPicker), Ui.Row(Ui.Field("Random seed", seed), Ui.Field("Replay step limit", limit)), ending));
            var overrides = new StackPanel { Spacing = 8 };
            foreach (var key in DialogueEngine.DiscoverVariables(project).Concat(editing.NumericOverrides.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                var k = key; var enabled = new CheckBox { Content = "Override " + k, IsChecked = editing.NumericOverrides.ContainsKey(k) };
                AutomationProperties.SetName(enabled, "Enable starting override " + k);
                var value = new NumericUpDown { Value = editing.NumericOverrides.GetValueOrDefault(k, project.InitialVariables.GetValueOrDefault(k)), Minimum = k.StartsWith("inv:", StringComparison.Ordinal) ? 0 : int.MinValue, Maximum = int.MaxValue, FormatString = "0", IsEnabled = enabled.IsChecked == true };
                AutomationProperties.SetName(value, "Starting " + k);
                enabled.IsCheckedChanged += (_, _) => { value.IsEnabled = enabled.IsChecked == true; if (value.IsEnabled) editing.NumericOverrides[k] = (int)(value.Value ?? 0); else editing.NumericOverrides.Remove(k); };
                value.ValueChanged += (_, _) => { if (value.IsEnabled && value.Value is { } n) editing.NumericOverrides[k] = (int)n; };
                overrides.Children.Add(Ui.Stack(enabled, value));
            }
            foreach (var key in project.TextVariables.Keys.Concat(editing.TextOverrides.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                var k = key; var enabled = new CheckBox { Content = "Override " + k + " (text)", IsChecked = editing.TextOverrides.ContainsKey(k) };
                AutomationProperties.SetName(enabled, "Enable starting text override " + k);
                var value = Ui.Input(editing.TextOverrides.GetValueOrDefault(k, project.TextVariables.GetValueOrDefault(k, "")), text => { if (enabled.IsChecked == true) editing.TextOverrides[k] = text; });
                value.IsEnabled = enabled.IsChecked == true; AutomationProperties.SetName(value, "Starting text " + k);
                enabled.IsCheckedChanged += (_, _) => { value.IsEnabled = enabled.IsChecked == true; if (value.IsEnabled) editing.TextOverrides[k] = value.Text ?? ""; else editing.TextOverrides.Remove(k); };
                overrides.Children.Add(Ui.Stack(enabled, value));
            }
            editor.Children.Add(new Expander { Header = "Starting overrides · unchecked values inherit project defaults", Content = overrides, IsExpanded = true });
            RefreshSteps(); RefreshAssertions();
            var record = Ui.AsyncButton("Record path…", async () =>
            {
                try
                {
                    var issues = ScenarioReplay.CheckDefinition(editing); // Old paths can be replaced even if their references are stale.
                    if (issues.Any(d => d.Step is null)) throw new ArgumentException(issues.First(d => d.Step is null).Message);
                    if (DialogueEngine.Validate(project).Any(d => d.Severity == "Error")) throw new ArgumentException("Resolve structural story errors before recording. Open Story diagnostics after closing this manager.");
                    var player = new PlaytestWindow(project, editing, preferences.Scale, true);
                    var session = await player.ShowDialog<Simulation?>(window!);
                    if (session is null) return;
                    editing.Steps = JsonSerializer.Deserialize<List<ReplayStep>>(JsonSerializer.Serialize(session.ReplaySteps, ProjectStore.Options), ProjectStore.Options)!;
                    RefreshSteps(); error.Text = "Recorded path is in this draft. Save scenario to retain it.";
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { error.Text = ex.Message; }
            }); record.Name = "RecordScenarioPath";
            var removeStep = Ui.Button("Remove selected step", () => { if (steps.SelectedIndex is var i && i >= 0) { editing.Steps.RemoveAt(i); RefreshSteps(); } }); removeStep.Name = "RemoveScenarioStep";
            var pathButtons = new WrapPanel(); pathButtons.Children.Add(record); pathButtons.Children.Add(removeStep);
            editor.Children.Add(Ui.Stack(Ui.Text("Replay path", 18), Ui.Text("Record by playing from this configuration. Back trims the recorded path. References detect changed scenes, responses and logic; re-record after reviewing a stale failure.", 12, true), steps, pathButtons));
            var addAssertion = Ui.AsyncButton("Add assertion…", async () => { var a = new ScenarioAssertion(); if (await EditAssertion(window!, a)) { editing.Assertions.Add(a); RefreshAssertions(); } }); addAssertion.Name = "AddScenarioAssertion";
            var editAssertion = Ui.AsyncButton("Edit assertion…", async () => { if (assertions.SelectedIndex is var i && i >= 0) { var copy = JsonSerializer.Deserialize<ScenarioAssertion>(JsonSerializer.Serialize(editing.Assertions[i], ProjectStore.Options), ProjectStore.Options)!; if (await EditAssertion(window!, copy)) { editing.Assertions[i] = copy; RefreshAssertions(); } } });
            var removeAssertion = Ui.Button("Remove assertion", () => { if (assertions.SelectedIndex is var i && i >= 0) { editing.Assertions.RemoveAt(i); RefreshAssertions(); } });
            var assertionButtons = new WrapPanel(); foreach (var b in new[] { addAssertion, editAssertion, removeAssertion }) assertionButtons.Children.Add(b);
            editor.Children.Add(Ui.Stack(Ui.Text("Expected outcome", 18), assertions, assertionButtons));
        }
        list.SelectionChanged += (_, _) => { if (list.SelectedItem is PlaytestScenario s) LoadDraft(s); };
        void Reload(string? id)
        {
            list.ItemsSource = project.PlaytestScenarios.ToArray(); list.SelectedItem = project.PlaytestScenarios.FirstOrDefault(s => s.Id == id) ?? project.PlaytestScenarios.FirstOrDefault();
            if (list.SelectedItem is null) { draft = null; editor.Children.Clear(); }
        }
        Button Named(Button b, string name) { b.Name = name; return b; }
        var create = Named(Ui.Button("New scenario", () => { list.SelectedItem = null; LoadDraft(new() { StartStateId = selected?.State ?? project.Entry }); }), "NewScenario");
        var save = Named(Ui.Button("Save scenario", () =>
        {
            if (draft is null) return;
            try { var copy = ScenarioStore.Clone(draft); ScenarioStore.Save(ProjectStore.Clone(project), copy); Mutate(() => ScenarioStore.Save(project, copy)); Reload(copy.Id); error.Text = "Saved definition. Save the project file to persist it on disk; close to Undo/Redo."; }
            catch (ArgumentException ex) { error.Text = ex.Message; }
        }, "primary"), "SaveScenario");
        var duplicate = Named(Ui.Button("Duplicate", () => { if (list.SelectedItem is PlaytestScenario s) { PlaytestScenario? copy = null; Mutate(() => copy = ScenarioStore.Duplicate(project, s)); Reload(copy!.Id); } }), "DuplicateScenario");
        var delete = Named(Ui.Button("Delete", () => { if (list.SelectedItem is PlaytestScenario s) { Mutate(() => project.PlaytestScenarios.Remove(s)); Reload(null); } }), "DeleteScenario");
        var cancelRun = Named(Ui.Button("Cancel run", () => running?.Cancel()), "CancelScenarioRun"); cancelRun.IsEnabled = false;
        Button? run = null, runAll = null, view = null, export = null;
        async Task Run(bool all)
        {
            if (running is not null || !all && draft is null) return;
            var definitions = all ? project.PlaytestScenarios.Select(ScenarioStore.Clone).ToArray() : new[] { ScenarioStore.Clone(draft!) };
            if (definitions.Length == 0) { error.Text = "Save at least one scenario to run all."; return; }
            var storySnapshot = ProjectStore.Clone(project); var cts = new CancellationTokenSource(); running = cts;
            var activeProject = project;
            editor.IsEnabled = list.IsEnabled = create.IsEnabled = save.IsEnabled = duplicate.IsEnabled = delete.IsEnabled = run!.IsEnabled = runAll!.IsEnabled = false; cancelRun.IsEnabled = true;
            summary.Text = "Running replay and bounded configuration checks…";
            try
            {
                var batch = await Task.Run(() =>
                {
                    var outcomes = new List<(ScenarioResult Result, DeadEndReport Analysis)>();
                    foreach (var definition in definitions)
                    {
                        outcomes.Add((ScenarioReplay.Run(storySnapshot, definition, cts.Token), ScenarioAnalysis.Analyze(storySnapshot, definition, cancellation: cts.Token)));
                        if (cts.IsCancellationRequested) break;
                    }
                    return outcomes.ToArray();
                });
                if (!ReferenceEquals(project, activeProject) || !window!.IsVisible) return;
                foreach (var item in batch) { scenarioResults[item.Result.ScenarioId] = item.Result; scenarioAnalyses[item.Result.ScenarioId] = item.Analysis; }
                var coverage = ScenarioReplay.Coverage(project, scenarioResults.Values); SetCoverage(coverage);
                summary.Text = $"{batch.Count(b => b.Result.Passed)}/{batch.Length} replays passed · {coverage.VisitedScenes}/{coverage.TotalScenes} scenes · {coverage.VisitedChoices}/{coverage.TotalChoices} choices. " +
                    $"{batch.Sum(b => b.Analysis.DeadEnds.Count)} configuration dead ends; {batch.Count(b => b.Analysis.Truncated)} analyses reached their limit. Cyclic paths are not exhaustively verified.";
                error.Text = string.Join("\n", batch.SelectMany(b => b.Result.Diagnostics.Concat(b.Analysis.Diagnostics)).Take(4).Select(d => d.Message));
                if (batch.Any(b => b.Analysis.DeadEnds.Count > 0)) error.Text += "\nView results for each dead-end configuration and unmet requirements.";
            }
            finally
            {
                running = null; cts.Dispose(); cancelRun.IsEnabled = false;
                editor.IsEnabled = list.IsEnabled = create.IsEnabled = save.IsEnabled = duplicate.IsEnabled = delete.IsEnabled = run!.IsEnabled = runAll!.IsEnabled = true;
            }
        }
        run = Named(Ui.AsyncButton("Run draft", () => Run(false), "play"), "RunScenario");
        runAll = Named(Ui.AsyncButton("Run all saved", () => Run(true)), "RunAllScenarios");
        view = Named(Ui.AsyncButton("View results…", async () =>
        {
            var rows = scenarioResults.Values.SelectMany(r => new[] { $"{r.Name} · {r.Status} · seed {r.Seed} · {r.ExecutedSteps} operations" }
                .Concat(new[] { $"Observed coverage: {r.Coverage.VisitedScenes}/{r.Coverage.TotalScenes} scenes · {r.Coverage.VisitedChoices}/{r.Coverage.TotalChoices} responses", "Final numeric values: " + string.Join(", ", r.Variables.Select(v => v.Key + " = " + v.Value)), "Final text values: " + string.Join(", ", r.TextVariables.Select(v => v.Key + " = " + v.Value)), "Ending: " + (r.Ending?.SceneId ?? "still running at " + r.CurrentSceneId) })
                .Concat(r.Diagnostics.Select(d => d.Code + ": " + d.Message))
                .Concat(scenarioAnalyses.GetValueOrDefault(r.ScenarioId)?.DeadEnds.Select(d => "CONFIGURATION DEAD END · " + d.SceneId + " · " + string.Join(", ", d.Variables.Select(v => v.Key + "=" + v.Value)) + " · " + string.Join("; ", d.Reasons)) ?? [])
                .Concat(scenarioAnalyses.GetValueOrDefault(r.ScenarioId)?.Diagnostics.Select(d => d.Code + ": " + d.Message) ?? [])
                .Concat(r.Trace.Select(t => $"{t.Sequence}. {t.Kind} · {t.SceneId ?? "[EXIT]"}" + (t.Destination is null ? "" : " → " + t.Destination) + (t.Text is null ? "" : " · " + t.Text) + (t.Key is null ? "" : $" · {t.Key}: {t.Before} → {t.After}") + (t.EventType is null ? "" : $" · {t.EventType}: {t.EventId}({t.Parameter})")))).ToArray();
            var trace = new ListBox { ItemsSource = rows, Name = "ScenarioTrace" }; AutomationProperties.SetName(trace, "Scenario results and structured trace");
            trace.ItemTemplate = new FuncDataTemplate<string>((text, _) => Ui.Text(text ?? "", 12));
            var content = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(20), RowSpacing = 10 };
            content.Children.Add(Ui.Text("Replay results & configuration analysis", 22)); Grid.SetRow(trace, 1); content.Children.Add(trace);
            Window? results = null; var close = Ui.Button("Close", () => results!.Close()); Grid.SetRow(close, 2); content.Children.Add(close);
            results = Ui.Dialog(window!, "Scenario results", content, 840, 600, preferences.Scale); await results.ShowDialog(window!);
        }), "ViewScenarioResults");
        export = Named(Ui.AsyncButton("Export results…", async () =>
        {
            var path = await projectFilePicker.ExportAsync(window!, "Export scenario results", "scenario-results.json", "json"); if (path is null) return;
            try
            {
                if (filePath is not null && ProjectFiles.SamePath(path, filePath)) throw new System.IO.IOException("Choose a separate results file from the authored project.");
                ProjectStore.AtomicWrite(path, JsonSerializer.Serialize(new { SchemaVersion = 1, Results = scenarioResults.Values.ToArray(), Analysis = scenarioAnalyses.Values.ToArray(), Coverage = TestCoverage }, ProjectStore.Options));
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { error.Text = ex.Message; }
        }), "ExportScenarioResults");
        var buttons = new WrapPanel(); foreach (var button in new[] { create, save, duplicate, delete, run, runAll, cancelRun, view, export }) { button.Margin = new Thickness(0,0,8,8); buttons.Children.Add(button); }
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions(".65*,1.5*"), ColumnSpacing = 16 }; body.Children.Add(list);
        var scroll = new ScrollViewer { Name = "ScenarioDraftScroll", Content = editor, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }; Grid.SetColumn(scroll, 1); body.Children.Add(scroll);
        var content = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"), Margin = new Thickness(22), RowSpacing = 10 };
        content.Children.Add(Ui.Stack(Ui.Text("Test scenarios", 24), summary)); Grid.SetRow(body, 1); content.Children.Add(body);
        var messages = new ScrollViewer { Content = error, MaxHeight = 90, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        var footer = Ui.Stack(messages, buttons, Ui.Text("Configuration checks follow reachable choices from the starting overrides. They differ from structural reachability and use a 1,000-configuration budget. Draft edits apply only with Save scenario; each save, duplicate and delete can be undone after closing.", 11, true)); Grid.SetRow(footer, 2); content.Children.Add(footer);
        var closeManager = Ui.Button("Close", () => window!.Close()); Grid.SetRow(closeManager, 3); content.Children.Add(closeManager);
        window = Ui.Dialog(this, "Test scenarios", content, 980, 780, preferences.Scale); window.MinWidth = 640 * preferences.Scale; window.MinHeight = 530 * preferences.Scale;
        window.Closing += (_, _) => running?.Cancel(); Reload(null); await window.ShowDialog(this);
    }
    private async Task<bool> EditAssertion(Window owner, ScenarioAssertion assertion)
    {
        Window? window = null; var fields = new StackPanel { Spacing = 10 }; var error = Ui.Text("", 12, true);
        void RefreshFields()
        {
            fields.Children.Clear();
            if (assertion.Kind is "Numeric" or "Text")
            {
                var keys = (assertion.Kind == "Numeric" ? DialogueEngine.DiscoverVariables(project) : project.TextVariables.Keys).ToList();
                if (!keys.Contains(assertion.Key)) keys.Insert(0, assertion.Key);
                fields.Children.Add(Ui.Field("Variable ID", Ui.Select(keys, assertion.Key, v => assertion.Key = v)));
                if (assertion.Kind == "Numeric")
                {
                    var n = new NumericUpDown { Value = assertion.Number ?? 0, Minimum = int.MinValue, Maximum = int.MaxValue, FormatString = "0" }; assertion.Number ??= 0;
                    n.ValueChanged += (_, _) => { if (n.Value is { } value) assertion.Number = (int)value; }; fields.Children.Add(Ui.Field("Expected number", n));
                }
                else { assertion.Text ??= ""; fields.Children.Add(Ui.Field("Expected text", Ui.Input(assertion.Text, v => assertion.Text = v))); }
            }
            else if (assertion.Kind == "Ending")
            {
                var ids = project.ListOfCharacterStates.Select(s => s.State).Distinct().ToList(); if (!ids.Contains(assertion.SceneId ?? "")) ids.Insert(0, assertion.SceneId ?? "");
                fields.Children.Add(Ui.Field("Scene that ends the conversation", Ui.Select(ids, assertion.SceneId ?? "", v => { assertion.SceneId = v; assertion.ChoiceId = null; })));
                fields.Children.Add(Ui.Text("Asserts an explicit ending from this scene. The replay itself checks which response was taken.", 12, true));
            }
            else
            {
                assertion.EventType ??= "Event";
                fields.Children.Add(Ui.Field("Integration hook type", Ui.Select(new[] { "Event", "Script", "Shop" }, assertion.EventType, v => assertion.EventType = v)));
                fields.Children.Add(Ui.Field("Target / shop ID", Ui.Input(assertion.Key, v => assertion.Key = v)));
                fields.Children.Add(Ui.Field("Parameter (blank matches any)", Ui.Input(assertion.Parameter ?? "", v => assertion.Parameter = v.Length == 0 ? null : v)));
                var exact = new CheckBox { Content = "Assert exact event count", IsChecked = assertion.Count is not null };
                var count = new NumericUpDown { Value = assertion.Count ?? 1, Minimum = 0, Maximum = int.MaxValue, FormatString = "0", IsEnabled = exact.IsChecked == true };
                exact.IsCheckedChanged += (_, _) => { count.IsEnabled = exact.IsChecked == true; assertion.Count = count.IsEnabled ? (int)(count.Value ?? 0) : null; };
                count.ValueChanged += (_, _) => { if (count.IsEnabled && count.Value is { } n) assertion.Count = (int)n; }; fields.Children.Add(Ui.Stack(exact, Ui.Field("Expected count", count)));
            }
        }
        var kind = Ui.Select(new[] { "Numeric", "Text", "Ending", "Event" }, assertion.Kind, v => { assertion.Kind = v; RefreshFields(); }); kind.Name = "AssertionKind"; RefreshFields();
        var apply = Ui.Button("Apply assertion", () =>
        {
            var issues = ScenarioReplay.CheckDefinition(new() { StartStateId = project.Entry, Assertions = [assertion] });
            if (issues.Count > 0) { error.Text = issues[0].Message; return; } window!.Close(true);
        }, "primary"); apply.Name = "ApplyScenarioAssertion";
        var content = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"), Margin = new Thickness(22), RowSpacing = 10 };
        content.Children.Add(Ui.Stack(Ui.Text("Outcome assertion", 22), Ui.Field("Assertion kind", kind)));
        var scroll = new ScrollViewer { Content = fields }; Grid.SetRow(scroll, 1); content.Children.Add(scroll);
        Grid.SetRow(error, 2); content.Children.Add(error);
        var footer = Ui.Row(Ui.Button("Cancel", () => window!.Close(false)), apply); Grid.SetRow(footer, 3); content.Children.Add(footer);
        window = Ui.Dialog(owner, "Outcome assertion", content, 580, 520, preferences.Scale); return await window.ShowDialog<bool>(owner);
    }
}
