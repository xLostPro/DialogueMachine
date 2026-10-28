#!/usr/bin/env bash
set -euo pipefail
export AVALONIA_TELEMETRY_OPTOUT=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
project_root="$(cd "$(dirname "$0")" && pwd)"
runtime="${1:-}"
if [ -z "$runtime" ]; then
  case "$(uname -s):$(uname -m)" in
    Darwin:arm64) runtime=osx-arm64 ;; Darwin:*) runtime=osx-x64 ;;
    Linux:aarch64) runtime=linux-arm64 ;; Linux:*) runtime=linux-x64 ;;
    *) echo 'Specify a runtime: linux-x64, linux-arm64, osx-x64 or osx-arm64.'; exit 1 ;;
  esac
fi
case "$runtime" in linux-x64|linux-arm64|osx-x64|osx-arm64) ;; *) echo "Unsupported runtime: $runtime"; exit 1 ;; esac
output="$project_root/dist/$runtime"
# Runtime-specific restores leave the committed, platform-neutral locks unchanged.
dotnet publish "$project_root/DialogueMachine.csproj" -c Release -r "$runtime" --self-contained true -o "$output" --configfile "$project_root/NuGet.Config" "-p:NuGetLockFilePath=obj/$runtime.packages.lock.json"
cp "$project_root/README.md" "$output/"
chmod +x "$output/DialogueMachine"
if [[ "$runtime" == osx-* ]]; then
  bundle="$project_root/dist/$runtime/Dialogue Machine.app"
  mkdir -p "$bundle/Contents/MacOS" "$bundle/Contents/Resources"
  # Explicit files keep the bundle out of its own copy operation.
  find "$output" -maxdepth 1 -type f -exec cp {} "$bundle/Contents/MacOS/" \;
  if [ -d "$output/Examples" ]; then cp -R "$output/Examples" "$bundle/Contents/MacOS/"; fi
  cp "$project_root/Packaging/Info.plist" "$bundle/Contents/Info.plist"
  chmod +x "$bundle/Contents/MacOS/DialogueMachine"
  echo "App bundle ready: $bundle"
else
  echo "Build ready: $output/DialogueMachine"
fi
