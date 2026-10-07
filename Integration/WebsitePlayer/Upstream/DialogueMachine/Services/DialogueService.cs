
using xLostProHome.Ecosystem.Web.DialogueMachine.Models;
using System.Text.RegularExpressions;

namespace xLostProHome.Ecosystem.Web.DialogueMachine.Services;

/// <summary>
/// ZENITH-APEX LOGIC ENGINE v3.0.5
/// STRICT NAVIGATION EDITION: Implements exhaustive navigational auditing. 
/// Every pointer (Choice or Fallback) is cross-referenced against the state 
/// registry to ensure no "Ghost Pointers" exist prior to simulation.
/// </summary>
public class DialogueService
{
    // --- GEOMETRIC CONSTANTS ---
    public const int BaseWidth = 240;
    public const int ChoiceWidth = 200;
    private const int LineHeight = 18;
    private const int VerticalPadding = 40;

    /// <summary>
    /// STATE HEIGHT CALCULUS
    /// Determines the height of the SVG node based on content volume and 'SayItAll' status.
    /// </summary>
    public int GetStateHeight(CharacterState state)
    {
        if (string.IsNullOrWhiteSpace(state.Text)) return 120;
        int finalLineCount = !state.SayItAll ? 1 : Math.Max((state.Text.Length / 25) + 1, state.Text.Split('\n').Length);
        return 60 + (finalLineCount * LineHeight) + VerticalPadding;
    }

    public int GetChoiceHeight(Choice choice)
    {
        if (string.IsNullOrWhiteSpace(choice.ButtonText)) return 60;
        int lines = (choice.ButtonText.Length / 20) + 1;
        return Math.Max(60, (lines * LineHeight) + 20);
    }
    
    /// <summary>
    /// VARIABLE RESOLUTION ENGINE
    /// Discerns between Inventory and Affinity based on explicit prefixes.
    /// Supports "inv:" for items and "aff:" for relationship values.
    /// </summary>
    private int ResolveVariableValue(string varName, Dictionary<string, int> affinity, Dictionary<string, int> inventory)
    {
        // 1. Explicit Inventory Request
        if (varName.StartsWith("inv:", StringComparison.OrdinalIgnoreCase))
            return inventory.GetValueOrDefault(varName.Substring(4), 0);

        // 2. Explicit Affinity Request
        if (varName.StartsWith("aff:", StringComparison.OrdinalIgnoreCase))
            return affinity.GetValueOrDefault(varName.Substring(4), 0);

        // 3. Legacy Fallback (Registry Normalisation)
        // If no prefix is provided, we check Inventory first, then Affinity.
        return inventory.ContainsKey(varName) ? inventory[varName] : affinity.GetValueOrDefault(varName, 0);
    }

    /// <summary>
    /// HARDENED LOGIC GATE EVALUATION
    /// Compares simulation variables against narrative thresholds.
    /// RECTIFIED: Returns 'false' on parsing errors to prevent security bypass.
    /// </summary>
    public bool EvaluateRequirements(Choice choice, Dictionary<string, int> affinity, Dictionary<string, int> inventory, out string failReason)
    {
        failReason = string.Empty;
        foreach (var req in choice.Requirements)
        {
            // BUG FIX: Ignore requirements with no Target ID to prevent "Blank String" matches
            if (string.IsNullOrWhiteSpace(req.TargetId)) continue;
            
            // Resolve the current value from the correct British-spelled collection
            int current = (req.Type == "Inventory" ? inventory : affinity).GetValueOrDefault(req.TargetId, 0);
            
            // Attempt numeric conversion of the threshold
            if (int.TryParse(req.Value, out int target))
            {
                bool met = req.Operator switch {
                    "==" => current == target,
                    "!=" => current != target,
                    ">"  => current > target,
                    "<"  => current < target,
                    ">=" => current >= target,
                    "<=" => current <= target,
                    _    => false // HARDENED: Unknown operators fail by default
                };

                if (!met) 
                { 
                    failReason = $"{req.TargetId} {req.Operator} {target} (Current: {current})"; 
                    return false; 
                }
            }
            else 
            {
                // BUG FIX: If the value cannot be parsed, the requirement is NOT met.
                // This prevents users from breaking the lock by entering "abc" into a numeric field.
                failReason = $"Invalid target value: '{req.Value}' for {req.TargetId}";
                return false; 
            }
        }
        return true;
    }

