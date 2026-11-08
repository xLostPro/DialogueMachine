#include "DialogueMachineWidget.h"
#include "Blueprint/WidgetTree.h"
#include "Brushes/SlateColorBrush.h"
#include "Brushes/SlateRoundedBoxBrush.h"
#include "Components/Border.h"
#include "Components/HorizontalBox.h"
#include "Components/HorizontalBoxSlot.h"
#include "Components/Overlay.h"
#include "Components/OverlaySlot.h"
#include "Components/VerticalBox.h"
#include "Components/VerticalBoxSlot.h"
#include "Engine/DataTable.h"
#include "EnhancedInputComponent.h"
#include "GameFramework/PlayerController.h"
#include "InputAction.h"
#include "InputCoreTypes.h"
#include "Styling/CoreStyle.h"
#include "Styling/SlateBrush.h"
#include "Fonts/SlateFontInfo.h"

namespace
{
    // A code-built style set: the rich text block shows missing-glyph boxes without one.
    FRichTextStyleRow MakeRow(const FSlateFontInfo& Font, const FLinearColor& Colour, bool bUnderline = false)
    {
        FRichTextStyleRow Row;
        Row.TextStyle = FCoreStyle::Get().GetWidgetStyle<FTextBlockStyle>("NormalText");
        Row.TextStyle.SetFont(Font);
        Row.TextStyle.SetColorAndOpacity(FSlateColor(Colour));
        if (bUnderline)
        {
            Row.TextStyle.SetUnderlineBrush(FSlateColorBrush(Colour));
        }
        return Row;
    }

    FSlateFontInfo WithTypeface(FSlateFontInfo Font, const TCHAR* Typeface)
    {
        Font.TypefaceFontName = FName(Typeface);
        return Font;
    }

    FButtonStyle MakeButtonStyle(const FLinearColor& Fill, const FLinearColor& Hover, const FLinearColor& Outline)
    {
        FButtonStyle Style = FCoreStyle::Get().GetWidgetStyle<FButtonStyle>("Button");
        Style.SetNormal(FSlateRoundedBoxBrush(Fill, 6.f, Outline, 1.f));
        Style.SetHovered(FSlateRoundedBoxBrush(Hover, 6.f, Outline, 1.f));
        Style.SetPressed(FSlateRoundedBoxBrush(Hover, 6.f, Outline, 1.f));
        Style.SetDisabled(FSlateRoundedBoxBrush(Fill, 6.f, Outline, 1.f));
        Style.SetNormalPadding(FMargin(14.f, 8.f));
        Style.SetPressedPadding(FMargin(14.f, 8.f));
        return Style;
    }

    bool TryParseColour(const FString& Raw, FColor& OutColour)
    {
        const FString Value = Raw.TrimStartAndEnd().TrimQuotes();
        static const TMap<FString, FColor> Named = {
            { TEXT("red"), FColor(255, 0, 0) }, { TEXT("green"), FColor(0, 160, 0) }, { TEXT("blue"), FColor(0, 0, 255) },
            { TEXT("yellow"), FColor(255, 255, 0) }, { TEXT("orange"), FColor(255, 165, 0) }, { TEXT("purple"), FColor(128, 0, 128) },
            { TEXT("cyan"), FColor(0, 255, 255) }, { TEXT("magenta"), FColor(255, 0, 255) }, { TEXT("white"), FColor(255, 255, 255) },
            { TEXT("black"), FColor(0, 0, 0) }, { TEXT("gray"), FColor(128, 128, 128) }, { TEXT("grey"), FColor(128, 128, 128) },
        };
        if (const FColor* Found = Named.Find(Value.ToLower()))
        {
            OutColour = *Found;
            return true;
        }
        if (Value.StartsWith(TEXT("#")) && (Value.Len() == 7 || Value.Len() == 9))
        {
            OutColour = FColor::FromHex(Value);
            return true;
        }
        return false;
    }
}

