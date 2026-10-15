# Roadmap

Dialogue Machine is an offline editor for writers who export branching dialogue to the website or a game. Keep it focused on that: writing, connecting, checking and playtesting stories, then exporting them.

## Done

- **1.1.1:** recovery, validation, arrangement and playtest reliability fixes.
- **Unreleased (on `master`):** variable management, project-wide find & replace, destination search and navigation, reusable scenes/templates, multi-selection, groups/chapters, minimap, PNG and Markdown exports, conflict-safe saving, large-story performance work and saved test scenarios.

## Next

1. **Ship it.** Cut a release from the current `master` so writers get the unreleased features.
2. **Use it on a real story.** Write or import a full story and fix the friction that turns up. Let real use, not speculation, decide the next features.
3. **Simplify where it hurts.** Split `MainWindow` and the large test files when changing them, and remove features that turn out to be unused.

## Later, only if needed

- **Character roster:** if multi-speaker stories become common (speakers are plain text today).
- **Richer logic:** flags, OR conditions or set/toggle actions, when a story needs them. Any change to the exported JSON must stay compatible with the website.
- **Mac/Linux releases:** the build scripts already produce them; they need a native smoke test before being published.
- **Localization or voice references:** only after deciding how the website/game would consume them.

## Out of scope

Accounts, cloud sync, collaboration, auto-updaters, and engine-specific runtime adapters. The website JSON export is the integration point; the website and games own playback.
