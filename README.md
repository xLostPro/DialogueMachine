# Dialogue Machine · Desktop

An offline editor for branching dialogue, built with **C# / .NET 10 and Avalonia 12**. It reads and writes the website's `ListOfCharacterStates` JSON, so stories written here can be played on the website or in a game.

## Run it

- **Windows release:** download the ZIP from GitHub Releases, extract **all** files to one folder and run `DialogueMachine.exe`. No .NET install is needed.
- **Local build:** double-click **Launch Dialogue Machine.cmd**, or run `dist/Windows/DialogueMachine.exe`.
- **From source** (.NET 10 SDK, runtime 10.0.12 or newer):

  ```powershell
  $env:AVALONIA_TELEMETRY_OPTOUT = '1'
  dotnet restore DialogueMachine.slnx --configfile NuGet.Config
  dotnet run --project DialogueMachine.csproj -c Release --no-restore
  ```

Pass a project's JSON path as the first argument to open it at launch.

## What you can do

- **Welcome:** start a blank story, open desktop or website JSON, reopen one of eight recent projects, or explore the Last Lighthouse example.
- **Write:** each scene has a title, speaker, dialogue pages (read in order, one picked at random, or one per visit that escalates and stops on the last line), player responses, notes and tags. A **Script** view shows the whole story as text.
- **Connect:** drag from a response's dot (or the continue dot) onto a scene, or use **Choose…** to search destinations by title or ID. **Jump →** follows a link. **Arrange** and **Fit story** tidy the graph; the optional minimap (Ctrl/Cmd+Shift+M) helps in large stories.
- **Organize:** multi-select scenes (Ctrl/Cmd-click, Shift-click, Shift-drag), move them together, copy/paste them with links remapped, save scene and response templates, and put scenes in overlapping **groups/chapters** (Ctrl/Cmd+Shift+G).
- **Logic:** responses can require affinity/inventory values (failing responses are hidden, disabled or always shown) and can change values or fire Event/Script/Shop hooks. **Variables & characters** manages starting values and text substitutions and renames them everywhere they are used.
- **Find & replace:** Ctrl/Cmd+H searches dialogue, responses, titles and notes across the project.
- **Diagnostics:** missing targets, invalid IDs or conditions, unreachable scenes and dead ends are listed and can be filtered in the outline.
- **Playtest:** F5 plays from the start, Shift+F5 from the selected scene. Edit variables live, step Back, reroll random lines and export the transcript. Event/Script/Shop hooks are logged; your game implements them.
- **Test scenarios:** **Story → Test scenarios…** saves a recorded playthrough with a seed, starting values and expected results, then replays it after you edit the story. See [STORY_TESTING.md](STORY_TESTING.md).
- **Export & translation:** game engine runtime companion payload (dialogue-machine.story.v1), a full-story PNG of the graph, a readable Markdown script, or an RFC 4180 CSV translation table (with validated re-import).

Every edit can be undone (Ctrl/Cmd+Z / Shift+Z), and bulk operations undo as one step.

### Inline text

```text
[b]Bold[/b] [i]Italic[/i] [u]Underline[/u] [colour=#79DFC8]Coloured[/colour]
Welcome, {player_name}. You have {inv:coin} coins.
[if aff:trust >= 1]A friend.[else]A stranger.[/if]
[if calling != ""]You are {a:calling}.[else]Choose a calling.[/if]
```

Conditions can nest and use negative numbers. Use `aff:` for affinity and `inv:` for inventory; unprefixed names check inventory first, then affinity, like the website. A quoted value tests a text variable with `==` or `!=` (unset counts as `""`); choice conditions have a matching **Text** type in the Logic tab. `{a:key}` writes "a" or "an" before a text variable's value.

### Keyboard

| Shortcut | Action |
| --- | --- |
| Ctrl/Cmd+N / O / S / Shift+S | New / Open / Save / Save as |
| Ctrl/Cmd+Z / Shift+Z | Undo / Redo |
| Ctrl/Cmd+F / H | Search outline / Find & replace |
| Ctrl/Cmd+C / V | Copy / paste scenes (graph or outline focused) |
| Ctrl/Cmd+D | Duplicate scene |
| Ctrl/Cmd+A, Escape | Select all / clear selection |
| Ctrl/Cmd+Tab, Space, Enter | Move focus between graph scenes, toggle, select |
| Arrows (Shift for farther) | Move selected scenes; Alt+arrows pans |
| Ctrl/Cmd+0 | Fit story |
| F5 / Shift+F5 | Playtest from start / selected scene |
| F1 | Guide |

