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

try
{
if (args.Contains("--write-scenario-fixtures"))
{
    ProjectStore.AtomicWrite(Path.GetFullPath("tests/Fixtures/repeatable.dialogue.json"), ProjectStore.Serialize(ScenarioChecks.Fixture()));
    return;
}
var passed = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); passed++; Console.WriteLine("PASS " + message); }
void Throws(Action action, string message) { try { action(); } catch { Check(true, message); return; } throw new Exception(message); }
if (args.FirstOrDefault() == "--scenario-checks")
{
    var scenarioOutput = args.ElementAtOrDefault(1) ?? "artifacts/stage3"; Directory.CreateDirectory(scenarioOutput);
    ScenarioChecks.Core(Check, Throws, scenarioOutput);
    AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    void ScenarioCapture(Window window, string name) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame() ?? throw new Exception("No rendered frame"); frame.Save(Path.Combine(scenarioOutput, name + ".png"), PngBitmapEncoderOptions.Default); }
    ScenarioChecks.UI(Check, scenarioOutput, ScenarioCapture);
    Console.WriteLine($"SUCCESS: {passed} scenario checks. Screenshots: {scenarioOutput}"); return;
}

var p = Templates.Harbour();
Check(DialogueEngine.Validate(p).Count == 0, "example is valid and every scene can be reached");
var roundtrip = ProjectStore.Parse(ProjectStore.Serialize(p));
Check(roundtrip.ListOfCharacterStates[0].Choices[1].Requirements.Count == 1 && roundtrip.Entry == "arrival", "desktop JSON preserves conditions and entry");
var legacy = """{"ListOfCharacterStates":[{"State":"legacy","Text":"Hi","DefaultNextId":"[EXIT]","FutureField":{"keep":42},"Choices":[{"Visibility":0,"ButtonText":"Bye","NextStateId":"[EXIT]"}]}]}""";
var imported = ProjectStore.Parse(legacy);
Check(imported.ListOfCharacterStates[0].Choices[0].Visibility == VisibilityMode.Hide, "website numeric visibility enums import");
Check(ProjectStore.Serialize(imported).Contains("FutureField"), "unknown schema fields survive round trips");
Check(ProjectStore.Parse(ProjectStore.Serialize(imported)).ListOfCharacterStates[0].Choices[0].Visibility == VisibilityMode.Hide, "desktop string enum round trip");
Throws(() => ProjectStore.Parse("{}"), "reject unrelated JSON without replacing data");
Throws(() => ProjectStore.Parse("""{"ListOfCharacterStates":[null]}"""), "reject null scenes safely");
Throws(() => ProjectStore.Parse("""{"ListOfCharacterStates":[{"State":"a","Choices":[{"Requirements":[null]}]}]}"""), "reject null condition rows safely");
Throws(() => ProjectStore.Parse("""{"FormatVersion":999,"ListOfCharacterStates":[]}"""), "reject newer project formats rather than silently editing them");
Throws(() => ProjectStore.Parse("""{"FormatVersion":0,"ListOfCharacterStates":[]}"""), "reject unsupported older project formats");
Check(imported.FormatVersion == DialogueProject.CurrentFormatVersion, "website files without a format version remain compatible");
var reservedImport = ProjectStore.Parse("""{"EntryStateId":"[EXIT]","ListOfCharacterStates":[{"State":"[EXIT]","Text":"Hello","DefaultNextId":"[EXIT]"}]}""");
Check(DialogueEngine.Validate(reservedImport).Any(d => d.Severity == "Error" && d.Message.Contains("reserved")), "imported reserved IDs are diagnosed while remaining repairable");
Throws(() => new Simulation(reservedImport), "Core refuses to simulate a reserved scene ID");
DialogueEngine.Rename(reservedImport, reservedImport.ListOfCharacterStates[0], "repaired");
Check(reservedImport.Entry == "repaired" && reservedImport.ListOfCharacterStates[0].DefaultNextId == "[EXIT]" && !DialogueEngine.Validate(reservedImport).Any(), "repairing a reserved ID preserves intentional endings");
var invalidKinds = ProjectStore.Parse("""{"ListOfCharacterStates":[{"State":"start","Type":"Unknown","Text":"Hello","Choices":[{"ButtonText":"Bye","Visibility":99,"NextStateId":"[EXIT]"}]}]}""");
Check(DialogueEngine.Validate(invalidKinds).Count(d => d.Severity == "Error") == 2, "unsupported scene types and numeric visibility values cannot pass validation");

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

var locStory = Templates.Blank();
var s0 = locStory.ListOfCharacterStates[0];
s0.Metadata["title"] = "Start, \"greeting\"";
s0.Text = "Hello {player_name}.\n[if aff:trust >= 1]Welcome friend![else]Who are you?[/if]\nEnjoy your stay, \"traveller\"!";
s0.Choices = [new() { ButtonText = "Thank you, friend!", NextStateId = "[EXIT]" }];
var locBefore = ProjectStore.Serialize(locStory);
var csv = LocalizationStore.ExportCsv(locStory);
Check(csv.StartsWith("Key,SceneId,ChoiceId,Speaker,Field,SourceText,TranslatedText\n"), "localization CSV export writes RFC 4180 header");
Check(csv.Contains("\"Start, \"\"greeting\"\"\"") && csv.Contains("Welcome friend!"), "CSV export quotes cells with commas and quotes and escapes internal quotes");

var cleanReport = LocalizationStore.ImportCsv(locStory, csv);
Check(cleanReport.UpdatedFields == 0 && ProjectStore.Serialize(locStory) == locBefore, "importing unedited CSV reports 0 updates and leaves project byte-identical");

var editedCsv = csv.Replace("Hello {player_name}.", "Bonjour {player_name}.")
                   .Replace("Welcome friend!", "Bienvenue l'ami!")
                   .Replace("Thank you, friend!", "Merci, l'ami!");
var translatedReport = LocalizationStore.ImportCsv(locStory, editedCsv);
Check(translatedReport.UpdatedFields == 2 && s0.Text.Contains("Bonjour {player_name}.\n[if aff:trust >= 1]Bienvenue l'ami!") && s0.Choices[0].ButtonText == "Merci, l'ami!", "valid CSV import updates dialogue and choice text across multiple lines and preserved tags");

var brokenCsv = editedCsv.Replace("[else]", "[if aff:trust >= 1]unclosed");
Throws(() => LocalizationStore.ImportCsv(locStory, brokenCsv), "CSV import rejects broken conditional tags with actionable exception");
Check(s0.Text.Contains("Bonjour {player_name}."), "rejected broken CSV import leaves existing project content completely intact");

Throws(() => LocalizationStore.ImportCsv(locStory, ""), "CSV import rejects empty content");
Throws(() => LocalizationStore.ImportCsv(locStory, "Invalid,Header\n1,2"), "CSV import rejects missing required header columns");

var searchStory = ProjectStore.Parse("""
{"ProjectName":"cat","EntryStateId":"cat","FutureRoot":"cat","ListOfCharacterStates":[
{"State":"cat","Text":"Cat cat catapult.\n[b]cat[/b] {player_name}","DefaultNextId":"cat2","Metadata":{"title":"Cat crossing","notes":"A cat note.","speaker":"cat","tags":"cat","future":"cat"},"FutureScene":"cat","ShopData":{"ShopId":"cat","FutureShop":"cat"},"DefaultAfterActions":[{"Type":"Event","TargetId":"cat","Parameter":"cat","FutureAction":"cat"}],"Choices":[{"ButtonText":"cat?","NextStateId":"cat2","FutureChoice":"cat","Requirements":[{"TargetId":"cat","Value":"1","FutureRequirement":"cat"}]}]},
{"State":"cat2","Text":"cat elsewhere.","DefaultNextId":"[EXIT]","Metadata":{"title":"Cat again"}}]}
""");
var searchBefore = ProjectStore.Serialize(searchStory);
var searchResults = ProjectSearch.Find(searchStory, new("cat"));
Check(searchResults.TotalMatches == 9 && searchResults.Matches.Select(m => m.Field).Distinct().Count() == 4, "project search finds dialogue, responses, titles and notes with occurrence counts");
Check(searchResults.Matches.All(m => m.OriginalText.Substring(m.Start, m.Length).Equals("cat", StringComparison.OrdinalIgnoreCase)), "search previews point to exact original text offsets across fields");
Check(ProjectSearch.Find(searchStory, new("cat", MatchCase: true)).TotalMatches == 6 && ProjectSearch.Find(searchStory, new("cat", WholeWord: true)).TotalMatches == 8, "case and whole-word options distinguish capital letters and word fragments");
Check(ProjectSearch.Find(searchStory, new("cat", SearchFields.Dialogue, SceneIndex: 1)).TotalMatches == 1, "field and scene scope constrain search together");
Check(ProjectSearch.Find(searchStory, new("cat", SearchFields.None)).TotalMatches == 0 && ProjectSearch.Find(searchStory, new("")).TotalMatches == 0, "empty queries and unchecked fields never match every position");
Check(ProjectStore.Serialize(searchStory) == searchBefore, "search and previews leave the complete project unchanged");
var literalStory = ProjectStore.Clone(searchStory);
Check(ProjectSearch.ReplaceAll(literalStory, new("[b]cat[/b]"), "$1\\literal") == 1 && literalStory.ListOfCharacterStates[0].Text.Contains("$1\\literal"), "find and replacement treat formatting brackets, dollars and backslashes literally");
var expectedSearch = ProjectStore.Clone(searchStory); expectedSearch.ListOfCharacterStates[0].Text = "Cat dog catapult.\n[b]dog[/b] {player_name}";
Check(ProjectSearch.ReplaceAll(searchStory, new("cat", SearchFields.Dialogue, MatchCase: true, WholeWord: true, SceneIndex: 0), "dog") == 2 && ProjectStore.Serialize(searchStory) == ProjectStore.Serialize(expectedSearch), "scoped replacement changes only matching occurrences and preserves every other JSON field");
searchStory = ProjectStore.Parse(searchBefore); var expectedAll = ProjectStore.Clone(searchStory);
expectedAll.ListOfCharacterStates[0].Metadata["title"] = "dog crossing"; expectedAll.ListOfCharacterStates[0].Metadata["notes"] = "A dog note.";
expectedAll.ListOfCharacterStates[0].Text = "dog dog catapult.\n[b]dog[/b] {player_name}"; expectedAll.ListOfCharacterStates[0].Choices[0].ButtonText = "dog?";
expectedAll.ListOfCharacterStates[1].Metadata["title"] = "dog again"; expectedAll.ListOfCharacterStates[1].Text = "dog elsewhere.";
Check(ProjectSearch.ReplaceAll(searchStory, new("cat", WholeWord: true), "dog") == 8 && ProjectStore.Serialize(searchStory) == ProjectStore.Serialize(expectedAll), "bulk replacement preserves IDs, links, variables, logic, speaker, tags and nested unknown data");
searchStory = ProjectStore.Parse(searchBefore); var responseMatch = ProjectSearch.Find(searchStory, new("cat", SearchFields.Responses)).Matches.Single();
ProjectSearch.ReplaceMatch(searchStory, responseMatch, "");
Check(searchStory.ListOfCharacterStates[0].Choices[0].ButtonText == "?" && searchStory.ListOfCharacterStates[0].Text == "Cat cat catapult.\n[b]cat[/b] {player_name}", "selected replacement can remove text from just one response");
var afterSingleReplace = ProjectStore.Serialize(searchStory);
Throws(() => ProjectSearch.ReplaceMatch(searchStory, responseMatch, "again"), "stale match previews cannot overwrite later field edits");
Check(ProjectStore.Serialize(searchStory) == afterSingleReplace, "a rejected stale replacement does not partially mutate the project");
var duplicateSearch = Templates.Blank(); duplicateSearch.ListOfCharacterStates[0].Text = "one";
duplicateSearch.ListOfCharacterStates.Add(new() { State = duplicateSearch.ListOfCharacterStates[0].State, Text = "one" });
ProjectSearch.ReplaceMatch(duplicateSearch, ProjectSearch.Find(duplicateSearch, new("one", SceneIndex: 1)).Matches.Single(), "two");
Check(duplicateSearch.ListOfCharacterStates[0].Text == "one" && duplicateSearch.ListOfCharacterStates[1].Text == "two", "match locations distinguish repairable scenes with duplicate IDs");
var fallbackTitle = Templates.Blank(); fallbackTitle.ListOfCharacterStates[0].Metadata.Remove("title"); var fallbackId = fallbackTitle.ListOfCharacterStates[0].State;
ProjectSearch.ReplaceAll(fallbackTitle, new(fallbackId, SearchFields.Titles), "Opening");
Check(fallbackTitle.ListOfCharacterStates[0].Title == "Opening" && fallbackTitle.Entry == fallbackId, "replacing a fallback scene title creates metadata without renaming graph identity");
var repeatedSearch = Templates.Blank(); repeatedSearch.ListOfCharacterStates[0].Text = string.Join(" ", Enumerable.Repeat("cat", 250));
var limitedSearch = ProjectSearch.Find(repeatedSearch, new("cat"));
Check(limitedSearch.TotalMatches == 250 && limitedSearch.Matches.Count == ProjectSearch.PreviewLimit, "preview limits bound UI results while keeping the complete match count");
Check(ProjectSearch.ReplaceAll(repeatedSearch, new("cat"), "catcat") == 250 && repeatedSearch.ListOfCharacterStates[0].Text.Split(' ').All(t => t == "catcat"), "Replace all includes results beyond the preview limit without searching inserted text again");
repeatedSearch.ListOfCharacterStates[0].Text = "aaa";
Check(ProjectSearch.ReplaceAll(repeatedSearch, new("aa"), "X") == 1 && repeatedSearch.ListOfCharacterStates[0].Text == "Xa", "overlapping literals use predictable nonoverlapping replacement");
repeatedSearch.ListOfCharacterStates[0].Text = "cat catapult cat_1 1cat cat-cat cát cat\u0301";
Check(ProjectSearch.Find(repeatedSearch, new("cat", WholeWord: true)).TotalMatches == 3, "whole-word boundaries include digits, underscores and combining accents");
var unchangedSearch = ProjectStore.Serialize(repeatedSearch);
Check(ProjectSearch.ReplaceAll(repeatedSearch, new("cat"), "cat") == 7 && ProjectStore.Serialize(repeatedSearch) == unchangedSearch, "identical replacements leave serialized data unchanged");

var navigationStory = Templates.Harbour(); navigationStory.ListOfCharacterStates[0].DefaultNextId = "keeper"; navigationStory.ListOfCharacterStates[0].Choices[0].NextStateId = null;
navigationStory.ListOfCharacterStates[0].Metadata["tags"] = "coast, Mystery, mystery, ,";
navigationStory.ListOfCharacterStates[1].Metadata["speaker"] = "Ada"; navigationStory.ListOfCharacterStates[1].Metadata["tags"] = "Mystery";
navigationStory.ListOfCharacterStates[2].Metadata["tags"] = "coast";
navigationStory.ListOfCharacterStates[3].Metadata["speaker"] = "Ada"; navigationStory.ListOfCharacterStates[3].Metadata["tags"] = "ending";
navigationStory.ListOfCharacterStates[4].Metadata["tags"] = "ending";
var navigationBefore = ProjectStore.Serialize(navigationStory);
var navigationIndex = new SceneNavigation(navigationStory, [new("keeper", "Error", "Example error"), new("gift", "Warning", "Example warning")]);
Check(navigationIndex.Tags.SequenceEqual(new[] { "coast", "ending", "Mystery" }) && navigationIndex.Speakers.SequenceEqual(new[] { "Ada", "Mara" }), "navigation discovers sorted speakers and trims/deduplicates comma-separated tags");
Check(navigationIndex.Filter(new(Speaker: "mara", Tag: "mystery")).Select(s => s.Id).SequenceEqual(new[] { "arrival" }), "outline filters combine exact speaker and tag matches without case sensitivity");
Check(navigationIndex.Filter(new("keeper", Speaker: "Ada", Tag: "Mystery", Issues: SceneIssueFilter.Errors)).Single().Id == "keeper", "text, speaker, tag and diagnostic filters intersect");
Check(navigationIndex.Filter(new(Issues: SceneIssueFilter.WithIssues)).Count == 2 && navigationIndex.Filter(new(Issues: SceneIssueFilter.Warnings)).Single().Id == "gift" && navigationIndex.Filter(new(Issues: SceneIssueFilter.WithoutIssues)).Count == 3, "diagnostic filters distinguish errors, warnings, any issues and clean scenes");
Check(navigationIndex.Filter(new("coast")).Count == 2 && navigationIndex.Filter(new("Establish the mystery")).Single().Id == "arrival", "outline text search includes tags and writer's notes");
Check(navigationIndex.Outgoing(0).Count == 4 && navigationIndex.Outgoing(0)[1].TargetId == "keeper" && navigationIndex.Outgoing(0)[1].TargetSceneIndex == 1, "outgoing choice links resolve the same continue fallback as the runtime");
Check(navigationIndex.Incoming(1).Select(s => s.Id).SequenceEqual(new[] { "arrival", "gift" }), "incoming navigation deduplicates source scenes with multiple links");
Check(navigationIndex.Resolve("[EXIT]") is null && navigationIndex.Resolve("missing") is null && navigationIndex.Resolve("KEEPER") is null, "navigation preserves case-sensitive identity and distinguishes endings and missing targets");
var destinationOptions = navigationIndex.Destinations(true, "missing");
Check(destinationOptions[0].Id is null && destinationOptions[0].Label.Contains("continue") && destinationOptions[1].Id == "[EXIT]" && destinationOptions[^1].Id == "missing", "destination options preserve fallback, ending and a current missing target");
Check(SceneNavigation.FindDestinations(destinationOptions, "SMALL KINDNESS").Matches.Single().Id == "gift" && SceneNavigation.FindDestinations(destinationOptions, "promise").Matches.Single().Id == "promise", "destination search matches both displayed titles and exact IDs case-insensitively");
Check(ProjectStore.Serialize(navigationStory) == navigationBefore, "indexing, filtering and connection navigation never modify story JSON");
var nullNavigation = ProjectStore.Parse("""{"ListOfCharacterStates":[{"State":"start","Text":"Hello","Metadata":{"title":null,"speaker":null,"notes":null,"tags":null}}]}"""); var nullNavigationBefore = ProjectStore.Serialize(nullNavigation);
Check(new SceneNavigation(nullNavigation, []).Filter(new(Speaker: "Character")).Single().Title == "start" && ProjectStore.Serialize(nullNavigation) == nullNavigationBefore, "null imported metadata remains navigable without rewriting legacy JSON");
foreach (var size in new[] { 100, 1000, 10000 })
{
    var largeNavigation = new DialogueProject { ListOfCharacterStates = Enumerable.Range(0, size).Select(i => Templates.Scene($"scene_{i:D5}", "Chapter " + i, i % 2 == 0 ? "Ada" : "Mara", "A line of dialogue.", 0, 0)).ToList() };
    var largeIndex = new SceneNavigation(largeNavigation, []);
    var largeMatches = SceneNavigation.FindDestinations(largeIndex.Destinations(false, null), "Chapter");
    var filteredNavigation = largeIndex.Filter(new($"scene_{size - 1:D5}", Speaker: "Mara"));
    Check(largeMatches.TotalCount == size && largeMatches.Matches.Count == Math.Min(size, SceneNavigation.DestinationLimit) && filteredNavigation.Single().Index == size - 1, "large navigation retains complete counts with bounded destination previews at " + size + " scenes");
}
Check(DialogueEngine.ResolveText("[IF aff:trust <= -1]low [if inv:coin > 0]coin[else]none[/if][else]high[/if] {player_name}", vars, p.TextVariables) == "low coin Traveller", "nested, case-insensitive inline conditions support negative values");
var textStory = Templates.Blank(); textStory.ListOfCharacterStates[0].Text = "Hello";
textStory.ListOfCharacterStates[0].Choices = [new() { NextStateId = "[EXIT]" }];
foreach (var malformed in new[] {
    "[if aff:trust >= 1]unfinished", "[else]orphan", "[/if]orphan", "[if aff:trust >= 0]one[else]two[else]three[/if]",
    "[if aff:trust >= 0]valid[else][if inv:coin >= 999999999999]hidden[/if][/if]",
    "[if aff:trust >= 0]valid[else][if invalid]hidden[/if][/if]", "[if aff:trust >= 0", "[if invalid [b]text[/b]" })
{
    textStory.ListOfCharacterStates[0].Choices[0].ButtonText = malformed;
    Check(DialogueEngine.Validate(textStory).Any(d => d.Severity == "Error" && d.Message.StartsWith("Choice 1 response:")), "choice diagnostics identify malformed conditional: " + malformed);
}
textStory.ListOfCharacterStates[0].Choices[0].ButtonText = "[IF aff:trust <= -1]low [if inv:coin > 0]coin[else]none[/if][else]high[/if] {player_name}";
Check(!DialogueEngine.Validate(textStory).Any(d => d.Severity == "Error"), "valid nested choice condition syntax remains exportable");
textStory.ListOfCharacterStates[0].Text = string.Concat(Enumerable.Repeat("[if aff:trust >= 0]", 65)) + "deep" + string.Concat(Enumerable.Repeat("[/if]", 65));
Check(DialogueEngine.Validate(textStory).Any(d => d.Severity == "Error" && d.Message.StartsWith("Dialogue:")), "conditions beyond the runtime nesting limit are diagnosed");
textStory.ListOfCharacterStates[0].Text = "[b]unclosed {missing_name}";
Check(DialogueEngine.Validate(textStory).Count(d => d.Severity == "Warning") == 2, "unclosed formatting and unknown substitutions are advisory warnings");
textStory.ListOfCharacterStates[0].Text = "[b][i]Hello[/i][/b] [colour=#79DFC8]world[/colour] {inv:coin}";
Check(!DialogueEngine.Validate(textStory).Any(), "valid formatting and discovered numeric substitutions have no warnings");
var locked = new Choice { Requirements = [new() { TargetId = "trust", Value = "2" }] };
Check(!DialogueEngine.RequirementsMet(locked, vars, out _), "locked choices fail their numeric conditions");
locked.Requirements[0].Value = "invalid";
Check(!DialogueEngine.RequirementsMet(locked, vars, out _), "invalid thresholds fail closed");
locked.Requirements[0].Value = "0"; locked.Requirements[0].TargetId = "";
Check(!DialogueEngine.RequirementsMet(locked, vars, out _), "blank variable IDs fail closed");

