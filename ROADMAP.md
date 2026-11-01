# Roadmap

**Goal:** an offline editor where writers write, connect, check and playtest branching dialogue, then export it as JSON for the website or a game. Every task should make that loop better or simpler. Don't add infrastructure for its own sake.

## Now

1. **Shrink the hot spots.** Simplify code when you change it, not as a big rewrite.
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
- **Real-story pass (salt-ledger, 69 scenes).** Fixes: new-choice focus and Ctrl+Enter, editor scroll kept across choice edits and reset on scene change, choice conditions in Script view, keyboard Playtest (Enter, 1-9, Backspace). Unity headless pass, live-link session and by-hand Unity checks done earlier; Unity 6.6 flags `UIDocument` as superseded by Panel Renderer, so migrate only once the oldest supported Unity has it. Known and left: first outline click after opening a project can add to the selection; the zoomed-out graph is unreadable; 9-choice hubs make long Content/Logic tabs with no jump to a conditional choice.
- **Saved test scenarios are permanent.** CI replays the committed scenario fixture on every build and the CLI `validate`/`replay` commands and [STORY_TESTING.md](STORY_TESTING.md) depend on them, so they are maintained like any other core feature.
