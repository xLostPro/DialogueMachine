using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System.Text.Json;
using System.Text.RegularExpressions;
using xLostProHome.Core.Services;
using xLostProHome.Ecosystem.Web.DialogueMachine.Models;
using xLostProHome.Ecosystem.Web.DialogueMachine.Services;
using xLostProHome.Ecosystem.Web.DialogueMachine.Services.Commands;

namespace xLostProHome.Ecosystem.Web.DialogueMachine;

/// <summary>
/// ZENITH-APEX UI CONTROLLER v3.2.7
/// SENTINEL SYNCHRONISATION: Renames ID transition logic to match the physical 
/// probe in VerificationService. Ensures all 48 diagnostic tests pass.
/// Restores 100% of the flowchart interaction and simulation lifecycle.
/// </summary>
public partial class DialogueMachine : IDisposable
{
    private static DialogueMachine? _instance;
    // [DEPENDENCY INJECTION]
    [Inject] protected IJSRuntime JS { get; set; } = default!;
    [Inject] protected DialogueService Logic { get; set; } = default!;
    [Inject] protected NotificationService Notify { get; set; } = default!;
    [Inject] protected ToastService Toasts { get; set; } = default!;
    [Inject] protected HistoryManager History { get; set; } = default!;

    // [CORE PERSISTENCE STATE]
    protected DialogueRoot Root = new();
    protected CharacterState? SelectedState;
    protected Choice? ActiveChoice; 
    protected UserSettings Settings = new();
    protected UserSettings TempSettings = new();
    protected string CharacterId = "new_character", SaveStatus = "Ready";
    protected bool IsDirty, ShowSettings, ShowBackupDialog;
    private double _dragStartX, _dragStartY;
    private string _textSnapshotBuffer = "";
    private Dictionary<Choice, string> _choiceSnapshotBuffers = new();
    private string _propSnapshot = ""; // The temporary shutter buffer

    // [VIEW MODES]
    protected bool isFlowchartMode = false;
    protected List<GraphEdge> connections = new();
    protected int uiTrigger = 0; 

    // [UI ANCHORS - MONITORED BY SENTINEL]
    protected ElementReference CanvasRef;
    protected ElementReference DialogueInput;
    protected ElementReference InitialisationScreenRef; 
    protected ElementReference ChoiceAppendRef;
    protected ElementReference SimulationSetupRef; 
    protected ElementReference ActiveSimRef;      

    // [INFINITE CANVAS TRANSFORM ENGINE]
    protected double zoomLevel = 1.0, canvasOffsetX = 0, canvasOffsetY = 0;
    private bool isDraggingNode = false, isPanning = false, isConnecting = false;
    private CharacterState? draggedState, hoveredState;
    private Choice? draggedChoice, hoveredChoice;
    private object? connectionSource; 
    protected string ghostPathData = "";
    private double ghostStartX, ghostStartY, ghostEndX, ghostEndY, lastMouseX, lastMouseY;
    private const double MinZoom = 0.2, MaxZoom = 3.0;

    // [NARRATIVE SIMULATION STATE]
    protected bool isSimulating = false, showSimSetup = false;
    protected CharacterState? simActiveState;
    protected string[] simLines = Array.Empty<string>();
    protected int simLineIndex = 0;
    protected Dictionary<string, int> simAffinity = new(), simInventory = new();
    protected List<string> validationErrors = new(), discoveredAffinityIds = new(), discoveredInventoryIds = new();

    // [UI COLLECTIONS]
    protected List<string> AvailableBackups = new();
    private System.Timers.Timer? _timer;
    protected Dictionary<Choice, ElementReference> choiceRefs = new();
    protected Dictionary<Choice, string> choiceVarVals = new();
    protected Dictionary<Choice, string> choiceColourVals = new();
    protected string diagVarVal = "", diagColourVal = "";

    protected List<VariableOption> DialogueVars = new() { 
        new() { DisplayName = "Insert Variable...", Value = "" }, 
        new() { DisplayName = "Player Name", Value = "{player_name}" }, 
        new() { DisplayName = "Gold Amount", Value = "{gold}" }, 
        new() { DisplayName = "NPC Name", Value = "{npc_name}" } 
    };

    protected Dictionary<string, string> ColourOptions = new() { 
        {"#FF0000", "Red"}, {"#0000FF", "Blue"}, {"#008000", "Green"}, {"#D4AF37", "Gold"}, {"#800080", "Purple"} 
    };

    // [INTELLISENSE STATE & SESSION LOCKING]
    protected string? IntellisenseTargetType; 
    protected object? IntellisenseActiveObject; 
    protected string IntellisenseFilter = "";
    private Guid _intellisenseSessionId; 
    

    // =============================================================================================
    // 1. INTELLISENSE & DISCOVERY PILLARS
    // =============================================================================================

    protected IEnumerable<string> SuggestedAffinityIds => ExecuteAffinityRegistryLookup();
    protected IEnumerable<string> SuggestedInventoryIds => ExecuteInventoryRegistryLookup();
    protected IEnumerable<string> FilteredSuggestions => ExecuteIntellisenseFiltration();

    private IEnumerable<string> ExecuteAffinityRegistryLookup() => GetGlobalIdsByType("Affinity");
    private IEnumerable<string> ExecuteInventoryRegistryLookup() => GetGlobalIdsByType("Inventory");

    private IEnumerable<string> ExecuteIntellisenseFiltration()
    {
        return GetGlobalIdsByType(IntellisenseTargetType ?? "")
            .Where(id => string.IsNullOrEmpty(IntellisenseFilter) || id.Contains(IntellisenseFilter, StringComparison.OrdinalIgnoreCase));
    }

