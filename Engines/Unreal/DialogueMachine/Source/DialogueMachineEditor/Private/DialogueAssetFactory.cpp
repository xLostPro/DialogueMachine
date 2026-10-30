#include "DialogueAssetFactory.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "EditorFramework/AssetImportData.h"
#include "DialogueMachineStory.h"

UDialogueAssetFactory::UDialogueAssetFactory()
{
    bCreateNew = false;
    bEditAfterNew = true;
    bEditorImport = true;
    SupportedClass = UDialogueAsset::StaticClass();
    Formats.Add(TEXT("runtime.json;Dialogue Machine Runtime Companion"));
}

bool UDialogueAssetFactory::FactoryCanImport(const FString& Filename)
{
    return Filename.EndsWith(TEXT(".runtime.json"));
}

UObject* UDialogueAssetFactory::FactoryCreateFile(UClass* InClass, UObject* InParent, FName InName, EObjectFlags Flags, const FString& Filename, const TCHAR* Parms, FFeedbackContext* Warn, bool& bOutOperationCanceled)
{
    FString JsonString;
    if (!FFileHelper::LoadFileToString(JsonString, *Filename))
    {
        if (Warn)
        {
            Warn->Logf(ELogVerbosity::Error, TEXT("Failed to read file: %s"), *Filename);
        }
        return nullptr;
    }

    FDialogueHandoff Handoff;
    FString Error;
    if (!FDialogueHandoff::Parse(JsonString, Handoff, Error))
    {
        if (Warn)
        {
            Warn->Logf(ELogVerbosity::Error, TEXT("Failed to parse runtime companion '%s': %s"), *Filename, *Error);
        }
        return nullptr;
    }

    UDialogueAsset* Asset = Cast<UDialogueAsset>(StaticFindObject(UDialogueAsset::StaticClass(), InParent, *InName.ToString()));
    if (!Asset)
    {
        Asset = NewObject<UDialogueAsset>(InParent, InClass, InName, Flags);
    }

    FString ProjectName = FPaths::GetBaseFilename(Filename);
    if (ProjectName.EndsWith(TEXT(".runtime")))
    {
        ProjectName.LeftChopInline(8);
    }

    Asset->SetData(JsonString, Handoff.StorySha256, ProjectName);

    Asset->Modify();
    Asset->MarkPackageDirty();

    return Asset;
}

bool UDialogueAssetFactory::CanReimport(UObject* Obj, TArray<FString>& OutFilenames)
{
    UDialogueAsset* Asset = Cast<UDialogueAsset>(Obj);
    if (Asset)
    {
        // Allow reimport if asset exists
        return true;
    }
    return false;
}

void UDialogueAssetFactory::SetReimportPaths(UObject* Obj, const TArray<FString>& NewReimportPaths)
{
}

EReimportResult::Type UDialogueAssetFactory::Reimport(UObject* Obj)
{
    UDialogueAsset* Asset = Cast<UDialogueAsset>(Obj);
    if (!Asset)
    {
        return EReimportResult::Failed;
    }

    return EReimportResult::Succeeded;
}

int32 UDialogueAssetFactory::GetPriority() const
{
    return ImportPriority;
}
