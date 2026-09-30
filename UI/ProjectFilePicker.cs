using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace DialogueMachine.UI;

// Keep OS picker calls at the UI boundary so document workflows can be exercised
// with deterministic choices while production uses real native dialogs.
public interface IProjectFilePicker
{
    Task<string?> OpenAsync(Window owner);
    Task<string?> SaveAsync(Window owner, string suggestedName);
}

public sealed class NativeProjectFilePicker : IProjectFilePicker
{
    private static readonly FilePickerFileType ProjectType = new("Dialogue Machine project") { Patterns = ["*.dialogue.json", "*.json"], MimeTypes = ["application/json"] };
    public async Task<string?> OpenAsync(Window owner)
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Open Dialogue Machine project", AllowMultiple = false, FileTypeFilter = [ProjectType, FilePickerFileTypes.All] });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }
    public async Task<string?> SaveAsync(Window owner, string suggestedName)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Save your story", SuggestedFileName = suggestedName, DefaultExtension = "json", FileTypeChoices = [ProjectType], ShowOverwritePrompt = true });
        return file?.TryGetLocalPath();
    }
}
