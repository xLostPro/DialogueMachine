#include "DialogueMachineRunner.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"

FDialogueRunner::FDialogueRunner(TSharedPtr<IDialogueVariables> CustomVariables)
{
    bOwnsVariables = !CustomVariables.IsValid();
    Store = CustomVariables.IsValid() ? CustomVariables : MakeShared<FDictionaryVariables>();
    Variables = MakeShared<FTrackedVariables>(this);
}

bool FDialogueRunner::Load(const FString& HandoffJson, FString& OutError)
{
    if (FDialogueHandoff::Parse(HandoffJson, Handoff, OutError))
    {
        bHasHandoff = true;
        return true;
    }
    return false;
}

void FDialogueRunner::Load(const FDialogueHandoff& Story)
{
    Handoff = Story;
    bHasHandoff = true;
}

bool FDialogueRunner::Start(const FString& SceneId, TOptional<int32> Seed, const TMap<FString, int32>* NumericOverrides, const TMap<FString, FString>* TextOverrides, FString* OutError)
{
    if (!bHasHandoff)
    {
        if (OutError) *OutError = TEXT("Load a story before starting it.");
        return false;
    }

    if (bOwnsVariables)
    {
        Store = MakeShared<FDictionaryVariables>();
    }
    AddMissingVariables();

    if (NumericOverrides)
    {
        for (const auto& Pair : *NumericOverrides)
        {
            int32 Val = Pair.Key.StartsWith(TEXT("inv:")) ? FMath::Max(0, Pair.Value) : Pair.Value;
            Store->SetNumber(Pair.Key, Val);
        }
    }
    if (TextOverrides)
    {
        for (const auto& Pair : *TextOverrides)
        {
            Store->SetText(Pair.Key, Pair.Value);
        }
    }

    RandomState = FDialogueRules::SeedState(Seed.IsSet() ? Seed.GetValue() : Handoff.Seed);
    CurrentScene = nullptr;
    bIsEnded = false;

    FString StartId = SceneId.IsEmpty() ? Handoff.StartingScene : SceneId;
    return Enter(StartId, OutError);
}

bool FDialogueRunner::Continue(FString* OutError)
{
    if (HasMorePages())
    {
        PageIndex++;
        Deliver();
        return true;
    }
    else if (CurrentScene.IsValid())
    {
        return Move(CurrentScene->DefaultNextId, nullptr, OutError);
    }
    return false;
}

bool FDialogueRunner::Choose(const FString& ChoiceId, FString* OutError)
{
    if (!CurrentScene.IsValid())
    {
        if (OutError) *OutError = TEXT("No active scene.");
        return false;
    }

    const FDialogueChoice* FoundChoice = nullptr;
    for (const FDialogueChoice& C : CurrentScene->Choices)
    {
        if (C.InternalId.Equals(ChoiceId, ESearchCase::IgnoreCase))
        {
            FoundChoice = &C;
            break;
        }
    }

    if (!FoundChoice)
    {
        if (OutError) *OutError = TEXT("Select a choice from the current scene.");
        return false;
    }

    if (HasMorePages())
    {
        if (OutError) *OutError = TEXT("Finish the dialogue pages before choosing.");
        return false;
    }

    FString Reason;
    if (FoundChoice->Visibility != FDialogueChoice::Always && !FDialogueRules::RequirementsMet(*FoundChoice, GetNumbers(), Reason))
    {
        if (OutError) *OutError = Reason;
        return false;
    }

    FString DestId = FoundChoice->NextStateId.TrimStartAndEnd().IsEmpty() ? CurrentScene->DefaultNextId : FoundChoice->NextStateId;
    return Move(DestId, FoundChoice, OutError);
}

bool FDialogueRunner::Choose(const FGuid& ChoiceId, FString* OutError)
{
    return Choose(ChoiceId.ToString(EGuidFormats::DigitsWithHyphensLower), OutError);
}

