#pragma once

#include "CoreMinimal.h"
#include "DialogueMachineStory.h"

class DIALOGUEMACHINERUNTIME_API FDialogueRules
{
public:
    static const FString Exit;
    static const FString NoDialogue;

    static bool Compare(int32 Actual, const FString& Op, int32 Target);
    static FString Key(const FString& Type, const FString& Id);
    static bool TextMatches(const FString& Actual, const FString& Op, const FString& Target);
    static bool RequirementMet(const FString& Type, const FString& TargetId, const FString& Op, const FString& Value, const TMap<FString, int32>& Vars, const TMap<FString, FString>& TextVars, FString& OutReason);
    static bool RequirementsMet(const FDialogueChoice& Choice, const TMap<FString, int32>& Vars, const TMap<FString, FString>& TextVars, FString& OutReason);
    static FString ResolveText(const FString& Text, const TMap<FString, int32>& Vars, const TMap<FString, FString>& TextVars);
    static FString PlainText(const FString& Text);
    static int32 ApplyAmount(const FString& Type, int32 Before, int32 Amount);
    static bool IsVariableAction(const FString& Type);
    static int32 ApplyAction(const FString& Type, int32 Before, int32 Amount);
    static int32 ClampNumber(const FString& Key, int32 Value);
    static TArray<FString> SplitPages(const FString& Text);
    static void ParsePageHeader(const FString& RawPage, const FString& DefaultSpeaker, const FString& DefaultPortrait, FString& OutSpeaker, FString& OutPortrait, FString& OutText);
    static bool IsEscalate(const FString& Delivery);
    // Numeric key counting entries into an escalate scene; the next visit shows that line.
    static FString VisitKey(const FString& SceneId);
    static TArray<FString> BuildPages(const FString& Text, bool bSayItAll, uint64& InOutRandomState, int32& InOutRandomLine, const FString& Delivery = TEXT(""), int32 VisitIndex = 0);
    static uint64 SeedState(int32 Seed);
    static int32 SplitMix64(uint64& InOutState, int32 Count);
};
