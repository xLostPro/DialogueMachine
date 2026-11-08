#pragma once

#include "CoreMinimal.h"
#include "Blueprint/UserWidget.h"
#include "Components/TextBlock.h"
#include "Components/RichTextBlock.h"
#include "Components/PanelWidget.h"
#include "Components/Button.h"
#include "Components/Image.h"
#include "Engine/Texture2D.h"
#include "Styling/SlateTypes.h"
#include "DialogueRunnerComponent.h"
#include "DialogueMachineWidget.generated.h"

class UInputAction;
class UDataTable;
struct FInputActionValue;

DECLARE_DYNAMIC_MULTICAST_DELEGATE_OneParam(FOnDialogueChoiceButtonClicked, const FString&, ChoiceId);
DECLARE_DYNAMIC_MULTICAST_DELEGATE_OneParam(FOnDialogueChoiceButtonHovered, int32, Index);

/** Button that reports which choice it represents when clicked or hovered. */
UCLASS()
class DIALOGUEMACHINERUNTIME_API UDialogueChoiceButton : public UButton
{
    GENERATED_BODY()

public:
    FString ChoiceId;
    int32 Index = INDEX_NONE;
    FOnDialogueChoiceButtonClicked OnChoiceClicked;
    FOnDialogueChoiceButtonHovered OnChoiceHovered;

    void BindClick()
    {
        OnClicked.AddDynamic(this, &UDialogueChoiceButton::HandleClicked);
        OnHovered.AddDynamic(this, &UDialogueChoiceButton::HandleHovered);
    }

private:
    UFUNCTION()
    void HandleClicked() { OnChoiceClicked.Broadcast(ChoiceId); }

    UFUNCTION()
    void HandleHovered() { OnChoiceHovered.Broadcast(Index); }
};

/**
 * Ready-made dialogue box. Drive it from game input with Submit, MoveSelection and SelectChoice,
 * assign Enhanced Input actions, or focus it (Enter / gamepad bottom face button / up / down).
 * Restyle it with the Style properties, or subclass it as a Widget Blueprint with widgets named PortraitImage,
 * SpeakerTextBlock, BodyRichTextBlock, ChoicesVerticalBox and ContinueButton (all optional) to use your own layout.
 */
UCLASS(BlueprintType, Blueprintable)
class DIALOGUEMACHINERUNTIME_API UDialogueMachineWidget : public UUserWidget
{
    GENERATED_BODY()

public:
    UDialogueMachineWidget(const FObjectInitializer& ObjectInitializer);

    virtual void NativeOnInitialized() override;
    virtual void NativeConstruct() override;
    virtual void NativeDestruct() override;
    virtual void NativeTick(const FGeometry& MyGeometry, float InDeltaTime) override;
    virtual FReply NativeOnKeyDown(const FGeometry& InGeometry, const FKeyEvent& InKeyEvent) override;
    virtual FReply NativeOnMouseButtonDown(const FGeometry& InGeometry, const FPointerEvent& InMouseEvent) override;
    virtual TSharedRef<SWidget> RebuildWidget() override;

    UPROPERTY(BlueprintReadWrite, Category = "Dialogue Machine")
    UDialogueRunnerComponent* RunnerComponent;

    UFUNCTION(BlueprintCallable, Category = "Dialogue Machine")
    void BindToRunner(UDialogueRunnerComponent* InRunner);

    /**
     * The one button a game needs: finishes the typing, then picks the highlighted choice or continues.
     * A press in the frame that showed the line is ignored, so the press that started or advanced the
     * dialogue doesn't also skip the new line.
     */
    UFUNCTION(BlueprintCallable, Category = "Dialogue Machine|Input")
    void Submit();

    /** Moves the choice highlight by Delta (negative is up), skipping disabled choices and wrapping. */
    UFUNCTION(BlueprintCallable, Category = "Dialogue Machine|Input")
    void MoveSelection(int32 Delta);

