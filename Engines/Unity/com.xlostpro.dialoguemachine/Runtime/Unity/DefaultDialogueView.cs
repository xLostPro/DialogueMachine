using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
#if DM_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace DialogueMachine.Runtime.Unity
{
    public interface IDialogueView
    {
        void Show(string page, string speaker);
        void ShowChoices(IReadOnlyList<Choice> choices);
        void Hide();
    }

    /// <summary>
    /// Ready-made UI Toolkit dialogue box. Drive it from game input with <see cref="Submit"/>,
    /// <see cref="MoveSelection"/> and <see cref="SelectChoice"/>, or assign Input System actions.
    /// Restyle it with a Style Sheet (the dm-* classes) or replace the layout with UXML whose
    /// elements are named dialogue, speaker, text, choices and continue.
    /// </summary>
    [AddComponentMenu("Dialogue Machine/Default Dialogue View")]
    public class DefaultDialogueView : MonoBehaviour, IDialogueView
    {
        [SerializeField] private DialogueRunnerBehaviour runner;

        [Header("Typewriter")]
        [SerializeField] private bool enableTypewriter = true;
        [Tooltip("Characters per second.")]
        [SerializeField] private float typewriterSpeed = 40f;
        [Tooltip("Show a Typewriter on/off toggle in the box.")]
        [SerializeField] private bool showTypewriterToggle;

        [Header("Look")]
        [Tooltip("Applied after the built-in style. Override the dm-* classes: dm-box, dm-speaker, dm-text, dm-choice, dm-choice--selected, dm-choice--disabled, dm-continue.")]
        [SerializeField] private StyleSheet styleSheet;
        [Tooltip("Replaces the built-in layout. Name its elements dialogue, speaker, text, choices and continue; any of them can be left out.")]
        [SerializeField] private VisualTreeAsset layout;
        [SerializeField] private string continueLabel = "Continue ▶";
        [SerializeField] private string skipLabel = "Skip ▶";

#if DM_INPUT_SYSTEM
        [Header("Input (optional)")]
        [Tooltip("Finishes the typing, then continues or picks the highlighted choice.")]
        [SerializeField] private InputActionReference submitAction;
        [Tooltip("A Vector2 action; up and down move the choice highlight.")]
        [SerializeField] private InputActionReference navigateAction;
        [Tooltip("Seconds between moves while Navigate is held.")]
        [SerializeField] private float repeatSeconds = 0.18f;
        private float nextMove;
#endif

        public const string DialogueName = "dialogue", SpeakerName = "speaker", TextName = "text",
            ChoicesName = "choices", ContinueName = "continue", TypewriterToggleName = "typewriter-toggle";
        public const string ChoiceClass = "dm-choice", SelectedClass = "dm-choice--selected", DisabledClass = "dm-choice--disabled";

        private UIDocument uiDocument;
        private VisualElement dialogue;
        private TextElement speakerLabel;
        private TextElement textLabel;
        private VisualElement choicesContainer;
        private Button continueButton;
        private Toggle typewriterToggle;
        private readonly List<Button> choiceButtons = new List<Button>();

        private string fullText = "";
        private int fullLength;
        private float revealed;
        private int shown;
        private int selectedIndex = -1;
        private int lineFrame = -1;
        private int actionFrame = -1;

        public DialogueRunnerBehaviour Runner
        {
            get => runner;
            set => runner = value;
        }

        public bool EnableTypewriter
        {
            get => enableTypewriter;
            set
            {
                enableTypewriter = value;
                if (typewriterToggle != null) typewriterToggle.SetValueWithoutNotify(value);
            }
        }

        public float TypewriterSpeed
        {
            get => typewriterSpeed;
            set => typewriterSpeed = value;
        }

        /// <summary>Your style sheet, applied after the built-in one. Setting it rebuilds the box.</summary>
        public StyleSheet StyleSheet
        {
            get => styleSheet;
            set { styleSheet = value; Rebuild(); }
        }

        /// <summary>Your UXML layout in place of the built-in one. Setting it rebuilds the box.</summary>
        public VisualTreeAsset Layout
        {
            get => layout;
            set { layout = value; Rebuild(); }
        }

        public bool IsOpen { get; private set; }
        public bool IsTyping { get; private set; }

        /// <summary>True once the line has finished typing and the runner offers choices.</summary>
        public bool IsShowingChoices => IsOpen && !IsTyping && runner != null && runner.Choices.Count > 0 && runner.PageIndex == runner.PageCount - 1;

        /// <summary>The highlighted choice, or -1 when no choice is shown or enabled.</summary>
        public int SelectedIndex => IsShowingChoices ? selectedIndex : -1;

        private IReadOnlyList<Choice> Choices => runner != null ? runner.Choices : System.Array.Empty<Choice>();

        private void Awake()
        {
            if (runner == null) runner = GetComponent<DialogueRunnerBehaviour>();
            EnsureUIDocument();
            BuildUI();
        }

        private void OnEnable()
        {
            if (runner != null)
            {
                runner.LineShown += HandleLineShown;
                runner.ChoicesShown += ShowChoices;
                runner.Ended += HandleEnded;
            }
#if DM_INPUT_SYSTEM
            if (submitAction != null) submitAction.action.Enable();
            if (navigateAction != null) navigateAction.action.Enable();
#endif
        }

        private void OnDisable()
        {
            if (runner != null)
            {
                runner.LineShown -= HandleLineShown;
                runner.ChoicesShown -= ShowChoices;
                runner.Ended -= HandleEnded;
            }
        }

        private void EnsureUIDocument()
        {
            uiDocument = GetComponent<UIDocument>();
            if (uiDocument == null) uiDocument = gameObject.AddComponent<UIDocument>();

            if (uiDocument.panelSettings == null)
            {
                // A PanelSettings without a theme renders nothing, so give it an empty one;
                // the view's own style sheet styles every element.
                var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
                panelSettings.themeStyleSheet = ScriptableObject.CreateInstance<ThemeStyleSheet>();
                uiDocument.panelSettings = panelSettings;
            }
        }

        private void BuildUI()
        {
            var root = uiDocument.rootVisualElement;
            root.Clear();
            root.styleSheets.Clear();
            root.AddToClassList("dm-document");
            var defaultStyle = Resources.Load<StyleSheet>("DialogueMachine/DefaultDialogueView");
            if (defaultStyle != null) root.styleSheets.Add(defaultStyle);
            if (styleSheet != null) root.styleSheets.Add(styleSheet);

            if (layout != null) layout.CloneTree(root);
            else BuildDefaultLayout(root);

            dialogue = root.Q(DialogueName) ?? root;
            speakerLabel = root.Q<TextElement>(SpeakerName);
            textLabel = root.Q<TextElement>(TextName);
            choicesContainer = root.Q(ChoicesName);
            continueButton = root.Q<Button>(ContinueName);
            typewriterToggle = root.Q<Toggle>(TypewriterToggleName);

            if (textLabel != null) textLabel.enableRichText = true;
            if (continueButton != null) continueButton.clicked += () => Act(ContinueOrSkip);
            if (typewriterToggle != null)
            {
                typewriterToggle.SetValueWithoutNotify(enableTypewriter);
                typewriterToggle.RegisterValueChangedCallback(evt => enableTypewriter = evt.newValue);
            }
            // Clicking the box finishes the typing.
            dialogue.RegisterCallback<ClickEvent>(evt =>
            {
                if (IsTyping && Act(SkipTypewriter)) evt.StopPropagation();
            });

            dialogue.style.display = IsOpen ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void Rebuild()
        {
            if (uiDocument == null) return;
            BuildUI();
            if (IsOpen && runner != null) Show(runner.CurrentPage, runner.Speaker);
        }

        private void BuildDefaultLayout(VisualElement root)
        {
            var screen = new VisualElement { name = DialogueName };
            screen.AddToClassList("dm-root");

            var box = new VisualElement();
            box.AddToClassList("dm-box");

            var header = new VisualElement();
            header.AddToClassList("dm-header");
            var speaker = new Label { name = SpeakerName };
            speaker.AddToClassList("dm-speaker");
            header.Add(speaker);
            if (showTypewriterToggle)
            {
                var toggle = new Toggle("Typewriter") { name = TypewriterToggleName };
                toggle.AddToClassList("dm-typewriter-toggle");
                header.Add(toggle);
            }
            box.Add(header);

            var text = new Label { name = TextName };
            text.AddToClassList("dm-text");
            box.Add(text);

            var choices = new VisualElement { name = ChoicesName };
            choices.AddToClassList("dm-choices");
            box.Add(choices);

            var footer = new VisualElement();
            footer.AddToClassList("dm-footer");
            var cont = new Button { name = ContinueName };
            cont.AddToClassList("dm-continue");
            footer.Add(cont);
            box.Add(footer);

            screen.Add(box);
            root.Add(screen);
        }

        private void Update()
        {
            if (!IsOpen) return;

            if (IsTyping)
            {
                revealed += typewriterSpeed * Time.unscaledDeltaTime;
                var count = (int)revealed;
                if (count >= fullLength) FinishTyping();
                else if (count != shown)
                {
                    shown = count;
                    if (textLabel != null) textLabel.text = RichText.Reveal(fullText, count);
                }
            }

#if DM_INPUT_SYSTEM
            ReadInput();
#endif
        }

#if DM_INPUT_SYSTEM
        private void ReadInput()
        {
            if (submitAction != null && submitAction.action.WasPressedThisFrame()) Submit();
            if (navigateAction == null || !IsShowingChoices) return;

            var y = navigateAction.action.ReadValue<Vector2>().y;
            if (Mathf.Abs(y) < 0.5f) { nextMove = 0f; return; }
            if (Time.unscaledTime < nextMove) return;
            MoveSelection(y > 0 ? -1 : 1);
            nextMove = Time.unscaledTime + (nextMove == 0f ? repeatSeconds * 2f : repeatSeconds);
        }
#endif

        /// <summary>
        /// The one button a game needs: finishes the typing, then picks the highlighted choice or continues.
        /// Call it from your confirm/interact input. A press in the frame that showed the line is ignored,
        /// so the press that started or advanced the dialogue doesn't also skip the new line.
        /// </summary>
        public void Submit() => Act(() =>
        {
            if (IsTyping) SkipTypewriter();
            else if (IsShowingChoices) ChooseIndex(selectedIndex);
            else if (runner != null && runner.CanContinue) runner.Continue();
        });

        /// <summary>Moves the choice highlight by delta (negative is up), skipping disabled choices and wrapping.</summary>
        public void MoveSelection(int delta)
        {
            if (!IsShowingChoices || delta == 0) return;
            var step = delta > 0 ? 1 : -1;
            var n = Choices.Count;
            for (var i = 0; i < Mathf.Abs(delta); i++)
            {
                selectedIndex = FirstEnabled(((selectedIndex + step) % n + n) % n, step);
            }
            ApplySelection(true);
        }

        /// <summary>Picks the choice at index (as shown, from 0) if it is enabled. Use it for number keys.</summary>
        public void SelectChoice(int index) => Act(() => ChooseIndex(index));

        /// <summary>Shows the whole line at once.</summary>
        public void SkipTypewriter()
        {
            if (IsTyping) FinishTyping();
        }

        // One action per frame, and none in the frame a line appeared, so the game's input and
        // UI Toolkit's own button navigation can't both act on one press.
        private bool Act(System.Action action)
        {
            var frame = Time.frameCount;
            if (!IsOpen || frame == lineFrame || frame == actionFrame) return false;
            actionFrame = frame;
            action();
            return true;
        }

        private void ContinueOrSkip()
        {
            if (IsTyping) SkipTypewriter();
            else if (runner != null && runner.CanContinue) runner.Continue();
        }

        private void ChooseIndex(int index)
        {
            if (!IsShowingChoices || index < 0 || index >= Choices.Count || !Choices[index].Enabled) return;
            runner.Choose(Choices[index].Id);
        }

        private void HandleLineShown(string line) => Show(runner?.CurrentPage, runner?.Speaker);

        private void HandleEnded(string sceneId, string choiceId) => Hide();

        public void Show(string page, string speaker)
        {
            if (dialogue == null) BuildUI();
            IsOpen = true;
            dialogue.style.display = DisplayStyle.Flex;
            lineFrame = Time.frameCount;

            if (speakerLabel != null)
            {
                speakerLabel.text = speaker ?? string.Empty;
                speakerLabel.style.display = string.IsNullOrEmpty(speaker) ? DisplayStyle.None : DisplayStyle.Flex;
            }

            fullText = RichText.ToUnity(page ?? string.Empty);
            fullLength = RichText.VisibleLength(fullText);
            revealed = 0f;
            shown = 0;
            IsTyping = enableTypewriter && typewriterSpeed > 0f && fullLength > 0;
            if (textLabel != null) textLabel.text = IsTyping ? string.Empty : fullText;
            RefreshControls();
        }

        public void ShowChoices(IReadOnlyList<Choice> choices) => RefreshControls();

        public void Hide()
        {
            IsOpen = false;
            IsTyping = false;
            ClearChoices();
            if (dialogue != null) dialogue.style.display = DisplayStyle.None;
        }

        private void FinishTyping()
        {
            IsTyping = false;
            if (textLabel != null) textLabel.text = fullText;
            RefreshControls();
        }

        private void RefreshControls()
        {
            if (!IsOpen) return;
            ClearChoices();

            if (IsShowingChoices && choicesContainer != null)
            {
                choicesContainer.style.display = DisplayStyle.Flex;
                for (var i = 0; i < Choices.Count; i++)
                {
                    choiceButtons.Add(CreateChoiceButton(Choices[i], i));
                    choicesContainer.Add(choiceButtons[i]);
                }
                selectedIndex = FirstEnabled(0, 1);
            }

            if (continueButton != null)
            {
                var visible = IsTyping || (runner != null && runner.CanContinue);
                continueButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                continueButton.text = IsTyping ? skipLabel : continueLabel;
            }

            if (IsShowingChoices) ApplySelection(true);
            else if (continueButton != null && runner != null && runner.CanContinue && !IsTyping) continueButton.Focus();
        }

        private Button CreateChoiceButton(Choice choice, int index)
        {
            var button = new Button { text = RichText.ToUnity(choice.Text) };
            button.AddToClassList(ChoiceClass);
            button.focusable = choice.Enabled;
            button.SetEnabled(choice.Enabled);
            if (!choice.Enabled)
            {
                button.AddToClassList(DisabledClass);
                if (!string.IsNullOrEmpty(choice.DisabledReason))
                {
                    button.tooltip = choice.DisabledReason;
                    button.text += " (" + choice.DisabledReason + ")";
                }
            }

            button.clicked += () => SelectChoice(index);
            // Mouse hover and UI Toolkit focus move the same highlight the game's input moves.
            button.RegisterCallback<PointerEnterEvent>(_ => Highlight(index));
            button.RegisterCallback<FocusInEvent>(_ => Highlight(index));
            return button;
        }

        private void Highlight(int index)
        {
            if (index == selectedIndex || !IsShowingChoices || !Choices[index].Enabled) return;
            selectedIndex = index;
            ApplySelection(false);
        }

        private void ApplySelection(bool focus)
        {
            for (var i = 0; i < choiceButtons.Count; i++)
            {
                choiceButtons[i].EnableInClassList(SelectedClass, i == selectedIndex);
            }
            if (focus && selectedIndex >= 0 && selectedIndex < choiceButtons.Count) choiceButtons[selectedIndex].Focus();
        }

        private void ClearChoices()
        {
            choiceButtons.Clear();
            if (choicesContainer == null) return;
            choicesContainer.Clear();
            choicesContainer.style.display = DisplayStyle.None;
        }

        // The first enabled choice from start, walking in direction step (wraps); -1 if none is enabled.
        private int FirstEnabled(int start, int step)
        {
            var n = Choices.Count;
            for (var i = 0; i < n; i++)
            {
                var k = ((start + i * step) % n + n) % n;
                if (Choices[k].Enabled) return k;
            }
            return -1;
        }
    }
}
