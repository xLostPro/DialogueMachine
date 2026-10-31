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

#if ENGINE_MAJOR_VERSION == 5 && ENGINE_MINOR_VERSION < 4
void UDialogueAsset::GetAssetRegistryTags(TArray<FAssetRegistryTag>& OutTags) const
{
    if (AssetImportData)
    {
        OutTags.Add(FAssetRegistryTag(SourceFileTagName(), AssetImportData->GetSourceData().ToJson(), FAssetRegistryTag::TT_Hidden));
    }
    Super::GetAssetRegistryTags(OutTags);
}
#else
void UDialogueAsset::GetAssetRegistryTags(FAssetRegistryTagsContext Context) const
{
    if (AssetImportData)
    {
        Context.AddTag(FAssetRegistryTag(SourceFileTagName(), AssetImportData->GetSourceData().ToJson(), FAssetRegistryTag::TT_Hidden));
    }
    Super::GetAssetRegistryTags(Context);
}
#endif
#endif

void UDialogueAsset::SetData(const FString& InJson, const FString& InSha, const FString& InProjectName)
{
    HandoffJson = InJson;
    StorySha256 = InSha;
    ProjectName = InProjectName;
}
