#include "DialogueRunnerComponent.h"
#include "GameFramework/Actor.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"
#include "DialogueLiveLinkProtocol.h"
#if !UE_BUILD_SHIPPING
#include "WebSocketsModule.h"
#endif

class FInterfaceVariablesAdapter : public IDialogueVariables
{
public:
    TWeakObjectPtr<UObject> Target;

    FInterfaceVariablesAdapter(UObject* InObj) : Target(InObj) {}

    // Execute_ dispatches to Blueprint implementers too; a C++ Cast<> to the interface only works for native ones.
    virtual bool HasNumber(const FString& Key) const override
    {
        return Target.IsValid() && IDialogueVariablesInterface::Execute_HasNumber(Target.Get(), Key);
    }

    virtual int32 GetNumber(const FString& Key) const override
    {
        return Target.IsValid() ? IDialogueVariablesInterface::Execute_GetNumber(Target.Get(), Key) : 0;
    }

    virtual void SetNumber(const FString& Key, int32 Value) override
    {
        if (Target.IsValid()) IDialogueVariablesInterface::Execute_SetNumber(Target.Get(), Key, Value);
    }

    virtual bool GetText(const FString& Key, FString& OutValue) const override
    {
        return Target.IsValid() && IDialogueVariablesInterface::Execute_GetText(Target.Get(), Key, OutValue);
    }

    virtual void SetText(const FString& Key, const FString& Value) override
    {
        if (Target.IsValid()) IDialogueVariablesInterface::Execute_SetText(Target.Get(), Key, Value);
    }

    // The game owns the store and can't enumerate it; the runner looks up the story's declared keys instead.
    virtual TArray<FString> GetKeys() const override { return TArray<FString>(); }
};

UDialogueRunnerComponent::UDialogueRunnerComponent()
{
    PrimaryComponentTick.bCanEverTick = false;
}

UDialogueRunnerComponent::~UDialogueRunnerComponent()
{
#if !UE_BUILD_SHIPPING
    CloseLiveLink();
#endif
}

void UDialogueRunnerComponent::BeginPlay()
{
    Super::BeginPlay();

    TSharedPtr<IDialogueVariables> CustomVars = nullptr;
    AActor* Owner = GetOwner();
    if (Owner && Owner->GetClass()->ImplementsInterface(UDialogueVariablesInterface::StaticClass()))
    {
        CustomVars = MakeShared<FInterfaceVariablesAdapter>(Owner);
    }

    InternalRunner = MakeShared<FDialogueRunner>(CustomVars);
    BindRunnerEvents();

    if (bPlayOnStart && Story)
    {
        StartDialogue(StartScene, Seed);
    }
}

void UDialogueRunnerComponent::EndPlay(const EEndPlayReason::Type EndPlayReason)
{
#if !UE_BUILD_SHIPPING
    CloseLiveLink();
#endif
    Super::EndPlay(EndPlayReason);
}

void UDialogueRunnerComponent::BindRunnerEvents()
{
    if (!InternalRunner.IsValid()) return;

    InternalRunner->OnLineShown.AddLambda([this](const FString& Page)
    {
        OnLine.Broadcast(Page, InternalRunner->GetSpeaker());
    });

    InternalRunner->OnChoicesShown.AddLambda([this](const TArray<FDialogueRuntimeChoice>& Choices)
    {
        TArray<FDialogueComponentChoice> CompChoices;
        for (const auto& C : Choices)
        {
            FDialogueComponentChoice CC;
            CC.Id = C.Id;
            CC.Text = C.Text;
            CC.bEnabled = C.bEnabled;
            CC.DisabledReason = C.DisabledReason;
            CompChoices.Add(CC);
        }
        OnChoices.Broadcast(CompChoices);
    });

    InternalRunner->OnActionRaised.AddLambda([this](const FString& Type, const FString& TargetId, const FString& Parameter, int32 Amount)
    {
        OnAction.Broadcast(Type, TargetId, Parameter, Amount);
#if !UE_BUILD_SHIPPING
        SendLiveEvent(Type, TargetId, Parameter);
#endif
    });

    InternalRunner->OnShopOpened.AddLambda([this](const FString& ShopId)
    {
        OnShop.Broadcast(ShopId);
    });

    InternalRunner->OnVariableChanged.AddLambda([this](const FString& Key, int32 Before, int32 After)
    {
        OnVariableChanged.Broadcast(Key, Before, After);
    });

    InternalRunner->OnEnded.AddLambda([this](const FString& SceneId, const FString& ChoiceId)
    {
        OnEnded.Broadcast(SceneId, ChoiceId);
#if !UE_BUILD_SHIPPING
        SendLiveState(TEXT("ended"), ChoiceId);
#endif
    });
}