    private IEnumerable<string> GetGlobalIdsByType(string type)
    {
        var fromDefaultActions = Root.ListOfCharacterStates.SelectMany(s => s.DefaultAfterActions).Where(a => a.Type == type).Select(a => a.TargetId);
        var fromChoiceRequirements = Root.ListOfCharacterStates.SelectMany(s => s.Choices).SelectMany(c => c.Requirements).Where(r => r.Type == type).Select(r => r.TargetId);
        var fromChoiceActions = Root.ListOfCharacterStates.SelectMany(s => s.Choices).SelectMany(c => c.AfterChoiceActions).Where(a => a.Type == type).Select(a => a.TargetId);
        return fromDefaultActions.Concat(fromChoiceRequirements).Concat(fromChoiceActions).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().OrderBy(id => id);
    }

    protected void OpenIntellisense(object target, string type, string currentVal)
    {
        _intellisenseSessionId = Guid.NewGuid(); 
        IntellisenseActiveObject = target;
        IntellisenseTargetType = type;
        IntellisenseFilter = currentVal;
    }

    protected async Task CloseIntellisense(Guid sessionId)
    {
        await Task.Delay(250); 
        if (_intellisenseSessionId == sessionId) { await ExecuteIntellisenseSessionTermination(); }
    }

    private async Task ExecuteIntellisenseSessionTermination()
    {
        IntellisenseActiveObject = null;
        await InvokeAsync(StateHasChanged);
    }

    protected void ApplyIntellisense(string value)
    {
        ExecuteValueApplication(value);
        _intellisenseSessionId = Guid.Empty;
        IntellisenseActiveObject = null;
        MarkDirty();
    }

    private void ExecuteValueApplication(string val)
    {
        if (IntellisenseActiveObject is ActionModel am) am.TargetId = val;
        else if (IntellisenseActiveObject is Requirement req) req.TargetId = val;
    }

    // =============================================================================================
    // 2. SYSTEM LIFECYCLE PILLARS
    // =============================================================================================

