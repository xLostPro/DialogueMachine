#include "DialogueMachineStory.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"

TSharedPtr<FDialogueStory> FDialogueStory::Parse(const FString& StoryJson, FString& OutError)
{
    TSharedPtr<FJsonObject> Root;
    TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(StoryJson);
    if (!FJsonSerializer::Deserialize(Reader, Root) || !Root.IsValid())
    {
        OutError = TEXT("Failed to parse story JSON.");
        return nullptr;
    }

    const TArray<TSharedPtr<FJsonValue>>* List = nullptr;
    if (!Root->TryGetArrayField(TEXT("ListOfCharacterStates"), List) || !List)
    {
        OutError = TEXT("A story must contain ListOfCharacterStates.");
        return nullptr;
    }

    TSharedPtr<FDialogueStory> Story = MakeShared<FDialogueStory>();
    for (const TSharedPtr<FJsonValue>& Value : *List)
    {
        const TSharedPtr<FJsonObject>* SceneObj = nullptr;
        if (!Value->TryGetObject(SceneObj) || !SceneObj || !SceneObj->IsValid())
        {
            continue;
        }

        TSharedPtr<FDialogueScene> Scene = ReadScene(*SceneObj);
        if (Scene->State.TrimStartAndEnd().IsEmpty())
        {
            OutError = TEXT("Each scene needs a non-empty ID.");
            return nullptr;
        }
        if (Scene->State == TEXT("[EXIT]"))
        {
            OutError = TEXT("[EXIT] is reserved for endings.");
            return nullptr;
        }
        if (Story->ById.Contains(Scene->State))
        {
            OutError = FString::Printf(TEXT("Duplicate scene ID '%s'."), *Scene->State);
            return nullptr;
        }

        Story->ById.Add(Scene->State, Scene);
        Story->Scenes.Add(Scene);
    }

    return Story;
}

TSharedPtr<FDialogueScene> FDialogueStory::ReadScene(const TSharedPtr<FJsonObject>& Source)
{
    TSharedPtr<FDialogueScene> Scene = MakeShared<FDialogueScene>();
    Source->TryGetStringField(TEXT("State"), Scene->State);
    Source->TryGetStringField(TEXT("Type"), Scene->Type);
    if (Scene->Type.IsEmpty())
    {
        Scene->Type = TEXT("Dialogue");
    }
    Source->TryGetStringField(TEXT("Text"), Scene->Text);
    if (!Source->TryGetStringField(TEXT("DefaultNextId"), Scene->DefaultNextId))
    {
        Scene->DefaultNextId = TEXT("[EXIT]");
    }

    bool bSayItAll = true;
    if (Source->TryGetBoolField(TEXT("SayItAll"), bSayItAll))
    {
        Scene->bSayItAll = bSayItAll;
    }

    const TSharedPtr<FJsonObject>* ShopObj = nullptr;
    if (Source->TryGetObjectField(TEXT("ShopData"), ShopObj) && ShopObj && ShopObj->IsValid())
    {
        (*ShopObj)->TryGetStringField(TEXT("ShopId"), Scene->ShopId);
    }

    const TSharedPtr<FJsonObject>* MetadataObj = nullptr;
    if (Source->TryGetObjectField(TEXT("Metadata"), MetadataObj) && MetadataObj && MetadataObj->IsValid())
    {
        for (const auto& Pair : (*MetadataObj)->Values)
        {
            FString StrVal;
            if (Pair.Value.IsValid() && Pair.Value->TryGetString(StrVal))
            {
                Scene->Metadata.Add(FString(Pair.Key), StrVal);
            }
        }
    }

    const TArray<TSharedPtr<FJsonValue>>* ChoicesArray = nullptr;
    if (Source->TryGetArrayField(TEXT("Choices"), ChoicesArray) && ChoicesArray)
    {
        for (const TSharedPtr<FJsonValue>& ChoiceVal : *ChoicesArray)
        {
            const TSharedPtr<FJsonObject>* ChoiceObj = nullptr;
            if (ChoiceVal->TryGetObject(ChoiceObj) && ChoiceObj && ChoiceObj->IsValid())
            {
                Scene->Choices.Add(ReadChoice(*ChoiceObj));
            }
        }
    }

    Scene->DefaultAfterActions = ReadActions(Source, TEXT("DefaultAfterActions"));
    return Scene;
}

FDialogueChoice FDialogueStory::ReadChoice(const TSharedPtr<FJsonObject>& Source)
{
    FDialogueChoice Choice;
    Source->TryGetStringField(TEXT("InternalId"), Choice.InternalId);
    Source->TryGetStringField(TEXT("ButtonText"), Choice.ButtonText);
    Source->TryGetStringField(TEXT("NextStateId"), Choice.NextStateId);

    int32 Visibility = FDialogueChoice::Disable;
    if (Source->TryGetNumberField(TEXT("Visibility"), Visibility))
    {
        Choice.Visibility = Visibility;
    }

    FString Match;
    Source->TryGetStringField(TEXT("RequirementMatch"), Match);
    Choice.bAnyRequirement = Match == TEXT("Any");

    const TArray<TSharedPtr<FJsonValue>>* ReqArray = nullptr;
    if (Source->TryGetArrayField(TEXT("Requirements"), ReqArray) && ReqArray)
    {
        for (const TSharedPtr<FJsonValue>& ReqVal : *ReqArray)
        {
            const TSharedPtr<FJsonObject>* ReqObj = nullptr;
            if (ReqVal->TryGetObject(ReqObj) && ReqObj && ReqObj->IsValid())
            {
                Choice.Requirements.Add(ReadRequirement(*ReqObj));
            }
        }
    }

    Choice.AfterChoiceActions = ReadActions(Source, TEXT("AfterChoiceActions"));
    return Choice;
}

