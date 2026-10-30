using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DialogueMachine.Runtime.Unity
{
    public interface IDialogueView
    {
        void Show(string page, string speaker);
        void ShowChoices(IReadOnlyList<Choice> choices);
        void Hide();
    }

    [AddComponentMenu("Dialogue Machine/Default Dialogue View")]
    public class DefaultDialogueView : MonoBehaviour, IDialogueView
    {
        [SerializeField] private DialogueRunnerBehaviour runner;
        [SerializeField] private bool enableTypewriter = true;
        [SerializeField] private float typewriterSpeed = 40f; // characters per second

        private UIDocument uiDocument;
        private VisualElement rootContainer;
        private VisualElement dialogBox;
        private Label speakerLabel;
        private Label textLabel;
        private Button continueButton;
        private VisualElement choicesContainer;
        private Toggle typewriterToggle;

        private Coroutine typewriterRoutine;
        private string targetFullText = "";
        private bool isTyping = false;

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
                if (typewriterToggle != null) typewriterToggle.value = value;
            }
        }

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
                runner.ChoicesShown += HandleChoicesShown;
                runner.Ended += HandleEnded;
            }
        }

        private void OnDisable()
        {
            if (runner != null)
            {
                runner.LineShown -= HandleLineShown;
                runner.ChoicesShown -= HandleChoicesShown;
                runner.Ended -= HandleEnded;
            }
        }

        private void EnsureUIDocument()
        {
            uiDocument = GetComponent<UIDocument>();
            if (uiDocument == null)
            {
                uiDocument = gameObject.AddComponent<UIDocument>();
            }

            if (uiDocument.panelSettings == null)
            {
                // A PanelSettings without a theme renders nothing, so give it an empty one;
                // every element in this view is styled inline.
                var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
                panelSettings.themeStyleSheet = ScriptableObject.CreateInstance<ThemeStyleSheet>();
                uiDocument.panelSettings = panelSettings;
            }
        }

        private void BuildUI()
        {
            var root = uiDocument.rootVisualElement;
            root.Clear();
            // The default theme normally sizes the document root; without it the root collapses to zero height.
            root.style.flexGrow = 1;
            root.style.height = new Length(100, LengthUnit.Percent);

            // Screen container
            rootContainer = new VisualElement();
            rootContainer.style.position = Position.Absolute;
            rootContainer.style.left = 0;
            rootContainer.style.right = 0;
            rootContainer.style.top = 0;
            rootContainer.style.bottom = 0;
            rootContainer.style.justifyContent = Justify.FlexEnd;
            rootContainer.style.alignItems = Align.Center;
            rootContainer.style.paddingBottom = 28;
            rootContainer.style.display = DisplayStyle.None;

            // Dialogue Box
            dialogBox = new VisualElement();
            dialogBox.style.width = new Length(80, LengthUnit.Percent);
            dialogBox.style.maxWidth = 860;
            dialogBox.style.minHeight = 160;
            dialogBox.style.backgroundColor = new StyleColor(new Color(0.12f, 0.14f, 0.18f, 0.94f));
            dialogBox.style.borderTopLeftRadius = 10;
            dialogBox.style.borderTopRightRadius = 10;
            dialogBox.style.borderBottomLeftRadius = 10;
            dialogBox.style.borderBottomRightRadius = 10;
            dialogBox.style.borderLeftWidth = 1.5f;
            dialogBox.style.borderRightWidth = 1.5f;
            dialogBox.style.borderTopWidth = 1.5f;
            dialogBox.style.borderBottomWidth = 1.5f;
            dialogBox.style.borderLeftColor = new StyleColor(new Color(0.28f, 0.32f, 0.40f, 0.8f));
            dialogBox.style.borderRightColor = new StyleColor(new Color(0.28f, 0.32f, 0.40f, 0.8f));
            dialogBox.style.borderTopColor = new StyleColor(new Color(0.28f, 0.32f, 0.40f, 0.8f));
            dialogBox.style.borderBottomColor = new StyleColor(new Color(0.28f, 0.32f, 0.40f, 0.8f));
            dialogBox.style.paddingLeft = 24;
            dialogBox.style.paddingRight = 24;
            dialogBox.style.paddingTop = 18;
            dialogBox.style.paddingBottom = 18;

            // Header bar (Speaker + Typewriter toggle)
            var headerBar = new VisualElement();
            headerBar.style.flexDirection = FlexDirection.Row;
            headerBar.style.justifyContent = Justify.SpaceBetween;
            headerBar.style.alignItems = Align.Center;
            headerBar.style.marginBottom = 10;

            speakerLabel = new Label("Speaker");
            speakerLabel.style.fontSize = 18;
            speakerLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            speakerLabel.style.color = new StyleColor(new Color(0.47f, 0.87f, 0.78f)); // #79DFC8

            typewriterToggle = new Toggle("Typewriter");
            typewriterToggle.value = enableTypewriter;
            typewriterToggle.style.fontSize = 12;
            typewriterToggle.style.color = new StyleColor(new Color(0.7f, 0.75f, 0.82f));
            typewriterToggle.RegisterValueChangedCallback(evt => enableTypewriter = evt.newValue);

            headerBar.Add(speakerLabel);
            headerBar.Add(typewriterToggle);
            dialogBox.Add(headerBar);

            // Dialogue Text
            textLabel = new Label("");
            textLabel.enableRichText = true;
            textLabel.style.fontSize = 16;
            textLabel.style.color = new StyleColor(new Color(0.94f, 0.95f, 0.97f));
            textLabel.style.whiteSpace = WhiteSpace.Normal;
            textLabel.style.marginBottom = 16;
            textLabel.style.flexGrow = 1;
            dialogBox.Add(textLabel);

            // Click dialogue box to fast forward typewriter
            dialogBox.RegisterCallback<ClickEvent>(evt =>
            {
                if (isTyping)
                {
                    CompleteTypewriterImmediately();
                    evt.StopPropagation();
                }
            });

            // Choices Container
            choicesContainer = new VisualElement();
            choicesContainer.style.flexDirection = FlexDirection.Column;
            choicesContainer.style.marginBottom = 8;
            dialogBox.Add(choicesContainer);

            // Footer bar (Continue button)
            var footerBar = new VisualElement();
            footerBar.style.flexDirection = FlexDirection.Row;
            footerBar.style.justifyContent = Justify.FlexEnd;

            continueButton = new Button(() =>
            {
                if (isTyping)
                {
                    CompleteTypewriterImmediately();
                }
                else if (runner != null && runner.CanContinue)
                {
                    runner.Continue();
                }
            });
            continueButton.text = "Continue ▶";
            continueButton.focusable = true;
            continueButton.style.paddingLeft = 18;
            continueButton.style.paddingRight = 18;
            continueButton.style.paddingTop = 8;
            continueButton.style.paddingBottom = 8;
            continueButton.style.fontSize = 14;
            continueButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            StyleButton(continueButton);

            footerBar.Add(continueButton);
            dialogBox.Add(footerBar);

            rootContainer.Add(dialogBox);
            root.Add(rootContainer);
        }

        private static void StyleButton(Button button)
        {
            var border = new StyleColor(new Color(0.28f, 0.32f, 0.40f));
            button.style.backgroundColor = new StyleColor(new Color(0.2f, 0.24f, 0.31f));
            button.style.color = new StyleColor(new Color(0.94f, 0.95f, 0.97f));
            button.style.borderTopColor = border;
            button.style.borderRightColor = border;
            button.style.borderBottomColor = border;
            button.style.borderLeftColor = border;
            button.style.borderTopWidth = 1;
            button.style.borderRightWidth = 1;
            button.style.borderBottomWidth = 1;
            button.style.borderLeftWidth = 1;
            button.style.borderTopLeftRadius = 6;
            button.style.borderTopRightRadius = 6;
            button.style.borderBottomLeftRadius = 6;
            button.style.borderBottomRightRadius = 6;
        }

        private void HandleLineShown(string line)
        {
            Show(runner?.CurrentPage, runner?.Speaker);
        }

        private void HandleChoicesShown(IReadOnlyList<Choice> choices)
        {
            ShowChoices(choices);
        }

        private void HandleEnded(string sceneId, string choiceId)
        {
            Hide();
        }

        public void Show(string page, string speaker)
        {
            if (rootContainer == null) BuildUI();
            rootContainer.style.display = DisplayStyle.Flex;

            speakerLabel.text = speaker ?? string.Empty;
            speakerLabel.style.display = string.IsNullOrEmpty(speaker) ? DisplayStyle.None : DisplayStyle.Flex;

            choicesContainer.Clear();
            choicesContainer.style.display = DisplayStyle.None;

            targetFullText = RichText.ToUnity(page ?? string.Empty);

            if (typewriterRoutine != null)
            {
                StopCoroutine(typewriterRoutine);
                typewriterRoutine = null;
            }

            if (enableTypewriter && gameObject.activeInHierarchy)
            {
                typewriterRoutine = StartCoroutine(TypewriterLoop(targetFullText));
            }
            else
            {
                textLabel.text = targetFullText;
                isTyping = false;
                UpdateControlsAfterLine();
            }
        }

        private IEnumerator TypewriterLoop(string fullText)
        {
            isTyping = true;
            continueButton.style.display = DisplayStyle.Flex;
            continueButton.text = "Skip ▶";

            var plainText = DialogueRules.PlainText(fullText);
            var charDelay = typewriterSpeed > 0 ? 1f / typewriterSpeed : 0.025f;
            var currentLength = 0;

            while (currentLength < plainText.Length)
            {
                currentLength++;
                textLabel.text = plainText.Substring(0, currentLength);
                yield return new WaitForSeconds(charDelay);
            }

            textLabel.text = fullText;
            isTyping = false;
            typewriterRoutine = null;
            UpdateControlsAfterLine();
        }

        private void CompleteTypewriterImmediately()
        {
            if (typewriterRoutine != null)
            {
                StopCoroutine(typewriterRoutine);
                typewriterRoutine = null;
            }
            textLabel.text = targetFullText;
            isTyping = false;
            UpdateControlsAfterLine();
        }

        private void UpdateControlsAfterLine()
        {
            if (runner == null) return;

            if (runner.Choices != null && runner.Choices.Count > 0 && runner.PageIndex == runner.PageCount - 1)
            {
                ShowChoices(runner.Choices);
            }
            else
            {
                choicesContainer.Clear();
                choicesContainer.style.display = DisplayStyle.None;
                continueButton.style.display = runner.CanContinue ? DisplayStyle.Flex : DisplayStyle.None;
                continueButton.text = "Continue ▶";
                if (runner.CanContinue) continueButton.Focus();
            }
        }

        public void ShowChoices(IReadOnlyList<Choice> choices)
        {
            if (rootContainer == null) BuildUI();
            choicesContainer.Clear();

            if (choices == null || choices.Count == 0)
            {
                choicesContainer.style.display = DisplayStyle.None;
                continueButton.style.display = (runner != null && runner.CanContinue) ? DisplayStyle.Flex : DisplayStyle.None;
                if (runner != null && runner.CanContinue) continueButton.Focus();
                return;
            }

            choicesContainer.style.display = DisplayStyle.Flex;
            Button firstEnabledButton = null;

            for (var i = 0; i < choices.Count; i++)
            {
                var choice = choices[i];
                var button = new Button();
                button.text = RichText.ToUnity(choice.Text);
                button.focusable = choice.Enabled;
                button.SetEnabled(choice.Enabled);
                button.style.marginTop = 4;
                button.style.marginBottom = 4;
                button.style.paddingTop = 8;
                button.style.paddingBottom = 8;
                button.style.paddingLeft = 14;
                button.style.paddingRight = 14;
                button.style.fontSize = 15;
                button.style.unityTextAlign = TextAnchor.MiddleLeft;

                StyleButton(button);
                if (!choice.Enabled)
                {
                    button.style.opacity = 0.5f;
                    if (!string.IsNullOrEmpty(choice.DisabledReason))
                    {
                        button.tooltip = choice.DisabledReason;
                        button.text += " (" + choice.DisabledReason + ")";
                    }
                }

                var choiceId = choice.Id;
                button.clicked += () =>
                {
                    if (runner != null) runner.Choose(choiceId);
                };

                choicesContainer.Add(button);
                if (choice.Enabled && firstEnabledButton == null)
                {
                    firstEnabledButton = button;
                }
            }

            // Continue button is hidden if there are enabled choices, unless runner.CanContinue
            continueButton.style.display = (runner != null && runner.CanContinue) ? DisplayStyle.Flex : DisplayStyle.None;
            continueButton.text = "Continue ▶";

            if (firstEnabledButton != null)
            {
                firstEnabledButton.Focus();
            }
            else if (runner != null && runner.CanContinue)
            {
                continueButton.Focus();
            }
        }

        public void Hide()
        {
            if (typewriterRoutine != null)
            {
                StopCoroutine(typewriterRoutine);
                typewriterRoutine = null;
            }
            isTyping = false;
            if (rootContainer != null)
            {
                rootContainer.style.display = DisplayStyle.None;
            }
        }
    }
}
