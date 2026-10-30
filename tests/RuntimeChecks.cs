using DialogueMachine.Core;
using DialogueMachine.Runtime;
using System.Text.Json.Nodes;

internal static class RuntimeChecks
{
    private const string Folder = "Engines/Conformance";
    private const string Regenerate = "dotnet run --project CLI/DialogueMachine.Cli.csproj -c Release -- conformance Engines/Conformance";

    public static void Core(Action<bool, string> check, Action<Action, string> throws)
    {
        var generated = ConformanceFixtures.Generate();
        var files = Directory.GetFiles(Path.GetFullPath(Folder), "*.json").ToDictionary(f => Path.GetFileNameWithoutExtension(f), f => File.ReadAllText(f).Replace("\r\n", "\n"));
        foreach (var (name, json) in generated)
            check(files.TryGetValue(name, out var file) && file == json, files.TryGetValue(name, out file) && file == json ? $"conformance fixture {name} matches Core" : $"Conformance fixture {name} is stale. Run: {Regenerate}");
        check(files.Keys.Order().SequenceEqual(generated.Keys.Order()), $"{Folder} holds exactly the generated fixtures");
        check(files.Count >= 14, "at least 14 conformance cases are checked in");
        foreach (var (name, json) in files) { Replay(name, json, check); check(true, $"shared runner matches conformance fixture {name}"); }
        MiniJsonChecks(check, throws);
        RunnerChecks(check, throws);
    }

    private static void Replay(string name, string json, Action<bool, string> check)
    {
        var fixture = JsonNode.Parse(json)!;
        var inputs = fixture["inputs"]!.AsArray(); var expected = fixture["expected"]!.AsArray();
        var events = new JsonArray(); var runner = new DialogueRunner(); var saved = "";
        runner.ActionRaised += (type, target, parameter, _) => events.Add(new JsonObject { ["type"] = type, ["targetId"] = target, ["parameter"] = parameter });
        runner.ShopOpened += shop => events.Add(new JsonObject { ["type"] = "Shop", ["targetId"] = shop, ["parameter"] = null });
        runner.Load(fixture["handoff"]!.ToJsonString());
        for (var i = 0; i < inputs.Count; i++)
        {
            var input = inputs[i]!; var op = input["op"]!.GetValue<string>(); events.Clear();
            switch (op)
            {
                case "start": runner.Start(); break;
                case "continue": runner.Continue(); break;
                case "choose": runner.Choose(input["choiceId"]!.GetValue<string>()); break;
                case "setNumber": runner.Variables.SetNumber(input["key"]!.GetValue<string>(), input["value"]!.GetValue<int>()); break;
                case "setText": runner.Variables.SetText(input["key"]!.GetValue<string>(), input["value"]!.GetValue<string>()); break;
                case "save": saved = runner.SaveState(); break;
                case "restore": runner.RestoreState(saved); break;
                default: throw new Exception($"{name}: unknown fixture op {op}");
            }
            var actual = JsonNode.Parse(Snapshot(runner, events).ToJsonString())!.AsObject();
            foreach (var (field, value) in expected[i]!.AsObject())
                if (actual[field]?.ToJsonString() != value?.ToJsonString()) check(false, $"{name}: input {i} ({op}) {field}: expected {value?.ToJsonString()}, got {actual[field]?.ToJsonString()}");
        }
    }

    private static JsonObject Snapshot(DialogueRunner runner, JsonArray events)
    {
        var numeric = new JsonObject(); var text = new JsonObject();
        foreach (var key in runner.Variables.Keys.Order(StringComparer.Ordinal))
        {
            if (runner.Variables.HasNumber(key)) numeric[key] = runner.Variables.GetNumber(key);
            if (runner.Variables.GetText(key) is { } value) text[key] = value;
        }
        var choices = new JsonArray();
        foreach (var c in runner.Choices) choices.Add(new JsonObject { ["id"] = c.Id, ["text"] = c.Text, ["enabled"] = c.Enabled });
        return new JsonObject
        {
            ["sceneId"] = runner.CurrentSceneId, ["ended"] = runner.IsEnded, ["page"] = runner.CurrentPage, ["plain"] = runner.CurrentPagePlain, ["pageIndex"] = runner.PageIndex, ["pageCount"] = runner.PageCount,
            ["choices"] = choices, ["canContinue"] = runner.CanContinue, ["numeric"] = numeric, ["text"] = text, ["events"] = events.DeepClone()
        };
    }

