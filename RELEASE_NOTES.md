Dialogue Machine is an offline desktop workspace for branching dialogue.

## 1.5.1

### Project export configuration & JSON Schema

- **Project-owned engine export folder**: The engine export folder is now saved directly in the project file as `EngineExportFolder` (relative to the project), allowing projects to move across machines and directories without breaking export paths. Older projects configured via per-machine preferences are seamlessly migrated upon save, "Save as" rebases the relative path, and missing destination folders are reported explicitly instead of being silently recreated.
- **Project JSON Schema & `schema` CLI command**: Added `Schemas/dialogue-project.schema.json` generated directly from the core model with property descriptions and validation constraints. A new `schema` command on the CLI (`DialogueMachine.exe schema`) enables external scripts, tools, and agents to author and validate project files.

### Runtime runners & engine integration

- **Persistent escalate visit counts**: Single-node escalate delivery (`"One line per visit (escalate)"`) now tracks visit counts as numeric `visit:<sceneId>` entries in the variable store. Scene reloads, state saves, backtracking, and external variables providers preserve escalation progress without restarting from line 1. Supported across Desktop Simulation and the Unity, Godot, and Unreal runners (`escalate_visits_save_restore` conformance fixture).
- **Unreal Engine 5.8 build fix**: Added missing `SlateColorBrush.h` include to `DialogueMachineWidget.cpp`, fixing game target compilation failure C3861 when packaging games with Unreal Engine 5.8.
- **Engine packages**: Pre-packaged plugins for Unreal (`DialogueMachine-Unreal-1.5.1.zip`) and Godot (`DialogueMachine-Godot-1.5.1.zip`), plus the Unity UPM package pinned to `#v1.5.1`.

## 1.5.0

### Dialogue logic, barks & character features

- **OR conditions**: choices can require `ANY (OR)` condition satisfaction in addition to `ALL (AND)`, with full runtime support across Unity, Godot and Unreal.
- **Flags and Set/Toggle actions**: first-class boolean flags with Set/Toggle actions and conditions in the Logic tab, evaluated natively across the desktop editor and all engine runners. Arbitrary integer `Set` actions are also supported.
- **SetText action & EvaluateBark API**: added `SetText` action for string variable updates and `EvaluateBark(sceneId)` API across all three engine runners for non-interrupting ambient dialogue and barks.
- **Per-page speaker overrides & portraits**: override speakers and expression portraits per page with `@Speaker [portrait]:` syntax. Includes expression/portrait autocomplete and playtest preview.
- **Single-node prop escalation**: `"One line per visit (escalate)"` delivery sequentially cycles lines across visits and clamps at the final line without needing multi-node chains.
- **Secondary entry points**: mark scenes as `"Entry point (triggered by game)"` for game-triggered dialogue, integrated into reachability validation and auto-layout.
- **Character roster & swatches**: manage characters with names and custom hex colors in Variables & characters, offering autocomplete and visual color accents on graph nodes.

### Engine integration & contract modernization

- **Unified story export contract**: modernized the engine companion format to `dialogue-machine.story.v1` (with `dialogue-machine.website.v1` retained for backward compatibility).
- **Consolidated export workflow**: retired the legacy lossy website export in favor of the full-fidelity game engine runtime companion JSON.
- **Unity package improvements**: deterministic `.meta` files committed for stable UPM links, and removed `DEVELOPMENT_BUILD` preprocessor usage for Unity 6 compatibility.
- **Updated engine plugins**: pre-packaged plugins for Unreal (`DialogueMachine-Unreal-1.5.0.zip`) and Godot (`DialogueMachine-Godot-1.5.0.zip`), plus the Unity UPM package pinned to `#v1.5.0`.

### Editor ergonomics & performance

- **Zoomed-out graph readability**: level-of-detail rendering simplifies graph nodes when zoomed out below 55% for smooth overview navigation.
- **Collapsible choice cards**: scenes with more than 4 choices display collapsed cards with direct `If ›` jumps to choice conditions.
- **Playtest & writing refinements**: keyboard navigation in playtest (Enter, 1-9, Backspace), auto-focus for newly created choices, stable editor scroll position during edits, and visible choice conditions in Script view.
- **Modular view architecture**: decomposed monolithic panes into dedicated views (`SceneEditorView`, `ExportView`, `VariablesView`, `SearchView`, `ScenariosView`).

