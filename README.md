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
- **Story graph:** Ctrl/Cmd-click or Shift-click toggles scenes; Shift-drag on the background adds scenes intersecting a selection rectangle. Drag any selected scene to move the whole set, preserving its relative positions. Each drag or held arrow-key nudge is one Undo/Redo step; Escape cancels a movement. The editor shows one active scene from the set. Outgoing/internal selected connections are purple (yellow in High contrast); incoming connections are green. Highlighted links are thicker, with arrows; continue links stay dashed. Drag the background to pan; scroll to zoom. Drag a choice output dot or the bottom continue dot onto a scene to connect it. **Choose…** beside a destination opens a title/ID search: type, press Down to focus the results, and Enter to choose. Cancel/Escape preserves the current target; unset, continue fallback and End conversation stay explicit. Missing imported targets remain visible until repaired. One Undo restores a chosen connection. **Jump →** opens the connected scene, including inherited choice targets. Use **Fit story** or **Arrange** as the graph grows.
- **Content:** edit the title, speaker, dialogue and player responses. Each dialogue line is one page. Delivery can read all pages or choose one random line. **Variable…** searches known numeric and text IDs and inserts a substitution at the dialogue cursor. Responses inherit the scene's continue destination when their own target is unset.
- **Logic:** add conditions and actions, set condition failure to Disable/Hide/Always, and configure shop scenes. Variable ID fields suggest matching affinity or inventory IDs as you type; new IDs can still be entered. All conditions on a response must pass. Actions support affinity, inventory, Event and Script; inventory never drops below zero.
- **Variables & characters:** search starting values and text substitutions, view reference counts and scene locations, and rename IDs across actions, requirements and inline text. Delete unused variables after resolving their references. Changes stay in a draft until **Apply changes**; Cancel or Escape discards them, and one Undo restores the entire applied operation.
- **Notes:** writer's notes, tags, safe ID renaming, incoming/outgoing connection jumps, duplication and the story's start scene. Connected-scene jumps reveal a target hidden by outline filters; endings, missing and unset targets are labelled.
- **Reuse scenes & choices:** **New scene…** on a choice creates a blank scene, inherits its speaker, connects that choice and selects the new scene. One Undo restores both the scene and previous destination. **Edit → Copy selected scenes** copies the current graph/outline set directly. **Edit → Copy scenes…** starts with that set and lets you toggle scenes with the mouse or Space, select a range with Shift, or select all. **Edit → Paste scenes** selects all inserted scenes, gives them fresh scene/choice IDs, remaps links within the set (including cycles), and keeps relative graph positions. Other targets, including missing targets, remain visible for review. Copies stay available until this app window closes, including across project changes; the OS text clipboard is separate. Each paste is one undo step. Ctrl/Cmd+C/V uses this scene clipboard while the graph or outline is focused; text inputs keep their normal clipboard shortcuts.
- **Templates:** save a scene from **Notes → Save scene template…**, or a response with its logic from **Save template…** on a choice. **Edit → Scene & choice templates…** searches, previews, inserts or deletes saved definitions; **Add choice from template…** lists responses only. Down focuses the results; Enter inserts; Escape closes previews. Scene templates keep self-links and endings and clear other destinations; choice templates use the selected scene's continue destination. Inserts are independent copies, and saves, insertions and deletions support Undo/Redo. Templates belong to the saved desktop project and recovery, and are omitted from website exports. Variable renames update templates and their reference counts protect used variables from deletion. Copies and templates keep variable IDs; configure starting values and text substitutions in the destination project when copying between projects.
- **Find & replace:** Ctrl/Cmd+H or Ctrl/Cmd+Shift+F opens literal search across dialogue, responses, titles and notes. Choose the entire project or selected scene, select fields, and enable case or whole-word matching. Highlighted matches show context and a replacement preview; **Go to match** opens and selects the exact editor text. **Replace selected** changes one occurrence; **Replace all** covers every match in the chosen scope, including results beyond the first 200 previews. An empty replacement removes text. Replacements apply immediately; close the dialog and Undo/Redo the complete operation. Formatting and condition tags are searchable text; use Variables & characters for reference-aware variable renames.
- **Script:** a readable overview alongside the editor.
- **Groups & chapters:** open Story tools → Groups & chapters or Ctrl/Cmd+Shift+G. Create an empty named group, select it in the manager to rename/delete it, and assign or remove the complete currently selected scene set. Scenes can belong to several groups. **Select group's scenes** replaces the selection and keeps the active editor scene when it is a member; an empty group clears selection. **Filter outline by group** combines with text, speaker, tag and diagnostic filters; **Ungrouped scenes** shows scenes with no memberships. Creating, renaming, deleting, assigning and removing each form one Undo/Redo step after closing the manager. Deleting a group preserves every scene and connection. Groups persist in desktop saves and recovery, including repairable duplicate-ID scenes. Copy/paste, duplication and template insertion always create **ungrouped** scenes within or across projects; group names and memberships are never transferred or matched automatically.
- **Minimap:** View → Show minimap or Ctrl/Cmd+Shift+M toggles a small story overview with connections, selected scenes and a green viewport rectangle. Click or drag to center the viewport. Focus the minimap and use arrows to pan, Shift+arrows to pan farther, Enter/Home to fit, or Escape to return to the graph. Graph Alt+arrows pans the viewport with or without the minimap. Visibility is a workspace preference; navigation leaves scene positions, selection and history intact. The map shrinks and gesture guidance collapses in compact windows.
- **Diagnostics:** missing targets, duplicate/reserved IDs, unsupported scene types or visibility values, invalid conditions, unreachable scenes and regions without an ending. Inline conditions are checked in dialogue and responses, including unused branches. Formatting problems and unknown substitutions produce warnings. Cycles with an exit are allowed.
- **Playtest:** F5 from the story start; Shift+F5 from the selected scene. Test variables live, revisit decisions with Back, and export the transcript. Live edits and refreshed text are recorded; Back restores those edits too. Random delivery keeps its selected line during refresh; **Reroll line** explicitly samples again. Events, scripts and shops are **logged**, not executed; your game supplies those integrations.

