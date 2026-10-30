#include "DialogueLiveLink.h"
#include "WebSocketsModule.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "HAL/PlatformProcess.h"
#include "Editor.h"
#include "Engine/World.h"
#include "EngineUtils.h"
#include "AssetRegistry/AssetRegistryModule.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"
#include "Widgets/Layout/SBorder.h"
#include "Widgets/Layout/SBox.h"
#include "Widgets/Text/STextBlock.h"
#include "Widgets/Input/SButton.h"
#include "DialogueAsset.h"
#include "DialogueRunnerComponent.h"

FDialogueLiveLink::FDialogueLiveLink()
{
}

FDialogueLiveLink::~FDialogueLiveLink()
{
    Disconnect();
}

void FDialogueLiveLink::Connect()
{
    if (bIsConnected) return;

    FString DiscoveryPath;
#if PLATFORM_WINDOWS
    DiscoveryPath = FPlatformMisc::GetEnvironmentVariable(TEXT("LOCALAPPDATA")) / TEXT("DialogueMachine/livelink.json");
#else
    DiscoveryPath = FPaths::Combine(FPlatformProcess::UserHomeDir(), TEXT(".local/share/DialogueMachine/livelink.json"));
#endif

    if (!FPaths::FileExists(DiscoveryPath))
    {
        StatusMessage = TEXT("Dialogue Machine is not running (discovery file not found).");
        return;
    }

    FString JsonContent;
    if (!FFileHelper::LoadFileToString(JsonContent, *DiscoveryPath))
    {
        StatusMessage = TEXT("Failed to read discovery file.");
        return;
    }

    TSharedPtr<FJsonObject> Root;
    TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(JsonContent);
    if (!FJsonSerializer::Deserialize(Reader, Root) || !Root.IsValid())
    {
        StatusMessage = TEXT("Invalid discovery file JSON.");
        return;
    }

    int32 Port = 0;
    FString Token;
    if (!Root->TryGetNumberField(TEXT("port"), Port) || !Root->TryGetStringField(TEXT("token"), Token) || Port <= 0 || Token.IsEmpty())
    {
        StatusMessage = TEXT("Malformed discovery file.");
        return;
    }

    FString Url = FString::Printf(TEXT("ws://127.0.0.1:%d/dialogue-machine/"), Port);
    Socket = FWebSocketsModule::Get().CreateWebSocket(Url, TEXT(""));

    Socket->OnConnected().AddRaw(this, &FDialogueLiveLink::OnConnected);
    Socket->OnConnectionError().AddRaw(this, &FDialogueLiveLink::OnConnectionError);
    Socket->OnClosed().AddRaw(this, &FDialogueLiveLink::OnClosed);
    Socket->OnMessage().AddRaw(this, &FDialogueLiveLink::OnMessage);

    StatusMessage = FString::Printf(TEXT("Connecting to 127.0.0.1:%d..."), Port);
    Socket->Connect();
}

void FDialogueLiveLink::Disconnect()
{
    if (Socket.IsValid())
    {
        if (Socket->IsConnected())
        {
            Socket->Close();
        }
        Socket.Reset();
    }
    bIsConnected = false;
    StatusMessage = TEXT("Disconnected");
}

bool FDialogueLiveLink::IsConnected() const
{
    return bIsConnected && Socket.IsValid() && Socket->IsConnected();
}

FString FDialogueLiveLink::GetStatusText() const
{
    return StatusMessage;
}

bool FDialogueLiveLink::HasUpdateAvailable() const
{
    return !UpdateUrl.IsEmpty();
}

void FDialogueLiveLink::LaunchUpdateUrl() const
{
    if (!UpdateUrl.IsEmpty())
    {
        FPlatformProcess::LaunchURL(*UpdateUrl, nullptr, nullptr);
    }
}