Dark, Light and High contrast themes and 100/115/130% interface sizes are under Appearance.

## Files and safety

- **Save project** stores the full desktop project (layout, groups, templates, scenarios, starting values). **Export website / game JSON** writes the original website schema with numeric visibility values and the start scene first. Website JSON opens directly, and unknown fields are preserved.
- Saves are atomic. If another program changed the file since you opened it, Save offers **Compare**, **Reload**, **Save a copy** or **Cancel** instead of overwriting.
- Unsaved work is copied to a recovery file every 10 seconds and offered on the next start.
- **File → Engine export folder…** is saved in the project, relative to the project file, so it follows the project to other machines and through moves. Saving reports a folder that no longer exists instead of recreating it.
- Settings, recent files and recovery copies live in `%LOCALAPPDATA%/DialogueMachine` on Windows (the .NET `LocalApplicationData` folder elsewhere). The app makes no network calls.

### Writing projects with scripts or agents

Project files (`*.dialogue.json`) are plain JSON. To generate one outside the editor:

1. Follow [`Schemas/dialogue-project.schema.json`](Schemas/dialogue-project.schema.json). It describes every field, the allowed values and the text markup.
2. Start from [`Examples/lighthouse.dialogue.json`](Examples/lighthouse.dialogue.json) for a complete small story.
3. Validate it and fix every error it reports (missing destinations, duplicate IDs, bad conditions). Exit code 0 means the editor and engines will accept it:

```powershell
dotnet run --project CLI/DialogueMachine.Cli.csproj -c Release -- validate path/to/story.dialogue.json
```

## Unity

Dialogue Machine includes an official package for Unity (2022.3 LTS and Unity 6):

- **Install via UPM:** In Unity's Package Manager, choose **Add package from git URL...** and enter:
  ```text
  https://github.com/xLostPro/DialogueMachine.git?path=/Engines/Unity/com.xlostpro.dialoguemachine#v1.6.0
  ```
- **Importing:** Point Dialogue Machine's auto-export folder inside your Unity project's `Assets/` directory. Unity's `ScriptedImporter` automatically creates a `DialogueAsset` for each `*.runtime.json` file on change.
- **Runner:** Add `DialogueRunnerBehaviour` to a GameObject and assign the `DialogueAsset`. It exposes C# and Unity events (`OnLine`, `OnChoices`, `OnAction`, `OnEnded`) and supports custom `IDialogueVariables` providers. Without a provider each runner keeps its own variables and resets them on start; tick **Share Variables** to use `DialogueRunnerBehaviour.SharedVariables` instead, one store for every runner that survives scene loads. Save and load it (or any `DictionaryVariables`) with `ToJson()` / `LoadJson(json)` in your save file.
- **Presentation:** Add `DefaultDialogueView` to build a clean UI Toolkit conversation panel entirely in code with typewriter effects and keyboard/gamepad focus navigation. Or implement `IDialogueView` for custom UI. The view has no input binding of its own: call `runner.Continue()` from your game's interact/submit action.
- **Events & Hooks:** Attach `DialogueEventRouter` to route dialogue `Event` and `Script` actions to `UnityEvent` listeners, and handle `Shop` scenes with `onShop`.
- **Live Link:** Open **Window → Dialogue Machine → Live Link**. When the bridge is active in Dialogue Machine, edits push instantly to your asset and live runners, and running gameplay is tracked on the graph in DM's **Follow** pane.

## Unreal

Dialogue Machine includes an official plugin for Unreal Engine (UE 5.3+):