    /** Picks the choice at Index (as shown, from 0) if it is enabled. Use it for number keys. */
    UFUNCTION(BlueprintCallable, Category = "Dialogue Machine|Input")
    void SelectChoice(int32 Index);

    /** Shows the whole line at once. */
    UFUNCTION(BlueprintCallable, Category = "Dialogue Machine|Input")
    void SkipTypewriter();

    UFUNCTION(BlueprintPure, Category = "Dialogue Machine")
    bool IsOpen() const { return bOpen; }

    UFUNCTION(BlueprintPure, Category = "Dialogue Machine")
    bool IsTyping() const { return bTyping; }

    /** True once the line has finished typing and the runner offers choices. */
    UFUNCTION(BlueprintPure, Category = "Dialogue Machine")
    bool IsShowingChoices() const;

    /** The highlighted choice, or -1 when no choice is shown or enabled. */
    UFUNCTION(BlueprintPure, Category = "Dialogue Machine")
    int32 GetSelectedIndex() const { return IsShowingChoices() ? SelectedIndex : INDEX_NONE; }

    /** Converts Dialogue Machine markup ([b] [i] [u] [colour=X]) to Unreal rich text tags. */
    UFUNCTION(BlueprintCallable, Category = "Dialogue Machine")
    static FString ToUnrealRichText(const FString& Text);

    /** Same, and reports the colour style rows (named c_RRGGBBAA) the converted text refers to. */
    static FString ConvertRichText(const FString& Text, TSet<FString>& OutColourRows);

    /** The first Visible characters of converted rich text, closing an open tag, so typing keeps its styles. */
    static FString RevealRichText(const FString& RichText, int32 Visible, int32& OutTotalVisible);

    // Typewriter

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Dialogue Machine|Typewriter")
    bool bTypewriter = true;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Dialogue Machine|Typewriter", meta = (ClampMin = "0"))
    float CharactersPerSecond = 40.f;

    // Input

    /** Optional. Bound on the owning player's Enhanced Input component; it must be in an active mapping context. */
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Dialogue Machine|Input")
    TObjectPtr<UInputAction> SubmitAction;

    /** Optional. An Axis1D or Axis2D action; up moves the highlight up. */
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Dialogue Machine|Input")
    TObjectPtr<UInputAction> NavigateAction;

    /** Seconds between moves while Navigate is held. */
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Dialogue Machine|Input")
    float RepeatSeconds = 0.18f;

    // Portrait

    /**
     * The image for each portrait key: the line's portrait (the scene's, or a page's `@Speaker [portrait]:`),
     * or its speaker's name when it has none. A key with no entry hides the image.
     */
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Dialogue Machine|Portrait")
    TMap<FString, TSoftObjectPtr<UTexture2D>> Portraits;

    // Style (used by the built-in layout; a Widget Blueprint layout keeps its own look, except the choice buttons)

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Dialogue Machine|Style")
    FSlateBrush PanelBrush;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Dialogue Machine|Style")
    FMargin PanelPadding = FMargin(24.f, 18.f);

    /** Space around the panel; it sits at the bottom of the screen. */
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Dialogue Machine|Style")
    FMargin ScreenMargin = FMargin(120.f, 0.f, 120.f, 40.f);

    /** Size of the built-in layout's portrait, left of the text. */
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Dialogue Machine|Style")
    FVector2D PortraitSize = FVector2D(128.f, 128.f);

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Dialogue Machine|Style")
    FSlateFontInfo SpeakerFont;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Dialogue Machine|Style")
    FSlateColor SpeakerColor = FSlateColor(FLinearColor(0.47f, 0.87f, 0.78f));

    /** Body text font; [b] and [i] use its Bold and Italic typefaces. */
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Dialogue Machine|Style")
    FSlateFontInfo BodyFont;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Dialogue Machine|Style")
    FLinearColor BodyColor = FLinearColor(0.94f, 0.95f, 0.97f);

