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

ProjectStoreChecks.Core(Check, Throws);
VariableChecks.Core(Check, Throws);
LocalizationChecks.Core(Check, Throws);
SearchChecks.Core(Check, Throws);
NavigationChecks.Core(Check, Throws);
PlaytestChecks.Core(Check, Throws);
ProjectEditingChecks.Core(Check, Throws);
RecoveryChecks.Core(Check, Throws);
AuthoringChecks.Core(Check, Throws);
FlagChecks.Core(Check, Throws);
OrConditionChecks.Core(Check, Throws);

AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
var main = new MainWindow(null, testMode: true); main.Show(); Dispatcher.UIThread.RunJobs();
var output = args.FirstOrDefault() ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../artifacts")); Directory.CreateDirectory(output);
GraphOrganizationChecks.Core(Check, Throws, output);
ScenarioChecks.Core(Check, Throws, output);
ExportChecks.Core(Check, Throws, output);
ConflictChecks.Core(Check, Throws, output);
RuntimeChecks.Core(Check, Throws);
LiveLinkChecks.Core(Check, Throws, output);
ProjectStore.AtomicWrite(Path.Combine(output,"lighthouse.dialogue.json"), ProjectStore.Serialize(Templates.Harbour()));
void Capture(Window window, string name) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame() ?? throw new Exception("No rendered frame"); frame.Save(Path.Combine(output,name + ".png"), PngBitmapEncoderOptions.Default); }
GraphOrganizationChecks.UI(Check, output, Capture);
ExportChecks.UI(Check, output, Capture);
ConflictChecks.UI(Check, output, Capture);
EditorPerformanceRegressions.Run(Check, output, Capture);
ScenarioChecks.UI(Check, output, Capture);
LiveLinkChecks.UI(Check, output, Capture);
ShellChecks.UI(Check, Capture, main);
PlaytestChecks.UI(Check, Capture);
main.Close();
ProjectEditingChecks.UI(Check, Capture);

// Exercise file and modal workflows with real disk writes and an isolated workspace.
var workflowRoot = Path.Combine(Path.GetFullPath(output), "workflow-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(workflowRoot);
WorkspaceChecks.Preferences(Check, workflowRoot);
VariableManagerChecks.UI(Check, Capture);
SearchChecks.UI(Check, Capture, workflowRoot);
NavigationChecks.UI(Check, Capture, workflowRoot);
AuthoringChecks.UI(Check, Capture, workflowRoot);
GraphSelectionChecks.UI(Check, Capture, workflowRoot);
WorkspaceChecks.Session(Check, Capture, workflowRoot);
EngineExportChecks.UI(Check, Capture, workflowRoot);
Console.WriteLine($"SUCCESS: {passed} checks. Screenshots: {output}");
}
catch (Exception ex)
{
    Console.Error.WriteLine("TEST FAILED: " + ex);
    Environment.ExitCode = 1;
}
