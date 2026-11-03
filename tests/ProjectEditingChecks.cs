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


internal static class ProjectEditingChecks
{
    static DialogueProject tallStoryShared = null!;

    public static void Core(Action<bool, string> Check, Action<Action, string> Throws)
    {
        var p = Templates.Harbour(); var renamed = Templates.Harbour(); DialogueEngine.Rename(renamed, renamed.ListOfCharacterStates[0], "opening");
        Check(renamed.Entry == "opening", "renaming start scene updates entry"); DialogueEngine.Rename(renamed, renamed.ListOfCharacterStates[1], "keeper_new");
        Check(renamed.ListOfCharacterStates[0].Choices[0].NextStateId == "keeper_new" && renamed.ListOfCharacterStates[2].DefaultNextId == "keeper_new", "rename updates choice and continue connections");
        Throws(() => DialogueEngine.Rename(renamed, renamed.ListOfCharacterStates[0], "[EXIT]"), "reserved exit cannot become a scene ID");
        var hist = new ProjectHistory(); hist.Reset(p); p.ProjectName = "Edited"; hist.Commit(p);
        Check(hist.Undo()!.ProjectName == "The last lighthouse" && hist.Redo()!.ProjectName == "Edited", "history preserves complete project snapshots");
        hist.Undo(); p.ProjectName = "Branch"; hist.Commit(p); Check(!hist.CanRedo, "editing after undo clears redo branch");
        var exported = ProjectStore.StoryExport(renamed); using var web = JsonDocument.Parse(exported);
        Check(web.RootElement.EnumerateObject().Count() == 1 && web.RootElement.GetProperty("ListOfCharacterStates")[0].GetProperty("State").GetString() == "opening", "story export retains original root and starts at selected entry");
        Check(web.RootElement.GetProperty("ListOfCharacterStates")[0].GetProperty("Choices")[0].GetProperty("Visibility").ValueKind == JsonValueKind.Number, "story export uses original numeric enum format");
        var cyclic = Templates.Blank(); cyclic.ListOfCharacterStates[0].Choices = [new() { NextStateId = "start" }, new() { NextStateId = "[EXIT]" }];
        Check(!DialogueEngine.Validate(cyclic).Any(d => d.Severity == "Error"), "intentional loops with an exit are playable");
        cyclic.ListOfCharacterStates[0].DefaultNextId = "missing";
        Check(DialogueEngine.Validate(cyclic).Any(d => d.Severity == "Error"), "validate stale fallback even when scene has choices");
        var duplicate = Templates.Blank(); duplicate.ListOfCharacterStates.Add(Templates.Scene("start", "Duplicate", "A", "Hi", 0, 0));
        Check(DialogueEngine.Validate(duplicate).Any(d => d.Message.Contains("Duplicate")), "duplicate scene IDs diagnosed");
        var unreachable = Templates.Blank(); unreachable.ListOfCharacterStates.Add(Templates.Scene("unused", "Unused", "A", "Hi", 0, 0));
        Check(DialogueEngine.Validate(unreachable).Any(d => d.Message.Contains("unreachable")), "unreachable scenes diagnosed");
        unreachable.ListOfCharacterStates[1].Metadata["entry"] = "true";
        Check(!DialogueEngine.Validate(unreachable).Any(d => d.Message.Contains("unreachable")), "secondary entry points marked with Metadata[entry]=true are treated as reachable");
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
        tallStoryShared = tallStory;
    }

    public static void UI(Action<bool, string> Check, Action<Window, string> Capture)
    {
        var tallStory = tallStoryShared;
        var arrangedGraph = new GraphCanvas { Project = tallStory }; var arrangedWindow = new Window { Width = 1200, Height = 900, Content = arrangedGraph };
        arrangedWindow.Show(); Dispatcher.UIThread.RunJobs(); arrangedGraph.Fit(); Capture(arrangedWindow, "graph-tall-arranged"); arrangedWindow.Close();
    }
}
