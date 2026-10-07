using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DialogueMachine.Core;
using DialogueMachine.Website;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WebChoice = xLostProHome.Ecosystem.Web.DialogueMachine.Models.Choice;
using WebState = xLostProHome.Ecosystem.Web.DialogueMachine.Models.CharacterState;
using WebAction = xLostProHome.Ecosystem.Web.DialogueMachine.Models.ActionModel;
using WebVisibility = xLostProHome.Ecosystem.Web.DialogueMachine.Models.VisibilityMode;
using xLostProHome.Ecosystem.Web.DialogueMachine.Services;
using xLostProHome.Core.Services;

internal static class RuntimeChecks
{
    private static Guid Id(int i) => Guid.Parse($"40000000-0000-0000-0000-{i:000000000000}");
    private static string Json<T>(T value) => JsonSerializer.Serialize(value, ProjectStore.Options);
    public sealed record Operation(string Kind, int Choice = 0, string? Key = null, int Number = 0, string? Text = null);
    public sealed record RunCase(string Name, int Seed, Operation[] Operations);
    private sealed record Frame(string? Scene, string[] Pages, int Page, RuntimeChoice[] Choices, bool Continue,
        SortedDictionary<string, int> Numeric, SortedDictionary<string, string> Text, string? Ending, Guid? EndingChoice,
        ulong Random, string[] Delivered, string[] Transitions, Hook[] Hooks);

