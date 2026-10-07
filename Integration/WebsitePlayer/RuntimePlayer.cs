using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using xLostProHome.Core.Services;
using xLostProHome.Ecosystem.Web.DialogueMachine.Models;
using xLostProHome.Ecosystem.Web.DialogueMachine.Services;

namespace DialogueMachine.Website;

public sealed record Handoff(int SchemaVersion, string Contract, string StoryJson, string StorySha256,
    string StartingScene, int Seed, Dictionary<string, int> NumericValues, Dictionary<string, string> TextValues, int MaxOperations);
public sealed record RuntimeChoice(Guid Id, string Text, int Visibility, bool Enabled, string? Destination);
public sealed record Hook(string Type, string Id, string? Parameter, string Scene,
    IReadOnlyDictionary<string, int> Numeric, IReadOnlyDictionary<string, string> Text);
public interface IRuntimeHost
{
    void Event(Hook hook);
    void Script(Hook hook);
    void Shop(Hook hook);
}

// This is a subclass of the actual website component, compiled with its unchanged Razor,
// controller, models and service. It does not reference or execute desktop Core.
// Contract mode normalises a detached scene at the boundary to that legacy component.
public sealed class RuntimePlayer : xLostProHome.Ecosystem.Web.DialogueMachine.DialogueMachine
{
    public const string Contract = "dialogue-machine.website.v1";
    private readonly Dictionary<string, CharacterState> story;
    private readonly Dictionary<string, string> text;
    private readonly IRuntimeHost host;
    private readonly int limit;
    private ulong random;
    private int candidate = -1;
    private bool faulted;
    private bool inCallback;
    public int Operations { get; private set; }
    public string? EndingScene { get; private set; }
    public Guid? EndingChoice { get; private set; }
    public string? Scene => simActiveState?.State;
    public string[] Pages => simLines.ToArray();
    public int Page => simLineIndex;
    public ulong RandomState => random;
    public bool MorePages => simActiveState is not null && simLineIndex < simLines.Length - 1;
    public IReadOnlyDictionary<string, int> Numeric => new ReadOnlyDictionary<string, int>(
        simAffinity.ToDictionary(p => "aff:" + p.Key, p => p.Value).Concat(simInventory.Select(p => new KeyValuePair<string, int>("inv:" + p.Key, p.Value))).ToDictionary());
    public IReadOnlyDictionary<string, string> Text => new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(text));
    public List<Hook> Hooks { get; } = [];
    public List<string> Delivered { get; } = [];
    public List<string> Transitions { get; } = [];
    private CharacterState? Original => Scene is { } id ? story[id] : null;
    private bool Met(Choice c) => c.Visibility == VisibilityMode.Always || Logic.EvaluateRequirements(c, simAffinity, simInventory, out _);
    public IReadOnlyList<RuntimeChoice> Choices => MorePages || Original is null ? [] : Original.Choices
        .Where(c => c.Visibility != VisibilityMode.Hide || Met(c))
        .Select(c => new RuntimeChoice(c.InternalId, Resolve(c.ButtonText), (int)c.Visibility, Met(c), Destination(c, Original))).ToList();
    public bool CanContinue => Original is { } s && (MorePages || !s.Choices.Any(Met) && !string.IsNullOrWhiteSpace(s.DefaultNextId));

    public RuntimePlayer(string bundleJson, IRuntimeHost host, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        var config = JsonSerializer.Deserialize<Handoff>(bundleJson) ?? throw new InvalidDataException("Empty runtime handoff. Export it again with runtime-export.");
        if (config.SchemaVersion != 1 || config.Contract != Contract)
            throw new InvalidDataException($"Unsupported handoff schema {config.SchemaVersion}/contract ‘{config.Contract}’. Install the matching website adapter or export {Contract} with schema 1.");
        if (config.StoryJson is null || Encoding.UTF8.GetByteCount(config.StoryJson) > 20 * 1024 * 1024)
            throw new InvalidDataException("Runtime story is missing or exceeds 20 MiB. Export a smaller story.");
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(config.StoryJson)));
        if (hash != config.StorySha256) throw new InvalidDataException("Story hash does not match configuration. Re-export the complete handoff; do not mix story and configuration versions.");
        if (config.MaxOperations is < 1 or > 10000 || config.NumericValues is null || config.TextValues is null || config.TextValues.Any(p => p.Value is null))
            throw new InvalidDataException("Runtime configuration needs NumericValues, TextValues and MaxOperations between 1 and 10,000. Export the handoff again.");
        var root = JsonSerializer.Deserialize<DialogueRoot>(config.StoryJson) ?? throw new InvalidDataException("Story is empty.");
        Validate(root, config.NumericValues);
        story = root.ListOfCharacterStates.ToDictionary(s => s.State, StringComparer.Ordinal);
        if (!story.ContainsKey(config.StartingScene ?? "") || root.ListOfCharacterStates[0].State != config.StartingScene)
            throw new InvalidDataException("StartingScene must name the first exported scene. Choose an existing entry in the editor and re-export.");
        Logic = new DialogueService(); Toasts = new ToastService(); Notify = new NotificationService(); History = new HistoryManager(Toasts);
        this.host = host ?? throw new ArgumentNullException(nameof(host)); limit = config.MaxOperations;
        text = new(config.TextValues, StringComparer.Ordinal); random = unchecked((ulong)(long)config.Seed);
        foreach (var (key, value) in config.NumericValues)
        {
            if (!Regex.IsMatch(key, @"\A(aff|inv):[\w.\-]+\z")) throw new InvalidDataException($"NumericValues key ‘{key}’ must use aff: or inv:. Rename the variable and re-export.");
            (key.StartsWith("inv:", StringComparison.Ordinal) ? simInventory : simAffinity)[key[4..]] = key.StartsWith("inv:", StringComparison.Ordinal) ? Math.Max(0, value) : value;
        }
        Prepare(config.StartingScene!); SimGoToNext(config.StartingScene); Entered();
    }

    private static string? Destination(Choice c, CharacterState s) => string.IsNullOrWhiteSpace(c.NextStateId) ? s.DefaultNextId : c.NextStateId;
    private void Begin(CancellationToken cancellation)
    {
        if (faulted) throw new InvalidOperationException("A host callback failed. This session cannot resume; start a new session after resolving the host error.");
        if (inCallback) throw new InvalidOperationException("Host callbacks cannot re-enter the player. Use the supplied immutable state snapshot and apply host effects outside the story.");
        cancellation.ThrowIfCancellationRequested();
        if (Operations >= limit) throw new InvalidOperationException($"Runtime operation limit {limit} reached at scene ‘{Scene ?? "[EXIT]"}’. Review the cycle/path or raise MaxOperations (maximum 10,000).");
        Operations++;
    }
    public void Advance(CancellationToken cancellation = default)
    {
        if (!MorePages) throw new InvalidOperationException($"Scene ‘{Scene}’ has no next dialogue page.");
        Begin(cancellation); simLineIndex++; Delivered.Add(simLines[simLineIndex]);
    }
    public void Select(Guid choice, CancellationToken cancellation = default)
    {
        if (Original is not { } s || MorePages) throw new InvalidOperationException("Finish the current dialogue pages before selecting a response.");
        var c = s.Choices.SingleOrDefault(c => c.InternalId == choice) ?? throw new InvalidOperationException($"Response ‘{choice}’ is not in scene ‘{s.State}’. Re-record the path.");
        if (!Met(c)) throw new InvalidOperationException($"Response ‘{c.ButtonText}’ in ‘{s.State}’ is unavailable. Check its requirements and starting values.");
        Move(Destination(c, s), c, cancellation);
    }
    public void Proceed(CancellationToken cancellation = default)
    {
        if (MorePages) { Advance(cancellation); return; }
        if (!CanContinue) throw new InvalidOperationException($"Continue is unavailable in scene ‘{Scene}’. Select an enabled response or repair its fallback.");
        Move(Original!.DefaultNextId, null, cancellation);
    }
    private void Move(string? destination, Choice? choice, CancellationToken cancellation)
    {
        var source = Original!;
        if (!string.IsNullOrWhiteSpace(destination) && destination != "[EXIT]" && !story.ContainsKey(destination))
            throw new InvalidOperationException($"Destination ‘{destination}’ from ‘{source.State}’ is missing. No actions were applied; repair the connection.");
        Begin(cancellation);
        Apply(choice?.AfterChoiceActions ?? []); Apply(source.DefaultAfterActions);
        // The website's own selection/transition methods still resolve the target,
        // terminate and deliver pages. Suppress legacy duplicate action execution.
        simActiveState!.DefaultAfterActions = [];
        var ending = string.IsNullOrWhiteSpace(destination) || destination == "[EXIT]";
        if (!ending) { candidate = -1; Prepare(destination!); }
        if (choice is not null) SimSelectChoice(new Choice { NextStateId = destination, AfterChoiceActions = [] });
        else SimGoToNext(destination);
        Transitions.Add(source.State + "->" + (ending ? "[EXIT]" : destination));
        if (ending) { EndingScene = source.State; EndingChoice = choice?.InternalId; }
        else Entered();
    }
    private void Apply(IEnumerable<ActionModel> actions)
    {
        foreach (var action in actions)
        {
            if (action.Type is "Event" or "Script") { Dispatch(action.Type, action.TargetId, action.Parameter); continue; }
            var values = action.Type == "Inventory" ? simInventory : simAffinity;
            var before = values.GetValueOrDefault(action.TargetId);
            var after = (int)Math.Clamp((long)before + action.Amount, action.Type == "Inventory" ? 0 : int.MinValue, int.MaxValue);
            // Give the real website action processor an overflow-safe delta.
            Logic.ProcessActions([new ActionModel { Type = action.Type, TargetId = action.TargetId, Amount = (int)((long)after - before) }], simAffinity, simInventory);
        }
    }
    private void Dispatch(string type, string id, string? parameter)
    {
        var hook = new Hook(type, id, parameter, Scene!, Numeric, Text); Hooks.Add(hook);
        try { inCallback = true; if (type == "Event") host.Event(hook); else if (type == "Script") host.Script(hook); else host.Shop(hook); }
        catch (Exception ex) { faulted = true; throw new InvalidOperationException($"{type} callback ‘{id}’ failed in scene ‘{Scene}’. Host effects may have occurred; resolve the host error and start a new session. {ex.Message}", ex); }
        finally { inCallback = false; }
    }
    private void Entered()
    {
        Delivered.Add(simLines[0]);
        if (Original!.Type == "Shop") Dispatch("Shop", Original.ShopData.ShopId, null);
    }
    private void Prepare(string id)
    {
        var s = story[id]; var resolved = Resolve(s.Text);
        var lines = resolved.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) lines = ["(No dialogue)"];
        else if (!s.SayItAll)
        {
            if (candidate < 0) candidate = Next(lines.Length);
            lines = [lines[Math.Min(candidate, lines.Length - 1)]];
        }
        // Already-resolved text may contain literal [if] markers from substitutions.
        // The legacy parser must not interpret those for a second time.
        Root = new DialogueRoot { ListOfCharacterStates = [new CharacterState { State = s.State, Type = s.Type,
            Text = string.Join('\n', lines.Select(Encode)), SayItAll = true, ShopData = s.ShopData }] };
    }
    // Escape literal condition markers produced by substitutions without changing their
    // displayed text. The prefix also preserves whitespace-only pages in the legacy parser.
    private static string Encode(string line) => "\uE000P" + line.Replace("\uE000", "\uE000E").Replace("[", "\uE000L");
    private static string Decode(string line) => line[2..].Replace("\uE000L", "[").Replace("\uE000E", "\uE000");
    private new void SimGoToNext(string? id)
    {
        base.SimGoToNext(id);
        if (simActiveState is not null) simLines = simLines.Select(Decode).ToArray();
    }
    private new void SimSelectChoice(Choice choice)
    {
        base.SimSelectChoice(choice);
        if (simActiveState is not null) simLines = simLines.Select(Decode).ToArray();
    }
    public void SetNumeric(string key, int value, CancellationToken cancellation = default)
    {
        var values = key.StartsWith("inv:", StringComparison.Ordinal) ? simInventory : simAffinity;
        if (key.Length < 4 || !Numeric.ContainsKey(key)) throw new ArgumentException($"Unknown numeric variable ‘{key}’. Configure it before starting.");
        Begin(cancellation); values[key[4..]] = key.StartsWith("inv:", StringComparison.Ordinal) ? Math.Max(0, value) : value; Rebuild();
    }
    public void SetText(string key, string value, CancellationToken cancellation = default)
    {
        if (!text.ContainsKey(key)) throw new ArgumentException($"Unknown text variable ‘{key}’. Configure it before starting.");
        ArgumentNullException.ThrowIfNull(value); Begin(cancellation); text[key] = value; Rebuild();
    }
    public void Reroll(CancellationToken cancellation = default)
    {
        if (Original is null || Original.SayItAll) throw new InvalidOperationException("Reroll requires a random-delivery scene.");
        Begin(cancellation); candidate = -1; Rebuild();
    }
    public void Refresh(CancellationToken cancellation = default) { Begin(cancellation); Rebuild(); }
    private void Rebuild()
    {
        if (Scene is not { } id) return;
        var page = simLineIndex; Prepare(id);
        simLines = Logic.ParseDialoguePages(Root.ListOfCharacterStates[0], simAffinity, simInventory).Select(Decode).ToArray();
        simLineIndex = Math.Min(page, simLines.Length - 1);
    }
    private int Next(int count)
    {
        ulong value; var threshold = unchecked(0UL - (ulong)count) % (ulong)count;
        do
        {
            random = unchecked(random + 0x9E3779B97F4A7C15UL); value = random;
            value = unchecked((value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL);
            value = unchecked((value ^ (value >> 27)) * 0x94D049BB133111EBUL); value ^= value >> 31;
        } while (value < threshold);
        return (int)(value % (ulong)count);
    }
    private string Resolve(string input)
    {
        // Parse structure independently; evaluate each gate using the real website service.
        var tokens = Regex.Matches(input, @"\[if\s+([\w:.\-]+)\s*(==|!=|>=|<=|>|<)\s*(-?\d+)\]|\[else\]|\[/if\]", RegexOptions.IgnoreCase);
        var numeric = Numeric;
        var position = 0; var output = new StringBuilder(); var stack = new Stack<(bool Parent, bool Met, bool Else)>(); var include = true;
        foreach (Match token in tokens)
        {
            if (include) output.Append(input.AsSpan(position, token.Index - position)); position = token.Index + token.Length;
            if (token.Groups[1].Success)
            {
                if (stack.Count >= 64) throw new InvalidDataException("Inline conditions exceed 64 levels. Simplify the text and re-export.");
                var key = token.Groups[1].Value;
                if (!numeric.ContainsKey(key)) key = numeric.ContainsKey("inv:" + key) ? "inv:" + key : "aff:" + key;
                var met = Logic.EvaluateRequirements(new Choice { Requirements = [new Requirement { Type = key.StartsWith("inv:", StringComparison.Ordinal) ? "Inventory" : "Affinity", TargetId = key[4..], Operator = token.Groups[2].Value, Value = token.Groups[3].Value }] }, simAffinity, simInventory, out _);
                stack.Push((include, met, false)); include = include && met;
            }
            else if (token.Value.Equals("[else]", StringComparison.OrdinalIgnoreCase))
            {
                if (!stack.TryPop(out var frame) || frame.Else) throw new InvalidDataException("Inline else is unmatched or repeated. Repair the text and re-export.");
                stack.Push((frame.Parent, frame.Met, true)); include = frame.Parent && !frame.Met;
            }
            else
            {
                if (!stack.TryPop(out var frame)) throw new InvalidDataException("Inline closing tag is unmatched. Repair the text and re-export.");
                include = frame.Parent;
            }
        }
        if (stack.Count != 0) throw new InvalidDataException("Inline conditional is unclosed. Repair the text and re-export.");
        if (include) output.Append(input.AsSpan(position));
        return Regex.Replace(output.ToString(), @"\{([\w:.\-]+)\}", m => text.TryGetValue(m.Groups[1].Value, out var value) ? value : numeric.TryGetValue(m.Groups[1].Value, out var number) ? number.ToString(CultureInfo.InvariantCulture) : m.Value);
    }
    private static void Validate(DialogueRoot root, Dictionary<string, int> numeric)
    {
        void Fail(string scene, string detail) => throw new InvalidDataException($"Scene ‘{scene}’: {detail}. Repair the story/configuration and re-export.");
        if (root.ListOfCharacterStates is null || root.ListOfCharacterStates.Count is < 1 or > 10000) Fail("", "expected 1–10,000 scenes");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in root.ListOfCharacterStates!)
            if (s is null || string.IsNullOrWhiteSpace(s.State) || s.State == "[EXIT]" || !ids.Add(s.State)) Fail(s?.State ?? "", "scene identity is blank, duplicate or reserved");
        foreach (var s in root.ListOfCharacterStates!)
        {
            if (s.Type is not ("Dialogue" or "Shop") || s.Text is null || s.Choices is null || s.DefaultAfterActions is null || s.ShopData is null) Fail(s.State, "unsupported type or missing content/logic collection");
            var choices = new HashSet<Guid>();
            foreach (var c in s.Choices!)
            {
                if (c is null || c.InternalId == Guid.Empty || !choices.Add(c.InternalId) || c.ButtonText is null || !Enum.IsDefined(c.Visibility) || c.Requirements is null || c.AfterChoiceActions is null) Fail(s.State, "response identity, visibility or logic is invalid");
                foreach (var r in c!.Requirements!)
                    if (r is null || r.Type is not ("Affinity" or "Inventory") || string.IsNullOrWhiteSpace(r.TargetId) || !new[] { "==", "!=", ">", "<", ">=", "<=" }.Contains(r.Operator) || !int.TryParse(r.Value, out _) || !numeric.ContainsKey((r.Type == "Inventory" ? "inv:" : "aff:") + r.TargetId)) Fail(s.State, "requirement is invalid or its numeric configuration is missing");
                if (string.IsNullOrWhiteSpace(Destination(c, s))) Fail(s.State, "response has no destination or fallback; use [EXIT] for an ending");
                if (Destination(c, s) is { } destination && destination != "[EXIT]" && !ids.Contains(destination)) Fail(s.State, $"response destination ‘{destination}’ is missing; no actions were applied");
            }
            if (!string.IsNullOrWhiteSpace(s.DefaultNextId) && s.DefaultNextId != "[EXIT]" && !ids.Contains(s.DefaultNextId)) Fail(s.State, $"continue destination ‘{s.DefaultNextId}’ is missing; no actions were applied");
            foreach (var a in s.DefaultAfterActions!.Concat(s.Choices!.SelectMany(c => c.AfterChoiceActions)))
                if (a is null || a.Type is not ("Affinity" or "Inventory" or "Event" or "Script") || string.IsNullOrWhiteSpace(a.TargetId) || a.Type is "Affinity" or "Inventory" && !numeric.ContainsKey((a.Type == "Inventory" ? "inv:" : "aff:") + a.TargetId)) Fail(s.State, "action is invalid or its numeric configuration is missing");
            if (s.Choices.Count == 0 && string.IsNullOrWhiteSpace(s.DefaultNextId)) Fail(s.State, "no responses or continue destination; use [EXIT] for an ending");
            foreach (var value in s.Choices.Select(c => c.ButtonText).Prepend(s.Text))
            {
                // Validate every branch, including hidden malformed tags, before any callback.
                var stripped = Regex.Replace(value!, @"\[if\s+[\w:.\-]+\s*(==|!=|>=|<=|>|<)\s*(-?\d+)\]|\[else\]|\[/if\]", "", RegexOptions.IgnoreCase);
                if (Regex.IsMatch(stripped, @"\[(?:if\b|else\b|/if\b)", RegexOptions.IgnoreCase)) Fail(s.State, "malformed inline condition");
                var depth = new Stack<bool>();
                foreach (Match token in Regex.Matches(value!, @"\[if\s+[^\]]+\]|\[else\]|\[/if\]", RegexOptions.IgnoreCase))
                {
                    if (token.Value.StartsWith("[if", StringComparison.OrdinalIgnoreCase))
                    {
                        var condition = Regex.Match(token.Value, @"\[if\s+([\w:.\-]+)\s*(==|!=|>=|<=|>|<)\s*(-?\d+)\]", RegexOptions.IgnoreCase);
                        var key = condition.Groups[1].Value;
                        if (!numeric.ContainsKey(key)) key = numeric.ContainsKey("inv:" + key) ? "inv:" + key : "aff:" + key;
                        if (!int.TryParse(condition.Groups[3].Value, out _) || !numeric.ContainsKey(key)) Fail(s.State, "inline threshold is outside int32 or its numeric configuration is missing");
                        if (depth.Count == 64) Fail(s.State, "inline nesting exceeds 64"); depth.Push(false);
                    }
                    else if (token.Value.Equals("[else]", StringComparison.OrdinalIgnoreCase)) { if (!depth.TryPop(out var seen) || seen) Fail(s.State, "unmatched or repeated else"); depth.Push(true); }
                    else if (!depth.TryPop(out _)) Fail(s.State, "unmatched closing condition");
                }
                if (depth.Count != 0) Fail(s.State, "unclosed condition");
            }
        }
    }
}
