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

internal static class UiHelpers
    {
        public static void Pump(Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!task.IsCompleted && DateTime.UtcNow < deadline) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
        if (!task.IsCompleted) throw new Exception("UI operation did not complete; a dialog may still be open.");
    task.GetAwaiter().GetResult(); Dispatcher.UIThread.RunJobs();
}
    public static void Press(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
    public static Window Modal(MainWindow owner) { Dispatcher.UIThread.RunJobs(); return owner.OwnedWindows.Single(w => w.IsVisible); }
    public static void Answer(MainWindow owner, string answer) => Press(Modal(owner).GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, answer)));
    public static Button NamedButton(Window owner, string name) => owner.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == name);
    public static T NamedControl<T>(Window owner, string name) where T : Control => owner.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    public static void PressAnswer(Window owner, string label) => Press(owner.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, label)));
}

internal sealed class TestFilePicker : IProjectFilePicker
{
    public Queue<string?> OpenPaths { get; } = new();
    public Queue<string?> SavePaths { get; } = new();
    public Queue<string?> FolderPaths { get; } = new();
    public Task<string?> OpenAsync(Window owner) => Task.FromResult(OpenPaths.Count > 0 ? OpenPaths.Dequeue() : null);
    public Task<string?> SaveAsync(Window owner, string suggestedName) => Task.FromResult(SavePaths.Count > 0 ? SavePaths.Dequeue() : null);
    public Task<string?> ExportAsync(Window owner, string title, string suggestedName, string extension) => Task.FromResult(SavePaths.Count > 0 ? SavePaths.Dequeue() : null);
    public Task<string?> PickFolderAsync(Window owner, string title) => Task.FromResult(FolderPaths.Count > 0 ? FolderPaths.Dequeue() : null);
}
