Dialogue Machine is an offline desktop workspace for branching dialogue.

Version 1.1.1 improves recovery, validation, graph arrangement and playtesting:

- Damaged recovery records no longer block startup or hide valid unsaved stories. Damaged files remain available for manual investigation.
- Reserved scene IDs, unsupported scene types and invalid choice visibility values are diagnosed. Unsupported desktop format versions are rejected while retaining the open story.
- Dialogue and response labels both receive conditional-tag checks, including unused branches. Formatting problems and unknown substitutions produce warnings.
- Arrange leaves room for scenes with many choices. Ending-path validation uses a reverse traversal for larger stories.
- Live test-variable edits record their changes and refreshed text in the transcript. Back restores the edit, preview and transcript together. Random delivery retains its selected line during refresh; Reroll line explicitly samples again.

Website dialogue JSON imports directly. Desktop saves preserve story data and metadata; website/game exports retain the original schema. Save as, cancellation and invalid imports preserve the current document.

Download the **win-x64 ZIP**, extract all files together, and run **DialogueMachine.exe**. The .NET runtime is included. This is a portable Windows app; no SDK or browser is required.

The accompanying `.sha256` file contains the download checksum. The release workflow runs regression checks and native Windows startup checks before publishing this package.