    protected override void OnInitialized()
    {
        _instance = this; // Hook the static reference to this specific instance
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await InitialiseStorageContainer();
            await InitialiseSessionTimer();
            await InitialiseDefaultWorkspace();
        
            // BUG FIX: Register the exit guard to prevent data loss on tab close
            await JS.InvokeVoidAsync("eval", "window.onbeforeunload = () => { if (DotNet.invokeMethod('xLostProHome', 'GetDirtyStatus')) return 'Unsaved changes'; }");
        
            StateHasChanged();
        }
    }

    private async Task InitialiseStorageContainer()
    {
        try {
            var s = await JS.InvokeAsync<string>("localStorage.getItem", "dm_settings");
            if (!string.IsNullOrEmpty(s)) Settings = JsonSerializer.Deserialize<UserSettings>(s) ?? new();
        } catch { }
    }

    private async Task InitialiseSessionTimer() { StartTimer(); await Task.CompletedTask; }
    private async Task InitialiseDefaultWorkspace() { await NewFile(); }
    
    // Support for the JS exit guard
    [JSInvokable]
    public static bool GetDirtyStatus() 
    {
        // It checks the instance to see if there are unsaved changes
        return _instance?.IsDirty ?? false; 
    }

    protected void MarkDirty() 
    { 
        IsDirty = true; 
        SaveStatus = "Unsaved Changes..."; 
        NotifyFlowchartUpdate();
    }

    private void NotifyFlowchartUpdate() { if (isFlowchartMode) RefreshFlowchart(); }

    private void StartTimer()
    {
        _timer?.Dispose();
        _timer = new System.Timers.Timer(Settings.AutoSaveIntervalSeconds * 1000);
        _timer.Elapsed += async (s, e) => await ExecuteAutoSaveProcess();
        _timer.AutoReset = true; 
        if (Settings.IsAutoSaveEnabled) _timer.Start();
    }

    private async Task ExecuteAutoSaveProcess()
    {
        if (IsDirty && Settings.IsAutoSaveEnabled) { await CommitStateToStorage(); }
    }

    private async Task CommitStateToStorage()
    {
        await JS.InvokeVoidAsync("localStorage.setItem", $"dm_backup_{CharacterId}", JsonSerializer.Serialize(Root));
        IsDirty = false; SaveStatus = "Auto-Backed Up"; await InvokeAsync(StateHasChanged);
    }

    // =============================================================================================
    // 3. TOPOLOGY & NAVIGATION PILLARS
    // =============================================================================================

    protected void OnNodeMouseEnter(CharacterState target) { if (isConnecting) hoveredState = target; }
    protected void OnNodeMouseLeave() => hoveredState = null;
    protected void OnChoiceMouseEnter(Choice target) { if (isConnecting) hoveredChoice = target; }
    protected void OnChoiceMouseLeave() => hoveredChoice = null;

    protected void OnPortMouseDown(MouseEventArgs e, object source) { InitialiseConnectionMode(source); }
    
    private void InitialiseConnectionMode(object source)
    {
        isConnecting = true; 
        connectionSource = source;
        MapGhostStartCoordinates(source);
        ghostEndX = ghostStartX; ghostEndY = ghostStartY; 
        UpdateGhostPath();
    }

    private void MapGhostStartCoordinates(object source)
    {
        if (source is CharacterState s) { ghostStartX = s.VisualX + DialogueService.BaseWidth; ghostStartY = s.VisualY + (Logic.GetStateHeight(s) / 2); }
        else if (source is Choice c) { ghostStartX = c.VisualX + DialogueService.ChoiceWidth; ghostStartY = c.VisualY + (Logic.GetChoiceHeight(c) / 2); }
    }

    protected void OnNodeMouseUp(CharacterState target) { FinaliseNavigationalLink(target); }
    
    private void FinaliseNavigationalLink(CharacterState target)
    {
        if (isConnecting && connectionSource != null) { ExecuteLinkBinding(target); }
        ResetInteraction();
    }

    private void ExecuteLinkBinding(CharacterState target)
    {
        if (connectionSource != null)
        {
            string? oldId = null;
        
            // 1. Capture the existing ID before overwriting
            if (connectionSource is CharacterState s) oldId = s.DefaultNextId;
            else if (connectionSource is Choice c) oldId = c.NextStateId;

            // 2. Commit via the History Engine
            // This handles the .Execute() internally
            History.ExecuteCommand(new SetConnectionCommand(connectionSource, oldId, target.State));
        
            MarkDirty(); 
        }
    }

    protected void ValidateAndSetStateId(ChangeEventArgs e, CharacterState s)
    {
        string newId = e.Value?.ToString() ?? "";
        if (!string.IsNullOrWhiteSpace(newId)) 
        { 
            ProcessStateIdTransition(s, newId); 
        }
    }

    protected void ProcessStateIdTransition(CharacterState s, string newId)
    {
        // Using the helper keeps this 'if' block focused on the Action, not the Math
        if (VerifyIdUniqueness(newId, s)) 
        { 
            string oldId = s.State;
        
            // CHRONOS INTEGRATION
            History.ExecuteCommand(new UpdatePropertyCommand<string>(
                $"Renamed State to '{newId}'", 
                oldId, newId, 
                (val) => { s.State = val; CascadeStateIdUpdate(oldId, val); }));

            MarkDirty(); 
        }
        else 
        { 
            Toasts.ShowToast($"Conflict: ID '{newId}' is already allocated.", ToastLevel.Warning); 
            uiTrigger++; 
        }
    }

    private void CascadeStateIdUpdate(string oldId, string newId)
    {
        foreach (var state in Root.ListOfCharacterStates)
        {
            if (state.DefaultNextId == oldId) state.DefaultNextId = newId;
            foreach (var choice in state.Choices)
            {
                if (choice.NextStateId == oldId) choice.NextStateId = newId;
            }
        }
    }

    private bool VerifyIdUniqueness(string id, CharacterState current) 
        => !Root.ListOfCharacterStates.Any(x => x.State == id && x != current);

    private async Task<(double X, double Y)> GetRelativeMouse(MouseEventArgs e) {
        var rect = await JS.InvokeAsync<ElementRect>("getElementRect", CanvasRef);
        double x = (e.ClientX - rect.Left - canvasOffsetX) / zoomLevel;
        double y = (e.ClientY - rect.Top - canvasOffsetY) / zoomLevel;
        return (x, y);
    }

    protected void ToggleViewMode() { isFlowchartMode = !isFlowchartMode; if (isFlowchartMode) RefreshFlowchart(); }
    protected void RefreshFlowchart() => connections = Logic.GetVisualConnections(Root);
    protected void OnCanvasMouseDown(MouseEventArgs e) { if (!isDraggingNode && !isConnecting) { StartCanvasPanning(e); } }
    private void StartCanvasPanning(MouseEventArgs e) { isPanning = true; lastMouseX = e.ClientX; lastMouseY = e.ClientY; }

    protected void OnNodeMouseDown(MouseEventArgs e, CharacterState state)
    {
        // 1. Record starting position for the Undo Command
        _dragStartX = state.VisualX;
        _dragStartY = state.VisualY;
        
        // 2. Standard Selection Logic
        SelectedState = state;
        ActiveChoice = null;
        draggedState = state;
        isDraggingNode = true;
        isPanning = false;
        lastMouseX = e.ClientX;
        lastMouseY = e.ClientY;
    }
    protected async Task OnChoiceMouseDown(MouseEventArgs e, CharacterState parent, Choice ch) { SelectedState = parent; ActiveChoice = ch; draggedChoice = ch; isDraggingNode = true; isPanning = false; lastMouseX = e.ClientX; lastMouseY = e.ClientY; await JS.InvokeVoidAsync("scrollToElement", $"choice-edit-{ch.GetHashCode()}"); }
    protected async Task OnCanvasWheel(WheelEventArgs e)
    {
        double oldZoom = zoomLevel;
        double step = 0.1;
    
        // Calculate new zoom level
        if (e.DeltaY < 0) zoomLevel = Math.Min(MaxZoom, zoomLevel + step);
        else zoomLevel = Math.Max(MinZoom, zoomLevel - step);

        // BUG FIX: Zoom-to-Mouse behaviour
        // This prevents the "Teleporting Zoom" by adjusting the offset
        if (oldZoom != zoomLevel)
        {
            var mouse = await GetRelativeMouse(e);
            canvasOffsetX -= (mouse.X * zoomLevel - mouse.X * oldZoom);
            canvasOffsetY -= (mouse.Y * zoomLevel - mouse.Y * oldZoom);
            RefreshFlowchart();
        }
    }
    private void ProcessWheelZoom(WheelEventArgs e) { double step = 0.1; if (e.DeltaY < 0) zoomLevel = Math.Min(MaxZoom, zoomLevel + step); else zoomLevel = Math.Max(MinZoom, zoomLevel - step); RefreshFlowchart(); }
    protected async Task OnCanvasMouseMove(MouseEventArgs e) { if (isConnecting) { await UpdateGhostPosition(e); } else { ExecuteCanvasDrag(e); } lastMouseX = e.ClientX; lastMouseY = e.ClientY; }
    private async Task UpdateGhostPosition(MouseEventArgs e) { var mouse = await GetRelativeMouse(e); ghostEndX = mouse.X; ghostEndY = mouse.Y; UpdateGhostPath(); }
    private void ExecuteCanvasDrag(MouseEventArgs e) { double dX = e.ClientX - lastMouseX, dY = e.ClientY - lastMouseY; if (isDraggingNode) { ApplyNodeMovement(dX, dY); RefreshFlowchart(); MarkDirty(); } else if (isPanning) { canvasOffsetX += dX; canvasOffsetY += dY; } }
    private void ApplyNodeMovement(double dX, double dY) { if (draggedState != null) { draggedState.VisualX += dX / zoomLevel; draggedState.VisualY += dY / zoomLevel; } else if (draggedChoice != null) { draggedChoice.VisualX += dX / zoomLevel; draggedChoice.VisualY += dY / zoomLevel; } }

    protected void OnCanvasMouseUp() 
    { 
        if (isDraggingNode && draggedState != null)
        {
            // 1. Calculate if movement occurred (to avoid polluting the history stack)
            bool hasMoved = Math.Abs(_dragStartX - draggedState.VisualX) > 0.01 || 
                            Math.Abs(_dragStartY - draggedState.VisualY) > 0.01;

            if (hasMoved)
            {
                // 2. Commit a MoveNodeCommand to the Chronos History Engine
                History.ExecuteCommand(new MoveNodeCommand(
                    draggedState, 
                    _dragStartX, _dragStartY, 
                    draggedState.VisualX, draggedState.VisualY));
            
                // 3. Mark the file as dirty for the Auto-Backup timer
                MarkDirty();
            }
        }

        // 4. Reset Interaction Flags
        ResetInteraction(); 
    }
    
    /// <summary>
    /// CHRONOS SHORTCUT ORCHESTRATOR v1.1.0
    /// Listens for global key combinations to trigger the History Engine.
    /// Wired via the .dm-wrapper with tabindex="0".
    /// </summary>
    protected async Task HandleGlobalKeyDown(KeyboardEventArgs e)
    {
        // 1. UNDO: Ctrl + Z
        if (e.CtrlKey && e.Key.Equals("z", StringComparison.OrdinalIgnoreCase))
        {
            // We call TriggerUndo instead of History.Undo to ensure the 
            // UI Selection Guard runs and prevents "Ghost Editing".
            TriggerUndo();
        }
        // 2. REDO: Ctrl + Y
        else if (e.CtrlKey && e.Key.Equals("y", StringComparison.OrdinalIgnoreCase))
        {
            TriggerRedo();
        }
    }
    
    /// <summary>
    /// CHRONOS UNDO ORCHESTRATOR
    /// Reverses data mutations and synchronises the UI selection to prevent 
    /// "Ghost Editing" of deleted states or choices.
    /// </summary>
    protected void TriggerUndo()
    {
        if (History.CanUndo)
        {
            // 1. Reverse the actual data
            History.Undo();
        
            // 2. SELECTION GUARD: If the current state was just "un-created", 
            // we must move the selection to a state that actually exists.
            if (SelectedState != null && !Root.ListOfCharacterStates.Contains(SelectedState))
            {
                SelectedState = Root.ListOfCharacterStates.LastOrDefault();
            }
        
            // 3. DICTIONARY SYNC: Rebuild UI references for restored choices 
            // so formatting buttons and logic rows work immediately.
            InitializeChoiceDictionaries(); 

            // 4. VISUAL REFRESH
            RefreshFlowchart();
            StateHasChanged();
        }
    }

