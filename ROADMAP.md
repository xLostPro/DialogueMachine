# Roadmap

**Goal:** an offline editor where writers write, connect, check and playtest branching dialogue, then export it as JSON for the website or a game. Every task should make that loop better or simpler. Don't add infrastructure for its own sake.

## Now

1. **Real-story pass.** Write or import one complete story (50+ scenes) and fix the friction that turns up.
   - Done when: the friction found is fixed or listed here, and features nobody needed are flagged for removal.
2. **Shrink the hot spots.** Simplify code when you change it, not as a big rewrite.
   - Split `tests/Program.cs` (136 KB) into area files like the existing `tests/*Checks.cs`.
   - Move `MainWindow` panes into their own views as they're touched.
   - Decide whether saved test scenarios earn their size (~120 KB of code and tests). Keep them if real use relies on them; otherwise remove them.

## Later, only when a real story needs it

- Character roster (speakers are plain text today).
- Richer logic: flags, OR conditions, set/toggle actions. The exported JSON must stay website-compatible.
- Mac/Linux releases: the builds exist but need a native smoke test first.
- Localization or voice references, once the website/game side is defined.

## Out of scope

Accounts, cloud sync, collaboration, auto-update, and engine-specific runtime adapters. The website JSON export is the integration point.

## Done

- **1.1.1:** recovery, validation, arrangement and playtest reliability.
- **1.2.0:** variables manager, find & replace, destination search, templates and scene copy/paste, multi-select, groups, minimap, PNG/Markdown export, conflict-safe saving, large-story performance and saved test scenarios.
- **Cleanup (PR #14):** removed the unused runtime handoff and benchmark tooling, shortened the docs, cut CI artifact storage.
- **Runtime companion export:** added File → Export runtime companion… with seed and limit configuration.
- **Speaker roster, autocomplete & rename:** added project speaker discovery, autocomplete in scene content editor, and project-wide speaker rename with references in Variables dialog.
- **Virtualized script view:** converted the readable Script view to a virtualized ListBox with lightweight ScriptRow projection, eliminating eager whole-story control trees for large projects.