bool UDialogueRunnerComponent::StartDialogue(const FString& SceneId, int32 InSeed)
{
    return StartDialogueWithValues(SceneId, InSeed, nullptr, nullptr);
}

bool UDialogueRunnerComponent::StartDialogueWithValues(const FString& SceneId, int32 InSeed, const TMap<FString, int32>* Numeric, const TMap<FString, FString>* Text)
{
    if (!Story)
    {
        return false;
    }

    if (!InternalRunner.IsValid())
    {
        InternalRunner = MakeShared<FDialogueRunner>();
        BindRunnerEvents();
    }

    FString Error;
    if (!InternalRunner->Load(Story->HandoffJson, Error))
    {
        return false;
    }

#if !UE_BUILD_SHIPPING
    EnsureLiveLink();
#endif

    // Zero means "use the seed saved in the story", like the other engines.
    const int32 ChosenSeed = InSeed != 0 ? InSeed : Seed;
    TOptional<int32> ActualSeed;
    if (ChosenSeed != 0) ActualSeed = ChosenSeed;
    FString StartId = SceneId.IsEmpty() ? StartScene : SceneId;
    bool bSuccess = InternalRunner->Start(StartId, ActualSeed, Numeric, Text, &Error);

#if !UE_BUILD_SHIPPING
    if (bSuccess)
    {
        SendLiveState(TEXT("started"));
    }
#endif

    return bSuccess;
}

bool UDialogueRunnerComponent::ContinueDialogue()
{
    if (!InternalRunner.IsValid()) return false;
    bool bOk = InternalRunner->Continue();
#if !UE_BUILD_SHIPPING
    if (bOk && !InternalRunner->IsEnded())
    {
        SendLiveState(TEXT("step"));
    }
#endif
    return bOk;
}

bool UDialogueRunnerComponent::ChooseChoice(const FString& ChoiceId)
{
    if (!InternalRunner.IsValid()) return false;
    bool bOk = InternalRunner->Choose(ChoiceId);
#if !UE_BUILD_SHIPPING
    if (bOk && !InternalRunner->IsEnded())
    {
        SendLiveState(TEXT("step"), ChoiceId);
    }
#endif
    return bOk;
}

bool UDialogueRunnerComponent::ChooseChoiceByGuid(const FGuid& ChoiceId)
{
    return ChooseChoice(ChoiceId.ToString(EGuidFormats::DigitsWithHyphensLower));
}

TArray<FDialogueComponentChoice> UDialogueRunnerComponent::GetChoices() const
{
    TArray<FDialogueComponentChoice> Result;
    if (!InternalRunner.IsValid()) return Result;
    for (const auto& C : InternalRunner->GetChoices())
    {
        FDialogueComponentChoice CC;
        CC.Id = C.Id;
        CC.Text = C.Text;
        CC.bEnabled = C.bEnabled;
        CC.DisabledReason = C.DisabledReason;
        Result.Add(CC);
    }
    return Result;
}

FString UDialogueRunnerComponent::SaveState() const
{
    return InternalRunner.IsValid() ? InternalRunner->SaveState() : TEXT("");
}

bool UDialogueRunnerComponent::RestoreState(const FString& Json)
{
    if (!InternalRunner.IsValid() && Story)
    {
        InternalRunner = MakeShared<FDialogueRunner>();
        BindRunnerEvents();
        FString Err;
        InternalRunner->Load(Story->HandoffJson, Err);
    }
    if (!InternalRunner.IsValid()) return false;
    FString Err;
    return InternalRunner->RestoreState(Json, Err);
}