var sim = new Simulation(p); sim.NextPage(); sim.Choose(sim.Current!.Choices[1]);
Check(sim.Current!.State == "gift" && sim.Variables["inv:coin"] == 0 && sim.Variables["aff:trust"] == 1, "choice changes inventory and affinity exactly once");
sim.Back();
Check(sim.Current!.State == "arrival" && sim.PageIndex == 1 && sim.Variables["inv:coin"] == 1 && sim.Variables["aff:trust"] == 0, "Back restores page, scene, variables and transcript");
sim.Choose(sim.Current.Choices[0]); sim.NextPage(); sim.Choose(sim.Current!.Choices[0]); sim.NextPage(); sim.Continue();
Check(sim.Current is null && sim.Variables["aff:trust"] == 1, "playtest reaches an explicit ending");
sim.Back(); Check(sim.Current!.State == "promise", "Back can revisit an ending");
var previewStory = Templates.Blank(); previewStory.InitialVariables["aff:trust"] = 0;
previewStory.ListOfCharacterStates[0].Text = "[if aff:trust >= 1]Friend[else]Stranger[/if]";
var preview = new Simulation(previewStory); var originalTranscript = preview.Transcript.ToArray();
preview.SetVariable("aff:trust", 1);
Check(preview.Pages[0] == "Friend" && preview.Transcript[^1] == "Character: Friend" && preview.Transcript.Any(t => t.Contains("Test variable aff:trust: 0 → 1")), "live edits record the variable change and refreshed display in the transcript");
preview.Back();
Check(preview.Variables["aff:trust"] == 0 && preview.Pages[0] == "Stranger" && preview.Transcript.SequenceEqual(originalTranscript), "Back restores a live variable edit and its transcript together");
preview.SetVariable("aff:trust", 0);
Check(!preview.CanBack && preview.Transcript.SequenceEqual(originalTranscript), "unchanged live values do not create history or transcript noise");
var randomStory = Templates.Blank(); randomStory.InitialVariables["aff:trust"] = 0;
randomStory.ListOfCharacterStates[0].SayItAll = false;
randomStory.ListOfCharacterStates[0].Text = string.Join('\n', Enumerable.Range(0, 20).Select(i => $"Line {i}: [if aff:trust >= 1]Friend[else]Stranger[/if]"));
var randomPreview = new Simulation(randomStory); var originalRandomPage = randomPreview.Pages[0]; var originalRandomPrefix = originalRandomPage.Split(':')[0];
var randomLineStable = true;
for (var i = 0; i < 20; i++) { randomPreview.SetVariable("aff:trust", i % 2 == 0 ? 1 : 0); randomLineStable &= randomPreview.Pages[0].Split(':')[0] == originalRandomPrefix; }
Check(randomLineStable, "live refresh retains the selected random line throughout a scene visit");
randomPreview.RerollLine(); randomPreview.Back(); randomPreview.RefreshText();
Check(randomPreview.Pages[0] == originalRandomPage, "Back restores the random line selection after an explicit reroll");
var broken = Templates.Blank(); broken.ListOfCharacterStates[0].DefaultNextId = "missing"; broken.ListOfCharacterStates[0].DefaultAfterActions = [new() { TargetId = "trust", Amount = 10 }];
var failed = new Simulation(broken); Throws(failed.Continue, "missing destination stops transition");
Check(failed.Current!.State == "start" && failed.Variables["aff:trust"] == 0 && !failed.CanBack, "failed transitions do not mutate actions or history");
var actionStory = Templates.Blank(); actionStory.ListOfCharacterStates[0].DefaultAfterActions = [new() { Type = "Inventory", TargetId = "coin", Amount = -5 }, new() { Type = "Event", TargetId = "door", Parameter = "open" }];
var events = new Simulation(actionStory); events.Continue();
Check(events.Variables["inv:coin"] == 0 && events.Events.Count == 1, "inventory clamps to zero and events are logged"); events.Back(); Check(events.Events.Count == 0, "Back restores event log");

var renamed = Templates.Harbour(); DialogueEngine.Rename(renamed, renamed.ListOfCharacterStates[0], "opening");
Check(renamed.Entry == "opening", "renaming start scene updates entry"); DialogueEngine.Rename(renamed, renamed.ListOfCharacterStates[1], "keeper_new");
Check(renamed.ListOfCharacterStates[0].Choices[0].NextStateId == "keeper_new" && renamed.ListOfCharacterStates[2].DefaultNextId == "keeper_new", "rename updates choice and continue connections");
Throws(() => DialogueEngine.Rename(renamed, renamed.ListOfCharacterStates[0], "[EXIT]"), "reserved exit cannot become a scene ID");
var hist = new ProjectHistory(); hist.Reset(p); p.ProjectName = "Edited"; hist.Commit(p);
Check(hist.Undo()!.ProjectName == "The last lighthouse" && hist.Redo()!.ProjectName == "Edited", "history preserves complete project snapshots");
hist.Undo(); p.ProjectName = "Branch"; hist.Commit(p); Check(!hist.CanRedo, "editing after undo clears redo branch");
var exported = ProjectStore.WebExport(renamed); using var web = JsonDocument.Parse(exported);
Check(web.RootElement.EnumerateObject().Count() == 1 && web.RootElement.GetProperty("ListOfCharacterStates")[0].GetProperty("State").GetString() == "opening", "web export retains original root and starts at selected entry");
Check(web.RootElement.GetProperty("ListOfCharacterStates")[0].GetProperty("Choices")[0].GetProperty("Visibility").ValueKind == JsonValueKind.Number, "web export uses original numeric enum format");
var cyclic = Templates.Blank(); cyclic.ListOfCharacterStates[0].Choices = [new() { NextStateId = "start" }, new() { NextStateId = "[EXIT]" }];
Check(!DialogueEngine.Validate(cyclic).Any(d => d.Severity == "Error"), "intentional loops with an exit are playable");
cyclic.ListOfCharacterStates[0].DefaultNextId = "missing";
Check(DialogueEngine.Validate(cyclic).Any(d => d.Severity == "Error"), "validate stale fallback even when scene has choices");
var duplicate = Templates.Blank(); duplicate.ListOfCharacterStates.Add(Templates.Scene("start", "Duplicate", "A", "Hi", 0, 0));
Check(DialogueEngine.Validate(duplicate).Any(d => d.Message.Contains("Duplicate")), "duplicate scene IDs diagnosed");
var unreachable = Templates.Blank(); unreachable.ListOfCharacterStates.Add(Templates.Scene("unused", "Unused", "A", "Hi", 0, 0));
Check(DialogueEngine.Validate(unreachable).Any(d => d.Message.Contains("unreachable")), "unreachable scenes diagnosed");
DialogueEngine.AutoLayout(renamed); Check(renamed.ListOfCharacterStates.Select(s => (s.VisualX,s.VisualY)).Distinct().Count() == 5, "layout assigns distinct positions");
var tallStory = Templates.Blank(); tallStory.ListOfCharacterStates[0].Choices = [new() { NextStateId = "tall" }, new() { NextStateId = "short" }];
var tall = Templates.Scene("tall", "Tall", "A", "Hi", 0, 0); tall.Choices = Enumerable.Range(0, 10).Select(_ => new Choice { NextStateId = "[EXIT]" }).ToList();
var shortScene = Templates.Scene("short", "Short", "A", "Hi", 0, 0); tallStory.ListOfCharacterStates.AddRange([tall, shortScene]);
DialogueEngine.AutoLayout(tallStory);
Check(tall.VisualX == shortScene.VisualX && shortScene.VisualY >= tall.VisualY + GraphGeometry.NodeHeight(tall) + 48, "Arrange leaves a gap below choice-heavy scenes on the same layer");
var chain = new DialogueProject { EntryStateId = "s0" };
for (var i = 0; i < 10000; i++) chain.ListOfCharacterStates.Add(new DialogueState { State = "s" + i, Text = "Hi", DefaultNextId = i == 9999 ? "[EXIT]" : "s" + (i + 1) });
Check(!DialogueEngine.Validate(chain).Any(), "ending reachability handles a 10,000-scene chain");
chain.ListOfCharacterStates[^1].DefaultNextId = "s0";
Check(DialogueEngine.Validate(chain).Count(d => d.Message.Contains("No path")) == 10000, "ending reachability diagnoses a large closed cycle");
var temp = Path.Combine(AppContext.BaseDirectory, "atomic-test.json"); ProjectStore.AtomicWrite(temp, ProjectStore.Serialize(p)); ProjectStore.AtomicWrite(temp, ProjectStore.Serialize(renamed));
Check(ProjectStore.Load(temp).Entry == "opening" && !Directory.EnumerateFiles(AppContext.BaseDirectory, "atomic-test.json.*.tmp").Any(), "atomic saves replace completely and clean up temporary files"); File.Delete(temp);
var recoveryFolder = Path.Combine(AppContext.BaseDirectory, "recovery-test-" + Guid.NewGuid().ToString("N")); var recovery = new RecoveryStore(recoveryFolder); var recoveryId = Guid.NewGuid().ToString("N");
recovery.Save(recoveryId, new RecoveryRecord(null, DateTimeOffset.UtcNow, Templates.Harbour()));
Check(recovery.FindUnsaved().Count == 1 && recovery.FindUnsaved()[0].Record.Project.Entry == "arrival", "unsaved recovery can be loaded and validated");
recovery.Remove(recoveryId); Check(recovery.FindUnsaved().Count == 0, "completed or discarded recovery is removed and not offered again");
Throws(() => recovery.Save("../escape", new RecoveryRecord(null, DateTimeOffset.UtcNow, Templates.Blank())), "recovery paths reject traversal");
var badRecoveryId = Guid.NewGuid().ToString("N"); File.WriteAllText(Path.Combine(recoveryFolder, badRecoveryId + ".json"), "invalid");
Check(recovery.FindUnsaved().Count == 0, "invalid recovery records are ignored without destroying them"); File.Delete(Path.Combine(recoveryFolder, badRecoveryId + ".json"));
var semanticRecoveryId = Guid.NewGuid().ToString("N"); var semanticRecoveryPath = Path.Combine(recoveryFolder, semanticRecoveryId + ".json");
var semanticDamage = Templates.Blank(); semanticDamage.ListOfCharacterStates[0].State = "";
recovery.Save(semanticRecoveryId, new RecoveryRecord(null, DateTimeOffset.UtcNow, semanticDamage));
recovery.Save(recoveryId, new RecoveryRecord(null, DateTimeOffset.UtcNow, Templates.Harbour()));
Check(recovery.FindUnsaved().Count == 1 && File.Exists(semanticRecoveryPath), "semantically invalid recovery is retained and skipped without hiding a valid recovery");
recovery.Remove(semanticRecoveryId); recovery.Remove(recoveryId);
ProjectStore.AtomicWrite(temp, ProjectStore.Serialize(Templates.Blank())); var recoveryTime = DateTimeOffset.UtcNow.AddMinutes(1);
var recoverableStory = Templates.Harbour(); recovery.Save(recoveryId, new RecoveryRecord(temp, recoveryTime, recoverableStory));
Check(recovery.FindUnsaved().Count == 1, "recovery newer than a project file is offered"); File.SetLastWriteTimeUtc(temp, recoveryTime.UtcDateTime.AddSeconds(1));
Check(recovery.FindUnsaved().Count == 1, "a newer external save cannot hide different recovery content");
ProjectStore.AtomicWrite(temp, ProjectStore.Serialize(recoverableStory));
Check(recovery.FindUnsaved().Count == 0, "recovery matching saved content is ignored regardless of timestamps"); recovery.Remove(recoveryId); File.Delete(temp);