void FDialogueRunner::ReplaceStory(const FDialogueHandoff& Story)
{
    Handoff = Story;
    bHasHandoff = true;
    AddMissingVariables();

    if (!CurrentScene.IsValid()) return;

    FString PrevId = CurrentScene->State;
    CurrentScene = Handoff.Story.IsValid() ? Handoff.Story->Find(PrevId) : nullptr;
    if (!CurrentScene.IsValid())
    {
        Pages.Empty();
        PageIndex = 0;
        bIsEnded = true;
        OnWarning.Broadcast(FString::Printf(TEXT("Scene '%s' no longer exists, so the conversation ended."), *PrevId));
        return;
    }

    RebuildPages();
    Deliver();
}

FString FDialogueRunner::SaveState() const
{
    TSharedPtr<FJsonObject> Root = MakeShared<FJsonObject>();
    if (CurrentScene.IsValid())
    {
        Root->SetStringField(TEXT("sceneId"), CurrentScene->State);
    }
    else
    {
        Root->SetField(TEXT("sceneId"), MakeShared<FJsonValueNull>());
    }

    Root->SetNumberField(TEXT("pageIndex"), PageIndex);
    Root->SetStringField(TEXT("randomState"), FString::Printf(TEXT("%llu"), RandomState));
    Root->SetNumberField(TEXT("randomLine"), RandomLine);

    TSharedPtr<FJsonObject> NumObj = MakeShared<FJsonObject>();
    for (const auto& Pair : GetNumbers())
    {
        NumObj->SetNumberField(Pair.Key, Pair.Value);
    }
    Root->SetObjectField(TEXT("numeric"), NumObj);

    TSharedPtr<FJsonObject> TextObj = MakeShared<FJsonObject>();
    for (const auto& Pair : GetTexts())
    {
        TextObj->SetStringField(Pair.Key, Pair.Value);
    }
    Root->SetObjectField(TEXT("text"), TextObj);

    Root->SetStringField(TEXT("storySha256"), Handoff.StorySha256);

    FString OutputString;
    TSharedRef<TJsonWriter<>> Writer = TJsonWriterFactory<>::Create(&OutputString);
    FJsonSerializer::Serialize(Root.ToSharedRef(), Writer);
    return OutputString;
}

bool FDialogueRunner::RestoreState(const FString& Json, FString& OutError)
{
    if (!bHasHandoff)
    {
        OutError = TEXT("Load a story before restoring a save.");
        return false;
    }

    TSharedPtr<FJsonObject> Root;
    TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(Json);
    if (!FJsonSerializer::Deserialize(Reader, Root) || !Root.IsValid())
    {
        OutError = TEXT("A save state must be a JSON object.");
        return false;
    }

    FString SceneId;
    bool bHasSceneId = Root->TryGetStringField(TEXT("sceneId"), SceneId);
    TSharedPtr<FDialogueScene> SavedScene = nullptr;
    if (bHasSceneId && !SceneId.IsEmpty())
    {
        SavedScene = Handoff.Story.IsValid() ? Handoff.Story->Find(SceneId) : nullptr;
        if (!SavedScene.IsValid())
        {
            OutError = FString::Printf(TEXT("Scene '%s' does not exist."), *SceneId);
            return false;
        }
    }

    if (bOwnsVariables)
    {
        Store = MakeShared<FDictionaryVariables>();
    }

    const TSharedPtr<FJsonObject>* NumObj = nullptr;
    if (Root->TryGetObjectField(TEXT("numeric"), NumObj) && NumObj && NumObj->IsValid())
    {
        for (const auto& Pair : (*NumObj)->Values)
        {
            double Val = 0.0;
            if (Pair.Value.IsValid() && Pair.Value->TryGetNumber(Val))
            {
                Store->SetNumber(FString(Pair.Key), (int32)Val);
            }
        }
    }

    const TSharedPtr<FJsonObject>* TextObj = nullptr;
    if (Root->TryGetObjectField(TEXT("text"), TextObj) && TextObj && TextObj->IsValid())
    {
        for (const auto& Pair : (*TextObj)->Values)
        {
            FString Val;
            if (Pair.Value.IsValid() && Pair.Value->TryGetString(Val))
            {
                Store->SetText(FString(Pair.Key), Val);
            }
        }
    }

    // A save without these fields means "none yet", not "keep what the runner had".
    RandomState = 0;
    RandomLine = -1;
    PageIndex = 0;

    FString StateStr;
    if (Root->TryGetStringField(TEXT("randomState"), StateStr))
    {
        RandomState = FCString::Strtoui64(*StateStr, nullptr, 10);
    }

    Root->TryGetNumberField(TEXT("randomLine"), RandomLine);
    Root->TryGetNumberField(TEXT("pageIndex"), PageIndex);

    CurrentScene = SavedScene;
    bIsEnded = !SavedScene.IsValid();
    Pages.Empty();

    if (!SavedScene.IsValid())
    {
        PageIndex = 0;
    }
    else
    {
        RebuildPages();
        Deliver();
    }
    return true;
}

