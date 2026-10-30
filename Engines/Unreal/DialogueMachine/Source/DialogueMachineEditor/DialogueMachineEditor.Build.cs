using UnrealBuildTool;

public class DialogueMachineEditor : ModuleRules
{
    public DialogueMachineEditor(ReadOnlyTargetRules Target) : base(Target)
    {
        PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;

        PublicDependencyModuleNames.AddRange(
            new string[]
            {
                "Core",
                "CoreUObject",
                "Engine",
                "DialogueMachineRuntime",
                "UnrealEd",
                "AssetTools",
                "Projects",
                "WebSockets",
                "Json",
                "JsonUtilities",
                "Slate",
                "SlateCore",
                "ToolMenus",
                "WorkspaceMenuStructure"
            }
        );
    }
}
