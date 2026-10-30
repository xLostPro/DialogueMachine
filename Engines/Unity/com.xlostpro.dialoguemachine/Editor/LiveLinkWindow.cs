using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DialogueMachine.Runtime;
using DialogueMachine.Runtime.Unity;
using UnityEditor;
using UnityEngine;

namespace DialogueMachine.Editor
{
    public class LiveLinkWindow : EditorWindow
    {
        [SerializeField] private DialogueAsset targetAsset;
        [SerializeField] private bool autoApplyInPlayMode = true;

        private Vector2 scrollPos;
        private static (string sceneId, int? seed, Dictionary<string, int> numeric, Dictionary<string, string> text)? pendingPlayFrom;

        [MenuItem("Window/Dialogue Machine/Live Link")]
        public static void Open()
        {
            var window = GetWindow<LiveLinkWindow>("Live Link");
            window.minSize = new Vector2(340, 240);
            window.Show();
        }

        private void OnEnable()
        {
            var client = LiveLinkClient.Instance;
            client.StateChanged += OnClientStateChanged;
            client.StoryUpdated += OnStoryUpdated;
            client.StoryInvalid += OnStoryInvalid;
            client.PlayFromRequested += OnPlayFromRequested;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private void OnDisable()
        {
            var client = LiveLinkClient.Instance;
            client.StateChanged -= OnClientStateChanged;
            client.StoryUpdated -= OnStoryUpdated;
            client.StoryInvalid -= OnStoryInvalid;
            client.PlayFromRequested -= OnPlayFromRequested;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }

        private void OnClientStateChanged(LiveLinkClient.ConnectionState state)
        {
            Repaint();
        }

        private void OnStoryInvalid(List<string> diagnostics)
        {
            Repaint();
        }

        private void OnPlayFromRequested(string sceneId, int? seed, Dictionary<string, int> numeric, Dictionary<string, string> text)
        {
            pendingPlayFrom = (sceneId, seed, numeric, text);
            if (!EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = true;
            }
            else
            {
                ExecutePendingPlayFrom();
            }
        }

        private void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                EditorApplication.delayCall += () =>
                {
                    if (pendingPlayFrom.HasValue) ExecutePendingPlayFrom();
                };
            }
        }

        private void ExecutePendingPlayFrom()
        {
            if (!pendingPlayFrom.HasValue) return;
            var req = pendingPlayFrom.Value;
            pendingPlayFrom = null;

#if UNITY_2023_1_OR_NEWER || UNITY_6000_0_OR_NEWER
            var runner = UnityEngine.Object.FindAnyObjectByType<DialogueRunnerBehaviour>();
#else
            var runner = UnityEngine.Object.FindObjectOfType<DialogueRunnerBehaviour>();
#endif
            if (runner != null)
            {
                runner.StartDialogue(req.sceneId, req.seed, req.numeric, req.text);
            }
            else
            {
                Debug.LogWarning("[DialogueMachine] PlayFrom: No active DialogueRunnerBehaviour found in the scene.");
            }
        }

        private void OnStoryUpdated(string projectName, Handoff handoff, string rawJson)
        {
            var asset = targetAsset;
            if (asset == null && !string.IsNullOrEmpty(projectName))
            {
                asset = FindAssetForProject(projectName);
            }

            if (asset != null)
            {
                var path = AssetDatabase.GetAssetPath(asset);
                if (!string.IsNullOrEmpty(path) && path.EndsWith(".runtime.json", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        File.WriteAllText(path, rawJson);
                        AssetDatabase.ImportAsset(path);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[DialogueMachine] Could not rewrite asset file: {ex.Message}");
                    }
                }

                asset.SetData(rawJson, handoff?.StorySha256 ?? string.Empty, projectName ?? asset.ProjectName);
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();
            }

            if (autoApplyInPlayMode && EditorApplication.isPlaying && handoff != null)
            {
#if UNITY_6000_4_OR_NEWER
                var runners = UnityEngine.Object.FindObjectsByType<DialogueRunnerBehaviour>();
#elif UNITY_2023_1_OR_NEWER || UNITY_6000_0_OR_NEWER
                var runners = UnityEngine.Object.FindObjectsByType<DialogueRunnerBehaviour>(FindObjectsSortMode.None);
#else
                var runners = UnityEngine.Object.FindObjectsOfType<DialogueRunnerBehaviour>();
#endif
                foreach (var runner in runners)
                {
                    if (asset == null || runner.Story == asset)
                    {
                        runner.ReplaceStory(handoff);
                    }
                }
            }

            Repaint();
        }