UDialogueMachineWidget::UDialogueMachineWidget(const FObjectInitializer& ObjectInitializer)
    : Super(ObjectInitializer)
{
    const FLinearColor Panel(0.12f, 0.14f, 0.18f, 0.94f);
    const FLinearColor Outline(0.28f, 0.32f, 0.40f, 0.8f);
    const FLinearColor Fill(0.2f, 0.24f, 0.31f);
    const FLinearColor Hover(0.24f, 0.29f, 0.37f);
    const FLinearColor Accent(0.47f, 0.87f, 0.78f);

    PanelBrush = FSlateRoundedBoxBrush(Panel, 10.f, Outline, 1.5f);
    SpeakerFont = FCoreStyle::GetDefaultFontStyle("Bold", 22);
    BodyFont = FCoreStyle::GetDefaultFontStyle("Regular", 24);
    ChoiceFont = FCoreStyle::GetDefaultFontStyle("Regular", 20);
    ChoiceStyle = MakeButtonStyle(Fill, Hover, Outline);
    SelectedChoiceStyle = MakeButtonStyle(Hover, Hover, Accent);
    ContinueStyle = MakeButtonStyle(Fill, Hover, Outline);
    ContinueLabel = FText::FromString(TEXT("Continue"));
    SkipLabel = FText::FromString(TEXT("Skip"));
}

void UDialogueMachineWidget::NativeOnInitialized()
{
    Super::NativeOnInitialized();
    SetIsFocusable(true);
    if (ContinueButton)
    {
        ContinueButton->OnClicked.AddUniqueDynamic(this, &UDialogueMachineWidget::HandleContinueClicked);
    }
}

void UDialogueMachineWidget::NativeConstruct()
{
    Super::NativeConstruct();
    BindInputActions();
    if (RunnerComponent)
    {
        BindToRunner(RunnerComponent);
    }
    else if (!bOpen)
    {
        SetVisibility(ESlateVisibility::Collapsed);
    }
}

void UDialogueMachineWidget::NativeDestruct()
{
    UnbindInputActions();
    Super::NativeDestruct();
}

TSharedRef<SWidget> UDialogueMachineWidget::RebuildWidget()
{
    BuildWidgetTree();
    return Super::RebuildWidget();
}

