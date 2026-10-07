# Runtime handoff, contract v1

The first destination is the existing **xLostProHome Blazor Dialogue Machine player**, located at `C:\Users\Ben\Documents\Projects\xLostProHome\xLostProHome\Ecosystem\Web\DialogueMachine`. Its actual source was inspected at clean Git revision `58ebc2a8dd4eb4749b748975aced35fb30a5be60` of `https://github.com/xLostPro/xLostProHome`. `Integration/WebsitePlayer/Upstream` contains the unchanged component, Razor UI, models, logic service and their small service/command dependencies. `upstream.json` records SHA-256 for every file; regression checks reject any source drift. These are the user's website sources, not a recreation of desktop Core.

`RuntimePlayer` is an explicit adapter subclass of that actual component. It invokes its `SimSelectChoice`, `SimGoToNext`, `ParseDialoguePages`, `EvaluateRequirements` and `ProcessActions` methods. The website project has **no reference to Core or Avalonia**. The sample uses the website models and engine in a .NET Blazor Server host. Core is used only on the desktop/export/testing side. Neither the upstream website checkout nor its legacy behavior is changed.

## Reproduce

Requires .NET SDK 10 (tested with 10.0.204), .NET/ASP.NET Core 10.0.12 and Avalonia 12.1.3 for desktop tests. The sample pins its exercised browser client assets (`Microsoft.AspNetCore.App.Internal.Assets`) at 10.0.8 rather than following the build SDK patch. The inspected website project used `Microsoft.AspNetCore.Components.WebAssembly` 10.0.7. Its C# and Razor sources are compiled here against ASP.NET Core 10; **the deployed website and its WebAssembly/browser runtime were not exercised**. No Node packages, web accounts or game engine are required.

From the repository root:

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
dotnet restore DialogueMachine.slnx --configfile NuGet.Config --locked-mode
dotnet run --project CLI/DialogueMachine.Cli.csproj -c Release -- runtime-export tests/Fixtures/runtime/story.dialogue.json --seed 42 --max-operations 1000 --output artifacts/harbour.runtime.json
dotnet run --project tests/DialogueMachine.Tests.csproj -c Release -- --runtime-checks artifacts/runtime
dotnet run --project Integration/Sample/Sample.csproj -c Release -- --urls http://127.0.0.1:5238
```

Open `http://127.0.0.1:5238`. Choose **Enter, Ada → Next page → Enter anyway → Buy lantern → Continue**. The host unlocks `asked`, `gate_exit`, `purchase` and `ended` achievements; `arrived(harbour)` updates a quest; entering `harbour_shop` opens a stock panel; `close_shop` closes it. **Wait** revisits the gate and runs its exit actions once per choice. **Go home** demonstrates continue fallback when every reply is hidden or disabled. Restart creates a fresh configured session and fresh sample host state. These host effects are deliberately in-memory demonstrations.

The sample's copied `handoff.json` is committed alongside its authored project, website export, case operations and expected frames in `tests/Fixtures/runtime`. Git normalizes fixture file formatting to LF; the companion's embedded `StoryJson` and its hash retain the exact legacy export bytes. Replace the sample's output-directory `handoff.json` with a CLI-produced companion to try another story; restart the host. Build before starting the sample, and stop it before rebuilding on Windows.

The CLI emits the handoff to stdout and optionally writes `--output` atomically. Exit 0 means exported, 2 means usage/input/write/incompatibility error, 5 means cancelled. It refuses its input project as the output path. Seeds accept signed int32; limits accept 1–10,000. Ctrl+C cancels the CLI. Existing validate/replay commands remain unchanged.

```powershell
dotnet run --project tests/DialogueMachine.Tests.csproj -c Release -- artifacts
./build.ps1 -Runtime win-x64
./Packaging/Test-WindowsPackage.ps1
./Packaging/Test-RuntimeSample.ps1
```

Windows CI runs the independent compatibility checks inside the full harness, exports through the actual CLI, builds/starts the sample, checks its HTTP page and retains runtime JSON evidence. Intentional fixture regeneration uses `--write-runtime-fixtures`; review every changed expected outcome rather than regenerating to accept an unexplained failure.

Local verification passed 1,036 full regression checks (126 runtime checks), Windows publishing and native Welcome/project/PNG/script package smoke checks. The running Blazor Server sample was exercised in the browser through the complete shop and fallback paths, including restart and ending callbacks. All nine Dark/Light/High contrast × 100/115/130% combinations were captured and reviewed at 1280×900 and 390×700 viewports, with no horizontal overflow. Browser proof is in ignored `artifacts/runtime-browser`; the deterministic compatibility evidence is reproducible from the committed fixtures. Browser testing covers this sample host, not the original WebAssembly/deployed site.

## Configuration and versioning

The companion envelope uses `SchemaVersion: 1` and `Contract: "dialogue-machine.website.v1"`:

