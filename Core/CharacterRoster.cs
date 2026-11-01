using System.Text.RegularExpressions;

namespace DialogueMachine.Core;

/// <summary>Character roster: names with optional colour swatches. Desktop-only; website exports keep the plain speaker string.</summary>
public static partial class CharacterRoster
{
    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")] private static partial Regex HexColor();
    public static bool ValidColor(string? color) => color is not null && HexColor().IsMatch(color);

    public static Character? Find(DialogueProject p, string? name)
        => p.Characters.FirstOrDefault(c => c.Name.Equals(name?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The swatch for a speaker, or null when it has none.</summary>
    public static string? ColorFor(DialogueProject p, string speaker)
        => Find(p, speaker) is { Color: { } color } && ValidColor(color) ? color : null;

    /// <summary>Sets a swatch (null or blank clears it), adding the speaker to the roster when needed.</summary>
    public static void SetColor(DialogueProject p, string name, string? color)
    {
        name = name.Trim();
        if (name.Length == 0) throw new ArgumentException("Character name cannot be blank.");
        color = string.IsNullOrWhiteSpace(color) ? null : color.Trim();
        if (color is not null && !ValidColor(color)) throw new ArgumentException("Use a colour like #E07A5F.");
        var character = Find(p, name);
        if (character is null) p.Characters.Add(new() { Name = name, Color = color });
        else character.Color = color;
    }

    public static void Add(DialogueProject p, string name)
    {
        name = name.Trim();
        if (name.Length == 0) throw new ArgumentException("Character name cannot be blank.");
        if (Find(p, name) is not null) throw new ArgumentException($"‘{name}’ is already in the roster.");
        p.Characters.Add(new() { Name = name });
    }

    /// <summary>Removes the roster entry only; scenes keep their speaker text.</summary>
    public static void Remove(DialogueProject p, string name) => p.Characters.RemoveAll(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Follows a speaker rename; if the new name is already on the roster, that entry wins.</summary>
    internal static void Rename(DialogueProject p, string oldName, string newName)
    {
        var old = Find(p, oldName);
        if (old is null) return;
        if (Find(p, newName) is { } existing && existing != old) { existing.Color ??= old.Color; p.Characters.Remove(old); }
        else old.Name = newName;
    }

    /// <summary>Repairs hand-edited rosters on load instead of rejecting the file.</summary>
    internal static void Normalize(DialogueProject p)
    {
        p.Characters ??= [];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        p.Characters.RemoveAll(c => c is null || string.IsNullOrWhiteSpace(c.Name) || !seen.Add(c.Name.Trim()));
        foreach (var c in p.Characters) c.Name = c.Name.Trim();
    }
}
