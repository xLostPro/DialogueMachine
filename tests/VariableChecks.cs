using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DialogueMachine;
using DialogueMachine.Core;
using DialogueMachine.UI;
using System.Text.Json;
using static UiHelpers;


internal static class VariableChecks
{
    public static void Core(Action<bool, string> Check, Action<Action, string> Throws)
    {
        var vars = new Dictionary<string,int> { ["aff:trust"] = -2, ["inv:coin"] = 1 };
        var variableStory = Templates.Blank(); variableStory.InitialVariables = new() { ["aff:trust"] = 3, ["inv:trust"] = 2, ["aff:trust_more"] = 8, ["aff:unused"] = 0 };
        var variableScene = variableStory.ListOfCharacterStates[0];
        variableScene.Text = "[IF aff:trust >= 1]{aff:trust}[else]none[/if] [if trust >= 1]inventory[/if] {player_name} literal aff:trust {aff:trust_more}";
        variableScene.DefaultAfterActions = [new() { TargetId = "trust", Amount = 1 }, new() { Type = "Event", TargetId = "trust" }];
        variableScene.Choices = [new() { ButtonText = "{player_name} [if aff:trust > 0]Yes[/if]", NextStateId = "[EXIT]", Requirements = [new() { TargetId = "trust", Value = "1" }], AfterChoiceActions = [new() { TargetId = "trust", Amount = 1 }] }];
        Check(VariableStore.References(variableStory, "aff:trust").Count == 6 && VariableStore.References(variableStory, "inv:trust").Count == 1, "reference counts distinguish typed logic, substitutions and inventory-first bare conditions");
        var variableBefore = ProjectStore.Serialize(variableStory);
        Throws(() => VariableStore.Rename(variableStory, "aff:trust", "aff:trust_more"), "rename rejects an existing variable ID");
        Throws(() => VariableStore.Rename(variableStory, "aff:trust", "inv:rapport"), "numeric rename cannot change variable type");
        Throws(() => VariableStore.Rename(variableStory, "aff:trust", "aff:rapport\n"), "numeric IDs reject embedded or trailing line breaks");
        Throws(() => VariableStore.Delete(variableStory, "aff:trust"), "referenced numeric variables cannot be deleted");
        Check(ProjectStore.Serialize(variableStory) == variableBefore, "rejected variable operations leave the entire project unchanged");
        VariableStore.Rename(variableStory, "aff:trust", "aff:rapport");
        Check(variableStory.InitialVariables["aff:rapport"] == 3 && !variableStory.InitialVariables.ContainsKey("aff:trust") && variableScene.DefaultAfterActions[0].TargetId == "rapport" && variableScene.Choices[0].Requirements[0].TargetId == "rapport" && variableScene.Choices[0].AfterChoiceActions[0].TargetId == "rapport", "rename moves starting values and rewrites scene and response logic");
        Check(variableScene.Text.Contains("[IF aff:rapport >= 1]{aff:rapport}") && variableScene.Text.Contains("[if trust >= 1]") && variableScene.Text.Contains("literal aff:trust {aff:trust_more}") && variableScene.DefaultAfterActions[1].TargetId == "trust", "numeric rename preserves text literals, similar IDs, unrelated events and inventory aliases");
        VariableStore.Rename(variableStory, "inv:trust", "inv:tokens");
        Check(variableScene.Text.Contains("[if inv:tokens >= 1]inventory[/if]"), "renaming an inventory variable qualifies its bare inline references");
        Check(VariableStore.References(variableStory, "player_name", true).Count == 2, "text references include dialogue and response labels");
        VariableStore.Rename(variableStory, "player_name", "hero_name", true);
        Check(variableStory.TextVariables["hero_name"] == "Traveller" && variableScene.Text.Contains("{hero_name}") && variableScene.Choices[0].ButtonText.StartsWith("{hero_name}"), "text rename preserves its value and updates all runtime substitution fields");
        VariableStore.Delete(variableStory, "aff:unused"); VariableStore.Delete(variableStory, "npc_name", true);
        Check(!variableStory.InitialVariables.ContainsKey("aff:unused") && !variableStory.TextVariables.ContainsKey("npc_name"), "unused numeric and text variables can be deleted");
        var variableRoundtrip = ProjectStore.Parse(ProjectStore.Serialize(variableStory));
        Check(VariableStore.References(variableRoundtrip, "aff:rapport").Count == 6 && !DialogueEngine.Validate(variableRoundtrip).Any(), "renamed references survive desktop round trips and validation");
        var articleStory = Templates.Blank(); articleStory.TextVariables["calling"] = "accountant"; articleStory.ListOfCharacterStates[0].Text = "You are {a:calling}, {calling}.";
        Check(VariableStore.References(articleStory, "calling", true).Count == 2 && !DialogueEngine.Validate(articleStory).Any(d => d.Message.Contains("a:calling")), "{a:key} counts as a reference to its text variable and is not an unknown substitution");
        Throws(() => VariableStore.Delete(articleStory, "calling", true), "a text variable used only through {a:key} cannot be deleted");
        VariableStore.Rename(articleStory, "calling", "job", true);
        Check(articleStory.ListOfCharacterStates[0].Text == "You are {a:job}, {job}.", "text rename rewrites {a:key} placeholders");
        var textCondStory = Templates.Blank(); var textCondScene = textCondStory.ListOfCharacterStates[0];
        textCondScene.Text = "[if calling == \"Plumber\"]Pipes[else]Other[/if] [if calling != \"\"]set[/if] [if trust >= 1]x[/if]";
        textCondScene.Choices = [new() { ButtonText = "Go", NextStateId = "[EXIT]", Requirements = [new() { Type = "Text", TargetId = "calling", Operator = "!=", Value = "" }] }];
        Check(VariableStore.DiscoverText(textCondStory).Contains("calling") && !DialogueEngine.DiscoverVariables(textCondStory).Any(k => k.EndsWith(":calling")), "text conditions discover a text variable, never a numeric one");
        Check(!DialogueEngine.Validate(textCondStory).Any(d => d.Severity == "Error"), "quoted [if] checks and Text requirements validate");
        Check(VariableStore.References(textCondStory, "calling", true).Count == 3, "text condition references include inline checks and choice conditions");
        VariableStore.Rename(textCondStory, "calling", "job", true);
        Check(textCondScene.Text.StartsWith("[if job == \"Plumber\"]") && textCondScene.Text.Contains("[if job != \"\"]") && textCondScene.Choices[0].Requirements[0].TargetId == "job", "text rename rewrites inline checks and Text requirements");
        textCondScene.Choices[0].Requirements[0].Operator = ">=";
        Check(DialogueEngine.Validate(textCondStory).Any(d => d.Severity == "Error" && d.Message.Contains("text conditions use == or !=")), "Text requirements reject ordering comparisons");
        bool Malformed(string text) { var story = Templates.Blank(); story.ListOfCharacterStates[0].Text = text; return DialogueEngine.Validate(story).Any(d => d.Message.Contains("inline conditional tags are malformed")); }
        Check(Malformed("[if job >= \"a\"]x[/if]") && Malformed("[if job == \"a]b\"]x[/if]") && !Malformed("[if job == \"a b\"]x[/if]"), "quoted [if] values allow only == / != and no brackets");
        var textSim = new Simulation(textCondStory);
        Check(textSim.Pages[0].StartsWith("Other"), "a text variable no one has set compares as empty in playtest");
        var bareStory = Templates.Blank(); bareStory.ListOfCharacterStates[0].Text = "[if courage >= 1]Brave[else]Careful[/if] literal aff:not_a_reference";
        Check(DialogueEngine.DiscoverVariables(bareStory).SequenceEqual(new[] { "aff:courage" }), "discovery includes bare inline conditions and excludes numeric-looking prose");
        VariableStore.Rename(bareStory, "aff:courage", "aff:bravery");
        var bareSimulation = new Simulation(bareStory); bareSimulation.SetVariable("aff:bravery", 1);
        Check(bareSimulation.Pages[0].StartsWith("Brave"), "renamed inline-only variables are initialized and work during playtesting");
        var shadowStory = Templates.Blank(); shadowStory.InitialVariables["aff:trust"] = 1; shadowStory.TextVariables["aff:trust"] = "label"; shadowStory.ListOfCharacterStates[0].Text = "{aff:trust} [if aff:trust >= 1]yes[/if]";
        VariableStore.Rename(shadowStory, "aff:trust", "aff:confidence");
        Check(shadowStory.ListOfCharacterStates[0].Text == "{aff:trust} [if aff:confidence >= 1]yes[/if]", "numeric renames preserve legacy text substitutions that shadow the same ID");
        var aliasStory = Templates.Blank(); aliasStory.InitialVariables["aff:courage"] = 1; aliasStory.InitialVariables["inv:coin"] = 0;
        aliasStory.ListOfCharacterStates[0].Text = "[if courage >= 1]Brave[else]Careful[/if]";
        VariableStore.Rename(aliasStory, "inv:coin", "inv:courage");
        Check(aliasStory.ListOfCharacterStates[0].Text.StartsWith("[if aff:courage") && new Simulation(aliasStory).Pages[0] == "Brave", "renaming another variable cannot redirect existing bare condition aliases");
        var legacyVariableStory = Templates.Blank(); legacyVariableStory.InitialVariables["aff:old:id"] = 1;
        Throws(() => VariableStore.Rename(legacyVariableStory, "aff:old:id", "inv:tokens"), "repairing a legacy malformed numeric ID preserves its original type");
        var speakerStory = Templates.Blank();
        speakerStory.ListOfCharacterStates[0].Metadata["speaker"] = "Mara";
        speakerStory.ListOfCharacterStates.Add(new() { State = "s2", Metadata = new() { ["speaker"] = "Guard" } });
        speakerStory.ListOfCharacterStates.Add(new() { State = "s3", Metadata = new() { ["speaker"] = "Mara" } });
        speakerStory.ListOfCharacterStates.Add(new() { State = "s4", Metadata = new() { ["speaker"] = "  " } });
        speakerStory.ListOfCharacterStates.Add(new() { State = "s5" });
        Check(VariableStore.DiscoverSpeakers(speakerStory).SequenceEqual(new[] { "Character", "Guard", "Mara" }), "speaker discovery returns distinct sorted speakers and excludes whitespace-only values");
        speakerStory.SceneTemplates.Add(new() { Name = "Intro", Scene = Templates.Scene("t1", "Template Title", "Mara", "Text", 0, 0) });
        var maraRefs = VariableStore.SpeakerReferences(speakerStory, "Mara");
        Check(maraRefs.Count == 3 && maraRefs.Count(r => r.IsTemplate) == 1 && maraRefs.Count(r => !r.IsTemplate) == 2, "speaker references identify scenes and templates");
        Throws(() => VariableStore.RenameSpeaker(speakerStory, "Mara", ""), "speaker rename rejects empty new name");
        Throws(() => VariableStore.RenameSpeaker(speakerStory, "", "Captain"), "speaker rename rejects empty old name");
        Throws(() => VariableStore.RenameSpeaker(speakerStory, "Nonexistent", "New"), "speaker rename throws on nonexistent speaker");
        VariableStore.RenameSpeaker(speakerStory, "Mara", "Mara");
        VariableStore.RenameSpeaker(speakerStory, "Mara", "Captain Mara");
        Check(speakerStory.ListOfCharacterStates.Where(s => s.State is "start" or "s3").All(s => s.Speaker == "Captain Mara"), "speaker rename updates scenes");
        Check(speakerStory.SceneTemplates[0].Scene.Speaker == "Captain Mara", "speaker rename updates scene templates");
        Check(!VariableStore.DiscoverSpeakers(speakerStory).Contains("Mara") && VariableStore.DiscoverSpeakers(speakerStory).Contains("Captain Mara"), "discovered speakers reflect project-wide speaker rename");

        var roster = Templates.Blank(); roster.ListOfCharacterStates[0].Metadata["speaker"] = "Mara";
        CharacterRoster.SetColor(roster, "mara", "#E07A5F");
        CharacterRoster.Add(roster, "Guard");
        Check(roster.Characters.Count == 2 && CharacterRoster.ColorFor(roster, "Mara") == "#E07A5F" && CharacterRoster.ColorFor(roster, "Guard") is null, "roster colours match speakers case-insensitively and swatches are optional");
        Check(VariableStore.DiscoverSpeakers(roster).Contains("Guard"), "roster characters are offered for autocomplete before any scene uses them");
        Throws(() => CharacterRoster.SetColor(roster, "Mara", "red"), "roster rejects non-hex colours");
        Throws(() => CharacterRoster.Add(roster, " guard "), "roster rejects duplicate names");
        VariableStore.RenameSpeaker(roster, "Guard", "Sentry");
        VariableStore.RenameSpeaker(roster, "Mara", "Captain Mara");
        Check(CharacterRoster.ColorFor(roster, "Captain Mara") == "#E07A5F" && CharacterRoster.Find(roster, "Sentry") is not null && roster.Characters.Count == 2, "speaker rename carries the roster entry, including unused characters");
        roster.Characters[0].Extra = new() { ["Voice"] = JsonDocument.Parse("\"low\"").RootElement.Clone() };
        var rosterJson = ProjectStore.Serialize(roster);
        var reloaded = ProjectStore.Parse(rosterJson);
        Check(ProjectStore.Serialize(reloaded) == rosterJson && rosterJson.Contains("\"Voice\""), "roster and unknown character fields round-trip");
        using (var web = JsonDocument.Parse(ProjectStore.StoryExport(roster)))
            Check(!web.RootElement.TryGetProperty("Characters", out _) && web.RootElement.GetProperty("ListOfCharacterStates")[0].GetProperty("Metadata").GetProperty("speaker").GetString() == "Captain Mara", "story export omits the roster and keeps the plain speaker string");
        var damaged = ProjectStore.Parse(rosterJson.Replace("\"Characters\": [", "\"Characters\": [null, {\"Name\": \" \"}, {\"Name\": \"captain mara\"}, "));
        Check(damaged.Characters.Count == 2, "load drops null, blank and duplicate roster entries");
        Check(ProjectStore.Parse("{\"ListOfCharacterStates\": []}").Characters.Count == 0, "projects without a roster still open");
    }
}
