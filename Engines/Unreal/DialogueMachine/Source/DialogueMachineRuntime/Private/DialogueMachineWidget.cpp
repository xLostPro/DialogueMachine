#include "DialogueMachineWidget.h"
#include "Blueprint/WidgetTree.h"
#include "Components/VerticalBoxSlot.h"

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

void UDialogueMachineWidget::BindToRunner(UDialogueRunnerComponent* InRunner)
{
    RunnerComponent = InRunner;
    if (!RunnerComponent) return;

    RunnerComponent->OnLine.AddDynamic(this, &UDialogueMachineWidget::HandleLine);
    RunnerComponent->OnChoices.AddDynamic(this, &UDialogueMachineWidget::HandleChoices);
    RunnerComponent->OnEnded.AddDynamic(this, &UDialogueMachineWidget::HandleEnded);

    if (RunnerComponent->GetInternalRunner().IsValid())
    {
        HandleLine(RunnerComponent->GetCurrentPage(), RunnerComponent->GetSpeaker());
        HandleChoices(RunnerComponent->GetChoices());
    }
}

FString UDialogueMachineWidget::ToUnrealRichText(const FString& Text)
{
    if (Text.IsEmpty()) return Text;

    // Escape raw < by inserting a zero-width space after it so Unreal rich text parser doesn't break
    FString Escaped = Text.Replace(TEXT("<"), TEXT("<\u200B"));

    // Replace [b], [/b], [i], [/i], [u], [/u], [colour=X], [/colour]
    FString Result;
    int32 Pos = 0;
    int32 Len = Escaped.Len();

    while (Pos < Len)
    {
        if (Escaped[Pos] == TEXT('['))
        {
            int32 CheckPos = Pos + 1;
            bool bClosing = false;
            if (CheckPos < Len && Escaped[CheckPos] == TEXT('/'))
            {
                bClosing = true;
                ++CheckPos;
            }

            // Check b, i, u
            if (CheckPos < Len)
            {
                TCHAR C = FChar::ToLower(Escaped[CheckPos]);
                if ((C == TEXT('b') || C == TEXT('i') || C == TEXT('u')) && CheckPos + 1 < Len && Escaped[CheckPos + 1] == TEXT(']'))
                {
                    Result += bClosing ? FString::Printf(TEXT("</%c>"), C) : FString::Printf(TEXT("<%c>"), C);
                    Pos = CheckPos + 2;
                    continue;
                }
            }

            // Check colour or colour=...
            if (CheckPos + 6 <= Len && Escaped.Mid(CheckPos, 6).Compare(TEXT("colour"), ESearchCase::IgnoreCase) == 0)
            {
                int32 AfterColour = CheckPos + 6;
                if (bClosing && AfterColour < Len && Escaped[AfterColour] == TEXT(']'))
                {
                    Result += TEXT("</color>");
                    Pos = AfterColour + 1;
                    continue;
                }
                else if (!bClosing && AfterColour < Len && Escaped[AfterColour] == TEXT('='))
                {
                    int32 CloseBracket = Escaped.Find(TEXT("]"), ESearchCase::CaseSensitive, ESearchDir::FromStart, AfterColour + 1);
                    if (CloseBracket != INDEX_NONE)
                    {
                        FString ColorVal = Escaped.Mid(AfterColour + 1, CloseBracket - (AfterColour + 1));
                        Result += FString::Printf(TEXT("<color=\"%s\">"), *ColorVal);
                        Pos = CloseBracket + 1;
                        continue;
                    }
                }
            }
        }

        Result += Escaped[Pos];
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
        BodyRichTextBlock->SetText(FText::FromString(ToUnrealRichText(Page)));
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
        UButton* ChoiceBtn = WidgetTree->ConstructWidget<UButton>(UButton::StaticClass());
        UTextBlock* ChoiceTxt = WidgetTree->ConstructWidget<UTextBlock>(UTextBlock::StaticClass());
        ChoiceTxt->SetText(FText::FromString(Choice.Text));
        ChoiceBtn->AddChild(ChoiceTxt);
        ChoiceBtn->SetIsEnabled(Choice.bEnabled);

        FString ChoiceId = Choice.Id;
        ChoiceBtn->OnClicked.AddDynamic(this, &UDialogueMachineWidget::HandleContinueClicked);
        // Bind choice click via lambda or dynamic
        ChoiceBtn->OnClicked.Clear();
        ChoiceBtn->OnClicked.AddLambda([this, ChoiceId]()
        {
            HandleChoiceClicked(ChoiceId);
        });

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

void UDialogueMachineWidget::HandleChoiceClicked(FString ChoiceId)
{
    if (RunnerComponent)
    {
        RunnerComponent->ChooseChoice(ChoiceId);
    }
}