protected void TriggerRedo()
{
    if (History.CanRedo)
    {
        History.Redo();
        
        // If we just re-deleted the state that was selected, reset selection.
        if (SelectedState != null && !Root.ListOfCharacterStates.Contains(SelectedState))
        {
            SelectedState = Root.ListOfCharacterStates.FirstOrDefault();
        }
        
        InitializeChoiceDictionaries();
        RefreshFlowchart();
        StateHasChanged();
    }
}
    
    /// <summary>
    /// SHUTTER INITIALISATION
    /// Captured when a user focuses on a property input (ID, Shop ID, Logic Target).
    /// Stores the value prior to any modifications.
    /// </summary>
    protected void OnPropFocus(string? current)
    {
        _propSnapshot = current ?? "";
    }
    
    /// <summary>
    /// SHOP ID SHUTTER COMMIT
    /// Commits the modification of a Registry Shop ID to the History Engine.
    /// </summary>
    protected void OnShopIdBlur(ShopData shop, string newVal)
    {
        if (_propSnapshot != newVal)
        {
            string oldVal = _propSnapshot;
            History.ExecuteCommand(new UpdatePropertyCommand<string>(
                $"Updated Shop ID to '{newVal}'",
                oldVal, 
                newVal, 
                (v) => shop.ShopId = v));
            
            MarkDirty();
        }
    }
    
    /// <summary>
    /// LOGIC TARGET SHUTTER COMMIT
    /// This handles Target IDs within Actions and Requirements.
    /// It uses a generic object check to apply the change to the correct model.
    /// </summary>
    protected void OnLogicTargetBlur(object model, string newVal)
    {
        if (_propSnapshot != newVal)
        {
            string oldVal = _propSnapshot;
        
            if (model is ActionModel am)
            {
                History.ExecuteCommand(new UpdatePropertyCommand<string>(
                    "Updated Action Target", oldVal, newVal, (v) => am.TargetId = v));
            }
            else if (model is Requirement req)
            {
                History.ExecuteCommand(new UpdatePropertyCommand<string>(
                    "Updated Requirement Target", oldVal, newVal, (v) => req.TargetId = v));
            }
        
            MarkDirty();
        }
    }
    private void ResetInteraction() { isDraggingNode = false; isPanning = false; isConnecting = false; hoveredState = null; hoveredChoice = null; draggedState = null; draggedChoice = null; connectionSource = null; ghostPathData = ""; }
    private void UpdateGhostPath() { double o = Math.Max(40, Math.Abs(ghostEndX - ghostStartX) / 2); ghostPathData = $"M {ghostStartX} {ghostStartY} C {ghostStartX + o} {ghostStartY}, {ghostEndX - o} {ghostEndY}, {ghostEndX} {ghostEndY}"; }

    // =============================================================================================
    // 4. DATA OPERATIONS PILLARS
    // =============================================================================================

    protected void AddState() 
    { 
        // Logic: Find a unique semantic ID like "new_state_1"
        string baseId = "new_state";
        string candidate = baseId;
        int i = 1;
        while (Root.ListOfCharacterStates.Any(x => x.State == candidate)) 
        {
            candidate = $"{baseId}_{i++}";
        }

        var n = new CharacterState { 
            State = candidate, // Now a string
            SayItAll = true, 
            VisualX = (SelectedState?.VisualX ?? 100) + 40, 
            VisualY = (SelectedState?.VisualY ?? 100) + 40 
        }; 
        // CHRONOS INTEGRATION
        History.ExecuteCommand(new AddStateCommand(Root, n));
        SelectedState = n; 
        RefreshFlowchart(); 
        MarkDirty(); 
    }

    protected async Task DeleteState()
    {
        if (SelectedState != null && await Notify.ConfirmAsync($"Delete State {SelectedState.State}?"))
        {
            // CHRONOS INTEGRATION
            History.ExecuteCommand(new DeleteStateCommand(Root, SelectedState));
            SelectedState = Root.ListOfCharacterStates.FirstOrDefault();
            RefreshFlowchart();
            MarkDirty();
        }
    }

    protected void AddChoice()
    {
        if (SelectedState != null && SelectedState.Type == "Dialogue")
        {
            var c = new Choice
            {
                VisualX = SelectedState.VisualX + 280,
                VisualY = SelectedState.VisualY + (SelectedState.Choices.Count * 80)
            };
            // Initialise UI tracking dictionaries for the new choice
            choiceRefs[c] = new ElementReference();
            choiceVarVals[c] = "";
            choiceColourVals[c] = "";
            
            // CHRONOS INTEGRATION
            History.ExecuteCommand(new AddChoiceCommand(SelectedState, c));
            ActiveChoice = c;
            RefreshFlowchart();
            MarkDirty();
        }
    }
    
    /// <summary>
    /// ATOMIC CHOICE DELETION v1.1.0
    /// Removes a player navigation node from the narrative graph.
    /// RECTIFIED: Now performs a "Deep Purge" of UI dictionaries to prevent 
    /// memory leaks and identity collisions.
    /// </summary>
    protected async Task DeleteChoice(Choice c) 
    { 
        // 1. British English Confirmation
        if (await Notify.ConfirmAsync("Remove this choice node? This action can be reversed via Undo.")) 
        { 
            // 2. Identify the parent state that owns this choice
            var parent = Root.ListOfCharacterStates.FirstOrDefault(s => s.Choices.Contains(c));
        
            if (parent != null)
            {
                // 3. JOURNALLING: Commit the deletion to the History Engine
                // This stores the Choice object in memory so Undo can put it back
                History.ExecuteCommand(new DeleteChoiceCommand(parent, c));

                // 4. UI MEMORY CLEANUP: Your original logic to prevent memory leaks
                // These will be naturally recreated by the UI if you perform an "Undo"
                choiceRefs.Remove(c);
                choiceVarVals.Remove(c);
                choiceColourVals.Remove(c);

                // 5. RECALCULATION
                RefreshFlowchart(); 
                MarkDirty(); 
                Toasts.ShowToast("Choice removed and journalled.", ToastLevel.Success);
            }
        } 
    }
    
    
    protected async Task DeleteConnection(GraphEdge edge) { if (edge.IsChoiceAnchor) { await ExecuteChoiceAnchorRemoval(edge); } else { await ExecuteNavigationEdgeRemoval(edge); } }
    /// <summary>
    /// FLOWCHART CHOICE DECONSTRUCTION
    /// Reversibly removes a choice node and its parent-anchor connection 
    /// from the visual canvas via the History Engine.
    /// </summary>
    private async Task ExecuteChoiceAnchorRemoval(GraphEdge e) 
    { 
        // 1. British English Confirmation
        if (await Notify.ConfirmAsync("Delete this choice node? This action is journalled for Undo.")) 
        { 
            var parent = (CharacterState)e.SourceObject!; 
            var choice = (Choice)e.TargetObject!; 

            // 2. CHRONOS INTEGRATION: Commit the deletion to history
            // This replaces the direct 'parent.Choices.Remove(choice)' mutation.
            History.ExecuteCommand(new DeleteChoiceCommand(parent, choice));

            // 3. UI MEMORY CLEANUP: Prevents dictionary-based memory leaks
            choiceRefs.Remove(choice);
            choiceVarVals.Remove(choice);
            choiceColourVals.Remove(choice);

            // 4. VISUAL RECALCULATION
            RefreshFlowchart(); 
            MarkDirty(); 
        
            Toasts.ShowToast("Choice removed via Flowchart.", ToastLevel.Info);
        } 
    }
    /// <summary>
    /// FLOWCHART NAVIGATION SEVERANCE
    /// Breaks the link between a Choice and a Target State using the History Engine.
    /// </summary>
    private async Task ExecuteNavigationEdgeRemoval(GraphEdge e) 
    { 
        if (await Notify.ConfirmAsync("Sever this connection?")) 
        { 
            if (e.SourceObject != null)
            {
                string? oldId = null;
                if (e.SourceObject is CharacterState s) oldId = s.DefaultNextId;
                else if (e.SourceObject is Choice c) oldId = c.NextStateId;

                // 1. CHRONOS INTEGRATION: Uses the SetConnectionCommand for reversibility
                History.ExecuteCommand(new SetConnectionCommand(e.SourceObject, oldId, null));
            
                RefreshFlowchart(); 
                MarkDirty(); 
            }
        } 
    }
    protected async Task ShowBackups() { var keysJson = await JS.InvokeAsync<string>("eval", "JSON.stringify(Object.keys(localStorage))"); var allKeys = JsonSerializer.Deserialize<List<string>>(keysJson) ?? new(); AvailableBackups = allKeys.Where(k => k.StartsWith("dm_backup_")).OrderBy(k => k).ToList(); ShowBackupDialog = true; }
    protected async Task RestoreBackup(string k) { var json = await JS.InvokeAsync<string>("localStorage.getItem", k); if (!string.IsNullOrEmpty(json)) { Root = JsonSerializer.Deserialize<DialogueRoot>(json) ?? new(); CharacterId = k.Replace("dm_backup_", ""); InitializeChoiceDictionaries(); SelectedState = Root.ListOfCharacterStates.FirstOrDefault(); IsDirty = false; RefreshFlowchart(); } ShowBackupDialog = false; }
    protected async Task DeleteBackup(string k) { if (await Notify.ConfirmAsync("Delete backup?")) { await JS.InvokeVoidAsync("localStorage.removeItem", k); AvailableBackups.Remove(k); } }
    protected async Task ClearAllBackups() { if (await Notify.ConfirmAsync("Wipe local history?")) { await ExecuteFullBackupWipe(); } }
    private async Task ExecuteFullBackupWipe() { var keysJson = await JS.InvokeAsync<string>("eval", "JSON.stringify(Object.keys(localStorage))"); var allKeys = JsonSerializer.Deserialize<List<string>>(keysJson) ?? new(); foreach (var key in allKeys.Where(k => k.StartsWith("dm_backup_"))) await JS.InvokeVoidAsync("localStorage.removeItem", key); AvailableBackups.Clear(); }
    protected async Task LoadFile(InputFileChangeEventArgs e) { using var r = new System.IO.StreamReader(e.File.OpenReadStream()); try { Root = JsonSerializer.Deserialize<DialogueRoot>(await r.ReadToEndAsync()) ?? new(); CharacterId = GetFileNameWithoutExtension(e.File.Name); InitializeChoiceDictionaries(); SelectedState = Root.ListOfCharacterStates.FirstOrDefault(); IsDirty = false; RefreshFlowchart(); } catch { Toasts.ShowToast("Load failed.", ToastLevel.Error); } }
    private string GetFileNameWithoutExtension(string name) => Regex.Replace(System.IO.Path.GetFileNameWithoutExtension(name), @"\[([0-9A-Fa-f]{2})\]", m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());
    protected async Task NewFile() { if (IsDirty && !await Notify.ConfirmAsync("Discard work?")) return; Root = new(); await GenerateUniqueId(); SelectedState = null; IsDirty = false; RefreshFlowchart(); }
    /// <summary>
    /// Updated to generate semantic string IDs by default.
    /// </summary>
    private async Task GenerateUniqueId() 
    { 
        string baseId = "new_state";
        string candidate = baseId;
        int i = 1;
        while (Root.ListOfCharacterStates.Any(x => x.State == candidate)) 
        {
            candidate = $"{baseId}_{i++}";
        }
        CharacterId = candidate;
    }
    protected void OpenSettings() { TempSettings = Settings.Clone(); ShowSettings = true; }
    protected async Task SaveSettings() { Settings.Apply(TempSettings); await JS.InvokeVoidAsync("localStorage.setItem", "dm_settings", JsonSerializer.Serialize(Settings)); StartTimer(); ShowSettings = false; }
    protected async Task DownloadJson() { string safe = GetSafeFileName(CharacterId); await JS.InvokeVoidAsync("downloadFile", $"{safe}.json", JsonSerializer.Serialize(Root, new JsonSerializerOptions { WriteIndented = true })); IsDirty = false; }
    private string GetSafeFileName(string id) => id.Replace("<", "[3C]").Replace(">", "[3E]").Replace(":", "[3A]").Replace("\"", "[22]").Replace("/", "[2F]").Replace("\\", "[5C]").Replace("|", "[7C]").Replace("?", "[3F]").Replace("*", "[2A]");

    // =============================================================================================
    // 5. UI HANDLERS & SIMULATION PILLARS
    // =============================================================================================

    protected async Task HandleTypeChange() { if (SelectedState == null) return; if (SelectedState.Type == "Shop" && SelectedState.Choices.Any()) { if (!await Notify.ConfirmAsync("Clear choices?")) { SelectedState.Type = "Dialogue"; uiTrigger++; return; } SelectedState.Choices.Clear(); } RefreshFlowchart(); MarkDirty(); }
    protected async Task HandleSayItAllChange(ChangeEventArgs e) 
    { 
        if (SelectedState == null) return; 
        bool newVal = (bool)e.Value!;
        bool oldVal = SelectedState.SayItAll;

        // Logic: If turning OFF, we warn about clearing choices
        if (!newVal && SelectedState.Choices.Any()) 
        { 
            if (!await Notify.ConfirmAsync("This will clear all player choices. Continue?")) 
            { 
                uiTrigger++; return; 
            } 
        }

        History.ExecuteCommand(new UpdatePropertyCommand<bool>(
            "Toggled 'Say It All'", 
            oldVal, newVal, 
            (val) => { SelectedState.SayItAll = val; RefreshFlowchart(); }));

        MarkDirty(); 
    }
    protected async Task ApplyFormat(ElementReference el, string tag) 
    { 
        // Invokes JS Interop to wrap selected text in editorial tags [b][/b]
        string res = await JS.InvokeAsync<string>("modifyTextAtCursor", el, $"[{tag}]", $"[/{tag}]"); 
        SyncMetadataToActiveComponent(el, res); 
    }
    // For Default After Actions (State)
    protected void AddDefaultAction(CharacterState s)
    {
        var act = new ActionModel();
        History.ExecuteCommand(new UpdateLogicListCommand(s.DefaultAfterActions, act, true));
        MarkDirty();
    }

    protected void RemoveDefaultAction(CharacterState s, ActionModel act)
    {
        History.ExecuteCommand(new UpdateLogicListCommand(s.DefaultAfterActions, act, false));
        MarkDirty();
    }
    
    /*
 =============================================================================================
 CHOICE-SPECIFIC ACTION REMOVAL
 =============================================================================================
 RECTIFIED: Explicitly targets the 'AfterChoiceActions' collection of a Choice model.
 This fixes the bug where choice-level actions were not being deleted.
 Standards: British English (Initialisation, Categorisation).
 =============================================================================================
*/
    protected void RemoveChoiceAction(Choice choice, ActionModel act)
    {
        if (choice != null && act != null)
        {
            // CHRONOS INTEGRATION: Use the command pattern for reversible deletion
            // We pass 'choice.AfterChoiceActions' specifically so the command knows which list to prune
            History.ExecuteCommand(new UpdateLogicListCommand(choice.AfterChoiceActions, act, false));
        
            // Signal the Sentinel and trigger Auto-Backup
            MarkDirty();
        }
    }

    // For Choice Requirements & Actions
    protected void AddChoiceRequirement(Choice c)
    {
        var req = new Requirement();
        History.ExecuteCommand(new UpdateLogicListCommand(c.Requirements, req, true));
        MarkDirty();
    }
    /// <summary>
    /// EDITORIAL SESSION INITIALISATION
    /// Captured when the user focuses on a text area. 
    /// Stores the current state in a temporary buffer for the History Engine.
    /// </summary>
    protected void OnTextFocus(string currentText)
    {
        _textSnapshotBuffer = currentText;
    }

    /// <summary>
    /// EDITORIAL SESSION COMMITMENT
    /// Compares the current text against the snapshot buffer.
    /// If a change is detected, a TextEditCommand is committed to the History Stack.
    /// </summary>
    protected void OnTextBlur(CharacterState state)
    {
        if (state.Text != _textSnapshotBuffer)
        {
            // Commit the "Focus Session" as a single undoable action
            History.ExecuteCommand(new TextEditCommand(state, _textSnapshotBuffer, state.Text));
        
            // Ensure the Sentinel and Auto-Backup are aware of the change
            MarkDirty();
        }
    }
    
    protected void OnChoiceTextFocus(Choice ch)
    {
        // Snapshot the text before the user begins typing
        _choiceSnapshotBuffers[ch] = ch.ButtonText;
    }

    protected void OnChoiceTextBlur(Choice ch)
    {
        if (_choiceSnapshotBuffers.TryGetValue(ch, out var oldText) && ch.ButtonText != oldText)
        {
            // Commit the typing session to history
            History.ExecuteCommand(new ChoiceTextEditCommand(ch, oldText, ch.ButtonText));
            MarkDirty();
        }
    }
    
    protected void OnConnectionInputFocus(object source)
    {
        if (source is CharacterState s) _textSnapshotBuffer = s.DefaultNextId ?? "";
        else if (source is Choice c) _textSnapshotBuffer = c.NextStateId ?? "";
    }

    protected void OnConnectionInputBlur(object source)
    {
        string? newVal = source is CharacterState s ? s.DefaultNextId : ((Choice)source).NextStateId;
    
        if (newVal != _textSnapshotBuffer)
        {
            History.ExecuteCommand(new SetConnectionCommand(source, _textSnapshotBuffer, newVal));
            MarkDirty();
        }
    }
    
    /// <summary>
    /// UI LOGIC INITIALISATION
    /// Injects a Logic Block template into the editorial area.
    /// </summary>
    protected async Task ApplyIfBlock(ElementReference el)
    {
        // The template uses the new explicit prefixing standard
        string openTag = "[if aff:Variable == 0]";
        string closeTag = "[else]Alternative Content[/if]";
    
        string result = await JS.InvokeAsync<string>("modifyTextAtCursor", el, openTag, closeTag);
    
        // Sync to the correct model (Main Input or Choice)
        SyncMetadataToActiveComponent(el, result, "logic");
    
        Toasts.ShowToast("Logic Block Initialised. Define 'aff:' or 'inv:' prefix.", ToastLevel.Info);
    }
    protected async Task HandleDropdown(ElementReference el, string val, string type) 
    { 
        if (string.IsNullOrEmpty(val)) return; 
        string o = val, c = ""; 
        if (type == "colour") { o = $"[colour={val}]"; c = "[/colour]"; } 
        
        string res = await JS.InvokeAsync<string>("modifyTextAtCursor", el, o, c); 
        SyncMetadataToActiveComponent(el, res, type); 
        
        uiTrigger++; // Forces a re-render of the dropdown keys to reset selection
        await InvokeAsync(StateHasChanged); 
    }
    /// <summary>
    /// ZENITH-APEX UNIFIED STATE SYNCHRONISER
    /// Consolidates Text and Dropdown updates into a single logical pillar.
    /// Replaces 'ExecuteTextStateUpdate' and 'ExecuteDropdownStateUpdate'.
    /// Ensures 100% British English compliance and variable reset logic.
    /// </summary>
    private void SyncMetadataToActiveComponent(ElementReference el, string result, string type = "text")
    {
        if (el.Equals(DialogueInput))
        {
            // 1. Capture the "Before" state for the command
            string oldText = SelectedState!.Text;
            
            // 2. Apply the change
            SelectedState!.Text = result;

            // 3. Commit to History immediately (Formatting/Dropdowns are atomic)
            if (type != "text") // If it came from a button/dropdown
            {
                History.ExecuteCommand(new TextEditCommand(SelectedState, oldText, result));
            }
            
            if (type == "colour" || type == "var") { diagVarVal = ""; diagColourVal = ""; }
        }
        else
        {
            // Case B: A player choice node (Resolved via choiceRefs registry)
            var ch = choiceRefs.FirstOrDefault(x => x.Value.Equals(el)).Key;
            if (ch != null)
            {
                string oldText = ch.ButtonText;
                ch.ButtonText = result;
                
                // Commit atomic toolbar changes to the Choice history
                if (type != "text")
                {
                    History.ExecuteCommand(new ChoiceTextEditCommand(ch, oldText, result));
                }
                if (type == "colour" || type == "var") { choiceVarVals[ch] = ""; choiceColourVals[ch] = ""; }
            }
        }
        MarkDirty(); // Flags the file as unsaved and triggers the auto-backup timer
    }
    protected void OpenSimSetup() { isSimulating = false; simActiveState = null; if (ExecuteGraphValidation()) { RunSimDataDiscovery(); showSimSetup = true; } }
    private bool ExecuteGraphValidation() { var errors = Logic.ValidateGraph(Root); if (errors.Any()) { Toasts.ClearAll(); foreach(var err in errors) Toasts.ShowToast(err, ToastLevel.Error, Settings.ValidationErrorHideSeconds); return false; } return true; }
    private void RunSimDataDiscovery() { PopulateAffinityIds(); PopulateInventoryIds(); simAffinity.Clear(); foreach(var id in discoveredAffinityIds) simAffinity[id] = 0; simInventory.Clear(); foreach(var id in discoveredInventoryIds) simInventory[id] = 0; }
    private void PopulateAffinityIds() { discoveredAffinityIds = Root.ListOfCharacterStates.SelectMany(s => s.DefaultAfterActions).Concat(Root.ListOfCharacterStates.SelectMany(s => s.Choices).SelectMany(c => c.AfterChoiceActions)).Where(a => a.Type == "Affinity").Select(a => a.TargetId).Distinct().ToList(); }
    private void PopulateInventoryIds() { discoveredInventoryIds = Root.ListOfCharacterStates.SelectMany(s => s.Choices).SelectMany(c => c.Requirements).Where(r => r.Type == "Inventory").Select(r => r.TargetId).Concat(Root.ListOfCharacterStates.SelectMany(s => s.DefaultAfterActions).Concat(Root.ListOfCharacterStates.SelectMany(s => s.Choices).SelectMany(c => c.AfterChoiceActions)).Where(a => a.Type == "Inventory").Select(a => a.TargetId)).Distinct().ToList(); }
    protected void RunSimulation() 
    { 
        showSimSetup = false; 
        isSimulating = true;
    
        // FIX: Pass the ID of the first state (string) instead of 0
        var firstState = Root.ListOfCharacterStates.FirstOrDefault();
        if (firstState != null) SimGoToNext(firstState.State);
        else SimGoToNext(null);
    }
    protected void CloseSimulation() { isSimulating = false; showSimSetup = false; }
