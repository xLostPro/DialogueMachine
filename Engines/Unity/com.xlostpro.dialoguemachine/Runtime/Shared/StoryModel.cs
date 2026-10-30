using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DialogueMachine.Runtime
{
    // Member names mirror the website JSON. Missing fields take the same defaults as Core/Models.cs,
    // and unknown fields are ignored so newer exports still load.
    public sealed class StoryAction
    {
        public string Type = "Affinity";
        public string TargetId = "";
        public int Amount;
        public string Parameter;
    }

    public sealed class StoryRequirement
    {
        public string Type = "Affinity";
        public string TargetId = "";
        public string Operator = ">=";
        public string Value = "0";
    }

    public sealed class StoryChoice
    {
        public const int Hide = 0, Disable = 1, Always = 2;
        public string InternalId = "";
        public string ButtonText = "";
        public string NextStateId;
        public int Visibility = Disable;
        public List<StoryRequirement> Requirements = new List<StoryRequirement>();
        public List<StoryAction> AfterChoiceActions = new List<StoryAction>();
    }

    public sealed class Scene
    {
        public string State = "";
        public string Type = "Dialogue";
        public string Text = "";
        public string DefaultNextId = DialogueRules.Exit;
        public bool SayItAll = true;
        public string ShopId = "";
        public List<StoryChoice> Choices = new List<StoryChoice>();
        public List<StoryAction> DefaultAfterActions = new List<StoryAction>();
        public Dictionary<string, string> Metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        public string Title => Metadata.TryGetValue("title", out var title) ? title : State;
        public string Speaker => Metadata.TryGetValue("speaker", out var speaker) ? speaker : "Character";
    }

    public sealed class Story
    {
        public List<Scene> Scenes = new List<Scene>();
        private readonly Dictionary<string, Scene> byId = new Dictionary<string, Scene>(StringComparer.Ordinal);

        public Scene Find(string id) => id != null && byId.TryGetValue(id, out var scene) ? scene : null;

        public static Story Parse(string storyJson)
        {
            var root = MiniJson.Parse(storyJson) as Dictionary<string, object>;
            if (root == null || !root.TryGetValue("ListOfCharacterStates", out var list) || !(list is List<object>))
                throw new FormatException("A story must contain ListOfCharacterStates.");
            var story = new Story();
            foreach (var item in Objects(root, "ListOfCharacterStates"))
            {
                var scene = ReadScene(item);
                if (string.IsNullOrWhiteSpace(scene.State)) throw new FormatException("Each scene needs a non-empty ID.");
                if (scene.State == DialogueRules.Exit) throw new FormatException(DialogueRules.Exit + " is reserved for endings.");
                if (story.byId.ContainsKey(scene.State)) throw new FormatException("Duplicate scene ID '" + scene.State + "'.");
                story.byId[scene.State] = scene;
                story.Scenes.Add(scene);
            }
            return story;
        }

        private static Scene ReadScene(Dictionary<string, object> source)
        {
            var scene = new Scene
            {
                State = Text(source, "State", ""),
                Type = Text(source, "Type", "Dialogue"),
                Text = Text(source, "Text", "") ?? "",
                DefaultNextId = Text(source, "DefaultNextId", DialogueRules.Exit),
                SayItAll = !source.TryGetValue("SayItAll", out var sayItAll) || !(sayItAll is bool flag) || flag,
                Choices = Objects(source, "Choices").Select(ReadChoice).ToList(),
                DefaultAfterActions = Actions(source, "DefaultAfterActions")
            };
            if (source.TryGetValue("ShopData", out var shop) && shop is Dictionary<string, object> shopData) scene.ShopId = Text(shopData, "ShopId", "");
            if (source.TryGetValue("Metadata", out var metadata) && metadata is Dictionary<string, object> entries)
                foreach (var entry in entries)
                    if (entry.Value is string value) scene.Metadata[entry.Key] = value;
            return scene;
        }

        private static StoryChoice ReadChoice(Dictionary<string, object> source) => new StoryChoice
        {
            InternalId = Text(source, "InternalId", ""),
            ButtonText = Text(source, "ButtonText", "") ?? "",
            NextStateId = Text(source, "NextStateId", null),
            Visibility = Number(source, "Visibility", StoryChoice.Disable),
            Requirements = Objects(source, "Requirements").Select(ReadRequirement).ToList(),
            AfterChoiceActions = Actions(source, "AfterChoiceActions")
        };

        private static StoryRequirement ReadRequirement(Dictionary<string, object> source) => new StoryRequirement
        {
            Type = Text(source, "Type", "Affinity"),
            TargetId = Text(source, "TargetId", ""),
            Operator = Text(source, "Operator", ">="),
            Value = source.TryGetValue("Value", out var value) && value is double number ? number.ToString(CultureInfo.InvariantCulture) : Text(source, "Value", "0")
        };

        private static List<StoryAction> Actions(Dictionary<string, object> source, string key) => Objects(source, key).Select(item => new StoryAction
        {
            Type = Text(item, "Type", "Affinity"),
            TargetId = Text(item, "TargetId", ""),
            Amount = Number(item, "Amount", 0),
            Parameter = Text(item, "Parameter", null)
        }).ToList();

        internal static string Text(Dictionary<string, object> source, string key, string fallback) => source.TryGetValue(key, out var value) ? value as string : fallback;

        internal static int Number(Dictionary<string, object> source, string key, int fallback) => source.TryGetValue(key, out var value) && value is double number ? (int)number : fallback;

        private static IEnumerable<Dictionary<string, object>> Objects(Dictionary<string, object> source, string key) =>
            source.TryGetValue(key, out var value) && value is List<object> list ? list.OfType<Dictionary<string, object>>() : Enumerable.Empty<Dictionary<string, object>>();
    }

    public sealed class Handoff
    {
        public const int SupportedSchemaVersion = 1;
        public int SchemaVersion;
        public string Contract;
        public string StoryJson;
        public string StorySha256;
        public string StartingScene;
        public int Seed;
        public Dictionary<string, int> NumericValues = new Dictionary<string, int>(StringComparer.Ordinal);
        public Dictionary<string, string> TextValues = new Dictionary<string, string>(StringComparer.Ordinal);
        public int MaxOperations;
        public Story Story;

        public static Handoff Parse(string json)
        {
            var root = MiniJson.Parse(json) as Dictionary<string, object>;
            if (root == null) throw new FormatException("A runtime handoff must be a JSON object.");
            var schema = Story.Number(root, "SchemaVersion", 0);
            if (schema > SupportedSchemaVersion)
                throw new FormatException("This story uses runtime schema " + schema + ", but this plugin supports up to " + SupportedSchemaVersion + ". Update the Dialogue Machine plugin.");
            var storyJson = Story.Text(root, "StoryJson", null);
            if (storyJson == null) throw new FormatException("A runtime handoff must contain StoryJson.");
            var handoff = new Handoff
            {
                SchemaVersion = schema,
                Contract = Story.Text(root, "Contract", ""),
                StoryJson = storyJson,
                StorySha256 = Story.Text(root, "StorySha256", ""),
                StartingScene = Story.Text(root, "StartingScene", ""),
                Seed = Story.Number(root, "Seed", 0),
                MaxOperations = Story.Number(root, "MaxOperations", 1000),
                Story = Story.Parse(storyJson)
            };
            if (root.TryGetValue("NumericValues", out var numeric) && numeric is Dictionary<string, object> numbers)
                foreach (var entry in numbers)
                    if (entry.Value is double number) handoff.NumericValues[entry.Key] = (int)number;
            if (root.TryGetValue("TextValues", out var text) && text is Dictionary<string, object> texts)
                foreach (var entry in texts)
                    if (entry.Value is string value) handoff.TextValues[entry.Key] = value;
            return handoff;
        }
    }
}