bool FDialogueRunner::CanContinue() const
{
    if (!CurrentScene.IsValid()) return false;
    if (HasMorePages()) return true;

    TArray<FDialogueRuntimeChoice> Choices = GetChoices();
    bool bHasEnabledChoice = false;
    for (const FDialogueRuntimeChoice& C : Choices)
    {
        if (C.bEnabled)
        {
            bHasEnabledChoice = true;
            break;
        }
    }

    return !bHasEnabledChoice && !CurrentScene->DefaultNextId.TrimStartAndEnd().IsEmpty();
}

TArray<FDialogueRuntimeChoice> FDialogueRunner::GetChoices() const
{
    TArray<FDialogueRuntimeChoice> Result;
    if (!CurrentScene.IsValid()) return Result;

    TMap<FString, int32> Numbers = GetNumbers();
    TMap<FString, FString> Texts = GetTexts();

    for (const FDialogueChoice& Choice : CurrentScene->Choices)
    {
        FString Reason;
        bool bMet = FDialogueRules::RequirementsMet(Choice, Numbers, Reason) || Choice.Visibility == FDialogueChoice::Always;
        if (!bMet && Choice.Visibility == FDialogueChoice::Hide)
        {
            continue;
        }

        FDialogueRuntimeChoice RuntimeChoice;
        RuntimeChoice.Id = Choice.InternalId;
        RuntimeChoice.Text = FDialogueRules::ResolveText(Choice.ButtonText, Numbers, Texts);
        RuntimeChoice.bEnabled = bMet;
        RuntimeChoice.DisabledReason = bMet ? TEXT("") : Reason;
        Result.Add(RuntimeChoice);
    }
    return Result;
}

void FDialogueRunner::AddMissingVariables()
{
    for (const auto& Pair : Handoff.NumericValues)
    {
        if (!Store->HasNumber(Pair.Key))
        {
            Store->SetNumber(Pair.Key, FDialogueRules::ClampNumber(Pair.Key, Pair.Value));
        }
    }
    for (const auto& Pair : Handoff.TextValues)
    {
        FString Val;
        if (!Store->GetText(Pair.Key, Val))
        {
            Store->SetText(Pair.Key, Pair.Value);
        }
    }
}

TMap<FString, int32> FDialogueRunner::GetNumbers() const
{
    // A game-owned store can't list its keys, so the story's declared variables count as well.
    TSet<FString> Keys(Store->GetKeys());
    for (const auto& Pair : Handoff.NumericValues)
    {
        Keys.Add(Pair.Key);
    }

    TMap<FString, int32> Result;
    for (const FString& Key : Keys)
    {
        if (Store->HasNumber(Key))
        {
            Result.Add(Key, Store->GetNumber(Key));
        }
    }
    return Result;
}

TMap<FString, FString> FDialogueRunner::GetTexts() const
{
    TSet<FString> Keys(Store->GetKeys());
    for (const auto& Pair : Handoff.TextValues)
    {
        Keys.Add(Pair.Key);
    }

    TMap<FString, FString> Result;
    for (const FString& Key : Keys)
    {
        FString Val;
        if (Store->GetText(Key, Val))
        {
            Result.Add(Key, Val);
        }
    }
    return Result;
}

void FDialogueRunner::RebuildPages()
{
    if (!CurrentScene.IsValid()) return;
    FString Resolved = FDialogueRules::ResolveText(CurrentScene->Text, GetNumbers(), GetTexts());
    Pages = FDialogueRules::BuildPages(Resolved, CurrentScene->bSayItAll, RandomState, RandomLine);
    PageIndex = FMath::Min(PageIndex, FMath::Max(0, Pages.Num() - 1));
}

