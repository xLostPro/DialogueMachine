Dialogue Machine is an offline desktop workspace for branching dialogue.

## Unreleased

- Search numeric starting values and text substitutions in Variables & characters, inspect reference counts and scene locations, and rename IDs across actions, requirements and inline text. Unused variables can be deleted; referenced variables remain protected until their uses are resolved.
- Variable edits stay in a draft until Apply changes. Cancel or Escape preserves the story, and one Undo restores all applied changes and reference rewrites.
- Logic fields suggest IDs for their selected affinity/inventory type. The Content toolbar's Variable… picker searches known IDs and inserts a substitution at the cursor.
- Inline-only numeric variables are discovered for editing and playtesting; ordinary prose no longer creates variables. Reference rewrites preserve inventory-first condition aliases and legacy text substitutions.

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
