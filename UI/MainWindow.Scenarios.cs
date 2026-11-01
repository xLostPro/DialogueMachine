using Avalonia.Controls;
using DialogueMachine.Core;

namespace DialogueMachine.UI;

public sealed partial class MainWindow
{
    private ScenariosView? scenarios;
    private ScenariosView Scenarios => scenarios ??= new ScenariosView(() => project, Mutate, projectFilePicker, () => selected?.State, () => filePath, SetCoverage, () => Graph.Coverage);
    public CoverageReport? TestCoverage => Graph.Coverage;
    private void ClearScenarioResults()
    {
        if (!Scenarios.HasResults && Graph.Coverage is null) return;
        Scenarios.Clear(); SetCoverage(null);
    }
    private void SetCoverage(CoverageReport? coverage)
    {
        Graph.SetCoverage(coverage);
        C<TextBlock>("CoverageSummary").Text = coverage is null ? "" : $"✓ Tested {coverage.VisitedScenes}/{coverage.TotalScenes} scenes · {coverage.VisitedChoices}/{coverage.TotalChoices} choices";
    }
    public async Task ShowScenariosAsync()
    {
        if (!hasProject) return;
        Commit();
        await Scenarios.Show(this, preferences.Scale);
    }
}
