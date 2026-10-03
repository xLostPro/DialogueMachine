# Dialogue Machine · Desktop

An offline workspace for branching dialogue, built with **C# / .NET 10 and Avalonia 12**. The desktop project lives here, separately from the existing Blazor website. It imports and exports the website's `ListOfCharacterStates` JSON schema.

## Run the Windows app

Double-click **Launch Dialogue Machine.cmd**, or run `dist/Windows/DialogueMachine.exe`. The packaged Windows build contains the .NET runtime; no SDK or browser is needed. Keep the files in `dist/Windows` together. The app starts at **Welcome**, with New project, Browse files, and recent projects. The Last Lighthouse is an optional example you can explore and save as your own copy.

For GitHub Releases, download the Windows ZIP, extract **all** its files to a folder, and run **DialogueMachine.exe** in that folder.

From source, install the .NET 10 SDK and .NET runtime 10.0.12 or newer, then run:

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet restore DialogueMachine.slnx --configfile NuGet.Config
dotnet run --project DialogueMachine.csproj -c Release --no-restore
```

To open a project at launch, pass its JSON path as the executable's first argument.

## Your workspace

- **Welcome:** create a named blank story, browse for desktop or website JSON, or open one of your eight most recent projects. Removing a recent entry keeps the file. Unavailable files are shown so you can locate them again. **Home** returns here after resolving unsaved edits. Enable **Reopen my last saved project** if you prefer to resume automatically; unsaved recovery takes priority so it cannot be missed.
- **Outline:** search titles, IDs, speakers, dialogue, responses, tags and notes. Expand **Filter scenes** to combine speaker, tag and diagnostic filters with text search. The count shows matching/total scenes; **Clear search & filters** restores the full list. Filters refresh after edits and undo/redo. Select scenes using the mouse or keyboard.
- **Story graph:** drag scenes; drag the background to pan; scroll to zoom. Drag a choice output dot or the bottom continue dot onto a scene to connect it. **Choose…** beside a destination opens a title/ID search: type, press Down to focus the results, and Enter to choose. Cancel/Escape preserves the current target; unset, continue fallback and End conversation stay explicit. Missing imported targets remain visible until repaired. One Undo restores a chosen connection. **Jump →** opens the connected scene, including inherited choice targets. Use **Fit story** or **Arrange** as the graph grows.
- **Content:** edit the title, speaker, dialogue and player responses. Each dialogue line is one page. Delivery can read all pages or choose one random line. **Variable…** searches known numeric and text IDs and inserts a substitution at the dialogue cursor. Responses inherit the scene's continue destination when their own target is unset.
- **Logic:** add conditions and actions, set condition failure to Disable/Hide/Always, and configure shop scenes. Variable ID fields suggest matching affinity or inventory IDs as you type; new IDs can still be entered. All conditions on a response must pass. Actions support affinity, inventory, Event and Script; inventory never drops below zero.
- **Variables & characters:** search starting values and text substitutions, view reference counts and scene locations, and rename IDs across actions, requirements and inline text. Delete unused variables after resolving their references. Changes stay in a draft until **Apply changes**; Cancel or Escape discards them, and one Undo restores the entire applied operation.
- **Notes:** writer's notes, tags, safe ID renaming, incoming/outgoing connection jumps, duplication and the story's start scene. Connected-scene jumps reveal a target hidden by outline filters; endings, missing and unset targets are labelled.
- **Find & replace:** Ctrl/Cmd+H or Ctrl/Cmd+Shift+F opens literal search across dialogue, responses, titles and notes. Choose the entire project or selected scene, select fields, and enable case or whole-word matching. Highlighted matches show context and a replacement preview; **Go to match** opens and selects the exact editor text. **Replace selected** changes one occurrence; **Replace all** covers every match in the chosen scope, including results beyond the first 200 previews. An empty replacement removes text. Replacements apply immediately; close the dialog and Undo/Redo the complete operation. Formatting and condition tags are searchable text; use Variables & characters for reference-aware variable renames.
- **Script:** a readable overview alongside the editor.
- **Diagnostics:** missing targets, duplicate/reserved IDs, unsupported scene types or visibility values, invalid conditions, unreachable scenes and regions without an ending. Inline conditions are checked in dialogue and responses, including unused branches. Formatting problems and unknown substitutions produce warnings. Cycles with an exit are allowed.
- **Playtest:** F5 from the story start; Shift+F5 from the selected scene. Test variables live, revisit decisions with Back, and export the transcript. Live edits and refreshed text are recorded; Back restores those edits too. Random delivery keeps its selected line during refresh; **Reroll line** explicitly samples again. Events, scripts and shops are **logged**, not executed; your game supplies those integrations.

## Files and recovery

**Save project** stores desktop settings, entry point, variable starting values, scene metadata, graph positions, and all dialogue data. **Export website / game JSON** writes the original root schema with numeric visibility enums, and moves the chosen start scene to the first position for the website's simulator. Existing web JSON files import directly. Unknown object fields are retained when editing desktop projects. Desktop format version 1 is supported; incompatible format versions report an error while retaining the open story.

Web exports do not contain desktop variable starting values or text substitutions; provide these through your game's runtime or the web player's simulation setup. Script exports preserve inline condition syntax so both branches remain visible to writers. Graph export captures the visible viewport as a PNG; fit the story first to include every scene.

Writes use a flushed temporary file and atomic replacement. Recovery copies protect unsaved work every 10 seconds; they do not overwrite the open project. Save or Discard removes the active recovery file. Welcome shows unsaved recovery copies, including those newer than a saved project, for explicit restoration. Damaged recovery records are skipped and retained for manual investigation, allowing startup and valid recovery choices to remain available. Other recoveries are available from File → Recovery saves. Canceling project creation, file browsing, Save as, or the unsaved-changes prompt keeps your current work. Invalid imports report the problem without replacing the current story.

Preferences, recent files and recovery copies are stored under:

- Windows: `%LOCALAPPDATA%/DialogueMachine`
- Linux/macOS: the platform's .NET `LocalApplicationData` directory, under `DialogueMachine`

There are no accounts, network services, analytics, or online dependencies at runtime. Dependency downloads happen during build/restore.

## Accessibility and keyboard

Appearance settings offer **Dark, Light and High contrast** themes and **100%, 115%, 130%** interface size. Inputs have accessibility labels; buttons and pickers use Avalonia's native control automation. The scene outline and destination pickers provide keyboard alternatives to the custom graph. Tab moves through controls. Arrow keys move the selected graph scene; Shift increases the step.

| Shortcut | Action |
| --- | --- |
| Ctrl/Cmd+N / O / S | New / Open / Save |
| Ctrl/Cmd+Shift+S | Save as |
| Ctrl/Cmd+F | Search the outline |
| Ctrl/Cmd+H / Shift+F | Project-wide find & replace |
| Ctrl/Cmd+Z / Shift+Z | Undo / Redo |
| Ctrl/Cmd+D | Duplicate selected scene |
| Ctrl/Cmd+0 | Fit story |
| F5 / Shift+F5 | Play from start / selected scene |
| F1 | Guide |
| Escape | Cancel a dialog / close playtest |

## Inline text

```text
[b]Bold[/b] [i]Italic[/i] [u]Underline[/u]
[colour=#79DFC8]Coloured text[/colour]
Welcome, {player_name}. You have {inv:coin} coins.
[if aff:trust >= 1]A friend.[else]A stranger.[/if]
```

Nested condition blocks and negative integer thresholds are supported. Variable IDs are case-sensitive. Use `aff:` for affinity and `inv:` for inventory. Unprefixed names resolve inventory first, then affinity, matching the website's fallback behavior.

## Build packages

```powershell
# Windows, self-contained
./build.ps1 -Runtime win-x64
# Other targets can also be cross-published from Windows:
./build.ps1 -Runtime linux-x64
./build.ps1 -Runtime osx-arm64
```

```bash
# On Linux/macOS: defaults to the current OS and architecture
bash build.sh
# Explicit targets
bash build.sh linux-x64
bash build.sh osx-arm64
bash build.sh osx-x64
```

Both build scripts produce macOS `.app` bundles. Distribution signing/notarization is a separate release step. Linux requires the native dependencies described in [Avalonia's Linux deployment guide](https://docs.avaloniaui.net/docs/deployment/linux). When copying a raw cross-published Linux or Mac build from Windows, restore the executable bit with `chmod +x DialogueMachine` (or the `.app`'s `Contents/MacOS/DialogueMachine`). **Windows, Linux x64, Apple Silicon Mac and Intel Mac publish successfully; only Windows has been launched and exercised locally.** Linux and macOS builds require native smoke testing before a release. GitHub Actions automates Windows verification and releases; other platforms can be built using the scripts above.

Self-contained packages include .NET 10.0.12, pinned in the project file so packaged apps use the same tested runtime across platforms. Update this patch version deliberately when refreshing dependencies.

## GitHub builds and releases

This desktop app has its own private repository, [xLostPro/DialogueMachine](https://github.com/xLostPro/DialogueMachine), separate from the website.

Pull requests and pushes to `master` or `main` run the regression harness, build Windows x64 with its runtime included, verify native welcome/project startup, and upload a ZIP and SHA-256 checksum as workflow artifacts. The workflow can also be run manually from Actions. Verification screenshots and logs are retained as a separate artifact.

Routine source delivery follows [AGENTS.md](AGENTS.md). The default is to verify the change, commit the intended files on a `codex/` feature branch, push it to `origin`, open a pull request against `master`, and inspect Windows CI. A feature-branch push by itself does not run the current workflow. Report explicitly whether work remains local or has reached GitHub. The user's current instructions can override this default; release tags are a separate publishing step.

After an authorized merge, update local `master`, delete the merged feature branch locally and on GitHub, and prune stale remote-tracking refs. Older finished branches can be removed after confirming their PRs are merged and their tips are contained in `origin/master`; retain branches with unmerged work or active worktrees.

To publish a release:

1. Update `Version` in `DialogueMachine.csproj` and `RELEASE_NOTES.md` for the new version. Keep the Mac version in `Packaging/Info.plist` aligned if building Mac packages.
2. Merge the change after the Windows workflow passes.
3. Create and push the matching tag, for example:

```powershell
git tag -a v1.1.0 -m "Dialogue Machine 1.1.0"
git push origin v1.1.0
```

The tag must exactly match the project version. After all checks pass, the workflow creates a GitHub Release and attaches `DialogueMachine-1.1.0-win-x64.zip` and its `.sha256` file. A release stays a draft until both uploads succeed; rerunning a failed upload can complete the draft. Published releases are left unchanged on reruns. Tags with a prerelease version such as `v1.2.0-beta.1` are marked as prereleases.

Publishing uses GitHub's automatically supplied token. Only the release job has `contents: write`; compilation and tests use read access. Action versions are pinned to commits, with monthly Dependabot checks for updates. Build outputs, downloaded packages, and local recovery/test data are excluded from source control.

## Verify and maintain

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet run --project tests/DialogueMachine.Tests.csproj -c Release -- artifacts
```

The executable test harness exits nonzero on failure. Its 242 checks cover imports, exports, validation, conditions, simulations, atomic saves, recovery, history, actual graph mouse input, editor operations, accessibility labels, and welcome/open/save-as/cancel/reopen workflows. Reliability regressions include damaged recovery at startup, incompatible format imports, hidden malformed conditions, tall-node layout, 10,000-scene ending checks, and actual live-variable/Back/reroll controls. Variable checks cover reference resolution, numeric/text renames, collisions, unused deletion, draft cancellation, one-step undo, searchable insertion and logic suggestions. Search checks cover literal/case/whole-word matching, field/scene scope, stale-preview rejection, duplicate IDs, unknown-field preservation, bounded previews with complete replacement, exact editor navigation, selected/all replacement and one-step undo/redo. Navigation checks cover title/ID destination search, keyboard selection, cancellation, fallback/unset/ending/missing targets, one-step connection undo, incoming/outgoing jumps, combined outline filters, diagnostic/tag refresh, compact layouts and 100/1,000/10,000-scene probes. File workflow checks use isolated preferences, actual disk writes and an injected file picker; they preserve original website-format files. Headless rendering creates screenshots of welcome, recent/recovery lists, workspace, variable, find/replace and destination dialog themes, compact/large-text layouts and playtest in `artifacts` without opening windows. The Windows package has also passed native welcome and project startup/render smoke tests; `--smoke-test --smoke-output <directory>` runs offscreen, and `--smoke-project <path>` also tests opening a project. These checks do not replace manual screen-reader, OS file-picker or platform testing.

See [ARCHITECTURE.md](ARCHITECTURE.md) for the project boundaries, design decisions, and extension points.

See [ROADMAP.md](ROADMAP.md) for the project assessment, completed reliability work, and proposed next features.