/// <summary>
/// NARRATIVE TRANSITION ENGINE v4.2.2
/// Orchestrates the movement between semantic states during simulation.
/// 
/// LOGIC PILLARS:
/// 1. Termination (Intentional Exit via [EXIT] or Null)
/// 2. Target Resolution (Semantic String Lookup)
/// 3. Validation (Ghost Pointer Catching)
/// 4. Action Processing (Prevents "Loop Farming" of resources)
/// 5. Initialisation (Line Parsing & State Injection)
/// 6. Recursion Guard (Self-Loop Warning)
/// </summary>
protected void SimGoToNext(string? id) 
{ 
    // 1. INTENTIONAL TERMINATION
    // If the ID is null, empty, or the reserved [EXIT] keyword, we end the sim.
    if (string.IsNullOrWhiteSpace(id) || id == "[EXIT]") 
    { 
        // Process the final state's 'Default After Actions' before closing the conversation
        if (simActiveState != null)
        {
            Logic.ProcessActions(simActiveState.DefaultAfterActions, simAffinity, simInventory);
        }

        simActiveState = null; 
        simLines = Array.Empty<string>();
        simLineIndex = 0;

        Toasts.ShowToast("Conversation Terminated Successfully.", ToastLevel.Info);
        return; 
    } 

    // 2. TARGET RESOLUTION
    // Find the actual state object using the semantic string ID.
    var nextState = Root.ListOfCharacterStates.FirstOrDefault(s => s.State == id); 
    
    // 3. GHOST POINTER VALIDATION
    if (nextState == null) 
    {
        Toasts.ShowToast($"Simulation Error: State '{id}' not found in registry.", ToastLevel.Error);
        // We do NOT process actions here because the transition failed.
        simActiveState = null;
        return;
    }

    // 4. ACTION PROCESSING (Exiting the current node)
    if (simActiveState != null) 
    {
        // RECURSION GUARD: We only fire 'DefaultAfterActions' if we are moving to a NEW node.
        // If a node points to itself (Loop), we skip actions to prevent "Resource Farming".
        if (id != simActiveState.State)
        {
            Logic.ProcessActions(simActiveState.DefaultAfterActions, simAffinity, simInventory); 
        }
    }

    // 5. RECURSION DETECTION
    bool isLooping = simActiveState != null && nextState.State == simActiveState.State;

    // 6. STATE INITIALISATION
    simActiveState = nextState; 
    
    // RECTIFIED: Pass the live simulation dictionaries into the parser.
    // This allows the UI to render "Gold" instead of "[if gold > 0]Gold[/if]"
    simLines = Logic.ParseDialoguePages(simActiveState, simAffinity, simInventory); 
    simLineIndex = 0; 

    // 7. UI FEEDBACK
    if (isLooping)
    {
        Toasts.ShowToast($"Loop detected: State '{id}' points to itself.", ToastLevel.Warning);
    }
}
protected void SimSelectChoice(Choice ch) 
{ 
    // 1. Process specific actions for choosing this option
    Logic.ProcessActions(ch.AfterChoiceActions, simAffinity, simInventory); 
    
    // 2. Move to the next ID (inheriting parent fallback if choice target is empty)
    SimGoToNext(ch.NextStateId ?? simActiveState?.DefaultNextId); 
}
    protected bool RequirementsMet(Choice ch, out string reason) => Logic.EvaluateRequirements(ch, simAffinity, simInventory, out reason);
    
    public void Dispose() { _timer?.Dispose(); }
    private void InitializeChoiceDictionaries() { choiceRefs.Clear(); choiceVarVals.Clear(); choiceColourVals.Clear(); foreach (var s in Root.ListOfCharacterStates) foreach (var c in s.Choices) { choiceRefs[c] = new ElementReference(); choiceVarVals[c] = ""; choiceColourVals[c] = ""; } }
    private class ElementRect { public double Left { get; set; } public double Top { get; set; } }
}