| Field | Meaning |
| --- | --- |
| `StoryJson` | Exact string produced by the unchanged `ProjectStore.WebExport`; parse with the website's `DialogueRoot` model |
| `StorySha256` | Lowercase SHA-256 of UTF-8 `StoryJson`, without BOM; detects mismatched story/configuration, not an authenticity signature |
| `StartingScene` | Existing exported entry, also the first scene |
| `Seed` | Signed int32 seed, explicit even when zero |
| `NumericValues` | Complete discovered active numeric registry, using `aff:`/`inv:` keys, including inline-only and starting-only variables |
| `TextValues` | Explicit text substitution dictionary, including legacy text IDs that shadow numeric IDs |
| `MaxOperations` | Per-session bound, default 1,000, maximum 10,000 |

Groups, templates and inactive scenario definitions do not discover runtime variables. Initial inventory is normalised to zero when negative, matching existing desktop behavior. Numeric IDs must have a lowercase `aff:` or `inv:` prefix and a nonempty ID consisting of letters/digits/underscores/dots/hyphens. Legacy malformed IDs stay openable in desktop format 1; runtime export identifies the ID and asks for a rename. Duplicate/empty response GUIDs also require repair before export.

**No desktop schema migration is needed.** Format 1 and missing-version website imports retain their previous defaults, including groups/templates/scenarios and extension data. Configuration lives outside the document, history, selection, recovery and conflict-safe save paths. Legacy website exports retain their original numeric-enum schema and omit the companion. Unsupported companion schemas/contracts stop before callbacks and name the matching adapter/export needed. Future semantics require a new contract identifier, explicit migration and fixtures; silently treating a newer contract as v1 is prohibited.

## Narrative contract

| Area | Contract v1 |
| --- | --- |
| Start/identity | Start at `StartingScene`; first scene in the unchanged website export. Scene IDs and response GUIDs are exact/case-sensitive; blank, duplicate and reserved scene identities are rejected. Response GUIDs are unique within a scene. |
| Delivery | Resolve conditions and substitutions before splitting on CR/LF; remove empty lines but preserve whitespace-only lines. `SayItAll=true` delivers pages in authored order. Finish pages before choosing. No resolved lines delivers `(No dialogue)`. |
| Choice ordering | Preserve authored order. Hide/Disable/Always are numerically **0/1/2**. Unmet Hide is omitted; unmet Disable is visible but unusable; Always stays usable despite unmet valid requirements. Choices can follow random delivery and Shop scenes. |
| Fallback | Null, empty or whitespace response destinations inherit `DefaultNextId`. After the last page, Continue is available only with no usable response and a nonblank continue destination. Missing targets fail before any action; unset authored endings require repair. The low-level desktop `Continue()` API is permissive; integration hosts use `CanContinue`, matching the desktop UI. |
| Ending | `[EXIT]` is the explicit authoring ending. Run response actions, then scene exit actions, then terminate; record source scene and chosen GUID (null for Continue). No extra enter/Shop hook occurs. Blank low-level transitions also terminate, but the exporter rejects missing authored destinations. |
| Requirements | Ordered AND of typed Affinity/Inventory requirements; missing numeric values are configuration errors. Signed int32 thresholds; `== != > < >= <=`. Invalid types/operators/targets/thresholds are rejected even on Always responses. |
| Actions | Ordered response list, then ordered source-scene exit list, including self-loops, then transition and target delivery. Entering a scene never runs exit actions. Each numeric action is visible to the next action/hook. Event/Script hooks occur at their exact action position. Shop occurs on entering a Shop scene **after its first delivered text**, before any response. |
| Numbers | Affinity: −2,147,483,648 through 2,147,483,647; inventory: 0 through 2,147,483,647. Add using a wider intermediate and saturate after each action. Requirement thresholds and action amounts are signed int32. No wrapping or floating point arithmetic. |
| Random | SplitMix64: sign-extend the int32 seed to uint64. Add `0x9E3779B97F4A7C15`; mix with xor-shifts 30/27/31 and multipliers `0xBF58476D1CE4E5B9`/`0x94D049BB133111EB`, modulo 2^64. For count n reject values below `(-n mod 2^64) mod n`, then select `value mod n`. Draw once per nonempty random visit and explicit reroll. Retain candidate index on refresh, temporarily clamp if candidates shrink; an initially empty visit draws when candidates first appear. No draw for empty delivery. |
| Inline conditions | Case-insensitive tags, signed int32 thresholds, up to 64 nested blocks, at most one else per block. Resolve typed keys first, then unprefixed inventory before affinity. Preserve selected-branch whitespace. Inspect all branches structurally before executing. IDs themselves remain case-sensitive. |
| Substitutions | Single pass **after** conditions. `{id}` resolves exact text key first, then exact numeric key with invariant decimal rendering; unknown placeholders remain literal. Text values are not interpreted again as conditions/substitutions or executable HTML. Formatting tags remain delivered text, matching the upstream plain-text player; desktop rich presentation is outside this text comparison. |
| Live configuration | `SetNumeric`/`SetText` require configured IDs, refresh current text without a new random draw; inventory floors at zero. Explicit Reroll redraws; Refresh retains it. Desktop Back/restart remain its reversible logged preview services; host effects are not reversible. |

