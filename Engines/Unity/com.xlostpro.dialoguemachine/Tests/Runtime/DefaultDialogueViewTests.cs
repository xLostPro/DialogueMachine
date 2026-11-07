using System.Collections;
using System.Collections.Generic;
using DialogueMachine.Runtime.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DialogueMachine.Runtime.Tests
{
    public class DefaultDialogueViewTests
    {
        private GameObject host;

        [TearDown]
        public void TearDown() => Object.Destroy(host);

        private static Dictionary<string, object> Choice(string id, string text, string next, object[] requirements = null) => new Dictionary<string, object>
        {
            ["InternalId"] = id, ["ButtonText"] = text, ["NextStateId"] = next, ["Visibility"] = 1,
            ["Requirements"] = requirements ?? new object[0], ["AfterChoiceActions"] = new object[0]
        };

        // One scene: a line, then a disabled choice and two enabled ones.
        private (DialogueRunnerBehaviour, DefaultDialogueView) Create()
        {
            var scene = new Dictionary<string, object>
            {
                ["State"] = "a", ["Type"] = "Dialogue", ["Text"] = "Hello [b]there[/b]", ["SayItAll"] = true, ["DefaultNextId"] = "[EXIT]",
                ["Metadata"] = new Dictionary<string, object> { ["speaker"] = "Mara" },
                ["Choices"] = new object[]
                {
                    Choice("rich", "Pay", "[EXIT]", new object[] { new Dictionary<string, object> { ["Type"] = "Inventory", ["TargetId"] = "coin", ["Operator"] = ">=", ["Value"] = "5" } }),
                    Choice("stay", "Stay", "[EXIT]"),
                    Choice("leave", "Leave", "[EXIT]"),
                }
            };
            var story = new Dictionary<string, object> { ["ListOfCharacterStates"] = new object[] { scene } };
            var handoff = new Dictionary<string, object>
            {
                ["SchemaVersion"] = 1, ["Contract"] = "dialogue-machine.story.v1", ["StorySha256"] = "test", ["StartingScene"] = "a",
                ["Seed"] = 1, ["NumericValues"] = new Dictionary<string, object>(), ["TextValues"] = new Dictionary<string, object>(),
                ["MaxOperations"] = 1000, ["StoryJson"] = MiniJson.Serialize(story)
            };
            var asset = ScriptableObject.CreateInstance<DialogueAsset>();
            asset.SetData(MiniJson.Serialize(handoff), "test", "Test");

            host = new GameObject("Dialogue");
            host.SetActive(false);
            var runner = host.AddComponent<DialogueRunnerBehaviour>();
            runner.Story = asset;
            runner.PlayOnStart = false;
            var view = host.AddComponent<DefaultDialogueView>();
            view.Runner = runner;
            view.TypewriterSpeed = 1f;
            host.SetActive(true);
            return (runner, view);
        }

        [UnityTest]
        public IEnumerator SubmitSkipsTypingThenPicksTheHighlightedChoice()
        {
            var (runner, view) = Create();
            string chosen = null;
            runner.Ended += (scene, choice) => chosen = choice;
            runner.StartDialogue();

            Assert.IsTrue(view.IsOpen && view.IsTyping);
            view.Submit();
            Assert.IsTrue(view.IsTyping, "the press that started the dialogue doesn't skip its first line");

            yield return null;
            view.Submit();
            Assert.IsFalse(view.IsTyping);
            Assert.IsTrue(view.IsShowingChoices);
            Assert.AreEqual(1, view.SelectedIndex, "the highlight starts on the first enabled choice");

            view.Submit();
            Assert.IsFalse(runner.IsEnded, "one action per frame");

            view.MoveSelection(1);
            Assert.AreEqual(2, view.SelectedIndex);
            view.MoveSelection(1);
            Assert.AreEqual(1, view.SelectedIndex, "moving wraps and skips the disabled choice");
            view.MoveSelection(-1);
            Assert.AreEqual(2, view.SelectedIndex);

            yield return null;
            view.Submit();
            Assert.AreEqual("leave", chosen);
            Assert.IsFalse(view.IsOpen);
        }

        [UnityTest]
        public IEnumerator SelectChoiceIgnoresDisabledChoices()
        {
            var (runner, view) = Create();
            view.EnableTypewriter = false;
            runner.StartDialogue();
            yield return null;

            view.SelectChoice(0);
            Assert.IsFalse(runner.IsEnded);
            yield return null;
            view.SelectChoice(1);
            Assert.IsTrue(runner.IsEnded);
        }
    }
}
