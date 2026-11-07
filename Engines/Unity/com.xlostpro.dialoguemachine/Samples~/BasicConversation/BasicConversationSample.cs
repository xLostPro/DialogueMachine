using DialogueMachine.Runtime.Unity;
using UnityEngine;
using UnityEngine.UIElements;

namespace DialogueMachine.Samples
{
    /// <summary>
    /// Scene-free sample demonstrating Dialogue Machine in Unity.
    /// Attach this component to any GameObject in an empty scene and press Play.
    /// </summary>
    [AddComponentMenu("Dialogue Machine/Samples/Basic Conversation Sample")]
    public class BasicConversationSample : MonoBehaviour
    {
        [Tooltip("Optional DialogueAsset. If left empty, a built-in introductory story is played.")]
        [SerializeField] private DialogueAsset storyAsset;

        [Tooltip("Optional. Try WarmDialogue.uss from this sample to restyle the default view.")]
        [SerializeField] private StyleSheet styleSheet;

        private DefaultDialogueView view;

        private void Start()
        {
            // If no asset is assigned, generate a standalone introductory story in memory.
            if (storyAsset == null)
            {
                storyAsset = ScriptableObject.CreateInstance<DialogueAsset>();
                var sampleHandoffJson = @"
{
  ""SchemaVersion"": 1,
  ""Contract"": ""dialogue-machine.story.v1"",
  ""StorySha256"": ""sample"",
  ""StartingScene"": ""welcome"",
  ""Seed"": 42,
  ""NumericValues"": {
    ""inv:coin"": 5,
    ""aff:trust"": 1
  },
  ""TextValues"": {
    ""player_name"": ""Traveler""
  },
  ""MaxOperations"": 1000,
  ""StoryJson"": ""{\""ListOfCharacterStates\"":[{\""State\"":\""welcome\"",\""Type\"":\""Dialogue\"",\""Text\"":\""Welcome to [b]Dialogue Machine[/b], {player_name}!\r\nYou have [colour=#FFD700]{inv:coin} coins[/colour].\r\n[if aff:trust >= 1]I know I can trust you.[/if]\"",\""SayItAll\"":true,\""DefaultNextId\"":\""[EXIT]\"",\""Metadata\"":{\""speaker\"":\""Mara\"",\""title\"":\""Introduction\""},\""Choices\"":[{\""InternalId\"":\""c_explore\"",\""ButtonText\"":\""Tell me more about the engine integration.\"",\""NextStateId\"":\""more_info\"",\""Visibility\"":1,\""Requirements\"":[],\""AfterChoiceActions\"":[]},{\""InternalId\"":\""c_bribe\"",\""ButtonText\"":\""Offer 3 coins (Requires 3 coins)\"",\""NextStateId\"":\""bribe\"",\""Visibility\"":1,\""Requirements\"":[{\""Type\"":\""Inventory\"",\""TargetId\"":\""coin\"",\""Operator\"":\"">=\"",\""Value\"":\""3\""}],\""AfterChoiceActions\"":[{\""Type\"":\""Inventory\"",\""TargetId\"":\""coin\"",\""Amount\"":-3},{\""Type\"":\""Affinity\"",\""TargetId\"":\""trust\"",\""Amount\"":2}]},{\""InternalId\"":\""c_leave\"",\""ButtonText\"":\""I must be going.\"",\""NextStateId\"":\""[EXIT]\"",\""Visibility\"":2,\""Requirements\"":[],\""AfterChoiceActions\"":[]}]},{\""State\"":\""more_info\"",\""Type\"":\""Dialogue\"",\""Text\"":\""The Unity package includes a runtime runner, UI Toolkit presentation, scripted importer, and Live Link hot reload.\"",\""SayItAll\"":true,\""DefaultNextId\"":\""[EXIT]\"",\""Metadata\"":{\""speaker\"":\""Mara\"",\""title\"":\""Information\""},\""Choices\"":[]},{\""State\"":\""bribe\"",\""Type\"":\""Dialogue\"",\""Text\"":\""Thank you kindly! You now have [colour=#FFD700]{inv:coin} coins[/colour] and our trust is deepened.\"",\""SayItAll\"":true,\""DefaultNextId\"":\""[EXIT]\"",\""Metadata\"":{\""speaker\"":\""Mara\"",\""title\"":\""Gratitude\""},\""Choices\"":[]}]}""
}
";
                storyAsset.SetData(sampleHandoffJson, "sample", "BasicConversation");
            }

            // Ensure a DialogueRunnerBehaviour exists
            var runner = gameObject.GetComponent<DialogueRunnerBehaviour>();
            if (runner == null)
            {
                runner = gameObject.AddComponent<DialogueRunnerBehaviour>();
            }
            runner.Story = storyAsset;
            runner.PlayOnStart = false;

            // Ensure DefaultDialogueView exists
            view = gameObject.GetComponent<DefaultDialogueView>();
            if (view == null)
            {
                view = gameObject.AddComponent<DefaultDialogueView>();
            }
            view.Runner = runner;
            if (styleSheet != null) view.StyleSheet = styleSheet;

            // Log events for demonstration
            runner.ActionRaised += (type, targetId, parameter, amount) =>
            {
                Debug.Log($"[Sample] Action Raised: {type} -> {targetId} ({parameter}, amount: {amount})");
            };
            runner.Ended += (sceneId, choiceId) =>
            {
                Debug.Log($"[Sample] Dialogue Ended at scene '{sceneId}' (last choice: '{choiceId}')");
            };

            // Start dialogue playback
            runner.StartDialogue();
        }

        // The view takes no input of its own: send it the game's confirm and up/down presses.
        private void Update()
        {
            if (view == null) return;
#if ENABLE_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.enterKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame) view.Submit();
            if (keyboard.upArrowKey.wasPressedThisFrame) view.MoveSelection(-1);
            if (keyboard.downArrowKey.wasPressedThisFrame) view.MoveSelection(1);
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space)) view.Submit();
            if (Input.GetKeyDown(KeyCode.UpArrow)) view.MoveSelection(-1);
            if (Input.GetKeyDown(KeyCode.DownArrow)) view.MoveSelection(1);
#endif
        }
    }
}
