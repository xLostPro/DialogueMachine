using Avalonia;
using Avalonia.Controls;
using DialogueMachine.Core;

namespace DialogueMachine.UI;

public sealed partial class MainWindow
{
    private bool isExample;
    private void WireWelcome()
    {
        Welcome.NewProjectRequested = NewProject;
        Welcome.OpenRequested = Open;
        Welcome.ExampleRequested = LoadExampleAsync;
        Welcome.RecentRequested = path => OpenProjectAsync(path);
        Welcome.ForgetRequested = path => { WorkspaceStore.Forget(preferences, path); WritePreferences(); BuildMenu(); RefreshWelcome(); };
        Welcome.RestoreRequested = async (record, path) => { if (await ConfirmReplace()) Restore(record, path); };
        Welcome.AllRecoveriesRequested = ShowRecoveries;
        Welcome.ReopenChanged = value => { preferences.ReopenLastProjectOnStartup = value; WritePreferences(); };
    }
    private void RefreshWelcome()
    {
        Welcome.Refresh(preferences, persistWorkspace ? Recoveries() : []);
    }
    public async Task InitialiseWorkspaceAsync(string? explicitPath = null)
    {
        if (startupInitialised || hasProject) return;
        startupInitialised = true;
        var recoveries = persistWorkspace ? Recoveries() : [];
        var plan = WorkspaceStartup.Plan(preferences, explicitPath, recoveries.Count > 0);
        Welcome.Refresh(preferences, recoveries); Welcome.ShowNotice(plan.Notice);
        if (plan.ProjectPath is not null) await OpenProjectAsync(plan.ProjectPath, showErrorDialog: false);
    }
    public async Task<bool> ReturnHomeAsync()
    {
        if (!await ConfirmReplace()) return false;
        editTimer.Stop(); hasProject = false; isExample = false;
        project = new(); selected = null; filePath = null; history.Reset(project); savedSnapshot = ProjectStore.Serialize(project);
        C<Grid>("WorkspacePanel").IsVisible = false; Welcome.IsVisible = true; Welcome.ShowNotice(null);
        Refresh(); BuildMenu(); RefreshWelcome();
        Welcome.FindControl<Button>("NewProjectButton")!.Focus(); return true;
    }
    public async Task LoadExampleAsync()
    {
        if (!await ConfirmReplace()) return;
        LoadProject(Templates.Harbour(), null, false); isExample = true; UpdateStatus();
    }
    private async Task NewProject()
    {
        var title = new TextBox { Name = "NewProjectTitle", Text = "Untitled story" };
        var character = new TextBox { Name = "NewProjectCharacter", Text = "new_character" };
        var error = Ui.Text("", 12, true); Window? window = null;
        var cancel = Ui.Button("Cancel", () => window!.Close(false));
        var create = Ui.Button("Create project", () =>
        {
            if (string.IsNullOrWhiteSpace(title.Text)) { error.Text = "Give your project a title before creating it."; title.Focus(); return; }
            window!.Close(true);
        }, "primary"); create.IsDefault = true;
        var content = Ui.Stack(Ui.Text("Start your next story", 25), Ui.Text("Give your project a name. You can change these details later.", 13, true), Ui.Field("Project title", title), Ui.Field("Character ID (for website / game export)", character), error, Ui.Row(cancel, create));
        content.Margin = new Thickness(26); window = Ui.Dialog(this, "New project", content, 560, 410);
        window.Opened += (_, _) => { title.Focus(); title.SelectAll(); };
        if (!await window.ShowDialog<bool>(this) || !await ConfirmReplace()) return;
        var next = Templates.Blank(); next.ProjectName = title.Text!.Trim(); next.CharacterId = string.IsNullOrWhiteSpace(character.Text) ? "new_character" : character.Text.Trim();
        next.ListOfCharacterStates[0].Text = "";
        LoadProject(next, null, true);
        Status("‘" + next.ProjectName + "’ is ready. Write your opening scene, then save your project.");
    }
}
