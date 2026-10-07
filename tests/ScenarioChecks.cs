using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DialogueMachine.Core;
using DialogueMachine.UI;
using System.Text.Json;

internal static class ScenarioChecks
{
    private static string Json<T>(T value) => JsonSerializer.Serialize(value, ProjectStore.Options);
    public static DialogueProject Fixture()
    {
        Guid Id(int i) => Guid.Parse($"10000000-0000-0000-0000-{i:000000000000}");
        var project = new DialogueProject { ProjectName = "Repeatable harbour", EntryStateId = "arrival", TextVariables = new() { ["player_name"] = "Traveller" }, InitialVariables = new() { ["aff:trust"] = 0, ["inv:coin"] = 0, ["aff:limit"] = int.MaxValue } };
        var arrival = Templates.Scene("arrival", "Arrival", "Keeper", "Welcome {player_name}.\n[if aff:trust >= 1]A friend[else]A stranger[/if] arrives.\nThe lamp is lit.", 0, 0); arrival.SayItAll = false;
        arrival.DefaultAfterActions = [new() { Type = "Inventory", TargetId = "coin", Amount = 1 }, new() { Type = "Script", TargetId = "arrived", Parameter = "harbour" }];
        arrival.Choices = [new() { InternalId = Id(1), ButtonText = "Ask to enter", NextStateId = "gate", AfterChoiceActions = [new() { TargetId = "trust", Amount = 1 }, new() { Type = "Event", TargetId = "asked", Parameter = "gate" }] }, new() { InternalId = Id(2), ButtonText = "Go home", NextStateId = "farewell" }];
        var gate = Templates.Scene("gate", "Gate", "Keeper", "First page.\nSecond page for {player_name}.", 360, 0);
        gate.Choices = [new() { InternalId = Id(3), ButtonText = "Enter shop", Visibility = VisibilityMode.Hide, NextStateId = "market", Requirements = [new() { TargetId = "trust", Value = "2" }] }, new() { InternalId = Id(4), ButtonText = "Leave", NextStateId = "farewell" }];
        var market = Templates.Scene("market", "Market", "Merchant", "A small shop.", 720, 0); market.Type = "Shop"; market.ShopData.ShopId = "harbour_shop";
        market.Choices = [new() { InternalId = Id(5), ButtonText = "Buy lamp", Visibility = VisibilityMode.Always, NextStateId = "farewell", Requirements = [new() { Type = "Inventory", TargetId = "coin", Value = "99" }], AfterChoiceActions = [new() { Type = "Inventory", TargetId = "coin", Amount = -3 }, new() { Type = "Event", TargetId = "purchase", Parameter = "lamp" }] }];
        market.DefaultAfterActions = [new() { Type = "Script", TargetId = "shop_closed" }];
        var farewell = Templates.Scene("farewell", "Farewell", "Keeper", "Goodbye {player_name}.", 720, 340); farewell.DefaultAfterActions = [new() { TargetId = "limit", Amount = 99 }];
        project.ListOfCharacterStates = [arrival, gate, market, farewell];
        PlaytestScenario Record(string id, string name, int seed, int trust, int coin, string player, Action<Simulation> play)
        {
            var s = new PlaytestScenario { Id = id, Name = name, StartStateId = "arrival", Seed = seed, NumericOverrides = new() { ["aff:trust"] = trust, ["inv:coin"] = coin }, TextOverrides = new() { ["player_name"] = player } };
            var simulation = ScenarioReplay.Start(project, s); play(simulation); s.Steps = simulation.ReplaySteps.ToList();
            s.Assertions = [new() { Kind = "Numeric", Key = "aff:trust", Number = simulation.Variables["aff:trust"] }, new() { Kind = "Numeric", Key = "inv:coin", Number = simulation.Variables["inv:coin"] }, new() { Kind = "Numeric", Key = "aff:limit", Number = int.MaxValue }, new() { Kind = "Text", Key = "player_name", Text = player }, new() { Kind = "Ending", SceneId = "farewell" }];
            return s;
        }
        void Choose(Simulation s, int i) => s.Choose(s.Current!.Choices[i]);
        var shop = Record("shop-seeded", "Shop seeded", 42, 1, 2, "Ada", s => { Choose(s, 0); s.NextPage(); Choose(s, 0); Choose(s, 0); s.Continue(); });
        shop.Assertions.AddRange([new() { Kind = "Event", EventType = "Event", Key = "asked", Parameter = "gate", Count = 1 }, new() { Kind = "Event", EventType = "Script", Key = "arrived", Parameter = "harbour", Count = 1 }, new() { Kind = "Event", EventType = "Shop", Key = "harbour_shop", Count = 1 }, new() { Kind = "Event", EventType = "Event", Key = "purchase", Parameter = "lamp", Count = 1 }, new() { Kind = "Event", EventType = "Script", Key = "shop_closed", Count = 1 }]);
        var leave = Record("leave-seeded", "Leave seeded", -17, 0, 0, "Ben", s => { Choose(s, 0); s.NextPage(); Choose(s, 1); s.Continue(); });
        var home = Record("home-seeded", "Home seeded", 0, 0, 0, "Traveller", s => { Choose(s, 1); s.Continue(); });
        project.PlaytestScenarios = [shop, leave, home];
        project.Extra = new() { ["FutureProject"] = JsonSerializer.SerializeToElement("keep") };
        shop.Extra = new() { ["FutureScenario"] = JsonSerializer.SerializeToElement(42) };
        shop.Steps[0].Extra = new() { ["FutureStep"] = JsonSerializer.SerializeToElement(new { keep = true }) };
        shop.Assertions[0].Extra = new() { ["FutureAssertion"] = JsonSerializer.SerializeToElement("keep") };
        var chapter = SceneGroups.Create(project, "Harbour"); chapter.Id = "harbour-chapter"; SceneGroups.Assign(project, chapter, [arrival, gate]);
        AuthoringOperations.SaveChoiceTemplate(project, arrival.Choices[0], "Ask");
        return project;
    }

