# Maintenance guide

## Structure

```
Core/                         Platform-independent class library; no UI dependencies
  Models.cs                   Website-compatible schema plus desktop project fields
  ProjectStore.cs             JSON import/export, atomic writes, bounded history
  RecoveryStore.cs            Validated recovery storage and saved-file comparison
  WorkspaceStore.cs           Preferences migration, titled recents and startup decisions
  DialogueEngine.cs           Conditions, validation, safe renaming, layout, simulation
  Templates.cs                Blank and lighthouse example stories
UI/
  MainWindow.axaml             Declarative shell; menus, outline, graph, editor, status
  MainWindow.axaml.cs          Document orchestration, selection and editing commands
  MainWindow.Editor.cs        Content, logic, notes and script views
  MainWindow.Files.cs         OS storage dialogs, file/recovery lifecycle, preferences
  MainWindow.Welcome.cs       New-project, welcome and startup orchestration
  WelcomeView.axaml / .cs     Welcome cards, recent projects and recovery presentation
  ProjectFilePicker.cs        Native storage boundary, injectable for workflow checks
  MainWindow.Variables.cs     Cancel-safe project variable editing
  GraphCanvas.cs              Graph drawing and pointer/keyboard interaction
  PlaytestWindow.cs           Isolated simulation, variables, transcript export
  RichDialogue.cs            Safe rendering of the supported formatting tags
  Ui.cs                      Small common control builders and labelled fields
App.axaml / App.axaml.cs      Shared styling and theme palette
tests/                       Executable core and headless UI regression checks
build.ps1 / build.sh          Self-contained platform publishing
Packaging/*.ps1              Release version checks, native startup checks and Windows ZIP
Packaging/publish-release.sh  Draft/upload/publish lifecycle with immutable published releases
.github/workflows/desktop.yml Windows CI and tag-triggered releases
```

Core is a separate project and can be reused by a future UI, CLI, web frontend, or game integration. UI controls never need to enter the engine. The test harness references the application so the same real controls and engine are exercised together. No MVVM framework, browser bridge, database, or hosting service is needed for this local editor. Dynamic editor forms use explicit callbacks because the schema is small; if the editor grows substantially, move a pane into its own view/view-model while keeping its operations in Core.

## Data compatibility

Retain original PascalCase field names and website enum ordering: Hide=0, Disable=1, Always=2. Desktop JSON accepts both numeric and named enums; website exports deliberately use numeric enums. `JsonExtensionData` keeps unknown fields on root, scene, response, action, requirement and shop objects. Desktop additions live at the project root or in the existing Metadata dictionary.

State IDs are graph identity; titles are editable metadata. Every rename goes through `DialogueEngine.Rename` so entry, choice and continue connections stay consistent. Export starts with the chosen entry because the website starts at index zero. An intentional ending is `[EXIT]`, never an invalid target. Import sanitation is separate from graph validation so damaged graphs can be opened and repaired without losing data.

## Changes, history and persistence

Text and picker changes update the document immediately. Text edits are grouped after 700 ms, on focus changes, and before operations such as selection, undo and save. Graph drags commit once on release. Each history entry is a complete JSON snapshot, bounded to 100 entries; dirty state compares the document with the last successfully saved snapshot. This favors correctness for ordinary-sized stories. For extremely large projects, replace snapshots with reversible commands behind the same history API.

Never replace the current document until an import has parsed successfully and the user has resolved unsaved edits. Save updates the saved snapshot only after the atomic write succeeds. Recovery has its own file and never writes over the project. Restoring keeps the source recovery ID so a later successful Save or explicit Discard removes that active recovery.

The window starts without a document. Welcome is a separate view; project controls and menu commands are enabled only when a project is open. Creating a project uses a local form, then resolves existing edits before replacing the document. The example is explicitly requested and never added to recents until saved. Startup planning is UI-independent: an explicit launch path wins, then recovery presentation, then optional reopening of an existing last file, otherwise Welcome. Preferences migrate the previous recent-file list and keep at most eight titled entries. Inject `IProjectFilePicker` and an isolated data directory to test storage workflows without invoking OS dialogs or touching a user's settings.

## Simulation semantics

Simulation clones the document. Entering a scene does not fire exit actions. Choosing a response verifies its conditions, validates its destination, snapshots the runtime, runs response actions, then scene exit actions, and enters the target. Page advances also snapshot runtime. Back restores scene, pages, variables, transcript and event log. Inventory is nonnegative and arithmetic saturates at integer limits. All transitions are user-driven; graph cycles are allowed. Script and Event actions are recorded rather than executed, keeping a local preview independent from game code.

Inline condition processing resolves innermost tags first, with a bounded pass count. Rich formatting uses typed text runs rather than HTML or script execution. Text substitutions are evaluated after conditions.

## Adding features

- Add a logic type in Models/DialogueEngine first, then add its editor fields, validation and tests. Keep existing exported fields stable.
- Extend template content in Templates and regenerate sample JSON from that source.
- Add export formats in a dedicated Core service when an export becomes more complex than a short formatting function.
- Add UI palette tokens in App; check all three themes and interface sizes. Keep a keyboard path and automation name for every input.
- Keep dependency versions pinned; committed package lock files pin platform-neutral library dependencies. Publishing writes runtime-specific lock files under each project's ignored `obj` directory so cross-publishing does not rewrite the source locks. `RuntimeFrameworkVersion` pins the packaged .NET runtime to 10.0.12; check runtime updates alongside library updates. Update Avalonia packages together, intentionally refresh the source locks, run the test harness and review screenshots, then smoke-test native builds on each OS.

## Release checklist

Build and run regression checks; inspect rendered screens; verify new/open/save-as/cancel/recovery with the native storage dialogs; test Narrator/VoiceOver and the Linux accessibility backend on target systems; package using the supplied scripts; add signing/notarization if distributing publicly. Windows CI restores locked library dependencies, runs regression and native startup checks, then packages the complete app folder with a checksum. A matching version tag publishes those verified assets using a separate job with release permissions. Linux/Mac packages still need native launch checks before distribution.

Current boundaries: Windows is the only locally tested OS; native accessibility tools have not been exercised; game scripts/events/shops are integration hooks; there is no installer, auto-updater, collaborative editing or cloud sync.
