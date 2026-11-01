using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DialogueMachine;
using DialogueMachine.Core;
using DialogueMachine.UI;
using System.Text.Json;
using static UiHelpers;


internal static class RecoveryChecks
{
    internal static DialogueProject SemanticDamage = null!;

    public static void Core(Action<bool, string> Check, Action<Action, string> Throws)
    {
        var p = Templates.Harbour(); var renamed = Templates.Harbour(); DialogueEngine.Rename(renamed, renamed.ListOfCharacterStates[0], "opening");
        var temp = Path.Combine(AppContext.BaseDirectory, "atomic-test.json"); ProjectStore.AtomicWrite(temp, ProjectStore.Serialize(p)); ProjectStore.AtomicWrite(temp, ProjectStore.Serialize(renamed));
        Check(ProjectStore.Load(temp).Entry == "opening" && !Directory.EnumerateFiles(AppContext.BaseDirectory, "atomic-test.json.*.tmp").Any(), "atomic saves replace completely and clean up temporary files"); File.Delete(temp);
        var recoveryFolder = Path.Combine(AppContext.BaseDirectory, "recovery-test-" + Guid.NewGuid().ToString("N")); var recovery = new RecoveryStore(recoveryFolder); var recoveryId = Guid.NewGuid().ToString("N");
        recovery.Save(recoveryId, new RecoveryRecord(null, DateTimeOffset.UtcNow, Templates.Harbour()));
        Check(recovery.FindUnsaved().Count == 1 && recovery.FindUnsaved()[0].Record.Project.Entry == "arrival", "unsaved recovery can be loaded and validated");
        recovery.Remove(recoveryId); Check(recovery.FindUnsaved().Count == 0, "completed or discarded recovery is removed and not offered again");
        Throws(() => recovery.Save("../escape", new RecoveryRecord(null, DateTimeOffset.UtcNow, Templates.Blank())), "recovery paths reject traversal");
        var badRecoveryId = Guid.NewGuid().ToString("N"); File.WriteAllText(Path.Combine(recoveryFolder, badRecoveryId + ".json"), "invalid");
        Check(recovery.FindUnsaved().Count == 0, "invalid recovery records are ignored without destroying them"); File.Delete(Path.Combine(recoveryFolder, badRecoveryId + ".json"));
        var semanticRecoveryId = Guid.NewGuid().ToString("N"); var semanticRecoveryPath = Path.Combine(recoveryFolder, semanticRecoveryId + ".json");
        var semanticDamage = Templates.Blank(); semanticDamage.ListOfCharacterStates[0].State = "";
        recovery.Save(semanticRecoveryId, new RecoveryRecord(null, DateTimeOffset.UtcNow, semanticDamage));
        recovery.Save(recoveryId, new RecoveryRecord(null, DateTimeOffset.UtcNow, Templates.Harbour()));
        Check(recovery.FindUnsaved().Count == 1 && File.Exists(semanticRecoveryPath), "semantically invalid recovery is retained and skipped without hiding a valid recovery");
        recovery.Remove(semanticRecoveryId); recovery.Remove(recoveryId);
        ProjectStore.AtomicWrite(temp, ProjectStore.Serialize(Templates.Blank())); var recoveryTime = DateTimeOffset.UtcNow.AddMinutes(1);
        var recoverableStory = Templates.Harbour(); recovery.Save(recoveryId, new RecoveryRecord(temp, recoveryTime, recoverableStory));
        Check(recovery.FindUnsaved().Count == 1, "recovery newer than a project file is offered"); File.SetLastWriteTimeUtc(temp, recoveryTime.UtcDateTime.AddSeconds(1));
        Check(recovery.FindUnsaved().Count == 1, "a newer external save cannot hide different recovery content");
        ProjectStore.AtomicWrite(temp, ProjectStore.Serialize(recoverableStory));
        Check(recovery.FindUnsaved().Count == 0, "recovery matching saved content is ignored regardless of timestamps"); recovery.Remove(recoveryId); File.Delete(temp);
        SemanticDamage = semanticDamage;
    }
}
