using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DialogueMachine.Runtime
{
    public sealed class Choice
    {
        public string Id;
        // Resolved rich text, like CurrentPage.
        public string Text;
        public bool Enabled;
        public string DisabledReason;
    }

    public sealed class BarkLine
    {
        public string SceneId;
        public string Speaker;
        public string Portrait;
        public string Text;
        public string PlainText;
        public IReadOnlyDictionary<string, string> Metadata;
    }

    // Plays a handoff with the semantics of Core/Simulation.cs. Back, trace and scenarios stay desktop-only.
    public sealed class DialogueRunner
    {
        private static readonly IReadOnlyDictionary<string, string> NoMetadata = new Dictionary<string, string>();
        private readonly bool ownsVariables;
        private IDialogueVariables store;
        private Handoff handoff;
        private Scene scene;
        private string[] rawPages = new string[0];
        private string[] pages = new string[0];
        private ulong randomState;
        private int randomLine = -1;
        private readonly Dictionary<string, int> visits = new Dictionary<string, int>(StringComparer.Ordinal);
        private int currentVisitIndex;

        public event Action<string> LineShown;
        public event Action<IReadOnlyList<Choice>> ChoicesShown;
        public event Action<string, string, string, int> ActionRaised;
        public event Action<string> ShopOpened;
        // Before and after are int for numeric keys and string for text keys.
        public event Action<string, object, object> VariableChanged;
        public event Action<string, string> Ended;
        public event Action<string> Warning;

        // Without a store the runner keeps its own and resets it on Start. A game store keeps its values.
        public DialogueRunner(IDialogueVariables variables = null)
        {
            ownsVariables = variables == null;
            store = variables ?? new DictionaryVariables();
            Variables = new TrackedVariables(this);
        }

        public IDialogueVariables Variables { get; }
        public string CurrentSceneId => scene?.State;
        public string Speaker
        {
            get
            {
                if (scene == null) return null;
                if (rawPages.Length == 0) return scene.Speaker;
                DialogueRules.ParsePageHeader(rawPages[PageIndex], scene.Speaker, scene.Metadata.GetValueOrDefault("portrait"), out var sp, out _, out _);
                return sp;
            }
        }
        public string Portrait
        {
            get
            {
                if (scene == null) return null;
                var defPortrait = scene.Metadata.GetValueOrDefault("portrait", "");
                if (rawPages.Length == 0) return defPortrait;
                DialogueRules.ParsePageHeader(rawPages[PageIndex], scene.Speaker, defPortrait, out _, out var pt, out _);
                return pt;
            }
        }
        public string Title => scene?.Title;
        public bool IsEnded { get; private set; }
        public string CurrentPage => scene == null ? null : pages[PageIndex];
        public string CurrentPagePlain => scene == null ? null : DialogueRules.PlainText(CurrentPage);
        public int PageIndex { get; private set; }
        public int PageCount => pages.Length;
        public IReadOnlyDictionary<string, string> CurrentMetadata
        {
            get
            {
                if (scene == null) return NoMetadata;
                if (rawPages.Length == 0) return scene.Metadata;
                var defPortrait = scene.Metadata.GetValueOrDefault("portrait", "");
                DialogueRules.ParsePageHeader(rawPages[PageIndex], scene.Speaker, defPortrait, out var sp, out var pt, out _);
                if (sp == scene.Speaker && pt == defPortrait) return scene.Metadata;
                var copy = new Dictionary<string, string>();
                foreach (var kv in scene.Metadata) copy[kv.Key] = kv.Value;
                copy["speaker"] = sp;
                if (!string.IsNullOrEmpty(pt)) copy["portrait"] = pt;
                return copy;
            }
        }
        public bool CanContinue => scene != null && (MorePages || (!Choices.Any(c => c.Enabled) && !string.IsNullOrWhiteSpace(scene.DefaultNextId)));
        private bool MorePages => PageIndex < pages.Length - 1;

        public IReadOnlyList<Choice> Choices
        {
            get
            {
                var result = new List<Choice>();
                if (scene == null) return result;
                var numbers = Numbers();
                var texts = Texts();
                foreach (var choice in scene.Choices)
                {
                    var met = DialogueRules.RequirementsMet(choice, numbers, out var reason) || choice.Visibility == StoryChoice.Always;
                    if (!met && choice.Visibility == StoryChoice.Hide) continue;
                    result.Add(new Choice { Id = choice.InternalId, Text = DialogueRules.ResolveText(choice.ButtonText, numbers, texts), Enabled = met, DisabledReason = met ? null : reason });
                }
                return result;
            }
        }

        public void Load(string handoffJson) => Load(Handoff.Parse(handoffJson));

        public void Load(Handoff story)
        {
            handoff = story ?? throw new ArgumentNullException(nameof(story));
        }

        public void Start(string sceneId = null, int? seed = null, IReadOnlyDictionary<string, int> numeric = null, IReadOnlyDictionary<string, string> text = null)
        {
            if (handoff == null) throw new InvalidOperationException("Load a story before starting it.");
            if (ownsVariables) { store = new DictionaryVariables(); visits.Clear(); }
            AddMissingVariables();
            // Overrides apply before the first scene resolves, like a scenario's starting values.
            if (numeric != null) foreach (var kv in numeric) store.SetNumber(kv.Key, kv.Key.StartsWith("inv:", StringComparison.Ordinal) ? Math.Max(0, kv.Value) : kv.Value);
            if (text != null) foreach (var kv in text) store.SetText(kv.Key, kv.Value);
            randomState = DialogueRules.SeedState(seed ?? handoff.Seed);
            scene = null;
            IsEnded = false;
            Enter(sceneId ?? handoff.StartingScene);
        }

        public BarkLine EvaluateBark(string sceneId)
        {
            if (handoff == null) throw new InvalidOperationException("Load a story before evaluating a bark.");
            var barkScene = handoff.Story.Find(sceneId) ?? throw new InvalidOperationException("Scene '" + sceneId + "' does not exist.");
            AddMissingVariables();
            var visitIndex = visits.GetValueOrDefault(barkScene.State);
            visits[barkScene.State] = visitIndex + 1;
            var resolved = DialogueRules.ResolveText(barkScene.Text, Numbers(), Texts());
            var tempLine = -1;
            var barkRaw = DialogueRules.BuildPages(resolved, barkScene.SayItAll, ref randomState, ref tempLine, barkScene.Metadata.GetValueOrDefault("delivery"), visitIndex)[0];
            DialogueRules.ParsePageHeader(barkRaw, barkScene.Speaker, barkScene.Metadata.GetValueOrDefault("portrait", ""), out var sp, out var pt, out var body);
            return new BarkLine
            {
                SceneId = barkScene.State,
                Speaker = sp,
                Portrait = pt,
                Text = body,
                PlainText = DialogueRules.PlainText(body),
                Metadata = barkScene.Metadata
            };
        }

        public void Continue()
        {
            if (MorePages)
            {
                PageIndex++;
                Deliver();
            }
            else if (scene != null) Move(scene.DefaultNextId, null);
        }

        public void Choose(Guid choiceId) => Choose(choiceId.ToString());

        public void Choose(string choiceId)
        {
            var choice = scene?.Choices.FirstOrDefault(c => string.Equals(c.InternalId, choiceId, StringComparison.OrdinalIgnoreCase));
            if (choice == null) throw new InvalidOperationException("Select a choice from the current scene.");
            if (MorePages) throw new InvalidOperationException("Finish the dialogue pages before choosing.");
            if (choice.Visibility != StoryChoice.Always && !DialogueRules.RequirementsMet(choice, Numbers(), out var reason)) throw new InvalidOperationException(reason);
            Move(string.IsNullOrWhiteSpace(choice.NextStateId) ? scene.DefaultNextId : choice.NextStateId, choice);
        }

        // Keeps the variables. A scene that survived the edit is re-entered at the clamped page.
        public void ReplaceStory(Handoff story)
        {
            handoff = story ?? throw new ArgumentNullException(nameof(story));
            AddMissingVariables();
            if (scene == null) return;
            var id = scene.State;
            scene = story.Story.Find(id);
            if (scene == null)
            {
                pages = new string[0];
                PageIndex = 0;
                IsEnded = true;
                Warning?.Invoke("Scene '" + id + "' no longer exists, so the conversation ended.");
                return;
            }
            RebuildPages();
            Deliver();
        }

        // randomState is a string because MiniJson numbers are doubles and cannot hold 64 bits.
        public string SaveState() => MiniJson.Serialize(new Dictionary<string, object>
        {
            ["sceneId"] = scene?.State,
            ["pageIndex"] = PageIndex,
            ["randomState"] = randomState.ToString(CultureInfo.InvariantCulture),
            ["randomLine"] = randomLine,
            ["numeric"] = Numbers(),
            ["text"] = Texts(),
            ["storySha256"] = handoff?.StorySha256
        });

        public void RestoreState(string json)
        {
            if (handoff == null) throw new InvalidOperationException("Load a story before restoring a save.");
            var state = MiniJson.Parse(json) as Dictionary<string, object>;
            if (state == null) throw new FormatException("A save state must be a JSON object.");
            var id = Story.Text(state, "sceneId", null);
            var saved = handoff.Story.Find(id);
            if (id != null && saved == null) throw new InvalidOperationException("Scene '" + id + "' does not exist.");
            if (ownsVariables) store = new DictionaryVariables();
            if (state.TryGetValue("numeric", out var numeric) && numeric is Dictionary<string, object> numbers)
                foreach (var entry in numbers)
                    if (entry.Value is double number) store.SetNumber(entry.Key, (int)number);
            if (state.TryGetValue("text", out var text) && text is Dictionary<string, object> texts)
                foreach (var entry in texts)
                    if (entry.Value is string value) store.SetText(entry.Key, value);
            randomState = ulong.Parse(Story.Text(state, "randomState", "0"), CultureInfo.InvariantCulture);
            randomLine = Story.Number(state, "randomLine", -1);
            PageIndex = Story.Number(state, "pageIndex", 0);
            scene = saved;
            IsEnded = saved == null;
            pages = new string[0];
            if (saved == null) PageIndex = 0;
            else
            {
                RebuildPages();
                Deliver();
            }
        }

        private void AddMissingVariables()
        {
            foreach (var entry in handoff.NumericValues)
                if (!store.HasNumber(entry.Key)) store.SetNumber(entry.Key, DialogueRules.ClampNumber(entry.Key, entry.Value));
            foreach (var entry in handoff.TextValues)
                if (store.GetText(entry.Key) == null) store.SetText(entry.Key, entry.Value);
        }

        private Dictionary<string, int> Numbers() => store.Keys.Where(store.HasNumber).ToDictionary(k => k, store.GetNumber, StringComparer.Ordinal);

        private Dictionary<string, string> Texts() => store.Keys.Where(k => store.GetText(k) != null).ToDictionary(k => k, store.GetText, StringComparer.Ordinal);

        private static bool IsExit(string id) => string.IsNullOrWhiteSpace(id) || id == DialogueRules.Exit;

        private void RebuildPages()
        {
            var text = DialogueRules.ResolveText(scene.Text, Numbers(), Texts());
            var delivery = scene.Metadata.GetValueOrDefault("delivery");
            rawPages = DialogueRules.BuildPages(text, scene.SayItAll, ref randomState, ref randomLine, delivery, currentVisitIndex);
            pages = new string[rawPages.Length];
            var defPortrait = scene.Metadata.GetValueOrDefault("portrait", "");
            for (var i = 0; i < rawPages.Length; i++)
            {
                DialogueRules.ParsePageHeader(rawPages[i], scene.Speaker, defPortrait, out _, out _, out var pageText);
                pages[i] = pageText;
            }
            PageIndex = Math.Min(PageIndex, pages.Length - 1);
        }

        private void RefreshPage()
        {
            if (scene == null) return;
            var previous = CurrentPage;
            RebuildPages();
            if (previous != CurrentPage) LineShown?.Invoke(CurrentPage);
        }

        private void Deliver()
        {
            LineShown?.Invoke(CurrentPage);
            if (MorePages) return;
            var choices = Choices;
            if (choices.Count > 0) ChoicesShown?.Invoke(choices);
        }

        private void Enter(string id)
        {
            if (IsExit(id))
            {
                End(id, null);
                return;
            }
            scene = handoff.Story.Find(id) ?? throw new InvalidOperationException("Scene '" + id + "' does not exist.");
            currentVisitIndex = visits.GetValueOrDefault(scene.State);
            visits[scene.State] = currentVisitIndex + 1;
            PageIndex = 0;
            randomLine = -1;
            RebuildPages();
            Deliver();
            if (scene.Type == "Shop") ShopOpened?.Invoke(scene.ShopId);
        }

        private void End(string sceneId, string choiceId)
        {
            scene = null;
            rawPages = new string[0];
            pages = new string[0];
            PageIndex = 0;
            IsEnded = true;
            Ended?.Invoke(sceneId, choiceId);
        }

        private void Move(string id, StoryChoice choice)
        {
            if (!IsExit(id) && handoff.Story.Find(id) == null) throw new InvalidOperationException("Scene '" + id + "' does not exist. No actions were applied.");
            if (choice != null) Apply(choice.AfterChoiceActions);
            Apply(scene.DefaultAfterActions);
            if (IsExit(id)) End(scene.State, choice?.InternalId);
            else Enter(id);
        }

        private void Apply(IEnumerable<StoryAction> actions)
        {
            foreach (var action in actions)
            {
                if (action.Type == "Event" || action.Type == "Script") ActionRaised?.Invoke(action.Type, action.TargetId, action.Parameter, action.Amount);
                else if (action.Type == "SetText")
                {
                    var before = store.GetText(action.TargetId);
                    var after = action.Parameter ?? "";
                    store.SetText(action.TargetId, after);
                    if (after != before) VariableChanged?.Invoke(action.TargetId, before, after);
                }
                // Stories from newer versions can carry action types this runtime doesn't know.
                else if (DialogueRules.IsVariableAction(action.Type))
                {
                    var key = DialogueRules.Key(action.Type, action.TargetId);
                    var before = store.GetNumber(key);
                    var after = DialogueRules.ApplyAction(action.Type, before, action.Amount);
                    store.SetNumber(key, after);
                    if (after != before) VariableChanged?.Invoke(key, before, after);
                }
            }
        }

        // Game code writes through this wrapper so the page refreshes like a playtest override.
        private sealed class TrackedVariables : IDialogueVariables
        {
            private readonly DialogueRunner runner;
            public TrackedVariables(DialogueRunner runner) { this.runner = runner; }

            public IEnumerable<string> Keys => runner.store.Keys;
            public bool HasNumber(string key) => runner.store.HasNumber(key);
            public int GetNumber(string key) => runner.store.GetNumber(key);
            public string GetText(string key) => runner.store.GetText(key);

            public void SetNumber(string key, int value)
            {
                value = DialogueRules.ClampNumber(key, value);
                var before = runner.store.GetNumber(key);
                if (value == before && runner.store.HasNumber(key)) return;
                runner.store.SetNumber(key, value);
                runner.VariableChanged?.Invoke(key, before, value);
                runner.RefreshPage();
            }

            public void SetText(string key, string value)
            {
                var before = runner.store.GetText(key);
                if (value == before) return;
                runner.store.SetText(key, value);
                runner.VariableChanged?.Invoke(key, before, value);
                runner.RefreshPage();
            }
        }
    }
}
