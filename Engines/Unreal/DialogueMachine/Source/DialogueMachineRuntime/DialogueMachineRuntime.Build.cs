using UnrealBuildTool;

public class DialogueMachineRuntime : ModuleRules
{
    public DialogueMachineRuntime(ReadOnlyTargetRules Target) : base(Target)
    {
        PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;

        PublicDependencyModuleNames.AddRange(
            new string[]
            {
                "Core",
                "CoreUObject",
                "Engine",
                "Json",
                "JsonUtilities",
                "UMG",
                "Slate",
                "SlateCore",
                "WebSockets",
                "InputCore",
                "EnhancedInput"
            }
        );

        PrivateDependencyModuleNames.Add("Projects");

        if (Target.bBuildEditor)
        {
            PrivateDependencyModuleNames.AddRange(
                new string[]
                {
                    "UnrealEd"
                }
            );
        }
    }
}
