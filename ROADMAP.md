# Roadmap

**Goal:** an offline editor where writers write, connect, check and playtest branching dialogue, then export it as JSON for the website or a game. Every task should make that loop better or simpler. Don't add infrastructure for its own sake.

## Now

1. **Engine integration (Unity → Unreal → Godot).** See [Engines/CONTRACT.md](Engines/CONTRACT.md).
   - Phase 1: Shared C# runtime + conformance fixtures. Done.
   - Phase 2: Data pipeline in Dialogue Machine. Done.
   - Phase 3: Live link (localhost WebSocket bridge). Done when: a scripted client sees story updates on save, and a scripted runner's progress is tracked live on the graph with the trail and variables shown.
   - Phase 4: Unity (priority 1). Done when: the harness and conformance pass, the package compiles in Unity 2022.3, and a manual check works.
   - Phase 5: Unreal (priority 2). Done when: all fixtures pass in the UE automation test, and the manual checklist is done.
   - Phase 6: Godot (priority 3). Done when: the Godot CI job is green, and the manual live-link checklist is in the PR.
   - Phase 7: Release wiring for all engines. Done when: release packaging and version synchronization checks pass.
2. **Real-story pass.** Write or import one complete story (50+ scenes) and fix the friction that turns up.
   - Done when: the friction found is fixed or listed here, and features nobody needed are flagged for removal.
3. **Shrink the hot spots.** Simplify code when you change it, not as a big rewrite.
   - Split `tests/Program.cs` (136 KB) into area files like the existing `tests/*Checks.cs`.
   - Move `MainWindow` panes into their own views as they're touched.
   - Decide whether saved test scenarios earn their size (~120 KB of code and tests). Keep them if real use relies on them; otherwise remove them.

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