// A Widget Blueprint subclass brings its own tree (its widgets bind by name); otherwise build the default.
void UDialogueMachineWidget::BuildWidgetTree()
{
    if (!WidgetTree)
    {
        WidgetTree = NewObject<UWidgetTree>(this, TEXT("WidgetTree"));
    }

    if (WidgetTree->RootWidget)
    {
        UpdateTextStyles(TSet<FString>());
        return;
    }

    UOverlay* Root = WidgetTree->ConstructWidget<UOverlay>(UOverlay::StaticClass(), TEXT("RootOverlay"));
    WidgetTree->RootWidget = Root;

    UBorder* Panel = WidgetTree->ConstructWidget<UBorder>(UBorder::StaticClass(), TEXT("PanelBorder"));
    Panel->SetBrush(PanelBrush);
    Panel->SetPadding(PanelPadding);
    UOverlaySlot* PanelSlot = Root->AddChildToOverlay(Panel);
    PanelSlot->SetHorizontalAlignment(HAlign_Fill);
    PanelSlot->SetVerticalAlignment(VAlign_Bottom);
    PanelSlot->SetPadding(ScreenMargin);

    UHorizontalBox* Row = WidgetTree->ConstructWidget<UHorizontalBox>(UHorizontalBox::StaticClass(), TEXT("PortraitRow"));
    Panel->SetContent(Row);

    PortraitImage = WidgetTree->ConstructWidget<UImage>(UImage::StaticClass(), TEXT("PortraitImage"));
    PortraitImage->SetDesiredSizeOverride(PortraitSize);
    PortraitImage->SetVisibility(ESlateVisibility::Collapsed);
    UHorizontalBoxSlot* PortraitSlot = Row->AddChildToHorizontalBox(PortraitImage);
    PortraitSlot->SetVerticalAlignment(VAlign_Top);
    PortraitSlot->SetPadding(FMargin(0.f, 0.f, 20.f, 0.f));

    UVerticalBox* Box = WidgetTree->ConstructWidget<UVerticalBox>(UVerticalBox::StaticClass(), TEXT("ContentBox"));
    Row->AddChildToHorizontalBox(Box)->SetSize(FSlateChildSize(ESlateSizeRule::Fill));

    SpeakerTextBlock = WidgetTree->ConstructWidget<UTextBlock>(UTextBlock::StaticClass(), TEXT("SpeakerTextBlock"));
    SpeakerTextBlock->SetFont(SpeakerFont);
    SpeakerTextBlock->SetColorAndOpacity(SpeakerColor);
    Box->AddChildToVerticalBox(SpeakerTextBlock)->SetPadding(FMargin(0.f, 0.f, 0.f, 10.f));

    BodyRichTextBlock = WidgetTree->ConstructWidget<URichTextBlock>(URichTextBlock::StaticClass(), TEXT("BodyRichTextBlock"));
    BodyRichTextBlock->SetAutoWrapText(true);
    Box->AddChildToVerticalBox(BodyRichTextBlock)->SetPadding(FMargin(0.f, 0.f, 0.f, 16.f));

    UVerticalBox* ChoicesBox = WidgetTree->ConstructWidget<UVerticalBox>(UVerticalBox::StaticClass(), TEXT("ChoicesVerticalBox"));
    ChoicesVerticalBox = ChoicesBox;
    Box->AddChildToVerticalBox(ChoicesBox);

    ContinueButton = WidgetTree->ConstructWidget<UButton>(UButton::StaticClass(), TEXT("ContinueButton"));
    ContinueButton->SetStyle(ContinueStyle);
    ContinueTextBlock = WidgetTree->ConstructWidget<UTextBlock>(UTextBlock::StaticClass(), TEXT("ContinueTextBlock"));
    ContinueTextBlock->SetFont(ChoiceFont);
    ContinueTextBlock->SetColorAndOpacity(ChoiceTextColor);
    ContinueTextBlock->SetText(ContinueLabel);
    ContinueButton->AddChild(ContinueTextBlock);
    ContinueButton->OnClicked.AddUniqueDynamic(this, &UDialogueMachineWidget::HandleContinueClicked);
    ContinueButton->SetVisibility(ESlateVisibility::Collapsed);
    Box->AddChildToVerticalBox(ContinueButton)->SetHorizontalAlignment(HAlign_Right);

    UpdateTextStyles(TSet<FString>());
}

void UDialogueMachineWidget::UpdateTextStyles(const TSet<FString>& ColourRows)
{
    if (!BodyRichTextBlock) return;
    if (TextStyleSet)
    {
        if (BodyRichTextBlock->GetTextStyleSet() != TextStyleSet) BodyRichTextBlock->SetTextStyleSet(TextStyleSet);
        return;
    }
    if (BodyRichTextBlock->GetTextStyleSet() && ColourRows.Difference(StyledColours).Num() == 0) return;

    StyledColours.Append(ColourRows);

    UDataTable* Styles = NewObject<UDataTable>(this);
    Styles->RowStruct = FRichTextStyleRow::StaticStruct();
    Styles->AddRow(FName("Default"), MakeRow(BodyFont, BodyColor));
    Styles->AddRow(FName("b"), MakeRow(WithTypeface(BodyFont, TEXT("Bold")), BodyColor));
    Styles->AddRow(FName("i"), MakeRow(WithTypeface(BodyFont, TEXT("Italic")), BodyColor));
    Styles->AddRow(FName("u"), MakeRow(BodyFont, BodyColor, true));
    for (const FString& Row : StyledColours)
    {
        const FColor Colour = FColor::FromHex(Row.RightChop(2));
        Styles->AddRow(FName(*Row), MakeRow(BodyFont, FLinearColor::FromSRGBColor(Colour)));
    }
    BodyRichTextBlock->SetTextStyleSet(Styles);
}

