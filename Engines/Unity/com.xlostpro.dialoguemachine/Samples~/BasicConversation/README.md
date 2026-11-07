# Basic Conversation Sample

This sample shows how to run dialogue in Unity without requiring any authored prefabs, scenes, or assets.

## How to Run

1. Open or create an empty scene in Unity.
2. Create an empty GameObject (e.g. `DialogueSample`).
3. Add the `BasicConversationSample` component to it.
4. (Optional) Assign a custom `DialogueAsset` imported from `*.runtime.json` into the `Story Asset` slot. If left blank, a default interactive story will play.
5. Press **Play**.

## Input and look

- **Input:** the sample sends Enter/Space to `view.Submit()` and the arrow keys to `view.MoveSelection()`. In your game, call these from your own input actions, or assign the view's **Submit Action** and **Navigate Action** when the Input System is installed.
- **Look:** drop `WarmDialogue.uss` into the sample's **Style Sheet** slot (or the view's own **Style Sheet**) to see the box restyled: moved to the top, with new colours and a highlighted choice. Copy it as a starting point for your own.
