# Engine Integration Contract

This contract defines the shared data formats, runtime execution rules, runner API surface, live-link protocol, and compatibility guarantees for all Dialogue Machine engine adapters (Unity, Unreal Engine, Godot).

Core (`Core/Simulation.cs` and `Core/DialogueEngine.cs`) serves as the reference implementation. If an engine port disagrees with Core, the port is wrong.

---

## 1. Handoff Format

Engine integrations receive compiled stories through `RuntimeHandoff` (serialized as `*.runtime.json`). The schema is defined in `Core/RuntimeExport.cs`.

### Handoff Fields

| Field | Type | Description |
|---|---|---|
| `SchemaVersion` | `int` | Integer schema version. Currently `1`. |
| `Contract` | `string` | Target format identifier (`"dialogue-machine.website.v1"`). |
| `StoryJson` | `string` | Escaped JSON string containing the story definition `{ "ListOfCharacterStates": [...] }`. |
| `StorySha256` | `string` | Lowercase hexadecimal SHA-256 hash of `StoryJson` (UTF-8 bytes). Used to detect story changes. |
| `StartingScene` | `string` | ID of the entry scene. |
| `Seed` | `int` | Random seed for seeded non-deterministic behaviour (`SayItAll = false`). |
| `NumericValues` | `Dictionary<string, int>` | Initial values for numeric variables (`aff:<id>` and `inv:<id>`). |
| `TextValues` | `Dictionary<string, string>` | Initial values for text variables (`<id>` -> `<string>`). |
| `MaxOperations` | `int` | Bounds-checking limit on total simulation steps (1 to 10,000; default 1,000). |

### Story JSON Structure (`StoryJson`)

`StoryJson` parses into a root object containing `ListOfCharacterStates` (array of scenes).
Every engine parser must preserve website compatibility and **ignore unknown fields**:

- **Scenes:**
  - `State` (`string`): Unique scene identifier. `[EXIT]` is reserved for ending conversations.
  - `Text` (`string`): Authored scene text (supports inline conditionals, variable substitutions, and rich text tags).
  - `SayItAll` (`bool`): If `true`, splits lines into sequential pages. If `false`, chooses one random line.
  - `Type` (`string`): Scene kind (`"Dialogue"` or `"Shop"`).
  - `ShopData` (`object`): Object with `ShopId` (`string`), used when `Type == "Shop"`.
  - `Choices` (`array`): Response choices.
  - `DefaultNextId` (`string`): Fallback destination scene ID.
  - `DefaultAfterActions` (`array`): Scene exit actions executed when transitioning out.
  - `Metadata` (`Dictionary<string, string>`): Authoring and game metadata (e.g., portrait, audio, animation tags). The speaker is `Metadata["speaker"]` (default `"Character"`) and the title is `Metadata["title"]` (default: the scene ID).
- **Choices:**
  - `InternalId` (`Guid` / `string`): Stable unique choice identifier.
  - `ButtonText` (`string`): Authored response text (resolves substitutions and conditionals).
  - `NextStateId` (`string`): Destination scene ID. If empty, falls back to the scene's `DefaultNextId`.
  - `Visibility` (`int`): Visibility mode enum (`Hide = 0`, `Disable = 1`, `Always = 2`).
  - `Requirements` (`array`): Conditions required to enable the choice.
  - `RequirementMatch` (`string`, optional): `"Any"` makes one passing requirement enough; anything else (or absent) means all must pass. An `"Any"` choice with no requirements is available. Engine handoffs keep it; the website file never carries it (Core splits such a choice into one choice per requirement).
  - `AfterChoiceActions` (`array`): Actions executed immediately upon selecting this choice.
- **Requirements:**
  - `Type` (`string`): `"Affinity"` or `"Inventory"`.
  - `TargetId` (`string`): Variable name (without `aff:` / `inv:` prefix).
  - `Operator` (`string`): `"=="`, `"!="`, `"<"`, `"<="`, `">"`, `">="`.
  - `Value` (`string`): Integer threshold, written as a string. A requirement with any other type, an empty target or a non-integer value is unmet.