## Files and recovery

**Save project** stores desktop settings, entry point, variable starting values, scene metadata, graph positions, groups/chapters, and all dialogue data. **Export website / game JSON** writes the original root schema with numeric visibility enums, and moves the chosen start scene to the first position for the website's simulator. Existing web JSON files import directly. Unknown object fields are retained when editing desktop projects. Desktop format version 1 is supported; incompatible format versions report an error while retaining the open story.

Web exports omit desktop groups and templates. They also omit variable starting values and text substitutions; provide those through your game's runtime or the web player's simulation setup.

Before ordinary **Save**, the editor compares exact disk contents against the version opened or last saved. Rewrites with the same timestamp/size, file replacement and deletion open a conflict dialog; Windows also detects same-content replacements by file identity. **Compare** shows local and disk JSON side by side, including groups and unknown fields. **Reload** validates the external file, checks for newer changes and keeps your local version in Undo. Malformed or deleted files leave your story open. **Save a copy** saves local edits under a new filename and switches to that file; existing destinations are preserved. **Cancel**, Escape, closing the dialog, cancelled browsing and failures preserve local work, existing files, recents and recovery. Save as to the current path uses the same conflict protection. Files changed while comparing or choosing Reload require a fresh decision.

**File → Export graph image…** exports every scene and connection as a PNG, including unreachable scenes and scenes hidden by outline filters. It uses the current theme and complete story bounds, including negative positions, tall scenes and looping connections. Choose Standard (1×) or Detailed (2×); the dialog shows the output dimensions. Images are limited to 16,384 pixels per side and 32 megapixels. Oversized stories require **Fit within limits**, which reduces resolution with an explicit notice. If even 0.25× exceeds the limits, move scenes closer together with Arrange or export a script. The image excludes editor overlays, minimap, selection and keyboard focus. Exporting from Graph or Script preserves the current view, selection, positions, pan/zoom and Undo/Redo. Cancel or failure also preserves the editor; a failed write retains an existing export.

