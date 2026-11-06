#include "DialogueMachineWidget.h"
#include "Blueprint/WidgetTree.h"
#include "Brushes/SlateColorBrush.h"
#include "Components/VerticalBoxSlot.h"
#include "Engine/DataTable.h"
#include "Styling/CoreStyle.h"
#include "Styling/SlateBrush.h"
#include "Styling/SlateTypes.h"
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

void UDialogueMachineWidget::NativeConstruct()
{
    Super::NativeConstruct();
    if (RunnerComponent)
    {
        BindToRunner(RunnerComponent);
    }
}

TSharedRef<SWidget> UDialogueMachineWidget::RebuildWidget()
{
    BuildWidgetTree();
    return Super::RebuildWidget();
}

void UDialogueMachineWidget::BuildWidgetTree()
{
    if (!WidgetTree)
    {
        WidgetTree = NewObject<UWidgetTree>(this, TEXT("WidgetTree"));
    }

    if (WidgetTree->RootWidget)
    {
        return;
    }

    UVerticalBox* RootBox = WidgetTree->ConstructWidget<UVerticalBox>(UVerticalBox::StaticClass(), TEXT("RootBox"));
    WidgetTree->RootWidget = RootBox;

    SpeakerTextBlock = WidgetTree->ConstructWidget<UTextBlock>(UTextBlock::StaticClass(), TEXT("SpeakerTextBlock"));
    SpeakerTextBlock->SetText(FText::GetEmpty());
    RootBox->AddChildToVerticalBox(SpeakerTextBlock);

    BodyRichTextBlock = WidgetTree->ConstructWidget<URichTextBlock>(URichTextBlock::StaticClass(), TEXT("BodyRichTextBlock"));
    BodyRichTextBlock->SetText(FText::GetEmpty());
    UpdateTextStyles(TSet<FString>());
    RootBox->AddChildToVerticalBox(BodyRichTextBlock);

    ChoicesVerticalBox = WidgetTree->ConstructWidget<UVerticalBox>(UVerticalBox::StaticClass(), TEXT("ChoicesVerticalBox"));
    RootBox->AddChildToVerticalBox(ChoicesVerticalBox);

    ContinueButton = WidgetTree->ConstructWidget<UButton>(UButton::StaticClass(), TEXT("ContinueButton"));
    ContinueTextBlock = WidgetTree->ConstructWidget<UTextBlock>(UTextBlock::StaticClass(), TEXT("ContinueTextBlock"));
    ContinueTextBlock->SetText(FText::FromString(TEXT("Continue")));
    ContinueButton->AddChild(ContinueTextBlock);

    ContinueButton->OnClicked.AddDynamic(this, &UDialogueMachineWidget::HandleContinueClicked);
    ContinueButton->SetVisibility(ESlateVisibility::Collapsed);
    RootBox->AddChildToVerticalBox(ContinueButton);
}

void UDialogueMachineWidget::UpdateTextStyles(const TSet<FString>& ColourRows)
{
    if (!BodyRichTextBlock) return;
    if (BodyRichTextBlock->GetTextStyleSet() && ColourRows.Difference(StyledColours).Num() == 0) return;

    StyledColours.Append(ColourRows);

    const FLinearColor White = FLinearColor::White;
    UDataTable* Styles = NewObject<UDataTable>(this);
    Styles->RowStruct = FRichTextStyleRow::StaticStruct();
    Styles->AddRow(FName("Default"), MakeRow(FCoreStyle::GetDefaultFontStyle("Regular", 24), White));
    Styles->AddRow(FName("b"), MakeRow(FCoreStyle::GetDefaultFontStyle("Bold", 24), White));
    Styles->AddRow(FName("i"), MakeRow(FCoreStyle::GetDefaultFontStyle("Italic", 24), White));
    Styles->AddRow(FName("u"), MakeRow(FCoreStyle::GetDefaultFontStyle("Regular", 24), White, true));
    for (const FString& Row : StyledColours)
    {
        const FColor Colour = FColor::FromHex(Row.RightChop(2));
        Styles->AddRow(FName(*Row), MakeRow(FCoreStyle::GetDefaultFontStyle("Regular", 24), FLinearColor::FromSRGBColor(Colour)));
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

        // The runner only offers choices on a scene's last page.
        if (RunnerComponent->GetPageIndex() >= RunnerComponent->GetPageCount() - 1)
        {
            HandleChoices(RunnerComponent->GetChoices());
        }
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

void UDialogueMachineWidget::HandleLine(const FString& Page, const FString& Speaker)
{
    if (SpeakerTextBlock)
    {
        SpeakerTextBlock->SetText(FText::FromString(Speaker));
    }
    if (BodyRichTextBlock)
    {
        TSet<FString> Colours;
        const FString Converted = ConvertRichText(Page, Colours);
        UpdateTextStyles(Colours);
        BodyRichTextBlock->SetText(FText::FromString(Converted));
    }
    if (ChoicesVerticalBox)
    {
        ChoicesVerticalBox->ClearChildren();
    }
    if (ContinueButton && RunnerComponent)
    {
        ContinueButton->SetVisibility(RunnerComponent->CanContinue() ? ESlateVisibility::Visible : ESlateVisibility::Collapsed);
    }
}

void UDialogueMachineWidget::HandleChoices(const TArray<FDialogueComponentChoice>& Choices)
{
    if (!ChoicesVerticalBox || !WidgetTree) return;
    ChoicesVerticalBox->ClearChildren();

    for (const FDialogueComponentChoice& Choice : Choices)
    {
        UDialogueChoiceButton* ChoiceBtn = WidgetTree->ConstructWidget<UDialogueChoiceButton>(UDialogueChoiceButton::StaticClass());
        UTextBlock* ChoiceTxt = WidgetTree->ConstructWidget<UTextBlock>(UTextBlock::StaticClass());
        ChoiceTxt->SetText(FText::FromString(Choice.Text));
        ChoiceBtn->AddChild(ChoiceTxt);
        ChoiceBtn->SetIsEnabled(Choice.bEnabled);
        if (!Choice.bEnabled && !Choice.DisabledReason.IsEmpty())
        {
            ChoiceBtn->SetToolTipText(FText::FromString(Choice.DisabledReason));
        }

        ChoiceBtn->ChoiceId = Choice.Id;
        ChoiceBtn->BindClick();
        ChoiceBtn->OnChoiceClicked.AddDynamic(this, &UDialogueMachineWidget::HandleChoiceClicked);

        ChoicesVerticalBox->AddChildToVerticalBox(ChoiceBtn);
    }

    if (ContinueButton && RunnerComponent)
    {
        ContinueButton->SetVisibility(RunnerComponent->CanContinue() ? ESlateVisibility::Visible : ESlateVisibility::Collapsed);
    }
}

void UDialogueMachineWidget::HandleEnded(const FString& SceneId, const FString& ChoiceId)
{
    if (ChoicesVerticalBox) ChoicesVerticalBox->ClearChildren();
    if (ContinueButton) ContinueButton->SetVisibility(ESlateVisibility::Collapsed);
}

void UDialogueMachineWidget::HandleContinueClicked()
{
    if (RunnerComponent)
    {
        RunnerComponent->ContinueDialogue();
    }
}

void UDialogueMachineWidget::HandleChoiceClicked(const FString& ChoiceId)
{
    if (RunnerComponent)
    {
        RunnerComponent->ChooseChoice(ChoiceId);
    }
}