var authoringStory = Templates.Harbour();
var authoringSource = authoringStory.ListOfCharacterStates[0]; var authoringTarget = authoringStory.ListOfCharacterStates[1];
authoringSource.DefaultNextId = authoringSource.State; authoringTarget.DefaultNextId = authoringSource.State;
authoringSource.Choices.Add(new Choice { ButtonText = "Unspecified", NextStateId = "" });
authoringSource.Choices.Add(new Choice { ButtonText = "Missing", NextStateId = "missing_future" });
authoringSource.Extra = new() { ["FutureScene"] = JsonSerializer.SerializeToElement(new { keep = 42 }) };
authoringSource.Choices[1].Extra = new() { ["FutureChoice"] = JsonSerializer.SerializeToElement("kept") };
authoringSource.Choices[1].Requirements[0].Extra = new() { ["FutureRequirement"] = JsonSerializer.SerializeToElement(42) };
authoringSource.Choices[1].AfterChoiceActions[0].Extra = new() { ["FutureAction"] = JsonSerializer.SerializeToElement(42) };
authoringSource.ShopData.Extra = new() { ["FutureShop"] = JsonSerializer.SerializeToElement(42) };
var authoringBefore = ProjectStore.Serialize(authoringStory);
var bundle = AuthoringOperations.CopyScenes([authoringSource, authoringTarget]);
Check(bundle.Count == 2 && ProjectStore.Serialize(authoringStory) == authoringBefore, "copying a scene set captures content without changing the project");
authoringSource.Text = "Edited after copying";
var pasted = AuthoringOperations.PasteScenes(authoringStory, bundle, 1000, 1000);
Check(pasted.Count == 2 && pasted.All(s => s.State != authoringSource.State && s.State != authoringTarget.State) && authoringStory.ListOfCharacterStates.Select(s => s.State).Distinct().Count() == 7, "pasted scenes receive unique IDs without changing existing scene identity");
Check(pasted[0].DefaultNextId == pasted[0].State && pasted[0].Choices[0].NextStateId == pasted[1].State && pasted[1].DefaultNextId == pasted[0].State, "copying cycles remaps self, choice and continue links within the pasted set");
Check(pasted[0].Choices[1].NextStateId == "gift" && pasted[0].Choices[^2].NextStateId == "" && pasted[0].Choices[^1].NextStateId == "missing_future" && pasted[1].Choices[^1].NextStateId == "departure", "paste preserves external, missing and blank fallback destinations");
Check(pasted[0].Text != "Edited after copying" && pasted[0].Metadata["notes"] == authoringSource.Metadata["notes"] && pasted[0].Speaker == authoringSource.Speaker && pasted[0].Choices[1].Visibility == authoringSource.Choices[1].Visibility, "scene copies are snapshots and preserve metadata, speakers, delivery and visibility");
Check(pasted[0].Extra!["FutureScene"].GetProperty("keep").GetInt32() == 42 && pasted[0].Choices[1].Extra!.ContainsKey("FutureChoice") && pasted[0].Choices[1].Requirements[0].Extra!.ContainsKey("FutureRequirement") && pasted[0].Choices[1].AfterChoiceActions[0].Extra!.ContainsKey("FutureAction") && pasted[0].ShopData.Extra!.ContainsKey("FutureShop"), "bulk paste preserves unknown fields on scenes, choices, conditions, actions and shops");
Check(pasted[0].Choices[0].InternalId != authoringSource.Choices[0].InternalId && pasted[1].VisualX - pasted[0].VisualX == authoringTarget.VisualX - authoringSource.VisualX && pasted[1].VisualY - pasted[0].VisualY == authoringTarget.VisualY - authoringSource.VisualY, "paste renews choice identity and preserves relative graph positions");
pasted[0].Choices[1].Requirements[0].Value = "9"; pasted[0].Metadata["notes"] = "Changed copy";
var pastedAgain = AuthoringOperations.PasteScenes(authoringStory, bundle, 1500, 1500);
Check(pastedAgain[0].Choices[1].Requirements[0].Value == "1" && pastedAgain[0].Metadata["notes"] != "Changed copy" && authoringSource.Choices[1].Requirements[0].Value == "1", "pastes never share mutable logic or metadata with source or previous copies");
var otherAuthoringStory = Templates.Blank(); var otherPasted = AuthoringOperations.PasteScenes(otherAuthoringStory, bundle, 100, 100);
Check(otherPasted[0].Choices[0].NextStateId == otherPasted[1].State && otherPasted[0].Choices[1].NextStateId == "gift" && otherAuthoringStory.Entry == "start", "cross-project paste remaps internal links and retains visible external targets without replacing the entry");
Throws(() => AuthoringOperations.CopyScenes([]), "copy rejects an empty selection");
Throws(() => AuthoringOperations.CopyScenes([authoringSource, authoringSource]), "copy rejects duplicate source IDs before ambiguous remapping");
Throws(() => AuthoringOperations.CopyScenes([new DialogueState { State = "[EXIT]" }]), "copy requires reserved IDs to be repaired");
var fullAuthoringStory = new DialogueProject { ListOfCharacterStates = Enumerable.Range(0, 10000).Select(i => new DialogueState { State = "s" + i }).ToList() };
var fullBefore = ProjectStore.Serialize(fullAuthoringStory);
Throws(() => AuthoringOperations.PasteScenes(fullAuthoringStory, bundle, 0, 0), "paste enforces the import scene limit before mutation");
Check(ProjectStore.Serialize(fullAuthoringStory) == fullBefore, "capacity rejection leaves the whole project unchanged");
Throws(() => AuthoringOperations.CreateConnectedScene(otherAuthoringStory, otherAuthoringStory.ListOfCharacterStates[0], otherPasted[0].Choices[0], "Wrong owner"), "connected-scene creation rejects a choice from another scene");
var connected = AuthoringOperations.CreateConnectedScene(otherAuthoringStory, otherPasted[0], otherPasted[0].Choices[0], "  New branch  ");
Check(connected.Title == "New branch" && connected.Speaker == otherPasted[0].Speaker && connected.DefaultNextId == "[EXIT]" && otherPasted[0].Choices[0].NextStateId == connected.State, "connected-scene creation adds a blank ending and changes only the chosen destination");
AuthoringOperations.SaveSceneTemplate(authoringStory, pastedAgain[0], "Conversation");
var savedSceneTemplate = authoringStory.SceneTemplates.Single();
Check(savedSceneTemplate.Scene.DefaultNextId == savedSceneTemplate.Scene.State && savedSceneTemplate.Scene.Choices[0].NextStateId is null && savedSceneTemplate.Scene.Choices[^1].NextStateId is null, "scene templates retain self-links and detach external and missing destinations");
AuthoringOperations.SaveChoiceTemplate(authoringStory, authoringSource.Choices[1], "Offer item");
var savedChoiceTemplate = authoringStory.ChoiceTemplates.Single();
Check(savedChoiceTemplate.Choice.NextStateId is null && savedChoiceTemplate.Choice.Requirements.Count == 1 && savedChoiceTemplate.Choice.AfterChoiceActions.Count == 2, "choice templates detach destinations while preserving conditions and actions");
var templateBefore = ProjectStore.Serialize(authoringStory);
Throws(() => AuthoringOperations.SaveSceneTemplate(authoringStory, authoringSource, " conversation "), "template names reject case-insensitive duplicates");
Throws(() => AuthoringOperations.SaveChoiceTemplate(authoringStory, authoringSource.Choices[0], " "), "template names cannot be blank");
Check(ProjectStore.Serialize(authoringStory) == templateBefore, "failed template saves do not mutate project or templates");
var templateScene = AuthoringOperations.InsertSceneTemplate(authoringStory, savedSceneTemplate, 2000, 2000);
Check(templateScene.DefaultNextId == templateScene.State && templateScene.State != savedSceneTemplate.Scene.State, "using a scene template renews identity and remaps its self-connection");
var templateChoice = AuthoringOperations.InsertChoiceTemplate(templateScene, savedChoiceTemplate);
templateChoice.Requirements[0].Value = "12";
Check(templateChoice.NextStateId is null && templateChoice.InternalId != savedChoiceTemplate.Choice.InternalId && savedChoiceTemplate.Choice.Requirements[0].Value == "1", "using a choice template creates independent logic and fresh identity with a fallback destination");
savedSceneTemplate.Extra = new() { ["FutureTemplate"] = JsonSerializer.SerializeToElement(42) };
var templateRoundtrip = ProjectStore.Parse(ProjectStore.Serialize(authoringStory));
Check(templateRoundtrip.SceneTemplates[0].Extra!.ContainsKey("FutureTemplate") && templateRoundtrip.SceneTemplates[0].Scene.Extra!.ContainsKey("FutureScene") && templateRoundtrip.ChoiceTemplates[0].Choice.Extra!.ContainsKey("FutureChoice"), "desktop saves retain template definitions and unknown fields throughout them");
using (var webTemplates = JsonDocument.Parse(ProjectStore.WebExport(authoringStory)))
    Check(!webTemplates.RootElement.TryGetProperty("SceneTemplates", out _) && !webTemplates.RootElement.TryGetProperty("ChoiceTemplates", out _) && webTemplates.RootElement.GetProperty("ListOfCharacterStates")[0].GetProperty("Choices")[1].GetProperty("Visibility").ValueKind == JsonValueKind.Number, "website exports omit desktop templates and retain numeric visibility");
Check(ProjectStore.Parse("""{"ListOfCharacterStates":[],"SceneTemplates":null,"ChoiceTemplates":null}""").SceneTemplates.Count == 0, "null template collections normalize for compatible imports");
Throws(() => ProjectStore.Parse("""{"ListOfCharacterStates":[],"SceneTemplates":[{"Name":"Broken","Scene":null}]}"""), "damaged scene templates are rejected before document replacement");
Throws(() => ProjectStore.Parse("""{"ListOfCharacterStates":[],"ChoiceTemplates":[{"Name":"Broken","Choice":{"Requirements":[null]}}]}"""), "damaged template logic is rejected before document replacement");
var templateVariables = Templates.Blank(); templateVariables.InitialVariables["aff:trust"] = 1; templateVariables.ListOfCharacterStates[0].Text = "[if trust >= 1]Friend[else]Stranger[/if]";
var variableTemplateScene = Templates.Scene("template", "Template", "Character", "{player_name} [if trust >= 1]Friend[/if]", 0, 0);
variableTemplateScene.DefaultAfterActions.Add(new DialogueAction { Type = "Inventory", TargetId = "trust", Amount = 1 });
AuthoringOperations.SaveSceneTemplate(templateVariables, variableTemplateScene, "Inventory greeting");
AuthoringOperations.SaveChoiceTemplate(templateVariables, new Choice { ButtonText = "{player_name}", Requirements = [new Requirement { TargetId = "trust" }] }, "Trust response");
Check(new Simulation(templateVariables).Pages[0] == "Friend" && !DialogueEngine.DiscoverVariables(templateVariables).Contains("inv:trust"), "inactive template variables do not change live bare-condition bindings or runtime discovery");
Check(VariableStore.References(templateVariables, "aff:trust").Count(r => r.IsTemplate) == 1 && VariableStore.References(templateVariables, "inv:trust").Count(r => r.IsTemplate) == 2, "template reference details distinguish active and template-local alias bindings");
Throws(() => VariableStore.Delete(templateVariables, "player_name", true), "text variables referenced only by templates remain protected from deletion");
VariableStore.Rename(templateVariables, "aff:trust", "aff:confidence"); VariableStore.Rename(templateVariables, "player_name", "hero", true);
Check(templateVariables.SceneTemplates[0].Scene.Text.StartsWith("{hero} [if trust") && templateVariables.ChoiceTemplates[0].Choice.ButtonText == "{hero}" && templateVariables.ChoiceTemplates[0].Choice.Requirements[0].TargetId == "confidence" && new Simulation(templateVariables).Pages[0] == "Friend", "variable renames update saved templates while preserving their distinct aliases and live behavior");
VariableStore.Rename(templateVariables, "inv:trust", "inv:tokens");
Check(templateVariables.SceneTemplates[0].Scene.Text.Contains("[if inv:tokens") && templateVariables.SceneTemplates[0].Scene.DefaultAfterActions[0].TargetId == "tokens", "renaming a template-only numeric ID rewrites its typed action and inline alias");
Throws(() => VariableStore.Delete(templateVariables, "inv:tokens"), "numeric variables used by saved templates cannot be deleted");

AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
var main = new MainWindow(null, testMode: true); main.Show(); Dispatcher.UIThread.RunJobs();
var output = args.FirstOrDefault() ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../artifacts")); Directory.CreateDirectory(output);
GraphOrganizationChecks.Core(Check, Throws, output);
ScenarioChecks.Core(Check, Throws, output);
ExportChecks.Core(Check, Throws, output);
ConflictChecks.Core(Check, Throws, output);
RuntimeChecks.Core(Check, Throws);
ProjectStore.AtomicWrite(Path.Combine(output,"lighthouse.dialogue.json"), ProjectStore.Serialize(Templates.Harbour()));
void Capture(Window window, string name) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame() ?? throw new Exception("No rendered frame"); frame.Save(Path.Combine(output,name + ".png"), PngBitmapEncoderOptions.Default); }
GraphOrganizationChecks.UI(Check, output, Capture);
ExportChecks.UI(Check, output, Capture);
ConflictChecks.UI(Check, output, Capture);
EditorPerformanceRegressions.Run(Check, output, Capture);
ScenarioChecks.UI(Check, output, Capture);
T Find<T>(string name) where T : Control => main.FindControl<T>(name)!;
void Click(string name) { Find<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
Check(!main.HasProject && Find<WelcomeView>("Welcome").IsVisible, "startup shows welcome without automatically loading a sample"); Capture(main,"welcome-dark");
main.ChangeAppearance("Light"); Capture(main,"welcome-light"); main.ChangeAppearance("High contrast"); Capture(main,"welcome-high-contrast"); main.ChangeAppearance("Dark");
main.Width = 1000; main.Height = 680; Capture(main,"welcome-compact"); main.Width = 1540; main.Height = 940;
main.LoadExampleAsync().GetAwaiter().GetResult(); Dispatcher.UIThread.RunJobs();
Check(main.HasProject && !main.HasUnsavedChanges && main.CurrentFilePath is null, "optional example opens as a clean copy without a file location");
Check(main.ClientSize.Width > 1000 && main.Project.ListOfCharacterStates.Count == 5, "desktop window lays out the example story"); Capture(main,"workspace-dark");
Find<TextBox>("SearchBox").Text = "keeper"; Dispatcher.UIThread.RunJobs();
Check(Find<ListBox>("SceneList").Items.Count == 1, "outline searches both title and dialogue"); Find<TextBox>("SearchBox").Text = "";
Click("NotesTab"); Check(Find<StackPanel>("EditorPanel").GetVisualDescendants().OfType<TextBox>().Any(), "notes editor is reachable through native buttons");
Click("ContentTab");
Check(!Find<TextBlock>("ProjectTitle").Text!.EndsWith(" •"), "opening editor tabs does not create phantom edits");
var editor = Find<StackPanel>("EditorPanel");
var addChoice = editor.GetVisualDescendants().OfType<Button>().First(b => b.Content is string s && s.Contains("Add a player choice"));
addChoice.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Check(main.Project.ListOfCharacterStates[0].Choices.Count == 4, "choice creation changes the document"); Click("UndoButton"); Check(main.Project.ListOfCharacterStates[0].Choices.Count == 3, "undo restores choice creation"); Click("RedoButton"); Check(main.Project.ListOfCharacterStates[0].Choices.Count == 4, "redo restores choice creation"); Click("UndoButton");
var titleBox = Find<StackPanel>("EditorPanel").GetVisualDescendants().OfType<TextBox>().First(); titleBox.Focus(); titleBox.Text = "Edited opening";
main.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z"); main.KeyRelease(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z");
Check(main.Project.ListOfCharacterStates[0].Title == "A light in the fog", "global undo commits current typing and restores it");
Check(!Find<TextBlock>("ProjectTitle").Text!.EndsWith(" •"), "undo to saved state clears dirty indicator");
var graph = Find<GraphCanvas>("Graph"); var scene = main.Project.ListOfCharacterStates[0]; var initialX = scene.VisualX;
var from = graph.TranslatePoint(graph.WorldToViewport(new Point(scene.VisualX + 30, scene.VisualY + 40)), main)!.Value;
main.MouseDown(from, MouseButton.Left); main.MouseMove(new Point(from.X + 60, from.Y + 40)); main.MouseUp(new Point(from.X + 60, from.Y + 40), MouseButton.Left);
Check(main.Project.ListOfCharacterStates[0].VisualX > initialX, "mouse dragging moves graph scenes"); Click("UndoButton"); Check(main.Project.ListOfCharacterStates[0].VisualX == initialX, "node movement is undoable");
scene = main.Project.ListOfCharacterStates[0]; var target = main.Project.ListOfCharacterStates[4];
from = graph.TranslatePoint(graph.WorldToViewport(new Point(scene.VisualX + 270, scene.VisualY + 135)), main)!.Value;
var to = graph.TranslatePoint(graph.WorldToViewport(new Point(target.VisualX + 30, target.VisualY + 40)), main)!.Value;
main.MouseDown(from, MouseButton.Left); main.MouseMove(to); main.MouseUp(to, MouseButton.Left);
Check(main.Project.ListOfCharacterStates[0].Choices[0].NextStateId == target.State, "dragging an output port creates a choice connection"); Click("UndoButton"); Check(main.Project.ListOfCharacterStates[0].Choices[0].NextStateId == "keeper", "graph connection changes are undoable");
Click("LogicTab"); Capture(main,"workspace-logic"); Click("ContentTab"); Click("ScriptButton");
var scriptList = main.FindControl<ListBox>("ScriptList")!;
Check(scriptList.ItemsSource is not null && scriptList.Items.Count == main.Project.ListOfCharacterStates.Count, "script view populates virtualized list with project scenes");
Check(scriptList.ItemTemplate!.Build(null) is null, "script template returns null safely on null data during container recycling");
var secondScene = main.Project.ListOfCharacterStates[1];
var secondButton = scriptList.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Content, secondScene.Title));
secondButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
Check(main.FindControl<GraphCanvas>("Graph")!.Selected == secondScene, "clicking a scene card button in the script view selects that scene in the editor");
Capture(main,"workspace-script"); Click("GraphButton");
Check(scriptList.ItemsSource is null, "switching back to graph view releases script list items");
main.ChangeAppearance("Light"); Capture(main,"workspace-light"); main.ChangeAppearance("High contrast"); Capture(main,"workspace-high-contrast"); main.ChangeAppearance("Dark");
main.Width = 1000; main.Height = 680; Capture(main,"workspace-compact");
Check(Find<StackPanel>("EditorPanel").Bounds.Width > 260 && Find<GraphCanvas>("Graph").Bounds.Width > 250, "minimum window size keeps editor and canvas usable");
main.Width = 1540; main.Height = 940; main.ChangeAppearance("Dark", 1.15); Capture(main,"workspace-large-text"); main.ChangeAppearance("Dark");
main.Width = 1540; main.Height = 940; main.FindControl<GraphCanvas>("Graph")!.Fit();
var iconWindow = new Window { Width = 256, Height = 256, Content = new Border { Background = Avalonia.Media.Brushes.Transparent, Child = new Border { Margin = new Thickness(8), CornerRadius = new CornerRadius(64), Background = UiBrush("Accent"), Child = new TextBlock { Text = "D", FontFamily = new Avalonia.Media.FontFamily("Inter"), FontSize = 170, FontWeight = Avalonia.Media.FontWeight.Bold, Foreground = Avalonia.Media.Brushes.White, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center } } } };
Avalonia.Media.IBrush UiBrush(string key) => (Avalonia.Media.IBrush)Application.Current!.Resources[key]!;
iconWindow.Show(); Capture(iconWindow,"app-icon"); iconWindow.Close();
var controls = main.GetVisualDescendants().Where(c => c.TemplatedParent is null && c is TextBox or ComboBox or NumericUpDown).ToList();
Check(controls.All(c => !string.IsNullOrEmpty(AutomationProperties.GetName(c))), "editor inputs have screen-reader labels");
var playtest = new PlaytestWindow(Templates.Harbour(), null); playtest.Show(); Dispatcher.UIThread.RunJobs(); Capture(playtest,"playtest");
Check(playtest.Session.Current!.State == "arrival", "playtest window starts at entry");
playtest.GetVisualDescendants().OfType<Button>().First(b => b.Content is string s && s == "Continue →").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Check(playtest.Session.PageIndex == 1, "playtest Continue reveals choices after the final page");
var response = playtest.GetVisualDescendants().OfType<Button>().First(b => b.Content is TextBlock t && t.Text!.Contains("I brought")); response.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Check(playtest.Session.Current!.State == "gift" && playtest.Session.Variables["inv:coin"] == 0, "playtest response button applies actions and follows its destination"); Capture(playtest,"playtest-choice");
playtest.Close(); main.Close();
var livePlaytest = new PlaytestWindow(previewStory, null); livePlaytest.Show(); Dispatcher.UIThread.RunJobs();
var trustInput = livePlaytest.GetVisualDescendants().OfType<NumericUpDown>().Single(t => AutomationProperties.GetName(t) == "aff:trust");
trustInput.Value = 1; Dispatcher.UIThread.RunJobs();
var liveBack = livePlaytest.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "← Back"));
Check(livePlaytest.Session.Pages[0] == "Friend" && livePlaytest.Session.Transcript[^1] == "Character: Friend" && liveBack.IsEnabled, "actual playtest variable input updates text, transcript and Back");
Capture(livePlaytest, "playtest-live-edit"); liveBack.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
Check(livePlaytest.Session.Pages[0] == "Stranger" && livePlaytest.GetVisualDescendants().OfType<NumericUpDown>().Single(t => AutomationProperties.GetName(t) == "aff:trust").Value == 0, "actual playtest Back restores the displayed variable input");
livePlaytest.Close();
var randomPlaytest = new PlaytestWindow(randomStory, null); randomPlaytest.Show(); Dispatcher.UIThread.RunJobs();
randomPlaytest.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Reroll line")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Check(randomPlaytest.Session.CanBack && randomPlaytest.Session.Transcript.Any(t => t.Contains("Random line rerolled")), "random delivery offers an explicit undoable reroll button");
Capture(randomPlaytest, "playtest-random-line"); randomPlaytest.Close();
var arrangedGraph = new GraphCanvas { Project = tallStory }; var arrangedWindow = new Window { Width = 1200, Height = 900, Content = arrangedGraph };
arrangedWindow.Show(); Dispatcher.UIThread.RunJobs(); arrangedGraph.Fit(); Capture(arrangedWindow, "graph-tall-arranged"); arrangedWindow.Close();