**File → Export script…** creates readable Markdown with independent options for conditions/visibility, response actions, scene exit actions and writer's notes. Notes default off. Titles, IDs, speakers, ordered dialogue pages/random candidates, choices and continue destinations remain readable. Scenes appear in project order with the entry first, including unreachable scenes. Blank response targets inherit continue; endings, unset destinations and missing targets are labelled explicitly. Inline condition markers always preserve both branches and substitutions remain literal, even with condition details off. Author-written Markdown and HTML are escaped; supported rich formatting tags become plain text. Action lists preserve response-before-scene ordering without running integrations. Both export option dialogs support keyboard navigation and scrolling in compact layouts.

Writes use a flushed temporary file and atomic replacement. Project saves recheck disk contents immediately before replacement and coordinate writes by editor instances. Recovery copies protect unsaved work every 10 seconds; they do not overwrite the open project. Successful Save, conflict Reload or explicit Discard removes the active recovery file. Welcome offers recovery content that differs from the saved file, including after a newer external rewrite or malformed saved file. New recovery records retain their original saved-file baseline; older recoveries require an explicit conflict decision before saving to the original location. Damaged recovery records are skipped and retained for manual investigation, allowing startup and valid recovery choices to remain available. Other recoveries are available from File → Recovery saves. Canceling project creation, file browsing, Save as, or the unsaved-changes prompt keeps your current work. Invalid imports report the problem without replacing the current story.

Preferences, recent files and recovery copies are stored under:

- Windows: `%LOCALAPPDATA%/DialogueMachine`
- Linux/macOS: the platform's .NET `LocalApplicationData` directory, under `DialogueMachine`

There are no accounts, network services, analytics, or online dependencies at runtime. Dependency downloads happen during build/restore.

## Accessibility and keyboard

Appearance settings offer **Dark, Light and High contrast** themes and **100%, 115%, 130%** interface size. Inputs have accessibility labels; buttons and pickers use Avalonia's native control automation. The scene outline supports Ctrl/Cmd toggles, Shift ranges and Ctrl/Cmd+A for visible scenes. Filtering preserves the graph set, including hidden selections, until you select in the outline again. Tab moves through controls. In the graph, Ctrl/Cmd+Tab (Shift to reverse) moves a dotted focus outline between scenes; Space toggles that scene, Enter selects it alone, Ctrl/Cmd+A selects all scenes, and Escape clears selection. Arrow keys move the selected set by 10 units; Shift moves it by 40. The selected count is announced, and the editor identifies its active scene. Duplicate, delete, rename and play-from-selected commands apply to that active scene.

| Shortcut | Action |
| --- | --- |
| Ctrl/Cmd+N / O / S | New / Open / Save |
| Ctrl/Cmd+Shift+S | Save as |
| Ctrl/Cmd+F | Search the outline |
| Ctrl/Cmd+H / Shift+F | Project-wide find & replace |
| Ctrl/Cmd+Z / Shift+Z | Undo / Redo |
| Ctrl/Cmd+D | Duplicate selected scene |
| Ctrl/Cmd+C / V | Copy selected scenes / paste scenes when graph or outline is focused |
| Ctrl/Cmd+A | Select all graph scenes / all visible outline scenes |
| Ctrl/Cmd+Tab / Shift+Tab | Focus next / previous graph scene |
| Space / Enter in graph | Toggle focused scene / select it alone |
| Arrows / Shift+Arrows in graph | Move selected set / move farther |
| Ctrl/Cmd+0 | Fit story |
| Ctrl/Cmd+Shift+G | Manage groups & chapters |
| Ctrl/Cmd+Shift+M | Show / hide minimap |
| Alt+Arrows / Alt+Shift+Arrows in graph | Pan viewport / pan farther |
| Arrows / Shift+Arrows in minimap | Pan viewport / pan farther |
| Enter or Home / Escape in minimap | Fit story / focus graph |
| F5 / Shift+F5 | Play from start / selected scene |
| F1 | Guide |
| Escape | Cancel a dialog / close playtest / cancel graph gesture or clear selection |

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

