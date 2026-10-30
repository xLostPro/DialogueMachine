using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Events;

namespace DialogueMachine.Runtime.Unity
{
    [System.Serializable]
    public class DialogueLineUnityEvent : UnityEvent<string> { }

    [System.Serializable]
    public class DialogueChoicesUnityEvent : UnityEvent<IReadOnlyList<Choice>> { }

    [System.Serializable]
    public class DialogueEndedUnityEvent : UnityEvent<string, string> { }

    [System.Serializable]
    public class DialogueActionUnityEvent : UnityEvent<string, string, string, int> { }

    [AddComponentMenu("Dialogue Machine/Dialogue Runner")]
    public class DialogueRunnerBehaviour : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] private DialogueAsset story;
        [SerializeField] private bool playOnStart = true;
        [SerializeField] private string startScene = "";
        [SerializeField] private int seed = 0;
        [SerializeField] private MonoBehaviour variablesProvider;

        [Header("Events")]
        [SerializeField] private DialogueLineUnityEvent onLine = new DialogueLineUnityEvent();
        [SerializeField] private DialogueChoicesUnityEvent onChoices = new DialogueChoicesUnityEvent();
        [SerializeField] private DialogueEndedUnityEvent onEnded = new DialogueEndedUnityEvent();
        [SerializeField] private DialogueActionUnityEvent onAction = new DialogueActionUnityEvent();

        private DialogueRunner runner;
        private string lastChosenChoiceId;

        // C# Events
        public event Action<string> LineShown;
        public event Action<IReadOnlyList<Choice>> ChoicesShown;
        public event Action<string, string, string, int> ActionRaised;
        public event Action<string> ShopOpened;
        public event Action<string, object, object> VariableChanged;
        public event Action<string, string> Ended;
        public event Action<string> Warning;

        // Static events for Live Link integration
        public static event Action<DialogueRunnerBehaviour, string, string> OnRunnerStateChanged;
        public static event Action<DialogueRunnerBehaviour, string, string, string> OnRunnerActionRaised;

        public DialogueAsset Story
        {
            get => story;
            set => story = value;
        }

        public bool PlayOnStart
        {
            get => playOnStart;
            set => playOnStart = value;
        }

        public string StartScene
        {
            get => startScene;
            set => startScene = value;
        }

        public int Seed
        {
            get => seed;
            set => seed = value;
        }

        public MonoBehaviour VariablesProvider
        {
            get => variablesProvider;
            set => variablesProvider = value;
        }

        public DialogueLineUnityEvent OnLine => onLine;
        public DialogueChoicesUnityEvent OnChoices => onChoices;
        public DialogueEndedUnityEvent OnEnded => onEnded;
        public DialogueActionUnityEvent OnAction => onAction;

        // Delegated properties from DialogueRunner
        public IDialogueVariables Variables => runner?.Variables;
        public string CurrentSceneId => runner?.CurrentSceneId;
        public string Speaker => runner?.Speaker;
        public string Title => runner?.Title;
        public bool IsEnded => runner == null || runner.IsEnded;
        public string CurrentPage => runner?.CurrentPage;
        public string CurrentPagePlain => runner?.CurrentPagePlain;
        public int PageIndex => runner?.PageIndex ?? 0;
        public int PageCount => runner?.PageCount ?? 0;
        public IReadOnlyList<Choice> Choices => runner?.Choices ?? Array.Empty<Choice>();
        public bool CanContinue => runner != null && runner.CanContinue;
        public IReadOnlyDictionary<string, string> CurrentMetadata => runner?.CurrentMetadata;

        private void Awake()
        {
            InitializeRunner();
        }

        private void Start()
        {
            if (playOnStart && story != null)
            {
                StartDialogue(string.IsNullOrEmpty(startScene) ? null : startScene, seed != 0 ? (int?)seed : null);
            }
        }

        public void InitializeRunner()
        {
            IDialogueVariables vars = null;
            if (variablesProvider is IDialogueVariables customVars)
            {
                vars = customVars;
            }

            runner = new DialogueRunner(vars);
            runner.LineShown += HandleLineShown;
            runner.ChoicesShown += HandleChoicesShown;
            runner.ActionRaised += HandleActionRaised;
            runner.ShopOpened += HandleShopOpened;
            runner.VariableChanged += HandleVariableChanged;
            runner.Ended += HandleEnded;
            runner.Warning += HandleWarning;

            if (story != null && !string.IsNullOrEmpty(story.HandoffJson))
            {
                try
                {
                    runner.Load(story.HandoffJson);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[DialogueMachine] Failed to load story in runner: {ex.Message}", this);
                }
            }
        }

        public void StartDialogue(string sceneId = null, int? dialogueSeed = null, IReadOnlyDictionary<string, int> numeric = null, IReadOnlyDictionary<string, string> text = null)
        {
            if (runner == null) InitializeRunner();
            if (story != null && !string.IsNullOrEmpty(story.HandoffJson))
            {
                runner.Load(story.HandoffJson);
            }

            lastChosenChoiceId = null;
            runner.Start(sceneId, dialogueSeed ?? (seed != 0 ? (int?)seed : null), numeric, text);
            ReportState("started", null);
        }

        public void Continue()
        {
            if (runner == null) return;
            lastChosenChoiceId = null;
            runner.Continue();
            ReportState("step", null);
        }

        public void Choose(string choiceId)
        {
            if (runner == null) return;
            lastChosenChoiceId = choiceId;
            runner.Choose(choiceId);
            ReportState("step", choiceId);
        }

        public void Choose(Guid choiceId) => Choose(choiceId.ToString());

        public void ReplaceStory(Handoff newHandoff)
        {
            if (runner == null) return;
            runner.ReplaceStory(newHandoff);
            ReportState("step", null);
        }

        public void ReplaceStory(string handoffJson)
        {
            ReplaceStory(Handoff.Parse(handoffJson));
        }

        public string SaveState() => runner?.SaveState() ?? string.Empty;

        public void RestoreState(string json)
        {
            if (runner == null) InitializeRunner();
            runner.RestoreState(json);
            ReportState("step", null);
        }

        private void HandleLineShown(string line)
        {
            LineShown?.Invoke(line);
            onLine?.Invoke(line);
        }

        private void HandleChoicesShown(IReadOnlyList<Choice> choices)
        {
            ChoicesShown?.Invoke(choices);
            onChoices?.Invoke(choices);
        }

        private void HandleActionRaised(string type, string targetId, string parameter, int amount)
        {
            ActionRaised?.Invoke(type, targetId, parameter, amount);
            onAction?.Invoke(type, targetId, parameter, amount);
            OnRunnerActionRaised?.Invoke(this, type, targetId, parameter);
        }

        private void HandleShopOpened(string shopId)
        {
            ShopOpened?.Invoke(shopId);
        }

        private void HandleVariableChanged(string key, object before, object after)
        {
            VariableChanged?.Invoke(key, before, after);
        }

        private void HandleEnded(string sceneId, string choiceId)
        {
            Ended?.Invoke(sceneId, choiceId);
            onEnded?.Invoke(sceneId, choiceId);
            ReportState("ended", choiceId ?? lastChosenChoiceId);
        }

        private void HandleWarning(string warning)
        {
            Warning?.Invoke(warning);
            Debug.LogWarning($"[DialogueMachine] {warning}", this);
        }

        private void ReportState(string status, string choiceId)
        {
            OnRunnerStateChanged?.Invoke(this, status, choiceId);
#if DEVELOPMENT_BUILD && !UNITY_EDITOR && !UNITY_WEBGL
            StandaloneLiveLinkBridge.SendRunnerState(this, status, choiceId);
#endif
        }

        public static string GetObjectId(UnityEngine.Object obj)
        {
            if (obj == null) return string.Empty;
#if UNITY_6000_4_OR_NEWER
            return obj.GetEntityId().ToString();
#else
            return obj.GetInstanceID().ToString();
#endif
        }

        public Dictionary<string, int> GetCurrentNumericVariables()
        {
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            if (runner?.Variables == null) return result;
            foreach (var k in runner.Variables.Keys)
            {
                if (runner.Variables.HasNumber(k))
                    result[k] = runner.Variables.GetNumber(k);
            }
            return result;
        }

        public Dictionary<string, string> GetCurrentTextVariables()
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (runner?.Variables == null) return result;
            foreach (var k in runner.Variables.Keys)
            {
                var val = runner.Variables.GetText(k);
                if (val != null) result[k] = val;
            }
            return result;
        }