void FDialogueRunner::RefreshPage()
{
    if (!CurrentScene.IsValid()) return;
    FString Prev = GetCurrentPage();
    RebuildPages();
    FString Curr = GetCurrentPage();
    if (Prev != Curr)
    {
        OnLineShown.Broadcast(Curr);
    }
}

void FDialogueRunner::Deliver()
{
    OnLineShown.Broadcast(GetCurrentPage());
    if (HasMorePages()) return;

    TArray<FDialogueRuntimeChoice> Choices = GetChoices();
    if (Choices.Num() > 0)
    {
        OnChoicesShown.Broadcast(Choices);
    }
}

bool FDialogueRunner::Enter(const FString& Id, FString* OutError)
{
    if (IsExit(Id))
    {
        End(Id, TEXT(""));
        return true;
    }

    if (!Handoff.Story.IsValid())
    {
        if (OutError) *OutError = TEXT("No story loaded.");
        return false;
    }

    CurrentScene = Handoff.Story->Find(Id);
    if (!CurrentScene.IsValid())
    {
        if (OutError) *OutError = FString::Printf(TEXT("Scene '%s' does not exist."), *Id);
        return false;
    }

    PageIndex = 0;
    RandomLine = -1;
    RebuildPages();
    Deliver();

    if (CurrentScene->Type == TEXT("Shop"))
    {
        OnShopOpened.Broadcast(CurrentScene->ShopId);
    }
    return true;
}

void FDialogueRunner::End(const FString& SceneId, const FString& ChoiceId)
{
    CurrentScene = nullptr;
    Pages.Empty();
    PageIndex = 0;
    bIsEnded = true;
    OnEnded.Broadcast(SceneId, ChoiceId);
}

bool FDialogueRunner::Move(const FString& Id, const FDialogueChoice* Choice, FString* OutError)
{
    if (!IsExit(Id) && (!Handoff.Story.IsValid() || !Handoff.Story->Find(Id).IsValid()))
    {
        if (OutError) *OutError = FString::Printf(TEXT("Scene '%s' does not exist. No actions were applied."), *Id);
        return false;
    }

    if (Choice)
    {
        ApplyActions(Choice->AfterChoiceActions);
    }
    if (CurrentScene.IsValid())
    {
        ApplyActions(CurrentScene->DefaultAfterActions);
    }

    if (IsExit(Id))
    {
        End(CurrentScene.IsValid() ? CurrentScene->State : TEXT(""), Choice ? Choice->InternalId : TEXT(""));
    }
    else
    {
        Enter(Id, OutError);
    }
    return true;
}

void FDialogueRunner::ApplyActions(const TArray<FDialogueAction>& Actions)
{
    for (const FDialogueAction& Action : Actions)
    {
        if (Action.Type == TEXT("Event") || Action.Type == TEXT("Script"))
        {
            OnActionRaised.Broadcast(Action.Type, Action.TargetId, Action.Parameter, Action.Amount);
        }
        else if (Action.Type == TEXT("Affinity") || Action.Type == TEXT("Inventory"))
        {
            FString Key = FDialogueRules::Key(Action.Type, Action.TargetId);
            int32 Before = Store->GetNumber(Key);
            int32 After = FDialogueRules::ApplyAmount(Action.Type, Before, Action.Amount);
            Store->SetNumber(Key, After);
            if (After != Before)
            {
                OnVariableChanged.Broadcast(Key, Before, After);
            }
        }
    }
}

void FDialogueRunner::FTrackedVariables::SetNumber(const FString& Key, int32 Value)
{
    int32 Clamped = FDialogueRules::ClampNumber(Key, Value);
    int32 Before = Runner->Store->GetNumber(Key);
    if (Clamped == Before && Runner->Store->HasNumber(Key)) return;
    Runner->Store->SetNumber(Key, Clamped);
    Runner->OnVariableChanged.Broadcast(Key, Before, Clamped);
    Runner->RefreshPage();
}

void FDialogueRunner::FTrackedVariables::SetText(const FString& Key, const FString& Value)
{
    FString Before;
    bool bHadBefore = Runner->Store->GetText(Key, Before);
    if (bHadBefore && Before == Value) return;
    Runner->Store->SetText(Key, Value);
    Runner->RefreshPage();
}