void UDialogueMachineWidget::BindToRunner(UDialogueRunnerComponent* InRunner)
{
    if (BoundRunner.IsValid() && BoundRunner.Get() != InRunner)
    {
        BoundRunner->OnLine.RemoveDynamic(this, &UDialogueMachineWidget::HandleLine);
        BoundRunner->OnChoices.RemoveDynamic(this, &UDialogueMachineWidget::HandleChoices);
        BoundRunner->OnEnded.RemoveDynamic(this, &UDialogueMachineWidget::HandleEnded);
    }

    RunnerComponent = InRunner;
    BoundRunner = InRunner;
    if (!RunnerComponent) return;

    // Unique: NativeConstruct binds a preset runner and games often call BindToRunner as well.
    RunnerComponent->OnLine.AddUniqueDynamic(this, &UDialogueMachineWidget::HandleLine);
    RunnerComponent->OnChoices.AddUniqueDynamic(this, &UDialogueMachineWidget::HandleChoices);
    RunnerComponent->OnEnded.AddUniqueDynamic(this, &UDialogueMachineWidget::HandleEnded);

    if (RunnerComponent->GetInternalRunner().IsValid() && !RunnerComponent->IsEnded())
    {
        HandleLine(RunnerComponent->GetCurrentPage(), RunnerComponent->GetSpeaker());
    }
    else if (!bOpen)
    {
        SetVisibility(ESlateVisibility::Collapsed);
    }
}

FString UDialogueMachineWidget::ToUnrealRichText(const FString& Text)
{
    TSet<FString> Unused;
    return ConvertRichText(Text, Unused);
}

// Unreal rich text closes every tag with </>, and has no nesting, so [b][i]x[/i][/b] keeps only the outer style.
FString UDialogueMachineWidget::ConvertRichText(const FString& Text, TSet<FString>& OutColourRows)
{
    FString Result;
    const int32 Len = Text.Len();
    int32 Pos = 0;

    while (Pos < Len)
    {
        const TCHAR Ch = Text[Pos];
        if (Ch == TEXT('['))
        {
            int32 CheckPos = Pos + 1;
            const bool bClosing = CheckPos < Len && Text[CheckPos] == TEXT('/');
            if (bClosing) ++CheckPos;

            if (CheckPos + 1 < Len && Text[CheckPos + 1] == TEXT(']'))
            {
                const TCHAR Tag = FChar::ToLower(Text[CheckPos]);
                if (Tag == TEXT('b') || Tag == TEXT('i') || Tag == TEXT('u'))
                {
                    Result += bClosing ? FString(TEXT("</>")) : FString::Printf(TEXT("<%c>"), Tag);
                    Pos = CheckPos + 2;
                    continue;
                }
            }

            if (CheckPos + 6 <= Len && Text.Mid(CheckPos, 6).Equals(TEXT("colour"), ESearchCase::IgnoreCase))
            {
                const int32 After = CheckPos + 6;
                if (bClosing && After < Len && Text[After] == TEXT(']'))
                {
                    Result += TEXT("</>");
                    Pos = After + 1;
                    continue;
                }
                if (!bClosing && After < Len && Text[After] == TEXT('='))
                {
                    const int32 Close = Text.Find(TEXT("]"), ESearchCase::CaseSensitive, ESearchDir::FromStart, After + 1);
                    FColor Colour(0, 0, 0);
                    if (Close != INDEX_NONE && TryParseColour(Text.Mid(After + 1, Close - After - 1), Colour))
                    {
                        const FString Row = FString::Printf(TEXT("c_%02X%02X%02X%02X"), Colour.R, Colour.G, Colour.B, Colour.A);
                        OutColourRows.Add(Row);
                        Result += FString::Printf(TEXT("<%s>"), *Row);
                        Pos = Close + 1;
                        continue;
                    }
                }
            }
        }

        // Characters that would otherwise start rich text markup have to be escaped.
        switch (Ch)
        {
        case TEXT('&'): Result += TEXT("&amp;"); break;
        case TEXT('<'): Result += TEXT("&lt;"); break;
        case TEXT('>'): Result += TEXT("&gt;"); break;
        case TEXT('"'): Result += TEXT("&quot;"); break;
        default: Result.AppendChar(Ch); break;
        }
        ++Pos;
    }

    return Result;
}

