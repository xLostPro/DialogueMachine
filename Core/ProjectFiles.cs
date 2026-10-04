using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace DialogueMachine.Core;

// Disk bytes are deliberately separate from the normalized editor/history snapshot.
public sealed record ProjectFileVersion(bool Exists, string? ContentHash, string? Identity);
public sealed record ProjectFileSnapshot(string Path, ProjectFileVersion Version, string? Text)
{
    public DialogueProject Parse()
    {
        if (!Version.Exists) throw new FileNotFoundException("The project file was deleted or moved.", Path);
        if (Text is null) throw new InvalidDataException("Projects must be smaller than 20 MB.");
        return ProjectStore.Parse(Text);
    }
}
public sealed class ProjectFileConflictException(ProjectFileSnapshot current)
    : IOException("The project file changed on disk. Compare, reload, or save a copy before continuing.")
{
    public ProjectFileSnapshot Current { get; } = current;
}

public static class ProjectFiles
{
    private const int MaxBytes = 20 * 1024 * 1024;
    public static bool SamePath(string a, string b) => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    public static ProjectFileSnapshot Read(string path)
    {
        path = Path.GetFullPath(path);
        try
        {
            // On Windows this excludes writers and replacements while capturing one version.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var identity = Identity(stream);
            var hash = Convert.ToHexString(SHA256.HashData(stream));
            string? text = null;
            if (stream.Length <= MaxBytes)
            {
                stream.Position = 0;
                using var reader = new StreamReader(stream, leaveOpen: true);
                text = reader.ReadToEnd(); // Same BOM/encoding handling as ordinary imports.
            }
            return new(path, new(true, hash, identity), text);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        { return new(path, new(false, null, null), null); }
    }

    public static ProjectFileSnapshot RequireUnchanged(string path, ProjectFileVersion? expected)
    {
        var current = Read(path);
        // A recovered document has no trustworthy load baseline and must be resolved explicitly.
        if (expected is null || current.Version != expected) throw new ProjectFileConflictException(current);
        return current;
    }

    public static ProjectFileVersion Save(string path, string content, ProjectFileVersion expected)
    {
        path = Path.GetFullPath(path);
        var key = OperatingSystem.IsWindows() ? path.ToUpperInvariant() : path;
        using var mutex = new Mutex(false, "DialogueMachine.Save." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))));
        bool acquired;
        try { acquired = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) throw new IOException("Another editor instance is saving this file. Try again when it finishes.");
        try
        {
            RequireUnchanged(path, expected);
            ProjectFileVersion? written = null;
            ProjectStore.AtomicWrite(path, content, temp =>
            {
                written = Read(temp).Version;
                // Check again after flushing the staged write, immediately before replacement.
                RequireUnchanged(path, expected);
            }, overwrite: expected.Exists);
            // Use the version actually written, never a potentially newer post-save path read.
            return written!;
        }
        finally { mutex.ReleaseMutex(); }
    }

    public static ProjectFileVersion SaveCopy(string path, string content)
        => Save(path, content, new(false, null, null));

    // Windows additionally detects same-byte replacements, including hard-link aliases.
    // Other platforms compare exact content; Core has no Avalonia or Windows-only dependency.
    private static string? Identity(FileStream stream)
    {
        if (!OperatingSystem.IsWindows()) return null;
        if (!GetFileInformationByHandle(stream.SafeFileHandle, out var info))
            throw new IOException("Could not identify the project file.", new Win32Exception(Marshal.GetLastWin32Error()));
        return $"{info.Volume:X8}:{info.IndexHigh:X8}{info.IndexLow:X8}";
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);
    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Written;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
}
