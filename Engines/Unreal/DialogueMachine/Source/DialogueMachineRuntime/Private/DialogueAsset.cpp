#include "DialogueAsset.h"

void UDialogueAsset::SetData(const FString& InJson, const FString& InSha, const FString& InProjectName)
{
    HandoffJson = InJson;
    StorySha256 = InSha;
    ProjectName = InProjectName;
}
