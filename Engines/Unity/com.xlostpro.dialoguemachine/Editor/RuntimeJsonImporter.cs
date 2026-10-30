using System;
using System.IO;
using DialogueMachine.Runtime;
using DialogueMachine.Runtime.Unity;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace DialogueMachine.Editor
{
    [ScriptedImporter(1, "runtime.json")]
    public class RuntimeJsonImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            string json;
            try
            {
                json = File.ReadAllText(ctx.assetPath);
            }
            catch (Exception ex)
            {
                ctx.LogImportError($"Failed to read dialogue file '{ctx.assetPath}': {ex.Message}");
                return;
            }

            Handoff handoff;
            try
            {
                handoff = Handoff.Parse(json);
            }
            catch (Exception ex)
            {
                ctx.LogImportError($"Failed to parse runtime dialogue '{ctx.assetPath}': {ex.Message}");
                return;
            }

            var asset = ScriptableObject.CreateInstance<DialogueAsset>();
            var fileName = Path.GetFileName(ctx.assetPath);
            var projectName = fileName;
            if (projectName.EndsWith(".runtime.json", StringComparison.OrdinalIgnoreCase))
                projectName = projectName.Substring(0, projectName.Length - ".runtime.json".Length);
            else if (projectName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                projectName = projectName.Substring(0, projectName.Length - ".json".Length);

            asset.SetData(json, handoff.StorySha256 ?? string.Empty, projectName);

            ctx.AddObjectToAsset("main", asset);
            ctx.SetMainObject(asset);
        }
    }
}
