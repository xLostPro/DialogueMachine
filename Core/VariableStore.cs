using System.Text.RegularExpressions;
using DialogueMachine.Runtime;

namespace DialogueMachine.Core;

public sealed record VariableReference(string StateId, string Field, bool IsTemplate = false);

public static class VariableStore
{
    private static readonly Regex Substitutions = new(@"\{([\w:.\-]+)\}");
    private static readonly Regex Conditions = new(@"(\[if\s+)([\w:.\-]+)(?=\s*(?:==|!=|>=|<=|>|<))", RegexOptions.IgnoreCase);
    public static bool ValidNumericId(string id) => Regex.IsMatch(id, @"\A(aff|inv):[\w.\-]+\z");
    public static bool ValidTextId(string id) => Regex.IsMatch(id, @"\A[\w.\-]+\z");
    private static IEnumerable<DialogueAction> Actions(DialogueState s) => s.DefaultAfterActions.Concat(s.Choices.SelectMany(c => c.AfterChoiceActions));
    private static string ResolveConditionKey(string id, IReadOnlySet<string> keys)
        => keys.Contains(id) ? id : keys.Contains("inv:" + id) ? "inv:" + id : "aff:" + id;
    private static IEnumerable<(DialogueState Scene, string Label)> TemplateScenes(DialogueProject p)
        => p.SceneTemplates.Select(t => (t.Scene, "Scene template: " + t.Name)).Concat(p.ChoiceTemplates.Select(t => (new DialogueState { Choices = [t.Choice] }, "Choice template: " + t.Name)));
    private static DialogueProject TemplateContext(DialogueProject p, DialogueState scene) => new()
    {
        ListOfCharacterStates = p.ListOfCharacterStates.Concat([scene]).ToList(), InitialVariables = p.InitialVariables, TextVariables = p.TextVariables
    };

