#pragma once

#include "CoreMinimal.h"
#include "Components/ActorComponent.h"
#include "DialogueAsset.h"
#include "DialogueMachineRunner.h"
#include "DialogueVariablesInterface.h"
#if !UE_BUILD_SHIPPING
#include "IWebSocket.h"
#endif
#include "DialogueRunnerComponent.generated.h"

USTRUCT(BlueprintType)
struct DIALOGUEMACHINERUNTIME_API FDialogueComponentChoice
{
    GENERATED_BODY()

    UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "Dialogue Machine")
    FString Id;

    UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "Dialogue Machine")
    FString Text;

    UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "Dialogue Machine")
    bool bEnabled = false;

    UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "Dialogue Machine")
    FString DisabledReason;
};

DECLARE_DYNAMIC_MULTICAST_DELEGATE_TwoParams(FDialogueLineSignature, const FString&, Page, const FString&, Speaker);
DECLARE_DYNAMIC_MULTICAST_DELEGATE_OneParam(FDialogueChoicesSignature, const TArray<FDialogueComponentChoice>&, Choices);
DECLARE_DYNAMIC_MULTICAST_DELEGATE_FourParams(FDialogueActionSignature, const FString&, Type, const FString&, TargetId, const FString&, Parameter, int32, Amount);
DECLARE_DYNAMIC_MULTICAST_DELEGATE_OneParam(FDialogueShopSignature, const FString&, ShopId);
DECLARE_DYNAMIC_MULTICAST_DELEGATE_ThreeParams(FDialogueVariableChangedSignature, const FString&, Key, int32, Before, int32, After);
DECLARE_DYNAMIC_MULTICAST_DELEGATE_TwoParams(FDialogueEndedSignature, const FString&, SceneId, const FString&, ChoiceId);

UCLASS(ClassGroup = (Custom), meta = (BlueprintSpawnableComponent))
class DIALOGUEMACHINERUNTIME_API UDialogueRunnerComponent : public UActorComponent
{
    GENERATED_BODY()

public:
    UDialogueRunnerComponent();
    virtual ~UDialogueRunnerComponent();

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Dialogue Machine")
    UDialogueAsset* Story;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Dialogue Machine")
    bool bPlayOnStart = false;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Dialogue Machine")
    FString StartScene;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Dialogue Machine")
    int32 Seed = 0;

    UPROPERTY(BlueprintAssignable, Category = "Dialogue Machine")
    FDialogueLineSignature OnLine;

    UPROPERTY(BlueprintAssignable, Category = "Dialogue Machine")
    FDialogueChoicesSignature OnChoices;

    UPROPERTY(BlueprintAssignable, Category = "Dialogue Machine")
    FDialogueActionSignature OnAction;

    UPROPERTY(BlueprintAssignable, Category = "Dialogue Machine")
    FDialogueShopSignature OnShop;

    UPROPERTY(BlueprintAssignable, Category = "Dialogue Machine")
    FDialogueVariableChangedSignature OnVariableChanged;

    UPROPERTY(BlueprintAssignable, Category = "Dialogue Machine")
    FDialogueEndedSignature OnEnded;

    UFUNCTION(BlueprintCallable, Category = "Dialogue Machine")
    bool StartDialogue(const FString& SceneId = TEXT(""), int32 InSeed = 0);

    UFUNCTION(BlueprintCallable, Category = "Dialogue Machine")
    bool ContinueDialogue();

    UFUNCTION(BlueprintCallable, Category = "Dialogue Machine")
    bool ChooseChoice(const FString& ChoiceId);

    UFUNCTION(BlueprintCallable, Category = "Dialogue Machine")
    bool ChooseChoiceByGuid(const FGuid& ChoiceId);

    UFUNCTION(BlueprintCallable, Category = "Dialogue Machine")
    TArray<FDialogueComponentChoice> GetChoices() const;

    UFUNCTION(BlueprintCallable, Category = "Dialogue Machine")
    FString SaveState() const;

    UFUNCTION(BlueprintCallable, Category = "Dialogue Machine")
    bool RestoreState(const FString& Json);

    UFUNCTION(BlueprintCallable, Category = "Dialogue Machine")
    FString GetCurrentSceneId() const;

    UFUNCTION(BlueprintCallable, Category = "Dialogue Machine")
    FString GetSpeaker() const;

    UFUNCTION(BlueprintCallable, Category = "Dialogue Machine")
    FString GetTitle() const;

    UFUNCTION(BlueprintCallable, Category = "Dialogue Machine")
    bool IsEnded() const;

    UFUNCTION(BlueprintCallable, Category = "Dialogue Machine")
    FString GetCurrentPage() const;

    UFUNCTION(BlueprintCallable, Category = "Dialogue Machine")
    FString GetCurrentPagePlain() const;

    UFUNCTION(BlueprintCallable, Category = "Dialogue Machine")
    int32 GetPageIndex() const;

    UFUNCTION(BlueprintCallable, Category = "Dialogue Machine")
    int32 GetPageCount() const;

    UFUNCTION(BlueprintCallable, Category = "Dialogue Machine")
    bool CanContinue() const;

    UFUNCTION(BlueprintCallable, Category = "Dialogue Machine")
    TMap<FString, FString> GetCurrentMetadata() const;

    TSharedPtr<FDialogueRunner> GetInternalRunner() const { return InternalRunner; }

protected:
    virtual void BeginPlay() override;
    virtual void EndPlay(const EEndPlayReason::Type EndPlayReason) override;

private:
    TSharedPtr<FDialogueRunner> InternalRunner;
    void BindRunnerEvents();

#if !UE_BUILD_SHIPPING
    TSharedPtr<IWebSocket> LiveLinkSocket;
    bool bLiveLinkConnected = false;
    double LastConnectAttemptTime = 0.0;
    FString ActiveRunnerId;

    void EnsureLiveLink();
    void SendLiveState(const FString& Status, const FString& ChoiceId = TEXT(""));
    void SendLiveEvent(const FString& Type, const FString& TargetId, const FString& Parameter);
#endif
};
