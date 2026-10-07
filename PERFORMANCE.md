# Stage 2 editor performance

The performance runner exercises the production Avalonia `MainWindow`, editor callbacks, outline selection, graph gestures, search dialog, history and guarded file workflows. It renders the full window and open owned dialogs with real Skia `RenderTargetBitmap`; graph-only rows render the production canvas. This measures UI work, layout and synchronous raster rendering, rather than only Core calls. It does **not** measure physical input delivery, GPU compositor/present latency, display refresh, accessibility tooling or interactive typing cadence. Native Windows runs cross-check the backend; headless results are not an FPS guarantee.

## Reproduce

Run sequentially in PowerShell from the repository, with other workloads idle:

```powershell
./Packaging/Measure-EditorPerformance.ps1 -BaselineRef 74c2a17 -OutputDirectory artifacts/performance-before -Samples 3 -HistoryEntries 20
./Packaging/Measure-EditorPerformance.ps1 -OutputDirectory artifacts/performance-after -Samples 3 -HistoryEntries 20
./Packaging/Measure-EditorPerformance.ps1 -BaselineRef 74c2a17 -Native -Sizes 1000 -OutputDirectory artifacts/performance-native-before -Samples 3 -HistoryEntries 20
./Packaging/Measure-EditorPerformance.ps1 -Native -Sizes 1000 -OutputDirectory artifacts/performance-native-after -Samples 3 -HistoryEntries 20
./Packaging/Measure-EditorPerformance.ps1 -Sizes 1000 -OutputDirectory artifacts/performance-history-100 -Samples 3 -HistoryEntries 100
./Packaging/Measure-EditorPerformance.ps1 -CacheProbe -Sizes 1000 -OutputDirectory artifacts/performance-cache -Samples 5 -HistoryEntries 1
```

The baseline option archives the named commit into a new temporary directory, copies the current opt-in runner into it and adds only the performance dispatch. It restores against the existing package cache and leaves the checkout untouched. The archive/temp source is retained for inspection; remove that explicitly named temporary folder when no longer needed. Without the option the script measures the current source, including uncommitted edits. Keep the same runner, fixtures, runtime, machine, viewport and sample count on both sides. The default is five samples; the recorded comparison uses three to keep the large baseline tractable. Three-sample p95 is simply the maximum, not a reliable tail-latency estimate. Use more samples and multiple fresh-process runs when comparing smaller differences.

