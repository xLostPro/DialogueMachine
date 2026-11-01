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


internal static class PlaytestChecks
{
    static DialogueProject previewStoryShared = null!, randomStoryShared = null!;

    public static void Core(Action<bool, string> Check, Action<Action, string> Throws)
    {
        var p = Templates.Harbour(); var vars = new Dictionary<string,int> { ["aff:trust"] = -2, ["inv:coin"] = 1 };
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
        previewStoryShared = previewStory; randomStoryShared = randomStory;
    }

    public static void UI(Action<bool, string> Check, Action<Window, string> Capture)
    {
        var previewStory = previewStoryShared; var randomStory = randomStoryShared;
        var playtest = new PlaytestWindow(Templates.Harbour(), null); playtest.Show(); Dispatcher.UIThread.RunJobs(); Capture(playtest,"playtest");
        Check(playtest.Session.Current!.State == "arrival", "playtest window starts at entry");
        playtest.GetVisualDescendants().OfType<Button>().First(b => b.Content is string s && s == "Continue →").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(playtest.Session.PageIndex == 1, "playtest Continue reveals choices after the final page");
        var response = playtest.GetVisualDescendants().OfType<Button>().First(b => b.Content is TextBlock t && t.Text!.Contains("I brought")); response.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(playtest.Session.Current!.State == "gift" && playtest.Session.Variables["inv:coin"] == 0, "playtest response button applies actions and follows its destination"); Capture(playtest,"playtest-choice");
        playtest.Close();
        var keyed = new PlaytestWindow(Templates.Harbour(), null); keyed.Show(); Dispatcher.UIThread.RunJobs();
        void Tap(Key k) { keyed.KeyPress(k, RawInputModifiers.None, PhysicalKey.None, null); keyed.KeyRelease(k, RawInputModifiers.None, PhysicalKey.None, null); Dispatcher.UIThread.RunJobs(); }
        Tap(Key.Enter); Check(keyed.Session.PageIndex == 1, "playtest Enter continues");
        Tap(Key.D1); Check(keyed.Session.Current!.State != "arrival", "playtest number key picks a response");
        Tap(Key.Back); Check(keyed.Session.Current!.State == "arrival", "playtest Backspace goes back");
        keyed.Close();
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
    }
}
