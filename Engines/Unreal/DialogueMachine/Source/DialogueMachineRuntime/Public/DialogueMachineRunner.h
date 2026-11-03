#pragma once

#include "CoreMinimal.h"
#include "DialogueMachineStory.h"
#include "DialogueMachineRules.h"

struct DIALOGUEMACHINERUNTIME_API FDialogueRuntimeChoice
{
    FString Id;
    FString Text;
    bool bEnabled = false;
    FString DisabledReason;
};

class DIALOGUEMACHINERUNTIME_API IDialogueVariables
{
public:
    virtual ~IDialogueVariables() = default;
    virtual bool HasNumber(const FString& Key) const = 0;
    virtual int32 GetNumber(const FString& Key) const = 0;
    virtual void SetNumber(const FString& Key, int32 Value) = 0;
    virtual bool GetText(const FString& Key, FString& OutValue) const = 0;
    virtual void SetText(const FString& Key, const FString& Value) = 0;
    virtual TArray<FString> GetKeys() const = 0;
};

class DIALOGUEMACHINERUNTIME_API FDictionaryVariables : public IDialogueVariables
{
public:
    TMap<FString, int32> Numbers;
    TMap<FString, FString> Texts;

    virtual bool HasNumber(const FString& Key) const override { return Numbers.Contains(Key); }
    virtual int32 GetNumber(const FString& Key) const override { const int32* V = Numbers.Find(Key); return V ? *V : 0; }
    virtual void SetNumber(const FString& Key, int32 Value) override { Numbers.Add(Key, Value); }
    virtual bool GetText(const FString& Key, FString& OutValue) const override
    {
        const FString* V = Texts.Find(Key);
        if (V) { OutValue = *V; return true; }
        return false;
    }
    virtual void SetText(const FString& Key, const FString& Value) override { Texts.Add(Key, Value); }
    virtual TArray<FString> GetKeys() const override
    {
        TSet<FString> KeySet;
        for (const auto& P : Numbers) KeySet.Add(P.Key);
        for (const auto& P : Texts) KeySet.Add(P.Key);
        return KeySet.Array();
    }
};

class DIALOGUEMACHINERUNTIME_API FDialogueRunner : public TSharedFromThis<FDialogueRunner>
{
public:
    DECLARE_MULTICAST_DELEGATE_OneParam(FOnLineShown, const FString&);
    DECLARE_MULTICAST_DELEGATE_OneParam(FOnChoicesShown, const TArray<FDialogueRuntimeChoice>&);
    DECLARE_MULTICAST_DELEGATE_FourParams(FOnActionRaised, const FString&, const FString&, const FString&, int32);
    DECLARE_MULTICAST_DELEGATE_OneParam(FOnShopOpened, const FString&);
    DECLARE_MULTICAST_DELEGATE_ThreeParams(FOnVariableChanged, const FString&, int32, int32);
    DECLARE_MULTICAST_DELEGATE_TwoParams(FOnEnded, const FString&, const FString&);
    DECLARE_MULTICAST_DELEGATE_OneParam(FOnWarning, const FString&);

    FOnLineShown OnLineShown;
    FOnChoicesShown OnChoicesShown;
    FOnActionRaised OnActionRaised;
    FOnShopOpened OnShopOpened;
    FOnVariableChanged OnVariableChanged;
    FOnEnded OnEnded;
    FOnWarning OnWarning;

    FDialogueRunner(TSharedPtr<IDialogueVariables> CustomVariables = nullptr);
    ~FDialogueRunner() = default;

    bool Load(const FString& HandoffJson, FString& OutError);
    void Load(const FDialogueHandoff& Story);

    bool Start(const FString& SceneId = TEXT(""), TOptional<int32> Seed = TOptional<int32>(), const TMap<FString, int32>* NumericOverrides = nullptr, const TMap<FString, FString>* TextOverrides = nullptr, FString* OutError = nullptr);
    bool Continue(FString* OutError = nullptr);
    bool Choose(const FString& ChoiceId, FString* OutError = nullptr);
    bool Choose(const FGuid& ChoiceId, FString* OutError = nullptr);
    void ReplaceStory(const FDialogueHandoff& Story);

    FString SaveState() const;
    bool RestoreState(const FString& Json, FString& OutError);

    FString GetCurrentSceneId() const { return CurrentScene.IsValid() ? CurrentScene->State : TEXT(""); }
    FString GetSpeaker() const { return CurrentScene.IsValid() ? CurrentScene->GetSpeaker() : TEXT(""); }
    FString GetTitle() const { return CurrentScene.IsValid() ? CurrentScene->GetTitle() : TEXT(""); }
    bool IsEnded() const { return bIsEnded; }
    FString GetCurrentPage() const { return (CurrentScene.IsValid() && Pages.IsValidIndex(PageIndex)) ? Pages[PageIndex] : TEXT(""); }
    FString GetCurrentPagePlain() const { return CurrentScene.IsValid() ? FDialogueRules::PlainText(GetCurrentPage()) : TEXT(""); }
    int32 GetPageIndex() const { return PageIndex; }
    int32 GetPageCount() const { return Pages.Num(); }
    TMap<FString, FString> GetCurrentMetadata() const { return CurrentScene.IsValid() ? CurrentScene->Metadata : TMap<FString, FString>(); }
    bool CanContinue() const;
    TArray<FDialogueRuntimeChoice> GetChoices() const;

    TSharedPtr<IDialogueVariables> GetVariables() const { return Variables; }

private:
    bool bOwnsVariables;
    TSharedPtr<IDialogueVariables> Store;
    TSharedPtr<IDialogueVariables> Variables;
    FDialogueHandoff Handoff;
    bool bHasHandoff = false;
    TSharedPtr<FDialogueScene> CurrentScene;
    TArray<FString> RawPages;
    TArray<FString> Pages;
    TMap<FString, int32> Visits;
    int32 CurrentVisitIndex = 0;
    int32 PageIndex = 0;
    uint64 RandomState = 0;
    int32 RandomLine = -1;
    bool bIsEnded = false;

    bool HasMorePages() const { return PageIndex < Pages.Num() - 1; }
    void AddMissingVariables();
    TMap<FString, int32> GetNumbers() const;
    TMap<FString, FString> GetTexts() const;
    void RebuildPages();
    void RefreshPage();
    void Deliver();
    bool Enter(const FString& Id, FString* OutError = nullptr);
    void End(const FString& SceneId, const FString& ChoiceId);
    bool Move(const FString& Id, const FDialogueChoice* Choice, FString* OutError = nullptr);
    void ApplyActions(const TArray<FDialogueAction>& Actions);
    static bool IsExit(const FString& Id) { return Id.TrimStartAndEnd().IsEmpty() || Id == FDialogueRules::Exit; }

    class FTrackedVariables : public IDialogueVariables
    {
    public:
        FDialogueRunner* Runner;
        FTrackedVariables(FDialogueRunner* InRunner) : Runner(InRunner) {}
        virtual bool HasNumber(const FString& Key) const override { return Runner->Store->HasNumber(Key); }
        virtual int32 GetNumber(const FString& Key) const override { return Runner->Store->GetNumber(Key); }
        virtual void SetNumber(const FString& Key, int32 Value) override;
        virtual bool GetText(const FString& Key, FString& OutValue) const override { return Runner->Store->GetText(Key, OutValue); }
        virtual void SetText(const FString& Key, const FString& Value) override;
        virtual TArray<FString> GetKeys() const override { return Runner->Store->GetKeys(); }
    };
};
