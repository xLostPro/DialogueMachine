# Roadmap

**Goal:** an offline editor where writers write, connect, check and playtest branching dialogue, then export it as JSON for game engines (Unity, Unreal, Godot). Every task should make that loop better or simpler. Don't add infrastructure for its own sake.

## Now

1. **Shrink the hot spots.** Simplify code when you change it, not as a big rewrite.
   - Scene editor moved to `SceneEditorView`, Find & replace to `SearchView`, Variables to `VariablesView`, export dialogs to `ExportView`, Scenarios to `ScenariosView`. Still in `MainWindow`: Playtest launch. Move each into its own view as it's touched.

## Later, only when a real story needs it

- Mac/Linux releases: the builds exist but need a native smoke test first.
- Localization or voice references, once the website/game side is defined.

## Out of scope

Accounts, cloud sync, collaboration, and auto-update. The game engine JSON export is the integration point.

## Done

- **Retired website lossy export.** The separate website export and its degradation workarounds (`SplitAny` splitting OR choices and stripping Set/Toggle actions) are removed. The game engine runtime companion is now the sole story export format. Legacy website JSON files remain importable.
- **OR conditions.** A choice has `RequirementMatch` (All by default, Any), switched in the Logic tab as "ALL (AND)" / "ANY (OR)". It is stored only when Any. Story exports preserve `RequirementMatch: Any`. The Unity, Godot and Unreal runners honour it (new `or_conditions` conformance fixture, not yet run in Godot or Unreal).
- **Flags and Set/Toggle actions.** A flag is an ordinary 0/1 `aff:` variable; `Set` (Amount 0/1) and `Toggle` actions write it, and flag conditions are plain `Affinity ==/!= 0/1` requirements, so the Logic tab shows them as "flag is set / not set". Story exports preserve Set and Toggle actions, and the Unity, Godot and Unreal runners implement them (new `flags_set_toggle` conformance fixture, not yet run in Godot or Unreal).
- **Character roster.** `Characters` (name + optional `#RRGGBB` swatch) is desktop-only project data, managed in Variables & characters. Roster names join speaker autocomplete, speaker rename carries the entry, and graph nodes show the colour as an accent. Scenes and website exports still carry the plain speaker string.
- **Zoomed-out graph readability.** Below 55% zoom nodes draw only a large speaker, title, entry and end marker plus ports.
- **1.1.1:** recovery, validation, arrangement and playtest reliability.
- **1.2.0:** variables manager, find & replace, destination search, templates and scene copy/paste, multi-select, groups, minimap, PNG/Markdown export, conflict-safe saving, large-story performance and saved test scenarios.
- **1.3.0:** runtime companion export, speaker roster and project-wide rename, virtualized script view, RFC 4180 translation CSV export/import, and continuous cross-platform CI verification.
- **1.4.0:** engine integration for Unity, Unreal and Godot (runtime companion JSON export, desktop-parity dialogue runner, live link hot-reload, scene focus and graph follow).
- **Real-story pass (salt-ledger, 69 scenes).** Fixes: new-choice focus and Ctrl+Enter, editor scroll kept across choice edits and reset on scene change, choice conditions in Script view, keyboard Playtest (Enter, 1-9, Backspace). Unity headless pass, live-link session and by-hand Unity checks done earlier; Unity 6.6 flags `UIDocument` as superseded by Panel Renderer, so migrate only once the oldest supported Unity has it. Also fixed: the first outline click after opening a project no longer keeps a stale selection. Known and left: the zoomed-out graph is unreadable.
- **Hub scenes.** Scenes with more than 4 choices open with choice cards collapsed (number, response, destination, conditional marker); an `If ›` button jumps to that choice's conditions in Logic; Find expands the matched choice.
- **Saved test scenarios are permanent.** CI replays the committed scenario fixture on every build and the CLI `validate`/`replay` commands and [STORY_TESTING.md](STORY_TESTING.md) depend on them, so they are maintained like any other core feature.