// ConvertRichText output only has <tag>, </> and &entity; markup, and tags never nest.
FString UDialogueMachineWidget::RevealRichText(const FString& RichText, int32 Visible, int32& OutTotalVisible)
{
    FString Result;
    bool bInTag = false;
    bool bDone = false;
    OutTotalVisible = 0;
    const int32 Len = RichText.Len();

    for (int32 Pos = 0; Pos < Len; ++Pos)
    {
        const TCHAR Ch = RichText[Pos];
        int32 End = Pos;
        if (Ch == TEXT('<') || Ch == TEXT('&'))
        {
            const int32 Found = RichText.Find(Ch == TEXT('<') ? TEXT(">") : TEXT(";"), ESearchCase::CaseSensitive, ESearchDir::FromStart, Pos + 1);
            if (Found != INDEX_NONE) End = Found;
        }

        if (Ch == TEXT('<') && End > Pos)
        {
            const bool bClose = RichText.Mid(Pos, End - Pos + 1) == TEXT("</>");
            if (!bDone)
            {
                Result += RichText.Mid(Pos, End - Pos + 1);
                bInTag = !bClose;
            }
            Pos = End;
            continue;
        }

        ++OutTotalVisible;
        if (!bDone && OutTotalVisible <= Visible)
        {
            Result += RichText.Mid(Pos, End - Pos + 1);
        }
        else
        {
            bDone = true;
        }
        Pos = End;
    }

    if (bInTag) Result += TEXT("</>");
    return Result;
}

void UDialogueMachineWidget::HandleLine(const FString& Page, const FString& Speaker)
{
    bOpen = true;
    SetVisibility(ESlateVisibility::SelfHitTestInvisible);
    LineFrame = GFrameCounter;

    if (SpeakerTextBlock)
    {
        SpeakerTextBlock->SetText(FText::FromString(Speaker));
        SpeakerTextBlock->SetVisibility(Speaker.IsEmpty() ? ESlateVisibility::Collapsed : ESlateVisibility::HitTestInvisible);
    }
    ShowPortrait(Speaker);

    TSet<FString> Colours;
    FullText = ConvertRichText(Page, Colours);
    UpdateTextStyles(Colours);
    RevealRichText(FullText, MAX_int32, FullVisible);
    Revealed = 0.f;
    ShownVisible = 0;
    bTyping = bTypewriter && CharactersPerSecond > 0.f && FullVisible > 0;
    if (BodyRichTextBlock)
    {
        int32 Unused = 0;
        BodyRichTextBlock->SetText(FText::FromString(bTyping ? RevealRichText(FullText, 0, Unused) : FullText));
    }
    RefreshControls();
}

void UDialogueMachineWidget::ShowPortrait(const FString& Speaker)
{
    if (!PortraitImage) return;
    const FString Portrait = RunnerComponent ? RunnerComponent->GetPortrait() : FString();
    const TSoftObjectPtr<UTexture2D>* Found = Portraits.Find(Portrait.IsEmpty() ? Speaker : Portrait);
    UTexture2D* Texture = Found ? Found->LoadSynchronous() : nullptr;
    if (Texture) PortraitImage->SetBrushFromTexture(Texture);
    PortraitImage->SetVisibility(Texture ? ESlateVisibility::HitTestInvisible : ESlateVisibility::Collapsed);
}

void UDialogueMachineWidget::HandleChoices(const TArray<FDialogueComponentChoice>& InChoices)
{
    RefreshControls();
}