void FDialogueLiveLink::OnConnected()
{
    bIsConnected = true;
    StatusMessage = TEXT("Connected to Dialogue Machine. Handshaking...");

    FString DiscoveryPath;
#if PLATFORM_WINDOWS
    DiscoveryPath = FPlatformMisc::GetEnvironmentVariable(TEXT("LOCALAPPDATA")) / TEXT("DialogueMachine/livelink.json");
#else
    DiscoveryPath = FPaths::Combine(FPlatformProcess::UserHomeDir(), TEXT(".local/share/DialogueMachine/livelink.json"));
#endif

    FString Token;
    FString JsonContent;
    if (FFileHelper::LoadFileToString(JsonContent, *DiscoveryPath))
    {
        TSharedPtr<FJsonObject> Root;
        TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(JsonContent);
        if (FJsonSerializer::Deserialize(Reader, Root) && Root.IsValid())
        {
            Root->TryGetStringField(TEXT("token"), Token);
        }
    }

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
    Socket->Send(OutStr);
}

void FDialogueLiveLink::OnConnectionError(const FString& Error)
{
    bIsConnected = false;
    StatusMessage = FString::Printf(TEXT("Connection error: %s"), *Error);
}

void FDialogueLiveLink::OnClosed(int32 StatusCode, const FString& Reason, bool bWasClean)
{
    bIsConnected = false;
    StatusMessage = TEXT("Disconnected");
}

void FDialogueLiveLink::OnMessage(const FString& Message)
{
    TSharedPtr<FJsonObject> Root;
    TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(Message);
    if (!FJsonSerializer::Deserialize(Reader, Root) || !Root.IsValid()) return;

    FString Type;
    if (!Root->TryGetStringField(TEXT("type"), Type)) return;

    const TSharedPtr<FJsonObject>* Payload = nullptr;
    Root->TryGetObjectField(TEXT("payload"), Payload);

    if (Type == TEXT("welcome") && Payload)
    {
        (*Payload)->TryGetStringField(TEXT("appVersion"), AppVersion);
        (*Payload)->TryGetStringField(TEXT("updateUrl"), UpdateUrl);
        StatusMessage = FString::Printf(TEXT("Connected to Dialogue Machine (app version %s)"), *AppVersion);
    }
    else if (Type == TEXT("story.updated") && Payload)
    {
        HandleStoryUpdated(*Payload);
    }
    else if (Type == TEXT("play.from") && Payload)
    {
        HandlePlayFrom(*Payload);
    }
}

void FDialogueLiveLink::HandleStoryUpdated(const TSharedPtr<FJsonObject>& Payload)
{
    FString ProjectName;
    Payload->TryGetStringField(TEXT("ProjectName"), ProjectName);
    FString StorySha;
    Payload->TryGetStringField(TEXT("StorySha256"), StorySha);

    LastProjectName = ProjectName;
    LastStorySha = StorySha;

    FString HandoffJson;
    TSharedRef<TJsonWriter<>> Writer = TJsonWriterFactory<>::Create(&HandoffJson);
    FJsonSerializer::Serialize(Payload.ToSharedRef(), Writer);

    FDialogueHandoff Handoff;
    FString ParseErr;
    if (!FDialogueHandoff::Parse(HandoffJson, Handoff, ParseErr))
    {
        StatusMessage = FString::Printf(TEXT("Failed to parse pushed story: %s"), *ParseErr);
        return;
    }

    StatusMessage = FString::Printf(TEXT("Received update for '%s' (%s)"), *ProjectName, *StorySha.Left(8));

    // Hot reload running dialogues in PIE
    if (GEditor && GEditor->PlayWorld)
    {
        for (TActorIterator<AActor> It(GEditor->PlayWorld); It; ++It)
        {
            TArray<UDialogueRunnerComponent*> Runners;
            (*It)->GetComponents<UDialogueRunnerComponent>(Runners);
            for (UDialogueRunnerComponent* Runner : Runners)
            {
                if (Runner && Runner->GetInternalRunner().IsValid())
                {
                    Runner->GetInternalRunner()->ReplaceStory(Handoff);
                }
            }
        }
    }
}