// Exercise file and modal workflows with real disk writes and an isolated workspace.
var workflowRoot = Path.Combine(Path.GetFullPath(output), "workflow-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(workflowRoot);
var settingsRoot = Path.Combine(workflowRoot, "settings"); var store = new WorkspaceStore(settingsRoot);
var sourceFile = Path.Combine(workflowRoot, "website-story.json"); var savedFile = Path.Combine(workflowRoot, "my-story.dialogue.json");
var sourceJson = ProjectStore.WebExport(Templates.Harbour()); File.WriteAllText(sourceFile, sourceJson);
var missingFile = Path.Combine(workflowRoot, "moved-story.json");
ProjectStore.AtomicWrite(Path.Combine(settingsRoot, "preferences.json"), JsonSerializer.Serialize(new { Theme = "Light", Scale = 1.15, RecoveryEnabled = false, RecentFiles = new[] { sourceFile, sourceFile, missingFile } }, ProjectStore.Options));
var prefs = store.Load();
Check(prefs.Theme == "Light" && prefs.Scale == 1.15 && !prefs.RecoveryEnabled && prefs.RecentProjects.Count == 2, "old preferences migrate without resetting appearance or duplicating recent files");
Check(!prefs.ReopenLastProjectOnStartup && WorkspaceStartup.Plan(prefs, null, false).ProjectPath is null, "reopening the last project is opt-in");
WorkspaceStore.Remember(prefs, sourceFile, "Website story"); WorkspaceStore.Remember(prefs, savedFile, "My story"); WorkspaceStore.Remember(prefs, sourceFile, "Updated story");
Check(prefs.RecentProjects[0].Name == "Updated story" && prefs.RecentProjects.Count == 3 && prefs.LastProjectPath == sourceFile, "recents retain titles, deduplicate paths and move opened files to the top");
WorkspaceStore.Forget(prefs, sourceFile); Check(File.ReadAllText(sourceFile) == sourceJson && prefs.LastProjectPath == savedFile, "removing a recent never deletes the story file");
for (var i = 0; i < 10; i++) WorkspaceStore.Remember(prefs, Path.Combine(workflowRoot, $"story-{i}.json"), $"Story {i}");
Check(prefs.RecentProjects.Count == 8, "recent projects are bounded to eight entries");
prefs.LastProjectPath = sourceFile; prefs.ReopenLastProjectOnStartup = true;
Check(WorkspaceStartup.Plan(prefs, null, false).ProjectPath == sourceFile, "opt-in startup selects the existing last project");
Check(WorkspaceStartup.Plan(prefs, null, true).ProjectPath is null && WorkspaceStartup.Plan(prefs, null, true).Notice is not null, "recovery takes priority over automatic reopen");
Check(WorkspaceStartup.Plan(prefs, savedFile, true).ProjectPath == savedFile, "an explicit launch file takes priority over startup preferences");
prefs.LastProjectPath = missingFile; Check(WorkspaceStartup.Plan(prefs, null, false).Notice is not null, "a missing last project falls back to welcome with an explanation");
store.Save(new WorkspacePreferences());
void Pump(Task task)
{
    var deadline = DateTime.UtcNow.AddSeconds(5);
    while (!task.IsCompleted && DateTime.UtcNow < deadline) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
    if (!task.IsCompleted) throw new Exception("UI operation did not complete; a dialog may still be open.");
    task.GetAwaiter().GetResult(); Dispatcher.UIThread.RunJobs();
}
void Press(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
Window Modal(MainWindow owner) { Dispatcher.UIThread.RunJobs(); return owner.OwnedWindows.Single(w => w.IsVisible); }
void Answer(MainWindow owner, string answer) => Press(Modal(owner).GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, answer)));
var variableWorkspace = new MainWindow(null, true); variableWorkspace.Show(); Pump(variableWorkspace.LoadExampleAsync());
variableWorkspace.Project.InitialVariables["aff:unused"] = 0;
variableWorkspace.Project.ListOfCharacterStates[0].Text = "Hello {player_name}. [if aff:trust >= 1]Friend[else]Stranger[/if] {aff:trust}";
var beforeManager = ProjectStore.Serialize(variableWorkspace.Project);
Button NamedButton(Window owner, string name) => owner.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == name);
T NamedControl<T>(Window owner, string name) where T : Control => owner.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
void OpenManager() => Press(variableWorkspace.FindControl<Button>("VariablesButton")!);
void RenameInManager(Window manager, string oldId, string newId)
{
    Press(NamedButton(manager, "Rename " + oldId)); var prompt = manager.OwnedWindows.Single(w => w.IsVisible);
    prompt.GetVisualDescendants().OfType<TextBox>().Single().Text = newId;
    Press(prompt.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Continue")));
}
void AddInManager(Window manager, string label, string id)
{
    Press(NamedButton(manager, label)); var prompt = manager.OwnedWindows.Single(w => w.IsVisible);
    prompt.GetVisualDescendants().OfType<TextBox>().Single().Text = id;
    Press(prompt.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Continue")));
}
void PressAnswer(Window owner, string label) => Press(owner.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, label)));
void UndoWorkspace() { variableWorkspace.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z"); variableWorkspace.KeyRelease(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z"); Dispatcher.UIThread.RunJobs(); }
OpenManager(); var manager = Modal(variableWorkspace);
Check(!NamedButton(manager, "Delete aff:trust").IsEnabled && NamedButton(manager, "Delete aff:unused").IsEnabled, "manager allows unused deletion and protects referenced variables");
RenameInManager(manager, "aff:trust", "aff:rapport");
manager.GetVisualDescendants().OfType<NumericUpDown>().Single(n => AutomationProperties.GetName(n) == "aff:rapport").Value = 7;
Check(ProjectStore.Serialize(variableWorkspace.Project) == beforeManager, "renaming and editing manager values does not mutate the live project before Apply");
PressAnswer(manager, "Cancel");
Check(ProjectStore.Serialize(variableWorkspace.Project) == beforeManager, "Cancel discards variable renames and value changes together");
OpenManager(); manager = Modal(variableWorkspace); RenameInManager(manager, "aff:trust", "aff:rapport"); RenameInManager(manager, "player_name", "hero_name");
AddInManager(manager, "＋ Add numeric variable", "inv:tokens");
manager.GetVisualDescendants().OfType<NumericUpDown>().Single(n => AutomationProperties.GetName(n) == "inv:tokens").Value = 2;
AddInManager(manager, "＋ Add text substitution", "town_name");
manager.GetVisualDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "{town_name}").Text = "Harbour";
Press(NamedButton(manager, "Delete aff:unused"));
Press(NamedButton(manager, "References for Mara")); var speakerDialog = manager.OwnedWindows.Single(w => w.IsVisible);
Check(speakerDialog.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("arrival") == true), "speaker references dialog identifies matching scenes");
PressAnswer(speakerDialog, "Close");
RenameInManager(manager, "Mara", "Captain Mara");
Press(NamedButton(manager, "References for aff:rapport")); var referenceDialog = manager.OwnedWindows.Single(w => w.IsVisible);
Check(referenceDialog.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("arrival") == true), "reference details identify actual scene locations");
PressAnswer(referenceDialog, "Close"); NamedControl<TextBox>(manager, "VariableSearch").Text = "rapport"; Dispatcher.UIThread.RunJobs();
Check(manager.GetVisualDescendants().OfType<NumericUpDown>().Count() == 1 && !manager.GetVisualDescendants().OfType<TextBox>().Any(t => AutomationProperties.GetName(t) == "{hero_name}"), "variable search filters numeric and text cards");
Capture(manager, "variables-filtered"); NamedControl<TextBox>(manager, "VariableSearch").Text = ""; Dispatcher.UIThread.RunJobs(); Capture(manager, "variables-dark");
Check(manager.GetVisualDescendants().Where(c => c.TemplatedParent is null && c is TextBox or NumericUpDown).All(c => !string.IsNullOrEmpty(AutomationProperties.GetName(c))), "manager search and value fields have accessibility labels");
PressAnswer(manager, "Apply changes");
Check(variableWorkspace.Project.InitialVariables.ContainsKey("aff:rapport") && !variableWorkspace.Project.InitialVariables.ContainsKey("aff:unused") && VariableStore.References(variableWorkspace.Project, "aff:trust").Count == 0 && variableWorkspace.Project.ListOfCharacterStates[0].Text.Contains("{hero_name}"), "Apply updates numeric/text references and deletions together");
Check(variableWorkspace.Project.InitialVariables["inv:tokens"] == 2 && variableWorkspace.Project.TextVariables["town_name"] == "Harbour", "new numeric and text variables retain their edited values on Apply");
Check(variableWorkspace.Project.ListOfCharacterStates.All(s => s.Speaker == "Captain Mara"), "Apply renames scene speakers project-wide");
UndoWorkspace(); Check(ProjectStore.Serialize(variableWorkspace.Project) == beforeManager, "one Undo restores every applied manager operation and its references");
foreach (var theme in new[] { "Light", "High contrast" }) { variableWorkspace.ChangeAppearance(theme); OpenManager(); manager = Modal(variableWorkspace); Capture(manager, "variables-" + theme.ToLowerInvariant().Replace(' ', '-')); PressAnswer(manager, "Cancel"); }
variableWorkspace.ChangeAppearance("Dark", 1.3); OpenManager(); manager = Modal(variableWorkspace);
Check(manager.Content is LayoutTransformControl && Math.Abs(manager.Width - 845) < 1, "variable manager honors the 130 percent interface size"); Capture(manager, "variables-large-text");
RenameInManager(manager, "aff:trust", "aff:rapport"); manager.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, ""); Dispatcher.UIThread.RunJobs();
Check(!manager.IsVisible && ProjectStore.Serialize(variableWorkspace.Project) == beforeManager, "Escape cancels the complete manager draft");
variableWorkspace.ChangeAppearance("Dark");
var insertNarrative = variableWorkspace.FindControl<StackPanel>("EditorPanel")!.GetVisualDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "Dialogue text"); insertNarrative.SelectionStart = 0; insertNarrative.SelectionEnd = 0;
Press(NamedButton(variableWorkspace, "Variable…")); var insertion = Modal(variableWorkspace); NamedControl<TextBox>(insertion, "InsertVariableSearch").Text = "coin"; Dispatcher.UIThread.RunJobs();
Check(NamedControl<ListBox>(insertion, "InsertVariableList").Items.Count == 1, "inline variable picker searches available IDs"); Capture(insertion, "variable-insertion"); PressAnswer(insertion, "Insert");
Check(variableWorkspace.Project.ListOfCharacterStates[0].Text.StartsWith("{inv:coin}Hello"), "inline picker inserts the selected substitution at the cursor"); UndoWorkspace();
var speakerInput = variableWorkspace.FindControl<StackPanel>("EditorPanel")!.GetVisualDescendants().OfType<AutoCompleteBox>().Single(b => AutomationProperties.GetName(b) == "Speaker");
Check(speakerInput.ItemsSource!.Cast<string>().Contains("Mara"), "speaker autocomplete includes project speakers");
speakerInput.Text = "Navigator"; Dispatcher.UIThread.RunJobs();
Check(variableWorkspace.Project.ListOfCharacterStates[0].Speaker == "Navigator", "speaker input updates selected scene speaker");
UndoWorkspace();
Check(variableWorkspace.Project.ListOfCharacterStates[0].Speaker == "Mara", "speaker edits are undoable");
Press(variableWorkspace.FindControl<Button>("LogicTab")!); var beforePickerEdit = ProjectStore.Serialize(variableWorkspace.Project);
var variablePicker = variableWorkspace.FindControl<StackPanel>("EditorPanel")!.GetVisualDescendants().OfType<AutoCompleteBox>().First();
Check(variablePicker.ItemsSource!.Cast<string>().Contains("coin") && !variablePicker.ItemsSource!.Cast<string>().Contains("trust") && AutomationProperties.GetName(variablePicker) == "Variable ID", "logic variable picker offers IDs for the selected numeric type and has a label");
variablePicker.Text = "tokens"; Dispatcher.UIThread.RunJobs();
Check(DialogueEngine.DiscoverVariables(variableWorkspace.Project).Contains("inv:tokens"), "typing a new ID in the logic picker updates the actual condition");
UndoWorkspace(); Check(ProjectStore.Serialize(variableWorkspace.Project) == beforePickerEdit, "logic picker edits are undoable"); variableWorkspace.Close();