FString UDialogueRunnerComponent::GetCurrentSceneId() const { return InternalRunner.IsValid() ? InternalRunner->GetCurrentSceneId() : TEXT(""); }
FString UDialogueRunnerComponent::GetSpeaker() const { return InternalRunner.IsValid() ? InternalRunner->GetSpeaker() : TEXT(""); }
FString UDialogueRunnerComponent::GetPortrait() const { return InternalRunner.IsValid() ? InternalRunner->GetPortrait() : TEXT(""); }
FString UDialogueRunnerComponent::GetTitle() const { return InternalRunner.IsValid() ? InternalRunner->GetTitle() : TEXT(""); }
bool UDialogueRunnerComponent::IsEnded() const { return InternalRunner.IsValid() ? InternalRunner->IsEnded() : true; }
FString UDialogueRunnerComponent::GetCurrentPage() const { return InternalRunner.IsValid() ? InternalRunner->GetCurrentPage() : TEXT(""); }
FString UDialogueRunnerComponent::GetCurrentPagePlain() const { return InternalRunner.IsValid() ? InternalRunner->GetCurrentPagePlain() : TEXT(""); }
int32 UDialogueRunnerComponent::GetPageIndex() const { return InternalRunner.IsValid() ? InternalRunner->GetPageIndex() : 0; }
int32 UDialogueRunnerComponent::GetPageCount() const { return InternalRunner.IsValid() ? InternalRunner->GetPageCount() : 0; }
bool UDialogueRunnerComponent::CanContinue() const { return InternalRunner.IsValid() ? InternalRunner->CanContinue() : false; }
TMap<FString, FString> UDialogueRunnerComponent::GetCurrentMetadata() const { return InternalRunner.IsValid() ? InternalRunner->GetCurrentMetadata() : TMap<FString, FString>(); }

#if !UE_BUILD_SHIPPING
void UDialogueRunnerComponent::CloseLiveLink()
{
    if (LiveLinkSocket.IsValid())
    {
        // The lambdas below capture this component, so unbind them before it can go away.
        LiveLinkSocket->OnConnected().Clear();
        LiveLinkSocket->OnClosed().Clear();
        LiveLinkSocket->OnConnectionError().Clear();
        LiveLinkSocket->OnMessage().Clear();
        if (LiveLinkSocket->IsConnected())
        {
            LiveLinkSocket->Close();
        }
        LiveLinkSocket.Reset();
    }
    bLiveLinkConnected = false;
}

void UDialogueRunnerComponent::EnsureLiveLink()
{
    if (bLiveLinkConnected && LiveLinkSocket.IsValid() && LiveLinkSocket->IsConnected())
    {
        return;
    }

    double Now = FPlatformTime::Seconds();
    if (Now - LastConnectAttemptTime < 10.0)
    {
        return;
    }
    LastConnectAttemptTime = Now;

    int32 Port = 0;
    FString Token;
    if (!FDialogueLiveLinkProtocol::ReadDiscovery(Port, Token))
    {
        return;
    }

    CloseLiveLink();
    LiveLinkSocket = FWebSocketsModule::Get().CreateWebSocket(FDialogueLiveLinkProtocol::Url(Port), TEXT(""));
    ActiveRunnerId = FString::FromInt(GetUniqueID());

    LiveLinkSocket->OnConnected().AddLambda([this, Token]()
    {
        bLiveLinkConnected = true;
        LiveLinkSocket->Send(FDialogueLiveLinkProtocol::MakeHello(Token));

        // The first "started" was sent before the socket opened, so announce the runner again.
        if (InternalRunner.IsValid() && !InternalRunner->IsEnded() && !InternalRunner->GetCurrentSceneId().IsEmpty())
        {
            SendLiveState(TEXT("started"));
        }
    });

    LiveLinkSocket->OnClosed().AddLambda([this](int32 StatusCode, const FString& Reason, bool bWasClean)
    {
        bLiveLinkConnected = false;
    });

    LiveLinkSocket->OnConnectionError().AddLambda([this](const FString& Error)
    {
        bLiveLinkConnected = false;
    });

    LiveLinkSocket->OnMessage().AddLambda([this](const FString& Message)
    {
        TSharedPtr<FJsonObject> MsgRoot;
        if (!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Message), MsgRoot) || !MsgRoot.IsValid()) return;

        FString Type;
        if (!MsgRoot->TryGetStringField(TEXT("type"), Type)) return;

        if (Type == TEXT("ping"))
        {
            LiveLinkSocket->Send(FDialogueLiveLinkProtocol::MakePong());
            return;
        }

        if (Type != TEXT("runtime.setVariable")) return;

        const TSharedPtr<FJsonObject>* Payload = nullptr;
        if (MsgRoot->TryGetObjectField(TEXT("payload"), Payload) && Payload && Payload->IsValid())
        {
            FString RunnerId;
            (*Payload)->TryGetStringField(TEXT("runnerId"), RunnerId);
            if (RunnerId == ActiveRunnerId && InternalRunner.IsValid())
            {
                FString Key;
                (*Payload)->TryGetStringField(TEXT("key"), Key);
                double NumVal = 0.0;
                if ((*Payload)->TryGetNumberField(TEXT("number"), NumVal))
                {
                    InternalRunner->GetVariables()->SetNumber(Key, (int32)NumVal);
                }
                FString TextVal;
                if ((*Payload)->TryGetStringField(TEXT("text"), TextVal))
                {
                    InternalRunner->GetVariables()->SetText(Key, TextVal);
                }
            }
        }
    });

    LiveLinkSocket->Connect();
}

