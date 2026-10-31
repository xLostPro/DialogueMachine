#if WITH_DEV_AUTOMATION_TESTS

#include "Misc/AutomationTest.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Interfaces/IPluginManager.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"
#include "DialogueMachineRunner.h"

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FDialogueMachineConformanceTest, "DialogueMachine.Conformance", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

static FString FindConformanceFolder()
{
    TSharedPtr<IPlugin> Plugin = IPluginManager::Get().FindPlugin(TEXT("DialogueMachine"));
    if (Plugin.IsValid())
    {
        FString Path = FPaths::Combine(Plugin->GetBaseDir(), TEXT("Resources/Conformance"));
        if (FPaths::DirectoryExists(Path))
        {
            return Path;
        }
    }

    TArray<FString> Candidates = {
        FPaths::Combine(FPaths::ProjectPluginsDir(), TEXT("DialogueMachine/Resources/Conformance")),
        FPaths::Combine(FPaths::ProjectDir(), TEXT("Engines/Conformance")),
        FPaths::Combine(FPaths::ProjectDir(), TEXT("../../Engines/Conformance")),
        FPaths::Combine(FPaths::EnginePluginsDir(), TEXT("DialogueMachine/Resources/Conformance"))
    };

    for (const FString& Candidate : Candidates)
    {
        if (FPaths::DirectoryExists(Candidate))
        {
            return Candidate;
        }
    }

    return FString();
}