    private static Handoff Parse(DialogueProject project, int seed = 0) => Handoff.Parse(RuntimeExport.Serialize(RuntimeExport.Create(project, seed)));

    private static void MiniJsonChecks(Action<bool, string> check, Action<Action, string> throws)
    {
        var handoff = RuntimeExport.Serialize(RuntimeExport.Create(ProjectStore.Load("Examples/lighthouse.dialogue.json")));
        check(JsonNode.DeepEquals(JsonNode.Parse(handoff), JsonNode.Parse(MiniJson.Serialize(MiniJson.Parse(handoff)))), "MiniJson round-trips the example handoff");
        var story = Parse(ProjectStore.Load("Examples/lighthouse.dialogue.json")).Story;
        check(story.Scenes.Count == 5 && story.Find("arrival")!.Choices.Count == 3 && story.Find("arrival")!.Speaker == "Mara" && story.Find("gift")!.DefaultNextId == "keeper", "the handoff parser reads the example story");
        var future = Handoff.Parse("""{"SchemaVersion":1,"Future":[{"a":1}],"StoryJson":"{\"ListOfCharacterStates\":[{\"State\":\"a\",\"Future\":{\"b\":2}}],\"Future\":true}","NumericValues":{"aff:a":2,"aff:bad":"x"}}""");
        check(future.Story.Find("a") is { SayItAll: true, DefaultNextId: "[EXIT]", Type: "Dialogue" } && future.NumericValues.Count == 1, "the handoff parser ignores unknown fields and uses Core's defaults");
        check((string)MiniJson.Parse(""" "\ud83c\udf0a\n\u00e9" """)! == "🌊\né", "MiniJson decodes escapes and surrogate pairs");
        throws(() => Handoff.Parse("""{"SchemaVersion":2,"StoryJson":"{\"ListOfCharacterStates\":[]}"}"""), "an unsupported handoff schema is refused");
        throws(() => Handoff.Parse("""{"SchemaVersion":1}"""), "a handoff without a story is refused");
        foreach (var bad in new[] { "", "{", "[1,]", "{\"a\":1}x", "\"\\q\"", "nul", new string('[', 100) + new string(']', 100) })
            throws(() => MiniJson.Parse(bad), $"MiniJson rejects malformed input {bad[..Math.Min(bad.Length, 8)]}");
    }

