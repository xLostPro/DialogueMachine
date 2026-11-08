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
        var schemaPath = Path.Combine("Schemas", ProjectSchema.FileName);
        var schema = ProjectSchema.Generate();
        check(File.Exists(schemaPath) && File.ReadAllText(schemaPath).Replace("\r\n", "\n") == schema, $"{schemaPath} matches the project model. If stale, run: dotnet run --project CLI/DialogueMachine.Cli.csproj -c Release -- schema {schemaPath}");
        var schemaRoot = JsonNode.Parse(schema)!;
        check(schemaRoot["properties"]!["EngineExportFolder"]!["description"] is not null && schemaRoot["properties"]!["ListOfCharacterStates"]!["items"]!["properties"]!["Choices"]!["items"]!["properties"]!["AfterChoiceActions"]!["items"]!["properties"]!["Type"]!["enum"]!.AsArray().Count == 7, "project schema describes fields and lists the allowed action types");
        MiniJsonChecks(check, throws);
        RunnerChecks(check, throws);
        UnityPackageChecks(check, throws);
        UnrealPluginChecks(check, throws);
        GodotPluginChecks(check, throws);
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
            ["choices"] = choices, ["canContinue"] = runner.CanContinue, ["numeric"] = numeric, ["text"] = text, ["events"] = events.DeepClone(),
            ["speaker"] = runner.Speaker, ["portrait"] = runner.Portrait
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
        var startLines = new List<string>(); runner.LineShown += startLines.Add;
        runner.Start("second", null, new Dictionary<string, int> { ["inv:coin"] = 5, ["aff:trust"] = -3 }, null);
        check(runner.Variables.GetNumber("inv:coin") == 5 && runner.Variables.GetNumber("aff:trust") == -3 && startLines.Count == 1, "start overrides apply before the first scene resolves");
        runner.Start("second", null, new Dictionary<string, int> { ["inv:coin"] = -4 }, null);
        check(runner.Variables.GetNumber("inv:coin") == 0, "start overrides clamp inventory");

        var store = new DictionaryVariables(); store.SetNumber("inv:coin", 7);
        var gameOwned = new DialogueRunner(store); gameOwned.Load(Parse(project)); gameOwned.Start();
        check(gameOwned.Variables.GetNumber("inv:coin") == 7 && store.GetText("player_name") == "Traveller", "a game-owned store keeps its values and gains missing ones");
        store.SetText("calling", "Zoë \"the\" Plumber");
        var loaded = new DictionaryVariables(); loaded.SetNumber("aff:stale", 1); loaded.LoadJson(store.ToJson());
        check(loaded.GetNumber("inv:coin") == 7 && loaded.GetText("calling") == "Zoë \"the\" Plumber" && loaded.GetText("player_name") == "Traveller" && !loaded.HasNumber("aff:stale") && loaded.Keys.Count() == store.Keys.Count(), "DictionaryVariables saves to JSON and loading replaces every value");
        throws(() => loaded.LoadJson("[]"), "loading saved variables rejects a non-object");

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

        // EvaluateBark, SetText, escalate delivery and per-page @Speaker [portrait]: overrides
        var barkScene = Templates.Scene("bark_bed", "Bed", "Narrator", "Visit one.\n@Hero [hero_groggy]: Visit two as {player_calling}.\nVisit three.", 0, 0);
        barkScene.SayItAll = false; barkScene.Metadata["delivery"] = "escalate"; barkScene.Metadata["portrait"] = "narrator_smug"; barkScene.Metadata["entry"] = "true";
        first.Choices[1].AfterChoiceActions.Add(new() { Type = "SetText", TargetId = "player_calling", Parameter = "Plumber" });
        second.Text = "Multi-page banter.\n@Hero [hero_confused]: Wait, what?\n@Narrator [narrator_panicked]: Nothing!";
        second.Metadata["portrait"] = "narrator_calm";
        project.TextVariables["player_calling"] = "Noble Warrior";
        project.ListOfCharacterStates.Add(barkScene);
        var barkRunner = new DialogueRunner(); barkRunner.Load(Parse(project)); barkRunner.Start();
        var b1 = barkRunner.EvaluateBark("bark_bed");
        check(b1.Text == "Visit one." && b1.Speaker == "Narrator" && b1.Portrait == "narrator_smug" && barkRunner.CurrentSceneId == "first" && !barkRunner.IsEnded, "EvaluateBark resolves line 1 without interrupting active conversation");
        barkRunner.Continue(); barkRunner.Continue(); barkRunner.Choose(open);
        check(barkRunner.Variables.GetText("player_calling") == "Plumber", "SetText action updates text variable on transition");
        var b2 = barkRunner.EvaluateBark("bark_bed");
        check(b2.Text == "Visit two as Plumber." && b2.Speaker == "Hero" && b2.Portrait == "hero_groggy", "EvaluateBark advances escalation visit count and parses @Speaker [portrait]: override");
        var b3 = barkRunner.EvaluateBark("bark_bed"); var b4 = barkRunner.EvaluateBark("bark_bed");
        check(b3.Text == "Visit three." && b4.Text == "Visit three.", "escalate delivery clamps to the final line on subsequent visits");
        var gameStore = new DictionaryVariables();
        var beforeReload = new DialogueRunner(gameStore); beforeReload.Load(Parse(project)); beforeReload.Start(); beforeReload.EvaluateBark("bark_bed");
        var afterReload = new DialogueRunner(gameStore); afterReload.Load(Parse(project)); afterReload.Start();
        check(gameStore.GetNumber("visit:bark_bed") == 1 && afterReload.EvaluateBark("bark_bed").Text == "Visit two as Noble Warrior.", "a game-owned store keeps escalate visit counts when the runner is recreated");
        check(barkRunner.CurrentSceneId == "second" && barkRunner.Speaker == "Guide" && barkRunner.Portrait == "narrator_calm" && barkRunner.CurrentPage == "Multi-page banter.", "page 0 uses scene speaker and portrait");
        barkRunner.Continue();
        check(barkRunner.Speaker == "Hero" && barkRunner.Portrait == "hero_confused" && barkRunner.CurrentMetadata["portrait"] == "hero_confused" && barkRunner.CurrentPage == "Wait, what?", "page 1 applies @Hero [hero_confused]: override and strips header from CurrentPage");
        barkRunner.Continue();
        check(barkRunner.Speaker == "Narrator" && barkRunner.Portrait == "narrator_panicked" && barkRunner.CurrentPage == "Nothing!", "page 2 applies @Narrator [narrator_panicked]: override");
    }

    // The version in DialogueMachine.csproj; every engine plugin has to match it.
    private static string ProjectVersion(Action<bool, string> check)
    {
        var match = System.Text.RegularExpressions.Regex.Match(File.ReadAllText("DialogueMachine.csproj"), @"<Version>([^<]+)</Version>");
        check(match.Success, "DialogueMachine.csproj has a <Version>");
        return match.Success ? match.Groups[1].Value : "";
    }

    private static void UnityPackageChecks(Action<bool, string> check, Action<Action, string> throws)
    {
        var pkgPath = "Engines/Unity/com.xlostpro.dialoguemachine/package.json";
        check(File.Exists(pkgPath), "Unity package.json exists");
        var pkg = JsonNode.Parse(File.ReadAllText(pkgPath))!.AsObject();
        check(pkg["name"]?.GetValue<string>() == "com.xlostpro.dialoguemachine", "Unity package name is com.xlostpro.dialoguemachine");
        check(pkg["unity"]?.GetValue<string>() == "6000.5", "Unity package targets Unity 6.5 (Panel Renderer)");
        var expectedVersion = ProjectVersion(check);
        check(pkg["version"]?.GetValue<string>() == expectedVersion, "Unity package version matches DialogueMachine.csproj");
        var runnerBehSource = File.ReadAllText("Engines/Unity/com.xlostpro.dialoguemachine/Runtime/Unity/DialogueRunnerBehaviour.cs");
        var runtimeVersion = System.Text.RegularExpressions.Regex.Match(runnerBehSource, @"PluginVersion = ""([^""]+)""");
        check(runtimeVersion.Success && runtimeVersion.Groups[1].Value == expectedVersion, "Unity runtime PluginVersion matches DialogueMachine.csproj");
        check(!runnerBehSource.Contains("DEVELOPMENT_BUILD"), "Unity runtime avoids DEVELOPMENT_BUILD define inside asmdef (UAC0009)");

        var pkgDir = Path.GetFullPath("Engines/Unity/com.xlostpro.dialoguemachine");
        foreach (var entry in Directory.EnumerateFileSystemEntries(pkgDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(pkgDir, entry).Replace('\\', '/');
            if (rel.StartsWith("Samples~", StringComparison.Ordinal) || rel.EndsWith(".meta", StringComparison.Ordinal)) continue;
            var metaPath = entry + ".meta";
            check(File.Exists(metaPath) && System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(metaPath), @"\bguid:\s*[0-9a-f]{32}\b"), $"Unity package entry {rel} has a valid .meta file");
        }

        // RichText checks
        check(DialogueMachine.Runtime.Unity.RichText.ToUnity(null) == "", "RichText converts null to empty");
        check(DialogueMachine.Runtime.Unity.RichText.ToUnity("[b]bold[/b] [i]italic[/i] [u]underline[/u]") == "<b>bold</b> <i>italic</i> <u>underline</u>", "RichText converts basic tags");
        check(DialogueMachine.Runtime.Unity.RichText.ToUnity("[colour=#79DFC8]colored[/colour]") == "<color=#79DFC8>colored</color>", "RichText converts colour tag");
        check(DialogueMachine.Runtime.Unity.RichText.ToUnity("[color]stay[/color]") == "[color]stay[/color]", "RichText leaves [color] unchanged");
        check(DialogueMachine.Runtime.Unity.RichText.ToUnity("x < 5 and y > 3") == "x <\u200B 5 and y > 3", "RichText escapes raw < without altering >");
        check(DialogueMachine.Runtime.Unity.RichText.ToUnity("<b>already html</b>") == "<\u200Bb>already html<\u200B/b>", "RichText escapes existing html tags so only bracket markup becomes tags");
        var typed = DialogueMachine.Runtime.Unity.RichText.ToUnity("Hi [b]bold[/b] [colour=red]x < y[/colour]");
        check(DialogueMachine.Runtime.Unity.RichText.VisibleLength(typed) == 13, "RichText.VisibleLength skips tags and escape marks");
        check(DialogueMachine.Runtime.Unity.RichText.Reveal(typed, 5) == "Hi <b>bo", "RichText.Reveal keeps the tags before the shown characters");
        check(DialogueMachine.Runtime.Unity.RichText.Reveal(typed, 11) == "Hi <b>bold</b> <color=red>x <\u200B", "RichText.Reveal treats an escaped < as one character");
        check(DialogueMachine.Runtime.Unity.RichText.Reveal(typed, 99) == typed, "RichText.Reveal of everything is the whole text");
    }

    private static void UnrealPluginChecks(Action<bool, string> check, Action<Action, string> throws)
    {
        var upluginPath = "Engines/Unreal/DialogueMachine/DialogueMachine.uplugin";
        check(File.Exists(upluginPath), "Unreal DialogueMachine.uplugin exists");
        var uplugin = JsonNode.Parse(File.ReadAllText(upluginPath))!.AsObject();
        var expectedVersion = ProjectVersion(check);
        check(uplugin["VersionName"]?.GetValue<string>() == expectedVersion, "Unreal plugin VersionName matches DialogueMachine.csproj");

        var ueConformance = "Engines/Unreal/DialogueMachine/Resources/Conformance";
        check(Directory.Exists(ueConformance), "Unreal plugin Conformance directory exists");
        var originalFiles = Directory.GetFiles(Path.GetFullPath(Folder), "*.json").ToDictionary(f => Path.GetFileName(f), f => File.ReadAllText(f).Replace("\r\n", "\n"));
        var ueFiles = Directory.GetFiles(Path.GetFullPath(ueConformance), "*.json").ToDictionary(f => Path.GetFileName(f), f => File.ReadAllText(f).Replace("\r\n", "\n"));
        check(ueFiles.Keys.Order().SequenceEqual(originalFiles.Keys.Order()), "Unreal plugin has the exact same conformance fixtures");
        foreach (var (fileName, content) in originalFiles)
        {
            check(ueFiles.TryGetValue(fileName, out var ueContent) && ueContent == content, $"Unreal conformance fixture {fileName} matches Engines/Conformance");
        }
    }

    private static void GodotPluginChecks(Action<bool, string> check, Action<Action, string> throws)
    {
        var pluginCfgPath = "Engines/Godot/addons/dialogue_machine/plugin.cfg";
        check(File.Exists(pluginCfgPath), "Godot plugin.cfg exists");
        var cfgContent = File.ReadAllText(pluginCfgPath);
        var expectedVersion = ProjectVersion(check);
        var versionMatch = System.Text.RegularExpressions.Regex.Match(cfgContent, @"version=""([^""]+)""");
        check(versionMatch.Success && versionMatch.Groups[1].Value == expectedVersion, "Godot plugin.cfg version matches DialogueMachine.csproj");

        var godotConformance = "Engines/Godot/addons/dialogue_machine/tests/conformance";
        check(Directory.Exists(godotConformance), "Godot addon Conformance directory exists");
        var originalFiles = Directory.GetFiles(Path.GetFullPath(Folder), "*.json").ToDictionary(f => Path.GetFileName(f), f => File.ReadAllText(f).Replace("\r\n", "\n"));
        var godotFiles = Directory.GetFiles(Path.GetFullPath(godotConformance), "*.json").ToDictionary(f => Path.GetFileName(f), f => File.ReadAllText(f).Replace("\r\n", "\n"));
        check(godotFiles.Keys.Order().SequenceEqual(originalFiles.Keys.Order()), "Godot addon has the exact same conformance fixtures");
        foreach (var (fileName, content) in originalFiles)
        {
            check(godotFiles.TryGetValue(fileName, out var gContent) && gContent == content, $"Godot conformance fixture {fileName} matches Engines/Conformance");
        }

        check(File.Exists("Engines/Godot/project.godot"), "Godot sample project.godot exists");
    }
}
