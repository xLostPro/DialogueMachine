#pragma once

#include "CoreMinimal.h"
#include "Blueprint/UserWidget.h"
#include "Components/TextBlock.h"
#include "Components/RichTextBlock.h"
#include "Components/VerticalBox.h"
#include "Components/Button.h"
#include "DialogueRunnerComponent.h"
#include "DialogueMachineWidget.generated.h"

DECLARE_DYNAMIC_MULTICAST_DELEGATE_OneParam(FOnDialogueChoiceButtonClicked, const FString&, ChoiceId);

/** Button that reports which choice it represents when clicked. */
UCLASS()
class DIALOGUEMACHINERUNTIME_API UDialogueChoiceButton : public UButton
{
    GENERATED_BODY()

public:
    FString ChoiceId;
    FOnDialogueChoiceButtonClicked OnChoiceClicked;

    void BindClick() { OnClicked.AddDynamic(this, &UDialogueChoiceButton::HandleClicked); }

private:
    UFUNCTION()
    void HandleClicked() { OnChoiceClicked.Broadcast(ChoiceId); }
};

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

    /** Converts Dialogue Machine markup ([b] [i] [u] [colour=X]) to Unreal rich text tags. */
    UFUNCTION(BlueprintCallable, Category = "Dialogue Machine")
    static FString ToUnrealRichText(const FString& Text);

    /** Same, and reports the colour style rows (named c_RRGGBBAA) the converted text refers to. */
    static FString ConvertRichText(const FString& Text, TSet<FString>& OutColourRows);

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
    void HandleChoiceClicked(const FString& ChoiceId);

private:
    void BuildWidgetTree();
    void UpdateTextStyles(const TSet<FString>& ColourRows);

    TWeakObjectPtr<UDialogueRunnerComponent> BoundRunner;
    TSet<FString> StyledColours;
};