        // Assets are named after their file ("the-salt-ledger"), while Dialogue Machine sends the project name ("The Salt Ledger").
        private static bool SameProject(string a, string b)
        {
            static string Key(string s) => new string((s ?? "").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
            return Key(a) == Key(b);
        }

        private DialogueAsset FindAssetForProject(string projectName)
        {
            var guids = AssetDatabase.FindAssets("t:DialogueAsset");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<DialogueAsset>(path);
                if (asset != null && SameProject(asset.ProjectName, projectName))
                {
                    return asset;
                }
            }
            return null;
        }

        private void OnGUI()
        {
            var client = LiveLinkClient.Instance;

            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                var statusColor = client.State switch
                {
                    LiveLinkClient.ConnectionState.Connected => new Color(0.2f, 0.8f, 0.3f),
                    LiveLinkClient.ConnectionState.Connecting => new Color(0.9f, 0.7f, 0.2f),
                    _ => new Color(0.6f, 0.6f, 0.6f)
                };

                var prevColor = GUI.color;
                GUI.color = statusColor;
                GUILayout.Label("●", GUILayout.Width(16));
                GUI.color = prevColor;

                var statusText = client.State switch
                {
                    LiveLinkClient.ConnectionState.Connected => $"Connected (Port {client.ServerPort})",
                    LiveLinkClient.ConnectionState.Connecting => $"Connecting to 127.0.0.1:{client.ServerPort}...",
                    _ => "Disconnected"
                };

                EditorGUILayout.LabelField(statusText, EditorStyles.boldLabel);

                if (client.State == LiveLinkClient.ConnectionState.Connected)
                {
                    if (GUILayout.Button("Disconnect", GUILayout.Width(90))) client.Disconnect();
                }
                else
                {
                    if (GUILayout.Button("Connect", GUILayout.Width(90))) client.Connect();
                }
            }

            // Update warning
            if (!string.IsNullOrEmpty(client.ServerAppVersion) && IsVersionOlder(LiveLinkClient.PluginVersion, client.ServerAppVersion))
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.HelpBox($"Dialogue Machine desktop v{client.ServerAppVersion} is available (plugin is v{LiveLinkClient.PluginVersion}).", MessageType.Warning);
                if (GUILayout.Button($"Update Plugin to v{client.ServerAppVersion}"))
                {
                    var upmUrl = "https://github.com/xLostPro/DialogueMachine.git?path=/Engines/Unity/com.xlostpro.dialoguemachine#v" + client.ServerAppVersion;
                    UnityEditor.PackageManager.Client.Add(upmUrl);
                }
            }

            EditorGUILayout.Space(8);
            targetAsset = (DialogueAsset)EditorGUILayout.ObjectField("Story Asset", targetAsset, typeof(DialogueAsset), false);
            autoApplyInPlayMode = EditorGUILayout.Toggle("Hot Reload in Play Mode", autoApplyInPlayMode);

            if (!string.IsNullOrEmpty(client.ServerProjectName))
            {
                EditorGUILayout.LabelField("Server Project", client.ServerProjectName);
            }

            // Diagnostics errors
            if (client.LastDiagnostics.Count > 0)
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField("Story Diagnostics (Last update failed)", EditorStyles.boldLabel);
                using (var scroll = new EditorGUILayout.ScrollViewScope(scrollPos, GUILayout.Height(100)))
                {
                    scrollPos = scroll.scrollPosition;
                    foreach (var diag in client.LastDiagnostics)
                    {
                        EditorGUILayout.HelpBox(diag, MessageType.Error);
                    }
                }
            }
        }

        private static bool IsVersionOlder(string current, string target)
        {
            if (string.IsNullOrEmpty(current) || string.IsNullOrEmpty(target)) return false;
            var cParts = current.Split('.');
            var tParts = target.Split('.');
            for (var i = 0; i < Math.Min(cParts.Length, tParts.Length); i++)
            {
                if (int.TryParse(cParts[i], out var c) && int.TryParse(tParts[i], out var t))
                {
                    if (c < t) return true;
                    if (c > t) return false;
                }
            }
            return false;
        }
    }
}