The executable test harness exits nonzero on failure. Its 719 checks cover imports, exports, validation, conditions, simulations, atomic saves, recovery, history, actual graph mouse input, editor operations, accessibility labels, and welcome/open/save-as/cancel/reopen workflows. Reliability regressions include damaged recovery at startup, incompatible format imports, hidden malformed conditions, tall-node layout, 10,000-scene ending checks, and actual live-variable/Back/reroll controls. Variable checks cover reference resolution, numeric/text renames, collisions, unused deletion, draft cancellation, one-step undo, searchable insertion and logic suggestions. Search checks cover literal/case/whole-word matching, field/scene scope, stale-preview rejection, duplicate IDs, unknown-field preservation, bounded previews with complete replacement, exact editor navigation, selected/all replacement and one-step undo/redo. Navigation checks cover title/ID destination search, keyboard selection, cancellation, fallback/unset/ending/missing targets, one-step connection undo, incoming/outgoing jumps, combined outline filters, diagnostic/tag refresh, compact layouts and 100/1,000/10,000-scene probes. Graph organization checks cover modifier-click and marquee selection, native outline Shift-arrow ranges, keyboard focus/toggles, zoom-correct relative group movement, held-key repeats, gesture cancellation, capture/focus loss, duplicate-ID history rebinding, scene-set copy/paste, single-step Undo/Redo and all nine theme/interface-size combinations in normal and compact windows. Group/minimap checks cover overlapping memberships, deletion without scene/link loss, duplicate-ID repair, unknown-field preservation, save/load/recovery, empty selection, one-step operations, cached intersecting filters, ungrouped copy/paste within and across projects, viewport click/drag/keyboard navigation, 1,000/10,000-scene minimap render probes, focus, visibility preferences and group/minimap renders at all nine theme/interface sizes in normal and compact windows. Export checks cover all 16 script option combinations, both nested inline branches, literal substitutions, Markdown/HTML escaping, deterministic ordering, all destination states, actual full-story PNG bounds/dimensions, negative/tall/cyclic geometry, allocation limits, view/selection/filter/scale independence, cancellation, disk and overwrite failures, preserved Redo history, and export dialogs at all nine theme/interface sizes in normal and compact windows. Authoring checks cover connected-scene cancellation, bulk link remapping, deep-copy independence, project changes, template persistence, detached destinations, variable-reference safety, keyboard selection, one-step undo/redo and compact layouts. Conflict checks cover exact-byte and same-length/timestamp rewrites, same-byte Windows replacements, deletion, repeated changes during comparison and Reload, malformed/incompatible/oversized content, two editor instances, undoable Reload, save-before-leaving, active recovery preservation, safe copy destinations, write failures and all nine theme/interface sizes in normal and compact windows. File workflow checks use isolated preferences, actual disk writes and an injected file picker; they preserve original website-format files. Headless rendering creates screenshots of welcome, recent/recovery lists, workspace, variable, find/replace, destination, multi-scene copy, template, group and conflict/comparison dialog themes, minimaps, compact/large-text layouts and playtest in `artifacts` without opening windows. The Windows package has also passed native welcome and project startup/render smoke tests, including full-story PNG and readable script exports; `--smoke-test --smoke-output <directory>` runs offscreen, and `--smoke-project <path>` also tests opening a project. These checks do not replace manual screen-reader, OS file-picker or platform testing.

See [ARCHITECTURE.md](ARCHITECTURE.md) for the project boundaries, design decisions, and extension points.

See [ROADMAP.md](ROADMAP.md) for the project assessment, completed reliability work, and proposed next features.
