# Roadmap

**Goal:** an offline editor where writers write, connect, check and playtest branching dialogue, then export it as JSON for the website or a game. Every task should make that loop better or simpler. Don't add infrastructure for its own sake.

## Now

1. **Real-story pass.** Write or import one complete story (50+ scenes) and fix the friction that turns up.
   - Done when: the friction found is fixed or listed here, and features nobody needed are flagged for removal.
   - Unity headless pass on `Examples/salt-ledger.dialogue.json` (69 scenes) and a scripted live-link session are done; fixed: play-from overrides now apply before the first scene resolves, and the follow pane/Unity window match projects by name slug. Done by hand on Unity 6000.6.7f1: Engine Export Folder, `DefaultDialogueView` (now renders without a theme) and the Live Link window with the real app. Desktop pass on salt-ledger, run by hand in the built app: fixed new choices not taking focus (Ctrl+Enter in a response adds the next one), the editor scroll resetting after choice edits (and now returning to the top when you pick another scene), the Script view hiding choice conditions, and Playtest being mouse-only (Enter continues, 1-9 pick a response, Backspace goes back). Listed, not fixed: the first outline click after opening a project adds to the selection instead of replacing it; the 33% graph of 69 scenes is unreadable without zoom; the 9-choice hubs make long Content tabs; the Logic tab has no way to jump to a conditional choice. Nothing found that needs removing. Later: Unity 6.6 flags `UIDocument` as superseded by Panel Renderer; migrate only once the oldest supported Unity has it.
2. **Shrink the hot spots.** Simplify code when you change it, not as a big rewrite.
   - Move `MainWindow` panes into their own views as they're touched.

## Later, only when a real story needs it

- Character roster (speakers are plain text today).
- Richer logic: flags, OR conditions, set/toggle actions. The exported JSON must stay website-compatible.
- Mac/Linux releases: the builds exist but need a native smoke test first.
- Localization or voice references, once the website/game side is defined.

## Out of scope

Accounts, cloud sync, collaboration, and auto-update. The website JSON export is the integration point.

## Done

- **1.1.1:** recovery, validation, arrangement and playtest reliability.
- **1.2.0:** variables manager, find & replace, destination search, templates and scene copy/paste, multi-select, groups, minimap, PNG/Markdown export, conflict-safe saving, large-story performance and saved test scenarios.
- **1.3.0:** runtime companion export, speaker roster and project-wide rename, virtualized script view, RFC 4180 translation CSV export/import, and continuous cross-platform CI verification.
- **1.4.0:** engine integration for Unity, Unreal and Godot (runtime companion JSON export, desktop-parity dialogue runner, live link hot-reload, scene focus and graph follow).
- **Saved test scenarios are permanent.** CI replays the committed scenario fixture on every build and the CLI `validate`/`replay` commands and [STORY_TESTING.md](STORY_TESTING.md) depend on them, so they are maintained like any other core feature.