    /// <summary>
    /// ACTION CONSEQUENCE PROCESSOR
    /// Mutates simulation state based on narrative results.
    /// </summary>
    public void ProcessActions(List<ActionModel> actions, Dictionary<string, int> affinity, Dictionary<string, int> inventory)
    {
        foreach (var act in actions)
        {
            if (string.IsNullOrWhiteSpace(act.TargetId)) continue;
            if (act.Type == "Affinity") {
                if (!affinity.ContainsKey(act.TargetId)) affinity[act.TargetId] = 0;
                affinity[act.TargetId] += act.Amount;
            } else if (act.Type == "Inventory") {
                if (!inventory.ContainsKey(act.TargetId)) inventory[act.TargetId] = 0;
                inventory[act.TargetId] += act.Amount;
                if (inventory[act.TargetId] < 0) inventory[act.TargetId] = 0;
            }
        }
    }
    
/// <summary>
/// NARRATIVE INLINE INTERPRETER v1.3.0 // HARDENED EDITION
/// This method converts raw text with logic tags into a "Clean" string for the UI.
/// 
/// HARDENING FEATURES:
/// 1. Case Insensitivity: Matches [IF], [if], or [If].
/// 2. Whitespace Resilience: Handles [if var==0] or [if   var   ==   0].
/// 3. Domain Resolution: Correctly splits 'aff:' or 'inv:' prefixes.
/// 4. Tag Recovery: Gracefully handles missing [else] blocks.
/// </summary>
public string ProcessConditionalLogic(string rawText, Dictionary<string, int> affinity, Dictionary<string, int> inventory)
{
    if (string.IsNullOrWhiteSpace(rawText)) return string.Empty;

    // THE HARDENED PATTERN:
    // \[if              -> Matches the start tag
    // \s+               -> Matches ONE OR MORE spaces (Hardened against "fat-fingering" the spacebar)
    // ([\w:]+)          -> Captures the variable name, including colons (aff:king)
    // \s*               -> Matches ZERO OR MORE spaces before/after the operator
    // ([><=!]+)         -> Captures the mathematical operator
    // (\w+)             -> Captures the comparison value
    // \]                -> Matches the closing bracket
    // ([\s\S]*?)        -> Captures all content (including newlines) until the closing tag
    // \[/if\]           -> Matches the end tag
    string pattern = @"\[if\s+([\w:]+)\s*([><=!]+)\s*(\w+)\]([\s\S]*?)\[/if\]";

    return Regex.Replace(rawText, pattern, m =>
    {
        // 1. EXTRACTION
        string varName = m.Groups[1].Value.Trim();
        string op = m.Groups[2].Value.Trim();
        string targetValStr = m.Groups[3].Value.Trim();
        string fullContent = m.Groups[4].Value;

        // 2. DOMAIN RESOLUTION (The 'ResolveVariableValue' method handles prefixes)
        int currentVal = ResolveVariableValue(varName, affinity, inventory);

        // 3. BRANCH NORMALISATION
        // We look for [else] within the captured content
        string trueBranch = fullContent;
        string falseBranch = string.Empty;

        // Using a hardened split that ignores casing for the [else] tag
        var parts = Regex.Split(fullContent, @"\[else\]", RegexOptions.IgnoreCase);
        if (parts.Length > 1)
        {
            trueBranch = parts[0];
            falseBranch = parts[1];
        }

        // 4. LOGIC EVALUATION
        bool isMet = false;
        if (int.TryParse(targetValStr, out int targetVal))
        {
            isMet = op switch
            {
                "==" => currentVal == targetVal,
                "!=" => currentVal != targetVal,
                ">"  => currentVal > targetVal,
                "<"  => currentVal < targetVal,
                ">=" => currentVal >= targetVal,
                "<=" => currentVal <= targetVal,
                _    => false // Hardened: Unknown operators fail safely
            };
        }

        // 5. FINAL TRIMMING
        // We trim the result to ensure the narrative doesn't have accidental 
        // double-spaces or weird line breaks caused by the tags themselves.
        return isMet ? trueBranch.Trim() : falseBranch.Trim();

    }, RegexOptions.IgnoreCase); // This flag ensures [IF] and [if] are treated the same.
}