## 1.4.0

### Engine integration

Dialogue Machine can now drive Unity, Unreal and Godot games. One version number covers the app and all three plugins.

- **Engine export folder**: choose a folder per project and every successful Save writes `<project>.runtime.json` there.
- **Plugins**: a Unity package (install from the git URL pinned to the release tag), an Unreal plugin (`DialogueMachine-Unreal-1.4.0.zip`) and a Godot addon (`DialogueMachine-Godot-1.4.0.zip`, unzip into your project). Each imports `*.runtime.json`, runs the story with the same rules as the desktop playtest and includes a default dialogue view.
- **Live link**: turn it on in the app to hot-reload stories in the editor, play from a scene and follow a running game on the graph.

Known behaviours:

- **Play in engine** only reaches a game that is already running in Godot; it does not start one. Unity can enter Play Mode for you.
- The Godot view shows any bracket that is not `[b]`, `[i]`, `[u]` or `[colour=X]` as text. Other BBCode tags such as `[wave]`, or a raw `[color]`, appear literally.

## 1.3.0

Dialogue Machine 1.3.0 brings together all major features and performance improvements in a single release:

### Characters, Writing & Templates

- **Speaker roster & autocomplete**: Project-wide speaker discovery with autocomplete in the scene content editor.
- **Project-wide speaker rename**: Variables & characters dialog includes a Scene speakers roster with reference inspection showing where each speaker appears, and renames speakers across all scenes and templates in a single undo step while rebinding playtest scenario signatures.
- **Find & replace**: Project-wide search and replace across dialogue, responses, titles, and writer's notes with Ctrl/Cmd+H.
- **Scene & choice templates**: Save scenes and choices as templates and reuse them anywhere in the project.
- **Centralized variables & characters**: Search variables, see where each is referenced, rename everywhere, and delete unused ones with draft staging and single-step undo.
- **Safe condition parsing**: Inline variable conditions parse strictly so ordinary dialogue prose never creates stray variable references.

### Translation & Localization

- **CSV translation table export**: Export project strings to standard RFC 4180 CSV tables (`Key,SceneId,ChoiceId,Speaker,Field,SourceText,TranslatedText`) with preserved newlines and quoting.
- **Validated CSV import**: Import translated CSV files with structural validation checking conditional tags and syntax on all edited lines before applying changes.
- **Atomic safety**: Invalid or broken translations are rejected with actionable diagnostics naming the offending row without modifying the open project.
- **Single-step undo**: Applied translation imports undo completely in a single step.

### Organizing & Graph Navigation

- **Destination search & jump**: Pick link destinations by searching scene titles or IDs, and jump directly along connections.
- **Outline filtering**: Filter the outline by speaker, tag, or diagnostic issue, combined with text search.
- **Multi-scene selection**: Select several scenes with Ctrl/Cmd-click, Shift-click, Shift-drag, or Ctrl/Cmd+A, then move them together with one Undo step.
- **Copy & paste scenes**: Copy and paste selected scene clusters with Ctrl/Cmd+C / Ctrl/Cmd+V; internal links between pasted scenes are remapped automatically.
- **Groups & chapters**: Organize scenes into named groups or chapters, and filter the outline by group.
- **Minimap**: Toggle an interactive minimap with Ctrl/Cmd+Shift+M, and pan the graph with Alt+arrows.
- **Connection emphasis**: Selected scenes' connections are highlighted and colored so you can follow paths effortlessly.

### Testing, Simulation & Exports

