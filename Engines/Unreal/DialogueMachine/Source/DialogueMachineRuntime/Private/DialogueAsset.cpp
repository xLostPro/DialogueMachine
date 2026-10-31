#include "DialogueAsset.h"
#if WITH_EDITORONLY_DATA
#include "EditorFramework/AssetImportData.h"
#endif

#if WITH_EDITORONLY_DATA
void UDialogueAsset::PostInitProperties()
{
    if (!HasAnyFlags(RF_ClassDefaultObject))
    {
        AssetImportData = NewObject<UAssetImportData>(this, TEXT("AssetImportData"));
    }
    Super::PostInitProperties();
}
#endif

void UDialogueAsset::SetData(const FString& InJson, const FString& InSha, const FString& InProjectName)
{
    HandoffJson = InJson;
    StorySha256 = InSha;
    ProjectName = InProjectName;
}