void UDialogueRunnerComponent::SendLiveState(const FString& Status, const FString& ChoiceId)
{
    if (!bLiveLinkConnected || !LiveLinkSocket.IsValid() || !LiveLinkSocket->IsConnected() || !InternalRunner.IsValid())
    {
        return;
    }

    TSharedPtr<FJsonObject> Msg = MakeShared<FJsonObject>();
    Msg->SetNumberField(TEXT("protocol"), 1);
    Msg->SetStringField(TEXT("type"), TEXT("runtime.state"));

    TSharedPtr<FJsonObject> Payload = MakeShared<FJsonObject>();
    Payload->SetStringField(TEXT("runnerId"), ActiveRunnerId);

    AActor* Owner = GetOwner();
#if WITH_EDITOR
    FString RunnerName = Owner ? Owner->GetActorLabel() : GetName();
#else
    FString RunnerName = Owner ? Owner->GetName() : GetName();
#endif
    Payload->SetStringField(TEXT("runnerName"), RunnerName);

    if (Story)
    {
        Payload->SetStringField(TEXT("projectName"), Story->ProjectName);
        Payload->SetStringField(TEXT("storySha256"), Story->StorySha256);
    }

    Payload->SetStringField(TEXT("status"), Status);
    Payload->SetStringField(TEXT("sceneId"), InternalRunner->GetCurrentSceneId());
    Payload->SetNumberField(TEXT("pageIndex"), InternalRunner->GetPageIndex());
    if (!ChoiceId.IsEmpty())
    {
        Payload->SetStringField(TEXT("choiceId"), ChoiceId);
    }

    TSharedPtr<FJsonObject> NumObj = MakeShared<FJsonObject>();
    TSharedPtr<FJsonObject> TextObj = MakeShared<FJsonObject>();
    for (const FString& Key : InternalRunner->GetVariables()->GetKeys())
    {
        if (InternalRunner->GetVariables()->HasNumber(Key))
        {
            NumObj->SetNumberField(Key, InternalRunner->GetVariables()->GetNumber(Key));
        }
        FString TextVal;
        if (InternalRunner->GetVariables()->GetText(Key, TextVal))
        {
            TextObj->SetStringField(Key, TextVal);
        }
    }
    Payload->SetObjectField(TEXT("numeric"), NumObj);
    Payload->SetObjectField(TEXT("text"), TextObj);

    Msg->SetObjectField(TEXT("payload"), Payload);

    FString OutStr;
    TSharedRef<TJsonWriter<>> Writer = TJsonWriterFactory<>::Create(&OutStr);
    FJsonSerializer::Serialize(Msg.ToSharedRef(), Writer);
    LiveLinkSocket->Send(OutStr);
}

void UDialogueRunnerComponent::SendLiveEvent(const FString& Type, const FString& TargetId, const FString& Parameter)
{
    if (!bLiveLinkConnected || !LiveLinkSocket.IsValid() || !LiveLinkSocket->IsConnected())
    {
        return;
    }

    TSharedPtr<FJsonObject> Msg = MakeShared<FJsonObject>();
    Msg->SetNumberField(TEXT("protocol"), 1);
    Msg->SetStringField(TEXT("type"), TEXT("runtime.event"));

    TSharedPtr<FJsonObject> Payload = MakeShared<FJsonObject>();
    Payload->SetStringField(TEXT("runnerId"), ActiveRunnerId);
    Payload->SetStringField(TEXT("type"), Type);
    Payload->SetStringField(TEXT("targetId"), TargetId);
    Payload->SetStringField(TEXT("parameter"), Parameter);

    Msg->SetObjectField(TEXT("payload"), Payload);

    FString OutStr;
    TSharedRef<TJsonWriter<>> Writer = TJsonWriterFactory<>::Create(&OutStr);
    FJsonSerializer::Serialize(Msg.ToSharedRef(), Writer);
    LiveLinkSocket->Send(OutStr);
}
#endif
