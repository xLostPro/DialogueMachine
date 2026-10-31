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
    // Unreal matches only the last extension, so this has to be json. FactoryCanImport leaves other JSON to the data table
    // importer, and the higher priority stops that importer taking our files, which it accepts and then fails on.
    Formats.Add(TEXT("json;Dialogue Machine Runtime Companion"));
    ImportPriority = DefaultImportPriority + 1;
}

bool UDialogueAssetFactory::FactoryCanImport(const FString& Filename)
{
    return Filename.EndsWith(TEXT(".runtime.json"));
}

// Reads a runtime companion into the asset. The project name comes from the file name Dialogue Machine wrote.
static bool ReadCompanion(UDialogueAsset* Asset, const FString& Filename, FString& OutError)
{
    FString JsonString;
    if (!FFileHelper::LoadFileToString(JsonString, *Filename))
    {
        OutError = FString::Printf(TEXT("Failed to read file: %s"), *Filename);
        return false;
    }

    FDialogueHandoff Handoff;
    FString ParseError;
    if (!FDialogueHandoff::Parse(JsonString, Handoff, ParseError))
    {
        OutError = FString::Printf(TEXT("Failed to parse runtime companion '%s': %s"), *Filename, *ParseError);
        return false;
    }

    FString ProjectName = FPaths::GetBaseFilename(Filename);
    if (ProjectName.EndsWith(TEXT(".runtime")))
    {
        ProjectName.LeftChopInline(8);
    }

    Asset->SetData(JsonString, Handoff.StorySha256, ProjectName);
    if (Asset->AssetImportData)
    {
        Asset->AssetImportData->Update(Filename);
    }
    Asset->Modify();
    Asset->MarkPackageDirty();
    return true;
}

UObject* UDialogueAssetFactory::FactoryCreateFile(UClass* InClass, UObject* InParent, FName InName, EObjectFlags Flags, const FString& Filename, const TCHAR* Parms, FFeedbackContext* Warn, bool& bOutOperationCanceled)
{
    UDialogueAsset* Asset = Cast<UDialogueAsset>(StaticFindObject(UDialogueAsset::StaticClass(), InParent, *InName.ToString()));
    if (!Asset)
    {
        Asset = NewObject<UDialogueAsset>(InParent, InClass, InName, Flags);
    }

    FString Error;
    if (!ReadCompanion(Asset, Filename, Error))
    {
        if (Warn)
        {
            Warn->Logf(ELogVerbosity::Error, TEXT("%s"), *Error);
        }
        return nullptr;
    }

    return Asset;
}

bool UDialogueAssetFactory::CanReimport(UObject* Obj, TArray<FString>& OutFilenames)
{
    UDialogueAsset* Asset = Cast<UDialogueAsset>(Obj);
    if (Asset && Asset->AssetImportData)
    {
        Asset->AssetImportData->ExtractFilenames(OutFilenames);
        return true;
    }
    return false;
}

void UDialogueAssetFactory::SetReimportPaths(UObject* Obj, const TArray<FString>& NewReimportPaths)
{
    UDialogueAsset* Asset = Cast<UDialogueAsset>(Obj);
    if (Asset && Asset->AssetImportData && NewReimportPaths.Num() > 0)
    {
        Asset->AssetImportData->UpdateFilenameOnly(NewReimportPaths[0]);
    }
}

EReimportResult::Type UDialogueAssetFactory::Reimport(UObject* Obj)
{
    UDialogueAsset* Asset = Cast<UDialogueAsset>(Obj);
    if (!Asset || !Asset->AssetImportData)
    {
        return EReimportResult::Failed;
    }

    const FString Filename = Asset->AssetImportData->GetFirstFilename();
    FString Error;
    if (Filename.IsEmpty() || !ReadCompanion(Asset, Filename, Error))
    {
        UE_LOG(LogTemp, Warning, TEXT("Dialogue Machine reimport failed: %s"), Error.IsEmpty() ? TEXT("the source file is unknown.") : *Error);
        return EReimportResult::Failed;
    }

    return EReimportResult::Succeeded;
}

int32 UDialogueAssetFactory::GetPriority() const
{
    return ImportPriority;
}
