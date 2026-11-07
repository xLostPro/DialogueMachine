#if WITH_DEV_AUTOMATION_TESTS

#include "Misc/AutomationTest.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonSerializer.h"
#include "Serialization/JsonWriter.h"
#include "DialogueAsset.h"
#include "DialogueMachineWidget.h"
#include "DialogueRunnerComponent.h"

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FDialogueMachineWidgetTest, "DialogueMachine.Widget", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

namespace
{
    FString ToJson(const TSharedRef<FJsonObject>& Object)
    {
        FString Out;
        const TSharedRef<TJsonWriter<>> Writer = TJsonWriterFactory<>::Create(&Out);
        FJsonSerializer::Serialize(Object, Writer);
        return Out;
    }

    TSharedPtr<FJsonValue> Choice(const FString& Id, const FString& Text, bool bNeedsCoins)
    {
        TSharedRef<FJsonObject> C = MakeShared<FJsonObject>();
        C->SetStringField(TEXT("InternalId"), Id);
        C->SetStringField(TEXT("ButtonText"), Text);
        C->SetStringField(TEXT("NextStateId"), TEXT("[EXIT]"));
        C->SetNumberField(TEXT("Visibility"), 1);
        TArray<TSharedPtr<FJsonValue>> Requirements;
        if (bNeedsCoins)
        {
            TSharedRef<FJsonObject> R = MakeShared<FJsonObject>();
            R->SetStringField(TEXT("Type"), TEXT("Inventory"));
            R->SetStringField(TEXT("TargetId"), TEXT("coin"));
            R->SetStringField(TEXT("Operator"), TEXT(">="));
            R->SetStringField(TEXT("Value"), TEXT("5"));
            Requirements.Add(MakeShared<FJsonValueObject>(R));
        }
        C->SetArrayField(TEXT("Requirements"), Requirements);
        C->SetArrayField(TEXT("AfterChoiceActions"), {});
        return MakeShared<FJsonValueObject>(C);
    }

    // One scene: a line, then a disabled choice and two enabled ones.
    FString StoryHandoff()
    {
        TSharedRef<FJsonObject> Meta = MakeShared<FJsonObject>();
        Meta->SetStringField(TEXT("speaker"), TEXT("Mara"));
        TSharedRef<FJsonObject> Scene = MakeShared<FJsonObject>();
        Scene->SetStringField(TEXT("State"), TEXT("a"));
        Scene->SetStringField(TEXT("Type"), TEXT("Dialogue"));
        Scene->SetStringField(TEXT("Text"), TEXT("Hello [b]there[/b]"));
        Scene->SetBoolField(TEXT("SayItAll"), true);
        Scene->SetStringField(TEXT("DefaultNextId"), TEXT("[EXIT]"));
        Scene->SetObjectField(TEXT("Metadata"), Meta);
        Scene->SetArrayField(TEXT("Choices"), { Choice(TEXT("rich"), TEXT("Pay"), true), Choice(TEXT("stay"), TEXT("Stay"), false), Choice(TEXT("leave"), TEXT("Leave"), false) });

        TSharedRef<FJsonObject> Story = MakeShared<FJsonObject>();
        Story->SetArrayField(TEXT("ListOfCharacterStates"), { MakeShared<FJsonValueObject>(Scene) });

        TSharedRef<FJsonObject> Handoff = MakeShared<FJsonObject>();
        Handoff->SetNumberField(TEXT("SchemaVersion"), 1);
        Handoff->SetStringField(TEXT("Contract"), TEXT("dialogue-machine.story.v1"));
        Handoff->SetStringField(TEXT("StorySha256"), TEXT("test"));
        Handoff->SetStringField(TEXT("StartingScene"), TEXT("a"));
        Handoff->SetNumberField(TEXT("Seed"), 1);
        Handoff->SetObjectField(TEXT("NumericValues"), MakeShared<FJsonObject>());
        Handoff->SetObjectField(TEXT("TextValues"), MakeShared<FJsonObject>());
        Handoff->SetStringField(TEXT("StoryJson"), ToJson(Story));
        return ToJson(Handoff);
    }
}

bool FDialogueMachineWidgetTest::RunTest(const FString& Parameters)
{
    // Typing reveals rich text without breaking its tags.
    int32 Total = 0;
    const FString Rich = UDialogueMachineWidget::ToUnrealRichText(TEXT("Hi [b]bold[/b] & [colour=red]red[/colour]"));
    TestEqual(TEXT("Reveal counts visible characters"), (UDialogueMachineWidget::RevealRichText(Rich, MAX_int32, Total), Total), 13);
    TestEqual(TEXT("Reveal closes an open tag"), UDialogueMachineWidget::RevealRichText(Rich, 5, Total), FString(TEXT("Hi <b>bo</>")));
    TestEqual(TEXT("Reveal counts an entity as one character"), UDialogueMachineWidget::RevealRichText(Rich, 9, Total), FString(TEXT("Hi <b>bold</> &amp;")));

    UDialogueAsset* Asset = NewObject<UDialogueAsset>();
    Asset->SetData(StoryHandoff(), TEXT("test"), TEXT("Test"));
    UDialogueRunnerComponent* Runner = NewObject<UDialogueRunnerComponent>();
    Runner->Story = Asset;
    UDialogueMachineWidget* Widget = NewObject<UDialogueMachineWidget>();
    Widget->CharactersPerSecond = 1.f;
    Widget->Initialize();
    Widget->TakeWidget();
    Widget->BindToRunner(Runner);

    TestTrue(TEXT("Story starts"), Runner->StartDialogue());
    FString Chosen;
    Runner->GetInternalRunner()->OnEnded.AddLambda([&Chosen](const FString&, const FString& ChoiceId) { Chosen = ChoiceId; });
    TestTrue(TEXT("The first line types"), Widget->IsOpen() && Widget->IsTyping());

    Widget->Submit();
    TestTrue(TEXT("The press that started the dialogue doesn't skip its first line"), Widget->IsTyping());

    ++GFrameCounter;
    Widget->Submit();
    TestTrue(TEXT("Submit finishes the typing and shows the choices"), !Widget->IsTyping() && Widget->IsShowingChoices());
    TestEqual(TEXT("The highlight starts on the first enabled choice"), Widget->GetSelectedIndex(), 1);
    Widget->Submit();
    TestFalse(TEXT("One action per frame"), Runner->IsEnded());

    Widget->MoveSelection(1);
    TestEqual(TEXT("MoveSelection moves down"), Widget->GetSelectedIndex(), 2);
    Widget->MoveSelection(1);
    TestEqual(TEXT("MoveSelection wraps and skips the disabled choice"), Widget->GetSelectedIndex(), 1);
    Widget->MoveSelection(-1);

    ++GFrameCounter;
    Widget->SelectChoice(0);
    TestFalse(TEXT("SelectChoice ignores a disabled choice"), Runner->IsEnded());
    ++GFrameCounter;
    Widget->Submit();
    TestEqual(TEXT("Submit picks the highlighted choice"), Chosen, FString(TEXT("leave")));
    TestFalse(TEXT("The box closes when the dialogue ends"), Widget->IsOpen());
    return true;
}

#endif
