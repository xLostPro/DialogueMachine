using System.Collections.Generic;

namespace xLostProHome.Ecosystem.Web.DialogueMachine.Models;

/// <summary>
/// ZENITH-APEX DATA SCHEMA v3.0.1
/// Standardised narrative graph structures. Includes persistent spatial metadata 
/// for the unified flowchart system and player interaction nodes.
/// </summary>
public class DialogueRoot 
{ 
    public List<CharacterState> ListOfCharacterStates { get; set; } = new(); 
}

public class CharacterState 
{ 
    public string State { get; set; } = Guid.NewGuid().ToString().Substring(0, 8); 
    public string Type { get; set; } = "Dialogue"; 
    public string Text { get; set; } = ""; 
    public string? DefaultNextId { get; set; } 
    public bool SayItAll { get; set; } = true; 
    public ShopData ShopData { get; set; } = new(); 
    public List<Choice> Choices { get; set; } = new(); 
    public List<ActionModel> DefaultAfterActions { get; set; } = new();
    public Dictionary<string, string> Metadata { get; set; } = new();

    // SPATIAL METADATA (State Node)
    public double VisualX { get; set; } = 100; 
    public double VisualY { get; set; } = 100;
}

public class Choice 
{ 
    public Guid InternalId { get; set; } = Guid.NewGuid();
    
    public string ButtonText { get; set; } = "New Choice"; 
    public string? NextStateId { get; set; } 
    public VisibilityMode Visibility { get; set; } = VisibilityMode.Disable;
    public List<Requirement> Requirements { get; set; } = new(); 
    public List<ActionModel> AfterChoiceActions { get; set; } = new(); 

    // SPATIAL METADATA (Choice Node)
    public double VisualX { get; set; } = 150; 
    public double VisualY { get; set; } = 150;
}

public class ActionModel 
{ 
    public string Type { get; set; } = "Affinity"; 
    public string TargetId { get; set; } = ""; 
    public int Amount { get; set; } = 0; 
    public string? Parameter { get; set; }
}

public class Requirement 
{ 
    public string Type { get; set; } = "Affinity"; 
    public string TargetId { get; set; } = ""; 
    public string Operator { get; set; } = "=="; 
    public string Value { get; set; } = "0"; 
}

public enum VisibilityMode 
{ 
    Hide,    // Choice is invisible if requirements fail
    Disable, // Choice is visible but unclickable (Greyed out)
    Always   // Choice is always clickable (Logic handled inside the state)
}

public class ShopData { public string ShopId { get; set; } = ""; }

public class UserSettings 
{
    public string LanguageCode { get; set; } = "en-GB"; 
    public bool IsAutoSaveEnabled { get; set; } = true; 
    public int AutoSaveIntervalSeconds { get; set; } = 30;
    public int ValidationErrorHideSeconds { get; set; } = 15; 

    public UserSettings Clone() => new UserSettings 
    { 
        LanguageCode=LanguageCode, 
        IsAutoSaveEnabled=IsAutoSaveEnabled, 
        AutoSaveIntervalSeconds=AutoSaveIntervalSeconds, 
        ValidationErrorHideSeconds=ValidationErrorHideSeconds 
    };

    public void Apply(UserSettings o) 
    { 
        LanguageCode=o.LanguageCode; 
        IsAutoSaveEnabled=o.IsAutoSaveEnabled; 
        AutoSaveIntervalSeconds=o.AutoSaveIntervalSeconds; 
        ValidationErrorHideSeconds=o.ValidationErrorHideSeconds; 
    }
}

public class VariableOption { public string DisplayName { get; set; } = ""; public string Value { get; set; } = ""; }

public class GraphEdge 
{
    public string SourceId { get; set; } = ""; 
    public string TargetId { get; set; } = "";
    public string PathData { get; set; } = ""; 
    public bool IsFallback { get; set; }
    public bool IsChoiceAnchor { get; set; } 

    // ATTENTION: [JsonIgnore] prevents circular reference crashes 
    // during JSON save/load cycles.
    [System.Text.Json.Serialization.JsonIgnore]
    public object? SourceObject { get; set; } 

    [System.Text.Json.Serialization.JsonIgnore]
    public object? TargetObject { get; set; }

    public double MidX { get; set; }
    public double MidY { get; set; }
}