    public static void Core(Action<bool, string> check, Action<Action, string> throws, string output)
    {
        var project = ProjectStore.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "repeatable.dialogue.json"));
        var before = ProjectStore.Serialize(project); var scenario = project.PlaytestScenarios[0];
        var result = ScenarioReplay.Run(project, scenario); var second = ScenarioReplay.Run(project, scenario);
        check(result.Passed && result.ExecutedSteps == 5 && result.Ending?.SceneId == "farewell", "committed seeded scenario reaches its asserted ending");
        check(Json(result) == Json(second), "seeded replay produces byte-identical structured results and traces");
        check(result.Trace[0].RandomState == 11400714819323198527UL && result.Trace.First(t => t.Kind == "Dialogue").Text == "A friend arrives.", "seed 42 retains the portable SplitMix64 golden random state and delivered candidate");
        check(result.Variables["aff:trust"] == 2 && result.Variables["inv:coin"] == 0 && result.Variables["aff:limit"] == int.MaxValue, "replay preserves response-before-scene actions, inventory floors and saturation");
        check(result.Trace.Select(t => t.Kind).Contains("SceneVisit") && result.Trace.Count(t => t.Kind == "Dialogue") == 5 && result.Trace.Count(t => t.Kind == "Choice") == 3 && result.Trace.Last().Kind == "Ending", "structured trace records visits, all delivered pages, choices and the ending");
        check(result.Trace.Where(t => t.Kind == "Integration").Select(t => t.EventType + ":" + t.EventId).SequenceEqual(new[] { "Event:asked", "Script:arrived", "Shop:harbour_shop", "Event:purchase", "Script:shop_closed" }), "integration trace keeps Event, Script and Shop hook order without execution");
        check(result.Trace.Select(t => t.Sequence).SequenceEqual(Enumerable.Range(1, result.Trace.Count)), "trace sequences are stable contiguous ordinals without wall-clock timestamps");
        var all = project.PlaytestScenarios.Select(s => ScenarioReplay.Run(project, s)).ToArray();
        check(all.All(r => r.Passed), "all three committed scenario fixtures replay successfully");
        var coverage = ScenarioReplay.Coverage(project, all);
        check(coverage.VisitedScenes == 4 && coverage.TotalScenes == 4 && coverage.VisitedChoices == 5 && coverage.TotalChoices == 5, "aggregate scenario coverage counts distinct scenes and responses across paths");
        check(ProjectStore.Serialize(project) == before, "replay, assertions and coverage never mutate authored story, groups, templates or definitions");
        var live = ScenarioReplay.Start(project, scenario); var initial = Json(live.Trace); var line = live.Pages[0]; var random = live.RandomState;
        live.RefreshText(); check(live.Pages[0] == line && live.RandomState == random && Json(live.Trace) == initial, "preview refresh consumes no randomness and adds no delivery noise when text is unchanged"); live.Back();
        live.SetVariable("aff:trust", 9); live.SetTextVariable("player_name", "Changed"); live.Back(); live.Back();
        check(live.Variables["aff:trust"] == 1 && live.TextVariables["player_name"] == "Ada" && live.Pages[0] == line && Json(live.Trace) == initial && live.ReplaySteps.Count == 0, "Back restores numeric/text live edits, selected line, trace and replay operations together");
        live.RerollLine(); var rerolled = live.Pages[0]; var rolledState = live.RandomState; var rolledTrace = Json(live.Trace); live.Back();
        check(live.Pages[0] == line && live.RandomState == random && Json(live.Trace) == initial, "Back restores the PRNG state and trace before reroll");
        live.RerollLine(); check(live.Pages[0] == rerolled && live.RandomState == rolledState && Json(live.Trace) == rolledTrace, "repeating reroll after Back selects the same line and random stream"); live.Back();
        live.Choose(live.Current!.Choices[0]); var afterMove = Json(live.Trace); var movedVars = Json(live.Variables); live.Back(); live.Choose(live.Current!.Choices[0]);
        check(Json(live.Trace) == afterMove && Json(live.Variables) == movedVars && live.Events.Count == 2, "Back and rechoose restore action/event order and deterministic transition results");
        live.NextPage(); live.Back(); check(live.PageIndex == 0 && live.MorePages, "page Back restores delivery and its recorded operation");
        live.Restart(); check(live.Pages[0] == line && live.RandomState == random && Json(live.Trace) == initial && !live.CanBack && live.Events.Count == 0 && live.Ending is null, "restart resets overrides, text, random stream, history, events, trace and ending");
        var editedPath = ScenarioStore.Clone(project.PlaytestScenarios[2]); var editedSession = ScenarioReplay.Start(project, editedPath);
        editedSession.RerollLine(); editedSession.SetVariable("aff:trust", 5); editedSession.SetTextVariable("player_name", "Tester"); editedSession.Choose(editedSession.Current!.Choices[1]); editedSession.Continue();
        editedSession.SetVariable("aff:trust", 6); editedSession.SetTextVariable("player_name", "After ending"); editedPath.Steps = editedSession.ReplaySteps.ToList(); editedPath.Assertions = [new() { Key = "aff:trust", Number = 6 }, new() { Kind = "Text", Key = "player_name", Text = "After ending" }];
        var editedResult = ScenarioReplay.Run(project, editedPath); check(editedResult.Passed && Json(editedResult.Trace) == Json(editedSession.Trace) && editedResult.RandomState == editedSession.RandomState, "saved rerolls and live numeric/text edits replay exactly, including edits after an ending");
        editedSession.Back(); editedSession.Back(); editedSession.Back(); check(editedSession.Current?.State == "farewell" && editedSession.Ending is null && editedSession.Variables["aff:trust"] == 5 && editedSession.TextVariables["player_name"] == "Tester", "Back from final live edits and an ending restores one coherent active runtime state");
        var variety = Enumerable.Range(-5, 15).Select(seed => new Simulation(project, "arrival", seed).Pages[0]).Distinct().Count(); check(variety > 1, "different saved seeds exercise different random delivery candidates");
        var randomCycle = Templates.Blank(); randomCycle.ListOfCharacterStates[0].Text = "A\nB\nC\nD"; randomCycle.ListOfCharacterStates[0].SayItAll = false; randomCycle.ListOfCharacterStates[0].DefaultNextId = "start";
        var cycleLive = new Simulation(randomCycle, seed: -1); cycleLive.Continue(); var visit = cycleLive.Pages[0]; var visitState = cycleLive.RandomState; cycleLive.Back(); cycleLive.Continue();
        check(cycleLive.Pages[0] == visit && cycleLive.RandomState == visitState && cycleLive.Trace.Count(t => t.Kind == "SceneVisit") == 2, "Back restores random state before entering a random cyclic scene again");
        var contraction = Templates.Blank(); contraction.InitialVariables["aff:trust"] = 0; contraction.ListOfCharacterStates[0].SayItAll = false; contraction.ListOfCharacterStates[0].Text = "[if aff:trust == 0]A\nB\nC\nD[else]Only[/if]";
        var shrinking = new Simulation(contraction, seed: 1); var longLine = shrinking.Pages[0]; var stream = shrinking.RandomState; shrinking.SetVariable("aff:trust", 1); shrinking.SetVariable("aff:trust", 0);
        check(shrinking.Pages[0] == longLine && shrinking.RandomState == stream, "temporarily fewer resolved random candidates do not lose the original selection or consume random state");

        ScenarioResult Changed(Action<DialogueProject, PlaytestScenario> edit) { var copy = ProjectStore.Clone(project); var s = copy.PlaytestScenarios[0]; edit(copy, s); return ScenarioReplay.Run(copy, s); }
        check(Changed((p, _) => p.ListOfCharacterStates[0].Choices.Reverse()).Passed, "choice reordering preserves stable GUID replay references");
        check(Changed((p, _) => { p.ListOfCharacterStates[0].VisualX = 123; p.ListOfCharacterStates[0].Metadata["title"] = "New title"; p.ListOfCharacterStates[0].Metadata["notes"] = "New notes"; }).Passed, "layout/title/notes edits leave runtime replay signatures valid");
        check(Changed((p, _) => p.ListOfCharacterStates[0].Choices[0].ButtonText = "Changed response").Diagnostics.Any(d => d.Code == "ChangedStep" && d.Step == 1 && d.SceneId == "arrival"), "changed response labels fail at the exact stale step with repair guidance");
        check(Changed((p, _) => p.ListOfCharacterStates[0].Choices[0].NextStateId = "farewell").Diagnostics.Any(d => d.Code == "ChangedStep"), "changed destinations cannot silently replay a different path");
        check(Changed((p, _) => p.ListOfCharacterStates[0].Choices[0].AfterChoiceActions[0].Amount = 2).Diagnostics.Any(d => d.Code == "ChangedStep"), "changed response actions invalidate their recorded semantic reference");
        check(Changed((p, _) => p.ListOfCharacterStates[0].Text = "New scene dialogue").Diagnostics.Any(d => d.Code == "ChangedStep"), "changed scene delivery is reported as a stale replay step");
        check(Changed((p, _) => p.ListOfCharacterStates[0].Choices.RemoveAt(0)).Diagnostics.Any(d => d.Code == "MissingChoice" && d.ChoiceId == scenario.Steps[0].ChoiceId), "removed responses return actionable identity failures without label fallback");
        check(Changed((p, _) => p.ListOfCharacterStates[0].Choices[1].InternalId = p.ListOfCharacterStates[0].Choices[0].InternalId).Diagnostics.Any(d => d.Code == "AmbiguousChoice"), "duplicate response identities fail explicitly instead of choosing the first");
        check(Changed((p, _) => p.ListOfCharacterStates.Add(ProjectStore.Clone(p).ListOfCharacterStates[0])).Diagnostics.Any(d => d.Code == "AmbiguousScene"), "duplicate scene identities are reported as ambiguous replay references");
        check(Changed((_, s) => s.Steps[0].SceneId = "removed").Diagnostics.Any(d => d.Code == "MissingScene"), "missing expected scenes identify the broken replay step");
        check(Changed((_, s) => s.Steps[0].SceneId = "gate").Diagnostics.Any(d => d.Code == "UnexpectedScene"), "out-of-order scene references report expected and actual locations");
        check(Changed((_, s) => s.NumericOverrides["aff:removed"] = 1).Diagnostics.Any(d => d.Code == "InvalidConfiguration" && d.Message.Contains("removed")), "removed starting numeric overrides fail rather than creating runtime variables");
        check(Changed((_, s) => s.TextOverrides["removed"] = "x").Diagnostics.Any(d => d.Code == "InvalidConfiguration"), "removed starting text overrides receive configuration diagnostics");
        check(Changed((_, s) => s.NumericOverrides["inv:coin"] = -1).Diagnostics.Any(d => d.Code == "InvalidScenario"), "scenario inventory overrides cannot bypass the nonnegative runtime limit");
        check(Changed((_, s) => s.NumericOverrides["aff:trust"] = 0).Diagnostics.Any(d => d.Code == "UnavailableStep" && d.Step == 3 && d.Message.Contains("Requires")), "unmet replay conditions report the response step and required value");
        check(Changed((_, s) => s.Assertions[0].Number = 7).Diagnostics.Any(d => d.Code == "AssertionFailed" && d.Message.Contains("actual: 2")), "numeric assertion failures include expected and actual values");
        check(Changed((_, s) => s.Assertions[3].Text = "Different").Diagnostics.Any(d => d.Code == "AssertionFailed"), "final text assertions compare the runtime text state");
        check(Changed((_, s) => s.Assertions[4].SceneId = "market").Diagnostics.Any(d => d.Code == "AssertionFailed"), "ending assertions distinguish the scene that actually exited");
        check(Changed((_, s) => s.Assertions[5].Count = 0).Diagnostics.Any(d => d.Code == "AssertionFailed" && d.Message.Contains("actual: 1")), "event assertions support exact occurrence counts including expected absence");
        check(Changed((_, s) => s.Assertions[5].Parameter = "wrong").Diagnostics.Any(d => d.Code == "AssertionFailed"), "event assertions distinguish hook parameters");
        check(Changed((_, s) => { s.Steps.RemoveAt(s.Steps.Count - 1); s.Assertions.Clear(); }).Diagnostics.Any(d => d.Code == "IncompleteReplay"), "unfinished saved sequences fail the required ending gate");
        check(Changed((_, s) => { s.Steps.Clear(); s.Assertions.Clear(); s.RequireEnding = false; }).Passed, "checkpoint scenarios can explicitly finish without an ending");
        check(Changed((_, s) => s.Steps[0].Fingerprint = null).Diagnostics.Any(d => d.Code == "InvalidScenario"), "fingerprint-less sequences cannot silently adopt changed content");
        check(Changed((_, s) => s.Steps[0].Kind = "HostScript").Diagnostics.Any(d => d.Code == "InvalidScenario"), "unsupported future operations remain repairable but cannot execute");

        var rename = ProjectStore.Clone(project); DialogueEngine.Rename(rename, rename.ListOfCharacterStates[0], "new_arrival"); DialogueEngine.Rename(rename, rename.ListOfCharacterStates[3], "new_farewell");
        check(rename.PlaytestScenarios.All(s => ScenarioReplay.Run(rename, s).Passed) && rename.PlaytestScenarios[0].StartStateId == "new_arrival", "safe scene renames rebind scenario starts, valid steps, destinations and ending assertions");
        VariableStore.Rename(rename, "aff:trust", "aff:rapport"); VariableStore.Rename(rename, "player_name", "hero_name", true);
        check(rename.PlaytestScenarios.All(s => ScenarioReplay.Run(rename, s).Passed) && rename.PlaytestScenarios[0].NumericOverrides.ContainsKey("aff:rapport") && rename.PlaytestScenarios[0].TextOverrides.ContainsKey("hero_name"), "safe variable renames preserve scenarios and semantic reference signatures");
        throws(() => VariableStore.Delete(rename, "aff:rapport"), "scenario overrides and assertions protect variable definitions from deletion");
        var staleRename = ProjectStore.Clone(project); staleRename.ListOfCharacterStates[0].Text = "Already changed"; DialogueEngine.Rename(staleRename, staleRename.ListOfCharacterStates[0], "new_arrival");
        check(ScenarioReplay.Run(staleRename, staleRename.PlaytestScenarios[0]).Diagnostics.Any(d => d.Code == "ChangedStep"), "safe renames do not accidentally heal already-stale recordings");

        var blocked = Templates.Blank(); var b = blocked.ListOfCharacterStates[0]; b.DefaultNextId = null;
        b.Choices = [new() { NextStateId = "[EXIT]", Visibility = VisibilityMode.Hide, Requirements = [new() { TargetId = "trust", Value = "1" }] }, new() { NextStateId = "[EXIT]", Visibility = VisibilityMode.Disable, Requirements = [new() { TargetId = "trust", Value = "1" }] }];
        var config = new PlaytestScenario { StartStateId = "start", RequireEnding = false, NumericOverrides = new() { ["aff:trust"] = 0 } };
        check(!DialogueEngine.Validate(blocked).Any(d => d.Severity == "Error"), "structural validation allows branches whose supplied conditions might block them");
        var analysis = ScenarioAnalysis.Analyze(blocked, config);
        check(analysis.DeadEnds.Count == 1 && analysis.DeadEnds[0].Variables["aff:trust"] == 0 && analysis.DeadEnds[0].Reasons.All(r => r.Contains("Requires")), "condition-aware analysis reports the exact dead-end configuration and unmet requirements");
        config.RequireEnding = true; check(ScenarioReplay.Run(blocked, config).Diagnostics.Any(d => d.Code == "DeadEnd"), "replay distinguishes a configured dead end from an incomplete usable path");
        config.NumericOverrides["aff:trust"] = 1; check(ScenarioAnalysis.Analyze(blocked, config).DeadEnds.Count == 0, "the same structurally reachable story is playable with different scenario values");
        config.NumericOverrides["aff:trust"] = 0; b.DefaultNextId = "[EXIT]"; check(ScenarioAnalysis.Analyze(blocked, config).DeadEnds.Count == 0, "disabled/hidden responses use the existing continue fallback when none are usable");
        b.DefaultNextId = null; b.Choices[0].Visibility = VisibilityMode.Always; check(ScenarioAnalysis.Analyze(blocked, config).DeadEnds.Count == 0, "Always visibility remains usable despite unmet requirements");
        check(!ScenarioAnalysis.Analyze(blocked, config, 1).Truncated, "analysis at an exact configuration budget still checks direct endings without a false limit failure");
        var evolving = Templates.Blank(); evolving.ListOfCharacterStates[0].DefaultNextId = "start"; evolving.ListOfCharacterStates[0].DefaultAfterActions = [new() { TargetId = "counter", Amount = 1 }];
        var loop = new PlaytestScenario { StartStateId = "start", RequireEnding = false, MaxSteps = 3 }; var loopSession = ScenarioReplay.Start(evolving, loop);
        for (var i = 0; i < 8; i++) loopSession.Continue(); loop.Steps = loopSession.ReplaySteps.ToList();
        var limited = ScenarioReplay.Run(evolving, loop); check(limited.Diagnostics.Any(d => d.Code == "StepLimit") && limited.ExecutedSteps == 3 && limited.Variables["aff:counter"] == 3, "cyclic saved replay stops at a deterministic operation bound with partial coherent state");
        var limitedAnalysis = ScenarioAnalysis.Analyze(evolving, loop, 12); check(limitedAnalysis.Truncated && limitedAnalysis.ExploredConfigurations == 12 && limitedAnalysis.Diagnostics.Any(d => d.Code == "AnalysisLimit"), "evolving cyclic configurations stop at a bounded budget without claiming exhaustive verification");
        var simpleLoop = Templates.Blank(); simpleLoop.ListOfCharacterStates[0].DefaultNextId = "start";
        check(ScenarioAnalysis.Analyze(simpleLoop, new() { StartStateId = "start", RequireEnding = false }).ExploredConfigurations == 1, "unchanged runtime cycles deduplicate configurations without hanging");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        check(ScenarioReplay.Run(evolving, loop, cancellation.Token).Status == "Cancelled" && ScenarioAnalysis.Analyze(evolving, loop, cancellation: cancellation.Token).Cancelled, "Core replay and configuration analysis honor cancellation with structured diagnostics");
        throws(() => ScenarioAnalysis.Analyze(evolving, loop, 0), "configuration analysis rejects an unbounded/invalid budget");
        var saved = Path.Combine(output, "scenario-roundtrip.dialogue.json"); ProjectStore.AtomicWrite(saved, before); var loaded = ProjectStore.Load(saved);
        check(ProjectStore.Serialize(loaded) == before && loaded.PlaytestScenarios[0].Extra!.ContainsKey("FutureScenario") && loaded.PlaytestScenarios[0].Steps[0].Extra!.ContainsKey("FutureStep") && loaded.PlaytestScenarios[0].Assertions[0].Extra!.ContainsKey("FutureAssertion"), "scenario definitions and nested unknown fields survive exact desktop persistence");
        var old = ProjectStore.Parse("{\"ListOfCharacterStates\":[{\"State\":\"old\",\"Text\":\"Hi\"}],\"FutureRoot\":42}"); check(old.PlaytestScenarios.Count == 0 && old.Extra!.ContainsKey("FutureRoot"), "older desktop/website documents acquire an empty additive scenario collection without losing unknown fields");
        var webWith = ProjectStore.WebExport(project); loaded.PlaytestScenarios.Clear(); check(ProjectStore.WebExport(loaded) == webWith && !webWith.Contains("PlaytestScenarios"), "website exports remain byte-identical when desktop scenarios change");
        var history = new ProjectHistory(); history.Reset(project); var copyScenario = ScenarioStore.Duplicate(project, scenario); history.Commit(project);
        check(copyScenario.Id != scenario.Id && copyScenario.Name != scenario.Name && history.Undo()!.PlaytestScenarios.Count == 3 && history.Redo()!.PlaytestScenarios.Count == 4, "scenario duplication uses fresh identity and complete one-step history snapshots");
        copyScenario.Steps[0].SceneId = "independent"; check(scenario.Steps[0].SceneId == "arrival", "scenario duplicate owns detached steps and assertions");
        var recovery = new RecoveryStore(Path.Combine(output, "scenario-recovery")); var recoveryId = Guid.NewGuid().ToString("N"); recovery.Save(recoveryId, new(null, DateTimeOffset.UtcNow, project));
        check(recovery.FindUnsaved().Single().Record.Project.PlaytestScenarios.Count == 4, "saved scenarios survive recovery independently of transient results"); recovery.Remove(recoveryId);
        throws(() => ProjectStore.Parse(before.Replace("\"Steps\": [", "\"Steps\": [null,")), "null scenario steps are rejected before replacing a document");

        (int Exit, string Json) Cli(params string[] args) { using var writer = new StringWriter(System.Globalization.CultureInfo.InvariantCulture); var exit = StoryTestCommand.Run(args, writer); var text = writer.ToString(); using var parsed = JsonDocument.Parse(text); check(parsed.RootElement.GetProperty("ExitCode").GetInt32() == exit, "CLI JSON diagnostic exit code matches process contract"); return (exit, text); }
        check(Cli("validate", saved).Exit == 0, "Core CLI validates the committed story");
        var replayCli = Cli("replay", saved, "--all", "--analyze"); check(replayCli.Exit == 0 && replayCli.Json.Contains("SceneVisit"), "Core CLI replays saved fixtures and reports configuration checks and structured traces");
        var traceFile = Path.Combine(output, "reproducible-trace.json"); var cliAgain = Cli("replay", saved, "--all", "--analyze", "--trace", traceFile);
        check(replayCli.Json == cliAgain.Json && File.ReadAllText(traceFile).TrimEnd() == replayCli.Json.TrimEnd(), "CLI stdout and persisted trace output are reproducible across runs");
        check(Cli("replay", saved, "--scenario", scenario.Id).Exit == 0 && Cli("replay", saved, "--scenario", scenario.Name).Exit == 0, "CLI can select a scenario by stable ID or exact name");
        check(Cli("replay", saved, "--scenario", "missing").Exit == 2 && Cli("replay", saved, "--max-steps", "0").Exit == 2 && Cli("validate", saved, "--unknown").Exit == 2, "CLI usage, invalid limits and missing selectors return input failures");
        check(Cli("replay", saved, "--trace", saved).Exit == 2 && File.ReadAllText(saved) == before, "CLI refuses to overwrite the authored story with its trace");
        check(Cli("replay", saved, "--trace", output).Exit == 2, "CLI trace write failures return input/write exit code 2 after successful replay");
        check(Cli("validate", Path.Combine(output, "does-not-exist.json")).Exit == 2, "CLI missing files return structured input failures");
        var invalid = Path.Combine(output, "invalid-scenario-input.json"); File.WriteAllText(invalid, "{"); check(Cli("validate", invalid).Exit == 2, "CLI malformed JSON is reported without a crash");
        File.WriteAllText(invalid, "{\"FormatVersion\":999,\"ListOfCharacterStates\":[]}"); check(Cli("validate", invalid).Exit == 2, "CLI rejects unsupported desktop formats as input failures");
        var broken = Templates.Blank(); broken.ListOfCharacterStates[0].DefaultNextId = "missing"; File.WriteAllText(invalid, ProjectStore.Serialize(broken)); check(Cli("validate", invalid).Exit == 1, "CLI structural errors use validation exit code 1");
        var assertionFailure = ProjectStore.Clone(ProjectStore.Load(saved)); assertionFailure.PlaytestScenarios[0].Assertions[0].Number = 100; File.WriteAllText(invalid, ProjectStore.Serialize(assertionFailure)); check(Cli("replay", invalid).Exit == 3, "CLI assertion failures use scenario exit code 3");
        check(Cli("replay", saved, "--max-steps", "1").Exit == 4, "CLI deterministic replay limits use exit code 4");
        using var cancelledOutput = new StringWriter(); check(StoryTestCommand.Run(["replay", saved], cancelledOutput, cancellation.Token) == 5 && cancelledOutput.ToString().Contains("Cancelled"), "CLI cancellation uses exit code 5 with JSON diagnostics");
        File.WriteAllText(Path.Combine(output, "stage3-core-results.json"), replayCli.Json);
    }

    public static void UI(Action<bool, string> check, string output, Action<Window, string> capture)
    {
        var folder = Path.Combine(output, "scenario-ui"); Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "story.dialogue.json"); ProjectStore.AtomicWrite(path, ProjectStore.Serialize(Fixture()));
        var window = new MainWindow(null, true, Path.Combine(folder, "settings")); window.Show(); Pump(window.OpenProjectAsync(path));
        T Named<T>(Window owner, string name) where T : Control => owner.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
        Button Button(Window owner, string label) => owner.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, label));
        void Undo() => Press(window.FindControl<Button>("UndoButton")!);
        void Redo() => Press(window.FindControl<Button>("RedoButton")!);
        Window Manager() { _ = window.ShowScenariosAsync(); Dispatcher.UIThread.RunJobs(); return window.OwnedWindows.Single(w => w.IsVisible); }
        void Close(Window dialog) => Press(Button(dialog, "Close"));
        var graph = window.FindControl<GraphCanvas>("Graph")!;
        window.FindControl<ListBox>("SceneList")!.SelectedIndex = 1; Dispatcher.UIThread.RunJobs(); var selected = graph.Selected; var set = graph.SelectedScenes.ToArray(); var viewport = graph.ViewportWorld;
        var before = ProjectStore.Serialize(window.Project); var manager = Manager();
        Named<TextBox>(manager, "ScenarioName").Text = "Discarded draft"; Dispatcher.UIThread.RunJobs(); Close(manager);
        check(ProjectStore.Serialize(window.Project) == before && !window.HasUnsavedChanges, "closing unsaved scenario drafts preserves project bytes and dirty state");
        manager = Manager(); Press(Named<Button>(manager, "NewScenario")); Named<TextBox>(manager, "ScenarioName").Text = "New test"; Named<NumericUpDown>(manager, "ScenarioSeed").Value = -123;
        Dispatcher.UIThread.RunJobs(); Press(Named<Button>(manager, "SaveScenario")); Close(manager); var afterCreate = ProjectStore.Serialize(window.Project);
        check(window.Project.PlaytestScenarios.Count == 4 && window.Project.PlaytestScenarios[^1].Seed == -123 && graph.Selected == selected && graph.SelectedScenes.SequenceEqual(set), "scenario creation saves typed configuration and preserves authored selection");
        Undo(); check(ProjectStore.Serialize(window.Project) == before && graph.Selected?.State == selected?.State, "scenario creation has one complete Undo step with selection rebinding"); Redo(); check(ProjectStore.Serialize(window.Project) == afterCreate, "scenario creation has one complete Redo step");
        manager = Manager(); Named<ListBox>(manager, "ScenarioList").SelectedIndex = 0; Named<TextBox>(manager, "ScenarioName").Text = "Edited test"; Dispatcher.UIThread.RunJobs(); Press(Named<Button>(manager, "SaveScenario")); Close(manager); var edited = ProjectStore.Serialize(window.Project); Undo(); check(ProjectStore.Serialize(window.Project) == afterCreate, "scenario editing is one complete undoable definition change"); Redo(); check(ProjectStore.Serialize(window.Project) == edited, "scenario definition edits redo exactly");
        manager = Manager(); Press(Named<Button>(manager, "DuplicateScenario")); Close(manager); var duplicated = ProjectStore.Serialize(window.Project); Undo(); check(ProjectStore.Serialize(window.Project) == edited, "scenario duplication is one Undo operation"); Redo(); check(ProjectStore.Serialize(window.Project) == duplicated, "scenario duplication restores detached definitions on Redo");
        manager = Manager(); Named<ListBox>(manager, "ScenarioList").SelectedIndex = 4; Press(Named<Button>(manager, "DeleteScenario")); Close(manager); var deleted = ProjectStore.Serialize(window.Project); Undo(); check(ProjectStore.Serialize(window.Project) == duplicated, "scenario deletion is one complete Undo operation"); Redo(); check(ProjectStore.Serialize(window.Project) == deleted, "scenario deletion restores the remaining scenario collection on Redo");
        // Return to the committed three scenarios before testing nonmutating runs and Redo preservation.
        Undo(); Undo(); Undo(); Undo(); check(ProjectStore.Serialize(window.Project) == before && window.FindControl<Button>("RedoButton")!.IsEnabled, "scenario history returns exactly to the saved fixture with a usable Redo branch");
        selected = graph.Selected; set = graph.SelectedScenes.ToArray(); viewport = graph.ViewportWorld;
        manager = Manager(); Press(Named<Button>(manager, "RunAllScenarios")); PumpUntil(() => Named<Button>(manager, "RunAllScenarios").IsEnabled);
        check(Named<TextBlock>(manager, "ScenarioRunSummary").Text!.Contains("3/3 replays passed") && window.TestCoverage?.VisitedChoices == 5, "actual scenario manager executes shared replay and publishes aggregate coverage");
        check(ProjectStore.Serialize(window.Project) == before && graph.Selected == selected && graph.SelectedScenes.SequenceEqual(set) && graph.ViewportWorld == viewport && !window.HasUnsavedChanges && window.FindControl<Button>("RedoButton")!.IsEnabled, "running scenarios preserves authored project, selection, viewport, clean baseline and Redo history");
        Press(Named<Button>(manager, "ViewScenarioResults")); var resultWindow = manager.OwnedWindows.Single(w => w.IsVisible);
        check(Named<ListBox>(resultWindow, "ScenarioTrace").Items.Cast<string>().Any(s => s.Contains("SceneVisit")), "scenario result viewer exposes the structured trace and diagnostics"); capture(resultWindow, "scenario-results-dark"); Close(resultWindow); Close(manager);
        var png = Path.Combine(folder, "covered.png"); GraphImageExport.Save(window.Project, png); var exportBytes = File.ReadAllBytes(png);
        graph.SetCoverage(null); var plain = Path.Combine(folder, "plain.png"); GraphImageExport.Save(window.Project, plain);
        check(File.ReadAllBytes(plain).SequenceEqual(exportBytes), "full-story exports exclude coverage overlays while preserving every scene and connection");
        graph.SetCoverage(ScenarioReplay.Coverage(window.Project, window.Project.PlaytestScenarios.Select(s => ScenarioReplay.Run(window.Project, s))));
        graph.Focus(); var oldX = graph.Selected!.VisualX; window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, ""); window.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, ""); Dispatcher.UIThread.RunJobs();
        check(graph.Selected!.VisualX == oldX + 10 && graph.SelectedScenes.Count == set.Length, "coverage overlays preserve keyboard graph movement and authored selection"); Undo();
        check(window.TestCoverage is null, "authored changes and history replacement invalidate transient test coverage");
        manager = Manager(); Press(Named<Button>(manager, "RecordScenarioPath")); var player = manager.OwnedWindows.OfType<PlaytestWindow>().Single(w => w.IsVisible);
        Press(player.GetVisualDescendants().OfType<Button>().Single(b => b.Content is TextBlock t && t.Text == "↳  Go home")); Press(Button(player, "End conversation →"));
        Press(Named<Button>(player, "UseRecordedPath")); check(Named<ListBox>(manager, "ScenarioSteps").Items.Count == 2 && ProjectStore.Serialize(window.Project) == before, "interactive recording captures a replay path without mutating the authored story before save");
        Press(Named<Button>(manager, "SaveScenario")); Close(manager); var recorded = window.Project.PlaytestScenarios[0];
        check(recorded.Steps.Count == 2 && ScenarioReplay.Run(window.Project, recorded).Diagnostics.Any(d => d.Code == "AssertionFailed"), "recording saves a new stable path while preserving assertions that now detect changed outcomes"); Undo();
        manager = Manager(); Press(Named<Button>(manager, "AddScenarioAssertion")); var assertion = manager.OwnedWindows.Single(w => w.IsVisible);
        Named<ComboBox>(assertion, "AssertionKind").SelectedItem = "Ending"; var scenePicker = assertion.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name != "AssertionKind"); scenePicker.SelectedItem = "farewell"; Press(Named<Button>(assertion, "ApplyScenarioAssertion"));
        Press(Named<Button>(manager, "SaveScenario")); Close(manager); check(window.Project.PlaytestScenarios[0].Assertions.Count == 11, "assertion form adds a typed ending assertion to the saved scenario"); Undo();
        foreach (var theme in new[] { "Dark", "Light", "High contrast" })
        foreach (var scale in new[] { 1d, 1.15, 1.3 })
        {
            window.ChangeAppearance(theme, scale); window.Width = 1540 * scale; window.Height = 940 * scale; Dispatcher.UIThread.RunJobs(); graph.Fit();
            manager = Manager(); Press(Named<Button>(manager, "RunAllScenarios")); PumpUntil(() => Named<Button>(manager, "RunAllScenarios").IsEnabled);
            var label = "scenarios-" + theme.Replace(' ', '-') + "-" + (int)Math.Round(scale * 100);
            capture(manager, label); capture(window, label + "-coverage");
            var draftScroll = Named<ScrollViewer>(manager, "ScenarioDraftScroll"); draftScroll.Offset = new Vector(0, draftScroll.Extent.Height); Dispatcher.UIThread.RunJobs(); capture(manager, label + "-path");
            Press(Named<Button>(manager, "ViewScenarioResults")); resultWindow = manager.OwnedWindows.Single(w => w.IsVisible); capture(resultWindow, label + "-results"); resultWindow.Width = 660 * scale; resultWindow.Height = 480 * scale; Dispatcher.UIThread.RunJobs(); capture(resultWindow, label + "-results-compact"); Close(resultWindow);
            Press(Named<Button>(manager, "AddScenarioAssertion")); assertion = manager.OwnedWindows.Single(w => w.IsVisible); Named<ComboBox>(assertion, "AssertionKind").SelectedItem = "Event"; Dispatcher.UIThread.RunJobs(); capture(assertion, label + "-assertion"); assertion.Width = 420 * scale; assertion.Height = 390 * scale; Dispatcher.UIThread.RunJobs(); capture(assertion, label + "-assertion-compact"); assertion.Close();
            manager.Width = 680 * scale; manager.Height = 570 * scale; Dispatcher.UIThread.RunJobs(); capture(manager, label + "-compact");
            check(Named<Button>(manager, "SaveScenario").Bounds.Width > 0 && Named<Button>(manager, "CancelScenarioRun").Bounds.Width > 0 && Named<ListBox>(manager, "ScenarioList").Bounds.Height > 80 && manager.GetVisualDescendants().OfType<ScrollViewer>().Any(s => s.Extent.Height > s.Viewport.Height), $"scenario manager keeps actions visible and draft scrollable in compact {theme} at {scale:P0}");
            Press(Named<Button>(manager, "RecordScenarioPath")); player = manager.OwnedWindows.OfType<PlaytestWindow>().Single(w => w.IsVisible);
            capture(player, label + "-playtest"); player.Width = 680 * scale; player.Height = 530 * scale; Dispatcher.UIThread.RunJobs(); capture(player, label + "-playtest-compact");
            check(player.GetVisualDescendants().OfType<NumericUpDown>().All(n => !string.IsNullOrEmpty(AutomationProperties.GetName(n))) && Named<Button>(player, "UseRecordedPath").Bounds.Width > 0, $"scenario recorder retains labelled values and keyboard actions in compact {theme} at {scale:P0}");
            player.Close(); Close(manager);
        }
        window.Close();
    }
    private static void Press(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
    private static void Pump(Task task) { PumpUntil(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static void PumpUntil(Func<bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (!done()) { Dispatcher.UIThread.RunJobs(); if (DateTime.UtcNow > deadline) throw new Exception("UI operation did not complete"); Thread.Sleep(1); }
        Dispatcher.UIThread.RunJobs();
    }
}
