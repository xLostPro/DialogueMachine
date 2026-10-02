using System.Text;

namespace DialogueMachine.Core;

[Flags]
public enum SearchFields { None = 0, Dialogue = 1, Responses = 2, Titles = 4, Notes = 8, All = Dialogue | Responses | Titles | Notes }
public sealed record SearchOptions(string Query, SearchFields Fields = SearchFields.All, bool MatchCase = false, bool WholeWord = false, int? SceneIndex = null);
public sealed record TextMatch(int SceneIndex, string StateId, SearchFields Field, int? ChoiceIndex, int Start, int Length, string OriginalText)
{
    public string FieldLabel => Field switch { SearchFields.Dialogue => "Dialogue", SearchFields.Responses => $"Choice {ChoiceIndex + 1} response", SearchFields.Titles => "Scene title", _ => "Writer's notes" };
}
public sealed record SearchResults(IReadOnlyList<TextMatch> Matches, int TotalMatches);

// Literal authoring search only: identities, connections, logic and extension data stay untouched.
public static class ProjectSearch
{
    public const int PreviewLimit = 200;
    private sealed record TextField(int SceneIndex, DialogueState Scene, SearchFields Field, int? ChoiceIndex, string Text)
    {
        public void Set(string value)
        {
            switch (Field)
            {
                case SearchFields.Dialogue: Scene.Text = value; break;
                case SearchFields.Responses: Scene.Choices[ChoiceIndex!.Value].ButtonText = value; break;
                case SearchFields.Titles: Scene.Metadata["title"] = value; break;
                case SearchFields.Notes: Scene.Metadata["notes"] = value; break;
            }
        }
    }
    public static SearchResults Find(DialogueProject project, SearchOptions options)
    {
        var matches = new List<TextMatch>(); var count = 0;
        foreach (var field in Fields(project, options))
            foreach (var start in Positions(field.Text, options))
            {
                count++;
                if (matches.Count < PreviewLimit) matches.Add(new(field.SceneIndex, field.Scene.State, field.Field, field.ChoiceIndex, start, options.Query.Length, field.Text));
            }
        return new(matches, count);
    }
    public static int ReplaceAll(DialogueProject project, SearchOptions options, string replacement)
    {
        var edits = new List<(TextField Field, string Text)>(); var count = 0;
        foreach (var field in Fields(project, options))
        {
            StringBuilder? text = null; var previous = 0;
            foreach (var start in Positions(field.Text, options))
            {
                text ??= new StringBuilder(); text.Append(field.Text, previous, start - previous).Append(replacement);
                previous = start + options.Query.Length; count++;
            }
            if (text is not null)
            {
                text.Append(field.Text, previous, field.Text.Length - previous);
                var next = text.ToString(); if (next != field.Text) edits.Add((field, next));
            }
        }
        foreach (var edit in edits) edit.Field.Set(edit.Text);
        return count;
    }
    public static void ReplaceMatch(DialogueProject project, TextMatch match, string replacement)
    {
        // A preview is valid only for the original field. Reject stale offsets before writing.
        var field = Fields(project, new("", match.Field, SceneIndex: match.SceneIndex)).FirstOrDefault(f => f.Field == match.Field && f.ChoiceIndex == match.ChoiceIndex);
        if (field is null || field.Scene.State != match.StateId || field.Text != match.OriginalText || match.Start < 0 || match.Length <= 0 || match.Start > field.Text.Length - match.Length)
            throw new InvalidOperationException("This match has changed. Search again before replacing it.");
        var next = field.Text[..match.Start] + replacement + field.Text[(match.Start + match.Length)..];
        if (next != field.Text) field.Set(next);
    }
    private static IEnumerable<TextField> Fields(DialogueProject project, SearchOptions options)
    {
        for (var i = 0; i < project.ListOfCharacterStates.Count; i++)
        {
            if (options.SceneIndex is { } index && i != index) continue;
            var scene = project.ListOfCharacterStates[i];
            if (options.Fields.HasFlag(SearchFields.Titles)) yield return new(i, scene, SearchFields.Titles, null, scene.Title);
            if (options.Fields.HasFlag(SearchFields.Dialogue)) yield return new(i, scene, SearchFields.Dialogue, null, scene.Text);
            if (options.Fields.HasFlag(SearchFields.Responses))
                for (var c = 0; c < scene.Choices.Count; c++) yield return new(i, scene, SearchFields.Responses, c, scene.Choices[c].ButtonText);
            if (options.Fields.HasFlag(SearchFields.Notes)) yield return new(i, scene, SearchFields.Notes, null, scene.Metadata.GetValueOrDefault("notes", ""));
        }
    }
    private static IEnumerable<int> Positions(string text, SearchOptions options)
    {
        if (options.Query.Length == 0) yield break;
        var comparison = options.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var offset = 0;
        while (offset <= text.Length - options.Query.Length)
        {
            var start = text.IndexOf(options.Query, offset, comparison); if (start < 0) yield break;
            var end = start + options.Query.Length;
            if (!options.WholeWord || ((start == 0 || !WordCharacter(text[start - 1])) && (end == text.Length || !WordCharacter(text[end]))))
            { yield return start; offset = end; }
            else offset = start + 1;
        }
    }
    private static bool WordCharacter(char c) => char.IsLetterOrDigit(c) || c == '_' || char.GetUnicodeCategory(c) is System.Globalization.UnicodeCategory.NonSpacingMark or System.Globalization.UnicodeCategory.SpacingCombiningMark;
}
