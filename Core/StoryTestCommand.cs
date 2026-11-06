using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DialogueMachine.Core;

public static class StoryTestCommand
{
    public const string Help = "Dialogue Machine story tests\nvalidate <project.json>\nreplay <project.json> [--all | --scenario <id-or-name>] [--max-steps <1..10000>] [--analyze] [--max-configurations <1..10000>] [--trace <output.json>]\nexport-runtime <project.json> <out.runtime.json> [--seed N] [--max-operations N]\nconformance <outputDir>\nExit: 0 passed, 1 invalid story, 2 input/usage/write error, 3 replay/assertion/dead-end failure, 4 bounded limit, 5 cancelled.\n";
    public static int Run(string[] args, TextWriter output, CancellationToken cancellation = default)
    {
        if (args.Length == 1 && args[0] is "--help" or "-h") { output.Write(Help); return 0; }
        object report; var exit = 2; string? tracePath = null;
        try
        {
            if (args.Length == 2 && args[0] == "conformance")
            {
                var fixtures = ConformanceFixtures.Generate();
                foreach (var (name, content) in fixtures) ProjectStore.AtomicWrite(Path.Combine(args[1], name + ".json"), content);
                output.WriteLine(JsonSerializer.Serialize(new { SchemaVersion = 1, Command = "conformance", ExitCode = 0, Fixtures = fixtures.Keys }, ProjectStore.Options)); return 0;
            }
            if (args.Length < 2 || args[0] is not ("validate" or "replay" or "export-runtime")) throw new ArgumentException("Use validate <project.json>, replay <project.json>, export-runtime <project.json> <out.runtime.json> or conformance <outputDir>. See --help.");
            var command = args[0];
            if (command == "export-runtime")
            {
                if (args.Length < 3) throw new ArgumentException("Use export-runtime <project.json> <out.runtime.json> [--seed N] [--max-operations N]. See --help.");
                var projectPath = args[1]; var outPath = args[2];
                if (Path.GetFullPath(outPath).Equals(Path.GetFullPath(projectPath), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    throw new ArgumentException("Runtime export output must use a different path from the authored project.");
                var seed = 0; var maxOperations = 1000;
                var suppliedOpts = new HashSet<string>(StringComparer.Ordinal);
                for (var i = 3; i < args.Length; i++)
                {
                    var option = args[i];
                    if (!suppliedOpts.Add(option)) throw new ArgumentException($"Unsupported or duplicate option ‘{option}’.");
                    string Value() => ++i < args.Length ? args[i] : throw new ArgumentException($"{option} needs a value.");
                    switch (option)
                    {
                        case "--seed":
                            var s = Value();
                            if (!int.TryParse(s, out seed)) throw new ArgumentException("--seed must be an integer.");
                            break;
                        case "--max-operations":
                            var m = Value();
                            if (!int.TryParse(m, out maxOperations) || maxOperations < 1 || maxOperations > 10000)
                                throw new ArgumentException("--max-operations must be an integer between 1 and 10,000.");
                            break;
                        default: throw new ArgumentException($"Unknown option ‘{option}’. See --help.");
                    }
                }
                cancellation.ThrowIfCancellationRequested(); var exportProject = ProjectStore.Load(projectPath);
                var errors = DialogueEngine.Validate(exportProject).Where(d => d.Severity == "Error").ToList();
                if (errors.Count > 0)
                {
                    exit = 1;
                    report = new { SchemaVersion = 1, Command = command, ExitCode = exit, Diagnostics = errors };
                }
                else
                {
                    try
                    {
                        var handoff = RuntimeExport.Create(exportProject, seed, maxOperations);
                        ProjectStore.AtomicWrite(outPath, RuntimeExport.Serialize(handoff));
                        exit = 0;
                        report = new { SchemaVersion = 1, Command = command, ExitCode = exit, Output = outPath, StorySha256 = handoff.StorySha256 };
                    }
                    catch (InvalidDataException ex)
                    {
                        exit = 1;
                        report = new { SchemaVersion = 1, Command = command, ExitCode = exit, Diagnostics = new[] { new Diagnostic("", "Error", ex.Message) } };
                    }
                }
                var exportJson = JsonSerializer.Serialize(report, ProjectStore.Options);
                output.WriteLine(exportJson); return exit;
            }
            string? selector = null; var all = false; var analyze = false; int? maxSteps = null; var maxConfigurations = 1000;
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

// Golden fixtures for engine ports. Expected snapshots always come from Simulation, never the shared runner.
public static class ConformanceFixtures
{
    private sealed record Step(string Op, Guid? ChoiceId = null, string? Key = null, int? Number = null, string? Text = null);
    private static readonly Step Begin = new("start"), Next = new("continue"), Save = new("save"), Restore = new("restore");
    private static readonly JsonSerializerOptions Output = new() { WriteIndented = true, NewLine = "\n" };
    private static Step Pick(Guid id) => new("choose", id);
    private static Step SetNumber(string key, int value) => new("setNumber", Key: key, Number: value);
    private static Step SetText(string key, string value) => new("setText", Key: key, Text: value);
    private static Guid Id(int i) => Guid.Parse($"20000000-0000-0000-0000-{i:000000000000}");
    private static Guid Id(DialogueProject p, string scene, int index) => p.ListOfCharacterStates.First(s => s.State == scene).Choices[index].InternalId;
    private static DialogueState Scene(string id, string text, string? next = "[EXIT]", bool sayItAll = true)
    {
        var scene = Templates.Scene(id, id, "Narrator", text, 0, 0); scene.DefaultNextId = next; scene.SayItAll = sayItAll; return scene;
    }
    private static Choice Reply(int id, string text, string? next, VisibilityMode visibility = VisibilityMode.Disable, params Requirement[] requirements)
        => new() { InternalId = Id(id), ButtonText = text, NextStateId = next, Visibility = visibility, Requirements = [.. requirements] };
    private static Requirement Needs(string type, string id, string op, int value) => new() { Type = type, TargetId = id, Operator = op, Value = value.ToString() };
    private static DialogueAction Add(string type, string id, int amount) => new() { Type = type, TargetId = id, Amount = amount };
    private static DialogueAction Raise(string type, string id, string? parameter) => new() { Type = type, TargetId = id, Parameter = parameter };
    private static DialogueProject Project(params DialogueState[] scenes) => new()
    { ProjectName = "Conformance", EntryStateId = scenes[0].State, ListOfCharacterStates = [.. scenes], TextVariables = new() { ["player_name"] = "Traveller" } };

    public static SortedDictionary<string, string> Generate()
    {
        var fixtures = new SortedDictionary<string, string>(StringComparer.Ordinal);
        void Case(string name, DialogueProject project, Step[] steps, int seed = 0, DialogueProject? futureStory = null) => fixtures[name] = Fixture(name, project, seed, steps, futureStory);

        var lighthouse = ProjectStore.Load("Examples/lighthouse.dialogue.json");
        Case("lighthouse", lighthouse, [Begin, Next, Pick(Id(lighthouse, "arrival", 1)), Next, Next, Next, Pick(Id(lighthouse, "keeper", 0)), Next, Next]);

        foreach (var seed in new[] { 0, 1, 42 })
            Case($"pages_and_sayitall_false_seed{seed}", Project(
                Scene("random", "First line.\nSecond line.\nThird line.\nFourth line.\nFifth line.", "conditional", false),
                Scene("conditional", "[if aff:mood >= 1]Warm line.[else]Cold line.[/if]\nMiddle line.\nLast line.", "pages", false),
                Scene("pages", "Page one.\n\n   \nPage three.\r\nPage four.\rPage five.", "empty"),
                Scene("empty", "", "empty_random"),
                Scene("empty_random", "\n\n", "[EXIT]", false)),
                [Begin, Next, SetNumber("aff:mood", 1), Next, Next, Next, Next, Next, Next, Next, Next], seed);

        var nested = Scene("nested", "[if aff:a >= 1]A[if inv:b > 0]B[else]C[if aff:a == 2]D[/if][/if][else]E[/if] [IF aff:a < 0]negative[ELSE]not negative[/IF] [if aff:a != -1]not minus one[/if]");
        nested.Choices = [Reply(1, "[if inv:b > 0]Spend[else]Leave[/if]", "[EXIT]")];
        Case("nested_if_else", Project(nested), [Begin, SetNumber("aff:a", 1), SetNumber("inv:b", 1), SetNumber("aff:a", 2), SetNumber("inv:b", 0), SetNumber("aff:a", -1), Pick(Id(1))]);

        var bare = Project(Scene("bare", "[if coin >= 1]inventory set[else]inventory zero[/if] | [if trust >= 1]affinity set[else]affinity zero[/if] | [if missing == 0]defaults to zero[else]not zero[/if]"));
        bare.InitialVariables = new() { ["inv:coin"] = 0, ["aff:coin"] = 5, ["aff:trust"] = 0 };
        Case("bare_key_lookup", bare, [Begin, SetNumber("inv:coin", 1), SetNumber("aff:trust", 1), SetNumber("aff:coin", 0), Next]);

        var placeholder = Scene("placeholder", "{player_name} {unknown} {aff:trust} {inv:coin} {} {not a key}");
        placeholder.Choices = [Reply(1, "Hello {player_name} and {unknown}", "[EXIT]")];
        Case("placeholder_missing", Project(placeholder), [Begin, SetText("player_name", "Ada"), SetNumber("aff:trust", -2), SetNumber("inv:coin", 3), Pick(Id(1))]);

        // Keys follow .NET's \w, so letters outside ASCII are valid in conditions and placeholders.
        var unicodeKeys = Scene("unicode_keys", "[if aff:café >= 1]yes[else]no[/if] {aff:café} {naïve} [if ключ == 2]two[else]not two[/if]");
        unicodeKeys.Choices = [Reply(1, "[if aff:café >= 1]Café[else]None[/if]", "[EXIT]")];
        var keysProject = Project(unicodeKeys); keysProject.TextVariables["naïve"] = "Zoë";
        Case("unicode_keys", keysProject, [Begin, SetNumber("aff:café", 3), SetNumber("aff:ключ", 2), Pick(Id(1))]);

        var gate = Scene("gate", "Trust is {aff:trust}.", "[EXIT]");
        gate.Choices =
        [
            Reply(1, "Hide >=", "stuck", VisibilityMode.Hide, Needs("Affinity", "trust", ">=", 1)), Reply(2, "Disable >=", "stuck", VisibilityMode.Disable, Needs("Affinity", "trust", ">=", 1)),
            Reply(3, "Always >=", "stuck", VisibilityMode.Always, Needs("Affinity", "trust", ">=", 1)), Reply(4, "Disable ==", "stuck", VisibilityMode.Disable, Needs("Inventory", "coin", "==", 1)),
            Reply(5, "Hide !=", "stuck", VisibilityMode.Hide, Needs("Affinity", "trust", "!=", 0)), Reply(6, "Disable <", "stuck", VisibilityMode.Disable, Needs("Affinity", "trust", "<", 1)),
            Reply(7, "Hide <=", "stuck", VisibilityMode.Hide, Needs("Affinity", "trust", "<=", 0)), Reply(8, "Disable >", "stuck", VisibilityMode.Disable, Needs("Affinity", "trust", ">", 0)),
            Reply(9, "Two conditions", "stuck", VisibilityMode.Disable, Needs("Affinity", "trust", ">=", 1), Needs("Inventory", "coin", ">=", 1)),
        ];
        var stuck = Scene("stuck", "Only continue works here.", "dead_end"); stuck.Choices = [Reply(10, "Never", "[EXIT]", VisibilityMode.Disable, Needs("Inventory", "coin", ">=", 5))];
        var deadEnd = Scene("dead_end", "No way forward yet.", ""); deadEnd.Choices = [Reply(11, "Pay", "[EXIT]", VisibilityMode.Disable, Needs("Inventory", "coin", ">=", 5))];
        Case("visibility_hide_disable_always", Project(gate, stuck, deadEnd), [Begin, SetNumber("aff:trust", 1), SetNumber("inv:coin", 1), Pick(Id(3)), Next, SetNumber("inv:coin", 5), Pick(Id(11))]);

        var drain = Scene("drain", "Coin {inv:coin}, big {inv:big}, high {aff:high}, low {aff:low}.", "refill");
        drain.DefaultAfterActions = [Add("Inventory", "coin", -5), Add("Inventory", "big", 10), Add("Affinity", "high", 10), Add("Affinity", "low", -10)];
        var refill = Scene("refill", "Coin {inv:coin}, big {inv:big}, high {aff:high}, low {aff:low}."); refill.DefaultAfterActions = [Add("Inventory", "coin", 2), Add("Inventory", "coin", -1)];
        var clamp = Project(drain, refill); clamp.InitialVariables = new() { ["inv:coin"] = -3, ["inv:big"] = int.MaxValue - 1, ["aff:high"] = int.MaxValue - 1, ["aff:low"] = int.MinValue + 1 };
        Case("inventory_clamp_and_saturation", clamp, [Begin, SetNumber("inv:coin", 3), SetNumber("inv:coin", -4), SetNumber("inv:coin", 1), Next, Next]);

        var flags = Scene("flags", "Door [if aff:door == 1]open[else]shut[/if], lamp [if aff:lamp == 1]lit[else]dark[/if].", "flags_exit");
        flags.Choices = [Reply(1, "Open the door", null)]; flags.Choices[0].AfterChoiceActions = [Add("Set", "door", 1), Add("Toggle", "lamp", 0), Add("Affinity", "lamp", 4)];
        flags.DefaultAfterActions = [Add("Toggle", "door", 0), Add("Set", "never", 0)];
        var flagsExit = Scene("flags_exit", "Door [if aff:door == 1]open[else]shut[/if], lamp {aff:lamp}, never {aff:never}."); flagsExit.DefaultAfterActions = [Add("Toggle", "lamp", 0), Add("Set", "never", 7)];
        var flagProject = Project(flags, flagsExit); flagProject.InitialVariables = new() { ["aff:never"] = 3 };
        var either = Scene("either", "Pick a way in.");
        either.Choices = [Reply(1, "Show coin or key", null, VisibilityMode.Disable, Needs("Inventory", "coin", ">=", 1), Needs("Inventory", "key", ">=", 1)), Reply(2, "Trusted or bold", null, VisibilityMode.Hide, Needs("Affinity", "trust", ">=", 2), Needs("Affinity", "bold", ">=", 2))];
        either.Choices[0].RequirementMatch = either.Choices[1].RequirementMatch = RequirementMatch.Any;
        Case("or_conditions", Project(either), [Begin, SetNumber("inv:key", 1), SetNumber("aff:bold", 3), SetNumber("inv:key", 0), SetNumber("aff:bold", 0), SetNumber("aff:trust", 2), Pick(Id(2))]);
        Case("flags_set_toggle", flagProject, [Begin, Pick(Id(1)), Next, Begin, Next, SetNumber("aff:door", 1), Next]);

        var exit = Scene("exit", "Choices and a default exit."); exit.DefaultAfterActions = [Add("Affinity", "seen", 1), Raise("Event", "finished", null)];
        exit.Choices = [Reply(1, "Uses the default", null)];
        Case("exit_via_default", Project(exit), [Begin, Next, Next, Begin, Pick(Id(1))]);

        var fallback = Scene("fallback", "Pick a response.", "after");
        fallback.Choices = [Reply(1, "Empty", ""), Reply(2, "Whitespace", " \t"), Reply(3, "Null", null)];
        Case("empty_next_uses_default", Project(fallback, Scene("after", "Arrived.")), [Begin, Pick(Id(1)), Begin, Pick(Id(2)), Begin, Pick(Id(3)), Next]);

        var shop = Scene("shop", "Welcome to the shop.", "stall"); shop.Type = "Shop"; shop.ShopData.ShopId = "harbour_shop"; shop.DefaultAfterActions = [Raise("Script", "close_shop", null)];
        shop.Choices = [Reply(1, "Buy", null)]; shop.Choices[0].AfterChoiceActions = [Raise("Event", "purchase", "lamp"), Add("Inventory", "lamp", 1), Raise("Script", "open_bag", "one\ntwo")];
        var stall = Scene("stall", "A second shop.\nWith two pages."); stall.Type = "Shop"; stall.ShopData.ShopId = "stall";
        Case("event_script_shop", Project(shop, stall), [Begin, Pick(Id(1)), Next, Next]);

        // Core rejects unknown action types, so only the handoff carries them: a story from a newer exporter.
        DialogueProject FutureProof(bool future)
        {
            var extra = JsonSerializer.SerializeToElement(new { retained = 42 });
            var scene = Scene("future", "Trust is {aff:trust}."); scene.Extra = new() { ["FutureScene"] = extra }; scene.ShopData.Extra = new() { ["FutureShop"] = extra };
            scene.Choices = [Reply(1, "Go", "[EXIT]", VisibilityMode.Disable, Needs("Affinity", "trust", ">=", 0))];
            scene.Choices[0].Extra = new() { ["FutureChoice"] = extra }; scene.Choices[0].Requirements[0].Extra = new() { ["FutureRequirement"] = extra };
            scene.Choices[0].AfterChoiceActions = [Add("Affinity", "trust", 1)]; scene.Choices[0].AfterChoiceActions[0].Extra = new() { ["FutureAction"] = extra };
            scene.DefaultAfterActions = [Add("Affinity", "trust", 2)];
            if (future) { scene.Choices[0].AfterChoiceActions.Insert(0, Add("Teleport", "trust", 100)); scene.DefaultAfterActions.Add(Add("Quest", "ghost", 5)); }
            return Project(scene);
        }
        Case("unknown_fields_and_action_types", FutureProof(false), [Begin, Pick(Id(1))], futureStory: FutureProof(true));

        var saveStart = Scene("start", "One.\nTwo.\nThree.\nFour.\nFive.", "talk", false);
        var talk = Scene("talk", "First page for {player_name}.\nSecond page, trust {aff:trust}.\nThird page.", "[EXIT]"); talk.Choices = [Reply(1, "Onward", "omen")];
        var omen = Scene("omen", "Red sky.\nGrey sky.\nBlue sky.\nGold sky.\nBlack sky.\nWhite sky.\nGreen sky.", "[EXIT]", false);
        Case("save_restore_mid_pages", Project(saveStart, talk, omen), [Begin, Next, SetText("player_name", "Ada"), Next, SetNumber("aff:trust", 2), Save, SetNumber("aff:trust", 9), Next, Pick(Id(1)), Next, Restore, Next, Pick(Id(1)), Next], 42);

        var rich = Scene("rich", "[b]Bold[/b] [i]Italic[/i] [u]Under[/u] [colour=#ff8800]Amber[/colour] [COLOUR=red][B]Loud[/B][/COLOUR] [color=blue]left alone[/color] [if aff:x >= 0][b]inside[/b][/if]");
        rich.Choices = [Reply(1, "[i]Quietly[/i] [colour=red]leave[/colour]", "[EXIT]")];
        Case("rich_text_tags", Project(rich), [Begin, Pick(Id(1))]);

        var unicode = Scene("unicode", "こんにちは {player_name} 🌊\nمرحبا \"quoted\" back\\slash\ttab\nCafe\u0301 👩‍🚀 line\u2028separator <tag> & 'mark'");
        unicode.Choices = [Reply(1, "→ Ça va, {player_name}?", "[EXIT]")];
        var unicodeProject = Project(unicode); unicodeProject.TextVariables["player_name"] = "Zoë 🚢";
        Case("unicode_text", unicodeProject, [Begin, Next, SetText("player_name", "李雷"), Next, Pick(Id(1))]);

        var prop = Scene("prop", "First inspection of the bed.\n@Hero [hero_groggy]: Stop talking about my bed.\nFinal inspection as {player_calling}.", "prop_hub", false);
        prop.Metadata["delivery"] = "escalate"; prop.Metadata["portrait"] = "narrator_calm";
        var propHub = Scene("prop_hub", "You stand beside the bed as {player_calling}.", "[EXIT]");
        propHub.Choices = [Reply(1, "Inspect again", "prop"), Reply(2, "Become Plumber", "prop")];
        propHub.Choices[1].AfterChoiceActions = [Raise("SetText", "player_calling", "Plumber")];
        var propProject = Project(prop, propHub); propProject.TextVariables["player_calling"] = "Noble Warrior";
        Case("set_text_escalate_and_page_speakers", propProject, [Begin, Next, Pick(Id(1)), Next, Pick(Id(2)), Next, Pick(Id(1)), Next, Next]);
        Case("escalate_visits_save_restore", propProject, [Begin, Next, Pick(Id(1)), Save, Next, Pick(Id(1)), Restore, Next, Pick(Id(1)), Next, Pick(Id(1))]);
        return fixtures;
    }

    private static string Fixture(string name, DialogueProject project, int seed, Step[] steps, DialogueProject? futureStory)
    {
        var handoff = RuntimeExport.Create(project, seed);
        // StoryExport indents with the platform newline; fixtures must be identical on every OS.
        var story = (futureStory is null ? handoff.StoryJson : ProjectStore.StoryExport(futureStory)).Replace("\r\n", "\n");
        var node = JsonSerializer.SerializeToNode(handoff with { StoryJson = story, StorySha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(story))) })!.AsObject();
        if (futureStory is not null) node["FutureHandoffField"] = new JsonObject { ["retained"] = 42 };

        var expected = new JsonArray(); var played = new List<Step>(); var savedAt = 0; Simulation? simulation = null;
        foreach (var step in steps)
        {
            var mark = simulation?.Trace.Count ?? 0;
            switch (step.Op)
            {
                case "start": simulation = new(project, seed: seed); played.Clear(); mark = 0; break;
                case "save": savedAt = played.Count; break;
                case "restore":
                    // Simulation has no save format; replaying the same inputs gives the same state.
                    played.RemoveRange(savedAt, played.Count - savedAt); simulation = new(project, seed: seed);
                    foreach (var earlier in played) Play(simulation, earlier);
                    mark = simulation.Trace.Count; break;
                default: Play(simulation!, step); played.Add(step); break;
            }
            expected.Add(Snapshot(simulation!, mark));
        }
        return new JsonObject { ["name"] = name, ["handoff"] = node, ["inputs"] = new JsonArray(steps.Select(Input).ToArray<JsonNode?>()), ["expected"] = expected }.ToJsonString(Output) + "\n";
    }

    private static void Play(Simulation simulation, Step step)
    {
        switch (step.Op)
        {
            case "continue": simulation.Continue(); break;
            case "choose": simulation.Choose(simulation.Current!.Choices.First(c => c.InternalId == step.ChoiceId)); break;
            case "setNumber": simulation.SetVariable(step.Key!, step.Number!.Value); break;
            case "setText": simulation.SetTextVariable(step.Key!, step.Text!); break;
        }
    }

    private static JsonObject Input(Step step)
    {
        var input = new JsonObject { ["op"] = step.Op };
        if (step.ChoiceId is not null) input["choiceId"] = step.ChoiceId.Value.ToString();
        if (step.Key is not null) input["key"] = step.Key;
        if (step.Number is not null) input["value"] = step.Number.Value;
        if (step.Text is not null) input["value"] = step.Text;
        return input;
    }

    private static JsonObject Snapshot(Simulation s, int traceMark)
    {
        var page = s.Current is null ? null : s.Pages[s.PageIndex];
        var choices = new JsonArray();
        foreach (var c in s.Current?.Choices ?? [])
        {
            var met = DialogueEngine.RequirementsMet(c, s.Variables, out _) || c.Visibility == VisibilityMode.Always;
            if (!met && c.Visibility == VisibilityMode.Hide) continue;
            choices.Add(new JsonObject { ["id"] = c.InternalId.ToString(), ["text"] = DialogueEngine.ResolveText(c.ButtonText, s.Variables, s.TextVariables), ["enabled"] = met });
        }
        var numeric = new JsonObject(); foreach (var (key, value) in s.Variables.OrderBy(v => v.Key, StringComparer.Ordinal)) numeric[key] = value;
        var text = new JsonObject(); foreach (var (key, value) in s.TextVariables.OrderBy(v => v.Key, StringComparer.Ordinal)) text[key] = value;
        var events = new JsonArray();
        foreach (var t in s.Trace.Skip(traceMark).Where(t => t.Kind == "Integration")) events.Add(new JsonObject { ["type"] = t.EventType, ["targetId"] = t.EventId, ["parameter"] = t.Parameter });
        return new JsonObject
        {
            ["sceneId"] = s.Current?.State, ["ended"] = s.Current is null, ["page"] = page, ["plain"] = page is null ? null : DialogueEngine.PlainText(page), ["pageIndex"] = s.PageIndex, ["pageCount"] = s.Pages.Length,
            ["choices"] = choices, ["canContinue"] = s.CanContinue, ["numeric"] = numeric, ["text"] = text, ["events"] = events
        };
    }
}
