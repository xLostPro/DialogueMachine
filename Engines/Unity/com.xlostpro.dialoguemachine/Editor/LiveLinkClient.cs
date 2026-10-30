using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DialogueMachine.Runtime;
using DialogueMachine.Runtime.Unity;
using UnityEditor;
using UnityEngine;

namespace DialogueMachine.Editor
{
    public class LiveLinkClient : IDisposable
    {
        public const string PluginVersion = "1.3.0";
        private const int ProtocolVersion = 1;
        private const int SchemaVersion = 1;

        private static LiveLinkClient instance;
        public static LiveLinkClient Instance => instance ??= new LiveLinkClient();

        private ClientWebSocket socket;
        private CancellationTokenSource cts;
        private readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);
        private readonly ConcurrentQueue<Action> mainThreadQueue = new ConcurrentQueue<Action>();

        public enum ConnectionState
        {
            Disconnected,
            Connecting,
            Connected
        }

        public ConnectionState State { get; private set; } = ConnectionState.Disconnected;
        public string ServerAppVersion { get; private set; }
        public string ServerProjectName { get; private set; }
        public string UpdateUrl { get; private set; }
        public int ServerPort { get; private set; }
        public List<string> LastDiagnostics { get; } = new List<string>();

        public event Action<ConnectionState> StateChanged;
        public event Action<string, Handoff, string> StoryUpdated; // projectName, handoff, rawJson
        public event Action<List<string>> StoryInvalid;
        public event Action<string, int?, Dictionary<string, int>, Dictionary<string, string>> PlayFromRequested;

        public LiveLinkClient()
        {
            EditorApplication.update += ProcessMainThreadQueue;
            DialogueRunnerBehaviour.OnRunnerStateChanged += HandleRunnerStateChanged;
            DialogueRunnerBehaviour.OnRunnerActionRaised += HandleRunnerActionRaised;
        }

        public void Dispose()
        {
            EditorApplication.update -= ProcessMainThreadQueue;
            DialogueRunnerBehaviour.OnRunnerStateChanged -= HandleRunnerStateChanged;
            DialogueRunnerBehaviour.OnRunnerActionRaised -= HandleRunnerActionRaised;
            Disconnect();
            sendLock.Dispose();
        }

        private void ProcessMainThreadQueue()
        {
            while (mainThreadQueue.TryDequeue(out var action))
            {
                try
                {
                    action?.Invoke();
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
        }

        public void Connect()
        {
            if (State != ConnectionState.Disconnected) return;

            var discFile = GetDiscoveryFilePath();
            if (!File.Exists(discFile))
            {
                Debug.LogWarning("[DialogueMachine] Discovery file livelink.json not found. Ensure Dialogue Machine desktop is running with Live Link enabled.");
                return;
            }

            Dictionary<string, object> disc;
            try
            {
                disc = MiniJson.Parse(File.ReadAllText(discFile)) as Dictionary<string, object>;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DialogueMachine] Could not read discovery file: {ex.Message}");
                return;
            }

            if (disc == null) return;
            var port = disc.TryGetValue("port", out var p) && p is double dp ? (int)dp : 47615;
            var token = disc.TryGetValue("token", out var t) ? t as string : string.Empty;

            ServerPort = port;
            State = ConnectionState.Connecting;
            StateChanged?.Invoke(State);

            cts = new CancellationTokenSource();
            _ = Task.Run(() => ConnectAndRunAsync(port, token, cts.Token));
        }

        public void Disconnect()
        {
            if (State == ConnectionState.Disconnected) return;

            cts?.Cancel();
            if (socket != null)
            {
                try
                {
                    if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
                        socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None).Wait(1000);
                }
                catch { }
                socket.Dispose();
                socket = null;
            }

            State = ConnectionState.Disconnected;
            mainThreadQueue.Enqueue(() => StateChanged?.Invoke(State));
        }

