Dialogue Machine is an offline desktop workspace for branching dialogue.

## 1.2.0

### Writing

- Find & replace text across the whole project with Ctrl/Cmd+H.
- Save scenes and choices as templates and reuse them anywhere in the project.
- Manage variables in one place: search them, see where each is used, rename it everywhere, and delete unused ones.
- Variable edits stay a draft until you apply them, and one Undo reverts them all.
- Logic fields suggest variable IDs, and the Variable… picker inserts a text substitution at the cursor.
- Ordinary prose no longer creates stray variables.

### Organizing

- Pick a link's destination by searching scene titles or IDs, and jump along connections.
- Filter the outline by speaker, tag or problem, combined with text search.
- Select several scenes with Ctrl/Cmd-click, Shift-click, Shift-drag or Ctrl/Cmd+A, then move them together with one Undo step.
- Copy and paste selected scenes with Ctrl/Cmd+C; links between pasted scenes are kept.
- Sort scenes into named groups or chapters, and filter the outline by group.
- Toggle a minimap with Ctrl/Cmd+Shift+M, and pan the graph with Alt+arrows.
- Selected scenes' connections are thicker and colored so you can follow them.

### Testing & exporting

- Save named test scenarios with a starting scene, values and seed, record a path through the story, and check its outcome.
- Run all scenarios at once and see which scenes and choices they visited on the graph.
- Find scenes where a scenario's starting values leave the player stuck.
- Export the whole story graph as a PNG image.
- Export a readable Markdown script, with optional conditions, actions and writer's notes.

### Safety

- Dialogue Machine warns you before Save overwrites changes made to the file outside the app, and lets you compare, reload, save a copy or cancel.
- Recovery still finds your unsaved work when the file was changed elsewhere.
- Large stories stay responsive while editing and navigating.

## 1.1.1

Version 1.1.1 improves recovery, validation, graph arrangement and playtesting:

- Damaged recovery records no longer block startup or hide valid unsaved stories. Damaged files remain available for manual investigation.
- Reserved scene IDs, unsupported scene types and invalid choice visibility values are diagnosed. Unsupported desktop format versions are rejected while retaining the open story.
- Dialogue and response labels both receive conditional-tag checks, including unused branches. Formatting problems and unknown substitutions produce warnings.
- Arrange leaves room for scenes with many choices. Ending-path validation uses a reverse traversal for larger stories.
- Live test-variable edits record their changes and refreshed text in the transcript. Back restores the edit, preview and transcript together. Random delivery retains its selected line during refresh; Reroll line explicitly samples again.

Website dialogue JSON imports directly. Desktop saves preserve story data and metadata; website/game exports retain the original schema. Save as, cancellation and invalid imports preserve the current document.

Download the **win-x64 ZIP**, extract all files together, and run **DialogueMachine.exe**. The .NET runtime is included. This is a portable Windows app; no SDK or browser is required.

The accompanying `.sha256` file contains the download checksum. The release workflow runs regression checks and native Windows startup checks before publishing this package.
