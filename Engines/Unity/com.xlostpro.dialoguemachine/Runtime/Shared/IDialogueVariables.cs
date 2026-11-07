using System;
using System.Collections.Generic;
using System.Linq;

namespace DialogueMachine.Runtime
{
    // Implement this to let the game own inventory, affinity and text values.
    public interface IDialogueVariables
    {
        bool HasNumber(string key);
        int GetNumber(string key);
        void SetNumber(string key, int value);
        // Returns null when the text variable does not exist.
        string GetText(string key);
        void SetText(string key, string value);
        IEnumerable<string> Keys { get; }
    }

    public sealed class DictionaryVariables : IDialogueVariables
    {
        private readonly Dictionary<string, int> numbers = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> texts = new Dictionary<string, string>(StringComparer.Ordinal);

        public bool HasNumber(string key) => numbers.ContainsKey(key);
        public int GetNumber(string key) => numbers.GetValueOrDefault(key);
        public void SetNumber(string key, int value) => numbers[key] = value;
        public string GetText(string key) => texts.GetValueOrDefault(key);
        public void SetText(string key, string value) => texts[key] = value;
        public IEnumerable<string> Keys => numbers.Keys.Concat(texts.Keys).Distinct().ToList();

        // For game save files: the same "numeric"/"text" JSON shape as DialogueRunner.SaveState.
        public string ToJson() => MiniJson.Serialize(new Dictionary<string, object> { ["numeric"] = numbers, ["text"] = texts });

        // Replaces every value with the saved ones.
        public void LoadJson(string json)
        {
            if (!(MiniJson.Parse(json) is Dictionary<string, object> state)) throw new FormatException("Saved variables must be a JSON object.");
            numbers.Clear();
            texts.Clear();
            if (state.TryGetValue("numeric", out var numeric) && numeric is Dictionary<string, object> savedNumbers)
                foreach (var entry in savedNumbers)
                    if (entry.Value is double number) numbers[entry.Key] = (int)number;
            if (state.TryGetValue("text", out var text) && text is Dictionary<string, object> savedTexts)
                foreach (var entry in savedTexts)
                    if (entry.Value is string value) texts[entry.Key] = value;
        }
    }
}
