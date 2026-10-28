using System.Text;

namespace DialogueMachine.Core;

public sealed record LocalizationRow(string Key, string StateId, string ChoiceId, string Speaker, string Field, string SourceText, string TranslatedText);
public sealed record LocalizationImportReport(int UpdatedFields, int SkippedRows, IReadOnlyList<Diagnostic> Diagnostics);

public static class LocalizationStore
{
    private const int MaxCsvBytes = 20 * 1024 * 1024;

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var needsQuotes = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r') || value.StartsWith(' ') || value.EndsWith(' ');
        if (needsQuotes)
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }

    public static string ExportCsv(DialogueProject project)
    {
        var sb = new StringBuilder();
        sb.Append("Key,SceneId,ChoiceId,Speaker,Field,SourceText,TranslatedText\n");
        foreach (var s in project.ListOfCharacterStates)
        {
            if (s.Metadata.TryGetValue("title", out var title) && !string.IsNullOrEmpty(title))
            {
                var key = $"{s.State}:title";
                sb.Append($"{Escape(key)},{Escape(s.State)},,{Escape(s.Speaker)},Title,{Escape(title)},{Escape(title)}\n");
            }
            var dialogueKey = $"{s.State}:dialogue";
            sb.Append($"{Escape(dialogueKey)},{Escape(s.State)},,{Escape(s.Speaker)},Dialogue,{Escape(s.Text)},{Escape(s.Text)}\n");

            foreach (var c in s.Choices)
            {
                if (c.InternalId == Guid.Empty) c.InternalId = Guid.NewGuid();
                var choiceId = c.InternalId.ToString("D");
                var choiceKey = $"{s.State}:choice:{choiceId}";
                sb.Append($"{Escape(choiceKey)},{Escape(s.State)},{Escape(choiceId)},{Escape(s.Speaker)},Response,{Escape(c.ButtonText)},{Escape(c.ButtonText)}\n");
            }
        }
        return sb.ToString();
    }

    public static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>();
        var currentRow = new List<string>();
        var currentField = new StringBuilder();
        var inQuotes = false;
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        currentField.Append('"');
                        i += 2;
                    }
                    else
                    {
                        inQuotes = false;
                        i++;
                    }
                }
                else
                {
                    currentField.Append(c);
                    i++;
                }
            }
            else
            {
                if (c == '"')
                {
                    inQuotes = true;
                    i++;
                }
                else if (c == ',')
                {
                    currentRow.Add(currentField.ToString());
                    currentField.Clear();
                    i++;
                }
                else if (c == '\r')
                {
                    if (i + 1 < text.Length && text[i + 1] == '\n') i++;
                    currentRow.Add(currentField.ToString());
                    currentField.Clear();
                    rows.Add(currentRow);
                    currentRow = new List<string>();
                    i++;
                }
                else if (c == '\n')
                {
                    currentRow.Add(currentField.ToString());
                    currentField.Clear();
                    rows.Add(currentRow);
                    currentRow = new List<string>();
                    i++;
                }
                else
                {
                    currentField.Append(c);
                    i++;
                }
            }
        }
        if (inQuotes)
            throw new InvalidDataException("CSV contains unclosed quoted string.");
        if (currentField.Length > 0 || currentRow.Count > 0)
        {
            currentRow.Add(currentField.ToString());
            rows.Add(currentRow);
        }
        return rows;
    }

    public static LocalizationImportReport ImportCsv(DialogueProject project, string csvContent)
    {
        if (string.IsNullOrWhiteSpace(csvContent))
            throw new InvalidDataException("CSV content is empty.");
        if (csvContent.Length > MaxCsvBytes)
            throw new InvalidDataException("CSV file exceeds maximum allowed size of 20 MiB.");

        var rows = ParseCsv(csvContent);
        if (rows.Count == 0)
            throw new InvalidDataException("CSV content is empty.");

        var header = rows[0];
        int Col(string name)
        {
            var idx = header.FindIndex(h => string.Equals(h.Trim(), name, StringComparison.OrdinalIgnoreCase));
            if (idx == -1)
                idx = header.FindIndex(h => string.Equals(h.Trim().Replace("_", "").Replace(" ", ""), name.Replace("_", "").Replace(" ", ""), StringComparison.OrdinalIgnoreCase));
            return idx;
        }

        var keyIdx = Col("Key");
        var sceneIdIdx = Col("SceneId");
        var choiceIdIdx = Col("ChoiceId");
        var fieldIdx = Col("Field");
        var translatedTextIdx = Col("TranslatedText");

        if (keyIdx == -1 || sceneIdIdx == -1 || fieldIdx == -1 || translatedTextIdx == -1)
            throw new InvalidDataException("CSV header is missing required columns (Key, SceneId, Field, TranslatedText).");

        var diagnostics = new List<Diagnostic>();
        var updates = new List<Action>();
        var updatedCount = 0;
        var skippedCount = 0;

        for (var rowIndex = 1; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            if (row.All(string.IsNullOrWhiteSpace)) continue;

            string GetCol(int idx) => idx >= 0 && idx < row.Count ? row[idx] : "";

            var key = GetCol(keyIdx).Trim();
            var sceneId = GetCol(sceneIdIdx).Trim();
            var choiceId = GetCol(choiceIdIdx).Trim();
            var field = GetCol(fieldIdx).Trim();
            var translated = GetCol(translatedTextIdx);

            var scene = project.ListOfCharacterStates.FirstOrDefault(s => string.Equals(s.State, sceneId, StringComparison.Ordinal));
            if (scene is null)
            {
                skippedCount++;
                continue;
            }

            if (string.Equals(field, "Title", StringComparison.OrdinalIgnoreCase))
            {
                var current = scene.Title;
                if (!string.IsNullOrEmpty(translated) && translated != current)
                {
                    var diags = DialogueText.Validate(translated, scene.State, "Scene title");
                    diagnostics.AddRange(diags);
                    var error = diags.FirstOrDefault(d => d.Severity == "Error");
                    if (error is not null)
                        throw new InvalidDataException($"Translation error in row '{key}': {error.Message}");
                    updates.Add(() => scene.Metadata["title"] = translated);
                    updatedCount++;
                }
                else
                {
                    skippedCount++;
                }
            }
            else if (string.Equals(field, "Dialogue", StringComparison.OrdinalIgnoreCase))
            {
                var current = scene.Text;
                if (!string.IsNullOrEmpty(translated) && translated != current)
                {
                    var diags = DialogueText.Validate(translated, scene.State, "Dialogue");
                    diagnostics.AddRange(diags);
                    var error = diags.FirstOrDefault(d => d.Severity == "Error");
                    if (error is not null)
                        throw new InvalidDataException($"Translation error in row '{key}': {error.Message}");
                    updates.Add(() => scene.Text = translated);
                    updatedCount++;
                }
                else
                {
                    skippedCount++;
                }
            }
            else if (string.Equals(field, "Response", StringComparison.OrdinalIgnoreCase))
            {
                Choice? choice = null;
                if (!string.IsNullOrEmpty(choiceId) && Guid.TryParse(choiceId, out var guid))
                    choice = scene.Choices.FirstOrDefault(c => c.InternalId == guid);
                if (choice is null && key.Contains(":choice:", StringComparison.OrdinalIgnoreCase))
                {
                    var keyGuidStr = key[(key.LastIndexOf(':') + 1)..];
                    if (Guid.TryParse(keyGuidStr, out var keyGuid))
                        choice = scene.Choices.FirstOrDefault(c => c.InternalId == keyGuid);
                }

                if (choice is null)
                {
                    skippedCount++;
                    continue;
                }

                var current = choice.ButtonText;
                if (!string.IsNullOrEmpty(translated) && translated != current)
                {
                    var diags = DialogueText.Validate(translated, scene.State, "Response");
                    diagnostics.AddRange(diags);
                    var error = diags.FirstOrDefault(d => d.Severity == "Error");
                    if (error is not null)
                        throw new InvalidDataException($"Translation error in row '{key}': {error.Message}");
                    var targetChoice = choice;
                    updates.Add(() => targetChoice.ButtonText = translated);
                    updatedCount++;
                }
                else
                {
                    skippedCount++;
                }
            }
            else
            {
                skippedCount++;
            }
        }

        foreach (var update in updates) update();

        return new LocalizationImportReport(updatedCount, skippedCount, diagnostics);
    }
}