- **Installation:** Copy `Engines/Unreal/DialogueMachine` into your project's `Plugins/` folder (or download the pre-packaged zip from Releases) and enable the plugin.
- **Importing:** Point Dialogue Machine's auto-export folder inside your Unreal project's `Content/` directory. Import the first `<project-name>.runtime.json` once (Content Browser → Import, or drag it in) to create a `UDialogueAsset`; after that Unreal's auto-reimport offers to reimport it whenever Save rewrites the file. With the live link connected, saving also updates that asset, which you then save in Unreal. An asset imported with an older plugin needs one Reimport and save before auto-reimport finds it.
- **Runner:** Attach `UDialogueRunnerComponent` to any Actor and assign the `UDialogueAsset`. It exposes BlueprintCallable functions (`StartDialogue`, `ContinueDialogue`, `ChooseChoice`, `SaveState`, `RestoreState`) and BlueprintAssignable delegates (`OnLine`, `OnChoices`, `OnAction`, `OnShop`, `OnVariableChanged`, `OnEnded`). Implement `IDialogueVariablesInterface` on your Actor to let game state own the variables.
- **Presentation:** Use or subclass `UDialogueMachineWidget` (built in code using UMG) for speaker text, rich text formatting, choices vertical layout, and keyboard/gamepad navigation.
- **Live Link:** Open **Window → Dialogue Machine Live Link**. When the bridge is running in Dialogue Machine, story edits hot-reload in Play In Editor (PIE), and dialogue progression is tracked live in Dialogue Machine's **Follow** pane in non-shipping builds.
- **Automation Tests:** Run the `DialogueMachine.Conformance` test suite in the Unreal Editor via **Tools → Session Frontend → Automation** to verify all reference dialogue fixtures.

## Godot

Dialogue Machine includes an official addon for Godot (4.3+ with standard GDScript):

- **Installation:** Copy `Engines/Godot/addons/dialogue_machine` into your project's `addons/` folder (or extract the pre-packaged zip from Releases) and enable the plugin in **Project → Project Settings → Plugins**.
- **Importing:** Point Dialogue Machine's auto-export folder inside your Godot project's `res://` directory. Godot's `EditorImportPlugin` automatically imports each `*.runtime.json` file into a `DialogueStory` `.tres` resource whenever changes are saved.
- **Runner:** Add `DialogueRunner` to your scene and assign the `DialogueStory` resource. It exposes GDScript functions (`start`, `continue_dialogue`, `choose`, `save_state`, `restore_state`) and signals (`line_shown`, `choices_shown`, `action_raised`, `shop_opened`, `variable_changed`, `ended`). Assign a custom `variables_provider` node to let game state own the variables: it needs `get_number`, `set_number`, `get_text` and `set_text` (add `has_number` so story defaults never overwrite its values; `get_all_numeric`, `get_all_text` and `get_keys` are optional). The runner joins the `dialogue_runner` group.
- **Presentation:** Add `DialogueView` (built in code using Godot UI controls) to display speaker labels, BBCode formatted dialogue via `RichTextLabel`, and dynamic response buttons with built-in keyboard and gamepad navigation.
- **Live Link:** Open the **Dialogue Machine** dock panel. When the bridge is running in Dialogue Machine, story edits hot-reload in real time, and dialogue progression in debug builds is tracked live in Dialogue Machine's **Follow** pane. **Play in engine** only reaches a game that is already running; it does not start one.
- **Conformance Tests:** Run `godot --headless --path Engines/Godot --script res://addons/dialogue_machine/tests/run_conformance.gd` to verify all reference dialogue fixtures.

## Build packages

```powershell
./build.ps1 -Runtime win-x64      # also linux-x64, osx-arm64, ...
```

```bash
bash build.sh                     # current OS, or e.g. bash build.sh osx-arm64
```

Output goes to `dist/`. Windows, Linux (x64), and macOS (ARM64) builds and headless UI regression suites are continuously verified in CI. On Linux, native GUI runs require [Avalonia's native dependencies](https://docs.avaloniaui.net/docs/deployment/linux).

## Development

Run the regression harness (core and headless UI checks; exits nonzero on failure and writes screenshots to the output folder):

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet run --project tests/DialogueMachine.Tests.csproj -c Release -- artifacts
```

GitHub Actions runs the harness across Windows, Linux (x64) and macOS (ARM64) on pull requests and pushes to `master`, packages binaries for all platforms, and smoke-tests native Windows startup. To save Actions storage, the ZIP is only stored for release tags, and screenshots/logs only for failed runs (kept 3 days).

**Releasing:** bump `Version` in `DialogueMachine.csproj`, `Packaging/Info.plist` and `RELEASE_NOTES.md`, merge after CI passes, then push a matching tag (`git tag -a v1.6.0 -m "Dialogue Machine 1.6.0" && git push origin v1.6.0`). The tag workflow publishes the GitHub Release; prerelease versions such as `v1.6.0-beta.1` are marked as prereleases.

See [ARCHITECTURE.md](ARCHITECTURE.md) for how the code is organized and [ROADMAP.md](ROADMAP.md) for what's next.
