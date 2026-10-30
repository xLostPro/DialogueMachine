#pragma once

#include "CoreMinimal.h"
#include "Blueprint/UserWidget.h"
#include "Components/TextBlock.h"
#include "Components/RichTextBlock.h"
#include "Components/VerticalBox.h"
#include "Components/Button.h"
#include "DialogueRunnerComponent.h"
#include "DialogueMachineWidget.generated.h"

UCLASS(BlueprintType, Blueprintable)
class DIALOGUEMACHINERUNTIME_API UDialogueMachineWidget : public UUserWidget
{
    GENERATED_BODY()

public:
    virtual void NativeConstruct() override;
    virtual TSharedRef<SWidget> RebuildWidget() override;

    UPROPERTY(BlueprintReadWrite, Category = "Dialogue Machine")
    UDialogueRunnerComponent* RunnerComponent;

    UFUNCTION(BlueprintCallable, Category = "Dialogue Machine")
    void BindToRunner(UDialogueRunnerComponent* InRunner);

    UFUNCTION(BlueprintCallable, Category = "Dialogue Machine")
    static FString ToUnrealRichText(const FString& Text);

protected:
    UPROPERTY(BlueprintReadOnly, Category = "Dialogue Machine")
    UTextBlock* SpeakerTextBlock;

    UPROPERTY(BlueprintReadOnly, Category = "Dialogue Machine")
    URichTextBlock* BodyRichTextBlock;

    UPROPERTY(BlueprintReadOnly, Category = "Dialogue Machine")
    UVerticalBox* ChoicesVerticalBox;

    UPROPERTY(BlueprintReadOnly, Category = "Dialogue Machine")
    UButton* ContinueButton;

    UPROPERTY(BlueprintReadOnly, Category = "Dialogue Machine")
    UTextBlock* ContinueTextBlock;

    UFUNCTION()
    void HandleLine(const FString& Page, const FString& Speaker);

    UFUNCTION()
    void HandleChoices(const TArray<FDialogueComponentChoice>& Choices);

    UFUNCTION()
    void HandleEnded(const FString& SceneId, const FString& ChoiceId);

    UFUNCTION()
    void HandleContinueClicked();

    UFUNCTION()
    void HandleChoiceClicked(FString ChoiceId);

private:
    void BuildWidgetTree();
};