    private static void RunnerChecks(Action<bool, string> check, Action<Action, string> throws)
    {
        var first = Templates.Scene("first", "First title", "Mara", "One.\nTwo.\nThree.", 0, 0); first.Metadata["portrait"] = "mara_smile"; first.DefaultNextId = "second";
        first.Choices = [new() { InternalId = Guid.NewGuid(), ButtonText = "Locked", NextStateId = "second", Requirements = [new() { TargetId = "trust", Value = "5" }] }, new() { InternalId = Guid.NewGuid(), ButtonText = "Open", NextStateId = "second" }];
        var second = Templates.Scene("second", "Second title", "Guide", "Done.", 0, 0);
        var project = new DialogueProject { EntryStateId = "first", ListOfCharacterStates = [first, second], InitialVariables = new() { ["inv:coin"] = 2 } };
        var locked = first.Choices[0].InternalId.ToString(); var open = first.Choices[1].InternalId;

        var runner = new DialogueRunner();
        throws(() => runner.Start(), "starting without a story is refused");
        var ended = new List<(string?, string?)>(); var changes = new List<string>(); var lines = new List<string>();
        runner.Ended += (scene, choice) => ended.Add((scene, choice));
        runner.VariableChanged += (key, before, after) => changes.Add($"{key}:{before}>{after}");
        runner.LineShown += lines.Add;
        runner.Load(Parse(project)); runner.Start();
        check(runner.Title == "First title" && runner.Speaker == "Mara" && runner.CurrentMetadata["portrait"] == "mara_smile" && lines.SequenceEqual(new[] { "One." }), "the runner exposes scene details and shows the first line");
        check(runner.Choices[0].DisabledReason!.Contains("aff:trust") && runner.Choices[1].DisabledReason is null, "disabled choices explain themselves");
        throws(() => runner.Choose(open), "choosing is refused until the pages are finished");
        runner.Continue(); runner.Continue();
        throws(() => runner.Choose(locked), "a disabled choice cannot be chosen");
        throws(() => runner.Choose(Guid.NewGuid()), "an unknown choice cannot be chosen");
        runner.Variables.SetNumber("inv:coin", -9);
        check(runner.Variables.GetNumber("inv:coin") == 0 && changes.SequenceEqual(new[] { "inv:coin:2>0" }), "variable writes clamp inventory and report the change");
        runner.Choose(open); runner.Continue();
        check(runner.IsEnded && runner.CurrentPage is null && ended.SequenceEqual(new (string?, string?)[] { ("second", null) }), "the ending reports its scene");

        runner.Start("second");
        check(runner.CurrentSceneId == "second" && runner.Variables.GetNumber("inv:coin") == 2 && !runner.IsEnded, "starting again resets the runner's own variables at the chosen scene");
        throws(() => runner.Start("missing"), "starting at a missing scene is refused");

        var store = new DictionaryVariables(); store.SetNumber("inv:coin", 7);
        var gameOwned = new DialogueRunner(store); gameOwned.Load(Parse(project)); gameOwned.Start();
        check(gameOwned.Variables.GetNumber("inv:coin") == 7 && store.GetText("player_name") == "Traveller", "a game-owned store keeps its values and gains missing ones");

        var reload = new DialogueRunner(); var warnings = new List<string>(); reload.Warning += warnings.Add;
        reload.Load(Parse(project)); reload.Start(); reload.Continue(); reload.Continue(); reload.Variables.SetNumber("inv:coin", 5);
        var shorter = ProjectStore.Clone(project); shorter.ListOfCharacterStates[0].Text = "Only page."; shorter.InitialVariables["aff:new"] = 3;
        reload.ReplaceStory(Parse(shorter));
        check(reload.CurrentPage == "Only page." && reload.PageIndex == 0 && reload.Variables.GetNumber("inv:coin") == 5 && reload.Variables.GetNumber("aff:new") == 3, "hot reload keeps variables and clamps the page");
        var renamed = ProjectStore.Clone(project); DialogueEngine.Rename(renamed, renamed.ListOfCharacterStates[0], "renamed");
        reload.ReplaceStory(Parse(renamed));
        check(reload.IsEnded && reload.CurrentSceneId is null && warnings.Count == 1 && warnings[0].Contains("first"), "hot reload warns and ends when the current scene is gone");

        var saved = new DialogueRunner(); saved.Load(Parse(project)); saved.Start(); saved.Continue();
        var save = MiniJson.Parse(saved.SaveState()) as Dictionary<string, object>;
        check(save!["sceneId"] as string == "first" && (double)save["pageIndex"] == 1 && save["randomState"] is string && (double)save["randomLine"] == -1 && save.ContainsKey("storySha256"), "save states use the contract's fields");
        saved.Continue(); saved.Choose(open); saved.Continue(); var endedSave = saved.SaveState();
        saved.Start(); saved.RestoreState(endedSave);
        check(saved.IsEnded && saved.CurrentSceneId is null, "an ended conversation restores as ended");
        throws(() => saved.RestoreState("""{"sceneId":"missing"}"""), "restoring into a missing scene is refused");
    }
}
