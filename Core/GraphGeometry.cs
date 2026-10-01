namespace DialogueMachine.Core;

// Shared by automatic layout and the canvas so choice-heavy nodes keep their spacing.
public static class GraphGeometry
{
    public const double NodeWidth = 270;
    public const double ChoiceSpacing = 34;
    public static double NodeHeight(DialogueState state) => 158 + state.Choices.Count * ChoiceSpacing;
}
