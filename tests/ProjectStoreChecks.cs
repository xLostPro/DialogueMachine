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


internal static class ProjectStoreChecks
{
    public static void Core(Action<bool, string> Check, Action<Action, string> Throws)
    {
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
    }
}
