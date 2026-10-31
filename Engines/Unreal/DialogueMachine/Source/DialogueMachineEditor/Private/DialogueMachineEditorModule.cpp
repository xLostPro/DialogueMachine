#include "DialogueMachineEditorModule.h"
#include "DialogueLiveLink.h"
#include "DialogueAsset.h"
#include "AssetToolsModule.h"
#include "AssetTypeActions_Base.h"
#include "EditorFramework/AssetImportData.h"
#include "IAssetTools.h"
#include "ToolMenus.h"
#include "WorkspaceMenuStructure.h"
#include "WorkspaceMenuStructureModule.h"
#include "Widgets/Docking/SDockTab.h"

#define LOCTEXT_NAMESPACE "FDialogueMachineEditorModule"

static const FName LiveLinkTabName("DialogueMachineLiveLink");

// Unreal offers Reimport, in the Content Browser and for auto-reimport, only to asset types registered as imported.
class FDialogueAssetTypeActions : public FAssetTypeActions_Base
{
public:
    virtual FText GetName() const override { return LOCTEXT("DialogueAssetName", "Dialogue"); }
    virtual FColor GetTypeColor() const override { return FColor(80, 180, 160); }
    virtual UClass* GetSupportedClass() const override { return UDialogueAsset::StaticClass(); }
    virtual uint32 GetCategories() override { return EAssetTypeCategories::Misc; }
    virtual bool IsImportedAsset() const override { return true; }

    virtual void GetResolvedSourceFilePaths(const TArray<UObject*>& TypeAssets, TArray<FString>& OutSourceFilePaths) const override
    {
        for (UObject* Object : TypeAssets)
        {
            const UDialogueAsset* Asset = Cast<UDialogueAsset>(Object);
            if (Asset && Asset->AssetImportData)
            {
                Asset->AssetImportData->ExtractFilenames(OutSourceFilePaths);
            }
        }
    }
};

void FDialogueMachineEditorModule::StartupModule()
{
    LiveLinkClient = MakeShared<FDialogueLiveLink>();

    AssetTypeActions = MakeShared<FDialogueAssetTypeActions>();
    FModuleManager::LoadModuleChecked<FAssetToolsModule>("AssetTools").Get().RegisterAssetTypeActions(AssetTypeActions.ToSharedRef());

    FGlobalTabmanager::Get()->RegisterNomadTabSpawner(
        LiveLinkTabName,
        FOnSpawnTab::CreateRaw(this, &FDialogueMachineEditorModule::OnSpawnLiveLinkTab))
        .SetDisplayName(LOCTEXT("LiveLinkTabTitle", "Dialogue Machine Live Link"))
        .SetMenuType(ETabSpawnerMenuType::Hidden);

    RegisterMenus();
}

void FDialogueMachineEditorModule::ShutdownModule()
{
    FGlobalTabmanager::Get()->UnregisterNomadTabSpawner(LiveLinkTabName);

    if (AssetTypeActions.IsValid() && FModuleManager::Get().IsModuleLoaded("AssetTools"))
    {
        FModuleManager::GetModuleChecked<FAssetToolsModule>("AssetTools").Get().UnregisterAssetTypeActions(AssetTypeActions.ToSharedRef());
    }

    if (LiveLinkClient.IsValid())
    {
        LiveLinkClient->Disconnect();
        LiveLinkClient.Reset();
    }
}

void FDialogueMachineEditorModule::RegisterMenus()
{
    UToolMenus::RegisterStartupCallback(FSimpleMulticastDelegate::FDelegate::CreateLambda([]()
    {
        UToolMenu* Menu = UToolMenus::Get()->ExtendMenu("LevelEditor.MainMenu.Window");
        if (!Menu) return;

        FToolMenuSection& Section = Menu->FindOrAddSection("DialogueMachineSection");
        Section.AddMenuEntry(
            "DialogueMachineLiveLink",
            LOCTEXT("LiveLinkMenuEntry", "Dialogue Machine Live Link"),
            LOCTEXT("LiveLinkMenuEntryTooltip", "Open the Dialogue Machine Live Link connection panel."),
            FSlateIcon(),
            FUIAction(FExecuteAction::CreateLambda([]()
            {
                FGlobalTabmanager::Get()->TryInvokeTab(LiveLinkTabName);
            }))
        );
    }));
}

TSharedRef<SDockTab> FDialogueMachineEditorModule::OnSpawnLiveLinkTab(const FSpawnTabArgs& SpawnTabArgs)
{
    return SNew(SDockTab)
        .TabRole(ETabRole::NomadTab)
        [
            LiveLinkClient.IsValid() ? LiveLinkClient->CreatePanel() : SNullWidget::NullWidget
        ];
}

#undef LOCTEXT_NAMESPACE

IMPLEMENT_MODULE(FDialogueMachineEditorModule, DialogueMachineEditor)