- **Actions:**
  - `Type` (`string`): `"Affinity"`, `"Inventory"`, `"Set"`, `"Toggle"`, `"Event"`, or `"Script"`. Runners ignore any other type, because newer exports may add them. Core's validation rejects them, so they only reach a runner from a newer exporter.
  - `TargetId` (`string`): Target variable name, event name, or script function identifier.
  - `Amount` (`int`): Value to add (for numeric variables); for `Set`, the value to write (flags use 0 or 1). `Set` and `Toggle` target `aff:<TargetId>`; `Toggle` writes 1 if the value is 0, otherwise 0.
  - `Parameter` (`string`): Argument string (for events and scripts).

---

## 2. Runtime Semantics

Engine runners must mirror Core semantics without deviation:

### Variables
- Numeric keys use the prefix `aff:<id>` for Affinity and `inv:<id>` for Inventory.
- Initial values are populated from `NumericValues`.
- Inventory values (`inv:`) are clamped to non-negative integers (`value >= 0`).
- Arithmetic addition and subtraction saturate at 32-bit signed integer boundaries (`[int.MinValue, int.MaxValue]`).

### Text Resolution
1. **Conditionals:**
   - Resolve `[if key op N]...[else]...[/if]` **innermost-first** (blocks containing no nested `[if` or `[/if]`), case-insensitively, for up to 64 passes.
   - Operators: `==`, `!=`, `<`, `<=`, `>`, `>=`.
   - Variable lookup in conditionals: a bare key `x` first looks up `x`, then `inv:x`, then `aff:x`, defaulting to `0` if not found.
   - If condition is met, take the text before `[else]`; otherwise take the text after `[else]` (or empty string if no `[else]`).
2. **Substitutions:**
   - After conditional passes finish, resolve `{key}` placeholders.
   - Look up `key` in text variables (`TextValues`), then numeric variables (`NumericValues` converted to invariant string).
   - If neither exists, leave `{key}` intact.

### Pages & Lines
- Split resolved scene text on `\r` and `\n`, discarding empty lines. A whitespace-only line is kept as a page.
- If no lines remain, display `"(No dialogue)"`.
- If `SayItAll` is `false`:
  - Select one line using SplitMix64 seeded with `(ulong)(long)Seed`.
  - Advance the random state only when picking a new random line (not on preview refresh).
  - State advances as:
    ```
    State += 0x9E3779B97F4A7C15
    z = State
    z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9
    z = (z ^ (z >> 27)) * 0x94D049BB133111EB
    z = z ^ (z >> 31)
    ```
    With rejection threshold `(0UL - (ulong)count) % (ulong)count` to prevent modulo bias.
- If `SayItAll` is `true`:
  - Present lines as sequential pages (`PageIndex = 0, 1, ...`).

### Choice State & Visibility
- `Visibility` numeric ordering: `Hide = 0`, `Disable = 1`, `Always = 2`.
- When `Visibility == Always (2)`: Choice is always visible and enabled regardless of requirements.
- When requirements are evaluated:
  - If all requirements are met: Choice is visible and enabled.
  - If any requirement fails:
    - If `Visibility == Disable (1)`: Choice is visible but disabled, providing a disabled reason message.
    - If `Visibility == Hide (0)`: Choice is hidden (excluded from the active choice list).

