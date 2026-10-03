# Dialogue Machine: project assessment and proposed roadmap

Assessment date: 1 October 2026. Source baseline: `503f505`; desktop project version: `1.1.0`.

This is a proposed order of work based on the repository, not an existing release commitment. The working assumption is that the product should remain an offline dialogue editor for writers who export stories into a website or game. No GitHub release status or external website/game implementation was checked.

## Recommendation

The initial recommendation was a focused reliability patch. **Stage 1 is merged in [PR #2](https://github.com/xLostPro/DialogueMachine/pull/2) for version 1.1.1**, with passing Windows CI. Recovery handling, import/validation consistency, text diagnostics, node-height-aware arrangement and playtest refresh consistency have been addressed. The original findings below describe the assessment baseline.

**Stage 2 priorities 1–4 are implemented**, with 318 passing regression checks. Variable management, project-wide find/replace and destination navigation are merged in PRs #3, #4 and #5, with passing Windows CI. Reusable authoring operations add connected-scene creation, multi-scene copying/pasting with internal links remapped, project-owned scene/choice templates, template-aware variable references and one-step undo. The next feature is better graph organization. These source changes do not imply a published release.

The next work is Stage 2: improve the daily writing workflow, then add repeatable story testing and prove one real game integration. Expand distribution and advanced narrative features after those foundations are dependable.

The app already implements the basic write → connect → validate → playtest → export loop. Its main gaps are correctness at the edges, efficient editing as stories grow, and confidence that exported dialogue behaves as expected in its destination runtime.

## Baseline feature inventory

| Area | Implemented | Remaining gap |
| --- | --- | --- |
| Project lifecycle | Welcome, named projects, recent files, optional reopen, save/save-as, cancellation, atomic writes, recovery | Malformed recovery handling, future format policy, external file change detection |
| Authoring | Scene titles, free-text speakers, dialogue pages, ordered choices, notes, tags, script overview, duplication, safe ID rename, undo/redo | Find/replace, searchable destinations, variable rename/delete, reusable content, structured character management |
| Graph | Node movement, connections, pan/zoom, fit, automatic arrangement, keyboard alternatives | Arrangement overlap, navigation at scale, groups, multiple selection, full-story export |
| Logic | Integer affinity/inventory, AND requirements, visibility modes, additive actions, inline conditions/substitutions | Consistent text diagnostics, reference assistance, richer types and expressions if needed |
| Playtesting | Start/selected scene, live variables, Back, restart, transcript, integration event log | Display/transcript consistency, repeatable randomness, saved scenarios, coverage, assertions |
| Interchange | Website schema import/export, numeric enum export, unknown-field preservation in desktop round trips | Destination-runtime contract fixtures, real integration sample, runtime initialization handoff |
| Distribution | Self-contained publishing scripts, Windows CI, native Windows startup checks, ZIP/checksum/tag release workflow | Native Mac/Linux verification, native accessibility testing, signing and installation experience |

The `Core` library is separated from Avalonia. That is a useful base for a command-line validator or game adapter. There is no need for a broad architectural rewrite before the next features; move individual editor panes and services out of `MainWindow` as their responsibilities grow.

## Evidence and verification

The existing regression harness passed **91/91 checks**:

```powershell
dotnet run --project tests/DialogueMachine.Tests.csproj -c Release --no-restore -- artifacts/roadmap-review
```

It covers substantial core and headless UI behavior, including file workflows. I inspected the compact workspace render. The review did not manually exercise native file dialogs, screen readers, or Mac/Linux builds. Passing the existing suite does not cover the defects below.

Additional probes against the current Core assembly are retained under `artifacts/roadmap-review/probes/Program.txt` and `results.txt` (ignored build artifacts).

| Finding | Evidence | Consequence |
| --- | --- | --- |
| A semantically invalid recovery can escape error handling | Recovery JSON with a scene whose ID is empty throws `InvalidDataException` from `RecoveryStore.FindUnsaved`. The per-record catch omits that exception. Welcome loads recoveries during window construction. See `Core/RecoveryStore.cs:15` and `UI/MainWindow.Files.cs:159`. | A damaged recovery can prevent startup instead of being skipped while valid recoveries remain available. |
| Reserved scene IDs can enter through import | An imported scene named `[EXIT]` produces zero diagnostics, but simulation ends immediately. Rename rejects that ID; parsing and validation do not. See `Core/ProjectStore.cs:24` and `Core/DialogueEngine.cs:62`. | An apparently valid imported story can silently skip its content. |
| Choice text does not receive inline-condition validation | Choice label `[if aff:trust >= 1]unfinished` produces zero errors. Validation checks `s.Text` only. See `Core/DialogueEngine.cs:87`. | Malformed choice labels can reach playtest and export. Formatting-tag diagnostics also need a defined policy. |
| Arrange can overlap scenes | Two same-layer nodes are spaced 300 units apart. A six-choice node is 362 units tall; the probe overlaps the next node by 62 units. See `Core/DialogueEngine.cs:124` and `UI/GraphCanvas.cs:26`. | Ordinary branching scenes can become obscured after Arrange. |
| Live text refresh and transcript diverge | Change `aff:trust` from 0 to 1: the displayed page changes from “Stranger” to “Friend”, while the transcript retains “Stranger”. See `Core/DialogueEngine.cs:150` and `UI/PlaytestWindow.cs:77`. | Exported test evidence does not explain everything the tester saw. Random-line delivery also selects a new line on every text refresh by code inspection. |
| Project format versions are unchecked | JSON containing `FormatVersion: 999` is accepted. See `Core/Models.cs:9` and `Core/ProjectStore.cs:15`. | Future incompatible files could be edited without a compatibility warning. Define a policy before changing the format. |

A synthetic single-chain story gave these one-run timings on this machine, measured for Core validation and serialization only:

| Scenes | Validation | Serialize one snapshot | JSON snapshot size |
| --- | --- | --- | --- |
| 100 | 8.7 ms | 0.2 ms | 33 KB |
| 1,000 | 67.2 ms | 1.4 ms | 334 KB |
| 10,000 | 2,163.4 ms | 19.9 ms | 3.36 MB |

These are directional measurements, not a full UI benchmark. Validation's repeated ending-reachability scan is particularly expensive on this chain. Diagnostics run synchronously during commits. Graph rendering searches the scene list for each destination and draws every node; history stores up to 100 complete JSON strings; dirty checks repeatedly serialize the document. At the import limit, history alone can occupy hundreds of megabytes. Establish a tested practical story size rather than treating the 10,000-scene import limit as a responsiveness guarantee.

After replacing the repeated ending scan with reverse-edge traversal in Stage 1, the same synthetic 10,000-scene chain validated in **44.0 ms** in a one-run local follow-up, including the expanded diagnostics, versus 2,163.4 ms at baseline. This does not measure full editor responsiveness. Follow-up results are in `artifacts/reliability-patch/benchmark-results.txt`.

## Stage 1 — reliability patch: merged

Merged for version 1.1.1. The scope and completion criteria are retained here for review; publishing a release remains separate.

1. **Handle damaged recovery records safely.** Catch semantic validation failures per record, retain damaged files for investigation, and keep valid recovery choices available. Add a startup regression using a structurally valid but semantically invalid recovery.
2. **Align import, validation, and runtime invariants.** Diagnose reserved IDs, invalid enum values, unsupported scene types, and unsupported format versions consistently. Keep the existing policy of opening repairable graphs; do not reject every missing connection during import. Preserve the current document on rejected imports.
3. **Validate text wherever it appears.** Apply shared conditional-tag checks to scene dialogue and choice labels. Identify the exact choice/field in diagnostics. Add cases for malformed nesting, extra else tags, and conditions hidden inside unused branches. Define how unclosed formatting tags and unknown substitutions are reported.
4. **Arrange using actual node dimensions.** Share node geometry between layout and drawing, account for choice count, and leave a consistent gap. Test same-layer scenes with different numbers of choices and cycles.
5. **Define playtest refresh semantics.** Record live variable edits and refreshed page text in an explicit trace, or update the current transcript entry consistently. Preserve the selected random line for a visit; allow an explicit reroll. Back must restore the corresponding text, variables, and trace.

Completion gate: all existing checks plus targeted regressions pass; damaged recovery cannot block Welcome; imported reserved IDs cannot pass unnoticed; Arrange does not overlap nodes; exported playtest evidence explains live edits; desktop/web round trips remain compatible.

## Stage 2 — make writing faster

Recommended first feature release after the patch:

| Priority | Feature | What done looks like |
| --- | --- | --- |
| 1 — implemented | Variable management and reference assistance | Searchable manager and inline insertion, typed logic suggestions, reference counts/locations, rename across actions/requirements/inline text/starting values, unused deletion, cancel-safe changes and one-step undo |
| 2 — implemented | Project-wide find and replace | Search dialogue, responses, titles and notes; highlighted match/replacement previews; case/whole-word options; field and scene scope; jump to matching text; replace selected/all with one-step undo/redo |
| 3 — implemented | Destination search and better navigation | Search destinations by title/ID; retain missing/fallback/unset/ending values; undo connections; jump to incoming/outgoing scenes; intersect outline text/speaker/tag/diagnostic filters; keyboard selection and compact layouts |
| 4 — implemented | Reusable authoring operations | Create a connected scene from a choice; multi-scene copying/pasting with fresh IDs and remapped internal links; saved scene/choice templates with preview, insertion, deletion and variable-reference safety; single-step undo/redo |
| 5 | Better graph organization | Scene groups/chapters, multiple selection and group movement, clearer selected connections, optional minimap; incremental schema additions where possible |
| 6 | Export improvements | Full-story image export independent of viewport; readable script export options for conditions, actions and notes |

Include external modification detection before ordinary Save overwrites a project changed by another app or editor instance. Offer compare/reload/save-a-copy choices while retaining local edits. Atomic writes prevent partial files; they do not resolve concurrent edits.

Continue performance work here: Stage 1 has replaced repeated ending scans with reverse-edge traversal and indexed IDs for layout. Index destinations during graph drawing, skip offscreen nodes, cache text layouts, and avoid whole-document work on each keystroke. Profile 100/1,000/10,000-scene stories with variable choice counts and record editor latency, render time, and memory. Replace snapshot history only if measurements justify it.

Navigation now caches searchable scene summaries and connection indices, avoiding validation and serialization while typing filters. A one-run synthetic Core probe measured:

| Scenes | Build navigation index | Search destinations | Filter outline | Index allocations |
| --- | --- | --- | --- | --- |
| 100 | 0.44 ms | 0.01 ms | 0.01 ms | 102,904 bytes |
| 1,000 | 1.09 ms | 0.04 ms | 0.03 ms | 1,004,776 bytes |
| 10,000 | 17.18 ms | 1.74 ms | 0.43 ms | 9,928,096 bytes |

These measure Core operations and allocated bytes, excluding graph drawing, UI rendering and retained process memory. Results are retained in `artifacts/destination-navigation/navigation-benchmarks-baseline.txt`; the harness also checks full counts with bounded destination previews at all three sizes. Full editor profiling remains follow-up work.

Completion gate: a writer can reorganize and revise a substantial story without repeatedly typing IDs, inspecting JSON, or losing references; all bulk changes undo reliably.

## Stage 3 — make story testing repeatable

- Save named playtest scenarios with starting scene, numeric/text values, and random seed.
- Record scenes, choices, variable changes, integration events and endings in a structured trace.
- Highlight visited scenes and choices, and report coverage across scenarios.
- Support replayable choice sequences and assertions on final variables, endings and emitted events.
- Add condition-aware dead-end checks for supplied scenarios. Keep structural reachability distinct from what a particular variable configuration can reach; do not promise exhaustive verification of all paths in cyclic stories.
- Expose a Core-based CLI for validation and scenario replay, with JSON diagnostics and meaningful exit codes.

Completion gate: changing a branch or action can be checked against saved scenarios locally and in CI, with enough trace detail to reproduce a failure.

## Stage 4 — prove the runtime handoff

Choose the first destination from actual usage: the existing website player or one game engine. Build one complete sample before accumulating adapters.

- Document and version the contract: start scene, fallback destinations, visibility, action order, numeric limits, random delivery, inline conditions and substitutions.
- Maintain destination-runtime compatibility fixtures for imports and exports. The current suite checks local schema behavior; the other runtime is not present here.
- Provide starting numeric values and text substitutions through an explicit runtime configuration or companion export. Current website JSON deliberately omits them.
- Define host callbacks for Event, Script and Shop behavior, with a working integration example and deterministic tests. Local playtesting currently logs these hooks.
- Establish additive migrations and clear messages for incompatible desktop project versions. Keep legacy website exports stable.

Completion gate: the same sample story has matching choices, transitions and variable outcomes in the desktop preview and the selected real runtime.

## Distribution track

Windows is the verified primary target in the repository. Before claiming Mac/Linux support, run native launch, dialogs, fonts/rendering, keyboard and accessibility checks on those systems and add CI jobs. Cross-publishing alone is insufficient.

For broader distribution, add signing/notarization where applicable, a suitable installer or portable onboarding, and file associations. Consider updating only once release channels and signing are settled. Native Windows accessibility and file-picker checks remain useful near-term manual gates even before expanding platforms.

## Later features and product decisions

- **Character roster:** useful if multi-speaker stories are common. Currently speakers are strings in scene metadata and “characters” primarily means text substitutions.
- **Localization and audio:** stable text identifiers, translation import/export, speaker/voice asset references and missing-content diagnostics. Decide the runtime contract first.
- **Richer logic:** flags, set/toggle actions, OR groups, and more advanced expressions should follow concrete authoring needs and a compatibility plan.
- **Multi-project libraries:** useful after single-project navigation and reusable content are working well.
- **Collaboration/cloud sync:** a separate product decision involving project identity, conflict handling and infrastructure. It should not displace the offline editor roadmap without demonstrated demand.

The key decisions before committing to later milestones are the primary audience, typical and largest intended story sizes, first export destination, and whether multi-character/localized stories are core use cases. These do not block the reliability patch or the first authoring improvements.

## Next implementation batch

Implement better graph organization next: multiple selection and group movement, clearer selected connections, then scene groups/chapters and an optional minimap. Variable/reference management, project-wide find/replace, destination navigation and reusable authoring operations are implemented and Stage 1 is merged; release publishing and native Mac/Linux verification remain separate work. Scene copying currently selects sets in a dedicated dialog; the next batch should bring multiple selection into the graph itself. External modification detection should precede broader shared-file workflows.
