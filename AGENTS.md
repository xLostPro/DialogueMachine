# Dialogue Machine: agent guide

Offline C#/.NET 10 + Avalonia desktop dialogue editor. It's separate from the website, but exports the website's JSON. Remote: `origin` (`xLostPro/DialogueMachine`), primary branch `master`.

## Principles

- Pick work from `ROADMAP.md` → Now, and update it when an item is done. Read `ARCHITECTURE.md` before structural changes.
- Keep it simple: build the smallest change that solves the real problem. No speculative abstractions, extra docs, benchmarks or "stage" reports. Prefer deleting code over adding it.
- Keep website JSON field names, numeric visibility order (Hide=0, Disable=1, Always=2) and unknown-field round trips. Core must not reference UI.

## Verify

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'; $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
dotnet run --project tests/DialogueMachine.Tests.csproj -c Release -- artifacts
```

- The harness exits nonzero on failure and writes screenshots to `artifacts/`; look at the relevant ones for UI changes. It also runs on Linux. Windows packaging is checked by CI.
- If you change dependencies, refresh the lock files (`dotnet restore DialogueMachine.slnx --configfile NuGet.Config`); CI restores with `--locked-mode`.
- Run `git diff --check` and review the full diff before committing.

## Deliver

- Default: commit on a feature branch, push to `origin`, open a PR against `master` (this also starts Windows CI), and fix any CI failures your change causes. Skip this only if the user asks for local-only work.
- Stage only your task's files. Never force-push, rewrite shared history or push directly to `master` unless asked.
- After a merge: update `master`, delete the merged branch locally and on `origin`, and prune.
- Finish by saying where the work is: local, pushed, or in a PR (with link and CI status).

## Releases

Only when asked. Bump the version in `DialogueMachine.csproj`, `Packaging/Info.plist`, `Engines/Unity/com.xlostpro.dialoguemachine/package.json`, `Engines/Unity/com.xlostpro.dialoguemachine/Runtime/Unity/DialogueRunnerBehaviour.cs` (`PluginVersion`), `Engines/Unreal/DialogueMachine/DialogueMachine.uplugin`, `Engines/Godot/addons/dialogue_machine/plugin.cfg`, the About text and `RELEASE_NOTES.md`, merge, then push tag `v<Version>`. The tag workflow publishes the release and attaches the Godot and Unreal plugin ZIPs. Never commit build output, ZIPs, `.packages` or `.dotnet`.
