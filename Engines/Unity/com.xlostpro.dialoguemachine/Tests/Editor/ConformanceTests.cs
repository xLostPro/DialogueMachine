using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DialogueMachine.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace DialogueMachine.Editor.Tests
{
    public class ConformanceTests
    {
        private static string FindConformanceFolder()
        {
            try
            {
                var pkgInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(ConformanceTests).Assembly);
                if (pkgInfo != null && !string.IsNullOrEmpty(pkgInfo.resolvedPath))
                {
                    var relativePath = Path.GetFullPath(Path.Combine(pkgInfo.resolvedPath, "../../Conformance"));
                    if (Directory.Exists(relativePath) && Directory.GetFiles(relativePath, "*.json").Length > 0)
                    {
                        return relativePath;
                    }
                }
            }
            catch { }

            var candidates = new[]
            {
                Path.GetFullPath("Engines/Conformance"),
                Path.GetFullPath("../Engines/Conformance"),
                Path.GetFullPath("../../Engines/Conformance"),
                Path.GetFullPath("../../../Engines/Conformance"),
                Path.Combine(Application.dataPath, "../../Engines/Conformance"),
                Path.Combine(Application.dataPath, "../../../Engines/Conformance")
            };

            foreach (var candidate in candidates)
            {
                if (Directory.Exists(candidate) && Directory.GetFiles(candidate, "*.json").Length > 0)
                {
                    return candidate;
                }
            }

            // Fallback search up directory tree
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null)
            {
                var check = Path.Combine(dir.FullName, "Engines", "Conformance");
                if (Directory.Exists(check) && Directory.GetFiles(check, "*.json").Length > 0)
                {
                    return check;
                }
                dir = dir.Parent;
            }

            return null;
        }

        [Test]
        public void AllConformanceFixturesPassInUnity()
        {
            var folder = FindConformanceFolder();
            Assert.IsNotNull(folder, "Could not locate Engines/Conformance folder.");

            var files = Directory.GetFiles(folder, "*.json");
            Assert.GreaterOrEqual(files.Length, 14, "Expected at least 14 conformance fixtures.");

            foreach (var file in files)
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var json = File.ReadAllText(file);
                ReplayFixture(name, json);
            }
        }

        private static void ReplayFixture(string name, string json)
        {
            var fixture = MiniJson.Parse(json) as Dictionary<string, object>;
            Assert.IsNotNull(fixture, $"{name}: Fixture is not valid JSON object.");

            var handoffObj = fixture["handoff"] as Dictionary<string, object>;
            var inputs = fixture["inputs"] as List<object>;
            var expectedList = fixture["expected"] as List<object>;
            Assert.IsNotNull(inputs, $"{name}: missing inputs");
            Assert.IsNotNull(expectedList, $"{name}: missing expected");

            var runner = new DialogueRunner();
            var events = new List<Dictionary<string, object>>();
            var saved = "";

            runner.ActionRaised += (type, target, param, _) =>
            {
                events.Add(new Dictionary<string, object>
                {
                    ["type"] = type,
                    ["targetId"] = target,
                    ["parameter"] = param
                });
            };

            runner.ShopOpened += shop =>
            {
                events.Add(new Dictionary<string, object>
                {
                    ["type"] = "Shop",
                    ["targetId"] = shop,
                    ["parameter"] = null
                });
            };

            runner.Load(MiniJson.Serialize(handoffObj));

            for (var i = 0; i < inputs.Count; i++)
            {
                var input = inputs[i] as Dictionary<string, object>;
                var op = input["op"] as string;
                events.Clear();

                switch (op)
                {
                    case "start":
                        runner.Start();
                        break;
                    case "continue":
                        runner.Continue();
                        break;
                    case "choose":
                        runner.Choose(input["choiceId"] as string);
                        break;
                    case "setNumber":
                        runner.Variables.SetNumber(input["key"] as string, (int)(double)input["value"]);
                        break;
                    case "setText":
                        runner.Variables.SetText(input["key"] as string, input["value"] as string);
                        break;
                    case "save":
                        saved = runner.SaveState();
                        break;
                    case "restore":
                        runner.RestoreState(saved);
                        break;
                    default:
                        Assert.Fail($"{name}: unknown op {op}");
                        break;
                }

                var expected = expectedList[i] as Dictionary<string, object>;
                Assert.IsNotNull(expected, $"{name}: step {i} missing expected snapshot");

                // Check sceneId
                var expSceneId = expected.TryGetValue("sceneId", out var s) ? s as string : null;
                Assert.AreEqual(expSceneId, runner.CurrentSceneId, $"{name} step {i} ({op}) sceneId");

                // Check ended
                var expEnded = expected.TryGetValue("ended", out var ed) && ed is bool b && b;
                Assert.AreEqual(expEnded, runner.IsEnded, $"{name} step {i} ({op}) ended");

                // Check page text
                if (expected.TryGetValue("page", out var expPage) && expPage != null)
                {
                    Assert.AreEqual(expPage as string, runner.CurrentPage, $"{name} step {i} ({op}) page");
                }

                // Check page index & count
                if (expected.TryGetValue("pageIndex", out var expPi) && expPi is double dpi)
                {
                    Assert.AreEqual((int)dpi, runner.PageIndex, $"{name} step {i} ({op}) pageIndex");
                }
                if (expected.TryGetValue("pageCount", out var expPc) && expPc is double dpc)
                {
                    Assert.AreEqual((int)dpc, runner.PageCount, $"{name} step {i} ({op}) pageCount");
                }

                // Check canContinue
                if (expected.TryGetValue("canContinue", out var expCc) && expCc is bool bcc)
                {
                    Assert.AreEqual(bcc, runner.CanContinue, $"{name} step {i} ({op}) canContinue");
                }

                // Only some fixtures record the page's speaker and portrait.
                if (expected.TryGetValue("speaker", out var expSpeaker))
                    Assert.AreEqual(expSpeaker as string, runner.Speaker, $"{name} step {i} ({op}) speaker");
                if (expected.TryGetValue("portrait", out var expPortrait))
                    Assert.AreEqual(expPortrait as string, runner.Portrait, $"{name} step {i} ({op}) portrait");

                // Check numeric variables
                if (expected.TryGetValue("numeric", out var expNumObj) && expNumObj is Dictionary<string, object> expNums)
                {
                    foreach (var kv in expNums)
                    {
                        var expVal = (int)(double)kv.Value;
                        var actVal = runner.Variables.GetNumber(kv.Key);
                        Assert.AreEqual(expVal, actVal, $"{name} step {i} ({op}) numeric {kv.Key}");
                    }
                }

                // Check text variables
                if (expected.TryGetValue("text", out var expTxtObj) && expTxtObj is Dictionary<string, object> expTexts)
                {
                    foreach (var kv in expTexts)
                    {
                        var expVal = kv.Value as string;
                        var actVal = runner.Variables.GetText(kv.Key);
                        Assert.AreEqual(expVal, actVal, $"{name} step {i} ({op}) text {kv.Key}");
                    }
                }

                // Check choices
                if (expected.TryGetValue("choices", out var expChoicesObj) && expChoicesObj is List<object> expChoices)
                {
                    Assert.AreEqual(expChoices.Count, runner.Choices.Count, $"{name} step {i} ({op}) choice count");
                    for (var c = 0; c < expChoices.Count; c++)
                    {
                        var expC = expChoices[c] as Dictionary<string, object>;
                        var actC = runner.Choices[c];
                        Assert.AreEqual(expC["id"] as string, actC.Id, $"{name} step {i} ({op}) choice {c} id");
                        Assert.AreEqual(expC["text"] as string, actC.Text, $"{name} step {i} ({op}) choice {c} text");
                        Assert.AreEqual((bool)expC["enabled"], actC.Enabled, $"{name} step {i} ({op}) choice {c} enabled");
                    }
                }
            }
        }
    }
}
