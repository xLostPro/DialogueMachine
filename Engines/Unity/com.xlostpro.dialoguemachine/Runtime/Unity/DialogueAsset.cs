using UnityEngine;

namespace DialogueMachine.Runtime.Unity
{
    [CreateAssetMenu(fileName = "Story", menuName = "Dialogue Machine/Dialogue Asset")]
    public class DialogueAsset : ScriptableObject
    {
        [TextArea(3, 20)]
        [SerializeField] private string handoffJson = "";
        [SerializeField] private string sha = "";
        [SerializeField] private string projectName = "";

        public string HandoffJson
        {
            get => handoffJson;
            set => handoffJson = value;
        }

        public string Sha
        {
            get => sha;
            set => sha = value;
        }

        public string ProjectName
        {
            get => projectName;
            set => projectName = value;
        }

        public void SetData(string json, string storySha, string name)
        {
            handoffJson = json;
            sha = storySha;
            projectName = name;
        }
    }
}
