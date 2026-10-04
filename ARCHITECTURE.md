# Maintenance guide

## Structure

```
Core/                         Platform-independent class library; no UI dependencies
  Models.cs                   Website-compatible schema plus desktop project fields
  ProjectStore.cs             JSON import/export, atomic writes, bounded history
  RecoveryStore.cs            Validated recovery storage and saved-file comparison
  WorkspaceStore.cs           Preferences migration, titled recents and startup decisions
  DialogueEngine.cs           Conditions, validation, safe renaming, layout, simulation
  DialogueText.cs             Structural conditional and formatting checks for all text fields
  VariableStore.cs            Discovery, typed references, safe variable rename/delete
  ProjectSearch.cs            Literal authoring search, scoped previews and replacement
  SceneNavigation.cs          Indexed destinations, connections and outline filtering
  AuthoringOperations.cs      Detached scene copies, remapped paste and reusable templates
  GraphGeometry.cs            Node dimensions shared by layout and drawing
  SceneGroups.cs              Desktop group membership and safe identity rebinding
  Templates.cs                Blank and lighthouse example stories
UI/
  MainWindow.axaml             Declarative shell; menus, outline, graph, editor, status
  MainWindow.axaml.cs          Document orchestration, selection and editing commands
  MainWindow.Editor.cs        Content, logic, notes and script views
  MainWindow.Files.cs         OS storage dialogs, file/recovery lifecycle, preferences
  MainWindow.Welcome.cs       New-project, welcome and startup orchestration
  WelcomeView.axaml / .cs     Welcome cards, recent projects and recovery presentation
  ProjectFilePicker.cs        Native storage boundary, injectable for workflow checks
  MainWindow.Variables.cs     Searchable variable drafts, reference lists and insertion picker
  MainWindow.Search.cs        Find/replace dialog, match previews and editor navigation
  MainWindow.Navigation.cs    Searchable destination picker, filters and connection jumps
  MainWindow.Authoring.cs     Connected scenes, scene clipboard and template dialogs
  GraphCanvas.cs              Graph selection sets, drawing and pointer/keyboard gestures
  GraphMinimap.cs             Viewport overview and pointer/keyboard navigation
  MainWindow.Groups.cs        Group manager and compact graph overlays
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

Only desktop format version 1 is currently supported. Files without a version use that default for website compatibility; explicitly unsupported versions are rejected before document replacement. Reserved scene IDs, unsupported scene types and invalid numeric visibility values remain openable for repair, but validation blocks playtest/export. Repairing an imported `[EXIT]` ID updates the entry without rewriting intentional ending targets.

## Changes, history and persistence

Text and picker changes update the document immediately. Text edits are grouped after 700 ms, on focus changes, and before operations such as selection, undo and save. Graph drags commit once on release. Each history entry is a complete JSON snapshot, bounded to 100 entries; dirty state compares the document with the last successfully saved snapshot. This favors correctness for ordinary-sized stories. For extremely large projects, replace snapshots with reversible commands behind the same history API.

Graph selection is transient UI state: a scene set plus one active scene for the editor. The outline shares that set; filtering keeps hidden selected scenes until the user changes the outline selection. Modifier-click toggles nodes, Shift-drag adds intersecting nodes, and keyboard focus can travel independently of selection. Selection never changes project JSON or history. History replacement rebinds the set by ID and duplicate-ID occurrence, preserving distinct repairable imported scenes. New/open/Home clears the previous document's selection.

Graph movement snapshots the selected positions at gesture start and applies the same world-space delta to every member. Pending typing commits before a gesture. Pointer release, capture loss, keyboard release after repeats, or focus loss finishes the gesture once; Escape restores its starting positions without a history entry. Other document commands finish an active gesture before committing. Connection dragging operates on one output, even with a selected set. Rendering indexes destination IDs once and draws selected outgoing/internal and incoming connections after ordinary links, with thicker strokes and separate theme colors; continue links retain their dashed style.

Never replace the current document until an import has parsed successfully and the user has resolved unsaved edits. Save updates the saved snapshot only after the atomic write succeeds. Recovery has its own file and never writes over the project. Restoring keeps the source recovery ID so a later successful Save or explicit Discard removes that active recovery.

Recovery discovery validates each record separately and skips both JSON syntax failures and semantic `InvalidDataException` failures. Keep damaged files for investigation and continue offering valid records.

Variable management clones the full project into a draft. Apply replaces it through `Mutate`, preserving scene selection and recording one undo entry; Cancel and Escape discard the draft. `VariableStore` counts runtime references in typed actions/requirements, dialogue and response substitutions, and inline conditions. Renames preserve the numeric type and starting value, reject collisions before mutation, and rewrite only actual references. Unprefixed conditions resolve direct known keys, then inventory, then affinity; renames qualify those aliases to preserve the binding. Text substitutions take precedence over numeric placeholders with the same legacy ID. Notes, ordinary prose, Event/Script IDs and unknown extension fields remain untouched. Deletion is limited to unused definitions. Discovery includes inline-only numeric variables, but excludes numeric-looking prose.

Logic fields suggest IDs of the selected numeric type while allowing new IDs. The Content picker inserts a known substitution at the caret. Variable dialogs pass the interface scale to the shared dialog builder, which scales both content and window dimensions; other callers retain its default scale.

Project search is a Core service over dialogue, response labels, displayed titles and writer's notes. It uses literal ordinal matching, optional case/whole-word rules, field selection and an optional scene index. Scene indices distinguish repairable duplicate IDs. Preview results retain original text and offsets, with at most 200 materialized matches but a complete count. Replace all scans the full scope once, applies nonoverlapping matches without re-searching inserted text, and only writes changed fields. Single replacement rejects a stale preview before writing. A fallback title is replaced by adding title metadata, preserving the scene ID. IDs, connections, logic, other metadata, starting values and extension data are untouched.

The find/replace window commits pending typing before searching. Replacements apply immediately through `Mutate`, each with one history entry; Close/Escape discards only unapplied previews. The modal owner prevents concurrent editor changes. Go to match closes the window, clears a hiding outline filter, opens Content or Notes, and focuses/selects the matching text. Dialogs honor interface scale; the bounded match list keeps large result sets out of the visual tree.

Scene navigation snapshots titles, IDs, speakers, comma-separated tags, search text and diagnostic flags when diagnostics refresh after a commit or document replacement. Incoming/outgoing indices use the same null/blank choice fallback as the runtime. Filtering and destination search use this snapshot without validation, serialization or document mutation on each keystroke. Destination summaries and jumps resolve IDs through a dictionary; opening the picker builds its options once and materializes at most 200 results with a complete count. Missing current targets stay visible, while unset/fallback and `[EXIT]` remain explicit choices. Choosing a destination applies through `Mutate` with one undo entry; typing, Cancel and Escape do not change connections.

Outline text, speaker, tag and diagnostic filters intersect. Filter options refresh after edits and undo/redo; opening another project clears them. A scene jump clears filters only when they hide its destination. Notes lists indexed outgoing links and deduplicated incoming sources, including inherited choice targets; endings, unset and missing links have labels instead of jump buttons. The outline header scrolls in compact windows while retaining a scene list and a visible clear-filter command.

Authoring operations live in Core. `SceneBundle` stores an immutable JSON snapshot of a selected scene set. Paste validates capacity and positions before mutation, assigns unique scene IDs and fresh choice IDs, remaps internal choice/continue/self/cycle links, and preserves relative positions. External, missing, ending and blank targets remain explicit; existing scenes, entry points and unknown fields stay intact. The editor's scene clipboard lasts for the window's app session, including project changes; it is separate from the OS text clipboard. Duplication uses the same service. Creating a connected scene records the new scene and chosen response destination together through `Mutate`.

Copy selected scenes and graph/outline Ctrl/Cmd+C snapshot that transient set through the existing Core service. The copy dialog starts with the same selection and can choose another bundle without changing graph selection. Paste selects all inserted scenes and records the complete insertion in one history entry. Ctrl/Cmd+C/V are handled as scene commands only while the graph or outline has keyboard focus; text inputs retain native clipboard behavior.

Scene and choice templates are additive desktop root collections in format version 1, saved with the project and included in recovery/history. Website exports omit these collections. A scene template captures content, metadata, shop data, delivery and logic; self-links and endings survive, while external destinations become unset. Choice templates capture responses, visibility, conditions and actions, and insert with fresh identity and a continue fallback. Template insertion and deletion each form one history entry; previewing, searching and closing do not mutate the document. Variable reference counts, deletion guards and renames include template content. Alias resolution considers each template in its prospective insertion context, while inactive template variables never enter runtime discovery. Templates and copied scenes retain variable IDs but do not transfer root starting values or text substitutions to another project.

The window starts without a document. Welcome is a separate view; project controls and menu commands are enabled only when a project is open. Creating a project uses a local form, then resolves existing edits before replacing the document. The example is explicitly requested and never added to recents until saved. Startup planning is UI-independent: an explicit launch path wins, then recovery presentation, then optional reopening of an existing last file, otherwise Welcome. Preferences migrate the previous recent-file list and keep at most eight titled entries. Inject `IProjectFilePicker` and an isolated data directory to test storage workflows without invoking OS dialogs or touching a user's settings.

## Scene groups and minimap

Scene groups/chapters are an additive desktop root collection (`SceneGroups`) in format version 1. A group has a stable ID, a display name and member references by scene ID plus duplicate-ID occurrence. Memberships may overlap. Core owns create/rename/delete/assign/remove and resolves sets in project order; each UI mutation uses the existing snapshot history once. Names must be non-empty and unique ignoring case for authoring operations. Group/member extension data round-trips. Import rejects malformed group identities/references, normalizes null collections and removes duplicate/stale memberships so they cannot bind to a future inserted scene. Group deletion never changes scene data or connections. Scene rename and deletion capture memberships by object identity before rebinding shifted occurrences, preserving repairable duplicate-ID scenes.

Navigation snapshots include group IDs, so group/ungrouped outline filters intersect existing filters without document work on each keystroke. The group filter follows a rename by ID and falls back to Any group when its group disappears. Group selection retains a member active scene or activates the first member in project order; empty groups clear selection. History preserves an explicitly empty selection. Group operations leave the editor's scene set intact. Scene bundles, duplication and templates contain scene data only: all new copies are ungrouped within and across projects. Groups never enter website exports or scene Metadata.

The minimap is a UI control using shared node geometry and the graph's world viewport. Graph redraw notifications refresh it for selection, movement, zoom, pan and replacement. It shows connections, selected nodes and the viewport with theme resources. Its aspect-preserving transform fits the union of story bounds and viewport; pointer navigation freezes that transform until release/capture loss. Click/drag centers the graph, arrow keys pan, Shift pans farther, Enter/Home fits and Escape focuses the graph. Alt+arrows offers graph viewport panning when the map is hidden. The map resizes and extra gesture guidance hides in compact windows. `ShowMinimap` is a workspace preference, defaulting to false; navigation/visibility never change project history. Script view hides it while retaining the preference.

## Simulation semantics

Simulation clones the document. Entering a scene does not fire exit actions. Choosing a response verifies its conditions, validates its destination, snapshots the runtime, runs response actions, then scene exit actions, and enters the target. Page advances also snapshot runtime. Back restores scene, pages, variables, transcript and event log. Inventory is nonnegative and arithmetic saturates at integer limits. All transitions are user-driven; graph cycles are allowed. Script and Event actions are recorded rather than executed, keeping a local preview independent from game code.

Inline condition processing resolves innermost tags first, with a bounded pass count. Rich formatting uses typed text runs rather than HTML or script execution. Text substitutions are evaluated after conditions.

Structural validation checks dialogue and response labels without evaluating their branches. It rejects malformed conditions and nesting beyond the runtime's 64-pass limit; formatting and unknown-substitution diagnostics are advisory. Ending reachability traverses reverse edges from `[EXIT]`, avoiding repeated whole-graph scans.

Use `Simulation.SetVariable` for live test edits. It snapshots runtime, records the value change and any refreshed page text, and lets Back restore everything together. Random delivery retains the chosen candidate index for a scene visit, clamped temporarily if resolved text has fewer lines. Explicit reroll snapshots and selects again; Back also restores that selection. Automatic layout uses the canvas node height and a vertical gap, with a minimum 300-unit row pitch for smaller nodes.

## Adding features

- Add a logic type in Models/DialogueEngine first, then add its editor fields, validation and tests. Keep existing exported fields stable.
- Extend template content in Templates and regenerate sample JSON from that source.
- Add export formats in a dedicated Core service when an export becomes more complex than a short formatting function.
- Add UI palette tokens in App; check all three themes and interface sizes. Keep a keyboard path and automation name for every input.
- Keep dependency versions pinned; committed package lock files pin platform-neutral library dependencies. Publishing writes runtime-specific lock files under each project's ignored `obj` directory so cross-publishing does not rewrite the source locks. `RuntimeFrameworkVersion` pins the packaged .NET runtime to 10.0.12; check runtime updates alongside library updates. Update Avalonia packages together, intentionally refresh the source locks, run the test harness and review screenshots, then smoke-test native builds on each OS.

## Release checklist

Build and run regression checks; inspect rendered screens; verify new/open/save-as/cancel/recovery with the native storage dialogs; test Narrator/VoiceOver and the Linux accessibility backend on target systems; package using the supplied scripts; add signing/notarization if distributing publicly. Windows CI restores locked library dependencies, runs regression and native startup checks, then packages the complete app folder with a checksum. A matching version tag publishes those verified assets using a separate job with release permissions. Linux/Mac packages still need native launch checks before distribution.

Current boundaries: Windows is the only locally tested OS; native accessibility tools have not been exercised; game scripts/events/shops are integration hooks; there is no installer, auto-updater, collaborative editing or cloud sync.