    /** Replaces the generated rich text styles (rows Default, b, i, u). [colour] text then uses Default. */
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Dialogue Machine|Style", meta = (RequiredAssetDataTags = "RowStructure=/Script/UMG.RichTextStyleRow"))
    TObjectPtr<UDataTable> TextStyleSet;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Dialogue Machine|Style")
    FButtonStyle ChoiceStyle;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Dialogue Machine|Style")
    FButtonStyle SelectedChoiceStyle;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Dialogue Machine|Style")
    FSlateFontInfo ChoiceFont;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Dialogue Machine|Style")
    FSlateColor ChoiceTextColor = FSlateColor(FLinearColor(0.94f, 0.95f, 0.97f));

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Dialogue Machine|Style")
    FButtonStyle ContinueStyle;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Dialogue Machine|Style")
    FText ContinueLabel;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Dialogue Machine|Style")
    FText SkipLabel;

protected:
    UPROPERTY(BlueprintReadOnly, Category = "Dialogue Machine", meta = (BindWidgetOptional))
    UImage* PortraitImage;

    UPROPERTY(BlueprintReadOnly, Category = "Dialogue Machine", meta = (BindWidgetOptional))
    UTextBlock* SpeakerTextBlock;

    UPROPERTY(BlueprintReadOnly, Category = "Dialogue Machine", meta = (BindWidgetOptional))
    URichTextBlock* BodyRichTextBlock;

    /** Any panel; choice buttons are added to it. */
    UPROPERTY(BlueprintReadOnly, Category = "Dialogue Machine", meta = (BindWidgetOptional))
    UPanelWidget* ChoicesVerticalBox;

    UPROPERTY(BlueprintReadOnly, Category = "Dialogue Machine", meta = (BindWidgetOptional))
    UButton* ContinueButton;

    UPROPERTY(BlueprintReadOnly, Category = "Dialogue Machine", meta = (BindWidgetOptional))
    UTextBlock* ContinueTextBlock;

    UFUNCTION()
    void HandleLine(const FString& Page, const FString& Speaker);

    UFUNCTION()
    void HandleChoices(const TArray<FDialogueComponentChoice>& InChoices);

    UFUNCTION()
    void HandleEnded(const FString& SceneId, const FString& ChoiceId);

    UFUNCTION()
    void HandleContinueClicked();

    UFUNCTION()
    void HandleChoiceClicked(const FString& ChoiceId);

    UFUNCTION()
    void HandleChoiceHovered(int32 Index);

private:
    void BuildWidgetTree();
    void UpdateTextStyles(const TSet<FString>& ColourRows);
    void ShowPortrait(const FString& Speaker);
    void RefreshControls();
    void FinishTyping();
    void ApplySelection(bool bFocus);
    void ChooseIndex(int32 Index);
    void ContinueOrSkip();
    bool BeginAction();
    int32 FirstEnabled(int32 Start, int32 Step) const;
    void BindInputActions();
    void UnbindInputActions();
    void HandleSubmitAction();
    void HandleNavigateAction(const FInputActionValue& Value);
    void HandleNavigateReleased();

    TWeakObjectPtr<UDialogueRunnerComponent> BoundRunner;
    TSet<FString> StyledColours;

    UPROPERTY(Transient)
    TArray<TObjectPtr<UDialogueChoiceButton>> ChoiceButtons;

    TArray<FDialogueComponentChoice> Choices;
    FString FullText;
    int32 FullVisible = 0;
    int32 ShownVisible = 0;
    float Revealed = 0.f;
    int32 SelectedIndex = INDEX_NONE;
    uint64 LineFrame = MAX_uint64;
    uint64 ActionFrame = MAX_uint64;
    double NextMoveTime = 0.0;
    bool bOpen = false;
    bool bTyping = false;
    TArray<uint32> InputBindingHandles;
};
