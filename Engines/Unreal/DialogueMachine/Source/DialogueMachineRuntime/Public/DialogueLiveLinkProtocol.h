#pragma once

#include "CoreMinimal.h"

/** Pieces of the Dialogue Machine live-link protocol shared by the editor tab and the runner component. */
struct DIALOGUEMACHINERUNTIME_API FDialogueLiveLinkProtocol
{
    static constexpr int32 Protocol = 1;
    static constexpr int32 SchemaVersion = 1;

    /** Reads port and token from the discovery file Dialogue Machine writes while Live link is on. */
    static bool ReadDiscovery(int32& OutPort, FString& OutToken, FString* OutError = nullptr, int32* OutPid = nullptr);

    /** True when the process that wrote the discovery file has exited. An unknown pid (0) counts as still running. */
    static bool IsProcessGone(int32 Pid);

    static FString Url(int32 Port);

    /** The plugin's own version, read from the .uplugin so it can't drift from the app's version. */
    static FString PluginVersion();

    static FString MakeHello(const FString& Token);

    /** Dialogue Machine drops a connection after 30 s of silence, so every ping has to be answered. */
    static FString MakePong();
};