    private static DialogueProject Fixture()
    {
        Choice Choice(int id, string label, string? next) => new() { InternalId = Id(id), ButtonText = label, NextStateId = next };
        var p = new DialogueProject { ProjectName = "Harbour runtime handoff", EntryStateId = "arrival",
            InitialVariables = new() { ["aff:trust"] = 0, ["inv:trust"] = 0, ["inv:coin"] = 2, ["aff:high"] = int.MaxValue, ["aff:low"] = int.MinValue },
            TextVariables = new() { ["player_name"] = "Ada", ["inv:coin"] = "purse" } };
        var arrival = Templates.Scene("arrival", "Arrival", "Keeper", "Welcome {player_name}.\r\n[if aff:trust >= -1] [if inv:coin > 0]Buyer {inv:coin}[else]Traveller[/if] [else]Stranger[/if].\nThe light is {aff:high}.", 0, 0); arrival.SayItAll = false;
        arrival.Choices = [Choice(1, "Enter, {player_name}", "gate"), Choice(2, "Go home", "fallback")];
        arrival.Choices[0].AfterChoiceActions = [new() { Type = "Inventory", TargetId = "coin", Amount = -1 }, new() { Type = "Event", TargetId = "asked", Parameter = "gate" }, new() { TargetId = "trust", Amount = 2 }];
        arrival.DefaultAfterActions = [new() { Type = "Script", TargetId = "arrived", Parameter = "harbour" }, new() { TargetId = "high", Amount = 123 }, new() { TargetId = "low", Amount = -5 }];
        var gate = Templates.Scene("gate", "Gate", "Keeper", "First page for {player_name}.\n[if trust >= 1]Inventory alias [else]Affinity alias [/if]{aff:trust}.", 360, 0); gate.DefaultNextId = "market";
        gate.Choices = [Choice(3, "Hidden rich path", "market"), Choice(4, "Locked friend path", "market"), Choice(5, "Enter anyway", " \t"), Choice(6, "Wait", "gate")];
        gate.Choices[0].Visibility = VisibilityMode.Hide; gate.Choices[0].Requirements = [new() { Type = "Inventory", TargetId = "coin", Value = "10" }];
        gate.Choices[1].Requirements = [new() { TargetId = "trust", Value = "3" }];
        gate.Choices[2].Visibility = VisibilityMode.Always; gate.Choices[2].Requirements = [new() { Type = "Inventory", TargetId = "coin", Value = "99" }];
        gate.Choices[3].AfterChoiceActions = [new() { TargetId = "trust", Amount = 1 }];
        gate.DefaultAfterActions = [new() { Type = "Inventory", TargetId = "coin", Amount = 1 }, new() { Type = "Event", TargetId = "gate_exit", Parameter = "leaving" }];
        var market = Templates.Scene("market", "Market", "Merchant", "Lantern for {player_name}.\nRope and supplies.", 720, 0); market.Type = "Shop"; market.ShopData.ShopId = "harbour_shop"; market.SayItAll = false;
        market.Choices = [Choice(7, "Buy lantern", "farewell")]; market.Choices[0].Visibility = VisibilityMode.Always;
        market.Choices[0].AfterChoiceActions = [new() { Type = "Inventory", TargetId = "coin", Amount = -5 }, new() { Type = "Event", TargetId = "purchase", Parameter = "lantern" }];
        market.DefaultAfterActions = [new() { Type = "Script", TargetId = "close_shop" }];
        var fallback = Templates.Scene("fallback", "Fallback", "Keeper", "No unlocked replies.", 360, 330); fallback.DefaultNextId = "farewell";
        fallback.Choices = [Choice(8, "Hidden", "farewell"), Choice(9, "Disabled", "farewell")]; fallback.Choices[0].Visibility = VisibilityMode.Hide;
        foreach (var c in fallback.Choices) c.Requirements = [new() { Type = "Inventory", TargetId = "coin", Value = "99" }];
        var farewell = Templates.Scene("farewell", "Farewell", "Keeper", "", 720, 330); farewell.DefaultAfterActions = [new() { Type = "Event", TargetId = "ended" }];
        p.ListOfCharacterStates = [farewell, arrival, gate, market, fallback];
        p.Extra = new() { ["FutureRoot"] = JsonSerializer.SerializeToElement(new { keep = 42 }) };
        foreach (var s in p.ListOfCharacterStates)
        {
            s.Extra = new() { ["FutureScene"] = JsonSerializer.SerializeToElement(true) };
            s.ShopData.Extra = new() { ["FutureShop"] = JsonSerializer.SerializeToElement("keep") };
            foreach (var c in s.Choices)
            {
                c.Extra = new() { ["FutureResponse"] = JsonSerializer.SerializeToElement(7) };
                foreach (var r in c.Requirements) r.Extra = new() { ["FutureRequirement"] = JsonSerializer.SerializeToElement(8) };
                foreach (var a in c.AfterChoiceActions) a.Extra = new() { ["FutureAction"] = JsonSerializer.SerializeToElement(9) };
            }
        }
        var group = SceneGroups.Create(p, "Harbour"); SceneGroups.Assign(p, group, [arrival, gate]); AuthoringOperations.SaveSceneTemplate(p, arrival, "Arrival");
        return p;
    }
    private static RunCase[] Cases() =>
    [
        new("shop", 42, [new("Choice", 1), new("Page"), new("Choice", 5), new("Choice", 7), new("Continue")]),
        new("fallback", -17, [new("Choice", 2), new("Continue"), new("Continue")]),
        new("inventory-ceiling", 0, [new("Choice", 1), new("SetNumeric", Key: "inv:coin", Number: int.MaxValue), new("Page"), new("Choice", 5), new("Choice", 7), new("Continue")]),
        new("self-loop", int.MinValue, [new("Choice", 1), new("Page"), new("Choice", 6), new("Page"), new("Choice", 6), new("Page"), new("Choice", 4), new("Choice", 7), new("Continue")]),
        new("live-text-random", -1, [new("SetText", Key: "player_name", Text: "[if aff:trust > 0]literal[/if]\uE000"), new("Reroll"), new("SetNumeric", Key: "inv:coin", Number: -10), new("Refresh"), new("Choice", 1), new("SetText", Key: "player_name", Text: "Grace"), new("Page"), new("Choice", 5), new("Reroll"), new("Choice", 7), new("Continue"), new("SetText", Key: "player_name", Text: "Done")])
    ];
    private static Simulation Preview(Handoff handoff)
    {
        var p = ProjectStore.Parse(handoff.StoryJson); p.InitialVariables = handoff.NumericValues; p.TextVariables = handoff.TextValues;
        return new Simulation(p, handoff.StartingScene, handoff.Seed, retainHistory: false);
    }
    private static void Apply(Simulation p, Operation op)
    {
        switch (op.Kind)
        {
            case "Choice": p.Choose(p.Current!.Choices.Single(c => c.InternalId == Id(op.Choice))); break;
            case "Page": p.NextPage(); break;
            case "Continue": p.Continue(); break;
            case "SetNumeric": p.SetVariable(op.Key!, op.Number); break;
            case "SetText": p.SetTextVariable(op.Key!, op.Text!); break;
            case "Reroll": p.RerollLine(); break;
            case "Refresh": p.RefreshText(); break;
            default: throw new Exception("Unknown fixture operation: " + op.Kind);
        }
    }
    private static void Apply(RuntimePlayer p, Operation op)
    {
        switch (op.Kind)
        {
            case "Choice": p.Select(Id(op.Choice)); break;
            case "Page": p.Advance(); break;
            case "Continue": p.Proceed(); break;
            case "SetNumeric": p.SetNumeric(op.Key!, op.Number); break;
            case "SetText": p.SetText(op.Key!, op.Text!); break;
            case "Reroll": p.Reroll(); break;
            case "Refresh": p.Refresh(); break;
            default: throw new Exception("Unknown fixture operation: " + op.Kind);
        }
    }
    private static Frame Snapshot(RuntimePlayer p) => new(p.Scene, p.Pages, p.Page, p.Choices.ToArray(), p.CanContinue,
        new(p.Numeric.ToDictionary()), new(p.Text.ToDictionary()), p.EndingScene, p.EndingChoice, p.RandomState,
        p.Delivered.ToArray(), p.Transitions.ToArray(), p.Hooks.ToArray());
    private static Frame Snapshot(Simulation p, Handoff config)
    {
        var hooks = new List<Hook>(); var numeric = new Dictionary<string, int>(config.NumericValues); var text = new Dictionary<string, string>(config.TextValues);
        foreach (var entry in p.Trace)
        {
            if (entry.Kind == "VariableChange") numeric[entry.Key!] = entry.After!.Value;
            if (entry.Kind == "TextChange") text[entry.Key!] = entry.Text!;
            if (entry.Kind == "Integration") hooks.Add(new(entry.EventType!, entry.EventId!, entry.Parameter, entry.SceneId!, new SortedDictionary<string, int>(numeric), new SortedDictionary<string, string>(text)));
        }
        var choices = p.MorePages || p.Current is null ? [] : p.Current.Choices
            .Where(c => c.Visibility != VisibilityMode.Hide || DialogueEngine.RequirementsMet(c, p.Variables, out _))
            .Select(c => new RuntimeChoice(c.InternalId, DialogueEngine.ResolveText(c.ButtonText, p.Variables, p.TextVariables), (int)c.Visibility,
                c.Visibility == VisibilityMode.Always || DialogueEngine.RequirementsMet(c, p.Variables, out _), string.IsNullOrWhiteSpace(c.NextStateId) ? p.Current.DefaultNextId : c.NextStateId)).ToArray();
        return new(p.Current?.State, p.Pages, p.PageIndex, choices, p.CanContinue, new(p.Variables), new(p.TextVariables), p.Ending?.SceneId, p.Ending?.ChoiceId, p.RandomState,
            p.Trace.Where(t => t.Kind == "Dialogue").Select(t => t.Text!).ToArray(),
            p.Trace.Where(t => t.Kind is "Choice" or "Continue").Select(t => t.SceneId + "->" + (string.IsNullOrWhiteSpace(t.Destination) ? "[EXIT]" : t.Destination)).ToArray(), hooks.ToArray());
    }
    public static void WriteFixtures()
    {
        var dir = Path.GetFullPath("tests/Fixtures/runtime"); Directory.CreateDirectory(dir); var p = Fixture();
        ProjectStore.AtomicWrite(Path.Combine(dir, "story.dialogue.json"), ProjectStore.Serialize(p));
        ProjectStore.AtomicWrite(Path.Combine(dir, "website.json"), ProjectStore.WebExport(p));
        ProjectStore.AtomicWrite(Path.Combine(dir, "handoff.json"), RuntimeExport.Serialize(RuntimeExport.Create(p, 42)));
        ProjectStore.AtomicWrite(Path.Combine(dir, "cases.json"), Json(Cases()));
        var expected = new Dictionary<string, List<Frame>>();
        foreach (var run in Cases())
        {
            var config = JsonSerializer.Deserialize<Handoff>(RuntimeExport.Serialize(RuntimeExport.Create(p, run.Seed)))!;
            var preview = Preview(config); var frames = new List<Frame> { Snapshot(preview, config) };
            foreach (var op in run.Operations) { Apply(preview, op); frames.Add(Snapshot(preview, config)); }
            expected[run.Name] = frames;
        }
        ProjectStore.AtomicWrite(Path.Combine(dir, "expected.json"), Json(expected));
    }
    public static void Run(Action<bool, string> check, Action<Action, string> throws, string output)
    {
        Directory.CreateDirectory(output); var dir = Path.Combine(AppContext.BaseDirectory, "Fixtures/runtime");
        var p = ProjectStore.Load(Path.Combine(dir, "story.dialogue.json")); var before = ProjectStore.Serialize(p);
        var json = File.ReadAllText(Path.Combine(dir, "handoff.json")); var original = JsonSerializer.Deserialize<Handoff>(json)!;
        // Git normalizes JSON file formatting to LF; embedded StoryJson remains exact.
        check(RuntimeExport.Serialize(RuntimeExport.Create(p, 42)) == Json(original), "committed runtime handoff matches the authored fixture and explicit defaults");
        check(original.StoryJson == ProjectStore.WebExport(p) && original.StoryJson.Replace("\r\n", "\n") == File.ReadAllText(Path.Combine(dir, "website.json")).Replace("\r\n", "\n"), "website JSON export remains byte-for-byte unchanged inside the runtime handoff");
        check(original.NumericValues["aff:high"] == int.MaxValue && original.NumericValues["aff:low"] == int.MinValue && original.TextValues["player_name"] == "Ada", "handoff carries int32 boundary values, text and explicit seed");
        check(ProjectStore.Parse(original.StoryJson).Entry == "arrival" && p.ListOfCharacterStates[0].State == "farewell", "entry is first in export without reordering the desktop story");
        check(!original.StoryJson.Contains("SceneGroups") && !original.StoryJson.Contains("SceneTemplates") && !original.StoryJson.Contains("InitialVariables"), "legacy export omits desktop groups/templates/runtime configuration");
        check(!typeof(RuntimePlayer).Assembly.GetReferencedAssemblies().Any(a => a.Name!.Contains("DialogueMachine.Core")), "real website runtime assembly has no desktop Core dependency");
        CheckProvenance(check);
        var cases = JsonSerializer.Deserialize<RunCase[]>(File.ReadAllText(Path.Combine(dir, "cases.json")), ProjectStore.Options)!;
        var expected = JsonSerializer.Deserialize<Dictionary<string, List<Frame>>>(File.ReadAllText(Path.Combine(dir, "expected.json")), ProjectStore.Options)!;
        var results = new Dictionary<string, List<Frame>>();
        foreach (var run in cases)
        {
            var config = original with { Seed = run.Seed }; var preview = Preview(config); var host = new SampleHost(); using var target = new RuntimePlayer(Json(config), host);
            var frames = new List<Frame>();
            void Compare(int step)
            {
                var desktop = Snapshot(preview, config); var website = Snapshot(target);
                // Dictionary enumeration order is outside the runtime contract.
                website = website with { Hooks = website.Hooks.Select(h => h with { Numeric = new SortedDictionary<string, int>(h.Numeric.ToDictionary()), Text = new SortedDictionary<string, string>(h.Text.ToDictionary()) }).ToArray() };
                var actual = Json(website); var wanted = Json(desktop);
                if (actual != wanted || wanted != Json(expected[run.Name][step]))
                {
                    var detail = Path.GetFullPath(Path.Combine(output, run.Name + "-difference.json"));
                    ProjectStore.AtomicWrite(detail, Json(new { Case = run.Name, Step = step, Operation = step == 0 ? null : run.Operations[step - 1], Desktop = desktop, Website = website, Expected = expected[run.Name][step] }));
                    throw new Exception($"Runtime compatibility difference in {run.Name} at operation {step}; inspect {detail}. Review the contract/adapter and re-export or re-record only if the changed behavior is intended.");
                }
                check(true, $"{run.Name} operation {step}: text, choice order/availability, transitions, values, hook order/state, random and ending match independent website source"); frames.Add(website);
            }
            Compare(0);
            for (var i = 0; i < run.Operations.Length; i++) { Apply(preview, run.Operations[i]); Apply(target, run.Operations[i]); Compare(i + 1); }
            check(target.EndingScene == "farewell", run.Name + " reaches the explicit ending");
            check(host.Achievements.Contains("ended"), run.Name + " invokes actual host Event behavior");
            if (run.Name == "shop")
            {
                check(host.Quests.Contains("arrived:harbour") && host.OpenShop is null && host.Log.Any(l => l.StartsWith("Shop harbour_shop")), "sample Script and Shop callbacks update observable quest/shop state");
                check(target.Numeric["inv:coin"] == 0 && target.Numeric["aff:high"] == int.MaxValue && target.Numeric["aff:low"] == int.MinValue, "inventory floors at zero and affinity saturates at both int32 boundaries");
            }
            results[run.Name] = frames;
        }
        ProjectStore.AtomicWrite(Path.Combine(output, "runtime-results.json"), Json(new { Contract = RuntimePlayer.Contract, Cases = results }));
        CharacterizeLegacy(check);
        InvalidInputs(check, throws, p, original, json, output);
        check(ProjectStore.Serialize(p) == before, "runtime export and target execution preserve all unknown fields, groups, templates and project state");
        var history = new ProjectHistory(); history.Reset(p); p.ProjectName = "Edited"; history.Commit(p); var undo = history.Undo()!; var redo = history.Redo()!;
        check(ProjectStore.Serialize(undo) == before && redo.ProjectName == "Edited", "runtime fixture retains complete compressed Undo/Redo snapshots");
        // Existing regression checks cover selection, guarded saving and recovery; no adapter state enters the project model.
        check(ProjectStore.Parse("{\"ListOfCharacterStates\":[{\"State\":\"legacy\",\"DefaultNextId\":\"[EXIT]\"}]}").FormatVersion == 1, "older website projects keep the additive format-1 default without migration");
        RenderSample(check, json, output).GetAwaiter().GetResult();
    }
    private static void CheckProvenance(Action<bool, string> check)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../Integration/WebsitePlayer"));
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "upstream.json")));
        foreach (var file in manifest.RootElement.GetProperty("Files").EnumerateArray())
        {
            var path = file.GetProperty("Path").GetString()!;
            check(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, "Upstream", path)))) == file.GetProperty("Sha256").GetString(), "actual website source unchanged: " + path);
        }
    }
    private sealed class Legacy : xLostProHome.Ecosystem.Web.DialogueMachine.DialogueMachine
    {
        public Legacy(WebState[] states) { Root.ListOfCharacterStates = states.ToList(); Logic = new(); Toasts = new(); }
        public void Go(string? id) => SimGoToNext(id);
        public void Choose(WebChoice c) => SimSelectChoice(c);
        public string? Scene => simActiveState?.State;
        public int Affinity(string id) => simAffinity.GetValueOrDefault(id);
    }
    private static void CharacterizeLegacy(Action<bool, string> check)
    {
        var service = new DialogueService(); var affinity = new Dictionary<string, int> { ["n"] = int.MaxValue }; var inventory = new Dictionary<string, int>();
        service.ProcessActions([new() { TargetId = "n", Amount = 1 }], affinity, inventory);
        check(affinity["n"] == int.MinValue, "upstream characterization: legacy numeric overflow wraps (adapter explicitly saturates)");
        check(service.ProcessConditionalLogic(" {player_name} [if aff:n < -1]negative[/if] ", affinity, inventory).Contains("[if"), "upstream characterization: substitutions and signed inline thresholds are unsupported");
        check(service.ProcessConditionalLogic("a[if aff:n < 0] b [else] c [/if]d", affinity, inventory) == "abd", "upstream characterization: legacy conditional branches trim whitespace");
        var self = new WebState { State = "self", Text = "Hi", DefaultNextId = "[EXIT]", DefaultAfterActions = [new() { TargetId = "n", Amount = 1 }] };
        using var legacy = new Legacy([self]); legacy.Go("self"); legacy.Go("self");
        check(legacy.Affinity("n") == 0, "upstream characterization: self-loop scene exit actions are skipped");
        legacy.Choose(new WebChoice { NextStateId = " ", AfterChoiceActions = [] });
        check(legacy.Scene is null, "upstream characterization: blank response ends rather than inheriting its fallback");
        using var missing = new Legacy([self]); missing.Go("self"); missing.Choose(new WebChoice { NextStateId = "missing", AfterChoiceActions = [new() { TargetId = "n", Amount = 5 }] });
        check(missing.Affinity("n") == 5 && missing.Scene is null, "upstream characterization: response actions run before missing-target failure");
        var blank = new WebChoice { Requirements = [new() { TargetId = "", Value = "99" }] };
        check(service.EvaluateRequirements(blank, affinity, inventory, out _), "upstream characterization: blank requirement targets are ignored (adapter rejects)");
    }
    private sealed class FailingHost : IRuntimeHost
    {
        public void Event(Hook h) => throw new Exception("Host unavailable");
        public void Script(Hook h) { }
        public void Shop(Hook h) { }
    }
    private static void InvalidInputs(Action<bool, string> check, Action<Action, string> throws, DialogueProject p, Handoff original, string json, string output)
    {
        void Reject(Handoff h, string fragment, string label)
        {
            try { using var target = new RuntimePlayer(Json(h), new SampleHost()); throw new Exception("Expected rejection"); }
            catch (InvalidDataException ex) { check(ex.Message.Contains(fragment), label + ": actionable error"); }
        }
        Reject(original with { SchemaVersion = 2 }, "matching website adapter", "future companion schema");
        Reject(original with { Contract = "other.v1" }, "matching website adapter", "unsupported destination contract");
        Reject(original with { StoryJson = original.StoryJson + " " }, "Re-export", "mismatched story/configuration hash");
        Reject(original with { StartingScene = "missing" }, "StartingScene", "missing entry configuration");
        Reject(original with { MaxOperations = 0 }, "MaxOperations", "invalid operation budget");
        Reject(original with { NumericValues = original.NumericValues.Where(p => p.Key != "inv:coin").ToDictionary() }, "configuration", "missing referenced starting numeric value");
        Handoff WithStory(DialogueProject story)
        {
            var data = ProjectStore.WebExport(story);
            return original with { StoryJson = data, StorySha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(data))) };
        }
        var broken = ProjectStore.Clone(p); broken.ListOfCharacterStates.Single(s => s.State == "arrival").Choices[0].NextStateId = "missing";
        Reject(WithStory(broken), "missing", "missing target rejects before response actions or callbacks");
        broken = ProjectStore.Clone(p); broken.ListOfCharacterStates.Single(s => s.State == "arrival").Text = "[if aff:trust > 0]hidden [if aff:trust >< 1]bad[/if][/if]";
        Reject(WithStory(broken), "malformed", "hidden malformed condition rejects before execution");
        broken = ProjectStore.Clone(p); broken.ListOfCharacterStates.Single(s => s.State == "arrival").Text = "[if aff:trust > 2147483648]bad[/if]";
        Reject(WithStory(broken), "int32", "inline threshold outside int32 rejects before execution");
        broken = ProjectStore.Clone(p); broken.ListOfCharacterStates.Single(s => s.State == "arrival").Text = "[if aff:missing > 0]bad[/if]";
        Reject(WithStory(broken), "configuration", "inline-only numeric reference requires explicit configuration");
        broken = ProjectStore.Clone(p); broken.ListOfCharacterStates.Single(s => s.State == "arrival").Choices[0].Visibility = (VisibilityMode)99;
        Reject(WithStory(broken), "visibility", "unsupported numeric visibility rejects before execution");
        broken = ProjectStore.Clone(p); broken.ListOfCharacterStates.Single(s => s.State == "arrival").Choices[0].Requirements = [new() { TargetId = "", Value = "99" }];
        Reject(WithStory(broken), "requirement", "blank requirement target cannot silently bypass a gate");
        var bad = ProjectStore.Clone(p); bad.InitialVariables["legacy:key"] = 0;
        throws(() => RuntimeExport.Create(bad), "legacy malformed numeric IDs stay openable but runtime export asks for repair");
        bad = ProjectStore.Clone(p); bad.ListOfCharacterStates.Single(s => s.State == "gate").Choices[1].InternalId = Id(3);
        throws(() => RuntimeExport.Create(bad), "ambiguous response identities cannot produce a runtime handoff");
        using var limited = new RuntimePlayer(Json(original with { MaxOperations = 2 }), new SampleHost()); limited.Select(Id(1)); limited.Advance();
        var snapshot = Json(Snapshot(limited)); throws(() => limited.Select(Id(6)), "bounded cycle execution stops before the next operation");
        check(Json(Snapshot(limited)) == snapshot, "operation-limit failure preserves partial runtime evidence and values");
        using var cancelled = new RuntimePlayer(json, new SampleHost()); using var token = new CancellationTokenSource(); token.Cancel();
        snapshot = Json(Snapshot(cancelled)); throws(() => cancelled.Select(Id(1), token.Token), "runtime cancellation stops before applying actions");
        check(Json(Snapshot(cancelled)) == snapshot && cancelled.Operations == 0, "cancellation retains the last coherent runtime frame");
        throws(() => new RuntimePlayer(json, new SampleHost(), token.Token), "initial cancellation emits no host callbacks");
        using var failing = new RuntimePlayer(json, new FailingHost()); throws(() => failing.Select(Id(1)), "host callback errors identify the failed integration");
        throws(() => failing.Select(Id(1)), "failed host session cannot replay already-applied external effects");
        var host = new SampleHost(); using var disabled = new RuntimePlayer(json, host); disabled.Select(Id(1)); disabled.Advance();
        snapshot = Json(Snapshot(disabled)); throws(() => disabled.Select(Id(4)), "disabled response selection is rejected");
        check(Json(Snapshot(disabled)) == snapshot && disabled.Choices.Select(c => c.Id).SequenceEqual(new[] { Id(4), Id(5), Id(6) }), "Hide/Disable/Always maintain authored visible order and Always bypasses unmet requirements");
        throws(() => disabled.Proceed(), "continue cannot bypass enabled choices");
        var unknown = JsonSerializer.Deserialize<Handoff>(json)!; unknown.NumericValues["inv:coin"] = -10;
        using var clamped = new RuntimePlayer(Json(unknown), new SampleHost()); check(clamped.Numeric["inv:coin"] == 0, "legacy negative initial inventory normalises explicitly to zero");
        using var stdout = new StringWriter(); var input = Path.Combine(output, "runtime-input.json"); ProjectStore.AtomicWrite(input, ProjectStore.Serialize(p));
        var dest = Path.Combine(output, "cli-handoff.json");
        check(RuntimeExportCommand.Run(["runtime-export", input, "--seed", "-42", "--output", dest], stdout) == 0 && JsonSerializer.Deserialize<Handoff>(File.ReadAllText(dest))!.Seed == -42, "Core-only CLI creates a runnable configuration companion");
        var bytes = File.ReadAllBytes(input); check(RuntimeExportCommand.Run(["runtime-export", input, "--output", input], new StringWriter()) == 2 && bytes.SequenceEqual(File.ReadAllBytes(input)), "CLI refuses to overwrite its authored input");
        check(RuntimeExportCommand.Run(["runtime-export", input, "--seed", "2147483648"], new StringWriter()) == 2, "CLI rejects seeds outside int32");
        check(RuntimeExportCommand.Run(["runtime-export", input, "--max-operations", "10001"], new StringWriter()) == 2, "CLI rejects unbounded operation budgets");
        check(RuntimeExportCommand.Run(["runtime-export", input], new StringWriter(), token.Token) == 5, "CLI runtime export honors cancellation");
    }
    private static async Task RenderSample(Action<bool, string> check, string json, string output)
    {
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        foreach (var theme in new[] { "Dark", "Light", "High contrast" })
            foreach (var scale in new[] { 100, 115, 130 })
            {
                var html = await renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync<PlayerDemo>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["BundleJson"] = json, ["Theme"] = theme, ["Scale"] = scale }))).ToHtmlString());
                check(html.Contains("Enter, Ada") && html.Contains("font-size: " + scale + "%") && html.Contains("data-theme=\"" + theme + "\""), $"runnable website sample renders at {theme}/{scale}% with real target choices");
                File.WriteAllText(Path.Combine(output, $"sample-{theme.Replace(' ', '-')}-{scale}.html"), html);
            }
    }
}