The direct runner command is also available:

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
dotnet run --project tests/DialogueMachine.Tests.csproj -c Release -- --performance --output artifacts/performance --samples 5 --history 20
# Append --native to use the Windows backend, or --sizes 1000 for one fixture.
```

Output includes fixture JSON, byte sizes/SHA-256, raw sample times and allocation counts, medians/p95, process/managed memory, environment, product source fingerprint and commands (`results.json`, `run.json`, `console.txt`). Partial JSON survives a failed run. Large fixtures and build/test data remain ignored; compact measured results belong in `docs/performance`. The recorded after runs used the uncommitted implementation in this PR; their SourceRevision names the parent HEAD, not an unchanged baseline. ProductSourceSha256 records the actual Core/UI/project/palette source bytes (backfilled from preserved baseline source for the initial before runs; recorded directly for final after runs). Instrumentation changes do not enter that product fingerprint.

## Fixtures and method

`tests/PerformanceChecks.cs` deterministically generates 100, 1,000 and 10,000 scenes: 0/1/2/3/5 choices, a guaranteed chain to an ending, backward cycles, cross-row links, inherited continue links, 12-page dialogue every twentieth scene, formatting, inline conditions, substitutions, alternating speakers, tags, notes, overlapping 50-scene chapters with up to 60 members, scene/choice templates and unknown extension fields. Choice GUIDs and group IDs are deterministic. The serialized fixtures are approximately 193 KB, 1.91 MB and 19.09 MB, below the existing 20 MiB disk limit. These are **mixed synthetic workloads**, not representative evidence for every real story.

Each operation warms once and records three repetitions without forced collection inside the timed interval. Allocations use process-wide `GC.GetTotalAllocatedBytes(true)`, including UI/renderer managed work but excluding native allocations. Native allocations contribute to private bytes/working set. Text events flush before explicit commit; the 700 ms timer is extended to one hour in this isolated runner so slow baseline frames cannot race the separately measured commit. Recovery timer is off in test mode; actual recovery behavior remains covered by regressions. Loading uses exact-byte fingerprints, parsing, diagnostics, controls and a frame. Saving uses actual guarded atomic disk writes; the save samples include clean saves after the initial warm save, with all edits retained. Input is programmatically raised through real production controls; no native file pickers are automated here.

The viewport is 1540×940, Dark, 100%, with the default hidden minimap and local graph zoom 0.85 around the entry. Selection alternates active outline scenes; group movement nudges 20 selected scenes and commits once. Outline queries alternate checkpoint/harbour; find queries alternate perf-marker/harbour, including bounded previews and rendered dialog surfaces. Undo/Redo is a paired operation. Pan alternates left/right, zoom alternates 0.85/0.95. Fit is also rendered separately. Warm state changes intentionally accumulate in a fixed order. Loaded memory is collected after switching from Fit to the local viewport; history memory is collected before and after **20 additional real text/commit/frame edits**, on top of earlier workload entries. These are process totals, not isolated history sizes; the difference is a retention signal, and other controls/renderer state can vary. Private/working-set memory may retain allocator arenas after collection. The 100-entry run checks a realistic history-capacity workload independently.

## Environment and results

Measured on 4 October 2026: Windows 11 build 26200 (reported by .NET as Windows 10.0.26200), AMD Ryzen 7 7800X3D, 8 cores/16 logical processors, 32 GB RAM, .NET SDK 10.0.204/runtime 10.0.12 x64, Avalonia 12.1.3, Inter fonts, Release build, real Skia raster targets. Baseline source is `74c2a17` (latest master at the start of this batch). The raw runs are committed in [docs/performance](docs/performance):

| Operation (median ms, before → after) | 100 scenes | 1,000 scenes | 10,000 scenes |
| --- | ---: | ---: | ---: |
| load-and-frame | 548.3 → 187.8 | 880.5 → 207.2 | 8571.0 → 699.3 |
| graph-local-render | 187.1 → 4.7 | 151.9 → 3.9 | 1525.7 → 7.0 |
| typing-one-character | 306.2 → 28.6 | 648.2 → 8.6 | 5660.2 → 13.5 |
| typing-commit | 124.6 → 51.7 | 579.8 → 58.5 | 6489.3 → 342.1 |
| selection | 107.2 → 58.8 | 511.4 → 30.2 | 6014.4 → 149.1 |
| group-move-20 | 221.5 → 163.4 | 910.4 → 104.2 | 9069.2 → 418.8 |
| outline-filter | 30.9 → 24.4 | 187.0 → 20.5 | 1618.3 → 15.4 |
| find-dialog-query | 56.3 → 25.5 | 183.3 → 12.8 | 1789.6 → 19.1 |
| undo-redo-pair | 227.9 → 300.3 | 1098.3 → 143.6 | 10813.0 → 986.0 |
| pan-and-frame | 21.1 → 9.0 | 154.8 → 5.6 | 1614.0 → 9.0 |
| zoom-and-frame | 73.2 → 10.0 | 490.1 → 6.0 | 5725.9 → 130.4 |
| graph-fit-render | 63.8 → 45.1 | 178.5 → 30.8 | 1555.4 → 32.8 |
| save-and-frame | 161.5 → 42.1 | 939.3 → 115.3 | 8808.0 → 684.3 |

| Managed allocation per operation (MB, before → after) | 100 scenes | 1,000 scenes | 10,000 scenes |
| --- | ---: | ---: | ---: |
| graph-local-render | 6.41 → 0.16 | 64.44 → 0.25 | 658.47 → 1.09 |
| typing-one-character | 15.25 → 0.63 | 150.97 → 0.78 | 1524.13 → 2.45 |
| typing-commit | 21.53 → 5.43 | 198.98 → 28.57 | 2009.86 → 264.00 |
| undo-redo-pair | 46.18 → 26.93 | 377.65 → 89.07 | 3714.23 → 721.82 |
| load-and-frame | 35.18 → 15.22 | 295.60 → 64.70 | 2960.37 → 529.18 |
| save-and-frame | 36.56 → 5.26 | 292.93 → 41.60 | 2889.25 → 405.04 |

| Process retention (MB, before → after) | 100 scenes | 1,000 scenes | 10,000 scenes |
| --- | ---: | ---: | ---: |
| Loaded managed | 34.7 → 22.8 | 185.1 → 60.5 | 1360.5 → 372.3 |
| After history managed | 79.2 → 32.0 | 281.4 → 76.3 | 2205.5 → 517.2 |
| After history private | 207.4 → 118.3 | 629.3 → 207.7 | 3757.6 → 836.7 |
| After history working set | 278.9 → 186.0 | 689.9 → 275.3 | 3756.5 → 822.5 |

Native Windows cross-check at 1,000 scenes (median ms):

| Operation | Before | After |
| --- | ---: | ---: |
| load-and-frame | 993.2 | 302.8 |
| graph-local-render | 130.9 | 5.4 |
| typing-one-character | 344.3 | 21.8 |
| typing-commit | 434.4 | 96.3 |
| selection | 360.7 | 51.0 |
| group-move-20 | 644.6 | 189.1 |
| outline-filter | 144.6 | 20.2 |
| find-dialog-query | 142.5 | 22.7 |
| undo-redo-pair | 771.9 | 353.1 |
| pan-and-frame | 343.6 | 21.8 |
| zoom-and-frame | 391.6 | 16.1 |
| save-and-frame | 669.3 | 100.4 |

A separate 1,000-scene run with 100 additional edit/commit cycles retained 102.4 MB managed, 252.9 MB private and 319.8 MB working set after collection. The final edit was verified in committed history. See the raw samples for variation between fresh processes.

## Practical story size and remaining costs

Use **about 1,000 scenes** as a starting recommendation for daily writing on comparable Windows hardware, with a few thousand responses and project text comparable to the 1.91 MB fixture. Native measurements here put typing at 22 ms, commits at 96 ms, selection at 51 ms and a group move at 189 ms; Undo/Redo pairs still take 353 ms. This is a practical budget, not a hard limit or a minimum hardware specification. Smaller projects leave more headroom for long text, many choices, complex logic and accessibility tools.

**10,000 scenes is a stress/import limit, not a responsiveness promise.** In the headless full-editor run, commit is 342 ms, selection 149 ms, group movement 419 ms, Undo/Redo 986 ms, loading 699 ms and saving 684 ms. Retained managed memory is 517 MB after the history workload; private memory is 837 MB. The near-limit fixture leaves little room before the 20 MiB file cap. The native cross-check covers 1,000 scenes only. Dense overlapping nodes, long viewport-crossing connections, the optional whole-story minimap and the visible Script view can cost much more. Native compositor/presentation, actual typing cadence, slow disks and lower-end machines were not established by these tests.

The biggest baseline costs were offscreen graph text/draw work and repeated whole-document serialization/control creation. A local graph render at 10,000 scenes allocated 658 MB; after culling/reuse it allocates 1.09 MB. A typed character fell from 1.52 GB to 2.45 MB allocated. Removing the hidden script tree and virtualizing outline rows also reduce loaded retention. History's additional retention signal fell from about 600 MB to 82 MB over the 20-cycle large-story workload. Compression trades some CPU for storage while preserving complete snapshots; the retained-memory results include other UI and renderer state, rather than isolating history alone.

Real commits and history replacement remain the main pauses: full JSON serialization/parsing, text diagnostics, navigation snapshots and selection/editor rebinding scale with document size. Link visibility still scans all edges and builds an ID dictionary each frame. Core separation and snapshot semantics remain intact. Reversible command history, spatial indexing, background validation and visible Script virtualization remain possible follow-up work requiring more focused evidence.

Results are directional. The 100-scene Undo/Redo median rose from 228 ms to 300 ms in this three-sample run despite lower allocations; no across-the-board speedup is claimed. Fresh-process variation is substantial (compare headless and native rows and raw samples). The large reductions in allocation and multi-second large-story pauses are much stronger evidence than small timing differences.

Verification: **773 regression checks passed**, all nine theme/interface-size combinations were reviewed in normal and compact windows, Windows x64 self-contained publishing passed, and Packaging/Test-WindowsPackage.ps1 passed native Welcome/project startup plus complete PNG/script exports. Native OS picker interaction and screen-reader testing remain outside these automated checks.

## Changes supported by the measurements

A five-sample cache probe at 1,000 scenes measured the same local graph after explicitly clearing layouts versus reusing them: **6.71 ms / 414 KB allocated cold** and **5.07 ms / 255 KB allocated warm**. Culling stayed enabled in both cases. This isolates a benefit from bounded layout reuse beyond the much larger culling gain; see `docs/performance/cache.json`.

- Cull offscreen nodes before plain-text conversion/layout/drawing, and conservatively cull links without excluding viewport crossings. Export explicitly draws the whole story.
- Bound and reuse text layouts across frames. Content, history/reload, palette and interface-scale invalidation retain current text/colours without unbounded story-sized caches.
- Virtualize outline rows; preserve the full scene set, filtering, duplicate-ID indices, keyboard/range selection and hidden selected scenes.
- Remove document serialization and word splitting from each typing status update; skip idle UI commits, reconcile mutable-model edits at explicit command/file boundaries, and refresh diagnostics once per bulk operation.
- Build script content only when shown and release it when hidden. Visible Script remains a whole-story control tree and is a remaining limitation.
- Compress archived complete JSON snapshots. The baseline's history retention justified this bounded storage change; reversible command history was not introduced. Schema, extensions, groups/templates, 100 Undo entries and Redo semantics stay lossless. Parsing/serialization/validation on real commits still scale with document size.

Correctness checks deliberately contain no timing thresholds. They cover crossing curves, complete exports, cache bounds/invalidation, recycled outline rows, exact dirty queries, statistics, scalar picker mutations, public mutable-model reconciliation, no-op/branched/bounded history and existing save/recovery/conflict flows. All themes/interface sizes and compact layouts are rendered and reviewed. The Stage 2 measurements above predate Stage 3; runtime adapters and release publishing remain separate work.

## Stage 3 protections

Repeatable testing preserves the measured editor protections: coverage uses hashed scene/response identity sets in the existing culled canvas path, adds no text-layout cache or document serialization to graph interactions, and is excluded from complete exports. Scenario editing uses the existing compressed complete-snapshot history. Runs capture detached definitions/story data and execute Core replay/analysis off the UI thread. Result/path lists are virtualized; forms and evidence remain scrollable in compact layouts. Interactive runtime history retains trace/event/transcript lengths rather than copying growing evidence lists on every transition. Automated replay records evidence without retaining unused Back snapshots for each operation. Analysis forks share one isolated story/index, copy runtime variables/pages only, and retain no history/evidence tree; both exploration and queued branches are bounded and cancellable.

The original Stage 2 correctness regressions remain in the full suite, alongside deterministic seed/history restoration, unchanged project/selection/viewport/Redo checks, complete exports with overlays, bounded cycles and configured dead ends. Correctness tests add no timing thresholds. These are preservation checks, not new before/after performance measurements or a larger practical story-size claim. Reuse the commands above if measuring a changed workload. See [STORY_TESTING.md](STORY_TESTING.md) for the scenario limits and CLI fixture commands.
