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
    FString Token;

    /** A "Play in engine" request waiting for a running play session. */
    struct FPendingPlay
    {
        FString SceneId;
        int32 Seed = 0;
        TMap<FString, int32> Numeric;
        TMap<FString, FString> Text;
    };
    TSharedPtr<FPendingPlay> PendingPlay;
    FDelegateHandle PlayStartedHandle;

    void OnConnected();
    void OnConnectionError(const FString& Error);
    void OnClosed(int32 StatusCode, const FString& Reason, bool bWasClean);
    void OnMessage(const FString& Message);

    void HandleStoryUpdated(const TSharedPtr<class FJsonObject>& Payload);
    void HandlePlayFrom(const TSharedPtr<class FJsonObject>& Payload);
    void OnPlayStarted(bool bSimulating);
    void ApplyPendingPlay();
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