        private async Task ConnectAndRunAsync(int port, string token, CancellationToken ct)
        {
            socket = new ClientWebSocket();
            try
            {
                var uri = new Uri($"ws://127.0.0.1:{port}/dialogue-machine/");
                await socket.ConnectAsync(uri, ct);

                // Send hello
                var helloPayload = new Dictionary<string, object>
                {
                    ["token"] = token,
                    ["engine"] = "unity",
                    ["engineVersion"] = Application.unityVersion,
                    ["pluginVersion"] = PluginVersion,
                    ["protocol"] = ProtocolVersion,
                    ["schemaVersion"] = SchemaVersion
                };

                await SendMessageAsync("hello", helloPayload, ct);

                mainThreadQueue.Enqueue(() =>
                {
                    State = ConnectionState.Connected;
                    StateChanged?.Invoke(State);
                });

                var buffer = new byte[65536];
                var ms = new MemoryStream();

                while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
                {
                    ms.SetLength(0);
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by server", ct);
                            return;
                        }
                        ms.Write(buffer, 0, result.Count);
                    }
                    while (!result.EndOfMessage);

                    var text = Encoding.UTF8.GetString(ms.ToArray());
                    HandleReceivedJson(text);
                }
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                {
                    Debug.LogWarning($"[DialogueMachine] Live link connection ended: {ex.Message}");
                }
            }
            finally
            {
                mainThreadQueue.Enqueue(() =>
                {
                    State = ConnectionState.Disconnected;
                    StateChanged?.Invoke(State);
                });
            }
        }

        private void HandleReceivedJson(string json)
        {
            Dictionary<string, object> root;
            try
            {
                root = MiniJson.Parse(json) as Dictionary<string, object>;
            }
            catch
            {
                return;
            }

            if (root == null) return;
            var type = root.TryGetValue("type", out var t) ? t as string : string.Empty;
            var payload = root.TryGetValue("payload", out var p) ? p as Dictionary<string, object> : null;

            switch (type)
            {
                case "welcome":
                    if (payload != null)
                    {
                        ServerAppVersion = payload.TryGetValue("appVersion", out var av) ? av as string : "";
                        ServerProjectName = payload.TryGetValue("projectName", out var pn) ? pn as string : "";
                        UpdateUrl = payload.TryGetValue("updateUrl", out var uu) ? uu as string : null;
                    }
                    break;

                case "story.updated":
                    if (payload != null)
                    {
                        var rawJson = MiniJson.Serialize(payload);
                        var projectName = payload.TryGetValue("ProjectName", out var pn) ? pn as string : ServerProjectName;
                        Handoff handoff = null;
                        try { handoff = Handoff.Parse(rawJson); } catch { }

                        mainThreadQueue.Enqueue(() =>
                        {
                            LastDiagnostics.Clear();
                            StoryUpdated?.Invoke(projectName, handoff, rawJson);
                        });
                    }
                    break;

                case "story.invalid":
                    if (payload != null && payload.TryGetValue("diagnostics", out var dList) && dList is List<object> items)
                    {
                        var diagStrings = new List<string>();
                        foreach (var item in items)
                        {
                            if (item is Dictionary<string, object> dict)
                            {
                                var msg = dict.TryGetValue("message", out var m) ? m as string : "";
                                var stateId = dict.TryGetValue("stateId", out var s) ? s as string : "";
                                diagStrings.Add(string.IsNullOrEmpty(stateId) ? msg : $"[{stateId}] {msg}");
                            }
                        }
                        mainThreadQueue.Enqueue(() =>
                        {
                            LastDiagnostics.Clear();
                            LastDiagnostics.AddRange(diagStrings);
                            StoryInvalid?.Invoke(LastDiagnostics);
                        });
                    }
                    break;

                case "play.from":
                    if (payload != null)
                    {
                        var sceneId = payload.TryGetValue("sceneId", out var s) ? s as string : null;
                        int? seed = payload.TryGetValue("seed", out var sd) && sd is double dsd ? (int)dsd : null;

                        var numeric = new Dictionary<string, int>(StringComparer.Ordinal);
                        if (payload.TryGetValue("numeric", out var numObj) && numObj is Dictionary<string, object> numDict)
                        {
                            foreach (var kv in numDict)
                                if (kv.Value is double dn) numeric[kv.Key] = (int)dn;
                        }

                        var text = new Dictionary<string, string>(StringComparer.Ordinal);
                        if (payload.TryGetValue("text", out var txtObj) && txtObj is Dictionary<string, object> txtDict)
                        {
                            foreach (var kv in txtDict)
                                if (kv.Value is string sv) text[kv.Key] = sv;
                        }

                        mainThreadQueue.Enqueue(() => PlayFromRequested?.Invoke(sceneId, seed, numeric, text));
                    }
                    break;

                case "runtime.setVariable":
                    if (payload != null)
                    {
                        var runnerId = payload.TryGetValue("runnerId", out var r) ? r as string : null;
                        var key = payload.TryGetValue("key", out var k) ? k as string : null;
                        int? number = payload.TryGetValue("number", out var no) && no is double dno ? (int)dno : null;
                        var textVal = payload.TryGetValue("text", out var to) ? to as string : null;

                        mainThreadQueue.Enqueue(() =>
                        {
                            if (string.IsNullOrEmpty(key)) return;
#if UNITY_6000_4_OR_NEWER
                            var runners = UnityEngine.Object.FindObjectsByType<DialogueRunnerBehaviour>();
#elif UNITY_2023_1_OR_NEWER || UNITY_6000_0_OR_NEWER
                            var runners = UnityEngine.Object.FindObjectsByType<DialogueRunnerBehaviour>(FindObjectsSortMode.None);
#else
                            var runners = UnityEngine.Object.FindObjectsOfType<DialogueRunnerBehaviour>();
#endif
                            foreach (var runner in runners)
                            {
                                if (runnerId == null || DialogueRunnerBehaviour.GetObjectId(runner) == runnerId)
                                {
                                    if (number.HasValue) runner.Variables?.SetNumber(key, number.Value);
                                    if (textVal != null) runner.Variables?.SetText(key, textVal);
                                }
                            }
                        });
                    }
                    break;

                case "ping":
                    _ = SendMessageAsync("pong", new Dictionary<string, object>(), CancellationToken.None);
                    break;
            }
        }

        public async Task SendMessageAsync(string type, Dictionary<string, object> payload, CancellationToken ct)
        {
            if (socket == null || socket.State != WebSocketState.Open) return;

            var frame = new Dictionary<string, object>
            {
                ["protocol"] = ProtocolVersion,
                ["type"] = type,
                ["payload"] = payload ?? new Dictionary<string, object>()
            };

            var json = MiniJson.Serialize(frame);
            var bytes = Encoding.UTF8.GetBytes(json);

            await sendLock.WaitAsync(ct);
            try
            {
                await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
            }
            finally
            {
                sendLock.Release();
            }
        }

        public void SendEditorReveal(string sceneId)
        {
            if (State != ConnectionState.Connected || string.IsNullOrEmpty(sceneId)) return;
            _ = SendMessageAsync("editor.reveal", new Dictionary<string, object> { ["sceneId"] = sceneId }, CancellationToken.None);
        }

        private void HandleRunnerStateChanged(DialogueRunnerBehaviour runner, string status, string choiceId)
        {
            if (State != ConnectionState.Connected || runner == null) return;

            var numeric = new Dictionary<string, object>();
            foreach (var kv in runner.GetCurrentNumericVariables()) numeric[kv.Key] = (double)kv.Value;

            var text = new Dictionary<string, object>();
            foreach (var kv in runner.GetCurrentTextVariables()) text[kv.Key] = kv.Value;

            var payload = new Dictionary<string, object>
            {
                ["runnerId"] = DialogueRunnerBehaviour.GetObjectId(runner),
                ["runnerName"] = runner.gameObject.name,
                ["projectName"] = runner.Story != null ? runner.Story.ProjectName : (ServerProjectName ?? ""),
                ["storySha256"] = runner.Story != null ? runner.Story.Sha : "",
                ["status"] = status,
                ["sceneId"] = runner.CurrentSceneId,
                ["pageIndex"] = runner.PageIndex,
                ["choiceId"] = choiceId,
                ["numeric"] = numeric,
                ["text"] = text
            };

            _ = SendMessageAsync("runtime.state", payload, CancellationToken.None);
        }

        private void HandleRunnerActionRaised(DialogueRunnerBehaviour runner, string type, string targetId, string parameter)
        {
            if (State != ConnectionState.Connected || runner == null) return;

            var payload = new Dictionary<string, object>
            {
                ["runnerId"] = DialogueRunnerBehaviour.GetObjectId(runner),
                ["type"] = type,
                ["targetId"] = targetId,
                ["parameter"] = parameter
            };

            _ = SendMessageAsync("runtime.event", payload, CancellationToken.None);
        }

        public static string GetDiscoveryFilePath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(appData, "DialogueMachine", "livelink.json");
        }
    }
}
