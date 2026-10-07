namespace DialogueMachine.Website;

// A runnable host with observable effects, rather than logging-only hook placeholders.
public sealed class SampleHost : IRuntimeHost
{
    public HashSet<string> Achievements { get; } = [];
    public List<string> Quests { get; } = [];
    public string? OpenShop { get; private set; }
    public IReadOnlyList<string> Stock { get; private set; } = [];
    public List<string> Log { get; } = [];
    public void Event(Hook hook)
    {
        Achievements.Add(hook.Id);
        Log.Add($"Event {hook.Id}({hook.Parameter}): achievement unlocked; coin={hook.Numeric.GetValueOrDefault("inv:coin")}");
    }
    public void Script(Hook hook)
    {
        // IDs select host code; authored parameters are data, never evaluated as code.
        if (hook.Id == "close_shop") { OpenShop = null; Stock = []; }
        else Quests.Add(hook.Id + ":" + hook.Parameter);
        Log.Add($"Script {hook.Id}({hook.Parameter}): quest/host state updated");
    }
    public void Shop(Hook hook)
    {
        OpenShop = hook.Id; Stock = ["Lantern", "Rope"];
        Log.Add($"Shop {hook.Id}: opened stock panel ({string.Join(", ", Stock)})");
    }
}