void UDialogueMachineWidget::HandleEnded(const FString& SceneId, const FString& ChoiceId)
{
    bOpen = false;
    bTyping = false;
    Choices.Reset();
    ChoiceButtons.Reset();
    if (ChoicesVerticalBox) ChoicesVerticalBox->ClearChildren();
    if (ContinueButton) ContinueButton->SetVisibility(ESlateVisibility::Collapsed);
    SetVisibility(ESlateVisibility::Collapsed);
}

void UDialogueMachineWidget::NativeTick(const FGeometry& MyGeometry, float InDeltaTime)
{
    Super::NativeTick(MyGeometry, InDeltaTime);
    if (!bTyping) return;

    Revealed += CharactersPerSecond * InDeltaTime;
    const int32 Count = FMath::FloorToInt(Revealed);
    if (Count >= FullVisible)
    {
        FinishTyping();
    }
    else if (Count != ShownVisible && BodyRichTextBlock)
    {
        ShownVisible = Count;
        int32 Unused = 0;
        BodyRichTextBlock->SetText(FText::FromString(RevealRichText(FullText, Count, Unused)));
    }
}

FReply UDialogueMachineWidget::NativeOnKeyDown(const FGeometry& InGeometry, const FKeyEvent& InKeyEvent)
{
    const FKey Key = InKeyEvent.GetKey();
    if (bOpen)
    {
        if (Key == EKeys::Enter || Key == EKeys::SpaceBar || Key == EKeys::Gamepad_FaceButton_Bottom)
        {
            if (!InKeyEvent.IsRepeat()) Submit();
            return FReply::Handled();
        }
        if (IsShowingChoices() && (Key == EKeys::Up || Key == EKeys::Gamepad_DPad_Up || Key == EKeys::Gamepad_LeftStick_Up))
        {
            MoveSelection(-1);
            return FReply::Handled();
        }
        if (IsShowingChoices() && (Key == EKeys::Down || Key == EKeys::Gamepad_DPad_Down || Key == EKeys::Gamepad_LeftStick_Down))
        {
            MoveSelection(1);
            return FReply::Handled();
        }
    }
    return Super::NativeOnKeyDown(InGeometry, InKeyEvent);
}

// Clicking the box finishes the typing.
FReply UDialogueMachineWidget::NativeOnMouseButtonDown(const FGeometry& InGeometry, const FPointerEvent& InMouseEvent)
{
    if (bTyping && InMouseEvent.GetEffectingButton() == EKeys::LeftMouseButton && BeginAction())
    {
        SkipTypewriter();
        return FReply::Handled();
    }
    return Super::NativeOnMouseButtonDown(InGeometry, InMouseEvent);
}

bool UDialogueMachineWidget::IsShowingChoices() const
{
    return bOpen && !bTyping && Choices.Num() > 0;
}

// One action per frame, and none in the frame a line appeared, so the game's input and the
// buttons' own Slate handling can't both act on one press.
bool UDialogueMachineWidget::BeginAction()
{
    if (!bOpen || GFrameCounter == LineFrame || GFrameCounter == ActionFrame) return false;
    ActionFrame = GFrameCounter;
    return true;
}

void UDialogueMachineWidget::Submit()
{
    if (!BeginAction()) return;
    if (bTyping) SkipTypewriter();
    else if (IsShowingChoices()) ChooseIndex(SelectedIndex);
    else if (RunnerComponent && RunnerComponent->CanContinue()) RunnerComponent->ContinueDialogue();
}

void UDialogueMachineWidget::MoveSelection(int32 Delta)
{
    if (!IsShowingChoices() || Delta == 0) return;
    const int32 Step = Delta > 0 ? 1 : -1;
    const int32 N = Choices.Num();
    for (int32 i = 0; i < FMath::Abs(Delta); ++i)
    {
        SelectedIndex = FirstEnabled(((SelectedIndex + Step) % N + N) % N, Step);
    }
    ApplySelection(true);
}

void UDialogueMachineWidget::SelectChoice(int32 Index)
{
    if (BeginAction()) ChooseIndex(Index);
}

