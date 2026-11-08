using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DialogueMachine.Core;
using DialogueMachine.Core.LiveLink;
using DialogueMachine.Runtime;
using DialogueMachine.UI;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;

internal static class LiveLinkChecks
{
    private const string AppVersion = "1.7.0";

    // A scripted engine. It reads on a background thread so tests can pump the UI meanwhile.
    private sealed class ScriptedEngine : IDisposable
    {
        private readonly ClientWebSocket socket = new();
        private readonly ConcurrentQueue<JsonNode> inbox = new();
        private readonly Action pump;
        public volatile bool Closed;
        public ScriptedEngine(int port, Action? pump = null, string? origin = null)
        {
            this.pump = pump ?? (() => Thread.Sleep(5));
            if (origin is not null) socket.Options.SetRequestHeader("Origin", origin);
            var connect = Task.Run(() => socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/dialogue-machine/"), new CancellationTokenSource(5000).Token));
            Until(() => connect.IsCompleted, "the scripted engine connects");
            connect.GetAwaiter().GetResult();
            _ = Task.Run(Read);
        }
        private async Task Read()
        {
            var buffer = new byte[1024 * 1024]; using var message = new MemoryStream();
            try
            {
                while (true)
                {
                    var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                    message.Write(buffer, 0, result.Count);
                    if (!result.EndOfMessage) continue;
                    inbox.Enqueue(JsonNode.Parse(Encoding.UTF8.GetString(message.ToArray()))!); message.SetLength(0);
                }
            }
            catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException or IOException) { }
            Closed = true;
        }
        public void Send(string type, JsonNode? payload = null) => SendRaw(new JsonObject { ["protocol"] = 1, ["type"] = type, ["payload"] = payload ?? new JsonObject() }.ToJsonString());
        public void SendRaw(string text) { var send = Task.Run(() => socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, CancellationToken.None)); Until(() => send.IsCompleted, "a scripted message is sent"); }
        public void Hello(string token, int protocol = 1, string pluginVersion = AppVersion) => Send("hello", new JsonObject { ["token"] = token, ["engine"] = "unity", ["engineVersion"] = "6000.5", ["pluginVersion"] = pluginVersion, ["protocol"] = protocol, ["schemaVersion"] = 1 });
        public JsonNode Expect(string type)
        {
            JsonNode? found = null;
            Until(() => { while (found is null && inbox.TryDequeue(out var next)) if (next["type"]!.GetValue<string>() == type) found = next; return found is not null; }, "the engine receives " + type);
            return found!;
        }
        public void Drain() { while (inbox.TryDequeue(out _)) { } }
        public bool Has(string type) => inbox.Any(m => m["type"]!.GetValue<string>() == type);
        public void Until(Func<bool> condition, string what)
        {
            var deadline = Environment.TickCount64 + 10000;
            while (!condition()) { if (Environment.TickCount64 > deadline) throw new Exception("Timed out waiting until " + what); pump(); }
        }
        public void WaitClosed() => Until(() => Closed, "the server closes the connection");
        public void Dispose() { socket.Abort(); socket.Dispose(); }
    }

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop(); return port;
    }
    private static void Wait(Func<bool> condition, string what)
    {
        var deadline = Environment.TickCount64 + 10000;
        while (!condition()) { if (Environment.TickCount64 > deadline) throw new Exception("Timed out waiting until " + what); Thread.Sleep(5); }
    }
    private static LiveRunnerState State(string status, string? scene, string project = "Story", string sha = "sha", string? choice = null, int aff = 0, string runner = "1", string client = "1")
        => new(client, "unity", runner, "Mara", project, sha, status, scene, 0, choice, new Dictionary<string, int> { ["aff:trust"] = aff }, new Dictionary<string, string> { ["player_name"] = "Ada" });

    public static void Core(Action<bool, string> check, Action<Action, string> throws, string output)
    {
        var folder = Path.Combine(output, "livelink"); Directory.CreateDirectory(folder);
        var discovery = Path.Combine(folder, "livelink.json"); if (File.Exists(discovery)) File.Delete(discovery);
        var project = Templates.Harbour();
        var server = new LiveLinkServer(new(discovery, AppVersion) { Port = FreePort() });
        server.Publish(project.ProjectName, RuntimeExport.Create(project), false);
        check(!File.Exists(discovery), "the discovery file only appears once the server starts");
        server.Start();

        var found = JsonNode.Parse(File.ReadAllText(discovery))!;
        check(found["port"]!.GetValue<int>() == server.Port && found["token"]!.GetValue<string>() == server.Token && server.Token.Length == 32 && found["pid"]!.GetValue<int>() == Environment.ProcessId && found["appVersion"]!.GetValue<string>() == AppVersion && found["protocol"]!.GetValue<int>() == 1, "the discovery file gives engines the port, token, pid, version and protocol");
        if (!OperatingSystem.IsWindows()) check(File.GetUnixFileMode(discovery) == (UnixFileMode.UserRead | UnixFileMode.UserWrite), "only the current user can read the discovery file");

        using (var http = new HttpClient())
        {
            check(http.GetAsync($"http://127.0.0.1:{server.Port}/dialogue-machine/").GetAwaiter().GetResult().StatusCode == HttpStatusCode.BadRequest, "plain HTTP requests are refused");
            var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{server.Port}/dialogue-machine/"); request.Headers.Add("Origin", "https://example.com");
            check(http.Send(request).StatusCode == HttpStatusCode.Forbidden, "requests with an Origin header are refused");
        }
        throws(() => new ScriptedEngine(server.Port, origin: "https://example.com"), "a browser-style WebSocket with an Origin header cannot connect");
        using (new ScriptedEngine(server.Port, origin: "http://127.0.0.1")) check(true, "Unreal's fixed loopback Origin is accepted");

        var engine = new ScriptedEngine(server.Port);
        engine.Hello(server.Token);
        var welcome = engine.Expect("welcome")["payload"]!;
        check(welcome["appVersion"]!.GetValue<string>() == AppVersion && welcome["protocol"]!.GetValue<int>() == 1 && welcome["schemaVersion"]!.GetValue<int>() == 1 && welcome["contract"]!.GetValue<string>() == RuntimeExport.Contract && welcome["projectName"]!.GetValue<string>() == project.ProjectName && welcome["updateUrl"] is null, "hello gets a welcome, with no update offer for a current plugin");
        var connected = engine.Expect("story.updated")["payload"]!;
        check(connected["StorySha256"]!.GetValue<string>() == RuntimeExport.Create(project).StorySha256 && connected["ProjectName"]!.GetValue<string>() == project.ProjectName, "a new engine receives the story on connect");
        check(Handoff.Parse(connected.ToJsonString()).Story.Find(project.Entry) is not null, "the pushed story parses with the shared runtime, ignoring the extra field");
        Wait(() => server.ClientCount == 1, "the engine is counted");

        engine.Send("story.request"); check(engine.Expect("story.updated") is not null, "story.request sends the current story");
        engine.SendRaw("""{"protocol":1,"type":"future.message","payload":{}}"""); engine.SendRaw("not json"); engine.SendRaw("[1,2]");
        engine.Send("ping"); engine.Expect("pong");
        check(!engine.Closed, "unknown types and malformed frames are ignored");

        var edited = Templates.Harbour(); edited.ListOfCharacterStates[0].Text = "Saved again.";
        server.Publish(edited.ProjectName, RuntimeExport.Create(edited));
        check(engine.Expect("story.updated")["payload"]!["StorySha256"]!.GetValue<string>() == server.StorySha256, "story.updated arrives after a save");
        var goodSha = server.StorySha256;
        var broken = Templates.Harbour(); broken.ListOfCharacterStates[0].DefaultNextId = "missing";
        server.PublishInvalid(broken.ProjectName, DialogueEngine.Validate(broken).Where(d => d.Severity == "Error"));
        var diagnostics = engine.Expect("story.invalid")["payload"]!["diagnostics"]!.AsArray();
        check(diagnostics.Count > 0 && diagnostics[0]!["message"] is not null && server.StorySha256 == goodSha, "story.invalid carries diagnostics and the last good story is kept");
        engine.Send("story.request"); engine.Expect("story.invalid");
        server.Publish(edited.ProjectName, RuntimeExport.Create(edited));

        var old = new ScriptedEngine(server.Port); old.Hello(server.Token, pluginVersion: "1.2.0");
        check(old.Expect("welcome")["payload"]!["updateUrl"]!.GetValue<string>() == LiveLinkServer.UpdateUrl, "an older plugin still connects and is offered the update");
        old.Dispose();

        var wrong = new ScriptedEngine(server.Port); wrong.Hello("wrong");
        check(wrong.Expect("error")["payload"]!["code"]!.GetValue<string>() == "unauthorized", "a bad token gets an error"); wrong.WaitClosed(); check(wrong.Closed, "a bad token closes the connection"); wrong.Dispose();
        var early = new ScriptedEngine(server.Port); early.Send("story.request"); early.Expect("error"); early.WaitClosed(); check(early.Closed, "no message is served before hello"); early.Dispose();
        var incompatible = new ScriptedEngine(server.Port); incompatible.Hello(server.Token, protocol: 2);
        check(incompatible.Expect("error")["payload"]!["code"]!.GetValue<string>() == "incompatible", "a different protocol is refused as incompatible"); incompatible.WaitClosed(); incompatible.Dispose();

        var huge = new ScriptedEngine(server.Port); huge.Hello(server.Token); huge.Expect("welcome");
        huge.SendRaw(new string('x', LiveLinkServer.MaxMessageBytes + 1));
        check(huge.Expect("error")["payload"]!["code"]!.GetValue<string>() == "too-large", "messages over 16 MB are refused"); huge.WaitClosed(); huge.Dispose();

        Wait(() => server.ClientCount == 1, "closed engines are no longer counted");
        Thread.Sleep(300); // Refused connections release their slot just after the socket closes.
        var crowd = Enumerable.Range(0, LiveLinkServer.MaxClients - 1).Select(_ => new ScriptedEngine(server.Port)).ToList();
        foreach (var member in crowd) { member.Hello(server.Token); member.Expect("welcome"); }
        check(server.ClientCount == LiveLinkServer.MaxClients, "eight engines can connect");
        throws(() => new ScriptedEngine(server.Port), "a ninth engine is refused");
        foreach (var member in crowd) member.Dispose();
        Wait(() => server.ClientCount == 1, "aborted engines are removed");

        LiveRunnerState? state = null; LiveRunnerEvent? raised = null; string? reveal = null;
        server.RunnerState += s => state = s; server.RunnerEvent += e => raised = e; server.RevealRequested += s => reveal = s;
        engine.Send("runtime.state", new JsonObject { ["runnerId"] = "7", ["runnerName"] = "Mara", ["projectName"] = "Story", ["storySha256"] = "abc", ["status"] = "started", ["sceneId"] = "arrival", ["pageIndex"] = 2, ["choiceId"] = null, ["numeric"] = new JsonObject { ["aff:trust"] = 3, ["aff:bad"] = "x" }, ["text"] = new JsonObject { ["player_name"] = "Ada" } });
        engine.Send("runtime.state", new JsonObject { ["runnerId"] = "7", ["status"] = "mystery" });
        engine.Send("runtime.event", new JsonObject { ["runnerId"] = "7", ["type"] = "Event", ["targetId"] = "door", ["parameter"] = "open" });
        engine.Send("editor.reveal", new JsonObject { ["sceneId"] = "keeper" });
        Wait(() => reveal is not null, "the reveal arrives");
        check(state is { RunnerId: "7", SceneId: "arrival", PageIndex: 2, Status: "started", Engine: "unity" } && state.Numeric.Count == 1 && state.Numeric["aff:trust"] == 3 && state.Text["player_name"] == "Ada", "runtime.state parses and skips unknown statuses and bad values");
        check(raised is { Type: "Event", TargetId: "door", Parameter: "open" } && reveal == "keeper", "runtime.event and editor.reveal reach the editor");
        server.PlayFrom("keeper", 9, new Dictionary<string, int> { ["aff:trust"] = 1 }, new Dictionary<string, string> { ["player_name"] = "Ada" });
        var play = engine.Expect("play.from")["payload"]!;
        check(play["sceneId"]!.GetValue<string>() == "keeper" && play["seed"]!.GetValue<int>() == 9 && play["numeric"]!["aff:trust"]!.GetValue<int>() == 1 && play["text"]!["player_name"]!.GetValue<string>() == "Ada", "play.from reaches the engine");
        server.SetVariable(state.ClientId, "7", "aff:trust", 4, null); server.SetVariable(state.ClientId, "7", "player_name", null, "Bo");
        var number = engine.Expect("runtime.setVariable")["payload"]!; var text = engine.Expect("runtime.setVariable")["payload"]!;
        check(number["number"]!.GetValue<int>() == 4 && number["text"] is null && text["text"]!.GetValue<string>() == "Bo" && text["number"] is null, "runtime.setVariable sends a number or a text value");

        // A taken port moves the server on instead of failing.
        var second = new LiveLinkServer(new(Path.Combine(folder, "second.json"), AppVersion) { Port = server.Port }); second.Start();
        check(second.Port > server.Port && second.Port < server.Port + LiveLinkServer.PortAttempts, "a busy port falls through to the next one");
        var secondDiscovery = Path.Combine(folder, "second.json"); second.Dispose(); check(!File.Exists(secondDiscovery), "each server removes its own discovery file");

        engine.Dispose();
        server.Dispose(); check(!File.Exists(discovery), "shutdown deletes the discovery file"); server.Dispose();
        throws(() => new ScriptedEngine(server.Port), "nothing listens after shutdown");

        var timed = new LiveLinkServer(new(discovery, AppVersion) { Port = FreePort(), HelloTimeout = TimeSpan.FromMilliseconds(200), PingInterval = TimeSpan.FromMilliseconds(100), SilenceTimeout = TimeSpan.FromMilliseconds(700) }); timed.Start();
        var silent = new ScriptedEngine(timed.Port); silent.Expect("error"); silent.WaitClosed(); check(silent.Closed, "a connection without hello is closed in time"); silent.Dispose();
        var quiet = new ScriptedEngine(timed.Port); quiet.Hello(timed.Token); quiet.Expect("welcome"); quiet.Expect("ping");
        quiet.WaitClosed(); check(quiet.Closed, "the server pings and drops an engine after the silence limit"); quiet.Dispose(); timed.Dispose();
        var replaced = new LiveLinkServer(new(discovery, AppVersion) { Port = FreePort() }); replaced.Start();
        File.WriteAllText(discovery, """{"pid":-5}"""); replaced.Dispose();
        check(File.Exists(discovery), "shutdown leaves a discovery file another copy wrote"); File.Delete(discovery);

        FollowChecks(check);
    }

    private static void FollowChecks(Action<bool, string> check)
    {
        var project = Templates.Harbour(); project.ProjectName = "Story"; var choice = project.ListOfCharacterStates[0].Choices[0];
        var first = project.ListOfCharacterStates[0].State; var next = choice.NextStateId;
        var follow = new LiveFollow();
        follow.Apply(State("started", first));
        follow.Apply(State("step", first, aff: 1));
        follow.Apply(State("step", next, choice: choice.InternalId.ToString(), aff: 2));
        check(follow.CurrentSceneId == next && follow.TrailScenes.SetEquals([first, next]) && follow.TrailLinks.Single() == (first, next) && follow.TrailChoices.Single() == new ChoiceCoverage(first, choice.InternalId) && follow.Followed!.Numeric["aff:trust"] == 2, "follow tracks the scene, trail, response taken and variables");
        follow.Apply(State("ended", null, aff: 2));
        check(follow.Ended && follow.CurrentSceneId == next && follow.TrailScenes.Count == 2, "an ended conversation keeps its trail");
        follow.AddEvent(new("1/1", "Event", "door", "open")); follow.AddEvent(new("1/other", "Event", "ignored", null));
        check(follow.Events.Single() == "Event · door · open", "only the followed runner's events are listed");

        follow.Apply(State("step", first, runner: "2", client: "2"));
        check(follow.Followed!.RunnerId == "1" && follow.Runners.Count == 2 && follow.CurrentSceneId == next, "other runners only update the dropdown");
        follow.Apply(State("started", next, runner: "2", client: "2"));
        check(follow.Followed!.RunnerId == "2" && follow.TrailScenes.SetEquals([next]) && !follow.Ended && follow.Events.Count == 0, "the newest started runner is followed from a fresh trail");
        follow.Follow("1/1");
        check(follow.Followed!.RunnerId == "1" && follow.Ended, "the dropdown can pick another runner");

        check(follow.Check(project, "sha") == new LiveFollowStatus(true, null, false), "a matching game gets a highlight and no notice");
        check(follow.Check(project, "newer") is { Highlight: true, Outdated: true, Notice: "Game is on an older version." }, "a different story hash offers Push update");
        check(follow.Check(new DialogueProject { ProjectName = "STORY" }, "sha") is { Highlight: true, Notice: null }, "a game named after its file still matches the project");
        check(follow.Check(new DialogueProject { ProjectName = "Other" }, "sha") is { Highlight: false, Notice: "Game is running Story" }, "a different project shows what the game runs and highlights nothing");
        follow.Apply(State("step", "not_here"));
        check(follow.Check(project, "sha") is { Highlight: false, Outdated: false } status && status.Notice!.Contains("not_here"), "an unknown scene shows a notice instead of a highlight");

        follow.RemoveClient("1");
        check(follow.Disconnected && follow.Followed is not null && follow.Runners.Single().RunnerId == "2", "a dropped engine leaves the dropdown and keeps its trail");
        follow.Stop();
        follow.Apply(State("step", first, runner: "2", client: "2"));
        check(follow.Followed is null && follow.TrailScenes.Count == 0, "Stop following clears the trail and stays stopped");
        follow.Apply(State("started", first, runner: "2", client: "2"));
        check(follow.Followed?.RunnerId == "2", "a new conversation can be followed after stopping");

        var random = new LiveFollow(); var scene = project.ListOfCharacterStates[0]; scene.Text = "[if aff:trust >= 1]Hello {player_name}[else]No[/if]\nTwo"; scene.SayItAll = true;
        random.Apply(State("started", scene.State, aff: 1));
        check(random.CurrentLine(project) == "Hello Ada", "the current line resolves with the game's variables");
        scene.SayItAll = false;
        check(random.CurrentLine(project) == "Hello Ada\nTwo", "a random scene shows every candidate line");
        var late = new LiveFollow(); late.Apply(State("step", first));
        check(late.Followed is not null, "a game already mid-conversation is picked up");
    }

    public static void UI(Action<bool, string> check, string output, Action<Window, string> capture)
    {
        var folder = Path.Combine(output, "livelink-ui"); if (Directory.Exists(folder)) Directory.Delete(folder, true); Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "story.dialogue.json");
        var story = Templates.Harbour(); ProjectStore.AtomicWrite(path, ProjectStore.Serialize(story));
        var window = new MainWindow(null, testMode: true, dataDirectory: folder); window.Show();
        // RunJobs skips timers; the real loop also exercises the redraw throttle and edit debounce.
        void Pump(int ms = 5) { using var slice = new CancellationTokenSource(ms); Dispatcher.UIThread.MainLoop(slice.Token); }
        void Finish(Task task) { while (!task.IsCompleted) Pump(); task.GetAwaiter().GetResult(); }
        T Named<T>(string name) where T : Control => window.GetVisualDescendants().OfType<T>().First(c => c.Name == name);
        void Press(string name) { Named<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(); }
        void Settle() { for (var i = 0; i < 12; i++) Pump(); window.RefreshFollow(); Dispatcher.UIThread.RunJobs(); }
        Finish(window.OpenProjectAsync(path));
        var snapshot = ProjectStore.Serialize(window.Project);
        var toggle = window.FindControl<ToggleButton>("LiveLinkButton")!; var status = window.FindControl<TextBlock>("LiveLinkStatus")!; var graph = window.FindControl<GraphCanvas>("Graph")!;
        var undo = window.FindControl<Button>("UndoButton")!;

        check(window.LiveLink is null && toggle.IsChecked == false && !status.IsVisible && !File.Exists(window.LiveLinkDiscoveryPath), "the live link is off by default");
        var offscreen = window.FindControl<Button>("PlayEngineButton")!; check(!offscreen.IsVisible, "Play in engine only shows with the live link on");
        toggle.IsChecked = true; Pump();
        var server = window.LiveLink!;
        check(server is not null && File.Exists(window.LiveLinkDiscoveryPath) && offscreen.IsVisible && status.Text == "Live link: waiting for engines", "the toggle starts the server and shows its status");

        var engine = new ScriptedEngine(server.Port, () => Pump());
        engine.Hello(server.Token); engine.Expect("welcome");
        check(engine.Expect("story.updated")["payload"]!["StorySha256"]!.GetValue<string>() == server.StorySha256, "the open story is pushed on connect");
        engine.Until(() => status.Text == "Live link: 1 engine connected", "the status counts the engine");
        var other = new ScriptedEngine(server.Port, () => Pump()); other.Hello(server.Token); other.Expect("welcome");
        engine.Until(() => status.Text == "Live link: 2 engines connected", "the status counts both engines"); other.Dispose();
        engine.Until(() => status.Text == "Live link: 1 engine connected", "the status drops the closed engine");

        var name = window.Project.ProjectName; var sha = server.StorySha256!;
        var scenes = window.Project.ListOfCharacterStates; var first = scenes[0]; var choice = first.Choices[0]; var second = scenes.First(s => s.State == choice.NextStateId); var third = second.Choices.Count > 0 ? second.Choices[0].NextStateId : second.DefaultNextId!;
        JsonObject Runner(string state, string? scene, string? choiceId = null, int trust = 0, string? project = null, string? hash = null, string runner = "12") => new()
        {
            ["runnerId"] = runner, ["runnerName"] = "Mara", ["projectName"] = project ?? name, ["storySha256"] = hash ?? sha, ["status"] = state, ["sceneId"] = scene, ["pageIndex"] = 0, ["choiceId"] = choiceId,
            ["numeric"] = new JsonObject { ["aff:trust"] = trust }, ["text"] = new JsonObject { ["player_name"] = "Ada" }
        };
        engine.Send("runtime.state", Runner("started", first.State));
        engine.Send("runtime.state", Runner("step", second.State, choice.InternalId.ToString(), 1));
        engine.Send("runtime.event", new JsonObject { ["runnerId"] = "12", ["type"] = "Event", ["targetId"] = "lamp_lit", ["parameter"] = "north" });
        engine.Send("runtime.state", Runner("step", third, null, 2));
        engine.Until(() => graph.LiveScene == third, "the graph shows the current scene");
        Settle();
        var pane = Named<Border>("FollowPane");
        check(pane.IsVisible && window.Following.Followed!.Label == $"Unity · Mara ({name})" && graph.LiveTrail.SetEquals([first.State, second.State, third]), "the running game is tracked on the graph with its trail");
        var trust = pane.GetVisualDescendants().OfType<NumericUpDown>().Single();
        check(trust.Value == 2 && Named<TextBlock>("FollowLocation").Text!.StartsWith(third) && pane.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Event · lamp_lit · north"), "the follow pane shows the location, variables and events");
        check(ProjectStore.Serialize(window.Project) == snapshot && !undo.IsEnabled && !window.HasUnsavedChanges, "following leaves the project and undo history untouched");
        capture(window, "live-follow-dark");
        window.ChangeAppearance("Light"); Settle(); capture(window, "live-follow-light");
        window.ChangeAppearance("High contrast"); Settle(); capture(window, "live-follow-high-contrast");
        window.ChangeAppearance("Dark"); Settle();
        window.Width = 1000; window.Height = 680; Settle(); capture(window, "live-follow-compact");
        var play = window.FindControl<Button>("PlayButton")!;
        check(play.TranslatePoint(new Avalonia.Point(play.Bounds.Width, 0), window)!.Value.X <= window.ClientSize.Width, "the toolbar still fits at the minimum window width");
        window.Width = 1540; window.Height = 940; Settle();

        pane.GetVisualDescendants().OfType<NumericUpDown>().Single().Value = 5;
        var edit = engine.Expect("runtime.setVariable")["payload"]!;
        check(edit["runnerId"]!.GetValue<string>() == "12" && edit["key"]!.GetValue<string>() == "aff:trust" && edit["number"]!.GetValue<int>() == 5, "editing a variable tells the game");
        pane.GetVisualDescendants().OfType<TextBox>().First(t => t.Text == "Ada").Text = "Bo";
        check(engine.Expect("runtime.setVariable")["payload"]!["text"]!.GetValue<string>() == "Bo", "editing a text variable tells the game");
        check(ProjectStore.Serialize(window.Project) == snapshot, "variable edits stay out of the project");

        engine.Send("runtime.state", Runner("ended", null, null, 5)); engine.Until(() => window.Following.Ended, "the game reports the ending"); Settle();
        check(Named<TextBlock>("FollowEnding").Text == "Conversation ended" && graph.LiveScene == third && graph.LiveTrail.Count == 3, "the ending is labelled and the trail stays");

        engine.Send("editor.reveal", new JsonObject { ["sceneId"] = second.State }); engine.Until(() => graph.Selected?.State == second.State, "the requested scene is selected");
        check(ProjectStore.Serialize(window.Project) == snapshot, "reveal only changes the selection");

        engine.Send("runtime.state", Runner("step", third, null, 5, project: "Other Game")); engine.Until(() => window.Following.Followed!.ProjectName == "Other Game", "the other project arrives"); Settle();
        check(Named<TextBlock>("FollowNotice").Text == "Game is running Other Game" && graph.LiveScene is null && graph.LiveTrail.Count == 0, "a different project shows a notice and no highlight");
        engine.Send("runtime.state", Runner("step", "not_here", null, 5)); engine.Until(() => window.Following.CurrentSceneId == "not_here", "the unknown scene arrives"); Settle();
        check(Named<TextBlock>("FollowNotice").Text!.Contains("not_here") && graph.LiveScene is null, "an unknown scene shows a notice instead of a highlight");
        engine.Send("runtime.state", Runner("step", third, null, 5, hash: "old")); engine.Until(() => window.Following.Followed!.StorySha256 == "old", "the old story arrives"); Settle();
        check(Named<TextBlock>("FollowNotice").Text == "Game is on an older version." && Named<Button>("FollowPush").IsVisible && graph.LiveScene == third, "an older game offers Push update and keeps the highlight");
        engine.Drain(); Press("FollowPush");
        check(engine.Expect("story.updated")["payload"]!["StorySha256"]!.GetValue<string>() == sha, "Push update sends the story");

        var original = first.DefaultNextId;
        first.Text = "Changed on save."; engine.Drain(); Finish(window.SaveProjectAsync());
        var saved = engine.Expect("story.updated")["payload"]!["StorySha256"]!.GetValue<string>();
        check(saved != sha && saved == server.StorySha256, "saving pushes the new story");
        first.DefaultNextId = "broken_target"; Finish(window.SaveProjectAsync());
        check(engine.Expect("story.invalid")["payload"]!["diagnostics"]!.AsArray().Count > 0 && server.StorySha256 == saved, "saving a broken story sends diagnostics instead");
        first.DefaultNextId = original; Finish(window.SaveProjectAsync()); engine.Expect("story.updated");

        var title = window.FindControl<StackPanel>("EditorPanel")!.GetVisualDescendants().OfType<TextBox>().First();
        title.Text = "Not pushed"; for (var i = 0; i < 160; i++) Pump();
        check(!engine.Has("story.updated"), "edits wait for Save while Live edits is off");
        window.LiveEdits = true; title.Text = "Pushed live";
        var pushed = engine.Expect("story.updated")["payload"]!;
        check(pushed["StoryJson"]!.GetValue<string>().Contains("Pushed live"), "Live edits sends a valid edit after the debounce");
        window.LiveEdits = false; Finish(window.SaveProjectAsync());

        engine.Drain(); offscreen.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var started = engine.Expect("play.from")["payload"]!;
        check(started["sceneId"]!.GetValue<string>() == second.State && started["numeric"] is JsonObject && started["text"]!["player_name"] is not null, "Play in engine plays from the selected scene");

        Press("StopFollowing");
        check(window.Following.Followed is null && graph.LiveScene is null && graph.LiveTrail.Count == 0 && !pane.IsVisible, "Stop following clears the pane and the graph");
        toggle.IsChecked = false; Pump(); engine.WaitClosed();
        check(window.LiveLink is null && !File.Exists(window.LiveLinkDiscoveryPath) && !status.IsVisible && !offscreen.IsVisible, "turning the live link off stops the server and removes the discovery file");
        engine.Dispose(); window.Close();
    }
}
