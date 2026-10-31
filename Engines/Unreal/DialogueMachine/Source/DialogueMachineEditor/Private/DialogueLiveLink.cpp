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
#include "DialogueLiveLinkProtocol.h"
#include "UObject/UObjectIterator.h"
#include "Editor/EditorEngine.h"
#include "Settings/LevelEditorPlaySettings.h"

// Dialogue Machine names the export <project-slug>.runtime.json: lower-case words joined by single hyphens.
static FString SlugOf(const FString& Name)
{
    FString Slug;
    bool bHyphen = false;
    for (TCHAR Ch : Name.TrimStartAndEnd())
    {
        if (FChar::IsAlnum(Ch))
        {
            if (bHyphen && !Slug.IsEmpty()) Slug += TEXT('-');
            Slug.AppendChar(FChar::ToLower(Ch));
            bHyphen = false;
        }
        else
        {
            bHyphen = true;
        }
    }
    return Slug.IsEmpty() ? TEXT("story") : Slug;
}

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

    int32 Port = 0;
    FString Error;
    if (!FDialogueLiveLinkProtocol::ReadDiscovery(Port, Token, &Error))
    {
        StatusMessage = Error;
        return;
    }

    Disconnect();
    Socket = FWebSocketsModule::Get().CreateWebSocket(FDialogueLiveLinkProtocol::Url(Port), TEXT(""));

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
        // Unbind first: the raw delegates point at this object, which may be about to be destroyed.
        Socket->OnConnected().RemoveAll(this);
        Socket->OnConnectionError().RemoveAll(this);
        Socket->OnClosed().RemoveAll(this);
        Socket->OnMessage().RemoveAll(this);
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
    if (IsConnected() && !AppVersion.IsEmpty())
    {
        return FString::Printf(TEXT("%s  ·  Dialogue Machine %s"), *StatusMessage, *AppVersion);
    }
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
    Socket->Send(FDialogueLiveLinkProtocol::MakeHello(Token));
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

    if (Type == TEXT("ping"))
    {
        // Dialogue Machine closes the connection after 30 s of silence.
        Socket->Send(FDialogueLiveLinkProtocol::MakePong());
    }
    else if (Type == TEXT("error") && Payload)
    {
        FString Reason;
        (*Payload)->TryGetStringField(TEXT("message"), Reason);
        StatusMessage = FString::Printf(TEXT("Dialogue Machine refused the connection: %s"), *Reason);
    }
    else if (Type == TEXT("welcome") && Payload)
    {
        UpdateUrl.Empty();
        (*Payload)->TryGetStringField(TEXT("appVersion"), AppVersion);
        (*Payload)->TryGetStringField(TEXT("updateUrl"), UpdateUrl);
        StatusMessage = TEXT("Connected to Dialogue Machine");
    }
    else if (Type == TEXT("story.invalid") && Payload)
    {
        // Keep the last good story; just tell the writer why the new one wasn't applied.
        const TArray<TSharedPtr<FJsonValue>>* Diagnostics = nullptr;
        (*Payload)->TryGetArrayField(TEXT("diagnostics"), Diagnostics);
        StatusMessage = FString::Printf(TEXT("The story has %d problem(s), so the last good version is still in use."), Diagnostics ? Diagnostics->Num() : 0);
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

    // Builds should match what was played, so refresh the asset imported from this project's export too.
    const FString Slug = SlugOf(ProjectName);
    for (TObjectIterator<UDialogueAsset> It; It; ++It)
    {
        UDialogueAsset* Asset = *It;
        if (!Asset->HasAnyFlags(RF_ClassDefaultObject) && Asset->StorySha256 != StorySha
            && (Asset->ProjectName.Equals(Slug, ESearchCase::IgnoreCase) || Asset->ProjectName.Equals(ProjectName, ESearchCase::IgnoreCase)))
        {
            Asset->SetData(HandoffJson, StorySha, Asset->ProjectName);
            Asset->Modify();
            Asset->MarkPackageDirty();
        }
    }

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
    PendingPlay = MakeShared<FPendingPlay>();
    Payload->TryGetStringField(TEXT("sceneId"), PendingPlay->SceneId);
    Payload->TryGetNumberField(TEXT("seed"), PendingPlay->Seed);

    const TSharedPtr<FJsonObject>* Numeric = nullptr;
    if (Payload->TryGetObjectField(TEXT("numeric"), Numeric) && Numeric && Numeric->IsValid())
    {
        for (const auto& Pair : (*Numeric)->Values)
        {
            double Value = 0.0;
            if (Pair.Value.IsValid() && Pair.Value->TryGetNumber(Value))
            {
                PendingPlay->Numeric.Add(FString(Pair.Key), (int32)Value);
            }
        }
    }
    const TSharedPtr<FJsonObject>* Text = nullptr;
    if (Payload->TryGetObjectField(TEXT("text"), Text) && Text && Text->IsValid())
    {
        for (const auto& Pair : (*Text)->Values)
        {
            FString Value;
            if (Pair.Value.IsValid() && Pair.Value->TryGetString(Value))
            {
                PendingPlay->Text.Add(FString(Pair.Key), Value);
            }
        }
    }

    if (GEditor && GEditor->PlayWorld)
    {
        ApplyPendingPlay();
    }
    else if (GEditor)
    {
        // Start a play session; the dialogue begins once the runners have had their BeginPlay.
        StatusMessage = FString::Printf(TEXT("Starting Play In Editor at '%s'..."), *PendingPlay->SceneId);
        PlayStartedHandle = FEditorDelegates::PostPIEStarted.AddSP(this, &FDialogueLiveLink::OnPlayStarted);
        FRequestPlaySessionParams Params;
        GEditor->RequestPlaySession(Params);
    }
}

void FDialogueLiveLink::OnPlayStarted(bool bSimulating)
{
    FEditorDelegates::PostPIEStarted.Remove(PlayStartedHandle);
    ApplyPendingPlay();
}

void FDialogueLiveLink::ApplyPendingPlay()
{
    if (!PendingPlay.IsValid() || !GEditor || !GEditor->PlayWorld) return;
    TSharedPtr<FPendingPlay> Play = PendingPlay;
    PendingPlay.Reset();

    // One runner answers "Play in engine": the first one found.
    for (TActorIterator<AActor> It(GEditor->PlayWorld); It; ++It)
    {
        if (UDialogueRunnerComponent* Runner = (*It)->FindComponentByClass<UDialogueRunnerComponent>())
        {
            Runner->StartDialogueWithValues(Play->SceneId, Play->Seed, &Play->Numeric, &Play->Text);
            return;
        }
    }
    StatusMessage = TEXT("Play from here: no Dialogue Runner component is in the play world.");
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
