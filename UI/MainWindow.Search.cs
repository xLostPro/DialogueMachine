using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using DialogueMachine.Core;

namespace DialogueMachine.UI;

public sealed partial class MainWindow
{
    private SearchView? search;
    private async Task ShowFindReplace()
    {
        if (!hasProject) return;
        Commit();
        search ??= new SearchView(() => project, new SearchActions(Mutate, GoToMatch));
        await search.Show(this, preferences.Scale, selected is null ? null : project.ListOfCharacterStates.IndexOf(selected));
    }
    private void GoToMatch(TextMatch match)
    {
        var scene = project.ListOfCharacterStates[match.SceneIndex]; sceneEditor.Tab = match.Field == SearchFields.Notes ? "Notes" : "Content";
        if (match.Field == SearchFields.Responses) sceneEditor.Expand(scene, match.ChoiceIndex ?? 0);
        ClearOutlineFilters(); SelectScene(scene, true);
        var name = match.Field switch { SearchFields.Dialogue => "Dialogue text", SearchFields.Responses => "Player response", SearchFields.Titles => "Scene title", _ => "Writer's notes" };
        var inputs = Editor.GetVisualDescendants().OfType<TextBox>().Where(t => AutomationProperties.GetName(t) == name).ToList();
        var input = inputs[match.ChoiceIndex ?? 0]; input.Focus(); input.SelectionStart = match.Start; input.SelectionEnd = match.Start + match.Length; input.BringIntoView();
    }
}
