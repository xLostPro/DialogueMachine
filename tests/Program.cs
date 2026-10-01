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
var passed = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); passed++; Console.WriteLine("PASS " + message); }
void Throws(Action action, string message) { try { action(); } catch { Check(true, message); return; } throw new Exception(message); }

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
var recoveryFolder = Path.Combine(AppContext.BaseDirectory, "recovery-test"); var recovery = new RecoveryStore(recoveryFolder); var recoveryId = Guid.NewGuid().ToString("N");
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
recovery.Save(recoveryId, new RecoveryRecord(temp, recoveryTime, Templates.Harbour()));
Check(recovery.FindUnsaved().Count == 1, "recovery newer than a project file is offered"); File.SetLastWriteTimeUtc(temp, recoveryTime.UtcDateTime.AddSeconds(1));
Check(recovery.FindUnsaved().Count == 0, "recovery older than a saved project is ignored"); recovery.Remove(recoveryId); File.Delete(temp);

AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
var main = new MainWindow(null, testMode: true); main.Show(); Dispatcher.UIThread.RunJobs();
var output = args.FirstOrDefault() ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../artifacts")); Directory.CreateDirectory(output);
ProjectStore.AtomicWrite(Path.Combine(output,"lighthouse.dialogue.json"), ProjectStore.Serialize(Templates.Harbour()));
void Capture(Window window, string name) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame() ?? throw new Exception("No rendered frame"); frame.Save(Path.Combine(output,name + ".png"), PngBitmapEncoderOptions.Default); }
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
Click("LogicTab"); Capture(main,"workspace-logic"); Click("ContentTab"); Click("ScriptButton"); Capture(main,"workspace-script"); Click("GraphButton");
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
}
