#pragma once

#include "CoreMinimal.h"
#include "Runtime/Launch/Resources/Version.h"
#include "UObject/NoExportTypes.h"
#include "DialogueAsset.generated.h"

UCLASS(BlueprintType)
class DIALOGUEMACHINERUNTIME_API UDialogueAsset : public UObject
{
    GENERATED_BODY()

public:
#if WITH_EDITORONLY_DATA
    /** The *.runtime.json this asset was imported from. */
    UPROPERTY(VisibleAnywhere, Instanced, Category = "Import Settings")
    TObjectPtr<class UAssetImportData> AssetImportData;

    virtual void PostInitProperties() override;

    // Auto-reimport finds the asset for a changed file through this tag.
#if ENGINE_MAJOR_VERSION == 5 && ENGINE_MINOR_VERSION < 4
    virtual void GetAssetRegistryTags(TArray<FAssetRegistryTag>& OutTags) const override;
#else
    virtual void GetAssetRegistryTags(FAssetRegistryTagsContext Context) const override;
#endif
#endif

    UPROPERTY(VisibleAnywhere, Category = "Dialogue Machine")
    FString ProjectName;

    UPROPERTY(VisibleAnywhere, Category = "Dialogue Machine")
    FString StorySha256;

    UPROPERTY()
    FString HandoffJson;

    UFUNCTION(BlueprintCallable, Category = "Dialogue Machine")
    void SetData(const FString& InJson, const FString& InSha, const FString& InProjectName);
};
