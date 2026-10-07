# Repeatable story testing

Open **Story → Test scenarios…**. Create a named scenario, choose its starting scene and integer random seed, and enable any starting numeric or text overrides. Unchecked values inherit the project defaults. Inventory starts at zero or above; affinity supports the full signed integer range. Each scenario has a replay operation limit between 1 and 10,000 (default 1,000).

**Record path…** opens the same player used by ordinary playtesting with this configuration. Play through dialogue pages and choices. Live numeric/text changes, explicit rerolls and refreshes are recordable operations. Back removes undone operations and restores the corresponding random state, variables, text, events, delivery and trace. Restart returns to the original seed and overrides. **Use recorded path** puts the current path into the draft; closing/Escape discards that recording. Save scenario applies the definition; Save project persists it on disk. Re-recording keeps assertions so changed outcomes are still checked.

The manager supports new definitions, editing, duplication with a fresh ID, deletion, and removal of individual recorded steps. Each saved edit, duplication and deletion is one Undo/Redo operation after closing the manager. Draft changes stay separate until Save scenario. Duplicate and Delete apply to the selected saved definition. Run draft uses the displayed configuration; Run all saved uses project definitions. These operations do not save or modify the story. Cancel run or closing the window cancels the worker.

Add or edit assertions for a final numeric value, final text value, the scene that exits, or an emitted Event/Script/Shop hook. Event assertions can match a parameter and an exact occurrence count, including zero; without an exact count they require at least one match. An omitted parameter matches any parameter. Require ending defaults on; turn it off for a deliberate checkpoint scenario. Unknown final variable IDs fail rather than comparing a missing value to zero. Integrations remain logged hooks; game code is not executed.

View results shows failures, configured dead ends and structured trace rows. Export results writes JSON separately from the project. The graph shows visited scene bars/checkmarks and response dots, plus distinct scene/choice counts. Coverage aggregates the latest run for each scenario, including partial failed runs; it measures observed replay visits, not inferred reachability or assertion quality. Authored changes invalidate transient results/coverage. Selection, keyboard/pointer interactions, filters, pan/zoom and Undo/Redo remain independent. Complete graph/script/website exports exclude coverage overlays and keep every authored scene/link.

## Replay references and compatibility

Scenario definitions are the additive desktop root collection `PlaytestScenarios` in format version 1. Older desktop and website imports use an empty collection. Definitions and unknown fields on scenarios, steps and assertions survive save/load, history, recovery and conflict-safe saving. Unsupported operations and stale references remain openable for repair; replay reports them. Transient runtime state, results and coverage are never serialized into the project. Website exports remain the original `ListOfCharacterStates` schema with numeric visibility values; no scenario configuration or trace is added.

Every recorded operation contains its expected scene ID, kind, and a `v1:` SHA-256 semantic fingerprint. Choice steps also contain the response's persisted `InternalId` GUID and a human-readable recorded label. Labels and list indices are never used to resolve a response. Reordering choices keeps recordings valid. Removing/replacing a choice, duplicating its GUID, changing a destination, dialogue, delivery, speaker, shop ID, requirements or actions produces an actionable failure with scenario, step, expected scene/response and repair guidance. Layout, titles, notes and unknown fields are outside this runtime fingerprint. Newly imported website responses without GUIDs must be recorded and saved in desktop format to persist their generated identities.

Safe editor scene/variable renames update scenario references, overrides/assertions and **previously valid** fingerprints together. They do not heal already-stale steps. Other authored changes require reviewing the failure and recording the path again; assertions should be revised only when the changed behavior is intended. Live variable/text edits after an ending use an explicit `[EXIT]` runtime reference and the `v1:ended` signature; the preceding transition still checks the authored ending path.

Random delivery uses the explicitly specified SplitMix64 integer algorithm, seeded by the signed 32-bit scenario seed converted to an unsigned 64-bit state. Each selection draws the next 64-bit value with rejection sampling for the candidate count. There are no process timestamps or `Random.Shared` calls. A visit keeps its selected candidate index during live refresh, temporarily clamps to shorter resolved text and restores the original index if lines return. If a visit initially resolves no candidates, its first newly available candidate selection consumes the initial seeded draw; later refreshes retain it. Reroll with candidates consumes a new draw even if it selects the same line. Back restores the stream before the undone operation, and Restart resets it. The opt-in website adapter exercises the same stream against the inspected actual website source; see [RUNTIME_HANDOFF.md](RUNTIME_HANDOFF.md) for its explicit differences from the original sandbox.

## Structural and configured checks