## Explicit differences from the legacy website

The adapter is opt-in. Opening plain website JSON in the original website retains the behaviors below. Using the companion requires installing/invoking `RuntimePlayer` and rendering its `Choices`/`CanContinue` API; the original Razor sandbox does not acquire these semantics automatically.

| Inspected website behavior | Explicit adapter correction |
| --- | --- |
| Sandbox starts at index zero and discovers only some typed references at zero | Companion includes entry and the complete active numeric/text starting registry |
| Razor ignores Hide/Always, disables all unmet responses, suppresses responses for random delivery/Shop, offers no fallback when all responses are locked | Contract API exposes ordered visible/enabled choices and condition-aware Continue; sample renders this API |
| Response fallback uses `??`, so empty/whitespace targets terminate | Normalise whitespace fallback before calling real `SimSelectChoice` |
| Response actions run before discovering a missing target | Validate destinations before callbacks/actions |
| Scene exit actions skipped on self-loop | Adapter applies response/scene lists once before invoking the real transition with duplicate legacy actions suppressed |
| Int32 arithmetic wraps | Calculate a saturating delta per action, then invoke real `ProcessActions` |
| Time-seeded `new Random()` | Seeded portable stream selects the visit candidate; the real page parser receives the selected, ordered delivery |
| One regex pass, unsigned thresholds, nested blocks fail, branch whitespace trimmed | Structural parser selects branches independently and uses real `EvaluateRequirements` for each numeric gate |
| No substitutions; blank text produces `[Empty]`; empty requirement IDs ignored | Explicit substitution/fallback normalization and actionable structural validation |
| Event/Script ignored; Shop only displays an exit button | Ordered typed host callbacks with immutable snapshots and observable sample effects |

The unchanged source and characterization tests retain these differences as evidence. The adapter hands detached resolved/encoded pages to the real website page parser and transition controller; a lossless boundary escape protects literal condition tags returned by substitutions and whitespace-only pages. It never mutates the authored story or invokes desktop Simulation internally.

## Hosts, limits and evidence

Implement `IRuntimeHost.Event(Hook)`, `.Script(Hook)` and `.Shop(Hook)`. Hook fields contain type, ID, literal parameter, source/entered scene and read-only snapshots of numeric/text state at that exact point. Use IDs to select trusted host code and treat parameters as data. `SampleHost` demonstrates achievements, quest updates and a stock panel. Desktop playtests continue to log hooks only; no UI service or host callback was added to Core.

Callbacks are synchronous and must return promptly. They cannot re-enter the player. A callback failure reports its type/ID/scene, retains partial evidence, and makes the session unusable to avoid accidentally repeating already-applied external effects. External host effects cannot be rolled back; a production host needs its own idempotency/persistence policy. Callbacks do not modify story variables; explicit live numeric/text APIs are separate operations. A production asynchronous shop/network service should complete outside the callback and then schedule the next operation.

`MaxOperations` counts page/choice/continue/live-edit/reroll/refresh calls admitted to the session. Every admitted operation checks cancellation before changing values or invoking callbacks. Rejected selections do not consume the budget. Limit/cancellation preserves the last coherent frame. Parse/construction are bounded to 10,000 scenes and 20 MiB of story JSON, with 64 levels of JSON/condition nesting. Cancellation does not forcibly interrupt a callback, parsing or one synchronous operation. Restart means constructing a new session; never retry a failed host session in place.

The committed five paths contain 40 initial/post-operation frames. Tests execute the **same exported story** in desktop preview and the independent website source, compare against committed expected frames, and check delivered text, ordered visibility/enabled state, transitions, final/live numeric and text state, hook order and hook snapshots, random state and ending scene/response. Tests include seed zero/negative/int32-minimum, fallback, nested signed conditions, substitutions that shadow numbers or contain literal condition tags, empty delivery, both affinity limits, inventory floor/ceiling and bounded self-loops. Differences produce `*-difference.json` with case/operation/desktop/website/expected frames and repair guidance. `runtime-results.json` retains successful evidence. The adapter assembly reference and upstream hash checks guard against replacing the real runtime with a Core wrapper.

This verifies those paths in the source-backed .NET host. It does **not** prove exhaustive cyclic behavior, all Unicode/formatting/presentation combinations, deployed website/WebAssembly behavior, asynchronous host integration, Unity/other engines, or native Mac/Linux support. Distribution expansion and release publishing remain separate. Desktop version 1.1.1 is unchanged; only the new companion/runtime contract is versioned here.