    public static IEnumerable<string> DiscoverNumeric(DialogueProject p)
    {
        var keys = new HashSet<string>(p.InitialVariables.Keys, StringComparer.Ordinal);
        var bareConditions = new List<string>();
        foreach (var s in p.ListOfCharacterStates)
        {
            foreach (var a in Actions(s))
                if (DialogueRules.IsVariableAction(a.Type) && !string.IsNullOrWhiteSpace(a.TargetId)) keys.Add(DialogueEngine.Key(a.Type, a.TargetId));
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

    /// <summary>Flag IDs (no prefix): every variable a Set or Toggle action writes. A flag is an ordinary 0/1 affinity variable.</summary>
    public static IReadOnlyList<string> DiscoverFlags(DialogueProject p)
        => p.ListOfCharacterStates.Concat(p.SceneTemplates.Select(t => t.Scene)).Concat(p.ChoiceTemplates.Select(t => new DialogueState { Choices = [t.Choice] }))
            .SelectMany(Actions).Where(a => a.Type is "Set" or "Toggle" && !string.IsNullOrWhiteSpace(a.TargetId))
            .Select(a => a.TargetId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

    /// <summary>A requirement the editor shows as "flag is set / not set": affinity ==/!= 0 or 1 on a known flag.</summary>
    public static bool IsFlagCondition(Requirement r, IReadOnlyCollection<string> flags)
        => r.Type == "Affinity" && r.Operator is "==" or "!=" && r.Value is "0" or "1" && flags.Contains(r.TargetId);

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
                    if (DialogueRules.IsVariableAction(a.Type) && DialogueEngine.Key(a.Type, a.TargetId) == key) references.Add(new(s.State, "Scene exit action"));
            for (var i = 0; i < s.Choices.Count; i++)
            {
                var c = s.Choices[i]; Text(s.State, c.ButtonText, $"Choice {i + 1} response");
                if (textVariable) continue;
                foreach (var r in c.Requirements)
                    if (r.Type is "Affinity" or "Inventory" && DialogueEngine.Key(r.Type, r.TargetId) == key) references.Add(new(s.State, $"Choice {i + 1} condition"));
                foreach (var a in c.AfterChoiceActions)
                    if (DialogueRules.IsVariableAction(a.Type) && DialogueEngine.Key(a.Type, a.TargetId) == key) references.Add(new(s.State, $"Choice {i + 1} action"));
            }
        }
        foreach (var (scene, label) in TemplateScenes(p))
        {
            // A template is inactive until inserted. Resolve its aliases in the context
            // of that insertion without introducing its keys into the live runtime.
            var context = TemplateContext(p, scene);
            // Template scene IDs may equal a live scene ID; isolate its references.
            var isolated = new DialogueProject { ListOfCharacterStates = [scene], InitialVariables = DiscoverNumeric(context).ToDictionary(k => k, _ => 0), TextVariables = p.TextVariables };
            foreach (var reference in References(isolated, key, textVariable)) references.Add(new(label, reference.Field, true));
        }
        foreach (var scenario in p.PlaytestScenarios)
        {
            var label = "Scenario: " + scenario.Name;
            if (textVariable ? scenario.TextOverrides.ContainsKey(key) : scenario.NumericOverrides.ContainsKey(key)) references.Add(new(label, "Starting override"));
            foreach (var step in scenario.Steps.Where(s => s.Kind == (textVariable ? "SetText" : "SetNumeric") && s.Key == key)) references.Add(new(label, "Recorded live edit"));
            foreach (var assertion in scenario.Assertions.Where(a => a.Kind == (textVariable ? "Text" : "Numeric") && a.Key == key)) references.Add(new(label, "Final assertion"));
        }
        return references;
    }

    public static void Rename(DialogueProject p, string oldKey, string newKey, bool textVariable = false)
    {
        var liveKnown = DiscoverNumeric(p).ToHashSet(StringComparer.Ordinal);
        var templateScopes = TemplateScenes(p).Select(t => (t.Scene, Known: DiscoverNumeric(TemplateContext(p, t.Scene)).ToHashSet(StringComparer.Ordinal))).ToList();
        var scopes = p.ListOfCharacterStates.Select(s => (Scene: s, Known: liveKnown)).Concat(templateScopes).ToList();
        var known = templateScopes.SelectMany(s => s.Known).Concat(liveKnown).ToHashSet(StringComparer.Ordinal);
        if (!(textVariable ? p.TextVariables.ContainsKey(oldKey) : known.Contains(oldKey))) throw new ArgumentException("The variable does not exist.");
        if (!(textVariable ? ValidTextId(newKey) : ValidNumericId(newKey))) throw new ArgumentException("Use a valid variable ID. Numeric IDs start with aff: or inv:; text IDs contain letters, digits, dots, underscores or hyphens.");
        if (!textVariable && (oldKey.StartsWith("aff:", StringComparison.Ordinal) || oldKey.StartsWith("inv:", StringComparison.Ordinal)) && oldKey[..4] != newKey[..4]) throw new ArgumentException("Renaming keeps the variable's affinity or inventory type.");
        if (oldKey == newKey) return;
        if (known.Contains(newKey) || p.TextVariables.ContainsKey(newKey)) throw new ArgumentException("That variable ID already exists.");
        var renamedScopes = scopes.Select(s => s.Known).Distinct().ToDictionary(keys => keys, keys =>
        {
            var renamed = new HashSet<string>(keys, StringComparer.Ordinal); renamed.Remove(oldKey); renamed.Add(newKey); return renamed;
        });
        string Rewrite(string value, HashSet<string> scopeKeys)
        {
            var renamedKeys = renamedScopes[scopeKeys];
            value = Substitutions.Replace(value, m => m.Groups[1].Value == oldKey && (textVariable || !p.TextVariables.ContainsKey(oldKey)) ? "{" + newKey + "}" : m.Value);
            return textVariable ? value : Conditions.Replace(value, m =>
            {
                var id = m.Groups[2].Value; var binding = ResolveConditionKey(id, scopeKeys);
                if (binding == oldKey) return m.Groups[1].Value + newKey;
                // Introducing an inventory ID must not redirect another variable's bare alias.
                return ResolveConditionKey(id, renamedKeys) != binding ? m.Groups[1].Value + binding : m.Value;
            });
        }
        var rebindScenarios = ScenarioStore.CaptureValidReferences(p);
        foreach (var (s, scopeKeys) in scopes)
        {
            s.Text = Rewrite(s.Text, scopeKeys);
            foreach (var c in s.Choices) c.ButtonText = Rewrite(c.ButtonText, scopeKeys);
            if (textVariable) continue;
            var target = newKey[4..];
            foreach (var a in Actions(s))
                if (DialogueRules.IsVariableAction(a.Type) && DialogueEngine.Key(a.Type, a.TargetId) == oldKey) a.TargetId = target;
            foreach (var r in s.Choices.SelectMany(c => c.Requirements))
                if (r.Type is "Affinity" or "Inventory" && DialogueEngine.Key(r.Type, r.TargetId) == oldKey) r.TargetId = target;
        }
        if (textVariable) { var value = p.TextVariables[oldKey]; p.TextVariables.Remove(oldKey); p.TextVariables[newKey] = value; }
        else { var value = p.InitialVariables.GetValueOrDefault(oldKey); p.InitialVariables.Remove(oldKey); p.InitialVariables[newKey] = value; }
        foreach (var scenario in p.PlaytestScenarios)
        {
            if (textVariable) { if (scenario.TextOverrides.Remove(oldKey, out var value)) scenario.TextOverrides[newKey] = value; }
            else { if (scenario.NumericOverrides.Remove(oldKey, out var value)) scenario.NumericOverrides[newKey] = value; }
            foreach (var step in scenario.Steps.Where(s => s.Kind == (textVariable ? "SetText" : "SetNumeric") && s.Key == oldKey)) step.Key = newKey;
            foreach (var assertion in scenario.Assertions.Where(a => a.Kind == (textVariable ? "Text" : "Numeric") && a.Key == oldKey)) assertion.Key = newKey;
        }
        rebindScenarios();
    }

    public static void Delete(DialogueProject p, string key, bool textVariable = false)
    {
        if (References(p, key, textVariable).Count > 0) throw new ArgumentException("Remove this variable's references before deleting it, or rename it to update them together.");
        if (textVariable) p.TextVariables.Remove(key); else p.InitialVariables.Remove(key);
    }

    /// <summary>Roster names plus every speaker used by a scene, for autocomplete and the manager.</summary>
    public static IReadOnlyList<string> DiscoverSpeakers(DialogueProject p)
        => p.ListOfCharacterStates
            .Select(s => s.Speaker)
            .Concat(p.Characters.Select(c => c.Name))
            .Where(sp => !string.IsNullOrWhiteSpace(sp))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

    public static List<VariableReference> SpeakerReferences(DialogueProject p, string speaker)
    {
        var references = new List<VariableReference>();
        foreach (var s in p.ListOfCharacterStates)
            if (s.Speaker == speaker) references.Add(new(s.State, "Speaker"));
        foreach (var t in p.SceneTemplates)
            if (t.Scene.Speaker == speaker) references.Add(new("Scene template: " + t.Name, "Speaker", true));
        return references;
    }

    public static void RenameSpeaker(DialogueProject p, string oldSpeaker, string newSpeaker)
    {
        if (string.IsNullOrWhiteSpace(oldSpeaker)) throw new ArgumentException("Existing speaker name cannot be blank.");
        var trimmed = newSpeaker?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(trimmed)) throw new ArgumentException("Speaker name cannot be blank.");
        var references = SpeakerReferences(p, oldSpeaker);
        if (references.Count == 0 && CharacterRoster.Find(p, oldSpeaker) is null) throw new ArgumentException($"Speaker ‘{oldSpeaker}’ is not used in this project.");
        if (oldSpeaker == trimmed) return;
        CharacterRoster.Rename(p, oldSpeaker, trimmed);
        var rebind = ScenarioStore.CaptureValidReferences(p);
        foreach (var s in p.ListOfCharacterStates)
            if (s.Speaker == oldSpeaker) s.Metadata["speaker"] = trimmed;
        foreach (var t in p.SceneTemplates)
            if (t.Scene.Speaker == oldSpeaker) t.Scene.Metadata["speaker"] = trimmed;
        rebind();
    }
}