### Flow & Navigation
- `CanContinue`: `true` if `PageIndex < PageCount - 1` (more pages remain), OR if there are zero enabled choices and `DefaultNextId` is non-empty.
- `Continue()`: advances `PageIndex` if more pages remain; otherwise transitions to `DefaultNextId` (running the scene's `DefaultAfterActions`), even when choices are available.
- `Choices` lists visible choices while pages remain, but `Choose` is refused until the last page. Choice text is resolved rich text, like `CurrentPage`.
- A destination that names a missing scene is refused before any action runs.
- `Choose(choiceId)`:
  - Destination is `NextStateId` if non-empty, otherwise `DefaultNextId`.
  - Transition order:
    1. Execute `AfterChoiceActions` from the chosen response.
    2. Execute `DefaultAfterActions` from the current scene.
    3. Transition to destination scene.
  - If destination is `[EXIT]`, empty string, or null: end conversation (`IsEnded = true`).

### Actions & Hooks
- `Affinity` / `Inventory`: Add `Amount` to `Variables[Key]`, clamped to `>= 0` for inventory and saturated at 32-bit bounds.
- `Event` / `Script`: Raise callback or event to the host game with `(Type, TargetId, Parameter, Amount)`.
- `Shop`: Entering a scene with `Type == "Shop"` raises `ShopOpened(ShopId)` using `ShopData.ShopId`.

### Rich Text Formatting
- Supported markup tags: `[b]`, `[/b]`, `[i]`, `[/i]`, `[u]`, `[/u]`, `[colour=X]`, `[/colour]`. The spelling `[color]` is not a tag and stays in the text.
- `PlainText` strips these tags case-insensitively.

---

## 3. Runner API

Each engine adapter implements the same core runner concepts using engine-idiomatic naming:

| Member | Unity (C#) | Unreal (C++) | Godot (GDScript) | Description |
|---|---|---|---|---|
| Load story | `Load(string handoffJson)` / `Load(Handoff)` | `Load(const FString& HandoffJson)` / `Load(const FDialogueHandoff& Handoff)` | `load_story(handoff_json: String)` / `load_story(handoff: DialogueStory)` | Parses handoff, validates schema version, and retains story. |
| Start story | `Start(string sceneId = null, int? seed = null, numeric = null, text = null)` | `StartDialogue(const FString& SceneId = TEXT(""), int32 Seed = 0)` | `start(scene_id: String = "", seed: Variant = null)` | Resets variables and enters the initial scene. Optional starting `numeric` / `text` overrides are applied before the first scene resolves (used by `play.from`). |
| Current scene ID | `CurrentSceneId` (`string`) | `GetCurrentSceneId()` (`FString`) | `current_scene_id` (`String`) | Active scene identifier. |
| Speaker | `Speaker` (`string`) | `GetSpeaker()` (`FString`) | `speaker` (`String`) | Speaker name of active scene. |
| Title | `Title` (`string`) | `GetTitle()` (`FString`) | `title` (`String`) | Title of active scene. |
| Is ended | `IsEnded` (`bool`) | `IsEnded()` (`bool`) | `is_ended` (`bool`) | True if conversation has ended. |
| Current page text | `CurrentPage` (`string`) | `GetCurrentPage()` (`FString`) | `current_page` (`String`) | Rich text content of current page. |
| Plain text | `CurrentPagePlain` (`string`) | `GetCurrentPagePlain()` (`FString`) | `current_page_plain` (`String`) | Text with rich formatting stripped. |
| Page index | `PageIndex` (`int`) | `GetPageIndex()` (`int32`) | `page_index` (`int`) | Zero-based index of current page. |
| Page count | `PageCount` (`int`) | `GetPageCount()` (`int32`) | `page_count` (`int`) | Total number of pages in current scene. |
| Available choices | `Choices` (`IReadOnlyList<Choice>`) | `GetChoices()` (`TArray<FDialogueChoice>`) | `get_choices()` (`Array[DialogueChoice]`) | Active visible choices `{ Id, Text, Enabled, DisabledReason }`. |
| Can continue | `CanContinue` (`bool`) | `CanContinue()` (`bool`) | `can_continue` (`bool`) | True if advance / continue is available. |
| Continue | `Continue()` | `Continue()` | `continue_dialogue()` | Advances page or proceeds to default destination. |
| Choose response | `Choose(string choiceId)` / `Choose(Guid)` | `Choose(const FGuid& ChoiceId)` | `choose(choice_id: String)` | Selects a choice. Throws if disabled or unknown. |
| Variables | `Variables` (`IDialogueVariables`) | `GetVariables()` (`IDialogueVariablesInterface*`) | `variables` (`DialogueVariables`) | Access to numeric and text variable store. |
| Scene metadata | `CurrentMetadata` (`IReadOnlyDictionary<string, string>`) | `GetCurrentMetadata()` (`TMap<FString, FString>`) | `current_metadata` (`Dictionary`) | Read-only scene metadata (portrait, voice, anim). |
| Hot reload | `ReplaceStory(Handoff)` | `ReplaceStory(const FDialogueHandoff&)` | `replace_story(handoff: DialogueStory)` | Replaces story in place; retains variable state and re-enters the scene at the clamped page. If the scene is gone, raises a warning and ends. |
| State hooks | Events (`LineShown`, `ChoicesShown`, `ActionRaised`, `ShopOpened`, `VariableChanged`, `Ended`, `Warning`) | Delegates (`OnLine`, `OnChoices`, `OnAction`, `OnShop`, `OnVariableChanged`, `OnEnded`, `OnWarning`) | Signals (`line_shown`, `choices_shown`, `action_raised`, `shop_opened`, `variable_changed`, `ended`, `warning`) | Callbacks for presentation and game hooks. |
| Save / Restore | `SaveState()`, `RestoreState(string json)` | `SaveState()`, `RestoreState(const FString& Json)` | `save_state()`, `restore_state(json: String)` | Serializes / deserializes dialogue progress. |

---

## 4. Save State Schema

Save states capture complete mid-dialogue progress as a JSON string:

```json
{
  "sceneId": "scene_lighthouse_entry",
  "pageIndex": 0,
  "randomState": "11400714819323198485",
  "randomLine": 2,
  "numeric": {
    "aff:trust": 3,
    "inv:lantern": 1
  },
  "text": {
    "player_name": "Ada"
  },
  "storySha256": "4b227777d4dd1fc61c6f884f48641d02b4d121d3fd328cb08b5531fcacdabf8a"
}
```

Fields:
- `sceneId` (`string`, nullable): Current scene ID (`null` if conversation ended).
- `pageIndex` (`int`): Current page index.
- `randomState` (`string`): Internal SplitMix64 RNG state as a decimal `uint64`. It's a string because JSON numbers can't hold 64 bits in every engine.
- `randomLine` (`int`): Index of the line picked for this scene visit, or `-1` if none was picked. Restoring re-resolves the text and keeps this index.
- `numeric` (`map<string, int>`): Current numeric variables.
- `text` (`map<string, string>`): Current text variables.
- `storySha256` (`string`): Hash of the story version when saved.

---

## Conformance Fixtures

`Engines/Conformance/*.json` holds one case per file: `{ name, handoff, inputs, expected }`. `expected[i]` is the snapshot after `inputs[i]`, and its `events` are the ones raised by that input. Core's `Simulation` produces every snapshot, and the harness fails if the files are stale. Regenerate them with:

```powershell
dotnet run --project CLI/DialogueMachine.Cli.csproj -c Release -- conformance Engines/Conformance
```

---

## 5. Live-Link Protocol (v1)

The live link is a local WebSocket bridge between Dialogue Machine (desktop server) and engine editor plugins / dev builds (clients).

### Operational Constraints
- **Dev-only:** Compiled out of shipping games:
  - Unity: `#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !UNITY_WEBGL`
  - Unreal: `#if !UE_BUILD_SHIPPING`
  - Godot: `OS.is_debug_build()`
- **Localhost only:** Server binds strictly to `127.0.0.1:<port>/dialogue-machine/` (default port 47615; probes up to +10).
- **Security:**
  - Server immediately rejects any connection with an `Origin` header (blocks browser exploits).
  - Ephemeral 128-bit random token generated per app run.
  - Client must send `hello` with matching `token` within 5 seconds.
  - Maximum message frame size: 16 MB. Max concurrent clients: 8.
- **Discovery file:** Written on server start, deleted on shutdown:
  - Path: `<LocalApplicationData>/DialogueMachine/livelink.json`
    - Windows: `%LOCALAPPDATA%\DialogueMachine\livelink.json`
    - macOS: `~/Library/Application Support/DialogueMachine/livelink.json`
    - Linux: `~/.local/share/DialogueMachine/livelink.json`
  - Payload: `{ "port": 47615, "token": "...", "pid": 1234, "appVersion": "1.4.0", "protocol": 1 }`
  - The token is a secret. On macOS and Linux only the current user can read the file. A file left by a crash just fails to connect, so treat that like Dialogue Machine not running.
- **Non-blocking:** Gameplay never stalls on socket operations; sends are fire-and-forget.

### Message Frame
One JSON object per text WebSocket frame:
```json
{
  "protocol": 1,
  "type": "message.type",
  "id": "optional-uuid",
  "payload": { }
}
```

### Message Types

| Direction | `type` | Payload Fields | Description |
|---|---|---|---|
| Engine → DM | `hello` | `token, engine, engineVersion, pluginVersion, protocol, schemaVersion` | Client handshake on connect. |
| DM → Engine | `welcome` | `appVersion, protocol, schemaVersion, contract, projectName, updateUrl` | Server handshake response. `updateUrl` is `null` unless `pluginVersion` is older than `appVersion`. |
| DM → Engine | `story.updated` | Full `RuntimeHandoff` plus `ProjectName` | Sent on connect, on save, on Push update, before `play.from`, and on each valid edit (500 ms debounce) when Live edits is on. |
| DM → Engine | `story.invalid` | `diagnostics[]` of `{ stateId, severity, message }` | Diagnostics when story validation fails. Engine preserves last good story. |
| Engine → DM | `story.request` | `{}` | Requests current story handoff from DM. |
| DM → Engine | `play.from` | `sceneId, seed, numeric{}, text{}` | Command to start engine playback from a given scene. |
| Engine → DM | `runtime.state` | `runnerId, runnerName, projectName, storySha256, status, sceneId, pageIndex, choiceId, numeric{}, text{}` | Live follow tracking (`status`: `"started"`, `"step"`, `"ended"`). |
| DM → Engine | `runtime.setVariable` | `runnerId, key, number?, text?` | Variable override sent from DM follow pane. |
| Engine → DM | `runtime.event` | `runnerId, type, targetId, parameter` | Event/script raised during dialogue execution. |
| Engine → DM | `editor.reveal` | `sceneId` | Request DM editor to centre and select a scene. |
| Both | `ping` / `pong` | `{}` | Heartbeat sent every 10 s. Connection closed after 30 s silence. |
| Both | `error` | `code, message` | Error reporting. Server codes: `unauthorized`, `incompatible`, `too-large`. The server closes the connection after sending one. |

`runnerId` is a string, unique within one connection. `runtime.setVariable` sends either `number` or `text`. Every field except `runnerId` and `status` in `runtime.state` is optional. `runtime.state` carries positions, not text, so Dialogue Machine shows the line from the open story.

---

## 6. Versioning and Compatibility

- **Unified Versioning:** All engine plugins share the root Dialogue Machine version from `DialogueMachine.csproj`.
- **Breaking vs. Additive Changes:**
  - `RuntimeHandoff.SchemaVersion` and live-link `protocol` are integers.
  - **Additive changes do not bump version integers.** New model fields, new action types, and new message types must be ignored by older plugins without throwing errors.
  - Breaking changes bump the integer; plugins reject unsupported schemas with an actionable update prompt.
- **Update Workflow:**
  - Handshake compares `pluginVersion` against `appVersion`.
  - When `pluginVersion < appVersion`, the plugin displays an Update action:
    - Unity: single-click update via Unity Package Manager (UPM git URL pinned to `#v<version>`).
    - Unreal / Godot: opens the release download page (`welcome.updateUrl`).
