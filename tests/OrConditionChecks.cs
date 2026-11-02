using DialogueMachine.Core;
using System.Text.Json;

internal static class OrConditionChecks
{
    public static void Core(Action<bool, string> Check, Action<Action, string> Throws)
    {
        var scene = new DialogueState { State = "gate", Text = "Gate" };
        var choice = new Choice
        {
            ButtonText = "Enter", Visibility = VisibilityMode.Hide, RequirementMatch = RequirementMatch.Any,
            Requirements = [new() { Type = "Inventory", TargetId = "key", Operator = ">=", Value = "1" }, new() { TargetId = "trust", Operator = ">=", Value = "2" }],
            AfterChoiceActions = [new() { TargetId = "trust", Amount = 1 }]
        };
        choice.Requirements[1].Extra = new() { ["FutureRequirement"] = JsonSerializer.SerializeToElement(7) };
        scene.Choices = [choice];
        var project = new DialogueProject { ListOfCharacterStates = [scene], EntryStateId = "gate" };

        var none = new Dictionary<string, int>(); var key = new Dictionary<string, int> { ["inv:key"] = 1 }; var trust = new Dictionary<string, int> { ["aff:trust"] = 2 };
        Check(!DialogueEngine.RequirementsMet(choice, none, out var reason) && reason.StartsWith("Requires inv:key"), "ANY fails when no requirement passes and reports the first reason");
        Check(DialogueEngine.RequirementsMet(choice, key, out _) && DialogueEngine.RequirementsMet(choice, trust, out _), "ANY passes when either requirement passes");
        choice.RequirementMatch = RequirementMatch.All;
        Check(!DialogueEngine.RequirementsMet(choice, key, out _) && DialogueEngine.RequirementsMet(choice, new Dictionary<string, int> { ["inv:key"] = 1, ["aff:trust"] = 2 }, out _), "ALL still needs every requirement");
        choice.RequirementMatch = RequirementMatch.Any; choice.Requirements.Clear();
        Check(DialogueEngine.RequirementsMet(choice, none, out _), "ANY with no requirements is available");
        choice.Requirements = [new() { Type = "Inventory", TargetId = "key", Operator = ">=", Value = "1" }, new() { TargetId = "trust", Operator = ">=", Value = "2" }];
        choice.Requirements[1].Extra = new() { ["FutureRequirement"] = JsonSerializer.SerializeToElement(7) };

        var sim = new Simulation(project, numericOverrides: key);
        Check(sim.AvailableChoices.Count == 1, "playtest offers an ANY choice when one requirement passes");

        var saved = ProjectStore.Serialize(project);
        Check(saved.Contains("\"RequirementMatch\": \"Any\""), "desktop file stores ANY");
        var round = ProjectStore.Parse(saved).ListOfCharacterStates[0].Choices[0];
        Check(round.RequirementMatch == RequirementMatch.Any && round.Requirements[1].Extra!.ContainsKey("FutureRequirement"), "ANY and requirement unknown fields round-trip");
        choice.RequirementMatch = RequirementMatch.All;
        Check(!ProjectStore.Serialize(project).Contains("RequirementMatch") && !ProjectStore.StoryExport(project).Contains("RequirementMatch"), "ALL leaves the desktop file unchanged");
        choice.RequirementMatch = RequirementMatch.Any;

        var story = ProjectStore.StoryExport(project);
        var storyChoices = ProjectStore.Parse(story).ListOfCharacterStates[0].Choices;
        Check(story.Contains("\"RequirementMatch\": \"Any\"") && storyChoices.Count == 1 && storyChoices[0].Requirements.Count == 2, "story export preserves ANY without splitting choices");
        Check(storyChoices[0].Requirements[1].Extra!.ContainsKey("FutureRequirement") && scene.Choices.Count == 1 && choice.RequirementMatch == RequirementMatch.Any, "story export keeps unknown fields and leaves the open project unchanged");

        var fingerprint = ScenarioFingerprint(scene, choice);
        choice.RequirementMatch = RequirementMatch.All;
        Check(fingerprint != ScenarioFingerprint(scene, choice), "switching ALL/ANY marks a recorded scenario step stale");
    }
    private static string ScenarioFingerprint(DialogueState scene, Choice choice) => ScenarioStore.Fingerprint(scene, choice);
}