var findFile = Path.Combine(workflowRoot, "find-story.dialogue.json"); ProjectStore.AtomicWrite(findFile, searchBefore);
var findWorkspace = new MainWindow(null, true); findWorkspace.Show(); Pump(findWorkspace.OpenProjectAsync(findFile));
Window OpenFind()
{
    Press(findWorkspace.FindControl<Button>("FindReplaceButton")!); return Modal(findWorkspace);
}
void FindUndo() => Press(findWorkspace.FindControl<Button>("UndoButton")!);
string InlineText(Window owner, string name) => string.Concat(NamedControl<TextBlock>(owner, name).Inlines!.OfType<Avalonia.Controls.Documents.Run>().Select(r => r.Text));
var finder = OpenFind();
Check(!NamedControl<Button>(finder, "FindReplaceAll").IsEnabled && NamedControl<TextBox>(finder, "FindQuery").IsFocused, "find opens with keyboard focus and disables empty-query replacements");
NamedControl<TextBox>(finder, "FindQuery").Text = "cat"; NamedControl<TextBox>(finder, "FindReplacement").Text = "dog"; Dispatcher.UIThread.RunJobs();
Check(NamedControl<ListBox>(finder, "FindMatches").Items.Count == 9 && InlineText(finder, "FindBefore") == "Cat crossing" && InlineText(finder, "FindAfter") == "dog crossing", "find dialog lists occurrences and previews the selected replacement in context");
Check(ProjectStore.Serialize(findWorkspace.Project) == searchBefore && !findWorkspace.HasUnsavedChanges, "changing query, replacement and previews does not dirty the project");
Check(finder.GetVisualDescendants().Where(c => c.TemplatedParent is null && c is TextBox or ComboBox or CheckBox).All(c => !string.IsNullOrEmpty(AutomationProperties.GetName(c))), "find inputs, scope and checkboxes have accessibility labels");
Capture(finder, "find-dark");
NamedControl<CheckBox>(finder, "FindWholeWord").IsChecked = true; NamedControl<ComboBox>(finder, "FindScope").SelectedItem = "Selected scene";
NamedControl<CheckBox>(finder, "FindTitles").IsChecked = false; NamedControl<CheckBox>(finder, "FindResponses").IsChecked = false; NamedControl<CheckBox>(finder, "FindNotes").IsChecked = false; Dispatcher.UIThread.RunJobs();
Check(NamedControl<ListBox>(finder, "FindMatches").Items.Count == 3 && NamedControl<TextBlock>(finder, "FindSummary").Text!.Contains("selected scene"), "dialog field and scene scope refresh the match list");
NamedControl<CheckBox>(finder, "FindMatchCase").IsChecked = true; Dispatcher.UIThread.RunJobs();
Check(NamedControl<ListBox>(finder, "FindMatches").Items.Count == 2, "dialog case matching updates scoped occurrences");
PressAnswer(finder, "Replace all");
Check(ProjectStore.Serialize(findWorkspace.Project) == ProjectStore.Serialize(expectedSearch) && findWorkspace.HasUnsavedChanges && NamedControl<TextBlock>(finder, "FindSummary").Text!.StartsWith("Replaced 2 matches."), "actual Replace all applies precisely the previewed scope and reports its count");
PressAnswer(finder, "Close"); FindUndo();
Check(ProjectStore.Serialize(findWorkspace.Project) == searchBefore && !findWorkspace.HasUnsavedChanges, "one Undo restores an entire scoped replacement and clears dirty state");
Press(findWorkspace.FindControl<Button>("RedoButton")!);
Check(ProjectStore.Serialize(findWorkspace.Project) == ProjectStore.Serialize(expectedSearch), "Redo reapplies the complete replacement"); FindUndo();
finder = OpenFind(); NamedControl<TextBox>(finder, "FindQuery").Text = "cat"; NamedControl<TextBox>(finder, "FindReplacement").Text = "dog"; NamedControl<CheckBox>(finder, "FindWholeWord").IsChecked = true;
Dispatcher.UIThread.RunJobs(); PressAnswer(finder, "Replace all"); PressAnswer(finder, "Close");
Check(ProjectStore.Serialize(findWorkspace.Project) == ProjectStore.Serialize(expectedAll), "project-wide UI replacement updates all four fields across scenes without losing unknown data"); FindUndo();
finder = OpenFind(); NamedControl<TextBox>(finder, "FindQuery").Text = "cat"; NamedControl<TextBox>(finder, "FindReplacement").Text = "";
NamedControl<CheckBox>(finder, "FindTitles").IsChecked = false; NamedControl<CheckBox>(finder, "FindDialogue").IsChecked = false; NamedControl<CheckBox>(finder, "FindNotes").IsChecked = false; Dispatcher.UIThread.RunJobs(); PressAnswer(finder, "Replace selected");
Check(findWorkspace.Project.ListOfCharacterStates[0].Choices[0].ButtonText == "?" && NamedControl<ListBox>(finder, "FindMatches").Items.Count == 0 && !NamedControl<Button>(finder, "FindReplaceOne").IsEnabled, "Replace selected deletes one response occurrence and refreshes the remaining matches");
PressAnswer(finder, "Close"); FindUndo();
finder = OpenFind(); NamedControl<TextBox>(finder, "FindQuery").Text = "[b]cat[/b]"; Dispatcher.UIThread.RunJobs(); PressAnswer(finder, "Go to match");
var foundInput = findWorkspace.FindControl<StackPanel>("EditorPanel")!.GetVisualDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "Dialogue text");
Check(foundInput.IsFocused && foundInput.SelectedText == "[b]cat[/b]", "Go to match focuses and selects the exact dialogue occurrence");
findWorkspace.FindControl<TextBox>("SearchBox")!.Text = "no outline match";
finder = OpenFind(); NamedControl<TextBox>(finder, "FindQuery").Text = "note"; Dispatcher.UIThread.RunJobs(); PressAnswer(finder, "Go to match");
foundInput = findWorkspace.FindControl<StackPanel>("EditorPanel")!.GetVisualDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "Writer's notes");
Check(foundInput.IsFocused && foundInput.SelectedText == "note" && findWorkspace.FindControl<TextBox>("SearchBox")!.Text == "", "Go to match opens Notes and clears an outline filter that hides the scene");
finder = OpenFind(); NamedControl<TextBox>(finder, "FindQuery").Text = "cat?"; Dispatcher.UIThread.RunJobs(); PressAnswer(finder, "Go to match");
foundInput = findWorkspace.FindControl<StackPanel>("EditorPanel")!.GetVisualDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "Player response");
Check(foundInput.IsFocused && foundInput.SelectedText == "cat?", "Go to match selects the matching response editor");
finder = OpenFind(); NamedControl<TextBox>(finder, "FindQuery").Text = "cat";
foreach (var field in new[] { "FindDialogue", "FindResponses", "FindTitles", "FindNotes" }) NamedControl<CheckBox>(finder, field).IsChecked = false;
Dispatcher.UIThread.RunJobs();
Check(!NamedControl<Button>(finder, "FindReplaceAll").IsEnabled && NamedControl<TextBlock>(finder, "FindSummary").Text!.Contains("Choose at least one field"), "unchecking every field explains the empty scope and disables replacement");
finder.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, ""); Dispatcher.UIThread.RunJobs();
Check(!finder.IsVisible && ProjectStore.Serialize(findWorkspace.Project) == searchBefore, "Escape closes search without applying a preview");
findWorkspace.KeyPress(Key.H, RawInputModifiers.Control, PhysicalKey.H, "h"); findWorkspace.KeyRelease(Key.H, RawInputModifiers.Control, PhysicalKey.H, "h"); Dispatcher.UIThread.RunJobs();
finder = Modal(findWorkspace); Check(NamedControl<TextBox>(finder, "FindQuery").IsFocused, "Ctrl+H opens find and replace from the keyboard"); PressAnswer(finder, "Close");
findWorkspace.KeyPress(Key.F, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.F, "F"); findWorkspace.KeyRelease(Key.F, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.F, "F"); Dispatcher.UIThread.RunJobs();
finder = Modal(findWorkspace); Check(NamedControl<TextBox>(finder, "FindQuery").IsFocused, "Ctrl+Shift+F opens project-wide find without changing outline search"); PressAnswer(finder, "Close");
foreach (var theme in new[] { "Light", "High contrast" })
{
    findWorkspace.ChangeAppearance(theme); finder = OpenFind(); NamedControl<TextBox>(finder, "FindQuery").Text = "cat"; NamedControl<TextBox>(finder, "FindReplacement").Text = "dog"; Dispatcher.UIThread.RunJobs();
    Capture(finder, "find-" + theme.ToLowerInvariant().Replace(' ', '-')); PressAnswer(finder, "Close");
}
foreach (var scale in new[] { 1.15, 1.3 })
{
    findWorkspace.ChangeAppearance("Dark", scale); finder = OpenFind(); NamedControl<TextBox>(finder, "FindQuery").Text = "cat"; Dispatcher.UIThread.RunJobs();
    Check(finder.Content is LayoutTransformControl && Math.Abs(finder.Width - 780 * scale) < 1 && NamedControl<Button>(finder, "FindReplaceAll").IsEffectivelyVisible, "find dialog scales its content and window at " + scale);
    Capture(finder, "find-scale-" + (int)(scale * 100)); PressAnswer(finder, "Close");
}
findWorkspace.ChangeAppearance("Dark"); finder = OpenFind(); finder.Width = finder.MinWidth; finder.Height = finder.MinHeight;
NamedControl<TextBox>(finder, "FindQuery").Text = "cat"; Dispatcher.UIThread.RunJobs(); Capture(finder, "find-compact");
var findFooterButton = NamedControl<Button>(finder, "FindReplaceAll"); var footerPoint = findFooterButton.TranslatePoint(default, finder)!.Value;
Check(footerPoint.Y >= 0 && footerPoint.Y + findFooterButton.Bounds.Height <= finder.ClientSize.Height && footerPoint.X + findFooterButton.Bounds.Width <= finder.ClientSize.Width, "compact find dialog keeps replacement controls within the window");
PressAnswer(finder, "Close");
var pendingTitle = findWorkspace.FindControl<StackPanel>("EditorPanel")!.GetVisualDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "Scene title");
pendingTitle.Focus(); pendingTitle.Text = "Pending cat edit"; Dispatcher.UIThread.RunJobs();
finder = OpenFind(); NamedControl<TextBox>(finder, "FindQuery").Text = "cat"; NamedControl<TextBox>(finder, "FindReplacement").Text = "dog";
NamedControl<CheckBox>(finder, "FindDialogue").IsChecked = false; NamedControl<CheckBox>(finder, "FindResponses").IsChecked = false; NamedControl<CheckBox>(finder, "FindNotes").IsChecked = false;
NamedControl<ComboBox>(finder, "FindScope").SelectedItem = "Selected scene"; Dispatcher.UIThread.RunJobs(); PressAnswer(finder, "Replace all"); PressAnswer(finder, "Close"); FindUndo();
Check(findWorkspace.Project.ListOfCharacterStates[0].Title == "Pending cat edit", "replacement Undo preserves typing committed immediately before opening find");
FindUndo(); Check(ProjectStore.Serialize(findWorkspace.Project) == searchBefore && !findWorkspace.HasUnsavedChanges, "typing before find retains its separate undo entry");

var locExportPath = Path.Combine(workflowRoot, "exported-translations.csv");
Pump(findWorkspace.ExportLocalizationAsync(locExportPath));
Check(File.Exists(locExportPath) && File.ReadAllText(locExportPath).Contains("cat2:dialogue"), "ExportLocalizationAsync exports project translation table to disk");

var locEdit = File.ReadAllText(locExportPath).Replace("Cat crossing", "Chat traversant");
File.WriteAllText(locExportPath, locEdit);
var beforeImport = ProjectStore.Serialize(findWorkspace.Project);
Pump(findWorkspace.ImportLocalizationAsync(locExportPath));
Check(findWorkspace.Project.ListOfCharacterStates[0].Title == "Chat traversant", "ImportLocalizationAsync updates scene title from CSV file");
FindUndo();
Check(ProjectStore.Serialize(findWorkspace.Project) == beforeImport, "Imported translation changes undo in one step");

findWorkspace.Close();

