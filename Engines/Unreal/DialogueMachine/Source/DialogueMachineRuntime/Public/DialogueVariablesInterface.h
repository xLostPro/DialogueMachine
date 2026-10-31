#pragma once

#include "CoreMinimal.h"
#include "UObject/Interface.h"
#include "DialogueVariablesInterface.generated.h"

UINTERFACE(MinimalAPI, BlueprintType)
class UDialogueVariablesInterface : public UInterface
{
    GENERATED_BODY()
};

class DIALOGUEMACHINERUNTIME_API IDialogueVariablesInterface
{
    GENERATED_BODY()

public:
    UFUNCTION(BlueprintNativeEvent, BlueprintCallable, Category = "Dialogue Machine")
    bool HasNumber(const FString& Key) const;

    UFUNCTION(BlueprintNativeEvent, BlueprintCallable, Category = "Dialogue Machine")
    int32 GetNumber(const FString& Key) const;

    UFUNCTION(BlueprintNativeEvent, BlueprintCallable, Category = "Dialogue Machine")
    void SetNumber(const FString& Key, int32 Value);

    UFUNCTION(BlueprintNativeEvent, BlueprintCallable, Category = "Dialogue Machine")
    bool GetText(const FString& Key, FString& OutValue) const;

    UFUNCTION(BlueprintNativeEvent, BlueprintCallable, Category = "Dialogue Machine")
    void SetText(const FString& Key, const FString& Value);
};
