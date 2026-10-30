# Architecture

## Layout

```
Core/                       Class library with no UI dependency
  Models.cs                 Website schema plus desktop-only project fields
  ProjectStore.cs           JSON import/export, atomic writes, undo history snapshots
  ProjectFiles.cs           Disk fingerprints and conflict-checked saves/reloads
  RecoveryStore.cs          Unsaved-work recovery files
  WorkspaceStore.cs         Preferences, recent files, startup decisions
  DialogueEngine.cs         Conditions, inline text, validation, rename, auto-layout
  DialogueText.cs           Inline condition/formatting checks
  Simulation.cs             Playtest runtime (seeded random lines, Back, trace)
  VariableStore.cs          Variable discovery, references, rename/delete
  ProjectSearch.cs          Find & replace
  SceneNavigation.cs        Destination search, connection index, outline filters
  AuthoringOperations.cs    Scene copy/paste and templates
  SceneGroups.cs            Groups/chapters
  ScriptExport.cs           Markdown script export
  GraphGeometry.cs          Node sizes shared by layout and drawing
  PlaytestScenarios.cs      Saved test scenario definitions
  ScenarioReplay.cs         Scenario replay, assertions, coverage, dead-end analysis
  StoryTestCommand.cs       CLI: validate / replay / conformance
  Templates.cs              Blank and example stories
  LiveLink/                 Localhost WebSocket bridge server
UI/                         Avalonia views; MainWindow is split by area (Files, Editor, Search, ...)
  GraphCanvas.cs            Graph drawing and mouse/keyboard interaction
  GraphMinimap.cs           Minimap
  GraphImageExport.cs       Full-story PNG export
  PlaytestWindow.cs         Playtest player
Runtime/                    Project for the shared C# rules and runner (netstandard2.1, C# 9); sources live in the Unity package
Engines/                    Engine packages (Unity, Unreal, Godot), conformance fixtures and contract
CLI/                        Console entry point; references Core only
tests/                      Executable harness: Core checks plus headless UI checks and screenshots
Packaging/, build.*         Self-contained publishing, Windows smoke test, release ZIP
```

Core never references Avalonia. The UI uses code-built controls with explicit callbacks; no MVVM framework. When `MainWindow` code gets in the way, move a pane into its own view and keep its operations in Core.

## Rules that keep data safe

- **Website compatibility.** Keep the website's PascalCase field names and numeric visibility order (Hide=0, Disable=1, Always=2). Website exports use numeric enums and put the entry scene first, since the website starts at index 0. Desktop-only data (groups, templates, scenarios, starting values) lives at the project root or in `Metadata` and is left out of website exports.
- **Unknown fields round-trip.** `JsonExtensionData` keeps unknown fields on every model object. Don't drop them.
- **Format version.** Only desktop format 1 exists. Files without a version are treated as format 1; other versions are rejected before replacing the open document. New desktop features should be additive fields so no migration is needed.
- **IDs are identity.** Scene IDs are graph links; titles are just metadata. Rename through `DialogueEngine.Rename` so every link follows. `[EXIT]` means "end conversation".
- **Open damaged files, block bad output.** Import only rejects files it cannot represent. Graph problems (missing targets, reserved IDs, bad conditions) show as diagnostics and block playtest/export until fixed.
- **Never lose the user's work.** Parse an import completely before replacing the document. Writes go to a temp file followed by an atomic replace. Save checks that the file on disk is still the version that was opened and offers compare/reload/copy if not. Recovery files are separate and never overwrite the project.

## Editing and history

- All document changes go through `Mutate`, which records one undo entry, so a bulk operation is a single Undo step.
- History stores complete JSON snapshots (up to 100; older ones Brotli-compressed). Text typing is grouped into one entry after 700 ms or on focus change; a graph drag commits once on release.
- Selection, filters, pan/zoom and minimap visibility are UI state. They never enter the project or the history.

## Playtest semantics

Choosing a response checks its conditions, runs the response's actions, then the scene's exit actions, then enters the destination. Inventory never goes below zero and arithmetic saturates. Inline conditions resolve innermost-first; substitutions apply afterwards. Random lines use a seeded SplitMix64 stream, so the same seed and choices reproduce the same run, and Back restores it. Event/Script/Shop hooks are only logged; the website or game implements them.

Saved scenarios reference scenes by ID and responses by GUID. Each recorded step stores a fingerprint, so editing a response's text, logic or destination flags the recording as stale. Replay and dead-end analysis are bounded and never claim to check every path of a cyclic story. CLI exit codes: 0 pass, 1 validation errors, 2 bad input, 3 replay/assertion failure, 4 limit reached, 5 cancelled. See [STORY_TESTING.md](STORY_TESTING.md).

## Adding things

- New logic: add it to `Models`/`DialogueEngine` first, then validation, editor fields and checks. Exported fields must stay website-compatible.
- New UI: add colours as palette tokens in `App.axaml`, check all three themes and interface sizes, and give every input a keyboard path and an automation name.
- Dependencies: versions are pinned and `packages.lock.json` files are committed; CI restores in locked mode. Update the Avalonia packages together. `RuntimeFrameworkVersion` pins the bundled .NET runtime.
- Tests: add checks to the harness under `tests/`. Prefer Core checks; use the headless UI only for real interaction or layout.

## Limits

Windows is the only platform tested natively. Screen readers haven't been tested. About 1,000 scenes is comfortable for everyday editing, and imports are capped at 10,000. There is no installer, auto-update or cloud sync.
