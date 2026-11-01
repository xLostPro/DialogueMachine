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


internal static class LocalizationChecks
{
    public static void Core(Action<bool, string> Check, Action<Action, string> Throws)
    {
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
    }
}