void UDialogueMachineWidget::SkipTypewriter()
{
    if (bTyping) FinishTyping();
}

void UDialogueMachineWidget::ContinueOrSkip()
{
    if (bTyping) SkipTypewriter();
    else if (RunnerComponent && RunnerComponent->CanContinue()) RunnerComponent->ContinueDialogue();
}

void UDialogueMachineWidget::ChooseIndex(int32 Index)
{
    if (!IsShowingChoices() || !Choices.IsValidIndex(Index) || !Choices[Index].bEnabled || !RunnerComponent) return;
    RunnerComponent->ChooseChoice(Choices[Index].Id);
}

void UDialogueMachineWidget::FinishTyping()
{
    bTyping = false;
    if (BodyRichTextBlock) BodyRichTextBlock->SetText(FText::FromString(FullText));
    RefreshControls();
}

void UDialogueMachineWidget::RefreshControls()
{
    if (!bOpen || !RunnerComponent) return;

    Choices.Reset();
    ChoiceButtons.Reset();
    if (ChoicesVerticalBox) ChoicesVerticalBox->ClearChildren();

    // The runner only offers choices on a scene's last page, and they appear once the line has finished typing.
    if (!bTyping && RunnerComponent->GetPageIndex() >= RunnerComponent->GetPageCount() - 1)
    {
        Choices = RunnerComponent->GetChoices();
    }

    if (ChoicesVerticalBox && WidgetTree)
    {
        for (int32 i = 0; i < Choices.Num(); ++i)
        {
            const FDialogueComponentChoice& Choice = Choices[i];
            UDialogueChoiceButton* ChoiceBtn = WidgetTree->ConstructWidget<UDialogueChoiceButton>(UDialogueChoiceButton::StaticClass());
            UTextBlock* ChoiceTxt = WidgetTree->ConstructWidget<UTextBlock>(UTextBlock::StaticClass());
            FString Label = Choice.Text;
            if (!Choice.bEnabled && !Choice.DisabledReason.IsEmpty())
            {
                Label += FString::Printf(TEXT(" (%s)"), *Choice.DisabledReason);
                ChoiceBtn->SetToolTipText(FText::FromString(Choice.DisabledReason));
            }
            ChoiceTxt->SetText(FText::FromString(Label));
            ChoiceTxt->SetFont(ChoiceFont);
            ChoiceTxt->SetColorAndOpacity(ChoiceTextColor);
            ChoiceTxt->SetAutoWrapText(true);
            ChoiceBtn->AddChild(ChoiceTxt);
            ChoiceBtn->SetStyle(ChoiceStyle);
            ChoiceBtn->SetIsEnabled(Choice.bEnabled);
            if (!Choice.bEnabled) ChoiceBtn->SetRenderOpacity(0.5f);

            ChoiceBtn->ChoiceId = Choice.Id;
            ChoiceBtn->Index = i;
            ChoiceBtn->BindClick();
            ChoiceBtn->OnChoiceClicked.AddDynamic(this, &UDialogueMachineWidget::HandleChoiceClicked);
            ChoiceBtn->OnChoiceHovered.AddDynamic(this, &UDialogueMachineWidget::HandleChoiceHovered);

            UPanelSlot* ChoiceSlot = ChoicesVerticalBox->AddChild(ChoiceBtn);
            if (UVerticalBoxSlot* VSlot = Cast<UVerticalBoxSlot>(ChoiceSlot)) VSlot->SetPadding(FMargin(0.f, 4.f));
            ChoiceButtons.Add(ChoiceBtn);
        }
    }
    SelectedIndex = FirstEnabled(0, 1);

    if (ContinueButton)
    {
        const bool bVisible = bTyping || RunnerComponent->CanContinue();
        ContinueButton->SetVisibility(bVisible ? ESlateVisibility::Visible : ESlateVisibility::Collapsed);
    }
    if (ContinueTextBlock)
    {
        ContinueTextBlock->SetText(bTyping ? SkipLabel : ContinueLabel);
    }

    if (IsShowingChoices()) ApplySelection(HasFocusedDescendants() || HasKeyboardFocus());
}

