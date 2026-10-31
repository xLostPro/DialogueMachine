#include "DialogueMachineEditorModule.h"
#include "DialogueLiveLink.h"
#include "ToolMenus.h"
#include "WorkspaceMenuStructure.h"
#include "WorkspaceMenuStructureModule.h"
#include "Widgets/Docking/SDockTab.h"

#define LOCTEXT_NAMESPACE "FDialogueMachineEditorModule"

static const FName LiveLinkTabName("DialogueMachineLiveLink");

void FDialogueMachineEditorModule::StartupModule()
{
    LiveLinkClient = MakeShared<FDialogueLiveLink>();

    FGlobalTabmanager::Get()->RegisterNomadTabSpawner(
        LiveLinkTabName,
        FOnSpawnTab::CreateRaw(this, &FDialogueMachineEditorModule::OnSpawnLiveLinkTab))
        .SetDisplayName(LOCTEXT("LiveLinkTabTitle", "Dialogue Machine Live Link"))
        .SetMenuType(ETabSpawnerType::Hidden);

    RegisterMenus();
}

void FDialogueMachineEditorModule::ShutdownModule()
{
    FGlobalTabmanager::Get()->UnregisterNomadTabSpawner(LiveLinkTabName);

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
