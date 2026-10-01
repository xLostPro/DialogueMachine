using System.Text.RegularExpressions;

namespace DialogueMachine.Core;

public sealed record VariableReference(string StateId, string Field);

public static class VariableStore
{
    private static readonly Regex Substitutions = new(@"\{([\w:.\-]+)\}");
    private static readonly Regex Conditions = new(@"(\[if\s+)([\w:.\-]+)(?=\s*(?:==|!=|>=|<=|>|<))", RegexOptions.IgnoreCase);
    public static bool ValidNumericId(string id) => Regex.IsMatch(id, @"\A(aff|inv):[\w.\-]+\z");
    public static bool ValidTextId(string id) => Regex.IsMatch(id, @"\A[\w.\-]+\z");
    private static IEnumerable<DialogueAction> Actions(DialogueState s) => s.DefaultAfterActions.Concat(s.Choices.SelectMany(c => c.AfterChoiceActions));
    private static string ResolveConditionKey(string id, IReadOnlySet<string> keys)
        => keys.Contains(id) ? id : keys.Contains("inv:" + id) ? "inv:" + id : "aff:" + id;

    public static IEnumerable<string> DiscoverNumeric(DialogueProject p)
    {
        var keys = new HashSet<string>(p.InitialVariables.Keys, StringComparer.Ordinal);
        var bareConditions = new List<string>();
        foreach (var s in p.ListOfCharacterStates)
        {
            foreach (var a in Actions(s))
                if (a.Type is "Affinity" or "Inventory" && !string.IsNullOrWhiteSpace(a.TargetId)) keys.Add(DialogueEngine.Key(a.Type, a.TargetId));
            foreach (var r in s.Choices.SelectMany(c => c.Requirements))
                if (r.Type is "Affinity" or "Inventory" && !string.IsNullOrWhiteSpace(r.TargetId)) keys.Add(DialogueEngine.Key(r.Type, r.TargetId));
            foreach (var text in s.Choices.Select(c => c.ButtonText).Prepend(s.Text))
            {
                foreach (Match m in Substitutions.Matches(text))
                    if (ValidNumericId(m.Groups[1].Value) && !p.TextVariables.ContainsKey(m.Groups[1].Value)) keys.Add(m.Groups[1].Value);
                foreach (Match m in Conditions.Matches(text))
                {
                    var key = m.Groups[2].Value;
                    if (key.Contains(':')) keys.Add(key); else bareConditions.Add(key);
                }
            }
        }
        foreach (var id in bareConditions) keys.Add(ResolveConditionKey(id, keys));
        return keys.Order(StringComparer.Ordinal);
    }

    public static List<VariableReference> References(DialogueProject p, string key, bool textVariable = false)
    {
        var references = new List<VariableReference>();
        var known = DiscoverNumeric(p).ToHashSet(StringComparer.Ordinal);
        void Text(string state, string value, string field)
        {
            foreach (Match m in Substitutions.Matches(value))
                if (m.Groups[1].Value == key && (textVariable || !p.TextVariables.ContainsKey(key))) references.Add(new(state, field));
            if (!textVariable)
                foreach (Match m in Conditions.Matches(value))
                    if (ResolveConditionKey(m.Groups[2].Value, known) == key) references.Add(new(state, field + " condition"));
        }
        foreach (var s in p.ListOfCharacterStates)
        {
            Text(s.State, s.Text, "Dialogue");
            if (!textVariable)
                foreach (var a in s.DefaultAfterActions)
                    if (a.Type is "Affinity" or "Inventory" && DialogueEngine.Key(a.Type, a.TargetId) == key) references.Add(new(s.State, "Scene exit action"));
            for (var i = 0; i < s.Choices.Count; i++)
            {
                var c = s.Choices[i]; Text(s.State, c.ButtonText, $"Choice {i + 1} response");
                if (textVariable) continue;
                foreach (var r in c.Requirements)
                    if (r.Type is "Affinity" or "Inventory" && DialogueEngine.Key(r.Type, r.TargetId) == key) references.Add(new(s.State, $"Choice {i + 1} condition"));
                foreach (var a in c.AfterChoiceActions)
                    if (a.Type is "Affinity" or "Inventory" && DialogueEngine.Key(a.Type, a.TargetId) == key) references.Add(new(s.State, $"Choice {i + 1} action"));
            }
        }
        return references;
    }

    public static void Rename(DialogueProject p, string oldKey, string newKey, bool textVariable = false)
    {
        var known = DiscoverNumeric(p).ToHashSet(StringComparer.Ordinal);
        if (!(textVariable ? p.TextVariables.ContainsKey(oldKey) : known.Contains(oldKey))) throw new ArgumentException("The variable does not exist.");
        if (!(textVariable ? ValidTextId(newKey) : ValidNumericId(newKey))) throw new ArgumentException("Use a valid variable ID. Numeric IDs start with aff: or inv:; text IDs contain letters, digits, dots, underscores or hyphens.");
        if (!textVariable && (oldKey.StartsWith("aff:", StringComparison.Ordinal) || oldKey.StartsWith("inv:", StringComparison.Ordinal)) && oldKey[..4] != newKey[..4]) throw new ArgumentException("Renaming keeps the variable's affinity or inventory type.");
        if (oldKey == newKey) return;
        if (known.Contains(newKey) || p.TextVariables.ContainsKey(newKey)) throw new ArgumentException("That variable ID already exists.");
        var renamedKeys = new HashSet<string>(known, StringComparer.Ordinal); renamedKeys.Remove(oldKey); renamedKeys.Add(newKey);
        string Rewrite(string value)
        {
            value = Substitutions.Replace(value, m => m.Groups[1].Value == oldKey && (textVariable || !p.TextVariables.ContainsKey(oldKey)) ? "{" + newKey + "}" : m.Value);
            return textVariable ? value : Conditions.Replace(value, m =>
            {
                var id = m.Groups[2].Value; var binding = ResolveConditionKey(id, known);
                if (binding == oldKey) return m.Groups[1].Value + newKey;
                // Introducing an inventory ID must not redirect another variable's bare alias.
                return ResolveConditionKey(id, renamedKeys) != binding ? m.Groups[1].Value + binding : m.Value;
            });
        }
        foreach (var s in p.ListOfCharacterStates)
        {
            s.Text = Rewrite(s.Text);
            foreach (var c in s.Choices) c.ButtonText = Rewrite(c.ButtonText);
            if (textVariable) continue;
            var target = newKey[4..];
            foreach (var a in Actions(s))
                if (a.Type is "Affinity" or "Inventory" && DialogueEngine.Key(a.Type, a.TargetId) == oldKey) a.TargetId = target;
            foreach (var r in s.Choices.SelectMany(c => c.Requirements))
                if (r.Type is "Affinity" or "Inventory" && DialogueEngine.Key(r.Type, r.TargetId) == oldKey) r.TargetId = target;
        }
        if (textVariable) { var value = p.TextVariables[oldKey]; p.TextVariables.Remove(oldKey); p.TextVariables[newKey] = value; }
        else { var value = p.InitialVariables.GetValueOrDefault(oldKey); p.InitialVariables.Remove(oldKey); p.InitialVariables[newKey] = value; }
    }

    public static void Delete(DialogueProject p, string key, bool textVariable = false)
    {
        if (References(p, key, textVariable).Count > 0) throw new ArgumentException("Remove this variable's references before deleting it, or rename it to update them together.");
        if (textVariable) p.TextVariables.Remove(key); else p.InitialVariables.Remove(key);
    }
}