var navigationUiStory = ProjectStore.Clone(navigationStory); navigationUiStory.ListOfCharacterStates[2].Text += " {unknown_text}"; navigationUiStory.ListOfCharacterStates[3].DefaultNextId = "missing_target";
navigationUiStory.Extra = new() { ["FutureNavigation"] = JsonSerializer.SerializeToElement(new { keep = "keeper" }) };
var navigationUiBefore = ProjectStore.Serialize(navigationUiStory); var navigationFile = Path.Combine(workflowRoot, "navigation.dialogue.json"); ProjectStore.AtomicWrite(navigationFile, navigationUiBefore);
var navigationWorkspace = new MainWindow(null, true); navigationWorkspace.Show(); Pump(navigationWorkspace.OpenProjectAsync(navigationFile));
void NavigationUndo() => Press(navigationWorkspace.FindControl<Button>("UndoButton")!);
void OutlineOption(string name, string label)
{
    var combo = navigationWorkspace.FindControl<ComboBox>(name)!; combo.SelectedItem = combo.Items.Cast<object>().Single(o => o.ToString() == label); Dispatcher.UIThread.RunJobs();
}
void NavigationScene(string id)
{
    Press(navigationWorkspace.FindControl<Button>("ClearFiltersButton")!); var scenes = navigationWorkspace.FindControl<ListBox>("SceneList")!;
    scenes.SelectedItem = scenes.Items.Cast<NavigationScene>().Single(item => item.Id == id); Dispatcher.UIThread.RunJobs();
}
Window OpenDestination(string name) { Press(NamedButton(navigationWorkspace, name)); return Modal(navigationWorkspace); }
var destinationPicker = OpenDestination("Choose choice 1 destination");
Check(NamedControl<TextBox>(destinationPicker, "DestinationSearch").IsFocused && NamedControl<ListBox>(destinationPicker, "DestinationMatches").Items.Cast<DestinationOption>().First().Id is null, "destination picker focuses search and preserves the inherited continue option");
NamedControl<TextBox>(destinationPicker, "DestinationSearch").Text = "SMALL KINDNESS"; Dispatcher.UIThread.RunJobs();
Check(NamedControl<ListBox>(destinationPicker, "DestinationMatches").Items.Cast<DestinationOption>().Single().Id == "gift" && ProjectStore.Serialize(navigationWorkspace.Project) == navigationUiBefore, "typing a destination title narrows results without editing the story");
PressAnswer(destinationPicker, "Cancel"); Check(ProjectStore.Serialize(navigationWorkspace.Project) == navigationUiBefore, "Cancel leaves destination, unknown fields and history unchanged");
destinationPicker = OpenDestination("Choose choice 1 destination"); NamedControl<TextBox>(destinationPicker, "DestinationSearch").Text = "gift"; Dispatcher.UIThread.RunJobs();
destinationPicker.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, ""); destinationPicker.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, ""); Dispatcher.UIThread.RunJobs();
Check(NamedControl<ListBox>(destinationPicker, "DestinationMatches").IsKeyboardFocusWithin, "Down moves from destination search into keyboard results");
destinationPicker.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, ""); Dispatcher.UIThread.RunJobs();
var expectedDestination = ProjectStore.Parse(navigationUiBefore); expectedDestination.ListOfCharacterStates[0].Choices[0].NextStateId = "gift";
Check(!destinationPicker.IsVisible && ProjectStore.Serialize(navigationWorkspace.Project) == ProjectStore.Serialize(expectedDestination), "Enter chooses the selected destination and changes only that connection");
NavigationUndo(); Check(ProjectStore.Serialize(navigationWorkspace.Project) == navigationUiBefore && !navigationWorkspace.HasUnsavedChanges, "one Undo restores the previous fallback and all unknown data");
destinationPicker = OpenDestination("Choose continue destination"); NamedControl<TextBox>(destinationPicker, "DestinationSearch").Text = "no continue"; Dispatcher.UIThread.RunJobs(); PressAnswer(destinationPicker, "Choose destination");
Check(navigationWorkspace.Project.ListOfCharacterStates[0].DefaultNextId is null && navigationWorkspace.Project.ListOfCharacterStates[0].Choices[0].NextStateId is null, "choosing no continue keeps an inherited choice unset");
navigationWorkspace.FindControl<ComboBox>("IssueFilter")!.SelectedIndex = 2; Dispatcher.UIThread.RunJobs();
Check(navigationWorkspace.FindControl<ListBox>("SceneList")!.Items.Cast<NavigationScene>().Any(item => item.Id == "arrival"), "diagnostic filters refresh immediately after changing a connection");
NavigationUndo(); Press(navigationWorkspace.FindControl<Button>("ClearFiltersButton")!);
destinationPicker = OpenDestination("Choose choice 1 destination"); NamedControl<TextBox>(destinationPicker, "DestinationSearch").Text = "nothing matches this"; Dispatcher.UIThread.RunJobs();
Check(!NamedControl<Button>(destinationPicker, "ChooseDestination").IsEnabled && NamedControl<TextBlock>(destinationPicker, "DestinationSummary").Text!.StartsWith("No matching"), "empty destination results explain the state and disable Choose");
destinationPicker.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, ""); Dispatcher.UIThread.RunJobs(); Check(!destinationPicker.IsVisible && !navigationWorkspace.HasUnsavedChanges, "Escape cancels destination search without creating history");
navigationWorkspace.FindControl<Expander>("OutlineFilters")!.IsExpanded = true; OutlineOption("SpeakerFilter", "Ada"); OutlineOption("TagFilter", "Mystery");
navigationWorkspace.FindControl<TextBox>("SearchBox")!.Text = "keeper"; Dispatcher.UIThread.RunJobs();
Check(navigationWorkspace.FindControl<ListBox>("SceneList")!.Items.Cast<NavigationScene>().Single() is NavigationScene { Id: "keeper" } && navigationWorkspace.FindControl<TextBlock>("SceneCount")!.Text == "1/5", "outline combines speaker, tag and text filters and reports visible/total scenes");
Capture(navigationWorkspace, "navigation-filters-dark"); navigationWorkspace.FindControl<TextBox>("SearchBox")!.Text = "absent scene"; Dispatcher.UIThread.RunJobs();
Check(navigationWorkspace.FindControl<TextBlock>("OutlineEmpty")!.IsVisible && navigationWorkspace.FindControl<ListBox>("SceneList")!.Items.Count == 0, "an empty filtered outline offers clear-search guidance");
Press(navigationWorkspace.FindControl<Button>("ClearFiltersButton")!); navigationWorkspace.FindControl<ComboBox>("IssueFilter")!.SelectedIndex = 2; Dispatcher.UIThread.RunJobs();
Check(navigationWorkspace.FindControl<ListBox>("SceneList")!.Items.Cast<NavigationScene>().Single() is NavigationScene { Id: "promise" }, "the Errors outline filter lists the actual broken-target scene");
navigationWorkspace.FindControl<ComboBox>("IssueFilter")!.SelectedIndex = 3; Dispatcher.UIThread.RunJobs();
Check(navigationWorkspace.FindControl<ListBox>("SceneList")!.Items.Cast<NavigationScene>().Any(item => item.Id == "gift"), "the Warnings outline filter includes unknown substitution diagnostics");
Press(navigationWorkspace.FindControl<Button>("ClearFiltersButton")!);
Check(navigationWorkspace.FindControl<ListBox>("SceneList")!.Items.Count == 5 && ProjectStore.Serialize(navigationWorkspace.Project) == navigationUiBefore && !navigationWorkspace.HasUnsavedChanges, "clearing filters restores the full outline without editing or dirtying the story");
OutlineOption("SpeakerFilter", "Mara"); OutlineOption("TagFilter", "coast"); Press(NamedButton(navigationWorkspace, "Jump to choice 1 destination"));
Check(navigationWorkspace.FindControl<TextBlock>("EditorSubtitle")!.Text!.StartsWith("keeper") && navigationWorkspace.FindControl<ListBox>("SceneList")!.Items.Count == 5, "Jump follows the inherited destination and reveals a scene hidden by filters");
Press(navigationWorkspace.FindControl<Button>("NotesTab")!);
Check(NamedButton(navigationWorkspace, "← A light in the fog [arrival]").IsEnabled && NamedButton(navigationWorkspace, "← A small kindness [gift]").IsEnabled, "Notes presents both incoming source scenes with keyboard buttons");
Press(NamedButton(navigationWorkspace, "Choice 1 → Into the unknown [promise]"));
Check(navigationWorkspace.FindControl<TextBlock>("EditorSubtitle")!.Text!.StartsWith("promise") && navigationWorkspace.FindControl<StackPanel>("EditorPanel")!.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Continue → Missing: missing_target"), "outgoing navigation opens its target and explains missing downstream connections");
Press(navigationWorkspace.FindControl<Button>("ContentTab")!); Check(!NamedButton(navigationWorkspace, "Jump to continue destination").IsEnabled, "missing destination keeps Jump disabled while remaining repairable");
destinationPicker = OpenDestination("Choose continue destination");
Check((NamedControl<ListBox>(destinationPicker, "DestinationMatches").SelectedItem as DestinationOption)?.Id == "missing_target", "picker preserves a missing current destination until an explicit choice");
NamedControl<TextBox>(destinationPicker, "DestinationSearch").Text = "end conversation"; Dispatcher.UIThread.RunJobs(); PressAnswer(destinationPicker, "Choose destination");
Check(navigationWorkspace.Project.ListOfCharacterStates[3].DefaultNextId == "[EXIT]" && !NamedButton(navigationWorkspace, "Jump to continue destination").IsEnabled, "choosing an ending writes the reserved ending target without offering scene navigation"); NavigationUndo();
NavigationScene("arrival"); Press(navigationWorkspace.FindControl<Button>("NotesTab")!);
var tagInput = navigationWorkspace.FindControl<StackPanel>("EditorPanel")!.GetVisualDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "Tags (comma separated)");
tagInput.Focus(); tagInput.Text += ", newtag"; Dispatcher.UIThread.RunJobs(); navigationWorkspace.FindControl<TextBox>("SearchBox")!.Focus(); Dispatcher.UIThread.RunJobs();
Check(navigationWorkspace.FindControl<ComboBox>("TagFilter")!.Items.Cast<object>().Any(o => o.ToString() == "newtag"), "committed tag edits refresh available outline filters"); NavigationUndo();
Check(!navigationWorkspace.FindControl<ComboBox>("TagFilter")!.Items.Cast<object>().Any(o => o.ToString() == "newtag") && ProjectStore.Serialize(navigationWorkspace.Project) == navigationUiBefore, "undo refreshes the navigation index and removes obsolete tags");
Press(navigationWorkspace.FindControl<Button>("ContentTab")!); destinationPicker = OpenDestination("Choose continue destination");
Check(destinationPicker.GetVisualDescendants().Where(c => c.TemplatedParent is null && c is TextBox or ListBox).All(c => !string.IsNullOrEmpty(AutomationProperties.GetName(c))), "destination search and result list have accessibility labels"); Capture(destinationPicker, "destination-dark"); PressAnswer(destinationPicker, "Cancel");
foreach (var theme in new[] { "Light", "High contrast" })
{
    navigationWorkspace.ChangeAppearance(theme); OutlineOption("SpeakerFilter", "Ada"); Capture(navigationWorkspace, "navigation-filters-" + theme.ToLowerInvariant().Replace(' ', '-')); Press(navigationWorkspace.FindControl<Button>("ClearFiltersButton")!);
    destinationPicker = OpenDestination("Choose continue destination"); NamedControl<TextBox>(destinationPicker, "DestinationSearch").Text = "keeper"; Dispatcher.UIThread.RunJobs(); Capture(destinationPicker, "destination-" + theme.ToLowerInvariant().Replace(' ', '-')); PressAnswer(destinationPicker, "Cancel");
}
foreach (var scale in new[] { 1.15, 1.3 })
{
    navigationWorkspace.ChangeAppearance("Dark", scale); destinationPicker = OpenDestination("Choose continue destination");
    Check(destinationPicker.Content is LayoutTransformControl && Math.Abs(destinationPicker.Width - 610 * scale) < 1, "destination picker respects interface scale " + scale); Capture(destinationPicker, "destination-scale-" + (int)(scale * 100)); PressAnswer(destinationPicker, "Cancel");
}
navigationWorkspace.ChangeAppearance("Dark"); navigationWorkspace.Width = 1000; navigationWorkspace.Height = 680;
OutlineOption("SpeakerFilter", "Ada"); Dispatcher.UIThread.RunJobs(); Capture(navigationWorkspace, "navigation-filters-compact");
Check(navigationWorkspace.FindControl<ComboBox>("IssueFilter")!.IsEffectivelyVisible && navigationWorkspace.FindControl<ListBox>("SceneList")!.Bounds.Height > 30, "compact workspace keeps expanded filters and a scrollable outline available");
navigationWorkspace.Close();
var authoringUiPath = Path.Combine(workflowRoot, "authoring.dialogue.json"); ProjectStore.AtomicWrite(authoringUiPath, ProjectStore.Serialize(Templates.Harbour()));
var authoringWorkspace = new MainWindow(null, true); authoringWorkspace.Show(); Pump(authoringWorkspace.OpenProjectAsync(authoringUiPath));
void AuthoringEdit(string label)
{
    var editMenu = authoringWorkspace.FindControl<Menu>("AppMenu")!.Items.Cast<MenuItem>().Single(m => Equals(m.Header, "_Edit"));
    editMenu.Items.OfType<MenuItem>().Single(m => Equals(m.Header, label)).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Dispatcher.UIThread.RunJobs();
}
void AuthoringUndo() => Press(authoringWorkspace.FindControl<Button>("UndoButton")!);
void AuthoringRedo() => Press(authoringWorkspace.FindControl<Button>("RedoButton")!);
var authoringUiBefore = ProjectStore.Serialize(authoringWorkspace.Project);
Press(NamedButton(authoringWorkspace, "Create scene from choice 1")); var authoringPrompt = Modal(authoringWorkspace);
authoringPrompt.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, ""); Dispatcher.UIThread.RunJobs();
Check(ProjectStore.Serialize(authoringWorkspace.Project) == authoringUiBefore && !authoringWorkspace.HasUnsavedChanges, "canceling connected-scene creation preserves the original connection and project");
Press(NamedButton(authoringWorkspace, "Create scene from choice 1")); authoringPrompt = Modal(authoringWorkspace);
authoringPrompt.GetVisualDescendants().OfType<TextBox>().Single().Text = "An unexpected visitor"; PressAnswer(authoringPrompt, "Continue");
var uiConnected = authoringWorkspace.Project.ListOfCharacterStates[^1];
Check(authoringWorkspace.Project.ListOfCharacterStates[0].Choices[0].NextStateId == uiConnected.State && uiConnected.Title == "An unexpected visitor" && authoringWorkspace.FindControl<TextBlock>("EditorSubtitle")!.Text!.StartsWith(uiConnected.State), "New scene creates and selects a connected scene through actual controls");
AuthoringUndo(); Check(ProjectStore.Serialize(authoringWorkspace.Project) == authoringUiBefore && !authoringWorkspace.HasUnsavedChanges, "one Undo restores connected-scene creation and its previous destination");
AuthoringRedo(); Check(authoringWorkspace.Project.ListOfCharacterStates.Count == 6, "Redo restores the scene and the choice connection together"); AuthoringUndo();
AuthoringEdit("Copy scenes…"); var copyDialog = Modal(authoringWorkspace); var copyList = NamedControl<ListBox>(copyDialog, "CopySceneList");
Check(copyList.IsFocused && copyList.SelectedItems!.Count == 1, "copy dialog focuses the scene list and preselects the current scene");
copyDialog.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " "); copyDialog.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " "); Dispatcher.UIThread.RunJobs();
Check(copyList.SelectedItems!.Count == 0, "Space toggles scene selection from the keyboard");
copyList.SelectedItems!.Clear(); Check(!NamedControl<Button>(copyDialog, "CopyScenesConfirm").IsEnabled, "copy dialog disables empty selections");
copyList.SelectedItems.Add(copyList.Items[0]); copyList.SelectedItems.Add(copyList.Items[1]); Dispatcher.UIThread.RunJobs();
Capture(copyDialog, "copy-scenes-dark"); PressAnswer(copyDialog, "Copy selected scenes");
Check(ProjectStore.Serialize(authoringWorkspace.Project) == authoringUiBefore && !authoringWorkspace.HasUnsavedChanges, "copying multiple scenes through the dialog does not dirty the story");
AuthoringEdit("Paste scenes"); var uiPastes = authoringWorkspace.Project.ListOfCharacterStates.TakeLast(2).ToList();
Check(authoringWorkspace.Project.ListOfCharacterStates.Count == 7 && uiPastes[0].Choices[0].NextStateId == uiPastes[1].State && authoringWorkspace.HasUnsavedChanges, "Paste scenes adds the selected set with remapped choice links and marks edits unsaved");
AuthoringUndo(); Check(ProjectStore.Serialize(authoringWorkspace.Project) == authoringUiBefore, "one Undo removes an entire multi-scene paste");
AuthoringRedo(); Check(authoringWorkspace.Project.ListOfCharacterStates.Count == 7, "Redo restores the entire pasted set"); AuthoringUndo();
AuthoringEdit("Copy scenes…"); copyDialog = Modal(authoringWorkspace); PressAnswer(copyDialog, "Select all");
Check(NamedControl<ListBox>(copyDialog, "CopySceneList").SelectedItems!.Count == 5, "Select all marks every source scene for copying"); PressAnswer(copyDialog, "Cancel");
AuthoringEdit("Paste scenes"); Check(authoringWorkspace.Project.ListOfCharacterStates.Count == 7, "canceling a later copy preserves the previous scene clipboard"); AuthoringUndo();
Press(authoringWorkspace.FindControl<Button>("NotesTab")!); Press(NamedButton(authoringWorkspace, "Save scene template…")); authoringPrompt = Modal(authoringWorkspace);
PressAnswer(authoringPrompt, "Cancel"); Check(ProjectStore.Serialize(authoringWorkspace.Project) == authoringUiBefore, "canceling template naming saves no template");
Press(NamedButton(authoringWorkspace, "Save scene template…")); authoringPrompt = Modal(authoringWorkspace);
authoringPrompt.GetVisualDescendants().OfType<TextBox>().Single().Text = "Opening conversation"; PressAnswer(authoringPrompt, "Continue");
Check(authoringWorkspace.Project.SceneTemplates.Single().Name == "Opening conversation" && authoringWorkspace.HasUnsavedChanges, "Notes saves a reusable scene template in the project");
AuthoringUndo(); Check(authoringWorkspace.Project.SceneTemplates.Count == 0 && !authoringWorkspace.HasUnsavedChanges, "saving a scene template is one undo step"); AuthoringRedo();
Press(authoringWorkspace.FindControl<Button>("ContentTab")!); Press(NamedButton(authoringWorkspace, "Save choice 2 template")); authoringPrompt = Modal(authoringWorkspace);
authoringPrompt.GetVisualDescendants().OfType<TextBox>().Single().Text = "Give an item"; PressAnswer(authoringPrompt, "Continue");
Check(authoringWorkspace.Project.ChoiceTemplates.Single().Choice.Requirements.Count == 1, "Content captures the chosen response and its logic as a template");
var authoringWithTemplates = ProjectStore.Serialize(authoringWorkspace.Project);
AuthoringEdit("Scene & choice templates…"); var templateDialog = Modal(authoringWorkspace);
Check(NamedControl<TextBox>(templateDialog, "TemplateSearch").IsFocused && NamedControl<ListBox>(templateDialog, "TemplateList").Items.Count == 2, "template manager opens with scene and choice templates and keyboard focus");
NamedControl<TextBox>(templateDialog, "TemplateSearch").Text = "nothing matches"; Dispatcher.UIThread.RunJobs();
Check(!NamedControl<Button>(templateDialog, "InsertTemplate").IsEnabled && !NamedControl<Button>(templateDialog, "DeleteTemplate").IsEnabled && NamedControl<TextBlock>(templateDialog, "TemplateSummary").Text!.StartsWith("No matching"), "empty template searches explain the state and disable operations");
NamedControl<TextBox>(templateDialog, "TemplateSearch").Text = "OPENING"; Dispatcher.UIThread.RunJobs(); Capture(templateDialog, "templates-dark");
PressAnswer(templateDialog, "Close"); Check(ProjectStore.Serialize(authoringWorkspace.Project) == authoringWithTemplates, "searching and closing the template manager leaves story and definitions unchanged");
AuthoringEdit("Scene & choice templates…"); templateDialog = Modal(authoringWorkspace); PressAnswer(templateDialog, "Insert template");
Check(authoringWorkspace.Project.ListOfCharacterStates.Count == 6 && authoringWorkspace.Project.ListOfCharacterStates[^1].Title == "A light in the fog" && authoringWorkspace.Project.ListOfCharacterStates[^1].Choices[0].NextStateId is null, "Insert template creates a new scene with detached external links");
AuthoringUndo(); Check(ProjectStore.Serialize(authoringWorkspace.Project) == authoringWithTemplates, "one Undo removes a template scene while preserving template definitions");
Press(NamedButton(authoringWorkspace, "Add choice from template…")); templateDialog = Modal(authoringWorkspace);
Check(NamedControl<ListBox>(templateDialog, "TemplateList").Items.Count == 1, "choice insertion lists only reusable choices"); templateDialog.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r"); Dispatcher.UIThread.RunJobs();
Check(authoringWorkspace.Project.ListOfCharacterStates[0].Choices.Count == 4 && authoringWorkspace.Project.ListOfCharacterStates[0].Choices[^1].NextStateId is null && authoringWorkspace.Project.ListOfCharacterStates[0].Choices[^1].AfterChoiceActions.Count == 2, "choice template insertion adds the full logic and uses the scene fallback");
AuthoringUndo(); Check(ProjectStore.Serialize(authoringWorkspace.Project) == authoringWithTemplates, "one Undo removes an inserted choice template");
AuthoringEdit("Scene & choice templates…"); templateDialog = Modal(authoringWorkspace); PressAnswer(templateDialog, "Delete template");
Check(authoringWorkspace.Project.SceneTemplates.Count == 0 && NamedControl<ListBox>(templateDialog, "TemplateList").Items.Count == 1, "template deletion refreshes the available definitions"); PressAnswer(templateDialog, "Close");
AuthoringUndo(); Check(ProjectStore.Serialize(authoringWorkspace.Project) == authoringWithTemplates, "template deletion can be undone after closing the manager");
Press(authoringWorkspace.FindControl<Button>("VariablesButton")!); var templateVariableManager = Modal(authoringWorkspace);
Press(NamedButton(templateVariableManager, "References for aff:trust")); var templateReferenceDialog = templateVariableManager.OwnedWindows.Single(w => w.IsVisible);
Check(templateReferenceDialog.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("Scene template: Opening conversation") == true) && templateReferenceDialog.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("Choice template: Give an item") == true), "variable manager reference details display both scene and choice templates");
PressAnswer(templateReferenceDialog, "Close"); Press(NamedButton(templateVariableManager, "Rename aff:trust")); var templateRenamePrompt = templateVariableManager.OwnedWindows.Single(w => w.IsVisible);
templateRenamePrompt.GetVisualDescendants().OfType<TextBox>().Single().Text = "aff:rapport"; PressAnswer(templateRenamePrompt, "Continue"); PressAnswer(templateVariableManager, "Apply changes");
Check(authoringWorkspace.Project.SceneTemplates[0].Scene.Choices[1].AfterChoiceActions[1].TargetId == "rapport" && authoringWorkspace.Project.ChoiceTemplates[0].Choice.AfterChoiceActions[1].TargetId == "rapport", "applying a variable rename updates reusable template logic in the actual manager");
AuthoringUndo(); Check(ProjectStore.Serialize(authoringWorkspace.Project) == authoringWithTemplates, "one Undo restores template-aware variable changes and live story together");
AuthoringEdit("Scene & choice templates…"); templateDialog = Modal(authoringWorkspace);
templateDialog.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, ""); Dispatcher.UIThread.RunJobs();
Check(NamedControl<ListBox>(templateDialog, "TemplateList").IsKeyboardFocusWithin, "Down moves from template search to selectable results");
templateDialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, ""); Dispatcher.UIThread.RunJobs();
Check(ProjectStore.Serialize(authoringWorkspace.Project) == authoringWithTemplates, "Escape closes template previews without inserting anything");
foreach (var theme in new[] { "Light", "High contrast" })
{
    authoringWorkspace.ChangeAppearance(theme); AuthoringEdit("Copy scenes…"); copyDialog = Modal(authoringWorkspace); Capture(copyDialog, "copy-scenes-" + theme.ToLowerInvariant().Replace(' ', '-')); PressAnswer(copyDialog, "Cancel");
    AuthoringEdit("Scene & choice templates…"); templateDialog = Modal(authoringWorkspace); Capture(templateDialog, "templates-" + theme.ToLowerInvariant().Replace(' ', '-')); PressAnswer(templateDialog, "Close");
}
foreach (var scale in new[] { 1.15, 1.3 })
{
    authoringWorkspace.ChangeAppearance("Dark", scale); AuthoringEdit("Copy scenes…"); copyDialog = Modal(authoringWorkspace);
    Check(copyDialog.Content is LayoutTransformControl && Math.Abs(copyDialog.Width - 650 * scale) < 1, "copy dialog respects interface size " + scale); Capture(copyDialog, "copy-scenes-scale-" + (int)(scale * 100)); PressAnswer(copyDialog, "Cancel");
    AuthoringEdit("Scene & choice templates…"); templateDialog = Modal(authoringWorkspace);
    Check(templateDialog.Content is LayoutTransformControl && Math.Abs(templateDialog.Width - 760 * scale) < 1, "template manager respects interface size " + scale); Capture(templateDialog, "templates-scale-" + (int)(scale * 100));
    Check(templateDialog.GetVisualDescendants().Where(c => c.TemplatedParent is null && c is TextBox or ListBox).All(c => !string.IsNullOrEmpty(AutomationProperties.GetName(c))), "template search and selection have accessibility names at " + scale); PressAnswer(templateDialog, "Close");
}
authoringWorkspace.ChangeAppearance("Dark", 1.3); authoringWorkspace.Width = 1000; authoringWorkspace.Height = 680; Dispatcher.UIThread.RunJobs(); Capture(authoringWorkspace, "authoring-compact-large-text");
NamedButton(authoringWorkspace, "Create scene from choice 1").BringIntoView(); Dispatcher.UIThread.RunJobs(); Capture(authoringWorkspace, "authoring-choices-large-text");
AuthoringEdit("Scene & choice templates…"); templateDialog = Modal(authoringWorkspace); templateDialog.Width = 600; templateDialog.Height = 430; Dispatcher.UIThread.RunJobs(); Capture(templateDialog, "templates-compact-large-text");
var insertTemplateButton = NamedControl<Button>(templateDialog, "InsertTemplate"); var insertPoint = insertTemplateButton.TranslatePoint(default, templateDialog)!.Value;
Check(insertPoint.Y + insertTemplateButton.Bounds.Height * 1.3 < templateDialog.ClientSize.Height, "compact large-text template manager keeps Insert reachable"); PressAnswer(templateDialog, "Close");
var authoringSavePicker = new TestFilePicker(); var authoringSave = new MainWindow(null, true, filePicker: authoringSavePicker); authoringSave.Show();
var authoringRoundtripPath = Path.Combine(workflowRoot, "authoring-template-roundtrip.dialogue.json");
ProjectStore.AtomicWrite(authoringRoundtripPath, authoringWithTemplates); Pump(authoringSave.OpenProjectAsync(authoringRoundtripPath));
Check(authoringSave.Project.SceneTemplates.Count == 1 && authoringSave.Project.ChoiceTemplates.Count == 1, "opening a saved project restores reusable template definitions"); authoringSave.Close();
Pump(authoringWorkspace.SaveProjectAsync()); var otherAuthoringPath = Path.Combine(workflowRoot, "other-authoring.dialogue.json"); ProjectStore.AtomicWrite(otherAuthoringPath, ProjectStore.Serialize(Templates.Blank()));
Pump(authoringWorkspace.OpenProjectAsync(otherAuthoringPath)); AuthoringEdit("Paste scenes");
Check(authoringWorkspace.Project.ListOfCharacterStates.Count == 3 && authoringWorkspace.Project.ListOfCharacterStates[1].Choices[0].NextStateId == authoringWorkspace.Project.ListOfCharacterStates[2].State && authoringWorkspace.Project.Entry == "start", "scene clipboard persists across actual project changes and preserves the destination project's entry");
AuthoringUndo(); Check(!authoringWorkspace.HasUnsavedChanges && authoringWorkspace.Project.ListOfCharacterStates.Count == 1, "cross-project paste remains a single undo step");
authoringWorkspace.Close();

