#pragma once

#include "CoreMinimal.h"
#include "Modules/ModuleManager.h"

class FDialogueMachineEditorModule : public IModuleInterface
{
public:
    virtual void StartupModule() override;
    virtual void ShutdownModule() override;

private:
    void RegisterMenus();
    TSharedRef<class SDockTab> OnSpawnLiveLinkTab(const class FSpawnTabArgs& SpawnTabArgs);

    TSharedPtr<class FDialogueLiveLink> LiveLinkClient;
    TSharedPtr<class IAssetTypeActions> AssetTypeActions;
};
