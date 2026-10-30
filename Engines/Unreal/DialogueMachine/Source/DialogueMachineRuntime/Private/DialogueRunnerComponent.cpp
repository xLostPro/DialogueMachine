#include "DialogueRunnerComponent.h"
#include "GameFramework/Actor.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"
#if !UE_BUILD_SHIPPING
#include "WebSocketsModule.h"
#endif

class FInterfaceVariablesAdapter : public IDialogueVariables
{
public:
    TScriptInterface<IDialogueVariablesInterface> Target;
    FDictionaryVariables Fallback;

    FInterfaceVariablesAdapter(UObject* InObj)
    {
        Target.SetObject(InObj);
        Target.SetInterface(Cast<IDialogueVariablesInterface>(InObj));
    }

    virtual bool HasNumber(const FString& Key) const override
    {
        if (Target.GetInterface()) return IDialogueVariablesInterface::Execute_HasNumber(Target.GetObject(), Key);
        return Fallback.HasNumber(Key);
    }

    virtual int32 GetNumber(const FString& Key) const override
    {
        if (Target.GetInterface()) return IDialogueVariablesInterface::Execute_GetNumber(Target.GetObject(), Key);
        return Fallback.GetNumber(Key);
    }

    virtual void SetNumber(const FString& Key, int32 Value) override
    {
        if (Target.GetInterface()) IDialogueVariablesInterface::Execute_SetNumber(Target.GetObject(), Key, Value);
        else Fallback.SetNumber(Key, Value);
    }

    virtual bool GetText(const FString& Key, FString& OutValue) const override
    {
        if (Target.GetInterface()) return IDialogueVariablesInterface::Execute_GetText(Target.GetObject(), Key, OutValue);
        return Fallback.GetText(Key, OutValue);
    }

    virtual void SetText(const FString& Key, const FString& Value) override
    {
        if (Target.GetInterface()) IDialogueVariablesInterface::Execute_SetText(Target.GetObject(), Key, Value);
        else Fallback.SetText(Key, Value);
    }

    virtual TArray<FString> GetKeys() const override
    {
        return Fallback.GetKeys();
    }
};

UDialogueRunnerComponent::UDialogueRunnerComponent()
{
    PrimaryComponentTick.bCanEverTick = false;
}

UDialogueRunnerComponent::~UDialogueRunnerComponent()
{
#if !UE_BUILD_SHIPPING
    if (LiveLinkSocket.IsValid() && LiveLinkSocket->IsConnected())
    {
        LiveLinkSocket->Close();
    }
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
    if (LiveLinkSocket.IsValid() && LiveLinkSocket->IsConnected())
    {
        LiveLinkSocket->Close();
    }
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

    int32 ActualSeed = InSeed != 0 ? InSeed : Seed;
    FString StartId = SceneId.IsEmpty() ? StartScene : SceneId;
    bool bSuccess = InternalRunner->Start(StartId, ActualSeed, nullptr, nullptr, &Error);

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
FString UDialogueRunnerComponent::GetTitle() const { return InternalRunner.IsValid() ? InternalRunner->GetTitle() : TEXT(""); }
bool UDialogueRunnerComponent::IsEnded() const { return InternalRunner.IsValid() ? InternalRunner->IsEnded() : true; }
FString UDialogueRunnerComponent::GetCurrentPage() const { return InternalRunner.IsValid() ? InternalRunner->GetCurrentPage() : TEXT(""); }
FString UDialogueRunnerComponent::GetCurrentPagePlain() const { return InternalRunner.IsValid() ? InternalRunner->GetCurrentPagePlain() : TEXT(""); }
int32 UDialogueRunnerComponent::GetPageIndex() const { return InternalRunner.IsValid() ? InternalRunner->GetPageIndex() : 0; }
int32 UDialogueRunnerComponent::GetPageCount() const { return InternalRunner.IsValid() ? InternalRunner->GetPageCount() : 0; }
bool UDialogueRunnerComponent::CanContinue() const { return InternalRunner.IsValid() ? InternalRunner->CanContinue() : false; }
TMap<FString, FString> UDialogueRunnerComponent::GetCurrentMetadata() const { return InternalRunner.IsValid() ? InternalRunner->GetCurrentMetadata() : TMap<FString, FString>(); }

#if !UE_BUILD_SHIPPING
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

    FString DiscoveryPath;
#if PLATFORM_WINDOWS
    DiscoveryPath = FPlatformMisc::GetEnvironmentVariable(TEXT("LOCALAPPDATA")) / TEXT("DialogueMachine/livelink.json");
#else
    DiscoveryPath = FPaths::Combine(FPlatformProcess::UserHomeDir(), TEXT(".local/share/DialogueMachine/livelink.json"));
#endif

    if (!FPaths::FileExists(DiscoveryPath))
    {
        return;
    }

    FString JsonContent;
    if (!FFileHelper::LoadFileToString(JsonContent, *DiscoveryPath))
    {
        return;
    }

    TSharedPtr<FJsonObject> Root;
    TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(JsonContent);
    if (!FJsonSerializer::Deserialize(Reader, Root) || !Root.IsValid())
    {
        return;
    }

    int32 Port = 0;
    FString Token;
    if (!Root->TryGetNumberField(TEXT("port"), Port) || !Root->TryGetStringField(TEXT("token"), Token) || Port <= 0 || Token.IsEmpty())
    {
        return;
    }

    FString Url = FString::Printf(TEXT("ws://127.0.0.1:%d/dialogue-machine/"), Port);
    LiveLinkSocket = FWebSocketsModule::Get().CreateWebSocket(Url, TEXT(""));
    ActiveRunnerId = FString::FromInt(GetUniqueID());

    LiveLinkSocket->OnConnected().AddLambda([this, Token]()
    {
        bLiveLinkConnected = true;
        TSharedPtr<FJsonObject> Hello = MakeShared<FJsonObject>();
        Hello->SetNumberField(TEXT("protocol"), 1);
        Hello->SetStringField(TEXT("type"), TEXT("hello"));

        TSharedPtr<FJsonObject> Payload = MakeShared<FJsonObject>();
        Payload->SetStringField(TEXT("token"), Token);
        Payload->SetStringField(TEXT("engine"), TEXT("unreal"));
        Payload->SetStringField(TEXT("engineVersion"), TEXT("5.3"));
        Payload->SetStringField(TEXT("pluginVersion"), TEXT("1.3.0"));
        Payload->SetNumberField(TEXT("protocol"), 1);
        Payload->SetNumberField(TEXT("schemaVersion"), 1);
        Hello->SetObjectField(TEXT("payload"), Payload);

        FString OutStr;
        TSharedRef<TJsonWriter<>> Writer = TJsonWriterFactory<>::Create(&OutStr);
        FJsonSerializer::Serialize(Hello.ToSharedRef(), Writer);
        LiveLinkSocket->Send(OutStr);
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
        TSharedRef<TJsonReader<>> MsgReader = TJsonReaderFactory<>::Create(Message);
        if (!FJsonSerializer::Deserialize(MsgReader, MsgRoot) || !MsgRoot.IsValid()) return;

        FString Type;
        if (MsgRoot->TryGetStringField(TEXT("type"), Type) && Type == TEXT("runtime.setVariable"))
        {
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
    FString RunnerName = Owner ? Owner->GetName() : GetName();
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