Story diagnostics check graph destinations, types, visibility values, logic/text validity, structural reachability and potential ending paths without evaluating runtime requirements. Scenario replay follows the saved operations with its seed/values and checks actual outcomes.

Configuration analysis branches over usable responses starting from the scenario's **starting** overrides. It uses the same runtime availability, page delivery, fallback destinations and action order, with Event/Script/Shop recorded as hooks. A configured dead end has no usable response and no nonblank continue fallback after its dialogue pages; results include the scene, numeric values and unmet requirements. The analysis is separate from the recorded sequence and does not apply its later manual live edits. Hidden/disabled responses do not prevent a usable fallback; Always responses remain usable.

Analysis deduplicates matching scene/variable/text/random configurations and bounds both exploration and queued branches. The editor's budget is 1,000 configurations; the CLI can choose 1–10,000. Transition work is separately bounded to four times that configuration budget so duplicate-heavy branching cannot monopolize a run. Replays bound operations, including page advances and live edits. Cancellation is checked between operations/configurations/branches. Limits retain partial evidence and explicitly report unexplored work. **Neither saved replay nor configuration analysis proves all paths of a cyclic story.** A repeated configuration may collapse a cycle, while evolving variables or random streams can reach the budget. Coverage from configuration analysis is not mixed into observed replay coverage.

## Core-only CLI

Requires the .NET 10 SDK when running from source; the CLI references only `DialogueMachine.Core` and has no Avalonia dependency.

```powershell
dotnet run --project CLI/DialogueMachine.Cli.csproj -c Release -- validate tests/Fixtures/repeatable.dialogue.json
dotnet run --project CLI/DialogueMachine.Cli.csproj -c Release -- replay tests/Fixtures/repeatable.dialogue.json --all --analyze --trace artifacts/scenario-trace.json
dotnet run --project CLI/DialogueMachine.Cli.csproj -c Release -- replay tests/Fixtures/repeatable.dialogue.json --scenario shop-seeded --max-steps 1000
dotnet run --project CLI/DialogueMachine.Cli.csproj -c Release -- replay tests/Fixtures/repeatable.dialogue.json --all --analyze --max-configurations 2000
dotnet run --project CLI/DialogueMachine.Cli.csproj -c Release -- --help
```

Replay defaults to all saved scenarios. `--scenario` accepts a stable ID or exact case-sensitive name and rejects ambiguous matches. `--max-steps` lowers each saved scenario's bound. `--max-configurations` requires `--analyze`. Ctrl+C cancels. JSON goes to stdout; `--trace` atomically saves the same report, including partial traces on replay/assertion failures. It refuses the input project as the output path. Save command output if input/write errors also need an artifact. The JSON envelope has `SchemaVersion: 1` and `ExitCode`; replay reports include per-scenario configuration, final runtime state, structured trace, diagnostics, observed coverage and optional separate configuration analysis. No timestamp or elapsed-time field affects reproducibility.

| Exit | Meaning |
| --- | --- |
| 0 | Validation has no errors, or replay/assertions and requested configuration checks pass |
| 1 | Structural validation errors from `validate` (warnings alone pass) |
| 2 | Invalid usage, missing/ambiguous scenario selector, malformed/unsupported/unreadable input, or output write failure |
| 3 | Replay/reference/condition/assertion failure, or configured dead end/analysis error |
| 4 | Replay or requested configuration analysis reached its bounded limit |
| 5 | Cancelled (partial replay state is retained when available) |

The committed fixture contains three seeded paths covering every scene and response, final numeric/text/ending assertions and Event/Script/Shop expectations. The regression harness loads this file; Windows CI also invokes the real CLI to validate/replay it and retains JSON evidence. To deliberately regenerate it from its deterministic authoring source, run `dotnet run --project tests/DialogueMachine.Tests.csproj -c Release -- --write-scenario-fixtures` and review the fixture diff. For focused development checks, run `dotnet run --project tests/DialogueMachine.Tests.csproj -c Release -- --scenario-checks artifacts/stage3`; the ordinary full regression command includes these checks.

Stage 4 compatibility fixtures are separate from saved desktop scenarios: five paths run the same exported story in desktop preview and the source-backed website adapter, with committed expected frames and actionable difference reports. `--runtime-checks artifacts/runtime` runs their focused checks; the full harness and Windows CI include them. `runtime-export <project.json> --seed 42 --output artifacts/story.runtime.json` provides explicit initial values, substitutions and seed without changing website JSON or project format 1. See [RUNTIME_HANDOFF.md](RUNTIME_HANDOFF.md) for the complete runnable sample, callbacks, contract, cancellation and bounds. Distribution expansion, package version changes and release publishing remain separate work.