- **Virtualized script view**: Converted the readable Script view to a virtualized `ListBox` with lightweight row projection, making switching instantaneous on 1,000–10,000 scene stories and releasing memory when returning to the graph.
- **Runtime companion export**: File → Export runtime companion… creates a self-contained runtime payload (`dialogue-machine.website.v1`) with seed and maximum operation limits for website and headless integrations.
- **Saved test scenarios**: Save named test scenarios with starting values, seed, path recordings, and expected ending/event assertions.
- **Scenario coverage & dead-end analysis**: Replay scenarios and view visited scenes and choices highlighted directly on the graph, and detect condition combinations that leave the player stranded.
- **Image & Markdown exports**: Export full-story PNG graphics with custom scaling, or readable Markdown scripts with optional logic and writer's notes.

### Safety & Platform Distribution

- **Conflict-safe saving**: Warns before Save overwrites changes made outside the app, offering compare, reload, save a copy, or cancel.
- **Crash & unsaved recovery**: Automatically creates recovery baselines every 10 seconds and restores unsaved work on launch.
- **Portable Windows release**: Download the self-contained win-x64 ZIP, extract all files, and run `DialogueMachine.exe`. The .NET runtime is bundled; no install or browser required.
- **Cross-platform continuous verification**: Headless regression suites and self-contained builds continuously verified in CI for Windows (x64), Linux (x64), and macOS (ARM64).

## 1.2.0

### Writing

- Find & replace text across the whole project with Ctrl/Cmd+H.
- Save scenes and choices as templates and reuse them anywhere in the project.
- Manage variables in one place: search them, see where each is used, rename it everywhere, and delete unused ones.
- Variable edits stay a draft until you apply them, and one Undo reverts them all.
- Logic fields suggest variable IDs, and the Variable… picker inserts a text substitution at the cursor.
- Ordinary prose no longer creates stray variables.

### Organizing

- Pick a link's destination by searching scene titles or IDs, and jump along connections.
- Filter the outline by speaker, tag or problem, combined with text search.
- Select several scenes with Ctrl/Cmd-click, Shift-click, Shift-drag or Ctrl/Cmd+A, then move them together with one Undo step.
- Copy and paste selected scenes with Ctrl/Cmd+C; links between pasted scenes are kept.
- Sort scenes into named groups or chapters, and filter the outline by group.
- Toggle a minimap with Ctrl/Cmd+Shift+M, and pan the graph with Alt+arrows.
- Selected scenes' connections are thicker and colored so you can follow them.

### Testing & exporting

- Save named test scenarios with a starting scene, values and seed, record a path through the story, and check its outcome.
- Run all scenarios at once and see which scenes and choices they visited on the graph.
- Find scenes where a scenario's starting values leave the player stuck.
- Export the whole story graph as a PNG image.
- Export a readable Markdown script, with optional conditions, actions and writer's notes.

### Safety

- Dialogue Machine warns you before Save overwrites changes made to the file outside the app, and lets you compare, reload, save a copy or cancel.
- Recovery still finds your unsaved work when the file was changed elsewhere.
- Large stories stay responsive while editing and navigating.

## 1.1.1

Version 1.1.1 improves recovery, validation, graph arrangement and playtesting:

- Damaged recovery records no longer block startup or hide valid unsaved stories. Damaged files remain available for manual investigation.
- Reserved scene IDs, unsupported scene types and invalid choice visibility values are diagnosed. Unsupported desktop format versions are rejected while retaining the open story.
- Dialogue and response labels both receive conditional-tag checks, including unused branches. Formatting problems and unknown substitutions produce warnings.
- Arrange leaves room for scenes with many choices. Ending-path validation uses a reverse traversal for larger stories.
- Live test-variable edits record their changes and refreshed text in the transcript. Back restores the edit, preview and transcript together. Random delivery retains its selected line during refresh; Reroll line explicitly samples again.

Website dialogue JSON imports directly. Desktop saves preserve story data and metadata; website/game exports retain the original schema. Save as, cancellation and invalid imports preserve the current document.

Download the **win-x64 ZIP**, extract all files together, and run **DialogueMachine.exe**. The .NET runtime is included. This is a portable Windows app; no SDK or browser is required.

The accompanying `.sha256` file contains the download checksum. The release workflow runs regression checks and native Windows startup checks before publishing this package.
