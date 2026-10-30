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
    }
}
