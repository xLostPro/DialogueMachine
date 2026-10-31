#include "DialogueLiveLinkProtocol.h"
#include "Interfaces/IPluginManager.h"
#include "Misc/EngineVersion.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "HAL/PlatformMisc.h"
#include "HAL/PlatformProcess.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"
#include "Serialization/JsonWriter.h"

static FString Serialize(const TSharedRef<FJsonObject>& Object)
{
    FString Out;
    TSharedRef<TJsonWriter<>> Writer = TJsonWriterFactory<>::Create(&Out);
    FJsonSerializer::Serialize(Object, Writer);
    return Out;
}

bool FDialogueLiveLinkProtocol::ReadDiscovery(int32& OutPort, FString& OutToken, FString* OutError, int32* OutPid)
{
#if PLATFORM_WINDOWS
    const FString Path = FPlatformMisc::GetEnvironmentVariable(TEXT("LOCALAPPDATA")) / TEXT("DialogueMachine/livelink.json");
#else
    const FString Path = FPaths::Combine(FPlatformProcess::UserHomeDir(), TEXT(".local/share/DialogueMachine/livelink.json"));
#endif

    auto Fail = [OutError](const TCHAR* Message)
    {
        if (OutError) *OutError = Message;
        return false;
    };

    if (!FPaths::FileExists(Path)) return Fail(TEXT("Dialogue Machine is not running (discovery file not found)."));

    FString Content;
    if (!FFileHelper::LoadFileToString(Content, *Path)) return Fail(TEXT("Failed to read discovery file."));

    TSharedPtr<FJsonObject> Root;
    if (!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Content), Root) || !Root.IsValid()) return Fail(TEXT("Invalid discovery file JSON."));

    if (!Root->TryGetNumberField(TEXT("port"), OutPort) || !Root->TryGetStringField(TEXT("token"), OutToken) || OutPort <= 0 || OutToken.IsEmpty())
    {
        return Fail(TEXT("Malformed discovery file."));
    }
    if (OutPid)
    {
        *OutPid = 0;
        Root->TryGetNumberField(TEXT("pid"), *OutPid);
    }
    return true;
}

bool FDialogueLiveLinkProtocol::IsProcessGone(int32 Pid)
{
#if PLATFORM_DESKTOP
    return Pid > 0 && !FPlatformProcess::IsApplicationRunning((uint32)Pid);
#else
    return false; // Not implemented on every other platform, where calling it is fatal.
#endif
}

FString FDialogueLiveLinkProtocol::Url(int32 Port)
{
    return FString::Printf(TEXT("ws://127.0.0.1:%d/dialogue-machine/"), Port);
}

FString FDialogueLiveLinkProtocol::PluginVersion()
{
    TSharedPtr<IPlugin> Plugin = IPluginManager::Get().FindPlugin(TEXT("DialogueMachine"));
    return Plugin.IsValid() ? Plugin->GetDescriptor().VersionName : FString();
}

FString FDialogueLiveLinkProtocol::MakeHello(const FString& Token)
{
    TSharedRef<FJsonObject> Payload = MakeShared<FJsonObject>();
    Payload->SetStringField(TEXT("token"), Token);
    Payload->SetStringField(TEXT("engine"), TEXT("unreal"));
    Payload->SetStringField(TEXT("engineVersion"), FString::Printf(TEXT("%d.%d.%d"), FEngineVersion::Current().GetMajor(), FEngineVersion::Current().GetMinor(), FEngineVersion::Current().GetPatch()));
    Payload->SetStringField(TEXT("pluginVersion"), PluginVersion());
    Payload->SetNumberField(TEXT("protocol"), Protocol);
    Payload->SetNumberField(TEXT("schemaVersion"), SchemaVersion);

    TSharedRef<FJsonObject> Hello = MakeShared<FJsonObject>();
    Hello->SetNumberField(TEXT("protocol"), Protocol);
    Hello->SetStringField(TEXT("type"), TEXT("hello"));
    Hello->SetObjectField(TEXT("payload"), Payload);
    return Serialize(Hello);
}

FString FDialogueLiveLinkProtocol::MakePong()
{
    TSharedRef<FJsonObject> Pong = MakeShared<FJsonObject>();
    Pong->SetNumberField(TEXT("protocol"), Protocol);
    Pong->SetStringField(TEXT("type"), TEXT("pong"));
    Pong->SetObjectField(TEXT("payload"), MakeShared<FJsonObject>());
    return Serialize(Pong);
}