// Select and move sets through actual graph/outline input, with serialized compatibility checks.
var graphStory = Templates.Harbour();
for (var i = 0; i < graphStory.ListOfCharacterStates.Count; i++) { graphStory.ListOfCharacterStates[i].VisualX = i % 3 * 370; graphStory.ListOfCharacterStates[i].VisualY = i / 3 * 340; }
graphStory.Extra = new() { ["FutureRoot"] = JsonSerializer.SerializeToElement(new { Keep = true }) };
graphStory.ListOfCharacterStates[0].Extra = new() { ["FutureScene"] = JsonSerializer.SerializeToElement("keep") };
graphStory.ListOfCharacterStates[0].Choices[0].Extra = new() { ["FutureChoice"] = JsonSerializer.SerializeToElement(17) };
var graphPath = Path.Combine(workflowRoot, "graph.dialogue.json"); ProjectStore.AtomicWrite(graphPath, ProjectStore.Serialize(graphStory));
var graphWorkspace = new MainWindow(null, true); graphWorkspace.Show(); Pump(graphWorkspace.OpenProjectAsync(graphPath));
var multiGraph = graphWorkspace.FindControl<GraphCanvas>("Graph")!;
var graphBaseline = ProjectStore.Serialize(graphWorkspace.Project);
List<DialogueState> GraphScenes() => graphWorkspace.Project.ListOfCharacterStates;
Point GraphPoint(int index, double x = 35, double y = 95) => multiGraph.TranslatePoint(multiGraph.WorldToViewport(new Point(GraphScenes()[index].VisualX + x, GraphScenes()[index].VisualY + y)), graphWorkspace)!.Value;
void GraphClick(int index, RawInputModifiers modifiers = RawInputModifiers.None)
{
    var point = GraphPoint(index); graphWorkspace.MouseDown(point, MouseButton.Left, modifiers); graphWorkspace.MouseUp(point, MouseButton.Left, modifiers); Dispatcher.UIThread.RunJobs();
}
void GraphKey(Key key, RawInputModifiers modifiers = RawInputModifiers.None)
{
    graphWorkspace.KeyPress(key, modifiers, PhysicalKey.None, ""); graphWorkspace.KeyRelease(key, modifiers, PhysicalKey.None, ""); Dispatcher.UIThread.RunJobs();
}
void GraphUndo() => Press(graphWorkspace.FindControl<Button>("UndoButton")!);
void GraphRedo() => Press(graphWorkspace.FindControl<Button>("RedoButton")!);
void GraphEdit(string label)
{
    graphWorkspace.FindControl<Menu>("AppMenu")!.Items.Cast<MenuItem>().Single(m => Equals(m.Header, "_Edit")).Items.OfType<MenuItem>().Single(m => Equals(m.Header, label)).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Dispatcher.UIThread.RunJobs();
}
bool GraphSelected(params int[] indices) => multiGraph.SelectedScenes.SequenceEqual(indices.Select(i => GraphScenes()[i]));
GraphClick(0); GraphClick(1, RawInputModifiers.Control);
Check(GraphSelected(0, 1) && graphWorkspace.FindControl<ListBox>("SceneList")!.SelectedItems!.Count == 2, "Ctrl-click selects a scene set and synchronizes the outline");
Check(multiGraph.Selected == GraphScenes()[1] && graphWorkspace.FindControl<TextBlock>("EditorSubtitle")!.Text!.Contains("Editing 1 of 2"), "a selected set retains one explicit active editor scene");
GraphClick(1, RawInputModifiers.Shift); Check(GraphSelected(0), "Shift-click toggles an included graph scene off");
GraphClick(1, RawInputModifiers.Meta); Check(GraphSelected(0, 1), "Cmd-click supports additive graph selection");
Check(ProjectStore.Serialize(graphWorkspace.Project) == graphBaseline && !graphWorkspace.HasUnsavedChanges && !graphWorkspace.FindControl<Button>("UndoButton")!.IsEnabled, "selection alone changes neither JSON, dirty state nor history");
var groupFrom = GraphPoint(0); var dx = GraphScenes()[1].VisualX - GraphScenes()[0].VisualX; var dy = GraphScenes()[1].VisualY - GraphScenes()[0].VisualY;
var groupZoom = multiGraph.Zoom;
graphWorkspace.MouseDown(groupFrom, MouseButton.Left); graphWorkspace.MouseMove(groupFrom + new Vector(30, 15)); graphWorkspace.MouseMove(groupFrom + new Vector(75, 45)); graphWorkspace.MouseUp(groupFrom + new Vector(75, 45), MouseButton.Left); Dispatcher.UIThread.RunJobs();
Check(Math.Abs(GraphScenes()[0].VisualX - graphStory.ListOfCharacterStates[0].VisualX - 75 / groupZoom) < .0001 && Math.Abs(GraphScenes()[1].VisualX - GraphScenes()[0].VisualX - dx) < .0001 && Math.Abs(GraphScenes()[1].VisualY - GraphScenes()[0].VisualY - dy) < .0001, "a multi-event group drag applies the zoom-correct delta and preserves relative positions");
Check(GraphScenes()[2].VisualX == graphStory.ListOfCharacterStates[2].VisualX && GraphSelected(0, 1), "group dragging leaves unselected scenes and the selected set intact");
var groupMoved = ProjectStore.Serialize(graphWorkspace.Project);
var expectedMoved = ProjectStore.Clone(graphStory); expectedMoved.ListOfCharacterStates[0].VisualX = GraphScenes()[0].VisualX; expectedMoved.ListOfCharacterStates[0].VisualY = GraphScenes()[0].VisualY; expectedMoved.ListOfCharacterStates[1].VisualX = GraphScenes()[1].VisualX; expectedMoved.ListOfCharacterStates[1].VisualY = GraphScenes()[1].VisualY;
Check(groupMoved == ProjectStore.Serialize(expectedMoved), "group movement changes only positions, preserving root/nested unknown fields, links and logic");
GraphUndo(); Check(ProjectStore.Serialize(graphWorkspace.Project) == graphBaseline && GraphSelected(0, 1) && !graphWorkspace.HasUnsavedChanges && !graphWorkspace.FindControl<Button>("UndoButton")!.IsEnabled, "one Undo restores the entire group drag and selection without an extra history step");
GraphRedo(); Check(ProjectStore.Serialize(graphWorkspace.Project) == groupMoved && GraphSelected(0, 1), "one Redo restores all moved scenes and their selection"); GraphUndo();
multiGraph.Focus(); graphWorkspace.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, ""); graphWorkspace.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, ""); graphWorkspace.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, ""); graphWorkspace.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, ""); Dispatcher.UIThread.RunJobs();
Check(GraphScenes()[0].VisualX == graphStory.ListOfCharacterStates[0].VisualX + 30 && GraphScenes()[1].VisualX == graphStory.ListOfCharacterStates[1].VisualX + 30, "held arrow-key repeats move the selected set together");
GraphUndo(); Check(ProjectStore.Serialize(graphWorkspace.Project) == graphBaseline && !graphWorkspace.FindControl<Button>("UndoButton")!.IsEnabled, "a held group nudge is one Undo step");
multiGraph.Focus(); GraphKey(Key.Down, RawInputModifiers.Shift); Check(GraphScenes()[0].VisualY == graphStory.ListOfCharacterStates[0].VisualY + 40 && GraphScenes()[1].VisualY == graphStory.ListOfCharacterStates[1].VisualY + 40, "Shift-arrow moves the group by the larger step"); GraphUndo();
multiGraph.Focus(); graphWorkspace.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, ""); GraphKey(Key.Escape); graphWorkspace.KeyRelease(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, "");
Check(ProjectStore.Serialize(graphWorkspace.Project) == graphBaseline && GraphSelected(0, 1) && !graphWorkspace.FindControl<Button>("UndoButton")!.IsEnabled, "Escape rolls back an in-progress keyboard move without clearing selection or creating history");
multiGraph.Fit(); groupFrom = GraphPoint(0); graphWorkspace.MouseDown(groupFrom, MouseButton.Left); graphWorkspace.MouseMove(groupFrom + new Vector(25, 15)); GraphKey(Key.Escape); graphWorkspace.MouseUp(groupFrom + new Vector(25, 15), MouseButton.Left);
Check(ProjectStore.Serialize(graphWorkspace.Project) == graphBaseline && GraphSelected(0, 1), "Escape cancels a group pointer drag and restores every position");
IPointer? groupPointer = null;
multiGraph.AddHandler(InputElement.PointerPressedEvent, (_, e) => groupPointer = e.Pointer, RoutingStrategies.Bubble, handledEventsToo: true);
groupFrom = GraphPoint(0); graphWorkspace.MouseDown(groupFrom, MouseButton.Left); graphWorkspace.MouseMove(groupFrom + new Vector(30, 15)); groupPointer!.Capture(null);
Check(graphWorkspace.HasUnsavedChanges && graphWorkspace.FindControl<Button>("UndoButton")!.IsEnabled, "actual capture loss records the moved group before another command");
graphWorkspace.MouseUp(groupFrom + new Vector(30, 15), MouseButton.Left); GraphUndo();
Check(ProjectStore.Serialize(graphWorkspace.Project) == graphBaseline && !graphWorkspace.FindControl<Button>("UndoButton")!.IsEnabled, "losing pointer capture commits a completed group movement exactly once");
multiGraph.Focus(); graphWorkspace.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, ""); graphWorkspace.FindControl<TextBox>("SearchBox")!.Focus();
Check(graphWorkspace.HasUnsavedChanges && graphWorkspace.FindControl<Button>("UndoButton")!.IsEnabled, "leaving graph focus finishes an active keyboard group move"); GraphUndo();
Check(ProjectStore.Serialize(graphWorkspace.Project) == graphBaseline && GraphSelected(0, 1), "one Undo restores a group move finished by focus loss");
multiGraph.Focus(); GraphKey(Key.A, RawInputModifiers.Control); Check(GraphSelected(0, 1, 2, 3, 4), "Ctrl+A selects every graph scene from the keyboard"); GraphKey(Key.Escape); Check(GraphSelected() && multiGraph.Selected is null, "Escape clears the graph and editor selection");
GraphKey(Key.Tab, RawInputModifiers.Control); GraphKey(Key.Enter); var keyboardFirst = multiGraph.Selected!;
GraphKey(Key.Tab, RawInputModifiers.Control); GraphKey(Key.Space);
Check(multiGraph.SelectedScenes.Count == 2 && multiGraph.SelectedScenes.Contains(keyboardFirst), "Ctrl+Tab and Space build a graph selection without the mouse");
GraphKey(Key.Space); Check(multiGraph.SelectedScenes.Count == 1 && multiGraph.Selected == keyboardFirst, "Space toggles the keyboard-focused scene off while retaining the active editor");
GraphKey(Key.Tab, RawInputModifiers.Control | RawInputModifiers.Shift); GraphKey(Key.Enter); Check(multiGraph.Selected == keyboardFirst, "Ctrl+Shift+Tab navigates backwards and Enter selects only that scene");
GraphKey(Key.Escape); multiGraph.Fit();
var marqueeA = multiGraph.TranslatePoint(multiGraph.WorldToViewport(new Point(-20, -20)), graphWorkspace)!.Value;
var marqueeB = multiGraph.TranslatePoint(multiGraph.WorldToViewport(new Point(660, 265)), graphWorkspace)!.Value;
graphWorkspace.MouseDown(marqueeA, MouseButton.Left, RawInputModifiers.Shift); graphWorkspace.MouseMove(marqueeB, RawInputModifiers.Shift); Capture(graphWorkspace, "graph-marquee"); graphWorkspace.MouseUp(marqueeB, MouseButton.Left, RawInputModifiers.Shift); Dispatcher.UIThread.RunJobs();
Check(GraphSelected(0, 1), "Shift-drag selects intersecting scenes using world coordinates");
var panFrom = multiGraph.TranslatePoint(new Point(multiGraph.Bounds.Width - 20, multiGraph.Bounds.Height / 2), graphWorkspace)!.Value;
graphWorkspace.MouseDown(panFrom, MouseButton.Left); graphWorkspace.MouseMove(panFrom + new Vector(-15, 10)); graphWorkspace.MouseUp(panFrom + new Vector(-15, 10), MouseButton.Left);
Check(ProjectStore.Serialize(graphWorkspace.Project) == graphBaseline && GraphSelected(0, 1) && !graphWorkspace.HasUnsavedChanges, "background panning preserves selected scenes and creates no document edit");
multiGraph.Fit(); GraphClick(2); Check(GraphSelected(2), "an unmodified click on another scene replaces the graph set");
var graphOutline = graphWorkspace.FindControl<ListBox>("SceneList")!; graphOutline.SelectedItems!.Clear(); graphOutline.SelectedItems!.Add(graphOutline.Items[0]); graphOutline.SelectedItems!.Add(graphOutline.Items[1]); Dispatcher.UIThread.RunJobs();
Check(GraphSelected(0, 1), "multiple outline selections synchronize the graph set");
var outlineItem = graphOutline.ContainerFromIndex(0)!; var outlineClick = outlineItem.TranslatePoint(new Point(35, 20), graphWorkspace)!.Value;
graphWorkspace.MouseDown(outlineClick, MouseButton.Left); graphWorkspace.MouseUp(outlineClick, MouseButton.Left); Dispatcher.UIThread.RunJobs();
Check(GraphSelected(0), "a plain outline click replaces a multiple selection");
GraphKey(Key.Down, RawInputModifiers.Shift); GraphKey(Key.Down, RawInputModifiers.Shift);
Check(GraphSelected(0, 1, 2) && multiGraph.Selected == GraphScenes()[2], "native outline Shift-arrow selection retains its anchor across successive keys");
graphOutline.Focus(); GraphKey(Key.A, RawInputModifiers.Control); Check(multiGraph.SelectedScenes.Count == 5, "outline Ctrl+A selects every visible scene with native keyboard input");
multiGraph.Focus(); GraphKey(Key.Escape); multiGraph.Fit(); GraphClick(0); GraphClick(1, RawInputModifiers.Control);
graphWorkspace.FindControl<TextBox>("SearchBox")!.Text = "fog"; Dispatcher.UIThread.RunJobs();
Check(GraphSelected(0, 1), "outline filtering preserves the graph selection, including hidden scenes");
Press(graphWorkspace.FindControl<Button>("ClearFiltersButton")!); multiGraph.Focus(); GraphKey(Key.C, RawInputModifiers.Control);
Check(ProjectStore.Serialize(graphWorkspace.Project) == graphBaseline && !graphWorkspace.HasUnsavedChanges, "Ctrl+C copies the current graph set without changing history");
GraphEdit("Copy scenes…"); var graphCopyDialog = Modal(graphWorkspace);
Check(NamedControl<ListBox>(graphCopyDialog, "CopySceneList").SelectedItems!.Count == 2, "the existing copy dialog preselects the complete graph selection"); PressAnswer(graphCopyDialog, "Cancel");
multiGraph.Focus(); GraphKey(Key.V, RawInputModifiers.Control); var graphPasted = GraphScenes().TakeLast(2).ToList();
Check(GraphScenes().Count == 7 && multiGraph.SelectedScenes.SequenceEqual(graphPasted) && graphPasted[0].Choices[0].NextStateId == graphPasted[1].State, "Ctrl+V pastes and selects the complete graph bundle with internal links remapped");
Check(graphPasted[1].VisualX - graphPasted[0].VisualX == dx && graphPasted[1].VisualY - graphPasted[0].VisualY == dy && graphPasted[0].Extra!.ContainsKey("FutureScene") && graphPasted[0].Choices[0].Extra!.ContainsKey("FutureChoice"), "graph-set paste preserves relative positions and nested unknown data");
GraphUndo(); Check(ProjectStore.Serialize(graphWorkspace.Project) == graphBaseline && !graphWorkspace.HasUnsavedChanges, "graph-set paste remains one undo step");
multiGraph.Fit(); GraphClick(0); GraphEdit("Copy selected scenes"); GraphEdit("Paste scenes"); Check(GraphScenes().Count == 6 && multiGraph.SelectedScenes.Count == 1, "Edit menu copies the selected set directly"); GraphUndo();
multiGraph.Fit(); GraphClick(0);
var originalPortTarget = GraphScenes()[0].Choices[0].NextStateId;
GraphClick(1, RawInputModifiers.Control);
var groupPort = GraphPoint(0, GraphGeometry.NodeWidth, 135); var groupDestination = GraphPoint(4);
graphWorkspace.MouseDown(groupPort, MouseButton.Left); graphWorkspace.MouseMove(groupDestination); graphWorkspace.MouseUp(groupDestination, MouseButton.Left); Dispatcher.UIThread.RunJobs();
Check(GraphScenes()[0].Choices[0].NextStateId == GraphScenes()[4].State && GraphSelected(0, 1) && GraphScenes()[0].VisualX == graphStory.ListOfCharacterStates[0].VisualX && GraphScenes()[1].VisualX == graphStory.ListOfCharacterStates[1].VisualX, "dragging one selected output edits only that connection without moving the group");
GraphUndo(); Check(GraphScenes()[0].Choices[0].NextStateId == originalPortTarget && ProjectStore.Serialize(graphWorkspace.Project) == graphBaseline && GraphSelected(0, 1), "one Undo restores a connection made from a selected set");
multiGraph.Focus(); GraphKey(Key.Escape); multiGraph.Fit(); GraphClick(0);
Check(multiGraph.ConnectionHighlight(GraphScenes()[0], GraphScenes()[1]) == "GraphSelection" && multiGraph.ConnectionHighlight(GraphScenes()[1], GraphScenes()[0]) == "GraphIncoming" && multiGraph.ConnectionHighlight(GraphScenes()[1], GraphScenes()[2]) is null, "connection emphasis distinguishes outgoing, incoming and unrelated links");
GraphClick(1, RawInputModifiers.Control); Check(multiGraph.ConnectionHighlight(GraphScenes()[1], GraphScenes()[0]) == "GraphSelection", "connections internal to a selected set use the outgoing highlight");
using (var graphWeb = JsonDocument.Parse(ProjectStore.WebExport(graphWorkspace.Project)))
    Check(graphWeb.RootElement.EnumerateObject().Count() == 1 && graphWeb.RootElement.GetProperty("ListOfCharacterStates")[0].GetProperty("Choices")[1].GetProperty("Visibility").ValueKind == JsonValueKind.Number && graphWeb.RootElement.GetProperty("ListOfCharacterStates")[0].GetProperty("FutureScene").GetString() == "keep", "graph selection introduces no JSON fields and preserves numeric website visibility and extensions");
