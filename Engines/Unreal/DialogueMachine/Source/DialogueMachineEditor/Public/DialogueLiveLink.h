#pragma once

#include "CoreMinimal.h"
#include "IWebSocket.h"
#include "Widgets/SCompoundWidget.h"
#include "DialogueMachineStory.h"

class DIALOGUEMACHINEEDITOR_API FDialogueLiveLink : public TSharedFromThis<FDialogueLiveLink>
{
public:
    FDialogueLiveLink();
    ~FDialogueLiveLink();

    void Connect();
    void Disconnect();
    bool IsConnected() const;

    FString GetStatusText() const;
    bool HasUpdateAvailable() const;
    void LaunchUpdateUrl() const;

    TSharedRef<SWidget> CreatePanel();

private:
    TSharedPtr<IWebSocket> Socket;
    bool bIsConnected = false;
    FString StatusMessage = TEXT("Disconnected");
    FString AppVersion;
    FString UpdateUrl;
    FString LastProjectName;
    FString LastStorySha;

    void OnConnected();
    void OnConnectionError(const FString& Error);
    void OnClosed(int32 StatusCode, const FString& Reason, bool bWasClean);
    void OnMessage(const FString& Message);

    void HandleStoryUpdated(const TSharedPtr<class FJsonObject>& Payload);
    void HandlePlayFrom(const TSharedPtr<class FJsonObject>& Payload);
};

class SDialogueLiveLinkPanel : public SCompoundWidget
{
public:
    SLATE_BEGIN_ARGS(SDialogueLiveLinkPanel) {}
    SLATE_END_ARGS()

    void Construct(const FArguments& InArgs, TSharedPtr<FDialogueLiveLink> InLiveLink);

private:
    TSharedPtr<FDialogueLiveLink> LiveLink;
    FText GetStatusText() const;
    EVisibility GetUpdateVisibility() const;
    FReply OnConnectClicked();
    FReply OnDisconnectClicked();
    FReply OnUpdateClicked();
};