#if DEVELOPMENT_BUILD && !UNITY_EDITOR && !UNITY_WEBGL
        private static class StandaloneLiveLinkBridge
        {
            private static System.Net.WebSockets.ClientWebSocket socket;
            private static bool connecting;
            private static DateTime lastConnectAttempt = DateTime.MinValue;

            public static void SendRunnerState(DialogueRunnerBehaviour runner, string status, string choiceId)
            {
                if (socket == null || socket.State != System.Net.WebSockets.WebSocketState.Open)
                {
                    TryConnect();
                    return;
                }

                try
                {
                    var numeric = new Dictionary<string, object>();
                    foreach (var kv in runner.GetCurrentNumericVariables()) numeric[kv.Key] = (double)kv.Value;
                    var text = new Dictionary<string, object>();
                    foreach (var kv in runner.GetCurrentTextVariables()) text[kv.Key] = kv.Value;

                    var payload = new Dictionary<string, object>
                    {
                        ["runnerId"] = GetObjectId(runner),
                        ["runnerName"] = runner.gameObject.name,
                        ["projectName"] = runner.Story != null ? runner.Story.ProjectName : "",
                        ["storySha256"] = runner.Story != null ? runner.Story.Sha : "",
                        ["status"] = status,
                        ["sceneId"] = runner.CurrentSceneId,
                        ["pageIndex"] = runner.PageIndex,
                        ["choiceId"] = choiceId,
                        ["numeric"] = numeric,
                        ["text"] = text
                    };

                    var msg = new Dictionary<string, object>
                    {
                        ["protocol"] = 1,
                        ["type"] = "runtime.state",
                        ["payload"] = payload
                    };

                    var json = MiniJson.Serialize(msg);
                    var bytes = System.Text.Encoding.UTF8.GetBytes(json);
                    _ = socket.SendAsync(new ArraySegment<byte>(bytes), System.Net.WebSockets.WebSocketMessageType.Text, true, System.Threading.CancellationToken.None);
                }
                catch { }
            }

            private static void TryConnect()
            {
                if (connecting || (DateTime.UtcNow - lastConnectAttempt).TotalSeconds < 10) return;
                lastConnectAttempt = DateTime.UtcNow;
                connecting = true;

                System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                        var discFile = System.IO.Path.Combine(appData, "DialogueMachine", "livelink.json");
                        if (!System.IO.File.Exists(discFile)) { connecting = false; return; }

                        var disc = MiniJson.Parse(System.IO.File.ReadAllText(discFile)) as Dictionary<string, object>;
                        if (disc == null) { connecting = false; return; }

                        var port = disc.TryGetValue("port", out var p) && p is double dp ? (int)dp : 47615;
                        var token = disc.TryGetValue("token", out var t) ? t as string : "";

                        socket = new System.Net.WebSockets.ClientWebSocket();
                        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/dialogue-machine/"), System.Threading.CancellationToken.None);

                        var hello = new Dictionary<string, object>
                        {
                            ["protocol"] = 1,
                            ["type"] = "hello",
                            ["payload"] = new Dictionary<string, object>
                            {
                                ["token"] = token,
                                ["engine"] = "unity",
                                ["engineVersion"] = Application.unityVersion,
                                ["pluginVersion"] = "1.3.0",
                                ["protocol"] = 1,
                                ["schemaVersion"] = 1
                            }
                        };
                        var bytes = System.Text.Encoding.UTF8.GetBytes(MiniJson.Serialize(hello));
                        await socket.SendAsync(new ArraySegment<byte>(bytes), System.Net.WebSockets.WebSocketMessageType.Text, true, System.Threading.CancellationToken.None);
                    }
                    catch
                    {
                        socket?.Dispose();
                        socket = null;
                    }
                    finally
                    {
                        connecting = false;
                    }
                });
            }
        }
#endif
    }
}