FDialogueRequirement FDialogueStory::ReadRequirement(const TSharedPtr<FJsonObject>& Source)
{
    FDialogueRequirement Req;
    Source->TryGetStringField(TEXT("Type"), Req.Type);
    if (Req.Type.IsEmpty())
    {
        Req.Type = TEXT("Affinity");
    }
    Source->TryGetStringField(TEXT("TargetId"), Req.TargetId);
    Source->TryGetStringField(TEXT("Operator"), Req.Operator);
    if (Req.Operator.IsEmpty())
    {
        Req.Operator = TEXT(">=");
    }

    double NumVal = 0.0;
    if (Source->TryGetNumberField(TEXT("Value"), NumVal))
    {
        Req.Value = FString::FromInt((int32)NumVal);
    }
    else
    {
        Source->TryGetStringField(TEXT("Value"), Req.Value);
        if (Req.Value.IsEmpty())
        {
            Req.Value = TEXT("0");
        }
    }

    return Req;
}

TArray<FDialogueAction> FDialogueStory::ReadActions(const TSharedPtr<FJsonObject>& Source, const FString& Key)
{
    TArray<FDialogueAction> Actions;
    const TArray<TSharedPtr<FJsonValue>>* ActionArray = nullptr;
    if (Source->TryGetArrayField(Key, ActionArray) && ActionArray)
    {
        for (const TSharedPtr<FJsonValue>& ActionVal : *ActionArray)
        {
            const TSharedPtr<FJsonObject>* ActionObj = nullptr;
            if (ActionVal->TryGetObject(ActionObj) && ActionObj && ActionObj->IsValid())
            {
                FDialogueAction Action;
                (*ActionObj)->TryGetStringField(TEXT("Type"), Action.Type);
                if (Action.Type.IsEmpty())
                {
                    Action.Type = TEXT("Affinity");
                }
                (*ActionObj)->TryGetStringField(TEXT("TargetId"), Action.TargetId);
                (*ActionObj)->TryGetNumberField(TEXT("Amount"), Action.Amount);
                (*ActionObj)->TryGetStringField(TEXT("Parameter"), Action.Parameter);
                Actions.Add(Action);
            }
        }
    }
    return Actions;
}

bool FDialogueHandoff::Parse(const FString& Json, FDialogueHandoff& OutHandoff, FString& OutError)
{
    TSharedPtr<FJsonObject> Root;
    TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(Json);
    if (!FJsonSerializer::Deserialize(Reader, Root) || !Root.IsValid())
    {
        OutError = TEXT("A runtime handoff must be a JSON object.");
        return false;
    }

    int32 Schema = 0;
    if (Root->TryGetNumberField(TEXT("SchemaVersion"), Schema))
    {
        OutHandoff.SchemaVersion = Schema;
    }
    if (OutHandoff.SchemaVersion > SupportedSchemaVersion)
    {
        OutError = FString::Printf(TEXT("This story uses runtime schema %d, but this plugin supports up to %d. Update the Dialogue Machine plugin."), OutHandoff.SchemaVersion, SupportedSchemaVersion);
        return false;
    }

    if (!Root->TryGetStringField(TEXT("StoryJson"), OutHandoff.StoryJson) || OutHandoff.StoryJson.IsEmpty())
    {
        OutError = TEXT("A runtime handoff must contain StoryJson.");
        return false;
    }

    Root->TryGetStringField(TEXT("Contract"), OutHandoff.Contract);
    Root->TryGetStringField(TEXT("StorySha256"), OutHandoff.StorySha256);
    Root->TryGetStringField(TEXT("StartingScene"), OutHandoff.StartingScene);
    Root->TryGetNumberField(TEXT("Seed"), OutHandoff.Seed);
    Root->TryGetNumberField(TEXT("MaxOperations"), OutHandoff.MaxOperations);

    const TSharedPtr<FJsonObject>* NumericObj = nullptr;
    if (Root->TryGetObjectField(TEXT("NumericValues"), NumericObj) && NumericObj && NumericObj->IsValid())
    {
        for (const auto& Pair : (*NumericObj)->Values)
        {
            double NumVal = 0.0;
            if (Pair.Value.IsValid() && Pair.Value->TryGetNumber(NumVal))
            {
                OutHandoff.NumericValues.Add(FString(Pair.Key), (int32)NumVal);
            }
        }
    }

    const TSharedPtr<FJsonObject>* TextObj = nullptr;
    if (Root->TryGetObjectField(TEXT("TextValues"), TextObj) && TextObj && TextObj->IsValid())
    {
        for (const auto& Pair : (*TextObj)->Values)
        {
            FString StrVal;
            if (Pair.Value.IsValid() && Pair.Value->TryGetString(StrVal))
            {
                OutHandoff.TextValues.Add(FString(Pair.Key), StrVal);
            }
        }
    }

    OutHandoff.Story = FDialogueStory::Parse(OutHandoff.StoryJson, OutError);
    return OutHandoff.Story.IsValid();
}
