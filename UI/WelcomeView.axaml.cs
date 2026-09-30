using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using DialogueMachine.Core;
using System.IO;

namespace DialogueMachine.UI;

public sealed partial class WelcomeView : UserControl
{
    public Func<Task>? NewProjectRequested { get; set; }
    public Func<Task>? OpenRequested { get; set; }
    public Func<Task>? ExampleRequested { get; set; }
    public Func<string, Task>? RecentRequested { get; set; }
    public Action<string>? ForgetRequested { get; set; }
    public Func<RecoveryRecord, string, Task>? RestoreRequested { get; set; }
    public Func<Task>? AllRecoveriesRequested { get; set; }
    public Action<bool>? ReopenChanged { get; set; }
    private string? lastPath;
    private bool refreshing;
    public WelcomeView()
    {
        InitializeComponent();
        NewProjectButton.Click += async (_, _) => { if (NewProjectRequested is not null) await NewProjectRequested(); };
        OpenProjectButton.Click += async (_, _) => { if (OpenRequested is not null) await OpenRequested(); };
        ExampleButton.Click += async (_, _) => { if (ExampleRequested is not null) await ExampleRequested(); };
        ContinueButton.Click += async (_, _) => { if (lastPath is not null && RecentRequested is not null) await RecentRequested(lastPath); };
        ReopenLastCheckBox.IsCheckedChanged += (_, _) => { if (!refreshing) ReopenChanged?.Invoke(ReopenLastCheckBox.IsChecked == true); };
    }
    public void ShowNotice(string? text)
    {
        NoticeText.Text = text; NoticeBorder.IsVisible = !string.IsNullOrWhiteSpace(text);
    }
    public void Refresh(WorkspacePreferences p, List<(string Path, RecoveryRecord Record)> recoveries)
    {
        refreshing = true; ReopenLastCheckBox.IsChecked = p.ReopenLastProjectOnStartup; refreshing = false;
        lastPath = p.LastProjectPath; ContinueButton.IsVisible = lastPath is not null && File.Exists(lastPath);
        EmptyRecentText.IsVisible = p.RecentProjects.Count == 0; RecentCards.Children.Clear();
        foreach (var recent in p.RecentProjects)
        {
            var exists = File.Exists(recent.Path);
            var name = Ui.Text(recent.Name, 15); name.FontWeight = FontWeight.SemiBold;
            var path = Ui.Text(recent.Path, 12, true); path.TextWrapping = TextWrapping.NoWrap; path.TextTrimming = TextTrimming.CharacterEllipsis;
            var label = Ui.Stack(name, path);
            if (!exists) label.Children.Add(Ui.Text("File unavailable · browse to its new location", 11, true));
            var open = Ui.AsyncButton("Open " + recent.Name, async () => { if (RecentRequested is not null) await RecentRequested(recent.Path); }, "subtle");
            open.Content = label; open.HorizontalAlignment = HorizontalAlignment.Stretch; open.HorizontalContentAlignment = HorizontalAlignment.Left; open.IsEnabled = exists; open.Tag = recent.Path;
            ToolTip.SetTip(open, recent.Path);
            var remove = Ui.Button("×", () => ForgetRequested?.Invoke(recent.Path), "subtle"); AutomationProperties.SetName(remove, "Remove " + recent.Name + " from recent projects"); ToolTip.SetTip(remove, "Remove from recents. The project file is kept.");
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") }; row.Children.Add(open); Grid.SetColumn(remove, 1); row.Children.Add(remove);
            RecentCards.Children.Add(Ui.Card(row, 8));
        }
        RecoverySection.IsVisible = recoveries.Count > 0; RecoveryCards.Children.Clear();
        foreach (var (path, record) in recoveries.Take(3))
        {
            var panel = Ui.Stack(Ui.Text(record.Project.ProjectName, 16), Ui.Text("Unsaved work · " + record.SavedAt.ToLocalTime().ToString("g"), 12, true), Ui.AsyncButton("Restore story", async () => { if (RestoreRequested is not null) await RestoreRequested(record, path); }, "primary"));
            RecoveryCards.Children.Add(Ui.Card(panel, 16));
        }
        if (recoveries.Count > 3) RecoveryCards.Children.Add(Ui.AsyncButton("View all recovery saves…", async () => { if (AllRecoveriesRequested is not null) await AllRecoveriesRequested(); }, "subtle"));
    }
}
