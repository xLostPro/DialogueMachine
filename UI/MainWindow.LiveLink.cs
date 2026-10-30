using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using DialogueMachine.Core;
using DialogueMachine.Core.LiveLink;
using System.IO;

namespace DialogueMachine.UI;

public sealed partial class MainWindow
{
    private LiveLinkServer? liveLink;
    private readonly LiveFollow liveFollow = new();
    private readonly FollowPane followPane = new();
    private readonly DispatcherTimer liveEditTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer followTimer = new() { Interval = TimeSpan.FromMilliseconds(33) }; // At most 30 redraws per second.
    private bool liveEdits, followHidden;
    private string? followedScene;
    private static string AppVersion => typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    internal LiveLinkServer? LiveLink => liveLink;
    internal LiveFollow Following => liveFollow;
    internal string LiveLinkDiscoveryPath => Path.Combine(dataDir, "livelink.json");
    public bool LiveEdits { get => liveEdits; set { liveEdits = value; BuildMenu(); } }

    private void WireLiveLink()
    {
        Grid.SetRowSpan(followPane, 3); C<Grid>("EditorColumn").Children.Add(followPane);
        C<ToggleButton>("LiveLinkButton").IsCheckedChanged += (_, _) => SetLiveLink(C<ToggleButton>("LiveLinkButton").IsChecked == true);
        C<Button>("PlayEngineButton").Click += (_, _) => PlayInEngine();
        liveEditTimer.Tick += (_, _) => { liveEditTimer.Stop(); PublishLiveStory(); };
        followTimer.Tick += (_, _) => { followTimer.Stop(); RefreshFollow(); };
        followPane.RunnerPicked += key => { liveFollow.Follow(key); RequestFollowRefresh(); };
        followPane.NumberEdited += (key, value) => { if (liveFollow.Followed is { } r) liveLink?.SetVariable(r.ClientId, r.RunnerId, key, value, null); };
        followPane.TextEdited += (key, value) => { if (liveFollow.Followed is { } r) liveLink?.SetVariable(r.ClientId, r.RunnerId, key, null, value); };
        followPane.PushRequested += () => { Commit(); PublishLiveStory(); };
        followPane.StopRequested += () => { liveFollow.Stop(); followPane.IsVisible = false; followHidden = false; RefreshFollow(); BuildMenu(); };
        followPane.HideRequested += () => { followPane.IsVisible = false; followHidden = true; BuildMenu(); };
        Closed += (_, _) => SetLiveLink(false);
    }

    public void SetLiveLink(bool on)
    {
        if (on == (liveLink is not null)) return;
        if (on)
        {
            var server = new LiveLinkServer(new(LiveLinkDiscoveryPath, AppVersion));
            void OnUi(Action action) => Dispatcher.UIThread.Post(() => { if (ReferenceEquals(liveLink, server)) action(); });
            server.ClientsChanged += _ => OnUi(UpdateLiveLinkStatus);
            server.ClientLeft += id => OnUi(() => { liveFollow.RemoveClient(id); RequestFollowRefresh(); });
            server.RunnerState += state => OnUi(() => { liveFollow.Apply(state); RequestFollowRefresh(); });
            server.RunnerEvent += entry => OnUi(() => { liveFollow.AddEvent(entry); RequestFollowRefresh(); });
            server.RevealRequested += scene => OnUi(() => { if (project.ListOfCharacterStates.FirstOrDefault(s => s.State == scene) is { } s) SelectScene(s, true); });
            Commit(); liveLink = server; PublishLiveStory(false); // Engines may connect the moment the port opens.
            try { server.Start(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                liveLink = null; server.Dispose(); C<ToggleButton>("LiveLinkButton").IsChecked = false; Status("Live link could not start: " + ex.Message); return;
            }
        }
        else
        {
            liveEditTimer.Stop(); followTimer.Stop(); liveLink?.Dispose(); liveLink = null;
            liveFollow.Clear(); followPane.IsVisible = false; followHidden = false; RefreshFollow();
        }
        C<ToggleButton>("LiveLinkButton").IsChecked = on;
        C<Button>("PlayEngineButton").IsVisible = on;
        UpdateLiveLinkStatus(); BuildMenu();
    }
    private void UpdateLiveLinkStatus()
    {
        var count = liveLink?.ClientCount ?? 0; var status = C<TextBlock>("LiveLinkStatus");
        status.IsVisible = liveLink is not null;
        status.Text = count == 0 ? "Live link: waiting for engines" : $"Live link: {count} {(count == 1 ? "engine" : "engines")} connected";
    }

    // Returns the handoff that engines now hold, or null when the story has errors.
    private RuntimeHandoff? PublishLiveStory(bool send = true)
    {
        if (liveLink is not { } server) return null;
        if (!hasProject) { server.ClearStory(); return null; }
        try
        {
            var handoff = RuntimeExport.Create(project);
            server.Publish(project.ProjectName, handoff, send);
            RequestFollowRefresh();
            return handoff;
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
        {
            var errors = DialogueEngine.Validate(project).Where(d => d.Severity == "Error").ToList();
            server.PublishInvalid(project.ProjectName, errors.Count > 0 ? errors : [new("", "Error", ex.Message)], send);
            return null;
        }
    }
    private void LiveStoryEdited()
    {
        if (!liveEdits || liveLink is null) return;
        liveEditTimer.Stop(); liveEditTimer.Start();
    }

    public void PlayInEngine()
    {
        if (liveLink is not { } server || !hasProject) return;
        if (server.ClientCount == 0) { Status("No engine is connected. Open the Dialogue Machine live link in your engine first."); return; }
        Commit();
        // Playtest uses the open story, so the engine gets it too, saved or not.
        if (PublishLiveStory() is not { } handoff) { RefreshDiagnostics(); C<Border>("DiagnosticsBorder").IsVisible = true; Status("Resolve the story errors before playing in the engine."); return; }
        server.PlayFrom(selected?.State ?? project.Entry, handoff.Seed, handoff.NumericValues, handoff.TextValues);
        Status("Asked the engine to play from " + (selected?.State ?? project.Entry));
    }

    private void RequestFollowRefresh() { if (liveLink is not null && !followTimer.IsEnabled) followTimer.Start(); }
    internal void RefreshFollow()
    {
        var status = liveFollow.Check(hasProject ? project : null, liveLink?.StorySha256);
        var scene = status.Highlight ? liveFollow.CurrentSceneId : null;
        if (status.Highlight) Graph.SetLiveFollow(scene, liveFollow.TrailScenes, liveFollow.TrailChoices, liveFollow.TrailLinks);
        else Graph.SetLiveFollow(null, [], [], []);
        if (liveFollow.Followed is not null && !followHidden) followPane.IsVisible = true;
        var current = project.ListOfCharacterStates.FirstOrDefault(s => s.State == scene);
        followPane.Update(liveFollow, status, current?.Speaker, liveFollow.CurrentLine(hasProject ? project : null));
        if (scene != followedScene && current is not null && followPane.AutoPan && Graph.IsVisible) Graph.Centre(current);
        followedScene = scene;
    }
}
