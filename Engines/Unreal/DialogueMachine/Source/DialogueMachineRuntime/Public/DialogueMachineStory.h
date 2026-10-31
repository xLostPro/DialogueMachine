#pragma once

#include "CoreMinimal.h"
#include "Dom/JsonObject.h"

struct DIALOGUEMACHINERUNTIME_API FDialogueAction
{
    FString Type = TEXT("Affinity");
    FString TargetId;
    int32 Amount = 0;
    FString Parameter;
};

struct DIALOGUEMACHINERUNTIME_API FDialogueRequirement
{
    FString Type = TEXT("Affinity");
    FString TargetId;
    FString Operator = TEXT(">=");
    FString Value = TEXT("0");
};

struct DIALOGUEMACHINERUNTIME_API FDialogueChoice
{
    enum EVisibility
    {
        Hide = 0,
        Disable = 1,
        Always = 2
    };

    FString InternalId;
    FString ButtonText;
    FString NextStateId;
    int32 Visibility = Disable;
    TArray<FDialogueRequirement> Requirements;
    TArray<FDialogueAction> AfterChoiceActions;
};

struct DIALOGUEMACHINERUNTIME_API FDialogueScene
{
    FString State;
    FString Type = TEXT("Dialogue");
    FString Text;
    FString DefaultNextId = TEXT("[EXIT]");
    bool bSayItAll = true;
    FString ShopId;
    TArray<FDialogueChoice> Choices;
    TArray<FDialogueAction> DefaultAfterActions;
    TMap<FString, FString> Metadata;

    FString GetTitle() const
    {
        const FString* Val = Metadata.Find(TEXT("title"));
        return Val ? *Val : State;
    }

    FString GetSpeaker() const
    {
        const FString* Val = Metadata.Find(TEXT("speaker"));
        return Val ? *Val : TEXT("Character");
    }
};

class DIALOGUEMACHINERUNTIME_API FDialogueStory
{
public:
    TArray<TSharedPtr<FDialogueScene>> Scenes;
    TMap<FString, TSharedPtr<FDialogueScene>> ById;

    TSharedPtr<FDialogueScene> Find(const FString& Id) const
    {
        const TSharedPtr<FDialogueScene>* Found = ById.Find(Id);
        return Found ? *Found : nullptr;
    }

    static TSharedPtr<FDialogueStory> Parse(const FString& StoryJson, FString& OutError);

private:
    static TSharedPtr<FDialogueScene> ReadScene(const TSharedPtr<FJsonObject>& Source);
    static FDialogueChoice ReadChoice(const TSharedPtr<FJsonObject>& Source);
    static FDialogueRequirement ReadRequirement(const TSharedPtr<FJsonObject>& Source);
    static TArray<FDialogueAction> ReadActions(const TSharedPtr<FJsonObject>& Source, const FString& Key);
};

struct DIALOGUEMACHINERUNTIME_API FDialogueHandoff
{
    static constexpr int32 SupportedSchemaVersion = 1;

    int32 SchemaVersion = 1;
    FString Contract;
    FString StoryJson;
    FString StorySha256;
    FString StartingScene;
    int32 Seed = 0;
    TMap<FString, int32> NumericValues;
    TMap<FString, FString> TextValues;
    int32 MaxOperations = 1000;
    TSharedPtr<FDialogueStory> Story;

    static bool Parse(const FString& Json, FDialogueHandoff& OutHandoff, FString& OutError);
};