void FDialogueLiveLink::HandlePlayFrom(const TSharedPtr<FJsonObject>& Payload)
{
    FString SceneId;
    Payload->TryGetStringField(TEXT("sceneId"), SceneId);
    int32 Seed = 0;
    Payload->TryGetNumberField(TEXT("seed"), Seed);

    if (GEditor && GEditor->PlayWorld)
    {
        for (TActorIterator<AActor> It(GEditor->PlayWorld); It; ++It)
        {
            TArray<UDialogueRunnerComponent*> Runners;
            (*It)->GetComponents<UDialogueRunnerComponent>(Runners);
            for (UDialogueRunnerComponent* Runner : Runners)
            {
                if (Runner)
                {
                    Runner->StartDialogue(SceneId, Seed);
                    break;
                }
            }
        }
    }
}

TSharedRef<SWidget> FDialogueLiveLink::CreatePanel()
{
    return SNew(SDialogueLiveLinkPanel, AsShared());
}

void SDialogueLiveLinkPanel::Construct(const FArguments& InArgs, TSharedPtr<FDialogueLiveLink> InLiveLink)
{
    LiveLink = InLiveLink;

    ChildSlot
    [
        SNew(SBorder)
        .Padding(16.0f)
        [
            SNew(SVerticalBox)
            + SVerticalBox::Slot()
            .AutoHeight()
            .Padding(0, 0, 0, 12)
            [
                SNew(STextBlock)
                .Text_Raw(this, &SDialogueLiveLinkPanel::GetStatusText)
            ]
            + SVerticalBox::Slot()
            .AutoHeight()
            .Padding(0, 0, 0, 8)
            [
                SNew(SHorizontalBox)
                + SHorizontalBox::Slot()
                .AutoWidth()
                .Padding(0, 0, 8, 0)
                [
                    SNew(SButton)
                    .Text(FText::FromString(TEXT("Connect")))
                    .OnClicked(this, &SDialogueLiveLinkPanel::OnConnectClicked)
                ]
                + SHorizontalBox::Slot()
                .AutoWidth()
                .Padding(0, 0, 8, 0)
                [
                    SNew(SButton)
                    .Text(FText::FromString(TEXT("Disconnect")))
                    .OnClicked(this, &SDialogueLiveLinkPanel::OnDisconnectClicked)
                ]
                + SHorizontalBox::Slot()
                .AutoWidth()
                [
                    SNew(SButton)
                    .Text(FText::FromString(TEXT("Update Plugin")))
                    .Visibility_Raw(this, &SDialogueLiveLinkPanel::GetUpdateVisibility)
                    .OnClicked(this, &SDialogueLiveLinkPanel::OnUpdateClicked)
                ]
            ]
        ]
    ];
}

FText SDialogueLiveLinkPanel::GetStatusText() const
{
    return LiveLink.IsValid() ? FText::FromString(LiveLink->GetStatusText()) : FText::GetEmpty();
}

EVisibility SDialogueLiveLinkPanel::GetUpdateVisibility() const
{
    return (LiveLink.IsValid() && LiveLink->HasUpdateAvailable()) ? EVisibility::Visible : EVisibility::Collapsed;
}

FReply SDialogueLiveLinkPanel::OnConnectClicked()
{
    if (LiveLink.IsValid()) LiveLink->Connect();
    return FReply::Handled();
}

FReply SDialogueLiveLinkPanel::OnDisconnectClicked()
{
    if (LiveLink.IsValid()) LiveLink->Disconnect();
    return FReply::Handled();
}

FReply SDialogueLiveLinkPanel::OnUpdateClicked()
{
    if (LiveLink.IsValid()) LiveLink->LaunchUpdateUrl();
    return FReply::Handled();
}