foreach (var theme in new[] { "Dark", "Light", "High contrast" })
    foreach (var scale in new[] { 1.0, 1.15, 1.3 })
    {
        graphWorkspace.ChangeAppearance(theme, scale); graphWorkspace.Width = 1540 * scale; graphWorkspace.Height = 940 * scale; Dispatcher.UIThread.RunJobs(); multiGraph.Fit();
        var renderName = "graph-selection-" + theme.ToLowerInvariant().Replace(' ', '-') + "-" + (int)Math.Round(scale * 100);
        Capture(graphWorkspace, renderName);
        Check(GraphSelected(0, 1) && graphWorkspace.FindControl<TextBlock>("GraphSelectionSummary")!.Text == "2 scenes selected" && AutomationProperties.GetLiveSetting(graphWorkspace.FindControl<TextBlock>("GraphSelectionSummary")!) == AutomationLiveSetting.Polite, "appearance retains selection and its accessible count at " + theme + " " + scale);
        graphWorkspace.Width = 1000 * scale; graphWorkspace.Height = 680 * scale; Dispatcher.UIThread.RunJobs(); multiGraph.Fit(); Capture(graphWorkspace, renderName + "-compact");
        var hint = graphWorkspace.FindControl<Border>("GraphHint")!;
        Check(hint.Bounds.Right <= multiGraph.Bounds.Width && multiGraph.Bounds.Width > 250 && graphWorkspace.FindControl<ListBox>("SceneList")!.Bounds.Height > 30, "compact graph keeps selection guidance and outline usable at " + theme + " " + scale);
    }
graphWorkspace.ChangeAppearance("Dark"); graphWorkspace.Width = 1540; graphWorkspace.Height = 940; Dispatcher.UIThread.RunJobs(); multiGraph.Fit();
var graphText = graphWorkspace.FindControl<StackPanel>("EditorPanel")!.GetVisualDescendants().OfType<TextBox>().First(); graphText.Text = "Typing before a group move";
multiGraph.Focus(); GraphKey(Key.Right); GraphUndo(); Check(GraphScenes()[1].Title == "Typing before a group move" && GraphSelected(0, 1), "undoing a group move preserves typing committed before the gesture");
GraphUndo(); Check(ProjectStore.Serialize(graphWorkspace.Project) == graphBaseline, "typing before a group gesture retains its own Undo step");
multiGraph.Focus(); GraphKey(Key.Escape); GraphKey(Key.Right); Check(ProjectStore.Serialize(graphWorkspace.Project) == graphBaseline && !graphWorkspace.FindControl<Button>("UndoButton")!.IsEnabled, "empty selection ignores group movement without adding history");
GraphKey(Key.A, RawInputModifiers.Meta); Check(GraphSelected(0, 1, 2, 3, 4), "Cmd+A supports select-all with the same graph behavior");
graphWorkspace.FindControl<TextBox>("SearchBox")!.Focus(); graphWorkspace.FindControl<TextBox>("SearchBox")!.Text = "keeper"; GraphKey(Key.A, RawInputModifiers.Control);
Check(GraphSelected(0, 1, 2, 3, 4) && graphWorkspace.FindControl<TextBox>("SearchBox")!.SelectionEnd == 6, "text-input select-all retains its native behavior without changing graph selection");
graphWorkspace.FindControl<TextBox>("SearchBox")!.Text = "";
Pump(graphWorkspace.OpenProjectAsync(otherAuthoringPath)); Check(multiGraph.SelectedScenes.Count == 1 && multiGraph.Selected == graphWorkspace.Project.ListOfCharacterStates[0], "opening another project replaces the previous graph set with its entry scene");
var duplicateGraph = ProjectStore.Clone(graphStory); duplicateGraph.ListOfCharacterStates[4].State = duplicateGraph.ListOfCharacterStates[1].State;
var duplicateGraphPath = Path.Combine(workflowRoot, "duplicate-graph.dialogue.json"); ProjectStore.AtomicWrite(duplicateGraphPath, ProjectStore.Serialize(duplicateGraph)); Pump(graphWorkspace.OpenProjectAsync(duplicateGraphPath));
multiGraph.Fit(); GraphClick(1); GraphClick(4, RawInputModifiers.Control); GraphKey(Key.Right);
Check(GraphSelected(1, 4) && GraphScenes()[1].VisualX == duplicateGraph.ListOfCharacterStates[1].VisualX + 10 && GraphScenes()[4].VisualX == duplicateGraph.ListOfCharacterStates[4].VisualX + 10, "repairable duplicate-ID scenes can be selected and moved as distinct graph nodes");
GraphUndo(); Check(ProjectStore.Serialize(graphWorkspace.Project) == ProjectStore.Serialize(duplicateGraph) && GraphSelected(1, 4) && multiGraph.Selected == GraphScenes()[4], "Undo rebinds a duplicate-ID selection by occurrence and preserves its active scene");
graphWorkspace.Close();

var picker = new TestFilePicker(); var workspace = new MainWindow(null, true, settingsRoot, picker); workspace.Show(); Dispatcher.UIThread.RunJobs();
var welcome = workspace.FindControl<WelcomeView>("Welcome")!;
Pump(workspace.InitialiseWorkspaceAsync());
Check(!workspace.HasProject, "a fresh persisted workspace starts at welcome");
Press(welcome.FindControl<Button>("ExampleButton")!);
Check(workspace.HasProject && store.Load().RecentProjects.Count == 0, "exploring the bundled example does not add it to recent projects");
Pump(workspace.ReturnHomeAsync());
workspace.KeyPress(Key.N, RawInputModifiers.Control, PhysicalKey.N, "n"); workspace.KeyRelease(Key.N, RawInputModifiers.Control, PhysicalKey.N, "n"); Dispatcher.UIThread.RunJobs();
var newDialog = Modal(workspace); Check(newDialog.GetVisualDescendants().OfType<TextBox>().SingleOrDefault(t => t.Name == "NewProjectTitle") is not null, "Ctrl+N opens the named project form from welcome");
newDialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, ""); Dispatcher.UIThread.RunJobs();
Check(!workspace.HasProject && !workspace.OwnedWindows.Any(w => w.IsVisible), "Escape cancels project creation without changing the workspace");
Press(welcome.FindControl<Button>("NewProjectButton")!); newDialog = Modal(workspace);
newDialog.GetVisualDescendants().OfType<TextBox>().SingleOrDefault(t => t.Name == "NewProjectTitle")!.Text = ""; Answer(workspace, "Create project");
Check(!workspace.HasProject && newDialog.IsVisible, "a blank project title keeps the form open for correction");
newDialog.GetVisualDescendants().OfType<TextBox>().SingleOrDefault(t => t.Name == "NewProjectTitle")!.Text = "My first story"; newDialog.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "NewProjectCharacter")!.Text = "captain"; Answer(workspace, "Create project");
Check(workspace.HasProject && workspace.HasUnsavedChanges && workspace.Project.ProjectName == "My first story" && workspace.Project.CharacterId == "captain" && workspace.Project.ListOfCharacterStates.Single().Text == "", "new projects have the requested identity and a genuinely blank opening scene");
picker.SavePaths.Enqueue(null); var home = workspace.ReturnHomeAsync(); Answer(workspace, "Save changes"); Pump(home);
Check(!home.Result && workspace.HasProject && workspace.HasUnsavedChanges, "canceling Save as also cancels leaving an unsaved project");
home = workspace.ReturnHomeAsync(); Answer(workspace, "Cancel"); Pump(home);
Check(!home.Result && workspace.Project.ProjectName == "My first story", "canceling the unsaved-changes dialog keeps the current story");
home = workspace.ReturnHomeAsync(); Answer(workspace, "Discard changes"); Pump(home);
Check(home.Result && !workspace.HasProject && welcome.IsVisible, "discard returns to welcome without creating a project file");
picker.OpenPaths.Enqueue(sourceFile); Press(welcome.FindControl<Button>("OpenProjectButton")!);
Check(workspace.HasProject && !workspace.HasUnsavedChanges && workspace.CurrentFilePath == sourceFile && workspace.Project.ListOfCharacterStates.Count == 5, "Browse files opens website-format JSON through the storage boundary");
var contentInputs = workspace.FindControl<StackPanel>("EditorPanel")!.GetVisualDescendants().OfType<TextBox>().ToList();
contentInputs.First().Text = "My opening";
contentInputs.Single(t => AutomationProperties.GetName(t) == "Dialogue text").Text = "The door is open.\nWill you enter?";
picker.SavePaths.Enqueue(null); var save = workspace.SaveProjectAsync(true); Pump(save);
Check(!save.Result && workspace.CurrentFilePath == sourceFile && workspace.HasUnsavedChanges && File.ReadAllText(sourceFile) == sourceJson, "canceled Save as keeps the original location, file and pending edits");
picker.SavePaths.Enqueue(savedFile); save = workspace.SaveProjectAsync(true); Pump(save);
var savedProject = ProjectStore.Load(savedFile);
Check(save.Result && !workspace.HasUnsavedChanges && workspace.CurrentFilePath == savedFile && savedProject.ListOfCharacterStates[0].Title == "My opening" && savedProject.ListOfCharacterStates[0].Text.StartsWith("The door"), "Save as writes edited content and marks the new file as saved");
Check(File.ReadAllText(sourceFile) == sourceJson && savedProject.ListOfCharacterStates[0].Choices[1].Requirements.Count == 1, "Save as preserves the original website file and branching conditions");
var badFile = Path.Combine(workflowRoot, "invalid.json"); File.WriteAllText(badFile, "{}"); var beforeBadOpen = ProjectStore.Serialize(workspace.Project);
var openBad = workspace.OpenProjectAsync(badFile); Answer(workspace, "Close"); Pump(openBad);
Check(!openBad.Result && workspace.CurrentFilePath == savedFile && ProjectStore.Serialize(workspace.Project) == beforeBadOpen, "invalid imports show an error and preserve the open story");
File.WriteAllText(badFile, """{"FormatVersion":999,"ListOfCharacterStates":[]}""");
openBad = workspace.OpenProjectAsync(badFile); Answer(workspace, "Close"); Pump(openBad);
Check(!openBad.Result && workspace.CurrentFilePath == savedFile && ProjectStore.Serialize(workspace.Project) == beforeBadOpen, "unsupported project versions preserve the currently open story");
Pump(workspace.ReturnHomeAsync());
Check(welcome.FindControl<StackPanel>("RecentCards")!.GetVisualDescendants().OfType<Button>().Count(b => b.Tag is string) == 2, "welcome lists the saved and imported projects with their titles");
Capture(workspace, "welcome-recent");
Press(welcome.FindControl<StackPanel>("RecentCards")!.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Tag, savedFile)));
Check(workspace.CurrentFilePath == savedFile && workspace.Project.ListOfCharacterStates[0].Title == "My opening", "opening a recent project reloads the saved content");
workspace.Project.ProjectName = "Saved on Home"; picker.SavePaths.Enqueue(savedFile); home = workspace.ReturnHomeAsync(); Answer(workspace, "Save changes"); Pump(home);
Check(home.Result && !workspace.HasProject && ProjectStore.Load(savedFile).ProjectName == "Saved on Home", "Save changes completes before returning to welcome");
welcome.FindControl<CheckBox>("ReopenLastCheckBox")!.IsChecked = true; Dispatcher.UIThread.RunJobs();
Check(store.Load().ReopenLastProjectOnStartup, "the welcome reopen preference persists immediately"); workspace.Close();
var reopened = new MainWindow(null, true, settingsRoot, new TestFilePicker()); reopened.Show(); Pump(reopened.InitialiseWorkspaceAsync());
Check(reopened.HasProject && reopened.CurrentFilePath == savedFile && reopened.Project.ProjectName == "Saved on Home", "a new session reopens the last saved project when enabled"); reopened.Close();
prefs = store.Load(); WorkspaceStore.Remember(prefs, missingFile, "Moved story"); store.Save(prefs);
var missing = new MainWindow(null, true, settingsRoot, new TestFilePicker()); missing.Show(); Pump(missing.InitialiseWorkspaceAsync());
var missingWelcome = missing.FindControl<WelcomeView>("Welcome")!;
Check(!missing.HasProject && missingWelcome.FindControl<Border>("NoticeBorder")!.IsVisible && !missingWelcome.FindControl<StackPanel>("RecentCards")!.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Tag, missingFile)).IsEnabled, "missing files are explained and unavailable recent entries are disabled");
missing.Close();
var restoreId = Guid.NewGuid().ToString("N"); var restoreStore = new RecoveryStore(Path.Combine(settingsRoot, "Recovery"));
var recovered = Templates.Harbour(); recovered.ProjectName = "Recovered work"; restoreStore.Save(restoreId, new RecoveryRecord(null, DateTimeOffset.Now, recovered));
var damagedStartupId = Guid.NewGuid().ToString("N"); restoreStore.Save(damagedStartupId, new RecoveryRecord(null, DateTimeOffset.Now, semanticDamage));
prefs = store.Load(); prefs.LastProjectPath = savedFile; store.Save(prefs);
picker = new TestFilePicker(); var recoverWindow = new MainWindow(null, true, settingsRoot, picker); recoverWindow.Show(); Pump(recoverWindow.InitialiseWorkspaceAsync());
var recoveryWelcome = recoverWindow.FindControl<WelcomeView>("Welcome")!;
Check(!recoverWindow.HasProject && recoveryWelcome.FindControl<StackPanel>("RecoverySection")!.IsVisible, "unsaved recovery is presented before automatically reopening a saved project"); Capture(recoverWindow,"welcome-recovery");
Check(File.Exists(Path.Combine(settingsRoot, "Recovery", damagedStartupId + ".json")) && restoreStore.FindUnsaved().Count == 1, "Welcome starts with damaged recovery and still offers valid unsaved work");
Press(recoveryWelcome.FindControl<StackPanel>("RecoveryCards")!.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Content, "Restore story")));
Check(recoverWindow.Project.ProjectName == "Recovered work" && recoverWindow.HasUnsavedChanges && restoreStore.FindUnsaved().Count == 1 && ProjectStore.Load(savedFile).ProjectName == "Saved on Home", "restoring opens unsaved work without overwriting or removing its recovery");
picker.SavePaths.Enqueue(Path.Combine(workflowRoot, "recovered.dialogue.json")); Pump(recoverWindow.SaveProjectAsync());
Check(restoreStore.FindUnsaved().Count == 0 && !recoverWindow.HasUnsavedChanges, "successful saving clears the restored source recovery"); recoverWindow.Close();

var runtimePicker = new TestFilePicker();
var runtimeWindow = new MainWindow(null, true, settingsRoot, runtimePicker);
runtimeWindow.Show();
Pump(runtimeWindow.LoadExampleAsync());
var fileMenu = runtimeWindow.FindControl<Menu>("AppMenu")!.Items.OfType<MenuItem>().First(m => (string?)m.Header == "_File");
Check(fileMenu.ItemsSource!.OfType<MenuItem>().Any(m => (string?)m.Header == "Export runtime companion…"), "File menu includes Export runtime companion action");

var exportTask = runtimeWindow.ExportRuntimeCompanionAsync();
var exportDialog = Modal(runtimeWindow);
var seedInput = NamedControl<NumericUpDown>(exportDialog, "ExportRuntimeSeed");
var maxOpsInput = NamedControl<NumericUpDown>(exportDialog, "ExportRuntimeMaxOps");
var exportSummary = NamedControl<TextBlock>(exportDialog, "ExportRuntimeSummary");
var confirmBtn = NamedControl<Button>(exportDialog, "ConfirmExport");
var cancelBtn = NamedControl<Button>(exportDialog, "CancelExport");

Check(seedInput is not null && maxOpsInput is not null && exportSummary is not null && confirmBtn is not null && cancelBtn is not null, "runtime companion dialog controls exist");
Check(AutomationProperties.GetName(seedInput!) == "Random seed" && AutomationProperties.GetName(maxOpsInput!) == "Maximum operations", "runtime companion controls have accessibility names");
Check(confirmBtn!.IsEnabled && exportSummary!.Text!.Contains(runtimeWindow.Project.Entry), "runtime companion dialog shows starting scene and enables export");
Capture(exportDialog, "export-runtime-companion");

seedInput!.Value = 42;
var runtimeOut = Path.Combine(workflowRoot, "harbour.runtime.json");
runtimePicker.SavePaths.Enqueue(runtimeOut);
Press(confirmBtn!);
Pump(exportTask);

Check(File.Exists(runtimeOut), "runtime companion export creates file on disk");
var parsedCompanion = JsonSerializer.Deserialize<RuntimeHandoff>(File.ReadAllText(runtimeOut), ProjectStore.Options)!;
Check(parsedCompanion.Seed == 42 && parsedCompanion.Contract == RuntimeExport.Contract && parsedCompanion.StartingScene == runtimeWindow.Project.Entry, "exported runtime companion contains seed 42 and valid contract");

var beforeState = ProjectStore.Serialize(runtimeWindow.Project);
runtimeWindow.Project.InitialVariables["bad_var"] = 5;
var invalidTask = runtimeWindow.ExportRuntimeCompanionAsync();
var invalidDialog = Modal(runtimeWindow);
var invalidSummary = NamedControl<TextBlock>(invalidDialog, "ExportRuntimeSummary");
var invalidConfirm = NamedControl<Button>(invalidDialog, "ConfirmExport");

Check(invalidConfirm is not null && !invalidConfirm.IsEnabled && invalidSummary.Text?.Contains("bad_var") == true, "invalid runtime variable ID disables ConfirmExport and shows repair guidance");
invalidDialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");
Dispatcher.UIThread.RunJobs();
Pump(invalidTask);
runtimeWindow.Project.InitialVariables.Remove("bad_var");
Check(ProjectStore.Serialize(runtimeWindow.Project) == beforeState && !runtimeWindow.HasUnsavedChanges, "malformed runtime variable check does not mutate history or crash");
runtimeWindow.Close();
Console.WriteLine($"SUCCESS: {passed} checks. Screenshots: {output}");
}
catch (Exception ex)
{
    Console.Error.WriteLine("TEST FAILED: " + ex);
    Environment.ExitCode = 1;
}

sealed class TestFilePicker : IProjectFilePicker
{
    public Queue<string?> OpenPaths { get; } = new();
    public Queue<string?> SavePaths { get; } = new();
    public Task<string?> OpenAsync(Window owner) => Task.FromResult(OpenPaths.Count > 0 ? OpenPaths.Dequeue() : null);
    public Task<string?> SaveAsync(Window owner, string suggestedName) => Task.FromResult(SavePaths.Count > 0 ? SavePaths.Dequeue() : null);
    public Task<string?> ExportAsync(Window owner, string title, string suggestedName, string extension) => Task.FromResult(SavePaths.Count > 0 ? SavePaths.Dequeue() : null);
}
