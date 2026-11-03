using DialogueMachine.Core;
using System.Text.Json;

internal static class FlagChecks
{
    public static void Core(Action<bool, string> Check, Action<Action, string> Throws)
    {
        var door = new DialogueState { State = "door", Text = "[if aff:open == 1]Open[else]Shut[/if]" };
        door.Choices = [new Choice { ButtonText = "Go", NextStateId = "next", AfterChoiceActions = [new() { Type = "Set", TargetId = "open", Amount = 1 }, new() { Type = "Toggle", TargetId = "lit" }, new() { Type = "Affinity", TargetId = "trust", Amount = 2 }] }];
        door.Choices[0].AfterChoiceActions[0].Extra = new() { ["FutureAction"] = JsonSerializer.SerializeToElement(42) };
        door.Choices[0].Requirements = [new() { Type = "Affinity", TargetId = "open", Operator = "==", Value = "0" }];
        var next = new DialogueState { State = "next", Text = "After", DefaultAfterActions = [new() { Type = "Toggle", TargetId = "open" }] };
        var project = new DialogueProject { ListOfCharacterStates = [door, next], EntryStateId = "door" };

        Check(DialogueEngine.Validate(project).All(d => d.Severity != "Error"), "Set and Toggle are valid action types");
        Check(VariableStore.DiscoverFlags(project).SequenceEqual(["lit", "open"]) && DialogueEngine.DiscoverVariables(project).Contains("aff:lit"), "flags are discovered from Set and Toggle targets as affinity variables");
        Check(VariableStore.IsFlagCondition(door.Choices[0].Requirements[0], ["open"]) && !VariableStore.IsFlagCondition(new Requirement { TargetId = "open", Operator = ">=", Value = "1" }, ["open"]), "flag conditions are affinity ==/!= 0 or 1 on a known flag");

        var sim = new Simulation(project);
        sim.Choose(sim.Current!.Choices[0]);
        Check(sim.Variables["aff:open"] == 1 && sim.Variables["aff:lit"] == 1 && sim.Variables["aff:trust"] == 2, "Set writes its value and Toggle flips 0 to 1 alongside normal actions");
        sim.Continue();
        Check(sim.Variables["aff:open"] == 0 && sim.Ending is not null, "Toggle flips 1 back to 0 and scene exit actions run");
        sim.Back();
        Check(sim.Variables["aff:open"] == 1, "Back restores flag values");
        var onTwo = new Simulation(project, numericOverrides: new Dictionary<string, int> { ["aff:lit"] = 5 });
        onTwo.Choose(onTwo.Current!.Choices[0]);
        Check(onTwo.Variables["aff:lit"] == 0, "Toggle turns any non-zero value off");

        var story = ProjectStore.StoryExport(project);
        Check(story.Contains("\"Set\"") && story.Contains("\"Toggle\"") && story.Contains("\"Affinity\""), "story export preserves Set and Toggle actions for engines");
        Check(project.ListOfCharacterStates[0].Choices[0].AfterChoiceActions.Count == 3, "story export leaves the open project unchanged");
        var round = ProjectStore.Parse(ProjectStore.Serialize(project));
        Check(round.ListOfCharacterStates[0].Choices[0].AfterChoiceActions[0].Type == "Set" && round.ListOfCharacterStates[0].Choices[0].AfterChoiceActions[0].Extra!.ContainsKey("FutureAction"), "Set actions and their unknown fields round-trip");

        VariableStore.Rename(project, "aff:open", "aff:unlocked");
        Check(door.Choices[0].AfterChoiceActions[0].TargetId == "unlocked" && next.DefaultAfterActions[0].TargetId == "unlocked" && door.Choices[0].Requirements[0].TargetId == "unlocked" && door.Text.Contains("aff:unlocked"), "renaming a flag variable updates its Set/Toggle actions, conditions and text");

        door.Metadata["portrait"] = "mara_smile"; next.Metadata["portrait"] = "mara_stern";
        Check(VariableStore.DiscoverPortraits(project).SequenceEqual(["mara_smile", "mara_stern"]), "portrait discovery lists distinct sorted non-empty portrait IDs");
        door.DefaultAfterActions.Add(new() { Type = "Set", TargetId = "moral_track", Amount = 2 });
        Check(!VariableStore.DiscoverFlags(project).Contains("moral_track"), "non-binary Set actions are not misclassified as boolean flags");
    }
}
