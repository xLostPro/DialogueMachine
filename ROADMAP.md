# Roadmap

**Goal:** an offline editor where writers write, connect, check and playtest branching dialogue, then export it as JSON for game engines (Unity, Unreal, Godot). Every task should make that loop better or simpler. Don't add infrastructure for its own sake.

## Now

1. **Shrink the hot spots.** Simplify code when you change it, not as a big rewrite.
   - Scene editor moved to `SceneEditorView`, Find & replace to `SearchView`, Variables to `VariablesView`, export dialogs to `ExportView`, Scenarios to `ScenariosView`. Still in `MainWindow`: Playtest launch. Move each into its own view as it's touched.

## Later, only when a real story needs it

- Mac/Linux releases: the builds exist but need a native smoke test first.
- Localization or voice references, once the website/game side is defined.
- Portraits in the default dialogue views: Unity exposes `Portrait`, but the Godot and Unreal runners don't yet, and the views need a portrait-key → image mapping. A custom layout already has room for one.
- Unity `UIDocument` → Panel Renderer for `DefaultDialogueView`, once the oldest supported Unity (2022.3) has it.

## Out of scope

Accounts, cloud sync, collaboration, and auto-update. The game engine JSON export is the integration point.

## Done

- **Default dialogue views take game input and restyle without code.** Unity `DefaultDialogueView`, Godot `DialogueView` and Unreal `UDialogueMachineWidget` share `Submit` / `MoveSelection` / `SelectChoice` / `SkipTypewriter`, a highlight they track themselves (skips disabled choices, wraps), and a guard so the press that shows a line doesn't skip it. Each can read input itself (Unity Input System actions, Godot `ui_*` actions, Unreal Enhanced Input or focus), types with rich text kept, and restyles natively (Unity USS `dm-*` classes + UXML layout, Godot Theme type variations + `layout_scene`, Unreal Style properties + `BindWidgetOptional` Widget Blueprint layout). Tests: Unity PlayMode, Godot `run_view_tests.gd` (CI), Unreal `DialogueMachine.Widget`.
- **Unity shared & saveable variables.** `DialogueRunnerBehaviour` has a **Share Variables** toggle that uses one `SharedVariables` store across runners and scene loads (reset when play mode starts), and `DictionaryVariables.ToJson()` / `LoadJson()` write and read the `SaveState` variable shape for game save files.
- **Text-variable conditions.** `[if calling == "Plumber"]` / `[if calling != ""]` inline, and a `Text` choice requirement (`==`/`!=`, missing counts as `""`) in the Logic tab, Core, Unity, Godot and Unreal (`text_conditions` conformance fixture). Renaming a text variable rewrites both.
- **`{a:key}` article placeholder.** `{a:calling}` writes "a" or "an" before a text variable's value (vowel start → "an"), in Core/Unity, Godot and Unreal (`article_helper` conformance fixture, not yet run in Godot or Unreal).
- **Project-owned engine export folder & project JSON Schema.** The engine export folder is stored in the project (`EngineExportFolder`, relative to the project file) instead of per-machine preferences; a folder set by an older version moves into the project on the next save. Save as rebases it, and a missing folder is reported instead of silently recreated. `Schemas/dialogue-project.schema.json` (generated from the model, `schema` CLI command) lets scripts and agents write projects, checked with `validate`.
- **Escalate visit counts persist.** Escalate scenes count visits in the variable store as `visit:<sceneId>`, so saves, Back and game-owned stores keep them across runner restarts. Restoring into an escalate scene shows the saved line. Escalate delivery and `visit:` keys are documented in `Engines/CONTRACT.md` (`escalate_visits_save_restore` conformance fixture).
- **Single-node prop escalation & per-page `@Speaker [portrait]:` overrides.** Added `"One line per visit (escalate)"` delivery (`SayItAll = false`, `Metadata["delivery"] = "escalate"`) that advances sequentially per visit and clamps at the final line, plus `@Speaker [portrait]:` per-page speaker/portrait overrides in `DialogueRules`, `Simulation`, `PlaytestWindow`, and the Unity, Godot, and Unreal runners (`set_text_escalate_and_page_speakers` conformance fixture).
- **`SetText` action & `EvaluateBark(string sceneId)` runner API.** Added `Type = "SetText"` (`TargetId = <text_key>`, `Parameter = <string_value>`) across `DialogueEngine`, `VariableStore`, `Simulation`, `SceneEditorView`, and all three engine runners, plus `EvaluateBark(string sceneId)` on `DialogueRunner` and `DialogueRunnerBehaviour` for non-interrupting ambient barks.
- **Portrait/expression field, secondary entry points, and integer `Set` actions.** `SceneEditorView` now includes an autocompleted `"Expression / portrait"` field (`Metadata["portrait"]`, also shown in `PlaytestWindow`), an `"Entry point (triggered by game)"` toggle (`Metadata["entry"] = "true"`) that seeds `DialogueEngine.Validate` and `AutoLayout` reachability, and `"Set value"` support for arbitrary integer `Set` actions alongside 0/1 flags.
- **Unity UPM package `.meta` files & Unity 6.6 `UAC0009` fix.** Committed deterministic `.meta` files for `Engines/Unity/com.xlostpro.dialoguemachine` so `file:` UPM links stay read-only on disk and keep stable script GUIDs across projects; replaced deprecated `#if DEVELOPMENT_BUILD` in `DialogueRunnerBehaviour.cs` with `#if DEBUG` + `Debug.isDebugBuild`.
- **Retired website lossy export & modernized engine contract.** The separate website export and its degradation workarounds (`SplitAny` splitting OR choices and stripping Set/Toggle actions) are removed. The game engine runtime companion is now the sole story export format, modernized to contract `dialogue-machine.story.v1` (with `dialogue-machine.website.v1` retained for backwards compatibility). Legacy website JSON files remain importable.
- **OR conditions.** A choice has `RequirementMatch` (All by default, Any), switched in the Logic tab as "ALL (AND)" / "ANY (OR)". It is stored only when Any. Story exports preserve `RequirementMatch: Any`. The Unity, Godot and Unreal runners honour it (new `or_conditions` conformance fixture, not yet run in Godot or Unreal).
- **Flags and Set/Toggle actions.** A flag is an ordinary 0/1 `aff:` variable; `Set` (Amount 0/1) and `Toggle` actions write it, and flag conditions are plain `Affinity ==/!= 0/1` requirements, so the Logic tab shows them as "flag is set / not set". Story exports preserve Set and Toggle actions, and the Unity, Godot and Unreal runners implement them (new `flags_set_toggle` conformance fixture, not yet run in Godot or Unreal).
- **Character roster.** `Characters` (name + optional `#RRGGBB` swatch) is desktop-only project data, managed in Variables & characters. Roster names join speaker autocomplete, speaker rename carries the entry, and graph nodes show the colour as an accent. Scenes and website exports still carry the plain speaker string.
- **Zoomed-out graph readability.** Below 55% zoom nodes draw only a large speaker, title, entry and end marker plus ports.
- **1.1.1:** recovery, validation, arrangement and playtest reliability.
- **1.2.0:** variables manager, find & replace, destination search, templates and scene copy/paste, multi-select, groups, minimap, PNG/Markdown export, conflict-safe saving, large-story performance and saved test scenarios.
- **1.3.0:** runtime companion export, speaker roster and project-wide rename, virtualized script view, RFC 4180 translation CSV export/import, and continuous cross-platform CI verification.
- **1.4.0:** engine integration for Unity, Unreal and Godot (runtime companion JSON export, desktop-parity dialogue runner, live link hot-reload, scene focus and graph follow).
- **1.6.0:** text-variable conditions (inline and Text choice requirements), `{a:key}` article placeholder, and shared, saveable Unity variables.
- **1.5.1:** project-owned engine export folder, project JSON Schema (`schema` CLI command), persistent escalate visit counts across all engine runners, and Unreal Engine 5.8 build fix.
- **1.5.0:** dialogue logic expansion (OR conditions, flags, Set/Toggle/SetText actions, EvaluateBark API, per-page speaker/portrait overrides, escalation delivery, secondary entry points, character swatches), modernized `story.v1` runtime export, view decomposition and editor ergonomics pass.
- **Real-story pass (salt-ledger, 69 scenes).** Fixes: new-choice focus and Ctrl+Enter, editor scroll kept across choice edits and reset on scene change, choice conditions in Script view, keyboard Playtest (Enter, 1-9, Backspace). Unity headless pass, live-link session and by-hand Unity checks done earlier; Unity 6.6 flags `UIDocument` as superseded by Panel Renderer, so migrate only once the oldest supported Unity has it. Also fixed: the first outline click after opening a project no longer keeps a stale selection. Known and left: the zoomed-out graph is unreadable.
- **Hub scenes.** Scenes with more than 4 choices open with choice cards collapsed (number, response, destination, conditional marker); an `If ›` button jumps to that choice's conditions in Logic; Find expands the matched choice.
- **Saved test scenarios are permanent.** CI replays the committed scenario fixture on every build and the CLI `validate`/`replay` commands and [STORY_TESTING.md](STORY_TESTING.md) depend on them, so they are maintained like any other core feature.