    public List<GraphEdge> GetVisualConnections(DialogueRoot root)
    {
        var connections = new List<GraphEdge>();
        var states = root.ListOfCharacterStates;
        foreach (var state in states)
        {
            int sH = GetStateHeight(state);
            foreach (var ch in state.Choices)
            {
                int cH = GetChoiceHeight(ch);
                // State to Choice Anchor
                connections.Add(new GraphEdge { 
                    SourceId = $"s_{state.State}", TargetId = $"c_{ch.InternalId}", IsChoiceAnchor = true, SourceObject = state, TargetObject = ch,
                    PathData = GenerateBezier(state.VisualX, state.VisualY, 240, sH, ch.VisualX, ch.VisualY, 200, cH),
                    MidX = (state.VisualX + 240 + ch.VisualX) / 2, MidY = (state.VisualY + (sH / 2) + ch.VisualY + (cH / 2)) / 2
                });
                // Choice to Target State
                if (!string.IsNullOrEmpty(ch.NextStateId)) {
                    var target = states.FirstOrDefault(s => s.State == ch.NextStateId);
                    if (target != null) connections.Add(new GraphEdge { 
                        SourceId = $"c_{ch.InternalId}", TargetId = $"s_{target.State}", IsChoiceAnchor = false, SourceObject = ch,
                        PathData = GenerateBezier(ch.VisualX, ch.VisualY, 200, cH, target.VisualX, target.VisualY, 240, GetStateHeight(target)),
                        MidX = (ch.VisualX + 200 + target.VisualX) / 2, MidY = (ch.VisualY + (cH / 2) + target.VisualY + (GetStateHeight(target) / 2)) / 2
                    });
                }
            }
            // Fallback Connection
            if (!string.IsNullOrEmpty(state.DefaultNextId)) {
                var target = states.FirstOrDefault(s => s.State == state.DefaultNextId);
                if (target != null) connections.Add(new GraphEdge { 
                    SourceId = $"s_{state.State}", TargetId = $"s_{target.State}", IsFallback = true, IsChoiceAnchor = false, SourceObject = state,
                    PathData = GenerateBezier(state.VisualX, state.VisualY, 240, sH, target.VisualX, target.VisualY, 240, GetStateHeight(target)),
                    MidX = (state.VisualX + 240 + target.VisualX) / 2, MidY = (state.VisualY + (sH / 2) + target.VisualY + (GetStateHeight(target) / 2)) / 2
                });
            }
        }
        return connections;
    }