void UDialogueMachineWidget::ApplySelection(bool bFocus)
{
    for (int32 i = 0; i < ChoiceButtons.Num(); ++i)
    {
        ChoiceButtons[i]->SetStyle(i == SelectedIndex ? SelectedChoiceStyle : ChoiceStyle);
    }
    // Keep keyboard focus on the highlighted button while the player navigates with focus.
    if (bFocus && ChoiceButtons.IsValidIndex(SelectedIndex) && GetOwningPlayer())
    {
        ChoiceButtons[SelectedIndex]->SetKeyboardFocus();
    }
}

int32 UDialogueMachineWidget::FirstEnabled(int32 Start, int32 Step) const
{
    const int32 N = Choices.Num();
    for (int32 i = 0; i < N; ++i)
    {
        const int32 K = ((Start + i * Step) % N + N) % N;
        if (Choices[K].bEnabled) return K;
    }
    return INDEX_NONE;
}

void UDialogueMachineWidget::HandleContinueClicked()
{
    if (BeginAction()) ContinueOrSkip();
}

void UDialogueMachineWidget::HandleChoiceClicked(const FString& ChoiceId)
{
    const int32 Index = Choices.IndexOfByPredicate([&ChoiceId](const FDialogueComponentChoice& C) { return C.Id == ChoiceId; });
    SelectChoice(Index);
}

void UDialogueMachineWidget::HandleChoiceHovered(int32 Index)
{
    if (Index == SelectedIndex || !IsShowingChoices() || !Choices.IsValidIndex(Index) || !Choices[Index].bEnabled) return;
    SelectedIndex = Index;
    ApplySelection(false);
}

void UDialogueMachineWidget::BindInputActions()
{
    UnbindInputActions();
    APlayerController* PC = GetOwningPlayer();
    UEnhancedInputComponent* Input = PC ? Cast<UEnhancedInputComponent>(PC->InputComponent) : nullptr;
    if (!Input) return;

    if (SubmitAction)
    {
        InputBindingHandles.Add(Input->BindAction(SubmitAction, ETriggerEvent::Started, this, &UDialogueMachineWidget::HandleSubmitAction).GetHandle());
    }
    if (NavigateAction)
    {
        InputBindingHandles.Add(Input->BindAction(NavigateAction, ETriggerEvent::Triggered, this, &UDialogueMachineWidget::HandleNavigateAction).GetHandle());
        InputBindingHandles.Add(Input->BindAction(NavigateAction, ETriggerEvent::Completed, this, &UDialogueMachineWidget::HandleNavigateReleased).GetHandle());
    }
}

void UDialogueMachineWidget::UnbindInputActions()
{
    APlayerController* PC = GetOwningPlayer();
    if (UEnhancedInputComponent* Input = PC ? Cast<UEnhancedInputComponent>(PC->InputComponent) : nullptr)
    {
        for (const uint32 Handle : InputBindingHandles) Input->RemoveBindingByHandle(Handle);
    }
    InputBindingHandles.Reset();
}

void UDialogueMachineWidget::HandleSubmitAction()
{
    Submit();
}

void UDialogueMachineWidget::HandleNavigateAction(const FInputActionValue& Value)
{
    if (!IsShowingChoices()) return;
    const float Y = Value.GetValueType() == EInputActionValueType::Axis1D ? Value.Get<float>() : Value.Get<FVector2D>().Y;
    if (FMath::Abs(Y) < 0.5f)
    {
        NextMoveTime = 0.0;
        return;
    }
    const double Now = FPlatformTime::Seconds();
    if (Now < NextMoveTime) return;
    MoveSelection(Y > 0.f ? -1 : 1);
    NextMoveTime = Now + (NextMoveTime == 0.0 ? RepeatSeconds * 2.0 : RepeatSeconds);
}

void UDialogueMachineWidget::HandleNavigateReleased()
{
    NextMoveTime = 0.0;
}
