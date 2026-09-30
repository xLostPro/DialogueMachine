namespace DialogueMachine.Core;

public static class Templates
{
    public static DialogueProject Blank() => new() { ProjectName = "Untitled story", ListOfCharacterStates = [Scene("start", "Opening scene", "Character", "Your story begins here.", 80, 100)] };
    public static DialogueState Scene(string id, string title, string speaker, string text, double x, double y) => new()
    { State = id, Text = text, VisualX = x, VisualY = y, Metadata = new() { ["title"] = title, ["speaker"] = speaker } };
    public static DialogueProject Harbour()
    {
        var p = new DialogueProject { ProjectName = "The last lighthouse", CharacterId = "mara", EntryStateId = "arrival", InitialVariables = new() { ["aff:trust"] = 0, ["inv:coin"] = 1 }, TextVariables = new() { ["player_name"] = "Traveller", ["npc_name"] = "Mara" } };
        var arrival = Scene("arrival", "A light in the fog", "Mara", "You're late, {player_name}. The tide has already turned.\nBeyond the harbour, a single light flickers in the mist.", 70, 220);
        arrival.Metadata["notes"] = "Establish the mystery. Let the player decide how much to trust Mara.";
        arrival.Choices = [new() { ButtonText = "What happened to the lighthouse?", NextStateId = "keeper" }, new() { ButtonText = "I brought something for you.", NextStateId = "gift", Requirements = [new() { Type = "Inventory", TargetId = "coin", Operator = ">=", Value = "1" }], AfterChoiceActions = [new() { Type = "Inventory", TargetId = "coin", Amount = -1 }, new() { TargetId = "trust", Amount = 1 }] }, new() { ButtonText = "I should go.", NextStateId = "departure" }];
        var keeper = Scene("keeper", "The missing keeper", "Mara", "The keeper vanished three nights ago. No footprints. No boat. Just the light, still burning.\n[if aff:trust >= 1]I think you can help me find him.[else]I don't know if I can trust you with the rest.[/if]", 440, 55);
        keeper.Choices = [new() { ButtonText = "I'll help you find him.", NextStateId = "promise", AfterChoiceActions = [new() { TargetId = "trust", Amount = 1 }] }, new() { ButtonText = "This sounds dangerous.", NextStateId = "departure" }];
        var gift = Scene("gift", "A small kindness", "Mara", "A ferryman's coin? I haven't seen one of these since I was a child.\nThank you. Perhaps I was wrong about you.", 440, 385); gift.DefaultNextId = "keeper";
        var promise = Scene("promise", "Into the unknown", "Mara", "Then meet me at the old jetty at dawn.\nAnd whatever you do… don't follow the light alone.", 810, 70); promise.Metadata["ending"] = "true";
        var departure = Scene("departure", "The road home", "Mara", "Of course. The road is safer before dark.\nBehind you, the lighthouse goes out.", 810, 400); departure.Metadata["ending"] = "true";
        p.ListOfCharacterStates = [arrival, keeper, gift, promise, departure]; return p;
    }
}