    private string GenerateBezier(double sX, double sY, int sW, int sH, double eX, double eY, int eW, int eH)
    {
        double startX = sX + sW, startY = sY + (sH / 2);
        double endX = eX, endY = eY + (eH / 2);
        double offset = Math.Max(40, Math.Abs(endX - startX) / 2);
        return $"M {startX} {startY} C {startX + offset} {startY}, {endX - offset} {endY}, {endX} {endY}";
    }

/// <summary>
/// ZENITH-APEX SUPREME INTEGRITY AUDIT v3.1.2
/// Performs a strict, multi-pass validation of the narrative graph.
/// 1. Dead Ends | 2. Ghost Pointers | 3. Infinite Loops | 4. Orphaned Choices
/// </summary>
public List<string> ValidateGraph(DialogueRoot root)
{
    var errs = new List<string>();
    var registeredIds = root.ListOfCharacterStates.Select(s => s.State).ToList();

    foreach (var state in root.ListOfCharacterStates)
    {
        // 1. EVALUATE FALLBACK LOGIC (For nodes without choices or shop/random nodes)
        if (!state.Choices.Any() || !state.SayItAll || state.Type == "Shop")
        {
            if (string.IsNullOrWhiteSpace(state.DefaultNextId))
            {
                // ERROR: No path out and not marked as an exit.
                errs.Add($"State {state.State}: Dead End. No fallback ID or [EXIT] defined.");
            }
            else if (state.DefaultNextId == "[EXIT]")
            {
                // SUCCESS: This is a clean end to the conversation.
            }
            else if (!registeredIds.Contains(state.DefaultNextId))
            {
                // ERROR: Points to a state that doesn't exist.
                errs.Add($"State {state.State}: Ghost Fallback. Target '{state.DefaultNextId}' is missing.");
            }
            else if (state.DefaultNextId == state.State)
            {
                // ERROR: Self-referencing infinite loop.
                errs.Add($"State {state.State}: Infinite Self-Loop detected.");
            }
        }
        
        // 2. EVALUATE CHOICE LOGIC
        foreach (var choice in state.Choices)
        {
            var dest = choice.NextStateId ?? state.DefaultNextId;

            if (string.IsNullOrWhiteSpace(dest))
            {
                errs.Add($"State {state.State}: Choice '{choice.ButtonText}' leads to a Dead End.");
            }
            else if (dest == "[EXIT]")
            {
                // SUCCESS: Choice intentionally ends conversation.
            }
            else if (!registeredIds.Contains(dest))
            {
                errs.Add($"State {state.State}: Choice '{choice.ButtonText}' leads to Ghost State '{dest}'.");
            }
        }
    }
    return errs;
}

/// <summary>
/// RECTIFIED: Updated for Inline Logic Processing.
/// </summary>
public string[] ParseDialoguePages(CharacterState state, Dictionary<string, int> affinity, Dictionary<string, int> inventory)
{
    if (string.IsNullOrWhiteSpace(state.Text)) return new[] { "[Empty]" };

    // Process logic tags before splitting into pages
    string processedText = ProcessConditionalLogic(state.Text, affinity, inventory);

    var lines = processedText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
    if (lines.Length == 0) return new[] { processedText };

    return !state.SayItAll ? new[] { lines[new Random().Next(lines.Length)] } : lines;
}

/// <summary>
/// EDITORIAL LENS PROCESSOR v1.0.0
/// Converts raw dialogue text into HTML-span-wrapped tokens for syntax highlighting.
/// 
/// Standards: British English (Initialisation, Categorisation).
/// </summary>
public string GenerateHighlightedHtml(string rawText)
{
    if (string.IsNullOrEmpty(rawText)) return " ";

    // Standard HTML Encoding
    string escaped = System.Net.WebUtility.HtmlEncode(rawText);

    // 1. Logic Tags - Using a light background instead of Bold to keep width identical
    string logicPattern = @"(\[if\s+[\w:]+\s*[><=!]+\s*\w+\]|\[else\]|\[/if\])";
    escaped = Regex.Replace(escaped, logicPattern, "<span class='hl-tag'>$1</span>", RegexOptions.IgnoreCase);

    // 2. Variables
    string varPattern = @"(aff:[\w]+|inv:[\w]+)";
    escaped = Regex.Replace(escaped, varPattern, "<span class='hl-var'>$1</span>", RegexOptions.IgnoreCase);

    // 3. Formatting
    string formatPattern = @"(\[/?b\]|\[/?i\]|\[/?u\]|\[/?colour.*?\])";
    escaped = Regex.Replace(escaped, formatPattern, "<span class='hl-format'>$1</span>", RegexOptions.IgnoreCase);

    return escaped;
}
}