using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace DialogueMachine.Runtime.Unity
{
    [Serializable]
    public class DialogueEventBinding
    {
        public string targetId = "";
        public UnityEvent<string> onRaised = new UnityEvent<string>();
    }

    [AddComponentMenu("Dialogue Machine/Dialogue Event Router")]
    public class DialogueEventRouter : MonoBehaviour
    {
        [SerializeField] private DialogueRunnerBehaviour runner;
        [SerializeField] private List<DialogueEventBinding> bindings = new List<DialogueEventBinding>();
        [SerializeField] private UnityEvent<string> onShop = new UnityEvent<string>();

        private readonly HashSet<string> warnedUnmatched = new HashSet<string>(StringComparer.Ordinal);

        public DialogueRunnerBehaviour Runner
        {
            get => runner;
            set => runner = value;
        }

        public List<DialogueEventBinding> Bindings => bindings;
        public UnityEvent<string> OnShop => onShop;

        private void Awake()
        {
            if (runner == null) runner = GetComponent<DialogueRunnerBehaviour>();
        }

        private void OnEnable()
        {
            if (runner != null)
            {
                runner.ActionRaised += HandleAction;
                runner.ShopOpened += HandleShop;
            }
        }

        private void OnDisable()
        {
            if (runner != null)
            {
                runner.ActionRaised -= HandleAction;
                runner.ShopOpened -= HandleShop;
            }
        }

        public void HandleAction(string type, string targetId, string parameter, int amount)
        {
            if (type != "Event" && type != "Script") return;
            var matched = false;
            foreach (var binding in bindings)
            {
                if (string.Equals(binding.targetId, targetId, StringComparison.OrdinalIgnoreCase))
                {
                    matched = true;
                    binding.onRaised?.Invoke(parameter);
                }
            }

            if (!matched && !string.IsNullOrEmpty(targetId))
            {
                if (warnedUnmatched.Add(targetId))
                {
                    Debug.LogWarning($"[DialogueMachine] Unmatched {type} targetId '{targetId}' (parameter: '{parameter}').", this);
                }
            }
        }

        public void HandleShop(string shopId)
        {
            onShop?.Invoke(shopId);
        }
    }
}
