namespace DialogueMachine.UI;

public sealed partial class MainWindow
{
    private VariablesView? variables;
    private VariablesView Variables => variables ??= new VariablesView(() => project, draft =>
        Mutate(() => { var id = selected?.State; project = draft; selected = project.ListOfCharacterStates.FirstOrDefault(s => s.State == id); }));
    private Task<string?> PickVariable() => Variables.Pick(this, preferences.Scale);
    private async Task ShowVariables() { Commit(); await Variables.Show(this, preferences.Scale); }
}
