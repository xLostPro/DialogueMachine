#pragma once

#include "CoreMinimal.h"
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