bool FDialogueMachineConformanceTest::RunTest(const FString& Parameters)
{
    FString Folder = FindConformanceFolder();
    if (!TestTrue(TEXT("Conformance folder exists"), !Folder.IsEmpty() && FPaths::DirectoryExists(Folder)))
    {
        return false;
    }

    TArray<FString> FixtureFiles;
    IFileManager::Get().FindFiles(FixtureFiles, *FPaths::Combine(Folder, TEXT("*.json")), true, false);

    if (!TestTrue(TEXT("At least 14 conformance fixtures found"), FixtureFiles.Num() >= 14))
    {
        return false;
    }

    for (const FString& FileName : FixtureFiles)
    {
        FString FilePath = FPaths::Combine(Folder, FileName);
        FString JsonContent;
        if (!FFileHelper::LoadFileToString(JsonContent, *FilePath))
        {
            AddError(FString::Printf(TEXT("Failed to read fixture: %s"), *FileName));
            continue;
        }

        TSharedPtr<FJsonObject> FixtureObj;
        TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(JsonContent);
        if (!FJsonSerializer::Deserialize(Reader, FixtureObj) || !FixtureObj.IsValid())
        {
            AddError(FString::Printf(TEXT("Invalid fixture JSON: %s"), *FileName));
            continue;
        }

        const TSharedPtr<FJsonObject>* HandoffObj = nullptr;
        FixtureObj->TryGetObjectField(TEXT("handoff"), HandoffObj);
        const TArray<TSharedPtr<FJsonValue>>* Inputs = nullptr;
        FixtureObj->TryGetArrayField(TEXT("inputs"), Inputs);
        const TArray<TSharedPtr<FJsonValue>>* ExpectedList = nullptr;
        FixtureObj->TryGetArrayField(TEXT("expected"), ExpectedList);

        if (!HandoffObj || !Inputs || !ExpectedList)
        {
            AddError(FString::Printf(TEXT("Malformed fixture format in %s"), *FileName));
            continue;
        }

        FString HandoffJson;
        TSharedRef<TJsonWriter<>> HandoffWriter = TJsonWriterFactory<>::Create(&HandoffJson);
        FJsonSerializer::Serialize((*HandoffObj).ToSharedRef(), HandoffWriter);

        TSharedPtr<FDialogueRunner> Runner = MakeShared<FDialogueRunner>();
        TArray<TSharedPtr<FJsonObject>> Events;
        FString SavedState;

        Runner->OnActionRaised.AddLambda([&Events](const FString& Type, const FString& TargetId, const FString& Parameter, int32 Amount)
        {
            TSharedPtr<FJsonObject> Evt = MakeShared<FJsonObject>();
            Evt->SetStringField(TEXT("type"), Type);
            Evt->SetStringField(TEXT("targetId"), TargetId);
            if (!Parameter.IsEmpty()) Evt->SetStringField(TEXT("parameter"), Parameter);
            else Evt->SetField(TEXT("parameter"), MakeShared<FJsonValueNull>());
            Events.Add(Evt);
        });

        Runner->OnShopOpened.AddLambda([&Events](const FString& ShopId)
        {
            TSharedPtr<FJsonObject> Evt = MakeShared<FJsonObject>();
            Evt->SetStringField(TEXT("type"), TEXT("Shop"));
            Evt->SetStringField(TEXT("targetId"), ShopId);
            Evt->SetField(TEXT("parameter"), MakeShared<FJsonValueNull>());
            Events.Add(Evt);
        });

        FString LoadError;
        if (!Runner->Load(HandoffJson, LoadError))
        {
            AddError(FString::Printf(TEXT("%s: Failed to load handoff: %s"), *FileName, *LoadError));
            continue;
        }

        for (int32 StepIdx = 0; StepIdx < Inputs->Num(); ++StepIdx)
        {
            const TSharedPtr<FJsonObject>* InputObj = nullptr;
            if (!(*Inputs)[StepIdx]->TryGetObject(InputObj) || !InputObj) continue;

            FString Op;
            (*InputObj)->TryGetStringField(TEXT("op"), Op);
            Events.Empty();

            if (Op == TEXT("start"))
            {
                Runner->Start();
            }
            else if (Op == TEXT("continue"))
            {
                Runner->Continue();
            }
            else if (Op == TEXT("choose"))
            {
                FString ChoiceId;
                (*InputObj)->TryGetStringField(TEXT("choiceId"), ChoiceId);
                Runner->Choose(ChoiceId);
            }
            else if (Op == TEXT("setNumber"))
            {
                FString Key;
                int32 Value = 0;
                (*InputObj)->TryGetStringField(TEXT("key"), Key);
                (*InputObj)->TryGetNumberField(TEXT("value"), Value);
                Runner->GetVariables()->SetNumber(Key, Value);
            }
            else if (Op == TEXT("setText"))
            {
                FString Key;
                FString Value;
                (*InputObj)->TryGetStringField(TEXT("key"), Key);
                (*InputObj)->TryGetStringField(TEXT("value"), Value);
                Runner->GetVariables()->SetText(Key, Value);
            }
            else if (Op == TEXT("save"))
            {
                SavedState = Runner->SaveState();
            }
            else if (Op == TEXT("restore"))
            {
                FString RestErr;
                Runner->RestoreState(SavedState, RestErr);
            }

            const TSharedPtr<FJsonObject>* Expected = nullptr;
            if (!(*ExpectedList)[StepIdx]->TryGetObject(Expected) || !Expected) continue;

            FString ExpectedScene;
            (*Expected)->TryGetStringField(TEXT("sceneId"), ExpectedScene);
            TestEqual(FString::Printf(TEXT("%s[%d] sceneId"), *FileName, StepIdx), Runner->GetCurrentSceneId(), ExpectedScene);

            bool bExpectedEnded = false;
            (*Expected)->TryGetBoolField(TEXT("ended"), bExpectedEnded);
            TestEqual(FString::Printf(TEXT("%s[%d] ended"), *FileName, StepIdx), Runner->IsEnded(), bExpectedEnded);

            FString ExpectedPage;
            (*Expected)->TryGetStringField(TEXT("page"), ExpectedPage);
            TestEqual(FString::Printf(TEXT("%s[%d] page"), *FileName, StepIdx), Runner->GetCurrentPage(), ExpectedPage);

            FString ExpectedPlain;
            (*Expected)->TryGetStringField(TEXT("plain"), ExpectedPlain);
            TestEqual(FString::Printf(TEXT("%s[%d] plain"), *FileName, StepIdx), Runner->GetCurrentPagePlain(), ExpectedPlain);

            int32 ExpectedPageIndex = 0;
            (*Expected)->TryGetNumberField(TEXT("pageIndex"), ExpectedPageIndex);
            TestEqual(FString::Printf(TEXT("%s[%d] pageIndex"), *FileName, StepIdx), Runner->GetPageIndex(), ExpectedPageIndex);

            int32 ExpectedPageCount = 0;
            (*Expected)->TryGetNumberField(TEXT("pageCount"), ExpectedPageCount);
            TestEqual(FString::Printf(TEXT("%s[%d] pageCount"), *FileName, StepIdx), Runner->GetPageCount(), ExpectedPageCount);

            bool bExpectedCanContinue = false;
            (*Expected)->TryGetBoolField(TEXT("canContinue"), bExpectedCanContinue);
            TestEqual(FString::Printf(TEXT("%s[%d] canContinue"), *FileName, StepIdx), Runner->CanContinue(), bExpectedCanContinue);

            const FString Step = FString::Printf(TEXT("%s[%d]"), *FileName, StepIdx);

            const TSharedPtr<FJsonObject>* ExpectedNumeric = nullptr;
            if ((*Expected)->TryGetObjectField(TEXT("numeric"), ExpectedNumeric) && ExpectedNumeric)
            {
                for (const auto& Pair : (*ExpectedNumeric)->Values)
                {
                    double Value = 0.0;
                    Pair.Value->TryGetNumber(Value);
                    TestEqual(FString::Printf(TEXT("%s numeric %s"), *Step, *FString(Pair.Key)), Runner->GetVariables()->GetNumber(FString(Pair.Key)), (int32)Value);
                }
            }

            const TSharedPtr<FJsonObject>* ExpectedText = nullptr;
            if ((*Expected)->TryGetObjectField(TEXT("text"), ExpectedText) && ExpectedText)
            {
                for (const auto& Pair : (*ExpectedText)->Values)
                {
                    FString Value;
                    Pair.Value->TryGetString(Value);
                    FString Actual;
                    Runner->GetVariables()->GetText(FString(Pair.Key), Actual);
                    TestEqual(FString::Printf(TEXT("%s text %s"), *Step, *FString(Pair.Key)), Actual, Value);
                }
            }

            const TArray<TSharedPtr<FJsonValue>>* ExpectedChoices = nullptr;
            if ((*Expected)->TryGetArrayField(TEXT("choices"), ExpectedChoices) && ExpectedChoices)
            {
                const TArray<FDialogueRuntimeChoice> Actual = Runner->GetChoices();
                if (TestEqual(FString::Printf(TEXT("%s choice count"), *Step), Actual.Num(), ExpectedChoices->Num()))
                {
                    for (int32 i = 0; i < Actual.Num(); ++i)
                    {
                        const TSharedPtr<FJsonObject>* Choice = nullptr;
                        if (!(*ExpectedChoices)[i]->TryGetObject(Choice) || !Choice) continue;
                        FString Id, Text;
                        bool bEnabled = false;
                        (*Choice)->TryGetStringField(TEXT("id"), Id);
                        (*Choice)->TryGetStringField(TEXT("text"), Text);
                        (*Choice)->TryGetBoolField(TEXT("enabled"), bEnabled);
                        TestEqual(FString::Printf(TEXT("%s choice %d id"), *Step, i), Actual[i].Id, Id);
                        TestEqual(FString::Printf(TEXT("%s choice %d text"), *Step, i), Actual[i].Text, Text);
                        TestEqual(FString::Printf(TEXT("%s choice %d enabled"), *Step, i), Actual[i].bEnabled, bEnabled);
                    }
                }
            }

            const TArray<TSharedPtr<FJsonValue>>* ExpectedEvents = nullptr;
            if ((*Expected)->TryGetArrayField(TEXT("events"), ExpectedEvents) && ExpectedEvents)
            {
                if (TestEqual(FString::Printf(TEXT("%s event count"), *Step), Events.Num(), ExpectedEvents->Num()))
                {
                    for (int32 i = 0; i < Events.Num(); ++i)
                    {
                        const TSharedPtr<FJsonObject>* Event = nullptr;
                        if (!(*ExpectedEvents)[i]->TryGetObject(Event) || !Event) continue;
                        for (const TCHAR* Field : { TEXT("type"), TEXT("targetId"), TEXT("parameter") })
                        {
                            FString Want, Got;
                            (*Event)->TryGetStringField(Field, Want);
                            Events[i]->TryGetStringField(Field, Got);
                            TestEqual(FString::Printf(TEXT("%s event %d %s"), *Step, i, Field), Got, Want);
                        }
                    }
                }
            }
        }
    }

    return true;
}

#endif
